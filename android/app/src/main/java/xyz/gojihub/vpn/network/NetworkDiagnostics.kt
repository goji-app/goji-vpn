package xyz.gojihub.vpn.network

import android.content.Context
import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import org.json.JSONObject
import xyz.gojihub.vpn.util.AppLogger
import xyz.gojihub.vpn.util.LogCategory
import xyz.gojihub.vpn.vpn.GodjiVpnService
import java.net.InetSocketAddress
import java.net.Proxy
import java.util.concurrent.TimeUnit
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Проверка утечек одной кнопкой (Настройки → Безопасность).
 *
 * Приложение само исключено из собственного VPN-туннеля (см. GodjiVpnService.establishTunnel),
 * поэтому "как видят интернет остальные приложения" проверяем через локальный SOCKS самого
 * Xray (127.0.0.1:SOCKS_PORT) — тот же путь, по которому уходит их трафик, — а "как без VPN"
 * через обычный прямой запрос:
 *  - IP: публичный IP через туннель не должен совпадать с настоящим (прямым);
 *  - DNS: edns.ip-api.com отдаёт каждому запросу уникальный поддомен и сообщает, какой
 *    DNS-резолвер его разрешил. Если это резолвер провайдера (тот же ISP, что у прямого IP)
 *    или той же страны, что и настоящий IP, при другой стране выхода — DNS-запросы уходят
 *    мимо VPN, провайдер видит, какие сайты открываются.
 */
@Singleton
class NetworkDiagnostics @Inject constructor(
    @ApplicationContext private val appContext: Context
) {
    data class IpInfo(val ip: String, val country: String?, val isp: String?)
    data class DnsInfo(val ip: String, val country: String?, val isp: String?)

    enum class Verdict { SAFE, LEAK, VPN_OFF, ERROR }

    data class Report(
        val verdict: Verdict,
        val realIp: IpInfo?,
        val vpnIp: IpInfo?,
        val dns: DnsInfo?,
        val ipLeak: Boolean,
        val dnsLeak: Boolean,
        val vpnDnsServers: List<String>,
        /** true — резолвер у того же провайдера, что и настоящий IP; false — просто в стране пользователя. */
        val dnsViaIsp: Boolean = false
    )

    private val directClient = OkHttpClient.Builder()
        .proxy(Proxy.NO_PROXY)
        .connectTimeout(8, TimeUnit.SECONDS)
        .readTimeout(8, TimeUnit.SECONDS)
        .build()

    private val tunnelClient = OkHttpClient.Builder()
        .proxy(Proxy(Proxy.Type.SOCKS, InetSocketAddress("127.0.0.1", GodjiVpnService.SOCKS_PORT)))
        .connectTimeout(10, TimeUnit.SECONDS)
        .readTimeout(10, TimeUnit.SECONDS)
        .build()

    suspend fun check(): Report = withContext(Dispatchers.IO) {
        val vpnOn = GodjiVpnService.isRunning.value
        coroutineScope {
            val real = async { fetchIp(directClient) }
            if (!vpnOn) {
                return@coroutineScope Report(Verdict.VPN_OFF, real.await(), null, null, false, false, emptyList())
            }
            val vpn = async { fetchIp(tunnelClient) }
            val dns = async { fetchDns(tunnelClient) }
            val realIp = real.await()
            val vpnIp = vpn.await()
            val dnsInfo = dns.await()
            val vpnDns = readVpnDnsServers()
            // "Всё защищено" без проверенного DNS было бы голословным — тогда честно "не удалось".
            if (vpnIp == null || dnsInfo == null) {
                return@coroutineScope Report(Verdict.ERROR, realIp, vpnIp, dnsInfo, false, false, vpnDns)
            }
            val ipLeak = realIp != null && realIp.ip == vpnIp.ip
            val dnsLeak = dnsInfo != null && realIp != null && isDnsLeak(dnsInfo, realIp, vpnIp)
            val dnsViaIsp = dnsLeak && sameIsp(dnsInfo!!, realIp!!)
            Report(
                verdict = if (ipLeak || dnsLeak) Verdict.LEAK else Verdict.SAFE,
                realIp = realIp, vpnIp = vpnIp, dns = dnsInfo,
                ipLeak = ipLeak, dnsLeak = dnsLeak, dnsViaIsp = dnsViaIsp, vpnDnsServers = vpnDns
            )
        }
    }

    /** Утечка DNS: резолвер у того же провайдера, что и настоящий IP, — или в стране
     *  настоящего IP, когда выход VPN в другой стране (для узла в той же стране страна
     *  ничего не говорит, остаётся только сравнение провайдера). */
    private fun sameIsp(dns: DnsInfo, real: IpInfo): Boolean =
        !dns.isp.isNullOrBlank() && !real.isp.isNullOrBlank() && normalizeIsp(dns.isp) == normalizeIsp(real.isp)

    private fun isDnsLeak(dns: DnsInfo, real: IpInfo, vpn: IpInfo): Boolean {
        val sameIsp = sameIsp(dns, real)
        val sameCountryAsReal = dns.country != null && real.country != null &&
            dns.country.equals(real.country, ignoreCase = true) &&
            !real.country.equals(vpn.country, ignoreCase = true)
        return sameIsp || sameCountryAsReal
    }

    private fun normalizeIsp(s: String): String =
        s.lowercase().replace(Regex("""\b(llc|ltd|ooo|jsc|pjsc|inc|oao|zao|gmbh|limited|company)\b"""), "")
            .replace(Regex("""[^\p{L}\p{N}]"""), "")

    /** Два независимых источника: ipwho.is у части провайдеров недоступен напрямую. */
    private fun fetchIp(client: OkHttpClient): IpInfo? = fetchIpWhoIs(client) ?: fetchIpApi(client)

    private fun fetchIpApi(client: OkHttpClient): IpInfo? = runCatching {
        client.newCall(Request.Builder().url("http://ip-api.com/json?fields=status,query,country,isp").build()).execute().use { response ->
            if (!response.isSuccessful) return@use null
            val json = JSONObject(response.body?.string().orEmpty())
            if (json.optString("status") != "success") return@use null
            val ip = json.optString("query").takeIf { it.isNotBlank() } ?: return@use null
            IpInfo(
                ip = ip,
                country = json.optString("country").takeIf { it.isNotBlank() }?.removePrefix("The "),
                isp = json.optString("isp").takeIf { it.isNotBlank() }
            )
        }
    }.onFailure { AppLogger.e(appContext, LogCategory.SUBSCRIPTION, TAG, "fetchIpApi failed", it) }.getOrNull()

    private fun fetchIpWhoIs(client: OkHttpClient): IpInfo? = runCatching {
        client.newCall(Request.Builder().url("https://ipwho.is/").build()).execute().use { response ->
            if (!response.isSuccessful) return@use null
            val json = JSONObject(response.body?.string().orEmpty())
            if (!json.optBoolean("success", true)) return@use null
            val ip = json.optString("ip").takeIf { it.isNotBlank() } ?: return@use null
            IpInfo(
                ip = ip,
                country = json.optString("country").takeIf { it.isNotBlank() },
                isp = json.optJSONObject("connection")?.optString("isp")?.takeIf { it.isNotBlank() }
            )
        }
    }.onFailure { AppLogger.e(appContext, LogCategory.SUBSCRIPTION, TAG, "fetchIp failed", it) }.getOrNull()

    /** Резолвер — тем же путём, что у обычных приложений в туннеле: они шлют DNS по UDP на
     *  адрес DNS VPN-сети (1.1.1.1, см. Builder в GodjiVpnService), и пакет уходит в Xray.
     *  Повторяем это через SOCKS5 UDP ASSOCIATE самого Xray: берём у edns.ip-api.com
     *  случайный поддомен (редирект), разрешаем его UDP-запросом к 1.1.1.1 через туннель и
     *  спрашиваем ip-api, какой резолвер пришёл за этим именем. Если UDP-путь не сработал —
     *  запасной вариант: домен целиком через SOCKS (разрешает внутренний DNS Xray). */
    private fun fetchDns(client: OkHttpClient): DnsInfo? =
        runCatching { fetchDnsViaTunnelUdp() }
            .onFailure { AppLogger.e(appContext, LogCategory.SUBSCRIPTION, TAG, "fetchDnsViaTunnelUdp failed", it) }
            .getOrNull() ?: fetchDnsViaSocksHost(client)

    private fun fetchDnsViaTunnelUdp(): DnsInfo? {
        val noRedirect = tunnelClient.newBuilder().followRedirects(false).build()
        val location = noRedirect.newCall(Request.Builder().url("http://edns.ip-api.com/json").build()).execute().use {
            it.header("Location")
        } ?: return null
        val host = java.net.URI(location).host ?: return null
        val ip = SocksUdpDns.resolveA(host, "1.1.1.1", GodjiVpnService.SOCKS_PORT) ?: return null
        val request = Request.Builder().url("http://$ip/json").header("Host", host).build()
        return tunnelClient.newCall(request).execute().use { parseEdns(it) }
    }

    /** edns.ip-api.com сам редиректит на случайный поддомен — кэш резолвера не мешает. */
    private fun fetchDnsViaSocksHost(client: OkHttpClient): DnsInfo? = runCatching {
        client.newCall(Request.Builder().url("http://edns.ip-api.com/json").build()).execute().use { parseEdns(it) }
    }.onFailure { AppLogger.e(appContext, LogCategory.SUBSCRIPTION, TAG, "fetchDns failed", it) }.getOrNull()

    private fun parseEdns(response: okhttp3.Response): DnsInfo? {
        if (!response.isSuccessful) return null
        val dns = JSONObject(response.body?.string().orEmpty()).optJSONObject("dns") ?: return null
        val ip = dns.optString("ip").takeIf { it.isNotBlank() } ?: return null
        // geo приходит строкой "Страна - Провайдер".
        val geo = dns.optString("geo")
        val country = geo.substringBefore(" - ").trim().removePrefix("The ").takeIf { it.isNotBlank() }
        val isp = geo.substringAfter(" - ", "").trim().takeIf { it.isNotBlank() }
        return DnsInfo(ip, country, isp)
    }

    private fun readVpnDnsServers(): List<String> = runCatching {
        val cm = appContext.getSystemService(ConnectivityManager::class.java) ?: return@runCatching emptyList()
        @Suppress("DEPRECATION")
        val vpnNetwork = cm.allNetworks.firstOrNull { cm.getNetworkCapabilities(it)?.hasTransport(NetworkCapabilities.TRANSPORT_VPN) == true }
            ?: return@runCatching emptyList()
        cm.getLinkProperties(vpnNetwork)?.dnsServers?.mapNotNull { it.hostAddress }.orEmpty()
    }.getOrElse { emptyList() }

    private companion object {
        const val TAG = "GodjiDiag"
    }
}
