using System.IO;
using System.Text.Json;

namespace GodjiVpn.Services;

/// <summary>Аналог PingMethod.kt/SettingsRepository (Android, часть) — способ проверки
/// серверов: через прокси GET/HEAD (реальный туннель, видит задержку самого VLESS+Reality,
/// не только сеть) или напрямую TCP/ICMP до хоста (быстрее, не требует поднимать xray.exe, но
/// не отражает работоспособность самого прокси-протокола).</summary>
public enum PingMethod { ProxyGet, ProxyHead, Tcp, Icmp }

public sealed class PingSettings
{
    private const string DefaultTestUrl = "https://www.gstatic.com/generate_204";

    private static string StatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodjiVpn", "ping-settings.json");

    private sealed class State
    {
        public PingMethod Method { get; set; } = PingMethod.ProxyGet;
        public string TestUrl { get; set; } = DefaultTestUrl;
    }

    private State _state;

    public PingSettings() => _state = Load();

    public PingMethod Method
    {
        get => _state.Method;
        set { _state.Method = value; Save(); }
    }

    public string TestUrl
    {
        get => _state.TestUrl;
        set { _state.TestUrl = string.IsNullOrWhiteSpace(value) ? DefaultTestUrl : value; Save(); }
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
            File.WriteAllText(StatePath, JsonSerializer.Serialize(_state));
        }
        catch { /* лучшее усилие — потеря состояния приведёт максимум к сбросу на дефолт */ }
    }
}
