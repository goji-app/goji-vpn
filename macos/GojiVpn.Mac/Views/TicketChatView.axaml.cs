using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using GodjiVpn.ViewModels;

namespace GodjiVpn.Views;

public partial class TicketChatView : UserControl
{
    private TicketChatViewModel? _vm;

    public TicketChatView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.Messages.CollectionChanged -= OnMessagesChanged;
            _vm = DataContext as TicketChatViewModel;
            if (_vm != null) _vm.Messages.CollectionChanged += OnMessagesChanged;
        };
    }

    // Автопрокрутка к последнему сообщению.
    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(() => MessagesScroll.ScrollToEnd(), DispatcherPriority.Background);
}
