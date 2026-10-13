using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GodjiVpn.Models;
using GodjiVpn.Services;
using GodjiVpn.Utils;

namespace GodjiVpn.ViewModels;

/// <summary>Цвет плашки пинга — pingColor() из эталона: не проверен — TextSecondary,
/// &lt;40 — TealDeep, &lt;90 — жёлтый, иначе/недоступен — Danger.</summary>
public enum PingLevel { Unchecked, Good, Mid, Bad }

public sealed partial class NodeItem : ObservableObject
{
    public required VlessNode Node { get; init; }
    /// <summary>-2 = ещё не проверяли ("проверить"), -1 = недоступен, иначе — мс.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PingLabel))]
    [NotifyPropertyChangedFor(nameof(PingLevel))]
    private int pingMs = -2;
    [ObservableProperty] private bool isSelected;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PingLabel))]
    private bool isChecking;
    [ObservableProperty] private bool isFavorite;

    public string PingLabel => IsChecking ? "…" : PingMs switch
    {
        -2 => "проверить",
        < 0 => "недоступен",
        _ => $"{PingMs} мс"
    };

    public PingLevel PingLevel => PingMs switch
    {
        -2 => PingLevel.Unchecked,
        < 0 => PingLevel.Bad,
        < 40 => PingLevel.Good,
        < 90 => PingLevel.Mid,
        _ => PingLevel.Bad
    };

    /// <summary>Добавлен вручную по JSON-профилю (см. CustomNodeStore), а не пришёл с
    /// подписки — только у таких узлов показываем кнопку удаления.</summary>
    public bool IsCustom => Node.Id.StartsWith("custom-", StringComparison.Ordinal);

    public CountryGeo? Geo => CountryGeoLookup.Find(Node.Name);
    public string DisplayName => RemarkText.StripLeadingFlag(Node.Name);
    public string Flag => Geo != null ? CountryGeoLookup.FlagEmoji(Geo.Code) : "🌐";
    public string? FlagImagePath => FlagIcon.ImagePath(Geo?.Code);
}

/// <summary>Список узлов подписки + пинг через реальный прокси-туннель — аналог
/// PingRepository.kt (Android): временный xray.exe с профилем узла, HTTP-запрос к
/// generate_204 через получившийся SOCKS5. Меряет настоящую задержку с учётом оверхеда
/// VLESS+Reality, а не просто TCP-рукопожатие до порта сервера (см. PingService.cs).</summary>
public sealed partial class ServersViewModel : ObservableObject
{
    private readonly SubscriptionRepository _subscription;
    private readonly PingService _pingService;
    private readonly CustomNodeStore _customNodes;
    private readonly FavoriteServersStore _favorites;
    private readonly AppSettings _settings;
    private static readonly System.Globalization.CultureInfo Ru = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");

    public ObservableCollection<NodeItem> Nodes { get; } = new();

    /// <summary>Режимы сортировки (порт Android 64aa106): Избранные / Пинг / А–Я.</summary>
    public IReadOnlyList<string> SortOptions { get; } = new[] { "Избранные", "Пинг", "А–Я" };

    [ObservableProperty] private int sortIndex;

    partial void OnSortIndexChanged(int value)
    {
        _settings.ServerSort = (ServerSort)Math.Clamp(value, 0, 2);
        ApplySort();
    }

    [ObservableProperty] private bool isCheckingAll;
    [ObservableProperty] private bool isRefreshing;

    /// <summary>Баннер-результат ручного обновления (AnimatedContent в Android) — появляется
    /// один раз после нажатия "Обновить", не показывается на автообновлении при открытии
    /// вкладки.</summary>
    [ObservableProperty] private string? refreshResultMessage;
    [ObservableProperty] private bool refreshResultIsError;
    private CancellationTokenSource? _refreshMessageCts;

    /// <summary>Выбор узла сразу возвращает на "Главную" — как pick() в эталоне v5.</summary>
    public event Action? ServerPicked;

    public ServersViewModel(SubscriptionRepository subscription, PingService pingService, CustomNodeStore customNodes,
        FavoriteServersStore favorites, AppSettings settings)
    {
        _subscription = subscription;
        _pingService = pingService;
        _customNodes = customNodes;
        _favorites = favorites;
        _settings = settings;
        sortIndex = (int)settings.ServerSort;
        _subscription.PropertyChanged += (_, _) => RunOnUiThread(SyncFromRepository);
        _favorites.Changed += () => RunOnUiThread(SyncFromRepository);
        SyncFromRepository();
    }

    [RelayCommand]
    private void ToggleFavorite(NodeItem item) => _favorites.Toggle(item.Node.Id);

    [RelayCommand]
    private void RemoveCustomNode(NodeItem item)
    {
        _customNodes.Remove(item.Node.Id);
        SyncFromRepository();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsRefreshing = true;
        var ok = await _subscription.RefreshAsync();
        IsRefreshing = false;
        // RefreshAsync внутри меняет Subscription/Nodes, что уже придёт через PropertyChanged,
        // но подписка регистрируется до первого узла — на всякий случай синхронизируем и тут.
        SyncFromRepository();

        RefreshResultIsError = !ok || _subscription.LastError != null;
        ShowRefreshMessage(RefreshResultIsError
            ? _subscription.LastError ?? "Не удалось обновить подписку — проверьте соединение"
            : "Подписка обновлена");
    }

    /// <summary>Баннер-результат прячется сам через 4.5 с (как в Android), либо по крестику.</summary>
    private async void ShowRefreshMessage(string message)
    {
        _refreshMessageCts?.Cancel();
        var cts = _refreshMessageCts = new CancellationTokenSource();
        RefreshResultMessage = message;
        try
        {
            await Task.Delay(4500, cts.Token);
            RefreshResultMessage = null;
        }
        catch (TaskCanceledException) { }
    }

    [RelayCommand]
    private void DismissRefreshResult()
    {
        _refreshMessageCts?.Cancel();
        RefreshResultMessage = null;
    }

    private void SyncFromRepository()
    {
        _favorites.MigrateLegacyIds(_subscription.Nodes.Where(n => !n.Id.StartsWith("custom-", StringComparison.Ordinal)).ToList());
        var selectedId = _subscription.SelectedId;
        var existingById = Nodes.ToDictionary(n => n.Node.Id);
        var newNodes = new List<NodeItem>();
        foreach (var node in _subscription.Nodes)
        {
            var item = existingById.TryGetValue(node.Id, out var previous)
                ? new NodeItem { Node = node, IsSelected = node.Id == selectedId, PingMs = previous.PingMs }
                : new NodeItem { Node = node, IsSelected = node.Id == selectedId };
            item.IsFavorite = _favorites.IsFavorite(node.Id);
            newNodes.Add(item);
        }
        Nodes.Clear();
        foreach (var item in Sorted(newNodes)) Nodes.Add(item);
        if (Nodes.Count == 0 && Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW_DEMO") == "1") FillPreviewDemo();
    }

    /// <summary>Только для превью-экземпляра (GODJI_UI_PREVIEW_DEMO=1): сверка вёрстки без подписки.</summary>
    private void FillPreviewDemo()
    {
        (string Name, int Ping, bool Fav)[] demo =
        {
            ("🇳🇱 Нидерланды", 48, true), ("🇩🇪 Германия", 63, false), ("🇫🇮 Финляндия", 142, false),
            ("🇹🇷 Турция", 311, false), ("🇺🇸 США", -1, false), ("🇰🇿 Казахстан", -2, false),
            ("🇵🇱 Польша", 71, false), ("🇸🇪 Швеция", 88, true), ("🇫🇷 Франция", 95, false),
            ("🇬🇧 Великобритания", 104, false), ("🇯🇵 Япония", 236, false), ("🇸🇬 Сингапур", 268, false),
            ("🇦🇲 Армения", 57, false), ("🇷🇺 Россия LTE", 21, false)
        };
        for (var i = 0; i < demo.Length; i++)
            Nodes.Add(new NodeItem
            {
                Node = new VlessNode { Id = $"demo-{i}", Name = demo[i].Name, Host = "demo", Port = 443, ConnectPayloadJson = "{}" },
                IsSelected = i == 0, PingMs = demo[i].Ping, IsFavorite = demo[i].Fav
            });
    }

    /// <summary>Сортировки стабильные: при равенстве остаётся порядок подписки. Избранные идут
    /// первыми только в режиме "Избранные"; в остальных — строго по ключу (как в Android).</summary>
    private IEnumerable<NodeItem> Sorted(IEnumerable<NodeItem> items) => (ServerSort)SortIndex switch
    {
        ServerSort.Ping => items
            .OrderBy(n => n.PingMs >= 0 ? 0 : n.PingMs == -2 ? 1 : 2)
            .ThenBy(n => n.PingMs >= 0 ? n.PingMs : int.MaxValue),
        ServerSort.Name => items.OrderBy(n => n.DisplayName, StringComparer.Create(Ru, ignoreCase: true)),
        _ => items.OrderByDescending(n => n.IsFavorite)
    };

    /// <summary>Переставляет уже существующие строки без пересоздания — пинги и подсветка
    /// выбранного узла не сбрасываются при смене сортировки или приходе нового замера.</summary>
    private void ApplySort()
    {
        var target = Sorted(Nodes.ToList()).ToList();
        for (var i = 0; i < target.Count; i++)
        {
            var current = Nodes.IndexOf(target[i]);
            if (current != i) Nodes.Move(current, i);
        }
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    [RelayCommand]
    private void Select(NodeItem item)
    {
        foreach (var n in Nodes) n.IsSelected = n == item;
        _subscription.Select(item.Node.Id);
        ServerPicked?.Invoke();
    }

    [RelayCommand]
    private async Task PingOneAsync(NodeItem item)
    {
        item.IsChecking = true;
        item.PingMs = await _pingService.MeasureAsync(item.Node);
        item.IsChecking = false;
        if ((ServerSort)SortIndex == ServerSort.Ping) ApplySort();
    }

    private DateTime _lastAutoPingUtc = DateTime.MinValue;

    /// <summary>Авто-пинг при каждом открытии вкладки (Android: LaunchedEffect в ServersScreen) —
    /// раньше выбор узла шёл вслепую, пока не нажмёшь "Пинг". Не накладывается на уже идущую
    /// проверку и не повторяется чаще раза в 20 с при быстром переключении вкладок.</summary>
    public async Task AutoPingAsync()
    {
        if (IsCheckingAll || DateTime.UtcNow - _lastAutoPingUtc < TimeSpan.FromSeconds(20)) return;
        await PingAllCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task PingAllAsync()
    {
        if (IsCheckingAll) return;
        _lastAutoPingUtc = DateTime.UtcNow;
        IsCheckingAll = true;
        var tasks = Nodes.Select(async item =>
        {
            item.IsChecking = true;
            item.PingMs = await _pingService.MeasureAsync(item.Node);
            item.IsChecking = false;
        });
        await Task.WhenAll(tasks);
        IsCheckingAll = false;
        if ((ServerSort)SortIndex == ServerSort.Ping) ApplySort();
    }
}
