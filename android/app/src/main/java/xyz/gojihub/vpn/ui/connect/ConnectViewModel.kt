package xyz.gojihub.vpn.ui.connect

import android.content.Context
import android.content.Intent
import android.net.TrafficStats
import android.os.Process
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.debounce
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import xyz.gojihub.vpn.geo.CountryGeo
import xyz.gojihub.vpn.geo.CountryGeoLookup
import xyz.gojihub.vpn.geo.Greetings
import xyz.gojihub.vpn.globe.GlobeNode
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.network.NetState
import xyz.gojihub.vpn.network.NetworkMonitor
import xyz.gojihub.vpn.settings.SettingsRepository
import xyz.gojihub.vpn.subscription.PingRepository
import xyz.gojihub.vpn.subscription.SubscriptionRepository
import xyz.gojihub.vpn.subscription.VlessNode
import xyz.gojihub.vpn.subscription.toGlobeNode
import xyz.gojihub.vpn.util.AppVisibility
import xyz.gojihub.vpn.util.formatDate
import xyz.gojihub.vpn.util.stripLeadingFlag
import xyz.gojihub.vpn.vpn.GodjiVpnService
import javax.inject.Inject

data class ConnectUiState(
    val connected: Boolean = false,
    val connecting: Boolean = false,
    val downSpeedMbps: Double = 0.0,
    val upSpeedMbps: Double = 0.0,
    val downHistory: List<Float> = emptyList(),
    val upHistory: List<Float> = emptyList(),
    val usedGb: Double = 0.0,
    val quotaGb: Double = 0.0,
    val isUnlimited: Boolean = false,
    val expiryLabel: String = "—",
    val daysLeft: Int = 0,
    val error: String? = null,
    /** Что предложить рядом с ошибкой подключения (кнопка следующего шага). */
    val errorAction: ErrorAction? = null,

    val globeStatus: String = "off", // "off" | "connecting" | "on"
    val globeNode: GlobeNode? = null,

    val currentNodeName: String = "",
    val currentGeo: CountryGeo? = null,
    val currentCode: String = "АВТО",
    val currentFlag: String = "🌐",

    val greetingHi: String = "Hello",
    val greetingSub: String = "английский",
    val greetingUsesSerif: Boolean = true,

    val netState: NetState = NetState.CELLULAR,
    val banner: String? = null,
    val bannerKind: BannerKind = BannerKind.INFO,
    val trialDays: Int? = null,
    val trialActivating: Boolean = false,

    val connectedTimeLabel: String = "00:00:00",

    // Качество канала (плитка на главной): медиана задержки через туннель за последние
    // QUALITY_WINDOW замеров, доля неудачных и итоговая оценка 0..100. null — не измерено.
    val qualityScore: Int? = null,
    val qualityPingMs: Int? = null,
    val qualityLossPct: Int = 0
)

enum class BannerKind { WARNING, SUCCESS, INFO }

/** Следующий шаг при ошибке подключения: пользователи сами не разбираются, что делать. */
enum class ErrorAction { CHANGE_SERVER, OPEN_PLANS }

/** Оценка канала 0..100: задержка до 150 мс — 100, к 900 мс линейно падает до 35; каждые 10%
 *  потерь — минус 5. Все замеры неудачны — 0. */
internal fun qualityScore(rttMs: Int?, lossPct: Int): Int {
    if (rttMs == null) return 0
    val base = when {
        rttMs <= 150 -> 100.0
        rttMs >= 900 -> 35.0
        else -> 100.0 - (rttMs - 150) * 65.0 / 750.0
    }
    return (base - lossPct * 0.5).toInt().coerceIn(0, 100)
}

private const val SPEED_HISTORY_SIZE = 30
private const val QUALITY_WINDOW = 6
private const val QUALITY_INTERVAL_MS = 10_000L

@HiltViewModel
class ConnectViewModel @Inject constructor(
    private val subscriptionRepository: SubscriptionRepository,
    private val pingRepository: PingRepository,
    private val networkMonitor: NetworkMonitor,
    private val settingsRepository: SettingsRepository,
    // Ходит через локальный SOCKS Xray, пока туннель поднят (см. NetworkModule) — то есть
    // меряет задержку именно по VPN-маршруту, без авторизационных заголовков приложения.
    @javax.inject.Named("diagnostics") diagnosticsClient: okhttp3.OkHttpClient,
    @ApplicationContext private val appContext: Context
) : ViewModel() {

    private val probeClient = diagnosticsClient.newBuilder()
        .callTimeout(5, java.util.concurrent.TimeUnit.SECONDS)
        .build()
    private var qualityJob: Job? = null

    private val _state = MutableStateFlow(ConnectUiState())
    val state: StateFlow<ConnectUiState> = _state

    private var speedJob: Job? = null
    private var preferredNodeId: String? = null
    private var lastNetState: NetState? = null

    init {
        viewModelScope.launch {
            // "Автообновление подписки и пинга при открытии приложения" — экран "Защита"
            // это стартовый экран, поэтому именно здесь считаем момент открытия приложения.
            subscriptionRepository.refreshIfStale()
            pingRepository.pingAllInternalIfStale()
        }

        viewModelScope.launch {
            settingsRepository.preferredNodeId.collect { preferredNodeId = it }
        }

        viewModelScope.launch {
            subscriptionRepository.subscription.collect { sub ->
                val traffic = sub?.traffic
                _state.value = _state.value.copy(
                    usedGb = (traffic?.usedBytes ?: 0L) / 1_000_000_000.0,
                    quotaGb = (traffic?.limitBytes ?: 0L) / 1_000_000_000.0,
                    isUnlimited = traffic?.isUnlimited ?: false,
                    expiryLabel = sub?.expireAt?.let(::formatDate) ?: "—",
                    daysLeft = sub?.daysLeft ?: 0
                )
            }
        }

        viewModelScope.launch {
            subscriptionRepository.trialDays.collect { _state.value = _state.value.copy(trialDays = it) }
        }

        viewModelScope.launch {
            subscriptionRepository.nodes.collect { updateNodeDependentState() }
        }
        viewModelScope.launch {
            subscriptionRepository.selectedId.collect { updateNodeDependentState() }
        }

        viewModelScope.launch {
            // combine, а не два независимых collect на isRunning/isConnecting — раньше
            // "connecting" тут жёстко сбрасывался в false при любом изменении isRunning,
            // из-за чего в момент переключения узла на активном соединении (см.
            // GodjiVpnService.performConnect: старый туннель уже остановлен, isRunning на
            // мгновение false, новый ещё не поднят) экран показывал "отключено", а не
            // "переключаюсь" — выглядело как зависание, хотя сервис работал как задумано.
            combine(GodjiVpnService.isRunning, GodjiVpnService.isConnecting) { running, connecting -> running to connecting }
                .collect { (running, connecting) ->
                    _state.value = _state.value.copy(
                        connected = running,
                        connecting = connecting,
                        globeStatus = when {
                            connecting -> "connecting"
                            running -> "on"
                            else -> "off"
                        },
                        // Реальное время начала отсчёта живёт в самом сервисе (connectedSinceMillis) —
                        // формируется в startSpeedPolling() ниже; здесь только сбрасываем на "выключено"
                        // для случая, когда туннель уже упал, а UI ещё не обновился спустя тик таймера.
                        connectedTimeLabel = if (running) _state.value.connectedTimeLabel else "00:00:00"
                    )
                }
        }
        viewModelScope.launch {
            // Скорость и таймер раз в секунду нужны только на видимом экране: в свёрнутом
            // приложении ViewModel жива, и раньше этот цикл будил процессор впустую.
            combine(GodjiVpnService.isRunning, AppVisibility.visible) { running, visible -> running && visible }
                .collect { active ->
                    if (active) { startSpeedPolling(); startQualityProbe() } else { stopSpeedPolling(); stopQualityProbe() }
                }
        }
        viewModelScope.launch {
            // "connecting" сюда больше не трогаем — единственный источник правды для него
            // теперь GodjiVpnService.isConnecting (см. combine выше): при промежуточной неудаче
            // одной из RECONNECT_ATTEMPTS попыток внутри performConnect сервис ещё продолжает
            // пытаться, и сбрасывать здесь connecting в false было бы враньём про реальное
            // состояние — экран уже успевал показать "не получилось" ровно в тот момент,
            // когда сервис молча готовил следующую попытку.
            GodjiVpnService.lastError.collect { error ->
                if (error != null) _state.value = _state.value.copy(error = error, errorAction = errorActionFor())
            }
        }
        viewModelScope.launch {
            GodjiVpnService.actualCountry.collect { country ->
                if (country != null) applyActualCountry(country) else updateNodeDependentState()
            }
        }

        viewModelScope.launch {
            // NetworkMonitor.onLost() безусловно обнуляет и hasWifi, и hasCellular в момент,
            // когда система на мгновение теряет текущую default-сеть — а это штатно случается
            // при роуминге между точками Wi-Fi, обновлении DHCP-адреса, повторной проверке
            // captive portal, пробуждении экрана: не только при реальном отсутствии связи.
            // recompute() в этот момент попадает в ветку JAMMED (её "else" означает буквально
            // "нет default-сети прямо сейчас", а не "сотовая сеть заглушена"), и без дебаунса
            // ниже WIFI -> JAMMED -> WIFI за доли секунды успевало реально переключить узел на
            // российский и показать баннер "Глушат мобильную", хотя пользователь всё это время
            // был на стабильном Wi-Fi. 1с — с запасом больше типичной вспышки при роуминге/DHCP,
            // но пренебрежимо мало по сравнению с реальным затягиванием при настоящем глушении.
            networkMonitor.state.debounce(1000L).collect { net -> onNetworkChanged(net) }
        }
    }

    private fun updateNodeDependentState() {
        val nodes = subscriptionRepository.nodes.value
        val selected = subscriptionRepository.selectedNode()
        val globeNodes = nodes.mapNotNull { it.toGlobeNode() }
        val g = Greetings.forLang(selected?.geo?.lang)
        _state.value = _state.value.copy(
            globeNode = selected?.toGlobeNode() ?: globeNodes.firstOrNull(),
            currentNodeName = selected?.name?.let(::stripLeadingFlag) ?: "",
            currentGeo = selected?.geo,
            currentCode = selected?.geo?.code ?: "АВТО",
            currentFlag = selected?.let { CountryGeoLookup.nodeFlag(it.name, it.geo) } ?: "🌐",
            greetingHi = g.hi,
            greetingSub = (if (g.transliteration.isNotBlank()) "«${g.transliteration}» · " else "") + g.language,
            greetingUsesSerif = g.useSerifFont
        )
    }

    /** Remark выбранного узла — не гарантия реального местоположения выхода (видели живьём
     *  узел "Germany", реально выходивший через Latvia). Подменяем отображаемую страну на
     *  настоящую, определённую GodjiVpnService через сам туннель, когда она известна. */
    private fun applyActualCountry(country: String) {
        val previousGeo = _state.value.currentGeo
        val previousDisplay = previousGeo?.displayCityCountry(Loc.lang)
        val geo = CountryGeoLookup.find(country)
        val g = Greetings.forLang(geo?.lang)
        val selectedId = subscriptionRepository.selectedId.value
        val newDisplay = geo?.displayCityCountry(Loc.lang) ?: country
        _state.value = _state.value.copy(
            currentGeo = geo,
            currentCode = geo?.code ?: country.take(2).uppercase(),
            currentFlag = geo?.code?.let(CountryGeoLookup::flagEmoji) ?: "🌐",
            globeNode = geo?.let { GlobeNode(selectedId ?: "actual", it.lat, it.lon, it.country) } ?: _state.value.globeNode,
            greetingHi = g.hi,
            greetingSub = (if (g.transliteration.isNotBlank()) "«${g.transliteration}» · " else "") + g.language,
            greetingUsesSerif = g.useSerifFont
        )
        // Сравниваем в том же формате "Город, Страна", в котором хранится currentGeo —
        // иначе (сравнение с "голой" страной) баннер срабатывал бы каждый раз, даже когда
        // страна на самом деле не изменилась.
        if (previousDisplay != null && previousDisplay != newDisplay) {
            // У узла "Автовыбор LTE" сама геометка — Россия (это его назначение при глушении),
            // но реально он балансирует по нескольким резервным аутбаундам (см. PingRepository) и
            // вне глушения вполне может выйти через европейский IP — это ожидаемо, а не "подмена
            // страны", поэтому для него отдельное, не тревожное объяснение вместо общего баннера.
            val isLte = subscriptionRepository.selectedNode()?.name?.contains("LTE", ignoreCase = true) == true
            val bannerText = if (isLte) {
                Loc.s.bannerLteOnlyDuringJam
            } else {
                Loc.s.bannerActualCountry(geo?.displayName(Loc.lang) ?: country, previousDisplay)
            }
            _state.value = _state.value.copy(banner = bannerText, bannerKind = BannerKind.INFO)
        }
    }

    /** "Российский" узел для ухода при глушении — у бэкенда это именно узел автовыбора LTE
     *  (тот же критерий, что и в [maybeSwitchToLte]), а не поиск по geo.country == "Russia":
     *  сам узел автовыбора геометки не имел до правки CountryGeoLookup (алиас "lte" → Russia). */
    private fun russianNode(): VlessNode? =
        subscriptionRepository.nodes.value.firstOrNull { it.name.contains("LTE", ignoreCase = true) }

    private fun onNetworkChanged(net: NetState) {
        val prev = lastNetState
        lastNetState = net
        val s = _state.value
        _state.value = s.copy(netState = net)
        if (prev == null || prev == net) return

        if (net == NetState.JAMMED) {
            val ru = russianNode()
            if (ru != null) {
                val current = subscriptionRepository.selectedNode()
                if (current?.id != ru.id) {
                    viewModelScope.launch { settingsRepository.setPreferredNodeId(current?.id) }
                }
                subscriptionRepository.select(ru.id)
                xyz.gojihub.vpn.journal.NetworkJournal.log(xyz.gojihub.vpn.journal.NetworkJournal.Kind.SWITCH_JAMMED, stripLeadingFlag(ru.name))
                _state.value = _state.value.copy(
                    banner = Loc.s.bannerJammedSwitched(stripLeadingFlag(ru.name)),
                    bannerKind = BannerKind.WARNING
                )
            } else {
                _state.value = _state.value.copy(
                    banner = Loc.s.bannerJammedNoRuNode,
                    bannerKind = BannerKind.WARNING
                )
            }
        } else if (net == NetState.WIFI) {
            // "Включать VPN в чужих Wi-Fi" и доверенные сети — теперь в NetworkRulesManager
            // (работает и без открытого экрана, учитывает список доверенных сетей).
            val current = subscriptionRepository.selectedNode()
            val ru = russianNode()
            if (ru != null && current?.id == ru.id && preferredNodeId != null && preferredNodeId != ru.id) {
                subscriptionRepository.select(preferredNodeId!!)
                xyz.gojihub.vpn.journal.NetworkJournal.log(xyz.gojihub.vpn.journal.NetworkJournal.Kind.SWITCH_WIFI_RESTORED, subscriptionRepository.selectedNode()?.name?.let(::stripLeadingFlag).orEmpty())
                _state.value = _state.value.copy(banner = Loc.s.bannerWifiRestored, bannerKind = BannerKind.SUCCESS)
            }
        } else if (net == NetState.CELLULAR) {
            viewModelScope.launch { maybeSwitchToLte() }
        }
    }

    /** При появлении мобильной сети выбираем между двумя узлами-балансировщиками провайдера:
     *  «Автовыбор серверов EU», если хоть один европейский узел живой (сам балансировщик
     *  внутри переберёт резервные аутбаунды, см. PingRepository), и «Автовыбор LTE» (он
     *  настроен именно под мобильные сети), если по мобильной сети Европа вообще не пингуется.
     *  «Европейские» здесь = любой узел с реальной геометкой страны, кроме России (её уже
     *  обслуживает ветка JAMMED выше) — сам LTE- и EU-автовыбор геометки не имеют и в эту
     *  выборку не попадают. */
    private suspend fun maybeSwitchToLte() {
        val nodes = subscriptionRepository.nodes.value
        val lteNode = nodes.firstOrNull { it.name.contains("LTE", ignoreCase = true) } ?: return
        val euNode = nodes.firstOrNull { it.id != lteNode.id && it.geo == null && it.name.contains("EU") }
        val europeanNodes = nodes.filter { it.id != lteNode.id && it.geo != null && it.geo?.country != "Russia" }
        if (europeanNodes.isEmpty() || euNode == null) return

        pingRepository.pingAllInternal()
        val europeReachable = europeanNodes.any { (pingRepository.pings.value[it.id] ?: -1) >= 0 }
        val target = if (europeReachable) euNode else lteNode
        if (subscriptionRepository.selectedNode()?.id == target.id) return

        subscriptionRepository.select(target.id)
        xyz.gojihub.vpn.journal.NetworkJournal.log(
            if (europeReachable) xyz.gojihub.vpn.journal.NetworkJournal.Kind.SWITCH_CELLULAR_EU else xyz.gojihub.vpn.journal.NetworkJournal.Kind.SWITCH_CELLULAR_LTE,
            stripLeadingFlag(target.name)
        )
        _state.value = _state.value.copy(
            banner = if (europeReachable)
                Loc.s.bannerCellularSwitchedEu(stripLeadingFlag(euNode.name))
            else
                Loc.s.bannerCellularSwitchedLte(stripLeadingFlag(lteNode.name)),
            bannerKind = BannerKind.WARNING
        )
    }

    fun activateTrial() {
        if (_state.value.trialActivating) return
        _state.value = _state.value.copy(trialActivating = true)
        viewModelScope.launch {
            val error = subscriptionRepository.activateTrial()
            _state.value = _state.value.copy(
                trialActivating = false,
                banner = error ?: Loc.f.trialActivated,
                bannerKind = if (error == null) BannerKind.SUCCESS else BannerKind.WARNING
            )
        }
    }

    /** Неактивная подписка — частая причина «не подключается»: тогда ведём на «Подписку»,
     *  иначе — сменить сервер. */
    private fun errorActionFor(): ErrorAction =
        if (subscriptionRepository.subscription.value?.isActive == false) ErrorAction.OPEN_PLANS else ErrorAction.CHANGE_SERVER

    fun dismissBanner() {
        _state.value = _state.value.copy(banner = null)
    }

    fun startTunnel(context: Context) {
        val node = subscriptionRepository.selectedNode()
        if (node == null) {
            _state.value = _state.value.copy(error = Loc.s.noServerError, errorAction = errorActionFor())
            return
        }
        _state.value = _state.value.copy(error = null, errorAction = null, connecting = true, globeStatus = "connecting")
        val intent = Intent(context, GodjiVpnService::class.java).apply {
            action = GodjiVpnService.ACTION_CONNECT
            putExtra(GodjiVpnService.EXTRA_VLESS_LINK, node.connectPayload)
            putExtra(GodjiVpnService.EXTRA_NODE_LABEL, node.geo?.country ?: stripLeadingFlag(node.name))
        }
        context.startForegroundService(intent)
    }

    fun stopTunnel(context: Context) {
        val intent = Intent(context, GodjiVpnService::class.java).apply {
            action = GodjiVpnService.ACTION_DISCONNECT
        }
        context.startService(intent)
        _state.value = _state.value.copy(connecting = false)
    }

    private fun startSpeedPolling() {
        if (speedJob?.isActive == true) return
        speedJob = viewModelScope.launch {
            val uid = Process.myUid()
            var lastRx = TrafficStats.getUidRxBytes(uid)
            var lastTx = TrafficStats.getUidTxBytes(uid)
            var lastTime = System.currentTimeMillis()
            // Считаем сразу же, не дожидаясь первого тика delay(1000) — иначе при пересоздании
            // ViewModel (свернули приложение, процесс убила система) секунду-другую висел бы
            // старый ярлык таймера, пока не сработает первый цикл ниже.
            connectedSinceOrNow().let { start ->
                _state.value = _state.value.copy(connectedTimeLabel = formatElapsed(System.currentTimeMillis() - start))
            }
            while (isActive) {
                delay(1000)
                val now = System.currentTimeMillis()
                val rx = TrafficStats.getUidRxBytes(uid)
                val tx = TrafficStats.getUidTxBytes(uid)
                val dtSeconds = (now - lastTime).coerceAtLeast(1) / 1000.0
                val down = ((rx - lastRx).coerceAtLeast(0) / dtSeconds / 1_000_000.0)
                val up = ((tx - lastTx).coerceAtLeast(0) / dtSeconds / 1_000_000.0)
                _state.value = _state.value.copy(
                    downSpeedMbps = down,
                    upSpeedMbps = up,
                    // Скользящее окно последних точек для мини-графика в StatCard — секунды
                    // текущей сессии, не переживает пересоздание ViewModel, что тут и не нужно.
                    downHistory = (_state.value.downHistory + down.toFloat()).takeLast(SPEED_HISTORY_SIZE),
                    upHistory = (_state.value.upHistory + up.toFloat()).takeLast(SPEED_HISTORY_SIZE),
                    connectedTimeLabel = formatElapsed(now - connectedSinceOrNow())
                )
                lastRx = rx
                lastTx = tx
                lastTime = now
            }
        }
    }

    /** Настоящий момент подключения хранится в самом VPN-сервисе (connectedSinceMillis) —
     *  переживает пересоздание этого ViewModel, в отличие от локальной переменной. null
     *  теоретически возможен в узком окне между isRunning=true и записью таймстампа сервисом;
     *  подстраховка на "сейчас" не даёт таймеру не запуститься в этом окне. */
    private fun connectedSinceOrNow(): Long = GodjiVpnService.connectedSinceMillis.value ?: System.currentTimeMillis()

    private fun stopSpeedPolling() {
        speedJob?.cancel()
        speedJob = null
        _state.value = _state.value.copy(downSpeedMbps = 0.0, upSpeedMbps = 0.0, downHistory = emptyList(), upHistory = emptyList())
    }

    /** Раз в QUALITY_INTERVAL_MS — GET на адрес проверки пинга (Настройки → Пинг) через
     *  туннель. Только пока экран виден и VPN подключён (тот же переключатель, что у
     *  скорости), чтобы не тратить батарею в фоне. Первый удачный замер — с TLS-рукопожатием,
     *  он не показателен: сразу повторяем по уже открытому соединению. */
    private fun startQualityProbe() {
        if (qualityJob?.isActive == true) return
        qualityJob = viewModelScope.launch {
            val samples = ArrayDeque<Int>()
            var warmedUp = false
            while (isActive) {
                val ms = probeOnce(settingsRepository.pingTestUrlNow())
                if (!warmedUp && ms >= 0) { warmedUp = true; continue }
                warmedUp = true
                samples.addLast(ms)
                while (samples.size > QUALITY_WINDOW) samples.removeFirst()
                publishQuality(samples)
                delay(QUALITY_INTERVAL_MS)
            }
        }
    }

    private fun stopQualityProbe() {
        qualityJob?.cancel()
        qualityJob = null
        if (!GodjiVpnService.isRunning.value) {
            _state.value = _state.value.copy(qualityScore = null, qualityPingMs = null, qualityLossPct = 0)
        }
    }

    private suspend fun probeOnce(url: String): Int = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
        runCatching {
            val start = android.os.SystemClock.elapsedRealtime()
            probeClient.newCall(okhttp3.Request.Builder().url(url).build()).execute().use { r ->
                if (r.code in 200..399) (android.os.SystemClock.elapsedRealtime() - start).toInt() else -1
            }
        }.getOrDefault(-1)
    }

    private fun publishQuality(samples: Collection<Int>) {
        val ok = samples.filter { it >= 0 }.sorted()
        val loss = (samples.size - ok.size) * 100 / samples.size.coerceAtLeast(1)
        val rtt = ok.getOrNull(ok.size / 2)
        _state.value = _state.value.copy(
            qualityScore = qualityScore(rtt, loss),
            qualityPingMs = rtt,
            qualityLossPct = loss
        )
    }

    private fun formatElapsed(millis: Long): String {
        val totalSeconds = (millis / 1000).coerceAtLeast(0)
        val h = totalSeconds / 3600
        val m = (totalSeconds % 3600) / 60
        val s = totalSeconds % 60
        return "%02d:%02d:%02d".format(h, m, s)
    }
}
