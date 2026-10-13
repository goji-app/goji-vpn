using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodjiVpn.Utils;

namespace GodjiVpn.Services;

/// <summary>Сортировка списка серверов — порт Android ServerSort (SettingsRepository).</summary>
public enum ServerSort { Favorites, Ping, Name }

/// <summary>
/// Мелкие пользовательские настройки приложения, переживающие перезапуск (аналог
/// SettingsRepository в Android): %LOCALAPPDATA%\GodjiVpn\app-settings.json. Отдельные файлы
/// уже существующих настроек (тема, пинг, избранное) не переносятся сюда — чтобы не ломать
/// сохранённый выбор при обновлении.
/// </summary>
public sealed class AppSettings
{
    private static string StatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodjiVpn", "app-settings.json");

    public sealed class State
    {
        public ServerSort ServerSort { get; set; } = ServerSort.Favorites;
        /// <summary>Размер шрифта (Настройки → Внешний вид), как FontSizePreset в Android.</summary>
        public FontSizePreset FontSize { get; set; } = FontSizePreset.Normal;
        /// <summary>"Сайты мимо VPN" — домены в ASCII (punycode), без www., с поддоменами.</summary>
        public List<string> BypassDomains { get; set; } = new();
        /// <summary>Правила Wi-Fi (NetworkRulesManager).</summary>
        public bool WifiAutoConnect { get; set; }
        public bool WifiDisconnectTrusted { get; set; }
        public List<string> TrustedSsids { get; set; } = new();
    }

    private readonly State _state;

    public event Action? Changed;

    public AppSettings() => _state = Load();

    public ServerSort ServerSort
    {
        get => _state.ServerSort;
        set { if (_state.ServerSort == value) return; _state.ServerSort = value; Save(); }
    }

    public FontSizePreset FontSize
    {
        get => _state.FontSize;
        set { if (_state.FontSize == value) return; _state.FontSize = value; Save(); }
    }

    public IReadOnlyList<string> BypassDomains => _state.BypassDomains;

    public bool WifiAutoConnect
    {
        get => _state.WifiAutoConnect;
        set { if (_state.WifiAutoConnect == value) return; _state.WifiAutoConnect = value; Save(); }
    }

    public bool WifiDisconnectTrusted
    {
        get => _state.WifiDisconnectTrusted;
        set { if (_state.WifiDisconnectTrusted == value) return; _state.WifiDisconnectTrusted = value; Save(); }
    }

    public IReadOnlyList<string> TrustedSsids => _state.TrustedSsids;

    public void AddTrustedSsid(string ssid)
    {
        if (string.IsNullOrEmpty(ssid) || _state.TrustedSsids.Contains(ssid)) return;
        _state.TrustedSsids.Add(ssid);
        _state.TrustedSsids.Sort(StringComparer.CurrentCultureIgnoreCase);
        Save();
    }

    public void RemoveTrustedSsid(string ssid)
    {
        if (_state.TrustedSsids.Remove(ssid)) Save();
    }

    public void AddBypassDomain(string domain)
    {
        if (_state.BypassDomains.Contains(domain)) return;
        _state.BypassDomains.Add(domain);
        _state.BypassDomains.Sort(StringComparer.Ordinal);
        Save();
    }

    public void RemoveBypassDomain(string domain)
    {
        if (_state.BypassDomains.Remove(domain)) Save();
    }

    private static readonly IdnMapping Idn = new();
    private static readonly Regex DomainRegex = new(@"^[a-z0-9-]+(\.[a-z0-9-]+)+$", RegexOptions.Compiled);

    /// <summary>Порт SettingsRepository.normalizeDomain (Android): https://www.site.ru/path →
    /// site.ru, кириллица → punycode; null — если это не похоже на адрес сайта.</summary>
    public static string? NormalizeDomain(string input)
    {
        var d = input.Trim().ToLowerInvariant();
        var scheme = d.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) d = d[(scheme + 3)..];
        foreach (var stop in new[] { '/', '?', '#', ':' })
        {
            var i = d.IndexOf(stop);
            if (i >= 0) d = d[..i];
        }
        if (d.StartsWith("*.")) d = d[2..];
        if (d.StartsWith('.')) d = d[1..];
        if (d.StartsWith("www.")) d = d[4..];
        d = d.TrimEnd('.');
        if (d.Length == 0) return null;
        string ascii;
        try { ascii = Idn.GetAscii(d); } catch { return null; }
        return DomainRegex.IsMatch(ascii) ? ascii : null;
    }

    /// <summary>Для показа: xn--… → кириллица.</summary>
    public static string ToDisplay(string domain)
    {
        try { return Idn.GetUnicode(domain); } catch { return domain; }
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

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* лучшее усилие — при сбое записи настройка просто не переживёт перезапуск */ }
        Changed?.Invoke();
    }
}
