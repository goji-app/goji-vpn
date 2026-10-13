using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GodjiVpn.ViewModels;

/// <summary>Главный экран после логина — 4 вкладки (Главная/Серверы/Подписка/Настройки), как
/// нижняя навигация в Android (GodjiDestinations.kt: connect/servers/plans/settings).</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    public ConnectViewModel Connect { get; }
    public ServersViewModel Servers { get; }
    public PlansViewModel Plans { get; }
    public SettingsViewModel Settings { get; }

    public event Action? LoggedOut;

    [ObservableProperty] private int selectedTabIndex;

    public ShellViewModel(ConnectViewModel connect, ServersViewModel servers, PlansViewModel plans,
        SettingsViewModel settings)
    {
        Connect = connect;
        Servers = servers;
        Plans = plans;
        Settings = settings;
        Settings.RequestLogout += () => LoggedOut?.Invoke();
        Connect.NavigateToPlansRequested += () => SelectedTabIndex = 2;
        Connect.NavigateToServersRequested += () => SelectedTabIndex = 1;
        Connect.OpenJournalRequested += () =>
        {
            Settings.OpenJournal();
            SelectedTabIndex = 3;
        };
        Servers.ServerPicked += () => SelectedTabIndex = 0;
        // Превью-экземпляр (сверка вёрстки): открыть сразу нужную вкладку.
        if (Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW") == "1" &&
            int.TryParse(Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW_TAB"), out var tab))
            SelectedTabIndex = tab;
    }

    /// <summary>Вызывается один раз при старте приложения (см. MainViewModel.InitializeAsync)
    /// — подписка и пинг всех серверов сами обновляются при открытии, не только по ручному
    /// "Обновить"/"Пинг" на вкладке "Серверы".</summary>
    public async Task LoadAsync()
    {
        await Connect.LoadAsync();
        await Servers.PingAllCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void GoToPlans() => SelectedTabIndex = 2;

    partial void OnSelectedTabIndexChanged(int value)
    {
        // Подписка на вкладке "Подписка" раньше обновлялась только вручную (кнопка ⟳) —
        // открывший её пользователь мог смотреть устаревшие/пустые данные, если до этого
        // ни разу не жал "обновить". См. также LoadAsync — автообновление при самом
        // старте приложения.
        if (value == 1) _ = Servers.AutoPingAsync();
        if (value == 2) _ = Plans.LoadAsync();
        if (value == 3) Settings.Load();
        // Подэкран Настроек (пинг/журнал) не переживает уход с вкладки — вернувшись,
        // пользователь видит сам список настроек, как после "назад" в Android.
        else Settings.Page = SettingsPage.Main;
    }
}
