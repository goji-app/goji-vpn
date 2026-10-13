import SwiftUI

/// «Журнал сети» — порт Android JournalScreen.kt (84b0fb9): доля времени под защитой по часам за
/// сутки (тёплый столбик — в этот час были сбои: глушение, ошибка), счётчики и лента событий.
/// Данные — NetworkJournal, только локальные.
struct JournalView: View {
    @EnvironmentObject private var vpn: VPNController
    @ObservedObject private var journal = NetworkJournal.shared
    @State private var daysAgo = 0

    var body: some View {
        SubScreen(title: L("journal.title")) {
            TimelineView(.periodic(from: .now, by: 60)) { context in
                let day = journal.day(daysAgo: daysAgo, runningNow: vpn.phase == .connected, now: context.date)
                VStack(alignment: .leading, spacing: 8) {
                    PillSegmented(options: [0, 1], selection: $daysAgo) { $0 == 0 ? L("journal.today") : L("journal.yesterday") }
                    uptimeCard(day)
                    HStack(spacing: 8) {
                        counter(day.connects, L("journal.connects"), GojiColor.textPrimary)
                        counter(day.switches, L("journal.switches"), GojiColor.textPrimary)
                        counter(day.issues, L("journal.issues"), day.issues > 0 ? GojiColor.terracotta : GojiColor.teal)
                    }
                    Text(L("journal.events"))
                        .font(GojiFont.manrope(11, .semibold, relativeTo: .caption))
                        .foregroundStyle(GojiColor.textSecondary)
                        .padding(.horizontal, 8)
                        .padding(.top, 6)
                    if day.events.isEmpty {
                        GlassCard {
                            Text(L("journal.empty"))
                                .font(GojiFont.manrope(12.5, .medium))
                                .foregroundStyle(GojiColor.textSecondary)
                        }
                    } else {
                        VStack(spacing: 0) {
                            ForEach(Array(day.events.enumerated()), id: \.element.id) { index, event in
                                if index > 0 { Rectangle().fill(GojiColor.hair).frame(height: 2) }
                                eventRow(event)
                            }
                        }
                        .background(GlassSurface())
                    }
                    Text(L("journal.footer"))
                        .font(GojiFont.manrope(11, .regular, relativeTo: .caption2))
                        .foregroundStyle(GojiColor.textMuted)
                        .padding(.horizontal, 8)
                        .padding(.vertical, 4)
                }
            }
        }
    }

    private func uptimeCard(_ day: NetworkJournal.Day) -> some View {
        let pct = day.elapsed > 0 ? (day.protected * 1000 / day.elapsed).rounded(.down) / 10 : 0
        let pctText = pct.truncatingRemainder(dividingBy: 1) == 0 ? String(format: "%.0f%%", pct) : String(format: "%.1f%%", pct)
        return GlassCard(padding: 16) {
            VStack(alignment: .leading, spacing: 8) {
                HStack(alignment: .bottom) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text(L("journal.protected"))
                            .font(GojiFont.manrope(12, .semibold, relativeTo: .caption))
                            .foregroundStyle(GojiColor.textSecondary)
                        Text(L("journal.of", Self.duration(day.protected), Self.duration(day.elapsed)))
                            .font(GojiFont.manrope(11.5, .medium, relativeTo: .caption))
                            .foregroundStyle(GojiColor.textSecondary)
                    }
                    Spacer()
                    Text(pctText)
                        .font(GojiFont.manrope(24, .extraBold, relativeTo: .title2))
                        .foregroundStyle(GojiColor.teal)
                }
                HStack(alignment: .bottom, spacing: 3) {
                    ForEach(Array(day.hours.enumerated()), id: \.offset) { _, hour in
                        let up = hour.uptime
                        let color: Color = {
                            guard let up else { return GojiColor.track.opacity(0.45) }
                            if hour.issue { return GojiColor.terracotta }
                            return up <= 0 ? GojiColor.track : GojiColor.teal
                        }()
                        RoundedRectangle(cornerRadius: 3, style: .continuous)
                            .fill(color)
                            .frame(height: (up ?? 0) > 0 ? 6 + 50 * CGFloat(up ?? 0) : 4)
                            .frame(maxWidth: .infinity)
                    }
                }
                .frame(height: 56, alignment: .bottom)
                HStack {
                    ForEach(["00:00", "06:00", "12:00", "18:00", "24:00"], id: \.self) { tick in
                        Text(tick)
                            .font(GojiFont.manrope(10.5, .regular, relativeTo: .caption2))
                            .foregroundStyle(GojiColor.textMuted)
                        if tick != "24:00" { Spacer() }
                    }
                }
            }
        }
        .accessibilityElement(children: .combine)
    }

    private func counter(_ value: Int, _ label: String, _ color: Color) -> some View {
        GlassCard(padding: 12, radius: 20) {
            VStack(alignment: .leading, spacing: 1) {
                Text("\(value)")
                    .font(GojiFont.manrope(20, .extraBold, relativeTo: .title3))
                    .foregroundStyle(color)
                Text(label)
                    .font(GojiFont.manrope(11, .medium, relativeTo: .caption))
                    .foregroundStyle(GojiColor.textSecondary)
                    .lineLimit(1)
            }
        }
        .accessibilityElement(children: .combine)
    }

    private func eventRow(_ event: NetworkJournal.Event) -> some View {
        let (title, text) = NetworkJournal.describe(event)
        let dot: Color = event.kind.isIssue ? GojiColor.terracotta
            : event.kind.isSwitch ? GojiColor.warning
            : (event.kind == .connected || event.kind == .leakOK) ? GojiColor.teal
            : GojiColor.outline
        return HStack(alignment: .top, spacing: 10) {
            Text(Self.time.string(from: event.at))
                .font(GojiFont.manrope(12, .bold, relativeTo: .caption).monospacedDigit())
                .foregroundStyle(GojiColor.textSecondary)
                .frame(width: 42, alignment: .leading)
            Circle().fill(dot).frame(width: 8, height: 8).padding(.top, 5)
            VStack(alignment: .leading, spacing: 1) {
                Text(title)
                    .font(GojiFont.manrope(13.5, .semibold))
                    .foregroundStyle(GojiColor.textPrimary)
                if !text.isEmpty {
                    Text(text)
                        .font(GojiFont.manrope(11.5, .regular, relativeTo: .caption))
                        .foregroundStyle(GojiColor.textSecondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
        .accessibilityElement(children: .combine)
    }

    private static let time: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "HH:mm"
        return f
    }()

    static func duration(_ t: TimeInterval) -> String {
        let minutes = Int(t / 60)
        return minutes >= 60 ? L("journal.duration.hm", minutes / 60, minutes % 60) : L("journal.duration.m", minutes)
    }
}
