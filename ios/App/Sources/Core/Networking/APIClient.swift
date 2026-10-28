import Foundation

enum APIError: LocalizedError {
    case http(Int, String?)
    case unauthorized
    case decoding(String)
    case network(String)
    case missingSessionCookie

    var errorDescription: String? {
        switch self {
        case .http(let code, let message):
            if let message, !message.isEmpty { return message }
            return L("error.http", code)
        case .unauthorized: return L("error.session_expired")
        case .decoding: return L("error.bad_response")
        case .network(let message): return message
        case .missingSessionCookie: return L("error.bad_response")
        }
    }
}

extension Notification.Name {
    /// Бэкенд окончательно отверг сессию (401 и обновить не удалось) — нужно на экран входа.
    static let sessionExpired = Notification.Name("goji.sessionExpired")
}

/// HTTP-клиент кабинета. Ключевые особенности бэкенда (сверены на Android):
///  - на POST /api/auth/* обязателен заголовок X-Requested-With (лёгкая CSRF-защита);
///  - токен сессии приходит в Set-Cookie rw_session_token и дальше работает как Bearer;
///  - JWT живёт 24 ч, продлевается POST /api/auth/refresh с кукой rw_refresh_token.
/// Пока VPN поднят, запросы приложения идут через туннель (в отличие от Android, где
/// приложение исключено из туннеля) — в зоне глушения мобильной сети это и нужно.
final class APIClient {
    static let shared = APIClient()

    private let session: URLSession
    private let tokens = TokenStore.shared
    private let refresher = RefreshCoordinator()

    init() {
        let config = URLSessionConfiguration.ephemeral
        config.httpCookieStorage = nil
        config.httpShouldSetCookies = false
        config.httpCookieAcceptPolicy = .never
        config.timeoutIntervalForRequest = 20
        config.timeoutIntervalForResource = 60
        config.waitsForConnectivity = false
        session = URLSession(configuration: config)
    }

    // MARK: - Общий запрос

    func get<T: Decodable>(_ path: String, query: [URLQueryItem] = []) async throws -> T {
        let (data, _) = try await send(method: "GET", path: path, query: query, body: nil)
        return try decode(data)
    }

    func post<T: Decodable, B: Encodable>(_ path: String, body: B) async throws -> T {
        let (data, _) = try await send(method: "POST", path: path, body: try JSONEncoder().encode(body))
        return try decode(data)
    }

    func postNoContent<B: Encodable>(_ path: String, body: B) async throws {
        _ = try await send(method: "POST", path: path, body: try JSONEncoder().encode(body))
    }

    func patchNoContent<B: Encodable>(_ path: String, body: B) async throws {
        _ = try await send(method: "PATCH", path: path, body: try JSONEncoder().encode(body))
    }

    func delete(_ path: String) async throws {
        _ = try await send(method: "DELETE", path: path, body: nil)
    }

    /// Запрос с проверкой статуса и одним повтором после обновления сессии на 401.
    @discardableResult
    func send(
        method: String,
        path: String,
        query: [URLQueryItem] = [],
        body: Data?,
        contentType: String = "application/json",
        bearer overrideToken: String? = nil
    ) async throws -> (Data, HTTPURLResponse) {
        let isAuthPath = path.hasPrefix("api/auth/")
        let token = overrideToken ?? tokens.accessToken
        var (data, response) = try await perform(method: method, path: path, query: query, body: body, contentType: contentType, token: token)

        if response.statusCode == 401, token != nil, !isAuthPath, overrideToken == nil {
            // Пока ждали ответа, параллельный запрос мог уже продлить сессию — тогда просто повтор.
            let outcome: RefreshOutcome
            if let current = tokens.accessToken, current != token {
                outcome = .refreshed(current)
            } else {
                outcome = await refresher.refresh(using: self)
            }
            switch outcome {
            case .refreshed(let fresh):
                (data, response) = try await perform(method: method, path: path, query: query, body: body, contentType: contentType, token: fresh)
            case .temporary:
                // Нет сети, таймаут, 5xx, ошибка CDN — не повод стирать вход (как Android 955fd69):
                // запрос падает как обычная сетевая ошибка, сессия продлится при следующем.
                throw APIError.network(L("error.network"))
            case .rejected:
                break
            }
            if response.statusCode == 401 {
                AppLog.w(.main, "сессия отвергнута бэкендом (\(path)) — выход")
                tokens.clear()
                await MainActor.run { NotificationCenter.default.post(name: .sessionExpired, object: nil) }
                throw APIError.unauthorized
            }
        }
        guard (200..<300).contains(response.statusCode) else {
            throw APIError.http(response.statusCode, Self.errorMessage(from: data))
        }
        return (data, response)
    }

    private func perform(
        method: String,
        path: String,
        query: [URLQueryItem],
        body: Data?,
        contentType: String,
        token: String?
    ) async throws -> (Data, HTTPURLResponse) {
        var components = URLComponents(url: AppConstants.apiBaseURL.appendingPathComponent(path), resolvingAgainstBaseURL: false)!
        if !query.isEmpty { components.queryItems = query }
        var request = URLRequest(url: components.url!)
        request.httpMethod = method
        request.setValue("XMLHttpRequest", forHTTPHeaderField: "X-Requested-With")
        request.setValue(AppConstants.apiUserAgent, forHTTPHeaderField: "User-Agent")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        if let token { request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization") }
        if let body {
            request.httpBody = body
            request.setValue(contentType, forHTTPHeaderField: "Content-Type")
        }
        do {
            let (data, response) = try await session.data(for: request)
            guard let http = response as? HTTPURLResponse else { throw APIError.network(L("error.network")) }
            return (data, http)
        } catch let error as APIError {
            throw error
        } catch {
            AppLog.w(.main, "\(method) \(path): \(error.localizedDescription)")
            throw APIError.network(L("error.network"))
        }
    }

    func decode<T: Decodable>(_ data: Data) throws -> T {
        do {
            return try JSONDecoder().decode(T.self, from: data)
        } catch {
            AppLog.e(.main, "разбор ответа \(T.self)", error)
            throw APIError.decoding(String(describing: error))
        }
    }

    static func errorMessage(from data: Data) -> String? {
        guard let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
        for key in ["message", "error", "detail"] {
            if let text = json[key] as? String, !text.isEmpty { return text }
        }
        return nil
    }

    // MARK: - Куки сессии

    static func cookie(_ name: String, in response: HTTPURLResponse) -> String? {
        let headers = response.allHeaderFields.reduce(into: [String: String]()) { result, pair in
            if let key = pair.key as? String, let value = pair.value as? String { result[key] = value }
        }
        let cookies = HTTPCookie.cookies(withResponseHeaderFields: headers, for: AppConstants.apiBaseURL)
        return cookies.first { $0.name == name }?.value
    }

    /// POST /api/auth/refresh с кукой rw_refresh_token → новый rw_session_token. Отказ — только
    /// 400/401 (refresh-токен отвергнут); 409 с новой сессией — успех (сайт считает его «уже
    /// продлено параллельно»); всё остальное временное — вход не стирается.
    fileprivate func performRefresh() async -> RefreshOutcome {
        guard let refresh = tokens.refreshToken else { return .rejected }
        var request = URLRequest(url: AppConstants.apiBaseURL.appendingPathComponent("api/auth/refresh"))
        request.httpMethod = "POST"
        request.setValue("rw_refresh_token=\(refresh)", forHTTPHeaderField: "Cookie")
        request.setValue("XMLHttpRequest", forHTTPHeaderField: "X-Requested-With")
        request.setValue(AppConstants.apiUserAgent, forHTTPHeaderField: "User-Agent")
        guard let result = try? await session.data(for: request), let http = result.1 as? HTTPURLResponse else {
            AppLog.w(.main, "обновление сессии: нет связи — попробуем позже")
            return .temporary
        }
        if (200..<300).contains(http.statusCode) || http.statusCode == 409,
           let fresh = Self.cookie("rw_session_token", in: http) {
            if let rotated = Self.cookie("rw_refresh_token", in: http) { tokens.refreshToken = rotated }
            tokens.accessToken = fresh
            AppLog.i(.main, "сессия продлена")
            return .refreshed(fresh)
        }
        AppLog.w(.main, "обновление сессии: HTTP \(http.statusCode)")
        return http.statusCode == 400 || http.statusCode == 401 ? .rejected : .temporary
    }

    // MARK: - Multipart (вложения в поддержку)

    struct UploadFile {
        let name: String
        let mimeType: String
        let data: Data
    }

    func sendMultipart(path: String, fields: [String: String], files: [UploadFile]) async throws {
        let boundary = "Boundary-\(UUID().uuidString)"
        var body = Data()
        func append(_ string: String) { body.append(Data(string.utf8)) }
        for (name, value) in fields {
            append("--\(boundary)\r\n")
            append("Content-Disposition: form-data; name=\"\(name)\"\r\n\r\n")
            append("\(value)\r\n")
        }
        for file in files {
            let safeName = file.name.replacingOccurrences(of: "\"", with: "")
            append("--\(boundary)\r\n")
            append("Content-Disposition: form-data; name=\"files\"; filename=\"\(safeName)\"\r\n")
            append("Content-Type: \(file.mimeType)\r\n\r\n")
            body.append(file.data)
            append("\r\n")
        }
        append("--\(boundary)--\r\n")
        _ = try await send(method: "POST", path: path, body: body, contentType: "multipart/form-data; boundary=\(boundary)")
    }
}

enum RefreshOutcome {
    case refreshed(String)
    /// Refresh-токена нет или сервер его отверг — сессия мертва, нужен вход.
    case rejected
    /// Сеть, таймаут, 5xx, 429 — вход сохраняем.
    case temporary
}

/// Одновременные 401 от нескольких запросов должны вызвать ровно одно обновление сессии.
private actor RefreshCoordinator {
    private var inFlight: Task<RefreshOutcome, Never>?

    func refresh(using client: APIClient) async -> RefreshOutcome {
        if let inFlight { return await inFlight.value }
        let task = Task { await client.performRefresh() }
        inFlight = task
        let result = await task.value
        inFlight = nil
        return result
    }
}
