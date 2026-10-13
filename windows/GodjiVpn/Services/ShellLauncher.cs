using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace GodjiVpn.Services;

/// <summary>Открывает Проводник из повышенного (requireAdministrator) процесса.
/// Explorer.exe принципиально не запускается с правами администратора — Windows
/// перенаправляет такой запуск в уже работающий обычный shell через DDE, и когда
/// вызывающий процесс имеет более высокую целостность (UIPI блокирует межпроцессные
/// сообщения снизу вверх), эта переадресация не проходит и вместо папки показывается
/// пустая ошибка "Расположение недоступно". Обходим это тем же трюком, что используют
/// инсталляторы: дублируем токен уже существующего explorer.exe (он всегда работает от
/// имени вошедшего пользователя, без повышения) и создаём новый процесс explorer.exe
/// этим токеном — с обычными правами, как будто его открыл сам пользователь.</summary>
public static class ShellLauncher
{
    public static void OpenFolder(string path)
    {
        if (!TryOpenUnelevated(path, out var diagnostic))
        {
            LogFallback(diagnostic);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
    }

    private static bool TryOpenUnelevated(string path, out string diagnostic)
    {
        var explorer = Process.GetProcessesByName("explorer").FirstOrDefault();
        if (explorer == null) { diagnostic = "explorer.exe process not found"; return false; }

        IntPtr hExplorerProcess = IntPtr.Zero;
        IntPtr hProcessToken = IntPtr.Zero;
        IntPtr hPrimaryToken = IntPtr.Zero;
        try
        {
            hExplorerProcess = OpenProcess(PROCESS_QUERY_INFORMATION, false, (uint)explorer.Id);
            if (hExplorerProcess == IntPtr.Zero)
            {
                diagnostic = $"OpenProcess failed: {Marshal.GetLastWin32Error()}";
                return false;
            }

            if (!OpenProcessToken(hExplorerProcess, TOKEN_DUPLICATE, out hProcessToken))
            {
                diagnostic = $"OpenProcessToken failed: {Marshal.GetLastWin32Error()}";
                return false;
            }

            if (!DuplicateTokenEx(hProcessToken, TOKEN_ALL_ACCESS, IntPtr.Zero,
                    SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation, TOKEN_TYPE.TokenPrimary, out hPrimaryToken))
            {
                diagnostic = $"DuplicateTokenEx failed: {Marshal.GetLastWin32Error()}";
                return false;
            }

            var si = new STARTUPINFO();
            si.cb = Marshal.SizeOf<STARTUPINFO>();
            var commandLine = $"explorer.exe \"{path}\"";

            var ok = CreateProcessWithTokenW(hPrimaryToken, 0, null, commandLine, 0, IntPtr.Zero, null,
                ref si, out var pi);
            if (!ok)
            {
                diagnostic = $"CreateProcessWithTokenW failed: {Marshal.GetLastWin32Error()}";
                return false;
            }

            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
            diagnostic = "";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = $"exception: {ex}";
            return false;
        }
        finally
        {
            if (hExplorerProcess != IntPtr.Zero) CloseHandle(hExplorerProcess);
            if (hProcessToken != IntPtr.Zero) CloseHandle(hProcessToken);
            if (hPrimaryToken != IntPtr.Zero) CloseHandle(hPrimaryToken);
        }
    }

    private static void LogFallback(string diagnostic)
    {
        try
        {
            var logsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodjiVpn", "logs");
            Directory.CreateDirectory(logsDir);
            File.AppendAllText(Path.Combine(logsDir, "shell-launcher.log"),
                $"{DateTime.Now:O} unelevated open failed, falling back to elevated explorer.exe: {diagnostic}{Environment.NewLine}");
        }
        catch { /* диагностика — не критично */ }
    }

    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_ALL_ACCESS = 0xF01FF;

    private enum SECURITY_IMPERSONATION_LEVEL { SecurityAnonymous, SecurityIdentification, SecurityImpersonation, SecurityDelegation }
    private enum TOKEN_TYPE { TokenPrimary = 1, TokenImpersonation = 2 }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr existingToken, uint desiredAccess, IntPtr tokenAttributes,
        SECURITY_IMPERSONATION_LEVEL impersonationLevel, TOKEN_TYPE tokenType, out IntPtr newToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessWithTokenW(IntPtr token, int logonFlags, string? applicationName,
        string? commandLine, int creationFlags, IntPtr environment, string? currentDirectory,
        ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
