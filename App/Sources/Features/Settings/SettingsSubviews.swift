import SwiftUI

// MARK: - Автоподключение и доверенные Wi-Fi

/// Правила работают через системный VPN On Demand — и при закрытом приложении. Сопоставление
/// SSID делает сама iOS, приложению не нужен доступ к геолокации.
struct AutoConnectView: View {
    @EnvironmentObject private var settings: AppSettings
    @EnvironmentObject private var vpn: VPNController
    @State private var newSSID = ""

    var body: some View {
        SubScreen(title: L("settings.autoconnect")) {
            Text(L("autoconnect.lead"))
                .font(GojiFont.caption())
                .foregroundStyle(GojiColor.textSecondary)
            VStack(spacing: 10) {
                ForEach(AutoConnectMode.allCases) { mode in
                    Button {
                        settings.autoConnect = mode
                        apply()
                    } label: {
                        HStack(alignment: .top, spacing: 12) {
                            Image(systemName: settings.autoConnect == mode ? "largecircle.fill.circle" : "circle")
                                .foregroundStyle(GojiColor.teal)
                            VStack(alignment: .leading, spacing: 2) {
                                Text(mode.title).font(GojiFont.manrope(14, .semibold)).foregroundStyle(GojiColor.textPrimary)
                                Text(mode.subtitle).font(GojiFont.caption()).foregroundStyle(GojiColor.textSecondary)
                            }
                            Spacer()
                        }
                        .padding(14)
                        .background(GlassSurface(radius: 18, highlighted: settings.autoConnect == mode))
                    }
                    .buttonStyle(.plain)
                    .accessibilityAddTraits(settings.autoConnect == mode ? .isSelected : [])
                }
            }

            SectionHeader(title: L("wifi.trusted.title"))
            Text(L("wifi.trusted.desc"))
                .font(GojiFont.caption())
                .foregroundStyle(GojiColor.textSecondary)
            GlassCard {
                VStack(alignment: .leading, spacing: 10) {
                    Toggle(isOn: Binding(get: { settings.disconnectOnTrusted }, set: { settings.disconnectOnTrusted = $0; apply() })) {
                        Text(L("wifi.trusted.disconnect")).font(GojiFont.manrope(14, .semibold)).foregroundStyle(GojiColor.textPrimary)
                    }
                    .toggleStyle(SwitchToggleStyle(tint: GojiColor.teal))
                    HStack {
                        TextField(L("wifi.trusted.placeholder"), text: $newSSID)
                            .textInputAutocapitalization(.never)
                            .autocorrectionDisabled()
                            .padding(10)
                            .background(RoundedRectangle(cornerRadius: 12).fill(GojiColor.chip))
                        Button(L("common.add")) {
                            let ssid = newSSID.trimmingCharacters(in: .whitespaces)
                            guard !ssid.isEmpty, !settings.trustedSSIDs.contains(ssid) else { return }
                            settings.trustedSSIDs.append(ssid)
                            newSSID = ""
                            apply()
                        }
                        .font(GojiFont.manrope(14, .bold))
                        .disabled(newSSID.trimmingCharacters(in: .whitespaces).isEmpty)
                    }
                    ForEach(settings.trustedSSIDs, id: \.self) { ssid in
                        HStack {
                            Image(systemName: "wifi").foregroundStyle(GojiColor.teal)
                            Text(ssid).font(GojiFont.body()).foregroundStyle(GojiColor.textPrimary)
                            Spacer()
                            Button {
                                settings.trustedSSIDs.removeAll { $0 == ssid }
                                apply()
                            } label: {
                                Image(systemName: "xmark.circle.fill").foregroundStyle(GojiColor.textMuted)
                            }
                            .accessibilityLabel(L("common.delete"))
                        }
                    }
                }
            }
            Text(L("autoconnect.manual_note"))
                .font(GojiFont.caption())
                .foregroundStyle(GojiColor.textMuted)
        }
    }

    private func apply() {
        Task { await vpn.applySettings(reloadCore: false) }
    }
}

// MARK: - Сайты мимо VPN

struct BypassDomainsView: View {
    @EnvironmentObject private var settings: AppSettings
    @EnvironmentObject private var vpn: VPNController
    @State private var input = ""
    @State private var error: String?

    var body: some View {
        SubScreen(title: L("bypass.title")) {
            Text(L("bypass.lead"))
                .font(GojiFont.caption())
                .foregroundStyle(GojiColor.textSecondary)
            GlassCard {
                VStack(alignment: .leading, spacing: 10) {
                    HStack {
                        TextField("example.com", text: $input)
                            .keyboardType(.URL)
                            .textInputAutocapitalization(.never)
                            .autocorrectionDisabled()
                            .onSubmit(add)
                            .padding(10)
                            .background(RoundedRectangle(cornerRadius: 12).fill(GojiColor.chip))
                        Button(L("common.add"), action: add)
                            .font(GojiFont.manrope(14, .bold))
                    }
                    if let error {
                        Text(error).font(GojiFont.caption()).foregroundStyle(GojiColor.danger)
                    }
                    if settings.bypassDomains.isEmpty {
                        Text(L("bypass.empty")).font(GojiFont.caption()).foregroundStyle(GojiColor.textSecondary)
                    }
                    ForEach(settings.bypassDomains, id: \.self) { domain in
                        HStack {
                            Text(domain).font(GojiFont.manrope(14, .medium).monospaced()).foregroundStyle(GojiColor.textPrimary)
                            Spacer()
                            Button {
                                settings.bypassDomains.removeAll { $0 == domain }
                                reload()
                            } label: {
                                Image(systemName: "xmark.circle.fill").foregroundStyle(GojiColor.textMuted)
                            }
                            .accessibilityLabel(L("common.delete"))
                        }
                    }
                }
            }
        }
    }

    private func add() {
        guard let domain = AppSettings.normalizeDomain(input) else {
            error = L("bypass.error.invalid")
            return
        }
        error = nil
        if !settings.bypassDomains.contains(domain) { settings.bypassDomains.append(domain) }
        input = ""
        reload()
    }

    private func reload() {
        Task { await vpn.applySettings(reloadCore: true) }
    }
}

// MARK: - Пинг

struct PingSettingsView: View {
    @EnvironmentObject private var settings: AppSettings

    var body: some View {
        SubScreen(title: L("ping.title")) {
            VStack(spacing: 10) {
                ForEach(PingMethod.allCases) { method in
                    Button { settings.pingMethod = method } label: {
                        HStack {
                            Image(systemName: settings.pingMethod == method ? "largecircle.fill.circle" : "circle")
                                .foregroundStyle(GojiColor.teal)
                            VStack(alignment: .leading, spacing: 2) {
                                Text(method.title).font(GojiFont.manrope(14, .semibold)).foregroundStyle(GojiColor.textPrimary)
                                Text(L("ping.method.\(method.rawValue).desc")).font(GojiFont.caption()).foregroundStyle(GojiColor.textSecondary)
                            }
                            Spacer()
                        }
                        .padding(14)
                        .background(GlassSurface(radius: 18, highlighted: settings.pingMethod == method))
                    }
                    .buttonStyle(.plain)
                }
            }
            SectionHeader(title: L("ping.url"))
            TextField(AppConstants.defaultPingURL, text: $settings.pingURL)
                .keyboardType(.URL)
                .textInputAutocapitalization(.never)
                .autocorrectionDisabled()
                .padding(12)
                .background(GlassSurface(radius: 14))
            Button(L("ping.url.reset")) { settings.pingURL = AppConstants.defaultPingURL }
                .font(GojiFont.manrope(13, .semibold))
        }
    }
}

// MARK: - Проверка утечек

struct LeakCheckView: View {
    @State private var report: LeakChecker.Report?
    @State private var checking = false

    var body: some View {
        SubScreen(title: L("leak.title")) {
            Text(L("leak.caption"))
                .font(GojiFont.caption())
                .foregroundStyle(GojiColor.textSecondary)
            Button {
                Task {
                    checking = true
                    report = await LeakChecker.check()
                    checking = false
                }
            } label: {
                if checking { ProgressView().tint(GojiColor.inkButtonText) } else { Text(L("leak.check")) }
            }
            .buttonStyle(PrimaryButtonStyle())
            .disabled(checking)

            if let report {
                GlassCard {
                    VStack(alignment: .leading, spacing: 10) {
                        verdict(report.verdict)
                        if let vpnIP = report.vpnIP { row(L("leak.vpn_ip"), vpnIP) }
                        if let real = report.realIP { row(L("leak.real_ip"), real) }
                        if let dns = report.dns { row(L("leak.dns"), dns) }
                        if report.ipLeak { Text(L("leak.ip_leak")).font(GojiFont.caption()).foregroundStyle(GojiColor.danger) }
                        if report.dnsLeak { Text(L("leak.dns_leak")).font(GojiFont.caption()).foregroundStyle(GojiColor.danger) }
                    }
                }
            }
        }
    }

    private func verdict(_ v: LeakChecker.Verdict) -> some View {
        let (icon, text, color): (String, String, Color) = {
            switch v {
            case .safe: return ("checkmark.shield.fill", L("leak.verdict.safe"), GojiColor.tealDeep)
            case .leak: return ("exclamationmark.shield.fill", L("leak.verdict.leak"), GojiColor.danger)
            case .vpnOff: return ("shield.slash", L("leak.verdict.off"), GojiColor.warning)
            case .error: return ("wifi.exclamationmark", L("leak.verdict.error"), GojiColor.warning)
            }
        }()
        return Label(text, systemImage: icon)
            .font(GojiFont.manrope(15, .bold))
            .foregroundStyle(color)
    }

    private func row(_ title: String, _ info: LeakChecker.IPInfo) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(title).font(GojiFont.caption()).foregroundStyle(GojiColor.textSecondary)
            Text(info.ip).font(GojiFont.manrope(15, .semibold).monospaced()).foregroundStyle(GojiColor.textPrimary)
            Text([info.country, info.isp].compactMap { $0 }.joined(separator: " · "))
                .font(GojiFont.caption())
                .foregroundStyle(GojiColor.textSecondary)
        }
        .accessibilityElement(children: .combine)
    }
}

// MARK: - Журналы

struct LogsView: View {
    @EnvironmentObject private var settings: AppSettings
    @State private var category: LogCategory = .main
    @State private var text = ""

    var body: some View {
        SubScreen(title: L("logs.title")) {
            Text(L("logs.lead"))
                .font(GojiFont.caption())
                .foregroundStyle(GojiColor.textSecondary)
            HStack {
                Text(L("log.level")).font(GojiFont.manrope(14, .semibold)).foregroundStyle(GojiColor.textPrimary)
                Spacer()
                Picker(L("log.level"), selection: $settings.logLevel) {
                    ForEach(LogLevel.allCases) { level in Text(L("log.level.\(level.rawValue)")).tag(level) }
                }
                .pickerStyle(.menu)
            }
            .padding(12)
            .background(GlassSurface(radius: 16))
            PillSegmented(options: LogCategory.allCases, selection: $category) { L("log.cat.\($0.rawValue)") }
            HStack(spacing: 10) {
                ShareLink(item: AppLog.exportAll()) {
                    Label(L("logs.share"), systemImage: "square.and.arrow.up")
                }
                .buttonStyle(SecondaryButtonStyle())
                Button {
                    AppLog.clear(category)
                    load()
                } label: {
                    Label(L("logs.clear"), systemImage: "trash")
                }
                .buttonStyle(SecondaryButtonStyle())
            }
            Text(text.isEmpty ? L("logs.empty") : text)
                .font(.system(size: 11, design: .monospaced))
                .foregroundStyle(GojiColor.textPrimary)
                .textSelection(.enabled)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(12)
                .background(GlassSurface(radius: 16))
        }
        .onAppear(perform: load)
        .onChange(of: category) { _ in load() }
    }

    private func load() {
        var content = AppLog.read(category)
        if category == .core {
            let xrayLog = (try? String(contentsOf: SharedStorage.logsDir.appendingPathComponent("xray-error.log"), encoding: .utf8)) ?? ""
            if !xrayLog.isEmpty { content += "\n===== xray-error.log =====\n" + String(xrayLog.suffix(8000)) }
        }
        // Последние ~40 КБ — длинный Text в SwiftUI тормозит.
        text = String(content.suffix(40_000))
    }
}

// MARK: - О программе

struct AboutView: View {
    @EnvironmentObject private var model: AppModel
    @State private var xrayVersion = "…"

    var body: some View {
        SubScreen(title: L("about.title")) {
            GlassCard {
                VStack(alignment: .leading, spacing: 12) {
                    item(L("about.version"), "\(AppConstants.appVersion) (\(AppConstants.buildNumber))")
                    item(L("about.xray"), xrayVersion)
                    item("HWID", DeviceID.hwid)
                    item(L("about.device"), "\(DeviceModel.identifier) · iOS \(UIDevice.current.systemVersion)")
                }
            }
            Text(L("about.copy_hint")).font(GojiFont.caption()).foregroundStyle(GojiColor.textMuted)
            SectionHeader(title: L("about.licenses"))
            Text(L("about.licenses.text"))
                .font(GojiFont.caption())
                .foregroundStyle(GojiColor.textSecondary)
        }
        .task {
            let version = await Task.detached { XrayCore.version() }.value
            xrayVersion = version.map { "Xray-core \($0)" } ?? "—"
        }
    }

    private func item(_ title: String, _ value: String) -> some View {
        Button {
            UIPasteboard.general.string = value
            model.toast = L("common.copied")
        } label: {
            VStack(alignment: .leading, spacing: 2) {
                Text(title).font(GojiFont.caption()).foregroundStyle(GojiColor.textSecondary)
                Text(value).font(GojiFont.manrope(14, .semibold)).foregroundStyle(GojiColor.textPrimary)
                    .textSelection(.enabled)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .buttonStyle(.plain)
    }
}

// MARK: - Удаление аккаунта (правило App Store 5.1.1(v))

struct DeleteAccountView: View {
    @EnvironmentObject private var model: AppModel
    @State private var reason = ""
    @State private var confirm = false
    @State private var sending = false
    @State private var done = false

    var body: some View {
        SubScreen(title: L("account.delete.title")) {
            if done {
                MessageCard(icon: "checkmark.circle", text: L("account.delete.done"))
            } else {
                Text(L("account.delete.lead"))
                    .font(GojiFont.body())
                    .foregroundStyle(GojiColor.textPrimary)
                Text(L("account.delete.what"))
                    .font(GojiFont.caption())
                    .foregroundStyle(GojiColor.textSecondary)
                TextField(L("account.delete.reason"), text: $reason, axis: .vertical)
                    .lineLimit(2...5)
                    .padding(12)
                    .background(GlassSurface(radius: 14))
                Button(role: .destructive) { confirm = true } label: {
                    if sending { ProgressView() } else { Text(L("account.delete.button")) }
                }
                .buttonStyle(PrimaryButtonStyle(tint: GojiColor.danger, textColor: .white))
                .disabled(sending)
            }
        }
        .confirmationDialog(L("account.delete.confirm"), isPresented: $confirm, titleVisibility: .visible) {
            Button(L("account.delete.button"), role: .destructive) { Task { await submit() } }
            Button(L("common.cancel"), role: .cancel) {}
        }
    }

    private func submit() async {
        sending = true
        defer { sending = false }
        do {
            try await AuthService.shared.requestAccountDeletion(reason: reason)
            done = true
            try? await Task.sleep(nanoseconds: 2_500_000_000)
            await model.logout()
        } catch {
            model.toast = error.localizedDescription
        }
    }
}
