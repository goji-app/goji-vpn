import Foundation

/// Обновление геоданных для маршрутизации «российское → напрямую» (раз в неделю, как на
/// Android): runetfreedom/russia-v2ray-rules-dat (обновляется у них каждые 6 часов) и
/// краудсорс-список hxehex/russia-mobile-internet-whitelist. Сначала качаем копию с зеркал нашего
/// сайта загрузки (обновляется каждые 6 часов, открывается в РФ без VPN), при их недоступности —
/// с GitHub. Файлы кладутся в App Group —
/// расширение туннеля берёт их оттуда в первую очередь, встроенные — запасной вариант.
enum GeoDataUpdater {
    private struct Source {
        let url: String
        let file: String
        let minSize: Int

        /// Зеркала сайта по порядку, затем исходный адрес на GitHub.
        var urls: [URL] {
            (GeoDataUpdater.mirrorSites.map { "\($0)/geo/\(file)" } + [url]).compactMap(URL.init(string:))
        }
    }

    /// Зеркала сайта загрузки (исходники — «10. Сайт загрузки»). Новое зеркало = новая строка.
    static let mirrorSites = ["https://get.gojihub.xyz"]

    private static let sources = [
        Source(url: "https://raw.githubusercontent.com/runetfreedom/russia-v2ray-rules-dat/release/geosite.dat", file: "geosite.dat", minSize: 50_000),
        Source(url: "https://raw.githubusercontent.com/runetfreedom/russia-v2ray-rules-dat/release/geoip.dat", file: "geoip.dat", minSize: 50_000),
        Source(url: "https://raw.githubusercontent.com/hxehex/russia-mobile-internet-whitelist/main/whitelist.txt", file: "mobile_whitelist_domains.txt", minSize: 1_000),
        Source(url: "https://raw.githubusercontent.com/hxehex/russia-mobile-internet-whitelist/main/cidrwhitelist.txt", file: "mobile_whitelist_cidr.txt", minSize: 1_000)
    ]

    private static let lastUpdateKey = "geo.lastUpdate"
    private static let interval: TimeInterval = 7 * 86400

    static var lastUpdate: Date? {
        let t = UserDefaults.standard.double(forKey: lastUpdateKey)
        return t > 0 ? Date(timeIntervalSince1970: t) : nil
    }

    /// geoip.dat в сборку не входит (≈23 МБ) — при его отсутствии обновляемся сразу.
    static func refreshIfNeeded() async {
        let geoipMissing = !FileManager.default.fileExists(atPath: SharedStorage.geoDir.appendingPathComponent("geoip.dat").path)
        if !geoipMissing, let last = lastUpdate, Date().timeIntervalSince(last) < interval { return }
        await refresh()
    }

    @discardableResult
    static func refresh() async -> Bool {
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 30
        config.timeoutIntervalForResource = 180
        let session = URLSession(configuration: config)
        var allOk = true
        for source in sources {
            var updated = false
            for url in source.urls where !updated {
                updated = await download(url, to: source, session: session)
            }
            if !updated { allOk = false }
        }
        if allOk { UserDefaults.standard.set(Date().timeIntervalSince1970, forKey: lastUpdateKey) }
        return allOk
    }

    /// Одна попытка закачки; битый или слишком маленький ответ не затирает рабочий файл.
    private static func download(_ url: URL, to source: Source, session: URLSession) async -> Bool {
        do {
            let (tmp, response) = try await session.download(from: url)
            let size = (try? FileManager.default.attributesOfItem(atPath: tmp.path)[.size] as? NSNumber)?.intValue ?? 0
            guard (response as? HTTPURLResponse)?.statusCode == 200, size >= source.minSize else {
                AppLog.w(.core, "геоданные: \(source.file) с \(url.host ?? "?") отброшен (размер \(size))")
                return false
            }
            let dest = SharedStorage.geoDir.appendingPathComponent(source.file)
            _ = try? FileManager.default.removeItem(at: dest)
            try FileManager.default.moveItem(at: tmp, to: dest)
            try? (dest as NSURL).setResourceValue(URLFileProtection.completeUntilFirstUserAuthentication, forKey: .fileProtectionKey)
            AppLog.i(.core, "геоданные: \(source.file) обновлён с \(url.host ?? "?") (\(size) байт)")
            return true
        } catch {
            AppLog.w(.core, "геоданные: \(source.file) с \(url.host ?? "?") — \(error.localizedDescription)")
            return false
        }
    }
}
