using System.Windows;
using System.Windows.Controls;
using GodjiVpn.ViewModels;

namespace GodjiVpn.Views;

public partial class SupportListView : UserControl
{
    public SupportListView()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Window.GetWindow(this)?.Close();

    private void OnTicketClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is SupportListViewModel vm && sender is FrameworkElement { Tag: TicketListItem item })
            vm.OpenTicketCommand.Execute(item);
    }
}
