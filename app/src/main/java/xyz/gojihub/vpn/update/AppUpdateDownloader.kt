package xyz.gojihub.vpn.update

import android.app.DownloadManager
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Environment
import android.util.Log
import androidx.core.content.FileProvider
import xyz.gojihub.vpn.util.AppLogger
import xyz.gojihub.vpn.util.LogCategory
import java.io.File

private const val PREFS_NAME = "godji_update_download"
private const val KEY_DOWNLOAD_ID = "download_id"
private const val KEY_PENDING_VERSION = "pending_version"
private const val TAG = "GodjiUpdate"

data class DownloadStatus(val status: Int, val percent: Int)

/** Загрузка APK-обновления через системный DownloadManager (переживает сворачивание и даже
 *  убийство процесса приложения, умеет ретраи — писать это заново поверх голого OkHttp не
 *  было смысла) и установка через системный экран, скачанный файл отдаётся ему через
 *  FileProvider (Android 7+ запрещает передавать file://-URI между приложениями). */
object AppUpdateDownloader {

    fun fileNameFor(version: String) = "Goji-$version.apk"

    fun startDownload(context: Context, apkUrl: String, version: String): Long {
        val fileName = fileNameFor(version)
        // Стереть возможный недокачанный файл от прошлой попытки с тем же именем — иначе
        // DownloadManager иногда дописывает поверх старого частичного файла молча.
        File(context.getExternalFilesDir(Environment.DIRECTORY_DOWNLOADS), fileName)
            .takeIf { it.exists() }?.delete()

        val request = DownloadManager.Request(Uri.parse(apkUrl))
            .setTitle("Goji VPN $version")
            .setDescription("Загрузка обновления")
            .setDestinationInExternalFilesDir(context, Environment.DIRECTORY_DOWNLOADS, fileName)
            .setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED)
            .setMimeType("application/vnd.android.package-archive")
        val id = context.getSystemService(DownloadManager::class.java).enqueue(request)
        Log.d(TAG, "startDownload: enqueued id=$id url=$apkUrl -> $fileName")
        context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE).edit()
            .putLong(KEY_DOWNLOAD_ID, id)
            .putString(KEY_PENDING_VERSION, version)
            .apply()
        return id
    }

    fun queryStatus(context: Context, downloadId: Long): DownloadStatus? {
        val dm = context.getSystemService(DownloadManager::class.java)
        dm.query(DownloadManager.Query().setFilterById(downloadId))?.use { c ->
            if (!c.moveToFirst()) {
                Log.d(TAG, "queryStatus: id=$downloadId — нет строки в DownloadManager")
                return null
            }
            val status = c.getInt(c.getColumnIndexOrThrow(DownloadManager.COLUMN_STATUS))
            val total = c.getLong(c.getColumnIndexOrThrow(DownloadManager.COLUMN_TOTAL_SIZE_BYTES))
            val downloaded = c.getLong(c.getColumnIndexOrThrow(DownloadManager.COLUMN_BYTES_DOWNLOADED_SO_FAR))
            val percent = if (total > 0) ((downloaded * 100) / total).toInt() else 0
            // reason — код ошибки/причины паузы (см. DownloadManager.ERROR_*/PAUSED_*), не имеет
            // смысла для STATUS_RUNNING/SUCCESSFUL, но критичен для диагностики зависаний/ошибок.
            val reason = c.getInt(c.getColumnIndexOrThrow(DownloadManager.COLUMN_REASON))
            Log.d(TAG, "queryStatus: id=$downloadId status=$status reason=$reason downloaded=$downloaded/$total ($percent%)")
            return DownloadStatus(status, percent)
        }
        return null
    }

    fun installDownloaded(context: Context, version: String) {
        val file = File(context.getExternalFilesDir(Environment.DIRECTORY_DOWNLOADS), fileNameFor(version))
        if (!file.exists()) {
            Log.e(TAG, "installDownloaded: файл не найден — $file")
            AppLogger.e(context, LogCategory.MAIN, TAG, "installDownloaded: файл не найден — $file")
            return
        }
        Log.d(TAG, "installDownloaded: запускаю установщик для $file (${file.length()} байт)")
        val uri = FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", file)
        val intent = Intent(Intent.ACTION_VIEW).apply {
            setDataAndType(uri, "application/vnd.android.package-archive")
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        runCatching { context.startActivity(intent) }
            .onFailure {
                Log.e(TAG, "installDownloaded: не удалось открыть установщик", it)
                AppLogger.e(context, LogCategory.MAIN, TAG, "installDownloaded: не удалось открыть установщик", it)
            }
    }
}

/** DownloadManager переживает смерть процесса приложения — регистрация только в манифесте,
 *  не динамическая (см. AndroidManifest.xml). Как только наша загрузка (сверяем по
 *  download_id, сохранённому в startDownload — DOWNLOAD_COMPLETE приходит для ЛЮБой закачки
 *  через DownloadManager в системе, не только нашей) завершилась успешно, сразу открываем
 *  системный экран установки — пользователю остаётся только подтвердить, отдельно заходить
 *  в приложение не нужно. */
class UpdateDownloadReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action != DownloadManager.ACTION_DOWNLOAD_COMPLETE) return
        val id = intent.getLongExtra(DownloadManager.EXTRA_DOWNLOAD_ID, -1L)
        Log.d(TAG, "UpdateDownloadReceiver: получен DOWNLOAD_COMPLETE, id=$id")
        if (id == -1L) return
        val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        val expectedId = prefs.getLong(KEY_DOWNLOAD_ID, -1L)
        val version = prefs.getString(KEY_PENDING_VERSION, null)
        if (id != expectedId || version == null) {
            Log.d(TAG, "UpdateDownloadReceiver: id=$id не совпадает с ожидаемым $expectedId (version=$version) — не наша загрузка, игнорирую")
            return
        }
        val status = AppUpdateDownloader.queryStatus(context, id) ?: return
        if (status.status == DownloadManager.STATUS_SUCCESSFUL) {
            Log.d(TAG, "UpdateDownloadReceiver: загрузка $version успешна, запускаю установку")
            AppUpdateDownloader.installDownloaded(context, version)
        } else {
            Log.w(TAG, "UpdateDownloadReceiver: загрузка $version завершилась неуспешно, status=${status.status}")
            AppLogger.e(context, LogCategory.MAIN, TAG, "Загрузка обновления $version завершилась неуспешно (status=${status.status})")
        }
    }
}
