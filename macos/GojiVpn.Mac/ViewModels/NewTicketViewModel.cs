using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GodjiVpn.Services;

namespace GodjiVpn.ViewModels;

public sealed partial class QueueItem : ObservableObject
{
    public required long Id { get; init; }
    public required string Name { get; init; }
    [ObservableProperty] private bool isSelected;
}

/// <summary>Аналог NewTicketScreen.kt/NewTicketViewModel.kt — форма создания обращения, с
/// превентивной проверкой лимита открытых тикетов до показа формы (см. LoadAsync). Порт из
/// Android (720f5ff).</summary>
public sealed partial class NewTicketViewModel : ObservableObject
{
    private readonly ApiClient _api;

    [ObservableProperty] private bool loading = true;
    [ObservableProperty] private bool canCreate = true;
    // NullToVisibilityConverter кастует value как string (см. его комментарий/PlansViewModel) —
    // на long? это всегда unsafe-null, поэтому видимость кнопки "Перейти к обращению" решает
    // отдельный bool, а не биндинг Visibility напрямую на ActiveTicketId.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveTicketId))]
    private long? activeTicketId;
    public bool HasActiveTicketId => ActiveTicketId != null;
    [ObservableProperty] private string subject = "";
    [ObservableProperty] private string message = "";
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool submitting;

    public ObservableCollection<QueueItem> Queues { get; } = new();
    public bool ShowQueuePicker => Queues.Count > 1;

    public event Action<long>? Created;
    public event Action? BackRequested;
    public event Action<long>? OpenActiveTicketRequested;

    public NewTicketViewModel(ApiClient api) => _api = api;

    public async Task LoadAsync()
    {
        Loading = true;
        CanCreate = true;
        ActiveTicketId = null;
        Subject = "";
        Message = "";
        ErrorMessage = null;
        Queues.Clear();
        OnPropertyChanged(nameof(ShowQueuePicker));

        // Только для превью-экземпляра (GODJI_UI_PREVIEW_DEMO=1): вёрстка без API.
        if (Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW_DEMO") == "1")
        {
            Queues.Add(new QueueItem { Id = 1, Name = "Техническая помощь", IsSelected = true });
            Queues.Add(new QueueItem { Id = 2, Name = "Оплата" });
            OnPropertyChanged(nameof(ShowQueuePicker));
            Loading = false;
            return;
        }

        var limitTask = _api.GetSupportTicketLimitAsync();
        var queuesTask = _api.GetSupportQueuesAsync();
        try
        {
            var limit = await limitTask;
            CanCreate = limit.CanCreate;
            ActiveTicketId = limit.ActiveTicketId;
        }
        catch { /* лимит недоступен — не блокируем форму по недоступному сигналу */ }

        try
        {
            var queues = await queuesTask;
            foreach (var q in queues) Queues.Add(new QueueItem { Id = q.Id, Name = q.Name });
            OnPropertyChanged(nameof(ShowQueuePicker));
        }
        catch { /* очередь — необязательный уточняющий выбор, при недоступности просто не показываем */ }

        Loading = false;
    }

    [RelayCommand]
    private void SelectQueue(QueueItem item)
    {
        foreach (var q in Queues) q.IsSelected = q == item;
    }

    [RelayCommand]
    private void OpenActiveTicket()
    {
        if (ActiveTicketId is { } id) OpenActiveTicketRequested?.Invoke(id);
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        ErrorMessage = null;
        var trimmed = Message.Trim();
        if (trimmed.Length == 0) { ErrorMessage = "Введите сообщение"; return; }
        if (ShowQueuePicker && Queues.All(q => !q.IsSelected)) { ErrorMessage = "Выберите, куда задать вопрос"; return; }

        Submitting = true;
        try
        {
            var queueId = Queues.FirstOrDefault(q => q.IsSelected)?.Id ?? Queues.FirstOrDefault()?.Id;
            var ticket = await _api.CreateSupportTicketAsync(
                string.IsNullOrWhiteSpace(Subject) ? null : Subject.Trim(), trimmed, queueId);
            Created?.Invoke(ticket.Id);
        }
        catch { ErrorMessage = "Не удалось создать обращение"; }
        finally { Submitting = false; }
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke();
}
