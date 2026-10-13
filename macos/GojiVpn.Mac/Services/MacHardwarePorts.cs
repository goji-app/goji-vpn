using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GodjiVpn.Services;

/// <summary>Какие сетевые устройства — Wi-Fi ("Hardware Port: Wi-Fi / Device: en0" из
/// networksetup -listallhardwareports). Читается один раз за запуск.</summary>
public static class MacHardwarePorts
{
    private static readonly Lazy<HashSet<string>> Wifi = new(Load);

    public static HashSet<string> WifiDevices => Wifi.Value;

    private static HashSet<string> Load()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return result;
        try
        {
            var psi = new ProcessStartInfo("/usr/sbin/networksetup", "-listallhardwareports")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return result;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            string? port = null;
            foreach (var raw in output.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("Hardware Port:", StringComparison.Ordinal)) port = line["Hardware Port:".Length..].Trim();
                else if (line.StartsWith("Device:", StringComparison.Ordinal) && port != null &&
                         (port.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) || port.Contains("AirPort", StringComparison.OrdinalIgnoreCase)))
                    result.Add(line["Device:".Length..].Trim());
            }
        }
        catch { /* не удалось — будем считать все сети кабельными */ }
        return result;
    }
}
