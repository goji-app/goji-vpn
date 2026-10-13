package xyz.gojihub.vpn.ui.theme

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.Density

/** Читает ТЕКУЩИЕ (реактивные, см. GodjiColors) цвета при каждой перекомпоновке — раньше это
 *  был обычный val, посчитанный один раз при загрузке класса из тогдашних значений GodjiColors,
 *  поэтому переключение тёмной темы красило сами экраны (они читают GodjiColors.* напрямую), но
 *  НЕ трогало дефолты стандартных M3-компонентов (например обводку/подпись OutlinedTextField,
 *  где явного цвета нет) — снаружи это выглядело как "часть текста осталась в старом цвете".
 *  Базовая схема (light/dark ColorScheme) выбирается по GodjiColors.isDark, чтобы поля, которые
 *  мы сами не переопределяем ниже, тоже брали разумные тёмные/светлые дефолты. */
@Composable
private fun godjiColorScheme() = (if (GodjiColors.isDark) darkColorScheme() else lightColorScheme()).copy(
    primary = GodjiColors.Teal,
    onPrimary = GodjiColors.Surface,
    primaryContainer = GodjiColors.PrimaryContainer,
    onPrimaryContainer = GodjiColors.OnPrimaryContainer,
    secondaryContainer = GodjiColors.SecondaryContainer,
    onSecondaryContainer = GodjiColors.OnSecondaryContainer,
    tertiary = GodjiColors.Terracotta,
    tertiaryContainer = GodjiColors.TertiaryContainer,
    onTertiaryContainer = GodjiColors.OnTertiaryContainer,
    background = GodjiColors.SurfaceBase,
    onBackground = GodjiColors.TextPrimary,
    surface = GodjiColors.SurfaceBase,
    onSurface = GodjiColors.TextPrimary,
    surfaceVariant = GodjiColors.SurfaceContainerHighest,
    onSurfaceVariant = GodjiColors.TextSecondary,
    surfaceContainerLowest = GodjiColors.SurfaceBase,
    surfaceContainerLow = GodjiColors.SurfaceContainerLow,
    surfaceContainer = GodjiColors.SurfaceContainer,
    surfaceContainerHigh = GodjiColors.SurfaceContainerHigh,
    surfaceContainerHighest = GodjiColors.SurfaceContainerHighest,
    outline = GodjiColors.Outline,
    outlineVariant = GodjiColors.OutlineVariant,
    error = GodjiColors.Danger,
    errorContainer = GodjiColors.ErrorContainer,
)

// Ширина экрана (в dp), под которую подобраны все .dp/.sp размеры во всех экранах —
// среднестатистический компактный телефон. Все Compose-экраны написаны с фиксированными
// значениями в dp/sp, которые сами по себе одинаковы в физических размерах на любом экране
// (это и есть смысл dp), но из-за этого на узких экранах вёрстка теснее и требует прокрутки
// (см. verticalScroll в ConnectScreen/PlansScreen/LoginScreen), а на широких — наоборот,
// выглядит мельче относительно доступного места. Подгоняем плотность экрана так, чтобы одна
// и та же вёрстка занимала одну и ту же ДОЛЮ ширины экрана на любом устройстве, а не
// фиксированное количество dp.
private const val REFERENCE_WIDTH_DP = 392f

// Не даём масштабу уйти в крайности: на совсем маленьких экранах текст не становится
// нечитаемо мелким, а на планшетах/очень широких экранах интерфейс не раздувается в разы.
private const val MIN_SCALE = 0.85f
private const val MAX_SCALE = 1.3f

// Системный «Размер шрифта» учитываем, но с потолком: экраны свёрстаны под фиксированные
// карточки, кольца и панель вкладок, и на телефонах с крупным системным шрифтом (у Xiaomi/Redmi
// он доходит до 1.5–2.0) текст вылезал из них и налезал друг на друга. Крупнее можно сделать
// своей настройкой «Размер шрифта» в приложении — итоговый масштаб тоже ограничен.
private const val MAX_SYSTEM_FONT_SCALE = 1.1f
private const val MAX_FONT_SCALE = 1.25f

@Composable
fun GodjiVpnTheme(
    // Тёплая "бумажная" дневная тема v3 (или тёмная — см. GodjiColors.isDark) — системную
    // тему устройства сознательно игнорируем, тема выбирается только тумблером в Настройках.
    content: @Composable () -> Unit
) {
    val configuration = LocalConfiguration.current
    val baseDensity = LocalDensity.current
    val scale = (configuration.screenWidthDp / REFERENCE_WIDTH_DP).coerceIn(MIN_SCALE, MAX_SCALE)
    val scaledDensity = Density(
        density = baseDensity.density * scale,
        // Только fontScale, не density — настройка "Размер шрифта" должна менять размер
        // текста, а не отступы/иконки/размеры карточек (те уже отмасштабированы под ширину
        // экрана выше через density).
        fontScale = (baseDensity.fontScale.coerceIn(0.85f, MAX_SYSTEM_FONT_SCALE) * GodjiColors.fontSizePreset.multiplier)
            .coerceAtMost(MAX_FONT_SCALE)
    )

    CompositionLocalProvider(LocalDensity provides scaledDensity) {
        MaterialTheme(
            colorScheme = godjiColorScheme(),
            typography = GodjiTypography
        ) {
            // Относительный межстрочный интервал для обычного текста (см. DefaultTextStyle).
            androidx.compose.material3.ProvideTextStyle(DefaultTextStyle, content)
        }
    }
}
