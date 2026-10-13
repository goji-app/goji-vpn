using System.ComponentModel;
using System.Windows;
using GodjiVpn.Services;
using GodjiVpn.ViewModels;

namespace GodjiVpn.Views;

/// <summary>Открывается модально из PlansViewModel/SettingsViewModel (см. OpenSupportCommand в
/// обоих) — своя ApiClient-зависимая мини-MVVM-иерархия внутри одного окна, а не часть
/// композиционного корня App.xaml.cs: кэша между открытиями нет (как и в Android, см. отчёт по
/// 720f5ff), поэтому пересоздание SupportShellViewModel при каждом открытии ничего не теряет.</summary>
public partial class SupportWindow : Window
{
    private readonly SupportShellViewModel _vm;

    public SupportWindow(ApiClient api)
    {
        InitializeComponent();
        _vm = new SupportShellViewModel(api);
        DataContext = _vm;
        Loaded += async (_, _) => await _vm.OpenAsync();
    }

    private void OnClosing(object? sender, CancelEventArgs e) => _vm.Chat.Stop();
}
