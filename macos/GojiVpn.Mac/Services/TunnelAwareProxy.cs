using System.Net;

namespace GodjiVpn.Services;

/// <summary>
/// Прокси по 127.0.0.1:SocksPort, пока VpnEngine.Current.IsRunning — решение принимается
/// заново на каждый запрос (а не фиксируется при создании HttpClient), поэтому первый вызов
/// до подключения и все последующие после — ведут себя корректно. Общий класс — раньше был
/// продублирован в ApiClient/SubscriptionService почти один в один (TunnelAwareProxy/
/// TunnelAwareProxyForSubscription), теперь и UpdateService использует этот же экземпляр.
/// </summary>
public sealed class TunnelAwareProxy : IWebProxy
{
    public ICredentials? Credentials { get; set; }

    public Uri? GetProxy(Uri destination) =>
        VpnEngine.Current?.IsRunning == true
            ? new Uri($"socks5://127.0.0.1:{VpnEngine.SocksPort}")
            : null;

    public bool IsBypassed(Uri host) => VpnEngine.Current?.IsRunning != true;
}
