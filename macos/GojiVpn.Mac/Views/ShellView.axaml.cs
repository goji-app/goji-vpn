using Avalonia.Controls;
using Avalonia.Interactivity;
using GodjiVpn.ViewModels;

namespace GodjiVpn.Views;

public partial class ShellView : UserControl
{
    public ShellView() => InitializeComponent();

    private void OnTabClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel vm && sender is Control { Tag: string tag } && int.TryParse(tag, out var index))
            vm.SelectedTabIndex = index;
    }
}
