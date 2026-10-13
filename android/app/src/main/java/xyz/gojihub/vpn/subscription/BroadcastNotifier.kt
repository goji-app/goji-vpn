package xyz.gojihub.vpn.subscription

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.text.HtmlCompat
import xyz.gojihub.vpn.MainActivity
import xyz.gojihub.vpn.R
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.network.models.BroadcastDto
import xyz.gojihub.vpn.util.AppLogger
import xyz.gojihub.vpn.util.LogCategory

/** Короткое локальное уведомление о новой новости/рассылке (gojihub.xyz/api/broadcasts) —
 *  тот же приём, что и SubscriptionNotifier: считается на устройстве по данным, которые
 *  и так приходят при обычном обновлении подписки (см. SubscriptionRepository.refresh(),
 *  вызывается воркером раз в час), отдельного push-сервера нет. Уведомляет только про самую
 *  свежую новость с прошлой проверки — если накопилось несколько, не спамит по одному
 *  уведомлению на каждую. */
object BroadcastNotifier {
    private const val TAG = "GodjiPush"
    private const val CHANNEL_ID = "godji_broadcast_alerts"
    private const val PREFS_NAME = "godji_broadcast_notify"
    private const val KEY_LAST_SEEN_ID = "last_seen_id"
    private const val NOTIF_ID = 2003
    private const val PREVIEW_LENGTH = 120

    fun check(context: Context, broadcasts: List<BroadcastDto>) {
        if (broadcasts.isEmpty()) return
        ensureChannel(context)
        val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        val lastSeenId = prefs.getString(KEY_LAST_SEEN_ID, null)

        // Порядок с бэкенда не гарантирован — сортируем сами, свежая по CreatedAt (ISO-8601,
        // лексикографическая сортировка строк здесь эквивалентна хронологической) первая.
        val newest = broadcasts.maxByOrNull { it.createdAt } ?: return

        // lastSeenId == null — первый запуск/первая проверка на этом устройстве: просто
        // запоминаем текущее состояние, не уведомляем о всей уже накопленной истории новостей.
        if (lastSeenId != null && newest.id != lastSeenId) {
            notify(context, newest)
        }
        prefs.edit().putString(KEY_LAST_SEEN_ID, newest.id).apply()
    }

    private fun notify(context: Context, broadcast: BroadcastDto) {
        val preview = HtmlCompat.fromHtml(broadcast.content, HtmlCompat.FROM_HTML_MODE_COMPACT)
            .toString().trim().take(PREVIEW_LENGTH)
        AppLogger.i(context, LogCategory.PUSH, TAG, "Показ уведомления о новой рассылке (id=${broadcast.id})")
        show(context, Loc.s.notifBroadcastTitle, preview)
    }

    private fun show(context: Context, title: String, text: String) {
        val contentIntent = PendingIntent.getActivity(
            context, NOTIF_ID,
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
        runCatching { NotificationManagerCompat.from(context).notify(NOTIF_ID, notification) }
            .onFailure { AppLogger.e(context, LogCategory.PUSH, TAG, "notify(id=$NOTIF_ID) failed", it) }
    }

    private fun ensureChannel(context: Context) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return
        val channel = NotificationChannel(CHANNEL_ID, Loc.s.notifBroadcastChannelName, NotificationManager.IMPORTANCE_DEFAULT).apply {
            description = Loc.s.notifBroadcastChannelDesc
            // Значок-бейдж на иконке приложения при непрочитанном уведомлении — сам бейдж
            // рисует лаунчер (не наш код), от нас только разрешение на канале; по умолчанию
            // и так true, но перечисляем явно, а не полагаемся на дефолт.
            setShowBadge(true)
        }
        context.getSystemService(NotificationManager::class.java).createNotificationChannel(channel)
    }
}
