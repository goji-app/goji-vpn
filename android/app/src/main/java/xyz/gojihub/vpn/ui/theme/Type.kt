package xyz.gojihub.vpn.ui.theme

import androidx.compose.material3.Typography
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.em
import androidx.compose.ui.unit.sp
import xyz.gojihub.vpn.R

// v5 «Стекло»: во всём приложении один шрифт — Manrope (как в эталоне).
// SpaceGroteskFamily / JetBrainsMonoFamily оставлены как ПСЕВДОНИМЫ Manrope, чтобы экраны 1.0.77,
// которые их импортируют, компилировались без правок, но рисовали Manrope.
val ManropeFamily = FontFamily(
    Font(R.font.manrope_extralight, FontWeight.ExtraLight),
    Font(R.font.manrope_light, FontWeight.Light),
    Font(R.font.manrope_regular, FontWeight.Normal),
    Font(R.font.manrope_medium, FontWeight.Medium),
    Font(R.font.manrope_semibold, FontWeight.SemiBold),
    Font(R.font.manrope_bold, FontWeight.Bold),
    Font(R.font.manrope_extrabold, FontWeight.ExtraBold)
)
val InstrumentSerifFamily = FontFamily(
    Font(R.font.instrument_serif, FontWeight.Normal, FontStyle.Normal),
    Font(R.font.instrument_serif_italic, FontWeight.Normal, FontStyle.Italic)
)
val SpaceGroteskFamily = ManropeFamily
val JetBrainsMonoFamily = ManropeFamily

private val base = Typography()
/** Стиль текста по умолчанию (LocalTextStyle) для всех Text без явного lineHeight. У M3 bodyLarge
 *  интервал фиксированный 24sp — у текста 11–13sp он давал двойные промежутки между строками при
 *  переносе. Относительный интервал растёт вместе со шрифтом. В типографику (её смешивают поля
 *  ввода) его не кладём — только в LocalTextStyle, см. GodjiVpnTheme. */
val DefaultTextStyle = TextStyle(fontFamily = ManropeFamily, fontSize = 16.sp, lineHeight = 1.3.em)

val GodjiTypography = Typography(
    displayLarge = base.displayLarge.copy(fontFamily = ManropeFamily),
    displayMedium = base.displayMedium.copy(fontFamily = ManropeFamily),
    displaySmall = base.displaySmall.copy(fontFamily = ManropeFamily),
    headlineLarge = base.headlineLarge.copy(fontFamily = ManropeFamily),
    headlineMedium = base.headlineMedium.copy(fontFamily = ManropeFamily),
    headlineSmall = TextStyle(fontFamily = ManropeFamily, fontWeight = FontWeight.Bold, fontSize = 27.sp),
    titleLarge = base.titleLarge.copy(fontFamily = ManropeFamily),
    titleMedium = TextStyle(fontFamily = ManropeFamily, fontWeight = FontWeight.Bold, fontSize = 16.sp),
    titleSmall = base.titleSmall.copy(fontFamily = ManropeFamily),
    // Интервал здесь — sp, как у остальных стилей: поля ввода M3 плавно переводят подпись из
    // bodyLarge в bodySmall (lerp), и смесь em с sp роняет приложение («Cannot perform operation
    // for Em and Sp»). Относительный интервал для обычного текста — DefaultTextStyle в Theme.kt.
    bodyLarge = base.bodyLarge.copy(fontFamily = ManropeFamily),
    bodyMedium = TextStyle(fontFamily = ManropeFamily, fontWeight = FontWeight.Normal, fontSize = 13.5.sp),
    bodySmall = base.bodySmall.copy(fontFamily = ManropeFamily),
    labelLarge = base.labelLarge.copy(fontFamily = ManropeFamily),
    labelMedium = base.labelMedium.copy(fontFamily = ManropeFamily),
    labelSmall = TextStyle(fontFamily = ManropeFamily, fontWeight = FontWeight.SemiBold, fontSize = 10.5.sp),
)
