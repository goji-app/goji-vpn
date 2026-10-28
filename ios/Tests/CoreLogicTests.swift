import XCTest
@testable import GojiVPN

final class XrayConfigBuilderTests: XCTestCase {
    private let profile = """
    {
      "remarks": "🇩🇪 Германия",
      "dns": {"servers": ["1.1.1.1", {"address": "8.8.8.8", "domains": ["geosite:google"]}]},
      "outbounds": [
        {"tag": "proxy", "protocol": "vless",
         "settings": {"vnext": [{"address": "serv1.example.com", "port": 443, "users": [{"id": "uuid-1"}]}]},
         "streamSettings": {"network": "tcp", "security": "tls"}},
        {"tag": "direct", "protocol": "freedom"},
        {"tag": "block", "protocol": "blackhole"}
      ],
      "routing": {"rules": [
        {"type": "field", "ip": ["geoip:private"], "outboundTag": "direct"},
        {"type": "field", "domain": ["ext:custom.dat:ads"], "outboundTag": "block"},
        {"type": "field", "network": "tcp,udp", "outboundTag": "proxy"}
      ]}
    }
    """

    private func build(_ options: XrayConfigBuilder.Options) throws -> [String: Any] {
        let json = try XrayConfigBuilder.build(payload: profile, options: options)
        return try XCTUnwrap(JSONSerialization.jsonObject(with: Data(json.utf8)) as? [String: Any])
    }

    func testTunInboundAndEnv() throws {
        let root = try build(.init(tunFd: 7, socksPort: 10808, metricsPort: 10809))
        let inbounds = try XCTUnwrap(root["inbounds"] as? [[String: Any]])
        XCTAssertEqual(inbounds.first?["protocol"] as? String, "tun")
        XCTAssertEqual(inbounds.last?["port"] as? Int, 10808)
        let env = try XCTUnwrap(root["env"] as? [String: String])
        XCTAssertEqual(env["xray.tun.fd"], "7")
        XCTAssertEqual((root["metrics"] as? [String: Any])?["listen"] as? String, "127.0.0.1:10809")
    }

    func testUnresolvedGeoAndExtRulesAreDropped() throws {
        // Геоданных нет (пустые каталоги поиска) — geoip:private и ext: должны исчезнуть,
        // а правило без матчеров — выпасть целиком, но catch-all остаться последним.
        let root = try build(.init(geoSearchDirs: []))
        let rules = try XCTUnwrap((root["routing"] as? [String: Any])?["rules"] as? [[String: Any]])
        XCTAssertEqual(rules.last?["outboundTag"] as? String, "proxy")
        XCTAssertFalse(rules.contains { ($0["outboundTag"] as? String) == "block" })
        let dnsServers = try XCTUnwrap((root["dns"] as? [String: Any])?["servers"] as? [Any])
        let second = try XCTUnwrap(dnsServers.last as? [String: Any])
        XCTAssertNil(second["domains"], "пустой список доменов DNS-сервера удаляется целиком")
    }

    func testBypassDomainsRuleIsFirst() throws {
        let root = try build(.init(bypassDomains: ["sberbank.ru"]))
        let rules = try XCTUnwrap((root["routing"] as? [String: Any])?["rules"] as? [[String: Any]])
        XCTAssertEqual(rules.first?["outboundTag"] as? String, "direct")
        XCTAssertEqual(rules.first?["domain"] as? [String], ["domain:sberbank.ru"])
    }

    func testKeepAliveDefaults() throws {
        let root = try build(.init())
        let outbounds = try XCTUnwrap(root["outbounds"] as? [[String: Any]])
        let sockopt = (outbounds[0]["streamSettings"] as? [String: Any])?["sockopt"] as? [String: Any]
        XCTAssertEqual(sockopt?["tcpKeepAliveIdle"] as? Int, 30)
        XCTAssertEqual(sockopt?["tcpKeepAliveInterval"] as? Int, 30)
    }

    func testInjectDirectRulesBeforeCatchAll() {
        let rules: [[String: Any]] = [["outboundTag": "a"], ["outboundTag": "catch-all"]]
        let root: [String: Any] = ["outbounds": [["tag": "direct"]]]
        let merged = XrayConfigBuilder.injectOwnDirectRules(rules, root: root, categoryRu: ["domain:ya.ru"], injectDomainRule: true, extraCidrs: ["5.0.0.0/8"])
        XCTAssertEqual(merged.count, 4)
        XCTAssertEqual(merged.last?["outboundTag"] as? String, "catch-all")
        XCTAssertEqual(merged[1]["domain"] as? [String], ["domain:ya.ru"])
        XCTAssertEqual(merged[2]["ip"] as? [String], ["5.0.0.0/8"])
    }

    func testEndpointAndTags() throws {
        let obj = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(profile.utf8)) as? [String: Any])
        let endpoint = try XCTUnwrap(XrayConfigBuilder.endpoint(of: obj))
        XCTAssertEqual(endpoint.host, "serv1.example.com")
        XCTAssertEqual(endpoint.port, 443)
        XCTAssertEqual(endpoint.uuid, "uuid-1")
        XCTAssertEqual(XrayConfigBuilder.proxyOutboundTags(of: profile), ["proxy"])
    }
}

final class GeoDataParserTests: XCTestCase {
    /// Минимальный geosite.dat: GeoSiteList{ entry(1): GeoSite{ country_code(1), domain(2): Domain{type(1), value(2)} } }.
    private func varint(_ v: Int) -> [UInt8] {
        var v = v, out: [UInt8] = []
        repeat {
            var byte = UInt8(v & 0x7F)
            v >>= 7
            if v != 0 { byte |= 0x80 }
            out.append(byte)
        } while v != 0
        return out
    }

    private func field(_ number: Int, _ payload: [UInt8]) -> [UInt8] {
        [UInt8(number << 3 | 2)] + varint(payload.count) + payload
    }

    private func domain(type: Int, value: String) -> [UInt8] {
        [UInt8(1 << 3 | 0)] + varint(type) + field(2, Array(value.utf8))
    }

    func testResolvesOnlyRequestedCategories() throws {
        let ru = field(1, Array("CATEGORY-RU".utf8)) + field(2, domain(type: 2, value: "ya.ru")) + field(2, domain(type: 3, value: "vk.com")) + field(2, domain(type: 1, value: "^sber"))
        let other = field(1, Array("GOOGLE".utf8)) + field(2, domain(type: 2, value: "google.com"))
        let file = field(1, ru) + field(1, other)
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("geosite-test-\(UUID().uuidString).dat")
        try Data(file).write(to: url)
        defer { try? FileManager.default.removeItem(at: url) }

        let result = GeoDataParser.resolveDomains(file: url, tags: ["category-ru"])
        XCTAssertEqual(result["CATEGORY-RU"], ["domain:ya.ru", "domain:vk.com", "regexp:^sber"])
        XCTAssertNil(result["GOOGLE"])
    }

    func testTruncatedFileDoesNotCrash() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("geosite-broken-\(UUID().uuidString).dat")
        try Data([0x0A, 0xFF, 0xFF, 0x01]).write(to: url)
        defer { try? FileManager.default.removeItem(at: url) }
        XCTAssertTrue(GeoDataParser.resolveDomains(file: url, tags: ["X"]).isEmpty)
    }

    func testFormatIP() {
        XCTAssertEqual(GeoDataParser.formatIP([10, 0, 0, 1]), "10.0.0.1")
        XCTAssertEqual(GeoDataParser.formatIP([0x20, 0x01, 0x0d, 0xb8] + Array(repeating: 0, count: 11) + [1]), "2001:db8::1")
        XCTAssertNil(GeoDataParser.formatIP([1, 2, 3]))
    }
}

@MainActor
final class SubscriptionParsingTests: XCTestCase {
    func testParsesJSONProfilesWithStableIds() throws {
        let raw = """
        [{"remarks": "🇳🇱 Нидерланды", "outbounds": [{"tag": "proxy", "protocol": "vless", "settings": {"vnext": [{"address": "serv3.example.com", "port": 443, "users": [{"id": "u"}]}]}}]},
         {"remarks": "Trojan", "outbounds": [{"protocol": "trojan", "settings": {"servers": [{"address": "t.example.com", "port": 8443}]}}]},
         {"remarks": "Без прокси", "outbounds": [{"protocol": "freedom"}]}]
        """
        let nodes = try XCTUnwrap(SubscriptionStore.parseJSONProfiles(raw))
        XCTAssertEqual(nodes.count, 2)
        XCTAssertEqual(nodes[0].id, "🇳🇱 Нидерланды|serv3.example.com:443")
        XCTAssertEqual(nodes[0].geo?.code, "NL")
        XCTAssertEqual(nodes[0].displayName, "Нидерланды")
        XCTAssertEqual(nodes[1].port, 8443)
    }

    func testLegacyBase64Links() {
        let links = "vless://uuid@a.example.com:443?security=tls#Germany\nvless://uuid@b.example.com:8443#Finland"
        let encoded = Data(links.utf8).base64EncodedString()
        let nodes = SubscriptionStore.parseLegacy(encoded)
        XCTAssertEqual(nodes.map(\.host), ["a.example.com", "b.example.com"])
        XCTAssertEqual(nodes[1].geo?.code, "FI")
    }
}

@MainActor
final class UtilityTests: XCTestCase {
    func testNormalizeDomain() {
        XCTAssertEqual(AppSettings.normalizeDomain("https://www.Example.com:443/path?q=1"), "example.com")
        XCTAssertEqual(AppSettings.normalizeDomain("*.sub.domain.ru"), "sub.domain.ru")
        XCTAssertNil(AppSettings.normalizeDomain("not a domain"))
        XCTAssertNil(AppSettings.normalizeDomain(""))
    }

    func testFlagAndStrip() {
        XCTAssertEqual(CountryGeoLookup.flag("DE"), "🇩🇪")
        XCTAssertEqual(CountryGeoLookup.flag("X"), "🌐")
        XCTAssertEqual(CountryGeoLookup.stripLeadingFlag("🇩🇪 Германия"), "Германия")
        XCTAssertEqual(CountryGeoLookup.find("Автовыбор серверов LTE")?.code, "RU")
    }

    func testRichText() {
        XCTAssertEqual(RichText.plain("<b>Привет</b>&nbsp;мир<br>!"), "Привет мир\n!")
        XCTAssertEqual(RichText.toMarkdown("<a href=\"https://x.y\">ссылка</a>"), "[ссылка](https://x.y)")
    }

    func testPurchaseLinksAreFiltered() {
        XCTAssertTrue(LinkPolicy.isPurchaseLink("https://\(AppConstants.siteHost)/#/plans"))
        XCTAssertTrue(LinkPolicy.isPurchaseLink("https://t.me/Shadow_Duck_bot?start=pay"))
        XCTAssertFalse(LinkPolicy.isPurchaseLink("https://status.example.com/incident"))
    }

    func testTransferURIDetection() {
        XCTAssertTrue(AuthService.isTransferURI("godjivpn://transfer?s=abc&t=1"))
        XCTAssertFalse(AuthService.isTransferURI("https://\(AppConstants.siteHost)"))
        // Код переноса — только почта; код старой версии с сессией внутри не принимается.
        XCTAssertEqual(try AuthService.transferEmail("godjivpn://transfer?e=a%2Bb@mail.ru"), "a+b@mail.ru")
        XCTAssertThrowsError(try AuthService.transferEmail("godjivpn://transfer?s=abc&t=1"))
        XCTAssertThrowsError(try AuthService.transferEmail("godjivpn://transfer?e=not-an-email"))
    }
}
