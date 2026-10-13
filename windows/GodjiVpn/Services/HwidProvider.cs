using System.IO;

namespace GodjiVpn.Services;

/// <summary>
/// X-HWID для subs.gojihub.xyz. Не читаем реестровый MachineGuid — генерируем свой GUID
/// при первом запуске и храним рядом с токеном: не требует дополнительных прав и не зависит
/// от политик, которые могут закрыть чтение реестра в корпоративной среде.
/// </summary>
public sealed class HwidProvider
{
    private static readonly string HwidPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GodjiVpn", "hwid.txt");

    private string? _cached;

    public string Get()
    {
        if (_cached != null) return _cached;

        if (File.Exists(HwidPath))
        {
            var existing = File.ReadAllText(HwidPath).Trim();
            if (!string.IsNullOrEmpty(existing))
            {
                _cached = existing;
                return existing;
            }
        }

        var fresh = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.GetDirectoryName(HwidPath)!);
        File.WriteAllText(HwidPath, fresh);
        _cached = fresh;
        return fresh;
    }
}
