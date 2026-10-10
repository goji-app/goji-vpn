import Combine
import Foundation

/// «Журнал сети» — история работы VPN на этом телефоне, как страница аптайма: когда туннель был
/// поднят, когда и почему переключались узлы, что происходило с сетью. Порт Android
/// journal/NetworkJournal.kt (84b0fb9). Хранится только локально (network-journal.jsonl, по строке
/// JSON на событие), последние 7 дней. Тексты событий не хранятся готовыми — только тип и
/// параметр, заголовки собирает экран на текущем языке.
///
/// В отличие от Android, здесь туннелем управляет расширение, а приложение в фоне засыпает:
/// подключения и отключения записываются по переходам состояния, которые видит приложение (время
/// подключения — из системного connectedDate), без «пульса».
@MainActor
final class NetworkJournal: ObservableObject {
    static let shared = NetworkJournal()

    enum Kind: String, Codable {
        case connected = "CONNECTED", disconnected = "DISCONNECTED"
        case netWifi = "NET_WIFI", netCellular = "NET_CELLULAR", netJammed = "NET_JAMMED"
        case switchJammed = "SWITCH_JAMMED", switchCellularEU = "SWITCH_CELLULAR_EU"
        case switchCellularLTE = "SWITCH_CELLULAR_LTE", switchWifiRestored = "SWITCH_WIFI_RESTORED"
        case error = "ERROR", leakOK = "LEAK_OK", leakFail = "LEAK_FAIL"

        var isIssue: Bool { [.netJammed, .switchJammed, .error, .leakFail].contains(self) }
        var isSwitch: Bool { [.switchJammed, .switchCellularEU, .switchCellularLTE, .switchWifiRestored].contains(self) }
    }

    struct Event: Codable, Identifiable, Equatable {
        let at: Date
        let kind: Kind
        let arg: String
        var id: String { "\(at.timeIntervalSince1970)-\(kind.rawValue)-\(arg)" }

        enum CodingKeys: String, CodingKey { case at = "t", kind = "k", arg = "a" }

        init(at: Date, kind: Kind, arg: String = "") {
            self.at = at
            self.kind = kind
            self.arg = arg
        }

        init(from decoder: Decoder) throws {
            let c = try decoder.container(keyedBy: CodingKeys.self)
            at = Date(timeIntervalSince1970: try c.decode(Double.self, forKey: .at) / 1000)
            kind = try c.decode(Kind.self, forKey: .kind)
            arg = try c.decodeIfPresent(String.self, forKey: .arg) ?? ""
        }

        func encode(to encoder: Encoder) throws {
            var c = encoder.container(keyedBy: CodingKeys.self)
            try c.encode((at.timeIntervalSince1970 * 1000).rounded(), forKey: .at)
            try c.encode(kind, forKey: .kind)
            try c.encode(arg, forKey: .arg)
        }
    }

    /// Час суток: доля времени под защитой 0…1 (nil — час ещё не наступил) и были ли сбои.
    struct Hour: Hashable {
        let uptime: Double?
        let issue: Bool
    }

    struct Day {
        let hours: [Hour]
        let protected: TimeInterval
        let elapsed: TimeInterval
        let connects: Int
        let switches: Int
        let issues: Int
        let events: [Event]
    }

    @Published private(set) var events: [Event] = []

    private let keepDays = 7.0
    private let maxLines = 3000
    private var cancellables = Set<AnyCancellable>()
    private var attached = false

    private var fileURL: URL { SharedStorage.directory("Journal").appendingPathComponent("network-journal.jsonl") }

    private init() {
        if DemoMode.isEnabled {
            events = Self.demoEvents()
            return
        }
        load()
    }

    private func load() {
        let cutoff = Date().addingTimeInterval(-keepDays * 86_400)
        guard let text = try? String(contentsOf: fileURL, encoding: .utf8) else { return }
        let decoder = JSONDecoder()
        let all = text.split(separator: "\n").compactMap { try? decoder.decode(Event.self, from: Data($0.utf8)) }
        let kept = Array(all.filter { $0.at >= cutoff }.sorted { $0.at < $1.at }.suffix(maxLines))
        events = kept
        if kept.count != all.count { rewrite() }
    }

    private func rewrite() {
        let encoder = JSONEncoder()
        let text = events.compactMap { try? encoder.encode($0) }.compactMap { String(data: $0, encoding: .utf8) }.joined(separator: "\n")
        try? (text + (text.isEmpty ? "" : "\n")).write(to: fileURL, atomically: true, encoding: .utf8)
    }

    func log(_ kind: Kind, _ arg: String = "", at date: Date = Date()) {
        guard !DemoMode.isEnabled else { return }
        let event = Event(at: date, kind: kind, arg: String(arg.prefix(160)))
        events = Array((events + [event]).sorted { $0.at < $1.at }.suffix(maxLines))
        if let data = try? JSONEncoder().encode(event), let line = String(data: data, encoding: .utf8) {
            if let handle = try? FileHandle(forWritingTo: fileURL) {
                handle.seekToEndOfFile()
                handle.write(Data((line + "\n").utf8))
                try? handle.close()
            } else {
                try? (line + "\n").write(to: fileURL, atomically: true, encoding: .utf8)
            }
        }
    }

    /// Подписка на состояние VPN, сети и ошибки — вызывается один раз при запуске.
    func attach(vpn: VPNController, network: NetworkMonitor, store: SubscriptionStore) {
        guard !attached, !DemoMode.isEnabled else { return }
        attached = true

        // Сразу после запуска фаза ещё не известна (до загрузки конфигурации VPN она «отключено») —
        // ждём, пока состояние устоится, и сверяем его с последней записью журнала.
        vpn.$phase
            .removeDuplicates()
            .debounce(for: .seconds(1.5), scheduler: RunLoop.main)
            .sink { [weak self] phase in
                Task { @MainActor in self?.handle(phase: phase, vpn: vpn, store: store) }
            }
            .store(in: &cancellables)

        network.$state
            .removeDuplicates()
            .dropFirst()
            .debounce(for: .seconds(2), scheduler: RunLoop.main)
            .sink { [weak self] state in
                Task { @MainActor in
                    switch state {
                    case .wifi: self?.log(.netWifi)
                    case .cellular: self?.log(.netCellular)
                    case .jammed: self?.log(.netJammed)
                    }
                }
            }
            .store(in: &cancellables)

        vpn.$lastError
            .removeDuplicates()
            .compactMap { $0 }
            .sink { [weak self] error in Task { @MainActor in self?.log(.error, error) } }
            .store(in: &cancellables)
    }

    private func handle(phase: VPNController.Phase, vpn: VPNController, store: SubscriptionStore) {
        let lastSession = events.last { $0.kind == .connected || $0.kind == .disconnected }
        let wasConnected = lastSession?.kind == .connected
        switch phase {
        case .connected where !wasConnected:
            log(.connected, store.selectedNode?.displayName ?? "", at: vpn.connectedSince ?? Date())
        case .disconnected where wasConnected:
            log(.disconnected)
        default:
            break
        }
    }

    nonisolated static func dayStart(daysAgo: Int, now: Date = Date()) -> Date {
        let calendar = Calendar.current
        let today = calendar.startOfDay(for: now)
        return calendar.date(byAdding: .day, value: -daysAgo, to: today) ?? today
    }

    /// Сводка за сутки: аптайм по часам, счётчики и события дня. runningNow — открыт ли интервал
    /// подключения прямо сейчас (тогда он тянется до текущего момента).
    func day(daysAgo: Int, runningNow: Bool, now: Date = Date()) -> Day {
        let all = events
        let start = Self.dayStart(daysAgo: daysAgo, now: now)
        let end = Self.dayStart(daysAgo: daysAgo - 1, now: now)
        let limit = min(end, now)

        var intervals: [(Date, Date)] = []
        var openAt: Date?
        for e in all {
            switch e.kind {
            case .connected: if openAt == nil { openAt = e.at }
            case .disconnected:
                if let a = openAt { intervals.append((a, e.at)); openAt = nil }
            default: break
            }
        }
        if let a = openAt, runningNow { intervals.append((a, now)) }
        let clipped: [(Date, Date)] = intervals.compactMap { a, b in
            let s = max(a, start), t = min(b, limit)
            return t > s ? (s, t) : nil
        }

        let dayEvents = all.filter { $0.at >= start && $0.at < end }
        let hours: [Hour] = (0..<24).map { h in
            let hs = start.addingTimeInterval(Double(h) * 3600)
            let he = hs.addingTimeInterval(3600)
            if hs >= limit { return Hour(uptime: nil, issue: false) }
            let span = max(min(he, limit).timeIntervalSince(hs), 1)
            let covered = clipped.reduce(0.0) { sum, i in sum + max(0, min(i.1, he).timeIntervalSince(max(i.0, hs))) }
            let issue = dayEvents.contains { $0.at >= hs && $0.at < he && $0.kind.isIssue }
            return Hour(uptime: min(max(covered / span, 0), 1), issue: issue)
        }
        // Журнал мог начаться посреди суток — процент считаем от первой записи, а не от полуночи.
        let first = all.first.map { max($0.at, start) } ?? start
        return Day(
            hours: hours,
            protected: clipped.reduce(0) { $0 + $1.1.timeIntervalSince($1.0) },
            elapsed: max(limit.timeIntervalSince(first), 0),
            connects: dayEvents.filter { $0.kind == .connected }.count,
            switches: dayEvents.filter { $0.kind.isSwitch }.count,
            issues: dayEvents.filter { $0.kind.isIssue }.count,
            events: dayEvents.sorted { $0.at > $1.at }
        )
    }

    /// Заголовок и текст события на языке интерфейса (как Loc.f.journalEvent в Android).
    static func describe(_ e: Event) -> (title: String, text: String) {
        switch e.kind {
        case .connected: return (L("journal.ev.connected"), e.arg.isEmpty ? L("journal.ev.connected.tunnel") : L("journal.ev.connected.node", e.arg))
        case .disconnected: return (L("journal.ev.disconnected"), L("journal.ev.disconnected.text"))
        case .netWifi: return (L("journal.ev.wifi"), L("journal.ev.wifi.text"))
        case .netCellular: return (L("journal.ev.cellular"), L("journal.ev.cellular.text"))
        case .netJammed: return (L("journal.ev.jammed"), L("journal.ev.jammed.text"))
        case .switchJammed: return (L("journal.ev.switch_jammed"), L("journal.ev.switched_to", e.arg))
        case .switchCellularEU: return (L("journal.ev.switch_eu"), L("journal.ev.selected", e.arg))
        case .switchCellularLTE: return (L("journal.ev.switch_lte"), L("journal.ev.switched_to", e.arg))
        case .switchWifiRestored: return (L("journal.ev.wifi_restored"), L("journal.ev.restored", e.arg))
        case .error: return (L("journal.ev.error"), e.arg)
        case .leakOK: return (L("journal.ev.leak_ok"), L("journal.ev.leak_ok.text"))
        case .leakFail: return (L("journal.ev.leak_fail"), L("journal.ev.leak_fail.text"))
        }
    }

    /// Демо-режим (снимки экрана): правдоподобный день.
    private static func demoEvents() -> [Event] {
        let day = Calendar.current.startOfDay(for: Date())
        func at(_ h: Int, _ m: Int) -> Date { day.addingTimeInterval(Double(h * 3600 + m * 60)) }
        let ru = AppLanguage.isRussian
        let list: [Event] = [
            Event(at: at(0, 5), kind: .connected, arg: ru ? "Германия" : "Germany"),
            Event(at: at(2, 40), kind: .netJammed),
            Event(at: at(2, 41), kind: .switchJammed, arg: ru ? "Автовыбор серверов LTE" : "Auto-select LTE"),
            Event(at: at(7, 55), kind: .netWifi),
            Event(at: at(7, 56), kind: .switchWifiRestored, arg: ru ? "Германия" : "Germany"),
            Event(at: at(9, 30), kind: .leakOK),
            Event(at: at(12, 10), kind: .disconnected),
            Event(at: at(12, 40), kind: .connected, arg: ru ? "Нидерланды" : "Netherlands")
        ]
        return list.filter { $0.at <= Date() }
    }
}
