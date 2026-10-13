package xyz.gojihub.vpn.update

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
import xyz.gojihub.vpn.util.AppLogger
import xyz.gojihub.vpn.util.LogCategory

/** Короткое локальное уведомление о новой версии приложения — тот же паттерн, что
 *  BroadcastNotifier/SubscriptionNotifier: SharedPreferences с последней версией, о которой
 *  уже уведомляли, чтобы не показывать одно и то же при каждой периодической проверке. */
object AppUpdateNotifier {
    private const val TAG = "GodjiUpdate"
    private const val CHANNEL_ID = "godji_update_alerts"
    private const val PREFS_NAME = "godji_update_notify"
    private const val KEY_LAST_NOTIFIED = "last_notified_version"
    private const val NOTIF_ID = 2004
    private const val PREVIEW_LENGTH = 150

    fun notifyIfNew(context: Context, update: UpdateInfo) {
        val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        if (prefs.getString(KEY_LAST_NOTIFIED, null) == update.version) return
        ensureChannel(context)

        // changelog — тело GitHub-релиза в Markdown; полноценный RichContent-рендер (см.
        // ui/util/RichContent.kt) — только в самом приложении (экран "Настройки"), здесь
        // достаточно короткого превью обычным текстом.
        val preview = HtmlCompat.fromHtml(update.changelog, HtmlCompat.FROM_HTML_MODE_COMPACT)
            .toString().trim().take(PREVIEW_LENGTH)
        val contentIntent = PendingIntent.getActivity(
            context, NOTIF_ID,
            Intent(context, MainActivity::class.java).setFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        val notification = NotificationCompat.Builder(context, CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle(Loc.s.notifUpdateTitle(update.version))
            .setContentText(preview)
            .setStyle(NotificationCompat.BigTextStyle().bigText(preview))
            .setAutoCancel(true)
            .setContentIntent(contentIntent)
            .build()
        runCatching { NotificationManagerCompat.from(context).notify(NOTIF_ID, notification) }
            .onFailure { AppLogger.e(context, LogCategory.MAIN, TAG, "notify(id=$NOTIF_ID) failed", it) }
        prefs.edit().putString(KEY_LAST_NOTIFIED, update.version).apply()
    }

    private fun ensureChannel(context: Context) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return
        val channel = NotificationChannel(CHANNEL_ID, Loc.s.notifUpdateChannelName, NotificationManager.IMPORTANCE_DEFAULT).apply {
            description = Loc.s.notifUpdateChannelDesc
            setShowBadge(true)
        }
        context.getSystemService(NotificationManager::class.java).createNotificationChannel(channel)
    }
}
