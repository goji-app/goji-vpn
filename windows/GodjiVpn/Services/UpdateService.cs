using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Windows;
using GodjiVpn.Models;

namespace GodjiVpn.Services;

/// <summary>Одна доступная версия, готовая к скачиванию. Changelog — сырой Markdown из тела
/// релиза (release.Body), рендерится через Utils/RichContent.cs на стороне ViewModel — тот же
/// рендерер, что и у новостей, GitHub-флейвор Markdown он покрывает разумно (заголовки, списки,
/// жирный/курсив, ссылки — то немногое, чем обычно пишут release notes).</summary>
public sealed record UpdateInfo(Version Version, string VersionLabel, string Changelog, string DownloadUrl, string FileName, long? SizeBytes);

/// <summary>
/// Проверка новых версий: сначала манифест сайта загрузки (get.gojihub.xyz/latest.json, открывается
/// в РФ без VPN), при его недоступности — GitHub Releases API (публичный, авторизация не нужна) и скачивание/запуск установщика прямо из приложения.
/// Устанавливать "тихо" в фоне не пытаемся — просто передаём эстафету обычному Inno Setup
/// установщику (тому же .exe, что и ручная установка/обновление), который сам закрывает нас
/// через AppMutex/CloseApplications=yes (см. installer/GodjiVpn.iss) и опционально перезапускает
/// после установки. Это надёжнее самодельного patch-in-place, особенно когда TUN-туннель может
/// быть поднят в момент обновления.
/// </summary>
public sealed class UpdateService
{
    private const string ManifestUrl = "https://get.gojihub.xyz/latest.json";
    private const string ReleasesApiUrl = "https://api.github.com/repos/goji-app/goji-vpn-desktop/releases/latest";

    private readonly HttpClient _http;

    public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public UpdateService()
    {
        var handler = new SocketsHttpHandler { Proxy = new TunnelAwareProxy(), UseProxy = true };
        _http = new HttpClient(handler);
        var v = CurrentVersion;
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GodjiVPN-Windows", $"{v.Major}.{v.Minor}.{v.Build}"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    /// <returns>null — обновлений нет, либо проверка не удалась (сайт и GitHub недоступны,
    /// версия не распарсилась и т.п. — не критично, просто тихо пропускаем, как и у
    /// broadcasts/referrals).</returns>
    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        var latest = await FetchFromSiteAsync(ct).ConfigureAwait(false)
                     ?? await FetchFromGitHubAsync(ct).ConfigureAwait(false);
        return latest != null && latest.Version.CompareTo(CurrentVersion) > 0 ? latest : null;
    }

    private async Task<UpdateInfo?> FetchFromSiteAsync(CancellationToken ct)
    {
        try
        {
            var manifest = await _http.GetFromJsonAsync<SiteManifest>(ManifestUrl, ct).ConfigureAwait(false);
            var platform = manifest?.Windows;
            if (platform == null || !Version.TryParse(platform.Version, out var version)) return null;

            var file = platform.Files.FirstOrDefault(f => f.Kind == "exe");
            if (file == null || !file.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;

            return new UpdateInfo(version, platform.Version, platform.Notes, file.Url, file.Name, file.Size);
        }
        catch { return null; }
    }

    private async Task<UpdateInfo?> FetchFromGitHubAsync(CancellationToken ct)
    {
        try
        {
            var release = await _http.GetFromJsonAsync<GitHubRelease>(ReleasesApiUrl, ct).ConfigureAwait(false);
            if (release == null) return null;

            var tag = release.TagName.TrimStart('v', 'V');
            if (!Version.TryParse(tag, out var version)) return null;

            var asset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            if (asset == null) return null;

            return new UpdateInfo(version, tag, release.Body, asset.BrowserDownloadUrl, asset.Name, asset.Size);
        }
        catch { return null; }
    }

    /// <returns>Путь к скачанному установщику во временной папке.</returns>
    public async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct = default)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), update.FileName);
        using var response = await _http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? update.SizeBytes ?? -1L;

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var file = File.Create(tempPath);
        var buffer = new byte[81920];
        long downloaded = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            downloaded += read;
            if (total > 0) progress?.Report((double)downloaded / total);
        }
        return tempPath;
    }

    /// <summary>Запускает скачанный установщик и сразу завершает приложение — установщик сам
    /// закрыл бы ещё оставшийся процесс через AppMutex/CloseApplications=yes, но самостоятельное
    /// завершение чище и без гонки за файлы.</summary>
    public static void RunInstallerAndExit(string installerPath)
    {
        Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
        Application.Current.Shutdown();
    }
}
