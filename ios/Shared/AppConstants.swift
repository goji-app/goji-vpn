import Foundation

/// Константы, общие для приложения и расширения туннеля.
enum AppConstants {
    /// App Group — читается из Info.plist (ключ GojiAppGroup), чтобы идентификатор задавался
    /// в одном месте (Config/Base.xcconfig) и не расходился между целями.
    static let appGroup: String = Bundle.main.object(forInfoDictionaryKey: "GojiAppGroup") as? String
        ?? "group.xyz.gojihub.vpn"
    static let tunnelBundleId: String = Bundle.main.object(forInfoDictionaryKey: "GojiTunnelBundleId") as? String
        ?? "xyz.gojihub.vpn.tunnel"

    /// Хосты кабинета и сайта загрузки — из Info.plist (Config/Hosts.local.xcconfig, в репозитории
    /// их нет). Пустое значение — ошибка сборки, о ней лучше узнать сразу при запуске.
    static let siteHost = infoHost("GojiSiteHost")
    static let downloadHost = infoHost("GojiDownloadHost")

    private static func infoHost(_ key: String) -> String {
        guard let host = Bundle.main.object(forInfoDictionaryKey: key) as? String,
              !host.isEmpty, !host.contains("$(") else {
            fatalError("Не задан \(key): запустите Scripts/write-hosts-xcconfig.sh перед сборкой")
        }
        return host
    }

    /// Клиентский бэкенд (шоп). Админ-панель Remnawave из приложения туда
    /// ходить нельзя никогда (там админские права на всех пользователей).
    static let apiBaseURL = URL(string: "https://\(siteHost)/")!

    static let privacyPolicyURL = URL(string: "https://telegra.ph/Politika-konfidencialnosti-Goji-VPN-09-20")!
    static let termsURL = URL(string: "https://telegra.ph/Polzovatelskoe-soglashenie-Goji-VPN-09-20")!

    static let defaultPingURL = "https://cp.cloudflare.com/generate_204"
    static let tunnelSessionName = "Goji VPN"

    static var appVersion: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0"
    }
    static var buildNumber: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? "0"
    }

    /// User-Agent собственных запросов приложения к кабинету.
    static var apiUserAgent: String { "Goji/\(appVersion)/iOS" }

    /// Схема deeplink'а QR-переноса входа (совместима с Android: godjivpn://transfer?s=…&r=…&t=…).
    static let transferScheme = "godjivpn"
    static let transferHost = "transfer"
}

/// Ключи сообщений приложение ⇄ расширение (NETunnelProviderSession.sendProviderMessage).
enum TunnelMessage: String, Codable {
    case stats          // счётчики трафика ядра
    case status         // время подключения, последняя ошибка
    case switchProfile  // сменить узел без разрыва туннеля
    case directIP       // публичный IP «мимо VPN» (трафик самого расширения туннель не использует)
    case reloadSettings // перечитать настройки (сайты мимо VPN и т.п.) и перезапустить ядро
}

/// Ключи options при запуске туннеля из приложения.
enum TunnelStartOption {
    static let profile = "profile"
    static let label = "label"
}
