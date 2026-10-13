using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using GodjiVpn.Models;

namespace GodjiVpn.Services;

/// <summary>
/// VPN-туннель на macOS — та же связка, что в Windows-клиенте: xray отдаёт локальный SOCKS5
/// (протоколы бэкенда, включая XHTTP, есть только в xray), а sing-box поднимает utun-интерфейс
/// и заворачивает в этот SOCKS весь системный трафик.
///
/// Отличия от Windows:
///  - sing-box с TUN требует root. Он запускается через системный запрос пароля администратора
///    (osascript "do shell script … with administrator privileges") внутри небольшой root-обёртки
///    на /bin/sh. Обёртка останавливает sing-box, когда приложение создаёт файл-сигнал
///    (DisconnectAsync) или когда процесс приложения исчезает — второй запрос пароля для
///    отключения не нужен, и туннель не остаётся висеть после падения приложения.
///  - Петли "исходящее соединение xray снова попадает в туннель" исключаются правилом sing-box
///    по имени процесса (xray и само приложение идут напрямую, мимо туннеля), а не привязкой к
///    адресу физического интерфейса, как в Windows: на macOS привязка к адресу не выбирает
///    интерфейс (слабая модель хоста).
///  - DNS: на время работы туннеля обёртка ставит всем сетевым службам DNS-сервер туннеля
///    (иначе запросы к DNS роутера шли бы мимо VPN по локальной сети) и восстанавливает прежние
///    значения при остановке, в том числе через trap при аварийном завершении.
/// </summary>
public sealed class VpnEngine : INotifyPropertyChanged
{
    public const int SocksPort = 10808;
    private const string AdapterIp = "172.19.0.1";
    // Системный DNS на время VPN. Запросы к нему уходят в туннель (auto_route) и там
    // перехватываются (hijack-dns). Не 172.19.0.2: это внутренний адрес стека "system" sing-box,
    // с ним сайты не открывались.
    private const string TunDnsIp = "1.1.1.1";
    private const string DnsIp = "1.1.1.1";

    public static VpnEngine? Current { get; private set; }

    public static string RuntimeDir => Path.Combine(AppContext.BaseDirectory, "Runtime");
    private static string StateDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodjiVpn");
    private static string LogsDir => Path.Combine(StateDir, "logs");
    private static string StopFile => Path.Combine(StateDir, "tun.stop");
    private static string PidFile => Path.Combine(StateDir, "tun.pid");

    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private Process? _xrayProcess;
    private StreamWriter? _xrayLog;
    private StreamWriter? _engineLog;
    private CancellationTokenSource? _watchCts;
    private VlessNode? _lastNode;
    private string? _tunInterface;

    /// <summary>На macOS не используется (петли исключает правило sing-box по процессу) —
    /// оставлено ради общего с Windows кода пинга и проверки утечек.</summary>
    public string? PhysicalIp => null;

    private bool _isRunning;
    public bool IsRunning { get => _isRunning; private set => SetField(ref _isRunning, value); }

    private bool _isConnecting;
    public bool IsConnecting { get => _isConnecting; private set => SetField(ref _isConnecting, value); }

    private string? _lastError;
    public string? LastError { get => _lastError; private set => SetField(ref _lastError, value); }

    private DateTime? _connectedSinceUtc;
    public DateTime? ConnectedSinceUtc { get => _connectedSinceUtc; private set => SetField(ref _connectedSinceUtc, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public VpnEngine()
    {
        Current = this;
        Directory.CreateDirectory(LogsDir);
    }

    public static void Log(string message) => Current?.LogEngine(message);

    private void LogEngine(string message)
    {
        try
        {
            _engineLog ??= new StreamWriter(Path.Combine(LogsDir, "engine.log"), append: true) { AutoFlush = true };
            _engineLog.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}");
        }
        catch { /* диагностика не должна мешать работе */ }
    }

    public async Task ReconnectAsync()
    {
        if (_lastNode is not { } node || !IsRunning) return;
        await DisconnectAsync().ConfigureAwait(false);
        await ConnectAsync(node).ConfigureAwait(false);
    }

    public async Task ConnectAsync(VlessNode node)
    {
        _lastNode = node;
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsRunning || IsConnecting) return;
            IsConnecting = true;
            LastError = null;
            LogEngine("ConnectAsync: start");
            await ConnectAttemptAsync(node).ConfigureAwait(false);
            ConnectedSinceUtc = DateTime.UtcNow;
            IsRunning = true;
            LogEngine($"ConnectAsync: success, utun={_tunInterface}");
            StartWatch();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            LogEngine($"ConnectAsync: FAILED — {ex}");
            await TeardownAsync().ConfigureAwait(false);
        }
        finally
        {
            IsConnecting = false;
            _lifecycleLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try { await TeardownAsync().ConfigureAwait(false); }
        finally { _lifecycleLock.Release(); }
    }

    private async Task ConnectAttemptAsync(VlessNode node)
    {
        KillStrayXray();
        EnsurePortFree(SocksPort);
        // Остатки прошлой сессии: сигнал остановки и pid-файл удаляем до старта обёртки.
        TryDelete(StopFile);
        TryDelete(PidFile);

        var xrayConfigPath = await WriteXrayConfigAsync(node).ConfigureAwait(false);
        _xrayLog = new StreamWriter(Path.Combine(LogsDir, "xray.log"), append: true) { AutoFlush = true };
        _xrayLog.WriteLine($"===== launch {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} =====");
        _xrayProcess = StartXray(xrayConfigPath, _xrayLog);
        LogEngine($"xray started, pid={_xrayProcess.Id}");
        await WaitForSocksReadyAsync(_xrayProcess, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        LogEngine("SOCKS ready");

        var singBoxConfigPath = WriteSingBoxConfig();
        var singBoxLog = Path.Combine(LogsDir, "sing-box.log");
        // Файл журнала создаём сами: root дописывает в существующий файл, владелец остаётся
        // пользователем — журнал можно открыть и удалить без прав администратора.
        File.AppendAllText(singBoxLog, $"===== launch {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} =====\n");
        File.AppendAllText(Path.Combine(LogsDir, "tun-wrapper.log"), $"===== launch {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} =====\n");
        await StartPrivilegedTunnelAsync(Path.Combine(RuntimeDir, "sing-box"), singBoxConfigPath, singBoxLog).ConfigureAwait(false);
        LogEngine("root wrapper started");

        _tunInterface = await WaitForTunAsync(TimeSpan.FromSeconds(40), singBoxLog).ConfigureAwait(false);
        if (_xrayProcess.HasExited)
            throw new InvalidOperationException("xray завершился во время подключения — см. журнал xray.");
    }

    /// <summary>Запуск sing-box от root через стандартное окно запроса пароля macOS.</summary>
    private static async Task StartPrivilegedTunnelAsync(string singBoxPath, string configPath, string logPath)
    {
        // Root-обёртка: $1 sing-box, $2 конфиг, $3 журнал, $4 pid приложения, $5 файл-сигнал
        // остановки, $6 pid-файл, $7 DNS туннеля. DNS всех сетевых служб подменяется на DNS
        // туннеля и восстанавливается при выходе (trap), в том числе при kill обёртки.
        // Внутри функции $5/$6 — её собственные (пустые) аргументы, поэтому пути сохраняются заранее.
        const string script = """
            STOPF="$5"; PIDF="$6"
            echo "$(date '+%F %T') обёртка: старт, sing-box=$1"
            SVC=$(networksetup -listallnetworkservices | tail -n +2 | grep -v '^\*')
            SAVED=""
            restore() {
              printf '%s\n' "$SAVED" | while IFS= read -r line; do
                [ -z "$line" ] && continue
                s=${line%%=*}; old=${line#*=}
                if [ -z "$old" ]; then networksetup -setdnsservers "$s" Empty; else networksetup -setdnsservers "$s" $old; fi
              done
              rm -f "$STOPF" "$PIDF"
              echo "$(date '+%F %T') обёртка: DNS восстановлен, выход"
            }
            IFS='
            '
            for s in $SVC; do
              cur=$(networksetup -getdnsservers "$s" | grep -E '^[0-9a-fA-F:.]+$' | tr '\n' ' ')
              SAVED="$SAVED
            $s=$cur"
            done
            unset IFS
            "$1" run -c "$2" >> "$3" 2>&1 &
            SB=$!
            echo $SB > "$PIDF"
            echo "$(date '+%F %T') обёртка: sing-box pid=$SB, службы: $(echo $SVC | tr '\n' ',')"
            trap 'kill $SB 2>/dev/null; restore; exit 0' TERM INT HUP
            sleep 2
            IFS='
            '
            for s in $SVC; do networksetup -setdnsservers "$s" "$7"; done
            unset IFS
            while kill -0 $SB 2>/dev/null; do
              if [ -f "$STOPF" ] || ! kill -0 "$4" 2>/dev/null; then
                echo "$(date '+%F %T') обёртка: остановка (сигнал или приложение закрыто)"
                kill $SB 2>/dev/null; sleep 2; kill -9 $SB 2>/dev/null
                break
              fi
              sleep 1
            done
            wait $SB 2>/dev/null; echo "$(date '+%F %T') обёртка: sing-box завершился, код $?"
            restore
            """;
        // Обёртка работает на переднем плане, а osascript живёт, пока жив туннель: фоновый процесс
        // (nohup … &), запущенный из "do shell script … with administrator privileges", macOS
        // завершает вместе с самим do shell script — туннель тогда не поднимается вовсе.
        var shell = new StringBuilder("/bin/sh -c ")
            .Append(ShQuote(script.Replace("\r", "")))
            .Append(" goji-tun ")
            .Append(ShQuote(singBoxPath)).Append(' ')
            .Append(ShQuote(configPath)).Append(' ')
            .Append(ShQuote(logPath)).Append(' ')
            .Append(Environment.ProcessId).Append(' ')
            .Append(ShQuote(StopFile)).Append(' ')
            .Append(ShQuote(PidFile)).Append(' ')
            .Append(TunDnsIp)
            .Append(" >> ").Append(ShQuote(Path.Combine(LogsDir, "tun-wrapper.log"))).Append(" 2>&1")
            .ToString();

        var psi = new ProcessStartInfo("/usr/bin/osascript")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        // Без "with timeout" долгий do shell script может оборваться по тайм-ауту Apple Event.
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add("with timeout of 31536000 seconds");
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add($"do shell script \"{AppleScriptEscape(shell)}\" with administrator privileges " +
                             "with prompt \"Goji VPN запрашивает разрешение на создание VPN-туннеля.\"");
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add("end timeout");

        _tunHelper?.Dispose();
        var process = Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить osascript");
        _tunHelper = process;
        var stderrTask = process.StandardError.ReadToEndAsync();
        _ = process.StandardOutput.ReadToEndAsync();

        // Успех — обёртка записала pid sing-box; конец osascript до этого — отмена или ошибка.
        // Запас времени — на ввод пароля.
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(5);
        while (!File.Exists(PidFile))
        {
            if (process.HasExited)
            {
                var stderr = await stderrTask.ConfigureAwait(false);
                // -128 — пользователь нажал "Отменить" в окне пароля.
                if (stderr.Contains("-128"))
                    throw new OperationCanceledException("Подключение отменено — без пароля администратора туннель не создать.");
                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"Не удалось получить права администратора: {stderr.Trim()}");
                var tail = TryReadLogTail(Path.Combine(LogsDir, "tun-wrapper.log"), 600);
                throw new InvalidOperationException("Процесс туннеля завершился сразу после запуска." +
                                                    (string.IsNullOrEmpty(tail) ? "" : $" Последнее в журнале: …{tail}"));
            }
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Не дождались запуска туннеля — окно пароля администратора не подтверждено.");
            await Task.Delay(200).ConfigureAwait(false);
        }
    }

    /// <summary>osascript, держащий root-обёртку туннеля; завершается вместе с ней.</summary>
    private static Process? _tunHelper;

    private static string ShQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";
    private static string AppleScriptEscape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>Готовность — поднятый utun с адресом туннеля; смерть sing-box во время
    /// ожидания (pid-файл обёртки исчез) — сразу ошибка с хвостом его журнала.</summary>
    private static async Task<string> WaitForTunAsync(TimeSpan timeout, string singBoxLog)
    {
        var expectedIp = IPAddress.Parse(AdapterIp);
        var deadline = DateTime.UtcNow + timeout;
        var sawPid = false;
        while (DateTime.UtcNow < deadline)
        {
            var candidate = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n =>
                n.OperationalStatus is OperationalStatus.Up or OperationalStatus.Unknown &&
                n.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(expectedIp)));
            if (candidate != null) return candidate.Name;
            if (File.Exists(PidFile)) sawPid = true;
            else if (sawPid) break; // обёртка уже завершилась — sing-box упал
            await Task.Delay(250).ConfigureAwait(false);
        }
        // Пустой журнал sing-box — значит, он не запускался вовсе: тогда показываем журнал обёртки.
        var tail = TryReadLogTail(singBoxLog, 800);
        if (string.IsNullOrEmpty(tail))
            tail = TryReadLogTail(Path.Combine(LogsDir, "tun-wrapper.log"), 800);
        throw new InvalidOperationException(
            "sing-box не смог поднять туннель" + (sawPid ? "." : " (процесс туннеля не запустился).") +
            (!string.IsNullOrEmpty(tail) ? $" Последнее в журнале: …{tail}" : ""));
    }

    /// <summary>Следит за ядрами после подключения: смерть xray или sing-box переводит
    /// состояние в "отключено" с понятной причиной, а не оставляет ложное "Подключено".</summary>
    private void StartWatch()
    {
        _watchCts = new CancellationTokenSource();
        var token = _watchCts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try { await Task.Delay(2000, token).ConfigureAwait(false); } catch { return; }
                string? dead = null;
                if (_xrayProcess is { HasExited: true }) dead = "xray";
                else if (!File.Exists(PidFile)) dead = "sing-box";
                if (dead == null) continue;
                await _lifecycleLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (!IsRunning || token.IsCancellationRequested) return;
                    LastError = $"{dead} неожиданно завершился — туннель разорван. См. журналы в Настройках.";
                    LogEngine($"watch: {dead} died");
                    await TeardownAsync().ConfigureAwait(false);
                }
                finally { _lifecycleLock.Release(); }
                return;
            }
        });
    }

    private async Task TeardownAsync()
    {
        LogEngine("Teardown: start");
        _watchCts?.Cancel();
        _watchCts = null;
        // Сигнал root-обёртке: она сама остановит sing-box и вернёт DNS.
        if (File.Exists(PidFile))
        {
            try { File.WriteAllText(StopFile, "stop"); } catch { /* каталог наш — не должно падать */ }
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (File.Exists(PidFile) && DateTime.UtcNow < deadline)
                await Task.Delay(200).ConfigureAwait(false);
        }
        TryDelete(StopFile);
        try { if (_xrayProcess is { HasExited: false }) _xrayProcess.Kill(entireProcessTree: true); } catch { }
        _xrayProcess?.Dispose();
        _xrayProcess = null;
        _xrayLog?.Dispose();
        _xrayLog = null;
        _tunInterface = null;
        IsRunning = false;
        ConnectedSinceUtc = null;
        LogEngine("Teardown: done");
    }

    /// <summary>Счётчики трафика — с utun-интерфейса туннеля.</summary>
    public (long RxBytes, long TxBytes)? ReadAdapterCounters()
    {
        if (_tunInterface == null) return null;
        var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Name == _tunInterface);
        if (nic == null) return null;
        try
        {
            var stats = nic.GetIPStatistics();
            return (stats.BytesReceived, stats.BytesSent);
        }
        catch { return null; }
    }

    private static Process StartXray(string configPath, StreamWriter log)
    {
        var exe = Path.Combine(RuntimeDir, "xray");
        if (!File.Exists(exe)) throw new FileNotFoundException("Не найден xray в Runtime внутри приложения.", exe);
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = RuntimeDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(configPath);
        psi.Environment["XRAY_LOCATION_ASSET"] = RuntimeDir;
        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (log) log.WriteLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (log) log.WriteLine(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static void KillStrayXray()
    {
        foreach (var p in Process.GetProcessesByName("xray"))
        {
            try
            {
                if (p.MainModule?.FileName?.StartsWith(RuntimeDir, StringComparison.Ordinal) == true)
                {
                    // Временные экземпляры пинга (PingService) тоже отсюда, но они слушают свои
                    // порты; держит 10808 только осиротевший экземпляр туннеля.
                    if (IsListening(SocksPort)) p.Kill(entireProcessTree: true);
                }
            }
            catch { }
            finally { p.Dispose(); }
        }
    }

    private static bool IsListening(int port)
    {
        try
        {
            using var probe = new TcpListener(IPAddress.Loopback, port);
            probe.Start();
            probe.Stop();
            return false;
        }
        catch (SocketException) { return true; }
    }

    private static void EnsurePortFree(int port)
    {
        if (IsListening(port))
            throw new InvalidOperationException(
                $"Порт {port} уже занят другим приложением — похоже, параллельно запущен другой VPN-клиент. Закройте его и подключитесь снова.");
    }

    private static async Task WaitForSocksReadyAsync(Process xray, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (xray.HasExited)
                throw new InvalidOperationException($"xray неожиданно завершился (код {xray.ExitCode}) — см. журнал xray.");
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync("127.0.0.1", SocksPort).ConfigureAwait(false);
                return;
            }
            catch (SocketException) { await Task.Delay(200).ConfigureAwait(false); }
        }
        throw new TimeoutException("xray не поднял локальный SOCKS5 за отведённое время — см. журнал xray.");
    }

    /// <summary>Конфиг xray — профиль узла из подписки плюс те же правки, что в Windows-клиенте:
    /// socks-инбаунд со sniffing (доменные правила для трафика из TUN), keep-alive для TCP и
    /// XHTTP, правило .ru → direct, "Сайты мимо VPN" первым правилом и подстановка IP вместо
    /// хостнейма сервера (резолв до старта туннеля, чтобы xray не зависел от DNS через себя же).</summary>
    private async Task<string> WriteXrayConfigAsync(VlessNode node)
    {
        var config = JsonNode.Parse(node.ConnectPayloadJson)!.AsObject();
        config["inbounds"] = new JsonArray(new JsonObject
        {
            ["tag"] = "socks-in",
            ["listen"] = "127.0.0.1",
            ["port"] = SocksPort,
            ["protocol"] = "socks",
            ["settings"] = new JsonObject { ["udp"] = true },
            ["sniffing"] = new JsonObject
            {
                ["enabled"] = true,
                ["destOverride"] = new JsonArray("http", "tls", "quic"),
                ["routeOnly"] = true
            }
        });

        foreach (var ob in config["outbounds"]?.AsArray() ?? new JsonArray())
        {
            var protocol = ob?["protocol"]?.GetValue<string>();
            if (protocol is not ("vless" or "vmess" or "trojan" or "shadowsocks" or "hysteria")) continue;
            var streamSettings = ob!["streamSettings"]?.AsObject();
            if (streamSettings != null && protocol != "hysteria")
            {
                var sockopt = streamSettings["sockopt"]?.AsObject();
                if (sockopt == null) { sockopt = new JsonObject(); streamSettings["sockopt"] = sockopt; }
                if (sockopt["tcpKeepAliveIdle"] == null || sockopt["tcpKeepAliveIdle"]!.GetValue<int>() == 0) sockopt["tcpKeepAliveIdle"] = 30;
                if (sockopt["tcpKeepAliveInterval"] == null || sockopt["tcpKeepAliveInterval"]!.GetValue<int>() == 0) sockopt["tcpKeepAliveInterval"] = 30;
            }
            var xmux = ob?["streamSettings"]?["xhttpSettings"]?["extra"]?["xmux"];
            if (xmux != null && (xmux["hKeepAlivePeriod"] == null || xmux["hKeepAlivePeriod"]!.GetValue<int>() == 0))
                xmux["hKeepAlivePeriod"] = 30;
        }

        var outbounds = config["outbounds"]?.AsArray() ?? new JsonArray();
        var routing = config["routing"]?.AsObject();
        if (routing == null) { routing = new JsonObject { ["domainStrategy"] = "AsIs" }; config["routing"] = routing; }
        var rules = routing["rules"]?.AsArray();
        if (rules == null) { rules = new JsonArray(); routing["rules"] = rules; }

        if (outbounds.Any(ob => ob?["tag"]?.GetValue<string>() == "direct"))
            rules.Insert(0, new JsonObject { ["type"] = "field", ["domain"] = new JsonArray("regexp:\\.ru$"), ["outboundTag"] = "direct" });

        var bypass = new AppSettings().BypassDomains;
        if (bypass.Count > 0)
        {
            var directTag = outbounds
                .Where(ob => ob?["protocol"]?.GetValue<string>() == "freedom")
                .Select(ob => ob?["tag"]?.GetValue<string>())
                .FirstOrDefault(t => !string.IsNullOrEmpty(t));
            if (directTag == null)
            {
                directTag = "user-bypass-direct";
                outbounds.Add(new JsonObject { ["tag"] = directTag, ["protocol"] = "freedom" });
                config["outbounds"] = outbounds;
            }
            rules.Insert(0, new JsonObject
            {
                ["type"] = "field",
                ["domain"] = new JsonArray(bypass.Select(d => (JsonNode)JsonValue.Create("domain:" + d)!).ToArray()),
                ["outboundTag"] = directTag
            });
            LogEngine($"bypass: {bypass.Count} доменов мимо VPN → {directTag}");
        }

        foreach (var ob in outbounds)
        {
            if (ob?["protocol"]?.GetValue<string>() != "vless") continue;
            var vnext = ob["settings"]?["vnext"]?.AsArray()?.FirstOrDefault();
            var hostname = vnext?["address"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(hostname) || IPAddress.TryParse(hostname, out _)) continue;
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(hostname).ConfigureAwait(false);
                var ip = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString();
                if (ip != null) vnext!["address"] = ip;
            }
            catch { /* оставляем хостнейм — xray попробует сам */ }
        }

        var path = Path.Combine(StateDir, "xray-config.json");
        await File.WriteAllTextAsync(path, config.ToJsonString()).ConfigureAwait(false);
        return path;
    }

    /// <summary>sing-box: utun (адрес туннеля, auto_route/strict_route) → SOCKS xray; xray и само
    /// приложение — напрямую; DNS-пакеты перехватываются и уходят через туннель.</summary>
    private static string WriteSingBoxConfig()
    {
        var config = new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = "info" },
            ["dns"] = new JsonObject
            {
                ["servers"] = new JsonArray(new JsonObject
                {
                    ["type"] = "udp",
                    ["tag"] = "dns-remote",
                    ["server"] = DnsIp,
                    ["detour"] = "socks-out"
                }),
                ["final"] = "dns-remote"
            },
            ["inbounds"] = new JsonArray(new JsonObject
            {
                ["type"] = "tun",
                ["tag"] = "tun-in",
                ["address"] = new JsonArray($"{AdapterIp}/30"),
                ["mtu"] = 1280,
                ["auto_route"] = true,
                ["strict_route"] = true,
                ["stack"] = "system"
            }),
            ["outbounds"] = new JsonArray(
                new JsonObject
                {
                    ["type"] = "socks",
                    ["tag"] = "socks-out",
                    ["server"] = "127.0.0.1",
                    ["server_port"] = SocksPort,
                    ["version"] = "5"
                },
                new JsonObject { ["type"] = "direct", ["tag"] = "direct" }),
            ["route"] = new JsonObject
            {
                ["auto_detect_interface"] = true,
                ["rules"] = new JsonArray(
                    new JsonObject { ["action"] = "sniff" },
                    // Сначала свои процессы: иначе DNS-запросы самого xray перехватывались бы
                    // и уходили обратно в xray по кругу.
                    new JsonObject { ["process_name"] = new JsonArray("xray", "GojiVpn"), ["outbound"] = "direct" },
                    new JsonObject { ["protocol"] = "dns", ["action"] = "hijack-dns" },
                    new JsonObject { ["ip_cidr"] = new JsonArray("169.254.0.0/16"), ["action"] = "reject" }),
                ["final"] = "socks-out"
            }
        };
        var path = Path.Combine(StateDir, "sing-box-config.json");
        File.WriteAllText(path, config.ToJsonString());
        return path;
    }

    private static string? TryReadLogTail(string path, int maxChars)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var length = (int)Math.Min(stream.Length, maxChars);
            stream.Seek(-length, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Trim();
        }
        catch { return null; }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
