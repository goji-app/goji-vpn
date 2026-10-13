import SwiftUI

/// «Главная» «Goji Expressive» (Material 3 Expressive) — по ConnectScreen.kt Android 1.0.117:
/// шапка (Goji и сеть), глобус, кнопка-«печенье» с заголовком и строкой статуса, плитка скорости с
/// мини-графиком, плитки «Качество канала» и «Подписка», карточка узла со строкой журнала сети.
struct ConnectView: View {
    @EnvironmentObject private var model: AppModel
    @EnvironmentObject private var vpn: VPNController
    @EnvironmentObject private var store: SubscriptionStore
    @EnvironmentObject private var pings: PingService
    @EnvironmentObject private var network: NetworkMonitor
    @StateObject private var quality = QualityProbe()
    @ObservedObject private var journal = NetworkJournal.shared

    var body: some View {
        ScrollView {
            VStack(spacing: 8) {
                header
                GlobeView(status: globeStatus, node: displayGeo)
                    .frame(height: 270)
                    .frame(maxWidth: .infinity)
                VStack(spacing: 8) {
                    CookieButton(phase: vpn.phase) { Task { await vpn.toggle() } }
                        .disabled(store.nodes.isEmpty && vpn.phase == .disconnected)
                        .accessibilityLabel(vpn.phase == .disconnected ? L("connect.cta.on") : L("connect.cta.off"))
                        .accessibilityHint(vpn.phase == .disconnected ? L("connect.a11y.connect") : L("connect.a11y.disconnect"))
                    headline
                }
                if let banner = currentBanner {
                    BannerView(text: banner.text, kind: banner.kind) { dismissBanner() }
                }
                speedCard
                HStack(alignment: .top, spacing: 8) {
                    qualityTile
                    subscriptionTile
                }
                .fixedSize(horizontal: false, vertical: true)
                if store.subscription == nil && store.subscriptionLoaded { NoSubscriptionCard() }
                nodeAndJournalCard
            }
            .padding(.horizontal, GojiMetrics.screenPadding)
            .padding(.top, 6)
            .padding(.bottom, GojiMetrics.tabBarHeight + 16)
        }
        .scrollIndicators(.hidden)
        .refreshable { await store.refresh() }
        .toolbar(.hidden, for: .navigationBar)
        .onAppear { quality.setActive(vpn.phase == .connected) }
        .onDisappear { quality.setActive(false) }
        .onChange(of: vpn.phase) { phase in quality.setActive(phase == .connected) }
    }

    // MARK: - Состояние

    private var globeStatus: GlobeView.Status {
        switch vpn.phase {
        case .connected: return .on
        case .connecting, .disconnecting: return .connecting
        case .disconnected: return .off
        }
    }

    /// Страна для глобуса/заголовка: реальная по IP (если известна), иначе по названию узла.
    private var displayGeo: CountryGeo? {
        if let actual = vpn.actualCountry, let geo = CountryGeoLookup.find(actual) { return geo }
        return store.selectedNode?.geo
    }

    private var currentBanner: (text: String, kind: NetworkMonitor.Banner.Kind)? {
        if let error = vpn.lastError { return (error, .warning) }
        if let banner = network.banner { return (banner.text, banner.kind) }
        if let actual = vpn.actualCountry, let node = store.selectedNode, let nodeGeo = node.geo,
           let actualGeo = CountryGeoLookup.find(actual), actualGeo.country != nodeGeo.country {
            if node.isLTE { return (L("banner.lte_only_jam"), .info) }
            return (L("banner.actual_country", actualGeo.displayName, node.displayName), .info)
        }
        return nil
    }

    private func dismissBanner() {
        if vpn.lastError != nil { vpn.lastError = nil } else { network.banner = nil }
    }

    // MARK: - Блоки

    /// Шапка: плашка Goji и плашка сети (глушение — на тёплой подложке).
    private var header: some View {
        let (text, color, tint): (String, Color, Color) = {
            switch network.state {
            case .wifi: return (L("net.wifi"), GojiColor.tealDeep, GojiColor.chip)
            case .cellular: return (L("net.cellular"), GojiColor.textSecondary, GojiColor.chip)
            case .jammed: return (L("net.jammed"), GojiColor.jamText, GojiColor.jamBg)
            }
        }()
        return HStack {
            HStack(spacing: 8) {
                Image("Mascot")
                    .resizable()
                    .scaledToFit()
                    .padding(4)
                    .frame(width: 28, height: 28)
                    .background(Circle().fill(GojiColor.textPrimary))
                Text("Goji")
                    .font(GojiFont.manrope(14, .bold))
                    .foregroundStyle(GojiColor.textPrimary)
            }
            .padding(.leading, 4)
            .padding(.trailing, 12)
            .padding(.vertical, 4)
            .background(Capsule().fill(GojiColor.chip))
            .accessibilityHidden(true)
            Spacer()
            HStack(spacing: 7) {
                Circle().fill(color).frame(width: 8, height: 8)
                Text(text).font(GojiFont.manrope(12, .bold, relativeTo: .caption))
            }
            .foregroundStyle(color)
            .padding(.horizontal, 13)
            .frame(height: 34)
            .background(Capsule().fill(tint))
            .accessibilityElement(children: .combine)
        }
    }

    private var headline: some View {
        let node = store.selectedNode
        let title: String
        let subtitle: String
        let dot: Color
        switch vpn.phase {
        case .connected:
            let place = displayGeo.map { AppLanguage.isRussian ? $0.ruPrep : $0.displayName } ?? L("connect.fallback_place")
            title = L("connect.headline.on", place)
            subtitle = L("connect.subline.on", node?.displayName ?? "")
            dot = GojiColor.teal
        case .connecting, .disconnecting:
            title = L("connect.headline.connecting")
            subtitle = L("connect.subline.connecting", node?.displayName ?? "")
            dot = GojiColor.warning
        case .disconnected:
            title = L("connect.headline.off")
            subtitle = L("connect.subline.off")
            dot = GojiColor.outline
        }
        return VStack(spacing: 2) {
            Text(title)
                .font(GojiFont.manrope(22, .bold, relativeTo: .title2))
                .foregroundStyle(GojiColor.textPrimary)
                .multilineTextAlignment(.center)
                .lineLimit(2)
                .contentTransition(.opacity)
            HStack(spacing: 7) {
                Circle().fill(dot).frame(width: 7, height: 7)
                Text(subtitle)
                    .font(GojiFont.manrope(12, .semibold, relativeTo: .caption))
                    .foregroundStyle(GojiColor.textSecondary)
                    .lineLimit(1)
                if vpn.phase == .connected, let since = vpn.connectedSince {
                    TimelineView(.periodic(from: .now, by: 1)) { context in
                        Text("· " + Self.elapsed(from: since, to: context.date))
                            .font(GojiFont.manrope(13, .bold).monospacedDigit())
                            .foregroundStyle(GojiColor.textPrimary)
                    }
                }
            }
        }
        .frame(maxWidth: .infinity)
        .accessibilityElement(children: .combine)
    }

    /// «Скорость сейчас»: ↓ крупно, ↑ мелко и мини-график последних ~30 с.
    private var speedCard: some View {
        GlassCard(padding: 16) {
            HStack(spacing: 12) {
                VStack(alignment: .leading, spacing: 3) {
                    Text(L("connect.speed_now"))
                        .font(GojiFont.manrope(12, .semibold, relativeTo: .caption))
                        .foregroundStyle(GojiColor.textSecondary)
                    HStack(spacing: 4) {
                        Image(systemName: "arrow.down").font(.system(size: 14, weight: .bold)).foregroundStyle(GojiColor.teal)
                        Text(String(format: "%.1f", vpn.downBytesPerSec / 1_000_000))
                            .font(GojiFont.manrope(22, .bold, relativeTo: .title2).monospacedDigit())
                            .foregroundStyle(GojiColor.textPrimary)
                        Image(systemName: "arrow.up").font(.system(size: 11, weight: .bold)).foregroundStyle(GojiColor.terracotta)
                            .padding(.leading, 6)
                        Text(L("speed.mb", String(format: "%.1f", vpn.upBytesPerSec / 1_000_000)))
                            .font(GojiFont.manrope(13, .semibold).monospacedDigit())
                            .foregroundStyle(GojiColor.textSecondary)
                            .lineLimit(1)
                    }
                }
                Spacer(minLength: 0)
                SpeedSparkline(down: vpn.phase == .connected ? vpn.downHistory : [], up: vpn.phase == .connected ? vpn.upHistory : [])
                    .frame(width: 108, height: 40)
            }
        }
        .accessibilityElement(children: .combine)
    }

    /// «Качество канала»: оценка 0…100 по медиане задержки и потерям за последнюю минуту.
    private var qualityTile: some View {
        let (label, color): (String, Color) = {
            guard let score = quality.score else { return ("", GojiColor.textSecondary) }
            if score >= 85 { return (L("quality.excellent"), GojiColor.teal) }
            if score >= 65 { return (L("quality.good"), GojiColor.teal) }
            if score >= 45 { return (L("quality.fair"), GojiColor.warning) }
            return (L("quality.poor"), GojiColor.danger)
        }()
        let detail: String = {
            if let ping = quality.pingMs {
                return quality.lossPct == 0 ? L("quality.ping", ping) : L("quality.ping_loss", ping, quality.lossPct)
            }
            if quality.score != nil { return L("quality.noreply") }
            return vpn.phase == .connected ? L("quality.measuring") : L("quality.off")
        }()
        return GlassCard(padding: 14) {
            VStack(alignment: .leading, spacing: 3) {
                Text(L("quality.title"))
                    .font(GojiFont.manrope(12, .semibold, relativeTo: .caption))
                    .foregroundStyle(GojiColor.textSecondary)
                    .lineLimit(1)
                HStack(alignment: .firstTextBaseline, spacing: 6) {
                    Text(quality.score.map { String($0) } ?? "—")
                        .font(GojiFont.manrope(26, .extraBold, relativeTo: .title).monospacedDigit())
                        .foregroundStyle(color)
                    if !label.isEmpty {
                        Text(label)
                            .font(GojiFont.manrope(12.5, .bold, relativeTo: .caption))
                            .foregroundStyle(GojiColor.textPrimary)
                    }
                }
                Text(detail)
                    .font(GojiFont.manrope(11, .medium, relativeTo: .caption2))
                    .foregroundStyle(GojiColor.textSecondary)
                    .fixedSize(horizontal: false, vertical: true)
                Spacer(minLength: 0)
            }
        }
        .accessibilityElement(children: .combine)
    }

    /// «Подписка»: остаток дней крупно, 5 полосок (меньше недели — тёплые, меньше трёх дней —
    /// красные), дата окончания и трафик.
    private var subscriptionTile: some View {
        let sub = store.subscription
        let days = sub?.daysLeft ?? 0
        let barColor = days < 3 ? GojiColor.danger : (days < 7 ? GojiColor.terracotta : GojiColor.teal)
        let filled = Self.subscriptionBars(days)
        let until = sub?.expireDate.map { L("subtile.until", DateParsing.shortDate($0)) } ?? L("subtile.until", "—")
        return Button {
            model.tab = .subscription
        } label: {
            GlassCard(padding: 14) {
                VStack(alignment: .leading, spacing: 4) {
                    Text(L("subtile.title"))
                        .font(GojiFont.manrope(12, .semibold, relativeTo: .caption))
                        .foregroundStyle(GojiColor.textSecondary)
                    HStack(spacing: 8) {
                        Text(Plural.form(days, "days"))
                            .font(GojiFont.manrope(20, .extraBold, relativeTo: .title3))
                            .foregroundStyle(GojiColor.textPrimary)
                            .lineLimit(1)
                            .minimumScaleFactor(0.8)
                        Spacer(minLength: 0)
                        HStack(alignment: .bottom, spacing: 3) {
                            ForEach(0..<5, id: \.self) { i in
                                Capsule()
                                    .fill(i < filled ? barColor : GojiColor.track)
                                    .frame(width: 5, height: CGFloat(8 + i * 2))
                            }
                        }
                    }
                    Text(sub.map { until + " · " + trafficText($0) } ?? until)
                        .font(GojiFont.manrope(11, .medium, relativeTo: .caption2))
                        .foregroundStyle(GojiColor.textSecondary)
                        .fixedSize(horizontal: false, vertical: true)
                    Spacer(minLength: 0)
                }
            }
        }
        .buttonStyle(.plain)
        .accessibilityElement(children: .combine)
    }

    /// Узел + «Журнал сети» — одна карточка: строка узла открывает «Серверы», строка журнала —
    /// журнал сети.
    private var nodeAndJournalCard: some View {
        let todayStart = NetworkJournal.dayStart(daysAgo: 0)
        let switches = journal.events.filter { $0.at >= todayStart && $0.kind.isSwitch }.count
        let last = journal.events.last.map { Self.time.string(from: $0.at) }
        let line = (switches == 0 ? L("journal.home.none") : L("journal.home.count", switches)) + (last.map { L("journal.home.last", $0) } ?? "")
        return VStack(spacing: 0) {
            Button {
                model.tab = .servers
            } label: {
                HStack(spacing: 12) {
                    Text(store.selectedNode?.flag ?? "🌐")
                        .font(.system(size: 19))
                        .frame(width: 38, height: 38)
                        .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(GojiColor.chip))
                    VStack(alignment: .leading, spacing: 2) {
                        Text(store.selectedNode?.displayName ?? L("connect.node.none"))
                            .font(GojiFont.manrope(14, .bold))
                            .foregroundStyle(GojiColor.textPrimary)
                            .lineLimit(1)
                        if let geo = store.selectedNode?.geo {
                            Text(geo.displayName)
                                .font(GojiFont.manrope(11.5, .medium, relativeTo: .caption))
                                .foregroundStyle(GojiColor.textSecondary)
                        }
                    }
                    Spacer()
                    if let id = store.selectedNode?.id, let ms = pings.pings[id] { PingLabel(ms: ms) }
                    Image(systemName: "chevron.right")
                        .font(.system(size: 13, weight: .semibold))
                        .foregroundStyle(GojiColor.textSecondary)
                }
                .padding(.horizontal, 14)
                .padding(.vertical, 10)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityHint(L("connect.node.a11y"))
            Rectangle().fill(GojiColor.hair).frame(height: 2)
            NavigationLink {
                JournalView()
            } label: {
                VStack(alignment: .leading, spacing: 3) {
                    HStack(spacing: 8) {
                        Image(systemName: "checkmark.shield.fill")
                            .font(.system(size: 13))
                            .foregroundStyle(GojiColor.tealDeep)
                        Text(L("journal.title"))
                            .font(GojiFont.manrope(14, .bold))
                            .foregroundStyle(GojiColor.textPrimary)
                    }
                    HStack(spacing: 6) {
                        Text(line)
                            .font(GojiFont.manrope(11.5, .semibold, relativeTo: .caption))
                            .foregroundStyle(GojiColor.tealDeep)
                            .lineLimit(1)
                        Spacer(minLength: 0)
                        Image(systemName: "chevron.right")
                            .font(.system(size: 13, weight: .semibold))
                            .foregroundStyle(GojiColor.tealDeep)
                    }
                }
                .padding(.horizontal, 14)
                .padding(.top, 10)
                .padding(.bottom, 11)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
        }
        .background(GlassSurface(radius: 26))
        .clipShape(RoundedRectangle(cornerRadius: 26, style: .continuous))
    }

    private func trafficText(_ sub: SubscriptionInfo) -> String {
        let used = Double(sub.traffic?.usedBytes ?? 0) / 1_000_000_000
        if sub.traffic?.isUnlimited ?? true {
            return L("connect.traffic.unlimited", String(format: "%.1f", used))
        }
        let quota = Double(sub.traffic?.limitBytes ?? 0) / 1_000_000_000
        return L("connect.traffic.limited", String(format: "%.1f", used), String(format: "%.0f", quota))
    }

    /// Сколько из 5 «полосок» остатка подписки заполнено (subscriptionBars в Android).
    static func subscriptionBars(_ days: Int) -> Int {
        switch days {
        case 90...: return 5
        case 30...: return 4
        case 14...: return 3
        case 7...: return 2
        case 1...: return 1
        default: return 0
        }
    }

    private static let time: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "HH:mm"
        return f
    }()

    static func elapsed(from start: Date, to now: Date) -> String {
        let total = max(Int(now.timeIntervalSince(start)), 0)
        return String(format: "%02d:%02d:%02d", total / 3600, (total % 3600) / 60, total % 60)
    }
}

// MARK: - Кнопка подключения

/// «Печенье» Material 3 Expressive (MaterialShapes.Cookie в Android ExpressiveShapes.kt): круг с
/// lobes мягкими волнами по краю; depth — глубина волны в долях радиуса (0 — ровный круг), её
/// анимация плавно превращает печенье в круг и обратно.
struct CookieShape: Shape {
    var lobes: Int = 8
    var depth: CGFloat

    var animatableData: CGFloat {
        get { depth }
        set { depth = newValue }
    }

    func path(in rect: CGRect) -> Path {
        let cx = rect.midX, cy = rect.midY
        let r = min(rect.width, rect.height) / 2 / (1 + max(0, depth))
        var path = Path()
        let steps = 144
        for i in 0..<steps {
            let t = Double(i) / Double(steps) * 2 * .pi
            let rr = r * (1 + depth * CGFloat(cos(Double(lobes) * t)))
            let point = CGPoint(x: cx + rr * CGFloat(cos(t)), y: cy + rr * CGFloat(sin(t)))
            if i == 0 { path.move(to: point) } else { path.addLine(to: point) }
        }
        path.closeSubpath()
        return path
    }
}

/// Кнопка подключения 64×64 (порт Android ConnectButton): выключено — primaryContainer;
/// подключение — печенье вращается; включено — заливка primary, волны почти сглажены в круг.
/// Нажатие «вдавливает» волны — форма морфится пружиной.
struct CookieButton: View {
    let phase: VPNController.Phase
    let action: () -> Void
    @State private var spin = false

    var body: some View {
        Button(action: action) { EmptyView() }
            .buttonStyle(CookieStyle(phase: phase, spin: spin))
            .onAppear { spin = phase == .connecting || phase == .disconnecting }
            .onChange(of: phase) { p in spin = p == .connecting || p == .disconnecting }
    }

    private struct CookieStyle: ButtonStyle {
        let phase: VPNController.Phase
        let spin: Bool

        func makeBody(configuration: Configuration) -> some View {
            let on = phase == .connected
            let depth: CGFloat = configuration.isPressed ? 0.09 : (on ? 0.025 : 0.055)
            ZStack {
                CookieShape(depth: depth)
                    .fill(on ? GojiColor.primary : GojiColor.primaryContainer)
                    .rotationEffect(.degrees(spin ? 360 : 0))
                    .animation(spin ? .linear(duration: 1.8).repeatForever(autoreverses: false) : .default, value: spin)
                Image(systemName: "power")
                    .font(.system(size: 24, weight: .semibold))
                    .foregroundStyle(on ? GojiColor.onPrimary : GojiColor.onPrimaryContainer)
            }
            .frame(width: 64, height: 64)
            .scaleEffect(configuration.isPressed ? 0.94 : 1)
            .animation(.spring(response: 0.4, dampingFraction: 0.45), value: depth)
            .animation(.easeOut(duration: 0.15), value: configuration.isPressed)
            .contentShape(Circle())
        }
    }
}

// MARK: - Мини-график скорости

/// Сглаженная линия скорости (порт Android SpeedSparkline): скачивание — акцентом с лёгкой
/// заливкой, отдача — тонкой тёплой линией. Без данных — пунктирная базовая линия.
struct SpeedSparkline: View {
    let down: [Double]
    let up: [Double]

    var body: some View {
        Canvas { ctx, size in
            let stroke: CGFloat = 2.5
            let top = stroke, bottom = size.height - stroke
            guard down.count >= 2 else {
                var line = Path()
                line.move(to: CGPoint(x: 0, y: bottom))
                line.addLine(to: CGPoint(x: size.width, y: bottom))
                ctx.stroke(line, with: .color(GojiColor.outlineVariant), style: StrokeStyle(lineWidth: 2, lineCap: .round, dash: [3, 4]))
                return
            }
            let maxValue = max(down.max() ?? 0, up.max() ?? 0, 50_000)
            func smooth(_ values: [Double]) -> Path {
                let step = size.width / CGFloat(values.count - 1)
                let pts = values.enumerated().map { i, v in
                    CGPoint(x: CGFloat(i) * step, y: bottom - CGFloat(v / maxValue) * (bottom - top))
                }
                var path = Path()
                path.move(to: pts[0])
                for i in 1..<pts.count {
                    let mid = CGPoint(x: (pts[i - 1].x + pts[i].x) / 2, y: (pts[i - 1].y + pts[i].y) / 2)
                    path.addQuadCurve(to: mid, control: pts[i - 1])
                }
                path.addLine(to: pts[pts.count - 1])
                return path
            }
            if up.count >= 2 {
                ctx.stroke(smooth(up), with: .color(GojiColor.terracotta.opacity(0.7)), style: StrokeStyle(lineWidth: 1.5, lineCap: .round))
            }
            let line = smooth(down)
            var fill = line
            fill.addLine(to: CGPoint(x: size.width, y: size.height))
            fill.addLine(to: CGPoint(x: 0, y: size.height))
            fill.closeSubpath()
            ctx.fill(fill, with: .linearGradient(
                Gradient(colors: [GojiColor.primary.opacity(0.22), GojiColor.primary.opacity(0)]),
                startPoint: .zero, endPoint: CGPoint(x: 0, y: size.height)))
            ctx.stroke(line, with: .color(GojiColor.primary), style: StrokeStyle(lineWidth: stroke, lineCap: .round, lineJoin: .round))
        }
        .accessibilityHidden(true)
    }
}

// MARK: - Качество канала

/// Замер раз в 10 с, пока VPN подключён и «Главная» на экране: GET адреса проверки пинга через
/// туннель (пока VPN включён, это и есть маршрут системы). Первый удачный замер — с
/// TLS-рукопожатием, он не показателен: пропускаем его. Оценка — по медиане задержки и потерям за
/// последние 6 замеров (qualityScore в Android).
@MainActor
final class QualityProbe: ObservableObject {
    @Published private(set) var score: Int?
    @Published private(set) var pingMs: Int?
    @Published private(set) var lossPct = 0

    private var task: Task<Void, Never>?
    private var samples: [Int] = []
    private let session: URLSession = {
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 5
        config.httpMaximumConnectionsPerHost = 1
        return URLSession(configuration: config)
    }()

    func setActive(_ active: Bool) {
        if DemoMode.isEnabled {
            if active && score == nil { score = 92; pingMs = 48; lossPct = 0 }
            return
        }
        if active {
            guard task == nil else { return }
            task = Task { [weak self] in await self?.loop() }
        } else {
            task?.cancel()
            task = nil
            samples = []
            score = nil
            pingMs = nil
            lossPct = 0
        }
    }

    private func loop() async {
        var warmedUp = false
        while !Task.isCancelled {
            let ms = await probeOnce()
            if !warmedUp && ms >= 0 {
                warmedUp = true
                continue
            }
            warmedUp = true
            samples.append(ms)
            if samples.count > 6 { samples.removeFirst() }
            publish()
            try? await Task.sleep(nanoseconds: 10_000_000_000)
        }
    }

    private func probeOnce() async -> Int {
        guard let url = URL(string: AppSettings.shared.pingURL) else { return -1 }
        let start = Date()
        do {
            let (_, response) = try await session.data(from: url)
            let code = (response as? HTTPURLResponse)?.statusCode ?? 0
            return (200..<400).contains(code) ? Int(Date().timeIntervalSince(start) * 1000) : -1
        } catch {
            return -1
        }
    }

    private func publish() {
        let ok = samples.filter { $0 >= 0 }.sorted()
        let loss = (samples.count - ok.count) * 100 / max(samples.count, 1)
        let rtt = ok.isEmpty ? nil : ok[ok.count / 2]
        lossPct = loss
        pingMs = rtt
        score = Self.score(rtt: rtt, lossPct: loss)
    }

    /// Задержка до 150 мс — 100, к 900 мс линейно падает до 35; каждые 10% потерь — минус 5.
    static func score(rtt: Int?, lossPct: Int) -> Int {
        guard let rtt else { return 0 }
        let base: Double = rtt <= 150 ? 100 : (rtt >= 900 ? 35 : 100 - Double(rtt - 150) * 65 / 750)
        return min(max(Int(base - Double(lossPct) * 0.5), 0), 100)
    }
}

// MARK: - Мелкие элементы

struct PingLabel: View {
    let ms: Int

    var body: some View {
        let color: Color = ms < 0 ? GojiColor.danger : (ms < 150 ? GojiColor.teal : (ms < 400 ? GojiColor.warning : GojiColor.terracotta))
        Text(ms < 0 ? L("servers.unavailable") : L("servers.ping_ms", ms))
            .font(GojiFont.manrope(12, .bold, relativeTo: .caption).monospacedDigit())
            .foregroundStyle(color)
    }
}

/// Баннер (порт Android BannerCard): подложка primaryContainer/tertiaryContainer, круг 28 со
/// значком, текст 12, крестик-круг 24.
struct BannerView: View {
    let text: String
    let kind: NetworkMonitor.Banner.Kind
    let onClose: () -> Void

    var body: some View {
        let ok = kind == .success
        let icon: String = {
            switch kind {
            case .warning: return "exclamationmark"
            case .success: return "checkmark"
            case .info: return "info"
            }
        }()
        HStack(alignment: .top, spacing: 11) {
            Image(systemName: icon)
                .font(.system(size: 13, weight: .bold))
                .foregroundStyle(GojiColor.onPrimary)
                .frame(width: 28, height: 28)
                .background(Circle().fill(ok ? GojiColor.teal : GojiColor.terracotta))
            Text(text)
                .font(GojiFont.manrope(12, .semibold))
                .foregroundStyle(GojiColor.textPrimary)
                .fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 4)
            Button(action: onClose) {
                Image(systemName: "xmark")
                    .font(.system(size: 10, weight: .bold))
                    .foregroundStyle(GojiColor.textPrimary)
                    .frame(width: 24, height: 24)
                    .background(Circle().fill(GojiColor.surfaceContainerHighest))
            }
            .accessibilityLabel(L("common.close"))
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 10)
        .background(
            RoundedRectangle(cornerRadius: 20, style: .continuous)
                .fill(ok ? GojiColor.tealTint : GojiColor.terracottaTint)
        )
    }
}

/// Нет активной подписки: пробный период (если доступен) и поддержка. Покупка в iOS-версии
/// не предлагается (правило App Store 3.1.1 — цифровые подписки только через Apple).
struct NoSubscriptionCard: View {
    @EnvironmentObject private var store: SubscriptionStore
    @EnvironmentObject private var model: AppModel
    @State private var activating = false

    var body: some View {
        GlassCard {
            VStack(alignment: .leading, spacing: 10) {
                Text(L("nosub.title"))
                    .font(GojiFont.section())
                    .foregroundStyle(GojiColor.textPrimary)
                Text(L("nosub.text"))
                    .font(GojiFont.body())
                    .foregroundStyle(GojiColor.textSecondary)
                Button {
                    Task {
                        activating = true
                        defer { activating = false }
                        do {
                            try await store.activateTrial()
                            model.toast = L("trial.activated")
                        } catch {
                            model.toast = error.localizedDescription
                        }
                    }
                } label: {
                    if activating { ProgressView() } else { Text(L("trial.activate")) }
                }
                .buttonStyle(PrimaryButtonStyle())
                .disabled(activating)
            }
        }
    }
}
