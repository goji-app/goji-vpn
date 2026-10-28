import SwiftUI

/// Первый запуск: два коротких экрана знакомства (что такое Goji, как начать — как на Android,
/// Windows и macOS, 2026-10-28), затем обязательный экран о данных (PrivacyIntroView, App Store
/// 5.4). «Пропустить» ведёт к экрану о данных — его пропустить нельзя.
struct OnboardingView: View {
    @State private var page = 0

    private let pages: [(title: String, points: String)] = [
        ("onboarding.title1", "onboarding.points1"),
        ("onboarding.title2", "onboarding.points2")
    ]

    var body: some View {
        if page >= pages.count {
            PrivacyIntroView()
                .transition(.opacity)
        } else {
            intro(pages[page])
                .transition(.opacity)
        }
    }

    private func intro(_ content: (title: String, points: String)) -> some View {
        VStack(alignment: .leading, spacing: 16) {
            Image("Mascot")
                .resizable()
                .scaledToFit()
                .frame(width: 96, height: 96)
                .accessibilityHidden(true)
                .padding(.top, 24)
            Spacer(minLength: 0)
            GlassCard {
                VStack(alignment: .leading, spacing: 14) {
                    Text(L(content.title))
                        .font(GojiFont.title(28))
                        .foregroundStyle(GojiColor.textPrimary)
                        .fixedSize(horizontal: false, vertical: true)
                    ForEach(L(content.points).components(separatedBy: "\n"), id: \.self) { point in
                        HStack(alignment: .top, spacing: 10) {
                            Circle()
                                .fill(GojiColor.teal)
                                .frame(width: 6, height: 6)
                                .padding(.top, 7)
                            Text(point)
                                .font(GojiFont.body())
                                .foregroundStyle(GojiColor.textPrimary)
                                .fixedSize(horizontal: false, vertical: true)
                        }
                    }
                    dots
                    HStack {
                        Button(L("onboarding.skip")) { withAnimation { page = pages.count } }
                            .font(GojiFont.manrope(14, .semibold))
                            .foregroundStyle(GojiColor.textSecondary)
                            .frame(minHeight: 44)
                        Spacer()
                        Button(L("onboarding.next")) { withAnimation { page += 1 } }
                            .buttonStyle(PrimaryButtonStyle())
                            .frame(maxWidth: 180)
                    }
                }
            }
            .padding(.bottom, 24)
        }
        .padding(.horizontal, GojiMetrics.screenPadding)
    }

    /// Точки страниц: два экрана знакомства и экран о данных.
    private var dots: some View {
        HStack(spacing: 6) {
            ForEach(0...pages.count, id: \.self) { i in
                Capsule()
                    .fill(i == page ? GojiColor.tealDeep : GojiColor.textSecondary.opacity(0.3))
                    .frame(width: i == page ? 18 : 6, height: 6)
            }
        }
        .frame(maxWidth: .infinity)
        .accessibilityElement()
        .accessibilityLabel("\(page + 1) / \(pages.count + 1)")
    }
}
