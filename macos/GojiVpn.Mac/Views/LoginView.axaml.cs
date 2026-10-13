using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using GodjiVpn.ViewModels;

namespace GodjiVpn.Views;

public partial class LoginView : UserControl
{
    private LoginViewModel? _vm;

    public LoginView()
    {
        InitializeComponent();
        // Автопроверка по заполнению всех 6 клеток.
        Otp.Completed += () =>
        {
            if (_vm?.VerifyCommand.CanExecute(null) == true) _vm.VerifyCommand.Execute(null);
        };
        EmailBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _vm?.SendOtpCommand.CanExecute(null) == true) _vm.SendOtpCommand.Execute(null);
        };
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm = DataContext as LoginViewModel;
            if (_vm != null) _vm.PropertyChanged += OnVmPropertyChanged;
        };
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoginViewModel.OtpSent) && _vm?.OtpSent == true)
            Dispatcher.UIThread.Post(() => Otp.FocusFirst(), DispatcherPriority.Background);
    }
}
