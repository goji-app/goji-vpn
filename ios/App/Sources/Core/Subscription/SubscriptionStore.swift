import Foundation
import UIKit

/// Один сервер (профиль) из подписки пользователя.
struct VPNNode: Codable, Identifiable, Equatable, Hashable {
    let id: String
    let name: String
    let host: String
    let port: Int
    /// Готовый Xray-конфиг профиля (JSON) или — для старого формата — vless://-ссылка.
    let payload: String
    let geo: CountryGeo?
    /// UUID клиента в Remnawave (одинаков для всех профилей пользователя).
    let uuid: String?

    var displayName: String { CountryGeoLookup.stripLeadingFlag(name) }
    var flag: String { geo?.flag ?? (CountryGeoLookup.isEuRegion(name) ? "🇪🇺" : "🌐") }
    var isLTE: Bool { name.range(of: "LTE", options: .caseInsensitive) != nil }
    var isJSONProfile: Bool { payload.trimmingCharacters(in: .whitespaces).hasPrefix("{") }
}

/// Подписка, список серверов и выбранный узел. Последний успешно полученный список хранится
/// на диске (App Group, защита данных iOS): если кабинет или сервер подписки недоступны
/// на старте, пользователь всё равно видит серверы и может подключиться.
@MainActor
final class SubscriptionStore: ObservableObject {
    static let shared = SubscriptionStore()

    @Published private(set) var subscription: SubscriptionInfo?
    @Published private(set) var nodes: [VPNNode] = []
    @Published private(set) var selectedId: String?
    @Published private(set) var broadcasts: [Broadcast] = []
    @Published private(set) var isRefreshing = false
    @Published private(set) var lastRefreshFailed = false
    /// true, если хоть раз за сессию пришёл ответ /api/subscriptions (для «нет подписки»).
    @Published private(set) var subscriptionLoaded = false

    private let api = APIClient.shared

    private struct Cache: Codable {
        var nodes: [VPNNode]
        var selectedId: String?
        var subscriptionLink: String?
        var subscription: SubscriptionInfo?
    }

    private var cacheURL: URL { SharedStorage.cacheDir.appendingPathComponent("servers.json") }
    private var cachedLink: String?

    init() {
        if let data = try? Data(contentsOf: cacheURL), let cache = try? JSONDecoder().decode(Cache.self, from: data) {
            nodes = cache.nodes
            selectedId = cache.selectedId
            cachedLink = cache.subscriptionLink
            subscription = cache.subscription
        }
    }

    var selectedNode: VPNNode? { nodes.first { $0.id == selectedId } ?? nodes.first }

    /// UUID клиента в Remnawave — реальный идентификатор для поддержки.
    var clientUUID: String? { nodes.lazy.compactMap(\.uuid).first }

    @discardableResult
    func refresh() async -> Bool {
        guard !DemoMode.isEnabled, AuthService.shared.isLoggedIn, !isRefreshing else { return false }
        isRefreshing = true
        defer { isRefreshing = false }

        var active: SubscriptionInfo?
        do {
            let response: SubscriptionsResponse = try await api.get("api/subscriptions")
            let list = response.subscriptions ?? []
            // Основная — но только среди подписок со ссылкой на серверы: с 2026-10-19 в списке
            // бывает подписка без тарифа и ссылки, она не должна вытеснить рабочую.
            let withLink = list.filter { !($0.subscriptionLink ?? "").isEmpty }
            active = withLink.first { $0.isPrimary == true } ?? withLink.first { $0.isActive == true }
                ?? withLink.first ?? list.first
            subscriptionLoaded = true
            // С бэкенда 7.1.0 список не отдаёт traffic — дозапрашиваем подробную запись.
            if let current = active, current.traffic == nil {
                if let detail: SubscriptionInfo = try? await api.get("api/subscriptions/\(current.id)") {
                    active = detail
                }
            }
        } catch {
            AppLog.e(.subscription, "getSubscriptions", error)
        }
        if let active {
            NotificationScheduler.subscriptionUpdated(old: subscription, new: active)
        }
        if subscriptionLoaded { subscription = active }

        if let list: [Broadcast] = try? await api.get("api/broadcasts/completed") {
            NotificationScheduler.broadcastsUpdated(old: broadcasts, new: list)
            broadcasts = list
        }

        // кабинет и сервер подписки — независимые хосты: если шоп недоступен, всё равно
        // пробуем обновить серверы по последней известной ссылке.
        let freshLink = active?.subscriptionLink
        guard let link = freshLink ?? cachedLink else {
            lastRefreshFailed = !subscriptionLoaded
            persist()
            return subscriptionLoaded
        }
        let fetched = await fetchNodes(link)
        // Пустой ответ = «не удалось получить», а не «серверов больше нет» — не затираем.
        if !fetched.isEmpty {
            nodes = fetched
            if selectedId == nil || !fetched.contains(where: { $0.id == selectedId }) {
                selectedId = fetched.first?.id
            }
        }
        if freshLink != nil { cachedLink = freshLink }
        persist()
        lastRefreshFailed = freshLink == nil && fetched.isEmpty
        return !lastRefreshFailed
    }

    func select(_ id: String) {
        guard selectedId != id, nodes.contains(where: { $0.id == id }) else { return }
        selectedId = id
        persist()
        if let node = selectedNode { VPNController.shared.nodeSelected(node) }
    }

    func clear() {
        subscription = nil
        nodes = []
        selectedId = nil
        broadcasts = []
        cachedLink = nil
        subscriptionLoaded = false
        try? FileManager.default.removeItem(at: cacheURL)
        try? FileManager.default.removeItem(at: SharedStorage.activeProfileURL)
    }

    /// Активация пробного периода (POST /api/dashboard/trial — тот же вызов, что на сайте).
    func activateTrial() async throws {
        let response: TrialResponse = try await api.post("api/dashboard/trial", body: [String: String]())
        if response.success == false {
            throw APIError.http(400, response.message ?? L("trial.error"))
        }
        await refresh()
    }

    /// Только для DemoMode (снимки экрана): данные без сети и без записи на диск.
    func installDemo(subscription: SubscriptionInfo, nodes: [VPNNode]) {
        self.subscription = subscription
        self.nodes = nodes
        self.selectedId = nodes.first?.id
        self.subscriptionLoaded = true
        self.broadcasts = [
            Broadcast(id: "demo-1", content: L("demo.news"), createdAt: ISO8601DateFormatter().string(from: Date()), buttonsUpper: nil, buttonsLower: nil)
        ]
    }

    private func persist() {
        guard !DemoMode.isEnabled else { return }
        let cache = Cache(nodes: nodes, selectedId: selectedId, subscriptionLink: cachedLink, subscription: subscription)
        guard let data = try? JSONEncoder().encode(cache) else { return }
        try? SharedStorage.write(data, to: cacheURL)
    }

    // MARK: - Список серверов (сервер подписки)

    /// Без X-HWID сервер подписки отдаёт заглушку («Приложение не поддерживается»), с ним —
    /// JSON-массив готовых Xray-профилей. User-Agent v2rayNG — по нему бэкенд выбирает JSON-
    /// формат вместо старого base64. HWID — постоянный (Keychain), чтобы не занимать новые
    /// места в лимите устройств.
    private func fetchNodes(_ link: String) async -> [VPNNode] {
        guard let url = URL(string: link) else { return [] }
        var request = URLRequest(url: url)
        request.timeoutInterval = 20
        request.setValue("v2rayNG/1.8.29", forHTTPHeaderField: "User-Agent")
        request.setValue(DeviceID.hwid, forHTTPHeaderField: "X-HWID")
        request.setValue("iOS", forHTTPHeaderField: "X-Device-OS")
        request.setValue(UIDevice.current.systemVersion, forHTTPHeaderField: "X-Device-OS-Version")
        // Версия ОС под тем именем, которое читает Remnawave (os_version в списке устройств).
        request.setValue(UIDevice.current.systemVersion, forHTTPHeaderField: "X-Ver-OS")
        request.setValue(DeviceModel.identifier, forHTTPHeaderField: "X-Device-Model")
        request.setValue("Goji app", forHTTPHeaderField: "X-App-Name")
        request.setValue(AppConstants.appVersion, forHTTPHeaderField: "X-App-Version")
        do {
            let config = URLSessionConfiguration.ephemeral
            config.httpCookieStorage = nil
            let (data, response) = try await URLSession(configuration: config).data(for: request)
            if let http = response as? HTTPURLResponse {
                if http.value(forHTTPHeaderField: "x-hwid-max-devices-reached") == "true" {
                    AppLog.w(.subscription, "сервер подписки: превышен лимит устройств")
                } else if http.value(forHTTPHeaderField: "x-hwid-not-supported") == "true" {
                    AppLog.w(.subscription, "сервер подписки: клиент не распознан (нет HWID)")
                }
            }
            let raw = String(decoding: data, as: UTF8.self)
            let parsed = Self.parseJSONProfiles(raw) ?? Self.parseLegacy(raw)
            AppLog.i(.subscription, "серверов в подписке: \(parsed.count)")
            return parsed
        } catch {
            AppLog.e(.subscription, "fetchNodes", error)
            return []
        }
    }

    static func parseJSONProfiles(_ raw: String) -> [VPNNode]? {
        guard let data = raw.trimmingCharacters(in: .whitespacesAndNewlines).data(using: .utf8),
              let array = try? JSONSerialization.jsonObject(with: data) as? [[String: Any]] else { return nil }
        let nodes: [VPNNode] = array.enumerated().compactMap { index, profile in
            guard let endpoint = XrayConfigBuilder.endpoint(of: profile),
                  let profileData = try? JSONSerialization.data(withJSONObject: profile),
                  let payload = String(data: profileData, encoding: .utf8) else { return nil }
            let remarks = (profile["remarks"] as? String).flatMap { $0.isEmpty ? nil : $0 } ?? L("servers.fallback_name", index + 1)
            // ID — remark+host:port, НЕ позиция в массиве: выбор пользователя хранится на диске
            // и не должен «переезжать» на другой сервер при изменении состава подписки.
            return VPNNode(
                id: "\(remarks)|\(endpoint.host):\(endpoint.port)",
                name: remarks,
                host: endpoint.host,
                port: endpoint.port,
                payload: payload,
                geo: CountryGeoLookup.find(remarks),
                uuid: endpoint.uuid
            )
        }
        return nodes.isEmpty ? nil : nodes
    }

    /// Старый формат: base64 со списком vless://-ссылок построчно.
    static func parseLegacy(_ raw: String) -> [VPNNode] {
        let trimmed = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        let decoded = Data(base64Encoded: trimmed, options: .ignoreUnknownCharacters)
            .flatMap { String(data: $0, encoding: .utf8) } ?? trimmed
        return decoded.split(whereSeparator: \.isNewline).enumerated().compactMap { index, line in
            let link = String(line).trimmingCharacters(in: .whitespaces)
            guard link.hasPrefix("vless://"), let components = URLComponents(string: link), let host = components.host else { return nil }
            let port = components.port ?? 443
            let remark = components.fragment?.removingPercentEncoding.flatMap { $0.isEmpty ? nil : $0 } ?? L("servers.fallback_name", index + 1)
            return VPNNode(
                id: "\(remark)|\(host):\(port)",
                name: remark,
                host: host,
                port: port,
                payload: link,
                geo: CountryGeoLookup.find(remark),
                uuid: components.user
            )
        }
    }
}

enum DeviceModel {
    /// «iPhone15,2» — модель для списка устройств в личном кабинете.
    static var identifier: String {
        var info = utsname()
        uname(&info)
        let mirror = Mirror(reflecting: info.machine)
        return mirror.children.reduce(into: "") { result, element in
            if let value = element.value as? Int8, value != 0 { result.append(Character(UnicodeScalar(UInt8(value)))) }
        }
    }
}
