import Foundation
import NetworkExtension
import Network
import Darwin

/// Расширение Packet Tunnel: системный utun-интерфейс отдаётся встроенному TUN-инбаунду
/// Xray-core по номеру дескриптора (env xray.tun.fd в самом конфиге — официальный способ
/// для iOS в Xray-core ≥ 26), трафик уходит через прокси-аутбаунды профиля подписки.
/// Один Go-рантайм на процесс (только libXray) — без отдельного tun2socks.
final class PacketTunnelProvider: NEPacketTunnelProvider {
    private let queue = DispatchQueue(label: "xyz.gojihub.vpn.tunnel")
    private var profile: SharedStorage.ActiveProfile?
    /// Payload, на котором ядро запущено прямо сейчас — для отката при неудачной смене узла
    /// (см. restartCore) и как признак «ядро живо».
    private var runningPayload: String?
    private var tunFd: Int32?
    private var socksPort: Int?
    private var metricsPort: Int?
    private var watchdog: DispatchSourceTimer?
    private var consecutiveFailures = 0
    /// Перезапусков ядра сторожем подряд без успешной проверки: выше лимита — туннель не
    /// спасают бесконечными рестартами, а честно закрывают (см. probe()).
    private var watchdogRestarts = 0
    private var lastTrafficBytes: Int64 = 0
    private var stopping = false
    /// Столько трафика за тик сторожа достаточно, чтобы считать туннель живым без проверки.
    private static let aliveTrafficBytes: Int64 = 32 * 1024
    /// Пользовательский текст ошибки сторожа — общий для отмены и backoff-отмены.
    private static let watchdogErrorMessage = "Связь не восстанавливается — включите VPN заново"
    /// После аварийного закрытия сторожем On Demand поднимает туннель снова почти сразу —
    /// этот период ядро не поднимаем вовсе (см. startTunnel), чтобы не получить цикл
    /// «cancel → рестарты ядра → cancel» на мёртвой сети.
    private static let watchdogBackoffSeconds: TimeInterval = 120
    /// Очередь запросов «прямого IP»: блокирующее ожидание NWPathMonitor не должно занимать
    /// очередь туннеля, на которой живёт сторож.
    private static let directQueue = DispatchQueue(label: "xyz.gojihub.vpn.directip")

    // MARK: - Жизненный цикл

    override func startTunnel(options: [String: NSObject]?, completionHandler: @escaping (Error?) -> Void) {
        AppLog.i(.service, "startTunnel (options: \(options?.keys.sorted() ?? []))")
        TunnelSettings.lastError = nil
        stopping = false

        // Профиль: из options (запуск из приложения) или последний сохранённый (система
        // поднимает туннель сама — VPN по требованию, переподключение после перезагрузки).
        if let payload = options?[TunnelStartOption.profile] as? String {
            let label = options?[TunnelStartOption.label] as? String ?? ""
            profile = SharedStorage.ActiveProfile(nodeId: "", label: label, payload: payload)
        } else {
            profile = SharedStorage.loadActiveProfile()
        }
        guard let profile else {
            fail(TunnelError.noProfile, completionHandler)
            return
        }

        // Backoff против цикла «сторож закрыл туннель → On Demand поднял снова»: меньше
        // watchdogBackoffSeconds после аварийного закрытия ядро не поднимаем — сразу
        // закрываем туннель с той же ошибкой (дёшево, без рестартов ядра и probe-запросов).
        if let cancelledAt = TunnelSettings.lastCancelledAt,
           Date().timeIntervalSince(cancelledAt) < Self.watchdogBackoffSeconds {
            AppLog.w(.service, "после закрытия сторожем прошло \(Int(Date().timeIntervalSince(cancelledAt))) с — backoff, ядро не поднимаем")
            fail(TunnelError.core("сторож: backoff после аварийного закрытия"), completionHandler)
            // fail() перезаписал lastError своей причиной — возвращаем пользовательский текст.
            TunnelSettings.lastError = Self.watchdogErrorMessage
            return
        }

        setTunnelNetworkSettings(makeNetworkSettings()) { [weak self] error in
            guard let self else { return }
            if let error {
                self.fail(TunnelError.settings(error.localizedDescription), completionHandler)
                return
            }
            self.queue.async {
                guard let fd = self.findTunFd() else {
                    self.fail(TunnelError.noTunFd, completionHandler)
                    return
                }
                self.tunFd = fd
                do {
                    try self.startCore(payload: profile.payload)
                    self.runningPayload = profile.payload
                    TunnelSettings.connectedSince = Date()
                    AppLog.i(.service, "туннель поднят: \(profile.label)")
                    // Счётчик рестартов сторожа обнуляется только при новом успешном
                    // подключении — не при wake() (после сна сеть может быть всё ещё мертва,
                    // и «3 рестарта не помогли» обязано сохраниться).
                    self.watchdogRestarts = 0
                    // Ядро только что поднялось — метрики с нуля, отметка трафика тоже.
                    self.lastTrafficBytes = 0
                    self.startWatchdog()
                    completionHandler(nil)
                } catch {
                    self.fail(error, completionHandler)
                }
            }
        }
    }

    override func stopTunnel(with reason: NEProviderStopReason, completionHandler: @escaping () -> Void) {
        AppLog.i(.service, "stopTunnel, причина: \(reason.rawValue)")
        stopping = true
        queue.async {
            self.watchdog?.cancel()
            self.watchdog = nil
            XrayCore.stop()
            self.runningPayload = nil
            TunnelSettings.connectedSince = nil
            completionHandler()
        }
    }

    override func sleep(completionHandler: @escaping () -> Void) {
        // Батарея (2026-10-28, как Android): во сне устройства сторож не будит радиомодуль
        // проверочными запросами каждые 30 с.
        queue.async {
            self.watchdog?.cancel()
            self.watchdog = nil
            completionHandler()
        }
    }

    override func wake() {
        // После сна соединения прокси часто уже мертвы — сторож снова следит и проверит сразу.
        queue.async {
            guard !self.stopping, self.socksPort != nil else { return }
            self.startWatchdog()
            self.watchdogTick()
        }
    }

    // MARK: - Сообщения от приложения

    override func handleAppMessage(_ messageData: Data, completionHandler: ((Data?) -> Void)?) {
        guard let request = try? JSONSerialization.jsonObject(with: messageData) as? [String: Any],
              let rawType = request["type"] as? String,
              let type = TunnelMessage(rawValue: rawType) else {
            completionHandler?(nil)
            return
        }
        switch type {
        case .stats:
            fetchStats { stats in completionHandler?(Self.encode(stats)) }
        case .status:
            // Читаем profile на очереди туннеля: параллельный switchProfile пишет его там же,
            // а handleAppMessage вызывается на системном потоке (гонка на label).
            queue.async {
                var result: [String: Any] = ["label": self.profile?.label ?? ""]
                if let since = TunnelSettings.connectedSince { result["connectedSince"] = since.timeIntervalSince1970 }
                if let error = TunnelSettings.lastError { result["lastError"] = error }
                completionHandler?(Self.encode(result))
            }
        case .switchProfile:
            guard let payload = request["payload"] as? String else { completionHandler?(nil); return }
            let label = request["label"] as? String ?? ""
            queue.async {
                self.profile = SharedStorage.ActiveProfile(nodeId: request["nodeId"] as? String ?? "", label: label, payload: payload)
                let ok = self.restartCore(reason: "смена узла на «\(label)»")
                completionHandler?(Self.encode(["ok": ok, "error": TunnelSettings.lastError ?? ""]))
            }
        case .reloadSettings:
            queue.async {
                let ok = self.restartCore(reason: "изменились настройки")
                completionHandler?(Self.encode(["ok": ok]))
            }
        case .directIP:
            // «Прямой» запрос обязательно привязывается к физическому интерфейсу: собственные
            // сокеты расширения идут по системной таблице маршрутизации (default route → utun),
            // то есть без привязки они падали в туннель — и «настоящий IP» совпадал с IP через
            // VPN (ложная «утечка»). Так приложение узнаёт свой реальный IP, не выключая VPN.
            fetchDirectIP { info in completionHandler?(Self.encode(info)) }
        }
    }

    // MARK: - Ядро

    private func startCore(payload: String) throws {
        guard let fd = tunFd else { throw TunnelError.noTunFd }
        let ports = XrayCore.freePorts(2)
        socksPort = ports.first ?? 10808
        metricsPort = ports.count > 1 ? ports[1] : 10809

        let errorLog = SharedStorage.logsDir.appendingPathComponent("xray-error.log")
        let accessLog = SharedStorage.logsDir.appendingPathComponent("xray-access.log")
        // Xray сам свои журналы не ротирует — новый файл на каждую сессию.
        try? FileManager.default.removeItem(at: errorLog)
        try? FileManager.default.removeItem(at: accessLog)

        var geoDirs = [SharedStorage.geoDir]
        if let bundled = Bundle.main.resourceURL { geoDirs.append(bundled) }

        // Ядру отдаём копию дескриптора: Go закрывает файл при сборке мусора остановленного
        // экземпляра, а сам utun принадлежит NetworkExtension и нужен для следующего запуска
        // (смена узла без разрыва туннеля).
        let coreFd = dup(fd)
        guard coreFd >= 0 else { throw TunnelError.noTunFd }

        // Владение dup-дескриптором: при успехе runXray дескриптор уходит в Go-рантайм —
        // os.NewFile закрывает его финализатором при остановке экземпляра (libXray включает
        // агрессивный GC, см. docs/ARCHITECTURE.md), из Swift закрывать нельзя (двойной
        // close). При неуспехе API libXray не документирует, успело ли ядро взять
        // дескриптор: закрываем сами — иначе каждый неудачный старт навсегда терял бы fd.

        let options = XrayConfigBuilder.Options(
            tunFd: coreFd,
            socksPort: socksPort,
            metricsPort: metricsPort,
            bypassDomains: TunnelSettings.bypassDomains,
            logLevel: TunnelSettings.logLevel,
            geoSearchDirs: geoDirs,
            errorLogPath: errorLog.path,
            accessLogPath: accessLog.path
        )
        let config: String
        do {
            config = try XrayConfigBuilder.build(payload: payload, options: options)
        } catch {
            close(coreFd)
            throw error
        }
        AppLog.d(.core, "конфиг собран: \(config.count) байт")
        let result = XrayCore.run(configJSON: config)
        guard result.success else {
            close(coreFd)
            throw TunnelError.core(result.error ?? "неизвестная ошибка")
        }
        AppLog.i(.core, "Xray запущен (\(XrayCore.version() ?? "?"))")
    }

    /// Перезапуск ядра на том же utun — без разрыва VPN для системы.
    @discardableResult
    private func restartCore(reason: String) -> Bool {
        guard let newProfile = profile else { return false }
        AppLog.i(.service, "перезапуск ядра: \(reason)")
        let previousPayload = runningPayload
        XrayCore.stop()
        runningPayload = nil
        do {
            try startCore(payload: newProfile.payload)
            runningPayload = newProfile.payload
            consecutiveFailures = 0
            // Метрики нового экземпляра ядра начинаются с нуля — старая отметка давала
            // отрицательный прирост и лишние probe каждые 30 с, пока счётчик не «перекрыт».
            lastTrafficBytes = 0
            TunnelSettings.lastError = nil
            return true
        } catch {
            AppLog.e(.service, "перезапуск ядра не удался", error)
            TunnelSettings.lastError = error.localizedDescription
            // Ядро мертво при поднятом системном туннеле — трафик никуда не идёт, а сторож
            // бесконечно перезапускал бы тот же битый профиль (он уже записан в
            // active-profile.json, системный рестарт поднял бы его же). Откат на предыдущий
            // рабочий payload, если он был.
            if let previousPayload, previousPayload != newProfile.payload {
                do {
                    try startCore(payload: previousPayload)
                    runningPayload = previousPayload
                    TunnelSettings.lastError = "Профиль «\(newProfile.label)» не запустился — работаем на предыдущем узле"
                    AppLog.w(.service, "ядро откатилось на предыдущий профиль")
                } catch {
                    AppLog.e(.service, "откат ядра тоже не удался", error)
                    TunnelSettings.lastError = error.localizedDescription
                }
            }
            return false
        }
    }

    private func fail(_ error: Error, _ completion: @escaping (Error?) -> Void) {
        AppLog.e(.service, "туннель не поднят", error)
        TunnelSettings.lastError = error.localizedDescription
        XrayCore.stop()
        runningPayload = nil
        completion(error)
    }

    // MARK: - Сеть

    private func makeNetworkSettings() -> NEPacketTunnelNetworkSettings {
        let settings = NEPacketTunnelNetworkSettings(tunnelRemoteAddress: "254.1.1.1")
        let ipv4 = NEIPv4Settings(addresses: ["172.19.0.1"], subnetMasks: ["255.255.255.252"])
        ipv4.includedRoutes = [NEIPv4Route.default()]
        // Локальная сеть мимо туннеля: CarPlay (в том числе беспроводной), AirPlay, принтеры.
        // 172.16.0.0/12 не трогаем: в нём адрес самого туннеля 172.19.0.1.
        ipv4.excludedRoutes = [
            NEIPv4Route(destinationAddress: "192.168.0.0", subnetMask: "255.255.0.0"),
            NEIPv4Route(destinationAddress: "10.0.0.0", subnetMask: "255.0.0.0"),
            NEIPv4Route(destinationAddress: "169.254.0.0", subnetMask: "255.255.0.0"),
        ]
        settings.ipv4Settings = ipv4
        // Без default-маршрута IPv6 весь v6-трафик уходил бы мимо туннеля с реальным IP
        // (Happy Eyeballs предпочитает v6 там, где он есть) — заворачиваем его тоже.
        let ipv6 = NEIPv6Settings(addresses: ["fd00::1"], networkPrefixLengths: [64])
        ipv6.includedRoutes = [NEIPv6Route.default()]
        // Link-local IPv6 — так CarPlay говорит с магнитолой по USB и Wi-Fi.
        ipv6.excludedRoutes = [NEIPv6Route(destinationAddress: "fe80::", networkPrefixLength: 10)]
        settings.ipv6Settings = ipv6
        let dns = NEDNSSettings(servers: ["1.1.1.1", "8.8.8.8"])
        dns.matchDomains = [""]
        settings.dnsSettings = dns
        settings.mtu = NSNumber(value: XrayConfigBuilder.mtu)
        return settings
    }

    /// Дескриптор utun, созданного NetworkExtension для этого процесса: перебором ищем
    /// сокет SYSPROTO_CONTROL с именем интерфейса utun* (рецепт из README Xray-core для iOS;
    /// так же делают WireGuard и sing-box).
    private func findTunFd() -> Int32? {
        var buf = [CChar](repeating: 0, count: Int(IFNAMSIZ))
        for fd: Int32 in 0...1024 {
            var len = socklen_t(buf.count)
            if getsockopt(fd, 2 /* SYSPROTO_CONTROL */, 2 /* UTUN_OPT_IFNAME */, &buf, &len) == 0 {
                let name = String(cString: buf)
                if name.hasPrefix("utun") {
                    AppLog.d(.service, "utun найден: fd=\(fd) (\(name))")
                    return fd
                }
            }
        }
        return nil
    }

    // MARK: - Сторож связи

    /// Ни ядро, ни система не сообщают, если передача данных молча умерла (обрыв сессии на
    /// сервере, протухший Reality-хендшейк). Раз в 30 с — запрос через локальный SOCKS ядра
    /// (тот же путь, что и трафик пользователя); три провала подряд — перезапуск ядра.
    private func startWatchdog() {
        watchdog?.cancel()
        consecutiveFailures = 0
        // watchdogRestarts и lastTrafficBytes не сбрасываем: счётчик рестартов живёт до
        // успешного подключения (startTunnel) или восстановившейся связи (probe) — wake()
        // после сна не должен маскировать «3 рестарта не помогли»; отметка трафика не
        // сбрасывается, потому что ядро через wake() НЕ перезапускается и метрики
        // продолжают расти (сброс — только в startTunnel/restartCore, где ядро новое).
        let timer = DispatchSource.makeTimerSource(queue: queue)
        timer.schedule(deadline: .now() + 30, repeating: 30)
        timer.setEventHandler { [weak self] in self?.watchdogTick() }
        timer.resume()
        watchdog = timer
    }

    private func watchdogTick() {
        guard !stopping, socksPort != nil else { return }
        // Через туннель и так идёт заметный трафик — он жив, проверочный запрос не нужен (как
        // Android). Если связь уже под подозрением, проверяем на каждом тике.
        guard consecutiveFailures == 0 else { return probe() }
        fetchStats { [weak self] stats in
            guard let self else { return }
            self.queue.async {
                let total = ((stats["up"] as? Int64) ?? 0) + ((stats["down"] as? Int64) ?? 0)
                let received = total - self.lastTrafficBytes
                self.lastTrafficBytes = total
                if received >= Self.aliveTrafficBytes { return }
                self.probe()
            }
        }
    }

    private func probe() {
        guard !stopping, let port = socksPort else { return }
        probeThroughProxy(port: port) { [weak self] alive in
            guard let self else { return }
            self.queue.async {
                if alive {
                    if self.consecutiveFailures > 0 { AppLog.i(.service, "сторож: связь восстановилась сама") }
                    self.consecutiveFailures = 0
                    self.watchdogRestarts = 0
                    return
                }
                self.consecutiveFailures += 1
                AppLog.w(.service, "сторож: проверка через туннель не прошла (\(self.consecutiveFailures) подряд)")
                if self.consecutiveFailures >= 3 && !self.stopping {
                    // Лимит: три перезапуска ядра подряд не вернули связь — дальше крутить
                    // бессмысленно (и это вечный цикл рестартов на мёртвой сети). Закрываем
                    // туннель с ошибкой, чтобы пользователь увидел состояние, а не «подключено
                    // без интернета».
                    if self.watchdogRestarts >= 3 {
                        AppLog.e(.service, "сторож: 3 перезапуска ядра не помогли — останавливаю туннель")
                        TunnelSettings.lastError = Self.watchdogErrorMessage
                        // Отмечаем момент отмены: On Demand поднимет туннель снова почти сразу,
                        // и startTunnel по этой метке не даст ядру рестартовать в цикле.
                        TunnelSettings.lastCancelledAt = Date()
                        self.cancelTunnelWithError(TunnelError.core("сторож: связь не восстановилась после перезапусков ядра"))
                        return
                    }
                    self.watchdogRestarts += 1
                    self.restartCore(reason: "сторож: туннель молча умер")
                }
            }
        }
    }

    private func proxySession(port: Int, timeout: TimeInterval) -> URLSession {
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = timeout
        config.timeoutIntervalForResource = timeout
        config.connectionProxyDictionary = [
            "SOCKSEnable": 1,
            "SOCKSProxy": "127.0.0.1",
            "SOCKSPort": port
        ]
        return URLSession(configuration: config)
    }

    private func probeThroughProxy(port: Int, completion: @escaping (Bool) -> Void) {
        let session = proxySession(port: port, timeout: 8)
        let url = URL(string: AppConstants.defaultPingURL)!
        session.dataTask(with: url) { _, response, error in
            let code = (response as? HTTPURLResponse)?.statusCode ?? 0
            completion(error == nil && (200..<400).contains(code))
            session.finishTasksAndInvalidate()
        }.resume()
    }

    // MARK: - Статистика

    /// Счётчики входящего TUN-инбаунда из /debug/vars метрик ядра (stats.inbound.tun-in).
    private func fetchStats(_ completion: @escaping ([String: Any]) -> Void) {
        guard let port = metricsPort, let url = URL(string: "http://127.0.0.1:\(port)/debug/vars") else {
            completion([:])
            return
        }
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 2
        config.connectionProxyDictionary = [:]
        let session = URLSession(configuration: config)
        session.dataTask(with: url) { data, _, _ in
            defer { session.finishTasksAndInvalidate() }
            guard let data,
                  let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let stats = json["stats"] as? [String: Any],
                  let inbound = stats["inbound"] as? [String: Any],
                  let tun = inbound["tun-in"] as? [String: Any] else {
                completion([:])
                return
            }
            let up = (tun["uplink"] as? NSNumber)?.int64Value ?? 0
            let down = (tun["downlink"] as? NSNumber)?.int64Value ?? 0
            completion(["up": up, "down": down])
        }.resume()
    }

    // MARK: - Прямой IP (мимо туннеля)

    /// Физический интерфейс устройства (Wi-Fi/сотовый/кабель): собственные сокеты расширения
    /// маршрутизируются системной таблицей (default route → utun), поэтому без привязки
    /// «прямой» запрос уходил в туннель. Интерфейс годится только не utun/lo — иначе вернёмся
    /// к той же проблеме.
    private static func physicalInterface() -> NWInterface? {
        let monitor = NWPathMonitor()
        let lock = NSLock()
        var settled = false
        var found: NWInterface?
        let signal = DispatchSemaphore(value: 0)
        monitor.pathUpdateHandler = { path in
            lock.lock()
            defer { lock.unlock() }
            guard !settled else { return }
            settled = true
            found = path.availableInterfaces.first {
                ($0.type == .wifi || $0.type == .cellular || $0.type == .wiredEthernet)
                    && !$0.name.hasPrefix("utun") && !$0.name.hasPrefix("lo")
            }
            signal.signal()
        }
        monitor.start(queue: DispatchQueue(label: "xyz.gojihub.vpn.path"))
        _ = signal.wait(timeout: .now() + 2)
        monitor.cancel()
        return found
    }

    /// IP «мимо VPN»: простой HTTPS GET к ipwho.is через NWConnection, привязанный к
    /// физическому интерфейсу. Если физического интерфейса нет — пустой ответ: приложению
    /// честнее показать «неизвестно», чем IP через VPN под видом настоящего.
    private func fetchDirectIP(_ completion: @escaping ([String: Any]) -> Void) {
        Self.directQueue.async {
            guard let interface = Self.physicalInterface() else {
                AppLog.w(.service, "прямой IP: физический интерфейс не найден")
                completion([:])
                return
            }
            AppLog.d(.service, "прямой IP через интерфейс \(interface.name)")
            let params = NWParameters.tls
            params.requiredInterface = interface
            let connection = NWConnection(host: "ipwho.is", port: 443, using: params)
            let once = DirectIPOnce(connection: connection, completion: completion)
            connection.stateUpdateHandler = { state in
                switch state {
                case .ready:
                    // Минимальный HTTP/1.1-запрос; Connection: close — сервер закроет соединение
                    // после тела, это естественная граница ответа.
                    let request = "GET / HTTP/1.1\r\nHost: ipwho.is\r\nUser-Agent: curl/8.7\r\nAccept: application/json\r\nConnection: close\r\n\r\n"
                    connection.send(content: Data(request.utf8), completion: .contentProcessed { _ in
                        once.receive()
                    })
                case .failed(let error):
                    AppLog.w(.service, "прямой IP: соединение не удалось (\(error.localizedDescription))")
                    once.finish()
                case .cancelled:
                    once.finish()
                default:
                    break
                }
            }
            connection.start(queue: Self.directQueue)
            Self.directQueue.asyncAfter(deadline: .now() + 5) { once.finish() }
        }
    }

    private static func encode(_ object: [String: Any]) -> Data? {
        try? JSONSerialization.data(withJSONObject: object)
    }
}

/// Ответ «прямого IP» отправляется ровно один раз (тело получено, таймаут 5 с или ошибка —
/// что раньше); соединение всегда отменяется, чтобы не висело на очереди.
private final class DirectIPOnce {
    private let lock = NSLock()
    private var finished = false
    private var body = Data()
    private let connection: NWConnection
    private let completion: ([String: Any]) -> Void

    init(connection: NWConnection, completion: @escaping ([String: Any]) -> Void) {
        self.connection = connection
        self.completion = completion
    }

    func receive() {
        connection.receive(minimumIncompleteLength: 1, maximumLength: 64 * 1024) { chunk, _, isComplete, error in
            if let chunk { self.body.append(chunk) }
            // Connection: close — сервер закрывает соединение после тела; обрыв после последнего
            // сегмента тоже считаем концом ответа, тело уже накоплено.
            if isComplete || error != nil { self.finish(); return }
            self.receive()
        }
    }

    func finish() {
        lock.lock()
        guard !finished else { lock.unlock(); return }
        finished = true
        lock.unlock()
        // Обрываем цикл удержания connection → stateUpdateHandler → … до отмены.
        connection.stateUpdateHandler = nil
        connection.cancel()
        completion(Self.parse(body))
    }

    /// Тело HTTP-ответа после пустой строки — разбирается так же, как раньше (ipwho.is).
    private static func parse(_ data: Data) -> [String: Any] {
        guard let headerEnd = data.range(of: Data("\r\n\r\n".utf8)),
              let json = try? JSONSerialization.jsonObject(with: data[headerEnd.upperBound...]) as? [String: Any] else {
            return [:]
        }
        var out: [String: Any] = [:]
        out["ip"] = json["ip"] as? String
        out["country"] = json["country"] as? String
        out["isp"] = (json["connection"] as? [String: Any])?["isp"] as? String
        return out
    }
}

enum TunnelError: LocalizedError {
    case noProfile
    case noTunFd
    case settings(String)
    case core(String)

    var errorDescription: String? {
        switch self {
        case .noProfile: return "Не выбран сервер — откройте приложение и выберите узел"
        case .noTunFd: return "Не удалось получить VPN-интерфейс"
        case .settings(let message): return "Система не приняла настройки VPN: \(message)"
        case .core(let message): return "Ядро Xray не запустилось: \(message)"
        }
    }
}
