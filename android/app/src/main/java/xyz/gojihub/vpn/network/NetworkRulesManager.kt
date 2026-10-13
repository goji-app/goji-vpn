package xyz.gojihub.vpn.network

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import android.net.wifi.WifiInfo
import android.net.wifi.WifiManager
import android.os.Build
import androidx.core.content.ContextCompat
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import xyz.gojihub.vpn.settings.SettingsRepository
import xyz.gojihub.vpn.subscription.SubscriptionRepository
import xyz.gojihub.vpn.util.AppLogger
import xyz.gojihub.vpn.util.LogCategory
import xyz.gojihub.vpn.vpn.GodjiVpnService
import xyz.gojihub.vpn.vpn.VpnLauncher
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Правила сетей (Настройки → Правила Wi-Fi):
 *  - "Доверенные сети": в них VPN не включается автоматически, а при подключении к ним
 *    (если включено "Отключать в доверенных сетях") работающий VPN выключается;
 *  - "Включать в чужих сетях": при подключении к любой НЕ доверенной Wi-Fi VPN поднимается сам.
 *
 * Имя сети (SSID) Android отдаёт только при разрешении на геолокацию (и включённой
 * геолокации) — без него сеть неизвестна: доверенной не считается и VPN не отключается
 * (безопасный вариант), а "включать в чужих" по-прежнему работает, как раньше работало
 * "Автоподключение при Wi-Fi".
 *
 * Правила срабатывают на смену сети, пока жив процесс приложения (открыто оно или работает
 * VPN). Подключить VPN из полностью фонового состояния Android может не дать (ограничение
 * на старт foreground-сервиса из фона) — для этого есть системный "Always-on VPN".
 */
@Singleton
class NetworkRulesManager @Inject constructor(
    @ApplicationContext private val context: Context,
    private val settingsRepository: SettingsRepository,
    private val subscriptionRepository: SubscriptionRepository
) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    /** Текущая Wi-Fi: null — не Wi-Fi; "" — Wi-Fi, но имя недоступно (нет разрешения). */
    private val _currentSsid = MutableStateFlow<String?>(null)
    val currentSsid: StateFlow<String?> = _currentSsid.asStateFlow()

    private var started = false
    private var pendingJob: Job? = null
    private var lastHandled: String? = null

    fun hasLocationPermission(): Boolean =
        ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED

    fun start() {
        if (started) return
        started = true
        val cm = context.getSystemService(ConnectivityManager::class.java) ?: return
        val request = NetworkRequest.Builder().addTransportType(NetworkCapabilities.TRANSPORT_WIFI).build()
        val callback = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            object : ConnectivityManager.NetworkCallback(FLAG_INCLUDE_LOCATION_INFO) {
                override fun onCapabilitiesChanged(network: Network, caps: NetworkCapabilities) = onWifi(network, caps)
                override fun onLost(network: Network) = onWifiLost()
            }
        } else {
            object : ConnectivityManager.NetworkCallback() {
                override fun onCapabilitiesChanged(network: Network, caps: NetworkCapabilities) = onWifi(network, caps)
                override fun onLost(network: Network) = onWifiLost()
            }
        }
        runCatching { cm.registerNetworkCallback(request, callback) }
            .onFailure { AppLogger.e(context, LogCategory.MAIN, TAG, "registerNetworkCallback failed", it) }
    }

    /** Перечитать имя сети — например, сразу после выдачи разрешения на геолокацию. */
    fun refresh() {
        if (_currentSsid.value != null) _currentSsid.value = readSsidLegacy() ?: _currentSsid.value
    }

    private fun onWifi(network: Network, caps: NetworkCapabilities) {
        val ssid = ssidFrom(caps) ?: readSsidLegacy() ?: ""
        _currentSsid.value = ssid
        val key = "$network|$ssid"
        if (key == lastHandled) return
        lastHandled = key
        // Первое событие после старта процесса — это уже бывшая сеть, а не новое подключение:
        // автоподключение по нему не делаем (как и раньше в ConnectViewModel — только на смену
        // сети), иначе VPN сам включался бы при каждом открытии приложения после ручного
        // отключения. Отключение в доверенной сети применяем и здесь.
        val isFirst = !seenFirstEvent
        seenFirstEvent = true
        // Небольшая пауза: сеть только что появилась, система ещё переключает default route.
        pendingJob?.cancel()
        pendingJob = scope.launch {
            delay(1500)
            applyRules(ssid, allowAutoConnect = !isFirst)
        }
    }

    @Volatile private var seenFirstEvent = false

    private fun onWifiLost() {
        _currentSsid.value = null
        lastHandled = null
    }

    private suspend fun applyRules(ssid: String, allowAutoConnect: Boolean) {
        val trusted = ssid.isNotEmpty() && ssid in settingsRepository.trustedSsids.first()
        val running = GodjiVpnService.isRunning.value || GodjiVpnService.isConnecting.value
        if (trusted) {
            if (running && settingsRepository.disconnectOnTrusted.first()) {
                AppLogger.i(context, LogCategory.MAIN, TAG, "Доверенная сеть «$ssid» — отключаю VPN")
                VpnLauncher.disconnect(context)
                xyz.gojihub.vpn.journal.NetworkJournal.log(xyz.gojihub.vpn.journal.NetworkJournal.Kind.RULE_TRUSTED_DISCONNECT, ssid)
            }
            return
        }
        if (allowAutoConnect && !running && settingsRepository.autoConnectOnWifi.first()) {
            val node = subscriptionRepository.selectedNode() ?: return
            if (!VpnLauncher.hasVpnPermission(context)) return
            val ok = VpnLauncher.connect(context, node)
            if (ok) xyz.gojihub.vpn.journal.NetworkJournal.log(xyz.gojihub.vpn.journal.NetworkJournal.Kind.RULE_AUTOCONNECT, ssid)
            AppLogger.i(context, LogCategory.MAIN, TAG, "Чужая сеть «${ssid.ifEmpty { "?" }}» — включаю VPN: ${if (ok) "ok" else "Android не дал запустить из фона"}")
        }
    }

    private fun ssidFrom(caps: NetworkCapabilities): String? {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.Q) return null
        val info = caps.transportInfo as? WifiInfo ?: return null
        return clean(info.ssid)
    }

    @Suppress("DEPRECATION")
    private fun readSsidLegacy(): String? = runCatching {
        val wm = context.applicationContext.getSystemService(WifiManager::class.java) ?: return@runCatching null
        clean(wm.connectionInfo?.ssid)
    }.getOrNull()

    /** WifiInfo отдаёт SSID в кавычках, а без разрешения — "<unknown ssid>". */
    private fun clean(raw: String?): String? {
        if (raw.isNullOrBlank() || raw == WifiManager.UNKNOWN_SSID || raw == "<unknown ssid>") return null
        return raw.removeSurrounding("\"").takeIf { it.isNotBlank() }
    }

    private companion object {
        const val TAG = "GodjiNetRules"
    }
}
