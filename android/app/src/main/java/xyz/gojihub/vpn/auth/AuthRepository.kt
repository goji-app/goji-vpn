package xyz.gojihub.vpn.auth

import android.content.Context
import dagger.hilt.android.qualifiers.ApplicationContext
import xyz.gojihub.vpn.network.RemnawaveApi
import xyz.gojihub.vpn.network.extractCookieValue
import xyz.gojihub.vpn.network.models.ConsentRequest
import xyz.gojihub.vpn.network.models.NativeExchangeRequest
import xyz.gojihub.vpn.network.models.SendOtpRequest
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.network.models.VerifyOtpRequest
import xyz.gojihub.vpn.util.AppLogger
import xyz.gojihub.vpn.util.LogCategory
import javax.inject.Inject
import javax.inject.Singleton

sealed class AuthResult {
    data object Success : AuthResult()
    data class Error(val message: String) : AuthResult()
}

/** Deeplink, на который сервер редиректит браузер после провайдерского OAuth-callback.
 *  Схему нужно один раз добавить в панели: Настройки → Мобильные приложения →
 *  "Разрешённые схемы app_redirect". Без этого сервер откажется на неё редиректить. */
private const val OAUTH_REDIRECT_URI = "godjivpn://oauth2redirect"

@Singleton
class AuthRepository @Inject constructor(
    private val api: RemnawaveApi,
    private val tokenManager: TokenManager,
    @ApplicationContext private val appContext: Context,
    private val subscriptionRepository: dagger.Lazy<xyz.gojihub.vpn.subscription.SubscriptionRepository>
) {
    // PKCE verifier/provider живут между стартом OAuth (открытие Custom Tabs) и возвратом
    // deeplink'ом — раньше держались только в памяти, и это было сознательно принятым риском
    // ("если процесс убьют посреди авторизации в браузере, придётся начать заново"). На практике
    // это ломает именно вход через Telegram: подтверждение там требует переключиться в другое
    // приложение и вручную найти уведомление — команда занимает намного больше времени, чем
    // "нажать один аккаунт" у Google/Яндекса, и Android с гораздо большей вероятностью успевает
    // убить наш процесс в фоне за это время. После этого callback приходит без pendingProvider(),
    // и OAuthCallbackActivity молча завершается — пользователь видит "зависание" вместо ошибки.
    // Переживает смерть процесса — обычный SharedPreferences, не Encrypted: verifier одноразовый,
    // короткоживущий и бесполезен без code, привязанного к нему на сервере.
    private val pendingPrefs = appContext.getSharedPreferences("godji_pending_oauth", Context.MODE_PRIVATE)

    private var pendingCodeVerifier: String?
        get() = pendingPrefs.getString(KEY_VERIFIER, null)
        set(value) { pendingPrefs.edit().putString(KEY_VERIFIER, value).apply() }

    private var pendingProviderValue: String?
        get() = pendingPrefs.getString(KEY_PROVIDER, null)
        set(value) { pendingPrefs.edit().putString(KEY_PROVIDER, value).apply() }

    suspend fun sendOtp(email: String): AuthResult = runCatching {
        api.sendOtp(SendOtpRequest(email))
        AuthResult.Success
    }.getOrElse {
        AppLogger.e(appContext, LogCategory.MAIN, TAG, "sendOtp failed", it)
        AuthResult.Error(it.message ?: Loc.s.errorOtpFailed)
    }

    suspend fun verifyOtp(email: String, code: String): AuthResult = runCatching {
        val response = api.verifyOtp(VerifyOtpRequest(email, code))
        if (!response.isSuccessful) throw retrofit2.HttpException(response)
        val body = response.body() ?: error("verifyOtp: пустое тело ответа")
        // С 7.1.0 сам токен сессии — в Set-Cookie (rw_session_token), не в теле ответа (см.
        // комментарий у VerifyOtpResponse). Тот же JWT работает как обычный Bearer-токен —
        // подтверждено живым запросом к /api/auth/me и /api/subscriptions.
        val token = extractCookieValue(response.headers(), "rw_session_token")
            ?: error("verifyOtp: в ответе нет куки rw_session_token")
        // Живёт намного дольше сессионного JWT — без неё TokenAuthenticator не сможет
        // обновить сессию по истечении суток и пользователя раз в день выкидывало бы на
        // логин (см. TokenAuthenticator в NetworkModule).
        extractCookieValue(response.headers(), "rw_refresh_token")?.let(tokenManager::saveRefreshToken)
        onAuthenticated(token, body.expiresIn)
        AuthResult.Success
    }.getOrElse {
        AppLogger.e(appContext, LogCategory.MAIN, TAG, "verifyOtp failed", it)
        AuthResult.Error(it.message ?: Loc.s.errorWrongCode)
    }

    /** provider: "google" | "yandex" | "telegram-oidc". Возвращает URL для Custom Tabs.
     *  (Проверено: /start всегда отдаёт голый JSON, даже при прямой навигации браузером —
     *  открывать его напрямую бессмысленно, дёргаем через API как и раньше.) */
    suspend fun startOAuth(provider: String): Result<String> = runCatching {
        val verifier = Pkce.generateVerifier()
        val response = api.startOAuth(
            provider = provider,
            appRedirect = OAUTH_REDIRECT_URI,
            codeChallenge = Pkce.challengeFor(verifier)
        )
        pendingCodeVerifier = verifier
        pendingProviderValue = provider
        response.authUrl
    }

    fun pendingProvider(): String? = pendingProviderValue

    /** Вызывается из OAuthCallbackActivity после возврата deeplink'ом из браузера. */
    suspend fun completeOAuth(provider: String, code: String): AuthResult {
        val verifier = pendingCodeVerifier
            ?: return AuthResult.Error(Loc.s.errorOAuthSessionExpired)
        return runCatching {
            val response = api.exchangeNativeOAuth(NativeExchangeRequest(code, verifier, provider))
            onAuthenticated(response.accessToken, response.expiresIn)
            pendingCodeVerifier = null
            pendingProviderValue = null
            AuthResult.Success
        }.getOrElse {
            AppLogger.e(appContext, LogCategory.MAIN, TAG, "completeOAuth($provider) failed", it)
            AuthResult.Error(it.message ?: Loc.s.errorOAuthCompleteFailed)
        }
    }

    /**
     * Перенос входа на другое устройство по QR-коду (Подписка → «Перенести на другое
     * устройство»): в коде только адрес почты аккаунта — godjivpn://transfer?e=<почта>. Новое
     * устройство подставляет его и просит код из письма, дальше обычный вход по почте.
     * Раньше в код клали саму сессию (JWT и refresh-токен): снимок экрана давал вход навсегда,
     * а два устройства с одним refresh-токеном выбивали друг друга (бэкенд его ротирует).
     * null — почту узнать не удалось (нет сессии или сети).
     */
    suspend fun transferUri(): String? {
        val email = runCatching { api.getMe().email }.getOrNull()?.takeIf { it.isNotBlank() } ?: return null
        return android.net.Uri.Builder()
            .scheme(TRANSFER_SCHEME).authority(TRANSFER_HOST)
            .appendQueryParameter("e", email)
            .build().toString()
    }

    enum class TransferError { INVALID, OUTDATED }

    /** Почта из отсканированного кода переноса или ошибка: не наш код, либо код старой версии
     *  приложения (с сессией внутри — такие больше не принимаем). */
    fun parseTransfer(raw: String): Pair<String?, TransferError?> {
        val uri = runCatching { android.net.Uri.parse(raw.trim()) }.getOrNull()
        if (uri == null || uri.scheme != TRANSFER_SCHEME || uri.host != TRANSFER_HOST) return null to TransferError.INVALID
        val email = uri.getQueryParameter("e")?.trim()
        if (email != null && android.util.Patterns.EMAIL_ADDRESS.matcher(email).matches()) return email to null
        return null to if (uri.getQueryParameter("s") != null) TransferError.OUTDATED else TransferError.INVALID
    }

    private suspend fun onAuthenticated(token: String, expiresInSeconds: Long) {
        tokenManager.save(token, expiresInSeconds)
        // Согласия на обработку данных / условия использования — требуются один раз после
        // первой регистрации. Если бэкенд их не запрашивал, вызов просто ни на что не влияет.
        runCatching {
            if (api.getMe().consentRequired) {
                api.consent(ConsentRequest())
            }
        }
    }

    fun logout() {
        tokenManager.clear()
        // Данные прежнего аккаунта (подписка, устройства, рефералы) не должны показываться
        // следующему, кто войдёт на этом телефоне.
        listOf(
            "account_subscription.json", "account_broadcasts.json", "account_plans.json",
            "account_referrals.json", "account_partner.json", "account_devices.json"
        ).forEach { runCatching { java.io.File(appContext.filesDir, it).delete() } }
        appContext.getSharedPreferences("subscription_refresh", Context.MODE_PRIVATE).edit().clear().apply()
        subscriptionRepository.get().clearAccount()
        xyz.gojihub.vpn.util.SubscriptionPeriod.clear(appContext)
    }
    fun isLoggedIn(): Boolean = tokenManager.isLoggedIn()

    /** true после реального 401 от бэкенда (см. authInterceptor в NetworkModule) — GodjiApp
     *  подписывается, чтобы среагировать переходом на логин прямо во время работы приложения. */
    val sessionExpired get() = tokenManager.sessionExpired
    fun consumeSessionExpired() = tokenManager.consumeSessionExpired()

    private companion object {
        const val KEY_VERIFIER = "verifier"
        const val KEY_PROVIDER = "provider"
        const val TAG = "GodjiAuth"
        const val TRANSFER_SCHEME = "godjivpn"
        const val TRANSFER_HOST = "transfer"
    }
}
