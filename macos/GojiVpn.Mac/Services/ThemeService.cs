using Avalonia;
using Avalonia.Styling;

namespace GodjiVpn.Services;

public enum ThemeMode { Light, Dark, System }

/// <summary>
/// Светлая / тёмная / системная тема — как в Windows-клиенте, но через
/// Application.RequestedThemeVariant: ThemeVariant.Default следует системной теме macOS
/// живьём, отдельная подписка на её смену не нужна. Выбор хранится в theme.txt.
/// </summary>
public sealed class ThemeService
{
    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodjiVpn", "theme.txt");

    public static ThemeService? Current { get; private set; }

    public ThemeMode Mode { get; private set; } = ThemeMode.System;

    public bool IsDark => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    public event Action? Changed;

    public ThemeService() => Current = this;

    public void Initialize()
    {
        try
        {
            if (File.Exists(StatePath) && Enum.TryParse<ThemeMode>(File.ReadAllText(StatePath).Trim(), out var saved))
                Mode = saved;
        }
        catch { /* по умолчанию — системная */ }
        // Превью-экземпляр для сверки вёрстки может принудительно показать тему (не сохраняется).
        if (Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW_THEME") is "light" or "dark")
            Mode = Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW_THEME") == "dark" ? ThemeMode.Dark : ThemeMode.Light;
        Apply();
        if (Application.Current != null)
            Application.Current.ActualThemeVariantChanged += (_, _) => Changed?.Invoke();
    }

    public void SetMode(ThemeMode mode)
    {
        Mode = mode;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, mode.ToString());
        }
        catch { /* не сохранится — не критично */ }
        Apply();
    }

    private void Apply()
    {
        if (Application.Current == null) return;
        Application.Current.RequestedThemeVariant = Mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
        Changed?.Invoke();
    }
}
