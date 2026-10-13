using System.IO;
using System.Windows;
using System.Windows.Threading;
using GodjiVpn.Services;
using GodjiVpn.ViewModels;

namespace GodjiVpn;

/// <summary>
/// Composition root. Приложение маленькое — контейнер DI избыточен, сервисы и вьюмодели
/// собираются вручную здесь, один раз при старте.
/// </summary>
public partial class App : Application
{
    private SingleInstanceService? _singleInstance;
    private SubscriptionRepository? _subscriptionRepository;
    private JournalRecorder? _journalRecorder;
    private DispatcherTimer? _hourlyRefreshTimer;
    private DispatcherTimer? _updateCheckTimer;
    public TrayIconService? Tray { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Раньше нигде в проекте не было глобального перехватчика — любое необработанное
        // исключение в async void-методе или обработчике события (клик по кнопке, Loaded
        // WPF-контрола и т.п.) валило весь процесс мгновенно, без лога и без сообщения
        // пользователю (реальный риск: сбой инициализации WebView2 в GlobeHost — нет
        // установленного рантайма, заблокирован групповой политикой и т.п. — крашил бы всё
        // приложение целиком просто из-за того, что на экране должен был быть глобус).
        // Логируем и показываем понятное сообщение вместо тихого исчезновения окна.
        DispatcherUnhandledException += (_, args) =>
        {
            LogUnhandledException(args.Exception);
            MessageBox.Show(
                "Произошла непредвиденная ошибка:\n\n" + args.Exception.Message +
                "\n\nПодробности сохранены в журнал (Настройки → Журнал). Приложение продолжит работу.",
                "Godji VPN", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogUnhandledException(args.Exception);
            args.SetObserved();
        };

        var activationUrl = e.Args.FirstOrDefault(a => a.StartsWith("godjivpn://", StringComparison.OrdinalIgnoreCase));

        // GODJI_UI_PREVIEW=1 — второй экземпляр для сверки интерфейса с эталоном, пока рядом
        // работает установленный клиент (запуск с __COMPAT_LAYER=RunAsInvoker, без повышения
        // прав — VPN в таком режиме не поднимется, только UI). В обычной работе не задаётся.
        var uiPreview = Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW") == "1";
        _singleInstance = new SingleInstanceService();
        if (!uiPreview && !_singleInstance.TryAcquire())
        {
            // Уже есть работающий экземпляр (мы запущены заново кликом по godjivpn://
            // после возврата из браузера с OAuth-логином) — передаём ему URL и молча
            // завершаемся, не показывая собственного окна.
            SingleInstanceService.ForwardToRunningInstanceAndExit(activationUrl);
            Shutdown();
            return;
        }

        // Применяем сохранённую тему ДО создания окна — иначе был бы виден краткий "мигок"
        // светлой темой перед подменой на тёмную сразу вслед.
        var themeService = new ThemeService();
        themeService.Initialize();

        var tokenStore = new TokenStore();
        var hwidProvider = new HwidProvider();
        var apiClient = new ApiClient(tokenStore);
        var subscriptionService = new SubscriptionService(hwidProvider);
        var customNodeStore = new CustomNodeStore();
        var favoriteServersStore = new FavoriteServersStore();
        var appSettings = new AppSettings();
        GodjiVpn.Utils.FontScale.Instance.Apply(appSettings.FontSize);
        // Превью-экземпляр: размер шрифта для сверки без изменения сохранённой настройки.
        if (uiPreview && Enum.TryParse<GodjiVpn.Utils.FontSizePreset>(
                Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW_FONT"), true, out var previewFont))
            GodjiVpn.Utils.FontScale.Instance.Apply(previewFont);
        var subscriptionRepository = new SubscriptionRepository(apiClient, subscriptionService, customNodeStore);
        _subscriptionRepository = subscriptionRepository;
        var subscriptionNotifier = new SubscriptionNotifier();
        var broadcastNotifier = new BroadcastNotifier();
        var updateService = new UpdateService();
        var updateNotifier = new UpdateNotifier();
        var vpnEngine = new VpnEngine(); // конструктор сам регистрирует себя в VpnEngine.Current
        // Журнал сети: подключения, смена сети, ошибки (см. JournalRecorder/NetworkJournal).
        _journalRecorder = new JournalRecorder(vpnEngine, subscriptionRepository);
        // Правила Wi-Fi: превью-экземпляр только читает имя сети для экрана, но не следит за
        // сменой сети (не должен сам включать/выключать VPN рядом с установленным клиентом).
        var networkRules = new NetworkRulesManager(appSettings, vpnEngine, subscriptionRepository);
        if (uiPreview) networkRules.Refresh(); else networkRules.Start();

        var loginViewModel = new LoginViewModel(apiClient, tokenStore);
        var pingSettings = new PingSettings();
        var connectViewModel = new ConnectViewModel(vpnEngine, subscriptionRepository, pingSettings);
        var pingService = new PingService(pingSettings);
        var serversViewModel = new ServersViewModel(subscriptionRepository, pingService, customNodeStore, favoriteServersStore, appSettings);
        var plansViewModel = new PlansViewModel(apiClient, subscriptionRepository, tokenStore);
        var settingsViewModel = new SettingsViewModel(tokenStore, vpnEngine, hwidProvider, pingSettings, themeService, updateService, apiClient, appSettings, networkRules);
        var shellViewModel = new ShellViewModel(connectViewModel, serversViewModel, plansViewModel, settingsViewModel);
        var mainViewModel = new MainViewModel(tokenStore, loginViewModel, shellViewModel);
        // Выход из аккаунта или истёкшая сессия — забыть подписку, новости и серверы прежнего
        // аккаунта (и на диске, см. AccountCache).
        tokenStore.Cleared += subscriptionRepository.ClearAccount;
        apiClient.SessionExpired += () => Dispatcher.BeginInvoke(mainViewModel.HandleSessionExpired);

        var window = new MainWindow { DataContext = mainViewModel };
        MainWindow = window;

        Tray = new TrayIconService(vpnEngine, connectViewModel, window, ExitApplication);
        subscriptionNotifier.NotificationRequested += (title, text) => Tray.ShowBalloon(title, text);
        broadcastNotifier.NotificationRequested += (title, text) => Tray.ShowBalloon(title, text);
        updateNotifier.NotificationRequested += (title, text) => Tray.ShowBalloon(title, text);
        subscriptionRepository.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SubscriptionRepository.Subscription) && subscriptionRepository.Subscription is { } sub)
                subscriptionNotifier.Check(sub);
            if (args.PropertyName == nameof(SubscriptionRepository.Broadcasts))
                broadcastNotifier.Check(subscriptionRepository.Broadcasts);
        };
        window.Closing += (_, args) =>
        {
            if (Tray.IsExiting) return;
            // Как и у любого VPN-клиента — крестик сворачивает в трей, а не завершает
            // процесс (иначе поднятый туннель пришлось бы либо обрывать вместе с окном,
            // либо он остался бы висеть без какого-либо UI вообще).
            args.Cancel = true;
            window.Hide();
        };

        window.Show();

        // Входы через сайт, сделанные до исправления, сохранены без refresh-токена — разово
        // забираем его из хранилища кук WebView2 (см. WebRefreshTokenRecovery).
        _ = WebRefreshTokenRecovery.TryRecoverAsync(tokenStore, window);

        // godjivpn:// раньше был нужен только для возврата OAuth-кода из системного браузера
        // (native-exchange, сломан на бэкенде 7.1.0 — см. LoginViewModel.OpenWebLoginAsync,
        // заменён на встроенное окно веб-входа). Регистрация схемы в реестре (были
        // OAuthProtocolRegistrar.EnsureRegistered()) больше не нужна — обработчик активации
        // ниже остаётся общей защитой от случайного повторного запуска процесса.
        _singleInstance.ActivationRequested += _ => Dispatcher.Invoke(() => HandleActivationUrl(window));
        if (activationUrl != null) HandleActivationUrl(window);

        _hourlyRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        _hourlyRefreshTimer.Tick += async (_, _) => await subscriptionRepository.RefreshAsync();
        _hourlyRefreshTimer.Start();

        // Раз в сутки — как в Android (UpdateCheckWorker: "Раз в сутки"); GitHub Releases не
        // меняются ежеминутно, чаще проверять незачем. Уведомление в трее — не чаще одного
        // раза на версию (см. UpdateNotifier), незамеченное не превращается в спам.
        async Task CheckForUpdatesAsync()
        {
            var update = await updateService.CheckForUpdateAsync();
            settingsViewModel.ApplyBackgroundUpdateCheck(update);
            if (update != null) updateNotifier.Check(update);
        }
        _ = CheckForUpdatesAsync();
        _updateCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(24) };
        _updateCheckTimer.Tick += async (_, _) => await CheckForUpdatesAsync();
        _updateCheckTimer.Start();

        await mainViewModel.InitializeAsync();
    }

    /// <summary>Второй экземпляр процесса (случайный повторный запуск .exe) передаёт нам эстафету
    /// через именованный pipe вместо открытия своего окна — просто поднимаем уже существующее.</summary>
    private static void HandleActivationUrl(MainWindow window)
    {
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    }

    /// <summary>Единственный путь закрыть процесс целиком (крестик на окне сворачивает в
    /// трей, см. window.Closing выше) — если не разорвать туннель здесь, xray.exe/sing-box.exe
    /// (обычные Process.Start, не привязанные к жизни родителя Job-объектом) остаются висеть
    /// сами по себе вместе с системным default route на TUN-адаптер, а у пользователя больше
    /// нет ни окна, ни трея, чтобы отключиться — только Task Manager или перезагрузка.</summary>
    internal static void LogUnhandledException(Exception ex)
    {
        try
        {
            var logsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodjiVpn", "logs");
            Directory.CreateDirectory(logsDir);
            File.AppendAllText(Path.Combine(logsDir, "crash.log"),
                $"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} =====\n{ex}\n\n");
        }
        catch { /* если даже лог не записать — уже ничего не поделать, не даём этому уронить обработчик */ }
    }

    private async void ExitApplication()
    {
        _hourlyRefreshTimer?.Stop();
        _updateCheckTimer?.Stop();
        try { await (VpnEngine.Current?.DisconnectAsync() ?? Task.CompletedTask); }
        catch { /* уходим в любом случае — не даём сбою разрыва тоннеля помешать закрытию */ }
        Tray?.Dispose();
        Shutdown();
    }
}
