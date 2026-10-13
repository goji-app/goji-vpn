using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GodjiVpn.Services;

/// <summary>
/// Хранит JWT (и, с 1.0.16, refresh-токен) в %LOCALAPPDATA%\GodjiVpn\secure.dat, зашифрованным
/// через DPAPI (CurrentUser scope) — аналог EncryptedSharedPreferences в Android-версии: файл
/// бесполезен вне профиля текущего пользователя Windows на этой машине.
/// </summary>
public sealed class TokenStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GodjiVpn", "secure.dat");

    private string? _cachedToken;
    private string? _cachedRefreshToken;
    private bool _loaded;

    public string? AccessToken
    {
        get { EnsureLoaded(); return _cachedToken; }
    }

    /// <summary>rw_refresh_token — живёт намного дольше сессионного JWT (тот истекает ровно
    /// через 24ч, см. ApiClient.RefreshSessionAsync). Может быть null для сессий, начатых до
    /// этого обновления (старый сохранённый accessToken без refresh-токена, см. Load) — тогда
    /// обновление сессии просто не сработает и пользователь один раз перелогинится, дальше уже
    /// с refresh-токеном.</summary>
    public string? RefreshToken
    {
        get { EnsureLoaded(); return _cachedRefreshToken; }
    }

    public void Save(string accessToken)
    {
        EnsureLoaded();
        _cachedToken = accessToken;
        Persist();
    }

    public void SaveRefreshToken(string refreshToken)
    {
        EnsureLoaded();
        _cachedRefreshToken = refreshToken;
        Persist();
    }

    public bool IsLoggedIn => !string.IsNullOrEmpty(AccessToken);

/// <summary>Сессия очищена (выход из аккаунта или истёкшая сессия) — SubscriptionRepository
    /// забывает данные прежнего аккаунта, см. App.</summary>
    public event Action? Cleared;

        public void Clear()
    {
        _cachedToken = null;
        _cachedRefreshToken = null;
        _loaded = true;
        if (File.Exists(StorePath)) File.Delete(StorePath);
        Cleared?.Invoke();
    }

    private void Persist()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        var json = JsonSerializer.Serialize(new StoredTokens { AccessToken = _cachedToken, RefreshToken = _cachedRefreshToken });
        var plainBytes = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(StorePath, protectedBytes);
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        Load();
    }

    private void Load()
    {
        if (!File.Exists(StorePath)) return;
        try
        {
            var protectedBytes = File.ReadAllBytes(StorePath);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            var text = Encoding.UTF8.GetString(plainBytes);
            // Новый формат (с 1.0.16) — JSON {"AccessToken":...,"RefreshToken":...}. Старый —
            // просто сырой JWT-строкой (версии до добавления refresh-токена сохраняли только
            // его) — тогда используем как accessToken без refresh-токена, ничего не падает.
            if (text.StartsWith('{'))
            {
                var stored = JsonSerializer.Deserialize<StoredTokens>(text);
                _cachedToken = stored?.AccessToken;
                _cachedRefreshToken = stored?.RefreshToken;
            }
            else
            {
                _cachedToken = text;
            }
        }
        catch (Exception)
        {
            // CryptographicException — файл зашифрован под другим профилем/машиной
            // (переустановка Windows и т.п.). Но File.ReadAllBytes/Unprotect может упасть и
            // по другим причинам (IOException — файл временно залочен антивирусом/бэкап-
            // агентом, UnauthorizedAccessException — права поменялись) — это вызывается из
            // App.OnStartup ДО показа окна, необработанное исключение здесь означало бы, что
            // приложение вообще не запускается, пока кто-то вручную не удалит secure.dat.
            // Во всех этих случаях правильнее считать, что токена нет (попросим войти заново),
            // чем не дать приложению открыться вовсе.
        }
    }

    private sealed class StoredTokens
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
    }
}
