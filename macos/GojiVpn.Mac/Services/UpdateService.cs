using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
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
    /// <summary>Зеркала сайта загрузки (исходники — «10. Сайт загрузки»): на каждом одни и те же
    /// latest.json и сборки. Пробуем по порядку, GitHub — запасной путь. Новое зеркало = новая строка.</summary>
    private static readonly string[] MirrorSites = { "https://get.gojihub.xyz" };
    private const string ReleasesApiUrl = "https://api.github.com/repos/goji-app/goji-vpn-desktop/releases/latest";

    private readonly HttpClient _http;

    public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public UpdateService()
    {
        var handler = new SocketsHttpHandler { Proxy = new TunnelAwareProxy(), UseProxy = true };
        _http = new HttpClient(handler);
        var v = CurrentVersion;
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GodjiVPN-macOS", $"{v.Major}.{v.Minor}.{v.Build}"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    /// <returns>null — обновлений нет, либо проверка не удалась (сайт и GitHub недоступны,
    /// версия не распарсилась и т.п. — не критично, просто тихо пропускаем, как и у
    /// broadcasts/referrals).</returns>
    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        UpdateInfo? latest = null;
        foreach (var site in MirrorSites)
        {
            latest = await FetchFromSiteAsync(site, ct).ConfigureAwait(false);
            if (latest != null) break;
        }
        latest ??= await FetchFromGitHubAsync(ct).ConfigureAwait(false);
        return latest != null && latest.Version.CompareTo(CurrentVersion) > 0 ? latest : null;
    }

    /// <summary>Архитектура этого Mac в именах сборок: …-macOS-arm64.zip (Apple Silicon) или …-macOS-x64.zip (Intel).</summary>
    private static string MacArch => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";

    /// <summary>Пути файлов в манифесте относительные — сборку качаем с того же зеркала, что ответило.</summary>
    private async Task<UpdateInfo?> FetchFromSiteAsync(string site, CancellationToken ct)
    {
        try
        {
            var manifest = await _http.GetFromJsonAsync<SiteManifest>($"{site}/latest.json", ct).ConfigureAwait(false);
            var platform = manifest?.Macos;
            if (platform == null || !Version.TryParse(platform.Version, out var version)) return null;

            var file = platform.Files.FirstOrDefault(f => f.Kind == $"zip-{MacArch}");
            if (file == null || string.IsNullOrWhiteSpace(file.Path) || file.Path.Contains("..")) return null;

            return new UpdateInfo(version, platform.Version, platform.Notes, $"{site}/{file.Path.TrimStart('/')}", file.Name, file.Size);
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

            var asset = release.Assets.FirstOrDefault(a => a.Name.EndsWith($"-macOS-{MacArch}.zip", StringComparison.OrdinalIgnoreCase));
            if (asset == null) return null;

            return new UpdateInfo(version, tag, release.Body, asset.BrowserDownloadUrl, asset.Name, asset.Size);
        }
        catch { return null; }
    }

    /// <returns>Путь к скачанному установщику во временной папке.</returns>
    public async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct = default)
    {
        // В "Загрузки": пользователь сам заменяет приложение в "Программах" (без подписи Apple
        // автоматическая замена бандла на месте ненадёжна).
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(downloads);
        var tempPath = Path.Combine(downloads, update.FileName);
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
    /// <summary>Показывает скачанный архив в Finder — дальше пользователь распаковывает его и
    /// переносит приложение в "Программы" поверх старого.</summary>
    public static void RevealInFinder(string path)
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
            psi.ArgumentList.Add("-R");
            psi.ArgumentList.Add(path);
            Process.Start(psi);
        }
        catch { /* не критично */ }
    }
}
