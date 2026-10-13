package xyz.gojihub.vpn.vpn

import android.content.Context
import android.content.Intent
import android.net.VpnService
import androidx.core.content.ContextCompat
import xyz.gojihub.vpn.subscription.VlessNode
import xyz.gojihub.vpn.util.stripLeadingFlag

/**
 * Общие команды GodjiVpnService для "внешних" точек входа — плитки в шторке, ярлыка на иконке,
 * виджета, уведомлений. Одна и та же сборка Intent'а раньше повторялась в нескольких местах.
 */
object VpnLauncher {

    /** true — системное разрешение на VPN уже выдано и туннель можно поднять без Activity. */
    fun hasVpnPermission(context: Context): Boolean = VpnService.prepare(context) == null

    fun nodeLabel(node: VlessNode): String = node.geo?.country ?: stripLeadingFlag(node.name)

    /** @return false, если запустить сервис не удалось (например, ограничение Android на
     *  старт foreground-сервиса из фона) — вызывающий тогда открывает приложение. */
    fun connect(context: Context, payload: String, label: String): Boolean = runCatching {
        val intent = Intent(context, GodjiVpnService::class.java).apply {
            action = GodjiVpnService.ACTION_CONNECT
            putExtra(GodjiVpnService.EXTRA_VLESS_LINK, payload)
            putExtra(GodjiVpnService.EXTRA_NODE_LABEL, label)
        }
        ContextCompat.startForegroundService(context, intent)
        true
    }.getOrDefault(false)

    fun connect(context: Context, node: VlessNode): Boolean = connect(context, node.connectPayload, nodeLabel(node))

    fun disconnect(context: Context): Boolean = runCatching {
        context.startService(Intent(context, GodjiVpnService::class.java).setAction(GodjiVpnService.ACTION_DISCONNECT))
        true
    }.getOrDefault(false)
}
