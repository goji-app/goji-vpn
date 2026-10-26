using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Text;

namespace GodjiVpn.Services;

/// <summary>
/// Сбои, которые приложение само поймать не может (исключение в фоновом потоке, переполнение
/// стека, падение в машинном коде), Windows записывает в журнал «Приложение». Пользователь
/// видит только, что окно пропало (жалоба 2026-10-26: «вошёл — закрылась, иконка в трее и
/// исчезла»), и искать журнал не станет. Поэтому при запуске берём записи о падении
/// GodjiVpn.exe и новые строки crash.log с прошлого запуска — App предлагает отправить их в
/// поддержку одним нажатием.
/// </summary>
public static class CrashReporter
{
    private const int MaxEventsPerReport = 3;
    private const int MaxReportChars = 4000;

    private static string StateDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodjiVpn");
    private static string StatePath => Path.Combine(StateDir, "last-start.txt");
    private static string CrashLogPath => Path.Combine(StateDir, "logs", "crash.log");

    /// <summary>Отчёт о сбоях с прошлого запуска (null — сбоев не было) и отметка нового запуска.
    /// Первый запуск после установки ничего не сообщает: не с чем сравнивать.</summary>
    public static string? TakePendingReport()
    {
        var (since, crashLogLength) = ReadState();
        WriteState();
        if (since == null) return null;

        var parts = ReadEvents(since.Value).ToList();
        var newCrashLog = ReadCrashLogFrom(crashLogLength);
        if (newCrashLog != null) parts.Add("crash.log:\n" + newCrashLog);
        if (parts.Count == 0) return null;

        var report = string.Join("\n\n", parts);
        return report.Length > MaxReportChars ? report[..MaxReportChars] : report;
    }

    private static IEnumerable<string> ReadEvents(DateTime sinceUtc)
    {
        var query = "*[System[(Provider[@Name='.NET Runtime'] or Provider[@Name='Application Error']) and " +
                    $"TimeCreated[@SystemTime>='{sinceUtc:yyyy-MM-ddTHH:mm:ss.fffZ}']]]";
        var exeName = Path.GetFileName(Environment.ProcessPath) ?? "GodjiVpn.exe";
        var found = new List<string>();
        try
        {
            using var reader = new EventLogReader(new EventLogQuery("Application", PathType.LogName, query));
            for (var record = reader.ReadEvent(); record != null && found.Count < MaxEventsPerReport; record = reader.ReadEvent())
            {
                using (record)
                {
                    var text = record.FormatDescription();
                    if (text == null || !text.Contains(exeName, StringComparison.OrdinalIgnoreCase)) continue;
                    found.Add($"{record.TimeCreated:yyyy-MM-dd HH:mm:ss} {record.ProviderName}:\n{text}");
                }
            }
        }
        catch { /* журнал недоступен — отчёт просто без него */ }
        return found;
    }

    private static string? ReadCrashLogFrom(long offset)
    {
        try
        {
            using var stream = new FileStream(CrashLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length <= offset) return null;
            stream.Seek(offset, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = reader.ReadToEnd().Trim();
            return text.Length > 0 ? text : null;
        }
        catch { return null; }
    }

    private static (DateTime? Since, long CrashLogLength) ReadState()
    {
        try
        {
            var parts = File.ReadAllText(StatePath).Split('|');
            var since = DateTime.Parse(parts[0], null, System.Globalization.DateTimeStyles.RoundtripKind);
            return (since, parts.Length > 1 && long.TryParse(parts[1], out var length) ? length : 0);
        }
        catch { return (null, 0); }
    }

    private static void WriteState()
    {
        try
        {
            Directory.CreateDirectory(StateDir);
            var length = File.Exists(CrashLogPath) ? new FileInfo(CrashLogPath).Length : 0;
            File.WriteAllText(StatePath, $"{DateTime.UtcNow:O}|{length}");
        }
        catch { /* без отметки следующий запуск просто ничего не сообщит */ }
    }
}
