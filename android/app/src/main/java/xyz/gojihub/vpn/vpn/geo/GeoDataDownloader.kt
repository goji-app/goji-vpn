package xyz.gojihub.vpn.vpn.geo

import android.content.Context
import okhttp3.OkHttpClient
import xyz.gojihub.vpn.update.DownloadMirrors
import java.io.File
import java.util.concurrent.TimeUnit

/**
 * geoip.dat/geosite.dat, встроенные в assets, — это обычный (не российский) набор, с которым
 * приложение когда-то собиралось, и Xray-core их всё равно никогда не открывал напрямую (см.
 * GeoAssets.kt — переменная окружения до Go-рантайма не доходит). Теперь эти файлы читает сам
 * GeoDataParser, в обход Xray, поэтому их актуальность и охват российских сервисов/блокировок —
 * целиком на нас. runetfreedom/russia-v2ray-rules-dat — тот же протобуф-формат (совместим с
 * GeoDataParser), с российской спецификой, обновляется на их стороне каждые 6 часов; здесь
 * просто периодически подтягиваем свежую версию (см. GeoDataRefreshWorker) поверх тех, что
 * скопированы из assets при первом запуске.
 */
object GeoDataDownloader {
    private const val TAG = "GodjiVpn"
    private const val GEOIP_URL =
        "https://raw.githubusercontent.com/runetfreedom/russia-v2ray-rules-dat/release/geoip.dat"
    private const val GEOSITE_URL =
        "https://raw.githubusercontent.com/runetfreedom/russia-v2ray-rules-dat/release/geosite.dat"

    // Оборванная на середине или ошибочная (HTML-страница вместо .dat) закачка не должна
    // вытеснять уже рабочую версию — настоящие geoip.dat/geosite.dat весят от сотен КБ.
    private const val MIN_VALID_SIZE = 50_000L

    private val client = OkHttpClient.Builder()
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(30, TimeUnit.SECONDS)
        .build()

    /** [maxAgeMs] — не качать, если последняя успешная проверка была не раньше этого срока
     *  (при старте приложения — раз в сутки; периодический воркер передаёт 0).
     *  0 — проверить в любом случае (условный запрос: без изменений сервер ответит 304). */
    fun refresh(context: Context, maxAgeMs: Long = 0L): Boolean {
        val dir = File(context.filesDir, "geoassets").apply { mkdirs() }
        return listOf(GEOIP_URL to "geoip.dat", GEOSITE_URL to "geosite.dat").map { (url, name) ->
            val dest = File(dir, name)
            GeoFileDownload.isFresh(dest, maxAgeMs) || GeoFileDownload.download(client, DownloadMirrors.urls("geo/$name") + url, dest, MIN_VALID_SIZE)
        }.all { it }
    }
}
