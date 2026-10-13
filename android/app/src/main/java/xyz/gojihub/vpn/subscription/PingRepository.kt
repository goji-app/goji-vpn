package xyz.gojihub.vpn.subscription

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.withContext
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import android.content.Context
import dagger.hilt.android.qualifiers.ApplicationContext
import xyz.gojihub.vpn.settings.PingMethod
import xyz.gojihub.vpn.settings.SettingsRepository
import xyz.gojihub.vpn.util.AppLogger
import xyz.gojihub.vpn.util.LogCategory
import xyz.gojihub.vpn.vpn.GodjiVpnService
import libXray.LibXray
import org.json.JSONArray
import org.json.JSONObject
import java.net.InetSocketAddress
import java.net.Socket
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Замер пинга серверов подписки — способ настраивается в "Настройках" (см. SettingsRepository,
 * PingMethod): через встроенный в libXray "pingBatch" (реальный HTTP-запрос через временный
 * Xray-инстанс, учитывает задержку самого прокси-протокола, не только сетевой RTT), обычным
 * TCP-connect до host:port узла, или системным ping (ICMP).
 *
 * Вынесен из ServersViewModel в отдельный singleton, чтобы запускать его не только с экрана
 * "Узлы" (кнопкой "обновить всё"), но и при открытии приложения и из фонового воркера
 * (см. SubscriptionRefreshWorker) — единая точка правды для всех подписчиков.
 */
@Singleton
class PingRepository @Inject constructor(
    private val subscriptionRepository: SubscriptionRepository,
    private val settingsRepository: SettingsRepository,
    private val isolatedXray: IsolatedXrayInvoker,
    @ApplicationContext private val appContext: Context
) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    private val _pings = MutableStateFlow<Map<String, Int>>(emptyMap())
    val pings: StateFlow<Map<String, Int>> = _pings.asStateFlow()

    private val _checkingId = MutableStateFlow<String?>(null)
    val checkingId: StateFlow<String?> = _checkingId.asStateFlow()

    private val _checkingAll = MutableStateFlow(false)
    val checkingAll: StateFlow<Boolean> = _checkingAll.asStateFlow()

    // LibXray.invoke() держит глобальное состояние на стороне Go — параллельные вызовы
    // (например, "пинг при открытии" с экрана "Защита" и с экрана "Узлы" почти одновременно,
    // раз оба ViewModel живут одновременно) ломают друг друга изнутри самого pingBatch
    // ("io: read/write on closed pipe" — проверено живьём). Сериализуем ВСЕ обращения к
    // LibXray.invoke() из этого репозитория одним мьютексом.
    private val invokeMutex = Mutex()

    fun pingOne(id: String) {
        scope.launch {
            _checkingId.value = id
            val node = subscriptionRepository.nodes.value.firstOrNull { it.id == id }
            if (node != null) {
                val method = settingsRepository.pingMethodNow()
                val url = settingsRepository.pingTestUrlNow()
                _pings.value = _pings.value + (node.id to measureOne(node, method, url))
            }
            _checkingId.value = null
        }
    }

    fun pingAll() {
        scope.launch { pingAllInternal() }
    }

    // Момент последней полной проверки всех узлов — для автоматических проверок (открытие
    // приложения, заход на "Серверы"): каждая полная проверка поднимает временные экземпляры
    // Xray на все узлы (а при включённом VPN — ещё и процесс ":ping"), и раньше одно только
    // открытие приложения запускало её 2 раза подряд, плюс ещё раз на каждый заход на вкладку.
    @Volatile private var lastFullPingAt = 0L

    /** Автоматическая проверка: только если последняя полная была дольше [maxAgeMs] назад.
     *  Кнопка "Пинг" по-прежнему проверяет сразу (pingAll). */
    fun pingAllIfStale(maxAgeMs: Long = AUTO_PING_MIN_INTERVAL_MS) {
        if (System.currentTimeMillis() - lastFullPingAt < maxAgeMs) return
        scope.launch { pingAllInternal() }
    }

    suspend fun pingAllInternalIfStale(maxAgeMs: Long = AUTO_PING_MIN_INTERVAL_MS) {
        if (System.currentTimeMillis() - lastFullPingAt < maxAgeMs) return
        pingAllInternal()
    }

    suspend fun pingAllInternal() {
        // Guard — без него авто-пинг при каждом заходе на вкладку "Серверы" (см. ServersScreen)
        // мог бы запускать параллельную проверку поверх уже идущей (например, если пользователь
        // быстро туда-обратно переключает вкладки), удваивая нагрузку на libXray без пользы.
        if (_checkingAll.value) return
        _checkingAll.value = true
        lastFullPingAt = System.currentTimeMillis()
        val method = settingsRepository.pingMethodNow()
        val url = settingsRepository.pingTestUrlNow()
        val nodes = subscriptionRepository.nodes.value
        val results = HashMap<String, Int>(_pings.value)

        if (method == PingMethod.TCP || method == PingMethod.ICMP) {
            // TCP/ICMP не смотрят на JSON-конфиг узла вообще — только host, без прокси и без
            // балансировщика резервных outbound'ов (тот вопрос имеет смысл только для методов
            // "через прокси", где реально поднимается временный Xray-инстанс на этот outbound).
            nodes.forEach { node -> results[node.id] = measureDirect(node, method) }
            _pings.value = results
            _checkingAll.value = false
            return
        }

        val (jsonNodes, legacyNodes) = nodes.partition { it.connectPayload.trimStart().startsWith("{") }

        // Узлы вида "Автовыбор LTE/EU" и "Hosting" — не один аутбаунд, а балансировщик
        // (routing.balancers) с несколькими резервными VLESS-аутбаундами (proxy, proxy-2, ...).
        // В подавляющем большинстве случаев первый ("proxy") живой, поэтому сначала проверяем
        // ТОЛЬКО его для всех узлов — так же быстро, как раньше для обычных одноаутбаундовых
        // узлов. Резервные кандидаты (proxy-2, proxy-3, ...) пробуем вторым проходом и только
        // для тех узлов, у которых первый не ответил — иначе каждый замер "всех серверов"
        // упирался бы в полный таймаут по 5-6 кандидатам даже когда всё и так работает.
        val tagsByNode = jsonNodes.associate { it.id to proxyOutboundTags(it.connectPayload) }
        val perNodeBest = HashMap<String, Int>()

        val firstPass = jsonNodes.mapNotNull { node ->
            tagsByNode[node.id]?.firstOrNull()?.let { PingTarget(node.id, node.connectPayload, it) }
        }
        firstPass.chunked(MAX_BATCH_SIZE).forEach { chunk ->
            measureTargetsViaProxy(chunk, method, url).forEach { (nodeId, delay) -> perNodeBest[nodeId] = mergeBest(perNodeBest[nodeId], delay) }
        }

        val secondPass = jsonNodes
            .filter { (perNodeBest[it.id] ?: -1) < 0 }
            .flatMap { node -> tagsByNode[node.id].orEmpty().drop(1).map { tag -> PingTarget(node.id, node.connectPayload, tag) } }
        // pingBatch поднимает один общий временный Xray-инстанс на все конфиги пачки —
        // ограничение самой библиотеки (maxPingBatchConfigs) в 5 конфигов за раз.
        secondPass.chunked(MAX_BATCH_SIZE).forEach { chunk ->
            measureTargetsViaProxy(chunk, method, url).forEach { (nodeId, delay) -> perNodeBest[nodeId] = mergeBest(perNodeBest[nodeId], delay) }
        }

        jsonNodes.forEach { node -> results[node.id] = perNodeBest[node.id] ?: -1 }
        legacyNodes.forEach { node -> results[node.id] = measureTcpRtt(node.host, node.port) }
        _pings.value = results
        _checkingAll.value = false
    }

    private suspend fun measureOne(node: VlessNode, method: PingMethod, url: String): Int {
        if (method == PingMethod.TCP || method == PingMethod.ICMP) return measureDirect(node, method)
        return measureViaProxy(node, method, url)
    }

    private suspend fun measureDirect(node: VlessNode, method: PingMethod): Int =
        if (method == PingMethod.ICMP) measureIcmp(node.host) else measureTcpRtt(node.host, node.port)

    private suspend fun measureViaProxy(node: VlessNode, method: PingMethod, url: String): Int = withContext(Dispatchers.IO) {
        if (!node.connectPayload.trimStart().startsWith("{")) return@withContext measureTcpRtt(node.host, node.port)
        val tags = proxyOutboundTags(node.connectPayload)
        val first = tags.firstOrNull() ?: return@withContext -1
        var best: Int? = measureTargetsViaProxy(listOf(PingTarget(node.id, node.connectPayload, first)), method, url).firstOrNull()?.second
        if ((best ?: -1) < 0 && tags.size > 1) {
            tags.drop(1).map { PingTarget(node.id, node.connectPayload, it) }.chunked(MAX_BATCH_SIZE).forEach { chunk ->
                measureTargetsViaProxy(chunk, method, url).forEach { (_, delay) -> best = mergeBest(best, delay) }
            }
        }
        best ?: -1
    }

    private data class PingTarget(val nodeId: String, val payload: String, val tag: String)

    /** "Лучший" результат из двух: успешный пинг всегда обгоняет неудачу, из двух успешных —
     *  меньшая задержка, из двух неудач — любая (обе -1). */
    private fun mergeBest(current: Int?, candidate: Int): Int = when {
        current == null -> candidate
        current < 0 && candidate < 0 -> current
        current < 0 -> candidate
        candidate < 0 -> current
        else -> minOf(current, candidate)
    }

    /** Теги VLESS-аутбаундов узла, которые реально может выбрать балансировщик ("proxy",
     *  "proxy-2", ... — см. routing.balancers[].selector в реальном профиле бэкенда). Для
     *  обычного узла с одним аутбаундом — просто ["proxy"], поведение не меняется. */
    private fun proxyOutboundTags(connectPayload: String): List<String> = runCatching {
        val outbounds = JSONObject(connectPayload).optJSONArray("outbounds") ?: return@runCatching listOf("proxy")
        (0 until outbounds.length())
            .map { outbounds.getJSONObject(it) }
            .filter { it.optJSONObject("settings")?.optJSONArray("vnext") != null }
            .mapNotNull { it.optString("tag").takeIf(String::isNotBlank) }
            .ifEmpty { listOf("proxy") }
    }.getOrDefault(listOf("proxy"))

    /** -1 = недоступен. Каждый элемент чанка со своим outboundTag — так pingBatch измеряет
     *  конкретный резервный аутбаунд узла, а не только дефолтный "proxy".
     *  [method] PROXY_HEAD добавляет в payload необязательное поле "method":"HEAD" — если
     *  конкретная сборка libXray его не читает, тихо ведёт себя как GET (не ломается, просто
     *  не даёт экономии на не-скачивании тела ответа). */
    private suspend fun measureTargetsViaProxy(chunk: List<PingTarget>, method: PingMethod, url: String): List<Pair<String, Int>> = withContext(Dispatchers.IO) {
        runCatching {
            val configs = JSONArray()
            chunk.forEach { target ->
                configs.put(JSONObject().put("xrayJson", target.payload).put("outboundTag", target.tag))
            }
            val payload = JSONObject()
                .put("configs", configs)
                .put("timeout", TIMEOUT_SECONDS)
                .put("url", url)
            if (method == PingMethod.PROXY_HEAD) payload.put("method", "HEAD")
            AppLogger.d(appContext, LogCategory.SUBSCRIPTION, TAG, "pingBatch request: ${chunk.map { "${it.nodeId}:${it.tag}" }}")
            val response = invokeMutex.withLock { invokeLibXray("pingBatch", payload) }
            AppLogger.d(appContext, LogCategory.SUBSCRIPTION, TAG, "pingBatch response: $response")
            val results = response.getJSONObject("data").getJSONArray("results")
            chunk.mapIndexed { index, target ->
                val item = results.getJSONObject(index)
                if (item.optBoolean("success")) {
                    target.nodeId to item.optLong("delay").toInt()
                } else {
                    AppLogger.w(appContext, LogCategory.SUBSCRIPTION, TAG, "ping ${target.nodeId}:${target.tag}: ${item.optString("error")}")
                    target.nodeId to -1
                }
            }
        }.getOrElse {
            AppLogger.e(appContext, LogCategory.SUBSCRIPTION, TAG, "pingBatch failed", it)
            chunk.map { it.nodeId to -1 }
        }
    }

    /** Пока в этом процессе работает ядро VPN (GodjiVpnService), libXray не выполняет pingBatch
     *  ("requires an isolated process without a managed Xray instance") — запрос уходит в
     *  отдельный процесс ":ping" (PingProcessService). Без VPN — как раньше, прямо здесь; если
     *  ядро успело подняться между проверкой и вызовом — тот же отказ ловим и повторяем там. */
    private suspend fun invokeLibXray(method: String, payload: JSONObject): JSONObject {
        val request = JSONObject()
            .put("apiVersion", LibXray.LibXrayAPIVersion)
            .put("method", method)
            .put("payload", payload)
            .toString()
        if (GodjiVpnService.isRunning.value || GodjiVpnService.isConnecting.value) {
            return JSONObject(isolatedXray.invoke(request, ISOLATED_TIMEOUT_MS))
        }
        val local = JSONObject(LibXray.invoke(request))
        if (!local.optBoolean("success") && local.optString("error").contains("isolated process")) {
            AppLogger.d(appContext, LogCategory.SUBSCRIPTION, TAG, "pingBatch: ядро уже запущено — повторяю в процессе :ping")
            return JSONObject(isolatedXray.invoke(request, ISOLATED_TIMEOUT_MS))
        }
        return local
    }

    /** Запасной вариант только для легаси-формата подписки (сырая vless://-ссылка без
     *  готового Xray-JSON) — TCP-connect до host:port, без замера через сам прокси. */
    private suspend fun measureTcpRtt(host: String, port: Int): Int = withContext(Dispatchers.IO) {
        runCatching {
            val start = System.currentTimeMillis()
            Socket().use { socket ->
                socket.connect(InetSocketAddress(host, port), TCP_TIMEOUT_MS)
            }
            (System.currentTimeMillis() - start).toInt()
        }.getOrElse { -1 }
    }

    /** Системная утилита ping (ICMP) через отдельный процесс — Android не даёт обычным
     *  приложениям сырые ICMP-сокеты без root, но сам бинарник /system/bin/ping имеет нужный
     *  capability на большинстве прошивок и его можно просто запустить. Один пакет, парсим
     *  "time=NN" из вывода. */
    private suspend fun measureIcmp(host: String): Int = withContext(Dispatchers.IO) {
        runCatching {
            val process = ProcessBuilder("/system/bin/ping", "-c", "1", "-W", TIMEOUT_SECONDS.toString(), host)
                .redirectErrorStream(true)
                .start()
            val output = process.inputStream.bufferedReader().readText()
            process.waitFor()
            val match = Regex("""time[=<]([0-9.]+)""").find(output)
            match?.groupValues?.get(1)?.toDoubleOrNull()?.toInt() ?: -1
        }.getOrElse { -1 }
    }

    private companion object {
        const val TCP_TIMEOUT_MS = 2000
        // Было 3 — узлы вроде Польши/Финляндии реально отвечают за 300-700мс, но пачка из
        // MAX_BATCH_SIZE=5 конфигов измеряется одним временным Xray-инстансом одновременно,
        // и конкуренция за ресурсы внутри пачки иногда выталкивает самый медленный узел за
        // таймаут — тогда он ложно показывает "недоступен", хотя при одиночной проверке
        // (без конкуренции) тот же узел отвечает нормально. Проверено живьём: с 3с Польша
        // периодически уходила в "недоступен" при пакетном "обновить всё".
        const val TIMEOUT_SECONDS = 4
        const val MAX_BATCH_SIZE = 5
        const val AUTO_PING_MIN_INTERVAL_MS = 60_000L
        // Пачка до MAX_BATCH_SIZE конфигов по TIMEOUT_SECONDS каждый + запуск процесса ":ping".
        const val ISOLATED_TIMEOUT_MS = 30_000L
        const val TAG = "GodjiPing"
    }
}
