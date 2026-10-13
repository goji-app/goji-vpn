namespace GodjiVpn.Services;

/// <summary>Запасная копия ссылки подписки (см. SecretFile) — на случай недоступности gojihub.xyz.</summary>
public static class SubscriptionLinkCache
{
    private static readonly string StorePath = SecretFile.PathFor("subscription-link.txt");

    public static void Save(string link)
    {
        try { SecretFile.Write(StorePath, link); } catch { }
    }

    public static string? Load()
    {
        var text = SecretFile.Read(StorePath)?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
