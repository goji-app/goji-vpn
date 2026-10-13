import AppIntents

/// Команды для «Быстрых команд» и Siri: «Включи Goji VPN» / «Выключи Goji VPN».
/// Выполняются в фоне без открытия приложения (запуск туннеля через NETunnelProviderManager).
struct ConnectVPNIntent: AppIntent {
    static var title: LocalizedStringResource = "intent.connect.title"
    static var description = IntentDescription("intent.connect.desc")

    @MainActor
    func perform() async throws -> some IntentResult & ProvidesDialog {
        let vpn = VPNController.shared
        await vpn.load()
        guard let node = SubscriptionStore.shared.selectedNode else {
            return .result(dialog: IntentDialog(stringLiteral: L("connect.no_server")))
        }
        if !vpn.isConnected { await vpn.connect(node: node) }
        return .result(dialog: IntentDialog(stringLiteral: L("intent.connect.done", node.displayName)))
    }
}

struct DisconnectVPNIntent: AppIntent {
    static var title: LocalizedStringResource = "intent.disconnect.title"
    static var description = IntentDescription("intent.disconnect.desc")

    @MainActor
    func perform() async throws -> some IntentResult & ProvidesDialog {
        let vpn = VPNController.shared
        await vpn.load()
        await vpn.disconnect()
        return .result(dialog: IntentDialog(stringLiteral: L("intent.disconnect.done")))
    }
}

struct GojiShortcuts: AppShortcutsProvider {
    static var appShortcuts: [AppShortcut] {
        AppShortcut(
            intent: ConnectVPNIntent(),
            phrases: ["Включи \(.applicationName)", "Turn on \(.applicationName)"],
            shortTitle: "intent.connect.short",
            systemImageName: "lock.shield"
        )
        AppShortcut(
            intent: DisconnectVPNIntent(),
            phrases: ["Выключи \(.applicationName)", "Turn off \(.applicationName)"],
            shortTitle: "intent.disconnect.short",
            systemImageName: "lock.open"
        )
    }
}
