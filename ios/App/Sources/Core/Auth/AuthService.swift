import Foundation

/// Вход в аккаунт кабинета.
///
/// На iOS сознательно ТОЛЬКО собственный вход сервиса (email + одноразовый код) и перенос
/// сессии QR-кодом с другого своего устройства. Вход через Google/Яндекс/Telegram здесь не
/// добавлен: правило App Store 4.8 требует при любом стороннем входе предлагать равноценный
/// «Вход с Apple», а он на бэкенде пока не настроен (см. docs/APP_STORE_COMPLIANCE.md).
final class AuthService {
    static let shared = AuthService()
    private let api = APIClient.shared
    private let tokens = TokenStore.shared

    var isLoggedIn: Bool { tokens.isLoggedIn }

    func sendCode(email: String) async throws {
        struct Response: Decodable { let sent: Bool? }
        let _: Response = try await api.post("api/auth/email/send-otp", body: SendOtpRequest(email: email))
        AppLog.i(.main, "код входа отправлен")
    }

    /// С бэкенда 7.1.0 JWT приходит только в Set-Cookie rw_session_token (не в теле).
    func verify(email: String, code: String) async throws {
        let body = try JSONEncoder().encode(VerifyOtpRequest(email: email, code: code))
        let (_, response) = try await api.send(method: "POST", path: "api/auth/email/verify-otp", body: body)
        guard let token = APIClient.cookie("rw_session_token", in: response) else {
            throw APIError.missingSessionCookie
        }
        if let refresh = APIClient.cookie("rw_refresh_token", in: response) { tokens.refreshToken = refresh }
        tokens.accessToken = token
        AppLog.i(.main, "вход по email выполнен")
        await acceptConsentIfNeeded()
    }

    /// Согласие с условиями/обработкой данных — один раз после регистрации. Пользователь
    /// видел ссылки на документы на экране входа до отправки кода.
    private func acceptConsentIfNeeded() async {
        do {
            let me: MeResponse = try await api.get("api/auth/me")
            if me.consentRequired == true {
                try await api.postNoContent("api/auth/consent", body: ConsentRequest())
                AppLog.i(.main, "согласие с условиями отправлено")
            }
        } catch {
            AppLog.w(.main, "проверка согласия: \(error.localizedDescription)")
        }
    }

    func me() async throws -> MeResponse {
        try await api.get("api/auth/me")
    }

    // MARK: - Перенос входа QR-кодом (совместим с Android/Windows)

    enum TransferError: Error {
        case invalid, expired, rejected

        var message: String {
            switch self {
            case .invalid: return L("transfer.error.invalid")
            case .expired: return L("transfer.error.expired")
            case .rejected: return L("transfer.error.rejected")
            }
        }
    }

    /// Код живёт 10 минут: старый снимок экрана не должен давать вход спустя дни.
    private let transferTTL: TimeInterval = 10 * 60

    /// godjivpn://transfer?s=<jwt>&r=<refresh>&t=<unix> — текущая сессия для другого устройства.
    func transferURI() -> String? {
        guard let token = tokens.accessToken else { return nil }
        var components = URLComponents()
        components.scheme = AppConstants.transferScheme
        components.host = AppConstants.transferHost
        var items = [URLQueryItem(name: "s", value: token)]
        if let refresh = tokens.refreshToken { items.append(URLQueryItem(name: "r", value: refresh)) }
        items.append(URLQueryItem(name: "t", value: String(Int(Date().timeIntervalSince1970))))
        components.queryItems = items
        return components.string
    }

    static func isTransferURI(_ raw: String) -> Bool {
        guard let url = URL(string: raw.trimmingCharacters(in: .whitespacesAndNewlines)) else { return false }
        return url.scheme == AppConstants.transferScheme && url.host == AppConstants.transferHost
    }

    /// Проверяет формат и срок, затем — что бэкенд принимает присланную сессию; только после
    /// этого заменяет текущую. При любой ошибке текущий вход не трогается.
    func login(transfer raw: String) async throws {
        guard let components = URLComponents(string: raw.trimmingCharacters(in: .whitespacesAndNewlines)),
              components.scheme == AppConstants.transferScheme, components.host == AppConstants.transferHost else {
            throw TransferError.invalid
        }
        let items = components.queryItems ?? []
        func value(_ name: String) -> String? { items.first { $0.name == name }?.value.flatMap { $0.isEmpty ? nil : $0 } }
        guard let session = value("s"), let created = value("t").flatMap(TimeInterval.init) else {
            throw TransferError.invalid
        }
        let age = Date().timeIntervalSince1970 - created
        guard age >= -300, age <= transferTTL else { throw TransferError.expired }
        do {
            _ = try await api.send(method: "GET", path: "api/auth/me", body: nil, bearer: session)
        } catch {
            throw TransferError.rejected
        }
        tokens.accessToken = session
        tokens.refreshToken = value("r")
        AppLog.i(.main, "вход по QR-переносу выполнен")
    }

    // MARK: - Выход и удаление аккаунта

    func logout() async {
        if tokens.isLoggedIn {
            _ = try? await api.send(method: "POST", path: "api/auth/logout", body: Data("{}".utf8))
        }
        tokens.clear()
        AppLog.i(.main, "выход из аккаунта")
    }

    /// Запрос на удаление аккаунта (правило App Store 5.1.1(v): удаление должно запускаться
    /// из приложения). Отдельного клиентского эндпоинта удаления на бэкенде пока нет —
    /// запрос уходит обращением в поддержку с фиксированной темой, команда удаляет аккаунт
    /// вручную. Когда на бэкенде появится эндпоинт, достаточно заполнить accountDeletionPath.
    static let accountDeletionPath: String? = nil

    func requestAccountDeletion(reason: String) async throws {
        if let path = Self.accountDeletionPath {
            try await api.delete(path)
        } else {
            let message = L("account.delete.ticket_message", reason.isEmpty ? "—" : reason)
            let request = CreateTicketRequest(subject: L("account.delete.ticket_subject"), message: message, queueId: nil)
            do {
                let _: CreateTicketResponse = try await api.post("api/support/tickets", body: request)
            } catch {
                // Лимит открытых обращений — дописываем запрос в уже открытое.
                let limit: SupportTicketLimit = try await api.get("api/support/ticket-limit")
                guard let active = limit.activeTicketId else { throw error }
                try await api.postNoContent("api/support/tickets/\(active)/messages", body: SendMessageRequest(message: message))
            }
        }
        AppLog.i(.main, "запрошено удаление аккаунта")
    }
}
