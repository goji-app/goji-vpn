package xyz.gojihub.vpn.widget

import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.ComponentName
import android.content.Context
import android.widget.RemoteViews
import androidx.core.content.ContextCompat
import xyz.gojihub.vpn.R
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.subscription.NodeListCache
import xyz.gojihub.vpn.util.stripLeadingFlag
import xyz.gojihub.vpn.vpn.GodjiVpnService

/**
 * Виджет «Статус» 2×2 (концепт Goji 2.0): пока VPN поднят — мятная плитка «Защищено» с
 * тёмной кнопкой «Отключить», иначе — обычная поверхность и кнопка «Подключить». Действия —
 * те же, что у [GojiWidgetProvider] (подключение через GojiWidgetActionReceiver, отключение
 * прямо сервисом). Перерисовывается из [GojiWidgetProvider.refresh] — его уже зовут сервис
 * (подключение/отключение) и SubscriptionRepository (смена узла).
 */
class GojiStatusWidgetProvider : AppWidgetProvider() {

    override fun onUpdate(context: Context, appWidgetManager: AppWidgetManager, appWidgetIds: IntArray) {
        val views = build(context)
        appWidgetIds.forEach { appWidgetManager.updateAppWidget(it, views) }
    }

    companion object {
        fun refresh(context: Context) {
            val manager = AppWidgetManager.getInstance(context)
            val ids = manager.getAppWidgetIds(ComponentName(context, GojiStatusWidgetProvider::class.java))
            if (ids.isEmpty()) return
            val views = build(context)
            ids.forEach { manager.updateAppWidget(it, views) }
        }

        private fun build(context: Context): RemoteViews {
            val running = GodjiVpnService.isRunning.value
            val cached = NodeListCache.load(context)
            val selected = cached?.nodes?.firstOrNull { it.id == cached.selectedId }
            val node = selected?.let { it.geo?.displayCityCountry(Loc.lang) ?: stripLeadingFlag(it.name) } ?: Loc.s.widgetNotSelected
            fun color(id: Int) = ContextCompat.getColor(context, id)
            val primary = color(if (running) R.color.widget_on_text else R.color.widget_text_primary)
            val secondary = color(if (running) R.color.widget_on_text_secondary else R.color.widget_text_secondary)

            return RemoteViews(context.packageName, R.layout.widget_status).apply {
                setInt(R.id.ws_root, "setBackgroundResource", if (running) R.drawable.widget_status_on_bg else R.drawable.widget_background)
                setTextColor(R.id.ws_brand, primary)
                setTextColor(R.id.ws_status, primary)
                setTextColor(R.id.ws_node, secondary)
                setInt(R.id.ws_dot, "setColorFilter", if (running) primary else color(R.color.widget_dot_muted))
                setTextViewText(R.id.ws_status, if (running) Loc.f.widgetProtected else Loc.f.widgetUnprotected)
                setTextViewText(R.id.ws_node, node)
                setTextViewText(R.id.ws_button, if (running) Loc.s.widgetDisconnect else Loc.s.widgetConnect)
                setInt(R.id.ws_button, "setBackgroundResource", if (running) R.drawable.widget_on_button else R.drawable.widget_button_teal)
                setTextColor(R.id.ws_button, color(if (running) R.color.widget_on_button_text else R.color.widget_button_text))
                setOnClickPendingIntent(R.id.ws_button, GojiWidgetProvider.connectToggleIntent(context, running))
                setOnClickPendingIntent(R.id.ws_root, GojiWidgetProvider.openAppIntent(context, requestCode = 11))
            }
        }
    }
}
