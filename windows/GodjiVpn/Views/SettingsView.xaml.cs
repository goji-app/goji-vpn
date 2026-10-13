using System.Windows.Controls;

namespace GodjiVpn.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    /// <summary>Журнал открывается на последних строках — свежие события внизу, как в
    /// LogViewerDialog (Android).</summary>
    /// <summary>Enter в поле домена — то же, что "Добавить" (imeAction Done в Android).</summary>
    private void OnBypassKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter || DataContext is not ViewModels.SettingsViewModel vm) return;
        vm.AddBypassCommand.Execute(null);
        e.Handled = true;
    }

    private void OnLogTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box) box.ScrollToEnd();
    }
}
