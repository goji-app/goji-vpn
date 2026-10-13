using CommunityToolkit.Mvvm.ComponentModel;
using GodjiVpn.Services;

namespace GodjiVpn.ViewModels;

/// <summary>Переключает содержимое главного окна между LoginViewModel и ShellViewModel —
/// какая View показывается, решают DataTemplate'ы в MainWindow.xaml по типу CurrentViewModel.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly TokenStore _tokenStore;
    private readonly LoginViewModel _login;
    private readonly ShellViewModel _shell;

    [ObservableProperty] private object currentViewModel;

    public MainViewModel(TokenStore tokenStore, LoginViewModel login, ShellViewModel shell)
    {
        _tokenStore = tokenStore;
        _login = login;
        _shell = shell;

        _login.LoggedIn += OnLoggedIn;
        _shell.LoggedOut += OnLoggedOut;

        currentViewModel = _tokenStore.IsLoggedIn ? _shell : _login;

        // Только для превью-экземпляра (GODJI_UI_PREVIEW, см. App.xaml.cs): показать экран
        // входа/подтверждения кода для сверки с эталоном, не трогая сохранённую сессию.
        if (Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW") == "1")
        {
            switch (Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW_SCREEN"))
            {
                case "login":
                    currentViewModel = _login;
                    break;
                case "verify":
                    _login.Email = "you@mail.ru";
                    _login.OtpSent = true;
                    currentViewModel = _login;
                    break;
            }
        }
    }

    public async Task InitializeAsync()
    {
        if (CurrentViewModel == _shell) await _shell.LoadAsync();
    }

    private async void OnLoggedIn()
    {
        CurrentViewModel = _shell;
        await _shell.LoadAsync();
    }

    /// <summary>Сессия кабинета истекла и не продлилась (ApiClient.SessionExpired) — на экран
    /// входа с объяснением. VPN не трогаем: туннель от сессии кабинета не зависит, и обрывать
    /// работающее соединение из-за этого незачем. В превью-экземпляре — ничего: он не должен
    /// стирать настоящую сохранённую сессию.</summary>
    public void HandleSessionExpired()
    {
        if (CurrentViewModel != _shell || Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW") == "1") return;
        _tokenStore.Clear();
        _login.Reset();
        _login.ErrorMessage = "Сессия истекла — войдите снова. VPN при этом продолжает работать.";
        CurrentViewModel = _login;
    }

    private void OnLoggedOut()
    {
        // LoginViewModel — один и тот же экземпляр на всё время жизни процесса (не
        // пересоздаётся при выходе), поэтому если пользователь на прошлом заходе успел
        // переключиться на email-форму (EmailMode=true), после разлогина экран молча
        // показывал бы её вместо стартовых кнопок OAuth — сбрасываем состояние явно.
        _login.Reset();
        CurrentViewModel = _login;
    }
}
