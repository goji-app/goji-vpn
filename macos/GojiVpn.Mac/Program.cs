using Avalonia;

namespace GodjiVpn;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Необработанное исключение — в crash.log (журнал "Сбои" в Настройках).
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { LogCrash(e.Exception); e.SetObserved(); };
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        catch (Exception ex) { LogCrash(ex); throw; }
    }

    internal static void LogCrash(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodjiVpn", "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"), $"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} =====\n{ex}\n\n");
        }
        catch { /* ничего не поделать */ }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
