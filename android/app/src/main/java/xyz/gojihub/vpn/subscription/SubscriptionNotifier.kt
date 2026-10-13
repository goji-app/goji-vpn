package xyz.gojihub.vpn.subscription

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import xyz.gojihub.vpn.MainActivity
import xyz.gojihub.vpn.R
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.network.models.SubscriptionInfo
import xyz.gojihub.vpn.util.AppLogger
import xyz.gojihub.vpn.util.LogCategory
import xyz.gojihub.vpn.util.formatDate
import java.time.Instant
import java.time.temporal.ChronoUnit

/**
 * Два вида локальных уведомлений о подписке — оба считаются исключительно на устройстве по
 * данным, которые приложение и так уже получает при обычном обновлении подписки (см.
 * SubscriptionRefreshWorker, раз в час); отдельного push-сервера для этого не заводили.
 *
 * 1. Скорое окончание подписки — за 3 дня и за 1 день для обычных тарифов, за 12 часов для
 *    триала (kind:"trial"), каждое один раз на конкретный expire_at, чтобы не дублировать на
 *    каждый следующий часовой прогон воркера.
 * 2. Успешная оплата — обнаруживается косвенно: оплата происходит вне приложения (на сайте
 *    или через Telegram-бота), отдельного колбэка от бэкенда нет, поэтому сравниваем
 *    expire_at с прошлым известным значением при каждом обновлении — если срок подписки
 *    сдвинулся вперёд (или тариф перестал быть триальным), значит оплата прошла.
 */
object SubscriptionNotifier {
    private const val TAG = "GodjiPush"
    private const val CHANNEL_ID = "godji_sub_alerts"
    private const val PREFS_NAME = "godji_sub_notify"
    private const val KEY_LAST_EXPIRE_AT = "last_expire_at"
    private const val KEY_LAST_KIND = "last_kind"
    private const val KEY_WARNED_FOR = "warned_for_expire_at"
    private const val KEY_WARNED_3D_FOR = "warned_3d_for_expire_at"
    private const val KEY_WARNED_1D_FOR = "warned_1d_for_expire_at"
    private const val NOTIF_ID_EXPIRY = 2001
    private const val NOTIF_ID_PAYMENT = 2002

    fun check(context: Context, sub: SubscriptionInfo) {
        ensureChannel(context)
        val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

        val expireInstant = runCatching { Instant.parse(sub.expireAt) }.getOrNull()
        val isTrial = sub.kind == "trial"

        val lastExpireAt = prefs.getString(KEY_LAST_EXPIRE_AT, null)
        val lastKind = prefs.getString(KEY_LAST_KIND, null)

        // Оплата прошла: срок подписки сдвинулся вперёд относительно прошлого известного,
        // или тариф перестал быть триальным — и то и другое бывает только после реальной
        // оплаты. lastExpireAt == null означает первый запуск/первую проверку — не считаем
        // это "оплатой", иначе уведомление придёт при первом же входе в аккаунт.
        if (lastExpireAt != null && expireInstant != null) {
            val lastInstant = runCatching { Instant.parse(lastExpireAt) }.getOrNull()
            val extended = lastInstant != null && expireInstant.isAfter(lastInstant)
            val trialEnded = lastKind == "trial" && !isTrial
            if (extended || trialEnded) {
                notifyPaymentSuccess(context, sub)
                // Новый срок — новое окно "скоро закончится", прошлые предупреждения больше
                // не актуальны.
                prefs.edit().remove(KEY_WARNED_FOR).remove(KEY_WARNED_3D_FOR).remove(KEY_WARNED_1D_FOR).apply()
            }
        }
        prefs.edit()
            .putString(KEY_LAST_EXPIRE_AT, sub.expireAt)
            .putString(KEY_LAST_KIND, sub.kind)
            .apply()

        if (expireInstant == null) return
        val hoursLeft = ChronoUnit.MINUTES.between(Instant.now(), expireInstant) / 60.0
        if (hoursLeft < 0.0) return
        if (isTrial) {
            if (hoursLeft <= 12.0 && prefs.getString(KEY_WARNED_FOR, null) != sub.expireAt) {
                notifyExpirySoon(context, sub, isTrial = true)
                prefs.edit().putString(KEY_WARNED_FOR, sub.expireAt).apply()
            }
            return
        }
        // Обычный тариф — два напоминания: за 3 дня и за 1 день. Каждое — один раз на
        // конкретный expire_at. Если приложение впервые увидело подписку уже в последние
        // сутки, отправляем только "завтра" (и помечаем "3 дня" как пройденное, чтобы оно
        // не пришло следом с устаревшим текстом).
        when {
            hoursLeft <= 24.0 && prefs.getString(KEY_WARNED_1D_FOR, null) != sub.expireAt -> {
                notifyPlanReminder(context, sub, days = 1)
                prefs.edit()
                    .putString(KEY_WARNED_1D_FOR, sub.expireAt)
                    .putString(KEY_WARNED_3D_FOR, sub.expireAt)
                    .apply()
            }
            hoursLeft in 24.0..72.0 && prefs.getString(KEY_WARNED_3D_FOR, null) != sub.expireAt -> {
                notifyPlanReminder(context, sub, days = 3)
                prefs.edit().putString(KEY_WARNED_3D_FOR, sub.expireAt).apply()
            }
        }
    }

    private fun notifyPlanReminder(context: Context, sub: SubscriptionInfo, days: Int) {
        val date = formatDate(sub.expireAt)
        val (title, text) = if (days == 1) {
            Loc.f.expiry1dTitle to Loc.f.expiry1dText(sub.planName, date)
        } else {
            Loc.f.expiry3dTitle to Loc.f.expiry3dText(sub.planName, date)
        }
        AppLogger.i(context, LogCategory.PUSH, TAG, "Напоминание об окончании подписки за $days дн. (planName=${sub.planName}, expireAt=${sub.expireAt})")
        show(context, NOTIF_ID_EXPIRY, title, text)
    }

    private fun notifyExpirySoon(context: Context, sub: SubscriptionInfo, isTrial: Boolean) {
        val text = if (isTrial) {
            Loc.s.notifTrialEnding
        } else {
            Loc.s.notifPlanEnding(sub.planName, formatDate(sub.expireAt))
        }
        AppLogger.i(context, LogCategory.PUSH, TAG, "Показ уведомления \"скоро закончится\" (trial=$isTrial, planName=${sub.planName}, expireAt=${sub.expireAt})")
        show(context, NOTIF_ID_EXPIRY, Loc.s.notifExpirySoonTitle, text)
    }

    private fun notifyPaymentSuccess(context: Context, sub: SubscriptionInfo) {
        AppLogger.i(context, LogCategory.PUSH, TAG, "Показ уведомления \"оплата прошла успешно\" (planName=${sub.planName}, expireAt=${sub.expireAt})")
        show(
            context, NOTIF_ID_PAYMENT, Loc.s.notifPaymentTitle,
            Loc.s.notifPaymentText(sub.planName, formatDate(sub.expireAt))
        )
    }

    private fun show(context: Context, id: Int, title: String, text: String) {
        val contentIntent = PendingIntent.getActivity(
            context, id,
            Intent(context, MainActivity::class.java).setFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        val notification = NotificationCompat.Builder(context, CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle(title)
            .setContentText(text)
            .setStyle(NotificationCompat.BigTextStyle().bigText(text))
            .setAutoCancel(true)
            .setContentIntent(contentIntent)
            .build()
        runCatching { NotificationManagerCompat.from(context).notify(id, notification) }
            .onFailure { AppLogger.e(context, LogCategory.PUSH, TAG, "notify(id=$id) failed", it) }
    }

    private fun ensureChannel(context: Context) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return
        val channel = NotificationChannel(CHANNEL_ID, Loc.s.notifSubChannelName, NotificationManager.IMPORTANCE_DEFAULT).apply {
            description = Loc.s.notifSubChannelDesc
            setShowBadge(true)
        }
        context.getSystemService(NotificationManager::class.java).createNotificationChannel(channel)
    }
}
