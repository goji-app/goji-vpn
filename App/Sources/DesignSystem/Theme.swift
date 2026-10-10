import SwiftUI
import UIKit

/// Токены «Goji Expressive» — Material 3 Expressive, как в Android (ui/theme/Color.kt, 163f5ab) и
/// Windows/macOS-клиентах: плотные тональные поверхности вместо стекла, без размытия, бликов и
/// теней. Тональная схема построена от фирменного бирюзового, тёплый акцент — терракотовый
/// (tertiary). Каждый цвет динамический: светлый/тёмный вариант по текущей теме.
///
/// Имена токенов v5 «Стекло» сохранены — экраны продолжают их читать, а значения теперь роли M3:
/// textPrimary = onSurface, teal/tealDeep = primary, tealTint = primaryContainer,
/// chip = surfaceContainerHigh, selection = secondaryContainer и т. д. Токены, которые больше
/// ничего не рисуют (блики, кромки, пятна фона, тени), прозрачные.
enum GojiColor {
    // ── роли Material 3 ─────────────────────────────────────
    static let surface = Color(light: 0xFFF4FBF8, dark: 0xFF0E1513)
    static let surfaceContainerLow = Color(light: 0xFFEEF5F2, dark: 0xFF161D1C)
    static let surfaceContainer = Color(light: 0xFFE8EFEC, dark: 0xFF1A2120)
    static let surfaceContainerHigh = Color(light: 0xFFE3EAE7, dark: 0xFF252B2A)
    static let surfaceContainerHighest = Color(light: 0xFFDDE4E1, dark: 0xFF2F3635)
    static let primary = Color(light: 0xFF006A62, dark: 0xFF80D5CA)
    static let onPrimary = Color(light: 0xFFFFFFFF, dark: 0xFF003732)
    static let primaryContainer = Color(light: 0xFF9DF2E6, dark: 0xFF005049)
    static let onPrimaryContainer = Color(light: 0xFF00201D, dark: 0xFF9DF2E6)
    static let secondaryContainer = Color(light: 0xFFCCE8E3, dark: 0xFF334B48)
    static let onSecondaryContainer = Color(light: 0xFF051F1C, dark: 0xFFCCE8E3)
    static let tertiaryContainer = Color(light: 0xFFFFDBCF, dark: 0xFF7B2F12)
    static let onTertiaryContainer = Color(light: 0xFF380D00, dark: 0xFFFFDBCF)
    static let errorContainer = Color(light: 0xFFFFDAD6, dark: 0xFF93000A)
    static let outline = Color(light: 0xFF6F7977, dark: 0xFF889391)
    static let outlineVariant = Color(light: 0xFFBEC9C6, dark: 0xFF3F4947)

    // ── поля v5 (значения — роли M3) ────────────────────────
    static let textPrimary = Color(light: 0xFF161D1C, dark: 0xFFDDE4E1)
    static let textSecondary = Color(light: 0xFF3F4947, dark: 0xFFBEC9C6)
    static let textMuted = outline

    static let teal = primary
    static let tealDeep = primary
    static let tealTint = primaryContainer
    static let tealTintBorder = primary

    static let terracotta = Color(light: 0xFF9A4524, dark: 0xFFFFB59A)
    static let terracottaTint = tertiaryContainer
    static let warning = Color(light: 0xFF8A6100, dark: 0xFFF0BF48)
    static let danger = Color(light: 0xFFBA1A1A, dark: 0xFFFFB4AB)
    static let jamBg = errorContainer
    static let jamText = danger

    static let glass = surfaceContainer
    static let glassStrong = surfaceContainerHigh
    static let glassFaint = surfaceContainerHighest
    static let stroke = Color.clear
    /// Разделитель внутри карточек — «щель» цвета фона (M3 Expressive), а не тонкая линия.
    static let hair = surface
    static let highlight = Color.clear
    static let chip = surfaceContainerHigh
    static let track = surfaceContainerHighest
    static let selection = secondaryContainer
    static let glassShadow = Color.clear

    // Градиенты v5 сведены к плоскому primary — в M3E заливки однотонные.
    static let accentTop = primary
    static let accentMid = primary
    static let accentBottom = primary
    static let accentGlow = Color.clear

    static let inkButton = primary
    static let inkButtonText = onPrimary
    static let ringCore = surfaceContainer

    static let baseTop = surface
    static let baseMid = surface
    static let baseBottom = surface

    static var accentGradient: LinearGradient {
        LinearGradient(colors: [accentTop, accentBottom], startPoint: .top, endPoint: .bottom)
    }
}

extension Color {
    /// ARGB-константы (0xAARRGGBB), как в Android-токенах.
    init(light: UInt32, dark: UInt32) {
        self.init(UIColor { traits in
            UIColor(argb: traits.userInterfaceStyle == .dark ? dark : light)
        })
    }
}

extension UIColor {
    convenience init(argb: UInt32) {
        self.init(
            red: CGFloat((argb >> 16) & 0xFF) / 255,
            green: CGFloat((argb >> 8) & 0xFF) / 255,
            blue: CGFloat(argb & 0xFF) / 255,
            alpha: CGFloat((argb >> 24) & 0xFF) / 255
        )
    }
}

/// Шрифты: Manrope во всём приложении, как в Android (Type.kt); Instrument Serif остался только
/// для приветствий на других письменностях. Размеры масштабируются системной настройкой
/// «Размер текста» (Dynamic Type) через relativeTo.
enum GojiFont {
    enum Weight: String {
        case regular = "Manrope-Regular"
        case medium = "Manrope-Medium"
        case semibold = "Manrope-SemiBold"
        case bold = "Manrope-Bold"
        case extraBold = "Manrope-ExtraBold"
    }

    static func manrope(_ size: CGFloat, _ weight: Weight = .regular, relativeTo style: Font.TextStyle = .body) -> Font {
        .custom(weight.rawValue, size: size, relativeTo: style)
    }

    static func serif(_ size: CGFloat, italic: Bool = false, relativeTo style: Font.TextStyle = .largeTitle) -> Font {
        .custom(italic ? "InstrumentSerif-Italic" : "InstrumentSerif-Regular", size: size, relativeTo: style)
    }

    /// Заголовки экранов: Manrope Bold 26 (Android 1.0.115).
    static func title(_ size: CGFloat = 26) -> Font { manrope(size, .bold, relativeTo: .largeTitle) }
    static func section() -> Font { manrope(17, .bold, relativeTo: .headline) }
    static func body() -> Font { manrope(14, .medium) }
    static func caption() -> Font { manrope(12, .medium, relativeTo: .caption) }
    static func button() -> Font { manrope(15, .bold, relativeTo: .headline) }
}

enum GojiMetrics {
    static let screenPadding: CGFloat = 16
    static let cardRadius: CGFloat = 24
    static let buttonRadius: CGFloat = 18
    static let tabBarHeight: CGFloat = 64
}
