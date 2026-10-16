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

/// Нижняя навигация — системная панель вкладок iOS (на iOS 26 — Liquid Glass). Раньше здесь была
/// перенесённая с Android панель Material 3: без системного вида, возврата наверх повторным
/// нажатием на вкладку и крупного шрифта.
struct MainTabView: View {
    @EnvironmentObject private var model: AppModel

    var body: some View {
        TabView(selection: $model.tab) {
            NavigationStack { ConnectView() }
                .tabItem { Label(L("tab.home"), systemImage: "checkmark.shield") }
                .tag(AppModel.Tab.home)
            NavigationStack { ServersView() }
                .tabItem { Label(L("tab.servers"), systemImage: "globe") }
                .tag(AppModel.Tab.servers)
            NavigationStack { SubscriptionView() }
                .tabItem { Label(L("tab.subscription"), systemImage: "creditcard") }
                .tag(AppModel.Tab.subscription)
            NavigationStack { SettingsView() }
                .tabItem { Label(L("tab.settings"), systemImage: "slider.horizontal.3") }
                .tag(AppModel.Tab.settings)
        }
        .tint(GojiColor.tealDeep)
        .onChange(of: model.tab) { _ in Haptics.light() }
    }
}
