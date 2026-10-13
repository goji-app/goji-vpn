import Foundation
import Network

/// Замер задержки до серверов подписки (порт PingRepository.kt).
///  - через прокси (GET/HEAD): libXray pingBatch — реальный HTTP-запрос сквозь VLESS/Reality,
///    учитывает задержку самого протокола, а не только сетевой RTT;
///  - TCP: время установления TCP-соединения с host:port узла.
/// Пока VPN включён, запросы приложения сами идут через туннель, поэтому цифры включают
/// задержку текущего узла — это ожидаемо и одинаково для всех серверов списка.
@MainActor
final class PingService: ObservableObject {
    static let shared = PingService()

    /// id узла → мс; -1 — недоступен.
    @Published private(set) var pings: [String: Int] = [:]
    @Published private(set) var checkingAll = false
    @Published private(set) var checkingId: String?

    private let timeoutSeconds = 4
    private let batchSize = 5
    private let worker = DispatchQueue(label: "xyz.gojihub.vpn.ping", qos: .userInitiated)

    func installDemo(_ values: [String: Int]) {
        pings = values
    }

    func pingAll() async {
        guard !DemoMode.isEnabled, !checkingAll else { return }
        checkingAll = true
        defer { checkingAll = false }
        let nodes = SubscriptionStore.shared.nodes
        let method = AppSettings.shared.pingMethod
        let url = AppSettings.shared.pingURL
        var results = pings

        if method == .tcp {
            await withTaskGroup(of: (String, Int).self) { group in
                for node in nodes {
                    group.addTask { (node.id, await Self.tcpPing(host: node.host, port: node.port)) }
                }
                for await (id, ms) in group { results[id] = ms }
            }
            pings = results
            return
        }

        let jsonNodes = nodes.filter(\.isJSONProfile)
        let legacyNodes = nodes.filter { !$0.isJSONProfile }
        // Узлы-балансировщики («Автовыбор LTE/EU») — несколько резервных аутбаундов. Сначала
        // проверяем только первый у всех, резервные — вторым проходом и только там, где
        // первый не ответил (иначе «проверить все» упиралось бы в таймауты).
        let tagsByNode = Dictionary(uniqueKeysWithValues: jsonNodes.map { ($0.id, XrayConfigBuilder.proxyOutboundTags(of: $0.payload)) })
        var best: [String: Int] = [:]

        let firstPass = jsonNodes.compactMap { node in tagsByNode[node.id]?.first.map { (node, $0) } }
        for (id, ms) in await measureViaProxy(firstPass, url: url, head: method == .proxyHead) {
            best[id] = Self.merge(best[id], ms)
        }
        let secondPass = jsonNodes
            .filter { (best[$0.id] ?? -1) < 0 }
            .flatMap { node in (tagsByNode[node.id] ?? []).dropFirst().map { (node, $0) } }
        for (id, ms) in await measureViaProxy(secondPass, url: url, head: method == .proxyHead) {
            best[id] = Self.merge(best[id], ms)
        }
        for node in jsonNodes { results[node.id] = best[node.id] ?? -1 }
        for node in legacyNodes { results[node.id] = await Self.tcpPing(host: node.host, port: node.port) }
        pings = results
    }

    func pingOne(_ node: VPNNode) async {
        checkingId = node.id
        defer { checkingId = nil }
        if AppSettings.shared.pingMethod == .tcp || !node.isJSONProfile {
            pings[node.id] = await Self.tcpPing(host: node.host, port: node.port)
            return
        }
        let tags = XrayConfigBuilder.proxyOutboundTags(of: node.payload)
        var result = -1
        for (_, ms) in await measureViaProxy(tags.map { (node, $0) }, url: AppSettings.shared.pingURL, head: AppSettings.shared.pingMethod == .proxyHead) {
            result = Self.merge(result, ms)
        }
        pings[node.id] = result
    }

    private func measureViaProxy(_ targets: [(VPNNode, String)], url: String, head: Bool) async -> [(String, Int)] {
        guard !targets.isEmpty else { return [] }
        let chunks = stride(from: 0, to: targets.count, by: batchSize).map { Array(targets[$0..<min($0 + batchSize, targets.count)]) }
        let timeout = timeoutSeconds
        return await withCheckedContinuation { continuation in
            worker.async {
                var out: [(String, Int)] = []
                for chunk in chunks {
                    let results = XrayCore.pingBatch(
                        chunk.map { XrayCore.PingTarget(xrayJSON: $0.0.payload, outboundTag: $0.1) },
                        url: url,
                        timeoutSeconds: timeout,
                        headMethod: head
                    )
                    for (index, item) in chunk.enumerated() where index < results.count {
                        out.append((item.0.id, results[index].delay))
                    }
                }
                continuation.resume(returning: out)
            }
        }
    }

    /// Успешный замер всегда лучше неудачи; из двух успешных — меньший.
    static func merge(_ current: Int?, _ candidate: Int) -> Int {
        guard let current else { return candidate }
        if current < 0 { return candidate }
        if candidate < 0 { return current }
        return min(current, candidate)
    }

    nonisolated static func tcpPing(host: String, port: Int, timeout: TimeInterval = 2) async -> Int {
        guard let nwPort = NWEndpoint.Port(rawValue: UInt16(clamping: port)) else { return -1 }
        let connection = NWConnection(host: NWEndpoint.Host(host), port: nwPort, using: .tcp)
        let start = Date()
        return await withCheckedContinuation { continuation in
            let queue = DispatchQueue(label: "xyz.gojihub.vpn.tcpping")
            let once = ResumeOnce(continuation: continuation, connection: connection)
            connection.stateUpdateHandler = { state in
                switch state {
                case .ready: once.finish(Int(Date().timeIntervalSince(start) * 1000))
                case .failed, .cancelled, .waiting: once.finish(-1)
                default: break
                }
            }
            connection.start(queue: queue)
            queue.asyncAfter(deadline: .now() + timeout) { once.finish(-1) }
        }
    }
}

/// Продолжение возобновляется ровно один раз (готовность, ошибка или таймаут — что раньше).
private final class ResumeOnce: @unchecked Sendable {
    private let lock = NSLock()
    private var continuation: CheckedContinuation<Int, Never>?
    private let connection: NWConnection

    init(continuation: CheckedContinuation<Int, Never>, connection: NWConnection) {
        self.continuation = continuation
        self.connection = connection
    }

    func finish(_ value: Int) {
        lock.lock()
        let pending = continuation
        continuation = nil
        lock.unlock()
        guard let pending else { return }
        connection.cancel()
        pending.resume(returning: value)
    }
}
