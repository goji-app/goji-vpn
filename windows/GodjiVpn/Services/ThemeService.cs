using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace GodjiVpn.Services;

/// <summary>Светлая/тёмная — явный выбор пользователя; Системная — приложение следует текущей
/// теме Windows (и живо реагирует на её смену, пока открыто, см. ThemeService.OnUserPreferenceChanged)
/// вместо того чтобы всегда открываться в последнем явно выбранном виде. Порт Android ThemeMode.</summary>
public enum ThemeMode { Light, Dark, System }

/// <summary>
/// Переключает светлую/тёмную палитру (см. Themes/LightTheme.xaml, Themes/DarkTheme.xaml —
/// 1:1 с Android GodjiColors.applyLight()/applyDark()) подменой ПЕРВОГО элемента
/// Application.Resources.MergedDictionaries — App.xaml всегда держит там ровно один словарь
/// темы (см. его собственный комментарий). Все *Brush-ссылки в XAML — {DynamicResource}, не
/// {StaticResource}: только Dynamic видит подмену словаря в рантайме без перезапуска.
///
/// Раньше тёмной темы не было вовсе — сознательный выбор ("фирменный светлый стиль, не
/// адаптивная тема"), который пользователь позже прямо попросил пересмотреть. Затем — простой
/// бинарный переключатель; редизайн Tactical Sand & Void заменил его на Light/Dark/System (см.
/// ThemeMode), портировано из Android.
/// </summary>
public sealed class ThemeService
{
    /// <summary>Тот же паттерн, что VpnEngine.Current — глобус (GlobeHost.xaml.cs) живёт в
    /// WebView2, до него не дотянуться обычной проводкой через ViewModel/DataContext на
    /// каждый экран, где он есть (Login, Connect), проще прочитать статический синглтон.</summary>
    public static ThemeService? Current { get; private set; }

    private static string StatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodjiVpn", "theme.txt");

    public ThemeMode Mode { get; private set; } = ThemeMode.System;
    public bool IsDark { get; private set; }

    /// <summary>Не событие PropertyChanged — GlobeHost не ViewModel, ему не нужен полный
    /// INotifyPropertyChanged ради одного bool.</summary>
    public event Action? Changed;

    public ThemeService() => Current = this;

    public void Initialize()
    {
        Mode = Load();
        // Превью-экземпляр (GODJI_UI_PREVIEW, см. App.xaml.cs) может принудительно показать
        // светлую/тёмную тему для сверки с эталоном, не трогая сохранённый выбор пользователя.
        if (Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW_THEME") is "light" or "dark")
            Mode = Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW_THEME") == "dark" ? ThemeMode.Dark : ThemeMode.Light;
        IsDark = ResolveIsDark(Mode);
        Apply();
        // Пока режим Системная — следим за живой сменой темы Windows, а не только за явным
        // выбором в Настройках (аналог isSystemInDarkTheme()+SideEffect в Android GodjiApp.kt).
        // Раньше системная тема полностью игнорировалась приложением.
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public void SetMode(ThemeMode mode)
    {
        Mode = mode;
        Save();
        var dark = ResolveIsDark(mode);
        if (dark == IsDark) return;
        IsDark = dark;
        Apply();
        Changed?.Invoke();
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (Mode != ThemeMode.System || e.Category != UserPreferenceCategory.General) return;
        var dark = IsSystemInDarkMode();
        if (dark == IsDark) return;
        IsDark = dark;
        Application.Current.Dispatcher.Invoke(() =>
        {
            Apply();
            Changed?.Invoke();
        });
    }

    private static bool ResolveIsDark(ThemeMode mode) => mode switch
    {
        ThemeMode.Dark => true,
        ThemeMode.Light => false,
        _ => IsSystemInDarkMode()
    };

    /// <summary>Тот же реестровый ключ, что читает сам Проводник/Параметры Windows для "Режим
    /// приложений" (Параметры → Персонализация → Цвета) — 0 = тёмный, 1 (или ключа нет) = светлый.</summary>
    private static bool IsSystemInDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    private void Apply()
    {
        var uri = new Uri(IsDark ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml", UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };
        Application.Current.Resources.MergedDictionaries[0] = dict;
    }

    /// <summary>Новый пользователь (или тот, кто никогда явно не трогал переключатель темы,
    /// либо раньше явно выбрал "светлая") получает System — приложение следует теме Windows.
    /// Только явный прошлый выбор "тёмная" (файл содержал "dark", старый бинарный переключатель
    /// писал это ТОЛЬКО когда его явно включали) мигрирует в Dark, а не в System — асимметрично,
    /// но 1:1 с тем, как это сделано в Android-версии (там та же миграция: explicit-false тоже
    /// уходит в SYSTEM, а не сохраняется как explicit LIGHT).</summary>
    private static ThemeMode Load()
    {
        try
        {
            if (!File.Exists(StatePath)) return ThemeMode.System;
            var raw = File.ReadAllText(StatePath).Trim();
            return raw switch
            {
                "dark" => ThemeMode.Dark,
                "system" => ThemeMode.System,
                _ => ThemeMode.System
            };
        }
        catch { return ThemeMode.System; }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, Mode switch
            {
                ThemeMode.Dark => "dark",
                ThemeMode.Light => "light",
                _ => "system"
            });
        }
        catch { /* лучшее усилие — при следующем старте просто снова спросим у файла и получим System по умолчанию */ }
    }
}
