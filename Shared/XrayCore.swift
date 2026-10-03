import Foundation
import LibXray

/// Обёртка над единственной точкой входа libXray: `CGoInvoke(requestJSON) -> responseJSON`
/// (API v3, см. https://github.com/XTLS/libXray#api). Ответ всегда освобождается через CGoFree.
enum XrayCore {
    static let apiVersion = 3

    struct Response {
        let success: Bool
        let data: Any?
        let error: String?

        var dataObject: [String: Any]? { data as? [String: Any] }
    }

    /// libXray держит глобальное состояние Go — параллельные вызовы из разных потоков
    /// сериализуем (на Android параллельный pingBatch ломал сам себя: «read/write on closed pipe»).
    private static let lock = NSLock()

    static func invoke(_ method: String, payload: [String: Any] = [:]) -> Response {
        let request: [String: Any] = ["apiVersion": apiVersion, "method": method, "payload": payload]
        guard let requestData = try? JSONSerialization.data(withJSONObject: request),
              let requestString = String(data: requestData, encoding: .utf8),
              let cRequest = strdup(requestString) else {
            return Response(success: false, data: nil, error: "не удалось сериализовать запрос \(method)")
        }
        defer { free(cRequest) }

        lock.lock()
        let cResponse = CGoInvoke(cRequest)
        lock.unlock()

        guard let cResponse else {
            return Response(success: false, data: nil, error: "пустой ответ ядра на \(method)")
        }
        defer { CGoFree(cResponse) }
        let responseString = String(cString: cResponse)
        guard let data = responseString.data(using: .utf8),
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            return Response(success: false, data: nil, error: "неразборчивый ответ ядра на \(method)")
        }
        let success = json["success"] as? Bool ?? false
        let error = (json["error"] as? String).flatMap { $0.isEmpty ? nil : $0 }
        return Response(success: success, data: json["data"], error: error)
    }

    static func version() -> String? {
        let response = invoke("xrayVersion")
        return response.dataObject?["version"] as? String
    }

    static func run(configJSON: String) -> Response {
        invoke("runXray", payload: ["xrayJson": configJSON])
    }

    @discardableResult
    static func stop() -> Response {
        invoke("stopXray")
    }

    static func isRunning() -> Bool {
        invoke("getXrayState").dataObject?["running"] as? Bool ?? false
    }

    /// Свободные TCP-порты на localhost (кандидаты — до момента bind их может занять кто-то ещё).
    static func freePorts(_ count: Int) -> [Int] {
        let ports = invoke("getFreePorts", payload: ["count": count]).dataObject?["ports"] as? [Any] ?? []
        return ports.compactMap { ($0 as? NSNumber)?.intValue }
    }

    /// Старый формат подписки (base64 со списком vless://) → `{"outbounds":[...]}`.
    static func convertShareLinks(_ text: String) -> [String: Any]? {
        let response = invoke("convertShareLinksToXrayJson", payload: ["text": text])
        guard response.success else { return nil }
        return response.dataObject
    }

    struct PingTarget {
        let xrayJSON: String
        let outboundTag: String?
    }

    struct PingResult {
        let success: Bool
        let delay: Int
        let error: String?
    }

    /// Замер задержки через временный экземпляр Xray (реальный HTTP-запрос сквозь прокси).
    /// Не более 5 конфигураций за вызов (ограничение libXray). Работает только в процессе,
    /// где ядро не запущено, — в приложении (ядро туннеля живёт в другом процессе).
    static func pingBatch(_ targets: [PingTarget], url: String, timeoutSeconds: Int, headMethod: Bool) -> [PingResult] {
        let configs: [[String: Any]] = targets.map { target in
            var item: [String: Any] = ["xrayJson": target.xrayJSON]
            if let tag = target.outboundTag { item["outboundTag"] = tag }
            return item
        }
        var payload: [String: Any] = ["configs": configs, "timeout": timeoutSeconds, "url": url]
        if headMethod { payload["method"] = "HEAD" }
        let response = invoke("pingBatch", payload: payload)
        guard response.success, let results = response.dataObject?["results"] as? [[String: Any]] else {
            if let error = response.error { AppLog.w(.subscription, "pingBatch: \(error)") }
            return targets.map { _ in PingResult(success: false, delay: -1, error: response.error) }
        }
        return results.map { item in
            let ok = item["success"] as? Bool ?? false
            let delay = (item["delay"] as? NSNumber)?.intValue ?? -1
            return PingResult(success: ok, delay: ok ? delay : -1, error: item["error"] as? String)
        }
    }
}
