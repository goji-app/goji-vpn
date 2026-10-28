using System.Windows.Controls;

namespace GodjiVpn.Views;

public partial class OnboardingView : UserControl
{
    public OnboardingView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await Globe.SetStatusAsync("off");
    }
}
