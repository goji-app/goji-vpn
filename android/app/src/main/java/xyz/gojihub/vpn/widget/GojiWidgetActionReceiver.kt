package xyz.gojihub.vpn.widget

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.net.VpnService
import xyz.gojihub.vpn.MainActivity
import xyz.gojihub.vpn.util.stripLeadingFlag
import xyz.gojihub.vpn.vpn.GodjiVpnService

/**
 * Отдельный от [GojiWidgetProvider] receiver — специально exported=false. Сам провайдер
 * виджета обязан быть exported=true (иначе APPWIDGET_UPDATE от system_server может не
 * доходить, см. манифест), но "выбрать сервер"/"подключиться" — это уже действия с реальным
 * эффектом (меняют текущий узел подписки, поднимают VPN), и через exported=true-компонент
 * их мог бы дёрнуть любой сторонний app, просто отправив подходящий Intent явно на наш
 * компонент. PendingIntent, которые создаёт сам виджет (кнопка/пункт списка), долетают сюда
 * нормально независимо от exported — это разрешение переносится вместе с самим PendingIntent.
 */
class GojiWidgetActionReceiver : BroadcastReceiver() {

    override fun onReceive(context: Context, intent: Intent) {
        when (intent.action) {
            ACTION_SELECT_NODE -> {
                val nodeId = intent.getStringExtra(EXTRA_NODE_ID) ?: return
                context.widgetEntryPoint().subscriptionRepository().select(nodeId)
                GojiWidgetProvider.refresh(context)
            }
            ACTION_PREV_NODE, ACTION_NEXT_NODE -> {
                // Стрелки ‹ › компактного виджета — соседний узел по кругу. select() сам
                // переподключит туннель, если VPN сейчас включён.
                val repository = context.widgetEntryPoint().subscriptionRepository()
                val nodes = repository.nodes.value
                if (nodes.isEmpty()) return
                val index = nodes.indexOfFirst { it.id == repository.selectedId.value }.coerceAtLeast(0)
                val step = if (intent.action == ACTION_NEXT_NODE) 1 else -1
                repository.select(nodes[(index + step + nodes.size) % nodes.size].id)
                GojiWidgetProvider.refresh(context)
            }
            ACTION_CONNECT -> {
                val repository = context.widgetEntryPoint().subscriptionRepository()
                val node = repository.selectedNode() ?: return
                // Если системное разрешение на VPN уже когда-то выдано — VpnService.prepare()
                // отдаёт null, и можно поднять туннель сразу же, без открытия приложения.
                // Если нет (первый раз/отозвали) — Android обязан показать системный диалог,
                // а без Activity в фоне это невозможно, поэтому просто открываем приложение,
                // как и раньше: там "Включить" сам корректно запросит разрешение.
                if (VpnService.prepare(context) == null) {
                    val connectIntent = Intent(context, GodjiVpnService::class.java).apply {
                        action = GodjiVpnService.ACTION_CONNECT
                        putExtra(GodjiVpnService.EXTRA_VLESS_LINK, node.connectPayload)
                        putExtra(GodjiVpnService.EXTRA_NODE_LABEL, node.geo?.country ?: stripLeadingFlag(node.name))
                    }
                    context.startForegroundService(connectIntent)
                } else {
                    context.startActivity(
                        Intent(context, MainActivity::class.java)
                            .setFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP)
                    )
                }
            }
        }
    }

    companion object {
        const val ACTION_SELECT_NODE = "xyz.gojihub.vpn.widget.SELECT_NODE"
        const val ACTION_CONNECT = "xyz.gojihub.vpn.widget.CONNECT"
        const val ACTION_PREV_NODE = "xyz.gojihub.vpn.widget.PREV_NODE"
        const val ACTION_NEXT_NODE = "xyz.gojihub.vpn.widget.NEXT_NODE"
        const val EXTRA_NODE_ID = "node_id"
    }
}
