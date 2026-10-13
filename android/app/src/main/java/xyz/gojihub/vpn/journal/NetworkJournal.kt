package xyz.gojihub.vpn.journal

import android.content.Context
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import org.json.JSONObject
import java.io.File
import java.util.Calendar

/**
 * «Журнал сети» — история работы VPN на этом телефоне, как страница аптайма: когда туннель
 * был поднят, когда и почему переключались узлы, что происходило с сетью. Хранится только
 * локально ([FILE_NAME] в filesDir, по строке JSON на событие), последние [KEEP_DAYS] дней.
 *
 * Тексты событий не хранятся готовыми — только тип и параметр (имя узла, сети, текст ошибки),
 * заголовки собирает экран на текущем языке.
 */
object NetworkJournal {

    enum class Kind {
        CONNECTED, DISCONNECTED, INTERRUPTED,
        NET_WIFI, NET_CELLULAR, NET_JAMMED,
        SWITCH_JAMMED, SWITCH_CELLULAR_EU, SWITCH_CELLULAR_LTE, SWITCH_WIFI_RESTORED,
        RULE_AUTOCONNECT, RULE_TRUSTED_DISCONNECT,
        ERROR, LEAK_OK, LEAK_FAIL
    }

    data class Event(val at: Long, val kind: Kind, val arg: String = "")

    /** Час суток: доля времени под защитой 0..1 (null — час ещё не наступил) и были ли сбои. */
    data class Hour(val uptime: Float?, val issue: Boolean)

    data class Day(
        val hours: List<Hour>,
        val protectedMs: Long,
        val elapsedMs: Long,
        val connects: Int,
        val switches: Int,
        val issues: Int,
        val events: List<Event>
    )

    private const val FILE_NAME = "network_journal.jsonl"
    private const val KEEP_DAYS = 7
    private const val MAX_LINES = 3000
    private const val PREFS = "network_journal"
    private const val KEY_HEARTBEAT = "heartbeat"
    private const val HEARTBEAT_MS = 5 * 60_000L

    private val ISSUE_KINDS = setOf(Kind.INTERRUPTED, Kind.NET_JAMMED, Kind.SWITCH_JAMMED, Kind.ERROR, Kind.LEAK_FAIL)
    private val SWITCH_KINDS = setOf(Kind.SWITCH_JAMMED, Kind.SWITCH_CELLULAR_EU, Kind.SWITCH_CELLULAR_LTE, Kind.SWITCH_WIFI_RESTORED)

    @OptIn(ExperimentalCoroutinesApi::class)
    private val io = CoroutineScope(SupervisorJob() + Dispatchers.IO.limitedParallelism(1))
    private val _events = MutableStateFlow<List<Event>>(emptyList())
    val events: StateFlow<List<Event>> = _events.asStateFlow()

    private lateinit var file: File
    private lateinit var appContext: Context
    @Volatile private var ready = false

    /** Из Application.onCreate (только основной процесс). [vpnRunningNow] — поднят ли туннель
     *  прямо сейчас: если журнал заканчивается подключением, а туннеля нет, значит процесс
     *  был убит вместе с сервисом — закрываем сессию моментом последнего «пульса». */
    fun init(context: Context, vpnRunningNow: Boolean) {
        if (ready) return
        appContext = context.applicationContext
        file = File(appContext.filesDir, FILE_NAME)
        ready = true
        io.launch {
            val cutoff = System.currentTimeMillis() - KEEP_DAYS * 86_400_000L
            val all = runCatching { file.readLines() }.getOrDefault(emptyList()).mapNotNull(::parse)
            val kept = all.filter { it.at >= cutoff }.takeLast(MAX_LINES)
            if (kept.size != all.size) runCatching { file.writeText(kept.joinToString("") { encode(it) + "\n" }) }
            _events.value = kept
            val lastSession = kept.lastOrNull { it.kind == Kind.CONNECTED || it.kind == Kind.DISCONNECTED || it.kind == Kind.INTERRUPTED }
            if (lastSession?.kind == Kind.CONNECTED && !vpnRunningNow) {
                val beat = prefs().getLong(KEY_HEARTBEAT, lastSession.at).coerceAtLeast(lastSession.at)
                append(Event(beat, Kind.INTERRUPTED))
            }
        }
    }

    fun log(kind: Kind, arg: String = "") {
        if (!ready) return
        val event = Event(System.currentTimeMillis(), kind, arg.take(160))
        io.launch { append(event) }
        if (kind == Kind.CONNECTED) heartbeat()
    }

    /** Пока VPN поднят — отметка «ещё работал» (см. [init]); зовётся из VpnStateObserver. */
    fun heartbeat() {
        if (!ready) return
        prefs().edit().putLong(KEY_HEARTBEAT, System.currentTimeMillis()).apply()
    }

    const val HEARTBEAT_INTERVAL_MS = HEARTBEAT_MS

    private fun append(event: Event) {
        runCatching { file.appendText(encode(event) + "\n") }
        _events.value = (_events.value + event).sortedBy { it.at }.takeLast(MAX_LINES)
    }

    private fun prefs() = appContext.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    private fun encode(e: Event) = JSONObject().put("t", e.at).put("k", e.kind.name).put("a", e.arg).toString()

    private fun parse(line: String): Event? = runCatching {
        val o = JSONObject(line)
        Event(o.getLong("t"), Kind.valueOf(o.getString("k")), o.optString("a"))
    }.getOrNull()

    fun isSwitch(kind: Kind) = kind in SWITCH_KINDS
    fun isIssue(kind: Kind) = kind in ISSUE_KINDS

    /** Начало суток [daysAgo] дней назад (0 — сегодня) в локальном времени. */
    fun dayStart(daysAgo: Int, now: Long = System.currentTimeMillis()): Long = Calendar.getInstance().apply {
        timeInMillis = now
        set(Calendar.HOUR_OF_DAY, 0); set(Calendar.MINUTE, 0); set(Calendar.SECOND, 0); set(Calendar.MILLISECOND, 0)
        add(Calendar.DAY_OF_YEAR, -daysAgo)
    }.timeInMillis

    /** Сводка за сутки: аптайм по часам, счётчики и события дня. [runningNow] — открыт ли
     *  интервал подключения прямо сейчас (тогда он тянется до текущего момента). */
    fun day(all: List<Event>, daysAgo: Int, runningNow: Boolean, now: Long = System.currentTimeMillis()): Day {
        val start = dayStart(daysAgo, now)
        val end = dayStart(daysAgo - 1, now)
        val limit = minOf(end, now)

        // Интервалы под защитой, обрезанные границами суток.
        val intervals = mutableListOf<LongArray>()
        var openAt: Long? = null
        for (e in all) {
            when (e.kind) {
                Kind.CONNECTED -> if (openAt == null) openAt = e.at
                Kind.DISCONNECTED, Kind.INTERRUPTED -> openAt?.let { intervals += longArrayOf(it, e.at); openAt = null }
                else -> {}
            }
        }
        openAt?.let { if (runningNow) intervals += longArrayOf(it, now) }
        val clipped = intervals.mapNotNull { (a, b) ->
            val s = maxOf(a, start); val t = minOf(b, limit)
            if (t > s) longArrayOf(s, t) else null
        }

        val dayEvents = all.filter { it.at in start until end }
        val hours = (0 until 24).map { h ->
            val hs = start + h * 3_600_000L
            val he = hs + 3_600_000L
            if (hs >= limit) Hour(null, false) else {
                val span = (minOf(he, limit) - hs).coerceAtLeast(1)
                val covered = clipped.sumOf { (a, b) -> (minOf(b, he) - maxOf(a, hs)).coerceAtLeast(0) }
                Hour((covered.toFloat() / span).coerceIn(0f, 1f), dayEvents.any { it.at in hs until he && isIssue(it.kind) })
            }
        }
        return Day(
            hours = hours,
            protectedMs = clipped.sumOf { (a, b) -> b - a },
            // Журнал мог начаться посреди суток (первый запуск с ним) — процент считаем от
            // первой записи, а не от полуночи, иначе первый день выглядел бы как «почти 0%».
            elapsedMs = (limit - maxOf(start, all.firstOrNull()?.at ?: start)).coerceAtLeast(0),
            connects = dayEvents.count { it.kind == Kind.CONNECTED },
            switches = dayEvents.count { isSwitch(it.kind) },
            issues = dayEvents.count { isIssue(it.kind) },
            events = dayEvents.sortedByDescending { it.at }
        )
    }
}
