import Foundation
import Network

enum NetState: Equatable {
    case wifi, cellular, jammed
}

/// Состояние сети для «Держит тебя в сети» (порт NetworkMonitor + логики ConnectViewModel):
///  - пропала мобильная сеть (нет удовлетворённого пути) → уходим на российский узел (LTE);
///  - появился Wi-Fi → возвращаемся на узел, выбранный пользователем;
///  - мобильная сеть → «Автовыбор EU», если Европа отвечает, иначе «Автовыбор LTE».
/// Работает, пока приложение открыто (iOS не даёт приложению следить за сетью в фоне).
@MainActor
final class NetworkMonitor: ObservableObject {
    static let shared = NetworkMonitor()

    @Published private(set) var state: NetState = .wifi
    @Published var banner: Banner?

    struct Banner: Equatable {
        enum Kind { case warning, success, info }
        let text: String
        let kind: Kind
    }

    private let monitor = NWPathMonitor()
    private var lastState: NetState?
    private var debounceTask: Task<Void, Never>?

    func start() {
        monitor.pathUpdateHandler = { [weak self] path in
            let next: NetState
            if path.status != .satisfied {
                next = .jammed
            } else if path.usesInterfaceType(.wifi) || path.usesInterfaceType(.wiredEthernet) {
                next = .wifi
            } else {
                next = .cellular
            }
            Task { @MainActor in self?.schedule(next) }
        }
        monitor.start(queue: DispatchQueue(label: "xyz.gojihub.vpn.netmon"))
    }

    /// Дебаунс 1 с: роуминг между точками Wi-Fi/DHCP на мгновение «теряет» сеть — это не
    /// глушение (наступали на это на Android).
    private func schedule(_ next: NetState) {
        debounceTask?.cancel()
        debounceTask = Task {
            try? await Task.sleep(nanoseconds: 1_000_000_000)
            if Task.isCancelled { return }
            apply(next)
        }
    }

    private func apply(_ next: NetState) {
        let previous = lastState
        lastState = next
        state = next
        guard let previous, previous != next, AppSettings.shared.autoSwitchOnJam else { return }
        let store = SubscriptionStore.shared
        let settings = AppSettings.shared

        switch next {
        case .jammed:
            guard let ru = store.nodes.first(where: \.isLTE) else {
                banner = Banner(text: L("banner.jammed_no_ru"), kind: .warning)
                return
            }
            if let current = store.selectedNode, current.id != ru.id { settings.preferredNodeId = current.id }
            store.select(ru.id)
            banner = Banner(text: L("banner.jammed_switched", ru.displayName), kind: .warning)
        case .wifi:
            if let ru = store.nodes.first(where: \.isLTE), store.selectedNode?.id == ru.id,
               let preferred = settings.preferredNodeId, preferred != ru.id,
               store.nodes.contains(where: { $0.id == preferred }) {
                store.select(preferred)
                banner = Banner(text: L("banner.wifi_restored"), kind: .success)
            }
        case .cellular:
            Task { await maybeSwitchOnCellular() }
        }
    }

    private func maybeSwitchOnCellular() async {
        let store = SubscriptionStore.shared
        let nodes = store.nodes
        guard let lte = nodes.first(where: \.isLTE),
              let eu = nodes.first(where: { $0.id != lte.id && $0.geo == nil && $0.name.contains("EU") }) else { return }
        let european = nodes.filter { $0.id != lte.id && $0.geo != nil && $0.geo?.country != "Russia" }
        guard !european.isEmpty else { return }
        await PingService.shared.pingAll()
        let reachable = european.contains { (PingService.shared.pings[$0.id] ?? -1) >= 0 }
        let target = reachable ? eu : lte
        guard store.selectedNode?.id != target.id else { return }
        store.select(target.id)
        banner = Banner(
            text: reachable ? L("banner.cellular_eu", eu.displayName) : L("banner.cellular_lte", lte.displayName),
            kind: .warning
        )
    }
}
