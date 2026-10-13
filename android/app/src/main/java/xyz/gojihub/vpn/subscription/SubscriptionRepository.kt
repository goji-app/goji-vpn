package xyz.gojihub.vpn.subscription

import android.content.Context
import android.content.Intent
import android.os.Build
import android.provider.Settings
import android.util.Base64
import androidx.core.content.ContextCompat
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import org.json.JSONArray
import xyz.gojihub.vpn.BuildConfig
import xyz.gojihub.vpn.geo.CountryGeoLookup
import xyz.gojihub.vpn.geo.CountryGeo
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.network.RemnawaveApi
import xyz.gojihub.vpn.network.models.BroadcastDto
import xyz.gojihub.vpn.network.models.SubscriptionInfo
import xyz.gojihub.vpn.util.AppLogger
import xyz.gojihub.vpn.util.LogCategory
import xyz.gojihub.vpn.vpn.GodjiVpnService
import xyz.gojihub.vpn.widget.GojiWidgetProvider
import java.net.URI
import java.net.URLDecoder
import javax.inject.Inject
import javax.inject.Singleton

/** Один сервер внутри ссылки подписки пользователя. [geo] — определена по тексту
 *  remark (см. CountryGeoLookup), может быть null.
 *  [connectPayload] — то, что нужно передать в GodjiVpnService для подключения: либо
 *  целиком готовый Xray-конфиг (dns/routing/outbounds, JSON-объект — реальный формат
 *  этого бэкенда при переданном X-HWID), либо, для обратной совместимости, сырая
 *  vless://-ссылка, которую сервис прогонит через libXray.convertShareLinksToXrayJson. */
data class VlessNode(
    val id: String,
    val name: String,
    val host: String,
    val port: Int,
    val connectPayload: String,
    val geo: CountryGeo? = null,
    /** UUID клиента в Remnawave (settings.vnext[].id у VLESS-аутбаунда) — тот же для всех
     *  профилей одного пользователя, реальный Remnawave-идентификатор для поддержки, в отличие
     *  от customer_id шоп-бэкенда (не совпадает с тем, что видно в панели Remnawave). */
    val uuid: String? = null
)

fun VlessNode.toGlobeNode(): xyz.gojihub.vpn.globe.GlobeNode? =
    geo?.let { xyz.gojihub.vpn.globe.GlobeNode(id, it.lat, it.lon, it.country) }

/**
 * Ссылка подписки (subscription_link из /api/subscriptions) без клиентского заголовка
 * X-HWID отдаёт лишь заглушку ("Приложение не поддерживается") — сервер считает
 * устройство непривязанным. С X-HWID он отдаёт настоящий список серверов: JSON-массив
 * готовых профилей Xray (dns/routing/outbounds на VLESS+Reality+XHTTP), а не привычный
 * base64 со списком vless://-ссылок — этот старый формат оставлен как запасной вариант.
 */
@Singleton
class SubscriptionRepository @Inject constructor(
    private val api: RemnawaveApi,
    @ApplicationContext private val appContext: Context,
    private val accountCache: AccountCache,
    private val tokenManager: xyz.gojihub.vpn.auth.TokenManager
) {
    // Тот же приём, что и в NetworkModule: пока туннель поднят, гоняем запрос списка серверов
    // через локальный SOCKS самого Xray — иначе он идёт по сырой сети телефона и падает в зоне
    // глушения мобильной сети даже при уже подключённом VPN на российский узел.
    private val plainClient = OkHttpClient.Builder()
        .proxySelector(GodjiVpnService.tunnelAwareProxySelector())
        .build()

    // Только для фоновой записи в NodeListCache из select() — сама функция не suspend
    // (вызывается напрямую из UI-обработчиков клика).
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    private val hwid: String by lazy {
        Settings.Secure.getString(appContext.contentResolver, Settings.Secure.ANDROID_ID)
            ?: "godji-${System.identityHashCode(appContext)}"
    }

    /** То же значение X-HWID, что уходит на бэкенд — для экрана "О программе". */
    fun hwidNow(): String = hwid

    // Последняя известная подписка с диска (см. AccountCache) — «Главная», «Подписка» и виджеты
    // показывают её сразу после запуска, не дожидаясь сети.
    private val _subscription = MutableStateFlow(accountCache.load<SubscriptionInfo>(AccountCache.Key.SUBSCRIPTION)?.value)
    val subscription: StateFlow<SubscriptionInfo?> = _subscription.asStateFlow()

    /** Пробный период, который можно включить прямо из приложения (только у аккаунта без
     *  подписки и только бесплатный — платный триал в Play-сборке предлагать нельзя).
     *  Значение — число дней (0, если бэкенд его не сообщил), null — предлагать нечего. */
    private val _trialDays = MutableStateFlow<Int?>(null)
    val trialDays: StateFlow<Int?> = _trialDays.asStateFlow()

    // Поколение предложения триала: ответ getDashboard, ушедший до активации/выхода, не должен
    // вернуть уже скрытую карточку.
    @Volatile private var trialGeneration = 0

    /** Включает пробный период. null — успех, иначе текст ошибки (сообщение бэкенда, если есть).
     *  Обновление подписки после ответа идёт в собственном scope репозитория: уход с экрана не
     *  должен оставить активированный на сервере триал без обновлённой подписки. */
    suspend fun activateTrial(): String? {
        val result = try {
            api.activateTrial()
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            AppLogger.e(appContext, LogCategory.SUBSCRIPTION, "GodjiSub", "activateTrial failed", e)
            return Loc.f.trialError
        }
        val body = result.body()
        if (result.isSuccessful && body?.success != false) {
            AppLogger.i(appContext, LogCategory.SUBSCRIPTION, "GodjiSub", "trial activated")
            trialGeneration++
            _trialDays.value = null
            scope.launch { refreshMutex.withLock { refresh() } }
            return null
        }
        val message = body?.message?.takeIf { it.isNotBlank() } ?: errorMessage(result.errorBody()?.string())
        AppLogger.w(appContext, LogCategory.SUBSCRIPTION, "GodjiSub", "trial rejected: ${result.code()} $message")
        // Отказ («уже использован», «недоступен») — перепроверяем, предлагать ли карточку дальше.
        if (result.code() != 401) scope.launch { refreshTrialOffer(_subscription.value != null) }
        return message ?: Loc.f.trialError
    }

    private fun errorMessage(raw: String?): String? = runCatching {
        val json = org.json.JSONObject(raw.orEmpty())
        listOf("message", "error").firstNotNullOfOrNull { key ->
            (json.opt(key) as? String)?.trim()?.takeIf { it.isNotEmpty() && it.length <= 300 }
        }
    }.getOrNull()

    private suspend fun refreshTrialOffer(hasSubscription: Boolean) {
        val generation = trialGeneration
        if (hasSubscription) { _trialDays.value = null; return }
        val dashboard = try {
            api.getDashboard()
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            AppLogger.e(appContext, LogCategory.SUBSCRIPTION, "GodjiSub", "getDashboard failed", e)
            return
        }
        if (generation != trialGeneration) return
        _trialDays.value = if (dashboard.trialAvailable == true && dashboard.trialPaid != true) dashboard.trialDays ?: 0 else null
    }

    /** Выход из аккаунта: в памяти не должно остаться ни подписки, ни новостей, ни предложения
     *  триала прежнего аккаунта, а следующий вход — сразу обновить данные, не дожидаясь часа. */
    fun clearAccount() {
        trialGeneration++
        lastRefreshAt = 0L
        lastRefreshOk = false
        refreshedForToken = null
        _subscription.value = null
        _trialDays.value = null
        _broadcasts.value = emptyList()
    }

    // Новости/рассылки (см. RemnawaveApi.getBroadcasts) — та же страница, что "Мои рассылки"
    // веб-версии (#/my-broadcasts). Тоже с диска: без этого раздел новостей на «Подписке»
    // пустовал при каждом запуске без сети.
    private val _broadcasts = MutableStateFlow(accountCache.load<List<BroadcastDto>>(AccountCache.Key.BROADCASTS)?.value.orEmpty())
    val broadcasts: StateFlow<List<BroadcastDto>> = _broadcasts.asStateFlow()

    // Заполняем из диска ДО первого сетевого запроса — как в Happ/Incy: если кабинет или
    // сервер подписки недоступны прямо на старте (сайт/панель Remnawave легли, глушение сети
    // и т.п.), пользователь видит последний известный список серверов и может подключиться,
    // а не пустой экран "Серверы" с нулём узлов. refresh() ниже перезапишет этот список свежим,
    // как только сеть появится.
    private val cachedOnDisk = NodeListCache.load(appContext)

    private val _nodes = MutableStateFlow(cachedOnDisk?.nodes.orEmpty())
    val nodes: StateFlow<List<VlessNode>> = _nodes.asStateFlow()

    private val _selectedId = MutableStateFlow(cachedOnDisk?.selectedId)
    val selectedId: StateFlow<String?> = _selectedId.asStateFlow()

    // Автоматические обновления (открытие приложения: Главная, Серверы, Подписка и возврат на
    // передний план — почти одновременно) раньше шли 3–4 раза подряд, каждое — 4 сетевых
    // запроса и разбор списка узлов. refreshIfStale() склеивает одновременные вызовы в один и
    // пропускает обновление, если свежее было меньше REFRESH_MIN_INTERVAL_MS назад.
    private val refreshMutex = kotlinx.coroutines.sync.Mutex()
    // Момент последнего обновления переживает перезапуск процесса — иначе каждый холодный старт
    // (Android регулярно выгружает приложение) заново тянул подписку, хотя ей несколько минут.
    private val refreshPrefs = appContext.getSharedPreferences("subscription_refresh", Context.MODE_PRIVATE)
    @Volatile private var lastRefreshAt = refreshPrefs.getLong("last_refresh_at", 0L)
    @Volatile private var lastRefreshOk = lastRefreshAt > 0L
    // Для какой сессии было последнее обновление. Вход в аккаунт (или в другой аккаунт) меняет
    // токен — тогда данные считаются устаревшими сразу, а не через час. Раньше неудачная попытка
    // до входа (запуск приложения без аккаунта) на час блокировала обновление после входа, и
    // новичок не видел ни подписку, ни карточку пробного периода.
    @Volatile private var refreshedForToken: String? = if (lastRefreshAt > 0L) tokenManager.accessToken() else null

    private fun isFresh(maxAgeMs: Long): Boolean {
        if (refreshedForToken != tokenManager.accessToken()) return false
        val age = System.currentTimeMillis() - lastRefreshAt
        // Неудачу (нет сети) повторяем через минуту, а не через час.
        return age < (if (lastRefreshOk) maxAgeMs else minOf(maxAgeMs, FAILED_RETRY_MS))
    }

    suspend fun refreshIfStale(maxAgeMs: Long = REFRESH_MIN_INTERVAL_MS): Boolean {
        // Без аккаунта запрашивать нечего — и такая попытка не должна считаться обновлением.
        if (!tokenManager.isLoggedIn()) return false
        if (isFresh(maxAgeMs)) return lastRefreshOk
        return refreshMutex.withLock {
            // Пока ждали, другой вызов мог уже обновить — его результат и вернём.
            if (isFresh(maxAgeMs)) lastRefreshOk else refresh()
        }
    }

    /** @return true, если подписку удалось реально получить с бэкенда (для UI ручного
     *  обновления — показать "обновлено" или ошибку сети/сервера). */
    suspend fun refresh(): Boolean = refreshInternal().also {
        lastRefreshOk = it
        lastRefreshAt = System.currentTimeMillis()
        refreshedForToken = tokenManager.accessToken()
        // Неудачную попытку не запоминаем на диске: после перезапуска стоит попробовать снова.
        if (it) refreshPrefs.edit().putLong("last_refresh_at", lastRefreshAt).apply()
    }

    private suspend fun refreshInternal(): Boolean {
        val response = runCatching { api.getSubscriptions() }
            .onFailure {
                if (BuildConfig.DEBUG) android.util.Log.e("GodjiSub", "getSubscriptions failed", it)
                AppLogger.e(appContext, LogCategory.SUBSCRIPTION, "GodjiSub", "getSubscriptions failed", it)
            }
            .getOrNull()
        var active = response?.subscriptions
            ?.let { list -> list.firstOrNull { it.isPrimary } ?: list.firstOrNull() }

        // С бэкенда 7.1.0 список выше больше не содержит traffic (подтверждено живым
        // запросом) — дозапрашиваем полную запись по id, чтобы на экранах "Защита"/"Подписка"
        // по-прежнему показывался реальный расход трафика, а не 0/безлимит по умолчанию.
        // Сбой этого отдельного запроса не должен откатывать уже полученную active — тогда
        // просто останется без трафика до следующего refresh(), а не пропадёт совсем.
        val toEnrich = active
        if (toEnrich != null && toEnrich.traffic == null) {
            active = runCatching { api.getSubscription(toEnrich.id) }
                .onFailure { AppLogger.e(appContext, LogCategory.SUBSCRIPTION, "GodjiSub", "getSubscription(${toEnrich.id}) failed", it) }
                .getOrNull() ?: toEnrich
        }
        // Запрос не прошёл (нет сети, VPN переподключается) — оставляем последнюю известную
        // подписку. Раньше сюда записывался null, и «Главная»/«Подписка» внезапно показывали
        // «0 дней» и «—» до следующего удачного обновления. null — только если бэкенд реально
        // ответил, что подписок нет.
        if (response != null) {
            _subscription.value = active
            withContext(Dispatchers.IO) {
                if (active != null) accountCache.save(AccountCache.Key.SUBSCRIPTION, active)
            }
            // Отдельно и параллельно: лишний запрос сводки кабинета не должен задерживать
            // новости и список серверов.
            val hasSubscription = active != null
            scope.launch { refreshTrialOffer(hasSubscription) }
        }
        // Уведомления о скором окончании подписки/успешной оплате — считаются здесь, а не в
        // отдельных вызывающих местах (воркер + 3 ViewModel), чтобы сработать при любом
        // источнике обновления, а не только раз в час в фоне.
        active?.let { SubscriptionNotifier.check(appContext, it) }

        // Не завязано на наличие активной подписки — новости могут быть релевантны и до
        // покупки тарифа. Отдельная от подписки/серверов ошибка не должна прерывать remainder
        // refresh(), поэтому просто логируется и пропускается.
        runCatching { api.getBroadcasts() }
            .onFailure { AppLogger.e(appContext, LogCategory.SUBSCRIPTION, "GodjiSub", "getBroadcasts failed", it) }
            .getOrNull()
            ?.let { list ->
                _broadcasts.value = list
                withContext(Dispatchers.IO) { accountCache.save(AccountCache.Key.BROADCASTS, list) }
                BroadcastNotifier.check(appContext, list)
            }

        // кабинет (наш шоп-бэкенд, откуда обычно приходит эта ссылка) и сервер подписки
        // (сам Remnawave, куда она указывает) — два независимых хоста на разной инфраструктуре.
        // Раньше недоступность ПЕРВОГО (например точечная блокировка его IP у конкретного
        // оператора — реально наблюдалось: ConnectException на кабинете при живом
        // сервер подписки) обрывала refresh() целиком, даже не пытаясь дойти до второго —
        // список серверов переставал обновляться, хотя сам Remnawave был всё это время
        // доступен напрямую (тот же путь, что использует любой сторонний v2ray-клиент —
        // Happ/Incy/v2rayNG, — которому просто один раз вручную вставили эту ссылку и который
        // вообще не знает о существовании кабинета). Теперь при сбое getSubscriptions()
        // берём последнюю успешно полученную ссылку из кэша и всё равно пробуем — единственный
        // случай без какого-либо выхода остаётся "ни разу не было ни одного успешного refresh()
        // за всё время" (кэш пуст).
        val freshLink = active?.subscriptionLink
        val link = freshLink ?: NodeListCache.load(appContext)?.subscriptionLink ?: return false
        val nodes = fetchNodes(link)
        // Пустой список из fetchNodes означает "не удалось получить" (см. runCatching там же),
        // а не "в подписке теперь ноль узлов" — если затирать им текущий список при временном
        // сбое сети (например сервер подписки недоступен через текущий узел, а сама подписка
        // в кабинете получена), пользователь на экране "Серверы" внезапно теряет и активный,
        // и вообще все узлы, хотя реальное VPN-подключение всё это время работает как ни в чём
        // не бывало. Оставляем прежний список нетронутым, пока не придёт непустой новый.
        if (nodes.isNotEmpty()) {
            _nodes.value = nodes
            if (_selectedId.value == null || nodes.none { it.id == _selectedId.value }) {
                _selectedId.value = nodes.firstOrNull()?.id
            }
            // Подтягиваем и сохраняем полные клиентские JSON-профили узлов на диск — не только
            // держим в памяти на момент подключения (см. ClientProfileStorage), плюс сам список
            // (имя/geo/uuid) — для офлайн-показа на случай следующего запуска без сети.
            withContext(Dispatchers.IO) {
                ClientProfileStorage.saveAll(appContext, nodes)
                NodeListCache.save(appContext, nodes, _selectedId.value, freshLink)
            }
        }
        return freshLink != null || nodes.isNotEmpty()
    }

    private companion object {
        // Автоматические обновления (открытие приложения/экранов) — не чаще раза в час;
        // фоновый воркер и так обновляет подписку ежечасно, а кнопка «обновить» — сразу.
        const val REFRESH_MIN_INTERVAL_MS = 60 * 60_000L
        /** Повтор неудачного обновления (нет сети) — через минуту, а не через REFRESH_MIN_INTERVAL_MS. */
        const val FAILED_RETRY_MS = 60_000L
    }

    fun select(id: String) {
        val previousId = _selectedId.value
        _selectedId.value = id
        scope.launch { NodeListCache.save(appContext, _nodes.value, id) }
        GojiWidgetProvider.refresh(appContext)
        // Раньше select() менял только "выбранный" узел в списке — сам туннель, если он уже был
        // поднят, продолжал молча работать через старый outbound. UI при этом показывал новую
        // страну (ConnectViewModel.updateNodeDependentState() берёт имя/гео из selectedId), хотя
        // реальный исходящий IP не менялся до ручного отключения-подключения. Явно дёргаем сервис
        // на реальный реконнект, если он уже запущен и узел действительно другой.
        if (previousId != id && GodjiVpnService.isRunning.value) {
            _nodes.value.firstOrNull { it.id == id }?.let(::reconnectToNode)
        }
    }

    private fun reconnectToNode(node: VlessNode) {
        val intent = Intent(appContext, GodjiVpnService::class.java).apply {
            action = GodjiVpnService.ACTION_CONNECT
            putExtra(GodjiVpnService.EXTRA_VLESS_LINK, node.connectPayload)
            putExtra(GodjiVpnService.EXTRA_NODE_LABEL, node.geo?.country ?: node.name)
        }
        ContextCompat.startForegroundService(appContext, intent)
    }

    fun selectedNode(): VlessNode? = _nodes.value.firstOrNull { it.id == _selectedId.value }

    /** UUID клиента в Remnawave — одинаков для всех профилей подписки, берём из первого. */
    fun clientUuid(): String? = _nodes.value.firstNotNullOfOrNull { it.uuid }

    private suspend fun fetchNodes(url: String): List<VlessNode> = withContext(Dispatchers.IO) {
        runCatching {
            // User-Agent/X-HWID намеренно не трогаем — бэкенд по User-Agent "v2rayNG/1.8.29"
            // определяет, что клиенту нужно отдавать настоящий JSON-формат профилей (см.
            // комментарий класса); замена на что-то своё возвращала бы старый base64-формат.
            // Название приложения/ОС/модель устройства/версию приложения передаём отдельными
            // заголовками — не мешают распознаванию клиента, но дают бэкенду показать в списке
            // устройств не голый HWID, а что это реально Godji app такой-то версии на таком-то
            // устройстве и версии Android.
            val request = Request.Builder()
                .url(url)
                .header("User-Agent", "v2rayNG/1.8.29")
                .header("X-HWID", hwid)
                .header("X-Device-OS", "Android")
                .header("X-Device-OS-Version", Build.VERSION.RELEASE ?: Build.VERSION.SDK_INT.toString())
                // Имя заголовка версии ОС, которое читает Remnawave (os_version в списке устройств).
                .header("X-Ver-OS", Build.VERSION.RELEASE ?: Build.VERSION.SDK_INT.toString())
                .header("X-Device-Model", "${Build.MANUFACTURER} ${Build.MODEL}".trim())
                .header("X-App-Name", "Goji app")
                .header("X-App-Version", BuildConfig.VERSION_NAME)
                .build()
            plainClient.newCall(request).execute().use { response ->
                val raw = response.body?.string().orEmpty()
                parseJsonProfiles(raw) ?: parseVlessText(raw)
            }
        }.onFailure {
            if (BuildConfig.DEBUG) android.util.Log.e("GodjiSub", "fetchNodes failed", it)
            AppLogger.e(appContext, LogCategory.SUBSCRIPTION, "GodjiSub", "fetchNodes failed", it)
        }.getOrElse { emptyList() }
    }

    /** Протоколы, которые Xray-core (через libXray) умеет поднимать как proxy-outbound — тот
     *  же список, что и в Windows-клиенте (см. SubscriptionService.ProxyProtocols там), и то же
     *  разделение по формату settings ниже. TUIC сюда не входит — xray-core его не поддерживает
     *  ни в каком виде, это отдельная, не связанная с этой правкой задача. */
    private val proxyProtocols = setOf("vless", "vmess", "trojan", "shadowsocks", "hysteria")

    /** Реальный формат: JSON-массив готовых профилей `{remarks, dns, routing, outbounds, ...}`.
     *
     *  Раньше здесь распознавался только формат settings.vnext (VLESS/VMess) — Trojan/
     *  Shadowsocks/Hysteria-узлы от Remnawave молча пропадали из списка серверов ещё на этом
     *  этапе парсинга, хотя establishTunnel() передаёт весь profile.toString() как есть и
     *  Xray-core прекрасно умеет их поднимать (сам движок тут ни при чём — это чисто клиентский
     *  парсинг списка для UI/пинга). Три разные структуры settings:
     *   - VLESS/VMess: settings.vnext[0] (адрес+порт+users[].id — отдельный UUID клиента);
     *   - Trojan/Shadowsocks: settings.servers[0] (адрес+порт+пароль/метод, без UUID);
     *   - Hysteria (v2): settings.address/settings.port ПРЯМО в settings (не в массиве), пароль
     *     — отдельно, в streamSettings.hysteriaSettings.auth, для списка серверов не нужен. */
    private fun parseJsonProfiles(raw: String): List<VlessNode>? = runCatching {
        val array = JSONArray(raw.trim())
        (0 until array.length()).mapNotNull { index ->
            val profile = array.getJSONObject(index)
            val outbounds = profile.optJSONArray("outbounds") ?: return@mapNotNull null
            val proxyOutbound = (0 until outbounds.length())
                .map { outbounds.getJSONObject(it) }
                .firstOrNull { ob ->
                    ob.optString("protocol") in proxyProtocols &&
                        ob.optJSONObject("settings")?.let { s ->
                            s.optJSONArray("vnext") != null || s.optJSONArray("servers") != null || s.has("address")
                        } == true
                }
                ?: return@mapNotNull null
            val settings = proxyOutbound.getJSONObject("settings")
            val vnext = settings.optJSONArray("vnext")?.optJSONObject(0)
            val server = settings.optJSONArray("servers")?.optJSONObject(0)
            val host: String
            val port: Int
            val uuid: String?
            when {
                vnext != null -> {
                    host = vnext.optString("address").takeIf { it.isNotBlank() } ?: return@mapNotNull null
                    port = vnext.optInt("port").takeIf { it > 0 } ?: 443
                    uuid = vnext.optJSONArray("users")?.optJSONObject(0)?.optString("id")?.takeIf { it.isNotBlank() }
                }
                server != null -> {
                    host = server.optString("address").takeIf { it.isNotBlank() } ?: return@mapNotNull null
                    port = server.optInt("port").takeIf { it > 0 } ?: 443
                    uuid = null
                }
                else -> {
                    host = settings.optString("address").takeIf { it.isNotBlank() } ?: return@mapNotNull null
                    port = settings.optInt("port").takeIf { it > 0 } ?: 443
                    uuid = null
                }
            }
            val remark = profile.optString("remarks").takeIf { it.isNotBlank() } ?: Loc.s.fallbackServerName(index + 1)
            // ID узла раньше был просто позицией в JSON-массиве (index.toString()) — а
            // _selectedId (выбор пользователя) кэшируется на диск и переживает перезапуск
            // приложения и обновление версии (см. NodeListCache/cachedOnDisk выше). Пока набор
            // профилей не менялся между запросами — совпадение позиции с реальным сервером
            // держалось случайно. Но стоило поменяться КОЛИЧЕСТВУ включаемых профилей (ровно
            // это и произошло в 1.0.72, когда сюда добавили распознавание Trojan/Shadowsocks/
            // Hysteria — раньше такие узлы молча выпадали из списка, теперь попадают в него),
            // как позиции всех последующих узлов в массиве сдвинулись, и старый сохранённый id
            // стал молча указывать на СОВСЕМ ДРУГОЙ сервер — тот же класс проблемы возникнет и
            // от любого будущего изменения порядка/состава профилей на бэкенде. Проверка на
            // "id вообще существует в новом списке" (см. refresh() ниже) это не ловит: индекс
            // как правило и после сдвига продолжает существовать, просто указывает не туда.
            // remark+host:port — стабильный ключ конкретного сервера, не зависящий ни от
            // порядка профилей в ответе, ни от того, что ещё попадает в фильтр протоколов.
            VlessNode(
                id = "$remark|$host:$port",
                name = remark,
                host = host,
                port = port,
                connectPayload = profile.toString(),
                geo = CountryGeoLookup.find(remark),
                uuid = uuid
            )
        }
    }.getOrNull()?.takeIf { it.isNotEmpty() }

    /** Запасной формат (легаси): base64 со списком vless://-ссылок построчно. */
    private fun parseVlessText(raw: String): List<VlessNode> {
        val decoded = runCatching {
            String(Base64.decode(raw.trim(), Base64.DEFAULT), Charsets.UTF_8)
        }.getOrDefault(raw)
        return decoded.lineSequence()
            .map { it.trim() }
            .filter { it.startsWith("vless://") }
            .mapIndexedNotNull { index, line -> parseVless(index, line) }
            .toList()
    }

    private fun parseVless(index: Int, link: String): VlessNode? = runCatching {
        val uri = URI(link)
        val host = uri.host ?: return null
        val port = if (uri.port > 0) uri.port else 443
        val remark = uri.rawFragment
            ?.let { URLDecoder.decode(it, "UTF-8") }
            ?.takeIf { it.isNotBlank() }
            ?: Loc.s.fallbackServerName(index + 1)
        val uuid = uri.userInfo?.takeIf { it.isNotBlank() }
        VlessNode(id = index.toString(), name = remark, host = host, port = port, connectPayload = link, geo = CountryGeoLookup.find(remark), uuid = uuid)
    }.getOrNull()
}
