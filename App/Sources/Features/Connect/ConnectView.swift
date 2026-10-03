import SwiftUI

struct ConnectView: View {
    @EnvironmentObject private var model: AppModel
    @EnvironmentObject private var vpn: VPNController
    @EnvironmentObject private var store: SubscriptionStore
    @EnvironmentObject private var pings: PingService
    @EnvironmentObject private var network: NetworkMonitor

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                greeting
                GlobeView(status: globeStatus, node: displayGeo)
                    .frame(height: 260)
                    .frame(maxWidth: .infinity)
                headline
                connectButton
                if let banner = currentBanner {
                    BannerView(text: banner.text, kind: banner.kind) { dismissBanner() }
                }
                nodeCard
                if vpn.phase == .connected { speedCard }
                subscriptionCard
                networkChip
            }
            .padding(.horizontal, GojiMetrics.screenPadding)
            .padding(.bottom, GojiMetrics.tabBarHeight + 24)
        }
        .scrollIndicators(.hidden)
        .refreshable { await store.refresh() }
        .toolbar(.hidden, for: .navigationBar)
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

    private var greeting: some View {
        let g = Greetings.forLang(displayGeo?.lang)
        return VStack(alignment: .leading, spacing: 2) {
            Text(g.hi)
                .font(g.useSerif ? GojiFont.serif(40) : GojiFont.manrope(34, .bold, relativeTo: .largeTitle))
                .foregroundStyle(GojiColor.textPrimary)
            Text(g.transliteration.isEmpty ? g.language : "«\(g.transliteration)» · \(g.language)")
                .font(GojiFont.caption())
                .foregroundStyle(GojiColor.textSecondary)
        }
        .padding(.top, 8)
        .accessibilityElement(children: .combine)
    }

    private var headline: some View {
        let node = store.selectedNode
        let title: String
        let subtitle: String
        switch vpn.phase {
        case .connected:
            let place = displayGeo.map { AppLanguage.isRussian ? $0.ruPrep : $0.displayName } ?? L("connect.fallback_place")
            title = L("connect.headline.on", place)
            subtitle = L("connect.subline.on", node?.displayName ?? "")
        case .connecting, .disconnecting:
            title = L("connect.headline.connecting")
            subtitle = L("connect.subline.connecting", node?.displayName ?? "")
        case .disconnected:
            title = L("connect.headline.off")
            subtitle = L("connect.subline.off")
        }
        return VStack(alignment: .leading, spacing: 4) {
            Text(title)
                .font(GojiFont.title(30))
                .foregroundStyle(GojiColor.textPrimary)
                .contentTransition(.opacity)
            Text(subtitle)
                .font(GojiFont.body())
                .foregroundStyle(GojiColor.textSecondary)
            if vpn.phase == .connected, let since = vpn.connectedSince {
                TimelineView(.periodic(from: .now, by: 1)) { context in
                    Text(Self.elapsed(from: since, to: context.date))
                        .font(GojiFont.manrope(13, .semibold).monospacedDigit())
                        .foregroundStyle(GojiColor.tealDeep)
                }
            }
        }
        .accessibilityElement(children: .combine)
    }

    private var connectButton: some View {
        let on = vpn.phase == .connected || vpn.phase == .connecting
        return Button {
            Task { await vpn.toggle() }
        } label: {
            HStack(spacing: 10) {
                if vpn.isBusy {
                    ProgressView().tint(on ? GojiColor.textPrimary : .white)
                } else {
                    Image(systemName: "power")
                        .font(.system(size: 18, weight: .bold))
                }
                Text(on ? L("connect.cta.off") : L("connect.cta.on"))
            }
            .font(GojiFont.manrope(17, .bold, relativeTo: .headline))
            .foregroundStyle(on ? GojiColor.textPrimary : .white)
            .frame(maxWidth: .infinity, minHeight: 60)
            .background {
                if on {
                    GlassSurface(radius: 30, strong: true)
                } else {
                    Capsule().fill(GojiColor.accentGradient)
                        .shadow(color: GojiColor.accentGlow, radius: 18, y: 8)
                }
            }
        }
        .buttonStyle(.plain)
        .disabled(store.nodes.isEmpty && vpn.phase == .disconnected)
        .accessibilityHint(on ? L("connect.a11y.disconnect") : L("connect.a11y.connect"))
    }

    private var nodeCard: some View {
        Button {
            model.tab = .servers
        } label: {
            GlassCard(padding: 14) {
                HStack(spacing: 12) {
                    Text(store.selectedNode?.flag ?? "🌐")
                        .font(.system(size: 28))
                        .frame(width: 44, height: 44)
                        .background(Circle().fill(GojiColor.chip))
                    VStack(alignment: .leading, spacing: 2) {
                        Text(L("connect.node.caption"))
                            .font(GojiFont.caption())
                            .foregroundStyle(GojiColor.textSecondary)
                        Text(store.selectedNode?.displayName ?? L("connect.node.none"))
                            .font(GojiFont.manrope(16, .bold))
                            .foregroundStyle(GojiColor.textPrimary)
                            .lineLimit(1)
                    }
                    Spacer()
                    if let id = store.selectedNode?.id, let ms = pings.pings[id] {
                        PingLabel(ms: ms)
                    }
                    Image(systemName: "chevron.right")
                        .foregroundStyle(GojiColor.textMuted)
                }
            }
        }
        .buttonStyle(.plain)
        .accessibilityHint(L("connect.node.a11y"))
    }

    private var speedCard: some View {
        HStack(spacing: 12) {
            SpeedTile(title: L("connect.stat.down"), icon: "arrow.down", value: vpn.downBytesPerSec, history: vpn.downHistory, color: GojiColor.teal)
            SpeedTile(title: L("connect.stat.up"), icon: "arrow.up", value: vpn.upBytesPerSec, history: vpn.upHistory, color: GojiColor.terracotta)
        }
    }

    @ViewBuilder
    private var subscriptionCard: some View {
        if let sub = store.subscription {
            GlassCard(padding: 14) {
                VStack(alignment: .leading, spacing: 8) {
                    HStack {
                        Text(L("connect.traffic"))
                            .font(GojiFont.caption())
                            .foregroundStyle(GojiColor.textSecondary)
                        Spacer()
                        Text(trafficText(sub))
                            .font(GojiFont.manrope(14, .bold))
                            .foregroundStyle(GojiColor.textPrimary)
                    }
                    if let traffic = sub.traffic, !traffic.isUnlimited, traffic.limitBytes > 0 {
                        ProgressView(value: min(Double(traffic.usedBytes) / Double(traffic.limitBytes), 1))
                            .tint(GojiColor.teal)
                    }
                    if let expire = sub.expireDate {
                        Text(L("connect.expiry", DateParsing.shortDate(expire), Plural.form(sub.daysLeft ?? 0, "days")))
                            .font(GojiFont.caption())
                            .foregroundStyle((sub.daysLeft ?? 0) <= 3 ? GojiColor.warning : GojiColor.textSecondary)
                    }
                }
            }
        } else if store.subscriptionLoaded {
            NoSubscriptionCard()
        }
    }

    private var networkChip: some View {
        let (icon, text, color): (String, String, Color) = {
            switch network.state {
            case .wifi: return ("wifi", L("net.wifi"), GojiColor.teal)
            case .cellular: return ("antenna.radiowaves.left.and.right", L("net.cellular"), GojiColor.teal)
            case .jammed: return ("exclamationmark.triangle", L("net.jammed"), GojiColor.jamText)
            }
        }()
        return HStack(spacing: 8) {
            Image(systemName: icon)
            Text(text)
        }
        .font(GojiFont.manrope(12, .semibold, relativeTo: .caption))
        .foregroundStyle(color)
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
        .background(Capsule().fill(GojiColor.chip))
        .frame(maxWidth: .infinity)
        .accessibilityElement(children: .combine)
    }

    private func trafficText(_ sub: SubscriptionInfo) -> String {
        let used = Double(sub.traffic?.usedBytes ?? 0) / 1_000_000_000
        if sub.traffic?.isUnlimited ?? true {
            return L("connect.traffic.unlimited", String(format: "%.1f", used))
        }
        let quota = Double(sub.traffic?.limitBytes ?? 0) / 1_000_000_000
        return L("connect.traffic.limited", String(format: "%.1f", used), String(format: "%.0f", quota))
    }

    static func elapsed(from start: Date, to now: Date) -> String {
        let total = max(Int(now.timeIntervalSince(start)), 0)
        return String(format: "%02d:%02d:%02d", total / 3600, (total % 3600) / 60, total % 60)
    }
}

struct PingLabel: View {
    let ms: Int

    var body: some View {
        let color: Color = ms < 0 ? GojiColor.danger : (ms < 150 ? GojiColor.teal : (ms < 400 ? GojiColor.warning : GojiColor.terracotta))
        Text(ms < 0 ? L("servers.unavailable") : L("servers.ping_ms", ms))
            .font(GojiFont.manrope(12, .bold, relativeTo: .caption).monospacedDigit())
            .foregroundStyle(color)
    }
}

struct SpeedTile: View {
    let title: String
    let icon: String
    let value: Double
    let history: [Double]
    let color: Color

    var body: some View {
        GlassCard(padding: 12) {
            VStack(alignment: .leading, spacing: 6) {
                Label(title, systemImage: icon)
                    .font(GojiFont.caption())
                    .foregroundStyle(GojiColor.textSecondary)
                Text(Self.format(value))
                    .font(GojiFont.manrope(18, .bold).monospacedDigit())
                    .foregroundStyle(GojiColor.textPrimary)
                Sparkline(values: history, color: color)
                    .frame(height: 26)
            }
        }
        .accessibilityElement(children: .combine)
    }

    /// Байты/с → «3.4 МБ/с» (в мегабайтах, как на Android).
    static func format(_ bytesPerSec: Double) -> String {
        if bytesPerSec >= 1_000_000 { return L("speed.mb", String(format: "%.1f", bytesPerSec / 1_000_000)) }
        return L("speed.kb", String(format: "%.0f", bytesPerSec / 1_000))
    }
}

struct Sparkline: View {
    let values: [Double]
    let color: Color

    var body: some View {
        GeometryReader { geo in
            let maxValue = max(values.max() ?? 1, 1)
            let step = values.count > 1 ? geo.size.width / CGFloat(values.count - 1) : 0
            Path { path in
                for (i, v) in values.enumerated() {
                    let point = CGPoint(x: CGFloat(i) * step, y: geo.size.height * (1 - CGFloat(v / maxValue)))
                    if i == 0 { path.move(to: point) } else { path.addLine(to: point) }
                }
            }
            .stroke(color, style: StrokeStyle(lineWidth: 2, lineCap: .round, lineJoin: .round))
        }
        .accessibilityHidden(true)
    }
}

struct BannerView: View {
    let text: String
    let kind: NetworkMonitor.Banner.Kind
    let onClose: () -> Void

    var body: some View {
        let (icon, color): (String, Color) = {
            switch kind {
            case .warning: return ("exclamationmark.triangle.fill", GojiColor.jamText)
            case .success: return ("checkmark.circle.fill", GojiColor.tealDeep)
            case .info: return ("info.circle.fill", GojiColor.tealDeep)
            }
        }()
        HStack(alignment: .top, spacing: 10) {
            Image(systemName: icon).foregroundStyle(color)
            Text(text)
                .font(GojiFont.manrope(13, .medium))
                .foregroundStyle(GojiColor.textPrimary)
                .fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 4)
            Button(action: onClose) {
                Image(systemName: "xmark")
                    .font(.system(size: 12, weight: .bold))
                    .foregroundStyle(GojiColor.textMuted)
                    .padding(4)
            }
            .accessibilityLabel(L("common.close"))
        }
        .padding(14)
        .background(
            RoundedRectangle(cornerRadius: 18, style: .continuous)
                .fill(kind == .warning ? GojiColor.jamBg : GojiColor.tealTint)
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
