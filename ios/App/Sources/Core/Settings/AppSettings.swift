import Foundation
import SwiftUI

enum ThemeMode: String, CaseIterable, Identifiable {
    case system, light, dark
    var id: String { rawValue }

    var colorScheme: ColorScheme? {
        switch self {
        case .system: return nil
        case .light: return .light
        case .dark: return .dark
        }
    }

    var title: String { L("theme.\(rawValue)") }
}

/// Способ замера пинга. ICMP на iOS обычному приложению недоступен (нет сырых сокетов),
/// поэтому, в отличие от Android, здесь его нет.
enum PingMethod: String, CaseIterable, Identifiable {
    case proxyGet, proxyHead, tcp
    var id: String { rawValue }
    var title: String { L("ping.method.\(rawValue)") }
}

enum ServerSort: String, CaseIterable, Identifiable {
    case favorites, ping, name
    var id: String { rawValue }
    var title: String { L("servers.sort.\(rawValue)") }
}

/// Автоподключение через системный механизм VPN On Demand (работает без открытого приложения).
enum AutoConnectMode: String, CaseIterable, Identifiable {
    case off            // только вручную
    case untrustedWifi  // в чужих Wi-Fi (кроме доверенных)
    case always         // держать VPN включённым всегда (кроме доверенных Wi-Fi)
    var id: String { rawValue }
    var title: String { L("autoconnect.\(rawValue)") }
    var subtitle: String { L("autoconnect.\(rawValue).desc") }
}

/// Пользовательские настройки. Всё, что нужно туннелю, дублируется в App Group (TunnelSettings).
@MainActor
final class AppSettings: ObservableObject {
    static let shared = AppSettings()
    private let defaults = UserDefaults.standard

    @Published var themeMode: ThemeMode { didSet { defaults.set(themeMode.rawValue, forKey: "theme") } }
    @Published var haptics: Bool { didSet { defaults.set(haptics, forKey: "haptics") } }
    @Published var pingMethod: PingMethod { didSet { defaults.set(pingMethod.rawValue, forKey: "ping.method") } }
    @Published var pingURL: String { didSet { defaults.set(pingURL, forKey: "ping.url") } }
    @Published var serverSort: ServerSort { didSet { defaults.set(serverSort.rawValue, forKey: "servers.sort") } }
    @Published var favorites: Set<String> { didSet { defaults.set(Array(favorites), forKey: "servers.favorites") } }
    @Published var killSwitch: Bool { didSet { defaults.set(killSwitch, forKey: "vpn.killSwitch") } }
    @Published var autoConnect: AutoConnectMode { didSet { defaults.set(autoConnect.rawValue, forKey: "vpn.autoConnect") } }
    @Published var trustedSSIDs: [String] { didSet { defaults.set(trustedSSIDs, forKey: "vpn.trustedSSIDs") } }
    @Published var disconnectOnTrusted: Bool { didSet { defaults.set(disconnectOnTrusted, forKey: "vpn.disconnectOnTrusted") } }
    @Published var autoSwitchOnJam: Bool { didSet { defaults.set(autoSwitchOnJam, forKey: "vpn.autoSwitchOnJam") } }
    @Published var bypassDomains: [String] { didSet { TunnelSettings.bypassDomains = bypassDomains } }
    @Published var logLevel: LogLevel { didSet { TunnelSettings.logLevel = logLevel } }
    @Published var privacyAccepted: Bool { didSet { defaults.set(privacyAccepted, forKey: "privacy.accepted") } }
    /// Последний узел, выбранный вручную до автоматического ухода на LTE при глушении.
    @Published var preferredNodeId: String? { didSet { defaults.set(preferredNodeId, forKey: "vpn.preferredNode") } }

    init() {
        themeMode = ThemeMode(rawValue: defaults.string(forKey: "theme") ?? "") ?? .system
        haptics = defaults.object(forKey: "haptics") as? Bool ?? true
        pingMethod = PingMethod(rawValue: defaults.string(forKey: "ping.method") ?? "") ?? .proxyGet
        pingURL = defaults.string(forKey: "ping.url") ?? AppConstants.defaultPingURL
        serverSort = ServerSort(rawValue: defaults.string(forKey: "servers.sort") ?? "") ?? .favorites
        favorites = Set(defaults.stringArray(forKey: "servers.favorites") ?? [])
        killSwitch = defaults.bool(forKey: "vpn.killSwitch")
        autoConnect = AutoConnectMode(rawValue: defaults.string(forKey: "vpn.autoConnect") ?? "") ?? .off
        trustedSSIDs = defaults.stringArray(forKey: "vpn.trustedSSIDs") ?? []
        disconnectOnTrusted = defaults.object(forKey: "vpn.disconnectOnTrusted") as? Bool ?? true
        autoSwitchOnJam = defaults.object(forKey: "vpn.autoSwitchOnJam") as? Bool ?? true
        bypassDomains = TunnelSettings.bypassDomains
        logLevel = TunnelSettings.logLevel
        privacyAccepted = defaults.bool(forKey: "privacy.accepted")
        preferredNodeId = defaults.string(forKey: "vpn.preferredNode")
    }

    func toggleFavorite(_ id: String) {
        if favorites.contains(id) { favorites.remove(id) } else { favorites.insert(id) }
    }

    /// «https://www.Example.com:443/path» → «example.com»; кириллица → punycode через URL.
    static func normalizeDomain(_ input: String) -> String? {
        var d = input.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        if let range = d.range(of: "://") { d = String(d[range.upperBound...]) }
        for sep in ["/", "?", "#", ":"] {
            if let idx = d.firstIndex(of: Character(sep)) { d = String(d[..<idx]) }
        }
        for prefix in ["*.", ".", "www."] where d.hasPrefix(prefix) { d.removeFirst(prefix.count) }
        while d.hasSuffix(".") { d.removeLast() }
        guard !d.isEmpty else { return nil }
        let ascii = URL(string: "https://\(d)")?.host ?? d
        let pattern = "^[a-z0-9-]+(\\.[a-z0-9-]+)+$"
        guard ascii.range(of: pattern, options: .regularExpression) != nil else {
            return punycodeHost(d)
        }
        return ascii
    }

    /// URLComponents кодирует IDN-хост в punycode (xn--…) при сборке URL.
    private static func punycodeHost(_ host: String) -> String? {
        var components = URLComponents()
        components.scheme = "https"
        components.host = host
        guard let encoded = components.url?.host, encoded.range(of: "^[a-z0-9-]+(\\.[a-z0-9-]+)+$", options: .regularExpression) != nil else {
            return nil
        }
        return encoded
    }
}
