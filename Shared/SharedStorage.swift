import Foundation

/// Файлы и настройки в общем контейнере App Group — единственный канал данных между
/// приложением и расширением туннеля (у них разные процессы и разные песочницы).
enum SharedStorage {
    static let containerURL: URL = resolveContainerURL()

    private static func resolveContainerURL() -> URL {
        if let url = FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: AppConstants.appGroup) {
            return url
        }
        // Без подписи (сборка в CI/симуляторе без App Group) — своя папка, чтобы UI работал.
        let fallback = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("SharedFallback", isDirectory: true)
        try? FileManager.default.createDirectory(at: fallback, withIntermediateDirectories: true)
        return fallback
    }

    static let defaults: UserDefaults = UserDefaults(suiteName: AppConstants.appGroup) ?? .standard

    static func directory(_ name: String) -> URL {
        let url = containerURL.appendingPathComponent(name, isDirectory: true)
        if !FileManager.default.fileExists(atPath: url.path) {
            try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        }
        return url
    }

    /// Обновлённые приложением геоданные (geoip.dat, geosite.dat, mobile_whitelist_*.txt).
    static var geoDir: URL { directory("GeoData") }
    static var logsDir: URL { directory("Logs") }
    static var cacheDir: URL { directory("Cache") }

    /// Профиль узла, к которому сейчас подключаемся (JSON-конфиг Xray из подписки) — нужен
    /// расширению, когда туннель поднимает сама система (VPN по требованию, «Всегда включён»),
    /// без options от приложения.
    static var activeProfileURL: URL { containerURL.appendingPathComponent("active-profile.json") }

    /// Запись с защитой данных: файл недоступен до первой разблокировки после перезагрузки,
    /// но доступен расширению в фоне после неё (VPN по требованию при заблокированном экране).
    static func write(_ data: Data, to url: URL) throws {
        try data.write(to: url, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
    }

    // MARK: - Активный профиль

    struct ActiveProfile: Codable {
        var nodeId: String
        var label: String
        var payload: String
    }

    static func saveActiveProfile(_ profile: ActiveProfile) {
        guard let data = try? JSONEncoder().encode(profile) else { return }
        try? write(data, to: activeProfileURL)
    }

    static func loadActiveProfile() -> ActiveProfile? {
        guard let data = try? Data(contentsOf: activeProfileURL) else { return nil }
        return try? JSONDecoder().decode(ActiveProfile.self, from: data)
    }
}

/// Настройки, которые нужны расширению туннеля (пишет приложение, читает туннель).
enum TunnelSettings {
    private static let bypassKey = "tunnel.bypassDomains"
    private static let logLevelKey = "log.level"
    private static let lastErrorKey = "tunnel.lastError"
    private static let connectedSinceKey = "tunnel.connectedSince"

    static var bypassDomains: [String] {
        get { SharedStorage.defaults.stringArray(forKey: bypassKey) ?? [] }
        set { SharedStorage.defaults.set(newValue, forKey: bypassKey) }
    }

    static var logLevel: LogLevel {
        get { LogLevel(rawValue: SharedStorage.defaults.string(forKey: logLevelKey) ?? "") ?? .info }
        set { SharedStorage.defaults.set(newValue.rawValue, forKey: logLevelKey) }
    }

    /// Последняя ошибка запуска туннеля — приложение показывает её пользователю, т.к. система
    /// сама сообщает только «не удалось подключиться» без подробностей.
    static var lastError: String? {
        get { SharedStorage.defaults.string(forKey: lastErrorKey) }
        set { SharedStorage.defaults.set(newValue, forKey: lastErrorKey) }
    }

    static var connectedSince: Date? {
        get {
            let t = SharedStorage.defaults.double(forKey: connectedSinceKey)
            return t > 0 ? Date(timeIntervalSince1970: t) : nil
        }
        set { SharedStorage.defaults.set(newValue?.timeIntervalSince1970 ?? 0, forKey: connectedSinceKey) }
    }
}
