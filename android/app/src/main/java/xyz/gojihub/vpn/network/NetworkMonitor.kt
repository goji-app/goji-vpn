package xyz.gojihub.vpn.network

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.telephony.PhoneStateListener
import android.telephony.ServiceState
import android.telephony.TelephonyManager
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import javax.inject.Inject
import javax.inject.Singleton

enum class NetState { WIFI, CELLULAR, JAMMED }

/**
 * Реальное отслеживание сети: Wi-Fi vs мобильная vs "глушение" (сотовая сеть потеряла
 * сервис). Специально используем LISTEN_SERVICE_STATE, а не LISTEN_SIGNAL_STRENGTHS —
 * первое не требует READ_PHONE_STATE, второе требует. Живёт всё время процесса приложения
 * как синглтон — отдельного stop() не нужно.
 */
@Singleton
class NetworkMonitor @Inject constructor(@ApplicationContext context: Context) {

    private val _state = MutableStateFlow(NetState.CELLULAR)
    val state: StateFlow<NetState> = _state.asStateFlow()

    private var hasWifi = false
    private var hasCellular = false
    private var cellularInService = true

    private fun recompute() {
        _state.value = when {
            hasWifi -> NetState.WIFI
            hasCellular && !cellularInService -> NetState.JAMMED
            hasCellular -> NetState.CELLULAR
            else -> NetState.JAMMED
        }
    }

    private val networkCallback = object : ConnectivityManager.NetworkCallback() {
        override fun onCapabilitiesChanged(network: Network, caps: NetworkCapabilities) {
            hasWifi = caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)
            hasCellular = caps.hasTransport(NetworkCapabilities.TRANSPORT_CELLULAR)
            recompute()
        }

        override fun onLost(network: Network) {
            hasWifi = false
            hasCellular = false
            recompute()
        }
    }

    @Suppress("DEPRECATION", "OVERRIDE_DEPRECATION")
    private val phoneStateListener = object : PhoneStateListener() {
        override fun onServiceStateChanged(serviceState: ServiceState) {
            cellularInService = serviceState.state == ServiceState.STATE_IN_SERVICE
            recompute()
        }
    }

    init {
        runCatching {
            val cm = context.getSystemService(ConnectivityManager::class.java)
            cm.registerDefaultNetworkCallback(networkCallback)
        }
        runCatching {
            val tm = context.getSystemService(TelephonyManager::class.java)
            @Suppress("DEPRECATION")
            tm.listen(phoneStateListener, PhoneStateListener.LISTEN_SERVICE_STATE)
        }
    }
}
