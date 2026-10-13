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

/// <summary>Только статус/сводка партнёрской программы — подача заявки и запрос вывода средств
/// делаются на сайте (та же логика, что и "Продлить" для тарифов: не переизобретаем денежные
/// формы нативно). Computed-флаги ниже — чтобы XAML мог выбирать нужный блок простым
/// Visibility-биндингом, без аналога Kotlin `when` в разметке.</summary>
public sealed class PartnerUiModel
{
    public required bool IsPartner { get; init; }
    public required bool IsActive { get; init; }
    public required string? ApplicationStatus { get; init; }
    public required double CommissionRate { get; init; }
    public required int ClientCount { get; init; }
    public required double TotalEarned { get; init; }
    public required double AvailableBalance { get; init; }
    public required double PendingBalance { get; init; }

    public bool IsDeactivated => IsPartner && !IsActive;
    public bool IsActivePartner => IsPartner && IsActive;
    public bool IsPending => !IsPartner && ApplicationStatus == "pending";
    public bool IsRejected => !IsPartner && ApplicationStatus == "rejected";
    public bool IsNotApplied => !IsPartner && ApplicationStatus == null;
    public bool ShowApplyButton => IsRejected || IsNotApplied;
    public bool ShowActionButton => IsActivePartner || ShowApplyButton;
    public string ActionButtonLabel => IsActivePartner ? "Открыть кабинет" : "Подать заявку";
    public bool ShowPendingBalance => PendingBalance > 0;
    public string CommissionLabel => $"{CommissionRate}%";
    public string EarnedLabel => $"{(int)TotalEarned} ₽";
    public string AvailableBalanceLabel => $"{(int)AvailableBalance} ₽";
    public string PendingBalanceLabel => $"{(int)PendingBalance} ₽";
}

/// <summary>Одно устройство подписки (см. DeviceDto) — busy гасит кнопки переименования/
/// удаления на время запроса, как busy в Android DeviceUi.</summary>
public sealed partial class DeviceItem : ObservableObject
{
    public required string Hwid { get; init; }
    [ObservableProperty] private string name = "";
    public string? Platform { get; init; }
    public string? CreatedAtLabel { get; init; }
    /// <summary>Через что реально зарегистрировалось устройство на бэкенде (User-Agent запроса
    /// подписки) — тот же клиент бэкенд запишет для ЛЮБОГО устройства на Goji (см.
    /// SubscriptionService, там он намеренно подменяется на "v2rayNG/..."), отдаём как есть.</summary>
    public string? ConnectedVia { get; init; }
    [ObservableProperty] private bool isBusy;

    public string Subtitle => string.Join(" · ", new[]
    {
        Platform,
        !string.IsNullOrEmpty(ConnectedVia) ? $"Через {ConnectedVia}" : null,
        CreatedAtLabel
    }.Where(s => !string.IsNullOrEmpty(s)));
}

public sealed partial class PeriodItem : ObservableObject
{
    public required int Months { get; init; }
    public required string Label { get; init; }
    [ObservableProperty] private bool isSelected;
}

/// <summary>Длинные описания тарифов раньше разворачивали карточку на пол-экрана — сжимаем до
/// приблизительно 2 строк и прячем остальное за "читать полностью" (порт из Android
/// PlansScreen.kt: там — реальный hasVisualOverflow с TextLayout, здесь — эквивалент по длине
/// строки, WPF не даёt такой колбэк без отдельного измерения текста, а на глаз для карточек
/// тарифов с их фиксированной шириной это даёт тот же результат).</summary>
public sealed partial class PlanItem : ObservableObject
{
    private const int CollapsedCharLimit = 90;

    public required long Id { get; init; }
    public required string Name { get; init; }
    public required string FullDescription { get; init; }
    public required string PriceLabel { get; init; }
    public required bool IsCurrent { get; init; }
    /// <summary>Реальная единица периода у ЭТОЙ конкретной цены (обычно "month", но не
    /// гарантия — берём как есть) — нужна для defaultPeriodUnit в ссылке "Продлить".</summary>
    public required string PeriodUnit { get; init; }

    [ObservableProperty] private bool isExpanded;

    public bool ShowDescriptionToggle => FullDescription.Length > CollapsedCharLimit;
    public string Description => IsExpanded || !ShowDescriptionToggle
        ? FullDescription
        : FullDescription[..CollapsedCharLimit].TrimEnd() + "…";
    public string ToggleLabel => IsExpanded ? "Свернуть" : "Читать полностью";

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(ToggleLabel));
    }

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}

/// <summary>Аналог PlansScreen.kt/PlansViewModel.kt — своя подписка, DaysRing, тарифы
/// (информационно, покупка — только по внешней ссылке на сайт, в API её нет).</summary>
/// <summary>Раздел экрана «Подписка» (переключатель под карточкой подписки).</summary>
public sealed class PlansSectionItem
{
    public required string Label { get; init; }
}

public sealed partial class PlansViewModel : ObservableObject
{
    private const string RenewUrl = "https://gojihub.xyz/#/plans";

    // Свёрнутый вид (по умолчанию) — только 2 последние новости, без пагинации. Развёрнутый
    // (по клику "Показать все") — постранично, максимум 3 на странице.
    private const int CollapsedNewsCount = 2;
    private const int NewsPageSize = 3;

    private readonly ApiClient _api;
    private readonly SubscriptionRepository _subscription;
    private readonly TokenStore _tokenStore;
    private List<PlanInfo> _rawPlans = new();
    private List<NewsItem> _allNews = new();

    /// <summary>Без прокрутки «Подписка» разбита на разделы: тарифы, устройства, друзья, новости —
    /// под карточкой подписки виден один из них.</summary>
    public IReadOnlyList<PlansSectionItem> Sections { get; } = new[]
    {
        new PlansSectionItem { Label = "Тарифы" },
        new PlansSectionItem { Label = "Устройства" },
        new PlansSectionItem { Label = "Друзья" },
        new PlansSectionItem { Label = "Новости" },
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTariffsSection), nameof(IsDevicesSection), nameof(IsFriendsSection), nameof(IsNewsSection))]
    private int sectionIndex;

    public bool IsTariffsSection => SectionIndex == 0;
    public bool IsDevicesSection => SectionIndex == 1;
    public bool IsFriendsSection => SectionIndex == 2;
    public bool IsNewsSection => SectionIndex == 3;

    /// <summary>Все новости — раздел «Новости» листает их сам (PagedItems).</summary>
    public IReadOnlyList<NewsItem> AllNews => _allNews;

    [ObservableProperty] private string planName = "—";
    [ObservableProperty] private string expiryLabel = "—";
    [ObservableProperty] private int daysLeft;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DevicesCountLabel))]
    [NotifyPropertyChangedFor(nameof(DevicesSectionLabel))]
    private int deviceLimit;
    [ObservableProperty] private string? customerId;
    [ObservableProperty] private bool refreshing;
    [ObservableProperty] private int selectedMonths = 1;
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
    [NotifyPropertyChangedFor(nameof(HasProgram))]
    [NotifyPropertyChangedFor(nameof(ShowProgramTabs))]
    [NotifyPropertyChangedFor(nameof(ShowReferralCard))]
    [NotifyPropertyChangedFor(nameof(ShowPartnerCard))]
    private ReferralUiModel? referral;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPartner))]
    [NotifyPropertyChangedFor(nameof(HasProgram))]
    [NotifyPropertyChangedFor(nameof(ShowProgramTabs))]
    [NotifyPropertyChangedFor(nameof(ShowReferralCard))]
    [NotifyPropertyChangedFor(nameof(ShowPartnerCard))]
    private PartnerUiModel? partner;

    public bool HasReferral => Referral != null;
    public bool HasPartner => Partner != null;

    /// <summary>Рефералы и партнёрка раньше были двумя отдельными секциями подряд — визуально
    /// дублировали друг друга (у обеих ссылка/сводка/список). Объединены в одно меню
    /// "Программа" с переключателем вкладок, когда доступны обе сразу; если доступна только
    /// одна — показываем её карточку без лишнего переключателя. Порт из Android
    /// PlansScreen.kt (ProgramSection).</summary>
    [ObservableProperty] private int programTab;

    public bool HasProgram => HasReferral || HasPartner;
    public bool ShowProgramTabs => HasReferral && HasPartner;
    public bool ShowReferralCard => HasReferral && (!HasPartner || ProgramTab == 0);
    public bool ShowPartnerCard => HasPartner && (!HasReferral || ProgramTab == 1);

    partial void OnProgramTabChanged(int value)
    {
        OnPropertyChanged(nameof(ShowReferralCard));
        OnPropertyChanged(nameof(ShowPartnerCard));
    }

    [RelayCommand]
    private void SelectProgramTab(string tab) => ProgramTab = tab == "partner" ? 1 : 0;

    private long? _subscriptionId;

    // На пробном/бесплатном тарифе — как на сайте, самостоятельное удаление устройства скрыто
    // за "обратитесь в поддержку" (см. комментарий у DeviceDto в ApiModels.cs).
    [ObservableProperty] private bool devicesDeleteSupportOnly;
    public bool HasDevices => _subscriptionId != null;
    /// <summary>"N из M" рядом с заголовком "Устройства" — DeviceLimit уже приходит с
    /// /api/subscriptions, просто раньше нигде не показывался. Пусто, если лимита нет (0).</summary>
    public string DevicesCountLabel => DeviceLimit > 0 ? $"{Devices.Count} из {DeviceLimit}" : "";

    /// <summary>customer_discount_percent с /api/dashboard/plans — 0, если у клиента нет
    /// персональной скидки; тогда бейдж просто не показываем.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PersonalDiscountLabel))]
    private int personalDiscountPercent;

    public string PersonalDiscountLabel => $"🏷️ Персональная скидка: −{PersonalDiscountPercent}%";

    public ObservableCollection<PeriodItem> Periods { get; } = new();
    public ObservableCollection<PlanItem> Plans { get; } = new();
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

    /// <summary>Индекс выбранного периода для сегмент-контрола (двусторонний биндинг).</summary>
    [ObservableProperty] private int selectedPeriodIndex;
    private bool _syncingPeriodIndex;

    partial void OnSelectedPeriodIndexChanged(int value)
    {
        if (_syncingPeriodIndex || value < 0 || value >= Periods.Count) return;
        SelectPeriod(Periods[value].Months);
    }

    public PlansViewModel(ApiClient api, SubscriptionRepository subscription, TokenStore tokenStore)
    {
        _api = api;
        _subscription = subscription;
        _tokenStore = tokenStore;
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
        if (AccountCache.Load<PlansResponse>(AccountCache.Key.Plans) is { } plans)
        {
            _savedAt[AccountCache.Key.Plans] = plans.SavedAtUtc;
            ApplyPlans(plans.Value);
        }
        if (AccountCache.Load<ReferralsResponse>(AccountCache.Key.Referrals) is { } referrals)
        {
            _savedAt[AccountCache.Key.Referrals] = referrals.SavedAtUtc;
            ApplyReferrals(referrals.Value);
        }
        if (AccountCache.Load<PartnerStatusResponse>(AccountCache.Key.Partner) is { } partner)
        {
            _savedAt[AccountCache.Key.Partner] = partner.SavedAtUtc;
            ApplyPartner(partner.Value);
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
        if (force || IsStale(AccountCache.Key.Plans))
            await FetchAsync(AccountCache.Key.Plans, () => _api.GetPlansAsync(subId), ApplyPlans);
        // referral_enabled/partner_program_enabled не проверяются отдельным запросом настроек:
        // если фичу когда-нибудь выключат на бэкенде, эндпоинт просто перестанет отвечать
        // успешно, и секция тихо не покажется (тот же принцип, что уже у broadcasts/plans).
        if (force || IsStale(AccountCache.Key.Referrals))
            await FetchAsync(AccountCache.Key.Referrals, () => _api.GetReferralsAsync(), ApplyReferrals);
        if (subId is { } id && (force || IsStale(AccountCache.Key.Devices)))
            await FetchAsync(AccountCache.Key.Devices, () => _api.GetDevicesAsync(id), ApplyDevices);
        if (force || IsStale(AccountCache.Key.Partner))
            await FetchAsync(AccountCache.Key.Partner, () => _api.GetPartnerStatusAsync(), ApplyPartner);

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
            case nameof(SubscriptionRepository.SelectedId):
            case nameof(SubscriptionRepository.Nodes): RunOnUi(() => CustomerId = _subscription.SelectedNode?.Uuid); break;
        }
    }

    /// <summary>Выход из аккаунта — забыть всё, что показывали для прежнего.</summary>
    private void OnAccountCleared() => RunOnUi(() =>
    {
        _savedAt.Clear();
        _rawPlans = new List<PlanInfo>();
        Periods.Clear();
        Plans.Clear();
        PersonalDiscountPercent = 0;
        Referral = null;
        Partner = null;
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
        DeviceLimit = sub?.DeviceLimit ?? 0;
        CustomerId = _subscription.SelectedNode?.Uuid;
        _subscriptionId = sub?.Id;
        DevicesDeleteSupportOnly = sub?.Kind == "trial" || sub?.Kind == "free";
        OnPropertyChanged(nameof(HasDevices));
        // «Текущий» тариф в списке определяется по имени — пересобрать карточки под новую подписку.
        if (_rawPlans.Count > 0) RebuildPlanCards();
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

    private void ApplyPlans(PlansResponse response)
    {
        _rawPlans = response.Plans;
        PersonalDiscountPercent = (int)(response.CustomerDiscountPercent ?? 0);

        var months = _rawPlans.SelectMany(p => p.Prices)
            .Where(p => p.PriceType == "base")
            .Select(p => p.PeriodValue)
            .Distinct().OrderBy(m => m).ToList();
        // Тихое обновление не сбрасывает выбранный пользователем период.
        var keepSelection = Periods.Count > 0 && months.Contains(SelectedMonths);

        Periods.Clear();
        foreach (var m in months)
            Periods.Add(new PeriodItem { Months = m, Label = m == 1 ? "1 месяц" : $"{m} мес." });

        if (!keepSelection) SelectedMonths = months.FirstOrDefault(1);
        RebuildPlanCards();
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

    private void ApplyPartner(PartnerStatusResponse p)
    {
        Partner = new PartnerUiModel
        {
            IsPartner = p.IsPartner,
            IsActive = p.Partner?.IsActive ?? false,
            ApplicationStatus = p.Application?.Status,
            CommissionRate = p.Partner?.CommissionRate ?? 0,
            ClientCount = p.Stats?.ClientCount ?? 0,
            TotalEarned = p.Partner?.TotalEarned ?? 0,
            AvailableBalance = p.Partner?.AvailableBalance ?? 0,
            PendingBalance = p.Partner?.PendingBalance ?? 0
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
        PersonalDiscountPercent = 10;
        _subscriptionId = 1;
        OnPropertyChanged(nameof(HasDevices));
        Devices.Clear();
        Devices.Add(new DeviceItem { Hwid = "a", Name = "Pixel 8", Platform = "Android", ConnectedVia = "Goji VPN 1.0.107", CreatedAtLabel = "3 сентября" });
        Devices.Add(new DeviceItem { Hwid = "b", Name = "Рабочий ноутбук", Platform = "Windows", CreatedAtLabel = "12 сентября" });
        OnPropertyChanged(nameof(DevicesSectionLabel));
        _rawPlans = new List<PlanInfo>
        {
            new() { Id = 1, Name = "EU Lite", Description = "1 устройство · Европа · ПЛОТЬ", Prices = { new PriceInfo { PriceType = "base", Price = 149, Currency = "₽", PeriodValue = 1, PeriodUnit = "month" }, new PriceInfo { PriceType = "base", Price = 402, Currency = "₽", PeriodValue = 3, PeriodUnit = "month" } } },
            new() { Id = 2, Name = "EU + RU Mobile", Description = "3 устройства · Европа и Россия · LTE-узлы для 3G/4G, чтобы работать в обход ограничений мобильных сетей", Prices = { new PriceInfo { PriceType = "base", Price = 290, Currency = "₽", PeriodValue = 1, PeriodUnit = "month" }, new PriceInfo { PriceType = "base", Price = 672, Currency = "₽", PeriodValue = 3, PeriodUnit = "month" } } },
            new() { Id = 3, Name = "Family", Description = "5 устройств · все узлы", Prices = { new PriceInfo { PriceType = "base", Price = 590, Currency = "₽", PeriodValue = 1, PeriodUnit = "month" }, new PriceInfo { PriceType = "base", Price = 1212, Currency = "₽", PeriodValue = 3, PeriodUnit = "month" } } },
        };
        Periods.Clear();
        Periods.Add(new PeriodItem { Months = 1, Label = "1 месяц" });
        Periods.Add(new PeriodItem { Months = 3, Label = "3 мес." });
        SelectedMonths = 3;
        RebuildPlanCards();
        _allNews = new List<NewsItem>
        {
            new() { Id = "1", RawContent = "**Новый узел в Стамбуле**\n\nДобавили сервер в Турции — удобно, если нужен близкий к России выход с низким пингом.", DateLabel = "25 сентября", Buttons = new List<BroadcastButtonDto> { new() { Text = "Подробнее", Url = "https://gojihub.xyz" } } },
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
        Partner = new PartnerUiModel { IsPartner = false, IsActive = false, ApplicationStatus = null, CommissionRate = 0, ClientCount = 0, TotalEarned = 0, AvailableBalance = 0, PendingBalance = 0 };
    }

    private static DeviceItem ToDeviceItem(DeviceDto d) => new()
    {
        Hwid = d.Hwid,
        Name = !string.IsNullOrWhiteSpace(d.ReadableName) ? d.ReadableName
             : !string.IsNullOrWhiteSpace(d.Platform) ? d.Platform
             : d.Hwid[..Math.Min(8, d.Hwid.Length)],
        Platform = d.Platform,
        CreatedAtLabel = d.CreatedAt is { } ca ? DateFormat.FormatDate(ca) : null,
        ConnectedVia = !string.IsNullOrWhiteSpace(d.UserAgent) ? d.UserAgent : null
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

    /// <summary>Перенос входа по QR (порт 7060e31): в код кладётся текущая сессия — тот же
    /// JWT и refresh-токен, что в TokenStore, — и момент создания. Бэкенд принимает JWT как
    /// обычный Bearer с любого устройства; новое устройство само займёт место в лимите.
    /// Приложение Goji на телефоне проверяет срок (10 минут) и саму сессию до сохранения.</summary>
    [RelayCommand]
    private void ShowTransferQr()
    {
        var token = _tokenStore.AccessToken;
        if (string.IsNullOrEmpty(token)) return;
        var uri = "godjivpn://transfer?s=" + Uri.EscapeDataString(token);
        if (_tokenStore.RefreshToken is { Length: > 0 } refresh) uri += "&r=" + Uri.EscapeDataString(refresh);
        uri += "&t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        new QrWindow("Вход на другом устройстве",
            "На новом устройстве открой Goji → «Войти по QR-коду» и наведи камеру на код. Устройство займёт одно место в лимите подписки.",
            uri,
            warning: "Код даёт вход в твой аккаунт — не показывай и не отправляй его посторонним. Действует 10 минут.",
            autoClose: TimeSpan.FromMinutes(10)) { Owner = Application.Current.MainWindow }.ShowDialog();
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
    private void OpenPartnerDashboard() => OpenUrl("https://gojihub.xyz/#/partner-dashboard");

    [RelayCommand]
    private void SelectPeriod(int months)
    {
        SelectedMonths = months;
        foreach (var p in Periods) p.IsSelected = p.Months == months;
        RebuildPlanCards();
    }

    private void RebuildPlanCards()
    {
        Plans.Clear();
        foreach (var plan in _rawPlans)
        {
            var price = plan.Prices.FirstOrDefault(p => p.PriceType == "base" && p.PeriodValue == SelectedMonths)
                ?? plan.Prices.FirstOrDefault(p => p.PriceType == "base");
            var priceLabel = price != null ? $"{price.Price} {price.Currency}" : "—";
            Plans.Add(new PlanItem
            {
                Id = plan.Id,
                Name = plan.Name,
                FullDescription = plan.Description,
                PriceLabel = priceLabel,
                IsCurrent = plan.Name == PlanName,
                PeriodUnit = price?.PeriodUnit ?? "month"
            });
        }
        foreach (var p in Periods) p.IsSelected = p.Months == SelectedMonths;
        _syncingPeriodIndex = true;
        SelectedPeriodIndex = Math.Max(0, Periods.ToList().FindIndex(p => p.Months == SelectedMonths));
        _syncingPeriodIndex = false;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        // Кнопка «обновить» — всё сразу, без оглядки на возраст сохранённого; спиннер ждёт ответов.
        Refreshing = true;
        try { await LoadCoreAsync(force: true); }
        finally { Refreshing = false; }
    }

    /// <summary>Открывает сразу /checkout с уже известным тарифом и периодом — раньше кнопка
    /// вела на общий /#/plans, откуда пришлось бы заново выбирать тариф на сайте, хотя
    /// "Продлить" уже подразумевает именно текущий тариф на уже выбранный здесь период. Сама
    /// оплата всё равно происходит на странице платёжного шлюза (ЮKassa/Т-Банк/Robokassa/…) —
    /// приложение не участвует в передаче данных карты. Без определённого текущего тарифа
    /// (например список ещё не подгрузился) — прежнее поведение, общий /#/plans.</summary>
    [RelayCommand]
    private void OpenRenew()
    {
        var currentPlan = Plans.FirstOrDefault(p => p.IsCurrent);
        var url = currentPlan != null
            ? $"https://gojihub.xyz/#/checkout?plan={currentPlan.Id}&defaultPeriod={SelectedMonths}&defaultPeriodUnit={currentPlan.PeriodUnit}"
            : RenewUrl;
        OpenUrl(url);
    }

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

    [RelayCommand]
    private void CopyCustomerId()
    {
        if (!string.IsNullOrEmpty(CustomerId)) Clipboard.SetText(CustomerId);
    }

    private static void OpenUrl(string url) => UrlLauncher.TryOpen(url);
}
