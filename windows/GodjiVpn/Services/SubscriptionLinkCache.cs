using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GodjiVpn.Services;

/// <summary>Последняя успешно полученная ссылка подписки (subs.gojihub.xyz) — используется
/// SubscriptionRepository.RefreshAsync как запасной путь, когда наш собственный шоп-бэкенд
/// (gojihub.xyz, откуда обычно приходит эта ссылка через GetSubscriptionsAsync) недоступен, но
/// сам Remnawave (subs.gojihub.xyz) технически независим и может быть доступен всё это время —
/// тот же путь, что использует любой сторонний v2ray-клиент, которому эту ссылку один раз
/// вставили вручную и который вообще не знает о существовании gojihub.xyz. Порт из Android
/// NodeListCache.subscriptionLink (там — EncryptedFile; здесь, как и TokenStore, DPAPI — ссылка
/// несёт в себе секрет, по которому получается конфиг узлов, хранить её открытым текстом не
/// стоит).</summary>
public static class SubscriptionLinkCache
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GodjiVpn", "subscription-link.dat");

    public static void Save(string link)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            var plainBytes = Encoding.UTF8.GetBytes(link);
            var protectedBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(StorePath, protectedBytes);
        }
        catch { /* лучшее усилие — при следующем сбое gojihub.xyz просто не будет запасного пути */ }
    }

    public static string? Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return null;
            var protectedBytes = File.ReadAllBytes(StorePath);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            var text = Encoding.UTF8.GetString(plainBytes).Trim();
            return text.Length > 0 ? text : null;
        }
        catch { return null; }
    }
}
