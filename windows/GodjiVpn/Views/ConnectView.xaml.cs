using System.ComponentModel;
using System.Windows.Controls;
using GodjiVpn.ViewModels;

namespace GodjiVpn.Views;

public partial class ConnectView : UserControl
{
    public ConnectView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ConnectViewModel oldVm) oldVm.PropertyChanged -= OnVmPropertyChanged;
        if (e.NewValue is ConnectViewModel newVm)
        {
            newVm.PropertyChanged += OnVmPropertyChanged;
            _ = SyncGlobeAsync(newVm);
        }
    }

    private async void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ConnectViewModel vm) return;
        if (e.PropertyName is nameof(ConnectViewModel.GlobeStatus) or nameof(ConnectViewModel.GlobeGeo))
            await SyncGlobeAsync(vm);
    }

    private async Task SyncGlobeAsync(ConnectViewModel vm)
    {
        await Globe.SetStatusAsync(vm.GlobeStatus);
        if (vm.GlobeGeo is { } geo)
            await Globe.SetNodeAsync(geo.Lat, geo.Lon, geo.Country, geo.City, geo.RuName);
    }
}
