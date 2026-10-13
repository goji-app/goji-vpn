using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GodjiVpn.Services;

namespace GodjiVpn.ViewModels;

public sealed class JournalDayOption
{
    public required string Label { get; init; }
}

/// <summary>Столбик часа: высота и уровень цвета (up — под защитой, issue — были сбои, empty —
/// VPN не работал, future — час ещё не наступил).</summary>
public sealed class JournalHourBar
{
    public required double Height { get; init; }
    public required string Level { get; init; }
}

public sealed class JournalEventItem
{
    public required string Time { get; init; }
    public required string Title { get; init; }
    public required string Text { get; init; }
    /// <summary>ok / issue / switch / neutral — цвет точки.</summary>
    public required string Level { get; init; }
    public bool HasText => Text.Length > 0;
}

/// <summary>
/// «Журнал сети» — порт Android JournalScreen.kt (84b0fb9): доля времени под защитой по часам за
/// сутки (тёплый столбик — в этот час были сбои: обрыв, пропала сеть, ошибка), счётчики и лента
/// событий. Данные — NetworkJournal, только локальные.
/// </summary>
public sealed partial class JournalViewModel : ObservableObject
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private readonly Func<bool> _isRunning;
    private readonly Action<Action> _runOnUi;

    public IReadOnlyList<JournalDayOption> Days { get; } = new[]
    {
        new JournalDayOption { Label = "Сегодня" },
        new JournalDayOption { Label = "Вчера" }
    };

    [ObservableProperty] private int dayIndex;
    [ObservableProperty] private string percentLabel = "0%";
    [ObservableProperty] private string protectedLabel = "";
    [ObservableProperty] private int connects;
    [ObservableProperty] private int networkChanges;
    [ObservableProperty] private int issues;
    [ObservableProperty] private bool hasEvents;

    public ObservableCollection<JournalHourBar> Hours { get; } = new();
    public ObservableCollection<JournalEventItem> Events { get; } = new();

    public JournalViewModel(Func<bool> isRunning, Action<Action> runOnUi)
    {
        _isRunning = isRunning;
        _runOnUi = runOnUi;
        NetworkJournal.Changed += () => _runOnUi(Refresh);
        Refresh();
    }

    partial void OnDayIndexChanged(int value) => Refresh();

    public void Refresh()
    {
        var day = NetworkJournal.DayOf(NetworkJournal.Events, DayIndex, _isRunning());
        var pct = day.Elapsed.TotalMilliseconds > 0 ? Math.Floor(day.Protected.TotalMilliseconds * 1000 / day.Elapsed.TotalMilliseconds) / 10.0 : 0;
        PercentLabel = (pct % 1 == 0 ? pct.ToString("0", Ru) : pct.ToString("0.0", Ru)) + "%";
        ProtectedLabel = $"{Duration(day.Protected)} из {Duration(day.Elapsed)}";
        Connects = day.Connects;
        NetworkChanges = day.Events.Count(e => e.Kind is NetworkJournal.Kind.NET_WIFI or NetworkJournal.Kind.NET_WIRED or NetworkJournal.Kind.NET_LOST);
        Issues = day.Issues;

        Hours.Clear();
        foreach (var h in day.Hours)
        {
            var up = h.Uptime;
            Hours.Add(new JournalHourBar
            {
                Height = up is null or <= 0 ? 4 : 6 + 50 * up.Value,
                Level = up == null ? "future" : h.Issue ? "issue" : up <= 0 ? "empty" : "up"
            });
        }

        Events.Clear();
        foreach (var e in day.Events)
        {
            var (title, text) = NetworkJournal.Describe(e);
            Events.Add(new JournalEventItem
            {
                Time = e.At.ToLocalTime().ToString("HH:mm", Ru),
                Title = title,
                Text = text,
                Level = NetworkJournal.IsIssue(e.Kind) ? "issue"
                    : NetworkJournal.IsSwitch(e.Kind) ? "switch"
                    : e.Kind is NetworkJournal.Kind.CONNECTED or NetworkJournal.Kind.LEAK_OK ? "ok"
                    : "neutral"
            });
        }
        HasEvents = Events.Count > 0;
    }

    private static string Duration(TimeSpan t)
    {
        var minutes = (long)t.TotalMinutes;
        return minutes >= 60 ? $"{minutes / 60} ч {minutes % 60} мин" : $"{minutes} мин";
    }
}
