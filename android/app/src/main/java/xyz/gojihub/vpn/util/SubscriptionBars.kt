package xyz.gojihub.vpn.util

/** Сколько из 5 «полосок» остатка подписки заполнено — как индикатор сигнала. Общая для
 *  плитки на главной (ConnectScreen) и виджета «Подписка». */
fun subscriptionBars(days: Int): Int = when {
    days >= 90 -> 5
    days >= 30 -> 4
    days >= 14 -> 3
    days >= 7 -> 2
    days >= 1 -> 1
    else -> 0
}
