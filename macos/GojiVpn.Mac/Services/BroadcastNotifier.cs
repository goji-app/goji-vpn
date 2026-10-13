using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodjiVpn.Models;

namespace GodjiVpn.Services;

/// <summary>
/// Короткое локальное уведомление о новой новости/рассылке (gojihub.xyz/api/broadcasts) —
/// аналог BroadcastNotifier.kt (Android) и того же приёма, что и SubscriptionNotifier здесь:
/// считается на устройстве по данным, которые и так приходят при обычном обновлении подписки
/// (см. вызов из App.xaml.cs на каждый SubscriptionRepository.Broadcasts). Уведомляет только
/// про самую свежую новость с прошлой проверки — если накопилось несколько, не спамит по
/// одному уведомлению на каждую.
/// </summary>
public sealed class BroadcastNotifier
{
    private const int PreviewLength = 120;

    private static string StatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodjiVpn", "broadcast-notify.json");

    private sealed class State
    {
        public string? LastSeenId { get; set; }
    }

    public event Action<string, string>? NotificationRequested;

    public void Check(IReadOnlyList<BroadcastDto> broadcasts)
    {
        if (broadcasts.Count == 0) return;
        var state = Load();

        // Порядок с бэкенда не гарантирован — сортируем сами, свежая по CreatedAt (ISO-8601,
        // лексикографическая сортировка строк здесь эквивалентна хронологической) первая.
        var newest = broadcasts.MaxBy(b => b.CreatedAt);
        if (newest == null) return;

        // LastSeenId == null — первая проверка на этом устройстве: просто запоминаем текущее
        // состояние, не уведомляем о всей уже накопленной истории новостей.
        if (state.LastSeenId != null && newest.Id != state.LastSeenId)
        {
            var preview = StripMarkup(newest.Content).Trim();
            if (preview.Length > PreviewLength) preview = preview[..PreviewLength];
            NotificationRequested?.Invoke("Новая новость от Godji", preview);
        }
        state.LastSeenId = newest.Id;
        Save(state);
    }

    /// <summary>Грубая зачистка Rich Markdown/Telegram-HTML разметки для превью в уведомлении —
    /// не нужен полноценный парсер (см. Utils/RichContent.cs) ради пары строк текста.</summary>
    private static string StripMarkup(string raw) =>
        Regex.Replace(Regex.Replace(raw, "<[^>]+>", " "), "[*_`~#>|=\\[\\]]", "").Trim();

    private static State Load()
    {
        try
        {
            if (!File.Exists(StatePath)) return new State();
            return JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath)) ?? new State();
        }
        catch { return new State(); }
    }

    private static void Save(State state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state));
        }
        catch { /* лучшее усилие — потеря состояния приведёт максимум к повторному уведомлению */ }
    }
}
