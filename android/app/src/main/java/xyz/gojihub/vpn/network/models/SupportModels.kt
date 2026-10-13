package xyz.gojihub.vpn.network.models

import com.squareup.moshi.Json
import com.squareup.moshi.JsonClass

// ── Поддержка (gojihub.xyz/api/support/*) ─────────────────────────────────
// Формат и пути сверены так же, как и остальные веб-only разделы — разбором JS-бандла
// веб-версии (страница "Поддержка", customer-режим её тикет-виджета: /api/support/tickets,
// не /api/admin/support/* — тот отдельный, для операторов). Часть полей у тикета/сообщения
// в веб-версии используется только в операторском/партнёрском режиме (assignee_id,
// telegram_id, csat_rating и т.п.) — здесь не объявлены, если приложению их некуда показать.

@JsonClass(generateAdapter = true)
data class SupportTicketsResponse(
    // Нет обращений — бэкенд отдаёт "tickets": null, а не "[]" (похоже на типичную для Go
    // сериализацию nil-слайса) — подтверждено живым логом (Moshi упал бы на non-null List).
    val tickets: List<SupportTicketDto>? = null
)

@JsonClass(generateAdapter = true)
data class SupportTicketDto(
    val id: Long,
    val subject: String? = null,
    @Json(name = "last_message") val lastMessage: String? = null,
    // Виденные значения: open, waiting_customer, awaiting_reply, on_hold, closed.
    val status: String,
    @Json(name = "unread_count") val unreadCount: Int = 0,
    @Json(name = "created_at") val createdAt: String? = null
)

@JsonClass(generateAdapter = true)
data class SupportTicketLimitResponse(
    @Json(name = "can_create") val canCreate: Boolean,
    val limit: Int = 0,
    val active: Int = 0,
    @Json(name = "active_ticket_id") val activeTicketId: Long? = null
)

@JsonClass(generateAdapter = true)
data class SupportQueueDto(
    val id: Long,
    val name: String
)

@JsonClass(generateAdapter = true)
data class CreateSupportTicketRequest(
    val subject: String? = null,
    val message: String,
    @Json(name = "queue_id") val queueId: Long? = null
)

@JsonClass(generateAdapter = true)
data class CreateSupportTicketResponse(
    val ticket: SupportTicketDto
)

@JsonClass(generateAdapter = true)
data class SendSupportMessageRequest(
    val message: String,
    @Json(name = "reply_to_id") val replyToId: Long? = null
)

@JsonClass(generateAdapter = true)
data class SupportMessageDto(
    val id: Long,
    // "user" — сам клиент (мы), любое другое значение (operator/admin/bot/…) — сторона
    // поддержки. Не перечисляем весь набор ролей — на экране важно только "моё" сообщение
    // это или нет (см. SupportMessageDto.isMine).
    @Json(name = "sender_type") val senderType: String,
    @Json(name = "sender_name") val senderName: String? = null,
    val message: String? = null,
    @Json(name = "created_at") val createdAt: String? = null,
    @Json(name = "reply_to_id") val replyToId: Long? = null,
    // Непустой у системных событий ленты (тикет создан/назначен/закрыт и т.п.) — обычные
    // сообщения его не содержат.
    @Json(name = "event_type") val eventType: String? = null,
    val attachments: List<SupportAttachmentDto>? = null
) {
    val isMine: Boolean get() = senderType == "user"
    val isEvent: Boolean get() = !eventType.isNullOrBlank()
}

@JsonClass(generateAdapter = true)
data class SupportAttachmentDto(
    val uuid: String,
    @Json(name = "file_name") val fileName: String? = null,
    @Json(name = "file_type") val fileType: String? = null,
    @Json(name = "file_size") val fileSize: Long? = null
)

// ── FAQ (gojihub.xyz/api/faq) ──────────────────────────────────────────────

@JsonClass(generateAdapter = true)
data class FaqResponse(
    // Nullable — тот же nil-срез-как-null бэкенда, что и у SupportTicketsResponse.tickets.
    val ungrouped: List<FaqItemDto>? = null,
    val sections: List<FaqSectionDto>? = null
)

@JsonClass(generateAdapter = true)
data class FaqSectionDto(
    val id: Long,
    val name: String,
    val items: List<FaqItemDto>? = null
)

@JsonClass(generateAdapter = true)
data class FaqItemDto(
    val id: Long,
    val question: String,
    val answer: String
)
