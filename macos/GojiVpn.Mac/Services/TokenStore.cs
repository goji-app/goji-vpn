using System.Text.Json;

namespace GodjiVpn.Services;

/// <summary>Сессия (JWT + refresh-токен) — в Keychain (см. Keychain): файл 600 защищал только
/// от других локальных пользователей, любой процесс того же пользователя читал refresh-токен.
/// Старый файл session.json при первом чтении мигрирует в Keychain и удаляется.
/// API тот же, что в Windows-клиенте.</summary>
public sealed class TokenStore
{
    private const string AccountToken = "access";
    private const string AccountRefresh = "refresh";
    /// <summary>Легаси-хранилище (до Keychain) — только для одноразовой миграции.</summary>
    private static readonly string LegacyPath = SecretFile.PathFor("session.json");

    private string? _cachedToken;
    private string? _cachedRefreshToken;
    private bool _loaded;

    public string? AccessToken { get { EnsureLoaded(); return _cachedToken; } }
    public string? RefreshToken { get { EnsureLoaded(); return _cachedRefreshToken; } }
    public bool IsLoggedIn => !string.IsNullOrEmpty(AccessToken);

    public void Save(string accessToken)
    {
        EnsureLoaded();
        _cachedToken = accessToken;
        Keychain.Write(AccountToken, accessToken);
    }

    public void SaveRefreshToken(string refreshToken)
    {
        EnsureLoaded();
        _cachedRefreshToken = refreshToken;
        Keychain.Write(AccountRefresh, refreshToken);
    }

/// <summary>Сессия очищена (выход из аккаунта или истёкшая сессия) — SubscriptionRepository
    /// забывает данные прежнего аккаунта, см. App.</summary>
    public event Action? Cleared;

        public void Clear()
    {
        _cachedToken = null;
        _cachedRefreshToken = null;
        _loaded = true;
        Keychain.Delete(AccountToken);
        Keychain.Delete(AccountRefresh);
        try { if (File.Exists(LegacyPath)) File.Delete(LegacyPath); } catch { }
        Cleared?.Invoke();
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        _cachedToken = Keychain.Read(AccountToken);
        _cachedRefreshToken = Keychain.Read(AccountRefresh);
        // Миграция с прежнего файла: прочитали — переложили в Keychain, файл удалили.
        if (_cachedToken == null && SecretFile.Read(LegacyPath) is { } legacy)
        {
            try
            {
                var stored = JsonSerializer.Deserialize<Stored>(legacy);
                if (!string.IsNullOrEmpty(stored?.AccessToken))
                {
                    _cachedToken = stored.AccessToken;
                    _cachedRefreshToken = stored.RefreshToken;
                    Keychain.Write(AccountToken, stored.AccessToken!);
                    if (!string.IsNullOrEmpty(stored.RefreshToken))
                        Keychain.Write(AccountRefresh, stored.RefreshToken!);
                }
                try { File.Delete(LegacyPath); } catch { }
            }
            catch { try { File.Delete(LegacyPath); } catch { } }
        }
    }

    private sealed class Stored
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
    }
}
