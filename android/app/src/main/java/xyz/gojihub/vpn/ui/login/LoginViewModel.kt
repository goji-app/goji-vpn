package xyz.gojihub.vpn.ui.login

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import xyz.gojihub.vpn.auth.AuthRepository
import xyz.gojihub.vpn.auth.AuthResult
import xyz.gojihub.vpn.i18n.Loc
import javax.inject.Inject

fun transferErrorText(error: AuthRepository.TransferError): String = when (error) {
    AuthRepository.TransferError.INVALID -> Loc.f.transferInvalid
    AuthRepository.TransferError.EXPIRED -> Loc.f.transferExpired
    AuthRepository.TransferError.FAILED -> Loc.f.transferFailed
}

data class LoginUiState(
    val email: String = "",
    val emailMode: Boolean = false,
    val loading: Boolean = false,
    val error: String? = null
)

@HiltViewModel
class LoginViewModel @Inject constructor(
    private val authRepository: AuthRepository
) : ViewModel() {

    private val _state = MutableStateFlow(LoginUiState())
    val state: StateFlow<LoginUiState> = _state

    fun onEmailChange(value: String) { _state.value = _state.value.copy(email = value, error = null) }
    fun toggleEmailMode(enabled: Boolean) { _state.value = _state.value.copy(emailMode = enabled, error = null) }

    fun sendOtp(onSent: (String) -> Unit) {
        val email = _state.value.email.trim()
        if (!android.util.Patterns.EMAIL_ADDRESS.matcher(email).matches()) {
            _state.value = _state.value.copy(error = Loc.s.errorInvalidEmail)
            return
        }
        _state.value = _state.value.copy(loading = true, error = null)
        viewModelScope.launch {
            when (val result = authRepository.sendOtp(email)) {
                is AuthResult.Success -> {
                    _state.value = _state.value.copy(loading = false)
                    onSent(email)
                }
                is AuthResult.Error -> _state.value = _state.value.copy(loading = false, error = result.message)
            }
        }
    }

    /** Вход по QR-коду переноса, отсканированному прямо на экране входа. */
    fun loginWithTransfer(raw: String, onSuccess: () -> Unit) {
        _state.value = _state.value.copy(loading = true, error = null)
        viewModelScope.launch {
            val error = authRepository.loginWithTransfer(raw)
            _state.value = _state.value.copy(loading = false, error = error?.let(::transferErrorText))
            if (error == null) onSuccess()
        }
    }

    /** provider: "google" | "yandex" | "telegram-oidc". [onUrlReady] открывает Custom Tabs. */
    fun startOAuth(provider: String, onUrlReady: (String) -> Unit) {
        _state.value = _state.value.copy(loading = true, error = null)
        viewModelScope.launch {
            authRepository.startOAuth(provider)
                .onSuccess {
                    _state.value = _state.value.copy(loading = false)
                    onUrlReady(it)
                }
                .onFailure {
                    _state.value = _state.value.copy(loading = false, error = it.message ?: Loc.s.errorOAuthStartFailed)
                }
        }
    }
}
