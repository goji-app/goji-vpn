using Avalonia.Controls;
using Avalonia.Interactivity;
using GodjiVpn.ViewModels;

namespace GodjiVpn.Views;

public partial class SupportListView : UserControl
{
    public SupportListView() => InitializeComponent();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => (TopLevel.GetTopLevel(this) as Window)?.Close();

    private void OnTicketClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SupportListViewModel vm && sender is Control { Tag: TicketListItem item })
            vm.OpenTicketCommand.Execute(item);
    }
}
