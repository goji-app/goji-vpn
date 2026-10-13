package xyz.gojihub.vpn.ui.support

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import xyz.gojihub.vpn.network.RemnawaveApi
import xyz.gojihub.vpn.network.models.FaqItemDto
import javax.inject.Inject

data class FaqSectionUi(val name: String?, val items: List<FaqItemDto>)

data class FaqUiState(
    val sections: List<FaqSectionUi> = emptyList(),
    val loading: Boolean = true,
    val error: Boolean = false
)

@HiltViewModel
class FaqViewModel @Inject constructor(
    private val api: RemnawaveApi
) : ViewModel() {

    private val _state = MutableStateFlow(FaqUiState())
    val state: StateFlow<FaqUiState> = _state

    init {
        load()
    }

    fun load() {
        _state.value = _state.value.copy(loading = true, error = false)
        viewModelScope.launch {
            runCatching { api.getFaq() }
                .onSuccess { response ->
                    val sections = buildList {
                        if (!response.ungrouped.isNullOrEmpty()) add(FaqSectionUi(null, response.ungrouped))
                        response.sections.orEmpty().filter { !it.items.isNullOrEmpty() }.forEach { add(FaqSectionUi(it.name, it.items.orEmpty())) }
                    }
                    _state.value = _state.value.copy(sections = sections, loading = false, error = false)
                }
                .onFailure { _state.value = _state.value.copy(loading = false, error = true) }
        }
    }
}
