import Foundation
import Security

/// Минимальная обёртка над Keychain (kSecClassGenericPassword).
/// Доступ — «после первой разблокировки, только это устройство»: токены и HWID не попадают в
/// резервные копии iCloud и не переезжают на другой iPhone при восстановлении.
enum Keychain {
    private static let service = "xyz.gojihub.vpn"

    static func get(_ key: String) -> String? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne
        ]
        var item: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &item) == errSecSuccess,
              let data = item as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }

    @discardableResult
    static func set(_ value: String?, for key: String) -> Bool {
        let base: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key
        ]
        guard let value else {
            let status = SecItemDelete(base as CFDictionary)
            return status == errSecSuccess || status == errSecItemNotFound
        }
        let data = Data(value.utf8)
        let update: [String: Any] = [
            kSecValueData as String: data,
            kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        ]
        let status = SecItemUpdate(base as CFDictionary, update as CFDictionary)
        if status == errSecItemNotFound {
            var add = base
            add.merge(update) { $1 }
            return SecItemAdd(add as CFDictionary, nil) == errSecSuccess
        }
        return status == errSecSuccess
    }
}

/// Сессия кабинета: JWT (rw_session_token, живёт 24 ч) и refresh-токен (rw_refresh_token).
/// Единственный источник правды о протухшей сессии — 401 от бэкенда (как и на Android).
final class TokenStore {
    static let shared = TokenStore()
    private let accessKey = "session.access"
    private let refreshKey = "session.refresh"

    var accessToken: String? {
        get { Keychain.get(accessKey) }
        set { Keychain.set(newValue, for: accessKey) }
    }

    var refreshToken: String? {
        get { Keychain.get(refreshKey) }
        set { Keychain.set(newValue, for: refreshKey) }
    }

    var isLoggedIn: Bool { accessToken != nil }

    func clear() {
        accessToken = nil
        refreshToken = nil
    }
}

/// Идентификатор устройства для X-HWID (лимит устройств подписки). Случайный UUID, а не
/// identifierForVendor: хранится в Keychain и переживает переустановку приложения — иначе
/// каждая переустановка занимала бы новое место в лимите устройств (у пробного тарифа он = 1).
/// Не используется для рекламы и не передаётся никому, кроме нашего сервера подписки.
enum DeviceID {
    private static let key = "device.hwid"

    static var hwid: String {
        if let existing = Keychain.get(key) { return existing }
        let fresh = UUID().uuidString.lowercased()
        Keychain.set(fresh, for: key)
        return fresh
    }
}
