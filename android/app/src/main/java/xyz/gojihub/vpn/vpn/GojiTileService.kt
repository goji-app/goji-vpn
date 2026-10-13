package xyz.gojihub.vpn.vpn

import android.annotation.SuppressLint
import android.app.PendingIntent
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.graphics.drawable.Icon
import android.os.Build
import android.service.quicksettings.Tile
import android.service.quicksettings.TileService
import xyz.gojihub.vpn.MainActivity
import xyz.gojihub.vpn.R
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.util.stripLeadingFlag
import xyz.gojihub.vpn.widget.widgetEntryPoint

/**
 * Плитка "Goji VPN" в шторке быстрых настроек: одно касание — подключиться к выбранному узлу
 * или отключиться. Если системное разрешение на VPN ещё не выдано (или Android не дал поднять
 * сервис из фона) — открывает приложение, где "Включить" корректно запросит всё сам.
 *
 * Состояние плитки обновляется и при открытии шторки (onStartListening), и сразу при смене
 * состояния туннеля — VpnStateObserver зовёт [requestUpdate].
 */
class GojiTileService : TileService() {

    override fun onStartListening() {
        super.onStartListening()
        render()
    }

    override fun onClick() {
        super.onClick()
        val busy = GodjiVpnService.isRunning.value || GodjiVpnService.isConnecting.value
        if (busy) {
            VpnLauncher.disconnect(this)
        } else {
            val node = widgetEntryPoint().subscriptionRepository().selectedNode()
            val started = node != null && VpnLauncher.hasVpnPermission(this) && VpnLauncher.connect(this, node)
            if (!started) {
                openApp()
                return
            }
        }
        render()
    }

    private fun render() {
        val tile = qsTile ?: return
        val running = GodjiVpnService.isRunning.value
        val connecting = GodjiVpnService.isConnecting.value
        tile.state = if (running || connecting) Tile.STATE_ACTIVE else Tile.STATE_INACTIVE
        tile.label = "Goji VPN"
        tile.icon = Icon.createWithResource(this, R.drawable.ic_vpn_notification)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            val node = widgetEntryPoint().subscriptionRepository().selectedNode()
            val nodeName = node?.let { it.geo?.displayName(Loc.lang) ?: stripLeadingFlag(it.name) }
            tile.subtitle = when {
                connecting -> Loc.f.tileConnecting
                running -> nodeName ?: Loc.f.tileOn
                else -> Loc.f.tileOff
            }
        }
        tile.updateTile()
    }

    @SuppressLint("StartActivityAndCollapseDeprecated")
    private fun openApp() {
        val intent = Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            startActivityAndCollapse(PendingIntent.getActivity(this, 0, intent, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT))
        } else {
            @Suppress("DEPRECATION")
            startActivityAndCollapse(intent)
        }
    }

    companion object {
        fun requestUpdate(context: Context) {
            runCatching { requestListeningState(context, ComponentName(context, GojiTileService::class.java)) }
        }
    }
}
