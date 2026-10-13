package xyz.gojihub.vpn.ui.servers

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import xyz.gojihub.vpn.geo.CountryGeoLookup
import xyz.gojihub.vpn.settings.ServerSort
import xyz.gojihub.vpn.settings.SettingsRepository
import java.text.Collator
import java.util.Locale
import xyz.gojihub.vpn.subscription.PingRepository
import xyz.gojihub.vpn.subscription.SubscriptionRepository
import xyz.gojihub.vpn.util.stripLeadingFlag
import javax.inject.Inject

data class RefreshMessage(val isError: Boolean)

data class NodeUi(
    val id: String,
    val name: String,
    val code: String,
    val flag: String,
    val host: String,
    val port: Int,
    val pingMs: Int,
    val isFavorite: Boolean = false
)

data class ServersUiState(
    val servers: List<NodeUi> = emptyList(),
    val selectedId: String? = null,
    val checkingId: String? = null,
    val checkingAll: Boolean = false,
    val loading: Boolean = true,
    val sort: ServerSort = ServerSort.FAVORITES
)

@HiltViewModel
class ServersViewModel @Inject constructor(
    private val subscriptionRepository: SubscriptionRepository,
    private val pingRepository: PingRepository,
    private val settingsRepository: SettingsRepository
) : ViewModel() {

    private val baseState = combine(
        subscriptionRepository.nodes,
        subscriptionRepository.selectedId,
        pingRepository.pings,
        pingRepository.checkingId,
        pingRepository.checkingAll
    ) { nodes, selectedId, pings, checkingId, checkingAll ->
        ServersUiState(
            servers = nodes.map {
                NodeUi(
                    id = it.id, name = stripLeadingFlag(it.name),
                    code = it.geo?.code ?: "??",
                    flag = it.geo?.code?.let(CountryGeoLookup::flagEmoji) ?: "🌐",
                    host = it.host, port = it.port, pingMs = pings[it.id] ?: -2
                )
            },
            selectedId = selectedId,
            checkingId = checkingId,
            checkingAll = checkingAll,
            loading = false
        )
    }

    // combine() у kotlinx.coroutines есть готовым только до 5 потоков — избранное добавляем
    // отдельным вторым combine поверх уже собранного состояния, а не переписываем всё на
    // вариант с Array<Flow<*>> ради одного лишнего источника.
    val state: StateFlow<ServersUiState> = combine(
        baseState, settingsRepository.favoriteServerIds, settingsRepository.serverSort
    ) { base, favorites, sort ->
        val marked = base.servers.map { it.copy(isFavorite = it.id in favorites) }
        base.copy(servers = sortServers(marked, sort), sort = sort)
    }.stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), ServersUiState())

    /** Сортировки стабильные: при равенстве остаётся порядок подписки. Избранные всегда
     *  идут первыми только в режиме FAVORITES — в остальных режимах порядок строго по ключу. */
    private fun sortServers(list: List<NodeUi>, sort: ServerSort): List<NodeUi> = when (sort) {
        ServerSort.FAVORITES -> list.sortedByDescending { it.isFavorite }
        // -2 — ещё не проверен, -1 — недоступен: оба в конец, недоступные — самыми последними.
        ServerSort.PING -> list.sortedWith(compareBy<NodeUi> {
            when {
                it.pingMs >= 0 -> 0
                it.pingMs == -2 -> 1
                else -> 2
            }
        }.thenBy { if (it.pingMs >= 0) it.pingMs else Int.MAX_VALUE })
        ServerSort.NAME -> {
            val collator = Collator.getInstance(Locale.getDefault())
            list.sortedWith { a, b -> collator.compare(a.name, b.name) }
        }
    }

    fun setSort(sort: ServerSort) = viewModelScope.launch { settingsRepository.setServerSort(sort) }

    private val _refreshingSubscription = MutableStateFlow(false)
    val refreshingSubscription: StateFlow<Boolean> = _refreshingSubscription.asStateFlow()

    /** Результат ручного обновления подписки — показываем собственным баннером (в стиле
     *  приложения, не системным Toast) и прячем его сами через задержку ниже. */
    private val _refreshMessage = MutableStateFlow<RefreshMessage?>(null)
    val refreshMessage: StateFlow<RefreshMessage?> = _refreshMessage.asStateFlow()
    private var refreshMessageJob: Job? = null

    init {
        viewModelScope.launch {
            subscriptionRepository.refreshIfStale()
            pingRepository.pingAllInternalIfStale()
        }
    }

    fun select(id: String) = subscriptionRepository.select(id)

    fun pingOne(id: String) = pingRepository.pingOne(id)

    fun pingAll() = pingRepository.pingAll()

    fun pingAllIfStale() = pingRepository.pingAllIfStale()

    fun toggleFavorite(id: String) = viewModelScope.launch { settingsRepository.toggleFavoriteServer(id) }

    /** Ручное обновление подписки (список серверов мог поменяться на бэкенде) — отдельная
     *  кнопка от "обновить пинг", с уведомлением об успехе/ошибке. */
    fun refreshSubscription() {
        if (_refreshingSubscription.value) return
        viewModelScope.launch {
            _refreshingSubscription.value = true
            val ok = subscriptionRepository.refresh()
            if (ok) pingRepository.pingAllInternal()
            _refreshingSubscription.value = false
            showRefreshMessage(RefreshMessage(isError = !ok))
        }
    }

    private fun showRefreshMessage(message: RefreshMessage) {
        refreshMessageJob?.cancel()
        _refreshMessage.value = message
        refreshMessageJob = viewModelScope.launch {
            delay(4500)
            _refreshMessage.value = null
        }
    }

    fun dismissRefreshMessage() {
        refreshMessageJob?.cancel()
        _refreshMessage.value = null
    }
}
