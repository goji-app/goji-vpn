package xyz.gojihub.vpn.di

import com.squareup.moshi.Moshi
import com.squareup.moshi.kotlin.reflect.KotlinJsonAdapterFactory
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.components.SingletonComponent
import okhttp3.Authenticator
import okhttp3.Interceptor
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.Response
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Retrofit
import retrofit2.converter.moshi.MoshiConverterFactory
import xyz.gojihub.vpn.BuildConfig
import xyz.gojihub.vpn.auth.TokenManager
import xyz.gojihub.vpn.network.RemnawaveApi
import xyz.gojihub.vpn.network.extractCookieValue
import xyz.gojihub.vpn.vpn.GodjiVpnService
import javax.inject.Named
import javax.inject.Singleton

// Реальный клиентский бэкенд (Telegram Mini App) — сверено напрямую с сетевыми запросами
// веб-версии приложения. host.gojihub.xyz — это панель Remnawave (админка), не сюда.
private const val BASE_URL = "https://gojihub.xyz/"

@Module
@InstallIn(SingletonComponent::class)
object NetworkModule {

    @Provides
    @Singleton
    fun provideAuthInterceptor(tokenManager: TokenManager): Interceptor = Interceptor { chain ->
        val token = tokenManager.accessToken()
        val request = chain.request().newBuilder().apply {
            if (token != null) addHeader("Authorization", "Bearer $token")
            // Бэкенд требует этот заголовок на POST /api/auth/* (лёгкая CSRF-защита —
            // проверено: без него /send-otp и /native/exchange отдают голый 403 "Forbidden"
            // ещё до хендлера, с ним — нормальный JSON-ответ). Веб-версия шлёт его через
            // XMLHttpRequest/fetch автоматически, plain OkHttp — нет, добавляем сами.
            addHeader("X-Requested-With", "XMLHttpRequest")
            // header(), а не addHeader() — заменяет дефолтный User-Agent клиента целиком,
            // а не добавляет второй. Касается только собственных запросов приложения к
            // gojihub.xyz (эта конкретная интерцепция); User-Agent запроса подписки
            // (SubscriptionRepository.fetchNodes) сюда не попадает — он идёт через отдельный
            // OkHttpClient и намеренно спуфит другой клиент, см. комментарий там.
            header("User-Agent", "Goji/${BuildConfig.VERSION_NAME}/Android")
        }.build()
        val response = chain.proceed(request)
        // 401 с уже приложенным токеном — единственный надёжный признак того, что сессия
        // реально мертва на бэкенде (см. TokenManager.isLoggedIn()/markSessionExpired() —
        // раньше это решалось локальным сравнением с expires_at, из-за чего приложение могло
        // разлогинить пользователя ещё до того, как бэкенд на самом деле отказался бы принимать
        // токен). Не трогаем ответ без токена (аноним и так получит 401 по делу, не о протухшей
        // сессии) и не трогаем сами auth-эндпоинты (неверный OTP-код тоже может прийти как 401
        // и не должен разлогинивать несуществующую ещё сессию).
        // Если ниже сработает TokenAuthenticator и продлит сессию, сюда придёт уже финальный
        // (не 401) ответ после успешного ретрая — markSessionExpired() увидит только "по-
        // настоящему" мёртвую сессию (refresh-токена нет или сам он тоже не принят).
        // Последнее условие: если параллельный запрос за это время уже обновил сессию, этот
        // устаревший 401 не должен стирать свежий токен.
        if (response.code == 401 && token != null && !request.url.encodedPath.startsWith("/api/auth/") &&
            tokenManager.accessToken() == token
        ) {
            tokenManager.markSessionExpired()
        }
        response
    }

    /** Отдельный клиент без authInterceptor/authenticator — только он используется внутри
     *  самого TokenAuthenticator для запроса на обновление сессии, чтобы не словить рекурсию
     *  (иначе обновляющий запрос сам мог бы получить 401 и снова вызвать аутентификатор). */
    @Provides
    @Singleton
    @Named("refresh")
    fun provideRefreshOkHttpClient(): OkHttpClient =
        OkHttpClient.Builder()
            .proxySelector(GodjiVpnService.tunnelAwareProxySelector())
            .build()

    /** Для проверки "публичный IP/DNS сейчас" (NetworkDiagnostics) — отдельный от
     *  provideOkHttpClient()/"refresh", чтобы точно не отправить авторизационные заголовки
     *  приложения (Bearer-токен, X-Requested-With) на сторонний ip-сервис. proxySelector тот
     *  же — именно он определяет, идёт ли запрос через локальный SOCKS Xray (когда туннель
     *  поднят) или напрямую (когда нет), то есть честно отражает то, что видит внешний мир. */
    @Provides
    @Singleton
    @Named("diagnostics")
    fun provideDiagnosticsOkHttpClient(): OkHttpClient =
        OkHttpClient.Builder()
            .proxySelector(GodjiVpnService.tunnelAwareProxySelector())
            .build()

    /** Сессионный JWT бэкенда живёт ровно 24 часа (проверено по exp/iat в самом токене) —
     *  без обновления пользователя стабильно выкидывало на логин раз в сутки. Веб-версия сайта
     *  при 401 сначала пытается POST /api/auth/refresh (по куке rw_refresh_token) и повторяет
     *  запрос — здесь та же логика через стандартный механизм OkHttp Authenticator: он
     *  вызывается автоматически именно на 401, и его успешный результат уходит на повторную
     *  попытку ДО того, как authInterceptor выше вообще увидит финальный ответ. */
    @Provides
    @Singleton
    fun provideTokenAuthenticator(
        tokenManager: TokenManager,
        @Named("refresh") refreshClient: OkHttpClient
    ): Authenticator = Authenticator { _, response ->
        if (responseCount(response) >= 2) return@Authenticator null
        if (response.request.url.encodedPath.startsWith("/api/auth/")) return@Authenticator null
        val failedToken = response.request.header("Authorization")?.removePrefix("Bearer ") ?: return@Authenticator null

        // Ровно в момент истечения JWT 401 получают сразу несколько запросов (подписка,
        // профиль, фоновый воркер). Бэкенд ротирует refresh-токен: второй параллельный
        // refresh со старым токеном получал отказ — и authInterceptor стирал уже свежую
        // сессию. Поэтому обновление — строго по одному, а кто ждал — берёт готовый токен.
        synchronized(refreshLock) {
            val current = tokenManager.accessToken() ?: return@Authenticator null
            if (current != failedToken) {
                return@Authenticator response.request.newBuilder().header("Authorization", "Bearer $current").build()
            }
            val refreshToken = tokenManager.refreshToken() ?: return@Authenticator null

            val refreshRequest = Request.Builder()
                .url(BASE_URL + "api/auth/refresh")
                .post("".toRequestBody(null))
                .header("Cookie", "rw_refresh_token=$refreshToken")
                .header("X-Requested-With", "XMLHttpRequest")
                .build()

            // Сетевой сбой самого обновления (нет сети, туннель переподключается) — НЕ повод
            // разлогинивать: IOException роняет исходный запрос как обычную сетевую ошибку,
            // 401 до authInterceptor не доходит, сессия обновится при следующем запросе.
            refreshClient.newCall(refreshRequest).execute().use { refreshResponse ->
                val newToken = extractCookieValue(refreshResponse.headers, "rw_session_token")
                when {
                    (refreshResponse.isSuccessful || refreshResponse.code == 409) && newToken != null -> {
                        // Ротация refresh-токена — если бэкенд не прислал новый, оставляем прежний.
                        extractCookieValue(refreshResponse.headers, "rw_refresh_token")?.let(tokenManager::saveRefreshToken)
                        // Значение здесь ни на что не влияет — TokenManager.isLoggedIn() его не
                        // читает, источник правды о протухшем токене — 401 от бэкенда.
                        tokenManager.save(newToken, 24L * 3600)
                        response.request.newBuilder().header("Authorization", "Bearer $newToken").build()
                    }
                    // Refresh-токен действительно отвергнут ("Invalid refresh token") —
                    // сессия мертва, дальше authInterceptor отправит на логин.
                    refreshResponse.code == 400 || refreshResponse.code == 401 -> null
                    // 409 без новой сессии (веб-клиент сайта считает его "уже обновлено
                    // параллельно"), 429, 5xx, CDN — временное: не разлогиниваем.
                    else -> throw java.io.IOException("session refresh failed: HTTP ${refreshResponse.code}")
                }
            }
        }
    }

    private val refreshLock = Any()

    private fun responseCount(response: Response): Int {
        var result = 1
        var prior = response.priorResponse
        while (prior != null) { result++; prior = prior.priorResponse }
        return result
    }

    @Provides
    @Singleton
    fun provideOkHttpClient(authInterceptor: Interceptor, tokenAuthenticator: Authenticator): OkHttpClient {
        val logging = HttpLoggingInterceptor().apply {
            // BODY — удобно для отладки, но логирует токены в plaintext. Не забудьте
            // переключить на NONE/BASIC в релизной сборке.
            level = if (BuildConfig.DEBUG) HttpLoggingInterceptor.Level.BODY
                    else HttpLoggingInterceptor.Level.NONE
        }
        return OkHttpClient.Builder()
            .addInterceptor(authInterceptor)
            .addInterceptor(logging)
            .authenticator(tokenAuthenticator)
            // Пускаем запросы к gojihub.xyz через локальный SOCKS самого Xray, пока туннель
            // поднят — без этого собственные запросы приложения (и так исключённые из VPN,
            // см. GodjiVpnService.establishTunnel) шли по сырой сети телефона и падали в зоне
            // глушения мобильной сети, даже когда VPN на российский узел уже подключён.
            .proxySelector(GodjiVpnService.tunnelAwareProxySelector())
            .build()
    }

    @Provides
    @Singleton
    fun provideMoshi(): Moshi = Moshi.Builder().add(KotlinJsonAdapterFactory()).build()

    @Provides
    @Singleton
    fun provideRetrofit(client: OkHttpClient, moshi: Moshi): Retrofit =
        Retrofit.Builder()
            .baseUrl(BASE_URL)
            .client(client)
            .addConverterFactory(MoshiConverterFactory.create(moshi))
            .build()

    @Provides
    @Singleton
    fun provideRemnawaveApi(retrofit: Retrofit): RemnawaveApi =
        retrofit.create(RemnawaveApi::class.java)
}
