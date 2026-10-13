using System.Net.NetworkInformation;

namespace GodjiVpn.Services;

/// <summary>
/// Правила Wi-Fi (Настройки → Подключение → «Правила Wi-Fi») — порт NetworkRulesManager.kt
/// (Android, ee03f58):
///  - «Отключать VPN в доверенных сетях»: при подключении к доверенной сети работающий VPN
///    выключается;
///  - «Включать VPN в чужих сетях»: при подключении к любой НЕ доверенной Wi-Fi VPN
///    поднимается сам на выбранном узле.
/// Правила срабатывают только на реальную смену Wi-Fi (сравнение с прошлым SSID) — поднятие
/// нашего же TUN-адаптера тоже дёргает NetworkAddressChanged, но SSID при этом не меняется.
/// Первое чтение после старта — уже бывшая сеть: автоподключение по нему не делаем, иначе VPN
/// сам включался бы при каждом запуске после ручного отключения. Неизвестная сеть (имя скрыто
/// без доступа к геолокации) доверенной не считается — безопасный вариант.
/// </summary>
public sealed class NetworkRulesManager
{
    private readonly AppSettings _settings;
    private readonly VpnEngine _engine;
    private readonly SubscriptionRepository _subscription;
    private readonly object _gate = new();
    private CancellationTokenSource? _pending;
    private bool _seenFirst;
    private string? _lastHandled = "\0"; // заведомо не совпадает ни с одним SSID и с null

    /// <summary>null — не Wi-Fi; "" — Wi-Fi, но имя недоступно.</summary>
    public string? CurrentSsid { get; private set; }

    public event Action? Changed;

    public NetworkRulesManager(AppSettings settings, VpnEngine engine, SubscriptionRepository subscription)
    {
        _settings = settings;
        _engine = engine;
        _subscription = subscription;
    }

    public void Start()
    {
        NetworkChange.NetworkAddressChanged += (_, _) => Schedule();
        NetworkChange.NetworkAvailabilityChanged += (_, _) => Schedule();
        Schedule(delayMs: 0);
    }

    /// <summary>Перечитать имя сети — например, после выдачи доступа к геолокации.</summary>
    public void Refresh() => Schedule(delayMs: 0);

    private void Schedule(int delayMs = 1500)
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            _pending?.Cancel();
            _pending = cts = new CancellationTokenSource();
        }
        // Пауза: сеть только что появилась, система ещё переключает маршруты.
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(delayMs, cts.Token); } catch (OperationCanceledException) { return; }
            await HandleAsync();
        });
    }

    private async Task HandleAsync()
    {
        var ssid = WifiInfo.CurrentSsid();
        CurrentSsid = ssid;
        Changed?.Invoke();

        bool isFirst;
        lock (_gate)
        {
            if (ssid == _lastHandled) return;
            _lastHandled = ssid;
            isFirst = !_seenFirst;
            _seenFirst = true;
        }
        if (ssid == null) return; // не Wi-Fi — правил для кабеля/без сети нет

        var trusted = ssid.Length > 0 && _settings.TrustedSsids.Contains(ssid);
        var running = _engine.IsRunning || _engine.IsConnecting;
        try
        {
            if (trusted)
            {
                if (_engine.IsRunning && _settings.WifiDisconnectTrusted)
                {
                    VpnEngine.Log($"wifi-rules: доверенная сеть «{ssid}» — отключаю VPN");
                    NetworkJournal.Log(NetworkJournal.Kind.RULE_TRUSTED_DISCONNECT, ssid);
                    await _engine.DisconnectAsync();
                }
                return;
            }
            if (!isFirst && !running && _settings.WifiAutoConnect && _subscription.SelectedNode is { } node)
            {
                VpnEngine.Log($"wifi-rules: чужая сеть «{(ssid.Length > 0 ? ssid : "?")}» — включаю VPN");
                NetworkJournal.Log(NetworkJournal.Kind.RULE_AUTOCONNECT, ssid);
                await _engine.ConnectAsync(node);
            }
        }
        catch (Exception ex)
        {
            VpnEngine.Log($"wifi-rules: {ex.Message}");
        }
    }
}
