import Foundation

/// Локализованная строка из Localizable.strings (ru — основной язык, en — второй).
/// Язык выбирает система (Настройки iOS → Goji VPN → Язык), отдельного переключателя в
/// приложении нет — так рекомендует Apple для приложений с несколькими локализациями.
func L(_ key: String) -> String {
    NSLocalizedString(key, comment: "")
}

/// Строка с подстановкой аргументов (%@, %d, %lld в тексте перевода).
func L(_ key: String, _ args: CVarArg...) -> String {
    String(format: NSLocalizedString(key, comment: ""), locale: Locale.current, arguments: args)
}

enum AppLanguage {
    /// Язык, который iOS реально выбрала для интерфейса приложения.
    static var code: String { Bundle.main.preferredLocalizations.first ?? "ru" }
    static var isRussian: Bool { code.hasPrefix("ru") }
}

enum Plural {
    /// Русские формы «1 день / 2 дня / 5 дней»; для английского — one/other.
    /// Ключи в Localizable.strings: "<base>.one", "<base>.few", "<base>.many".
    static func form(_ n: Int, _ base: String) -> String {
        let isRussian = AppLanguage.isRussian
        let suffix: String
        if isRussian {
            let mod10 = n % 10, mod100 = n % 100
            if mod10 == 1 && mod100 != 11 { suffix = "one" }
            else if (2...4).contains(mod10) && !(12...14).contains(mod100) { suffix = "few" }
            else { suffix = "many" }
        } else {
            suffix = n == 1 ? "one" : "many"
        }
        return L("\(base).\(suffix)", n)
    }
}
