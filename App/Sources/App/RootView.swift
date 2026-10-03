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
                .padding(.horizontal, 20)
                .padding(.bottom, 6)
        }
        .ignoresSafeArea(.keyboard)
    }
}

/// Плавающий стеклянный таб-бар (как в v5 на Android/Windows).
struct GlassTabBar: View {
    @Binding var selection: AppModel.Tab

    private struct Item: Identifiable {
        let tab: AppModel.Tab
        let icon: String
        let title: String
        var id: AppModel.Tab { tab }
    }

    private let items: [Item] = [
        Item(tab: .home, icon: "shield.lefthalf.filled", title: "tab.home"),
        Item(tab: .servers, icon: "globe.europe.africa", title: "tab.servers"),
        Item(tab: .subscription, icon: "person.crop.circle", title: "tab.subscription"),
        Item(tab: .settings, icon: "gearshape", title: "tab.settings")
    ]

    var body: some View {
        HStack(spacing: 4) {
            ForEach(items) { item in
                let tab = item.tab, icon = item.icon, title = item.title
                let selected = selection == tab
                Button {
                    if selection != tab {
                        Haptics.light()
                        selection = tab
                    }
                } label: {
                    VStack(spacing: 3) {
                        Image(systemName: icon)
                            .font(.system(size: 18, weight: selected ? .bold : .regular))
                        Text(L(title))
                            .font(GojiFont.manrope(10.5, selected ? .bold : .medium, relativeTo: .caption2))
                            .lineLimit(1)
                            .minimumScaleFactor(0.8)
                    }
                    .foregroundStyle(selected ? GojiColor.tealDeep : GojiColor.textSecondary)
                    .frame(maxWidth: .infinity, minHeight: 52)
                    .background(
                        Capsule().fill(selected ? GojiColor.selection : .clear)
                    )
                }
                .buttonStyle(.plain)
                .accessibilityLabel(L(title))
                .accessibilityAddTraits(selected ? .isSelected : [])
            }
        }
        .padding(6)
        .frame(height: GojiMetrics.tabBarHeight)
        .background(GlassSurface(radius: 32, strong: true))
    }
}
