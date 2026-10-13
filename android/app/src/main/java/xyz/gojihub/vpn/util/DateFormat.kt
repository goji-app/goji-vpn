package xyz.gojihub.vpn.util

import xyz.gojihub.vpn.i18n.AppLanguage
import xyz.gojihub.vpn.i18n.Loc
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

/** "2026-09-05T16:11:51Z" → "5 сентября"/"September 5"/"9月5日" в зависимости от текущего языка
 *  интерфейса (Loc.lang). Возвращает исходную строку, если распарсить не удалось. */
fun formatDate(isoDateTime: String): String = runCatching {
    val zdt = Instant.parse(isoDateTime).atZone(ZoneId.systemDefault())
    val (locale, pattern) = when (Loc.lang) {
        AppLanguage.RU -> Locale("ru") to "d MMMM"
        AppLanguage.EN -> Locale.ENGLISH to "MMMM d"
        AppLanguage.ZH -> Locale.CHINESE to "M月d日"
    }
    zdt.format(DateTimeFormatter.ofPattern(pattern, locale))
}.getOrDefault(isoDateTime)

/** То же самое, но с временем — "5 сентября в 14:30" (та же дата+время, что показывает
 *  веб-версия под каждой новостью/рассылкой). */
fun formatDateTime(isoDateTime: String): String = runCatching {
    val zdt = Instant.parse(isoDateTime).atZone(ZoneId.systemDefault())
    val (locale, datePattern) = when (Loc.lang) {
        AppLanguage.RU -> Locale("ru") to "d MMMM"
        AppLanguage.EN -> Locale.ENGLISH to "MMMM d"
        AppLanguage.ZH -> Locale.CHINESE to "M月d日"
    }
    val date = zdt.format(DateTimeFormatter.ofPattern(datePattern, locale))
    val time = zdt.format(DateTimeFormatter.ofPattern("HH:mm"))
    when (Loc.lang) {
        AppLanguage.RU -> "$date в $time"
        AppLanguage.EN -> "$date at $time"
        AppLanguage.ZH -> "$date $time"
    }
}.getOrDefault(isoDateTime)
