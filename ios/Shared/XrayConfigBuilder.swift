import Foundation

/// Превращает профиль узла из подписки (готовый Xray-конфиг: dns/routing/outbounds) в итоговый
/// конфиг ядра для туннеля. Логика перенесена из GodjiVpnService.establishTunnel (Android):
///  - свои инбаунды (TUN по fd из NetworkExtension + локальный SOCKS для сторожа связи);
///  - подстановка geosite:/geoip: готовыми доменами/подсетями (см. GeoDataParser) + всегда
///    «российские сервисы → direct» (category-ru + mobile-whitelist);
///  - «Сайты мимо VPN» первым правилом;
///  - таймауты/keepalive против обрывов в мобильных сетях;
///  - счётчики трафика (stats + metrics) для экрана «Главная».
enum XrayConfigBuilder {
    static let mtu = 1500

    struct Options {
        /// fd utun-интерфейса из NEPacketTunnelProvider (nil — без TUN, например для тестов).
        var tunFd: Int32?
        var socksPort: Int?
        var metricsPort: Int?
        var bypassDomains: [String] = []
        var logLevel: LogLevel = .info
        /// Каталоги, где искать geosite.dat/geoip.dat/whitelist — по порядку приоритета
        /// (обновлённые приложением в App Group → встроенные в расширение).
        var geoSearchDirs: [URL] = []
        var errorLogPath: String?
        var accessLogPath: String?
    }

    enum BuildError: LocalizedError {
        case badProfile
        case legacyConvertFailed

        var errorDescription: String? {
            switch self {
            case .badProfile: return "Не удалось разобрать конфигурацию сервера"
            case .legacyConvertFailed: return "Не удалось преобразовать ссылку сервера"
            }
        }
    }

    static let proxyProtocols: Set<String> = ["vless", "vmess", "trojan", "shadowsocks", "hysteria"]

    static func build(payload: String, options: Options) throws -> String {
        var root = try parseProfile(payload)

        var env = root["env"] as? [String: Any] ?? [:]
        var inbounds: [[String: Any]] = []
        let sniffing: [String: Any] = ["enabled": true, "destOverride": ["http", "tls", "quic"]]
        if let fd = options.tunFd {
            inbounds.append([
                "tag": "tun-in",
                "port": 0,
                "protocol": "tun",
                "settings": ["name": "utun", "mtu": mtu],
                "sniffing": sniffing
            ])
            env["xray.tun.fd"] = String(fd)
            env["XRAY_TUN_FD"] = String(fd)
        }
        if let port = options.socksPort {
            inbounds.append([
                "tag": "socks-in",
                "listen": "127.0.0.1",
                "port": port,
                "protocol": "socks",
                "settings": ["udp": true],
                "sniffing": sniffing
            ])
        }
        root["inbounds"] = inbounds
        if let assetDir = options.geoSearchDirs.first?.path {
            env["xray.location.asset"] = assetDir
            env["XRAY_LOCATION_ASSET"] = assetDir
        }
        root["env"] = env

        resolveGeoData(&root, searchDirs: options.geoSearchDirs)
        applyBypassDomains(&root, domains: options.bypassDomains)
        applyResilienceDefaults(&root, options: options)
        applyKeepAliveDefaults(&root)
        if let port = options.metricsPort { applyStats(&root, metricsPort: port) }

        let data = try JSONSerialization.data(withJSONObject: root)
        guard let json = String(data: data, encoding: .utf8) else { throw BuildError.badProfile }
        return json
    }

    static func parseProfile(_ payload: String) throws -> [String: Any] {
        let trimmed = payload.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.hasPrefix("{") {
            guard let data = trimmed.data(using: .utf8),
                  let obj = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
                throw BuildError.badProfile
            }
            return obj
        }
        guard let converted = XrayCore.convertShareLinks(trimmed) else { throw BuildError.legacyConvertFailed }
        return converted
    }

    // MARK: - Геоданные

    static func geoFile(_ name: String, in dirs: [URL]) -> URL? {
        for dir in dirs {
            let url = dir.appendingPathComponent(name)
            if FileManager.default.fileExists(atPath: url.path) { return url }
        }
        return nil
    }

    static func loadLines(_ name: String, in dirs: [URL]) -> [String] {
        guard let url = geoFile(name, in: dirs), let text = try? String(contentsOf: url, encoding: .utf8) else { return [] }
        return text.split(whereSeparator: \.isNewline)
            .map { $0.trimmingCharacters(in: .whitespaces) }
            .filter { !$0.isEmpty && !$0.hasPrefix("#") }
    }

    static func resolveGeoData(_ root: inout [String: Any], searchDirs: [URL]) {
        var geosite = Set<String>()
        var geoip = Set<String>()
        collectGeoTags(root, geosite: &geosite, geoip: &geoip)
        AppLog.d(.core, "geo: запрошены geosite=\(geosite.sorted()) geoip=\(geoip.sorted())")

        // category-ru — всегда: часть профилей полагается только на geoip:ru, а он не ловит
        // российские сервисы за глобальными CDN (подтверждено на Android).
        let backendRoutesCategoryRu = geosite.contains("CATEGORY-RU")
        geosite.insert("CATEGORY-RU")

        var domainsByTag: [String: [String]] = [:]
        if let file = geoFile("geosite.dat", in: searchDirs) {
            domainsByTag = GeoDataParser.resolveDomains(file: file, tags: geosite)
        }
        let whitelistDomains = loadLines("mobile_whitelist_domains.txt", in: searchDirs).map { "domain:\($0)" }
        // Известные пробелы category-ru (те же, что дополняет Android-клиент).
        let extraRu = ["ozone.ru", "ozonusercontent.com", "tinkoff.ru", "tbank.ru", "raiffeisen.ru"].map { "domain:\($0)" }
        var ru = domainsByTag["CATEGORY-RU"] ?? []
        ru.append(contentsOf: extraRu)
        ru.append(contentsOf: whitelistDomains)
        domainsByTag["CATEGORY-RU"] = Array(NSOrderedSet(array: ru)) as? [String] ?? ru

        var cidrsByTag: [String: [String]] = [:]
        if !geoip.isEmpty, let file = geoFile("geoip.dat", in: searchDirs) {
            cidrsByTag = GeoDataParser.resolveCidrs(file: file, tags: geoip)
        }
        let whitelistCidrs = loadLines("mobile_whitelist_cidr.txt", in: searchDirs)

        for tag in geosite where domainsByTag[tag]?.isEmpty ?? true {
            AppLog.w(.core, "geo: geosite:\(tag) не найден — условие будет пропущено")
        }
        for tag in geoip where cidrsByTag[tag]?.isEmpty ?? true {
            AppLog.w(.core, "geo: geoip:\(tag) не найден (geoip.dat ещё не скачан?) — условие будет пропущено")
        }

        root = expandGeoArrays(root, domains: domainsByTag, cidrs: cidrsByTag) as? [String: Any] ?? root

        guard var routing = root["routing"] as? [String: Any],
              let rules = routing["rules"] as? [[String: Any]] else { return }
        let listKeys = ["ip", "domain"]
        let otherKeys = ["port", "network", "source", "sourcePort", "inboundTag", "protocol", "attrs", "user"]
        let kept = rules.filter { rule in
            let hasList = listKeys.contains { ((rule[$0] as? [Any])?.count ?? 0) > 0 }
            let hasOther = otherKeys.contains { rule[$0] != nil }
            return hasList || hasOther
        }
        routing["rules"] = injectOwnDirectRules(
            kept,
            root: root,
            categoryRu: domainsByTag["CATEGORY-RU"] ?? [],
            injectDomainRule: !backendRoutesCategoryRu,
            extraCidrs: whitelistCidrs
        )
        root["routing"] = routing
    }

    /// Правила «российское → direct» перед последним (catch-all) правилом профиля.
    static func injectOwnDirectRules(
        _ rules: [[String: Any]],
        root: [String: Any],
        categoryRu: [String],
        injectDomainRule: Bool,
        extraCidrs: [String]
    ) -> [[String: Any]] {
        guard !rules.isEmpty else { return rules }
        let tags = (root["outbounds"] as? [[String: Any]] ?? []).compactMap { $0["tag"] as? String }
        guard tags.contains("direct") else { return rules }
        var extra: [[String: Any]] = []
        if injectDomainRule && !categoryRu.isEmpty {
            extra.append(["type": "field", "domain": categoryRu, "outboundTag": "direct"])
        }
        if !extraCidrs.isEmpty {
            extra.append(["type": "field", "ip": extraCidrs, "outboundTag": "direct"])
        }
        guard !extra.isEmpty else { return rules }
        return Array(rules.dropLast()) + extra + [rules[rules.count - 1]]
    }

    static func collectGeoTags(_ node: Any, geosite: inout Set<String>, geoip: inout Set<String>) {
        if let dict = node as? [String: Any] {
            for value in dict.values { collectGeoTags(value, geosite: &geosite, geoip: &geoip) }
        } else if let array = node as? [Any] {
            if let strings = array as? [String], !strings.isEmpty {
                for s in strings {
                    if s.hasPrefix("geosite:") { geosite.insert(String(s.dropFirst(8)).uppercased()) }
                    else if s.hasPrefix("geoip:") { geoip.insert(String(s.dropFirst(6)).uppercased()) }
                }
            } else {
                for item in array { collectGeoTags(item, geosite: &geosite, geoip: &geoip) }
            }
        }
    }

    /// Раскрывает geosite:/geoip: в чисто строковых массивах; ext: и ненайденные теги убирает.
    /// Массив, ставший пустым, удаляется из объекта целиком (для dns.servers[].domains «нет
    /// ограничения» — это отсутствие ключа, а не пустой список).
    static func expandGeoArrays(_ node: Any, domains: [String: [String]], cidrs: [String: [String]]) -> Any? {
        if let dict = node as? [String: Any] {
            var out: [String: Any] = [:]
            for (key, value) in dict {
                if let expanded = expandGeoArrays(value, domains: domains, cidrs: cidrs) { out[key] = expanded }
            }
            return out
        }
        if let array = node as? [Any] {
            if let strings = array as? [String], !strings.isEmpty {
                var expanded: [String] = []
                for s in strings {
                    if s.hasPrefix("geosite:") {
                        expanded.append(contentsOf: domains[String(s.dropFirst(8)).uppercased()] ?? [])
                    } else if s.hasPrefix("geoip:") {
                        expanded.append(contentsOf: cidrs[String(s.dropFirst(6)).uppercased()] ?? [])
                    } else if s.hasPrefix("ext:") {
                        continue
                    } else {
                        expanded.append(s)
                    }
                }
                return expanded.isEmpty ? nil : expanded
            }
            return array.compactMap { expandGeoArrays($0, domains: domains, cidrs: cidrs) }
        }
        return node
    }

    // MARK: - Сайты мимо VPN

    static func applyBypassDomains(_ root: inout [String: Any], domains: [String]) {
        guard !domains.isEmpty else { return }
        var outbounds = root["outbounds"] as? [[String: Any]] ?? []
        var directTag = outbounds.first { ($0["protocol"] as? String) == "freedom" && !(($0["tag"] as? String) ?? "").isEmpty }?["tag"] as? String
        if directTag == nil {
            directTag = "user-bypass-direct"
            outbounds.append(["tag": "user-bypass-direct", "protocol": "freedom"])
            root["outbounds"] = outbounds
        }
        var routing = root["routing"] as? [String: Any] ?? [:]
        let rules = routing["rules"] as? [[String: Any]] ?? []
        let rule: [String: Any] = ["type": "field", "domain": domains.map { "domain:\($0)" }, "outboundTag": directTag!]
        routing["rules"] = [rule] + rules
        root["routing"] = routing
        AppLog.i(.core, "сайты мимо VPN: \(domains.count) доменов → \(directTag!)")
    }

    // MARK: - Надёжность

    static func applyResilienceDefaults(_ root: inout [String: Any], options: Options) {
        var routing = root["routing"] as? [String: Any] ?? [:]
        if routing["domainMatcher"] == nil { routing["domainMatcher"] = "hybrid" }
        if routing["domainStrategy"] == nil { routing["domainStrategy"] = "IPIfNonMatch" }
        root["routing"] = routing

        if root["policy"] == nil {
            root["policy"] = ["levels": ["0": ["handshake": 8]]]
        }

        var log = root["log"] as? [String: Any] ?? [:]
        // Батарея и память (2026-10-19, порт Android): бэкенд присылает loglevel "debug" с
        // dnsLog — ядро писало строку на каждое соединение и DNS-запрос. Без режима отладки в
        // приложении оставляем одни предупреждения, даже если профиль просит больше.
        if options.logLevel == .debug {
            if log["loglevel"] == nil { log["loglevel"] = "info" }
        } else {
            log["loglevel"] = "warning"
            log["dnsLog"] = false
        }
        if let error = options.errorLogPath { log["error"] = error }
        if options.logLevel == .debug, let access = options.accessLogPath {
            log["access"] = access
        } else {
            log["access"] = "none"
        }
        root["log"] = log
        relaxObservatory(&root)
    }

    /// Батарея (2026-10-19, порт Android): burstObservatory из профиля опрашивал все узлы раз в
    /// минуту — круглосуточно, при погашенном экране тоже. Балансировщику для выбора живого
    /// узла достаточно раза в 5 минут; реже, чем задал бэкенд, не делаем.
    static let observatoryMinIntervalSeconds: Double = 5 * 60

    static func relaxObservatory(_ root: inout [String: Any]) {
        if var burst = root["burstObservatory"] as? [String: Any] {
            var ping = burst["pingConfig"] as? [String: Any] ?? [:]
            raiseInterval(&ping, field: "interval")
            burst["pingConfig"] = ping
            root["burstObservatory"] = burst
        }
        if var observatory = root["observatory"] as? [String: Any] {
            raiseInterval(&observatory, field: "probeInterval")
            root["observatory"] = observatory
        }
    }

    static func raiseInterval(_ holder: inout [String: Any], field: String) {
        let seconds = parseDurationSeconds(holder[field] as? String)
        // Без значения у Xray действует дефолт (1 минута у burstObservatory) — тоже поднимаем.
        if seconds == nil || seconds! < observatoryMinIntervalSeconds {
            holder[field] = "\(Int(observatoryMinIntervalSeconds / 60))m"
        }
    }

    /// "1m", "30s", "1m30s", "500ms", "2h" → секунды; nil, если строка пустая или непонятная.
    static func parseDurationSeconds(_ value: String?) -> Double? {
        guard let text = value?.trimmingCharacters(in: .whitespaces), !text.isEmpty,
              let regex = try? NSRegularExpression(pattern: "(\\d+(?:\\.\\d+)?)(ms|s|m|h)") else { return nil }
        let ns = text as NSString
        let matches = regex.matches(in: text, range: NSRange(location: 0, length: ns.length))
        guard !matches.isEmpty, matches.map({ ns.substring(with: $0.range) }).joined() == text else { return nil }
        return matches.reduce(0.0) { total, m in
            let n = Double(ns.substring(with: m.range(at: 1))) ?? 0
            switch ns.substring(with: m.range(at: 2)) {
            case "ms": return total + n / 1000
            case "s": return total + n
            case "m": return total + n * 60
            default: return total + n * 3600
            }
        }
    }

    /// TCP keepalive 30с (NAT/мобильные сети рвут простаивающие соединения раньше дефолтных
    /// 45с Xray) и HTTP-keepalive XHTTP — то же лечение «обрывается через 5–10 минут», что и
    /// в Android/Windows-клиентах.
    static func applyKeepAliveDefaults(_ root: inout [String: Any]) {
        guard var outbounds = root["outbounds"] as? [[String: Any]] else { return }
        for i in outbounds.indices {
            let protocolName = outbounds[i]["protocol"] as? String ?? ""
            guard proxyProtocols.contains(protocolName),
                  var stream = outbounds[i]["streamSettings"] as? [String: Any] else { continue }
            if protocolName != "hysteria" {
                var sockopt = stream["sockopt"] as? [String: Any] ?? [:]
                if ((sockopt["tcpKeepAliveIdle"] as? NSNumber)?.intValue ?? 0) <= 0 { sockopt["tcpKeepAliveIdle"] = 30 }
                if ((sockopt["tcpKeepAliveInterval"] as? NSNumber)?.intValue ?? 0) <= 0 { sockopt["tcpKeepAliveInterval"] = 30 }
                stream["sockopt"] = sockopt
            }
            if var xhttp = stream["xhttpSettings"] as? [String: Any],
               var extra = xhttp["extra"] as? [String: Any],
               var xmux = extra["xmux"] as? [String: Any] {
                if ((xmux["hKeepAlivePeriod"] as? NSNumber)?.intValue ?? 0) <= 0 { xmux["hKeepAlivePeriod"] = 30 }
                extra["xmux"] = xmux
                xhttp["extra"] = extra
                stream["xhttpSettings"] = xhttp
            }
            outbounds[i]["streamSettings"] = stream
        }
        root["outbounds"] = outbounds
    }

    // MARK: - Счётчики трафика

    static func applyStats(_ root: inout [String: Any], metricsPort: Int) {
        root["stats"] = [String: Any]()
        var policy = root["policy"] as? [String: Any] ?? [:]
        var system = policy["system"] as? [String: Any] ?? [:]
        system["statsInboundUplink"] = true
        system["statsInboundDownlink"] = true
        policy["system"] = system
        root["policy"] = policy
        root["metrics"] = ["listen": "127.0.0.1:\(metricsPort)"]
    }

    // MARK: - Разбор профиля для списка серверов

    /// Адрес/порт/UUID основного прокси-аутбаунда профиля (для списка серверов и TCP-пинга).
    static func endpoint(of profile: [String: Any]) -> (host: String, port: Int, uuid: String?)? {
        let outbounds = profile["outbounds"] as? [[String: Any]] ?? []
        for ob in outbounds {
            guard proxyProtocols.contains(ob["protocol"] as? String ?? ""),
                  let settings = ob["settings"] as? [String: Any] else { continue }
            if let vnext = (settings["vnext"] as? [[String: Any]])?.first,
               let host = vnext["address"] as? String, !host.isEmpty {
                let port = (vnext["port"] as? NSNumber)?.intValue ?? 443
                let uuid = ((vnext["users"] as? [[String: Any]])?.first?["id"] as? String).flatMap { $0.isEmpty ? nil : $0 }
                return (host, port > 0 ? port : 443, uuid)
            }
            if let server = (settings["servers"] as? [[String: Any]])?.first,
               let host = server["address"] as? String, !host.isEmpty {
                let port = (server["port"] as? NSNumber)?.intValue ?? 443
                return (host, port > 0 ? port : 443, nil)
            }
            if let host = settings["address"] as? String, !host.isEmpty {
                let port = (settings["port"] as? NSNumber)?.intValue ?? 443
                return (host, port > 0 ? port : 443, nil)
            }
        }
        return nil
    }

    /// Теги VLESS-аутбаундов, между которыми выбирает балансировщик узла («proxy», «proxy-2»…).
    static func proxyOutboundTags(of payload: String) -> [String] {
        guard let profile = try? parseProfileJSONOnly(payload) else { return ["proxy"] }
        let tags = (profile["outbounds"] as? [[String: Any]] ?? [])
            .filter { (($0["settings"] as? [String: Any])?["vnext"]) != nil }
            .compactMap { $0["tag"] as? String }
            .filter { !$0.isEmpty }
        return tags.isEmpty ? ["proxy"] : tags
    }

    private static func parseProfileJSONOnly(_ payload: String) throws -> [String: Any] {
        guard let data = payload.data(using: .utf8),
              let obj = try JSONSerialization.jsonObject(with: data) as? [String: Any] else { throw BuildError.badProfile }
        return obj
    }
}
