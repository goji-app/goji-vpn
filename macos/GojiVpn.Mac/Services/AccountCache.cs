using System.Text.Json;
using System.Text.Json.Nodes;

namespace GodjiVpn.Services;

/// <summary>
/// Последние успешно полученные данные аккаунта — подписка, новости, тарифы, рефералы,
/// партнёрка, устройства — на диске. Порт Android AccountCache (b3dff12): «Главная» и «Подписка»
/// показывают их сразу после запуска, не дожидаясь сети, а обновление идёт в фоне и при сбое (нет
/// интернета, VPN переподключается, кабинет не ответил) просто оставляет прежнее, а не «0 дней» и
/// «—». Каждый раздел — отдельный файл с моментом сохранения, чтобы решать, пора ли его обновлять.
/// Хранится как токены и ссылка подписки (SecretFile, права 600): в подписке лежит ссылка
/// подписки, то есть рабочие учётные данные.
/// </summary>
public static class AccountCache
{
    public enum Key { Subscription, Broadcasts, Plans, Referrals, Partner, Devices }

    public sealed record Entry<T>(T Value, DateTime SavedAtUtc);

    private static readonly JsonSerializerOptions JsonOptions = Utils.LenientJson.Options();

    // Превью-экземпляр работает с той же папкой данных, что и настоящий клиент, — он не должен ни
    // читать чужие данные аккаунта в скриншоты, ни перезаписывать их.
    private static readonly bool Disabled = Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW") == "1";

    private static string PathFor(Key key) => SecretFile.PathFor($"account-{key.ToString().ToLowerInvariant()}.json");

    public static void Save<T>(Key key, T value)
    {
        if (Disabled) return;
        try
        {
            var root = new JsonObject
            {
                ["at"] = DateTime.UtcNow.ToString("O"),
                ["data"] = JsonSerializer.SerializeToNode(value, JsonOptions)
            };
            WriteSecret(PathFor(key), root.ToJsonString());
        }
        catch { /* лучшее усилие — при следующем запуске раздел просто подождёт сеть */ }
    }

    public static Entry<T>? Load<T>(Key key)
    {
        if (Disabled) return null;
        try
        {
            if (ReadSecret(PathFor(key)) is not { } text) return null;
            var root = JsonNode.Parse(text)!.AsObject();
            var value = root["data"].Deserialize<T>(JsonOptions);
            if (value == null) return null;
            var at = DateTime.TryParse(root["at"]?.GetValue<string>(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)
                ? t.ToUniversalTime() : DateTime.MinValue;
            return new Entry<T>(value, at);
        }
        catch { return null; }
    }

    public static void Remove(Key key)
    {
        if (Disabled) return;
        try { File.Delete(PathFor(key)); } catch { }
    }

    /// <summary>При выходе из аккаунта: данные прежнего аккаунта не должны показываться тому, кто
    /// войдёт следующим.</summary>
    public static void Clear()
    {
        if (Disabled) return;
        foreach (var key in Enum.GetValues<Key>()) Remove(key);
    }

    private static void WriteSecret(string path, string text) => SecretFile.Write(path, text);

    private static string? ReadSecret(string path) => SecretFile.Read(path);
}
