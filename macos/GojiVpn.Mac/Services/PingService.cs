using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using GodjiVpn.Models;

namespace GodjiVpn.Services;

/// <summary>
/// Настоящий пинг через прокси-протокол — аналог PingRepository.kt (Android): вместо голого
/// TCP-connect до host:port поднимаем ВРЕМЕННЫЙ xray.exe с профилем узла как есть и меряем
/// реальную задержку HTTP-запроса через получившийся SOCKS5 (включая оверхед самого
/// VLESS+Reality-туннеля, а не только TCP-рукопожатие до порта сервера). Android вынужден
/// сериализовать такие проверки через Mutex — общее состояние одной нативной Go-библиотеки на
/// весь процесс. Здесь этого ограничения нет: каждый временный xray.exe — отдельный ОС-процесс
/// со своей памятью, поэтому проверки узлов идут по-настоящему параллельно (ограничено только
/// Concurrency ниже — чтобы не поднимать десятки процессов разом).
///
/// Конфиг узла для замера упрощается (см. BuildConfig): вся маршрутизация — сразу в прокси, без
/// DNS-блока, geoip/geosite и балансировщиков профиля. С ними каждый замер грузил гео-базы и,
/// главное, резолвил адрес самого сервера через DoH, который профиль отправляет… через этот же
/// ещё не найденный сервер: замер упирался в таймаут и показывал «недоступен», хотя VPN работал.
/// </summary>
public sealed class PingService
{
    // Каждый замер — отдельный лёгкий xray (без гео-баз ~20 МБ, старт ~0,3 с), поэтому полдюжины
    // параллельно не нагружают систему, а «Проверить все» заканчивается в разы быстрее.
    private static readonly SemaphoreSlim Concurrency = new(6);

    // Адреса серверов резолвим сами (как и туннель, VpnEngine.WriteXrayConfigAsync) — на минуту,
    // чтобы «Проверить все» по узлам одного хоста не спрашивал DNS заново.
    private static readonly ConcurrentDictionary<string, (string Ip, DateTime Until)> ResolveCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] ProxyProtocols = { "vless", "vmess", "trojan", "shadowsocks", "hysteria", "hysteria2", "wireguard" };

    private static string RuntimeDir => Path.Combine(AppContext.BaseDirectory, "Runtime");
    private static string TempDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodjiVpn", "ping-tmp");

    private readonly PingSettings _settings;

    public PingService(PingSettings settings) => _settings = settings;

    /// <returns>Задержка в мс, или -1, если узел недоступен (сервер не ответил вовремя,
    /// xray.exe не поднялся, HTTP-запрос через тоннель не прошёл и т.п.).</returns>
    public async Task<int> MeasureAsync(VlessNode node, CancellationToken ct = default)
    {
        var method = _settings.Method;
        // TCP/ICMP не смотрят на JSON-конфиг узла вообще — только host:port, без прокси и без
        // временного xray.exe (см. PingRepository.kt — тот же выбор: эти методы быстрее и не
        // требуют поднимать процесс, но не отражают работоспособность самого VLESS-протокола).
        if (method is PingMethod.Tcp or PingMethod.Icmp)
            return await MeasureDirectAsync(node, method, ct).ConfigureAwait(false);

        await Concurrency.WaitAsync(ct).ConfigureAwait(false);
        try { return await MeasureCoreAsync(node, method, _settings.TestUrl, ct).ConfigureAwait(false); }
        finally { Concurrency.Release(); }
    }

    private static async Task<int> MeasureDirectAsync(VlessNode node, PingMethod method, CancellationToken ct)
    {
        try
        {
            if (method == PingMethod.Icmp)
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = await ping.SendPingAsync(node.Host, 2000).ConfigureAwait(false);
                return reply.Status == System.Net.NetworkInformation.IPStatus.Success ? (int)reply.RoundtripTime : -1;
            }

            var sw = Stopwatch.StartNew();
            using var socket = new TcpClient();
            var connectTask = socket.ConnectAsync(node.Host, node.Port);
            if (await Task.WhenAny(connectTask, Task.Delay(2000, ct)).ConfigureAwait(false) != connectTask || !socket.Connected)
                return -1;
            sw.Stop();
            return (int)sw.ElapsedMilliseconds;
        }
        catch { return -1; }
    }

    private static async Task<int> MeasureCoreAsync(VlessNode node, PingMethod method, string probeUrl, CancellationToken ct)
    {
        Directory.CreateDirectory(TempDir);
        var port = GetFreeTcpPort();
        var configPath = Path.Combine(TempDir, $"ping-{Guid.NewGuid():N}.json");
        Process? process = null;
        try
        {
            File.WriteAllText(configPath, await BuildConfigAsync(node, port).ConfigureAwait(false));

            var exePath = Path.Combine(RuntimeDir, "xray");
            if (!File.Exists(exePath)) return -1;

            var psi = new ProcessStartInfo(exePath, $"run -c \"{configPath}\"")
            {
                WorkingDirectory = RuntimeDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.Environment["XRAY_LOCATION_ASSET"] = RuntimeDir;
            process = Process.Start(psi);
            if (process == null) return -1;

            if (!await WaitForSocksReadyAsync(port, TimeSpan.FromSeconds(4), ct).ConfigureAwait(false))
                return -1;

            using var handler = new SocketsHttpHandler
            {
                Proxy = new WebProxy($"socks5://127.0.0.1:{port}"),
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromSeconds(4)
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            using var request = new HttpRequestMessage(
                method == PingMethod.ProxyHead ? HttpMethod.Head : HttpMethod.Get, probeUrl);
            var sw = Stopwatch.StartNew();
            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            sw.Stop();
            // generate_204 отвечает 204 No Content на успехе — это ожидаемый "успешный" код,
            // а не ошибка.
            return response.StatusCode is HttpStatusCode.NoContent || response.IsSuccessStatusCode
                ? (int)sw.ElapsedMilliseconds
                : -1;
        }
        catch
        {
            return -1;
        }
        finally
        {
            if (process != null)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* уже мог сам завершиться */ }
                process.Dispose();
            }
            try { File.Delete(configPath); } catch { /* временный файл, не критично */ }
        }
    }

    private static async Task<string> BuildConfigAsync(VlessNode node, int socksPort)
    {
        var config = JsonNode.Parse(node.ConnectPayloadJson)!.AsObject();
        config["inbounds"] = new JsonArray(new JsonObject
        {
            ["tag"] = "socks-in",
            ["listen"] = "127.0.0.1",
            ["port"] = socksPort,
            ["protocol"] = "socks",
            ["settings"] = new JsonObject { ["udp"] = false }
        });
        // Для разового замера задержки логи не нужны — тише некуда, чтобы не плодить файлы
        // на каждый пинг.
        config["log"] = new JsonObject { ["loglevel"] = "none" };

        var outbounds = config["outbounds"]?.AsArray() ?? new JsonArray();
        var proxyTag = outbounds
            .Where(ob => ProxyProtocols.Contains(ob?["protocol"]?.GetValue<string>()))
            .Select(ob => ob?["tag"]?.GetValue<string>())
            .FirstOrDefault(tag => !string.IsNullOrEmpty(tag));
        if (proxyTag != null)
        {
            // Замер — это «дойдёт ли запрос через этот сервер и за сколько», поэтому всё из
            // socks-инбаунда сразу в прокси. Правила профиля (geoip:ru → direct, DoH-серверы,
            // балансировщики с observatory) здесь только мешают: грузят гео-базы, добавляют
            // DNS-запросы через туннель и могли увести тестовый запрос мимо сервера.
            config["routing"] = new JsonObject
            {
                ["domainStrategy"] = "AsIs",
                ["rules"] = new JsonArray(new JsonObject
                {
                    ["type"] = "field",
                    ["inboundTag"] = new JsonArray("socks-in"),
                    ["outboundTag"] = proxyTag
                })
            };
            config.Remove("dns");
            config.Remove("fakedns");
            config.Remove("observatory");
            config.Remove("burstObservatory");
        }

        // Адрес сервера — сразу IP: иначе xray резолвит его по DNS-блоку профиля (DoH через
        // этот же сервер — петля до таймаута) или, без DNS-блока, системным резолвером изнутри
        // процесса уже после старта. Не получилось — оставляем имя, xray попробует сам.
        foreach (var ob in outbounds)
        {
            if (!ProxyProtocols.Contains(ob?["protocol"]?.GetValue<string>())) continue;
            var settings = ob!["settings"];
            var targets = (settings?["vnext"] as JsonArray ?? settings?["servers"] as JsonArray)?.OfType<JsonObject>()
                          ?? (settings is JsonObject so && so["address"] != null ? new[] { so } : Array.Empty<JsonObject>());
            foreach (var target in targets)
            {
                var host = target["address"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(host) || IPAddress.TryParse(host, out _)) continue;
                var ip = await ResolveAsync(host).ConfigureAwait(false);
                if (ip != null) target["address"] = ip;
            }
        }

        // Пока туннель поднят, системный маршрут по умолчанию идёт в TUN — без привязки к
        // физическому интерфейсу замер шёл бы через текущий VPN-сервер ("через два сервера") и
        // показывал бы не задержку до узла, а сумму. sendThrough — тот же приём, что у самого
        // туннеля (VpnEngine.WriteXrayConfigAsync). Адрес берём актуальный: запомненный при
        // подключении мог смениться (переподключение Wi-Fi, другая сеть), и привязка к нему
        // роняла каждый замер с "bind: The requested address is not valid" → «недоступен».
        var physicalIp = CurrentPhysicalIp();
        if (physicalIp != null)
        {
            foreach (var ob in outbounds)
            {
                var protocol = ob?["protocol"]?.GetValue<string>();
                if (protocol == "freedom" || ProxyProtocols.Contains(protocol))
                    ob!["sendThrough"] = physicalIp;
            }
        }
        return config.ToJsonString();
    }

    private static async Task<string?> ResolveAsync(string host)
    {
        if (ResolveCache.TryGetValue(host, out var cached) && cached.Until > DateTime.UtcNow) return cached.Ip;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, cts.Token).ConfigureAwait(false);
            var ip = addresses.FirstOrDefault()?.ToString();
            if (ip != null) ResolveCache[host] = (ip, DateTime.UtcNow.AddMinutes(1));
            return ip;
        }
        catch { return null; }
    }

    /// <summary>Адрес физического интерфейса для sendThrough, пока поднят туннель; null — туннель
    /// не поднят или физический адрес сейчас не найти (тогда замер идёт как есть, через туннель:
    /// цифра выйдет чуть больше, но узел не будет ложно «недоступен»).</summary>
    private static string? CurrentPhysicalIp()
    {
        var engine = VpnEngine.Current;
        if (engine?.IsRunning != true || string.IsNullOrEmpty(engine.PhysicalIp)) return null;
        try
        {
            var candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             ni.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .Select(ni => (Props: ni.GetIPProperties(), Ni: ni))
                .ToList();
            var local = candidates
                .SelectMany(c => c.Props.UnicastAddresses)
                .Select(u => u.Address.ToString())
                .ToHashSet();
            // Адрес с момента подключения всё ещё наш — он и есть физический (так же его выбрал
            // и сам туннель).
            if (local.Contains(engine.PhysicalIp)) return engine.PhysicalIp;

            // Иначе — IPv4 интерфейса со шлюзом по умолчанию, кроме нашего TUN-адаптера.
            return candidates
                .Where(c => c.Props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork &&
                                                             !g.Address.Equals(IPAddress.Any)))
                .SelectMany(c => c.Props.UnicastAddresses)
                .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !u.Address.ToString().StartsWith("172.19.0.") &&
                            !u.Address.ToString().StartsWith("169.254."))
                .Select(u => u.Address.ToString())
                .FirstOrDefault();
        }
        catch { return null; }
    }

    private static async Task<bool> WaitForSocksReadyAsync(int port, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync("127.0.0.1", port, ct).ConfigureAwait(false);
                return true;
            }
            catch (SocketException) { await Task.Delay(100, ct).ConfigureAwait(false); }
        }
        return false;
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
