import Foundation
import NetworkExtension
import UIKit

/// Управление VPN-профилем Goji в системе (NETunnelProviderManager) и связь с расширением.
@MainActor
final class VPNController: ObservableObject {
    static let shared = VPNController()

    enum Phase: Equatable {
        case disconnected, connecting, connected, disconnecting
    }

    @Published private(set) var phase: Phase = .disconnected
    @Published private(set) var connectedSince: Date?
    @Published var lastError: String?
    @Published private(set) var downBytesPerSec: Double = 0
    @Published private(set) var upBytesPerSec: Double = 0
    @Published private(set) var downHistory: [Double] = []
    @Published private(set) var upHistory: [Double] = []
    /// Реальная страна выхода (по IP через туннель) — название узла не гарантия.
    @Published private(set) var actualCountry: String?

    private var manager: NETunnelProviderManager?
    private var statusObserver: NSObjectProtocol?
    private var statsTimer: Timer?
    private var lastCounters: (up: Int64, down: Int64, at: Date)?
    private var countryTask: Task<Void, Never>?
    private let historySize = 30

    var isConnected: Bool { phase == .connected }
    var isBusy: Bool { phase == .connecting || phase == .disconnecting }

    /// Только для DemoMode (снимки экрана в симуляторе, где NetworkExtension недоступен).
    func installDemo(state: String) {
        switch state {
        case "off": phase = .disconnected
        case "connecting": phase = .connecting
        default:
            phase = .connected
            connectedSince = Date().addingTimeInterval(-(42 * 60 + 17))
            actualCountry = nil
            downBytesPerSec = 3_400_000
            upBytesPerSec = 420_000
            downHistory = (0..<30).map { 2_000_000 + 1_800_000 * abs(sin(Double($0) / 3)) }
            upHistory = (0..<30).map { 250_000 + 200_000 * abs(cos(Double($0) / 4)) }
        }
    }

    // MARK: - Загрузка профиля

    func load() async {
        guard !DemoMode.isEnabled else { return }
        do {
            let managers = try await NETunnelProviderManager.loadAllFromPreferences()
            // Своего профиля нет — только новый: перехват чужого VPN-профиля (другого
            // VPN-приложения) ломал бы его конфигурацию при первом же сохранении.
            manager = managers.first { ($0.protocolConfiguration as? NETunnelProviderProtocol)?.providerBundleIdentifier == AppConstants.tunnelBundleId }
                ?? NETunnelProviderManager()
        } catch {
            AppLog.e(.main, "загрузка VPN-профиля", error)
        }
        observeStatus()
        updatePhase()
    }

    private func observeStatus() {
        if let statusObserver { NotificationCenter.default.removeObserver(statusObserver) }
        statusObserver = NotificationCenter.default.addObserver(forName: .NEVPNStatusDidChange, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in self?.updatePhase() }
        }
    }

    private func updatePhase() {
        guard let connection = manager?.connection else {
            phase = .disconnected
            return
        }
        let previous = phase
        switch connection.status {
        case .connected:
            phase = .connected
            connectedSince = connection.connectedDate ?? TunnelSettings.connectedSince ?? Date()
        case .connecting, .reasserting:
            phase = .connecting
        case .disconnecting:
            phase = .disconnecting
        default:
            phase = .disconnected
            connectedSince = nil
        }
        if phase != previous { phaseChanged(from: previous) }
    }

    private func phaseChanged(from previous: Phase) {
        AppLog.i(.main, "VPN: \(previous) → \(phase)")
        switch phase {
        case .connected:
            lastError = nil
            startStatsPolling()
            checkActualCountry()
            // Вибрация — только на переход из «подключается», не при открытии приложения
            // с уже поднятым туннелем.
            if previous == .connecting { Haptics.success() }
        case .disconnected:
            stopStatsPolling()
            actualCountry = nil
            // Причину показывает и «подключается → отключено» (ошибка старта), и
            // «подключено → отключено»: сторож закрывает туннель, записав причину в
            // TunnelSettings, — раньше это выглядело как тихое отключение.
            if previous == .connecting || previous == .connected, let error = TunnelSettings.lastError {
                lastError = error
            }
            if previous == .connecting, lastError != nil {
                Haptics.error()
            } else if previous == .connected || previous == .disconnecting {
                Haptics.light()
            }
        default:
            break
        }
    }

    // MARK: - Подключение

    func connect(node: VPNNode) async {
        lastError = nil
        SharedStorage.saveActiveProfile(.init(nodeId: node.id, label: node.displayName, payload: node.payload))
        TunnelSettings.lastError = nil
        // Ручное подключение снимает backoff сторожа: пауза после аварийного закрытия нужна
        // только против цикла On Demand, а не против самого пользователя.
        TunnelSettings.lastCancelledAt = nil
        do {
            let manager = try await preparedManager(enableOnDemand: true)
            let options: [String: NSObject] = [
                TunnelStartOption.profile: node.payload as NSString,
                TunnelStartOption.label: node.displayName as NSString
            ]
            phase = .connecting
            try manager.connection.startVPNTunnel(options: options)
            AppLog.i(.main, "запуск туннеля: \(node.displayName)")
        } catch {
            AppLog.e(.main, "не удалось запустить туннель", error)
            lastError = Self.describe(error)
            updatePhase()
        }
    }

    /// Любой NEVPNError раньше показывался как «нет разрешения» — теперь частные случаи
    /// честные: недействительная конфигурация или права — разными сообщениями, остальное —
    /// как есть (описание от системы). Сравнение по сырым кодам NSError (kNEVPNError…):
    /// имена кейсов NEVPNError.Code меняются между SDK, сырые значения стабильны.
    private static func describe(_ error: Error) -> String {
        let nsError = error as NSError
        guard nsError.domain == NEVPNErrorDomain else { return error.localizedDescription }
        switch nsError.code {
        case 1, 3:   // configurationInvalid, configurationReadWriteFailed
            return L("vpn.error.configuration")
        case 9:      // permissionDenied
            return L("vpn.error.permission")
        default:
            return error.localizedDescription
        }
    }

    func disconnect() async {
        guard let manager else { return }
        // При включённом VPN On Demand система сразу подняла бы туннель обратно — ручное
        // отключение снимает автоподключение до следующего ручного включения.
        if manager.isOnDemandEnabled {
            manager.isOnDemandEnabled = false
            try? await manager.saveToPreferences()
        }
        // Ручное отключение — не ошибка: убираем причину сторожа, чтобы phaseChanged
        // («подключено → отключено») не показал её как баннер.
        TunnelSettings.lastError = nil
        manager.connection.stopVPNTunnel()
    }

    func toggle() async {
        // DemoMode — только картинки для снимков экранов: ни записи active-profile.json,
        // ни системного VPN-профиля, ни реального туннеля.
        guard !DemoMode.isEnabled else { return }
        if phase == .connected || phase == .connecting {
            await disconnect()
        } else if let node = SubscriptionStore.shared.selectedNode {
            await connect(node: node)
        } else {
            lastError = L("connect.no_server")
        }
    }

    /// Пользователь выбрал другой узел: при активном туннеле — переключаем ядро на лету.
    func nodeSelected(_ node: VPNNode) {
        SharedStorage.saveActiveProfile(.init(nodeId: node.id, label: node.displayName, payload: node.payload))
        guard phase == .connected || phase == .connecting else { return }
        actualCountry = nil
        // Пока идёт подключение, переключить ядро на лету нельзя (старый запрос падал
        // «не удалось сменить узел») — честно просим подождать; выбранный узел сохранён
        // в active-profile.json и поднимется следующим подключением.
        if phase == .connecting {
            lastError = L("vpn.error.busy")
            return
        }
        Task {
            let response = await send(["type": TunnelMessage.switchProfile.rawValue, "payload": node.payload, "label": node.displayName, "nodeId": node.id])
            if (response?["ok"] as? Bool) == true {
                checkActualCountry()
            } else {
                lastError = (response?["error"] as? String).flatMap { $0.isEmpty ? nil : $0 } ?? L("vpn.error.switch")
            }
        }
    }

    /// Применить изменённые настройки: kill switch / автоподключение — в системный профиль,
    /// «Сайты мимо VPN» и уровень журнала — перезапуском ядра в расширении.
    func applySettings(reloadCore: Bool) async {
        if let manager {
            configure(manager, enableOnDemand: manager.isOnDemandEnabled || phase == .connected)
            do {
                try await manager.saveToPreferences()
                try await manager.loadFromPreferences()
            } catch {
                AppLog.e(.main, "сохранение VPN-профиля", error)
            }
        }
        if reloadCore, phase == .connected {
            _ = await send(["type": TunnelMessage.reloadSettings.rawValue])
        }
    }

    private func preparedManager(enableOnDemand: Bool) async throws -> NETunnelProviderManager {
        let manager = self.manager ?? NETunnelProviderManager()
        configure(manager, enableOnDemand: enableOnDemand)
        // Первый вызов показывает системный запрос «Разрешить добавление конфигурации VPN».
        try await manager.saveToPreferences()
        try await manager.loadFromPreferences()
        self.manager = manager
        observeStatus()
        return manager
    }

    private func configure(_ manager: NETunnelProviderManager, enableOnDemand: Bool) {
        let settings = AppSettings.shared
        let proto = (manager.protocolConfiguration as? NETunnelProviderProtocol) ?? NETunnelProviderProtocol()
        proto.providerBundleIdentifier = AppConstants.tunnelBundleId
        proto.serverAddress = AppConstants.tunnelSessionName
        proto.disconnectOnSleep = false
        // Kill switch: весь трафик устройства только через туннель; если он упал — сети нет,
        // а не «тихо мимо VPN». Локальная сеть (AirPlay, принтеры) остаётся доступной.
        proto.includeAllNetworks = settings.killSwitch
        proto.excludeLocalNetworks = true
        manager.protocolConfiguration = proto
        manager.localizedDescription = AppConstants.tunnelSessionName
        manager.isEnabled = true

        var rules: [NEOnDemandRule] = []
        let trusted = settings.trustedSSIDs
        if !trusted.isEmpty && settings.disconnectOnTrusted {
            let rule = NEOnDemandRuleDisconnect()
            rule.interfaceTypeMatch = .wiFi
            rule.ssidMatch = trusted
            rules.append(rule)
        }
        switch settings.autoConnect {
        case .off:
            break
        case .untrustedWifi:
            let rule = NEOnDemandRuleConnect()
            rule.interfaceTypeMatch = .wiFi
            rules.append(rule)
        case .always:
            rules.append(NEOnDemandRuleConnect())
        }
        let hasAutoRules = settings.autoConnect != .off || (!trusted.isEmpty && settings.disconnectOnTrusted)
        if hasAutoRules {
            rules.append(NEOnDemandRuleIgnore())
            manager.onDemandRules = rules
            manager.isOnDemandEnabled = enableOnDemand
        } else {
            manager.onDemandRules = []
            manager.isOnDemandEnabled = false
        }
    }

    /// Удалить VPN-профиль из системы (при выходе из аккаунта).
    func removeProfile() async {
        await disconnect()
        try? await manager?.removeFromPreferences()
        manager = nil
        phase = .disconnected
    }

    // MARK: - Сообщения расширению

    func send(_ message: [String: Any]) async -> [String: Any]? {
        guard let session = manager?.connection as? NETunnelProviderSession,
              let data = try? JSONSerialization.data(withJSONObject: message) else { return nil }
        return await withCheckedContinuation { continuation in
            do {
                try session.sendProviderMessage(data) { response in
                    let json = response.flatMap { try? JSONSerialization.jsonObject(with: $0) as? [String: Any] }
                    continuation.resume(returning: json)
                }
            } catch {
                continuation.resume(returning: nil)
            }
        }
    }

    // MARK: - Скорость

    private func startStatsPolling() {
        stopStatsPolling()
        lastCounters = nil
        statsTimer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in
            Task { @MainActor in await self?.pollStats() }
        }
    }

    private func stopStatsPolling() {
        statsTimer?.invalidate()
        statsTimer = nil
        downBytesPerSec = 0
        upBytesPerSec = 0
        downHistory = []
        upHistory = []
    }

    private func pollStats() async {
        guard phase == .connected, UIApplication.shared.applicationState == .active,
              let stats = await send(["type": TunnelMessage.stats.rawValue]),
              let up = (stats["up"] as? NSNumber)?.int64Value,
              let down = (stats["down"] as? NSNumber)?.int64Value else { return }
        let now = Date()
        if let last = lastCounters {
            let dt = max(now.timeIntervalSince(last.at), 0.25)
            // Счётчики сбрасываются при перезапуске ядра (смена узла) — отрицательное = 0.
            downBytesPerSec = Double(max(down - last.down, 0)) / dt
            upBytesPerSec = Double(max(up - last.up, 0)) / dt
            downHistory = Array((downHistory + [downBytesPerSec]).suffix(historySize))
            upHistory = Array((upHistory + [upBytesPerSec]).suffix(historySize))
        }
        lastCounters = (up, down, now)
    }

    // MARK: - Реальная страна выхода

    private func checkActualCountry() {
        countryTask?.cancel()
        countryTask = Task {
            // Узлы-балансировщики «прогреваются» несколько секунд — несколько попыток.
            for attempt in 1...6 {
                try? await Task.sleep(nanoseconds: UInt64(attempt) * 1_000_000_000)
                if Task.isCancelled || phase != .connected { return }
                if let info = await LeakChecker.fetchIP(timeout: 5), let country = info.country {
                    actualCountry = country
                    AppLog.i(.main, "страна выхода: \(country)")
                    return
                }
            }
        }
    }
}

enum Haptics {
    @MainActor static func success() {
        guard AppSettings.shared.haptics else { return }
        UINotificationFeedbackGenerator().notificationOccurred(.success)
    }

    @MainActor static func error() {
        guard AppSettings.shared.haptics else { return }
        UINotificationFeedbackGenerator().notificationOccurred(.error)
    }

    @MainActor static func light() {
        guard AppSettings.shared.haptics else { return }
        UIImpactFeedbackGenerator(style: .light).impactOccurred()
    }
}
