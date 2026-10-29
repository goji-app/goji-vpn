using System.Diagnostics;

namespace GodjiVpn.Services;

/// <summary>
/// Токены сессии — в системном Keychain через /usr/bin/security (add-generic-password).
/// Файл с правами 600 (прежнее хранение) защищал только от ДРУГИХ локальных пользователей —
/// любой процесс того же пользователя читал refresh-токен свободно и жил в аккаунте
/// бессрочно. Элемент Keychain, созданный самим приложением, другим процессам отдаётся
/// только после явного разрешения пользователя (ACL).
/// Нюанс ad-hoc подписи (своего Developer ID нет): после обновления приложения подпись
/// меняется, и Keychain может один раз спросить разрешение на доступ — это осознанная
/// цена против открытого файла. Значение -w идёт аргументом командной строки: на мгновение
/// видно в ps только процессам того же пользователя.
/// </summary>
internal static class Keychain
{
    private const string Service = "GodjiVpn-session";

    public static void Write(string account, string value)
    {
        var psi = Start("add-generic-password");
        psi.ArgumentList.Add("-s"); psi.ArgumentList.Add(Service);
        psi.ArgumentList.Add("-a"); psi.ArgumentList.Add(account);
        psi.ArgumentList.Add("-w"); psi.ArgumentList.Add(value);
        psi.ArgumentList.Add("-U"); // обновить существующую запись вместо ошибки "already exists"
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("security не запустился");
        p.WaitForExit(5000);
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"Keychain: {p.StandardError.ReadToEnd().Trim()} (код {p.ExitCode})");
    }

    public static string? Read(string account)
    {
        try
        {
            var psi = Start("find-generic-password");
            psi.ArgumentList.Add("-s"); psi.ArgumentList.Add(Service);
            psi.ArgumentList.Add("-a"); psi.ArgumentList.Add(account);
            psi.ArgumentList.Add("-w");
            using var p = Process.Start(psi);
            if (p == null) return null;
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            // 44 = "could not be found" — нормальный случай "не залогинен".
            return p.ExitCode == 0 ? stdout.TrimEnd('\n') : null;
        }
        catch { return null; }
    }

    public static void Delete(string account)
    {
        try
        {
            var psi = Start("delete-generic-password");
            psi.ArgumentList.Add("-s"); psi.ArgumentList.Add(Service);
            psi.ArgumentList.Add("-a"); psi.ArgumentList.Add(account);
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
        }
        catch { /* лучшее усилие */ }
    }

    private static ProcessStartInfo Start(string command)
    {
        var psi = new ProcessStartInfo("/usr/bin/security")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(command);
        return psi;
    }
}
