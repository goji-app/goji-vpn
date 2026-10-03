import Foundation
import UserNotifications

/// Локальные уведомления (без push-сервера):
///  - напоминания до окончания подписки: за 3 дня и за 1 день (для пробного — за 12 часов);
///  - «оплата прошла» — когда срок подписки увеличился после обновления;
///  - новые сообщения в «Новостях».
/// Напоминания планируются заранее на конкретное время, поэтому срабатывают, даже если
/// приложение давно не открывали.
enum NotificationScheduler {
    private static let center = UNUserNotificationCenter.current()
    private static let reminderPrefix = "goji.expiry."
    private static let seenBroadcastsKey = "notif.seenBroadcasts"

    static func requestAuthorization() async {
        let settings = await center.notificationSettings()
        guard settings.authorizationStatus == .notDetermined else { return }
        _ = try? await center.requestAuthorization(options: [.alert, .sound, .badge])
    }

    @MainActor
    static func subscriptionUpdated(old: SubscriptionInfo?, new: SubscriptionInfo) {
        scheduleExpiryReminders(for: new)
        if let oldDate = old?.expireDate, let newDate = new.expireDate,
           old?.id == new.id, newDate.timeIntervalSince(oldDate) > 12 * 3600 {
            post(
                id: "goji.payment.\(Int(newDate.timeIntervalSince1970))",
                title: L("notif.payment.title"),
                body: L("notif.payment.body", new.planName ?? "", DateParsing.shortDate(newDate))
            )
        }
    }

    static func scheduleExpiryReminders(for subscription: SubscriptionInfo) {
        center.getPendingNotificationRequests { requests in
            let old = requests.map(\.identifier).filter { $0.hasPrefix(reminderPrefix) }
            center.removePendingNotificationRequests(withIdentifiers: old)
            guard let expire = subscription.expireDate, expire > Date() else { return }
            let offsets: [(TimeInterval, String)] = subscription.kind == "trial"
                ? [(12 * 3600, L("notif.trial_ending"))]
                : [(3 * 86400, L("notif.plan_ending", subscription.planName ?? "", DateParsing.shortDate(expire))),
                   (1 * 86400, L("notif.plan_ending", subscription.planName ?? "", DateParsing.shortDate(expire)))]
            for (offset, body) in offsets {
                let fire = expire.addingTimeInterval(-offset)
                guard fire > Date().addingTimeInterval(60) else { continue }
                let content = UNMutableNotificationContent()
                content.title = L("notif.expiry.title")
                content.body = body
                content.sound = .default
                let components = Calendar.current.dateComponents([.year, .month, .day, .hour, .minute], from: fire)
                let trigger = UNCalendarNotificationTrigger(dateMatching: components, repeats: false)
                center.add(UNNotificationRequest(identifier: "\(reminderPrefix)\(Int(offset))", content: content, trigger: trigger))
            }
        }
    }

    static func broadcastsUpdated(old: [Broadcast], new: [Broadcast]) {
        let defaults = UserDefaults.standard
        var seen = Set(defaults.stringArray(forKey: seenBroadcastsKey) ?? [])
        // Первый запуск: всё, что уже есть, считаем прочитанным — без пачки уведомлений.
        if defaults.object(forKey: seenBroadcastsKey) == nil {
            defaults.set(new.map(\.id), forKey: seenBroadcastsKey)
            return
        }
        for item in new where !seen.contains(item.id) {
            let text = RichText.plain(item.content)
            post(id: "goji.news.\(item.id)", title: L("notif.news.title"), body: String(text.prefix(180)))
            seen.insert(item.id)
        }
        defaults.set(Array(seen), forKey: seenBroadcastsKey)
    }

    static func clearAll() {
        center.removeAllPendingNotificationRequests()
        center.removeAllDeliveredNotifications()
    }

    private static func post(id: String, title: String, body: String) {
        let content = UNMutableNotificationContent()
        content.title = title
        content.body = body
        content.sound = .default
        center.add(UNNotificationRequest(identifier: id, content: content, trigger: nil))
    }
}
