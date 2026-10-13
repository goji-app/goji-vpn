using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Windows.Threading;
using GodjiVpn.Utils;

namespace GodjiVpn.Services;

/// <summary>
/// Пишет в NetworkJournal то, что происходит с VPN и сетью (порт startNetworkJournal из Android
/// GodjiApplication.kt): подключение/отключение, «пульс» раз в 5 минут, пока туннель поднят,
/// ошибки подключения и смену сети (Wi-Fi, кабель, сеть пропала). Правила Wi-Fi и проверка
/// утечек пишут свои события сами.
/// </summary>
public sealed class JournalRecorder
{
    private enum Net { Unknown, Wifi, Wired, None }

    private readonly VpnEngine _engine;
    private readonly SubscriptionRepository _subscription;
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _heartbeat = new() { Interval = NetworkJournal.HeartbeatInterval };
    private bool _wasRunning;
    private string? _lastError;
    private Net _net = Net.Unknown;
    private string? _ssid;

    public JournalRecorder(VpnEngine engine, SubscriptionRepository subscription)
    {
        _engine = engine;
        _subscription = subscription;
        NetworkJournal.Init(engine.IsRunning);
        _wasRunning = engine.IsRunning;
        (_net, _ssid) = DetectNetwork();

        _heartbeat.Tick += (_, _) => NetworkJournal.Heartbeat();
        if (_wasRunning) _heartbeat.Start();
        // События движка и сети приходят с фоновых потоков — разбираем их на UI-потоке, где
        // живёт таймер «пульса».
        engine.PropertyChanged += (_, e) => _ui.BeginInvoke(() => OnEngineChanged(e));
        NetworkChange.NetworkAddressChanged += (_, _) => _ui.BeginInvoke(OnNetworkChanged);
        NetworkChange.NetworkAvailabilityChanged += (_, _) => _ui.BeginInvoke(OnNetworkChanged);
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

    // Изменения адресов приходят пачками (в том числе от самого TUN-адаптера) — пишем только
    // реальную смену типа сети или имени Wi-Fi.
    private void OnNetworkChanged()
    {
        var (net, ssid) = DetectNetwork();
        if (net == _net && (net != Net.Wifi || ssid == _ssid)) return;
        _net = net;
        _ssid = ssid;
        switch (net)
        {
            case Net.Wifi: NetworkJournal.Log(NetworkJournal.Kind.NET_WIFI, ssid ?? ""); break;
            case Net.Wired: NetworkJournal.Log(NetworkJournal.Kind.NET_WIRED); break;
            case Net.None: NetworkJournal.Log(NetworkJournal.Kind.NET_LOST); break;
        }
    }

    /// <summary>Физическое подключение с маршрутом по умолчанию, не считая самого туннеля и
    /// виртуальных адаптеров (то же правило, что у плашки сети на «Главной»).</summary>
    private static (Net, string?) DetectNetwork()
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
            if (active.Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
                return (Net.Wifi, WifiInfo.CurrentSsid());
            return active.Count > 0 ? (Net.Wired, null) : (Net.None, null);
        }
        catch
        {
            return (Net.Unknown, null);
        }
    }
}
