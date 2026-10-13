package xyz.gojihub.vpn.ui.support

import android.content.Context
import android.net.Uri
import android.provider.OpenableColumns
import android.webkit.MimeTypeMap
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.MultipartBody
import okhttp3.RequestBody.Companion.asRequestBody
import okhttp3.RequestBody.Companion.toRequestBody
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.network.RemnawaveApi
import xyz.gojihub.vpn.network.models.SendSupportMessageRequest
import xyz.gojihub.vpn.network.models.SupportMessageDto
import xyz.gojihub.vpn.util.AppLogger
import xyz.gojihub.vpn.util.LogCategory
import xyz.gojihub.vpn.util.formatDateTime
import java.io.File
import javax.inject.Inject

// Тот же порядок величины, что и другие живые опросы в приложении (см. GodjiVpnService
// watchdog — 30с, ConnectViewModel скорость — 1с) — вебсокета у этой тикет-системы нет
// нигде в веб-версии, только периодический рефетч, поэтому опрашиваем сами, пока экран
// открыт. 5с — заметно быстрее, чем у большинства фоновых опросов в приложении, но экран
// чата открыт недолго и активно, в отличие от них.
private const val POLL_INTERVAL_MS = 5000L

// См. addLogAttachment — тот же хвост, что и GodjiVpnService.dumpXrayLogs() для той же задачи
// (уместить диагностику в разумный объём текста).
private const val LOG_TAIL_CHARS = 8000

data class SupportMessageUi(
    val id: Long,
    val text: String?,
    val isMine: Boolean,
    val isEvent: Boolean,
    val senderName: String?,
    val timeLabel: String?,
    val attachmentNames: List<String>
)

/** Файл, выбранный пользователем (фото/видео через системный пикер, PDF через
 *  OpenMultipleDocuments) — читается лениво из [uri] только в момент отправки, не сразу при
 *  выборе, не держим байты крупного видео в памяти дольше необходимого. Логи сюда не входят —
 *  см. addLogAttachment: бэкенд принимает вложениями только изображения, видео и PDF (тот же
 *  список MIME-типов, что и accept у файлового инпута веб-версии) и отвечает HTTP 415 на что
 *  угодно ещё, включая обычный текстовый файл — подтверждено живым тестом с .txt-логом. */
class PendingAttachment(val uri: Uri, val name: String)

data class TicketChatUiState(
    val ticketTitle: String = "",
    val isClosed: Boolean = false,
    val messages: List<SupportMessageUi> = emptyList(),
    val loading: Boolean = true,
    val loadError: Boolean = false,
    val input: String = "",
    val attachments: List<PendingAttachment> = emptyList(),
    val sending: Boolean = false,
    val sendError: Boolean = false
)

private fun SupportMessageDto.toUi(): SupportMessageUi = SupportMessageUi(
    id = id,
    text = message,
    isMine = isMine,
    isEvent = isEvent,
    senderName = senderName,
    timeLabel = createdAt?.let(::formatDateTime),
    attachmentNames = attachments.orEmpty().mapNotNull { it.fileName }
)

@HiltViewModel
class TicketChatViewModel @Inject constructor(
    private val api: RemnawaveApi,
    @ApplicationContext private val appContext: Context
) : ViewModel() {

    private val _state = MutableStateFlow(TicketChatUiState())
    val state: StateFlow<TicketChatUiState> = _state

    private var ticketId: Long = 0
    private var pollJob: Job? = null

    /** Вызывается один раз из экрана через LaunchedEffect(ticketId) — сам ViewModel не знает
     *  аргумент навигации напрямую (см. тот же приём в VerifyEmailViewModel). Повторный вызов
     *  с тем же id — не перезапускает поллинг заново. */
    fun start(id: Long) {
        if (ticketId == id && pollJob?.isActive == true) return
        ticketId = id
        loadTicket()
        loadMessages(showLoading = true)
        pollJob?.cancel()
        pollJob = viewModelScope.launch {
            while (isActive) {
                delay(POLL_INTERVAL_MS)
                loadMessages(showLoading = false)
            }
        }
    }

    override fun onCleared() {
        pollJob?.cancel()
        super.onCleared()
    }

    private fun loadTicket() {
        viewModelScope.launch {
            runCatching { api.getSupportTicket(ticketId) }.onSuccess { ticket ->
                _state.value = _state.value.copy(
                    ticketTitle = ticket.subject?.takeIf { it.isNotBlank() } ?: Loc.s.support.supportNewTicket,
                    isClosed = ticket.status == "closed"
                )
            }
        }
    }

    private fun loadMessages(showLoading: Boolean) {
        if (showLoading) _state.value = _state.value.copy(loading = true, loadError = false)
        viewModelScope.launch {
            runCatching { api.getSupportMessages(ticketId) }
                .onSuccess { messages ->
                    _state.value = _state.value.copy(
                        messages = messages.orEmpty().map { it.toUi() },
                        loading = false,
                        loadError = false
                    )
                }
                .onFailure {
                    if (showLoading) _state.value = _state.value.copy(loading = false, loadError = _state.value.messages.isEmpty())
                }
        }
    }

    fun setInput(value: String) {
        _state.value = _state.value.copy(input = value, sendError = false)
    }

    /** [uris] — из системного пикера (фото/видео через PickMultipleVisualMedia, PDF через
     *  OpenMultipleDocuments) — читаем только отображаемое имя сейчас, содержимое лениво при
     *  отправке (см. buildFilePart). */
    fun addFileAttachments(uris: List<Uri>) {
        if (uris.isEmpty()) return
        viewModelScope.launch {
            val added = withContext(Dispatchers.IO) {
                uris.map { uri -> PendingAttachment(uri, displayNameFor(uri)) }
            }
            _state.value = _state.value.copy(attachments = _state.value.attachments + added, sendError = false)
        }
    }

    /** Вставляет хвост локального журнала (см. AppLogger) прямо в текст сообщения — НЕ как
     *  файл-вложение: бэкенд принимает вложениями только изображения, видео и PDF (см.
     *  комментарий у PendingAttachment) и отвечает 415 на обычный текстовый файл. Хвост, а не журнал
     *  целиком — категория может занимать до ~1МБ (см. AppLogger.MAX_SIZE_BYTES), это надёжно
     *  не поместится ни в одно разумное текстовое поле; тот же приём (последние 8000 символов),
     *  что уже использует GodjiVpnService.dumpXrayLogs() для этой же цели. Сознательно только
     *  по явному нажатию пользователя — см. комментарий о приватности в самом AppLogger:
     *  журнал никогда не уходит с устройства сам. */
    fun addLogAttachment(category: LogCategory, label: String) {
        viewModelScope.launch {
            val full = withContext(Dispatchers.IO) { AppLogger.readAll(appContext, category) }
            val tail = if (full.length > LOG_TAIL_CHARS) full.takeLast(LOG_TAIL_CHARS) else full
            val block = "$label:\n```\n$tail\n```"
            val current = _state.value.input
            _state.value = _state.value.copy(
                input = if (current.isBlank()) block else "$current\n\n$block",
                sendError = false
            )
        }
    }

    fun removeAttachment(attachment: PendingAttachment) {
        _state.value = _state.value.copy(attachments = _state.value.attachments.filterNot { it === attachment })
    }

    private fun displayNameFor(uri: Uri): String {
        if (uri.scheme != "content") return uri.lastPathSegment ?: "file"
        return runCatching {
            appContext.contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { cursor ->
                if (cursor.moveToFirst()) {
                    val idx = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                    if (idx >= 0) cursor.getString(idx) else null
                } else null
            }
        }.getOrNull() ?: uri.lastPathSegment ?: "file"
    }

    fun send() {
        val text = _state.value.input.trim()
        val attachments = _state.value.attachments
        if ((text.isEmpty() && attachments.isEmpty()) || _state.value.sending) return
        _state.value = _state.value.copy(sending = true, sendError = false)
        viewModelScope.launch {
            val tempFiles = mutableListOf<File>()
            runCatching {
                // sendSupportMessage(WithFiles) возвращает голый Response<Void> — Retrofit НЕ
                // бросает исключение на HTTP-ошибку для Response<T> (в отличие от обычного
                // "голого" возвращаемого типа), это единственный способ узнать про неё.
                // Подтверждено живым тестом: без этой проверки неудачная отправка молча
                // считалась успехом — чипы вложений/поле ввода очищались, а сообщение в
                // переписке так и не появлялось.
                val response = if (attachments.isEmpty()) {
                    api.sendSupportMessage(ticketId, SendSupportMessageRequest(message = text))
                } else {
                    val messageBody = text.toRequestBody("text/plain".toMediaTypeOrNull())
                    val parts = withContext(Dispatchers.IO) { attachments.map { buildFilePart(it, tempFiles) } }
                    api.sendSupportMessageWithFiles(ticketId, messageBody, parts)
                }
                if (!response.isSuccessful) {
                    error("HTTP ${response.code()}: ${response.errorBody()?.string().orEmpty()}")
                }
            }.onSuccess {
                _state.value = _state.value.copy(sending = false, input = "", attachments = emptyList())
                loadMessages(showLoading = false)
            }.onFailure {
                AppLogger.e(appContext, LogCategory.MAIN, "TicketChat", "sendSupportMessage failed", it)
                _state.value = _state.value.copy(sending = false, sendError = true)
            }
            // Копии из cacheDir (см. buildFilePart) нужны были только на время самого запроса —
            // OkHttp уже прочитал их целиком к этому моменту (запрос выполнен синхронно внутри
            // runCatching выше), не оставляем их копиться в cache до системной очистки.
            withContext(Dispatchers.IO) { tempFiles.forEach { runCatching { it.delete() } } }
        }
    }

    /** Читает содержимое файла непосредственно перед отправкой через ContentResolver (тип
     *  берём оттуда же, а не гадаем по расширению — оно не всегда есть у content://-Uri).
     *  Копия во временный файл (добавляется в [tempFiles] для последующей очистки — см.
     *  send()) нужна, потому что OkHttp asRequestBody(File) умеет стримить с диска без
     *  загрузки всего файла в память сразу — важно для видео, которые могут быть крупными;
     *  прямой поток из ContentResolver в RequestBody потребовал бы своей реализации
     *  RequestBody, а cacheDir-копия — надёжный и простой способ получить обычный File. */
    private fun buildFilePart(attachment: PendingAttachment, tempFiles: MutableList<File>): MultipartBody.Part {
        val mimeType = appContext.contentResolver.getType(attachment.uri)
            ?: MimeTypeMap.getSingleton().getMimeTypeFromExtension(attachment.name.substringAfterLast('.', ""))
            ?: "application/octet-stream"
        val tempFile = File.createTempFile("support_upload_", "_${attachment.name}", appContext.cacheDir)
        tempFiles.add(tempFile)
        appContext.contentResolver.openInputStream(attachment.uri)?.use { input ->
            tempFile.outputStream().use { output -> input.copyTo(output) }
        }
        val body = tempFile.asRequestBody(mimeType.toMediaTypeOrNull())
        return MultipartBody.Part.createFormData("files", attachment.name, body)
    }

    fun refresh() {
        loadTicket()
        loadMessages(showLoading = true)
    }
}
