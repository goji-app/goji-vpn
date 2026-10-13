using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using GodjiVpn.ViewModels;

namespace GodjiVpn.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            // Экран кода может оказаться открыт уже при загрузке View — фокус в первую клетку.
            if (DataContext is LoginViewModel { OtpSent: true }) Otp.FocusFirst();
            await Globe.SetStatusAsync("off");
        };

        // Автопроверка по заполнению всех 6 клеток (см. Controls/OtpInput.Completed) и
        // автофокус первой клетки, когда экран кода становится видимым — DataContext
        // появляется позже конструктора (устанавливается MainViewModel), поэтому подписка
        // идёт через DataContextChanged, а не напрямую здесь.
        Otp.Completed += () =>
        {
            if (DataContext is LoginViewModel vm && vm.VerifyCommand.CanExecute(null))
                vm.VerifyCommand.Execute(null);
        };
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is LoginViewModel oldVm) oldVm.PropertyChanged -= OnViewModelPropertyChanged;
            if (e.NewValue is LoginViewModel newVm) newVm.PropertyChanged += OnViewModelPropertyChanged;
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LoginViewModel.OtpSent)) return;
        if (sender is LoginViewModel { OtpSent: true }) Dispatcher.BeginInvoke(() => Otp.FocusFirst());
    }

    private void OnTermsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm) vm.OpenTermsCommand.Execute(null);
    }

    private void OnPrivacyClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm) vm.OpenPrivacyCommand.Execute(null);
    }
}
