using System.Text.Json;

namespace GodjiVpn.Services;

/// <summary>Сессия (JWT + refresh-токен) — см. SecretFile. API тот же, что в Windows-клиенте.</summary>
public sealed class TokenStore
{
    private static readonly string StorePath = SecretFile.PathFor("session.json");

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
        Persist();
    }

    public void SaveRefreshToken(string refreshToken)
    {
        EnsureLoaded();
        _cachedRefreshToken = refreshToken;
        Persist();
    }

/// <summary>Сессия очищена (выход из аккаунта или истёкшая сессия) — SubscriptionRepository
    /// забывает данные прежнего аккаунта, см. App.</summary>
    public event Action? Cleared;

        public void Clear()
    {
        _cachedToken = null;
        _cachedRefreshToken = null;
        _loaded = true;
        try { if (File.Exists(StorePath)) File.Delete(StorePath); } catch { }
        Cleared?.Invoke();
    }

    private void Persist() =>
        SecretFile.Write(StorePath, JsonSerializer.Serialize(new Stored { AccessToken = _cachedToken, RefreshToken = _cachedRefreshToken }));

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (SecretFile.Read(StorePath) is not { } text) return;
            var stored = JsonSerializer.Deserialize<Stored>(text);
            _cachedToken = stored?.AccessToken;
            _cachedRefreshToken = stored?.RefreshToken;
        }
        catch { /* повреждённый файл — просто попросим войти заново */ }
    }

    private sealed class Stored
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
    }
}
