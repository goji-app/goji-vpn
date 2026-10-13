package xyz.gojihub.vpn.widget

import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.ComponentName
import android.content.Context
import android.widget.RemoteViews
import androidx.core.content.ContextCompat
import xyz.gojihub.vpn.R
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.util.formatDate
import xyz.gojihub.vpn.util.subscriptionBars
import java.time.Instant

/**
 * Виджет «Подписка» 2×2 (концепт Goji 2.0): сколько дней осталось, пять полосок-индикатор
 * (как плитка на главной) и дата окончания. Дата окончания сохраняется при каждом обновлении
 * подписки ([saveExpiry] из GodjiApplication), а дни считаются от неё в момент отрисовки —
 * так виджет не врёт, даже если приложение несколько дней не открывали (система
 * перерисовывает его по updatePeriodMillis, раз в 3 часа).
 */
class GojiSubscriptionWidgetProvider : AppWidgetProvider() {

    override fun onUpdate(context: Context, appWidgetManager: AppWidgetManager, appWidgetIds: IntArray) {
        val views = build(context)
        appWidgetIds.forEach { appWidgetManager.updateAppWidget(it, views) }
    }

    companion object {
        private const val PREFS = "widget_subscription"
        private const val KEY_EXPIRE = "expire_at"
        private val BAR_IDS = intArrayOf(R.id.wsub_bar0, R.id.wsub_bar1, R.id.wsub_bar2, R.id.wsub_bar3, R.id.wsub_bar4)

        fun saveExpiry(context: Context, expireAtIso: String) {
            val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            if (prefs.getString(KEY_EXPIRE, null) == expireAtIso) return
            prefs.edit().putString(KEY_EXPIRE, expireAtIso).apply()
            refresh(context)
        }

        fun refresh(context: Context) {
            val manager = AppWidgetManager.getInstance(context)
            val ids = manager.getAppWidgetIds(ComponentName(context, GojiSubscriptionWidgetProvider::class.java))
            if (ids.isEmpty()) return
            val views = build(context)
            ids.forEach { manager.updateAppWidget(it, views) }
        }

        private fun build(context: Context): RemoteViews {
            val iso = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getString(KEY_EXPIRE, null)
            val days = iso?.let {
                runCatching { ((Instant.parse(it).toEpochMilli() - System.currentTimeMillis()) / 86_400_000L).toInt().coerceAtLeast(0) }.getOrNull()
            }
            fun color(id: Int) = ContextCompat.getColor(context, id)
            val filled = subscriptionBars(days ?: 0)
            val on = color(
                when {
                    days == null -> R.color.widget_bar_off
                    days < 3 -> R.color.widget_bar_danger
                    days < 7 -> R.color.widget_bar_warm
                    else -> R.color.widget_bar_on
                }
            )
            val off = color(R.color.widget_bar_off)
            return RemoteViews(context.packageName, R.layout.widget_subscription).apply {
                setTextViewText(R.id.wsub_title, Loc.f.subTileTitle)
                setTextViewText(R.id.wsub_days, days?.toString() ?: "—")
                setTextViewText(
                    R.id.wsub_until,
                    if (days != null && iso != null) Loc.f.widgetDaysLine(days, formatDate(iso)) else Loc.f.widgetNoSubscription
                )
                BAR_IDS.forEachIndexed { i, id -> setInt(id, "setColorFilter", if (i < filled) on else off) }
                setOnClickPendingIntent(R.id.wsub_root, GojiWidgetProvider.openAppIntent(context, requestCode = 12))
            }
        }
    }
}
