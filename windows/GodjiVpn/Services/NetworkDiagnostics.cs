using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GodjiVpn.Services;

/// <summary>
/// Проверка утечек одной кнопкой (Настройки → Безопасность) — порт NetworkDiagnostics.kt
/// (Android, 72a382d).
///
/// "Как видят интернет остальные приложения" — через локальный SOCKS самого xray (тот путь,
/// куда sing-box заворачивает весь трафик из TUN), "как без VPN" — прямым запросом, привязанным
/// к адресу физического адаптера (VpnEngine.PhysicalIp: строгая модель хоста Windows отправляет
/// такой пакет через его интерфейс, мимо TUN — тот же приём, что sendThrough у xray).
///  - IP: публичный IP через туннель не должен совпадать с настоящим;
///  - DNS: случайный поддомен edns.ip-api.com (генерируем сами, как iOS-клиент) разрешается
///    системным резолвером Windows — ровно тем путём, что и у обычных приложений, — а сам
///    сервис сообщает, какой резолвер его разрешил. Резолвер у провайдера пользователя (или в
///    его стране при другой стране выхода) — DNS идёт мимо VPN.
/// Все внешние запросы — только HTTPS: вердикт «утечек нет» по голому HTTP подделывается
/// активным MITM на физическом пути.
/// </summary>
public sealed class NetworkDiagnostics
{
    public sealed record IpInfo(string Ip, string? Country, string? Isp);
    public sealed record DnsInfo(string Ip, string? Country, string? Isp);

    public enum Verdict { Safe, Leak, VpnOff, Error }

    public sealed record Report(Verdict Verdict, IpInfo? RealIp, IpInfo? VpnIp, DnsInfo? Dns,
        bool IpLeak, bool DnsLeak, bool DnsViaIsp);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<Report> CheckAsync(CancellationToken ct = default)
    {
        var engine = VpnEngine.Current;
        var vpnOn = engine?.IsRunning == true;

        using var direct = CreateDirectClient(vpnOn ? engine!.PhysicalIp : null);
        var realTask = FetchIpAsync(direct, ct);
        if (!vpnOn)
            return new Report(Verdict.VpnOff, await realTask, null, null, false, false, false);

        using var tunnel = CreateTunnelClient(followRedirects: true);
        var vpnTask = FetchIpAsync(tunnel, ct);
        var dnsTask = FetchDnsAsync(ct);
        var realIp = await realTask;
        var vpnIp = await vpnTask;
        var dns = await dnsTask;

        // "Всё защищено" без проверенного DNS было бы голословным — тогда честно "не удалось".
        if (vpnIp == null || dns == null)
            return new Report(Verdict.Error, realIp, vpnIp, dns, false, false, false);

        var ipLeak = realIp != null && realIp.Ip == vpnIp.Ip;
        var dnsLeak = realIp != null && IsDnsLeak(dns, realIp, vpnIp);
        var dnsViaIsp = dnsLeak && SameIsp(dns, realIp!);
        return new Report(ipLeak || dnsLeak ? Verdict.Leak : Verdict.Safe, realIp, vpnIp, dns, ipLeak, dnsLeak, dnsViaIsp);
    }

    private static HttpClient CreateDirectClient(string? bindIp)
    {
        var handler = new SocketsHttpHandler { UseProxy = false, ConnectTimeout = Timeout };
        if (!string.IsNullOrEmpty(bindIp) && IPAddress.TryParse(bindIp, out var local))
        {
            handler.ConnectCallback = async (ctx, token) =>
            {
                var socket = new Socket(local.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    socket.Bind(new IPEndPoint(local, 0));
                    // Имя разрешаем сами и берём адрес той же семьи, что и привязка (IPv4).
                    var addresses = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, local.AddressFamily, token);
                    await socket.ConnectAsync(addresses, ctx.DnsEndPoint.Port, token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            };
        }
        return new HttpClient(handler) { Timeout = Timeout };
    }

    private static HttpClient CreateTunnelClient(bool followRedirects) =>
        new(new SocketsHttpHandler
        {
            Proxy = new WebProxy($"socks5://127.0.0.1:{VpnEngine.SocksPort}"),
            UseProxy = true,
            AllowAutoRedirect = followRedirects,
            ConnectTimeout = Timeout
        }) { Timeout = Timeout };

    /// <summary>Два независимых источника: ipwho.is у части провайдеров недоступен. Опрашиваем
    /// оба сразу и берём первый ответ — последовательно недоступный источник держал бы
    /// проверку до своего таймаута.</summary>
    private static async Task<IpInfo?> FetchIpAsync(HttpClient client, CancellationToken ct)
    {
        var pending = new List<Task<IpInfo?>> { FetchIpWhoIsAsync(client, ct), FetchIpApiAsync(client, ct) };
        while (pending.Count > 0)
        {
            var done = await Task.WhenAny(pending);
            pending.Remove(done);
            if (await done is { } info) return info;
        }
        return null;
    }

    private static async Task<IpInfo?> FetchIpWhoIsAsync(HttpClient client, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await client.GetStringAsync("https://ipwho.is/", ct));
            var root = doc.RootElement;
            if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False) return null;
            var ip = Str(root, "ip");
            if (ip == null) return null;
            string? isp = null;
            if (root.TryGetProperty("connection", out var conn) && conn.ValueKind == JsonValueKind.Object) isp = Str(conn, "isp");
            return new IpInfo(ip, Str(root, "country"), isp);
        }
        catch (Exception ex)
        {
            VpnEngine.Log($"leak-check: ipwho.is failed — {ex.Message}");
            return null;
        }
    }

    private static async Task<IpInfo?> FetchIpApiAsync(HttpClient client, CancellationToken ct)
    {
        try
        {
            // У ip-api.com TLS доступен только с платным ключом — голый HTTP туда позволял
            // MITM подделать «настоящий IP» (а с ним и вердикт «утечек нет»). ipapi.co —
            // единый с iOS-клиентом (LeakChecker.swift) запасной HTTPS-источник.
            using var doc = JsonDocument.Parse(await client.GetStringAsync("https://ipapi.co/json/", ct));
            var root = doc.RootElement;
            var ip = Str(root, "ip");
            return ip == null ? null : new IpInfo(ip, Str(root, "country_name")?.Replace("The ", ""), Str(root, "org"));
        }
        catch (Exception ex)
        {
            VpnEngine.Log($"leak-check: ipapi.co failed — {ex.Message}");
            return null;
        }
    }

    /// <summary>Поддомен edns.ip-api.com генерируем сами (как iOS-клиент) и разрешаем его
    /// системным резолвером Windows — это и есть проверяемый путь; edns-сервер запоминает,
    /// какой резолвер за ним пришёл, и по HTTPS сообщает, какой именно. Запасной вариант —
    /// домен целиком через SOCKS (имя разрешает xray внутри туннеля).</summary>
    private static async Task<DnsInfo?> FetchDnsAsync(CancellationToken ct)
    {
        try
        {
            var host = $"{Guid.NewGuid().ToString("N")[..24]}.edns.ip-api.com";
            if ((await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct)).Length == 0) return null;
            // Без прокси: имя резолвит ОС, ответ идёт обычным системным маршрутом (через TUN,
            // когда туннель поднят), TLS валидируется по настоящему имени хоста.
            using var systemPath = CreateDirectClient(null);
            return ParseEdns(await systemPath.GetStringAsync($"https://{host}/json", ct));
        }
        catch (Exception ex)
        {
            VpnEngine.Log($"leak-check: system DNS path failed — {ex.Message}");
        }

        try
        {
            var host = $"{Guid.NewGuid().ToString("N")[..24]}.edns.ip-api.com";
            using var tunnel = CreateTunnelClient(followRedirects: true);
            return ParseEdns(await tunnel.GetStringAsync($"https://{host}/json", ct));
        }
        catch (Exception ex)
        {
            VpnEngine.Log($"leak-check: edns via SOCKS failed — {ex.Message}");
            return null;
        }
    }

    private static DnsInfo? ParseEdns(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("dns", out var dns) || dns.ValueKind != JsonValueKind.Object) return null;
        var ip = Str(dns, "ip");
        if (ip == null) return null;
        // geo приходит строкой "Страна - Провайдер".
        var geo = Str(dns, "geo") ?? "";
        var sep = geo.IndexOf(" - ", StringComparison.Ordinal);
        var country = (sep >= 0 ? geo[..sep] : geo).Trim().Replace("The ", "");
        var isp = sep >= 0 ? geo[(sep + 3)..].Trim() : "";
        return new DnsInfo(ip, country.Length > 0 ? country : null, isp.Length > 0 ? isp : null);
    }

    /// <summary>Утечка DNS: резолвер у того же провайдера, что и настоящий IP, — или в стране
    /// настоящего IP, когда выход VPN в другой стране.</summary>
    private static bool IsDnsLeak(DnsInfo dns, IpInfo real, IpInfo vpn)
    {
        var sameCountryAsReal = dns.Country != null && real.Country != null &&
            string.Equals(dns.Country, real.Country, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(real.Country, vpn.Country, StringComparison.OrdinalIgnoreCase);
        return SameIsp(dns, real) || sameCountryAsReal;
    }

    private static bool SameIsp(DnsInfo dns, IpInfo real) =>
        !string.IsNullOrWhiteSpace(dns.Isp) && !string.IsNullOrWhiteSpace(real.Isp) &&
        NormalizeIsp(dns.Isp!) == NormalizeIsp(real.Isp!);

    private static string NormalizeIsp(string s)
    {
        var lower = Regex.Replace(s.ToLowerInvariant(), @"\b(llc|ltd|ooo|jsc|pjsc|inc|oao|zao|gmbh|limited|company)\b", "");
        return Regex.Replace(lower, @"[^\p{L}\p{N}]", "");
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()
            : null;
}
