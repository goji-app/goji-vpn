import SwiftUI

struct ServersView: View {
    @EnvironmentObject private var model: AppModel
    @EnvironmentObject private var store: SubscriptionStore
    @EnvironmentObject private var pings: PingService
    @EnvironmentObject private var settings: AppSettings

    var body: some View {
        ScreenScaffold(
            title: L("servers.title"),
            subtitle: Plural.form(store.nodes.count, "servers.count"),
            trailing: AnyView(toolbarButtons)
        ) {
            if store.nodes.isEmpty {
                if store.isRefreshing {
                    ProgressView().frame(maxWidth: .infinity).padding(40)
                } else {
                    MessageCard(icon: "server.rack", text: L("servers.empty"), actionTitle: L("common.retry")) {
                        Task { await store.refresh() }
                    }
                }
            } else {
                PillSegmented(options: ServerSort.allCases, selection: $settings.serverSort) { $0.title }
                VStack(spacing: 10) {
                    ForEach(sortedNodes) { node in
                        ServerRow(
                            node: node,
                            selected: node.id == store.selectedNode?.id,
                            favorite: settings.favorites.contains(node.id),
                            ping: pings.pings[node.id],
                            checking: pings.checkingId == node.id || (pings.checkingAll && pings.pings[node.id] == nil),
                            onSelect: {
                                store.select(node.id)
                                Haptics.light()
                                model.tab = .home
                            },
                            onFavorite: { settings.toggleFavorite(node.id) },
                            onPing: { Task { await pings.pingOne(node) } }
                        )
                    }
                }
            }
        }
        .refreshable {
            await store.refresh()
            await pings.pingAll()
        }
        .toolbar(.hidden, for: .navigationBar)
        .task { if pings.pings.isEmpty { await pings.pingAll() } }
    }

    private var toolbarButtons: some View {
        Button {
            Task {
                await store.refresh()
                await pings.pingAll()
                model.toast = store.lastRefreshFailed ? L("servers.refresh_fail") : L("servers.refresh_ok")
            }
        } label: {
            Group {
                if pings.checkingAll || store.isRefreshing {
                    ProgressView()
                } else {
                    Image(systemName: "arrow.clockwise")
                        .font(.system(size: 16, weight: .semibold))
                }
            }
            .frame(width: 40, height: 40)
            .background(GlassSurface(radius: 20))
        }
        .buttonStyle(.plain)
        .accessibilityLabel(L("servers.refresh"))
    }

    private var sortedNodes: [VPNNode] {
        let nodes = store.nodes
        switch settings.serverSort {
        case .favorites:
            return nodes.filter { settings.favorites.contains($0.id) } + nodes.filter { !settings.favorites.contains($0.id) }
        case .ping:
            return nodes.sorted { a, b in
                let pa = pings.pings[a.id].flatMap { $0 >= 0 ? $0 : nil } ?? Int.max
                let pb = pings.pings[b.id].flatMap { $0 >= 0 ? $0 : nil } ?? Int.max
                return pa < pb
            }
        case .name:
            return nodes.sorted { $0.displayName.localizedCaseInsensitiveCompare($1.displayName) == .orderedAscending }
        }
    }
}

struct ServerRow: View {
    let node: VPNNode
    let selected: Bool
    let favorite: Bool
    let ping: Int?
    let checking: Bool
    let onSelect: () -> Void
    let onFavorite: () -> Void
    let onPing: () -> Void

    var body: some View {
        HStack(spacing: 12) {
            Button(action: onSelect) {
                HStack(spacing: 12) {
                    Text(node.flag)
                        .font(.system(size: 26))
                        .frame(width: 42, height: 42)
                        .background(Circle().fill(GojiColor.chip))
                    VStack(alignment: .leading, spacing: 2) {
                        Text(node.displayName)
                            .font(GojiFont.manrope(15, .bold))
                            .foregroundStyle(GojiColor.textPrimary)
                            .lineLimit(1)
                        if let geo = node.geo {
                            Text(geo.displayCity)
                                .font(GojiFont.caption())
                                .foregroundStyle(GojiColor.textSecondary)
                        }
                    }
                    Spacer(minLength: 4)
                }
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityAddTraits(selected ? .isSelected : [])

            Button(action: onPing) {
                Group {
                    if checking {
                        ProgressView().scaleEffect(0.7)
                    } else if let ping {
                        PingLabel(ms: ping)
                    } else {
                        Text(L("servers.check"))
                            .font(GojiFont.manrope(12, .semibold, relativeTo: .caption))
                            .foregroundStyle(GojiColor.tealDeep)
                    }
                }
                .frame(minWidth: 56, minHeight: 32)
            }
            .buttonStyle(.plain)
            .accessibilityLabel(L("servers.ping"))

            Button(action: onFavorite) {
                Image(systemName: favorite ? "star.fill" : "star")
                    .foregroundStyle(favorite ? GojiColor.warning : GojiColor.textMuted)
                    .frame(width: 32, height: 32)
            }
            .buttonStyle(.plain)
            .accessibilityLabel(favorite ? L("servers.unfavorite") : L("servers.favorite"))
        }
        .padding(12)
        .background(GlassSurface(radius: 20, highlighted: selected))
    }
}
