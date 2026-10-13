using System.IO;
using System.Text.Json;

namespace GodjiVpn.Services;

/// <summary>
/// Короткое локальное уведомление о доступном обновлении — тот же приём, что и
/// BroadcastNotifier/SubscriptionNotifier: уведомляет один раз на конкретную версию, не
/// спамит при каждой периодической проверке (см. App.xaml.cs), пока не выйдет версия ещё новее.
/// </summary>
public sealed class UpdateNotifier
{
    private static string StatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodjiVpn", "update-notify.json");

    private sealed class State
    {
        public string? LastNotifiedVersion { get; set; }
    }

    public event Action<string, string>? NotificationRequested;

    public void Check(UpdateInfo update)
    {
        var state = Load();
        if (state.LastNotifiedVersion == update.VersionLabel) return;
        NotificationRequested?.Invoke("Доступно обновление Godji VPN",
            $"Версия {update.VersionLabel} — откройте «Настройки», чтобы посмотреть изменения и обновиться.");
        state.LastNotifiedVersion = update.VersionLabel;
        Save(state);
    }

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
