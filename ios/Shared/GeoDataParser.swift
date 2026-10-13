import Foundation

/// Ручной protobuf-декодер geoip.dat/geosite.dat (GeoIPList/GeoSiteList из Xray-core
/// app/router/config.proto) — порт GeoDataParser.kt из Android-клиента.
///
/// Зачем не отдать .dat самому Xray: в расширении туннеля жёсткий лимит памяти (~50 МБ),
/// а полный geosite.dat/geoip.dat — десятки МБ с категориями на сотни тысяч записей. Здесь
/// файл отображается в память (mmap, не считается «грязной» памятью процесса) и
/// материализуются ТОЛЬКО запрошенные категории — их и подставляем в конфиг готовыми
/// доменами/подсетями вместо ссылок geosite:/geoip:.
enum GeoDataParser {
    private struct CacheKey: Hashable {
        let path: String
        let modified: Date
        let size: Int
        let tags: Set<String>
    }

    private static let lock = NSLock()

    static func resolveDomains(file: URL, tags: Set<String>) -> [String: [String]] {
        resolve(file: file, tags: tags, cache: \Holder.domainCache, decode: decodeDomains)
    }

    static func resolveCidrs(file: URL, tags: Set<String>) -> [String: [String]] {
        resolve(file: file, tags: tags, cache: \Holder.cidrCache, decode: decodeCidrs)
    }

    private static func resolve(
        file: URL,
        tags: Set<String>,
        cache: ReferenceWritableKeyPath<Holder, (CacheKey, [String: [String]])?>,
        decode: (UnsafeRawBufferPointer, Int, Int) -> [String]
    ) -> [String: [String]] {
        guard !tags.isEmpty,
              let attrs = try? FileManager.default.attributesOfItem(atPath: file.path),
              let data = try? Data(contentsOf: file, options: .alwaysMapped) else { return [:] }
        let wanted = Set(tags.map { $0.uppercased() })
        let key = CacheKey(
            path: file.path,
            modified: attrs[.modificationDate] as? Date ?? .distantPast,
            size: (attrs[.size] as? NSNumber)?.intValue ?? data.count,
            tags: wanted
        )
        lock.lock()
        if let cached = Holder.shared[keyPath: cache], cached.0 == key {
            lock.unlock()
            return cached.1
        }
        lock.unlock()

        var result: [String: [String]] = [:]
        data.withUnsafeBytes { (buf: UnsafeRawBufferPointer) in
            var reader = ProtoReader(buf: buf, start: 0, end: buf.count)
            while reader.hasMore {
                guard let (field, wire) = reader.readTag() else { break }
                if field == 1 && wire == 2 {
                    guard let (s, e) = reader.readRange() else { break }
                    guard let code = peekCountryCode(buf, s, e)?.uppercased(), wanted.contains(code) else { continue }
                    result[code, default: []].append(contentsOf: decode(buf, s, e))
                } else if !reader.skip(wire) {
                    break
                }
            }
        }
        lock.lock()
        Holder.shared[keyPath: cache] = (key, result)
        lock.unlock()
        return result
    }

    /// Хранилище кэша (последний результат на тип файла — не растёт при смене узлов).
    private final class Holder {
        static let shared = Holder()
        var domainCache: (CacheKey, [String: [String]])?
        var cidrCache: (CacheKey, [String: [String]])?
    }

    private static func string(_ buf: UnsafeRawBufferPointer, _ s: Int, _ e: Int) -> String? {
        guard s <= e, e <= buf.count else { return nil }
        return String(decoding: UnsafeRawBufferPointer(rebasing: buf[s..<e]), as: UTF8.self)
    }

    /// Только строковое поле №1 (country_code) подсообщения — без декодирования (огромных)
    /// списков непрошенных категорий.
    private static func peekCountryCode(_ buf: UnsafeRawBufferPointer, _ start: Int, _ end: Int) -> String? {
        var reader = ProtoReader(buf: buf, start: start, end: end)
        while reader.hasMore {
            guard let (field, wire) = reader.readTag() else { return nil }
            if field == 1 && wire == 2 {
                guard let (s, e) = reader.readRange() else { return nil }
                return string(buf, s, e)
            } else if !reader.skip(wire) {
                return nil
            }
        }
        return nil
    }

    private static func decodeDomains(_ buf: UnsafeRawBufferPointer, _ start: Int, _ end: Int) -> [String] {
        var out: [String] = []
        var reader = ProtoReader(buf: buf, start: start, end: end)
        while reader.hasMore {
            guard let (field, wire) = reader.readTag() else { break }
            if field == 2 && wire == 2 {
                guard let (s, e) = reader.readRange() else { break }
                if let domain = decodeDomain(buf, s, e) { out.append(domain) }
            } else if !reader.skip(wire) {
                break
            }
        }
        return out
    }

    /// Domain{ type(1), value(2), attribute(3) }. Типы 2 (корень+поддомены) и 3 (точное
    /// совпадение) намеренно оба превращаются в "domain:" — сервисы ходят через поддомены
    /// API/CDN, точное совпадение их не ловит (подтверждено живыми логами на Android).
    private static func decodeDomain(_ buf: UnsafeRawBufferPointer, _ start: Int, _ end: Int) -> String? {
        var reader = ProtoReader(buf: buf, start: start, end: end)
        var type: UInt64 = 0
        var value: String?
        while reader.hasMore {
            guard let (field, wire) = reader.readTag() else { break }
            if field == 1 && wire == 0 {
                type = reader.readVarint() ?? 0
            } else if field == 2 && wire == 2 {
                guard let (s, e) = reader.readRange() else { break }
                value = string(buf, s, e)
            } else if !reader.skip(wire) {
                break
            }
        }
        guard let value else { return nil }
        switch type {
        case 1: return "regexp:\(value)"
        case 2, 3: return "domain:\(value)"
        default: return value
        }
    }

    private static func decodeCidrs(_ buf: UnsafeRawBufferPointer, _ start: Int, _ end: Int) -> [String] {
        var out: [String] = []
        var reader = ProtoReader(buf: buf, start: start, end: end)
        while reader.hasMore {
            guard let (field, wire) = reader.readTag() else { break }
            if field == 2 && wire == 2 {
                guard let (s, e) = reader.readRange() else { break }
                if let cidr = decodeCidr(buf, s, e) { out.append(cidr) }
            } else if !reader.skip(wire) {
                break
            }
        }
        return out
    }

    /// CIDR{ ip: bytes(1) — 4 или 16 байт, prefix: uint32(2) }.
    private static func decodeCidr(_ buf: UnsafeRawBufferPointer, _ start: Int, _ end: Int) -> String? {
        var reader = ProtoReader(buf: buf, start: start, end: end)
        var ip: [UInt8]?
        var prefix: UInt64 = 0
        while reader.hasMore {
            guard let (field, wire) = reader.readTag() else { break }
            if field == 1 && wire == 2 {
                guard let (s, e) = reader.readRange() else { break }
                ip = Array(buf[s..<e])
            } else if field == 2 && wire == 0 {
                prefix = reader.readVarint() ?? 0
            } else if !reader.skip(wire) {
                break
            }
        }
        guard let ip, let text = formatIP(ip) else { return nil }
        return "\(text)/\(prefix)"
    }

    static func formatIP(_ bytes: [UInt8]) -> String? {
        if bytes.count == 4 {
            return bytes.map(String.init).joined(separator: ".")
        }
        guard bytes.count == 16 else { return nil }
        var addr = in6_addr()
        withUnsafeMutableBytes(of: &addr) { raw in
            for i in 0..<16 { raw[i] = bytes[i] }
        }
        var out = [CChar](repeating: 0, count: Int(INET6_ADDRSTRLEN))
        guard inet_ntop(AF_INET6, &addr, &out, socklen_t(INET6_ADDRSTRLEN)) != nil else { return nil }
        return String(cString: out)
    }

    /// Курсор по срезу общего буфера, без копирования подсообщений. Все чтения проверяют
    /// границы — повреждённый/обрезанный файл не должен ронять процесс туннеля.
    private struct ProtoReader {
        let buf: UnsafeRawBufferPointer
        var pos: Int
        let end: Int

        init(buf: UnsafeRawBufferPointer, start: Int, end: Int) {
            self.buf = buf
            self.pos = start
            self.end = min(end, buf.count)
        }

        var hasMore: Bool { pos < end }

        mutating func readVarint() -> UInt64? {
            var result: UInt64 = 0
            var shift: UInt64 = 0
            while pos < end {
                let byte = UInt64(buf[pos])
                pos += 1
                result |= (byte & 0x7F) << shift
                if byte & 0x80 == 0 { return result }
                shift += 7
                if shift > 63 { return nil }
            }
            return nil
        }

        mutating func readTag() -> (Int, Int)? {
            guard let tag = readVarint() else { return nil }
            return (Int(tag >> 3), Int(tag & 0x7))
        }

        mutating func readRange() -> (Int, Int)? {
            guard let len = readVarint(), len <= UInt64(end - pos) else { return nil }
            let s = pos
            pos += Int(len)
            return (s, pos)
        }

        mutating func skip(_ wire: Int) -> Bool {
            switch wire {
            case 0: return readVarint() != nil
            case 2: return readRange() != nil
            case 1: pos += 8; return pos <= end
            case 5: pos += 4; return pos <= end
            default: return false
            }
        }
    }
}
