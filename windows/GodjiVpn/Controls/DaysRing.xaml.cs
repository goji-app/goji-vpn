using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GodjiVpn.Controls;

/// <summary>Кольцевой индикатор "осталось N дней" — аналог DaysRing в PlansScreen.kt v5
/// (дуга Teal по дорожке TrackBg, доля = daysLeft/30, ограничена [0,1]).</summary>
public partial class DaysRing : UserControl
{
    public static readonly DependencyProperty DaysProperty = DependencyProperty.Register(
        nameof(Days), typeof(int), typeof(DaysRing),
        new PropertyMetadata(0, OnDaysChanged));

    public int Days
    {
        get => (int)GetValue(DaysProperty);
        set => SetValue(DaysProperty, value);
    }

    public DaysRing()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Redraw();
    }

    private static void OnDaysChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DaysRing)d).Redraw();

    private void Redraw()
    {
        DaysText.Text = Days.ToString();

        var fraction = Math.Clamp(Days / 30.0, 0.0, 1.0);
        var w = ActualWidth > 0 ? ActualWidth : Width;
        var h = ActualHeight > 0 ? ActualHeight : Height;
        if (w <= 0 || h <= 0) return;

        var center = new Point(w / 2, h / 2);
        var radius = Math.Min(w, h) / 2 - 3.5;

        if (fraction <= 0.001)
        {
            ArcPath.Data = null;
            return;
        }

        // Полный круг (fraction≈1) не рисуется одним ArcSegment (start==end вырождает дугу) —
        // рисуем его как два полукруга, чтобы не терять сегмент при daysLeft>=30.
        if (fraction >= 0.999)
        {
            var top = new Point(center.X, center.Y - radius);
            var bottom = new Point(center.X, center.Y + radius);
            var figure = new PathFigure { StartPoint = top, IsClosed = false };
            figure.Segments.Add(new ArcSegment(bottom, new Size(radius, radius), 0, true, SweepDirection.Clockwise, true));
            figure.Segments.Add(new ArcSegment(top, new Size(radius, radius), 0, true, SweepDirection.Clockwise, true));
            ArcPath.Data = new PathGeometry([figure]);
            return;
        }

        var startAngle = -90.0;
        var endAngle = startAngle + fraction * 360.0;
        var start = PointOnCircle(center, radius, startAngle);
        var end = PointOnCircle(center, radius, endAngle);
        var isLargeArc = fraction > 0.5;

        var pathFigure = new PathFigure { StartPoint = start, IsClosed = false };
        pathFigure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, isLargeArc, SweepDirection.Clockwise, true));
        ArcPath.Data = new PathGeometry([pathFigure]);
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var rad = angleDegrees * Math.PI / 180.0;
        return new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
    }
}
