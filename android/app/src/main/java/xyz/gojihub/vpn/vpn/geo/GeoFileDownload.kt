package xyz.gojihub.vpn.vpn.geo

import android.util.Log
import okhttp3.OkHttpClient
import okhttp3.Request
import java.io.File

/**
 * Общая закачка файлов геоданных (geoip/geosite, белые списки мобильного интернета):
 *  - потоком во временный файл, без загрузки всего ответа в память (geoip.dat — ~23 МБ);
 *  - сначала с зеркал нашего сайта загрузки (там копия, обновляемая каждые 6 часов), при их
 *    недоступности — с GitHub;
 *  - условным запросом по ETag: если файл на сервере не менялся, сервер отвечает 304 без тела;
 *  - с отметкой времени последней успешной проверки (<файл>.checked) — по ней [isFresh]
 *    решает, нужна ли закачка вообще. Именно отметка, а не mtime файла: копия из assets при
 *    первом запуске имеет свежий mtime, но не российский набор, и её нужно заменить.
 */
internal object GeoFileDownload {
    private const val TAG = "GodjiVpn"

    fun isFresh(dest: File, maxAgeMs: Long): Boolean {
        val stamp = File(dest.path + ".checked").takeIf { it.exists() }?.readText()?.trim()?.toLongOrNull() ?: return false
        return dest.exists() && System.currentTimeMillis() - stamp in 0 until maxAgeMs
    }

    /** Пробует адреса по очереди (зеркала сайта, затем GitHub) до первой удачной закачки. */
    fun download(client: OkHttpClient, urls: List<String>, dest: File, minValidSize: Long): Boolean =
        urls.any { download(client, it, dest, minValidSize) }

    fun download(client: OkHttpClient, url: String, dest: File, minValidSize: Long): Boolean {
        val etagFile = File(dest.path + ".etag")
        val tmp = File(dest.parentFile, "${dest.name}.tmp")
        return try {
            val request = Request.Builder().url(url).apply {
                val etag = etagFile.takeIf { it.exists() && dest.exists() }?.readText()?.trim()
                if (!etag.isNullOrEmpty()) header("If-None-Match", etag)
            }.build()
            client.newCall(request).execute().use { response ->
                if (response.code == 304) {
                    markChecked(dest)
                    Log.d(TAG, "GeoFileDownload: $url не изменился (304)")
                    return true
                }
                if (!response.isSuccessful) {
                    Log.w(TAG, "GeoFileDownload: HTTP ${response.code} для $url")
                    return false
                }
                val body = response.body ?: return false
                val written = body.byteStream().use { input -> tmp.outputStream().use { input.copyTo(it, 64 * 1024) } }
                // Оборванная закачка или HTML-страница вместо файла не должна вытеснять рабочую версию.
                if (written < minValidSize) {
                    Log.w(TAG, "GeoFileDownload: подозрительно маленький ответ ($url, $written байт) — отброшен")
                    tmp.delete()
                    return false
                }
                if (!tmp.renameTo(dest)) {
                    // Копирование поверх рабочего файла не атомарно: смерть процесса посреди
                    // него оставила бы обрезанный geoip.dat вместо рабочей версии. Оставляем
                    // рабочую — недокачанный tmp удаляем, воркер повторит при следующей проверке.
                    Log.w(TAG, "GeoFileDownload: rename $url -> ${dest.name} не удался, оставляю прежнюю версию")
                    tmp.delete()
                    return false
                }
                response.header("ETag")?.let { etagFile.writeText(it) } ?: etagFile.delete()
                markChecked(dest)
                Log.d(TAG, "GeoFileDownload: $url -> ${dest.name} ($written байт)")
                true
            }
        } catch (t: Throwable) {
            Log.w(TAG, "GeoFileDownload: скачивание $url провалилось", t)
            tmp.delete()
            false
        }
    }

    private fun markChecked(dest: File) {
        runCatching { File(dest.path + ".checked").writeText(System.currentTimeMillis().toString()) }
    }
}
