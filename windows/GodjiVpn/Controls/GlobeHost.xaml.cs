using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using GodjiVpn.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

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
    /// <summary>Сколько раз пересоздаём контрол после смерти браузерного процесса, прежде чем
    /// сдаться и оставить глобус пустым (он декоративный, но процесс, умирающий сразу после
    /// пересоздания, не должен крутить бесконечный цикл «умер → создали → умер»).</summary>
    private const int MaxCrashRecoveries = 3;
    private bool _ready;
    private bool _disposed;
    private readonly Queue<Func<Task>> _pending = new();
    private Window? _window;
    private bool? _sentPaused;
    // Падение браузерного процесса (BrowserProcessExited): контрол после этого мёртв навсегда
    // (даже CoreWebView2-геттер кидает InvalidOperationException — реальный краш пользователя
    // 1.0.132/2026-11-03, серия записей в crash.log с каждого resize/activate окна).
    private bool _browserDead;
    private bool _recovering;
    private bool _crashedDuringRecovery;
    private int _crashRecoveries;

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
            await InitializeCoreAsync();
            return;
        }
        catch (Exception ex)
        {
            // Глобус — декоративный элемент, а не критичная для VPN функция. Если на машине
            // нет установленного WebView2 Runtime (не на всех Windows 10 он есть из коробки),
            // он заблокирован групповой политикой, или что-то ещё пошло не так — приложение
            // должно остаться полностью рабочим, просто без глобуса, а не падать целиком (это
            // было бы особенно обидно, учитывая, что GlobeHost встроен и в LoginView, то есть
            // сработало бы на самом первом экране, который видит пользователь).
            LogGlobeFailure("инициализация WebView2 не удалась", ex);
        }
    }

    private async Task InitializeCoreAsync()
    {
        if (_ready || _disposed) return;

        // Общий Environment на процесс (см. WebView2EnvironmentProvider) — та же папка
        // данных, которую использует и окно веб-входа, вместо создания второй независимой
        // копии по умолчанию рядом с exe (там нет прав на запись в Program Files).
        var env = await WebView2EnvironmentProvider.GetAsync();
        if (_disposed) return;
        await Web.EnsureCoreWebView2Async(env);
        if (_disposed) return;

        var globeDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Globe");
        Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
            VirtualHost, globeDir, CoreWebView2HostResourceAccessKind.Allow);

        // Должен висеть до Navigate: смерть процесса может случиться в любой момент,
        // и без обработчика контрол остаётся «зомби» (см. _browserDead).
        Web.CoreWebView2.ProcessFailed += OnProcessFailed;

        Web.CoreWebView2.NavigationCompleted += async (_, e) =>
        {
            if (_disposed || !e.IsSuccess) return;
            _ready = true;
            await SetThemeAsync(ThemeService.Current?.IsDark ?? false);
            while (_pending.Count > 0 && !_disposed) await _pending.Dequeue()();
            // Свежая страница (первая загрузка, перезагрузка после смерти рендер-процесса
            // или пересоздание контрола после смерти браузерного) не знает о состоянии окна —
            // заставляем UpdatePaused отправить godjiSetPaused и MemoryUsageTargetLevel заново.
            _sentPaused = null;
            UpdatePaused();
        };
        Web.CoreWebView2.Navigate($"https://{VirtualHost}/globe.html{(Satellites ? "?sat=1" : "")}");

        // Живое переключение темы, пока экран с глобусом уже открыт (не только на старте) —
        // тот же статический синглтон-паттерн, что и VpnEngine.Current. -= перед += нужен для
        // повторной инициализации после пересоздания контрола: подписка живёт на сервисе,
        // а не на контроле, и без -= накапливалась бы с каждым восстановлением.
        if (ThemeService.Current != null)
        {
            ThemeService.Current.Changed -= OnThemeChanged;
            ThemeService.Current.Changed += OnThemeChanged;
        }
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        if (_disposed) return;
        switch (e.ProcessFailedKind)
        {
            case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                if (_recovering) { _crashedDuringRecovery = true; return; }
                HandleBrowserProcessExited(e);
                break;
            case CoreWebView2ProcessFailedKind.RenderProcessExited:
                // Рендер-процесс WebView2 поднимает автоматически, но с той же ошибкой на
                // странице (по документации достаточно Reload, чтобы загрузить контент заново).
                try { Web.CoreWebView2.Reload(); }
                catch { /* не смогли — глобус просто замрёт до следующего события */ }
                break;
            // Остальные виды (render unresponsive, падения процессов фреймов) WebView2
            // переживает сам; глобус декоративный — в лог не спамим.
        }
    }

    private async void HandleBrowserProcessExited(CoreWebView2ProcessFailedEventArgs e)
    {
        // Второй ProcessFailed от уже умершего процесса не придёт, но подстраховаться стоит:
        // recovery запускается один раз, окончательно сдавшийся глобус помечен _browserDead.
        if (_recovering || _disposed || (_browserDead && _crashRecoveries >= MaxCrashRecoveries)) return;
        _recovering = true;
        var restarted = false;
        try
        {
            while (!_disposed && _crashRecoveries < MaxCrashRecoveries)
            {
                _browserDead = true; // RunAsync/UpdatePaused молчат, пока контрол мёртв
                _ready = false;
                _crashedDuringRecovery = false;
                _crashRecoveries++;
                App.LogUnhandledException(new InvalidOperationException(
                    $"WebView2: умер браузерный процесс глобуса (kind={e.ProcessFailedKind}, " +
                    $"reason={e.Reason}, exit={e.ExitCode}) — пересоздаю контрол " +
                    $"(попытка {_crashRecoveries} из {MaxCrashRecoveries})"));
                // Пауза даёт системе доубрать процессы и отойти от причины смерти
                // (сброс GPU-драйвера, память) — нарастающая, чтобы не долбиться в упор.
                await Task.Delay(TimeSpan.FromSeconds(_crashRecoveries));
                if (_disposed) return;
                try
                {
                    RecreateWebView();
                }
                catch (Exception ex)
                {
                    LogGlobeFailure("не удалось пересоздать WebView2-контрол", ex);
                    continue;
                }
                restarted = await TryInitializeCoreAsync();
                if (restarted && !_crashedDuringRecovery) break;
                restarted = false;
            }
        }
        finally
        {
            _recovering = false;
            if (!_disposed)
            {
                if (restarted || _ready)
                {
                    // _ready выставляет NavigationCompleted: если он успел — контрол уже
                    // фактически работает, _browserDead надо снять в любом случае
                    _browserDead = false;
                }
                else
                {
                    _browserDead = true;
                    App.LogUnhandledException(new InvalidOperationException(
                        "WebView2: глобус не удалось восстановить после падения браузерного " +
                        "процесса — приложение продолжает работу без него"));
                }
            }
        }
    }

    /// <summary>Одна попытка инициализации: сначала с закэшированной средой, при неудаче —
    /// со свежей (среда, пережившая смерть своего браузерного процесса, может не поднять
    /// новый; новая среда с той же папкой данных в том же процессе — штатный сценарий).</summary>
    private async Task<bool> TryInitializeCoreAsync()
    {
        try
        {
            await InitializeCoreAsync();
            return true;
        }
        catch (Exception ex)
        {
            LogGlobeFailure("инициализация пересозданного контрола не удалась", ex);
            if (_disposed) return false;
            WebView2EnvironmentProvider.Reset();
            try
            {
                await InitializeCoreAsync();
                return true;
            }
            catch (Exception ex2)
            {
                LogGlobeFailure("повтор с новой средой WebView2 тоже не удался", ex2);
                return false;
            }
        }
    }

    /// <summary>Старый контрол после BrowserProcessExited мёртв навсегда (по документации
    /// восстановить можно только пересозданием) — подменяем его в гриде новым.</summary>
    private void RecreateWebView()
    {
        var parent = (Grid)Web.Parent;
        var index = parent.Children.IndexOf(Web);
        parent.Children.Remove(Web);
        try { Web.Dispose(); }
        catch { /* мёртвый контрол мог не пережить Dispose — не важно, он уже невидим */ }
        Web = new WebView2CompositionControl { DefaultBackgroundColor = System.Drawing.Color.Transparent };
        parent.Children.Insert(index, Web);
        // Состояние pause/replay будет отправлено заново после NavigationCompleted
        // (см. сброс _sentPaused там же), очередь _pending сохраняем — она доиграет
        // на новом контроле и вернёт глобусу последний известный статус/узел.
        _sentPaused = null;
    }

    private static void LogGlobeFailure(string what, Exception ex) =>
        App.LogUnhandledException(new InvalidOperationException($"Глобус (WebView2): {what}", ex));

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
        // Окончательно сдавшийся глобус копить скрипты не должен (утечка очереди);
        // во время recovery очередь, наоборот, сохраняется и доигрывает на новом контроле.
        if (_browserDead && !_recovering) return Task.CompletedTask;
        if (_ready) return ExecuteScriptAsyncSafe(script);
        _pending.Enqueue(() => _disposed ? Task.CompletedTask : ExecuteScriptAsyncSafe(script));
        return Task.CompletedTask;
    }

    /// <summary>ExecuteScriptAsync, переживаемый молча: гонка «ProcessFailed ещё не долетел,
    /// а CoreWebView2 уже мёртв» кидает InvalidOperationException прямо в геттере, ObjectDisposed —
    /// на Dispose-гонке, COMException — при смерти GPU. Без перехвата это исключение уходит
    /// через UpdatePaused → WndProc → DispatcherUnhandledException (реальный краш клиента).</summary>
    private Task ExecuteScriptAsyncSafe(string script)
    {
        try
        {
            return Web.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or COMException)
        {
            LogGlobeFailure("не смог выполнить скрипт в глобусе", ex);
            return Task.CompletedTask;
        }
    }

    /// <summary>Экранирование строки для JS-литерала в одинарных кавычках (ExecuteScriptAsync):
    /// помимо кавычек и обратного слэша — переводы строк и прочие управляющие символы (\n/\r
    /// и т.п. раньше попадали в литерал как есть и ломали скрипт).</summary>
    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\'': sb.Append("\\'"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    sb.Append(char.IsControl(c) ? ' ' : c);
                    break;
            }
        }
        return sb.ToString();
    }
}
