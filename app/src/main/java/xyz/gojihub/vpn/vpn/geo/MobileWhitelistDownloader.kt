package xyz.gojihub.vpn.vpn.geo

import android.content.Context
import okhttp3.OkHttpClient
import xyz.gojihub.vpn.update.DownloadMirrors
import java.io.File
import java.util.concurrent.TimeUnit

/**
 * hxehex/russia-mobile-internet-whitelist — список доменов/IP, живьём собранный
 * сообществом во время вайтлистинга мобильного интернета в РФ (см. MobileWhitelist для
 * формата и точку подстановки в GodjiVpnService.resolveGeoDataRules). В отличие от
 * category-ru (runetfreedom/russia-v2ray-rules-dat, обновляется каждые 6 часов), этот
 * репозиторий пополняется вручную по мере новых замеров — раз в неделю (см.
 * MobileWhitelistRefreshWorker) достаточно, чтобы не отставать от реальных изменений.
 */
object MobileWhitelistDownloader {
    private const val TAG = "GodjiVpn"
    private const val DOMAINS_URL =
        "https://raw.githubusercontent.com/hxehex/russia-mobile-internet-whitelist/main/whitelist.txt"
    private const val CIDR_URL =
        "https://raw.githubusercontent.com/hxehex/russia-mobile-internet-whitelist/main/cidrwhitelist.txt"

    // Оборванная/ошибочная закачка (HTML-страница вместо текста) не должна вытеснять уже
    // рабочую версию — реальные файлы весят по крайней мере несколько КБ.
    private const val MIN_VALID_SIZE = 1_000L

    private val client = OkHttpClient.Builder()
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(30, TimeUnit.SECONDS)
        .build()

    /** [maxAgeMs] — не качать, если последняя успешная проверка была не раньше этого срока
     *  (при старте приложения — раз в неделю; периодический воркер передаёт 0).
     *  0 — проверить в любом случае (условный запрос: без изменений сервер ответит 304). */
    fun refresh(context: Context, maxAgeMs: Long = 0L): Boolean {
        val dir = File(context.filesDir, "geoassets").apply { mkdirs() }
        return listOf(DOMAINS_URL to "mobile_whitelist_domains.txt", CIDR_URL to "mobile_whitelist_cidr.txt").map { (url, name) ->
            val dest = File(dir, name)
            GeoFileDownload.isFresh(dest, maxAgeMs) || GeoFileDownload.download(client, DownloadMirrors.urls("geo/$name") + url, dest, MIN_VALID_SIZE)
        }.all { it }
    }
}
