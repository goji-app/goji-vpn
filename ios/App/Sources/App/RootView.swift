import SwiftUI

struct RootView: View {
    @EnvironmentObject private var model: AppModel

    var body: some View {
        ZStack {
            GlassBackdrop()
            switch model.route {
            case .onboarding:
                PrivacyIntroView()
                    .transition(.opacity)
            case .login:
                NavigationStack { LoginView() }
                    .transition(.opacity)
            case .main:
                MainTabView()
                    .transition(.opacity)
            }
        }
        .animation(.easeInOut(duration: 0.25), value: model.route)
        .overlay(alignment: .top) { ToastView() }
        .alert(L("transfer.confirm.title"), isPresented: Binding(
            get: { model.pendingTransfer != nil },
            set: { if !$0 { model.pendingTransfer = nil } }
        )) {
            Button(L("transfer.confirm.yes")) { Task { await model.confirmTransfer() } }
            Button(L("common.cancel"), role: .cancel) { model.pendingTransfer = nil }
        } message: {
            Text(AuthService.shared.isLoggedIn ? L("transfer.confirm.replace") : L("transfer.confirm.text"))
        }
    }
}

/// Короткое всплывающее сообщение сверху (ошибки сети, «скопировано» и т.п.).
struct ToastView: View {
    @EnvironmentObject private var model: AppModel

    var body: some View {
        if let text = model.toast {
            Text(text)
                .font(GojiFont.manrope(13, .semibold))
                .foregroundStyle(GojiColor.textPrimary)
                .padding(.horizontal, 16)
                .padding(.vertical, 12)
                .background(GlassSurface(radius: 16, strong: true))
                .padding(.horizontal, 24)
                .padding(.top, 8)
                .transition(.move(edge: .top).combined(with: .opacity))
                .onTapGesture { model.toast = nil }
                .task(id: text) {
                    try? await Task.sleep(nanoseconds: 3_000_000_000)
                    if model.toast == text { withAnimation { model.toast = nil } }
                }
                .accessibilityAddTraits(.isStaticText)
        }
    }
}

struct MainTabView: View {
    @EnvironmentObject private var model: AppModel

    var body: some View {
        ZStack(alignment: .bottom) {
            Group {
                switch model.tab {
                case .home: NavigationStack { ConnectView() }
                case .servers: NavigationStack { ServersView() }
                case .subscription: NavigationStack { SubscriptionView() }
                case .settings: NavigationStack { SettingsView() }
                }
            }
            GlassTabBar(selection: $model.tab)
        }
        .ignoresSafeArea(.keyboard)
    }
}

/// Короткая нижняя панель навигации Material 3 Expressive (порт Android GlassTabBar.kt, 163f5ab):
/// во всю ширину, surfaceContainer, высота 64 над индикатором «домой». У выбранной вкладки под
/// иконкой — таблетка secondaryContainer 56×32, раскрывается пружиной.
struct GlassTabBar: View {
    @Binding var selection: AppModel.Tab

    private struct Item: Identifiable {
        let tab: AppModel.Tab
        let icon: String
        let title: String
        var id: AppModel.Tab { tab }
    }

    private let items: [Item] = [
        Item(tab: .home, icon: "checkmark.shield", title: "tab.home"),
        Item(tab: .servers, icon: "globe", title: "tab.servers"),
        Item(tab: .subscription, icon: "creditcard", title: "tab.subscription"),
        Item(tab: .settings, icon: "slider.horizontal.3", title: "tab.settings")
    ]

    var body: some View {
        HStack(spacing: 0) {
            ForEach(items) { item in
                let tab = item.tab, icon = item.icon, title = item.title
                let selected = selection == tab
                Button {
                    if selection != tab {
                        Haptics.light()
                        selection = tab
                    }
                } label: {
                    VStack(spacing: 4) {
                        ZStack {
                            Capsule()
                                .fill(GojiColor.selection)
                                .frame(width: selected ? 56 : 24, height: 32)
                                .opacity(selected ? 1 : 0)
                            Image(systemName: icon)
                                .font(.system(size: 18, weight: selected ? .semibold : .regular))
                                .foregroundStyle(selected ? GojiColor.onSecondaryContainer : GojiColor.textSecondary)
                        }
                        .frame(width: 56, height: 32)
                        Text(L(title))
                            .font(GojiFont.manrope(12, selected ? .bold : .semibold, relativeTo: .caption2))
                            .foregroundStyle(selected ? GojiColor.textPrimary : GojiColor.textSecondary)
                            .lineLimit(1)
                            .minimumScaleFactor(0.8)
                    }
                    .frame(maxWidth: .infinity, minHeight: GojiMetrics.tabBarHeight)
                    .contentShape(Rectangle())
                    .animation(.spring(response: 0.35, dampingFraction: 0.6), value: selected)
                }
                .buttonStyle(.plain)
                .accessibilityLabel(L(title))
                .accessibilityAddTraits(selected ? .isSelected : [])
            }
        }
        .frame(height: GojiMetrics.tabBarHeight)
        .background(GojiColor.surfaceContainer.ignoresSafeArea(edges: .bottom))
    }
}
