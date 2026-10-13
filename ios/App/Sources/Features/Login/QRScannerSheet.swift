import SwiftUI
import AVFoundation

/// Сканер QR-кода переноса входа. Кадры камеры обрабатываются только на устройстве
/// (AVCaptureMetadataOutput) и никуда не отправляются.
struct QRScannerSheet: View {
    let onCode: (String) -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var denied = false

    var body: some View {
        NavigationStack {
            ZStack {
                Color.black.ignoresSafeArea()
                if denied {
                    VStack(spacing: 16) {
                        Image(systemName: "camera.fill").font(.system(size: 40))
                        Text(L("scanner.denied")).multilineTextAlignment(.center)
                        Button(L("scanner.open_settings")) {
                            if let url = URL(string: UIApplication.openSettingsURLString) { UIApplication.shared.open(url) }
                        }
                        .buttonStyle(PrimaryButtonStyle(tint: .white, textColor: .black))
                    }
                    .foregroundStyle(.white)
                    .padding(32)
                } else {
                    QRCameraView(onCode: onCode)
                        .ignoresSafeArea()
                    RoundedRectangle(cornerRadius: 24)
                        .strokeBorder(.white.opacity(0.9), lineWidth: 3)
                        .frame(width: 240, height: 240)
                        .accessibilityHidden(true)
                    VStack {
                        Spacer()
                        Text(L("scanner.hint"))
                            .font(GojiFont.body())
                            .foregroundStyle(.white)
                            .multilineTextAlignment(.center)
                            .padding(24)
                    }
                }
            }
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button(L("common.close")) { dismiss() }.foregroundStyle(.white)
                }
            }
            .task {
                switch AVCaptureDevice.authorizationStatus(for: .video) {
                case .notDetermined:
                    denied = !(await AVCaptureDevice.requestAccess(for: .video))
                case .authorized:
                    denied = false
                default:
                    denied = true
                }
            }
        }
    }
}

private struct QRCameraView: UIViewControllerRepresentable {
    let onCode: (String) -> Void

    func makeUIViewController(context: Context) -> ScannerController {
        let controller = ScannerController()
        controller.onCode = onCode
        return controller
    }

    func updateUIViewController(_ uiViewController: ScannerController, context: Context) {}
}

final class ScannerController: UIViewController, AVCaptureMetadataOutputObjectsDelegate {
    var onCode: ((String) -> Void)?
    private let session = AVCaptureSession()
    private var preview: AVCaptureVideoPreviewLayer?
    private var delivered = false

    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = .black
        guard let device = AVCaptureDevice.default(for: .video),
              let input = try? AVCaptureDeviceInput(device: device),
              session.canAddInput(input) else { return }
        session.addInput(input)
        let output = AVCaptureMetadataOutput()
        guard session.canAddOutput(output) else { return }
        session.addOutput(output)
        output.setMetadataObjectsDelegate(self, queue: .main)
        output.metadataObjectTypes = [.qr]
        let layer = AVCaptureVideoPreviewLayer(session: session)
        layer.videoGravity = .resizeAspectFill
        view.layer.addSublayer(layer)
        preview = layer
    }

    override func viewDidLayoutSubviews() {
        super.viewDidLayoutSubviews()
        preview?.frame = view.bounds
    }

    override func viewWillAppear(_ animated: Bool) {
        super.viewWillAppear(animated)
        let session = self.session
        DispatchQueue.global(qos: .userInitiated).async { if !session.isRunning { session.startRunning() } }
    }

    override func viewWillDisappear(_ animated: Bool) {
        super.viewWillDisappear(animated)
        let session = self.session
        DispatchQueue.global(qos: .userInitiated).async { if session.isRunning { session.stopRunning() } }
    }

    func metadataOutput(_ output: AVCaptureMetadataOutput, didOutput metadataObjects: [AVMetadataObject], from connection: AVCaptureConnection) {
        guard !delivered,
              let object = metadataObjects.first as? AVMetadataMachineReadableCodeObject,
              let value = object.stringValue else { return }
        delivered = true
        UINotificationFeedbackGenerator().notificationOccurred(.success)
        onCode?(value)
    }
}
