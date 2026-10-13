using CommunityToolkit.Mvvm.ComponentModel;
using GodjiVpn.Services;

namespace GodjiVpn.ViewModels;

/// <summary>Аналог MainViewModel — переключает содержимое SupportWindow между списком
/// обращений/созданием тикета/чатом/FAQ; какая View показывается, решают DataTemplate'ы в
/// App.xaml по типу CurrentViewModel (тот же приём, что Login↔Shell в MainViewModel).</summary>
public sealed partial class SupportShellViewModel : ObservableObject
{
    public SupportListViewModel List { get; }
    public NewTicketViewModel NewTicket { get; }
    public TicketChatViewModel Chat { get; }
    public FaqViewModel Faq { get; }

    [ObservableProperty] private object currentViewModel;

    public SupportShellViewModel(ApiClient api)
    {
        List = new SupportListViewModel(api);
        NewTicket = new NewTicketViewModel(api);
        Chat = new TicketChatViewModel(api);
        Faq = new FaqViewModel(api);
        currentViewModel = List;

        List.TicketOpened += id => _ = OpenChatAsync(id);
        List.NewTicketRequested += () => _ = OpenNewTicketAsync();
        List.FaqRequested += () => _ = OpenFaqAsync();

        NewTicket.Created += id => _ = OpenChatAsync(id);
        NewTicket.OpenActiveTicketRequested += id => _ = OpenChatAsync(id);
        NewTicket.BackRequested += ShowList;

        Chat.BackRequested += ShowList;
        Faq.BackRequested += ShowList;
    }

    /// <summary>Вызывается при каждом открытии SupportWindow — список обращений всегда
    /// загружается заново (кэша нет нигде в этой фиче, как и в Android, см. отчёт по 720f5ff).</summary>
    public async Task OpenAsync()
    {
        Chat.Stop();
        CurrentViewModel = List;
        await List.LoadAsync();
    }

    private void ShowList()
    {
        Chat.Stop();
        CurrentViewModel = List;
        _ = List.LoadAsync();
    }

    private async Task OpenChatAsync(long ticketId)
    {
        CurrentViewModel = Chat;
        await Chat.StartAsync(ticketId);
    }

    private async Task OpenNewTicketAsync()
    {
        CurrentViewModel = NewTicket;
        await NewTicket.LoadAsync();
    }

    private async Task OpenFaqAsync()
    {
        CurrentViewModel = Faq;
        await Faq.LoadAsync();
    }
}
