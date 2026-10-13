using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace GodjiVpn.Controls;

/// <summary>Индикатор загрузки 16px: дуга 270° цвета Foreground поверх дорожки TrackBg,
/// вращается от общих часов (оборот за 0,9 с) — порт Spinner Windows-клиента.</summary>
public class Spinner : AnimatedControl
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<Spinner, IBrush?>(nameof(Foreground));

    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public Spinner()
    {
        Width = 16;
        Height = 16;
        IsHitTestVisible = false;
    }

    protected override bool IsContinuous => true;

    public override void Render(DrawingContext ctx)
    {
        if (!IsEffectivelyVisible) return;
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        var c = size / 2;
        var r = size / 2 - 1;
        if (this.TryFindResource("TrackBgBrush", ActualThemeVariant, out var t) && t is IBrush track)
            ctx.DrawEllipse(null, new Pen(track, 2), new Point(c, c), r, r);

        var brush = Foreground ??
                    (this.TryFindResource("TealBrush", ActualThemeVariant, out var b) && b is IBrush teal ? teal : Brushes.Teal);
        var start = Seconds / 0.9 * 360 % 360 - 90;
        Point P(double deg) => new(c + r * Math.Cos(deg * Math.PI / 180), c + r * Math.Sin(deg * Math.PI / 180));
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(P(start), false);
            g.ArcTo(P(start + 270), new Size(r, r), 0, true, SweepDirection.Clockwise);
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(brush, 2, lineCap: PenLineCap.Round), geometry);
    }
}
