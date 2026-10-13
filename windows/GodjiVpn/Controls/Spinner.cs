using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace GodjiVpn.Controls;

/// <summary>Маленький круговой индикатор загрузки — аналог CircularProgressIndicator
/// (strokeWidth 2, дорожка TrackBg) из Android. Цвет дуги — Foreground.</summary>
public class Spinner : UserControl
{
    private readonly Ellipse _track = new();
    private readonly Path _arc = new();
    private readonly RotateTransform _rotate = new();

    public Spinner()
    {
        Width = 16;
        Height = 16;
        IsHitTestVisible = false;
        _track.StrokeThickness = 2;
        _track.SetResourceReference(Shape.StrokeProperty, "TrackBgBrush");
        _arc.StrokeThickness = 2;
        _arc.StrokeStartLineCap = PenLineCap.Round;
        _arc.StrokeEndLineCap = PenLineCap.Round;
        _arc.RenderTransformOrigin = new Point(0.5, 0.5);
        _arc.RenderTransform = _rotate;
        _arc.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding(nameof(Foreground)) { Source = this });
        var grid = new Grid();
        grid.Children.Add(_track);
        grid.Children.Add(_arc);
        Content = grid;
        SizeChanged += (_, _) => BuildArc();
        IsVisibleChanged += (_, _) => UpdateAnimation();
        Loaded += (_, _) => { BuildArc(); UpdateAnimation(); };
    }

    private void BuildArc()
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var r = size / 2 - 1;
        var c = size / 2;
        Point P(double deg) => new(c + r * Math.Cos(deg * Math.PI / 180), c + r * Math.Sin(deg * Math.PI / 180));
        var figure = new PathFigure { StartPoint = P(-90), IsClosed = false };
        figure.Segments.Add(new ArcSegment(P(180), new Size(r, r), 0, true, SweepDirection.Clockwise, true));
        _arc.Data = new PathGeometry(new[] { figure });
    }

    private void UpdateAnimation()
    {
        if (IsVisible)
        {
            var spin = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900)) { RepeatBehavior = RepeatBehavior.Forever };
            _rotate.BeginAnimation(RotateTransform.AngleProperty, spin);
        }
        else
        {
            _rotate.BeginAnimation(RotateTransform.AngleProperty, null);
        }
    }
}
