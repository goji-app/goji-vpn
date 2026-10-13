import SwiftUI

/// Экран до первого использования: правило App Store 5.4 требует ДО начала использования
/// VPN-сервиса явно сообщить, какие данные собираются и как используются.
struct PrivacyIntroView: View {
    @EnvironmentObject private var model: AppModel
    @Environment(\.openURL) private var openURL

    private let collected: [(String, String)] = [
        ("envelope", "privacy.item.email"),
        ("iphone", "privacy.item.device"),
        ("bubble.left.and.bubble.right", "privacy.item.support")
    ]
    private let notCollected: [(String, String)] = [
        ("eye.slash", "privacy.item.no_logs"),
        ("hand.raised", "privacy.item.no_tracking"),
        ("lock", "privacy.item.no_sale")
    ]

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Image("Mascot")
                    .resizable()
                    .scaledToFit()
                    .frame(width: 96, height: 96)
                    .accessibilityHidden(true)
                    .padding(.top, 24)
                Text(L("privacy.title"))
                    .font(GojiFont.title(32))
                    .foregroundStyle(GojiColor.textPrimary)
                Text(L("privacy.lead"))
                    .font(GojiFont.body())
                    .foregroundStyle(GojiColor.textSecondary)

                GlassCard {
                    VStack(alignment: .leading, spacing: 12) {
                        Text(L("privacy.collected"))
                            .font(GojiFont.section())
                            .foregroundStyle(GojiColor.textPrimary)
                        ForEach(collected.indices, id: \.self) { i in
                            bullet(icon: collected[i].0, text: L(collected[i].1), tint: GojiColor.teal)
                        }
                    }
                }
                GlassCard {
                    VStack(alignment: .leading, spacing: 12) {
                        Text(L("privacy.not_collected"))
                            .font(GojiFont.section())
                            .foregroundStyle(GojiColor.textPrimary)
                        ForEach(notCollected.indices, id: \.self) { i in
                            bullet(icon: notCollected[i].0, text: L(notCollected[i].1), tint: GojiColor.terracotta)
                        }
                    }
                }
                Text(L("privacy.vpn_permission"))
                    .font(GojiFont.caption())
                    .foregroundStyle(GojiColor.textSecondary)

                Button(L("privacy.read_policy")) { openURL(AppConstants.privacyPolicyURL) }
                    .buttonStyle(SecondaryButtonStyle())
                Button(L("privacy.accept")) { model.acceptPrivacy() }
                    .buttonStyle(PrimaryButtonStyle())
                    .padding(.bottom, 24)
            }
            .padding(.horizontal, GojiMetrics.screenPadding)
        }
    }

    private func bullet(icon: String, text: String, tint: Color) -> some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: icon)
                .foregroundStyle(tint)
                .frame(width: 22)
            Text(text)
                .font(GojiFont.body())
                .foregroundStyle(GojiColor.textPrimary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}
