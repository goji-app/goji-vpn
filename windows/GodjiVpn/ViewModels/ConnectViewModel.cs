using System.Globalization;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GodjiVpn.Services;
using GodjiVpn.Utils;

namespace GodjiVpn.ViewModels;

/// <summary>Аналог ConnectViewModel.kt/ConnectScreen.kt (Android) — заголовок/статус-строка,
/// приветствие поверх глобуса, карточка текущего узла, скорость/трафик. Авто-переключение на
/// резервный узел при глушении мобильной сети сознательно не переносится — специфично для SIM,
/// на десктопе такого сценария нет (решение принято с пользователем). Плашка сети справа в шапке
/// показывает тип активного подключения компьютера (Wi-Fi/Ethernet) вместо Wi-Fi/Мобильная.</summary>
public sealed partial class ConnectViewModel : ObservableObject, IDisposable
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    private readonly VpnEngine _vpnEngine;
    private readonly SubscriptionRepository _subscription;
    private readonly DispatcherTimer _speedTimer;
    private readonly DispatcherTimer _qualityTimer;
    private readonly Queue<int> _qualitySamples = new();
    private bool _qualityWarmedUp;
    private int _journalTick;

    // Замер качества идёт по обычному маршруту системы — пока VPN включён, это и есть туннель.
    // Один клиент на всё время: соединение переиспользуется, и замер показывает задержку канала,
    // а не каждый раз заново TLS-рукопожатие.
    private static readonly System.Net.Http.HttpClient ProbeClient = new(new System.Net.Http.SocketsHttpHandler
    {
        UseProxy = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
    }) { Timeout = TimeSpan.FromSeconds(5) };

    private long _lastRx;
    private long _lastTx;
    private DateTime _lastSampleUtc;

    [ObservableProperty] private bool isConnected;
    [ObservableProperty] private bool isConnecting;
    [ObservableProperty] private string? errorMessage;

    [ObservableProperty] private string headline = "Пока без защиты";
    [ObservableProperty] private string subline = "Провайдер видит всё, что ты открываешь";

    [ObservableProperty] private string currentNodeName = "Выбрать за меня";
    [ObservableProperty] private string currentNodeMeta = "";
    [ObservableProperty] private string currentFlag = "🌐";
    [ObservableProperty] private string? currentFlagImagePath;

    [ObservableProperty] private string greetingHi = "Hello";
    [ObservableProperty] private string greetingSub = "«хелло» · английский";

    [ObservableProperty] private string globeStatus = "off";
    [ObservableProperty] private CountryGeo? globeGeo;

    [ObservableProperty] private string planName = "—";
    [ObservableProperty] private string expiryLabel = "—";
    [ObservableProperty] private int daysLeft;
    [ObservableProperty] private double usedGb;
    [ObservableProperty] private double quotaGb;
    [ObservableProperty] private bool isUnlimited;

    [ObservableProperty] private string trafficValueLabel = "0,0 ГБ / ∞";
    [ObservableProperty] private string trafficExpiryLabel = "";
    [ObservableProperty] private double trafficFraction;
    [ObservableProperty] private bool showTrafficBar;

    // ── Плитки «Главной» M3 Expressive (порт Android ConnectScreen 1.0.117 / b3c3350) ──
    private const int SpeedHistorySize = 30;
    private const int QualityWindow = 6;
    private readonly List<double> _downHistory = new();
    private readonly List<double> _upHistory = new();

    /// <summary>Скорость за последние ~30 с (МБ/с) — для мини-графика на плитке скорости.</summary>
    [ObservableProperty] private IReadOnlyList<double> downHistory = Array.Empty<double>();
    [ObservableProperty] private IReadOnlyList<double> upHistory = Array.Empty<double>();

    /// <summary>Качество канала: оценка «—»/0..100, словесная оценка, пинг и потери; Level —
    /// excellent/good/fair/poor/none для цвета цифры.</summary>
    [ObservableProperty] private string qualityValue = "—";
    [ObservableProperty] private string qualityLabel = "";
    [ObservableProperty] private string qualityDetail = "Подключись — и покажем";
    [ObservableProperty] private string qualityLevel = "none";

    /// <summary>Подписка: «17 дней», сколько из 5 полосок заполнено, их цвет (ok/warm/danger) и
    /// строка «до 14 октября · трафик».</summary>
    [ObservableProperty] private string subDaysLabel = "0 дней";
    [ObservableProperty] private int subBars;
    [ObservableProperty] private string subBarLevel = "danger";
    [ObservableProperty] private string subDetail = "";

    /// <summary>Строка журнала сети в карточке узла.</summary>
    [ObservableProperty] private string journalLine = "Сегодня VPN ещё не включался";

    [ObservableProperty] private string downSpeedLabel = "0,0";
    [ObservableProperty] private string upSpeedLabel = "0,0";
    [ObservableProperty] private string connectedTimeLabel = "00:00:00";

    /// <summary>Тип активного подключения компьютера — плашка справа в шапке (NetworkPill).</summary>
    [ObservableProperty] private string netLabel = "Сеть";
    [ObservableProperty] private bool hasNetwork = true;

    public event Action? NavigateToPlansRequested;
    public event Action? NavigateToServersRequested;

    [RelayCommand]
    private void OpenTraffic() => NavigateToPlansRequested?.Invoke();

    /// <summary>Открыть «Журнал сети» (Настройки → Подключение).</summary>
    public event Action? OpenJournalRequested;

    [RelayCommand]
    private void OpenJournal() => OpenJournalRequested?.Invoke();

    /// <summary>Быстрая смена узла с Главной (Android 38457d4): строка узла открывает "Серверы".</summary>
    [RelayCommand]
    private void OpenServers() => NavigateToServersRequested?.Invoke();

    private readonly PingSettings _pingSettings;

    public ConnectViewModel(VpnEngine vpnEngine, SubscriptionRepository subscription, PingSettings pingSettings)
    {
        _vpnEngine = vpnEngine;
        _subscription = subscription;
        _pingSettings = pingSettings;

        _vpnEngine.PropertyChanged += OnVpnEnginePropertyChanged;
        _subscription.PropertyChanged += OnSubscriptionPropertyChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;

        _speedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _speedTimer.Tick += (_, _) => SampleSpeed();
        _qualityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _qualityTimer.Tick += async (_, _) => await ProbeQualityAsync();
        NetworkJournal.Changed += OnJournalChanged;

        RefreshFromState();
        RefreshNetwork();
    }

    /// <summary>Только для превью-экземпляра (GODJI_UI_PREVIEW_DEMO=1) — сверка заполненных плиток
    /// «Главной» без настоящего подключения. В обычной работе не вызывается.</summary>
    private void FillPreviewDemo()
    {
        var wave = Enumerable.Range(0, SpeedHistorySize).Select(i => 4 + 3 * Math.Sin(i / 3.0) + (i % 5) * 0.6).ToArray();
        DownHistory = wave;
        UpHistory = wave.Select(v => v * 0.25).ToArray();
        DownSpeedLabel = wave[^1].ToString("0.0", Ru);
        UpSpeedLabel = (wave[^1] * 0.25).ToString("0.0", Ru);
        QualityValue = "92";
        QualityLabel = "отлично";
        QualityLevel = "excellent";
        QualityDetail = "Пинг 48 мс · потерь нет";
        SubDaysLabel = "17 дней";
        SubBars = 3;
        SubBarLevel = "ok";
        SubDetail = "до 14 октября · 12,4 ГБ / ∞";
    }

    public async Task LoadAsync()
    {
        // При запуске серверов ещё нет — обновление пойдёт сразу; после повторного входа в
        // приложение свежая (меньше часа) подписка заново не запрашивается.
        await _subscription.RefreshIfStaleAsync();
        RefreshFromState();
    }

    private bool CanToggle => !IsConnecting;

    [RelayCommand(CanExecute = nameof(CanToggle))]
    private async Task ToggleAsync()
    {
        ErrorMessage = null;
        if (IsConnected)
        {
            await _vpnEngine.DisconnectAsync();
            return;
        }

        var node = _subscription.SelectedNode;
        if (node == null)
        {
            ErrorMessage = "Нет доступного сервера — выберите его на вкладке «Серверы»";
            return;
        }
        await _vpnEngine.ConnectAsync(node);
    }

    private void OnVpnEnginePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        RunOnUiThread(RefreshFromState);

    private void OnSubscriptionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        RunOnUiThread(RefreshFromState);

    private void OnNetworkChanged(object? sender, EventArgs e) => RunOnUiThread(RefreshNetwork);

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) => RunOnUiThread(RefreshNetwork);

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    /// <summary>Физическое подключение с маршрутом по умолчанию, не считая самого туннеля (Wintun)
    /// и прочих виртуальных адаптеров.</summary>
    private void RefreshNetwork()
    {
        try
        {
            var active = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                            && !n.Description.Contains("Wintun", StringComparison.OrdinalIgnoreCase)
                            && !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
                            && !n.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
                            && n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                                                                           && !g.Address.Equals(System.Net.IPAddress.Any)))
                .ToList();
            var wifi = active.FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
            if (wifi != null) { NetLabel = "Wi-Fi"; HasNetwork = true; }
            else if (active.Count > 0) { NetLabel = "Ethernet"; HasNetwork = true; }
            else { NetLabel = "Нет сети"; HasNetwork = false; }
        }
        catch
        {
            NetLabel = "Сеть";
            HasNetwork = true;
        }
    }

    private void RefreshFromState()
    {
        IsConnected = _vpnEngine.IsRunning;
        IsConnecting = _vpnEngine.IsConnecting;
        ErrorMessage = _vpnEngine.LastError ?? _subscription.LastError;
        ToggleCommand.NotifyCanExecuteChanged();

        var node = _subscription.SelectedNode;
        var geo = node != null ? CountryGeoLookup.Find(node.Name) : null;
        GlobeGeo = geo;

        CurrentNodeName = node != null ? RemarkText.StripLeadingFlag(node.Name) : "Выбрать за меня";
        CurrentNodeMeta = geo != null ? $"{geo.City}, {geo.RuName}" : "";
        CurrentFlag = geo != null ? CountryGeoLookup.FlagEmoji(geo.Code) : "🌐";
        CurrentFlagImagePath = FlagIcon.ImagePath(geo?.Code);

        var greeting = Greetings.ForLang(geo?.Lang);
        GreetingHi = greeting.Hi;
        GreetingSub = (greeting.Transliteration.Length > 0 ? $"«{greeting.Transliteration}» · " : "") + greeting.Language;

        GlobeStatus = IsConnected ? "on" : IsConnecting ? "connecting" : "off";

        var ruPrep = geo?.RuPrep ?? "надёжном месте";
        var nodeLabel = node != null ? RemarkText.StripLeadingFlag(node.Name) : "Выбрать за меня";
        Headline = IsConnected ? $"Ты в {ruPrep}" : IsConnecting ? "Ищем дорогу…" : "Пока без защиты";
        Subline = IsConnected ? $"{nodeLabel} · в туннеле"
            : IsConnecting ? $"Договариваемся с {nodeLabel}"
            : "Провайдер видит всё, что ты открываешь";

        var sub = _subscription.Subscription;
        PlanName = sub?.PlanName ?? "—";
        ExpiryLabel = sub?.ExpireAt is { } iso ? DateFormat.FormatDate(iso) : "—";
        DaysLeft = sub?.DaysLeft ?? 0;
        UsedGb = (sub?.Traffic?.UsedBytes ?? 0) / 1_000_000_000.0;
        QuotaGb = (sub?.Traffic?.LimitBytes ?? 0) / 1_000_000_000.0;
        IsUnlimited = sub?.Traffic?.IsUnlimited ?? false;

        var unlimited = IsUnlimited || QuotaGb <= 0;
        var used = UsedGb.ToString("0.0", Ru);
        TrafficValueLabel = unlimited ? $"{used} ГБ / ∞" : $"{used} / {(int)QuotaGb} ГБ";
        TrafficFraction = unlimited ? 0 : Math.Clamp(UsedGb / QuotaGb, 0, 1);
        ShowTrafficBar = !unlimited;
        TrafficExpiryLabel = $"Подписка до {ExpiryLabel} · осталось {DaysLeft} {RuPlural.Days(DaysLeft)}";

        SubDaysLabel = $"{DaysLeft} {RuPlural.Days(DaysLeft)}";
        SubBars = SubscriptionBars(DaysLeft);
        SubBarLevel = DaysLeft < 3 ? "danger" : DaysLeft < 7 ? "warm" : "ok";
        SubDetail = $"до {ExpiryLabel} · {TrafficValueLabel}";
        RefreshJournalLine();

        if (IsConnected)
        {
            var counters = _vpnEngine.ReadAdapterCounters();
            _lastRx = counters?.RxBytes ?? 0;
            _lastTx = counters?.TxBytes ?? 0;
            _lastSampleUtc = DateTime.UtcNow;
            if (!_speedTimer.IsEnabled) _speedTimer.Start();
            if (!_qualityTimer.IsEnabled)
            {
                _qualityTimer.Start();
                QualityDetail = "Измеряем…";
                _ = ProbeQualityAsync();
            }
        }
        else
        {
            _speedTimer.Stop();
            _qualityTimer.Stop();
            _qualitySamples.Clear();
            _qualityWarmedUp = false;
            QualityValue = "—";
            QualityLabel = "";
            QualityLevel = "none";
            QualityDetail = "Подключись — и покажем";
            _downHistory.Clear();
            _upHistory.Clear();
            DownHistory = Array.Empty<double>();
            UpHistory = Array.Empty<double>();
            DownSpeedLabel = "0,0";
            UpSpeedLabel = "0,0";
            ConnectedTimeLabel = "00:00:00";
        }

        if (Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW_DEMO") == "1") FillPreviewDemo();
    }

    private void SampleSpeed()
    {
        var counters = _vpnEngine.ReadAdapterCounters();
        var now = DateTime.UtcNow;
        if (counters != null)
        {
            var dt = Math.Max((now - _lastSampleUtc).TotalSeconds, 0.001);
            var down = Math.Max(counters.Value.RxBytes - _lastRx, 0) / dt / 1_000_000.0;
            var up = Math.Max(counters.Value.TxBytes - _lastTx, 0) / dt / 1_000_000.0;
            DownSpeedLabel = down.ToString("0.0", Ru);
            UpSpeedLabel = up.ToString("0.0", Ru);
            Push(_downHistory, down);
            Push(_upHistory, up);
            DownHistory = _downHistory.ToArray();
            UpHistory = _upHistory.ToArray();
            _lastRx = counters.Value.RxBytes;
            _lastTx = counters.Value.TxBytes;
        }
        _lastSampleUtc = now;

        var since = _vpnEngine.ConnectedSinceUtc;
        if (since != null)
        {
            var elapsed = now - since.Value;
            ConnectedTimeLabel = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        }
        // Время под защитой в строке журнала растёт — раз в минуту пересчитываем.
        if (++_journalTick % 60 == 0) RefreshJournalLine();
    }

    private static void Push(List<double> history, double value)
    {
        history.Add(value);
        if (history.Count > SpeedHistorySize) history.RemoveAt(0);
    }

    /// <summary>Сколько из 5 «полосок» остатка подписки заполнено — как индикатор сигнала
    /// (subscriptionBars в Android).</summary>
    private static int SubscriptionBars(int days) => days switch
    {
        >= 90 => 5,
        >= 30 => 4,
        >= 14 => 3,
        >= 7 => 2,
        >= 1 => 1,
        _ => 0
    };

    /// <summary>Оценка канала 0..100: задержка до 150 мс — 100, к 900 мс линейно падает до 35;
    /// каждые 10% потерь — минус 5. Все замеры неудачны — 0 (qualityScore в Android).</summary>
    private static int QualityScore(int? rttMs, int lossPct)
    {
        if (rttMs == null) return 0;
        var baseScore = rttMs <= 150 ? 100.0 : rttMs >= 900 ? 35.0 : 100.0 - (rttMs.Value - 150) * 65.0 / 750.0;
        return Math.Clamp((int)(baseScore - lossPct * 0.5), 0, 100);
    }

    /// <summary>Замер раз в 10 с, пока VPN подключён: GET адреса проверки пинга через туннель.
    /// Первый удачный замер — с TLS-рукопожатием, он не показателен: пропускаем его.</summary>
    private async Task ProbeQualityAsync()
    {
        if (!IsConnected) return;
        var ms = -1;
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var response = await ProbeClient.GetAsync(_pingSettings.TestUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            if ((int)response.StatusCode is >= 200 and < 400) ms = (int)sw.ElapsedMilliseconds;
        }
        catch { /* потеря — учтётся как неудачный замер */ }
        if (!IsConnected) return;
        if (!_qualityWarmedUp && ms >= 0)
        {
            _qualityWarmedUp = true;
            _ = Task.Delay(300).ContinueWith(_ => RunOnUiThread(() => _ = ProbeQualityAsync()));
            return;
        }
        _qualityWarmedUp = true;
        _qualitySamples.Enqueue(ms);
        while (_qualitySamples.Count > QualityWindow) _qualitySamples.Dequeue();

        var ok = _qualitySamples.Where(x => x >= 0).OrderBy(x => x).ToList();
        var loss = (_qualitySamples.Count - ok.Count) * 100 / Math.Max(1, _qualitySamples.Count);
        int? rtt = ok.Count > 0 ? ok[ok.Count / 2] : null;
        var score = QualityScore(rtt, loss);
        QualityValue = score.ToString(Ru);
        (QualityLabel, QualityLevel) = score switch
        {
            >= 85 => ("отлично", "excellent"),
            >= 65 => ("хорошо", "good"),
            >= 45 => ("средне", "fair"),
            _ => ("плохо", "poor")
        };
        QualityDetail = rtt is { } r
            ? (loss == 0 ? $"Пинг {r} мс · потерь нет" : $"Пинг {r} мс · потери {loss}%")
            : "Сервер не отвечает";
    }

    private void OnJournalChanged() => RunOnUiThread(RefreshJournalLine);

    /// <summary>«Сегодня под защитой 2 ч 10 мин · последнее событие 14:05».</summary>
    private void RefreshJournalLine()
    {
        var events = NetworkJournal.Events;
        var day = NetworkJournal.DayOf(events, 0, _vpnEngine.IsRunning);
        var minutes = (long)day.Protected.TotalMinutes;
        var head = minutes > 0
            ? $"Сегодня под защитой {(minutes >= 60 ? $"{minutes / 60} ч {minutes % 60} мин" : $"{minutes} мин")}"
            : "Сегодня VPN ещё не включался";
        JournalLine = events.Count > 0 ? $"{head} · последнее событие {events[^1].At.ToLocalTime():HH:mm}" : head;
    }

    public void Dispose()
    {
        _speedTimer.Stop();
        _qualityTimer.Stop();
        NetworkJournal.Changed -= OnJournalChanged;
        _vpnEngine.PropertyChanged -= OnVpnEnginePropertyChanged;
        _subscription.PropertyChanged -= OnSubscriptionPropertyChanged;
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
    }
}
