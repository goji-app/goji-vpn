using System.IO;
using System.Windows;
using System.Windows.Controls;
using GodjiVpn.Services;
using Microsoft.Web.WebView2.Core;

namespace GodjiVpn.Controls;

/// <summary>
/// Обёртка над WebView2, хостит оригинальный веб-компонент &lt;goji-globe&gt; (three.js),
/// скопированный из godji-android/goji-globe.js — тот же файл, с которого был сделан
/// Android-порт на OpenGL, только теперь используется как есть, без переписывания на
/// Direct3D. Статус/выбранный узел прокидываются в JS через ExecuteScriptAsync.
///
/// Виртуальный хост (SetVirtualHostNameToFolderMapping), а не file:// — ES-модули и
/// fetch() внутри goji-globe.js (three.js/world-atlas грузятся с CDN) не отрабатывают
/// корректно под file:// (CORS/opaque origin); подтверждено вручную через тестовый
/// http.server до интеграции — под http(s) всё работает как надо.
/// </summary>
public partial class GlobeHost : UserControl
{
    private const string VirtualHost = "godji.local";
    private bool _ready;
    private bool _disposed;
    private readonly Queue<Func<Task>> _pending = new();
    private Window? _window;
    private bool? _sentPaused;

    /// <summary>Спутники на орбитах — только экран входа (satellites="on" в эталоне).
    /// Задаётся в XAML до загрузки: компонент читает его один раз при старте.</summary>
    public bool Satellites { get; set; }

    public GlobeHost()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            _window = Window.GetWindow(this);
            if (_window != null)
            {
                _window.StateChanged += OnWindowStateChanged;
                _window.Activated += OnWindowStateChanged;
                _window.Deactivated += OnWindowStateChanged;
            }
            UpdatePaused();
            await InitializeAsync();
        };
        // Глобус рисуется, только пока его видно: ShellView держит все вкладки живыми и лишь
        // скрывает неактивные, окно может быть свёрнуто или убрано в трей — во всех этих
        // случаях three.js продолжал рендерить 60 кадров в секунду в никуда (десятки процентов
        // ядра в простое). Ещё глобус замирает, пока окно не в фокусе: каждый его кадр WPF
        // копирует из WebView2 в своё окно (WebView2CompositionControl), и даже на 24 кадрах
        // это заметная доля ядра — тратить её, пока пользователь работает в другой программе,
        // ради фоновой декорации незачем.
        IsVisibleChanged += (_, _) => UpdatePaused();
        // MainWindow меняет CurrentViewModel между LoginViewModel/ShellViewModel через implicit
        // DataTemplate (см. App.xaml) — при каждом входе/выходе из аккаунта WPF полностью
        // пересобирает визуальное дерево, а значит и этот GlobeHost со своим WebView2 создаётся
        // заново. Без явного Dispose() старый CoreWebView2Environment/браузерный процесс не
        // освобождается сам по себе (WebView2 не диспозится автоматически на Unloaded) — при
        // нескольких логинах/логаутах за сессию это утечка процессов/памяти.
        Unloaded += (_, _) =>
        {
            // Быстрый логин (например через WebLoginWindow, когда пользователь уже залогинен
            // на сайте) может заменить LoginView на ShellView раньше, чем WebView2 глобуса
            // закончит асинхронную инициализацию/навигацию — тогда её продолжение после await
            // резюмируется уже после Dispose() ниже и падает с "Cannot access a disposed
            // object. Object name: 'Web'." (реальный краш пользователя). _disposed проверяется
            // в каждой точке после await/в отложенных вызовах, чтобы такое продолжение просто
            // тихо остановилось, а не обращалось к освобождённому Web.
            _disposed = true;
            _pending.Clear();
            if (_window != null)
            {
                _window.StateChanged -= OnWindowStateChanged;
                _window.Activated -= OnWindowStateChanged;
                _window.Deactivated -= OnWindowStateChanged;
            }
            _window = null;
            if (ThemeService.Current != null) ThemeService.Current.Changed -= OnThemeChanged;
            Web.Dispose();
        };
    }

    private async Task InitializeAsync()
    {
        if (_ready || _disposed) return;

        try
        {
            // Общий Environment на процесс (см. WebView2EnvironmentProvider) — та же папка
            // данных, которую использует и WebLoginWindow, вместо создания второй независимой
            // копии по умолчанию рядом с exe (там нет прав на запись в Program Files).
            var env = await WebView2EnvironmentProvider.GetAsync();
            if (_disposed) return;
            await Web.EnsureCoreWebView2Async(env);
            if (_disposed) return;

            var globeDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Globe");
            Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHost, globeDir, CoreWebView2HostResourceAccessKind.Allow);

            Web.CoreWebView2.NavigationCompleted += async (_, e) =>
            {
                if (_disposed || !e.IsSuccess) return;
                _ready = true;
                await SetThemeAsync(ThemeService.Current?.IsDark ?? false);
                while (_pending.Count > 0 && !_disposed) await _pending.Dequeue()();
            };
            Web.CoreWebView2.Navigate($"https://{VirtualHost}/globe.html{(Satellites ? "?sat=1" : "")}");

            // Живое переключение темы, пока экран с глобусом уже открыт (не только на старте) —
            // тот же статический синглтон-паттерн, что и VpnEngine.Current.
            if (ThemeService.Current != null) ThemeService.Current.Changed += OnThemeChanged;
        }
        catch (Exception ex)
        {
            // Глобус — декоративный элемент, а не критичная для VPN функция. Если на машине
            // нет установленного WebView2 Runtime (не на всех Windows 10 он есть из коробки),
            // он заблокирован групповой политикой, или что-то ещё пошло не так — приложение
            // должно остаться полностью рабочим, просто без глобуса, а не падать целиком (это
            // было бы особенно обидно, учитывая, что GlobeHost встроен и в LoginView, то есть
            // сработало бы на самом первом экране, который видит пользователь).
            App.LogUnhandledException(ex);
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs e) => UpdatePaused();

    private void UpdatePaused()
    {
        if (_disposed) return;
        var paused = !IsVisible || _window == null || _window.WindowState == WindowState.Minimized || !_window.IsActive;
        if (_sentPaused == paused) return;
        _sentPaused = paused;
        _ = RunAsync($"window.godjiSetPaused && window.godjiSetPaused({(paused ? "true" : "false")})");
        // Пока глобус не виден, просим WebView2 держать поменьше памяти (кэши, GPU-ресурсы).
        if (_ready)
        {
            try
            {
                Web.CoreWebView2.MemoryUsageTargetLevel = paused
                    ? CoreWebView2MemoryUsageTargetLevel.Low
                    : CoreWebView2MemoryUsageTargetLevel.Normal;
            }
            catch { /* старый WebView2 Runtime — не критично */ }
        }
    }

    private async void OnThemeChanged()
    {
        if (_disposed) return;
        await SetThemeAsync(ThemeService.Current?.IsDark ?? false);
    }

    public Task SetThemeAsync(bool isDark) =>
        RunAsync($"window.godjiSetTheme && window.godjiSetTheme('{(isDark ? "dark" : "light")}')");

    public Task SetStatusAsync(string status) =>
        RunAsync($"window.godjiSetStatus && window.godjiSetStatus('{Escape(status)}')");

    public Task SetNodeAsync(double lat, double lon, string country, string city, string title) =>
        RunAsync($"window.godjiSetNode && window.godjiSetNode({lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
                  $"{lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}, " +
                  $"'{Escape(country)}', '{Escape(city)}', '{Escape(title)}')");

    private Task RunAsync(string script)
    {
        if (_disposed) return Task.CompletedTask;
        if (_ready) return Web.CoreWebView2.ExecuteScriptAsync(script);
        _pending.Enqueue(() => _disposed ? Task.CompletedTask : Web.CoreWebView2.ExecuteScriptAsync(script));
        return Task.CompletedTask;
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");
}
