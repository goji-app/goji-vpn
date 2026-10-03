import SwiftUI
import CoreImage.CIFilterBuiltins

@MainActor
final class AccountViewModel: ObservableObject {
    @Published var devices: [DeviceInfo] = []
    @Published var devicesError: String?
    @Published var referrals: ReferralsResponse?
    @Published var loading = false

    private let api = APIClient.shared

    func load(subscriptionId: Int64?) async {
        guard !DemoMode.isEnabled else { return }
        loading = true
        defer { loading = false }
        if let id = subscriptionId {
            do {
                devices = try await api.get("api/subscriptions/\(id)/devices")
                devicesError = nil
            } catch {
                devicesError = error.localizedDescription
            }
        }
        referrals = try? await api.get("api/dashboard/referrals")
    }

    func rename(_ device: DeviceInfo, to name: String, subscriptionId: Int64) async throws {
        let hwid = device.hwid.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed) ?? device.hwid
        try await api.patchNoContent("api/subscriptions/\(subscriptionId)/devices/\(hwid)", body: RenameDeviceRequest(readableName: name))
        await load(subscriptionId: subscriptionId)
    }

    func delete(_ device: DeviceInfo, subscriptionId: Int64) async throws {
        let hwid = device.hwid.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed) ?? device.hwid
        try await api.delete("api/subscriptions/\(subscriptionId)/devices/\(hwid)")
        await load(subscriptionId: subscriptionId)
    }
}

struct SubscriptionView: View {
    @EnvironmentObject private var store: SubscriptionStore
    @EnvironmentObject private var model: AppModel
    @StateObject private var account = AccountViewModel()
    @State private var renaming: DeviceInfo?
    @State private var newName = ""
    @State private var deleting: DeviceInfo?
    @State private var showTransfer = false
    @State private var showSupport = false

    var body: some View {
        ScreenScaffold(title: L("sub.title"), subtitle: L("sub.subtitle")) {
            planCard
            if let sub = store.subscription { devicesSection(sub) }
            referralSection
            newsSection
            SectionHeader(title: L("sub.more"))
            VStack(spacing: 10) {
                Button { showTransfer = true } label: {
                    RowLabel(icon: "qrcode", title: L("transfer.title"), subtitle: L("transfer.subtitle"))
                }
                .buttonStyle(.plain)
                NavigationLink { SupportListView() } label: {
                    RowLabel(icon: "bubble.left.and.bubble.right", title: L("support.title"), subtitle: L("sub.support_text"), tint: GojiColor.terracotta)
                }
                .buttonStyle(.plain)
            }
            .padding(14)
            .background(GlassSurface())
            if let uuid = store.clientUUID {
                Button {
                    UIPasteboard.general.string = uuid
                    model.toast = L("common.copied")
                } label: {
                    Text("ID: \(uuid)")
                        .font(GojiFont.manrope(11, .medium, relativeTo: .caption2).monospaced())
                        .foregroundStyle(GojiColor.textMuted)
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.plain)
                .accessibilityHint(L("common.tap_to_copy"))
            }
        }
        .refreshable {
            await store.refresh()
            await account.load(subscriptionId: store.subscription?.id)
        }
        .toolbar(.hidden, for: .navigationBar)
        .task(id: store.subscription?.id) { await account.load(subscriptionId: store.subscription?.id) }
        .sheet(isPresented: $showTransfer) { TransferQRSheet() }
        .navigationDestination(isPresented: $showSupport) { SupportListView() }
        .alert(L("devices.rename.title"), isPresented: Binding(get: { renaming != nil }, set: { if !$0 { renaming = nil } })) {
            TextField(L("devices.rename.label"), text: $newName)
            Button(L("common.save")) {
                guard let device = renaming, let id = store.subscription?.id else { return }
                let name = newName.trimmingCharacters(in: .whitespaces)
                Task {
                    do { try await account.rename(device, to: name, subscriptionId: id) } catch { model.toast = error.localizedDescription }
                }
            }
            Button(L("common.cancel"), role: .cancel) {}
        }
        .alert(L("devices.delete.title"), isPresented: Binding(get: { deleting != nil }, set: { if !$0 { deleting = nil } })) {
            if store.subscription?.isTrialOrFree == true {
                Button(L("devices.contact_support")) { showSupport = true }
                Button(L("common.close"), role: .cancel) {}
            } else {
                Button(L("devices.delete"), role: .destructive) {
                    guard let device = deleting, let id = store.subscription?.id else { return }
                    Task {
                        do { try await account.delete(device, subscriptionId: id) } catch { model.toast = error.localizedDescription }
                    }
                }
                Button(L("common.cancel"), role: .cancel) {}
            }
        } message: {
            Text(store.subscription?.isTrialOrFree == true ? L("devices.delete.support_only") : L("devices.delete.confirm"))
        }
    }

    // MARK: - Тариф

    @ViewBuilder
    private var planCard: some View {
        if let sub = store.subscription {
            GlassCard(highlighted: true) {
                VStack(alignment: .leading, spacing: 10) {
                    HStack {
                        VStack(alignment: .leading, spacing: 2) {
                            Text(L("sub.plan"))
                                .font(GojiFont.caption())
                                .foregroundStyle(GojiColor.textSecondary)
                            Text(sub.planName ?? sub.name ?? "—")
                                .font(GojiFont.title(26))
                                .foregroundStyle(GojiColor.textPrimary)
                        }
                        Spacer()
                        if sub.isActive == true {
                            Badge(text: L("sub.active"))
                        } else {
                            Badge(text: L("sub.inactive"), color: GojiColor.danger, background: GojiColor.jamBg)
                        }
                    }
                    HStack(alignment: .firstTextBaseline, spacing: 6) {
                        Text("\(sub.daysLeft ?? 0)")
                            .font(GojiFont.manrope(40, .extraBold, relativeTo: .largeTitle))
                            .foregroundStyle(GojiColor.tealDeep)
                        Text(Plural.form(sub.daysLeft ?? 0, "days.word"))
                            .font(GojiFont.body())
                            .foregroundStyle(GojiColor.textSecondary)
                    }
                    if let expire = sub.expireDate {
                        Text(L("sub.until", DateParsing.shortDate(expire)))
                            .font(GojiFont.caption())
                            .foregroundStyle(GojiColor.textSecondary)
                    }
                    if let limit = sub.deviceLimit {
                        Label(L("sub.devices_limit", limit), systemImage: "iphone.gen3")
                            .font(GojiFont.caption())
                            .foregroundStyle(GojiColor.textSecondary)
                    }
                    Text(L("sub.manage_note"))
                        .font(GojiFont.caption())
                        .foregroundStyle(GojiColor.textMuted)
                }
            }
        } else if store.subscriptionLoaded {
            NoSubscriptionCard()
        } else {
            ProgressView().frame(maxWidth: .infinity).padding(30)
        }
    }

    // MARK: - Устройства

    private func devicesSection(_ sub: SubscriptionInfo) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            SectionHeader(title: L("devices.title"), trailing: AnyView(
                Text(L("devices.count", account.devices.count, sub.deviceLimit ?? 0))
                    .font(GojiFont.caption())
                    .foregroundStyle(GojiColor.textSecondary)
            ))
            VStack(spacing: 0) {
                if let error = account.devicesError, account.devices.isEmpty {
                    Text(error).font(GojiFont.caption()).foregroundStyle(GojiColor.danger).padding(.vertical, 8)
                } else if account.devices.isEmpty {
                    Text(L("devices.empty")).font(GojiFont.caption()).foregroundStyle(GojiColor.textSecondary).padding(.vertical, 8)
                }
                ForEach(Array(account.devices.enumerated()), id: \.element.id) { index, device in
                    if index > 0 { Divider().overlay(GojiColor.hair) }
                    HStack(spacing: 12) {
                        Image(systemName: icon(for: device))
                            .foregroundStyle(GojiColor.teal)
                            .frame(width: 32, height: 32)
                            .background(Circle().fill(GojiColor.chip))
                        VStack(alignment: .leading, spacing: 2) {
                            HStack(spacing: 6) {
                                Text(device.readableName ?? device.platform ?? device.hwid.prefix(8).description)
                                    .font(GojiFont.manrope(14, .semibold))
                                    .foregroundStyle(GojiColor.textPrimary)
                                    .lineLimit(1)
                                if device.hwid == DeviceID.hwid {
                                    Badge(text: L("devices.this"))
                                }
                            }
                            Text([device.platform, DateParsing.dateTime(device.createdAt)].compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " · "))
                                .font(GojiFont.caption())
                                .foregroundStyle(GojiColor.textSecondary)
                        }
                        Spacer()
                        Menu {
                            Button { newName = device.readableName ?? ""; renaming = device } label: {
                                Label(L("devices.rename"), systemImage: "pencil")
                            }
                            Button(role: .destructive) { deleting = device } label: {
                                Label(L("devices.delete"), systemImage: "trash")
                            }
                        } label: {
                            Image(systemName: "ellipsis")
                                .foregroundStyle(GojiColor.textSecondary)
                                .frame(width: 36, height: 36)
                        }
                        .accessibilityLabel(L("devices.actions"))
                    }
                    .padding(.vertical, 8)
                }
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 6)
            .background(GlassSurface())
        }
    }

    private func icon(for device: DeviceInfo) -> String {
        let p = (device.platform ?? device.userAgent ?? "").lowercased()
        if p.contains("ios") || p.contains("iphone") { return "iphone" }
        if p.contains("android") { return "candybarphone" }
        if p.contains("windows") || p.contains("mac") || p.contains("linux") { return "laptopcomputer" }
        return "desktopcomputer"
    }

    // MARK: - Рефералы

    @ViewBuilder
    private var referralSection: some View {
        if let ref = account.referrals, let link = ref.link, !link.isEmpty {
            SectionHeader(title: L("referral.title"))
            GlassCard {
                VStack(alignment: .leading, spacing: 12) {
                    if let description = ref.description, !description.isEmpty {
                        Text(RichText.attributed(description))
                            .font(GojiFont.caption())
                            .foregroundStyle(GojiColor.textSecondary)
                    }
                    HStack(spacing: 10) {
                        stat(L("referral.invited"), ref.summary?.totalReferrals ?? 0)
                        stat(L("referral.active"), ref.summary?.activeReferrals ?? 0)
                        stat(L("referral.bonus_days"), ref.summary?.totalBonusDays ?? 0)
                    }
                    Text(link)
                        .font(GojiFont.manrope(12, .medium).monospaced())
                        .foregroundStyle(GojiColor.textPrimary)
                        .lineLimit(1)
                        .truncationMode(.middle)
                        .padding(10)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .background(RoundedRectangle(cornerRadius: 12).fill(GojiColor.chip))
                    HStack(spacing: 10) {
                        Button {
                            UIPasteboard.general.string = link
                            model.toast = L("common.copied")
                        } label: {
                            Label(L("referral.copy"), systemImage: "doc.on.doc")
                        }
                        .buttonStyle(SecondaryButtonStyle())
                        if let url = URL(string: link) {
                            ShareLink(item: url) {
                                Label(L("referral.share"), systemImage: "square.and.arrow.up")
                            }
                            .buttonStyle(SecondaryButtonStyle())
                        }
                    }
                    if let list = ref.referrals, !list.isEmpty {
                        Divider().overlay(GojiColor.hair)
                        ForEach(list.prefix(20)) { entry in
                            HStack {
                                Text(entry.displayName)
                                    .font(GojiFont.manrope(13, .semibold))
                                    .foregroundStyle(GojiColor.textPrimary)
                                Spacer()
                                if let days = entry.bonusDays, days > 0 {
                                    Text(L("referral.bonus_suffix", days))
                                        .font(GojiFont.caption())
                                        .foregroundStyle(GojiColor.tealDeep)
                                }
                                Badge(
                                    text: entry.isActive == true ? L("referral.badge.active") : L("referral.badge.inactive"),
                                    color: entry.isActive == true ? GojiColor.tealDeep : GojiColor.textSecondary,
                                    background: entry.isActive == true ? GojiColor.tealTint : GojiColor.chip
                                )
                            }
                        }
                    }
                }
            }
        }
    }

    private func stat(_ title: String, _ value: Int) -> some View {
        VStack(spacing: 2) {
            Text("\(value)")
                .font(GojiFont.manrope(20, .bold))
                .foregroundStyle(GojiColor.textPrimary)
            Text(title)
                .font(GojiFont.manrope(10.5, .medium, relativeTo: .caption2))
                .foregroundStyle(GojiColor.textSecondary)
                .multilineTextAlignment(.center)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 8)
        .background(RoundedRectangle(cornerRadius: 14).fill(GojiColor.chip))
        .accessibilityElement(children: .combine)
    }

    // MARK: - Новости

    @ViewBuilder
    private var newsSection: some View {
        if !store.broadcasts.isEmpty {
            SectionHeader(title: L("news.title"))
            VStack(spacing: 10) {
                ForEach(store.broadcasts.prefix(5)) { item in
                    NewsCard(item: item)
                }
            }
        }
    }
}

struct NewsCard: View {
    let item: Broadcast
    @State private var expanded = false
    @Environment(\.openURL) private var openURL

    var body: some View {
        GlassCard(padding: 14) {
            VStack(alignment: .leading, spacing: 8) {
                if let date = item.createdAt {
                    Text(DateParsing.dateTime(date))
                        .font(GojiFont.caption())
                        .foregroundStyle(GojiColor.textMuted)
                }
                Text(RichText.attributed(item.content))
                    .font(GojiFont.body())
                    .foregroundStyle(GojiColor.textPrimary)
                    .lineLimit(expanded ? nil : 4)
                    .tint(GojiColor.tealDeep)
                Button(expanded ? L("news.collapse") : L("news.read_more")) {
                    withAnimation { expanded.toggle() }
                }
                .font(GojiFont.manrope(12, .bold))
                .foregroundStyle(GojiColor.tealDeep)
                // Кнопки рассылок — только информационные ссылки; ссылки на оплату в iOS
                // не показываем (правило 3.1.1).
                ForEach(item.buttons.filter { !LinkPolicy.isPurchaseLink($0.url) }, id: \.self) { button in
                    if let url = URL(string: button.url) {
                        Button(button.text) { openURL(url) }
                            .buttonStyle(SecondaryButtonStyle())
                    }
                }
            }
        }
    }
}

/// Ссылки, ведущие к оплате вне App Store, в iOS-версии не показываем.
enum LinkPolicy {
    static func isPurchaseLink(_ raw: String) -> Bool {
        let lower = raw.lowercased()
        let markers = ["pay", "checkout", "plans", "tariff", "тариф", "оплат", "renew", "продл", "t.me/", "telegram.me", "gojihub.xyz/#", "subscribe", "billing", "promo"]
        return markers.contains { lower.contains($0) }
    }
}

/// QR-код текущей сессии для входа на другом своём устройстве (живёт 10 минут).
struct TransferQRSheet: View {
    @Environment(\.dismiss) private var dismiss
    @State private var uri = AuthService.shared.transferURI()
    @State private var generatedAt = Date()

    var body: some View {
        NavigationStack {
            VStack(spacing: 20) {
                Text(L("transfer.sheet.text"))
                    .font(GojiFont.body())
                    .foregroundStyle(GojiColor.textSecondary)
                    .multilineTextAlignment(.center)
                if let current = uri, let image = QRCode.image(for: current) {
                    Image(uiImage: image)
                        .interpolation(.none)
                        .resizable()
                        .scaledToFit()
                        .frame(maxWidth: 260)
                        .padding(16)
                        .background(RoundedRectangle(cornerRadius: 20).fill(.white))
                        .accessibilityLabel(L("transfer.qr.a11y"))
                    TimelineView(.periodic(from: .now, by: 1)) { context in
                        let left = max(0, 600 - Int(context.date.timeIntervalSince(generatedAt)))
                        Text(left > 0 ? L("transfer.expires_in", left / 60, left % 60) : L("transfer.expired"))
                            .font(GojiFont.caption().monospacedDigit())
                            .foregroundStyle(left > 0 ? GojiColor.textSecondary : GojiColor.danger)
                    }
                    Button(L("transfer.regenerate")) {
                        uri = AuthService.shared.transferURI()
                        generatedAt = Date()
                    }
                    .buttonStyle(SecondaryButtonStyle())
                } else {
                    Text(L("error.session_expired"))
                }
                Text(L("transfer.warning"))
                    .font(GojiFont.caption())
                    .foregroundStyle(GojiColor.warning)
                    .multilineTextAlignment(.center)
                Spacer()
            }
            .padding(24)
            .navigationTitle(L("transfer.title"))
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) { Button(L("common.done")) { dismiss() } }
            }
        }
        .presentationDetents([.large])
    }
}

enum QRCode {
    static func image(for text: String) -> UIImage? {
        let filter = CIFilter.qrCodeGenerator()
        filter.message = Data(text.utf8)
        filter.correctionLevel = "M"
        guard let output = filter.outputImage?.transformed(by: CGAffineTransform(scaleX: 10, y: 10)),
              let cg = CIContext().createCGImage(output, from: output.extent) else { return nil }
        return UIImage(cgImage: cg)
    }
}
