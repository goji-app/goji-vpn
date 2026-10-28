import Foundation
import MetricKit

/// Порт Windows CrashReporter (2026-10-26): пользователи логи не собирают. Падения приложения
/// система собирает сама и через MetricKit (iOS 14+) отдаёт их при следующих запусках — сохраняем
/// описание, и AppModel предлагает отправить его в поддержку одним нажатием.
final class CrashReporter: NSObject, MXMetricManagerSubscriber {
    static let shared = CrashReporter()

    private static let pendingKey = "crash.pendingReport"
    private static let maxDiagnostics = 2
    private static let maxReportChars = 4000

    func start() {
        MXMetricManager.shared.add(self)
    }

    func didReceive(_ payloads: [MXDiagnosticPayload]) {
        let crashes = payloads.flatMap { $0.crashDiagnostics ?? [] }
        guard !crashes.isEmpty else { return }
        let text = crashes.prefix(Self.maxDiagnostics)
            .map { String(decoding: $0.jsonRepresentation(), as: UTF8.self) }
            .joined(separator: "\n\n")
        UserDefaults.standard.set(String(text.prefix(Self.maxReportChars)), forKey: Self.pendingKey)
        Task { @MainActor in AppModel.shared.offerCrashReportIfPending() }
    }

    /// Сохранённое описание сбоя (один раз) — null, если сбоев не было.
    func takePendingReport() -> String? {
        let report = UserDefaults.standard.string(forKey: Self.pendingKey)
        UserDefaults.standard.removeObject(forKey: Self.pendingKey)
        return report
    }
}
