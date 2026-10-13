package xyz.gojihub.vpn.vpn

import android.content.Intent
import android.net.VpnService
import kotlinx.coroutines.flow.MutableStateFlow
import java.io.IOException
import java.net.InetSocketAddress
import java.net.Proxy
import java.net.ProxySelector
import java.net.SocketAddress
import java.net.URI

/**
 * Реальная реализация этого класса — поднятие системного VPN-туннеля через Xray-core
 * (libXray) и hev-socks5-tunnel, сборка VLESS/Reality-конфига под выбранный узел, обход
 * geoip/geosite-ограничений сборки libXray, обработка "смахивания" статусного уведомления
 * и т.д. — не публикуется в этом репозитории.
 *
 * Здесь оставлена только форма класса (публичные экшены Intent'ов и состояния),
 * необходимая для компиляции остального приложения. Если вы разворачиваете проект у себя —
 * реализуйте establishTunnel()/stopTunnel() самостоятельно поверх libXray
 * (https://github.com/XTLS/libXray) или любого другого движка.
 */
class GodjiVpnService : VpnService() {

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        // Заглушка публичной версии — см. докстринг класса.
        return START_NOT_STICKY
    }

    companion object {
        const val ACTION_CONNECT = "xyz.gojihub.vpn.CONNECT"
        const val ACTION_DISCONNECT = "xyz.gojihub.vpn.DISCONNECT"
        const val ACTION_RETRY = "xyz.gojihub.vpn.RETRY"
        const val EXTRA_VLESS_LINK = "vless_link"
        const val EXTRA_NODE_LABEL = "node_label"
        const val SOCKS_PORT = 10808

        val isRunning = MutableStateFlow(false)
        val isConnecting = MutableStateFlow(false)
        val lastError = MutableStateFlow<String?>(null)
        val actualCountry = MutableStateFlow<String?>(null)
        val connectedSinceMillis = MutableStateFlow<Long?>(null)

        fun tunnelAwareProxySelector(): ProxySelector = object : ProxySelector() {
            override fun select(uri: URI?): List<Proxy> = listOf(
                if (isRunning.value) Proxy(Proxy.Type.SOCKS, InetSocketAddress("127.0.0.1", SOCKS_PORT))
                else Proxy.NO_PROXY
            )
            override fun connectFailed(uri: URI?, sa: SocketAddress?, ioe: IOException?) {}
        }
    }
}
