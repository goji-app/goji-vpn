package xyz.gojihub.vpn.network

import okhttp3.MultipartBody
import okhttp3.RequestBody
import retrofit2.Response
import retrofit2.http.Body
import retrofit2.http.DELETE
import retrofit2.http.GET
import retrofit2.http.Multipart
import retrofit2.http.PATCH
import retrofit2.http.POST
import retrofit2.http.Part
import retrofit2.http.Path
import retrofit2.http.Query
import xyz.gojihub.vpn.network.models.*

// Пути и форматы ниже сверены напрямую с реальным бэкендом gojihub.xyz
// (перехвачены сетевые запросы веб-версии приложения через email-вход).
interface RemnawaveApi {

    @POST("api/auth/email/send-otp")
    suspend fun sendOtp(@Body body: SendOtpRequest): SendOtpResponse

    // Response<...>, а не голый VerifyOtpResponse — с 7.1.0 сам JWT-токен сессии приходит
    // только в заголовке Set-Cookie (rw_session_token), тела ответа для этого недостаточно.
    // См. AuthRepository.verifyOtp() и комментарий у VerifyOtpResponse.
    @POST("api/auth/email/verify-otp")
    suspend fun verifyOtp(@Body body: VerifyOtpRequest): Response<VerifyOtpResponse>

    @GET("api/auth/me")
    suspend fun getMe(): MeResponse

    // provider: "google" | "yandex" | "telegram-oidc" (vk/apple существуют в UI, но сейчас
    // не сконфигурированы на бэкенде — возвращают 403).
    // Параметр называется именно app_redirect (сверено с логами бэкенда: с redirect_uri
    // сервер оставлял origin="" и не редиректил обратно в приложение после логина).
    @GET("api/auth/{provider}/start")
    suspend fun startOAuth(
        @Path("provider") provider: String,
        @Query("app_redirect") appRedirect: String,
        @Query("code_challenge") codeChallenge: String,
        @Query("code_challenge_method") codeChallengeMethod: String = "S256"
    ): StartAuthResponse

    @POST("api/auth/native/exchange")
    suspend fun exchangeNativeOAuth(@Body body: NativeExchangeRequest): NativeExchangeResponse

    @POST("api/auth/consent")
    suspend fun consent(@Body body: ConsentRequest): Response<Void>

    // Сводка кабинета — из неё нужны только поля пробного периода.
    @GET("api/dashboard")
    suspend fun getDashboard(): DashboardResponse

    // Бесплатный пробный период для аккаунта без подписки (как кнопка в веб-кабинете).
    @POST("api/dashboard/trial")
    suspend fun activateTrial(@Body body: EmptyBody = EmptyBody()): Response<TrialResponse>

    @GET("api/subscriptions")
    suspend fun getSubscriptions(): SubscriptionsResponse

    // С бэкенда 7.1.0 GET api/subscriptions (список выше) больше не отдаёт traffic вообще —
    // подтверждено живым запросом (см. SubscriptionInfo.traffic) — только этот, детальный
    // эндпоинт по конкретному id. SubscriptionRepository.refresh() дозапрашивает его для
    // выбранной подписки.
    @GET("api/subscriptions/{id}")
    suspend fun getSubscription(@Path("id") id: Long): SubscriptionInfo

    // subscriptionId — веб-версия (Mh.getCatalog в её бандле) передаёт его как ?subscription_id=,
    // персональная скидка (customer_discount_percent) считается бэкендом ИМЕННО относительно
    // конкретной подписки, а не аккаунта вообще; без него ответ — обобщённый каталог без
    // привязки к подписке, где то же поле может означать что-то другое (см. живой тест: без
    // subscription_id пришло 100%, что для обычного тарифа неправдоподобно).
    @GET("api/dashboard/plans")
    suspend fun getPlans(@Query("subscription_id") subscriptionId: Long? = null): PlansResponse

    // Страница "Мои рассылки"/"Новости" веб-версии (#/my-broadcasts) — список уже
    // отправленных пользователю новостей/объявлений.
    @GET("api/broadcasts/completed")
    suspend fun getBroadcasts(): List<BroadcastDto>

    // Страница "Рефералы" веб-версии (#/my-referrals, за флагом referral_enabled).
    @GET("api/dashboard/referrals")
    suspend fun getReferrals(): ReferralsResponse

    // Страница "Партнёрская программа" веб-версии (#/partner-dashboard, за флагом
    // partner_program_enabled) — только сводка/статус, см. PartnerStatusResponse.
    @GET("api/partner/status")
    suspend fun getPartnerStatus(): PartnerStatusResponse

    @GET("api/subscriptions/{id}/devices")
    suspend fun getDevices(@Path("id") id: Long): List<DeviceDto>

    @PATCH("api/subscriptions/{id}/devices/{hwid}")
    suspend fun renameDevice(
        @Path("id") id: Long,
        @Path("hwid") hwid: String,
        @Body body: RenameDeviceRequest
    ): Response<Void>

    @DELETE("api/subscriptions/{id}/devices/{hwid}")
    suspend fun deleteDevice(@Path("id") id: Long, @Path("hwid") hwid: String): Response<Void>

    // ── Поддержка (gojihub.xyz/api/support/*, customer-режим) ────────────
    // Пути сверены с JS-бандлом веб-версии: у customer-роли префикс "/api/support" (у
    // оператора — "/api/admin/support", у партнёра — "/api/partner-admin/support", это нам
    // не подходит); customer видит только свои тикеты — сервер сам их фильтрует по сессии,
    // отдельного query-параметра "мои" для этой роли нет (в отличие от оператора/партнёра).

    @GET("api/support/tickets")
    suspend fun getSupportTickets(
        @Query("status") status: String,
        @Query("limit") limit: Int = 20,
        @Query("offset") offset: Int = 0
    ): SupportTicketsResponse

    @GET("api/support/tickets/{id}")
    suspend fun getSupportTicket(@Path("id") ticketId: Long): SupportTicketDto

    // Nullable, не List<> напрямую — тот же nil-срез-как-null бэкенда, что и у
    // SupportTicketsResponse.tickets (подтверждено живым логом на пустом списке тикетов;
    // здесь на всякий случай та же защита, реального теста на пустой список сообщений не было).
    @GET("api/support/tickets/{id}/messages")
    suspend fun getSupportMessages(@Path("id") ticketId: Long): List<SupportMessageDto>?

    @POST("api/support/tickets")
    suspend fun createSupportTicket(@Body body: CreateSupportTicketRequest): CreateSupportTicketResponse

    @POST("api/support/tickets/{id}/messages")
    suspend fun sendSupportMessage(@Path("id") ticketId: Long, @Body body: SendSupportMessageRequest): Response<Void>

    // Тот же эндпоинт, что и sendSupportMessage выше, но multipart с вложениями — ровно
    // fallback-путь веб-клиента (простой FormData: message + повторяющиеся files, без
    // отдельного протокола init/finalize/abort для прогресс-бара по каждому файлу — тот
    // сложнее и не нужен мобильному клиенту без пошагового прогресса закачки).
    @Multipart
    @POST("api/support/tickets/{id}/messages")
    suspend fun sendSupportMessageWithFiles(
        @Path("id") ticketId: Long,
        @Part("message") message: RequestBody,
        @Part files: List<MultipartBody.Part>
    ): Response<Void>

    @GET("api/support/ticket-limit")
    suspend fun getSupportTicketLimit(): SupportTicketLimitResponse

    @GET("api/support/queues")
    suspend fun getSupportQueues(): List<SupportQueueDto>?

    @GET("api/faq")
    suspend fun getFaq(): FaqResponse
}
