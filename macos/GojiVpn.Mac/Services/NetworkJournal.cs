
using System.Text.Json;

namespace GodjiVpn.Services;

/// <summary>
/// «Журнал сети» — история работы VPN на этом компьютере, как страница аптайма: когда туннель был
/// поднят, когда и почему переключались узлы, что происходило с сетью. Порт Android
/// journal/NetworkJournal.kt (84b0fb9). Хранится только локально (network-journal.jsonl рядом с
/// остальными данными приложения, по строке JSON на событие), последние 7 дней.
///
/// Тексты событий не хранятся готовыми — только тип и параметр (имя узла, сети, текст ошибки),
/// заголовки собирает экран (см. Title/Text).
/// </summary>
public static class NetworkJournal
{
    public enum Kind
    {
        CONNECTED, DISCONNECTED, INTERRUPTED,
        NET_WIFI, NET_WIRED, NET_LOST,
        SWITCH_NODE,
        RULE_AUTOCONNECT, RULE_TRUSTED_DISCONNECT,
        ERROR, LEAK_OK, LEAK_FAIL
    }

    public sealed record Event(DateTime At, Kind Kind, string Arg = "");

    /// <summary>Час суток: доля времени под защитой 0..1 (null — час ещё не наступил) и были ли сбои.</summary>
    public sealed record Hour(double? Uptime, bool Issue);

    public sealed record Day(
        IReadOnlyList<Hour> Hours, TimeSpan Protected, TimeSpan Elapsed,
        int Connects, int Switches, int Issues, IReadOnlyList<Event> Events);

    private const int KeepDays = 7;
    private const int MaxLines = 3000;
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(5);

    private static readonly HashSet<Kind> IssueKinds = new() { Kind.INTERRUPTED, Kind.NET_LOST, Kind.ERROR, Kind.LEAK_FAIL };
    private static readonly HashSet<Kind> SwitchKinds = new() { Kind.SWITCH_NODE };

    private static readonly object Gate = new();
    private static List<Event> _events = new();
    private static bool _ready;

    // Превью-экземпляр работает с той же папкой данных, что и настоящий клиент, — в чужой журнал
    // он не пишет и показывает пустой.
    private static readonly bool Disabled = Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW") == "1";

    private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodjiVpn");
    private static string FilePath => Path.Combine(Dir, "network-journal.jsonl");
    private static string HeartbeatPath => Path.Combine(Dir, "network-journal-heartbeat.txt");

    /// <summary>Журнал изменился (новое событие) — экраны пересчитывают сводку.</summary>
    public static event Action? Changed;

    public static IReadOnlyList<Event> Events
    {
        get { lock (Gate) return Disabled && Demo ? DemoEvents() : _events.ToList(); }
    }

    private static readonly bool Demo = Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW_DEMO") == "1";

    /// <summary>Только для превью-экземпляра (GODJI_UI_PREVIEW_DEMO=1): правдоподобный день для
    /// сверки экрана журнала. В обычной работе не используется.</summary>
    private static List<Event> DemoEvents()
    {
        var day = DateTime.Now.Date;
        DateTime At(int h, int m) => day.AddHours(h).AddMinutes(m).ToUniversalTime();
        var list = new List<Event>
        {
            new(At(0, 5), Kind.CONNECTED, "Германия"),
            new(At(2, 40), Kind.NET_LOST),
            new(At(2, 41), Kind.INTERRUPTED),
            new(At(7, 55), Kind.NET_WIFI, "Дом"),
            new(At(8, 2), Kind.CONNECTED, "Нидерланды"),
            new(At(9, 30), Kind.LEAK_OK),
            new(At(12, 10), Kind.DISCONNECTED),
            new(At(12, 40), Kind.RULE_AUTOCONNECT, "Кафе"),
            new(At(12, 40), Kind.CONNECTED, "Финляндия"),
        };
        return list.Where(e => e.At <= DateTime.UtcNow).ToList();
    }

    /// <summary>При запуске приложения. Если журнал заканчивается подключением, а туннеля сейчас
    /// нет, значит процесс был убит вместе с VPN (выключение компьютера, сбой) — закрываем сессию
    /// моментом последнего «пульса».</summary>
    public static void Init(bool vpnRunningNow)
    {
        if (_ready || Disabled) return;
        _ready = true;
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-KeepDays);
            var all = File.Exists(FilePath)
                ? File.ReadAllLines(FilePath).Select(Parse).OfType<Event>().ToList()
                : new List<Event>();
            var kept = all.Where(e => e.At >= cutoff).OrderBy(e => e.At).TakeLast(MaxLines).ToList();
            if (kept.Count != all.Count)
                File.WriteAllLines(FilePath, kept.Select(Encode));
            lock (Gate) _events = kept;

            var lastSession = kept.LastOrDefault(e => e.Kind is Kind.CONNECTED or Kind.DISCONNECTED or Kind.INTERRUPTED);
            if (lastSession?.Kind == Kind.CONNECTED && !vpnRunningNow)
            {
                var beat = ReadHeartbeat() is { } b && b > lastSession.At ? b : lastSession.At;
                Append(new Event(beat, Kind.INTERRUPTED));
            }
        }
        catch { /* журнал — вспомогательная функция, его сбой не должен мешать работе VPN */ }
    }

    public static void Log(Kind kind, string arg = "")
    {
        if (!_ready || Disabled) return;
        var text = arg.Length > 160 ? arg[..160] : arg;
        Append(new Event(DateTime.UtcNow, kind, text));
        if (kind == Kind.CONNECTED) Heartbeat();
    }

    /// <summary>Пока VPN поднят — отметка «ещё работал» (см. Init).</summary>
    public static void Heartbeat()
    {
        if (!_ready || Disabled) return;
        try { File.WriteAllText(HeartbeatPath, DateTime.UtcNow.ToString("O")); } catch { }
    }

    private static DateTime? ReadHeartbeat()
    {
        try
        {
            return File.Exists(HeartbeatPath) &&
                   DateTime.TryParse(File.ReadAllText(HeartbeatPath), null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)
                ? t.ToUniversalTime() : null;
        }
        catch { return null; }
    }

    private static void Append(Event e)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(FilePath, Encode(e) + "\n");
            }
            catch { }
            _events = _events.Append(e).OrderBy(x => x.At).TakeLast(MaxLines).ToList();
        }
        Changed?.Invoke();
    }

    private static string Encode(Event e) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["t"] = new DateTimeOffset(e.At).ToUnixTimeMilliseconds(),
        ["k"] = e.Kind.ToString(),
        ["a"] = e.Arg
    });

    private static Event? Parse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!Enum.TryParse<Kind>(root.GetProperty("k").GetString(), out var kind)) return null;
            var at = DateTimeOffset.FromUnixTimeMilliseconds(root.GetProperty("t").GetInt64()).UtcDateTime;
            return new Event(at, kind, root.TryGetProperty("a", out var a) ? a.GetString() ?? "" : "");
        }
        catch { return null; }
    }

    public static bool IsSwitch(Kind kind) => SwitchKinds.Contains(kind);
    public static bool IsIssue(Kind kind) => IssueKinds.Contains(kind);

    /// <summary>Начало суток daysAgo дней назад (0 — сегодня) по местному времени, в UTC.</summary>
    public static DateTime DayStartUtc(int daysAgo, DateTime? nowUtc = null) =>
        (nowUtc ?? DateTime.UtcNow).ToLocalTime().Date.AddDays(-daysAgo).ToUniversalTime();

    /// <summary>Сводка за сутки: аптайм по часам, счётчики и события дня. runningNow — открыт ли
    /// интервал подключения прямо сейчас (тогда он тянется до текущего момента).</summary>
    public static Day DayOf(IReadOnlyList<Event> all, int daysAgo, bool runningNow, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var start = DayStartUtc(daysAgo, now);
        var end = DayStartUtc(daysAgo - 1, now);
        var limit = end < now ? end : now;

        // Интервалы под защитой, обрезанные границами суток.
        var intervals = new List<(DateTime A, DateTime B)>();
        DateTime? openAt = null;
        foreach (var e in all)
        {
            switch (e.Kind)
            {
                case Kind.CONNECTED:
                    openAt ??= e.At;
                    break;
                case Kind.DISCONNECTED or Kind.INTERRUPTED when openAt != null:
                    intervals.Add((openAt.Value, e.At));
                    openAt = null;
                    break;
            }
        }
        if (openAt != null && runningNow) intervals.Add((openAt.Value, now));
        var clipped = intervals
            .Select(i => (A: i.A > start ? i.A : start, B: i.B < limit ? i.B : limit))
            .Where(i => i.B > i.A)
            .ToList();

        var dayEvents = all.Where(e => e.At >= start && e.At < end).ToList();
        var hours = Enumerable.Range(0, 24).Select(h =>
        {
            var hs = start.AddHours(h);
            var he = hs.AddHours(1);
            if (hs >= limit) return new Hour(null, false);
            var span = ((he < limit ? he : limit) - hs).TotalMilliseconds;
            var covered = clipped.Sum(i => Math.Max(0, ((i.B < he ? i.B : he) - (i.A > hs ? i.A : hs)).TotalMilliseconds));
            return new Hour(Math.Clamp(covered / Math.Max(1, span), 0, 1), dayEvents.Any(e => e.At >= hs && e.At < he && IsIssue(e.Kind)));
        }).ToList();

        // Журнал мог начаться посреди суток (первый запуск с ним) — процент считаем от первой
        // записи, а не от полуночи, иначе первый день выглядел бы как «почти 0%».
        var first = all.Count > 0 && all[0].At > start ? all[0].At : start;
        return new Day(
            hours,
            TimeSpan.FromMilliseconds(clipped.Sum(i => (i.B - i.A).TotalMilliseconds)),
            limit > first ? limit - first : TimeSpan.Zero,
            dayEvents.Count(e => e.Kind == Kind.CONNECTED),
            dayEvents.Count(e => IsSwitch(e.Kind)),
            dayEvents.Count(e => IsIssue(e.Kind)),
            dayEvents.OrderByDescending(e => e.At).ToList());
    }

    /// <summary>Заголовок и текст события на русском (как Loc.f.journalEvent в Android).</summary>
    public static (string Title, string Text) Describe(Event e) => e.Kind switch
    {
        Kind.CONNECTED => ("Подключено", e.Arg.Length > 0 ? $"Узел «{e.Arg}»" : "Защищённый туннель поднят"),
        Kind.DISCONNECTED => ("Отключено", "VPN выключен"),
        Kind.INTERRUPTED => ("Сессия прервалась", "Приложение закрылось вместе с VPN"),
        Kind.NET_WIFI => ("Wi-Fi" + (e.Arg.Length > 0 ? $" «{e.Arg}»" : ""), "Компьютер перешёл на Wi-Fi"),
        Kind.NET_WIRED => ("Проводная сеть", "Компьютер перешёл на кабель"),
        Kind.NET_LOST => ("Связь пропала", "Нет доступной сети"),
        Kind.SWITCH_NODE => ("Смена узла", $"Выбран «{e.Arg}»"),
        Kind.RULE_AUTOCONNECT => ("Чужой Wi-Fi — VPN включён", $"Сеть «{e.Arg}», по правилу"),
        Kind.RULE_TRUSTED_DISCONNECT => ("Доверенный Wi-Fi — VPN выключен", $"Сеть «{e.Arg}», по правилу"),
        Kind.ERROR => ("Ошибка подключения", e.Arg),
        Kind.LEAK_OK => ("Проверка утечек пройдена", "IP и DNS идут через туннель"),
        Kind.LEAK_FAIL => ("Найдена утечка", "Часть запросов идёт мимо туннеля"),
        _ => (e.Kind.ToString(), e.Arg)
    };
}
