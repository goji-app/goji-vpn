package xyz.gojihub.vpn.util

import android.content.Context

/**
 * Длина оплаченного периода подписки — для кольца дней, которое убывает каждый день
 * (остаток / длина периода). Даты начала периода бэкенд не отдаёт ни приложению, ни сайту,
 * поэтому запоминаем её сами по каждой подписке:
 *  • остаток вырос (продление, покупка) — новый период начинается сейчас, его длина равна
 *    текущему остатку, кольцо снова полное;
 *  • подписку видим впервые — берём ближайший стандартный срок, не меньше остатка
 *    (121 день → 180), чтобы кольцо не было полным у того, кто купил давно.
 */
object SubscriptionPeriod {
    private const val PREFS = "subscription_period"
    private val STANDARD_DAYS = intArrayOf(1, 3, 7, 14, 30, 60, 90, 180, 365, 730)

    /** Длина периода в днях (не меньше [daysLeft] и не меньше 1). */
    fun totalDays(context: Context, subscriptionId: Long, daysLeft: Int): Int {
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        val keyTotal = "total_$subscriptionId"
        val keyLast = "last_$subscriptionId"
        val savedTotal = prefs.getInt(keyTotal, 0)
        val lastDays = prefs.getInt(keyLast, -1)
        val total = when {
            savedTotal <= 0 || lastDays < 0 -> STANDARD_DAYS.firstOrNull { it >= daysLeft } ?: daysLeft
            daysLeft > lastDays -> daysLeft
            else -> savedTotal
        }.coerceAtLeast(daysLeft).coerceAtLeast(1)
        if (total != savedTotal || daysLeft != lastDays) {
            prefs.edit().putInt(keyTotal, total).putInt(keyLast, daysLeft).apply()
        }
        return total
    }

    fun clear(context: Context) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().clear().apply()
    }
}
