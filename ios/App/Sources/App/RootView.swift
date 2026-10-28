import SwiftUI

struct RootView: View {
    @EnvironmentObject private var model: AppModel

    var body: some View {
        ZStack {
            GlassBackdrop()
            switch model.route {
            case .onboarding:
                OnboardingView()
                    .transition(.opacity)
            case .login:
                NavigationStack { LoginView() }
                    .transition(.opacity)
            case .main:
                MainTabView()
                    .transition(.opacity)
            }
        }
        // На ZStack, а не рядом с alert переноса входа: два alert на одном view конфликтуют.
        .alert(L("crash.title"), isPresented: Binding(
            get: { model.crashReport != nil },
            set: { if !$0 { model.crashReport = nil } }
        )) {
            Button(L("crash.send")) { Task { await model.sendCrashReport() } }
            Button(L("crash.later"), role: .cancel) { model.crashReport = nil }
        } message: {
            Text(L("crash.text"))
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
        TabView(selection: Binding(
            get: { model.tab },
            set: { tab in
                if tab != model.tab { Haptics.light() }
                model.tab = tab
            }
        )) {
            NavigationStack { ConnectView() }
                // Иконки вкладок 2026-10-16 — как на Android/Windows/macOS: дом, метка, билет, шестерёнка
                // (системный TabView сам ставит вариант .fill у выбранной).
                .tabItem { Label(L("tab.home"), systemImage: "house") }
                .tag(AppModel.Tab.home)
            NavigationStack { ServersView() }
                .tabItem { Label(L("tab.servers"), systemImage: "mappin") }
                .tag(AppModel.Tab.servers)
            NavigationStack { SubscriptionView() }
                .tabItem { Label(L("tab.subscription"), systemImage: "ticket") }
                .tag(AppModel.Tab.subscription)
            NavigationStack { SettingsView() }
                .tabItem { Label(L("tab.settings"), systemImage: "gearshape") }
                .tag(AppModel.Tab.settings)
        }
        .tint(GojiColor.tealDeep)
    }
}
