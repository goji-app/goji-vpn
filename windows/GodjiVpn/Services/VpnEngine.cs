using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using GodjiVpn.Models;

namespace GodjiVpn.Services;

/// <summary>
/// Поднимает системный VPN-туннель связкой из двух процессов, каждый занят тем, в чём
/// специализирован: xray.exe отдаёт только локальный SOCKS5 на 127.0.0.1 (протокол VLESS+
/// XHTTP к бэкенду, плюс он же обслуживает собственные запросы приложения — см.
/// TunnelAwareProxy), а sing-box.exe поднимает Wintun-адаптер и захватывает весь системный
/// трафик, форвардя его на этот же локальный SOCKS xray.exe. Раньше пробовали и связку
/// xray+tun2socks (tun2socks систематически падал без единой трассы в логах), и нативный
/// "tun" inbound самого xray-core (менее обкатан на Windows, был замечен рассинхрон между
/// его внутренним L3-стеком и тем, что реально видела ОС) — sing-box взят вместо обоих
/// потому что это тот же паттерн "TUN поверх стороннего core через SOCKS", на котором
/// работают референсные open-source клиенты (NekoBox/Hiddify и т.п.): xray сам по себе
/// исторически не был рассчитан на TUN, а у sing-box'а auto_route/auto_detect_interface —
/// зрелая, годами обкатанная реализация именно этой задачи.
///
/// Единственный экземпляр создаётся один раз при старте приложения и хранится в Current —
/// на него завязан tunnel-aware прокси-селектор в ApiClient (см. TunnelAwareProxy), той же
/// идеи, что и tunnelAwareProxySelector() в Android: пока туннель поднят, собственные запросы
/// приложения к gojihub.xyz тоже идут через локальный SOCKS, а не напрямую.
/// </summary>
public sealed class VpnEngine : INotifyPropertyChanged
{
    // 10808 — тот же дефолтный SOCKS-порт всей v2ray/xray-экосистемы, что слушают и Happ, и
    // Incy (проверено вживую — их собственные config.json/tunnel.yml на этой машине). Здесь
    // сознательно взят ИМЕННО он, а не какой-то свой отдельный — так требовалось явно. Риск —
    // при параллельном запуске с Happ/Incy порт уже занят чужим процессом: тогда наш xray.exe
    // не сможет забиндиться, а наивная проверка готовности через TCP-connect (см.
    // WaitForSocksReadyAsync) в этом случае ложно считает готовым ЧУЖОЙ listener — трафик
    // молча пошёл бы через чужое приложение вместо нашего сервера. Поэтому перед стартом
    // xray.exe отдельно проверяем, что порт СВОБОДЕН (см. EnsurePortFree в ConnectAsync) —
    // если нет, ConnectAsync явно падает с понятной причиной вместо тихой путаницы маршрута.
    public const int SocksPort = 10808;
    private const string RequestedAdapterName = "Godji";
    private const string AdapterIp = "172.19.0.1";
    private const string AdapterSubnetCidr = "172.19.0.0/24";
    private const string DnsIp = "1.1.1.1";

    public static VpnEngine? Current { get; private set; }

    private static string RuntimeDir => Path.Combine(AppContext.BaseDirectory, "Runtime");
    private static string StateDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodjiVpn");

    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

    private Process? _xrayProcess;
    private Process? _singBoxProcess;
    private StreamWriter? _xrayLog;
    private StreamWriter? _singBoxLog;
    private StreamWriter? _engineLog;

    /// <summary>Запись в engine.log из других сервисов (проверка утечек и т.п.).</summary>
    public static void Log(string message) => Current?.LogEngine(message);

    private void LogEngine(string message)
    {
        try
        {
            if (_engineLog == null)
            {
                Directory.CreateDirectory(Path.Combine(StateDir, "logs"));
                _engineLog = new StreamWriter(Path.Combine(StateDir, "logs", "engine.log"), append: false) { AutoFlush = true };
            }
            _engineLog.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {message}");
        }
        catch { /* диагностика — не должна мешать основной работе */ }
    }

    /// <summary>Реальное имя адаптера, под которым Windows его в итоге показывает — Windows
    /// иногда переименовывает свежесозданный Wintun-адаптер обратно в generic "Подключение по
    /// локальной сети N" в процессе своей собственной идентификации новой сети, независимо от
    /// того, какое имя запрошено в inbound-настройках (interfaceName). Поэтому все операции
    /// над адаптером (маршруты, DNS, счётчики трафика) идут по этому полю, а не по константе
    /// RequestedAdapterName — оно выставляется один раз в WaitForAdapterAsync по факту (по
    /// интерфейсу с описанием "Wintun...", появившемуся после старта xray.exe), а не по
    /// запрошенному имени.</summary>
    private string? _actualAdapterName;

    /// <summary>Адрес физического интерфейса, определённый ДО переключения маршрута на TUN (см.
    /// ConnectAttemptAsync). Нужен и пингу серверов (PingService): пока туннель поднят, его
    /// временные xray.exe привязываются к этому адресу и меряют задержку до узла напрямую, мимо
    /// туннеля — иначе замер шёл бы "через два сервера" (порт смысла Android 1f63522).</summary>
    public string? PhysicalIp { get; private set; }

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
        Directory.CreateDirectory(StateDir);
    }

    /// <summary>До 3 попыток с короткой паузой между ними вместо немедленной сдачи на первой же
    /// переходной неудаче (порт временно занят осиротевшим процессом, медленная инициализация
    /// Wintun, разовый сбой сети) — раньше пользователю приходилось вручную жать "Включить"
    /// заново при том, что вторая попытка почти наверняка сработала бы сама. Порт из Android
    /// (67c25fd: GodjiVpnService.performConnect retries up to 3 times).</summary>
    private const int MaxConnectAttempts = 3;

    private VlessNode? _lastNode;

    /// <summary>Переподключение к тому же узлу — правила xray (например "Сайты мимо VPN")
    /// читаются только при подключении.</summary>
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

            for (var attempt = 1; attempt <= MaxConnectAttempts; attempt++)
            {
                try
                {
                    await ConnectAttemptAsync(node).ConfigureAwait(false);
                    ConnectedSinceUtc = DateTime.UtcNow;
                    IsRunning = true;
                    LogEngine("ConnectAsync: success, IsRunning=true");
                    return;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    LogEngine($"ConnectAsync: попытка {attempt}/{MaxConnectAttempts} FAILED — {ex}");
                    Teardown();
                    if (attempt == MaxConnectAttempts) throw;
                    await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            LogEngine($"ConnectAsync: FAILED — {ex}");
            Teardown();
        }
        finally
        {
            IsConnecting = false;
            _lifecycleLock.Release();
        }
    }

    private async Task ConnectAttemptAsync(VlessNode node)
    {
        // Осиротевшие xray.exe/sing-box.exe от прошлой аварийно завершённой сессии
        // (жёсткий Kill() иногда не убивает процесс — см. Teardown) держат наш SOCKS-порт
        // занятым или Wintun-адаптер захваченным — новый xray.exe тогда не может
        // стартовать ("Only one usage of each socket address..."), а наша проверка
        // готовности SOCKS ошибочно считает готовым ЧУЖОЙ старый listener — конкретно так
        // и проявлялось "VPN не работает" на практике.
        KillStrayCoreProcesses();
        // Наши же осиротевшие процессы уже отловлены выше — если порт 10808 всё ещё занят
        // после этого, значит слушает кто-то посторонний (Happ, Incy, любой другой v2ray-
        // клиент, запущенный параллельно). Явная проверка ДО старта xray.exe — иначе
        // WaitForSocksReadyAsync ниже успел бы принять этот чужой listener за наш готовый
        // (TCP-connect к нему отвечает мгновенно, раньше, чем наш xray.exe вообще успевает
        // стартовать или упасть на занятом порту), и весь трафик пользователя тихо ушёл бы
        // через чужое приложение вместо нашего сервера.
        EnsurePortFree(SocksPort);
        // Wintun-драйвер переиспользует один и тот же адаптер между запусками (см.
        // WaitForAdapterAsync) — а значит и IP, оставшийся на нём от ПРЕДЫДУЩЕЙ сессии
        // (нативный tun-inbound xray, отдельные тесты и т.п.), тоже никуда не девается.
        // sing-box, поднимая tun, пытается сам назначить свой address и падает с "The
        // object already exists", если тот уже там висит — чистим адаптер заранее.
        await CleanupStaleAdapterAsync().ConfigureAwait(false);

        // Должно определяться ДО того, как sing-box переключит системный default route на
        // TUN — иначе этот же трюк уже вернёт адрес самого TUN-адаптера, а не реального
        // физического интерфейса. См. комментарий у sendThrough в WriteXrayConfig.
        var physicalIp = GetLocalOutboundIp();
        PhysicalIp = physicalIp;
        LogEngine($"physical outbound IP: {physicalIp ?? "(не определён)"}");

        var xrayConfigPath = await WriteXrayConfigAsync(node, physicalIp).ConfigureAwait(false);
        _xrayProcess = StartProcess(
            Path.Combine(RuntimeDir, "xray.exe"), $"run -c \"{xrayConfigPath}\"",
            RuntimeDir, out _xrayLog, "xray.log",
            env => env["XRAY_LOCATION_ASSET"] = RuntimeDir);
        LogEngine($"xray.exe started, pid={_xrayProcess.Id}");
        AttachExitWatch(_xrayProcess, "xray.exe");

        await WaitForSocksReadyAsync(_xrayProcess, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        LogEngine("SOCKS ready");

        // Системный захват трафика — отдельным процессом sing-box.exe, а не внутри
        // xray.exe (пробовали оба других варианта — tun2socks и нативный "tun" inbound
        // xray-core — см. комментарий класса). sing-box поднимает Wintun-адаптер и сам,
        // через auto_route/auto_detect_interface (см. WriteSingBoxConfig), назначает ему
        // адрес/DNS/default route и привязывает форвардинг на локальный SOCKS xray.exe —
        // ровно та же роль, что раньше играл tun2socks, только зрелой, специально под это
        // заточенной реализацией.
        var singBoxConfigPath = WriteSingBoxConfig();
        _singBoxProcess = StartProcess(
            Path.Combine(RuntimeDir, "sing-box.exe"), $"run -c \"{singBoxConfigPath}\"",
            RuntimeDir, out _singBoxLog, "sing-box.log");
        LogEngine($"sing-box.exe started, pid={_singBoxProcess.Id}");
        AttachExitWatch(_singBoxProcess, "sing-box.exe");

        // 25с оказалось мало у реального пользователя — по логу sing-box адаптер (с уже
        // ходящим через него трафиком) поднимался почти к самому краю этого окна на его
        // машине; 40с даёт запас на медленную инициализацию Wintun/сетевого профиля
        // Windows, не удлиняя типичный (быстрый) случай — цикл возвращается сразу же, как
        // адаптер найден, а не ждёт полный таймаут.
        _actualAdapterName = await WaitForAdapterAsync(TimeSpan.FromSeconds(40), _singBoxProcess).ConfigureAwait(false);
        LogEngine($"adapter found: {_actualAdapterName}");

        // Пока IsRunning ещё false, OnCoreProcessExitedAsync на смерть любого из ядер молча
        // выходит (это "штатное" состояние для завершения ПОСЛЕ отключения) — то есть если
        // xray/sing-box успели упасть где-то между своим стартом и этой точкой (а такое
        // бывает — см. историю, когда xray падал через ~150мс после старта из-за занятого
        // порта, пока SOCKS-проверка успевала застать ещё не закрывшийся listener ЧУЖОГО
        // процесса), само подключение раньше всё равно объявлялось успешным. Явная
        // проверка здесь — последний рубеж перед тем, как показать пользователю "Подключено".
        if (_xrayProcess.HasExited || _singBoxProcess.HasExited)
        {
            var deadLabel = _xrayProcess.HasExited ? "xray.exe" : "sing-box.exe";
            throw new InvalidOperationException($"{deadLabel} завершился во время подключения — см. Runtime/logs/.");
        }
    }

    public async Task DisconnectAsync()
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            Teardown();
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>Живые счётчики трафика — берутся с самого tun-адаптера (весь системный
    /// трафик реально идёт через него), а не с процесса приложения, как TrafficStats в
    /// Android (там это трафик именно приложения, здесь эквивалента такому же счётчику для
    /// произвольного процесса на Windows без доп. драйвера нет, а адаптер отражает то же
    /// самое — на этот момент весь остальной трафик машины и так должен идти через него).</summary>
    public (long RxBytes, long TxBytes)? ReadAdapterCounters()
    {
        if (_actualAdapterName == null) return null;
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => n.Name == _actualAdapterName && n.OperationalStatus == OperationalStatus.Up);
        if (nic == null) return null;
        var stats = nic.GetIPv4Statistics();
        return (stats.BytesReceived, stats.BytesSent);
    }

    private void Teardown()
    {
        LogEngine("Teardown: start");
        KillProcess(ref _singBoxProcess, ref _singBoxLog, "sing-box.exe");
        KillProcess(ref _xrayProcess, ref _xrayLog, "xray.exe");
        _actualAdapterName = null;
        PhysicalIp = null;
        IsRunning = false;
        ConnectedSinceUtc = null;
        LogEngine("Teardown: done");
    }

    /// <summary>Раньше неожиданная смерть ядра ПОСЛЕ успешного подключения была не видна
    /// вообще — IsRunning оставался true, интерфейс продолжал молча показывать "Подключено",
    /// хотя туннеля уже нет (ровно так и проявлялось "VPN не работает, но статус подключён").
    /// Теперь процесс, упав самостоятельно, сам переводит состояние в отключённое с понятной
    /// причиной.</summary>
    private void AttachExitWatch(Process process, string label)
    {
        process.Exited += (_, _) => _ = OnCoreProcessExitedAsync(process, label);
    }

    private async Task OnCoreProcessExitedAsync(Process process, string label)
    {
        var exitCodeEarly = -1;
        try { exitCodeEarly = process.ExitCode; } catch { /* см. ниже */ }
        LogEngine($"{label} Exited event fired (code {exitCodeEarly}), IsRunning={IsRunning}, IsConnecting={IsConnecting}");
        if (!IsRunning) return; // штатное отключение (Teardown сам останавливает процессы) либо смерть во время самого подключения (см. комментарий ниже)
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsRunning) return; // отключение уже началось, пока ждали лок — не дублируем
            var exitCode = -1;
            try { exitCode = process.ExitCode; } catch { /* процесс мог быть уже уничтожен к этому моменту */ }
            LastError = $"{label} неожиданно завершился (код {exitCode}) — туннель разорван. См. Runtime/logs/.";
            LogEngine($"{label}: triggering teardown, exitCode={exitCode}");
            Teardown();
        }
        finally { _lifecycleLock.Release(); }
    }

    /// <summary>xray занят только протоколом (VLESS+XHTTP к бэкенду) — системный захват
    /// трафика теперь целиком на стороне sing-box (см. WriteSingBoxConfig), поэтому конфиг
    /// снова простой: один SOCKS5-инбаунд для sing-box (весь системный трафик) и для
    /// собственных запросов приложения (TunnelAwareProxy).</summary>
    private async Task<string> WriteXrayConfigAsync(VlessNode node, string? physicalIp)
    {
        var config = JsonNode.Parse(node.ConnectPayloadJson)!.AsObject();
        config["inbounds"] = new JsonArray(new JsonObject
        {
            ["tag"] = "socks-in",
            ["listen"] = "127.0.0.1",
            ["port"] = SocksPort,
            ["protocol"] = "socks",
            ["settings"] = new JsonObject { ["udp"] = true },
            // sing-box читает из TUN уже IP-пакеты — DNS давно отработал на уровне ОС, и
            // CONNECT в этот SOCKS всегда приходит с IP, а не доменом. Без sniffing ни одно
            // доменное правило routing.rules (наше .ru → direct ниже, ru-domain-direct и
            // трекеры из профиля, "Сайты мимо VPN") для системного трафика не срабатывало —
            // всё молча уходило в catch-all прокси. Sniffing достаёт домен из SNI/Host/QUIC
            // первых байт соединения — как в Android (GodjiVpnService.establishTunnel).
            // routeOnly: домен — только для выбора маршрута, соединение идёт на исходный IP
            // без повторного резолва (повторный DNS-запрос xray.exe рисковал бы петлёй через
            // TUN — см. комментарий про "no such host" ниже).
            ["sniffing"] = new JsonObject
            {
                ["enabled"] = true,
                ["destOverride"] = new JsonArray("http", "tls", "quic"),
                ["routeOnly"] = true
            }
        });

        // sing-box.auto_detect_interface (см. WriteSingBoxConfig) привязывает к физическому
        // интерфейсу исходящие соединения ТОЛЬКО самого sing-box — xray.exe отдельный процесс,
        // ему эта защита не достаётся сама по себе. Профиль бэкенда — почти всегда балансировщик
        // ("proxy"/"proxy-2"/...) ПЛЮС "freedom"-outbound "direct" для российских IP/доменов,
        // торрент-трекеров и части DNS-правил (см. живой конфиг: ru-ip-direct/ru-domain-direct/
        // torrent-*-direct всё используют tag "direct"). Раз системный default route теперь
        // всегда через TUN, ЛЮБОЕ исходящее соединение xray без явной привязки к физическому
        // интерфейсу само попадает обратно в TUN → sing-box → снова в SOCKS xray → снова
        // "direct"/"proxy" — петля. Для "proxy"-балансировщика это почти незаметно (один и тот
        // же TCP держится подолгу через xmux-мультиплексирование), а вот "direct" открывает
        // НОВОЕ соединение на каждый запрос — реально пойманный кейс: сотни соединений в
        // секунду к 77.88.8.8:443 (DoH Яндекса, попадает под ru-ip-direct), исчерпание портов
        // ("Only one usage of each socket address") и деградация всей сети, включая несвязанные
        // сайты. sendThrough — тот же смысл, что и auto_detect_interface у sing-box, только
        // на уровне отдельного outbound: явно указывает xray, с какого локального адреса
        // дозваниваться, вместо того чтобы отдавать выбор системной таблице маршрутизации.
        foreach (var ob in config["outbounds"]?.AsArray() ?? new JsonArray())
        {
            var protocol = ob?["protocol"]?.GetValue<string>();
            // Раньше все правки ниже применялись ТОЛЬКО к "vless" — работали для остальных
            // proxy-протоколов профиля (vmess/trojan/shadowsocks/hysteria — см.
            // SubscriptionService.ProxyProtocols, там тот же список) только случайно, если
            // бэкенд вообще их присылал. sendThrough нужен им всем по той же причине, что и
            // vless — протокол тут ни при чём, важно только что это proxy-outbound, а не
            // "freedom"/direct (для direct — только sendThrough, дальше он не нужен).
            var isProxyProtocol = protocol is "vless" or "vmess" or "trojan" or "shadowsocks" or "hysteria";
            // "freedom" ("direct") нужен здесь наравне с proxy-протоколами: это тот самый
            // outbound, в который ru-ip-direct/ru-domain-direct/torrent-*-direct с бэкенда (и
            // наше собственное domain:ru-правило ниже) заворачивают трафик мимо туннеля. Без
            // sendThrough у него в точности та же петля, что была у "proxy" до фикса —
            // исходящее соединение "direct" без явной привязки к физическому интерфейсу само
            // попадает обратно в TUN → sing-box → снова в SOCKS xray → снова "direct", и
            // получаем тот же шторм соединений/исчерпание портов, который уже один раз ловили.
            if (!isProxyProtocol && protocol != "freedom") continue;

            if (!string.IsNullOrEmpty(physicalIp))
                ob!["sendThrough"] = physicalIp;

            if (!isProxyProtocol) continue; // дальше — правки для proxy-outbound'ов (любой протокол/транспорт)

            // TCP keep-alive на уровне сокета — часть серверов профиля работает поверх голого
            // TCP (streamSettings.network "tcp", напр. VLESS+TLS+Vision у европейских узлов —
            // живой пример: sockopt.tcpKeepAliveIdle=100 у СЕРВЕРА на его собственном inbound),
            // не XHTTP, и туда фикс с xmux.hKeepAlivePeriod ниже не достаёт вовсе (это чисто
            // HTTP-уровневый пинг внутри XHTTP-обёртки, у голого TCP такой обёртки просто нет).
            // Xray-core сам по себе для outbound без явного sockopt уже применяет дефолт 45с и
            // на tcpKeepAliveIdle, и на tcpKeepAliveInterval (см. xtls.github.io/config/
            // transports/sockopt.html) — не "совсем без keep-alive", как можно было бы
            // предположить, но за 45с обычным NAT/файрволам ещё есть время оборвать
            // простаивающее соединение раньше первой пробы. Явно выставляем ОБА поля (idle и
            // interval), а не только interval, как раньше — idle короче серверных 100с, чтобы
            // клиент начинал пробы заведомо раньше, чем истечёт терпение промежуточных узлов.
            // sockopt — общее поле streamSettings, не зависит от network,
            // ставим его для любого TCP-based proxy-outbound'а (для XHTTP — лишний, но не
            // мешающий уровень защиты поверх xmux-пинга ниже, не замена ему). Hysteria — поверх
            // QUIC/UDP, не TCP: SO_KEEPALIVE тут не при чём (свой keepalive на уровне протокола,
            // см. streamSettings.hysteriaSettings.udpIdleTimeout в самом профиле бэкенда).
            var streamSettings = ob!["streamSettings"]?.AsObject();
            if (streamSettings != null && protocol != "hysteria")
            {
                var sockopt = streamSettings["sockopt"]?.AsObject();
                if (sockopt == null) { sockopt = new JsonObject(); streamSettings["sockopt"] = sockopt; }
                if (sockopt["tcpKeepAliveIdle"] == null || sockopt["tcpKeepAliveIdle"]!.GetValue<int>() == 0)
                    sockopt["tcpKeepAliveIdle"] = 30;
                if (sockopt["tcpKeepAliveInterval"] == null || sockopt["tcpKeepAliveInterval"]!.GetValue<int>() == 0)
                    sockopt["tcpKeepAliveInterval"] = 30;
            }

            // Профиль с бэкенда шлёт "hKeepAlivePeriod":0 (без keep-alive пингов на уровне
            // XHTTP) — для короткого запрос-ответа (обычная загрузка страницы) это незаметно,
            // но у долгоживущего, преимущественно "тихого" соединения (реальный кейс — десктоп-
            // клиент Claude: держит соединение открытым для real-time апдейтов, не шлёт данные
            // подолгу) есть все шансы быть незаметно оборванным где-то на промежуточном узле
            // без keep-alive — xray на своей стороне при этом не видит ни ошибки, ни закрытия
            // (реально пойманный кейс: "tunneling request" уходит, дальше — тишина, ни данных,
            // ни завершения). Ненулевой период держит XHTTP-обёртку живой на таких простоях.
            // Проверка по факту наличия xhttpSettings, а не по протоколу — в этом профиле
            // XHTTP пока встречается только у VLESS+Reality, но привязывать фикс к конкретному
            // протоколу незачем: сработает для чего угодно поверх XHTTP.
            var xmux = ob?["streamSettings"]?["xhttpSettings"]?["extra"]?["xmux"];
            if (xmux != null && (xmux["hKeepAlivePeriod"] == null || xmux["hKeepAlivePeriod"]!.GetValue<int>() == 0))
                xmux["hKeepAlivePeriod"] = 30;
        }

        // Явный запрос пользователя: вся зона .ru — в direct, не только то, что покрывает
        // geosite-категория "ru" с бэкенда (ru-domain-direct и т.п. — она построена на списке
        // популярных доменов, не гарантирует буквально каждый *.ru). Вставляем ПЕРВЫМ правилом
        // в routing.rules — xray применяет первое совпавшее правило по порядку, так что это
        // гарантированно перебивает любое другое (в том числе "proxy" для отдельного .ru-домена,
        // если такой вдруг попадётся). Работает только если у бэкенда есть outbound с тегом
        // "direct" (freedom-outbound, см. sendThrough выше) — если нет, тихо ничего не делаем,
        // чтобы не сослаться на несуществующий outboundTag и не сломать остальной роутинг.
        var hasDirectOutbound = (config["outbounds"]?.AsArray() ?? new JsonArray())
            .Any(ob => ob?["tag"]?.GetValue<string>() == "direct");
        if (hasDirectOutbound)
        {
            var routing = config["routing"]?.AsObject();
            if (routing == null) { routing = new JsonObject { ["domainStrategy"] = "AsIs" }; config["routing"] = routing; }
            var rules = routing["rules"]?.AsArray();
            if (rules == null) { rules = new JsonArray(); routing["rules"] = rules; }
            rules.Insert(0, new JsonObject
            {
                ["type"] = "field",
                ["domain"] = new JsonArray("regexp:\\.ru$"),
                ["outboundTag"] = "direct"
            });
        }

        ApplyUserBypassDomains(config, physicalIp);

        // Реально пойманная ошибка (повторяется каждые ~15с всю сессию, не разовый глюк):
        // "dial tcp: lookup serv1.gojihub.xyz: no such host". Первая попытка исправить —
        // прописать serv1.gojihub.xyz и т.п. статикой в dns.hosts — НЕ помогла: ошибка
        // повторялась даже с готовой hosts-записью. Значит транспорт XHTTP резолвит адрес
        // outbound'а не через DNS-подсистему самого xray (где hosts реально применяется), а
        // через обычный Go-резолвер стандартной библиотеки на более низком уровне — тот тоже
        // подвержен той же петле (xray.exe без явной привязки шлёт DNS-запрос, тот уходит в
        // TUN → sing-box → обратно в SOCKS xray → снова нужен рабочий прокси-кандидат, чтобы
        // резолвить ЛЮБОГО из прокси-кандидатов). Надёжный фикс — вообще не резолвить: раз мы
        // и так уже знаем реальный IP (резолвим его здесь сами, ещё до старта sing-box, пока
        // системный маршрут не переключён на TUN), просто подставляем IP вместо хостнейма
        // прямо в address самого outbound'а. SNI (realitySettings.serverName) и Host-заголовок
        // домена-прикрытия (xhttpSettings.host) — отдельные поля, их не трогаем, поэтому
        // маскировка под обычный HTTPS к google.com/zara.com и т.п. не ломается.
        foreach (var ob in config["outbounds"]?.AsArray() ?? new JsonArray())
        {
            if (ob?["protocol"]?.GetValue<string>() != "vless") continue;
            var vnext = ob["settings"]?["vnext"]?.AsArray()?.FirstOrDefault();
            var hostname = vnext?["address"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(hostname) || System.Net.IPAddress.TryParse(hostname, out _)) continue;

            try
            {
                var addresses = await System.Net.Dns.GetHostAddressesAsync(hostname).ConfigureAwait(false);
                var ip = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString();
                if (ip != null) vnext!["address"] = ip;
            }
            catch { /* один нерезолвящийся кандидат не должен ломать весь конфиг — оставляем хостнейм, xray попробует резолвить его сам, как раньше */ }
        }

        var path = Path.Combine(StateDir, "xray-config.json");
        File.WriteAllText(path, config.ToJsonString());
        return path;
    }

    /// <summary>"Сайты мимо VPN" (Настройки → Подключение, порт 6803326): домены из
    /// AppSettings (с поддоменами) — напрямую. Правило ставится ПЕРВЫМ, раньше любого правила
    /// профиля и нашего .ru; домены распознаёт sniffing socks-инбаунда. Outbound — freedom
    /// профиля ("direct", уже с sendThrough), а если его нет — свой с привязкой к физическому
    /// адаптеру (без неё — та же петля через TUN, что описана выше).</summary>
    private void ApplyUserBypassDomains(JsonObject config, string? physicalIp)
    {
        var domains = new AppSettings().BypassDomains;
        if (domains.Count == 0) return;
        var outbounds = config["outbounds"]?.AsArray();
        if (outbounds == null) { outbounds = new JsonArray(); config["outbounds"] = outbounds; }
        var directTag = outbounds
            .Where(ob => ob?["protocol"]?.GetValue<string>() == "freedom")
            .Select(ob => ob?["tag"]?.GetValue<string>())
            .FirstOrDefault(tag => !string.IsNullOrEmpty(tag));
        if (directTag == null)
        {
            directTag = "user-bypass-direct";
            var own = new JsonObject { ["tag"] = directTag, ["protocol"] = "freedom" };
            if (!string.IsNullOrEmpty(physicalIp)) own["sendThrough"] = physicalIp;
            outbounds.Add(own);
        }
        var routing = config["routing"]?.AsObject();
        if (routing == null) { routing = new JsonObject { ["domainStrategy"] = "AsIs" }; config["routing"] = routing; }
        var rules = routing["rules"]?.AsArray();
        if (rules == null) { rules = new JsonArray(); routing["rules"] = rules; }
        rules.Insert(0, new JsonObject
        {
            ["type"] = "field",
            ["domain"] = new JsonArray(domains.Select(d => (JsonNode)JsonValue.Create("domain:" + d)!).ToArray()),
            ["outboundTag"] = directTag
        });
        LogEngine($"bypass: {domains.Count} доменов мимо VPN → {directTag}");
    }

    /// <summary>Локальный адрес, который ОС выбрала бы для исходящего соединения к интернету
    /// прямо сейчас — трюк с UDP "connect" не шлёт ни одного пакета (UDP без рукопожатия),
    /// только заставляет ядро ОС разрешить маршрут и назначить сокету локальный адрес по нему;
    /// null, если сети сейчас нет вообще.</summary>
    private static string? GetLocalOutboundIp()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 65530);
            return ((System.Net.IPEndPoint)socket.LocalEndPoint!).Address.ToString();
        }
        catch { return null; }
    }

    /// <summary>sing-box поднимает Wintun-адаптер (inbound "tun") и форвардит всё на локальный
    /// SOCKS xray.exe (outbound "socks-out"). auto_route/strict_route — сам добавляет
    /// default route 0.0.0.0/0 на адаптер и WFP-правила против утечки DNS мимо туннеля;
    /// auto_detect_interface — сам привязывает СОБСТВЕННЫЕ исходящие соединения sing-box к
    /// реальному физическому интерфейсу (тут их и нет, кроме самого DNS-резолва, но это и есть
    /// защита от петли "исходящее к серверу зацикливается через свежедобавленный default route
    /// на сам туннель", которую раньше набивали вручную через route.exe-исключения на каждый
    /// IP из балансировщика). ip_cidr-правило на 172.19.0.0/24 и 169.254.0.0/16 — та же защита
    /// от NBNS/APIPA broadcast-шторма, что раньше стояла в самом xray, только теперь на
    /// уровне, где эта broadcast-мусорка реально возникает (сам TUN-адаптер).</summary>
    private string WriteSingBoxConfig()
    {
        var config = new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = "info" },
            // sing-box >= 1.12 убрал "легаси" формат DNS-сервера ("address": "udp://...") —
            // с 1.14 конфиг с ним не грузится вообще ("legacy DNS server formats ... removed
            // in sing-box 1.14.0"). Новый формат — явный "type"/"server", "detour" остался
            // (он теперь просто одно из общих Dial Fields, тех же, что и у outbound).
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
                ["interface_name"] = RequestedAdapterName,
                ["address"] = new JsonArray($"{AdapterIp}/24"),
                // 1400 не хватало: пакет после инкапсуляции (VLESS+TLS+XHTTP/HTTP2-обёртка
                // поверх реального TCP к серверу) мог превышать физический MTU (1500) и
                // фрагментироваться — а ICMP "fragmentation needed" часто молча теряется где-то
                // по пути через VPN-цепочки (PMTU black hole), из-за чего застревают именно
                // КРУПНЫЕ ответы (потоковые данные), а не рукопожатие/заголовки. 1280 — тот же
                // консервативный запас, что берут WireGuard/большинство VPN по умолчанию.
                ["mtu"] = 1280,
                ["auto_route"] = true,
                ["strict_route"] = true,
                ["stack"] = "system"
            }),
            ["outbounds"] = new JsonArray(new JsonObject
            {
                ["type"] = "socks",
                ["tag"] = "socks-out",
                ["server"] = "127.0.0.1",
                ["server_port"] = SocksPort,
                ["version"] = "5"
            }),
            ["route"] = new JsonObject
            {
                ["auto_detect_interface"] = true,
                ["rules"] = new JsonArray(new JsonObject
                {
                    ["ip_cidr"] = new JsonArray(AdapterSubnetCidr, "169.254.0.0/16"),
                    ["action"] = "reject"
                }),
                ["final"] = "socks-out"
            }
        };

        var path = Path.Combine(StateDir, "sing-box-config.json");
        File.WriteAllText(path, config.ToJsonString());
        return path;
    }

    private static Process StartProcess(
        string exePath, string args, string workingDir,
        out StreamWriter log, string logFileName,
        Action<System.Collections.Generic.IDictionary<string, string?>>? configureEnv = null)
    {
        if (!File.Exists(exePath))
            throw new FileNotFoundException($"Не найден исполняемый файл: {exePath}. Положите бинарники ядра в папку Runtime рядом с GodjiVpn.exe.", exePath);

        // append: true — раньше при переподключении файл лога перезаписывался с нуля, и если
        // ядро падало почти сразу после старта (см. Teardown → следующий ConnectAsync),
        // причина падения терялась ещё до того, как её успевали посмотреть. Разделитель ниже
        // делает границы отдельных запусков видимыми даже при накоплении лога.
        Directory.CreateDirectory(Path.Combine(StateDir, "logs"));
        log = new StreamWriter(Path.Combine(StateDir, "logs", logFileName), append: true) { AutoFlush = true };
        log.WriteLine($"===== launch {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} =====");

        var psi = new ProcessStartInfo(exePath, args)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        configureEnv?.Invoke(psi.Environment);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var localLog = log;
        process.OutputDataReceived += (_, e) => { if (e.Data != null) localLog.WriteLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) localLog.WriteLine(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private void KillProcess(ref Process? process, ref StreamWriter? log, string label)
    {
        if (process != null)
        {
            try
            {
                var alreadyExited = process.HasExited;
                LogEngine($"KillProcess({label}): HasExited={alreadyExited}, pid={(alreadyExited ? -1 : process.Id)}");
                if (!alreadyExited) process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) { LogEngine($"KillProcess({label}): Kill() threw — {ex.Message}"); }
            process.Dispose();
            process = null;
        }
        log?.Dispose();
        log = null;
    }

    /// <summary>Убивает xray.exe/sing-box.exe (и tun2socks.exe — от старых версий приложения,
    /// вдруг ещё остался живой), запущенные ИМЕННО из нашей папки Runtime (не трогает никакие
    /// сторонние процессы с такими же именами где-то ещё на машине) — остатки предыдущей
    /// сессии, которых не добил Kill() в Teardown (см. её комментарий про entireProcessTree и
    /// почему это всё равно иногда не помогает).</summary>
    private static void KillStrayCoreProcesses()
    {
        var ourPaths = new[] { "xray.exe", "sing-box.exe", "tun2socks.exe" }
            .Select(name => Path.Combine(RuntimeDir, name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "xray", "sing-box", "tun2socks" })
        {
            foreach (var proc in Process.GetProcessesByName(name))
            {
                try
                {
                    var path = proc.MainModule?.FileName;
                    if (path != null && ourPaths.Contains(path))
                    {
                        proc.Kill(entireProcessTree: true);
                        proc.WaitForExit(2000);
                    }
                }
                catch { /* чужой процесс без доступа к MainModule, или уже завершился сам — не критично */ }
                finally { proc.Dispose(); }
            }
        }
    }

    /// <summary>Пробует сама временно забиндиться на порт — единственный надёжный способ
    /// узнать, свободен ли он, ДО того как туда полезет xray.exe (простой TCP-connect для
    /// этого не годится: до старта xray.exe там просто некому отвечать, connect провалится
    /// одинаково что при свободном порту, что при занятом — разница видна только на попытке
    /// самого bind()). Вызывается один раз, сразу отпускает порт — не держит его занятым.</summary>
    private static void EnsurePortFree(int port)
    {
        try
        {
            using var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
            probe.Start();
            probe.Stop();
        }
        catch (SocketException)
        {
            throw new InvalidOperationException(
                $"Порт {port} уже занят другим приложением — похоже, параллельно запущен Happ, Incy или другой VPN-клиент. Закройте его и подключитесь снова.");
        }
    }

    /// <summary>Проверяет готовность НАШЕГО конкретного процесса, а не просто "кто-то слушает
    /// порт 10808" — иначе при гонке со старым осиротевшим xray.exe, всё ещё держащим порт,
    /// проверка ложно считает готовым чужой listener, пока наш свежий процесс тем временем
    /// падает с "Only one usage of each socket address..." (см. комментарий в ConnectAsync).</summary>
    private static async Task WaitForSocksReadyAsync(Process xrayProcess, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (xrayProcess.HasExited)
                throw new InvalidOperationException($"xray.exe неожиданно завершился (код {xrayProcess.ExitCode}) — см. Runtime/logs/xray.log");
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync("127.0.0.1", SocksPort).ConfigureAwait(false);
                return;
            }
            catch (SocketException) { await Task.Delay(200).ConfigureAwait(false); }
        }
        throw new TimeoutException("xray.exe не поднял локальный SOCKS5 за отведённое время — см. Runtime/logs/xray.log");
    }

    /// <summary>Снимает все IPv4-адреса с уже существующего Wintun-адаптера (если он есть) —
    /// см. вызов в ConnectAsync. Лучшее усилие: если адаптера ещё нет (первый чистый запуск)
    /// или снять нечего, PowerShell просто ничего не найдёт — не считаем это ошибкой.</summary>
    private static async Task CleanupStaleAdapterAsync()
    {
        var adapter = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => n.Description.Contains("Wintun", StringComparison.OrdinalIgnoreCase));
        if (adapter == null) return;

        try
        {
            // adapter.Name идёт в PowerShell-командную строку внутри одинарных кавычек — сам
            // по себе он приходит от Windows (обычно "Godji"/"Ethernet"/"Подключение по
            // локальной сети N"), но раз имя адаптера в принципе МОЖНО переименовать (netsh/
            // Панель управления) на что угодно, включая одинарную кавычку, не полагаемся на
            // "обычно безопасное" значение — экранируем её удвоением, стандартный способ
            // литерала PowerShell (O'Brien → 'O''Brien'), иначе строка вида "x' ; <команда> ; '"
            // вырвалась бы из кавычек и выполнилась как произвольный PowerShell.
            var escapedName = adapter.Name.Replace("'", "''");
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command " +
                $"\"Get-NetIPAddress -InterfaceAlias '{escapedName}' -AddressFamily IPv4 -ErrorAction SilentlyContinue | " +
                "Remove-NetIPAddress -Confirm:$false -ErrorAction SilentlyContinue\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process != null) await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch { /* лучшее усилие — если не получится, sing-box просто ещё раз упадёт с понятной ошибкой */ }
    }

    /// <returns>Реальное имя адаптера (см. _actualAdapterName) — не обязательно
    /// RequestedAdapterName, если Windows его переименовала в процессе идентификации сети.</returns>
    private static async Task<string> WaitForAdapterAsync(TimeSpan timeout, Process singBox)
    {
        var expectedIp = IPAddress.Parse(AdapterIp);
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            // Раньше здесь сравнивалось с "интерфейсов до старта", чтобы найти именно НОВЫЙ —
            // но Wintun-драйвер переиспользует один и тот же идентификатор адаптера между
            // запусками ("Removed orphaned adapter" в логе sing-box — он не создаёт адаптер с
            // нуля, а реактивирует существующий), так что "новый" интерфейс мог никогда не
            // появиться, хотя реальный адаптер уже поднят и рабочий. Практически на машине
            // всегда ровно один Wintun-адаптер под нашу VPN — этого достаточно.
            //
            // Проверка ТОЛЬКО по Description содержит "Wintun" оказалась недостаточной у
            // реального пользователя: лог sing-box подтверждал, что адаптер поднят и трафик
            // (172.19.0.1) через него уже ходит (DNS, inbound/outbound tun-соединения), а этот
            // цикл всё равно не находил его 25 секунд подряд и падал по таймауту — на той
            // машине Description либо не совпадал с ожидаемым, либо GetAllNetworkInterfaces()
            // отставал от фактического состояния адаптера. Добавлена вторая, независимая
            // проверка — по факту наличия у интерфейса собственного IPv4-адреса AdapterIp
            // (172.19.0.1), который sing-box всегда присваивает адаптеру сам и который уже
            // подтверждённо активен в её логе — это прямое доказательство готовности адаптера,
            // не зависящее от текста Description.
            //
            // Совпадение только по Description "Wintun" убрано: от аварийно оборванной прошлой
            // сессии в системе может остаться ОТКЛЮЧЁННЫЙ Wintun-адаптер — он находился за
            // 0,1с, подключение объявлялось успешным, а sing-box, так и не сумев открыть
            // интерфейс, падал через 15с ("configure tun interface: The system cannot find
            // the file specified") — у пользователя это выглядело как "туннель рвётся".
            // Теперь готовность — только поднятый адаптер с нашим адресом 172.19.0.1, а смерть
            // sing-box во время ожидания сразу даёт ошибку (и повтор попытки подключения).
            if (singBox.HasExited) break;
            var candidate = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n =>
                n.OperationalStatus == OperationalStatus.Up &&
                n.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(expectedIp)));
            if (candidate != null) return candidate.Name;
            await Task.Delay(200).ConfigureAwait(false);
        }

        // sing-box сам подчищает "осиротевший" адаптер от предыдущего аварийного завершения
        // (жёсткий Kill() не даёт Wintun-драйверу освободить его штатно), и на эту очистку с
        // пересозданием иногда уходит больше времени, чем на чистый старт — включаем хвост его
        // лога прямо в текст ошибки, чтобы не приходилось лезть в файл отдельно за диагнозом.
        var tail = TryReadLogTail(Path.Combine(StateDir, "logs", "sing-box.log"), 800);
        if (singBox.HasExited)
            throw new InvalidOperationException(
                "sing-box.exe не смог поднять сетевой адаптер." +
                (tail != null ? $" Последнее в логе: …{tail}" : " См. журнал sing-box."));
        throw new TimeoutException(
            "sing-box.exe не создал сетевой адаптер за отведённое время." +
            (tail != null ? $" Последнее в логе: …{tail}" : " См. Runtime/logs/sing-box.log"));
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

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
