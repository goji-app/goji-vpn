using System.Text;

namespace GodjiVpn.Services;

/// <summary>
/// Порт Windows CrashReporter (2026-10-26): пользователи логи не собирают, поэтому при запуске
/// берём всё, что осталось от сбоев с прошлого запуска, — новые строки crash.log (необработанные
/// исключения .NET, см. Program.LogCrash) и системные отчёты macOS о падении процесса
/// (~/Library/Logs/DiagnosticReports/GojiVpn*.ips: переполнение стека, падение в машинном коде) —
/// App предлагает отправить их в поддержку одним нажатием.
/// </summary>
public static class CrashReporter
{
    private const int MaxSystemReports = 2;
    private const int MaxSystemReportChars = 1800;
    private const int MaxReportChars = 4000;

    private static string StateDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodjiVpn");
    private static string StatePath => Path.Combine(StateDir, "last-start.txt");
    private static string CrashLogPath => Path.Combine(StateDir, "logs", "crash.log");
    private static string DiagnosticReportsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "DiagnosticReports");

    /// <summary>Отчёт о сбоях с прошлого запуска (null — сбоев не было) и отметка нового запуска.
    /// Первый запуск после установки ничего не сообщает: не с чем сравнивать.</summary>
    public static string? TakePendingReport()
    {
        var (since, crashLogLength) = ReadState();
        WriteState();
        if (since == null) return null;

        var parts = ReadSystemReports(since.Value).ToList();
        var newCrashLog = ReadCrashLogFrom(crashLogLength);
        if (newCrashLog != null) parts.Add("crash.log:\n" + newCrashLog);
        if (parts.Count == 0) return null;

        var report = string.Join("\n\n", parts);
        return report.Length > MaxReportChars ? report[..MaxReportChars] : report;
    }

    private static IEnumerable<string> ReadSystemReports(DateTime sinceUtc)
    {
        try
        {
            var processName = Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "GojiVpn";
            return new DirectoryInfo(DiagnosticReportsDir)
                .EnumerateFiles(processName + "*.ips")
                .Where(f => f.LastWriteTimeUtc > sinceUtc)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(MaxSystemReports)
                .Select(f =>
                {
                    var text = File.ReadAllText(f.FullName);
                    return $"{f.Name}:\n{(text.Length > MaxSystemReportChars ? text[..MaxSystemReportChars] : text)}";
                })
                .ToList();
        }
        catch { return Array.Empty<string>(); }
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
