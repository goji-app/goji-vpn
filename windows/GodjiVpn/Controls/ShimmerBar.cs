using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GodjiVpn.Controls;

/// <summary>Полоса прогресса 8px (трафик, загрузка обновления): дорожка TrackBg и плоская
/// заливка primary, плавно (0.9 с) догоняющая новое значение. «Goji Expressive» (Material 3
/// Expressive): бегущий блик v5 убран — заливки однотонные. Имя прежнее, чтобы не трогать
/// разметку.</summary>
public class ShimmerBar : UserControl
{
    private readonly Grid _track = new() { ClipToBounds = true };
    private readonly Border _fill = new() { HorizontalAlignment = HorizontalAlignment.Left };

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(ShimmerBar), new PropertyMetadata(0.0, (d, _) => ((ShimmerBar)d).Update(animate: true)));

    /// <summary>Доля 0..1.</summary>
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public ShimmerBar()
    {
        Height = 8;
        _track.SetResourceReference(Panel.BackgroundProperty, "TrackBgBrush");
        _fill.SetResourceReference(Border.BackgroundProperty, "TealBrush");
        _fill.CornerRadius = new CornerRadius(4);
        _fill.Width = 0;
        _track.Children.Add(_fill);
        Content = _track;
        SizeChanged += (_, _) =>
        {
            _track.Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), ActualHeight / 2, ActualHeight / 2);
            Update(animate: false);
        };
    }

    private void Update(bool animate)
    {
        var width = ActualWidth;
        if (width <= 0) return;
        var target = width * Math.Clamp(Value, 0, 1);
        if (animate)
        {
            _fill.BeginAnimation(WidthProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(900))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            });
        }
        else
        {
            _fill.BeginAnimation(WidthProperty, null);
            _fill.Width = target;
        }
    }
}
