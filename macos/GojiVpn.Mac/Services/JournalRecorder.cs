using System.ComponentModel;
using System.Net.NetworkInformation;
using Avalonia.Threading;
using GodjiVpn.Utils;

namespace GodjiVpn.Services;

/// <summary>
/// Пишет в NetworkJournal то, что происходит с VPN и сетью (порт startNetworkJournal из Android
/// GodjiApplication.kt, как в Windows-клиенте): подключение/отключение, «пульс» раз в 5 минут,
/// пока туннель поднят, ошибки подключения и смену сети (Wi-Fi, кабель, сеть пропала). Имя Wi-Fi
/// на macOS без разрешения геолокации недоступно — пишется только тип сети.
/// </summary>
public sealed class JournalRecorder
{
    private enum Net { Unknown, Wifi, Wired, None }

    private readonly VpnEngine _engine;
    private readonly SubscriptionRepository _subscription;
    private readonly DispatcherTimer _heartbeat = new() { Interval = NetworkJournal.HeartbeatInterval };
    private bool _wasRunning;
    private string? _lastError;
    private Net _net;

    public JournalRecorder(VpnEngine engine, SubscriptionRepository subscription)
    {
        _engine = engine;
        _subscription = subscription;
        NetworkJournal.Init(engine.IsRunning);
        _wasRunning = engine.IsRunning;
        _net = DetectNetwork();

        _heartbeat.Tick += (_, _) => NetworkJournal.Heartbeat();
        if (_wasRunning) _heartbeat.Start();
        // События движка и сети приходят с фоновых потоков — разбираем их на UI-потоке, где
        // живёт таймер «пульса».
        engine.PropertyChanged += (_, e) => Dispatcher.UIThread.Post(() => OnEngineChanged(e));
        NetworkChange.NetworkAddressChanged += (_, _) => Dispatcher.UIThread.Post(OnNetworkChanged);
        NetworkChange.NetworkAvailabilityChanged += (_, _) => Dispatcher.UIThread.Post(OnNetworkChanged);
    }

    private void OnEngineChanged(PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VpnEngine.IsRunning))
        {
            var running = _engine.IsRunning;
            if (running == _wasRunning) return;
            _wasRunning = running;
            if (running)
            {
                var node = _subscription.SelectedNode is { } n ? RemarkText.StripLeadingFlag(n.Name) : "";
                NetworkJournal.Log(NetworkJournal.Kind.CONNECTED, node);
                _heartbeat.Start();
            }
            else
            {
                _heartbeat.Stop();
                NetworkJournal.Log(NetworkJournal.Kind.DISCONNECTED);
            }
        }
        else if (e.PropertyName == nameof(VpnEngine.LastError))
        {
            var error = _engine.LastError;
            if (!string.IsNullOrWhiteSpace(error) && error != _lastError) NetworkJournal.Log(NetworkJournal.Kind.ERROR, error);
            _lastError = error;
        }
    }

    // Изменения адресов приходят пачками (в том числе от самого utun туннеля) — пишем только
    // реальную смену типа сети.
    private void OnNetworkChanged()
    {
        var net = DetectNetwork();
        if (net == _net || net == Net.Unknown) return;
        _net = net;
        switch (net)
        {
            case Net.Wifi: NetworkJournal.Log(NetworkJournal.Kind.NET_WIFI); break;
            case Net.Wired: NetworkJournal.Log(NetworkJournal.Kind.NET_WIRED); break;
            case Net.None: NetworkJournal.Log(NetworkJournal.Kind.NET_LOST); break;
        }
    }

    /// <summary>То же правило, что у плашки сети на «Главной»: поднятый enN с IPv4 (не
    /// link-local); Wi-Fi это или кабель — по таблице аппаратных портов.</summary>
    private static Net DetectNetwork()
    {
        try
        {
            var active = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.Name.StartsWith("en", StringComparison.Ordinal)
                            && n.GetIPProperties().UnicastAddresses.Any(a =>
                                a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                                !a.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal)))
                .ToList();
            if (active.Any(n => MacHardwarePorts.WifiDevices.Contains(n.Name))) return Net.Wifi;
            return active.Count > 0 ? Net.Wired : Net.None;
        }
        catch
        {
            return Net.Unknown;
        }
    }
}
