package xyz.gojihub.vpn.util

/** Remark из подписки часто начинается с флага-эмодзи (иногда ещё и с ⚡ следом, вроде
 *  "🇩🇪 Germany" или "🇸🇴⚡ Автовыбор серверов EU") — раз флаг теперь показываем отдельным
 *  бейджем (см. CountryGeoLookup.flagEmoji), из заголовка его убираем, чтобы не дублировать. */
fun stripLeadingFlag(text: String): String {
    val codePoints = text.codePoints().toArray()
    var idx = 0
    while (idx < codePoints.size && Character.isWhitespace(codePoints[idx])) idx++
    fun isRegionalIndicator(cp: Int) = cp in 0x1F1E6..0x1F1FF
    if (idx + 1 >= codePoints.size || !isRegionalIndicator(codePoints[idx]) || !isRegionalIndicator(codePoints[idx + 1])) {
        return text
    }
    idx += 2
    while (idx < codePoints.size && (codePoints[idx] == '⚡'.code || codePoints[idx] == 0xFE0F || Character.isWhitespace(codePoints[idx]))) idx++
    return String(codePoints, idx, codePoints.size - idx)
}
