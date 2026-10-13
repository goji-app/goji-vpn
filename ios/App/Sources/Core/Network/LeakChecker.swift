import Foundation

/// Проверка «что видит внешний мир» (порт NetworkDiagnostics.kt).
///  - IP через VPN: обычный запрос приложения (оно само идёт через туннель);
///  - настоящий IP: запрашивает расширение туннеля — его собственные соединения идут мимо VPN;
///  - DNS: edns.ip-api.com сообщает, какой резолвер разрешил уникальный поддомен. Резолвер
///    провайдера (тот же ISP, что у настоящего IP) — утечка DNS.
enum LeakChecker {
    struct IPInfo: Equatable {
        let ip: String
        let country: String?
        let isp: String?
    }

    enum Verdict { case safe, leak, vpnOff, error }

    struct Report {
        let verdict: Verdict
        let realIP: IPInfo?
        let vpnIP: IPInfo?
        let dns: IPInfo?
        let ipLeak: Bool
        let dnsLeak: Bool
    }

    private static func session(timeout: TimeInterval) -> URLSession {
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = timeout
        config.timeoutIntervalForResource = timeout
        config.httpCookieStorage = nil
        return URLSession(configuration: config)
    }

    static func fetchIP(timeout: TimeInterval = 8) async -> IPInfo? {
        if let info = await fetchIPWhoIs(timeout: timeout) { return info }
        return await fetchIPApi(timeout: timeout)
    }

    private static func fetchIPWhoIs(timeout: TimeInterval) async -> IPInfo? {
        guard let result = try? await session(timeout: timeout).data(from: URL(string: "https://ipwho.is/")!),
              let json = try? JSONSerialization.jsonObject(with: result.0) as? [String: Any],
              (json["success"] as? Bool) ?? true,
              let ip = json["ip"] as? String, !ip.isEmpty else { return nil }
        return IPInfo(ip: ip, country: json["country"] as? String, isp: (json["connection"] as? [String: Any])?["isp"] as? String)
    }

    private static func fetchIPApi(timeout: TimeInterval) async -> IPInfo? {
        guard let result = try? await session(timeout: timeout).data(from: URL(string: "https://ipapi.co/json/")!),
              let json = try? JSONSerialization.jsonObject(with: result.0) as? [String: Any],
              let ip = json["ip"] as? String, !ip.isEmpty else { return nil }
        return IPInfo(ip: ip, country: json["country_name"] as? String, isp: json["org"] as? String)
    }

    private static func fetchDNS() async -> IPInfo? {
        let token = UUID().uuidString.replacingOccurrences(of: "-", with: "").prefix(24).lowercased()
        guard let url = URL(string: "https://\(token).edns.ip-api.com/json"),
              let result = try? await session(timeout: 10).data(from: url),
              let json = try? JSONSerialization.jsonObject(with: result.0) as? [String: Any],
              let dns = json["dns"] as? [String: Any],
              let ip = dns["ip"] as? String else { return nil }
        // geo приходит строкой «Страна - Провайдер».
        let geo = (dns["geo"] as? String) ?? ""
        let parts = geo.components(separatedBy: " - ")
        return IPInfo(ip: ip, country: parts.first.flatMap { $0.isEmpty ? nil : $0 }, isp: parts.count > 1 ? parts[1] : nil)
    }

    @MainActor
    static func check() async -> Report {
        let vpn = VPNController.shared
        guard vpn.isConnected else {
            let real = await fetchIP()
            return Report(verdict: .vpnOff, realIP: real, vpnIP: nil, dns: nil, ipLeak: false, dnsLeak: false)
        }
        async let vpnIPTask = fetchIP()
        async let dnsTask = fetchDNS()
        let directResponse = await vpn.send(["type": TunnelMessage.directIP.rawValue])
        let realIP: IPInfo? = (directResponse?["ip"] as? String).map {
            IPInfo(ip: $0, country: directResponse?["country"] as? String, isp: directResponse?["isp"] as? String)
        }
        let vpnIP = await vpnIPTask
        let dns = await dnsTask
        guard let vpnIP else {
            return Report(verdict: .error, realIP: realIP, vpnIP: nil, dns: dns, ipLeak: false, dnsLeak: false)
        }
        let ipLeak = realIP != nil && realIP?.ip == vpnIP.ip
        var dnsLeak = false
        if let dns, let realIP {
            let sameIsp = normalize(dns.isp) != nil && normalize(dns.isp) == normalize(realIP.isp)
            let sameCountryAsReal = dns.country != nil && dns.country == realIP.country && realIP.country != vpnIP.country
            dnsLeak = sameIsp || sameCountryAsReal
        }
        return Report(
            verdict: (ipLeak || dnsLeak) ? .leak : .safe,
            realIP: realIP, vpnIP: vpnIP, dns: dns, ipLeak: ipLeak, dnsLeak: dnsLeak
        )
    }

    private static func normalize(_ isp: String?) -> String? {
        guard var s = isp?.lowercased(), !s.isEmpty else { return nil }
        for word in ["llc", "ltd", "ooo", "jsc", "pjsc", "inc", "oao", "zao", "gmbh", "limited", "company"] {
            s = s.replacingOccurrences(of: "\\b\(word)\\b", with: "", options: .regularExpression)
        }
        s = s.filter { $0.isLetter || $0.isNumber }
        return s.isEmpty ? nil : s
    }
}
