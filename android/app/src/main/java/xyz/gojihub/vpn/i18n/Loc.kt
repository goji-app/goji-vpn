package xyz.gojihub.vpn.i18n

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue

/**
 * Та же реактивная var-по-mutableStateOf схема, что и у GodjiColors (тема) — чтение
 * Loc.s.xxx внутри любого @Composable само подписывает его на перекомпоновку, поэтому
 * переключатель языка в Настройках не требует никакой ручной проводки по экранам.
 */
object Loc {
    var lang by mutableStateOf(AppLanguage.RU)
    val s: Strings get() = Strings.forLang(lang)
    val f: FeatureStrings get() = FeatureStrings.forLang(lang)
}
