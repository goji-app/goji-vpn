using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GodjiVpn.Services;

/// <summary>Системное уведомление macOS (напоминания о подписке, новости, обновления) — через
/// osascript "display notification": не требует отдельной регистрации приложения в Центре
/// уведомлений.</summary>
public static class Notifier
{
    public static void Show(string title, string text)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;
        try
        {
            static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var psi = new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add($"display notification \"{Esc(text)}\" with title \"Goji VPN\" subtitle \"{Esc(title)}\"");
            Process.Start(psi)?.Dispose();
        }
        catch { /* уведомление — лучшее усилие */ }
    }
}
