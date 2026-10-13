using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GodjiVpn.Services;
using GodjiVpn.Utils;
using GodjiVpn.Views;

namespace GodjiVpn.ViewModels;

/// <summary>Аналог LoginViewModel.kt/AuthRepository.kt (Android) — email+OTP и вход через сайт
/// (см. OpenWebLoginAsync). Раньше здесь были отдельные PKCE-команды для Google/Yandex/Telegram
/// (native-exchange OAuth) — убраны целиком: на бэкенде 7.1.0 api/auth/native/exchange отвечает
/// 400 на любой запрос независимо от провайдера, чинить было нечего, только заменять механизм.</summary>
public sealed partial class LoginViewModel : ObservableObject
{
    private const string BotUrl = "https://t.me/Shadow_Duck_bot";
    private const string TermsUrl = "https://telegra.ph/Polzovatelskoe-soglashenie-Goji-VPN-09-20";
    private const string PrivacyUrl = "https://telegra.ph/Politika-konfidencialnosti-Goji-VPN-09-20";

    private readonly ApiClient _api;
    private readonly TokenStore _tokenStore;

    public event Action? LoggedIn;

    [ObservableProperty] private bool emailMode;
    [ObservableProperty] private string email = "";
    [ObservableProperty] private string code = "";
    [ObservableProperty] private bool otpSent;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string? infoMessage;
    /// <summary>Полноэкранный успех (галочка) после верного кода, перед переходом дальше —
    /// см. VerifyAsync/LoginView.xaml. Порт из Android (VerifySuccessBadge).</summary>
    [ObservableProperty] private bool isVerifySuccess;

    public LoginViewModel(ApiClient api, TokenStore tokenStore)
    {
        _api = api;
        _tokenStore = tokenStore;
    }

    [RelayCommand]
    private void ShowEmailForm() => EmailMode = true;

    [RelayCommand]
    private void ShowOAuthOptions()
    {
        EmailMode = false;
        OtpSent = false;
        ErrorMessage = null;
        InfoMessage = null;
    }

    [RelayCommand(CanExecute = nameof(CanSendOtp))]
    private async Task SendOtpAsync()
    {
        ErrorMessage = null;
        InfoMessage = null;
        IsBusy = true;
        try
        {
            await _api.SendOtpAsync(Email.Trim());
            OtpSent = true;
            InfoMessage = $"Код отправлен на {Email.Trim()}";
        }
        catch (Exception ex)
        {
            ErrorMessage = "Не удалось отправить код: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSendOtp() => !IsBusy && Email.Contains('@');

    /// <summary>Автопроверка сразу по вводу 6-й цифры (см. Controls/OtpInput.Completed,
    /// подключено в LoginView.xaml.cs) — "It'll auto-verify once entered" из референса
    /// редизайна. Явной кнопки "Войти" больше нет. При верном коде — короткий полноэкранный
    /// успех (см. IsVerifySuccess), при неверном — код очищается (пустые клетки читаются как
    /// явное приглашение ввести код ещё раз, а не как забытые чужие цифры поверх которых
    /// печатать).</summary>
    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var (_, token, refreshToken) = await _api.VerifyOtpAsync(Email.Trim(), Code.Trim());
            IsVerifySuccess = true;
            await Task.Delay(1300);
            await OnAuthenticatedAsync(token, refreshToken);
        }
        catch (Exception ex)
        {
            ErrorMessage = "Неверный код: " + ex.Message;
            Code = "";
            IsBusy = false;
        }
    }

    private bool CanVerify() => !IsBusy && OtpSent && Code.Trim().Length == 6;

    /// <summary>Вызывается при разлогине — экземпляр ViewModel переживает выход, см.
    /// MainViewModel.OnLoggedOut().</summary>
    public void Reset()
    {
        EmailMode = false;
        Email = "";
        Code = "";
        OtpSent = false;
        IsBusy = false;
        IsVerifySuccess = false;
        ErrorMessage = null;
        InfoMessage = null;
    }

    [RelayCommand]
    private void ChangeEmail()
    {
        OtpSent = false;
        Code = "";
        ErrorMessage = null;
        InfoMessage = null;
    }

    /// <summary>Открывает встроенное окно веб-входа (см. Views/WebLoginWindow) — показывает
    /// пользователю сам сайт целиком, ждёт, пока он там залогинится любым способом (Google/
    /// Яндекс/Telegram/email), и забирает сессионный токен из выставленной сайтом куки. Замена
    /// сломанного на бэкенде 7.1.0 native-exchange OAuth (см. заголовок класса), портировано
    /// с Android WebLoginActivity.kt.</summary>
    [RelayCommand]
    private async Task OpenWebLoginAsync()
    {
        ErrorMessage = null;
        var window = new WebLoginWindow { Owner = Application.Current.MainWindow };
        var completed = window.ShowDialog();
        if (completed != true || string.IsNullOrEmpty(window.SessionToken)) return;

        IsBusy = true;
        try
        {
            await OnAuthenticatedAsync(window.SessionToken, window.RefreshToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>refreshToken — rw_refresh_token, живёт намного дольше сессионного JWT (тот
    /// истекает ровно через 24ч, см. ApiClient.RefreshSessionAsync). Может отсутствовать —
    /// бэкенд не всегда выставляет её отдельно (например при повторном входе с уже валидной
    /// сессией) — тогда просто не будет автообновления, как раньше, ничего не падает.</summary>
    private async Task OnAuthenticatedAsync(string token, string? refreshToken = null)
    {
        _tokenStore.Save(token);
        if (refreshToken != null) _tokenStore.SaveRefreshToken(refreshToken);
        // Согласия на обработку данных — требуются один раз после первой регистрации. Не
        // блокируем вход, если запрос не удался — согласие можно принять позже.
        try
        {
            var me = await _api.GetMeAsync();
            if (me.ConsentRequired) await _api.ConsentAsync();
        }
        catch { /* не критично для входа */ }

        LoggedIn?.Invoke();
    }

    [RelayCommand]
    private void OpenBot() => OpenUrl(BotUrl);

    [RelayCommand]
    private void OpenTerms() => OpenUrl(TermsUrl);

    [RelayCommand]
    private void OpenPrivacy() => OpenUrl(PrivacyUrl);

    private static void OpenUrl(string url) => UrlLauncher.TryOpen(url);

    partial void OnEmailChanged(string value) => SendOtpCommand.NotifyCanExecuteChanged();
    partial void OnCodeChanged(string value) => VerifyCommand.NotifyCanExecuteChanged();
    partial void OnOtpSentChanged(bool value) => VerifyCommand.NotifyCanExecuteChanged();
    /// <summary>Как loginSendingOtp/loginGetCode в Android Strings.kt — во время отправки
    /// кода кнопка показывает "Отправляем…" вместо "Получить код".</summary>
    public string SendOtpButtonText => IsBusy ? "Отправляем…" : "Получить код";

    partial void OnIsBusyChanged(bool value)
    {
        SendOtpCommand.NotifyCanExecuteChanged();
        VerifyCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SendOtpButtonText));
    }
}
