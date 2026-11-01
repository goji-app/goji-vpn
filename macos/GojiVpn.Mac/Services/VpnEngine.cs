using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
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

    /// <summary>Свежий xray (Go 1.27) работает только на macOS 13+; на 10.15–12 — xray-legacy,
    /// последняя версия, которую ещё можно собрать под старые macOS (Runtime/build-legacy-cores.sh).</summary>
    public static string XrayExe
    {
        get
        {
            var modern = Path.Combine(RuntimeDir, "xray");
            var legacy = Path.Combine(RuntimeDir, "xray-legacy");
            if (OperatingSystem.IsMacOSVersionAtLeast(13) || !File.Exists(legacy)) return modern;
            return legacy;
        }
    }

    /// <summary>Имена процессов ядра — оба варианта.</summary>
    public static readonly string[] XrayProcessNames = { "xray", "xray-legacy" };

    /// <summary>Имя собственного процесса — для правила sing-box (process_name: direct) и для
    /// проверки обёрткой, что приложение живо. Не захардкожено: берём из фактического
    /// исполняемого файла, запасной вариант — имя сборки.</summary>
    public static string AppProcessName { get; } =
        Environment.ProcessPath is { } exe ? Path.GetFileName(exe)
        : Assembly.GetExecutingAssembly().GetName().Name ?? "GojiVpn";

    /// <summary>Версии встроенных ядер — для строки «Версия XRAY» в «О программе»
    /// (SettingsViewModel): показываем версию фактически выбранного ядра (см. XrayExe).</summary>
    public static string BundledXrayVersion => Path.GetFileName(XrayExe) == "xray" ? "26.10.21" : "26.3.27";

    private static string StateDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodjiVpn");
    private static string LogsDir => Path.Combine(StateDir, "logs");

    // pid/stop-файлы обёртки — СЕССИОННЫЕ (tun.<id>.pid / tun.<id>.stop): у каждого
    // подключения свои. Один общий файл на две обёртки при быстром реконнекте приводил к
    // тому, что старая обёртка в restore удаляла pid-файл НОВОЙ, и сторож убивал здоровый
    // туннель; теперь старая обёртка трогает только свои файлы.
    private string? _sessionStopFile;
    private string? _sessionPidFile;

    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private Process? _xrayProcess;
    private StreamWriter? _xrayLog;
    private StreamWriter? _engineLog;
    private CancellationTokenSource? _watchCts;
    private CancellationTokenSource? _connectCts;
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
        TrimLogIfNeeded(Path.Combine(LogsDir, "engine.log"));
        // Остатки аварийного завершения (SIGKILL обёртки, жёсткая перезагрузка): осиротевшие
        // сессионные pid/stop-файлы и, возможно, подменённый DNS — прибираем, не блокируя
        // старт приложения и не спрашивая пароль администратора.
        _ = Task.Run(CleanupStaleTunnel);
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
        CancellationToken token = CancellationToken.None;
        try
        {
            if (IsRunning || IsConnecting) return;
            IsConnecting = true;
            LastError = null;
            // Отмена подключения (выход из приложения) — прерывает ожидание окна пароля
            // администратора, иначе ConnectAsync держал бы _lifecycleLock до 5 минут.
            _connectCts = new CancellationTokenSource();
            token = _connectCts.Token;
            LogEngine("ConnectAsync: start");
            await ConnectAttemptAsync(node, token).ConfigureAwait(false);
            ConnectedSinceUtc = DateTime.UtcNow;
            IsRunning = true;
            LogEngine($"ConnectAsync: success, utun={_tunInterface}");
            StartWatch();
        }
        catch (OperationCanceledException ex)
        {
            // Явная отмена (окно пароля закрыто) несёт понятный текст; отмену по токену
            // (выход из приложения) заменяем короткой фразой.
            LastError = token.IsCancellationRequested ? "Подключение прервано." : ex.Message;
            LogEngine($"ConnectAsync: отменено — {ex.Message}");
            await TeardownAsync().ConfigureAwait(false);
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

    /// <summary>Прерывает идущее подключение (в частности, ожидание ввода пароля
    /// администратора) — для выхода из приложения.</summary>
    public void CancelConnect()
    {
        // Не Dispose: Cancel может прийти параллельно с обнулением в TeardownAsync.
        try { _connectCts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    public async Task DisconnectAsync()
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try { await TeardownAsync().ConfigureAwait(false); }
        finally { _lifecycleLock.Release(); }
    }

    /// <summary>Журналы старше этого размера начинаются заново (см. TrimLogIfNeeded).</summary>
    private const long MaxLogBytes = 10 * 1024 * 1024;

    /// <summary>Логи sing-box (level info — строка на каждое соединение) и xray дописываются
    /// вечно; разросшийся файл начинаем заново при следующем подключении.</summary>
    internal static void TrimLogIfNeeded(string path)
    {
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes)
                File.Delete(path);
        }
        catch { /* не критично */ }
    }

    /// <summary>Уборка после аварийного завершения (SIGKILL обёртки, жёсткая перезагрузка):
    /// осиротевшие сессионные pid/stop-файлы, оставшийся root-sing-box и подменённый DNS.
    /// Старт приложения не блокирует; пароль администратора не спрашивает — root-процесс без
    /// прав остановить нельзя, тогда просто предупреждение в журнал (перезагрузка Mac или
    /// следующее подключение почистят).</summary>
    private static void CleanupStaleTunnel()
    {
        try
        {
            var stalePids = new List<(string File, int Pid)>();
            foreach (var file in Directory.EnumerateFiles(StateDir, "tun.*.pid"))
            {
                int? pid = null;
                try { pid = int.TryParse(File.ReadAllText(file).Trim(), out var p) ? p : null; }
                catch { }
                if (pid is { } live) stalePids.Add((file, live));
                TryDelete(file);
            }
            foreach (var file in Directory.EnumerateFiles(StateDir, "tun.*.stop")) TryDelete(file);

            foreach (var (_, pid) in stalePids)
            {
                var comm = ProcessComm(pid);
                if (comm == null || !comm.Contains("sing-box")) continue; // pid переиспользован другой программой
                try
                {
                    using var proc = Process.GetProcessById(pid);
                    proc.Kill(entireProcessTree: true);
                    Current?.LogEngine($"уборка: осиротевший sing-box (pid {pid}) остановлен");
                }
                catch (Exception ex)
                {
                    Current?.LogEngine($"уборка: осиротевший sing-box (pid {pid}) остановить не удалось ({ex.Message}). " +
                                       "Если интернет не работает — перезагрузите Mac или подключитесь заново.");
                }
            }
            RestoreDnsIfOrphaned();
        }
        catch { /* уборка не должна ронять старт приложения */ }
    }

    /// <summary>DNS всех сетевых служб остался 1.1.1.1 после гибели обёртки (туннеля уже нет) —
    /// возвращаем службам автоматический DNS. Без прав администратора networksetup не позволит
    /// — тогда предупреждение в engine.log.</summary>
    private static void RestoreDnsIfOrphaned()
    {
        try
        {
            // Есть живой utun (наш или чужой VPN) — DNS не трогаем.
            if (NetworkInterface.GetAllNetworkInterfaces().Any(n =>
                    n.Name.StartsWith("utun", StringComparison.Ordinal) && n.OperationalStatus == OperationalStatus.Up))
                return;
            var listResult = RunNetworkSetup("-listallnetworkservices");
            if (listResult is not { Code: 0 }) return;
            var services = listResult.Value.Output.Split('\n')
                .Select(l => l.Trim())
                // Первая строка вывода — пояснение про звёздочку; помеченные * службы отключены.
                .Where(l => l.Length > 0 && !l.StartsWith("An asterisk", StringComparison.OrdinalIgnoreCase) && !l.StartsWith('*'))
                .ToList();
            if (services.Count == 0) return;
            foreach (var service in services)
            {
                var dns = RunNetworkSetup("-getdnsservers", service);
                if (dns is not { Code: 0 }) return;
                var servers = string.Join(' ', dns.Value.Output.Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0 && l.All(c => char.IsDigit(c) || c is '.' or ':')));
                if (servers != TunDnsIp) return; // не похоже на подмену нашей обёрткой
            }
            var failed = new List<string>();
            foreach (var service in services)
            {
                var set = RunNetworkSetup("-setdnsservers", service, "Empty");
                if (set is not { Code: 0 }) failed.Add(service);
            }
            if (failed.Count > 0)
                Current?.LogEngine("уборка: DNS всех служб остался 1.1.1.1 после аварийного завершения, вернуть автоматически " +
                                   $"не удалось (нужны права администратора): {string.Join(", ", failed)}");
            else
                Current?.LogEngine("уборка: DNS служб восстановлен (оставался 1.1.1.1 после аварийного завершения)");
        }
        catch { /* не блокируем старт */ }
    }

    /// <summary>Запуск networksetup (чтение настроек — без прав; запись — требует root).
    /// null — процесс не запустился или не завершился вовремя.</summary>
    private static (int Code, string Output, string Error)? RunNetworkSetup(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/sbin/networksetup")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p == null) return null;
            var output = p.StandardOutput.ReadToEnd();
            var error = p.StandardError.ReadToEnd();
            p.WaitForExit(5000);
            return p.HasExited ? (p.ExitCode, output, error) : null;
        }
        catch { return null; }
    }

    /// <summary>Путь исполняемого файла процесса (через ps) или null, если процесс не найден.</summary>
    private static string? ProcessComm(int pid)
    {
        try
        {
            var psi = new ProcessStartInfo("/bin/ps")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-p");
            psi.ArgumentList.Add(pid.ToString());
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add("comm=");
            using var p = Process.Start(psi);
            if (p == null) return null;
            var comm = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(5000);
            return comm.Length > 0 ? comm : null;
        }
        catch { return null; }
    }

    private async Task ConnectAttemptAsync(VlessNode node, CancellationToken ct)
    {
        KillStrayXray();
        EnsurePortFree(SocksPort);
        // Остатки прошлых сессий: сигналы остановки и pid-файлы удаляем до старта новой
        // обёртки. Файлы СЕССИОННЫЕ — каждая обёртка дальше работает только со своими.
        var sessionId = Guid.NewGuid().ToString("N");
        _sessionPidFile = Path.Combine(StateDir, $"tun.{sessionId}.pid");
        _sessionStopFile = Path.Combine(StateDir, $"tun.{sessionId}.stop");
        foreach (var f in Directory.EnumerateFiles(StateDir, "tun.*.pid")) TryDelete(f);
        foreach (var f in Directory.EnumerateFiles(StateDir, "tun.*.stop")) TryDelete(f);
        TrimLogIfNeeded(Path.Combine(LogsDir, "xray.log"));
        TrimLogIfNeeded(Path.Combine(LogsDir, "sing-box.log"));
        TrimLogIfNeeded(Path.Combine(LogsDir, "tun-wrapper.log"));

        var xrayConfigPath = await WriteXrayConfigAsync(node).ConfigureAwait(false);
        _xrayLog = new StreamWriter(Path.Combine(LogsDir, "xray.log"), append: true) { AutoFlush = true };
        _xrayLog.WriteLine($"===== launch {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} =====");
        _xrayProcess = StartXray(xrayConfigPath, _xrayLog);
        LogEngine($"xray started, pid={_xrayProcess.Id}");
        await WaitForSocksReadyAsync(_xrayProcess, TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        LogEngine("SOCKS ready");
        // Слушатель SOCKS xray поднимает и при мёртвом сервере — готовность порта ничего
        // не гарантирует, проверяем канал контрольным запросом (как PingService.MeasureCoreAsync).
        await VerifyTunnelHttpAsync(ct).ConfigureAwait(false);
        LogEngine("канал проверен контрольным HTTP-запросом через SOCKS");

        var singBoxConfigPath = WriteSingBoxConfig();
        var singBoxLog = Path.Combine(LogsDir, "sing-box.log");
        // Файл журнала создаём сами: root дописывает в существующий файл, владелец остаётся
        // пользователем — журнал можно открыть и удалить без прав администратора.
        File.AppendAllText(singBoxLog, $"===== launch {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} =====\n");
        File.AppendAllText(Path.Combine(LogsDir, "tun-wrapper.log"), $"===== launch {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} =====\n");
        await StartPrivilegedTunnelAsync(Path.Combine(RuntimeDir, "sing-box"), singBoxConfigPath, singBoxLog, ct).ConfigureAwait(false);
        LogEngine("root wrapper started");

        _tunInterface = await WaitForTunAsync(TimeSpan.FromSeconds(40), singBoxLog, _sessionPidFile, ct).ConfigureAwait(false);
        if (_xrayProcess.HasExited)
            throw new InvalidOperationException("xray завершился во время подключения — см. журнал xray.");
        // Конфиг sing-box (внутри секреты профиля) нужен только при его старте — удаляем.
        TryDelete(singBoxConfigPath);
    }

    /// <summary>Запуск sing-box от root через стандартное окно запроса пароля macOS.</summary>
    private async Task StartPrivilegedTunnelAsync(string singBoxPath, string configPath, string logPath, CancellationToken ct)
    {
        var stopFile = _sessionStopFile!;
        var pidFile = _sessionPidFile!;
        // Root-обёртка: $1 sing-box, $2 конфиг, $3 журнал, $4 pid приложения, $5 файл-сигнал
        // остановки, $6 pid-файл, $7 DNS туннеля. DNS всех сетевых служб подменяется на DNS
        // туннеля и восстанавливается при выходе (trap), в том числе при kill обёртки.
        // Внутри функций $4/$5/$6 — их собственные (пустые) аргументы, поэтому всё сохраняется заранее.
        const string script = """
            STOPF="$5"; PIDF="$6"; APPPID="$4"
            apppid() {
              # Жив ли ещё само приложение: одного kill -0 мало — pid мог быть переиспользован
              # другим процессом, поэтому сверяем ещё и имя исполняемого файла.
              kill -0 "$APPPID" 2>/dev/null || return 1
              ps -p "$APPPID" -o comm= 2>/dev/null | grep -q "@APPNAME@"
            }
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
              if [ -f "$STOPF" ] || ! apppid; then
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
            .Append(ShQuote(script.Replace("\r", "").Replace("@APPNAME@", AppProcessName)))
            .Append(" goji-tun ")
            .Append(ShQuote(singBoxPath)).Append(' ')
            .Append(ShQuote(configPath)).Append(' ')
            .Append(ShQuote(logPath)).Append(' ')
            .Append(Environment.ProcessId).Append(' ')
            .Append(ShQuote(stopFile)).Append(' ')
            .Append(ShQuote(pidFile)).Append(' ')
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
        while (!File.Exists(pidFile))
        {
            ct.ThrowIfCancellationRequested();
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
            await Task.Delay(200, ct).ConfigureAwait(false);
        }
    }

    /// <summary>osascript, держащий root-обёртку туннеля; завершается вместе с ней.</summary>
    private static Process? _tunHelper;

    private static string ShQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";
    private static string AppleScriptEscape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>Готовность — поднятый utun с адресом туннеля; смерть sing-box во время
    /// ожидания (pid-файл обёртки исчез) — сразу ошибка с хвостом его журнала.</summary>
    private static async Task<string> WaitForTunAsync(TimeSpan timeout, string singBoxLog, string pidFile, CancellationToken ct)
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
            if (File.Exists(pidFile)) sawPid = true;
            else if (sawPid) break; // обёртка уже завершилась — sing-box упал
            await Task.Delay(250, ct).ConfigureAwait(false);
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
                try { await Task.Delay(2000, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                var stop = false;
                try
                {
                    await _lifecycleLock.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        if (!IsRunning || token.IsCancellationRequested) return;
                        // Состояние читаем только под локом и в локальной переменной: внизу
                        // Teardown (выход, переподключение) в этот же момент может убить и
                        // обнулить _xrayProcess — раньше эта гонка глушла сторож целиком.
                        string? dead = null;
                        if (_xrayProcess is { HasExited: true }) dead = "xray";
                        else if (_sessionPidFile == null || !File.Exists(_sessionPidFile)) dead = "sing-box";
                        // dead == null — всё живо, следующий круг через 2 с.
                        // Kill switch: упал только xray — sing-box с strict_route держит весь трафик в
                        // туннеле, мимо VPN ничего не уходит; перезапускаем одно ядро, туннель не трогаем.
                        if (dead == "xray" && new AppSettings().KillSwitch)
                        {
                            if (await TryRestartXrayAsync().ConfigureAwait(false))
                            {
                                LastError = null;
                                LogEngine("kill switch: xray перезапущен, туннель не разрывали");
                            }
                            else
                            {
                                LastError = "Ядро VPN не перезапускается — интернет закрыт (kill switch). Смените сервер или отключите VPN, чтобы вернуть интернет без защиты.";
                            }
                        }
                        else if (dead != null)
                        {
                            LastError = $"{dead} неожиданно завершился — туннель разорван. См. журналы в Настройках.";
                            LogEngine($"watch: {dead} died");
                            stop = true;
                            await TeardownAsync().ConfigureAwait(false);
                        }
                    }
                    finally { _lifecycleLock.Release(); }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    // Ошибка сторожа не должна оставлять статус навсегда «Подключено» —
                    // пишем в журнал и продолжаем смотреть.
                    LogEngine($"watch: ошибка цикла — {ex.Message}");
                }
                if (stop) return;
            }
        });
    }

    /// <summary>Kill switch: новый xray с тем же конфигом, пока sing-box держит туннель закрытым.
    /// Не вышло — сторож попробует снова на следующем круге. Вызывается под _lifecycleLock.</summary>
    private async Task<bool> TryRestartXrayAsync()
    {
        try
        {
            _xrayProcess?.Dispose();
            if (_xrayLog != null) lock (_xrayLog) _xrayLog.Dispose();
            _xrayLog = new StreamWriter(Path.Combine(LogsDir, "xray.log"), append: true) { AutoFlush = true };
            _xrayLog.WriteLine($"===== kill switch restart {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} =====");
            _xrayProcess = StartXray(Path.Combine(StateDir, "xray-config.json"), _xrayLog);
            await WaitForSocksReadyAsync(_xrayProcess, TimeSpan.FromSeconds(10), CancellationToken.None).ConfigureAwait(false);
            await VerifyTunnelHttpAsync(CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            LogEngine($"kill switch: перезапуск xray не удался — {ex.Message}");
            return false;
        }
    }

    private async Task TeardownAsync()
    {
        LogEngine("Teardown: start");
        _watchCts?.Cancel();
        _watchCts = null;
        _connectCts = null;
        var pidFile = _sessionPidFile;
        var stopFile = _sessionStopFile;
        // Сигнал root-обёртке ТЕКУЩЕЙ сессии: она сама остановит sing-box и вернёт DNS.
        // Сессионные файлы гарантируют, что старая обёртка при реконнекте не заденет новую.
        if (pidFile != null && File.Exists(pidFile))
        {
            try { File.WriteAllText(stopFile!, "stop"); } catch { /* каталог наш — не должно падать */ }
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (File.Exists(pidFile) && DateTime.UtcNow < deadline)
                await Task.Delay(200).ConfigureAwait(false);
        }
        if (pidFile == null || !File.Exists(pidFile))
        {
            if (stopFile != null) TryDelete(stopFile);
            // Обёртка не добралась до запуска sing-box (отмена на вводе пароля) — pid-файла
            // нет, значит и DNS она менять ещё не могла: окно osascript гасим, оно не нужно.
            try { if (_tunHelper is { HasExited: false }) _tunHelper.Kill(entireProcessTree: true); } catch { }
        }
        else
        {
            // Обёртка не уложилась (медленный networksetup на десятке служб): stop-файл —
            // ЕДИНСТВЕННЫЙ сигнал для root-процесса, стирать его нельзя. Иначе обёртка не
            // увидит ни stop, ни смерти приложения — и оставит root-sing-box со strict_route
            // (весь трафик заблокирован) и DNS служб, подменённым на 1.1.1.1. Оставляем файл:
            // следующий ConnectAsync удаляет его до старта новой обёртки, а сама обёртка
            // погасит туннель, чуть позже.
            LogEngine("Teardown: обёртка не завершилась за 8 с — stop-файл оставлен, туннель погасит она сама");
        }
        try { if (_xrayProcess is { HasExited: false }) _xrayProcess.Kill(entireProcessTree: true); } catch { }
        _xrayProcess?.Dispose();
        _xrayProcess = null;
        if (_xrayLog != null) lock (_xrayLog) _xrayLog.Dispose();
        _xrayLog = null;
        // Конфиги ядер (внутри секреты профиля) больше не нужны — убираем с диска.
        TryDelete(Path.Combine(StateDir, "xray-config.json"));
        TryDelete(Path.Combine(StateDir, "sing-box-config.json"));
        _tunInterface = null;
        _sessionPidFile = null;
        _sessionStopFile = null;
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

    /// <summary>Вывод ядра приходит из двух фоновых потоков и может прийти уже после закрытия
    /// журнала (KillProcess) — исключение в таком потоке завершило бы весь процесс без сообщения.</summary>
    private static void AppendCoreLog(StreamWriter log, string? line)
    {
        if (line == null) return;
        lock (log)
        {
            try { log.WriteLine(line); }
            catch (ObjectDisposedException) { }
        }
    }

    private static Process StartXray(string configPath, StreamWriter log)
    {
        var exe = XrayExe;
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
        process.OutputDataReceived += (_, e) => AppendCoreLog(log, e.Data);
        process.ErrorDataReceived += (_, e) => AppendCoreLog(log, e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static void KillStrayXray()
    {
        // Process.MainModule на macOS в .NET не работает (исключение, а пустой catch его
        // глотал) — из-за этого осиротевший после падения xray вечно держал порт 10808 и
        // не давал подключиться заново. Список процессов с их командами берём у ps.
        string[] lines;
        try
        {
            var psi = new ProcessStartInfo("/bin/ps")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-axo");
            psi.ArgumentList.Add("pid=,comm=");
            using var p = Process.Start(psi);
            if (p == null) return;
            lines = p.StandardOutput.ReadToEnd().Split('\n');
            p.WaitForExit(5000);
        }
        catch { return; }

        // Временные экземпляры пинга (PingService) тоже отсюда, но они слушают свои порты;
        // держит 10808 только осиротевший экземпляр туннеля — убираем его до проверки порта.
        if (!IsListening(SocksPort)) return;

        var runtimePrefix = RuntimeDir + Path.DirectorySeparatorChar;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            var space = line.IndexOf(' ');
            if (space <= 0 || !int.TryParse(line[..space], out var pid)) continue;
            var comm = line[(space + 1)..].Trim();
            // Полный путь из нашего бандла — точно наш экземпляр. ps может отдать усечённый
            // comm без пути — тогда смотрим на имя файла, но не трогаем самих себя.
            var ours = comm.StartsWith(runtimePrefix, StringComparison.Ordinal)
                       || (!comm.Contains('/') && pid != Environment.ProcessId
                           && XrayProcessNames.Contains(Path.GetFileName(comm)));
            if (!ours) continue;
            try
            {
                using var proc = Process.GetProcessById(pid);
                proc.Kill(entireProcessTree: true);
            }
            catch (ArgumentException) { } // процесс уже завершился
            catch (InvalidOperationException) { }
            catch { /* например, недостаточно прав — занятость порта потом проверит EnsurePortFree */ }
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

    private static async Task WaitForSocksReadyAsync(Process xray, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (xray.HasExited)
                throw new InvalidOperationException($"xray неожиданно завершился (код {xray.ExitCode}) — см. журнал xray.");
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync("127.0.0.1", SocksPort, ct).ConfigureAwait(false);
                return;
            }
            catch (SocketException) { await Task.Delay(200, ct).ConfigureAwait(false); }
        }
        throw new TimeoutException("xray не поднял локальный SOCKS5 за отведённое время — см. журнал xray.");
    }

    /// <summary>Контрольный запрос через локальный SOCKS xray (как PingService.MeasureCoreAsync):
    /// слушатель xray поднимает и при мёртвом сервере, поэтому готовность порта сама по себе
    /// ничего не говорит о работоспособности канала.</summary>
    private static async Task VerifyTunnelHttpAsync(CancellationToken ct)
    {
        try
        {
            using var handler = new SocketsHttpHandler
            {
                Proxy = new WebProxy($"socks5://127.0.0.1:{SocksPort}"),
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromSeconds(8)
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await client.GetAsync("https://www.gstatic.com/generate_204", ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Сервер не отвечает: тестовый запрос через ядро VPN не прошёл — см. журнал xray.", ex);
        }
        throw new InvalidOperationException("Сервер не ответил на тестовый запрос через ядро VPN — см. журнал xray.");
    }

    /// <summary>Конфиг xray — профиль узла из подписки плюс те же правки, что в Windows-клиенте:
    /// socks-инбаунд со sniffing (доменные правила для трафика из TUN), keep-alive для TCP и
    /// XHTTP, правило .ru → direct, "Сайты мимо VPN" первым правилом и подстановка IP вместо
    /// хостнейма сервера (резолв до старта туннеля, чтобы xray не зависел от DNS через себя же).</summary>
    private async Task<string> WriteXrayConfigAsync(VlessNode node)
    {
        var config = JsonNode.Parse(node.ConnectPayloadJson)!.AsObject();
        XrayQuietDefaults.Apply(config);
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
        // 600: в конфиге VLESS-UUID клиента — как токены, читать его должен только сам пользователь.
        SecretFile.Write(path, config.ToJsonString());
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
                    // 1. Петля: собственный трафик xray (и его DNS) — напрямую, иначе он
                    // перехватывался бы и уходил обратно в xray по кругу.
                    new JsonObject
                    {
                        ["process_name"] = new JsonArray("xray", "xray-legacy"),
                        ["outbound"] = "direct"
                    },
                    // 2. DNS — уже после исключения xray, но ДО direct для приложения и
                    // байпасов: иначе DNS-запросы самого приложения (API, обновления, проверка
                    // утечек) уходили открытым UDP мимо туннеля, а проверка утечек при этом
                    // рапортовала «безопасно», видя резолвер Cloudflare.
                    new JsonObject { ["protocol"] = "dns", ["action"] = "hijack-dns" },
                    // 3. Само приложение (TCP — напрямую, DNS теперь уходит в пункт 2) и
                    // «Приложения мимо VPN» (Настройки → Подключение).
                    new JsonObject
                    {
                        ["process_name"] = new JsonArray(new[] { AppProcessName }.Concat(new AppSettings().BypassApps)
                            .Select(name => (JsonNode)JsonValue.Create(name)!).ToArray()),
                        ["outbound"] = "direct"
                    },
                    new JsonObject { ["ip_cidr"] = new JsonArray("169.254.0.0/16"), ["action"] = "reject" }),
                ["final"] = "socks-out"
            }
        };
        var path = Path.Combine(StateDir, "sing-box-config.json");
        // 600: внутри секреты профиля узла (см. WriteXrayConfigAsync).
        SecretFile.Write(path, config.ToJsonString());
        return path;
    }

    private static string? TryReadLogTail(string path, int maxChars)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var length = (int)Math.Min(stream.Length, maxChars);
            var seeked = stream.Length > length; // читаем не с начала файла
            stream.Seek(-length, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd().Trim();
            // Разрез посередине многобайтового UTF-8-символа даёт битый знак в начале —
            // отбрасываем неполный фрагмент до первого перевода строки.
            if (seeked)
            {
                var nl = text.IndexOf('\n');
                if (nl >= 0) text = text[(nl + 1)..];
            }
            return text;
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
