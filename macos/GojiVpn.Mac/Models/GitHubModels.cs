using System.Text.Json.Serialization;

namespace GodjiVpn.Models;

// GitHub Releases API (api.github.com/repos/{owner}/{repo}/releases/latest) — публичный
// эндпоинт, авторизация не нужна. Только те поля, что реально используем.

public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
    public string? Name { get; set; }
    public string Body { get; set; } = "";
    [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
    public List<GitHubAsset> Assets { get; set; } = new();
}

public sealed class GitHubAsset
{
    public string Name { get; set; } = "";
    [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
    public long Size { get; set; }
}
