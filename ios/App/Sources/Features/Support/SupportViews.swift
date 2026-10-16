import SwiftUI
import PhotosUI
import UniformTypeIdentifiers

// MARK: - Список обращений

@MainActor
final class SupportListModel: ObservableObject {
    enum Tab: Hashable { case open, closed }
    @Published var tab: Tab = .open
    @Published var tickets: [SupportTicket] = []
    @Published var loading = false
    @Published var error: String?

    func load() async {
        loading = true
        defer { loading = false }
        do {
            let status = tab == .open ? "open" : "closed"
            let response: SupportTicketsResponse = try await APIClient.shared.get(
                "api/support/tickets",
                query: [URLQueryItem(name: "status", value: status), URLQueryItem(name: "limit", value: "50"), URLQueryItem(name: "offset", value: "0")]
            )
            tickets = response.tickets ?? []
            error = nil
        } catch {
            self.error = error.localizedDescription
        }
    }
}

struct SupportListView: View {
    @StateObject private var model = SupportListModel()

    var body: some View {
        SubScreen(title: L("support.title")) {
            NavigationLink { NewTicketView() } label: {
                Label(L("support.new"), systemImage: "plus.bubble")
                    .font(GojiFont.button())
                    .foregroundStyle(GojiColor.inkButtonText)
                    .frame(maxWidth: .infinity, minHeight: 52)
                    .background(RoundedRectangle(cornerRadius: GojiMetrics.buttonRadius).fill(GojiColor.inkButton))
            }
            .buttonStyle(.plain)
            NavigationLink { FaqView() } label: {
                RowLabel(icon: "questionmark.circle", title: L("faq.entry"))
                    .padding(14)
                    .background(GlassSurface())
            }
            .buttonStyle(.plain)

            PillSegmented(options: [SupportListModel.Tab.open, .closed], selection: $model.tab) {
                $0 == .open ? L("support.tab.open") : L("support.tab.closed")
            }
            if model.loading && model.tickets.isEmpty {
                ProgressView().frame(maxWidth: .infinity).padding(30)
            } else if let error = model.error, model.tickets.isEmpty {
                MessageCard(icon: "wifi.exclamationmark", text: error, actionTitle: L("common.retry")) { Task { await model.load() } }
            } else if model.tickets.isEmpty {
                MessageCard(icon: "tray", text: model.tab == .open ? L("support.empty.open") : L("support.empty.closed"))
            } else {
                ForEach(model.tickets) { ticket in
                    NavigationLink { TicketChatView(ticketId: ticket.id, title: ticket.subject) } label: {
                        TicketRow(ticket: ticket)
                    }
                    .buttonStyle(.plain)
                }
            }
        }
        .task(id: model.tab) { await model.load() }
        .refreshable { await model.load() }
    }
}

struct TicketRow: View {
    let ticket: SupportTicket

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            VStack(alignment: .leading, spacing: 4) {
                Text(ticket.subject.flatMap { $0.isEmpty ? nil : $0 } ?? L("support.ticket_number", ticket.id))
                    .font(GojiFont.manrope(15, .bold))
                    .foregroundStyle(GojiColor.textPrimary)
                    .lineLimit(1)
                if let last = ticket.lastMessage, !last.isEmpty {
                    Text(RichText.plain(last))
                        .font(GojiFont.caption())
                        .foregroundStyle(GojiColor.textSecondary)
                        .lineLimit(2)
                }
                Text(SupportStatus.title(ticket.status))
                    .font(GojiFont.manrope(11, .bold, relativeTo: .caption2))
                    .foregroundStyle(ticket.isClosed ? GojiColor.textMuted : GojiColor.tealDeep)
            }
            Spacer()
            if let unread = ticket.unreadCount, unread > 0 {
                Text("\(unread)")
                    .font(GojiFont.manrope(12, .bold))
                    .foregroundStyle(.white)
                    .padding(.horizontal, 8)
                    .padding(.vertical, 3)
                    .background(Capsule().fill(GojiColor.terracotta))
                    .accessibilityLabel(L("support.unread", unread))
            }
        }
        .padding(14)
        .background(GlassSurface(radius: 18))
    }
}

enum SupportStatus {
    static func title(_ status: String) -> String {
        switch status {
        case "open": return L("support.status.open")
        case "waiting_customer": return L("support.status.waiting_customer")
        case "awaiting_reply": return L("support.status.awaiting_reply")
        case "on_hold": return L("support.status.on_hold")
        case "closed": return L("support.status.closed")
        default: return status
        }
    }
}

// MARK: - Новое обращение

struct NewTicketView: View {
    @Environment(\.dismiss) private var dismiss
    @State private var queues: [SupportQueue] = []
    @State private var queue: SupportQueue?
    @State private var subject = ""
    @State private var message = ""
    @State private var attachLogs = false
    @State private var sending = false
    @State private var error: String?
    @State private var activeTicket: Int64?
    @State private var createdTicket: Int64?
    @State private var openCreated = false

    var body: some View {
        SubScreen(title: L("support.new")) {
            if let active = activeTicket {
                GlassCard {
                    VStack(alignment: .leading, spacing: 10) {
                        Text(L("support.limit.title")).font(GojiFont.section()).foregroundStyle(GojiColor.textPrimary)
                        Text(L("support.limit.text")).font(GojiFont.body()).foregroundStyle(GojiColor.textSecondary)
                        NavigationLink { TicketChatView(ticketId: active, title: nil) } label: {
                            Text(L("support.limit.goto"))
                        }
                        .buttonStyle(PrimaryButtonStyle())
                    }
                }
            } else {
                if !queues.isEmpty {
                    Text(L("support.queue")).font(GojiFont.caption()).foregroundStyle(GojiColor.textSecondary)
                    Picker(L("support.queue"), selection: $queue) {
                        Text(L("support.queue.choose")).tag(SupportQueue?.none)
                        ForEach(queues) { q in Text(q.name).tag(SupportQueue?.some(q)) }
                    }
                    .pickerStyle(.menu)
                    .padding(8)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(GlassSurface(radius: 14))
                }
                TextField(L("support.subject"), text: $subject)
                    .padding(12)
                    .background(GlassSurface(radius: 14))
                TextField(L("support.message.placeholder"), text: $message, axis: .vertical)
                    .lineLimit(5...12)
                    .padding(12)
                    .background(GlassSurface(radius: 14))
                Toggle(L("support.attach_logs"), isOn: $attachLogs)
                    .toggleStyle(SwitchToggleStyle(tint: GojiColor.teal))
                    .font(GojiFont.manrope(14, .semibold))
                if let error {
                    Text(error).font(GojiFont.caption()).foregroundStyle(GojiColor.danger)
                }
                Button {
                    Task { await submit() }
                } label: {
                    if sending { ProgressView().tint(GojiColor.inkButtonText) } else { Text(L("support.send")) }
                }
                .buttonStyle(PrimaryButtonStyle())
                .disabled(sending)
            }
        }
        .navigationDestination(isPresented: $openCreated) {
            if let id = createdTicket { TicketChatView(ticketId: id, title: subject.isEmpty ? nil : subject) }
        }
        .task { await load() }
    }

    private func load() async {
        if let limit: SupportTicketLimit = try? await APIClient.shared.get("api/support/ticket-limit"), !limit.canCreate {
            activeTicket = limit.activeTicketId
        }
        queues = (try? await APIClient.shared.get("api/support/queues") as [SupportQueue]?) ?? []
    }

    private func submit() async {
        let text = message.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else { error = L("support.error.message_required"); return }
        if !queues.isEmpty && queue == nil { error = L("support.error.queue_required"); return }
        error = nil
        sending = true
        defer { sending = false }
        do {
            let request = CreateTicketRequest(subject: subject.isEmpty ? nil : subject, message: text, queueId: queue?.id)
            let response: CreateTicketResponse = try await APIClient.shared.post("api/support/tickets", body: request)
            if attachLogs {
                try? await APIClient.shared.sendMultipart(
                    path: "api/support/tickets/\(response.ticket.id)/messages",
                    fields: ["message": L("support.logs_message")],
                    files: [SupportAttachments.logsFile()]
                )
            }
            createdTicket = response.ticket.id
            openCreated = true
        } catch {
            self.error = error.localizedDescription
        }
    }
}

// MARK: - Переписка

@MainActor
final class TicketChatModel: ObservableObject {
    let ticketId: Int64
    @Published var messages: [SupportMessage] = []
    @Published var ticket: SupportTicket?
    @Published var error: String?
    @Published var sending = false

    init(ticketId: Int64) { self.ticketId = ticketId }

    func load() async {
        do {
            ticket = try await APIClient.shared.get("api/support/tickets/\(ticketId)")
            let list: [SupportMessage]? = try await APIClient.shared.get("api/support/tickets/\(ticketId)/messages")
            messages = list ?? []
            error = nil
        } catch {
            self.error = error.localizedDescription
        }
    }

    func send(_ text: String, files: [APIClient.UploadFile]) async -> Bool {
        sending = true
        defer { sending = false }
        do {
            if files.isEmpty {
                try await APIClient.shared.postNoContent("api/support/tickets/\(ticketId)/messages", body: SendMessageRequest(message: text))
            } else {
                try await APIClient.shared.sendMultipart(path: "api/support/tickets/\(ticketId)/messages", fields: ["message": text], files: files)
            }
            await load()
            return true
        } catch {
            self.error = L("support.chat.send_error")
            return false
        }
    }
}

struct TicketChatView: View {
    let title: String?
    @StateObject private var model: TicketChatModel
    @State private var input = ""
    @State private var attachments: [APIClient.UploadFile] = []
    @State private var photoItems: [PhotosPickerItem] = []
    @State private var showFileImporter = false

    init(ticketId: Int64, title: String?) {
        self.title = title
        _model = StateObject(wrappedValue: TicketChatModel(ticketId: ticketId))
    }

    var body: some View {
        ZStack {
            GlassBackdrop()
            VStack(spacing: 0) {
                ScrollViewReader { proxy in
                    ScrollView {
                        LazyVStack(spacing: 10) {
                            if let error = model.error, model.messages.isEmpty {
                                MessageCard(icon: "wifi.exclamationmark", text: error, actionTitle: L("common.retry")) { Task { await model.load() } }
                            }
                            ForEach(model.messages) { message in
                                MessageBubble(message: message).id(message.id)
                            }
                        }
                        .padding(.horizontal, GojiMetrics.screenPadding)
                        .padding(.vertical, 12)
                    }
                    .scrollDismissesKeyboard(.interactively)
                    .onChange(of: model.messages.count) { _ in
                        if let last = model.messages.last { withAnimation { proxy.scrollTo(last.id, anchor: .bottom) } }
                    }
                }
                if model.ticket?.isClosed == true {
                    Text(L("support.chat.closed"))
                        .font(GojiFont.caption())
                        .foregroundStyle(GojiColor.textSecondary)
                        .padding()
                } else {
                    composer
                }
            }
            .padding(.bottom, 8)
        }
        .navigationTitle(title ?? L("support.title"))
        .navigationBarTitleDisplayMode(.inline)
        .task {
            await model.load()
            while !Task.isCancelled {
                try? await Task.sleep(nanoseconds: 15_000_000_000)
                await model.load()
            }
        }
        .onChange(of: photoItems) { items in
            Task {
                for item in items {
                    if let data = try? await item.loadTransferable(type: Data.self) {
                        let isVideo = item.supportedContentTypes.contains { $0.conforms(to: .movie) }
                        attachments.append(APIClient.UploadFile(
                            name: isVideo ? "video-\(attachments.count + 1).mov" : "photo-\(attachments.count + 1).jpg",
                            mimeType: isVideo ? "video/quicktime" : "image/jpeg",
                            data: isVideo ? data : (UIImage(data: data)?.jpegData(compressionQuality: 0.85) ?? data)
                        ))
                    }
                }
                photoItems = []
            }
        }
        .fileImporter(isPresented: $showFileImporter, allowedContentTypes: [.pdf], allowsMultipleSelection: false) { result in
            guard case .success(let urls) = result, let url = urls.first else { return }
            let access = url.startAccessingSecurityScopedResource()
            defer { if access { url.stopAccessingSecurityScopedResource() } }
            if let data = try? Data(contentsOf: url) {
                attachments.append(APIClient.UploadFile(name: url.lastPathComponent, mimeType: "application/pdf", data: data))
            }
        }
    }

    private var composer: some View {
        VStack(spacing: 8) {
            if !attachments.isEmpty {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack {
                        ForEach(attachments.indices, id: \.self) { index in
                            HStack(spacing: 4) {
                                Image(systemName: "paperclip")
                                Text(attachments[index].name).lineLimit(1)
                                Button { attachments.remove(at: index) } label: { Image(systemName: "xmark.circle.fill") }
                                    .accessibilityLabel(L("common.delete"))
                            }
                            .font(GojiFont.caption())
                            .padding(.horizontal, 10)
                            .padding(.vertical, 6)
                            .background(Capsule().fill(GojiColor.chip))
                        }
                    }
                }
            }
            HStack(alignment: .bottom, spacing: 8) {
                Menu {
                    PhotosPicker(selection: $photoItems, maxSelectionCount: 5, matching: .any(of: [.images, .videos])) {
                        Label(L("support.attach.media"), systemImage: "photo")
                    }
                    Button { showFileImporter = true } label: { Label(L("support.attach.file"), systemImage: "doc") }
                    Button { attachments.append(SupportAttachments.logsFile()) } label: { Label(L("support.attach.logs"), systemImage: "doc.text") }
                } label: {
                    Image(systemName: "paperclip")
                        .font(.system(size: 18, weight: .semibold))
                        .foregroundStyle(GojiColor.textSecondary)
                        .frame(width: 42, height: 42)
                        .background(GlassSurface(radius: 21))
                }
                .accessibilityLabel(L("support.attach"))
                TextField(L("support.chat.placeholder"), text: $input, axis: .vertical)
                    .lineLimit(1...5)
                    .padding(.horizontal, 14)
                    .padding(.vertical, 11)
                    .background(GlassSurface(radius: 21))
                Button {
                    let text = input.trimmingCharacters(in: .whitespacesAndNewlines)
                    let files = attachments
                    Task {
                        if await model.send(text.isEmpty ? "📎" : text, files: files) {
                            input = ""
                            attachments = []
                        }
                    }
                } label: {
                    Group {
                        if model.sending { ProgressView().tint(.white) } else { Image(systemName: "arrow.up").font(.system(size: 17, weight: .bold)) }
                    }
                    .foregroundStyle(.white)
                    .frame(width: 42, height: 42)
                    .background(Circle().fill(GojiColor.accentGradient))
                }
                .disabled(model.sending || (input.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && attachments.isEmpty))
                .accessibilityLabel(L("support.send"))
            }
            if let error = model.error, !model.messages.isEmpty {
                Text(error).font(GojiFont.caption()).foregroundStyle(GojiColor.danger)
            }
        }
        .padding(.horizontal, GojiMetrics.screenPadding)
        .padding(.vertical, 8)
    }
}

struct MessageBubble: View {
    let message: SupportMessage

    var body: some View {
        if message.isEvent {
            Text(RichText.plain(message.message ?? message.eventType ?? ""))
                .font(GojiFont.caption())
                .foregroundStyle(GojiColor.textMuted)
                .multilineTextAlignment(.center)
                .frame(maxWidth: .infinity)
        } else {
            HStack {
                if message.isMine { Spacer(minLength: 40) }
                VStack(alignment: .leading, spacing: 4) {
                    if !message.isMine, let name = message.senderName, !name.isEmpty {
                        Text(name).font(GojiFont.manrope(11, .bold, relativeTo: .caption2)).foregroundStyle(GojiColor.tealDeep)
                    }
                    if let text = message.message, !text.isEmpty {
                        Text(RichText.attributed(text))
                            .font(GojiFont.body())
                            .foregroundStyle(message.isMine ? Color.white : GojiColor.textPrimary)
                            .tint(message.isMine ? .white : GojiColor.tealDeep)
                            .textSelection(.enabled)
                    }
                    ForEach(message.attachments ?? [], id: \.uuid) { attachment in
                        Label(attachment.fileName ?? L("support.attachment"), systemImage: "paperclip")
                            .font(GojiFont.caption())
                            .foregroundStyle(message.isMine ? Color.white.opacity(0.9) : GojiColor.textSecondary)
                    }
                    Text(DateParsing.dateTime(message.createdAt))
                        .font(GojiFont.manrope(10, .medium, relativeTo: .caption2))
                        .foregroundStyle(message.isMine ? Color.white.opacity(0.75) : GojiColor.textMuted)
                }
                .padding(12)
                .background {
                    if message.isMine {
                        RoundedRectangle(cornerRadius: 18, style: .continuous).fill(GojiColor.accentGradient)
                    } else {
                        GlassSurface(radius: 18)
                    }
                }
                if !message.isMine { Spacer(minLength: 40) }
            }
            .accessibilityElement(children: .combine)
        }
    }
}

enum SupportAttachments {
    /// Журналы приложения и туннеля одним файлом — без токенов и содержимого профилей.
    static func logsFile() -> APIClient.UploadFile {
        let header = "Goji VPN iOS \(AppConstants.appVersion) (\(AppConstants.buildNumber)), iOS \(UIDevice.current.systemVersion), \(DeviceModel.identifier)\n\n"
        let data = Data((header + AppLog.exportAll()).utf8)
        return APIClient.UploadFile(name: "goji-ios-logs.txt", mimeType: "text/plain", data: data)
    }
}

// MARK: - FAQ

struct FaqView: View {
    @State private var response: FaqResponse?
    @State private var error: String?
    @State private var expanded: Set<Int64> = []

    var body: some View {
        SubScreen(title: L("faq.title")) {
            if let response {
                let ungrouped = response.ungrouped ?? []
                let sections = response.sections ?? []
                if ungrouped.isEmpty && sections.allSatisfy({ ($0.items ?? []).isEmpty }) {
                    MessageCard(icon: "questionmark.circle", text: L("faq.empty"))
                }
                ForEach(ungrouped) { item in faqItem(item) }
                ForEach(sections) { section in
                    if let items = section.items, !items.isEmpty {
                        SectionHeader(title: section.name)
                        ForEach(items) { item in faqItem(item) }
                    }
                }
            } else if let error {
                MessageCard(icon: "wifi.exclamationmark", text: error, actionTitle: L("common.retry")) { Task { await load() } }
            } else {
                ProgressView().frame(maxWidth: .infinity).padding(30)
            }
        }
        .task { await load() }
    }

    private func faqItem(_ item: FaqItem) -> some View {
        Button {
            withAnimation {
                if expanded.contains(item.id) { expanded.remove(item.id) } else { expanded.insert(item.id) }
            }
        } label: {
            VStack(alignment: .leading, spacing: 8) {
                HStack(alignment: .top) {
                    Text(item.question)
                        .font(GojiFont.manrope(14, .bold))
                        .foregroundStyle(GojiColor.textPrimary)
                        .multilineTextAlignment(.leading)
                    Spacer()
                    Image(systemName: expanded.contains(item.id) ? "chevron.up" : "chevron.down")
                        .foregroundStyle(GojiColor.textMuted)
                }
                if expanded.contains(item.id) {
                    Text(RichText.attributed(item.answer))
                        .font(GojiFont.body())
                        .foregroundStyle(GojiColor.textSecondary)
                        .tint(GojiColor.tealDeep)
                        .multilineTextAlignment(.leading)
                }
            }
            .padding(14)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(GlassSurface(radius: 18))
        }
        .buttonStyle(.plain)
    }

    private func load() async {
        do {
            response = try await APIClient.shared.get("api/faq")
            error = nil
        } catch {
            self.error = error.localizedDescription
        }
    }
}
