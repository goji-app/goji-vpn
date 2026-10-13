package xyz.gojihub.vpn.widget

import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.widget.RemoteViews
import androidx.core.content.ContextCompat
import xyz.gojihub.vpn.MainActivity
import xyz.gojihub.vpn.R
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.subscription.NodeListCache
import xyz.gojihub.vpn.util.stripLeadingFlag
import xyz.gojihub.vpn.vpn.GodjiVpnService

/**
 * Виджет на главный экран — как в Happ/Incy: статус, выбранный сервер, кнопка
 * подключения/отключения, без необходимости открывать само приложение.
 *
 * Размер настраивается от 2×1 (компактная строка статуса) до 3×3 — начиная с некоторого
 * порога высоты виджет переключается на развёрнутый макет со списком серверов подписки
 * прямо внутри (см. [GojiWidgetRemoteViewsService]), выбор узла и подключение работают без
 * открытия приложения. Цвета берутся из ресурсов с values-night-вариантами — виджет следует
 * системной (телефона) тёмной/светлой теме, а не внутриприложенческому тумблеру.
 *
 * Обновляется не только по системному updatePeriodMillis (тот не чаще раза в 30 минут —
 * слишком редко для "живого" статуса), а сразу же после реального события — см. вызовы
 * [refresh] из GodjiVpnService (подключение/отключение) и SubscriptionRepository.select()
 * (смена выбранного сервера).
 */
class GojiWidgetProvider : AppWidgetProvider() {

    override fun onUpdate(context: Context, appWidgetManager: AppWidgetManager, appWidgetIds: IntArray) {
        appWidgetIds.forEach { id ->
            appWidgetManager.updateAppWidget(id, buildViews(context, id, appWidgetManager.getAppWidgetOptions(id)))
        }
    }

    override fun onAppWidgetOptionsChanged(
        context: Context,
        appWidgetManager: AppWidgetManager,
        appWidgetId: Int,
        newOptions: Bundle
    ) {
        appWidgetManager.updateAppWidget(appWidgetId, buildViews(context, appWidgetId, newOptions))
    }

    companion object {
        // Порог высоты, начиная с которого виджет считается "большим" и переключается на
        // список серверов — примерно 2 ячейки лончера (формула большинства лончеров:
        // высота_dp ≈ 70×N − 30, для N=2 это 110dp). Ниже — просто статус-строка (2×1..2×2).
        private const val EXPANDED_MIN_HEIGHT_DP = 100

        fun refresh(context: Context) {
            val manager = AppWidgetManager.getInstance(context)
            val ids = manager.getAppWidgetIds(ComponentName(context, GojiWidgetProvider::class.java))
            ids.forEach { id ->
                manager.updateAppWidget(id, buildViews(context, id, manager.getAppWidgetOptions(id)))
            }
            if (ids.isNotEmpty()) manager.notifyAppWidgetViewDataChanged(ids, R.id.widget_server_list)
            // Виджеты концепта Goji 2.0 обновляются по тем же событиям.
            GojiStatusWidgetProvider.refresh(context)
            GojiSubscriptionWidgetProvider.refresh(context)
        }

        private fun buildViews(context: Context, appWidgetId: Int, options: Bundle?): RemoteViews {
            val minHeight = options?.getInt(AppWidgetManager.OPTION_APPWIDGET_MIN_HEIGHT, 0) ?: 0
            return if (minHeight >= EXPANDED_MIN_HEIGHT_DP) buildExpandedViews(context, appWidgetId)
            else buildCompactViews(context)
        }

        private fun buildCompactViews(context: Context): RemoteViews {
            val running = GodjiVpnService.isRunning.value
            val cached = NodeListCache.load(context)
            val selected = cached?.nodes?.firstOrNull { it.id == cached.selectedId }
            val nodeLabel = selected?.let { n ->
                val flag = n.geo?.code?.let(xyz.gojihub.vpn.geo.CountryGeoLookup::flagEmoji) ?: "🌐"
                "$flag  " + (n.geo?.displayCityCountry(Loc.lang) ?: stripLeadingFlag(n.name))
            } ?: Loc.s.widgetNotSelected

            val views = RemoteViews(context.packageName, R.layout.widget_goji)
            applyStatus(context, views, running)
            views.setTextViewText(R.id.widget_node, nodeLabel)
            views.setOnClickPendingIntent(R.id.widget_button, connectToggleIntent(context, running))
            views.setOnClickPendingIntent(R.id.widget_root, openAppIntent(context, requestCode = 1))
            views.setOnClickPendingIntent(R.id.widget_prev, nodeStepIntent(context, GojiWidgetActionReceiver.ACTION_PREV_NODE, requestCode = 2))
            views.setOnClickPendingIntent(R.id.widget_next, nodeStepIntent(context, GojiWidgetActionReceiver.ACTION_NEXT_NODE, requestCode = 3))
            return views
        }

        private fun buildExpandedViews(context: Context, appWidgetId: Int): RemoteViews {
            val running = GodjiVpnService.isRunning.value
            val views = RemoteViews(context.packageName, R.layout.widget_goji_expanded)
            applyStatus(context, views, running)
            views.setOnClickPendingIntent(R.id.widget_button, connectToggleIntent(context, running))

            // Уникальный data Uri на каждый экземпляр виджета — иначе система может не
            // обновить/переиспользовать RemoteViewsFactory другого виджета с тем же Intent.
            val serviceIntent = Intent(context, GojiWidgetRemoteViewsService::class.java).apply {
                data = Uri.parse("goji-widget://server-list/$appWidgetId")
                putExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, appWidgetId)
            }
            views.setRemoteAdapter(R.id.widget_server_list, serviceIntent)
            views.setEmptyView(R.id.widget_server_list, R.id.widget_server_empty)

            // setOnClickFillInIntent на строках списка (см. GojiWidgetRemoteViewsService)
            // требует именно MUTABLE PendingIntent-шаблон — система сама подставляет extras
            // из fillInIntent конкретной строки в момент клика.
            val selectTemplate = PendingIntent.getBroadcast(
                context, 0,
                Intent(context, GojiWidgetActionReceiver::class.java).setAction(GojiWidgetActionReceiver.ACTION_SELECT_NODE),
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_MUTABLE
            )
            views.setPendingIntentTemplate(R.id.widget_server_list, selectTemplate)
            return views
        }

        private fun applyStatus(context: Context, views: RemoteViews, running: Boolean) {
            views.setTextViewText(R.id.widget_status, if (running) Loc.s.widgetConnected else Loc.s.widgetDisconnected)
            views.setInt(
                R.id.widget_dot, "setColorFilter",
                ContextCompat.getColor(context, if (running) R.color.widget_dot_active else R.color.widget_dot_muted)
            )
            views.setTextViewText(R.id.widget_button, if (running) Loc.s.widgetDisconnect else Loc.s.widgetConnect)
            views.setInt(
                R.id.widget_button, "setBackgroundResource",
                if (running) R.drawable.widget_button_red else R.drawable.widget_button_teal
            )
        }

        /** Отключение дёргает сервис напрямую (тот же приём, что и в самом
         *  VPN-уведомлении). Подключение теперь тоже работает прямо из виджета —
         *  см. GojiWidgetActionReceiver.ACTION_CONNECT — и открывает приложение только если
         *  системное разрешение на VPN ещё не выдавалось (там его не запросить без Activity). */
        internal fun connectToggleIntent(context: Context, running: Boolean): PendingIntent = if (running) {
            PendingIntent.getService(
                context, 0,
                Intent(context, GodjiVpnService::class.java).setAction(GodjiVpnService.ACTION_DISCONNECT),
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
            )
        } else {
            PendingIntent.getBroadcast(
                context, 0,
                Intent(context, GojiWidgetActionReceiver::class.java).setAction(GojiWidgetActionReceiver.ACTION_CONNECT),
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
            )
        }

        private fun nodeStepIntent(context: Context, action: String, requestCode: Int): PendingIntent = PendingIntent.getBroadcast(
            context, requestCode,
            Intent(context, GojiWidgetActionReceiver::class.java).setAction(action),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        internal fun openAppIntent(context: Context, requestCode: Int): PendingIntent = PendingIntent.getActivity(
            context, requestCode,
            Intent(context, MainActivity::class.java).setFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
    }
}
