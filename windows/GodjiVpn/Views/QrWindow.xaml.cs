using System.Windows;
using GodjiVpn.Utils;

namespace GodjiVpn.Views;

/// <summary>Окно с QR-кодом текста (см. QrWindow.xaml).</summary>
public partial class QrWindow : Window
{
    /// <param name="warning">Красное предупреждение под кодом (для кода входа).</param>
    /// <param name="autoClose">Через сколько окно закрывается само — код входа не должен
    /// "забыто" висеть на экране дольше срока своего действия.</param>
    public QrWindow(string title, string subtitle, string content, string? caption = null,
        string? warning = null, TimeSpan? autoClose = null)
    {
        InitializeComponent();
        Title = title;
        TitleBlock.Text = title;
        SubtitleBlock.Text = subtitle;
        var modules = QrCode.Encode(content);
        QrImage.Source = QrCode.ToImage(modules, quietZone: 1);
        // Плотный код (длинный текст — например, сессия для переноса входа) на экране компьютера
        // в 236 px выходит ~2 px на модуль — камере телефона тяжело; растим подложку и окно.
        var side = modules.GetLength(0) > 60 ? 340 : 236;
        QrBox.Width = QrBox.Height = side;
        Width = side + 144;
        CaptionBlock.Text = caption ?? "";
        CaptionBlock.Visibility = string.IsNullOrEmpty(caption) ? Visibility.Collapsed : Visibility.Visible;
        if (!string.IsNullOrEmpty(warning))
        {
            WarningBlock.Text = warning;
            WarningBox.Visibility = Visibility.Visible;
        }
        if (autoClose is { } delay)
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = delay };
            timer.Tick += (_, _) => { timer.Stop(); Close(); };
            timer.Start();
            Closed += (_, _) => timer.Stop();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
