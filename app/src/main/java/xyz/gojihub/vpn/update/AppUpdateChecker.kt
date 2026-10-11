package xyz.gojihub.vpn.update

import android.util.Log
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import org.json.JSONObject
import xyz.gojihub.vpn.BuildConfig
import java.util.concurrent.TimeUnit

data class UpdateInfo(val version: String, val changelog: String, val apkUrl: String)

/**
 * Проверяет, вышла ли новая версия. Основной источник — манифест latest.json на зеркалах нашего
 * сайта загрузки ([DownloadMirrors]): они открываются в РФ без VPN, а GitHub там часто тормозит
 * или не отвечает. Если ни одно зеркало не ответило, проверяем последний релиз
 * в публичном репозитории goji-app/goji-vpn-android, как раньше. GitHub REST API отдаёт данные
 * без авторизации, но требует непустой User-Agent — без него отвечает 403.
 */
object AppUpdateChecker {
    private const val TAG = "GodjiUpdate"
    private const val RELEASES_URL = "https://api.github.com/repos/goji-app/goji-vpn-android/releases/latest"

    private val client = OkHttpClient.Builder()
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(20, TimeUnit.SECONDS)
        .build()

    /** null — либо ошибка сети/API, либо установленная версия уже последняя.
     *  withContext(IO) обязателен именно здесь, а не полагается на диспетчер вызывающего —
     *  client.newCall(...).execute() блокирующий, а SettingsViewModel.checkForUpdate() запускает
     *  эту suspend-функцию через обычный viewModelScope.launch (Dispatchers.Main.immediate по
     *  умолчанию). Без этого ручная проверка по кнопке "Проверить обновления" валилась с
     *  NetworkOnMainThreadException, которое тихо проглатывалось runCatching ниже и снаружи
     *  выглядело как "у вас установлена последняя версия" даже когда на GitHub давно вышел
     *  новый релиз. */
    suspend fun checkForUpdate(): UpdateInfo? = withContext(Dispatchers.IO) {
        val latest = DownloadMirrors.SITES.firstNotNullOfOrNull { fetchFromSite(it) }
            ?: fetchFromGitHub()
            ?: return@withContext null
        latest.takeIf { isNewer(it.version, BuildConfig.VERSION_NAME) }
    }

    /** Последняя версия по манифесту зеркала [site]; null — зеркало недоступно или манифест битый.
     *  Пути файлов в манифесте относительные: APK качаем с того же зеркала, что ответило. */
    private fun fetchFromSite(site: String): UpdateInfo? = fetchJson("$site/latest.json", accept = "application/json") { json ->
        val android = json.optJSONObject("android") ?: return@fetchJson null
        val version = android.optString("version")
        val files = android.optJSONArray("files") ?: return@fetchJson null
        val apkPath = (0 until files.length())
            .mapNotNull { files.optJSONObject(it) }
            .firstOrNull { it.optString("kind") == "apk" }
            ?.optString("path")
        if (version.isBlank() || apkPath.isNullOrBlank() || apkPath.contains("..")) return@fetchJson null
        UpdateInfo(version = version, changelog = android.optString("notes"), apkUrl = "$site/${apkPath.trimStart('/')}")
    }

    /** Запасной путь — последний релиз на GitHub. Теги релизов — vX.Y.Z, префикс убираем. */
    private fun fetchFromGitHub(): UpdateInfo? = fetchJson(RELEASES_URL, accept = "application/vnd.github+json") { json ->
        val version = json.optString("tag_name").removePrefix("v")
        val assets = json.optJSONArray("assets") ?: return@fetchJson null
        val apkUrl = (0 until assets.length())
            .mapNotNull { assets.optJSONObject(it) }
            .firstOrNull { it.optString("name").endsWith(".apk", ignoreCase = true) }
            ?.optString("browser_download_url")
        if (version.isBlank() || apkUrl.isNullOrBlank()) return@fetchJson null
        UpdateInfo(
            version = version,
            changelog = json.optString("body").ifBlank { json.optString("name") },
            apkUrl = apkUrl
        )
    }

    /** Блокирующий GET + разбор JSON; любая ошибка сети или формата — null с записью в лог. */
    private fun fetchJson(url: String, accept: String, parse: (JSONObject) -> UpdateInfo?): UpdateInfo? =
        runCatching {
            val request = Request.Builder()
                .url(url)
                .header("User-Agent", "Goji/${BuildConfig.VERSION_NAME}/Android")
                .header("Accept", accept)
                .build()
            client.newCall(request).execute().use { response ->
                if (!response.isSuccessful) return@use null
                val body = response.body?.string() ?: return@use null
                parse(JSONObject(body))
            }
        }.onFailure { Log.w(TAG, "update check via $url failed", it) }.getOrNull()

    /** Посегментное сравнение вида "1.0.29" > "1.0.28" — без поддержки суффиксов
     *  ("-beta" и т.п.), в тегах этого проекта их не бывает. */
    fun isNewer(remote: String, current: String): Boolean {
        val r = remote.split(".").map { it.toIntOrNull() ?: 0 }
        val c = current.split(".").map { it.toIntOrNull() ?: 0 }
        for (i in 0 until maxOf(r.size, c.size)) {
            val rv = r.getOrElse(i) { 0 }
            val cv = c.getOrElse(i) { 0 }
            if (rv != cv) return rv > cv
        }
        return false
    }
}
