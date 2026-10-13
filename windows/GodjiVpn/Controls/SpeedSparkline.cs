using System.Windows;
using System.Windows.Media;

namespace GodjiVpn.Controls;

/// <summary>
/// Мини-график скорости на плитке «Скорость сейчас» — порт Android SpeedSparkline
/// (ConnectScreen.kt, 1.0.117): сглаженная линия скачивания акцентом с лёгкой заливкой, отдача —
/// тонкой тёплой линией. Без данных (не подключено) — пунктирная базовая линия.
/// </summary>
public class SpeedSparkline : FrameworkElement
{
    public static readonly DependencyProperty DownProperty = DependencyProperty.Register(
        nameof(Down), typeof(IReadOnlyList<double>), typeof(SpeedSparkline),
        new FrameworkPropertyMetadata(Array.Empty<double>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double> Down { get => (IReadOnlyList<double>)GetValue(DownProperty); set => SetValue(DownProperty, value); }

    public static readonly DependencyProperty UpProperty = DependencyProperty.Register(
        nameof(Up), typeof(IReadOnlyList<double>), typeof(SpeedSparkline),
        new FrameworkPropertyMetadata(Array.Empty<double>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double> Up { get => (IReadOnlyList<double>)GetValue(UpProperty); set => SetValue(UpProperty, value); }

    public SpeedSparkline()
    {
        IsHitTestVisible = false;
        // Смена темы — перерисовать новыми цветами.
        if (Services.ThemeService.Current != null) Services.ThemeService.Current.Changed += InvalidateVisual;
    }

    private Color C(string key, Color fallback) => TryFindResource(key) is Color c ? c : fallback;

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var accent = C("PrimaryColor", Color.FromRgb(0x00, 0x6A, 0x62));
        var warm = C("TertiaryColor", Color.FromRgb(0x9A, 0x45, 0x24));
        var muted = C("OutlineVariantColor", Color.FromRgb(0xBE, 0xC9, 0xC6));
        const double stroke = 2.5;
        const double top = stroke;
        var bottom = h - stroke;

        var down = Down ?? Array.Empty<double>();
        var up = Up ?? Array.Empty<double>();
        if (down.Count < 2)
        {
            var dash = new Pen(new SolidColorBrush(muted), 2) { DashStyle = new DashStyle(new double[] { 3, 4 }, 0), DashCap = PenLineCap.Round };
            dash.Freeze();
            dc.DrawLine(dash, new Point(0, bottom), new Point(w, bottom));
            return;
        }

        var max = Math.Max(Math.Max(down.Max(), up.Count > 0 ? up.Max() : 0), 0.05);

        StreamGeometry Line(IReadOnlyList<double> values, bool closeToBottom)
        {
            var step = w / (values.Count - 1);
            var pts = values.Select((v, i) => new Point(i * step, bottom - v / max * (bottom - top))).ToList();
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(pts[0], closeToBottom, closeToBottom);
                for (var i = 1; i < pts.Count; i++)
                {
                    var mid = new Point((pts[i - 1].X + pts[i].X) / 2, (pts[i - 1].Y + pts[i].Y) / 2);
                    ctx.QuadraticBezierTo(pts[i - 1], mid, true, true);
                }
                ctx.LineTo(pts[^1], true, true);
                if (closeToBottom)
                {
                    ctx.LineTo(new Point(w, h), false, false);
                    ctx.LineTo(new Point(0, h), false, false);
                }
            }
            g.Freeze();
            return g;
        }

        if (up.Count >= 2)
        {
            var upPen = new Pen(new SolidColorBrush(Color.FromArgb(178, warm.R, warm.G, warm.B)), 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            upPen.Freeze();
            dc.DrawGeometry(null, upPen, Line(up, false));
        }

        var fill = new LinearGradientBrush(Color.FromArgb(56, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B), 90);
        fill.Freeze();
        dc.DrawGeometry(fill, null, Line(down, true));
        var pen = new Pen(new SolidColorBrush(accent), stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, Line(down, false));
    }
}
