using System.Diagnostics;

namespace GodjiVpn.Services;

/// <summary>Открыть папку в Finder (журналы) — через /usr/bin/open.</summary>
public static class ShellLauncher
{
    public static void OpenFolder(string path)
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
            psi.ArgumentList.Add(path);
            Process.Start(psi);
        }
        catch { /* не критично */ }
    }
}
