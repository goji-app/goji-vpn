using System.IO;
using System.Text.Json;

namespace GodjiVpn.Services;

/// <summary>Избранные серверы (закреплены сверху списка на вкладке "Серверы") — просто набор id
/// узлов, сами узлы приходят из подписки и не хранятся здесь. Порт из Android
/// (SettingsRepository.favoriteServerIds).</summary>
public sealed class FavoriteServersStore
{
    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodjiVpn", "favorite-servers.json");

    private readonly HashSet<string> _ids;

    public event Action? Changed;

    public FavoriteServersStore() => _ids = Load();

    public bool IsFavorite(string id) => _ids.Contains(id);

    /// <summary>До перехода на стабильные ID узла (см. SubscriptionService) избранное хранило
    /// позицию узла в массиве подписки ("0", "1"…). Один раз переводим такие записи в новые ID по
    /// текущему порядку подписки — это тот же порядок, в котором они и были отмечены.</summary>
    public void MigrateLegacyIds(IReadOnlyList<Models.VlessNode> subscriptionNodes)
    {
        var legacy = _ids.Where(id => id.Length > 0 && id.All(char.IsDigit)).ToList();
        if (legacy.Count == 0 || subscriptionNodes.Count == 0) return;
        foreach (var old in legacy)
        {
            _ids.Remove(old);
            if (int.TryParse(old, out var index) && index >= 0 && index < subscriptionNodes.Count)
                _ids.Add(subscriptionNodes[index].Id);
        }
        Save();
    }

    public void Toggle(string id)
    {
        if (!_ids.Remove(id)) _ids.Add(id);
        Save();
        Changed?.Invoke();
    }

    private static HashSet<string> Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return new HashSet<string>();
            return JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(StorePath)) ?? new HashSet<string>();
        }
        catch { return new HashSet<string>(); }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(_ids));
        }
        catch { /* лучшее усилие — потеря избранного не критична */ }
    }
}
