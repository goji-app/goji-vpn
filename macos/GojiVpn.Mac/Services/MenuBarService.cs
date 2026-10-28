using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GodjiVpn.Utils;
using GodjiVpn.ViewModels;

namespace GodjiVpn.Services;

/// <summary>
/// Значок в строке меню macOS — аналог TrayIconService Windows-клиента: "Открыть",
/// "Подключиться: &lt;узел&gt;" / "Отключить VPN", "Выход"; подсказка — статус подключения.
/// </summary>
public sealed class MenuBarService : IDisposable
{
    private readonly TrayIcon _icon;
    private readonly NativeMenuItem _toggleItem;
    private readonly VpnEngine _engine;
    private readonly ConnectViewModel _connect;
    private readonly Window _window;

    // Значок показывает состояние без открытия окна: серый — отключено, жёлтая точка —
    // подключаемся, зелёная — подключено (Assets/Images/tray-*.png, из logo.png).
    private readonly WindowIcon _iconOff = MenuBarImage("tray-off.png");
    private readonly WindowIcon _iconConnecting = MenuBarImage("tray-connecting.png");
    private readonly WindowIcon _iconOn = MenuBarImage("tray-on.png");
    private WindowIcon? _shownIcon;

    private static WindowIcon MenuBarImage(string name)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://GojiVpn/Assets/Images/{name}"));
        return new WindowIcon(new Bitmap(stream));
    }

    public MenuBarService(VpnEngine engine, ConnectViewModel connect, Window window, Action exit)
    {
        _engine = engine;
        _connect = connect;
        _window = window;

        var openItem = new NativeMenuItem("Открыть Goji VPN");
        openItem.Click += (_, _) => ShowWindow();
        _toggleItem = new NativeMenuItem("Подключиться");
        _toggleItem.Click += async (_, _) =>
        {
            if (connect.ToggleCommand.CanExecute(null)) await connect.ToggleCommand.ExecuteAsync(null);
        };
        var exitItem = new NativeMenuItem("Выход");
        exitItem.Click += (_, _) => exit();

        _icon = new TrayIcon
        {
            Icon = _iconOff,
            ToolTipText = "Goji VPN — отключено",
            Menu = new NativeMenu { openItem, _toggleItem, new NativeMenuItemSeparator(), exitItem },
            IsVisible = true
        };
        _icon.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _icon });

        engine.PropertyChanged += (_, _) => Ui.Post(UpdateStatus);
        connect.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConnectViewModel.CurrentNodeName)) Ui.Post(UpdateStatus);
        };
        UpdateStatus();
    }

    public void ShowWindow()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void UpdateStatus()
    {
        var node = _connect.CurrentNodeName;
        var icon = _engine.IsRunning ? _iconOn : _engine.IsConnecting ? _iconConnecting : _iconOff;
        if (!ReferenceEquals(_shownIcon, icon)) _icon.Icon = _shownIcon = icon;
        _icon.ToolTipText = _engine.IsRunning ? $"Goji VPN — подключено: {node}"
            : _engine.IsConnecting ? $"Goji VPN — подключаемся: {node}"
            : "Goji VPN — отключено";
        _toggleItem.Header = _engine.IsRunning ? "Отключить VPN"
            : _engine.IsConnecting ? $"Подключаемся: {node}"
            : $"Подключиться: {node}";
        _toggleItem.IsEnabled = !_engine.IsConnecting;
    }

    public void Dispose() => _icon.Dispose();
}
