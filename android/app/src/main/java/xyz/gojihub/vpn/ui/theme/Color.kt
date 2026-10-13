package xyz.gojihub.vpn.ui.theme

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.graphics.Color

enum class FontSizePreset(val multiplier: Float) { SMALL(0.9f), NORMAL(1f), LARGE(1.15f) }

/**
 * Токены дизайна «Goji Expressive» — Material 3 Expressive (Android 16/17): плотные тональные
 * поверхности вместо стекла, без размытия, бликов и теней. Тональная схема построена от
 * фирменного бирюзового (#00A79B), тёплый акцент — терракотовый (tertiary).
 *
 * Имена полей v5 «Стекло» НЕ менялись — экраны продолжают их читать, но значения теперь
 * роли M3: TextPrimary = onSurface, Teal/TealDeep = primary, TealTint = primaryContainer,
 * Chip = surfaceContainerHigh, Thumb = secondaryContainer и т. д. Поля, которые больше
 * ничего не рисуют (блики, кромки, пятна фона), оставлены прозрачными для совместимости.
 *
 * Background = Transparent: экраны делают `.background(GodjiColors.Background)` — это no-op,
 * под ними сплошной фон GlassBackdrop (BackgroundSolid = surface).
 */
object GodjiColors {
    var isDark by mutableStateOf(false)
    var themeMode by mutableStateOf(ThemeMode.SYSTEM)
    var fontSizePreset by mutableStateOf(FontSizePreset.NORMAL)

    // ── роли Material 3 ──────────────────────────────────────
    var SurfaceBase by mutableStateOf(Color(0xFFF4FBF8))
    var SurfaceContainerLow by mutableStateOf(Color(0xFFEEF5F2))
    var SurfaceContainer by mutableStateOf(Color(0xFFE8EFEC))
    var SurfaceContainerHigh by mutableStateOf(Color(0xFFE3EAE7))
    var SurfaceContainerHighest by mutableStateOf(Color(0xFFDDE4E1))
    var PrimaryContainer by mutableStateOf(Color(0xFF9DF2E6))
    var OnPrimaryContainer by mutableStateOf(Color(0xFF00201D))
    var SecondaryContainer by mutableStateOf(Color(0xFFCCE8E3))
    var OnSecondaryContainer by mutableStateOf(Color(0xFF051F1C))
    var TertiaryContainer by mutableStateOf(Color(0xFFFFDBCF))
    var OnTertiaryContainer by mutableStateOf(Color(0xFF380D00))
    var ErrorContainer by mutableStateOf(Color(0xFFFFDAD6))
    var Outline by mutableStateOf(Color(0xFF6F7977))
    var OutlineVariant by mutableStateOf(Color(0xFFBEC9C6))

    // ── поля v5 (значения — роли M3) ─────────────────────────
    var Background by mutableStateOf(Color.Transparent)
    var BackgroundSolid by mutableStateOf(Color(0xFFF4FBF8))
    var Surface by mutableStateOf(Color(0xFFFFFFFF))          // текст на акценте (onPrimary)
    var SurfaceGlass by mutableStateOf(Color(0xFFE3EAE7))

    var Ink by mutableStateOf(Color(0xFF161D1C))
    var InkShadow by mutableStateOf(Color.Transparent)
    var TextPrimary by mutableStateOf(Color(0xFF161D1C))
    var TextSecondary by mutableStateOf(Color(0xFF3F4947))
    var TextMuted by mutableStateOf(Color(0xFF6F7977))

    var Teal by mutableStateOf(Color(0xFF006A62))             // primary
    var TealBright by mutableStateOf(Color(0xFF006A62))
    var TealDeep by mutableStateOf(Color(0xFF006A62))         // акцентный ТЕКСТ
    var TealTint by mutableStateOf(Color(0xFF9DF2E6))         // primaryContainer
    var TealTintBorder by mutableStateOf(Color(0xFF006A62))

    var Terracotta by mutableStateOf(Color(0xFF9A4524))       // tertiary
    var TerracottaDeep by mutableStateOf(Color(0xFF7B2F12))
    var TerracottaTint by mutableStateOf(Color(0xFFFFDBCF))   // tertiaryContainer
    var TerracottaTintBorder by mutableStateOf(Color.Transparent)

    var Warning by mutableStateOf(Color(0xFF8A6100))
    var Danger by mutableStateOf(Color(0xFFBA1A1A))
    var JamBg by mutableStateOf(Color(0xFFFFDAD6))
    var JamBorder by mutableStateOf(Color.Transparent)
    var JamText by mutableStateOf(Color(0xFFBA1A1A))

    var CardBorder by mutableStateOf(Color.Transparent)       // у тональных карточек нет обводки
    var CardBorderStrong by mutableStateOf(Color.Transparent)
    var ButtonBorder by mutableStateOf(Color(0xFF6F7977))     // outline (поля ввода, outlined-кнопки)
    var Chip by mutableStateOf(Color(0xFFE3EAE7))             // surfaceContainerHigh
    var TrackBg by mutableStateOf(Color(0xFFDDE4E1))          // surfaceContainerHighest

    var BorderTeal by mutableStateOf(Color(0xFF006A62))
    var Purple by mutableStateOf(Color(0xFF5B3BD6))

    var Glass by mutableStateOf(Color(0xFFE8EFEC))            // карточки — surfaceContainer
    var GlassStrong by mutableStateOf(Color(0xFFE3EAE7))      // лист входа, тост — High
    var GlassFaint by mutableStateOf(Color(0xFFDDE4E1))       // строки внутри карточек — Highest
    var Hair by mutableStateOf(Color(0xFFDDE4E1))             // разделители
    var Hl by mutableStateOf(Color.Transparent)
    var GlassSpot by mutableStateOf(Color.Transparent)
    var GlassGloss by mutableStateOf(Color.Transparent)
    var RimA by mutableStateOf(Color.Transparent); var RimB by mutableStateOf(Color.Transparent)
    var RimC by mutableStateOf(Color.Transparent); var RimD by mutableStateOf(Color.Transparent)
    var InnerTop by mutableStateOf(Color.Transparent)
    var InnerBottom by mutableStateOf(Color.Transparent)
    var GlassShadow by mutableStateOf(Color.Transparent)
    // Градиенты v5 сведены к плоскому primary — в M3E заливки однотонные.
    var AccentGradTop by mutableStateOf(Color(0xFF006A62))
    var AccentGradMid by mutableStateOf(Color(0xFF006A62))
    var AccentGradBottom by mutableStateOf(Color(0xFF006A62))
    var AccentGlow by mutableStateOf(Color.Transparent)
    var SelBg by mutableStateOf(Color(0xFFCCE8E3))
    var Thumb by mutableStateOf(Color(0xFFCCE8E3))            // выбранный сегмент — secondaryContainer
    var Lens by mutableStateOf(Color(0xFFCCE8E3))             // индикатор вкладки — secondaryContainer
    var RingCore by mutableStateOf(Color(0xFFE8EFEC))
    var InkBtn by mutableStateOf(Color(0xFF006A62)); var InkBtnText by mutableStateOf(Color(0xFFFFFFFF))
    // фон (GlassBackdrop) — сплошной surface
    var BaseTop by mutableStateOf(Color(0xFFF4FBF8)); var BaseMid by mutableStateOf(Color(0xFFF4FBF8)); var BaseBottom by mutableStateOf(Color(0xFFF4FBF8))
    var Blob1 by mutableStateOf(Color.Transparent); var Blob2 by mutableStateOf(Color.Transparent)
    var Blob3 by mutableStateOf(Color.Transparent); var Blob4 by mutableStateOf(Color.Transparent)
    var BlobAlpha by mutableStateOf(0f)
    var TexLine by mutableStateOf(Color.Transparent)
    var TexDot by mutableStateOf(Color.Transparent)

    private fun apply(
        surface: Color, cLow: Color, c: Color, cHigh: Color, cHighest: Color,
        onSurface: Color, onSurfaceVariant: Color, outline: Color, outlineVariant: Color,
        primary: Color, onPrimary: Color, primaryC: Color, onPrimaryC: Color,
        secondaryC: Color, onSecondaryC: Color,
        tertiary: Color, tertiaryDeep: Color, tertiaryC: Color, onTertiaryC: Color,
        error: Color, errorC: Color, warning: Color
    ) {
        SurfaceBase = surface; SurfaceContainerLow = cLow; SurfaceContainer = c
        SurfaceContainerHigh = cHigh; SurfaceContainerHighest = cHighest
        PrimaryContainer = primaryC; OnPrimaryContainer = onPrimaryC
        SecondaryContainer = secondaryC; OnSecondaryContainer = onSecondaryC
        TertiaryContainer = tertiaryC; OnTertiaryContainer = onTertiaryC
        ErrorContainer = errorC; Outline = outline; OutlineVariant = outlineVariant

        Background = Color.Transparent; BackgroundSolid = surface
        Surface = onPrimary; SurfaceGlass = cHigh
        Ink = onSurface; TextPrimary = onSurface; TextSecondary = onSurfaceVariant; TextMuted = outline
        Teal = primary; TealBright = primary; TealDeep = primary; TealTint = primaryC; TealTintBorder = primary
        Terracotta = tertiary; TerracottaDeep = tertiaryDeep; TerracottaTint = tertiaryC
        Warning = warning; Danger = error; JamBg = errorC; JamText = error
        ButtonBorder = outline; Chip = cHigh; TrackBg = cHighest; BorderTeal = primary
        Glass = c; GlassStrong = cHigh; GlassFaint = cHighest; Hair = cHighest
        AccentGradTop = primary; AccentGradMid = primary; AccentGradBottom = primary
        SelBg = secondaryC; Thumb = secondaryC; Lens = secondaryC; RingCore = c
        InkBtn = primary; InkBtnText = onPrimary
        BaseTop = surface; BaseMid = surface; BaseBottom = surface
    }

    fun applyLight() {
        isDark = false
        apply(
            surface = Color(0xFFF4FBF8), cLow = Color(0xFFEEF5F2), c = Color(0xFFE8EFEC),
            cHigh = Color(0xFFE3EAE7), cHighest = Color(0xFFDDE4E1),
            onSurface = Color(0xFF161D1C), onSurfaceVariant = Color(0xFF3F4947),
            outline = Color(0xFF6F7977), outlineVariant = Color(0xFFBEC9C6),
            primary = Color(0xFF006A62), onPrimary = Color(0xFFFFFFFF),
            primaryC = Color(0xFF9DF2E6), onPrimaryC = Color(0xFF00201D),
            secondaryC = Color(0xFFCCE8E3), onSecondaryC = Color(0xFF051F1C),
            tertiary = Color(0xFF9A4524), tertiaryDeep = Color(0xFF7B2F12),
            tertiaryC = Color(0xFFFFDBCF), onTertiaryC = Color(0xFF380D00),
            error = Color(0xFFBA1A1A), errorC = Color(0xFFFFDAD6), warning = Color(0xFF8A6100)
        )
    }

    fun applyDark() {
        isDark = true
        apply(
            surface = Color(0xFF0E1513), cLow = Color(0xFF161D1C), c = Color(0xFF1A2120),
            cHigh = Color(0xFF252B2A), cHighest = Color(0xFF2F3635),
            onSurface = Color(0xFFDDE4E1), onSurfaceVariant = Color(0xFFBEC9C6),
            outline = Color(0xFF889391), outlineVariant = Color(0xFF3F4947),
            primary = Color(0xFF80D5CA), onPrimary = Color(0xFF003732),
            primaryC = Color(0xFF005049), onPrimaryC = Color(0xFF9DF2E6),
            secondaryC = Color(0xFF334B48), onSecondaryC = Color(0xFFCCE8E3),
            tertiary = Color(0xFFFFB59A), tertiaryDeep = Color(0xFFFFDBCF),
            tertiaryC = Color(0xFF7B2F12), onTertiaryC = Color(0xFFFFDBCF),
            error = Color(0xFFFFB4AB), errorC = Color(0xFF93000A), warning = Color(0xFFF0BF48)
        )
    }
}
