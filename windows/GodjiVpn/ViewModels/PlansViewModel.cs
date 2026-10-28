using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Documents;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GodjiVpn.Models;
using GodjiVpn.Services;
using GodjiVpn.Utils;
using GodjiVpn.Views;

namespace GodjiVpn.ViewModels;

/// <summary>Одна новость/рассылка (см. SubscriptionRepository.Broadcasts). Document
/// перестраивается один раз при клике "Читать полностью" (см. RichContent.Build —
/// collapsedBlocks сворачивает контент до 6 верхнеуровневых блоков, как на самом сайте).</summary>
public sealed partial class NewsItem : ObservableObject
{
    private const int CollapsedBlockCount = 6;
    private bool _expanded;

    public required string Id { get; init; }
    public required string RawContent { get; init; }
    public required string DateLabel { get; init; }
    public required List<BroadcastButtonDto> Buttons { get; init; }

    [ObservableProperty] private FlowDocument document = new();

    public void BuildCollapsed() => Document = RichContent.Build(RawContent, CollapsedBlockCount, Expand);

    private void Expand()
    {
        if (_expanded) return;
        _expanded = true;
        Document = RichContent.Build(RawContent);
    }
}

/// <summary>Один приглашённый пользователь — имя/юзернейм/email уже замаскированы наполовину
/// точками (см. PlansViewModel.MaskHalf/DisplayNameFor), как и на самом сайте: это персональные
/// данные третьих лиц, не наши, полностью открытым текстом их показывать не стоит.</summary>
public sealed class ReferralEntryItem
{
    public required string DisplayName { get; init; }
    public required bool IsActive { get; init; }
    public required int BonusDays { get; init; }
    public string StatusBadge => IsActive ? "Активен" : "Неактивен";
    public string Initial => DisplayName.Length > 0 ? DisplayName[..1].ToUpperInvariant() : "?";
    public string BonusLabel => BonusDays > 0 ? $"+{BonusDays} дн." : "";
    public bool HasBonusLabel => BonusDays > 0;
}

public sealed class ReferralUiModel
{
    public required string Link { get; init; }
    public required int TotalReferrals { get; init; }
    public required int ActiveReferrals { get; init; }
    public required int TotalBonusDays { get; init; }
    public required List<ReferralEntryItem> Entries { get; init; }
    public bool HasEntries => Entries.Count > 0;
}

/// <summary>Одно устройство подписки (см. DeviceDto) — busy гасит кнопки переименования/
/// удаления на время запроса, как busy в Android DeviceUi.</summary>
public sealed partial class DeviceItem : ObservableObject
{
    public required string Hwid { get; init; }
    [ObservableProperty] private string name = "";
    /// <summary>Строка под названием, как на сайте: платформа · версия ОС · клиент.</summary>
    public string? Details { get; init; }
    public string? CreatedAtLabel { get; init; }
    [ObservableProperty] private bool isBusy;

    public string Subtitle => string.Join(" · ", new[] { Details, CreatedAtLabel }.Where(s => !string.IsNullOrEmpty(s)));
}

/// <summary>Раздел экрана «Подписка» (переключатель под карточкой подписки).</summary>
public sealed class PlansSectionItem
{
    public required string Label { get; init; }
}

public sealed partial class PlansViewModel : ObservableObject
{
    /// <summary>Оплата — только в личном кабинете на сайте (как в Android-сборке для сайта).</summary>
    private const string CabinetUrl = GojiHosts.SiteUrl + "/#/my-dashboard";

    // Свёрнутый вид (по умолчанию) — только 2 последние новости, без пагинации. Развёрнутый
    // (по клику "Показать все") — постранично, максимум 3 на странице.
    private const int CollapsedNewsCount = 2;
    private const int NewsPageSize = 3;

    private readonly ApiClient _api;
    private readonly SubscriptionRepository _subscription;
    private List<NewsItem> _allNews = new();

    /// <summary>Под карточкой подписки — один из разделов: устройства, друзья или новости.</summary>
    public IReadOnlyList<PlansSectionItem> Sections { get; } = new[]
    {
        new PlansSectionItem { Label = "Устройства" },
        new PlansSectionItem { Label = "Друзья" },
        new PlansSectionItem { Label = "Новости" },
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDevicesSection), nameof(IsFriendsSection), nameof(IsNewsSection))]
    private int sectionIndex;

    public bool IsDevicesSection => SectionIndex == 0;
    public bool IsFriendsSection => SectionIndex == 1;
    public bool IsNewsSection => SectionIndex == 2;

    /// <summary>Все новости — раздел «Новости» листает их сам (PagedItems).</summary>
    public IReadOnlyList<NewsItem> AllNews => _allNews;

    [ObservableProperty] private string planName = "—";
    [ObservableProperty] private string expiryLabel = "—";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSubscriptionActive))]
    private int daysLeft;
    /// <summary>«АКТИВНА» — только при настоящей подписке с остатком дней (как на Android).</summary>
    public bool IsSubscriptionActive => DaysLeft > 0;
    /// <summary>Длина оплаченного периода (см. SubscriptionPeriod) — кольцо = DaysLeft / DaysTotal.</summary>
    [ObservableProperty] private int daysTotal = 30;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DevicesCountLabel))]
    [NotifyPropertyChangedFor(nameof(DevicesSectionLabel))]
    private int deviceLimit;
    [ObservableProperty] private bool refreshing;
    [ObservableProperty] private bool isNewsExpanded;
    [ObservableProperty] private int newsPageIndex;
    // NullToVisibilityConverter (Converters.cs) сделан специально под строковые свойства
    // (value as string) — для произвольного объекта вроде ReferralUiModel unsafe-каст всегда
    // даёт null, а значит Visibility.Collapsed БЕЗУСЛОВНО, даже когда данные реально пришли
    // с бэкенда (это и оказалось причиной "секции не отображаются", подтверждено: диагностика
    // сети/JSON ничего не показала, потому что сеть тут ни при чём). Поэтому — свои bool-флаги
    // + обычный BoolToVisibilityConverter, а не переиспользование NullToVisibilityConverter.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReferral))]
    private ReferralUiModel? referral;

    public bool HasReferral => Referral != null;

    private long? _subscriptionId;

    // На пробном/бесплатном тарифе — как на сайте, самостоятельное удаление устройства скрыто
    // за "обратитесь в поддержку" (см. комментарий у DeviceDto в ApiModels.cs).
    [ObservableProperty] private bool devicesDeleteSupportOnly;
    public bool HasDevices => _subscriptionId != null;
    /// <summary>"N из M" рядом с заголовком "Устройства" — DeviceLimit уже приходит с
    /// /api/subscriptions, просто раньше нигде не показывался. Пусто, если лимита нет (0).</summary>
    public string DevicesCountLabel => DeviceLimit > 0 ? $"{Devices.Count} из {DeviceLimit}" : "";

    public ObservableCollection<DeviceItem> Devices { get; } = new();

    /// <summary>То, что реально показывает XAML — свёрнутый список (2 последние) или текущая
    /// страница (3 на страницу), в зависимости от IsNewsExpanded. См. RefreshVisibleNews.</summary>
    public ObservableCollection<NewsItem> VisibleNews { get; } = new();

    public int NewsTotalPages => _allNews.Count == 0 ? 1 : (int)Math.Ceiling(_allNews.Count / (double)NewsPageSize);
    public bool NewsHasMultiplePages => IsNewsExpanded && NewsTotalPages > 1;
    public bool HasHiddenNews => _allNews.Count > CollapsedNewsCount;
    public string NewsToggleLabel => IsNewsExpanded ? "Свернуть" : "Показать все новости";
    public string NewsPageLabel => $"Страница {NewsPageIndex + 1} из {NewsTotalPages}";
    /// <summary>Пейджер в шапке секции новостей v5: "‹ 1 / 3 ›".</summary>
    public string NewsPagerLabel => $"{NewsPageIndex + 1} / {NewsTotalPages}";
    public bool ShowNewsShowAll => !IsNewsExpanded && HasHiddenNews;
    public bool ShowNewsPager => IsNewsExpanded && NewsTotalPages > 1;
    public bool HasNews => _allNews.Count > 0;

    /// <summary>Подпись секции устройств v5: "УСТРОЙСТВА · 1 из 5".</summary>
    public string DevicesSectionLabel => DeviceLimit > 0 ? $"УСТРОЙСТВА · {Devices.Count} из {DeviceLimit}" : "УСТРОЙСТВА";

    public PlansViewModel(ApiClient api, SubscriptionRepository subscription)
    {
        _api = api;
        _subscription = subscription;
        // Превью-экземпляр: сразу нужный раздел (GODJI_UI_PREVIEW_PLANS=0..3) для сверки вёрстки.
        if (Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW") == "1" &&
            int.TryParse(Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW_PLANS"), out var previewSection))
            sectionIndex = Math.Clamp(previewSection, 0, Sections.Count - 1);
        // Сразу — всё, что уже сохранено: страница не бывает пустой, даже если сети нет или VPN
        // как раз переподключается. Подписку и новости обновляют и другие экраны/часовой таймер —
        // страница просто следует за репозиторием.
        ShowCached();
        ApplySubscription();
        ApplyNews();
        _subscription.PropertyChanged += OnRepositoryChanged;
        _subscription.AccountCleared += OnAccountCleared;
    }

    // Порт Android b3dff12: страница сначала показывает сохранённое (AccountCache), а в фоне
    // обновляет только разделы старше часа; кнопка «обновить» — всё и сразу. Сбой любого запроса
    // ничего не стирает: на экране остаётся последнее сохранённое. Раньше каждое открытие вкладки
    // заново тянуло всё подряд, а любой сбой (нет сети, VPN переподключается) показывал «0 дней»,
    // «—» и пустые разделы.
    private static readonly TimeSpan PageTtl = TimeSpan.FromHours(1);
    private readonly Dictionary<AccountCache.Key, DateTime> _savedAt = new();
    private List<DeviceDto> _rawDevices = new();

    private void ShowCached()
    {
        if (AccountCache.Load<ReferralsResponse>(AccountCache.Key.Referrals) is { } referrals)
        {
            _savedAt[AccountCache.Key.Referrals] = referrals.SavedAtUtc;
            ApplyReferrals(referrals.Value);
        }
        if (AccountCache.Load<List<DeviceDto>>(AccountCache.Key.Devices) is { } devices)
        {
            _savedAt[AccountCache.Key.Devices] = devices.SavedAtUtc;
            ApplyDevices(devices.Value);
        }
    }

    private bool IsStale(AccountCache.Key key) =>
        !_savedAt.TryGetValue(key, out var at) || DateTime.UtcNow - at > PageTtl;

    /// <summary>Открытие вкладки: подписка — если ей больше часа, остальные разделы — только
    /// устаревшие.</summary>
    public Task LoadAsync() => LoadCoreAsync(force: false);

    private async Task LoadCoreAsync(bool force)
    {
        if (force) await _subscription.RefreshAsync();
        else await _subscription.RefreshIfStaleAsync();
        ApplySubscription();
        ApplyNews();

        var subId = _subscription.Subscription?.Id;
        // referral_enabled не проверяется отдельным запросом настроек:
        // если фичу когда-нибудь выключат на бэкенде, эндпоинт просто перестанет отвечать
        // успешно, и секция тихо не покажется (тот же принцип, что уже у broadcasts/plans).
        if (force || IsStale(AccountCache.Key.Referrals))
            await FetchAsync(AccountCache.Key.Referrals, () => _api.GetReferralsAsync(), ApplyReferrals);
        if (subId is { } id && (force || IsStale(AccountCache.Key.Devices)))
            await FetchAsync(AccountCache.Key.Devices, () => _api.GetDevicesAsync(id), ApplyDevices);
        if (Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW_DEMO") == "1") FillPreviewDemo();
    }

    /// <summary>Удачный ответ — на экран и на диск; сбой — ничего не трогаем.</summary>
    private async Task FetchAsync<T>(AccountCache.Key key, Func<Task<T>> request, Action<T> apply)
    {
        T value;
        try { value = await request(); }
        catch { return; }
        apply(value);
        _savedAt[key] = DateTime.UtcNow;
        AccountCache.Save(key, value);
    }

    private void OnRepositoryChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SubscriptionRepository.Subscription): RunOnUi(ApplySubscription); break;
            case nameof(SubscriptionRepository.Broadcasts): RunOnUi(ApplyNews); break;
        }
    }

    /// <summary>Выход из аккаунта — забыть всё, что показывали для прежнего.</summary>
    private void OnAccountCleared() => RunOnUi(() =>
    {
        _savedAt.Clear();
        Referral = null;
        ApplyDevices(new List<DeviceDto>());
        ApplySubscription();
        ApplyNews();
    });

        private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    private void ApplySubscription()
    {
        var sub = _subscription.Subscription;
        PlanName = sub?.PlanName ?? "—";
        ExpiryLabel = sub?.ExpireAt is { Length: > 0 } iso ? DateFormat.FormatDate(iso) : "—";
        DaysLeft = sub?.DaysLeft ?? 0;
        DaysTotal = sub is { } s0 ? SubscriptionPeriod.TotalDays(s0.Id, s0.DaysLeft) : 30;
        DeviceLimit = sub?.DeviceLimit ?? 0;
        _subscriptionId = sub?.Id;
        DevicesDeleteSupportOnly = sub?.Kind == "trial" || sub?.Kind == "free";
        OnPropertyChanged(nameof(HasDevices));
    }

    private void ApplyNews()
    {
        // Свежая по CreatedAt первая — порядок с бэкенда не гарантирован (см. BroadcastNotifier).
        _allNews = _subscription.Broadcasts.OrderByDescending(b => b.CreatedAt).Select(b =>
        {
            var item = new NewsItem
            {
                Id = b.Id,
                RawContent = b.Content,
                DateLabel = DateFormat.FormatDate(b.CreatedAt),
                Buttons = b.Buttons ?? new List<BroadcastButtonDto>()
            };
            item.BuildCollapsed();
            return item;
        }).ToList();
        IsNewsExpanded = false;
        NewsPageIndex = 0;
        RefreshVisibleNews();
    }

    private void ApplyReferrals(ReferralsResponse r)
    {
        Referral = new ReferralUiModel
        {
            Link = r.Link,
            TotalReferrals = r.Summary.TotalReferrals,
            ActiveReferrals = r.Summary.ActiveReferrals,
            TotalBonusDays = r.Summary.TotalBonusDays,
            Entries = r.Referrals.Select(e => new ReferralEntryItem
            {
                DisplayName = DisplayNameFor(e),
                IsActive = e.IsActive,
                BonusDays = e.BonusDays
            }).ToList()
        };
    }

    private void ApplyDevices(List<DeviceDto> devices)
    {
        _rawDevices = devices;
        Devices.Clear();
        foreach (var d in devices) Devices.Add(ToDeviceItem(d));
        OnPropertyChanged(nameof(DevicesCountLabel));
        OnPropertyChanged(nameof(DevicesSectionLabel));
    }

    /// <summary>Только для превью-экземпляра (GODJI_UI_PREVIEW_DEMO=1, см. App.xaml.cs) — сверка
    /// заполненного экрана с эталоном без реальной сессии. В обычной работе не вызывается.</summary>
    private void FillPreviewDemo()
    {
        PlanName = "EU + RU Mobile";
        ExpiryLabel = "14 октября";
        DaysLeft = 17;
        DeviceLimit = 5;
        _subscriptionId = 1;
        OnPropertyChanged(nameof(HasDevices));
        Devices.Clear();
        Devices.Add(new DeviceItem { Hwid = "a", Name = "Pixel 8", Details = "Android · Goji VPN 1.0.107", CreatedAtLabel = "3 сентября" });
        Devices.Add(new DeviceItem { Hwid = "b", Name = "Рабочий ноутбук", Details = "Windows", CreatedAtLabel = "12 сентября" });
        OnPropertyChanged(nameof(DevicesSectionLabel));
        _allNews = new List<NewsItem>
        {
            new() { Id = "1", RawContent = "**Новый узел в Стамбуле**\n\nДобавили сервер в Турции — удобно, если нужен близкий к России выход с низким пингом.", DateLabel = "25 сентября", Buttons = new List<BroadcastButtonDto> { new() { Text = "Подробнее", Url = "кабинет" } } },
            new() { Id = "2", RawContent = "Обновили приложение для Windows: новый дизайн «Стекло».", DateLabel = "20 сентября", Buttons = new List<BroadcastButtonDto>() },
            new() { Id = "3", RawContent = "Третья новость.", DateLabel = "10 сентября", Buttons = new List<BroadcastButtonDto>() },
        };
        foreach (var n in _allNews) n.BuildCollapsed();
        RefreshVisibleNews();
        Referral = new ReferralUiModel
        {
            Link = "https://t.me/Shadow_Duck_bot?start=ref_48213",
            TotalReferrals = 3, ActiveReferrals = 2, TotalBonusDays = 14,
            Entries = new List<ReferralEntryItem>
            {
                new() { DisplayName = "@alex•••", IsActive = true, BonusDays = 7 },
                new() { DisplayName = "Мари••", IsActive = false, BonusDays = 0 },
            }
        };
    }

    private const int Windows11FirstBuild = 22000;

    /// <summary>«Windows 11» вместо «Windows · 11_10.0.26200», «Android 16» вместо «Android · 16»
    /// (как на Android, PlansViewModel.osLabel).</summary>
    private static string? OsLabel(string? platform, string? osVersion)
    {
        var name = platform?.Trim() ?? "";
        // Happ присылает «11_10.0.26200»: до «_» — маркетинговая версия, дальше — сборка.
        var version = (osVersion?.Trim() ?? "").Split('_')[0];
        if (name.Equals("Windows", StringComparison.OrdinalIgnoreCase) && version.StartsWith("10.0."))
        {
            var parts = version.Split('.');
            version = parts.Length > 2 && int.TryParse(parts[2], out var build) && build >= Windows11FirstBuild ? "11" : "10";
        }
        if (name.Length == 0) return version.Length > 0 ? version : null;
        if (version.Length == 0) return name;
        return version.StartsWith(name, StringComparison.OrdinalIgnoreCase) ? version : $"{name} {version}";
    }

    private static DeviceItem ToDeviceItem(DeviceDto d) => new()
    {
        Hwid = d.Hwid,
        // Как на сайте: своё имя, иначе модель, иначе платформа; под ним — платформа, версия ОС
        // и клиент (User-Agent до первого «/»).
        Name = new[] { d.ReadableName, d.DeviceModel, d.Platform }.Select(x => x?.Trim()).FirstOrDefault(x => !string.IsNullOrEmpty(x))
             ?? d.Hwid[..Math.Min(8, d.Hwid.Length)],
        Details = string.Join(" · ", new[] { OsLabel(d.Platform, d.OsVersion), d.UserAgent?.Split('/')[0] }
            .Select(x => x?.Trim()).Where(x => !string.IsNullOrEmpty(x))),
        CreatedAtLabel = d.CreatedAt is { } ca ? DateFormat.FormatDate(ca) : null
    };

    [RelayCommand]
    private async Task RenameDeviceAsync(DeviceItem device)
    {
        if (_subscriptionId is not { } subId) return;
        var dialog = new PromptDialog
        {
            Owner = Application.Current.MainWindow,
            PromptTitle = "Переименовать устройство",
            InputLabel = "Название",
            InputValue = device.Name,
            PrimaryText = "Сохранить"
        };
        if (dialog.ShowDialog() != true) return;
        var newName = dialog.InputValue.Trim();
        if (newName.Length == 0 || newName == device.Name) return;

        device.IsBusy = true;
        try
        {
            await _api.RenameDeviceAsync(subId, device.Hwid, newName);
            device.Name = newName;
            if (_rawDevices.FirstOrDefault(d => d.Hwid == device.Hwid) is { } dto)
            {
                dto.ReadableName = newName;
                AccountCache.Save(AccountCache.Key.Devices, _rawDevices);
            }
        }
        catch { /* переименование — необязательное действие, молча оставляем прежнее имя */ }
        finally { device.IsBusy = false; }
    }

    [RelayCommand]
    private async Task DeleteDeviceAsync(DeviceItem device)
    {
        if (_subscriptionId is not { } subId) return;

        if (DevicesDeleteSupportOnly)
        {
            var supportDialog = new PromptDialog
            {
                Owner = Application.Current.MainWindow,
                PromptTitle = "Удаление устройства",
                Message = "На пробном и бесплатном тарифах удалить устройство можно только через поддержку. Обратитесь к нам — мы поможем.",
                ShowInput = false,
                PrimaryText = "Написать в поддержку"
            };
            if (supportDialog.ShowDialog() == true) OpenSupport();
            return;
        }

        var confirmDialog = new PromptDialog
        {
            Owner = Application.Current.MainWindow,
            PromptTitle = "Удаление устройства",
            Message = "Вы уверены, что хотите удалить это устройство? Это действие нельзя отменить.",
            ShowInput = false,
            PrimaryText = "Удалить",
            IsPrimaryDanger = true
        };
        if (confirmDialog.ShowDialog() != true) return;

        device.IsBusy = true;
        try
        {
            await _api.DeleteDeviceAsync(subId, device.Hwid);
            Devices.Remove(device);
            _rawDevices.RemoveAll(d => d.Hwid == device.Hwid);
            AccountCache.Save(AccountCache.Key.Devices, _rawDevices);
            OnPropertyChanged(nameof(DevicesCountLabel));
            OnPropertyChanged(nameof(DevicesSectionLabel));
        }
        catch { device.IsBusy = false; }
    }

    /// <summary>Веб-версия маскирует половину имени/юзернейма/локальной части email точками —
    /// повторяем то же самое, а не показываем приглашённых пользователей полностью открытым
    /// текстом (это не наши данные, а личные данные третьих лиц).</summary>
    private static string MaskHalf(string s)
    {
        if (s.Length == 0) return s;
        var visible = (s.Length + 1) / 2;
        return s[..visible] + new string('•', s.Length - visible);
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        return at < 0 ? MaskHalf(email) : MaskHalf(email[..at]) + email[at..];
    }

    private static string DisplayNameFor(ReferralEntry e)
    {
        if (!string.IsNullOrWhiteSpace(e.TgUsername)) return "@" + MaskHalf(e.TgUsername);
        var fullName = string.Join(" ", new[] { e.TgFirstName, e.TgLastName }.Where(s => !string.IsNullOrEmpty(s))).Trim();
        if (fullName.Length > 0) return MaskHalf(fullName);
        if (!string.IsNullOrWhiteSpace(e.Email)) return MaskEmail(e.Email);
        return $"ID: {e.RefereeTelegramId ?? e.RefereeId ?? 0}";
    }

    [RelayCommand]
    private void CopyReferralLink()
    {
        if (Referral is { } r) Clipboard.SetText(r.Link);
    }

    /// <summary>Перенос входа по QR: в коде только почта аккаунта (godjivpn://transfer?e=…) —
    /// телефон подставит её и попросит код из письма (как Android и iOS, 2026-10-28). Раньше в код
    /// клали саму сессию: снимок экрана давал вход навсегда, а два устройства с одним
    /// refresh-токеном выбивали друг друга из аккаунта.</summary>
    [RelayCommand]
    private async Task ShowTransferQrAsync()
    {
        string? email = null;
        try { email = (await _api.GetMeAsync()).Email; }
        catch { /* нет сети — ниже понятное сообщение */ }
        if (string.IsNullOrWhiteSpace(email))
        {
            await ShowTransferErrorAsync();
            return;
        }
        var uri = "godjivpn://transfer?e=" + Uri.EscapeDataString(email);
        new QrWindow("Вход на другом устройстве",
            "На новом устройстве открой Goji → «Войти по QR-коду» и наведи камеру на код. На твою почту придёт код — введи его там. Устройство займёт одно место в лимите подписки.",
            uri,
            warning: "В коде только адрес почты: без кода из письма по нему не войти.") { Owner = Application.Current.MainWindow }.ShowDialog();
    }

    private static Task ShowTransferErrorAsync()
    {
        new PromptDialog
        {
            Owner = Application.Current.MainWindow,
            PromptTitle = "Не удалось открыть QR-код",
            Message = "Проверь интернет и попробуй ещё раз.",
            ShowInput = false,
            PrimaryText = "Понятно"
        }.ShowDialog();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void ShowReferralQr()
    {
        if (Referral is not { } r || string.IsNullOrEmpty(r.Link)) return;
        new QrWindow("Пригласи друга",
            "Друг наводит камеру телефона на код и сразу открывает твою пригласительную ссылку.",
            r.Link, r.Link) { Owner = Application.Current.MainWindow }.ShowDialog();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        // Кнопка «обновить» — всё сразу, без оглядки на возраст сохранённого; спиннер ждёт ответов.
        Refreshing = true;
        try { await LoadCoreAsync(force: true); }
        finally { Refreshing = false; }
    }

    /// <summary>«Оплатить в личном кабинете» — продление и смена тарифа только на сайте.</summary>
    [RelayCommand]
    private void OpenCabinet() => OpenUrl(CabinetUrl);

    /// <summary>Раньше открывала внешний браузер на /#/support-chat — теперь нативный чат
    /// поддержки прямо в приложении (см. Views/SupportWindow). Порт из Android (720f5ff).</summary>
    [RelayCommand]
    private void OpenSupport() => new SupportWindow(_api) { Owner = Application.Current.MainWindow }.ShowDialog();

    [RelayCommand]
    private void OpenBroadcastButton(string url) => OpenUrl(url);

    [RelayCommand]
    private void ToggleNewsExpanded()
    {
        IsNewsExpanded = !IsNewsExpanded;
        NewsPageIndex = 0;
        RefreshVisibleNews();
    }

    [RelayCommand(CanExecute = nameof(CanGoNewsPrevPage))]
    private void NewsPrevPage() { NewsPageIndex--; RefreshVisibleNews(); }
    private bool CanGoNewsPrevPage() => NewsPageIndex > 0;

    [RelayCommand(CanExecute = nameof(CanGoNewsNextPage))]
    private void NewsNextPage() { NewsPageIndex++; RefreshVisibleNews(); }
    private bool CanGoNewsNextPage() => NewsPageIndex < NewsTotalPages - 1;

    /// <summary>Свёрнутый вид — 2 последние новости целиком, без пагинации (см.
    /// CollapsedNewsCount). Развёрнутый — постранично по NewsPageSize (см. IsNewsExpanded).</summary>
    private void RefreshVisibleNews()
    {
        OnPropertyChanged(nameof(AllNews));
        VisibleNews.Clear();
        var page = IsNewsExpanded
            ? _allNews.Skip(NewsPageIndex * NewsPageSize).Take(NewsPageSize)
            : _allNews.Take(CollapsedNewsCount);
        foreach (var item in page) VisibleNews.Add(item);

        OnPropertyChanged(nameof(NewsTotalPages));
        OnPropertyChanged(nameof(NewsHasMultiplePages));
        OnPropertyChanged(nameof(HasHiddenNews));
        OnPropertyChanged(nameof(NewsToggleLabel));
        OnPropertyChanged(nameof(NewsPageLabel));
        OnPropertyChanged(nameof(NewsPagerLabel));
        OnPropertyChanged(nameof(ShowNewsShowAll));
        OnPropertyChanged(nameof(ShowNewsPager));
        OnPropertyChanged(nameof(HasNews));
        NewsPrevPageCommand.NotifyCanExecuteChanged();
        NewsNextPageCommand.NotifyCanExecuteChanged();
    }

    private static void OpenUrl(string url) => UrlLauncher.TryOpen(url);
}
