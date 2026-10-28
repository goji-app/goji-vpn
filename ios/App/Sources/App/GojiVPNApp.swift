import SwiftUI
import UserNotifications

@main
struct GojiVPNApp: App {
    @UIApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @StateObject private var model = AppModel.shared
    @StateObject private var settings = AppSettings.shared
    @Environment(\.scenePhase) private var scenePhase

    var body: some Scene {
        WindowGroup {
            RootView()
                .environmentObject(model)
                .environmentObject(settings)
                .environmentObject(SubscriptionStore.shared)
                .environmentObject(VPNController.shared)
                .environmentObject(PingService.shared)
                .environmentObject(NetworkMonitor.shared)
                .preferredColorScheme(settings.themeMode.colorScheme)
                .tint(GojiColor.teal)
                .onOpenURL { model.handle(url: $0) }
        }
        .onChange(of: scenePhase) { phase in
            if phase == .active { model.didBecomeActive() }
            if phase == .background { BackgroundTasks.schedule() }
        }
    }
}

final class AppDelegate: NSObject, UIApplicationDelegate, UNUserNotificationCenterDelegate {
    func application(_ application: UIApplication, didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]? = nil) -> Bool {
        BackgroundTasks.register()
        // Журнал сети: подключения, смена сети, ошибки (см. NetworkJournal).
        Task { @MainActor in
            NetworkJournal.shared.attach(vpn: .shared, network: .shared, store: .shared)
        }
        UNUserNotificationCenter.current().delegate = self
        AppLog.i(.main, "запуск \(AppConstants.appVersion) (\(AppConstants.buildNumber)), iOS \(UIDevice.current.systemVersion), \(DeviceModel.identifier)")
        return true
    }

    /// Показывать уведомления и при открытом приложении.
    func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification) async -> UNNotificationPresentationOptions {
        [.banner, .sound]
    }
}

/// Корневое состояние: какой экран показывать, отложенный deeplink, тосты.
@MainActor
final class AppModel: ObservableObject {
    static let shared = AppModel()

    enum Route { case onboarding, login, main }
    enum Tab: Hashable { case home, servers, subscription, settings }

    @Published var route: Route
    @Published var tab: Tab = .home
    /// godjivpn://transfer?… из системной камеры — вход только после явного подтверждения.
    @Published var pendingTransfer: String?
    /// Почта из QR-кода переноса — экран входа отправит на неё код и откроет его ввод.
    @Published var transferEmail: String?
    @Published var toast: String?

    private var sessionObserver: NSObjectProtocol?
    private var lastActiveRefresh: Date?

    init() {
        if DemoMode.isEnabled {
            route = .main
            tab = DemoMode.initialTab
            DemoMode.install()
        } else if !AppSettings.shared.privacyAccepted {
            route = .onboarding
        } else {
            route = AuthService.shared.isLoggedIn ? .main : .login
        }
        sessionObserver = NotificationCenter.default.addObserver(forName: .sessionExpired, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in
                guard let self, self.route == .main else { return }
                self.route = .login
                self.toast = L("error.session_expired")
            }
        }
        NetworkMonitor.shared.start()
    }

    func didBecomeActive() {
        guard !DemoMode.isEnabled else { return }
        Task {
            await VPNController.shared.load()
            guard route == .main else { return }
            // Не чаще раза в минуту при переключениях приложений.
            if let last = lastActiveRefresh, Date().timeIntervalSince(last) < 60 { return }
            lastActiveRefresh = Date()
            await SubscriptionStore.shared.refresh()
            await PingService.shared.pingAll()
            await GeoDataUpdater.refreshIfNeeded()
        }
    }

    func acceptPrivacy() {
        AppSettings.shared.privacyAccepted = true
        route = AuthService.shared.isLoggedIn ? .main : .login
    }

    func didLogin() {
        route = .main
        tab = .home
        lastActiveRefresh = nil
        Task {
            await NotificationScheduler.requestAuthorization()
            didBecomeActive()
        }
    }

    func logout() async {
        await VPNController.shared.removeProfile()
        await AuthService.shared.logout()
        SubscriptionStore.shared.clear()
        NotificationScheduler.clearAll()
        route = .login
    }

    func handle(url: URL) {
        if AuthService.isTransferURI(url.absoluteString) {
            pendingTransfer = url.absoluteString
        }
    }

    func confirmTransfer() async {
        guard let raw = pendingTransfer else { return }
        pendingTransfer = nil
        do {
            let email = try AuthService.transferEmail(raw)
            if AuthService.shared.isLoggedIn { await logout() }
            transferEmail = email
        } catch let error as AuthService.TransferError {
            toast = error.message
        } catch {
            toast = error.localizedDescription
        }
    }
}
