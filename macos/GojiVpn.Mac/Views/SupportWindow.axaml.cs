using Avalonia.Controls;
using GodjiVpn.Services;
using GodjiVpn.ViewModels;

namespace GodjiVpn.Views;

public partial class SupportWindow : Window
{
    private readonly SupportShellViewModel? _vm;

    public SupportWindow() => InitializeComponent();

    public SupportWindow(ApiClient api) : this()
    {
        _vm = new SupportShellViewModel(api);
        DataContext = _vm;
        Opened += async (_, _) => await _vm.OpenAsync();
        Closing += (_, _) => _vm.Chat.Stop();
    }
}
