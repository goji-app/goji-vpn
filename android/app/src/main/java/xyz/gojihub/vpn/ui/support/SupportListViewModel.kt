package xyz.gojihub.vpn.ui.support

import android.content.Context
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.network.RemnawaveApi
import xyz.gojihub.vpn.network.models.SupportTicketDto
import xyz.gojihub.vpn.util.AppLogger
import xyz.gojihub.vpn.util.LogCategory
import xyz.gojihub.vpn.util.formatDate
import javax.inject.Inject

private const val PAGE_SIZE = 20

data class SupportTicketUi(
    val id: Long,
    val title: String,
    val lastMessage: String?,
    val statusLabel: String,
    val isClosed: Boolean,
    val unreadCount: Int,
    val dateLabel: String?
)

data class SupportListUiState(
    val tab: String = "open",
    val tickets: List<SupportTicketUi> = emptyList(),
    val loading: Boolean = true,
    val error: Boolean = false,
    val canLoadMore: Boolean = false,
    val loadingMore: Boolean = false
)

/** Статус тикета → локализованная подпись; значения сверены с JS-бандлом веб-версии
 *  (customer-режим тикет-виджета). Неизвестный статус — просто выводим как есть, а не
 *  скрываем: лучше показать сырой текст, чем ничего. */
private fun SupportTicketDto.toUi(): SupportTicketUi {
    val statusLabel = when (status) {
        "open" -> Loc.s.support.supportStatusOpen
        "waiting_customer" -> Loc.s.support.supportStatusWaitingCustomer
        "awaiting_reply" -> Loc.s.support.supportStatusAwaitingReply
        "on_hold" -> Loc.s.support.supportStatusOnHold
        "closed" -> Loc.s.support.supportStatusClosed
        else -> status
    }
    return SupportTicketUi(
        id = id,
        title = subject?.takeIf { it.isNotBlank() } ?: Loc.s.support.supportNewTicket,
        lastMessage = lastMessage,
        statusLabel = statusLabel,
        isClosed = status == "closed",
        unreadCount = unreadCount,
        dateLabel = createdAt?.let(::formatDate)
    )
}

@HiltViewModel
class SupportListViewModel @Inject constructor(
    private val api: RemnawaveApi,
    @ApplicationContext private val appContext: Context
) : ViewModel() {

    private val _state = MutableStateFlow(SupportListUiState())
    val state: StateFlow<SupportListUiState> = _state

    init {
        load()
    }

    fun selectTab(tab: String) {
        if (tab == _state.value.tab) return
        _state.value = _state.value.copy(tab = tab)
        load()
    }

    fun refresh() = load()

    private fun load() {
        _state.value = _state.value.copy(loading = true, error = false)
        viewModelScope.launch {
            runCatching { api.getSupportTickets(status = _state.value.tab, limit = PAGE_SIZE, offset = 0) }
                .onSuccess { response ->
                    val tickets = response.tickets.orEmpty().map { it.toUi() }
                    _state.value = _state.value.copy(
                        tickets = tickets,
                        loading = false,
                        error = false,
                        canLoadMore = tickets.size >= PAGE_SIZE
                    )
                }
                .onFailure {
                    AppLogger.e(appContext, LogCategory.MAIN, "SupportList", "getSupportTickets failed", it)
                    _state.value = _state.value.copy(loading = false, error = _state.value.tickets.isEmpty())
                }
        }
    }

    fun loadMore() {
        if (_state.value.loadingMore || !_state.value.canLoadMore) return
        _state.value = _state.value.copy(loadingMore = true)
        viewModelScope.launch {
            runCatching {
                api.getSupportTickets(status = _state.value.tab, limit = PAGE_SIZE, offset = _state.value.tickets.size)
            }.onSuccess { response ->
                val more = response.tickets.orEmpty().map { it.toUi() }
                _state.value = _state.value.copy(
                    tickets = _state.value.tickets + more,
                    loadingMore = false,
                    canLoadMore = more.size >= PAGE_SIZE
                )
            }.onFailure {
                _state.value = _state.value.copy(loadingMore = false)
            }
        }
    }
}
