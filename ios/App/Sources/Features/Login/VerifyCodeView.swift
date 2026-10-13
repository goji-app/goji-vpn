import SwiftUI

struct VerifyCodeView: View {
    let email: String
    @EnvironmentObject private var model: AppModel
    @State private var code = ""
    @State private var checking = false
    @State private var error: String?
    @State private var resendIn = 60
    @FocusState private var focused: Bool

    private let length = 6

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                Text(L("verify.title"))
                    .font(GojiFont.title(32))
                    .foregroundStyle(GojiColor.textPrimary)
                    .padding(.top, 24)
                Text(L("verify.sent_to", email))
                    .font(GojiFont.body())
                    .foregroundStyle(GojiColor.textSecondary)

                ZStack {
                    // Невидимое поле принимает ввод и автоподстановку кода из «Почты».
                    TextField("", text: $code)
                        .keyboardType(.numberPad)
                        .textContentType(.oneTimeCode)
                        .focused($focused)
                        .foregroundStyle(.clear)
                        .tint(.clear)
                        .accessibilityLabel(L("verify.code_label"))
                        .onChange(of: code) { value in
                            let digits = String(value.filter(\.isNumber).prefix(length))
                            if digits != value { code = digits }
                            if digits.count == length { Task { await verify() } }
                        }
                    HStack(spacing: 8) {
                        ForEach(0..<length, id: \.self) { index in
                            let chars = Array(code)
                            let char = index < chars.count ? String(chars[index]) : ""
                            Text(char)
                                .font(GojiFont.manrope(26, .bold))
                                .foregroundStyle(GojiColor.textPrimary)
                                .frame(maxWidth: .infinity, minHeight: 58)
                                .background(GlassSurface(radius: 14, highlighted: index == chars.count && focused))
                        }
                    }
                    .allowsHitTesting(false)
                    .accessibilityHidden(true)
                }
                .contentShape(Rectangle())
                .onTapGesture { focused = true }

                if checking {
                    HStack(spacing: 8) {
                        ProgressView()
                        Text(L("verify.checking")).font(GojiFont.caption())
                    }
                    .foregroundStyle(GojiColor.textSecondary)
                } else if let error {
                    Text(error)
                        .font(GojiFont.caption())
                        .foregroundStyle(GojiColor.danger)
                } else {
                    Text(L("verify.auto_hint"))
                        .font(GojiFont.caption())
                        .foregroundStyle(GojiColor.textSecondary)
                }

                Button(resendIn > 0 ? L("verify.resend_in", resendIn) : L("verify.resend")) {
                    Task { await resend() }
                }
                .buttonStyle(SecondaryButtonStyle())
                .disabled(resendIn > 0)
            }
            .padding(.horizontal, GojiMetrics.screenPadding)
        }
        .onAppear { focused = true }
        .task {
            while resendIn > 0 {
                try? await Task.sleep(nanoseconds: 1_000_000_000)
                resendIn -= 1
            }
        }
    }

    private func verify() async {
        guard !checking else { return }
        checking = true
        error = nil
        defer { checking = false }
        do {
            try await AuthService.shared.verify(email: email, code: code)
            Haptics.success()
            model.didLogin()
        } catch {
            Haptics.error()
            self.error = (error as? APIError).map { _ in L("verify.error.wrong_code") } ?? error.localizedDescription
            code = ""
        }
    }

    private func resend() async {
        do {
            try await AuthService.shared.sendCode(email: email)
            resendIn = 60
            Task {
                while resendIn > 0 {
                    try? await Task.sleep(nanoseconds: 1_000_000_000)
                    resendIn -= 1
                }
            }
        } catch {
            self.error = error.localizedDescription
        }
    }
}
