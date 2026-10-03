import SwiftUI
import UIKit

/// Токены дизайна v5 «Стекло» — те же значения, что в Android (ui/theme/Color.kt) и
/// Windows/macOS-клиентах. Каждый цвет динамический: светлый/тёмный вариант выбирается по
/// текущей теме (системной или принудительной из «Настроек»).
enum GojiColor {
    static let textPrimary = Color(light: 0xFF0B1F1C, dark: 0xFFF5F7FA)
    static let textSecondary = Color(light: 0x9E0B1F1C, dark: 0xCCE8EEF4)
    static let textMuted = Color(light: 0x7A0B1F1C, dark: 0x99E8EEF4)

    static let teal = Color(light: 0xFF00A79B, dark: 0xFF5FFFE6)
    static let tealDeep = Color(light: 0xFF007A70, dark: 0xFF7FFFEA)
    static let tealTint = Color(light: 0x2400A79B, dark: 0x242FE0CF)
    static let tealTintBorder = Color(light: 0x5900A79B, dark: 0x595FFFE6)

    static let terracotta = Color(light: 0xFFE0693F, dark: 0xFFFF8E62)
    static let terracottaTint = Color(light: 0x80FFD6C4, dark: 0x6178321C)
    static let warning = Color(light: 0xFFC98B12, dark: 0xFFE0A63C)
    static let danger = Color(light: 0xFFD2432C, dark: 0xFFFF7A66)
    static let jamBg = Color(light: 0x8CFFC8B9, dark: 0x66782819)
    static let jamText = Color(light: 0xFFC2412A, dark: 0xFFFF8A6E)

    static let glass = Color(light: 0x33FFFFFF, dark: 0x9E12161E)
    static let glassStrong = Color(light: 0x66FFFFFF, dark: 0xBD161A24)
    static let glassFaint = Color(light: 0x29FFFFFF, dark: 0x6612161E)
    static let stroke = Color(light: 0x80FFFFFF, dark: 0x24FFFFFF)
    static let hair = Color(light: 0x140B1F1C, dark: 0x1AFFFFFF)
    static let highlight = Color(light: 0xCCFFFFFF, dark: 0x2EFFFFFF)
    static let chip = Color(light: 0x59FFFFFF, dark: 0x14FFFFFF)
    static let track = Color(light: 0x170B1F1C, dark: 0x1FFFFFFF)
    static let selection = Color(light: 0x1A00A79B, dark: 0x172FE0CF)
    static let glassShadow = Color(light: 0x2E3C1E78, dark: 0x590A0528)

    static let accentTop = Color(light: 0xFF2AC9BA, dark: 0xFF34E3D2)
    static let accentMid = Color(light: 0xFF00A79B, dark: 0xFF10B8A9)
    static let accentBottom = Color(light: 0xFF008F84, dark: 0xFF079487)
    static let accentGlow = Color(light: 0x5900A79B, dark: 0x592FE0CF)

    static let inkButton = Color(light: 0xFF0B1F1C, dark: 0xFFF1F7F6)
    static let inkButtonText = Color(light: 0xFFF4FAF9, dark: 0xFF0B1F1C)
    static let ringCore = Color(light: 0xE0FFFFFF, dark: 0xE610141C)

    static let baseTop = Color(light: 0xFFF2F4F7, dark: 0xFF080C12)
    static let baseMid = Color(light: 0xFFE6ECF1, dark: 0xFF0C1320)
    static let baseBottom = Color(light: 0xFFDDE7EA, dark: 0xFF06100F)
    static let blob1 = Color(light: 0xFF6FD1C4, dark: 0xFF0F8C80)
    static let blob2 = Color(light: 0xFFAFA6F2, dark: 0xFF4332A8)
    static let blob3 = Color(light: 0xFFF2C2A5, dark: 0xFF8A3D2A)
    static let blob4 = Color(light: 0xFF8EC3EA, dark: 0xFF1B4F8C)

    static var accentGradient: LinearGradient {
        LinearGradient(colors: [accentTop, accentMid, accentBottom], startPoint: .top, endPoint: .bottom)
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

/// Шрифты: Manrope — основной текст, Instrument Serif — заголовки. Размеры масштабируются
/// системной настройкой «Размер текста» (Dynamic Type) через relativeTo.
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

    static func title(_ size: CGFloat = 30) -> Font { serif(size) }
    static func section() -> Font { manrope(17, .bold, relativeTo: .headline) }
    static func body() -> Font { manrope(14, .medium) }
    static func caption() -> Font { manrope(12, .medium, relativeTo: .caption) }
    static func button() -> Font { manrope(15, .bold, relativeTo: .headline) }
}

enum GojiMetrics {
    static let screenPadding: CGFloat = 18
    static let cardRadius: CGFloat = 22
    static let buttonRadius: CGFloat = 18
    static let tabBarHeight: CGFloat = 64
}
