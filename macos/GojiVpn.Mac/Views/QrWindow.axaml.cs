using Avalonia.Controls;
using Avalonia.Threading;
using GodjiVpn.Utils;

namespace GodjiVpn.Views;

public partial class QrWindow : Window
{
    public QrWindow() => InitializeComponent();

    /// <param name="warning">Красное предупреждение под кодом (код входа).</param>
    /// <param name="autoClose">Через сколько окно закрывается само.</param>
    public QrWindow(string title, string subtitle, string content, string? caption = null,
        string? warning = null, TimeSpan? autoClose = null) : this()
    {
        Title = title;
        TitleBlock.Text = title;
        SubtitleBlock.Text = subtitle;
        var modules = QrCode.Encode(content);
        QrImage.Source = QrCode.ToImage(modules, quietZone: 1);
        // Плотный код на экране компьютера — крупнее, чтобы камера телефона его читала.
        var side = modules.GetLength(0) > 60 ? 340 : 236;
        QrBox.Width = QrBox.Height = side;
        Width = side + 144;
        CaptionBlock.Text = caption ?? "";
        CaptionBlock.IsVisible = !string.IsNullOrEmpty(caption);
        if (!string.IsNullOrEmpty(warning))
        {
            WarningBlock.Text = warning;
            WarningBox.IsVisible = true;
        }
        CloseBtn.Click += (_, _) => Close();
        if (autoClose is { } delay)
        {
            var timer = new DispatcherTimer { Interval = delay };
            timer.Tick += (_, _) => { timer.Stop(); Close(); };
            timer.Start();
            Closed += (_, _) => timer.Stop();
        }
    }
}
