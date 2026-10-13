using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GodjiVpn.Models;
using GodjiVpn.Services;
using GodjiVpn.Utils;

namespace GodjiVpn.ViewModels;

public sealed class TicketListItem
{
    public required long Id { get; init; }
    public required string Title { get; init; }
    public string? LastMessage { get; init; }
    public required string StatusLabel { get; init; }
    public required bool IsClosed { get; init; }
    public required int UnreadCount { get; init; }
    public string? DateLabel { get; init; }
    public bool HasUnread => UnreadCount > 0;
}

/// <summary>Аналог SupportListScreen.kt/SupportListViewModel.kt — список обращений (открытые/
/// история), постраничная подгрузка по 20. Порт из Android (720f5ff).</summary>
public sealed partial class SupportListViewModel : ObservableObject
{
    private const int PageSize = 20;

    internal static readonly Dictionary<string, string> StatusLabels = new()
    {
        ["open"] = "Открыто",
        ["waiting_customer"] = "Ожидаем ваш ответ",
        ["awaiting_reply"] = "Ожидаем ответ поддержки",
        ["on_hold"] = "В обработке",
        ["closed"] = "Закрыто"
    };

    private readonly ApiClient _api;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyText))]
    private int tab; // 0 — открытые, 1 — история
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty), nameof(ShowTickets))]
    private bool loading;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    private bool loadError;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadMore))]
    private bool loadingMore;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadMore))]
    private bool canLoadMore;

    // Видимость блоков — в WPF это были триггеры разметки.
    public bool ShowEmpty => !Loading && !LoadError && Tickets.Count == 0;
    public bool ShowTickets => !Loading && Tickets.Count > 0;
    public bool ShowLoadMore => CanLoadMore && !LoadingMore;
    public string EmptyText => Tab == 1 ? "Нет закрытых обращений" : "Нет открытых обращений";

    public ObservableCollection<TicketListItem> Tickets { get; } = new();

    public event Action<long>? TicketOpened;
    public event Action? NewTicketRequested;
    public event Action? FaqRequested;

    public SupportListViewModel(ApiClient api)
    {
        _api = api;
        Tickets.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ShowEmpty));
            OnPropertyChanged(nameof(ShowTickets));
        };
    }

    [RelayCommand]
    private Task RetryAsync() => LoadAsync();

    /// <summary>Подписи сегмент-контрола вкладок (SupportListView).</summary>
    public string[] TabLabels { get; } = { "Открытые", "История" };

    /// <summary>tabName — "open"/"closed" или подпись из TabLabels (сегмент-контрол передаёт сам пункт).</summary>
    [RelayCommand]
    private async Task SelectTabAsync(string tabName)
    {
        var newTab = tabName is "closed" or "История" ? 1 : 0;
        if (Tab == newTab) return;
        Tab = newTab;
        await LoadAsync();
    }

    public async Task LoadAsync()
    {
        // Только для превью-экземпляра (GODJI_UI_PREVIEW_DEMO=1, см. App.xaml.cs): сверка
        // вёрстки без обращения к API — у превью нет своей сессии.
        if (Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW_DEMO") == "1")
        {
            Tickets.Clear();
            if (Tab == 0)
            {
                Tickets.Add(new TicketListItem { Id = 1, Title = "Не подключается к серверу NL", LastMessage = "Попробуйте, пожалуйста, сменить способ пинга…", StatusLabel = StatusLabels["waiting_customer"], IsClosed = false, UnreadCount = 2, DateLabel = "28 сентября" });
                Tickets.Add(new TicketListItem { Id = 2, Title = "Вопрос по оплате", LastMessage = "Спасибо, ждём ответа", StatusLabel = StatusLabels["awaiting_reply"], IsClosed = false, UnreadCount = 0, DateLabel = "25 сентября" });
            }
            else
            {
                Tickets.Add(new TicketListItem { Id = 3, Title = "Перенос на новый телефон", LastMessage = "Готово, устройство отвязано", StatusLabel = StatusLabels["closed"], IsClosed = true, UnreadCount = 0, DateLabel = "2 сентября" });
            }
            Loading = false;
            LoadError = false;
            CanLoadMore = false;
            return;
        }

        Loading = true;
        LoadError = false;
        try
        {
            var tickets = await _api.GetSupportTicketsAsync(Tab == 0 ? "open" : "closed", PageSize, 0);
            Tickets.Clear();
            foreach (var t in tickets) Tickets.Add(ToItem(t));
            CanLoadMore = tickets.Count >= PageSize;
        }
        catch
        {
            // Транзиентная ошибка на уже показанном списке не должна затирать контент —
            // помечаем LoadError только если список ещё реально пуст.
            if (Tickets.Count == 0) LoadError = true;
        }
        finally { Loading = false; }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (LoadingMore || !CanLoadMore) return;
        LoadingMore = true;
        try
        {
            var more = await _api.GetSupportTicketsAsync(Tab == 0 ? "open" : "closed", PageSize, Tickets.Count);
            foreach (var t in more) Tickets.Add(ToItem(t));
            CanLoadMore = more.Count >= PageSize;
        }
        catch { /* подгрузка страницы необязательна — просто оставляем кнопку "Показать ещё" */ }
        finally { LoadingMore = false; }
    }

    [RelayCommand]
    private void OpenTicket(TicketListItem item) => TicketOpened?.Invoke(item.Id);

    [RelayCommand]
    private void NewTicket() => NewTicketRequested?.Invoke();

    [RelayCommand]
    private void OpenFaq() => FaqRequested?.Invoke();

    private static TicketListItem ToItem(SupportTicketDto t) => new()
    {
        Id = t.Id,
        Title = !string.IsNullOrWhiteSpace(t.Subject) ? t.Subject! : "Новое обращение",
        LastMessage = t.LastMessage,
        StatusLabel = StatusLabels.GetValueOrDefault(t.Status, t.Status),
        IsClosed = t.Status == "closed",
        UnreadCount = t.UnreadCount,
        DateLabel = t.CreatedAt is { } ca ? DateFormat.FormatDate(ca) : null
    };
}
