using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using GodjiVpn.ViewModels;

namespace GodjiVpn.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        // Enter в поле домена — то же, что "Добавить".
        BypassBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || DataContext is not SettingsViewModel vm) return;
            vm.AddBypassCommand.Execute(null);
            e.Handled = true;
        };
        // Журнал открывается на последних строках.
        LogBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                Dispatcher.UIThread.Post(() => LogBox.CaretIndex = LogBox.Text?.Length ?? 0, DispatcherPriority.Background);
        };
    }
}
