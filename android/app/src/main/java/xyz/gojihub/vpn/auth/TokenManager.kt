package xyz.gojihub.vpn.auth

import android.content.Context
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class TokenManager @Inject constructor(
    @ApplicationContext context: Context
) {
    // Устанавливается authInterceptor'ом (см. NetworkModule) при реальном 401 от бэкенда —
    // единственный надёжный признак того, что токен действительно мёртв (см. isLoggedIn()
    // ниже и комментарий там про то, почему раньше решали это иначе). GodjiApp (MainActivity)
    // подписывается на этот флаг, чтобы выкинуть пользователя на экран логина реактивно,
    // а не только при следующем холодном старте.
    private val _sessionExpired = MutableStateFlow(false)
    val sessionExpired: StateFlow<Boolean> = _sessionExpired.asStateFlow()
    private val masterKey = MasterKey.Builder(context)
        .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
        .build()

    private val prefs = EncryptedSharedPreferences.create(
        context,
        "godji_secure_prefs",
        masterKey,
        EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
        EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
    )

    /** [expiresInSeconds] — как возвращает /api/auth/email/verify-otp. */
    fun save(token: String, expiresInSeconds: Long) {
        prefs.edit()
            .putString(KEY_TOKEN, token)
            .putLong(KEY_EXPIRES_AT, System.currentTimeMillis() + expiresInSeconds * 1000)
            .apply()
    }

    fun accessToken(): String? = prefs.getString(KEY_TOKEN, null)

    /** rw_refresh_token — живёт намного дольше сессионного JWT (тот истекает ровно через 24ч,
     *  см. TokenAuthenticator). Не приходит для сессий, начатых до этого обновления (старый
     *  сохранённый accessToken без refresh-токена) — тогда TokenAuthenticator просто не сможет
     *  продлить сессию и пользователь один раз перелогинится, дальше уже с refresh-токеном. */
    fun saveRefreshToken(token: String) {
        prefs.edit().putString(KEY_REFRESH_TOKEN, token).apply()
    }

    fun refreshToken(): String? = prefs.getString(KEY_REFRESH_TOKEN, null)

    /** Сессии, начатые через встроенный браузер до 1.0.112, сохранились без refresh-токена
     *  (кука с Path=/api/auth не читалась, см. WebLoginActivity) — и выкидывали на логин ровно
     *  через сутки. Сама кука при этом лежит в CookieManager WebView. Забираем её, только если
     *  сессионная кука там — та же, что наш текущий токен: значит, это тот же вход, а не
     *  старая сессия другого аккаунта. Вызывать на главном потоке (CookieManager). */
    fun recoverWebRefreshToken() {
        val token = accessToken() ?: return
        if (refreshToken() != null) return
        runCatching {
            val cm = android.webkit.CookieManager.getInstance()
            val session = cookie(cm.getCookie("https://gojihub.xyz/"), "rw_session_token")
            val refresh = cookie(cm.getCookie("https://gojihub.xyz/api/auth/refresh"), "rw_refresh_token")
            if (session == token && !refresh.isNullOrBlank()) saveRefreshToken(refresh)
        }
    }

    private fun cookie(cookies: String?, name: String): String? =
        cookies?.split("; ")?.firstOrNull { it.startsWith("$name=") }?.substringAfter("$name=")

    /** Раньше здесь ещё сравнивался KEY_EXPIRES_AT (локально посчитанный из expires_in при
     *  логине) с системным временем — из-за этого MainActivity при каждом холодном старте
     *  (Android регулярно убивает процесс в фоне, особенно без активного VPN-сервиса) могла
     *  посчитать токен протухшим и выкинуть пользователя на логин ещё ДО того, как токен
     *  реально переставал приниматься бэкендом (backend не выдаёт refresh_token для
     *  email-OTP входа вообще, а для OAuth его получаем, но не используем — обновлять токен
     *  заранее было нечем). Настоящий и единственный источник истины о протухшем токене —
     *  ответ 401 от самого бэкенда (см. authInterceptor в NetworkModule и sessionExpired
     *  выше); только на него теперь и полагаемся.
     */
    fun isLoggedIn(): Boolean = accessToken() != null

    fun clear() {
        prefs.edit().clear().apply()
    }

    /** Вызывается authInterceptor'ом при 401 — стирает токен и поднимает [sessionExpired]
     *  для реактивной навигации на логин прямо во время работы приложения. */
    fun markSessionExpired() {
        clear()
        _sessionExpired.value = true
    }

    /** GodjiApp вызывает после того, как отреагировал на [sessionExpired] (перешёл на логин) —
     *  иначе флаг остался бы true и снова сработал бы при следующей рекомпозиции/пересоздании. */
    fun consumeSessionExpired() {
        _sessionExpired.value = false
    }

    private companion object {
        const val KEY_TOKEN = "access_token"
        const val KEY_EXPIRES_AT = "expires_at"
        const val KEY_REFRESH_TOKEN = "refresh_token"
    }
}
