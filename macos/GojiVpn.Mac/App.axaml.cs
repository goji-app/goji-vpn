using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using GodjiVpn.Services;
using GodjiVpn.ViewModels;
using GodjiVpn.Views;

namespace GodjiVpn;

/// <summary>
/// Корень композиции — как App.xaml.cs Windows-клиента: сервисы, ViewModels, главное окно,
/// значок в строке меню, уведомления, таймеры обновления подписки и проверки обновлений.
/// Закрытие окна прячет его (VPN продолжает работать, управление — из строки меню); выход —
/// только пунктом "Выход", с разрывом туннеля.
/// </summary>
public partial class App : Application
{
    private JournalRecorder? _journalRecorder;
    private DispatcherTimer? _hourlyRefreshTimer;
    private DispatcherTimer? _updateCheckTimer;
    private bool _exiting;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

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
        if (Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW") == "1" &&
            Enum.TryParse<GodjiVpn.Utils.FontSizePreset>(Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW_FONT"), true, out var previewFont))
            GodjiVpn.Utils.FontScale.Instance.Apply(previewFont);
        var subscriptionRepository = new SubscriptionRepository(apiClient, subscriptionService, customNodeStore);
        var subscriptionNotifier = new SubscriptionNotifier();
        var broadcastNotifier = new BroadcastNotifier();
        var updateService = new UpdateService();
        var updateNotifier = new UpdateNotifier();
        var vpnEngine = new VpnEngine();
        // Журнал сети: подключения, смена сети, ошибки (см. JournalRecorder/NetworkJournal).
        _journalRecorder = new JournalRecorder(vpnEngine, subscriptionRepository);

        var loginViewModel = new LoginViewModel(apiClient, tokenStore);
        var pingSettings = new PingSettings();
        var connectViewModel = new ConnectViewModel(vpnEngine, subscriptionRepository, pingSettings);
        var pingService = new PingService(pingSettings);
        var serversViewModel = new ServersViewModel(subscriptionRepository, pingService, customNodeStore, favoriteServersStore, appSettings);
        var plansViewModel = new PlansViewModel(apiClient, subscriptionRepository, tokenStore);
        var settingsViewModel = new SettingsViewModel(tokenStore, vpnEngine, hwidProvider, pingSettings, themeService, updateService, apiClient, appSettings);
        var shellViewModel = new ShellViewModel(connectViewModel, serversViewModel, plansViewModel, settingsViewModel);
        var mainViewModel = new MainViewModel(tokenStore, loginViewModel, shellViewModel);
        // Выход из аккаунта или истёкшая сессия — забыть подписку, новости и серверы прежнего
        // аккаунта (и на диске, см. AccountCache).
        tokenStore.Cleared += subscriptionRepository.ClearAccount;
        apiClient.SessionExpired += () => Dispatcher.UIThread.Post(mainViewModel.HandleSessionExpired);

        var window = new MainWindow { DataContext = mainViewModel };
        desktop.MainWindow = window;

        MenuBarService? tray = null;
        tray = new MenuBarService(vpnEngine, connectViewModel, window, () => Exit());
        subscriptionNotifier.NotificationRequested += Notifier.Show;
        broadcastNotifier.NotificationRequested += Notifier.Show;
        updateNotifier.NotificationRequested += Notifier.Show;
        subscriptionRepository.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SubscriptionRepository.Subscription) && subscriptionRepository.Subscription is { } sub)
                subscriptionNotifier.Check(sub);
            if (args.PropertyName == nameof(SubscriptionRepository.Broadcasts))
                broadcastNotifier.Check(subscriptionRepository.Broadcasts);
        };
        window.Closing += (_, args) =>
        {
            if (_exiting) return;
            // Как у любого VPN-клиента: закрытие окна не рвёт туннель — приложение остаётся в
            // строке меню, окно открывается оттуда или кликом по значку в Dock.
            args.Cancel = true;
            window.Hide();
        };
        // Клик по значку в Dock при спрятанном окне — показать его снова.
        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
            activatable.Activated += (_, _) => tray?.ShowWindow();

        window.Show();

        _hourlyRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        _hourlyRefreshTimer.Tick += async (_, _) => await subscriptionRepository.RefreshAsync();
        _hourlyRefreshTimer.Start();

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

        _ = mainViewModel.InitializeAsync();
        base.OnFrameworkInitializationCompleted();

        async void Exit()
        {
            _exiting = true;
            _hourlyRefreshTimer?.Stop();
            _updateCheckTimer?.Stop();
            try { await vpnEngine.DisconnectAsync(); } catch { /* уходим в любом случае */ }
            tray?.Dispose();
            desktop.Shutdown();
        }
    }
}
