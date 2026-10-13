package xyz.gojihub.vpn.widget

import android.content.Context
import android.content.Intent
import android.widget.RemoteViews
import android.widget.RemoteViewsService
import xyz.gojihub.vpn.R
import xyz.gojihub.vpn.geo.CountryGeoLookup
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.subscription.VlessNode
import xyz.gojihub.vpn.util.stripLeadingFlag

/** Список серверов в развёрнутом (3x3) виджете — данные те же, что видит остальное
 *  приложение (через тот же Hilt-синглтон SubscriptionRepository, см. WidgetEntryPoint),
 *  а не отдельный офлайн-кэш: тап по строке должен сразу видеть актуальный список. */
class GojiWidgetRemoteViewsService : RemoteViewsService() {
    override fun onGetViewFactory(intent: Intent): RemoteViewsFactory =
        GojiWidgetItemFactory(applicationContext)
}

private class GojiWidgetItemFactory(private val context: Context) : RemoteViewsService.RemoteViewsFactory {
    private var nodes: List<VlessNode> = emptyList()
    private var selectedId: String? = null
    private var pings: Map<String, Int> = emptyMap()

    override fun onCreate() {}

    override fun onDataSetChanged() {
        val entryPoint = context.widgetEntryPoint()
        val repository = entryPoint.subscriptionRepository()
        nodes = repository.nodes.value
        selectedId = repository.selectedId.value
        pings = entryPoint.pingRepository().pings.value
    }

    override fun onDestroy() {}
    override fun getCount(): Int = nodes.size
    override fun getViewTypeCount(): Int = 1
    override fun getItemId(position: Int): Long = position.toLong()
    override fun hasStableIds(): Boolean = true
    override fun getLoadingView(): RemoteViews? = null

    override fun getViewAt(position: Int): RemoteViews {
        val node = nodes[position]
        val flag = node.geo?.code?.let(CountryGeoLookup::flagEmoji) ?: "🌐"
        val name = node.geo?.displayCityCountry(Loc.lang) ?: stripLeadingFlag(node.name)
        val views = RemoteViews(context.packageName, R.layout.widget_server_item)
        views.setTextViewText(R.id.item_flag, flag)
        views.setTextViewText(R.id.item_name, name)
        val ping = pings[node.id]
        views.setTextViewText(
            R.id.item_ping,
            when {
                ping == null || ping == -2 -> ""
                ping < 0 -> "—"
                else -> Loc.s.serversPingMs(ping)
            }
        )
        views.setInt(
            R.id.item_root, "setBackgroundResource",
            if (node.id == selectedId) R.drawable.widget_list_item_selected else R.drawable.widget_list_item
        )
        // Заполняется реальным onClickPendingIntent-шаблоном из GojiWidgetProvider
        // (setPendingIntentTemplate) — сам по себе этот Intent никуда не отправляется.
        views.setOnClickFillInIntent(R.id.item_root, Intent().putExtra(GojiWidgetActionReceiver.EXTRA_NODE_ID, node.id))
        return views
    }
}
