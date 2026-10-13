package xyz.gojihub.vpn.i18n

enum class AppLanguage(val code: String, val displayName: String) {
    RU("ru", "Русский"),
    EN("en", "English"),
    ZH("zh", "中文");

    companion object {
        fun fromCode(code: String?): AppLanguage = entries.firstOrNull { it.code == code } ?: RU
    }
}
