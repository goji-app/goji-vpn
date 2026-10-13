import Foundation

// Модели ответов кабинета — те же поля, что сверены в Android-клиенте (RemnawaveApi.kt /
// Models.kt) с живым бэкендом. Почти всё опционально: бэкенд на Go отдаёт null вместо
// пустых списков и местами меняет набор полей между версиями — разбор не должен падать
// целиком из-за одного отсутствующего поля.

// MARK: - Авторизация

struct SendOtpRequest: Encodable { let email: String }

struct VerifyOtpRequest: Encodable {
    let email: String
    let code: String
}

struct VerifyOtpResponse: Decodable {
    let expiresIn: Int64?
    let accountExisted: Bool?

    enum CodingKeys: String, CodingKey {
        case expiresIn = "expires_in"
        case accountExisted = "account_existed"
    }
}

struct MeResponse: Decodable {
    let customerId: String?
    let email: String?
    let role: String?
    let consentRequired: Bool?

    enum CodingKeys: String, CodingKey {
        case customerId = "customer_id"
        case email, role
        case consentRequired = "consent_required"
    }
}

struct ConsentRequest: Encodable {
    let terms = true
    let personalData = true

    enum CodingKeys: String, CodingKey {
        case terms
        case personalData = "personal_data"
    }
}

// MARK: - Подписка

struct SubscriptionsResponse: Decodable {
    let subscriptions: [SubscriptionInfo]?
}

struct TrafficInfo: Codable, Equatable {
    let usedBytes: Int64
    let limitBytes: Int64
    let isUnlimited: Bool

    enum CodingKeys: String, CodingKey {
        case usedBytes = "used_bytes"
        case limitBytes = "limit_bytes"
        case isUnlimited = "is_unlimited"
    }
}

struct SubscriptionInfo: Codable, Equatable, Identifiable {
    let id: Int64
    let name: String?
    let isPrimary: Bool?
    let isActive: Bool?
    let planName: String?
    let expireAt: String?
    let daysLeft: Int?
    let subscriptionLink: String?
    let deviceLimit: Int?
    let traffic: TrafficInfo?
    let kind: String?

    enum CodingKeys: String, CodingKey {
        case id, name, traffic, kind
        case isPrimary = "is_primary"
        case isActive = "is_active"
        case planName = "plan_name"
        case expireAt = "expire_at"
        case daysLeft = "days_left"
        case subscriptionLink = "subscription_link"
        case deviceLimit = "device_limit"
    }

    var isTrialOrFree: Bool { kind == "trial" || kind == "free" }

    var expireDate: Date? { expireAt.flatMap(DateParsing.parse) }
}

// MARK: - Новости

struct BroadcastButton: Codable, Equatable, Hashable {
    let url: String
    let text: String
}

struct Broadcast: Codable, Equatable, Identifiable {
    let id: String
    let content: String
    let createdAt: String?
    let buttonsUpper: [BroadcastButton]?
    let buttonsLower: [BroadcastButton]?

    enum CodingKeys: String, CodingKey {
        case id = "ID"
        case content = "Content"
        case createdAt = "CreatedAt"
        case buttonsUpper = "Buttons"
        case buttonsLower = "buttons"
    }

    var buttons: [BroadcastButton] {
        if let upper = buttonsUpper, !upper.isEmpty { return upper }
        return buttonsLower ?? []
    }
}

// MARK: - Рефералы

struct ReferralsResponse: Decodable {
    let link: String?
    let webLink: String?
    let description: String?
    let summary: ReferralSummary?
    let referrals: [ReferralEntry]?

    enum CodingKeys: String, CodingKey {
        case link, description, summary, referrals
        case webLink = "web_link"
    }
}

struct ReferralSummary: Decodable {
    let totalReferrals: Int?
    let activeReferrals: Int?
    let totalBonusDays: Int?

    enum CodingKeys: String, CodingKey {
        case totalReferrals = "total_referrals"
        case activeReferrals = "active_referrals"
        case totalBonusDays = "total_bonus_days"
    }
}

struct ReferralEntry: Decodable, Identifiable {
    let id: Int64
    let tgUsername: String?
    let tgFirstName: String?
    let email: String?
    let isActive: Bool?
    let usedAt: String?
    let bonusDays: Int?

    enum CodingKeys: String, CodingKey {
        case id, email
        case tgUsername = "tg_username"
        case tgFirstName = "tg_first_name"
        case isActive = "is_active"
        case usedAt = "used_at"
        case bonusDays = "bonus_days"
    }

    var displayName: String {
        if let name = tgFirstName, !name.isEmpty { return name }
        if let user = tgUsername, !user.isEmpty { return "@\(user)" }
        if let email, !email.isEmpty { return email }
        return "#\(id)"
    }
}

// MARK: - Устройства

struct DeviceInfo: Decodable, Identifiable, Equatable {
    let hwid: String
    let readableName: String?
    let platform: String?
    let userAgent: String?
    let createdAt: String?
    // Модель и версия ОС (заголовки X-Device-Model / X-Ver-OS запроса подписки) — их же показывает
    // сайт: название = readable_name || device_model || platform.
    var deviceModel: String? = nil
    var osVersion: String? = nil

    var id: String { hwid }

    /// Название как на сайте: своё имя, иначе модель, иначе платформа.
    var displayName: String {
        [readableName, deviceModel, platform]
            .compactMap { $0?.trimmingCharacters(in: .whitespaces) }
            .first { !$0.isEmpty } ?? String(hwid.prefix(8))
    }

    /// Строка под названием, как на сайте: платформа · версия ОС · клиент (User-Agent до «/»).
    var details: String {
        [platform, osVersion, userAgent?.split(separator: "/").first.map(String.init)]
            .compactMap { $0?.trimmingCharacters(in: .whitespaces) }
            .filter { !$0.isEmpty }
            .joined(separator: " · ")
    }

    enum CodingKeys: String, CodingKey {
        case hwid, platform
        case readableName = "readable_name"
        case userAgent = "user_agent"
        case createdAt = "created_at"
        case deviceModel = "device_model"
        case osVersion = "os_version"
    }
}

struct RenameDeviceRequest: Encodable {
    let readableName: String
    enum CodingKeys: String, CodingKey { case readableName = "readable_name" }
}

// MARK: - Пробный период

struct TrialResponse: Decodable {
    let success: Bool?
    let message: String?
}

// MARK: - Поддержка

struct SupportTicketsResponse: Decodable {
    let tickets: [SupportTicket]?
}

struct SupportTicket: Decodable, Identifiable, Equatable {
    let id: Int64
    let subject: String?
    let lastMessage: String?
    let status: String
    let unreadCount: Int?
    let createdAt: String?

    enum CodingKeys: String, CodingKey {
        case id, subject, status
        case lastMessage = "last_message"
        case unreadCount = "unread_count"
        case createdAt = "created_at"
    }

    var isClosed: Bool { status == "closed" }
}

struct SupportTicketLimit: Decodable {
    let canCreate: Bool
    let activeTicketId: Int64?

    enum CodingKeys: String, CodingKey {
        case canCreate = "can_create"
        case activeTicketId = "active_ticket_id"
    }
}

struct SupportQueue: Decodable, Identifiable, Hashable {
    let id: Int64
    let name: String
}

struct CreateTicketRequest: Encodable {
    let subject: String?
    let message: String
    let queueId: Int64?

    enum CodingKeys: String, CodingKey {
        case subject, message
        case queueId = "queue_id"
    }
}

struct CreateTicketResponse: Decodable {
    let ticket: SupportTicket
}

struct SendMessageRequest: Encodable {
    let message: String
}

struct SupportAttachment: Decodable, Equatable, Hashable {
    let uuid: String
    let fileName: String?
    let fileType: String?
    let fileSize: Int64?

    enum CodingKeys: String, CodingKey {
        case uuid
        case fileName = "file_name"
        case fileType = "file_type"
        case fileSize = "file_size"
    }
}

struct SupportMessage: Decodable, Identifiable, Equatable {
    let id: Int64
    let senderType: String
    let senderName: String?
    let message: String?
    let createdAt: String?
    let eventType: String?
    let attachments: [SupportAttachment]?

    enum CodingKeys: String, CodingKey {
        case id, message, attachments
        case senderType = "sender_type"
        case senderName = "sender_name"
        case createdAt = "created_at"
        case eventType = "event_type"
    }

    var isMine: Bool { senderType == "user" }
    var isEvent: Bool { !(eventType ?? "").isEmpty }
}

struct FaqResponse: Decodable {
    let ungrouped: [FaqItem]?
    let sections: [FaqSection]?
}

struct FaqSection: Decodable, Identifiable {
    let id: Int64
    let name: String
    let items: [FaqItem]?
}

struct FaqItem: Decodable, Identifiable {
    let id: Int64
    let question: String
    let answer: String
}

// MARK: - Даты

enum DateParsing {
    private static let iso: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()
    private static let isoNoFraction: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime]
        return f
    }()

    static func parse(_ string: String) -> Date? {
        iso.date(from: string) ?? isoNoFraction.date(from: string)
    }

    static func shortDate(_ date: Date) -> String {
        let f = DateFormatter()
        f.dateStyle = .medium
        f.timeStyle = .none
        return f.string(from: date)
    }

    static func dateTime(_ string: String?) -> String {
        guard let string, let date = parse(string) else { return "" }
        let f = DateFormatter()
        f.dateStyle = .short
        f.timeStyle = .short
        return f.string(from: date)
    }
}
