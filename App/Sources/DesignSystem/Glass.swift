import SwiftUI

/// Общий фон всех экранов: мягкий вертикальный градиент + четыре размытых цветных пятна —
/// под ним «стеклянные» карточки получают цветную подложку, как в эталоне v5.
struct GlassBackdrop: View {
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        GeometryReader { geo in
            let w = geo.size.width, h = geo.size.height
            ZStack {
                LinearGradient(colors: [GojiColor.baseTop, GojiColor.baseMid, GojiColor.baseBottom], startPoint: .top, endPoint: .bottom)
                Group {
                    blob(GojiColor.blob1, size: w * 0.9).position(x: w * 0.15, y: h * 0.12)
                    blob(GojiColor.blob2, size: w * 0.8).position(x: w * 0.95, y: h * 0.3)
                    blob(GojiColor.blob3, size: w * 0.75).position(x: w * 0.1, y: h * 0.72)
                    blob(GojiColor.blob4, size: w * 0.85).position(x: w * 0.9, y: h * 0.92)
                }
                .opacity(scheme == .dark ? 0.32 : 0.6)
                .blur(radius: 60)
            }
        }
        .ignoresSafeArea()
    }

    private func blob(_ color: Color, size: CGFloat) -> some View {
        Circle().fill(color).frame(width: size, height: size)
    }
}

/// Стеклянная карточка: материал + полупрозрачная заливка + светлая кромка + мягкая тень.
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

struct GlassSurface: View {
    var radius: CGFloat = GojiMetrics.cardRadius
    var highlighted = false
    var strong = false

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: radius, style: .continuous)
        shape
            .fill(.ultraThinMaterial)
            .overlay(shape.fill(strong ? GojiColor.glassStrong : GojiColor.glass))
            .overlay(
                shape.strokeBorder(
                    highlighted ? GojiColor.teal.opacity(0.7) : GojiColor.stroke,
                    lineWidth: highlighted ? 1.5 : 1
                )
            )
            .overlay(
                shape.strokeBorder(
                    LinearGradient(colors: [GojiColor.highlight, .clear], startPoint: .top, endPoint: .center),
                    lineWidth: 1
                )
            )
            .shadow(color: GojiColor.glassShadow, radius: 18, x: 0, y: 8)
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

/// Основная кнопка: тёмная «чернильная» на светлой теме, светлая на тёмной.
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

/// Сегментированный переключатель-«пилюля» (периоды, вкладки «Открытые/История» и т.п.).
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
                        .foregroundStyle(selected ? GojiColor.inkButtonText : GojiColor.textSecondary)
                        .frame(maxWidth: .infinity, minHeight: 36)
                        .background(
                            RoundedRectangle(cornerRadius: 11, style: .continuous)
                                .fill(selected ? GojiColor.inkButton : .clear)
                        )
                }
                .buttonStyle(.plain)
                .accessibilityAddTraits(selected ? .isSelected : [])
            }
        }
        .padding(4)
        .background(RoundedRectangle(cornerRadius: 15, style: .continuous).fill(GojiColor.chip))
    }
}

/// Общая «оболочка» экрана: фон + заголовок + прокручиваемое содержимое с отступом под таб-бар.
struct ScreenScaffold<Content: View>: View {
    let title: String
    var subtitle: String? = nil
    var trailing: AnyView? = nil
    var bottomInset: CGFloat = GojiMetrics.tabBarHeight + 24
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
