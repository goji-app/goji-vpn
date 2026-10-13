using System.Text.Json;
using System.Text.Json.Serialization;

namespace GodjiVpn.Models;

/// <summary>Живой ответ api/broadcasts/completed отдаёт "ID" числом, а не строкой (вопреки
/// исходному предположению по аналогии с Android BroadcastDto.id: String, см. комментарий
/// в BroadcastDto ниже) — читаем оба варианта, а не падаем на JsonException.</summary>
public sealed class FlexibleStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() ?? "",
            JsonTokenType.Number => reader.TryGetInt64(out var n) ? n.ToString() : reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.Null => "",
            _ => ""
        };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}

// Контракт подтверждён чтением реального кода Android-приложения
// (xyz.gojihub.vpn.network.models.Models.kt в каноническом источнике) — сверено
// напрямую с бэкендом gojihub.xyz разработчиком Android-версии, не догадка.

public sealed class SendOtpRequest
{
    public string Email { get; set; } = "";
}

public sealed class SendOtpResponse
{
    public bool Sent { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

public sealed class VerifyOtpRequest
{
    public string Email { get; set; } = "";
    public string Code { get; set; } = "";
}

// С обновления бэкенда до 7.1.0 тело больше не содержит токен вообще (подтверждено живым
// запросом на стороне Android — см. AuthRepository.kt/verifyOtp там) — сессия теперь выдаётся
// через HttpOnly Set-Cookie (rw_session_token), а не в JSON. Сам JWT из этой куки при этом
// по-прежнему работает как обычный Bearer-токен. См. ApiClient.VerifyOtpAsync — достаёт его
// из заголовков ответа, не из этого тела.
public sealed class VerifyOtpResponse
{
    [JsonPropertyName("expires_in")]
    public long ExpiresIn { get; set; }

    public AuthUser? User { get; set; }

    [JsonPropertyName("account_existed")]
    public bool AccountExisted { get; set; }
}

public sealed class AuthUser
{
    [JsonPropertyName("customer_id")]
    public string CustomerId { get; set; } = "";

    public string? Email { get; set; }
}

public sealed class MeResponse
{
    [JsonPropertyName("customer_id")]
    public string CustomerId { get; set; } = "";

    public string? Email { get; set; }
    public string Role { get; set; } = "";

    [JsonPropertyName("consent_required")]
    public bool ConsentRequired { get; set; }
}

public sealed class ConsentRequest
{
    public bool Terms { get; set; } = true;

    [JsonPropertyName("personal_data")]
    public bool PersonalData { get; set; } = true;
}

public sealed class SubscriptionsResponse
{
    public List<SubscriptionInfo> Subscriptions { get; set; } = new();
}

public sealed class SubscriptionInfo
{
    public long Id { get; set; }
    public string Name { get; set; } = "";

    [JsonPropertyName("is_primary")]
    public bool IsPrimary { get; set; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; }

    [JsonPropertyName("plan_name")]
    public string PlanName { get; set; } = "";

    [JsonPropertyName("expire_at")]
    public string ExpireAt { get; set; } = "";

    [JsonPropertyName("days_left")]
    public int DaysLeft { get; set; }

    [JsonPropertyName("subscription_link")]
    public string SubscriptionLink { get; set; } = "";

    [JsonPropertyName("device_limit")]
    public int DeviceLimit { get; set; }

    /// <summary>"trial" на пробном тарифе, иначе null — не приходит в старых ответах бэкенда,
    /// см. Models.kt (Android): по умолчанию считаем "не триал".</summary>
    public string? Kind { get; set; }

    /// <summary>С бэкенда 7.1.0 в списке api/subscriptions это поле больше не приходит (только
    /// в одиночном api/subscriptions/{id}) — см. Models.kt (Android): val traffic: TrafficInfo?
    /// = null. Раньше поле было не-nullable, из-за чего весь список подписок падал при парсинге
    /// целиком. См. SubscriptionRepository.RefreshAsync — дозапрашивает по id, если null.</summary>
    public TrafficInfo? Traffic { get; set; }
}

public sealed class TrafficInfo
{
    [JsonPropertyName("used_bytes")]
    public long UsedBytes { get; set; }

    [JsonPropertyName("limit_bytes")]
    public long LimitBytes { get; set; }

    [JsonPropertyName("is_unlimited")]
    public bool IsUnlimited { get; set; }
}

public sealed class PlansResponse
{
    public List<PlanInfo> Plans { get; set; } = new();

    /// <summary>Персональная скидка клиента в процентах (0-100) — то же поле, что веб-версия
    /// читает как customer_discount_percent на каталоге тарифов. null/отсутствует — скидки нет.</summary>
    [JsonPropertyName("customer_discount_percent")]
    public double? CustomerDiscountPercent { get; set; }
}

public sealed class PlanInfo
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<PriceInfo> Prices { get; set; } = new();
}

public sealed class PriceInfo
{
    [JsonPropertyName("price_type")]
    public string PriceType { get; set; } = "";

    public int Price { get; set; }
    public string Currency { get; set; } = "";

    [JsonPropertyName("period_value")]
    public int PeriodValue { get; set; }

    [JsonPropertyName("period_unit")]
    public string PeriodUnit { get; set; } = "";
}

// ── Новости/рассылки (gojihub.xyz/api/broadcasts/completed) — та же страница, что "Мои
//    рассылки" веб-версии. PropertyNameCaseInsensitive (Web defaults в ApiClient) сам сводит
//    "ID"/"Content"/"CreatedAt"/"Buttons" и их нижний регистр к этим же свойствам — в отличие
//    от Android (Moshi, строгий регистр), там ради этого нужны два отдельных поля-дубля. ────

public sealed class BroadcastDto
{
    // Реальный ответ отдаёт ID числом (не строкой, как в Android-эквиваленте) — см.
    // FlexibleStringConverter выше, подтверждено живым JsonException при первом тесте.
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string Id { get; set; } = "";
    public string Content { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public List<BroadcastButtonDto>? Buttons { get; set; }
}

public sealed class BroadcastButtonDto
{
    public string Url { get; set; } = "";
    public string Text { get; set; } = "";
}

// ── Рефералы (gojihub.xyz/api/dashboard/referrals) — формат сверен так же, как и broadcasts,
//    анализом JS-бандла веб-версии (ReferralsPage), без входа в чей-либо аккаунт. ─────────────

public sealed class ReferralsResponse
{
    public string Link { get; set; } = "";
    [JsonPropertyName("web_link")] public string? WebLink { get; set; }
    public string? Description { get; set; }
    public ReferralSummary Summary { get; set; } = new();
    public List<ReferralEntry> Referrals { get; set; } = new();
}

public sealed class ReferralSummary
{
    [JsonPropertyName("total_referrals")] public int TotalReferrals { get; set; }
    [JsonPropertyName("active_referrals")] public int ActiveReferrals { get; set; }
    [JsonPropertyName("total_bonus_days")] public int TotalBonusDays { get; set; }
}

public sealed class ReferralEntry
{
    public long Id { get; set; }
    [JsonPropertyName("tg_username")] public string? TgUsername { get; set; }
    [JsonPropertyName("tg_first_name")] public string? TgFirstName { get; set; }
    [JsonPropertyName("tg_last_name")] public string? TgLastName { get; set; }
    public string? Email { get; set; }
    [JsonPropertyName("referee_telegram_id")] public long? RefereeTelegramId { get; set; }
    [JsonPropertyName("referee_id")] public long? RefereeId { get; set; }
    [JsonPropertyName("is_active")] public bool IsActive { get; set; }
    [JsonPropertyName("used_at")] public string? UsedAt { get; set; }
    [JsonPropertyName("bonus_days")] public int BonusDays { get; set; }
}

// ── Партнёрская программа (gojihub.xyz/api/partner/status) — только то подмножество полей,
//    которое реально показываем (сводка/статус); форму заявки и вывода средств на клиенте не
//    переопределяем, открываем веб-версию (см. PlansViewModel/PlansView), как и "Продлить" для
//    тарифов — не переизобретаем денежные формы нативно. ─────────────────────────────────────

public sealed class PartnerStatusResponse
{
    [JsonPropertyName("is_partner")] public bool IsPartner { get; set; }
    public string? Description { get; set; }
    public PartnerApplication? Application { get; set; }
    public PartnerInfo? Partner { get; set; }
    public PartnerStats? Stats { get; set; }
    [JsonPropertyName("approval_message")] public string? ApprovalMessage { get; set; }
}

public sealed class PartnerApplication
{
    public string Status { get; set; } = "";
}

public sealed class PartnerInfo
{
    [JsonPropertyName("is_active")] public bool IsActive { get; set; }
    [JsonPropertyName("commission_rate")] public double CommissionRate { get; set; }
    [JsonPropertyName("available_balance")] public double AvailableBalance { get; set; }
    [JsonPropertyName("pending_balance")] public double? PendingBalance { get; set; }
    [JsonPropertyName("total_earned")] public double TotalEarned { get; set; }
}

public sealed class PartnerStats
{
    [JsonPropertyName("client_count")] public int ClientCount { get; set; }
}

// ── Устройства подписки (gojihub.xyz/api/subscriptions/{id}/devices) — формат сверен так же,
//    как и остальные веб-only разделы, анализом JS-бандла веб-версии (ClientDashboard).
//    Удаление на пробном/бесплатном тарифе веб-версия сознательно не даёт делать самостоятельно
//    ("удалить устройство можно только через поддержку") — чисто клиентская проверка на сайте
//    (см. PlansViewModel.DevicesDeleteSupportOnly), сам DELETE-эндпоинт её не требует, но мы её
//    повторяем, чтобы не давать в приложении то, что сайт намеренно прячет для этих тарифов. ──

public sealed class DeviceDto
{
    public string Hwid { get; set; } = "";

    [JsonPropertyName("readable_name")]
    public string? ReadableName { get; set; }

    public string? Platform { get; set; }

    [JsonPropertyName("user_agent")]
    public string? UserAgent { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
}

public sealed class RenameDeviceRequest
{
    [JsonPropertyName("readable_name")]
    public string ReadableName { get; set; } = "";
}

// ── Поддержка (gojihub.xyz/api/support/*, api/faq) — нативный чат поддержки, порт из
//    Android (720f5ff). Списочные поля везде nullable: бэкенд на Go отдаёт пустой список как
//    null (nil-slice), а не [] — подтверждено комментарием в исходном Android-коде, тот же
//    принцип уже применён выше у BroadcastDto.Buttons/ReferralsResponse. Для эндпоинтов,
//    отдающих список НЕПОСРЕДСТВЕННО телом ответа (messages/queues — не обёрнутых в объект),
//    сериализованный "null" может быть телом ответа целиком — см. ApiClient.GetNullableAsync,
//    обычный ReadOrThrowAsync такое тело ошибочно принял бы за "пустой ответ сервера".

public sealed class SupportTicketsResponse
{
    public List<SupportTicketDto>? Tickets { get; set; }
}

public sealed class SupportTicketDto
{
    public long Id { get; set; }
    public string? Subject { get; set; }

    [JsonPropertyName("last_message")]
    public string? LastMessage { get; set; }

    public string Status { get; set; } = "";

    [JsonPropertyName("unread_count")]
    public int UnreadCount { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
}

public sealed class SupportMessageDto
{
    public long Id { get; set; }

    [JsonPropertyName("sender_type")]
    public string SenderType { get; set; } = "";

    [JsonPropertyName("sender_name")]
    public string? SenderName { get; set; }

    public string? Message { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    /// <summary>Непустое — системное событие ленты ("тикет создан/назначен/закрыт"), не
    /// обычное сообщение (см. TicketChatViewModel.ToItem: IsEvent).</summary>
    [JsonPropertyName("event_type")]
    public string? EventType { get; set; }

    public List<SupportAttachmentDto>? Attachments { get; set; }
}

/// <summary>Только имя файла реально показывается в чате — скачать/открыть вложение из
/// приложения нельзя (в исходном Android-клиенте тоже нет такого endpoint'а/UI, см. отчёт
/// по 720f5ff), поэтому остальные поля (uuid/file_type/file_size) не объявлены.</summary>
public sealed class SupportAttachmentDto
{
    [JsonPropertyName("file_name")]
    public string? FileName { get; set; }
}

public sealed class CreateSupportTicketRequest
{
    public string? Subject { get; set; }
    public string Message { get; set; } = "";

    [JsonPropertyName("queue_id")]
    public long? QueueId { get; set; }
}

public sealed class CreateSupportTicketResponse
{
    public SupportTicketDto? Ticket { get; set; }
}

public sealed class SendSupportMessageRequest
{
    public string Message { get; set; } = "";
}

/// <summary>Проверяется превентивно ДО показа формы создания тикета — чтобы не дать
/// заполнить форму впустую и получить отказ на отправке (см. NewTicketViewModel.LoadAsync).
/// limit/active с бэкенда не читаем — в UI негде показать, важны только эти два поля.</summary>
public sealed class SupportTicketLimitResponse
{
    [JsonPropertyName("can_create")]
    public bool CanCreate { get; set; }

    [JsonPropertyName("active_ticket_id")]
    public long? ActiveTicketId { get; set; }
}

public sealed class SupportQueueDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class FaqResponse
{
    public List<FaqItemDto>? Ungrouped { get; set; }
    public List<FaqSectionDto>? Sections { get; set; }
}

public sealed class FaqSectionDto
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public List<FaqItemDto>? Items { get; set; }
}

public sealed class FaqItemDto
{
    public long Id { get; set; }
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "";
}

/// <summary>
/// Один сервер из подписки пользователя. ConnectPayloadJson — это уже готовый Xray-конфиг
/// (dns/routing/outbounds, без inbounds), полученный с subs.gojihub.xyz при переданном X-HWID.
/// </summary>
public sealed class VlessNode
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string ConnectPayloadJson { get; init; }
    public string? Uuid { get; init; }
}
