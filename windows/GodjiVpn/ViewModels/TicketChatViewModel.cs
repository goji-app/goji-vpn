using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GodjiVpn.Models;
using GodjiVpn.Services;
using GodjiVpn.Utils;
using Microsoft.Win32;

namespace GodjiVpn.ViewModels;

public sealed class MessageItem
{
    public required long Id { get; init; }
    public required bool IsMine { get; init; }
    public required bool IsEvent { get; init; }
    public string? SenderName { get; init; }
    public required string Text { get; init; }
    public string? TimeLabel { get; init; }
    public required List<string> AttachmentNames { get; init; }
    public bool HasAttachments => AttachmentNames.Count > 0;
    public bool HasSenderName => !IsMine && !IsEvent && !string.IsNullOrEmpty(SenderName);
}

public sealed class PendingAttachment
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required long SizeBytes { get; init; }
}

/// <summary>Аналог TicketChatScreen.kt/TicketChatViewModel.kt — переписка по одному обращению.
/// Поллинг раз в 5с, пока чат открыт (WebSocket на бэкенде нет нигде, даже у самого
/// веб-клиента — см. отчёт по Android 720f5ff). Вложения — фото/видео/PDF (бэкенд отвечает
/// 415 на любой другой тип); "Логи приложения" — НЕ вложение, а вставка хвоста лог-файла прямо
/// в текст сообщения (см. InsertLog), тот же обходной путь, что и в Android: журнал никогда не
/// уходит сам по себе, только по явному нажатию пользователя.</summary>
public sealed partial class TicketChatViewModel : ObservableObject
{
    private const int PollIntervalMs = 5000;
    private const int LogTailChars = 8000;

    private static string LogsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodjiVpn", "logs");

    private readonly ApiClient _api;
    private readonly DispatcherTimer _pollTimer;
    private long _ticketId;

    [ObservableProperty] private string title = "Поддержка";
    [ObservableProperty] private bool isClosed;
    [ObservableProperty] private bool loading = true;
    [ObservableProperty] private bool loadError;
    [ObservableProperty] private string input = "";
    [ObservableProperty] private bool sending;
    [ObservableProperty] private bool sendError;
    [ObservableProperty] private bool showLogPicker;
    /// <summary>Строка выбора вложения под скрепкой (DropdownMenu в Android).</summary>
    [ObservableProperty] private bool showAttachMenu;
    [ObservableProperty] private string? attachmentError;

    // Раньше выбор файла ничем не ограничивался — при отправке весь файл читается целиком в
    // память (File.ReadAllBytesAsync, см. SendAsync) перед тем, как уйти в MultipartFormDataContent;
    // desktop-пользователь может легко выбрать многогигабайтное видео через системный диалог
    // (в отличие от мобильного пикера, тут нет платформенного ограничения по умолчанию) —
    // без явного лимита это OutOfMemoryException и падение всего приложения, а не просто
    // неудачная отправка. Лимиты щедрые (это не защита от злоупотребления, а просто "не упасть
    // на случайно выбранном фильме"), сервер всё равно применит свои собственные ограничения.
    private const long MaxAttachmentBytes = 50 * 1024 * 1024;
    private const long MaxTotalAttachmentBytes = 200 * 1024 * 1024;

    public ObservableCollection<MessageItem> Messages { get; } = new();
    public ObservableCollection<PendingAttachment> Attachments { get; } = new();

    public event Action? BackRequested;

    public TicketChatViewModel(ApiClient api)
    {
        _api = api;
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PollIntervalMs) };
        _pollTimer.Tick += async (_, _) => await LoadMessagesAsync(showLoading: false);
    }

    partial void OnInputChanged(string value) => SendCommand.NotifyCanExecuteChanged();
    partial void OnSendingChanged(bool value) => SendCommand.NotifyCanExecuteChanged();

    /// <summary>Идемпотентно — повторный вызов с тем же ticketId не перезапускает поллинг
    /// заново (аналог Android start(), защита от повторного вызова при переоткрытии того же
    /// экрана).</summary>
    public async Task StartAsync(long ticketId)
    {
        Stop();
        _ticketId = ticketId;
        Messages.Clear();
        Attachments.Clear();
        Input = "";
        SendError = false;
        ShowLogPicker = false;
        ShowAttachMenu = false;
        Title = "Поддержка";
        IsClosed = false;

        // Только для превью-экземпляра (GODJI_UI_PREVIEW_DEMO=1): вёрстка без API и поллинга.
        if (Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW_DEMO") == "1")
        {
            Title = "Не подключается к серверу NL";
            Messages.Add(new MessageItem { Id = 1, IsMine = false, IsEvent = true, Text = "Обращение создано", AttachmentNames = new() });
            Messages.Add(new MessageItem { Id = 2, IsMine = true, IsEvent = false, Text = "Здравствуйте! С утра не подключается к Нидерландам, остальные узлы работают.", TimeLabel = "10:12", AttachmentNames = new() { "screenshot.png" } });
            Messages.Add(new MessageItem { Id = 3, IsMine = false, IsEvent = false, SenderName = "Поддержка Goji", Text = "Добрый день! Попробуйте, пожалуйста, сменить способ пинга на TCP и переподключиться.", TimeLabel = "10:20", AttachmentNames = new() });
            Loading = false;
            return;
        }

        try
        {
            var ticket = await _api.GetSupportTicketAsync(ticketId);
            Title = !string.IsNullOrWhiteSpace(ticket.Subject) ? ticket.Subject! : "Поддержка";
            IsClosed = ticket.Status == "closed";
        }
        catch { /* заголовок — второстепенная деталь, сами сообщения важнее */ }

        await LoadMessagesAsync(showLoading: true);
        _pollTimer.Start();
    }

    public void Stop() => _pollTimer.Stop();

    private async Task LoadMessagesAsync(bool showLoading)
    {
        if (showLoading) { Loading = true; LoadError = false; }
        try
        {
            var messages = await _api.GetSupportMessagesAsync(_ticketId);
            Messages.Clear();
            foreach (var m in messages) Messages.Add(ToItem(m));
        }
        catch
        {
            // Фоновый поллинг (showLoading=false) — ошибки молча игнорируются, ни баннера, ни
            // индикатора. Первую явную загрузку помечаем ошибкой, только если сообщений ещё
            // нет вовсе — транзиентный сбой не должен затирать уже показанный контент.
            if (showLoading && Messages.Count == 0) LoadError = true;
        }
        finally { if (showLoading) Loading = false; }
    }

    [RelayCommand]
    private Task RetryAsync() => LoadMessagesAsync(showLoading: true);

    [RelayCommand]
    private void PickMedia()
    {
        ShowAttachMenu = false;
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Фото и видео",
            Filter = "Фото и видео|*.jpg;*.jpeg;*.png;*.gif;*.webp;*.mp4;*.mov;*.avi;*.mkv"
        };
        if (dialog.ShowDialog() == true) AddAttachments(dialog.FileNames);
    }

    [RelayCommand]
    private void PickPdf()
    {
        ShowAttachMenu = false;
        var dialog = new OpenFileDialog { Multiselect = true, Title = "PDF-файл", Filter = "PDF-файлы|*.pdf" };
        if (dialog.ShowDialog() == true) AddAttachments(dialog.FileNames);
    }

    private void AddAttachments(IEnumerable<string> paths)
    {
        AttachmentError = null;
        foreach (var path in paths)
        {
            long size;
            try { size = new FileInfo(path).Length; } catch { continue; }

            if (size > MaxAttachmentBytes)
            {
                AttachmentError = $"«{Path.GetFileName(path)}» слишком большой (лимит {MaxAttachmentBytes / (1024 * 1024)} МБ) — не добавлен";
                continue;
            }
            if (Attachments.Sum(a => a.SizeBytes) + size > MaxTotalAttachmentBytes)
            {
                AttachmentError = $"Суммарный размер вложений превысил {MaxTotalAttachmentBytes / (1024 * 1024)} МБ — «{Path.GetFileName(path)}» не добавлен";
                continue;
            }

            Attachments.Add(new PendingAttachment { FilePath = path, FileName = Path.GetFileName(path), SizeBytes = size });
        }
        SendCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void RemoveAttachment(PendingAttachment item)
    {
        Attachments.Remove(item);
        SendCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ToggleAttachMenu()
    {
        ShowAttachMenu = !ShowAttachMenu;
        if (ShowAttachMenu) ShowLogPicker = false;
    }

    [RelayCommand]
    private void ToggleLogPicker()
    {
        ShowAttachMenu = false;
        ShowLogPicker = !ShowLogPicker;
    }

    /// <summary>Вставляет хвост лог-файла ПРЯМО В ТЕКСТ сообщения, не как вложение — бэкенд
    /// принимает вложениями только image/video/PDF и отвечает 415 на текстовые файлы
    /// (подтверждено живым тестом на Android, см. отчёт по 720f5ff). Категории — те же 4 файла,
    /// что уже показывает раздел "Журнал" в Настройках (см. SettingsViewModel.LogFiles).</summary>
    [RelayCommand]
    private void InsertLog(string fileName)
    {
        ShowLogPicker = false;
        var label = fileName switch
        {
            "engine.log" => "Приложение",
            "xray.log" => "VPN-ядро (xray)",
            "sing-box.log" => "VPN-туннель (sing-box)",
            "crash.log" => "Сбои",
            _ => fileName
        };

        string tail;
        try
        {
            var path = Path.Combine(LogsDir, fileName);
            if (!File.Exists(path)) tail = "(пока пусто)";
            else
            {
                var text = File.ReadAllText(path);
                tail = text.Length > LogTailChars ? text[^LogTailChars..] : text;
            }
        }
        catch { tail = "(не удалось прочитать журнал)"; }

        var block = $"{label}:\n```\n{tail}\n```";
        Input = Input.Length > 0 ? Input + "\n\n" + block : block;
    }

    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendAsync()
    {
        var text = Input.Trim();
        var pending = Attachments.ToList();

        Sending = true;
        SendError = false;
        try
        {
            if (pending.Count == 0)
            {
                await _api.SendSupportMessageAsync(_ticketId, text);
            }
            else
            {
                var files = new List<(string, byte[], string)>();
                foreach (var a in pending)
                    files.Add((a.FileName, await File.ReadAllBytesAsync(a.FilePath), MimeTypeFor(a.FileName)));
                await _api.SendSupportMessageWithFilesAsync(_ticketId, text, files);
            }

            Input = "";
            Attachments.Clear();
            SendCommand.NotifyCanExecuteChanged();
            await LoadMessagesAsync(showLoading: false);
        }
        catch { SendError = true; }
        finally { Sending = false; }
    }

    private bool CanSendMessage() => !Sending && (Input.Trim().Length > 0 || Attachments.Count > 0);

    [RelayCommand]
    private void Back()
    {
        Stop();
        BackRequested?.Invoke();
    }

    private static string MimeTypeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".mp4" => "video/mp4",
        ".mov" => "video/quicktime",
        ".avi" => "video/x-msvideo",
        ".mkv" => "video/x-matroska",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };

    private static MessageItem ToItem(SupportMessageDto m) => new()
    {
        Id = m.Id,
        IsMine = m.SenderType == "user",
        IsEvent = !string.IsNullOrWhiteSpace(m.EventType),
        SenderName = m.SenderName,
        Text = m.Message ?? "",
        TimeLabel = m.CreatedAt is { } ca ? DateFormat.FormatDateTime(ca) : null,
        AttachmentNames = (m.Attachments ?? new())
            .Where(a => !string.IsNullOrEmpty(a.FileName))
            .Select(a => a.FileName!)
            .ToList()
    };
}
