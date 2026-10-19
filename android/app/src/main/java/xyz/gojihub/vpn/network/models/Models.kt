package xyz.gojihub.vpn.network.models

import com.squareup.moshi.Json
import com.squareup.moshi.JsonClass

// ── Auth (email + OTP, см. <кабинет>/api/auth/email/*) ────

@JsonClass(generateAdapter = true)
data class SendOtpRequest(val email: String)

@JsonClass(generateAdapter = true)
data class SendOtpResponse(
    val sent: Boolean,
    @Json(name = "expires_in") val expiresIn: Int
)

@JsonClass(generateAdapter = true)
data class VerifyOtpRequest(val email: String, val code: String)

// С обновления бэкенда до 7.1.0 тело больше не содержит токен вообще (подтверждено живым
// запросом) — сессия теперь выдаётся через HttpOnly Set-Cookie (rw_session_token), а не в
// JSON. Сам JWT из этой куки при этом по-прежнему работает как обычный Bearer-токен (тоже
// проверено живым запросом) — поэтому AuthRepository.verifyOtp() достаёт его из заголовков
// ответа (см. RemnawaveApi.verifyOtp — Response<VerifyOtpResponse>), а не из этого тела.
@JsonClass(generateAdapter = true)
data class VerifyOtpResponse(
    @Json(name = "expires_in") val expiresIn: Long,
    val user: AuthUser,
    @Json(name = "account_existed") val accountExisted: Boolean
)

@JsonClass(generateAdapter = true)
data class AuthUser(
    @Json(name = "customer_id") val customerId: String,
    val email: String?
)

@JsonClass(generateAdapter = true)
data class MeResponse(
    @Json(name = "customer_id") val customerId: String,
    val email: String?,
    val role: String,
    @Json(name = "consent_required") val consentRequired: Boolean
)

@JsonClass(generateAdapter = true)
data class StartAuthResponse(
    @Json(name = "auth_url") val authUrl: String
)

@JsonClass(generateAdapter = true)
data class NativeExchangeRequest(
    val code: String,
    @Json(name = "code_verifier") val codeVerifier: String,
    val provider: String
)

// Сверено напрямую с реальным ответом бэкенда (перехвачен в логах OkHttp) — в отличие от
// /api/auth/email/verify-otp, здесь нет "token"/"user"/"account_existed", ключи называются
// access_token/refresh_token. Раньше этот ответ ошибочно парсился как VerifyOtpResponse —
// Moshi падал на отсутствующем обязательном поле "token" ещё до вызова onAuthenticated(),
// поэтому токен никогда не сохранялся, а пользователя тихо возвращало на экран логина.
@JsonClass(generateAdapter = true)
data class NativeExchangeResponse(
    @Json(name = "access_token") val accessToken: String,
    @Json(name = "refresh_token") val refreshToken: String?,
    @Json(name = "expires_in") val expiresIn: Long
)

// Поля сверены напрямую с бэкендом (перебором через curl с реальным токеном) —
// "terms" и "personal_data", ответ содержит terms_consent_at/pd_consent_at.
@JsonClass(generateAdapter = true)
data class ConsentRequest(
    val terms: Boolean = true,
    @Json(name = "personal_data") val personalData: Boolean = true
)

// ── Подписка (<кабинет>/api/subscriptions) ─────────────────

@JsonClass(generateAdapter = true)
data class SubscriptionsResponse(
    val subscriptions: List<SubscriptionInfo>
)

/**
 * 2026-10-19 в ответе появилась вторая подписка без тарифа: "plan_name": null. Обязательное
 * поле роняло разбор ВСЕГО списка — на «Подписке» стало «0 дней», хотя основная подписка жива.
 * Поэтому все поля из ответа необязательные (как в iOS), а экраны читают безопасные значения
 * через свойства ниже. Имена в JSON прежние — сохранённый кэш (AccountCache) читается как раньше.
 */
@JsonClass(generateAdapter = true)
data class SubscriptionInfo(
    val id: Long,
    @Json(name = "name") val nameOrNull: String? = null,
    @Json(name = "is_primary") val isPrimaryOrNull: Boolean? = null,
    @Json(name = "is_active") val isActiveOrNull: Boolean? = null,
    @Json(name = "plan_name") val planNameOrNull: String? = null,
    @Json(name = "expire_at") val expireAtOrNull: String? = null, // ISO-8601
    @Json(name = "days_left") val daysLeftOrNull: Int? = null,
    @Json(name = "subscription_link") val subscriptionLinkOrNull: String? = null,
    @Json(name = "device_limit") val deviceLimitOrNull: Int? = null,
    // Начиная с бэкенда 7.1.0 список /api/subscriptions больше не отдаёт traffic вообще
    // (подтверждено живым запросом) — переехал в отдельный GET /api/subscriptions/{id}.
    // null здесь — обычное дело, а не ошибка; SubscriptionRepository.refresh() отдельно
    // подтягивает недостающее через getSubscriptionDetail(). Раньше поле было обязательным
    // (без значения по умолчанию) — это и роняло разбор всего ответа при выходе этой версии
    // бэкенда, из-за чего подписка переставала подтягиваться целиком.
    val traffic: TrafficInfo? = null,
    // "trial" для пробных тарифов (device_limit:1) — см. project-subscription-hwid-gate.
    // Используется для разного порога уведомления об окончании (12ч для триала, 3 дня для
    // платных). Не приходит в старых ответах — по умолчанию null, тогда считаем "не триал".
    val kind: String? = null
) {
    val name: String get() = nameOrNull.orEmpty()
    val isPrimary: Boolean get() = isPrimaryOrNull == true
    val isActive: Boolean get() = isActiveOrNull == true
    val planName: String get() = planNameOrNull.orEmpty()
    val expireAt: String get() = expireAtOrNull.orEmpty()
    val daysLeft: Int get() = daysLeftOrNull ?: 0
    val subscriptionLink: String get() = subscriptionLinkOrNull.orEmpty()
    val deviceLimit: Int get() = deviceLimitOrNull ?: 0
    /** Подписка, через которую реально можно подключиться (есть ссылка на список серверов). */
    val hasLink: Boolean get() = subscriptionLink.isNotBlank()
}

@JsonClass(generateAdapter = true)
data class TrafficInfo(
    @Json(name = "used_bytes") val usedBytes: Long,
    @Json(name = "limit_bytes") val limitBytes: Long,
    @Json(name = "is_unlimited") val isUnlimited: Boolean
)

// ── Тарифы (<кабинет>/api/dashboard/plans) ─────────────────

@JsonClass(generateAdapter = true)
data class PlansResponse(
    val plans: List<PlanInfo>,
    // Персональная скидка клиента в процентах (0-100) — то же поле, что веб-версия читает
    // как customer_discount_percent на каталоге тарифов (см. PlansViewModel.getDiscountPercent
    // в JS-бандле кабинета). null/отсутствует — скидки нет, тогда просто не показываем.
    @Json(name = "customer_discount_percent") val customerDiscountPercent: Double? = null
)

@JsonClass(generateAdapter = true)
data class PlanInfo(
    val id: Long,
    val name: String,
    val description: String,
    val prices: List<PriceInfo>
)

@JsonClass(generateAdapter = true)
data class PriceInfo(
    @Json(name = "price_type") val priceType: String,
    val price: Int,
    val currency: String,
    @Json(name = "period_value") val periodValue: Int,
    @Json(name = "period_unit") val periodUnit: String
)

// ── Новости/рассылки (<кабинет>/api/broadcasts/completed) ──────────────
// Формат сверен с реальным ответом (перехвачен в сетевых запросах веб-версии, страница
// "Мои рассылки"/"Новости", #/my-broadcasts) — сама веб-версия защищается от
// непоследовательного регистра ключей (`e.Buttons||e.buttons||[]`), поэтому оба варианта
// объявлены здесь тоже, а BroadcastDto.buttons() ниже выбирает непустой.

@JsonClass(generateAdapter = true)
data class BroadcastDto(
    @Json(name = "ID") val id: String,
    @Json(name = "Content") val content: String,
    @Json(name = "CreatedAt") val createdAt: String,
    @Json(name = "Buttons") val buttonsUpper: List<BroadcastButtonDto>? = null,
    @Json(name = "buttons") val buttonsLower: List<BroadcastButtonDto>? = null
) {
    fun buttons(): List<BroadcastButtonDto> = buttonsUpper?.takeIf { it.isNotEmpty() } ?: buttonsLower.orEmpty()
}

@JsonClass(generateAdapter = true)
data class BroadcastButtonDto(val url: String, val text: String)

// ── Рефералы (<кабинет>/api/dashboard/referrals) ────────────────────────
// Формат сверен так же, как и broadcasts — анализом JS-бандла веб-версии
// (ReferralsPage), без входа в чей-либо аккаунт.

@JsonClass(generateAdapter = true)
data class ReferralsResponse(
    val link: String,
    @Json(name = "web_link") val webLink: String?,
    val description: String?,
    val summary: ReferralSummary,
    val referrals: List<ReferralEntry>
)

@JsonClass(generateAdapter = true)
data class ReferralSummary(
    @Json(name = "total_referrals") val totalReferrals: Int,
    @Json(name = "active_referrals") val activeReferrals: Int,
    @Json(name = "total_bonus_days") val totalBonusDays: Int
)

@JsonClass(generateAdapter = true)
data class ReferralEntry(
    val id: Long,
    @Json(name = "tg_username") val tgUsername: String? = null,
    @Json(name = "tg_first_name") val tgFirstName: String? = null,
    @Json(name = "tg_last_name") val tgLastName: String? = null,
    val email: String? = null,
    @Json(name = "referee_telegram_id") val refereeTelegramId: Long? = null,
    @Json(name = "referee_id") val refereeId: Long? = null,
    @Json(name = "is_active") val isActive: Boolean,
    @Json(name = "used_at") val usedAt: String?,
    @Json(name = "bonus_days") val bonusDays: Int
)

// ── Партнёрская программа (<кабинет>/api/partner/status) ────────────────
// Только то подмножество полей, которое реально показываем (сводка/статус) — форму заявки
// и вывода средств на клиенте не переопределяем, открываем веб-версию (см. PlansScreen),
// как и "Продлить" для тарифов — те же соображения: не переизобретать денежные формы нативно.

@JsonClass(generateAdapter = true)
data class PartnerStatusResponse(
    @Json(name = "is_partner") val isPartner: Boolean,
    val description: String?,
    val application: PartnerApplication?,
    val partner: PartnerInfo?,
    val stats: PartnerStats?,
    @Json(name = "approval_message") val approvalMessage: String?
)

@JsonClass(generateAdapter = true)
data class PartnerApplication(val status: String)

@JsonClass(generateAdapter = true)
data class PartnerInfo(
    @Json(name = "is_active") val isActive: Boolean,
    @Json(name = "commission_rate") val commissionRate: Double,
    @Json(name = "available_balance") val availableBalance: Double,
    @Json(name = "pending_balance") val pendingBalance: Double? = null,
    @Json(name = "total_earned") val totalEarned: Double
)

@JsonClass(generateAdapter = true)
data class PartnerStats(@Json(name = "client_count") val clientCount: Int = 0)

// ── Устройства подписки (<кабинет>/api/subscriptions/{id}/devices) ──────
// Формат сверен так же, как и остальные веб-only разделы — анализом JS-бандла веб-версии
// (ClientDashboard). Удаление на пробном/бесплатном тарифе (kind == "trial"/"free") веб-версия
// сознательно не даёт делать самостоятельно ("удалить устройство можно только через поддержку") —
// это чисто клиентская проверка на сайте (см. PlansViewModel), сам DELETE-эндпоинт её не требует,
// но мы её повторяем, чтобы не давать в приложении то, что сайт намеренно прячет для этих тарифов.

@JsonClass(generateAdapter = true)
data class DeviceDto(
    val hwid: String,
    @Json(name = "readable_name") val readableName: String?,
    val platform: String?,
    @Json(name = "user_agent") val userAgent: String?,
    @Json(name = "created_at") val createdAt: String?,
    // Модель и версия ОС — из заголовков X-Device-Model / X-Ver-OS запроса подписки; их же
    // показывает сайт (ClientDashboard: название = readable_name || device_model || platform).
    @Json(name = "device_model") val deviceModel: String? = null,
    @Json(name = "os_version") val osVersion: String? = null
)

@JsonClass(generateAdapter = true)
data class RenameDeviceRequest(@Json(name = "readable_name") val readableName: String)

// ── Пробный период (<кабинет>/api/dashboard, /api/dashboard/trial) ──
// Поля сверены с JS-бандлом веб-кабинета (ClientDashboard: trial_available/trial_days/
// trial_paid, POST /api/dashboard/trial с пустым телом → {success, message}).

@JsonClass(generateAdapter = true)
data class DashboardResponse(
    @Json(name = "trial_available") val trialAvailable: Boolean? = null,
    @Json(name = "trial_days") val trialDays: Int? = null,
    @Json(name = "trial_paid") val trialPaid: Boolean? = null
)

@JsonClass(generateAdapter = true)
class EmptyBody

@JsonClass(generateAdapter = true)
data class TrialResponse(
    val success: Boolean? = null,
    val message: String? = null
)
