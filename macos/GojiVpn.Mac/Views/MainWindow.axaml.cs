using Avalonia.Controls;

namespace GodjiVpn.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Utils.MemoryTrim.Attach(this);
    }
}
