import Foundation

/// Демонстрационные данные для снимков экрана App Store и проверки интерфейса в симуляторе
/// (там NetworkExtension не работает). Включается ТОЛЬКО аргументом запуска
/// `-GojiDemo YES` (xcrun simctl launch … -GojiDemo YES) — из интерфейса недоступно,
/// сеть и бэкенд в этом режиме не используются.
enum DemoMode {
    static var isEnabled: Bool {
        UserDefaults.standard.bool(forKey: "GojiDemo")
    }

    /// Состояние VPN для снимков: «off» | «connecting» | «on» (аргумент -GojiDemoVPN on).
    static var vpnState: String {
        UserDefaults.standard.string(forKey: "GojiDemoVPN") ?? "on"
    }

    /// Стартовая вкладка для снимков: home | servers | subscription | settings.
    static var initialTab: AppModel.Tab {
        switch UserDefaults.standard.string(forKey: "GojiDemoTab") {
        case "servers": return .servers
        case "subscription": return .subscription
        case "settings": return .settings
        default: return .home
        }
    }

    @MainActor
    static func install() {
        let nodes: [VPNNode] = [
            demoNode("🇩🇪 Германия", host: "de.example.com", geo: CountryGeoLookup.find("germany")),
            demoNode("🇳🇱 Нидерланды", host: "nl.example.com", geo: CountryGeoLookup.find("netherlands")),
            demoNode("🇫🇮 Финляндия", host: "fi.example.com", geo: CountryGeoLookup.find("finland")),
            demoNode("🇱🇻 Латвия", host: "lv.example.com", geo: CountryGeoLookup.find("latvia")),
            demoNode("🇪🇪 Эстония", host: "ee.example.com", geo: CountryGeoLookup.find("estonia")),
            demoNode("Автовыбор серверов EU", host: "eu.example.com", geo: nil),
            demoNode("Автовыбор серверов LTE", host: "lte.example.com", geo: CountryGeoLookup.find("lte"))
        ]
        let expire = Calendar.current.date(byAdding: .day, value: 24, to: Date()) ?? Date()
        let subscription = SubscriptionInfo(
            id: 1, name: "Goji", isPrimary: true, isActive: true, planName: "Базовый",
            expireAt: ISO8601DateFormatter().string(from: expire), daysLeft: 24,
            subscriptionLink: nil, deviceLimit: 3,
            traffic: TrafficInfo(usedBytes: 18_400_000_000, limitBytes: 0, isUnlimited: true),
            kind: "paid"
        )
        SubscriptionStore.shared.installDemo(subscription: subscription, nodes: nodes)
        VPNController.shared.installDemo(state: vpnState)
        var pings: [String: Int] = [:]
        for (i, node) in nodes.enumerated() { pings[node.id] = [48, 61, 74, 39, 44, 52, 96][i % 7] }
        PingService.shared.installDemo(pings)
    }

    private static func demoNode(_ name: String, host: String, geo: CountryGeo?) -> VPNNode {
        VPNNode(id: "\(name)|\(host):443", name: name, host: host, port: 443, payload: "{}", geo: geo, uuid: "00000000-0000-0000-0000-000000000000")
    }
}
