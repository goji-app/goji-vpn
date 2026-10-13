package xyz.gojihub.vpn.util

/** Порог детализации журнала — [rank] растёт с подробностью. Сообщение уровня X пишется на
 *  диск, только если текущий порог >= X (например при WARNING пишутся ERROR и WARNING, но не
 *  INFO/DEBUG; при NONE не пишется ничего). */
enum class LogLevel(val rank: Int) {
    NONE(0), ERROR(1), WARNING(2), INFO(3), DEBUG(4);

    companion object {
        /** Для тех, кто уровень не выбирал: Info — подробный Debug пишет на диск много лишнего
         *  (дампы ответов и конфигов) и включается вручную для диагностики. */
        val DEFAULT = INFO

        fun fromName(name: String?): LogLevel = entries.firstOrNull { it.name == name } ?: DEFAULT
    }
}
