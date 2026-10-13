using System.Collections.Specialized;
using System.Windows.Controls;
using GodjiVpn.ViewModels;

namespace GodjiVpn.Views;

public partial class TicketChatView : UserControl
{
    public TicketChatView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is TicketChatViewModel oldVm) oldVm.Messages.CollectionChanged -= OnMessagesChanged;
            if (e.NewValue is TicketChatViewModel newVm) newVm.Messages.CollectionChanged += OnMessagesChanged;
        };
    }

    // Автопрокрутка к последнему сообщению — как LaunchedEffect(state.messages.size) в
    // Android TicketChatScreen.kt.
    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        MessagesScroll.ScrollToEnd();
}
