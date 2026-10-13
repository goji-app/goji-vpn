package xyz.gojihub.vpn.util

import android.content.Context
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.launch
import xyz.gojihub.vpn.i18n.Loc
import java.io.File
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter

/**
 * Локальный файловый журнал приложения — независимо от Log.d/Log.e, которые в релизной сборке
 * никуда не попадают (см. BuildConfig.DEBUG-гварды в GodjiVpnService/PingRepository и т.п.).
 * Нужен, чтобы при жалобе пользователя на "не подключается"/"не продлевается" было что
 * посмотреть, а не гадать вслепую.
 *
 * Разбит на категории (см. LogCategory) — каждая пишется в свой файл, чтобы в экране "Логи"
 * можно было смотреть их по отдельности (ядро/подписки/служба/пуши/остальное), а не искать
 * нужное событие в одной большой простыне. [level] — порог детализации (см. LogLevel),
 * настраивается в отдельном экране "Уровень логирования"; по умолчанию Info — подробные
 * Debug-дампы (ответы pingBatch, сводки маршрутизации и т.п.) пишутся, только если включить
 * Debug для диагностики.
 *
 * Запись — одной очередью: строки копятся в канале и пишутся пачками, по одному открытию
 * файла на категорию за пачку (раньше — корутина и открытие/закрытие файла на каждую строку).
 *
 * Важно для документации/политики конфиденциальности: этот журнал пишется и хранится
 * ИСКЛЮЧИТЕЛЬНО локально на устройстве и НИКУДА автоматически не отправляется — открывается
 * только по запросу пользователя в самом приложении (см. readAll/LogViewerDialog), без сети.
 */
object AppLogger {
    private const val MAX_SIZE_BYTES = 1_000_000L // ~1 МБ на категорию, дальше ротация в .old
    private const val MAX_BATCH = 256

    @Volatile var level: LogLevel = LogLevel.DEFAULT

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val timeFormatter = DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm:ss")

    private sealed class Cmd {
        class Line(val dir: File, val category: LogCategory, val text: String) : Cmd()
        class Clear(val dir: File, val categories: List<LogCategory>) : Cmd()
    }

    // Одна очередь и для строк, и для очистки (startSession) — порядок между ними сохраняется.
    private val queue = Channel<Cmd>(Channel.UNLIMITED)

    init {
        scope.launch {
            val batch = ArrayList<Cmd>(64)
            while (true) {
                batch.add(queue.receive())
                while (batch.size < MAX_BATCH) batch.add(queue.tryReceive().getOrNull() ?: break)
                runCatching { flush(batch) }
                batch.clear()
            }
        }
    }

    fun d(context: Context, category: LogCategory, tag: String, message: String) =
        write(context, category, LogLevel.DEBUG, "D", tag, message, null)

    fun i(context: Context, category: LogCategory, tag: String, message: String) =
        write(context, category, LogLevel.INFO, "I", tag, message, null)

    fun w(context: Context, category: LogCategory, tag: String, message: String, t: Throwable? = null) =
        write(context, category, LogLevel.WARNING, "W", tag, message, t)

    fun e(context: Context, category: LogCategory, tag: String, message: String, t: Throwable? = null) =
        write(context, category, LogLevel.ERROR, "E", tag, message, t)

    private fun write(
        context: Context,
        category: LogCategory,
        messageLevel: LogLevel,
        levelLabel: String,
        tag: String,
        message: String,
        t: Throwable?
    ) {
        if (level.rank < messageLevel.rank) return
        val line = buildString {
            append(LocalDateTime.now().format(timeFormatter))
            append(" ").append(levelLabel).append("/").append(tag).append(": ").append(message)
            if (t != null) append(" — ").append(t.javaClass.simpleName).append(": ").append(t.message)
        }
        queue.trySend(Cmd.Line(context.applicationContext.filesDir, category, line))
    }

    /** Пишет пачку: подряд идущие строки одной категории — одним appendText; очистка — по месту
     *  (накопленное до неё дописывается раньше, чтобы порядок не нарушился). */
    private fun flush(batch: List<Cmd>) {
        val pending = LinkedHashMap<String, Pair<File, StringBuilder>>()
        fun writePending() {
            for ((file, text) in pending.values) {
                runCatching {
                    if (file.exists() && file.length() > MAX_SIZE_BYTES) {
                        file.copyTo(File(file.path + ".old"), overwrite = true)
                        file.delete()
                    }
                    file.appendText(text.toString())
                }
            }
            pending.clear()
        }
        for (cmd in batch) {
            when (cmd) {
                is Cmd.Line -> {
                    val file = File(cmd.dir, "${cmd.category.fileBaseName}.log")
                    pending.getOrPut(file.path) { file to StringBuilder() }.second.append(cmd.text).append('\n')
                }
                is Cmd.Clear -> {
                    writePending()
                    for (category in cmd.categories) {
                        runCatching {
                            File(cmd.dir, "${category.fileBaseName}.log").delete()
                            File(cmd.dir, "${category.fileBaseName}.log.old").delete()
                        }
                    }
                }
            }
        }
        writePending()
    }

    /** Стирает уже записанное для категорий (текущий файл + .old-ротацию) — вызывается в начале
     *  новой попытки подключения (см. GodjiVpnService.onStartCommand), чтобы "Лог ядра"/
     *  "Лог службы" всегда показывали только активное соединение, а не десяток прошлых попыток
     *  вперемешку. Идёт через ту же очередь, что и write() — благодаря этому, если вызвать это
     *  ДО первого dlog() новой попытки, порядок гарантированно сохранится: сначала очистка,
     *  потом уже строки самой попытки. */
    fun startSession(context: Context, vararg categories: LogCategory) {
        queue.trySend(Cmd.Clear(context.applicationContext.filesDir, categories.toList()))
    }

    /** Содержимое журнала конкретной категории для показа прямо в приложении (см.
     *  LogViewerDialog) — без отправки куда-либо, только чтение локального файла. Старый
     *  .old-файл (после ротации) читаем первым, чтобы порядок строк оставался хронологическим. */
    fun readAll(context: Context, category: LogCategory): String {
        val dir = context.filesDir
        val old = File(dir, "${category.fileBaseName}.log.old").takeIf { it.exists() }?.readText().orEmpty()
        val current = File(dir, "${category.fileBaseName}.log").takeIf { it.exists() }?.readText().orEmpty()
        val combined = old + current
        return combined.ifBlank { Loc.s.logEmpty }
    }
}
