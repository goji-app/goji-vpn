using System.Text.Json.Serialization;

namespace GodjiVpn.Models;

// Манифест сайта загрузки (latest.json), его собирает
// «10. Сайт загрузки/tools/publish.py». Основной источник обновлений: сайт открывается в РФ
// без VPN, а GitHub там часто недоступен. Только те поля, что реально используем.

public sealed class SiteManifest
{
    public SitePlatform? Windows { get; set; }
    public SitePlatform? Macos { get; set; }
}

public sealed class SitePlatform
{
    public string Version { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<SiteFile> Files { get; set; } = new();
}

public sealed class SiteFile
{
    /// <summary>apk | exe | dmg | zip-arm64 | zip-x64</summary>
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Относительный путь на зеркале, например files/windows/GodjiVpn-Setup-1.0.74.exe</summary>
    public string Path { get; set; } = "";
    public long Size { get; set; }
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
}
