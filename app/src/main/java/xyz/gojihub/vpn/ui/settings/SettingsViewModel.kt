package xyz.gojihub.vpn.ui.settings

import android.app.DownloadManager
import android.content.Context
import android.content.Intent
import android.os.Build
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import xyz.gojihub.vpn.BuildConfig
import xyz.gojihub.vpn.auth.AuthRepository
import xyz.gojihub.vpn.i18n.AppLanguage
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.network.NetworkDiagnostics
import xyz.gojihub.vpn.network.RemnawaveApi
import xyz.gojihub.vpn.network.models.CreateSupportTicketRequest
import xyz.gojihub.vpn.network.models.SendSupportMessageRequest
import xyz.gojihub.vpn.settings.PingMethod
import xyz.gojihub.vpn.settings.SettingsRepository
import xyz.gojihub.vpn.subscription.SubscriptionRepository
import xyz.gojihub.vpn.ui.theme.FontSizePreset
import xyz.gojihub.vpn.ui.theme.GodjiColors
import xyz.gojihub.vpn.ui.theme.ThemeMode
import xyz.gojihub.vpn.ui.theme.isSystemInDarkMode
import xyz.gojihub.vpn.util.AppLogger
import xyz.gojihub.vpn.util.LogCategory
import xyz.gojihub.vpn.util.LogLevel
import xyz.gojihub.vpn.vpn.GodjiVpnService
import javax.inject.Inject

data class SettingsUiState(
    val pinNotification: Boolean = true,
    val themeMode: ThemeMode = ThemeMode.SYSTEM,
    val language: AppLanguage = AppLanguage.RU,
    val fontSize: FontSizePreset = FontSizePreset.NORMAL,
    val autoConnectOnWifi: Boolean = false,
    val killSwitch: Boolean = false,
    val haptics: Boolean = true,
    val pingMethod: PingMethod = PingMethod.PROXY_GET,
    val pingTestUrl: String = SettingsRepository.DEFAULT_PING_URL,
    val logLevel: LogLevel = LogLevel.DEFAULT,
    val appVersion: String = BuildConfig.VERSION_NAME,
    val xrayVersion: String = SettingsViewModel.BUNDLED_XRAY_VERSION,
    val hwid: String = "",
    val deviceInfo: String = "${Build.MANUFACTURER} ${Build.MODEL}, Android ${Build.VERSION.RELEASE}",
    val leakChecking: Boolean = false,
    val leakReport: NetworkDiagnostics.Report? = null,
    val deletingAccount: Boolean = false,
    val deleteAccountError: String? = null
)

@HiltViewModel
class SettingsViewModel @Inject constructor(
    private val settingsRepository: SettingsRepository,
    private val authRepository: AuthRepository,
    private val subscriptionRepository: SubscriptionRepository,
    private val networkDiagnostics: NetworkDiagnostics,
    private val api: RemnawaveApi,
    @ApplicationContext private val appContext: Context
) : ViewModel() {

    private val _state = MutableStateFlow(SettingsUiState())
    val state: StateFlow<SettingsUiState> = _state

    init {
        viewModelScope.launch {
            _state.value = _state.value.copy(
                pinNotification = settingsRepository.pinNotificationNow(),
                themeMode = settingsRepository.themeModeNow(),
                pingMethod = settingsRepository.pingMethodNow(),
                pingTestUrl = settingsRepository.pingTestUrlNow(),
                language = settingsRepository.appLanguageNow(),
                fontSize = settingsRepository.fontSizeNow(),
                autoConnectOnWifi = settingsRepository.autoConnectOnWifi.first(),
                killSwitch = settingsRepository.killSwitchEnabled.first(),
                haptics = settingsRepository.hapticsEnabled.first(),
                logLevel = settingsRepository.logLevelNow(),
                hwid = subscriptionRepository.hwidNow()
            )
        }
    }

    fun setLanguage(language: AppLanguage) {
        _state.value = _state.value.copy(language = language)
        Loc.lang = language
        viewModelScope.launch { settingsRepository.setAppLanguage(language) }
    }

    fun setLogLevel(level: LogLevel) {
        _state.value = _state.value.copy(logLevel = level)
        AppLogger.level = level
        viewModelScope.launch { settingsRepository.setLogLevel(level) }
    }

    companion object {
        // libXray.invoke() в этой сборке AAR поддерживает только 8 методов (runXray, testXray,
        // pingBatch, getFreePorts, countGeoData, generateAgeKeyPair, convertShareLinksToXrayJson,
        // convertXrayJsonToShareLinks) — метода "версия ядра" среди них нет, поэтому его нельзя
        // спросить в рантайме. До v26.9.9 версию можно было вытащить строкой из самого нативного
        // .so ("REALITY: the default minimal client version is Xray-core vX.Y.Z", которую
        // xray-core формировал из своей же core.Version()) — начиная с этого AAR (обновление
        // XTLS/libXray "Updated Xray-core to v26.9.9 and refreshed dependencies") эта строка из
        // бинарника пропала (grep по libgojni.so на "Xray-core" не находит ничего), так что
        // источник версии теперь — только release notes XTLS/libXray на момент замены AAR.
        // При следующем обновлении libs/libXray.aar сверяться с https://github.com/XTLS/libXray/releases
        // и обновлять эту константу вручную.
        const val BUNDLED_XRAY_VERSION = "v26.9.9"
    }

    fun setPinNotification(pinned: Boolean) {
        _state.value = _state.value.copy(pinNotification = pinned)
        viewModelScope.launch { settingsRepository.setPinNotification(pinned) }
    }

    /** DARK/LIGHT применяются сразу и однозначно; SYSTEM применяет текущую системную тему один
     *  раз в момент выбора — дальнейшее слежение за её сменой уже не отсюда, а из GodjiApp.kt
     *  (SideEffect на isSystemInDarkTheme(), реагирует пока приложение открыто). */
    fun setThemeMode(mode: ThemeMode) {
        _state.value = _state.value.copy(themeMode = mode)
        GodjiColors.themeMode = mode
        val dark = when (mode) {
            ThemeMode.DARK -> true
            ThemeMode.LIGHT -> false
            ThemeMode.SYSTEM -> isSystemInDarkMode(appContext)
        }
        if (dark) GodjiColors.applyDark() else GodjiColors.applyLight()
        viewModelScope.launch { settingsRepository.setThemeMode(mode) }
    }

    fun setFontSize(preset: FontSizePreset) {
        _state.value = _state.value.copy(fontSize = preset)
        GodjiColors.fontSizePreset = preset
        viewModelScope.launch { settingsRepository.setFontSize(preset) }
    }

    fun setAutoConnectOnWifi(enabled: Boolean) {
        _state.value = _state.value.copy(autoConnectOnWifi = enabled)
        viewModelScope.launch { settingsRepository.setAutoConnectOnWifi(enabled) }
    }

    fun setKillSwitch(enabled: Boolean) {
        _state.value = _state.value.copy(killSwitch = enabled)
        viewModelScope.launch { settingsRepository.setKillSwitchEnabled(enabled) }
    }

    fun setHaptics(enabled: Boolean) {
        _state.value = _state.value.copy(haptics = enabled)
        viewModelScope.launch { settingsRepository.setHapticsEnabled(enabled) }
    }

    fun setPingMethod(method: PingMethod) {
        _state.value = _state.value.copy(pingMethod = method)
        viewModelScope.launch { settingsRepository.setPingMethod(method) }
    }

    fun setPingTestUrl(url: String) {
        _state.value = _state.value.copy(pingTestUrl = url)
        viewModelScope.launch { settingsRepository.setPingTestUrl(url) }
    }

    /** См. NetworkDiagnostics — IP и DNS через туннель против настоящих, с итоговым вердиктом. */
    fun checkLeak() {
        if (_state.value.leakChecking) return
        _state.value = _state.value.copy(leakChecking = true)
        viewModelScope.launch {
            val report = networkDiagnostics.check()
            if (report.vpnIp != null) {
                xyz.gojihub.vpn.journal.NetworkJournal.log(if (report.ipLeak || report.dnsLeak) xyz.gojihub.vpn.journal.NetworkJournal.Kind.LEAK_FAIL else xyz.gojihub.vpn.journal.NetworkJournal.Kind.LEAK_OK)
            }
            _state.value = _state.value.copy(leakChecking = false, leakReport = report)
        }
    }

    // Разовое событие «заявка отправлена, вышли из аккаунта» — навигацию делает экран в
    // LaunchedEffect, а не лямбда, пережившая пересоздание экрана.
    private val _accountDeleted = kotlinx.coroutines.flow.MutableSharedFlow<Unit>(extraBufferCapacity = 1)
    val accountDeleted: kotlinx.coroutines.flow.SharedFlow<Unit> = _accountDeleted

    /**
     * Удаление аккаунта (обязательно для Google Play). Своего эндпоинта у бэкенда нет —
     * отправляем заявку в поддержку от имени текущей сессии, после чего выходим. Если лимит
     * обращений исчерпан (уже есть открытое), заявка уходит сообщением в это обращение.
     */
    fun requestAccountDeletion() {
        if (_state.value.deletingAccount) return
        _state.value = _state.value.copy(deletingAccount = true, deleteAccountError = null)
        viewModelScope.launch {
            val info = "Android ${Build.VERSION.RELEASE}, Goji ${BuildConfig.VERSION_NAME}, HWID ${_state.value.hwid}"
            val text = Loc.f.deleteAccountTicketMessage(info)
            val error: String? = try {
                val limit = try { api.getSupportTicketLimit() } catch (e: CancellationException) { throw e } catch (e: Exception) { null }
                val activeTicket = limit?.activeTicketId
                if (limit != null && !limit.canCreate && activeTicket != null) {
                    val response = api.sendSupportMessage(activeTicket, SendSupportMessageRequest(text))
                    if (response.isSuccessful) null else Loc.f.deleteAccountServerError
                } else {
                    val queues = try { api.getSupportQueues().orEmpty() } catch (e: CancellationException) { throw e } catch (e: Exception) { emptyList() }
                    val queue = queues.firstOrNull { q ->
                        val n = q.name.lowercase()
                        listOf("аккаунт", "account", "общ", "general", "друг", "other").any { it in n }
                    } ?: queues.firstOrNull()
                    api.createSupportTicket(
                        CreateSupportTicketRequest(subject = Loc.f.deleteAccountTicketSubject, message = text, queueId = queue?.id)
                    )
                    null
                }
            } catch (e: CancellationException) {
                throw e
            } catch (e: java.io.IOException) {
                AppLogger.w(appContext, LogCategory.MAIN, "Settings", "account deletion request failed: ${e.message}")
                Loc.f.deleteAccountError
            } catch (e: Exception) {
                AppLogger.w(appContext, LogCategory.MAIN, "Settings", "account deletion request rejected: ${e.message}")
                Loc.f.deleteAccountServerError
            }
            if (error == null) {
                AppLogger.i(appContext, LogCategory.MAIN, "Settings", "account deletion requested")
                logout()
                _state.value = _state.value.copy(deletingAccount = false)
                _accountDeleted.tryEmit(Unit)
            } else {
                _state.value = _state.value.copy(deletingAccount = false, deleteAccountError = error)
            }
        }
    }

    fun clearDeleteAccountError() {
        _state.value = _state.value.copy(deleteAccountError = null)
    }

    fun logout() {
        if (GodjiVpnService.isRunning.value) {
            appContext.startService(Intent(appContext, GodjiVpnService::class.java).apply {
                action = GodjiVpnService.ACTION_DISCONNECT
            })
        }
        authRepository.logout()
    }
}
