package xyz.gojihub.vpn.ui.support

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.network.RemnawaveApi
import xyz.gojihub.vpn.network.models.CreateSupportTicketRequest
import xyz.gojihub.vpn.network.models.SupportQueueDto
import javax.inject.Inject

data class NewTicketUiState(
    val loadingQueues: Boolean = true,
    val queues: List<SupportQueueDto> = emptyList(),
    val selectedQueueId: Long? = null,
    val subject: String = "",
    val message: String = "",
    val submitting: Boolean = false,
    val error: String? = null,
    // Проверка /api/support/ticket-limit ДО отправки — то же самое, что делает веб-версия
    // (см. UserSupportPage): не даём даже начать заполнять форму, если уже есть активное
    // обращение и лимит новых исчерпан, а сразу предлагаем перейти в него.
    val limitReached: Boolean = false,
    val activeTicketId: Long? = null
)

@HiltViewModel
class NewTicketViewModel @Inject constructor(
    private val api: RemnawaveApi
) : ViewModel() {

    private val _state = MutableStateFlow(NewTicketUiState())
    val state: StateFlow<NewTicketUiState> = _state

    init {
        viewModelScope.launch {
            runCatching { api.getSupportTicketLimit() }.onSuccess { limit ->
                if (!limit.canCreate) {
                    _state.value = _state.value.copy(limitReached = true, activeTicketId = limit.activeTicketId)
                }
            }
        }
        viewModelScope.launch {
            runCatching { api.getSupportQueues() }
                .onSuccess { queues -> _state.value = _state.value.copy(queues = queues.orEmpty(), loadingQueues = false) }
                .onFailure { _state.value = _state.value.copy(loadingQueues = false) }
        }
    }

    fun setSubject(value: String) {
        _state.value = _state.value.copy(subject = value)
    }

    fun setMessage(value: String) {
        _state.value = _state.value.copy(message = value, error = null)
    }

    fun selectQueue(id: Long) {
        _state.value = _state.value.copy(selectedQueueId = id, error = null)
    }

    fun submit(onCreated: (Long) -> Unit) {
        val s = _state.value
        if (s.message.isBlank()) {
            _state.value = s.copy(error = Loc.s.support.supportNewTicketMessageRequired)
            return
        }
        if (s.queues.size > 1 && s.selectedQueueId == null) {
            _state.value = s.copy(error = Loc.s.support.supportNewTicketQueueRequired)
            return
        }
        _state.value = s.copy(submitting = true, error = null)
        viewModelScope.launch {
            runCatching {
                api.createSupportTicket(
                    CreateSupportTicketRequest(
                        subject = s.subject.trim().takeIf { it.isNotBlank() },
                        message = s.message.trim(),
                        queueId = s.selectedQueueId ?: s.queues.singleOrNull()?.id
                    )
                )
            }.onSuccess { response ->
                _state.value = _state.value.copy(submitting = false)
                onCreated(response.ticket.id)
            }.onFailure {
                _state.value = _state.value.copy(submitting = false, error = Loc.s.support.supportNewTicketError)
            }
        }
    }
}
