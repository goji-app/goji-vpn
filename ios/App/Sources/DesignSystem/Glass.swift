import SwiftUI

/// Общий фон всех экранов — «Goji Expressive» (Material 3 Expressive, порт Android
/// GlassBackdrop.kt 163f5ab): сплошной surface. Цветные пятна v5 убраны. Имя прежнее, чтобы не
/// трогать экраны.
struct GlassBackdrop: View {
    var body: some View {
        GojiColor.surface.ignoresSafeArea()
    }
}

/// Карточка-модуль: тональная заливка surfaceContainer (порт godjiCard из Android CardStyle.kt).
struct GlassCard<Content: View>: View {
    var padding: CGFloat = 16
    var radius: CGFloat = GojiMetrics.cardRadius
    var highlighted = false
    @ViewBuilder var content: Content

    var body: some View {
        content
            .padding(padding)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(GlassSurface(radius: radius, highlighted: highlighted))
    }
}

/// Поверхность «Goji Expressive»: плотная тональная заливка (surfaceContainer, strong — High),
/// у выбранного — акцентная обводка 2pt. Без материала, кромок, бликов и теней.
struct GlassSurface: View {
    var radius: CGFloat = GojiMetrics.cardRadius
    var highlighted = false
    var strong = false

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: radius, style: .continuous)
        shape
            .fill(strong ? GojiColor.glassStrong : GojiColor.glass)
            .overlay {
                if highlighted { shape.strokeBorder(GojiColor.primary, lineWidth: 2) }
            }
    }
}

struct SectionHeader: View {
    let title: String
    var trailing: AnyView? = nil

    var body: some View {
        HStack {
            Text(title)
                .font(GojiFont.section())
                .foregroundStyle(GojiColor.textPrimary)
            Spacer()
            trailing
        }
        .padding(.top, 14)
        .padding(.bottom, 4)
        .accessibilityAddTraits(.isHeader)
    }
}

/// Основная кнопка: плоская заливка primary, текст onPrimary (M3 Expressive).
struct PrimaryButtonStyle: ButtonStyle {
    var tint: Color = GojiColor.inkButton
    var textColor: Color = GojiColor.inkButtonText

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(GojiFont.button())
            .foregroundStyle(textColor)
            .frame(maxWidth: .infinity, minHeight: 54)
            .background(RoundedRectangle(cornerRadius: GojiMetrics.buttonRadius, style: .continuous).fill(tint))
            .scaleEffect(configuration.isPressed ? 0.97 : 1)
            .opacity(configuration.isPressed ? 0.9 : 1)
            .animation(.easeOut(duration: 0.15), value: configuration.isPressed)
    }
}

struct SecondaryButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(GojiFont.button())
            .foregroundStyle(GojiColor.textPrimary)
            .frame(maxWidth: .infinity, minHeight: 50)
            .background(GlassSurface(radius: GojiMetrics.buttonRadius))
            .scaleEffect(configuration.isPressed ? 0.97 : 1)
            .animation(.easeOut(duration: 0.15), value: configuration.isPressed)
    }
}

/// Пилюля-бейдж («АКТИВНА», «Неактивен»).
struct Badge: View {
    let text: String
    var color: Color = GojiColor.tealDeep
    var background: Color = GojiColor.tealTint

    var body: some View {
        Text(text)
            .font(GojiFont.manrope(11, .bold, relativeTo: .caption2))
            .foregroundStyle(color)
            .padding(.horizontal, 10)
            .padding(.vertical, 4)
            .background(Capsule().fill(background))
    }
}

/// Строка настройки/списка: иконка, заголовок с подписью, справа — значение или шеврон.
struct RowLabel: View {
    let icon: String
    let title: String
    var subtitle: String? = nil
    var value: String? = nil
    var tint: Color = GojiColor.teal
    var showsChevron = true

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: icon)
                .font(.system(size: 15, weight: .semibold))
                .foregroundStyle(tint)
                .frame(width: 32, height: 32)
                .background(Circle().fill(GojiColor.chip))
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                    .font(GojiFont.manrope(14, .semibold))
                    .foregroundStyle(GojiColor.textPrimary)
                if let subtitle {
                    Text(subtitle)
                        .font(GojiFont.caption())
                        .foregroundStyle(GojiColor.textSecondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            Spacer(minLength: 8)
            if let value {
                Text(value)
                    .font(GojiFont.caption())
                    .foregroundStyle(GojiColor.textSecondary)
                    .lineLimit(1)
            }
            if showsChevron {
                Image(systemName: "chevron.right")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(GojiColor.textMuted)
            }
        }
        .contentShape(Rectangle())
    }
}

/// Сегментированный переключатель-«пилюля» (периоды, вкладки «Открытые/История» и т.п.):
/// дорожка surfaceContainerHigh, выбранный сегмент — secondaryContainer.
struct PillSegmented<T: Hashable>: View {
    let options: [T]
    @Binding var selection: T
    let label: (T) -> String

    var body: some View {
        HStack(spacing: 4) {
            ForEach(options, id: \.self) { option in
                let selected = option == selection
                Button {
                    withAnimation(.easeInOut(duration: 0.2)) { selection = option }
                } label: {
                    Text(label(option))
                        .font(GojiFont.manrope(13, .bold))
                        .foregroundStyle(selected ? GojiColor.onSecondaryContainer : GojiColor.textSecondary)
                        .frame(maxWidth: .infinity, minHeight: 34)
                        .background(
                            Capsule().fill(selected ? GojiColor.secondaryContainer : .clear)
                        )
                }
                .buttonStyle(.plain)
                .accessibilityAddTraits(selected ? .isSelected : [])
            }
        }
        .padding(3)
        .background(Capsule().fill(GojiColor.chip))
    }
}

/// Общая «оболочка» экрана: фон + заголовок + прокручиваемое содержимое.
struct ScreenScaffold<Content: View>: View {
    let title: String
    var subtitle: String? = nil
    var trailing: AnyView? = nil
    var bottomInset: CGFloat = 24
    @ViewBuilder var content: Content

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 12) {
                HStack(alignment: .firstTextBaseline) {
                    VStack(alignment: .leading, spacing: 4) {
                        Text(title)
                            .font(GojiFont.title())
                            .foregroundStyle(GojiColor.textPrimary)
                            .accessibilityAddTraits(.isHeader)
                        if let subtitle {
                            Text(subtitle)
                                .font(GojiFont.caption())
                                .foregroundStyle(GojiColor.textSecondary)
                        }
                    }
                    Spacer()
                    trailing
                }
                .padding(.top, 8)
                content
            }
            .padding(.horizontal, GojiMetrics.screenPadding)
            .padding(.bottom, bottomInset)
        }
        .scrollIndicators(.hidden)
    }
}

/// Карточка ошибки/пустого состояния с кнопкой повтора.
struct MessageCard: View {
    let icon: String
    let text: String
    var actionTitle: String? = nil
    var action: (() -> Void)? = nil

    var body: some View {
        GlassCard {
            VStack(spacing: 12) {
                Image(systemName: icon)
                    .font(.system(size: 28))
                    .foregroundStyle(GojiColor.textMuted)
                Text(text)
                    .font(GojiFont.body())
                    .foregroundStyle(GojiColor.textSecondary)
                    .multilineTextAlignment(.center)
                if let actionTitle, let action {
                    Button(actionTitle, action: action)
                        .buttonStyle(SecondaryButtonStyle())
                }
            }
            .frame(maxWidth: .infinity)
        }
    }
}
