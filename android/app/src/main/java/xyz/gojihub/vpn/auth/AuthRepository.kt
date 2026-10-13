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
    // Клиент без авторизационных интерсепторов (см. NetworkModule) — для проверки чужой сессии.
    @javax.inject.Named("refresh") private val plainClient: okhttp3.OkHttpClient,
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

    /** Вход по готовой сессии сайта: сейчас — перенос входа QR-кодом (completeTransfer ниже);
     *  раньше так же завершался вход через встроенный сайт (убран вместе со сборкой не для Play). Обходной путь: нативный обмен кода на токен
     *  (POST /api/auth/native/exchange) на бэкенде 7.1.0 сломан и всегда отвечает 400 "invalid
     *  request" — независимо от провайдера (см. ARCHITECTURE.md). Веб-флоу сайта использует
     *  совсем другой, отдельный эндпоинт (/api/auth/session/exchange) и им не затронут — токен
     *  из его же Set-Cookie мы уже проверяли живым запросом как обычный Bearer и подтвердили, что
     *  бэкенд принимает его наравне с токеном из email-входа. */
    suspend fun completeWebLogin(sessionToken: String, refreshToken: String?): AuthResult = runCatching {
        refreshToken?.let(tokenManager::saveRefreshToken)
        onAuthenticated(sessionToken, WEB_LOGIN_EXPIRES_IN_SECONDS)
        AuthResult.Success
    }.getOrElse {
        AppLogger.e(appContext, LogCategory.MAIN, TAG, "completeWebLogin failed", it)
        AuthResult.Error(it.message ?: Loc.s.errorOAuthCompleteFailed)
    }

    /**
     * Перенос входа на другое устройство по QR-коду (Подписка → "Перенести на другое
     * устройство"): в код кладём текущую сессию — тот же JWT и refresh-токен, что уже лежат в
     * TokenManager, — и момент создания. Бэкенд принимает JWT как обычный Bearer с любого
     * устройства (так же, как токен из WebView-входа); новое устройство при первом запросе
     * подписки само зарегистрирует свой HWID и займёт место в лимите устройств.
     * null — если на этом устройстве сейчас нет сессии.
     */
    fun transferUri(): String? {
        val token = tokenManager.accessToken() ?: return null
        val refresh = tokenManager.refreshToken()
        return android.net.Uri.Builder()
            .scheme(TRANSFER_SCHEME).authority(TRANSFER_HOST)
            .appendQueryParameter("s", token)
            .apply { if (refresh != null) appendQueryParameter("r", refresh) }
            .appendQueryParameter("t", (System.currentTimeMillis() / 1000).toString())
            .build().toString()
    }

    enum class TransferError { INVALID, EXPIRED, FAILED }

    /** Вход по отсканированному коду переноса. Проверяет формат и срок (TRANSFER_TTL), затем
     *  проверяет присланную сессию ОТДЕЛЬНЫМ запросом /api/auth/me — и только если бэкенд её
     *  принял, сохраняет. Текущий вход на этом устройстве при неудаче не трогается. */
    suspend fun loginWithTransfer(raw: String): TransferError? {
        val uri = runCatching { android.net.Uri.parse(raw.trim()) }.getOrNull()
        if (uri == null || uri.scheme != TRANSFER_SCHEME || uri.host != TRANSFER_HOST) return TransferError.INVALID
        val session = uri.getQueryParameter("s")?.takeIf { it.isNotBlank() } ?: return TransferError.INVALID
        val createdAt = uri.getQueryParameter("t")?.toLongOrNull() ?: return TransferError.INVALID
        val age = System.currentTimeMillis() / 1000 - createdAt
        if (age !in -300..TRANSFER_TTL_SECONDS) return TransferError.EXPIRED
        if (!isSessionAccepted(session)) return TransferError.FAILED
        val result = completeWebLogin(session, uri.getQueryParameter("r")?.takeIf { it.isNotBlank() })
        return if (result is AuthResult.Error) TransferError.FAILED else null
    }

    /** GET /api/auth/me с присланным токеном — без authInterceptor основного клиента (тот
     *  подставил бы текущий токен устройства и при 401 разлогинил бы его). */
    private suspend fun isSessionAccepted(token: String): Boolean = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
        runCatching {
            val request = okhttp3.Request.Builder()
                .url("${xyz.gojihub.vpn.BuildConfig.SITE_URL}/api/auth/me")
                .header("Authorization", "Bearer $token")
                .header("X-Requested-With", "XMLHttpRequest")
                .header("User-Agent", "Goji/${xyz.gojihub.vpn.BuildConfig.VERSION_NAME}/Android")
                .build()
            plainClient.newCall(request).execute().use { it.isSuccessful }
        }.onFailure { AppLogger.e(appContext, LogCategory.MAIN, TAG, "isSessionAccepted failed", it) }
            .getOrDefault(false)
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
        // TokenManager реально этим значением уже не пользуется (см. комментарий у isLoggedIn()) —
        // единственный источник правды о протухшем токене — 401 от бэкенда. Держим с запасом.
        const val WEB_LOGIN_EXPIRES_IN_SECONDS = 30L * 24 * 3600
        const val TRANSFER_SCHEME = "godjivpn"
        const val TRANSFER_HOST = "transfer"
        // Код переноса показывают с экрана "здесь и сейчас" — старый снимок экрана не должен
        // давать вход спустя дни.
        const val TRANSFER_TTL_SECONDS = 10L * 60
    }
}
