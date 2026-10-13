using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace GodjiVpn.Controls;

/// <summary>
/// Пять полосок-индикатор остатка подписки, как индикатор сигнала (плитка «Подписка» на
/// «Главной», порт Android SubscriptionTile, b3c3350): ширина 5, высоты 8…16, промежуток 3.
/// Filled — сколько заполнено; Level — ok (primary), warm (меньше недели — tertiary) или danger
/// (меньше трёх дней — error); остальные — TrackBg.
/// </summary>
public class SignalBars : StackPanel
{
    private readonly Rectangle[] _bars = new Rectangle[5];

    public static readonly DependencyProperty FilledProperty = DependencyProperty.Register(
        nameof(Filled), typeof(int), typeof(SignalBars), new PropertyMetadata(0, (d, _) => ((SignalBars)d).Update()));

    public int Filled { get => (int)GetValue(FilledProperty); set => SetValue(FilledProperty, value); }

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(string), typeof(SignalBars), new PropertyMetadata("ok", (d, _) => ((SignalBars)d).Update()));

    public string Level { get => (string)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }

    public SignalBars()
    {
        Orientation = Orientation.Horizontal;
        for (var i = 0; i < _bars.Length; i++)
        {
            _bars[i] = new Rectangle
            {
                Width = 5,
                Height = 8 + i * 2,
                RadiusX = 2.5,
                RadiusY = 2.5,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0)
            };
            Children.Add(_bars[i]);
        }
        Update();
    }

    private void Update()
    {
        var on = Level switch { "danger" => "DangerBrush", "warm" => "TerracottaBrush", _ => "TealBrush" };
        for (var i = 0; i < _bars.Length; i++)
            _bars[i].SetResourceReference(Shape.FillProperty, i < Filled ? on : "TrackBgBrush");
    }
}
