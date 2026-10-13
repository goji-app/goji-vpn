import Foundation

enum LogLevel: String, CaseIterable, Codable, Identifiable {
    case none, error, warning, info, debug
    var id: String { rawValue }

    var rank: Int {
        switch self {
        case .none: return 0
        case .error: return 1
        case .warning: return 2
        case .info: return 3
        case .debug: return 4
        }
    }

    var tag: String {
        switch self {
        case .none: return "-"
        case .error: return "E"
        case .warning: return "W"
        case .info: return "I"
        case .debug: return "D"
        }
    }
}

enum LogCategory: String, CaseIterable, Identifiable {
    case main       // приложение: вход, API, интерфейс
    case core       // ядро Xray и сборка конфигурации
    case subscription // подписка, список серверов, пинг
    case service    // расширение туннеля: жизненный цикл, сторож связи
    var id: String { rawValue }
}

/// Журнал в файлах App Group — по файлу на категорию, общий для приложения и туннеля
/// (их можно посмотреть и отправить в поддержку из «Настройки → Журналы»).
/// Никогда не пишет токены сессии и содержимое профилей подписки.
enum AppLog {
    private static let queue = DispatchQueue(label: "xyz.gojihub.vpn.log")
    private static let maxBytes = 512 * 1024
    private static let keepBytes = 256 * 1024
    private static let formatter: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "MM-dd HH:mm:ss.SSS"
        return f
    }()

    static let processTag: String = Bundle.main.bundlePath.hasSuffix(".appex") ? "tun" : "app"

    static func d(_ category: LogCategory, _ message: @autoclosure () -> String) { write(category, .debug, message()) }
    static func i(_ category: LogCategory, _ message: @autoclosure () -> String) { write(category, .info, message()) }
    static func w(_ category: LogCategory, _ message: @autoclosure () -> String) { write(category, .warning, message()) }
    static func e(_ category: LogCategory, _ message: @autoclosure () -> String, _ error: Error? = nil) {
        let suffix = error.map { ": \($0.localizedDescription)" } ?? ""
        write(category, .error, message() + suffix)
    }

    static func write(_ category: LogCategory, _ level: LogLevel, _ message: String) {
        let current = TunnelSettings.logLevel
        guard current != .none, level.rank <= current.rank else { return }
        let line = "\(formatter.string(from: Date())) \(level.tag)/\(processTag) \(message)\n"
        #if DEBUG
        print("[\(category.rawValue)] \(line)", terminator: "")
        #endif
        queue.async {
            let url = fileURL(category)
            guard let data = line.data(using: .utf8) else { return }
            if let handle = try? FileHandle(forWritingTo: url) {
                defer { try? handle.close() }
                let size = (try? handle.seekToEnd()) ?? 0
                try? handle.write(contentsOf: data)
                if size + UInt64(data.count) > UInt64(maxBytes) { trim(url) }
            } else {
                try? data.write(to: url, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
            }
        }
    }

    static func read(_ category: LogCategory) -> String {
        queue.sync {
            (try? String(contentsOf: fileURL(category), encoding: .utf8)) ?? ""
        }
    }

    static func clear(_ category: LogCategory) {
        queue.sync { try? FileManager.default.removeItem(at: fileURL(category)) }
    }

    /// Все журналы одним текстом — для вложения в обращение в поддержку.
    static func exportAll() -> String {
        LogCategory.allCases.map { cat in
            "===== \(cat.rawValue) =====\n" + read(cat)
        }.joined(separator: "\n")
    }

    static func fileURL(_ category: LogCategory) -> URL {
        SharedStorage.logsDir.appendingPathComponent("\(category.rawValue).log")
    }

    private static func trim(_ url: URL) {
        guard let data = try? Data(contentsOf: url), data.count > keepBytes else { return }
        var tail = data.suffix(keepBytes)
        if let newline = tail.firstIndex(of: 0x0A) { tail = tail[(newline + 1)...] }
        try? Data(tail).write(to: url, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
    }
}
