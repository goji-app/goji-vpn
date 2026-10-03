import Foundation
import BackgroundTasks

/// Фоновое обновление (BGAppRefreshTask): подписка/новости и геоданные. Время запуска
/// выбирает iOS — гарантий частоты нет, поэтому критичное (напоминания об окончании
/// подписки) планируется заранее локальными уведомлениями, а не здесь.
enum BackgroundTasks {
    static let refreshId = "xyz.gojihub.vpn.refresh"

    static func register() {
        BGTaskScheduler.shared.register(forTaskWithIdentifier: refreshId, using: nil) { task in
            guard let task = task as? BGAppRefreshTask else { return }
            handle(task)
        }
    }

    static func schedule() {
        let request = BGAppRefreshTaskRequest(identifier: refreshId)
        request.earliestBeginDate = Date(timeIntervalSinceNow: 4 * 3600)
        do {
            try BGTaskScheduler.shared.submit(request)
        } catch {
            AppLog.d(.main, "фоновое обновление не запланировано: \(error.localizedDescription)")
        }
    }

    private static func handle(_ task: BGAppRefreshTask) {
        schedule()
        let work = Task { @MainActor in
            await SubscriptionStore.shared.refresh()
            await GeoDataUpdater.refreshIfNeeded()
            task.setTaskCompleted(success: true)
        }
        task.expirationHandler = { work.cancel() }
    }
}
