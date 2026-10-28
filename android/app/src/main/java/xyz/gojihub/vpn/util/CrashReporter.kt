package xyz.gojihub.vpn.util

import android.app.ActivityManager
import android.app.ApplicationExitInfo
import android.content.Context
import android.os.Build
import android.util.Log
import xyz.gojihub.vpn.BuildConfig
import java.io.File
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Порт Windows CrashReporter (2026-10-26): пользователи логи не собирают, поэтому при запуске
 * приложения собираем всё, что осталось от сбоев с прошлого запуска, и предлагаем отправить это
 * в поддержку одним нажатием (MainActivity):
 *  - необработанное исключение Java/Kotlin — пишем сами перед падением (install);
 *  - с Android 11 — системная история завершений процесса: падения в машинном коде (ядро Xray,
 *    hev-socks5-tunnel) и зависания (ANR), которые изнутри приложения не поймать.
 */
object CrashReporter {
    private const val PREFS = "crash_reporter"
    private const val KEY_LAST_START = "last_start"
    private const val MAX_EXIT_REASONS = 3
    private const val MAX_TRACE_CHARS = 1500
    private const val MAX_REPORT_CHARS = 4000

    private fun javaCrashFile(context: Context) = File(context.filesDir, "crash/java.txt")

    /** Раньше всего в Application.onCreate — в каждом процессе приложения. */
    fun install(context: Context) {
        val previous = Thread.getDefaultUncaughtExceptionHandler()
        Thread.setDefaultUncaughtExceptionHandler { thread, error ->
            runCatching {
                val file = javaCrashFile(context)
                file.parentFile?.mkdirs()
                file.appendText("${stamp(System.currentTimeMillis())} поток ${thread.name}\n${Log.getStackTraceString(error)}\n")
            }
            previous?.uncaughtException(thread, error)
        }
    }

    /** Отчёт о сбоях с прошлого запуска интерфейса (null — сбоев не было) и отметка нового
     *  запуска. Первый запуск после установки ничего не сообщает: не с чем сравнивать. */
    fun takePendingReport(context: Context): String? {
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        val since = prefs.getLong(KEY_LAST_START, 0L)
        prefs.edit().putLong(KEY_LAST_START, System.currentTimeMillis()).apply()
        val javaFile = javaCrashFile(context)
        val javaCrash = runCatching { javaFile.takeIf { it.exists() }?.readText()?.trim() }.getOrNull()
        javaFile.delete()
        if (since == 0L) return null

        val parts = mutableListOf<String>()
        if (!javaCrash.isNullOrEmpty()) parts += javaCrash
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) parts += exitReasons(context, since)
        if (parts.isEmpty()) return null

        val header = "Goji VPN ${BuildConfig.VERSION_NAME}, Android ${Build.VERSION.RELEASE} (API ${Build.VERSION.SDK_INT}), ${Build.MANUFACTURER} ${Build.MODEL}"
        return (header + "\n\n" + parts.joinToString("\n\n")).take(MAX_REPORT_CHARS)
    }

    @androidx.annotation.RequiresApi(Build.VERSION_CODES.R)
    private fun exitReasons(context: Context, since: Long): List<String> = runCatching {
        val am = context.getSystemService(ActivityManager::class.java)
        am.getHistoricalProcessExitReasons(context.packageName, 0, 10)
            .filter { it.timestamp > since && it.reason in REPORTED_REASONS }
            .take(MAX_EXIT_REASONS)
            .map { info ->
                val trace = runCatching { info.traceInputStream?.bufferedReader()?.use { it.readText().take(MAX_TRACE_CHARS) } }.getOrNull()
                buildString {
                    append("${stamp(info.timestamp)} ${reasonName(info.reason)} — процесс ${info.processName}")
                    info.description?.let { append("\n").append(it) }
                    if (!trace.isNullOrBlank()) append("\n").append(trace)
                }
            }
    }.getOrDefault(emptyList())

    private val REPORTED_REASONS = setOf(
        ApplicationExitInfo.REASON_CRASH,
        ApplicationExitInfo.REASON_CRASH_NATIVE,
        ApplicationExitInfo.REASON_ANR
    )

    private fun reasonName(reason: Int) = when (reason) {
        ApplicationExitInfo.REASON_CRASH -> "падение (Java)"
        ApplicationExitInfo.REASON_CRASH_NATIVE -> "падение в машинном коде"
        ApplicationExitInfo.REASON_ANR -> "зависание (ANR)"
        else -> "код $reason"
    }

    private fun stamp(millis: Long) = SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.US).format(Date(millis))
}
