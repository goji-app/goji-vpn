import SwiftUI

struct LoginView: View {
    @EnvironmentObject private var model: AppModel
    @Environment(\.openURL) private var openURL
    @State private var email = ""
    @State private var sending = false
    @State private var error: String?
    @State private var codeSentTo = ""
    @State private var showVerify = false
    @State private var showScanner = false
    @FocusState private var emailFocused: Bool

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                Image("Mascot")
                    .resizable()
                    .scaledToFit()
                    .frame(width: 88, height: 88)
                    .padding(.top, 32)
                    .accessibilityHidden(true)
                Text(L("login.greeting"))
                    .font(GojiFont.title(34))
                    .foregroundStyle(GojiColor.textPrimary)

                GlassCard {
                    VStack(alignment: .leading, spacing: 12) {
                        Text(L("login.email.label"))
                            .font(GojiFont.caption())
                            .foregroundStyle(GojiColor.textSecondary)
                        TextField("name@example.com", text: $email)
                            .textContentType(.emailAddress)
                            .keyboardType(.emailAddress)
                            .textInputAutocapitalization(.never)
                            .autocorrectionDisabled()
                            .submitLabel(.send)
                            .focused($emailFocused)
                            .onSubmit { Task { await send() } }
                            .font(GojiFont.manrope(17, .semibold))
                            .padding(14)
                            .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(GojiColor.chip))
                            .accessibilityLabel(L("login.email.label"))
                        if let error {
                            Text(error)
                                .font(GojiFont.caption())
                                .foregroundStyle(GojiColor.danger)
                        }
                        Button {
                            Task { await send() }
                        } label: {
                            if sending { ProgressView().tint(GojiColor.inkButtonText) } else { Text(L("login.get_code")) }
                        }
                        .buttonStyle(PrimaryButtonStyle())
                        .disabled(sending)
                        Text(L("login.no_account"))
                            .font(GojiFont.caption())
                            .foregroundStyle(GojiColor.textSecondary)
                    }
                }

                Button {
                    showScanner = true
                } label: {
                    Label(L("login.scan_qr"), systemImage: "qrcode.viewfinder")
                }
                .buttonStyle(SecondaryButtonStyle())

                consentText
                    .padding(.bottom, 24)
            }
            .padding(.horizontal, GojiMetrics.screenPadding)
        }
        .scrollDismissesKeyboard(.interactively)
        .navigationDestination(isPresented: $showVerify) {
            VerifyCodeView(email: codeSentTo)
        }
        .onAppear { takeTransferEmail() }
        .onChange(of: model.transferEmail) { _ in takeTransferEmail() }
        .sheet(isPresented: $showScanner) {
            QRScannerSheet { code in
                showScanner = false
                if AuthService.isTransferURI(code) {
                    model.pendingTransfer = code
                } else {
                    model.toast = L("transfer.error.invalid")
                }
            }
        }
    }

    /// Почта из QR-кода переноса (AppModel.confirmTransfer): подставить и сразу отправить код.
    private func takeTransferEmail() {
        guard let transfer = model.transferEmail else { return }
        model.transferEmail = nil
        email = transfer
        Task { await send() }
    }

    /// Ссылки на документы до регистрации (аккаунт создаётся при первом входе по коду).
    private var consentText: some View {
        let terms = "[\(L("login.consent.terms"))](\(AppConstants.termsURL.absoluteString))"
        let privacy = "[\(L("login.consent.privacy"))](\(AppConstants.privacyPolicyURL.absoluteString))"
        let markdown = L("login.consent", terms, privacy)
        return Text((try? AttributedString(markdown: markdown)) ?? AttributedString(markdown))
            .font(GojiFont.caption())
            .foregroundStyle(GojiColor.textSecondary)
            .tint(GojiColor.tealDeep)
    }

    private func send() async {
        let trimmed = email.trimmingCharacters(in: .whitespacesAndNewlines)
        guard trimmed.range(of: "^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", options: .regularExpression) != nil else {
            error = L("login.error.invalid_email")
            return
        }
        error = nil
        sending = true
        defer { sending = false }
        do {
            try await AuthService.shared.sendCode(email: trimmed)
            emailFocused = false
            codeSentTo = trimmed
            showVerify = true
        } catch {
            self.error = error.localizedDescription
        }
    }
}
