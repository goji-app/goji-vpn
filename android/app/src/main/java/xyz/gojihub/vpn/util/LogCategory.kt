package xyz.gojihub.vpn.util

/** Каждая категория пишется в свой файл (см. AppLogger) — так в журнале ядра (Xray/туннель)
 *  не тонут события подписки/пушей и наоборот. */
enum class LogCategory(val fileBaseName: String) {
    MAIN("main"),
    CORE("core"),
    SUBSCRIPTION("subscription"),
    SERVICE("service"),
    PUSH("push");

    companion object {
        fun fromName(name: String?): LogCategory = entries.firstOrNull { it.name == name } ?: MAIN
    }
}
