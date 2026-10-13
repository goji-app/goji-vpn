using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace GodjiVpn.Controls;

/// <summary>
/// Мини-график скорости на плитке «Скорость сейчас» — порт Android SpeedSparkline (ConnectScreen.kt,
/// 1.0.117), как в Windows-клиенте: сглаженная линия скачивания акцентом с лёгкой заливкой, отдача —
/// тонкой тёплой линией. Без данных (не подключено) — пунктирная базовая линия.
/// </summary>
public class SpeedSparkline : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>> DownProperty =
        AvaloniaProperty.Register<SpeedSparkline, IReadOnlyList<double>>(nameof(Down), Array.Empty<double>());

    public static readonly StyledProperty<IReadOnlyList<double>> UpProperty =
        AvaloniaProperty.Register<SpeedSparkline, IReadOnlyList<double>>(nameof(Up), Array.Empty<double>());

    public IReadOnlyList<double> Down { get => GetValue(DownProperty); set => SetValue(DownProperty, value); }
    public IReadOnlyList<double> Up { get => GetValue(UpProperty); set => SetValue(UpProperty, value); }

    static SpeedSparkline() => AffectsRender<SpeedSparkline>(DownProperty, UpProperty);

    public SpeedSparkline()
    {
        IsHitTestVisible = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    private Color C(string key, Color fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var v) && v is Color c ? c : fallback;

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var accent = C("PrimaryColor", Color.Parse("#006A62"));
        var warm = C("TertiaryColor", Color.Parse("#9A4524"));
        var muted = C("OutlineVariantColor", Color.Parse("#BEC9C6"));
        const double stroke = 2.5;
        const double top = stroke;
        var bottom = h - stroke;

        var down = Down ?? Array.Empty<double>();
        var up = Up ?? Array.Empty<double>();
        if (down.Count < 2)
        {
            ctx.DrawLine(new Pen(new SolidColorBrush(muted), 2, new DashStyle(new double[] { 3, 4 }, 0), PenLineCap.Round),
                new Point(0, bottom), new Point(w, bottom));
            return;
        }

        var max = Math.Max(Math.Max(down.Max(), up.Count > 0 ? up.Max() : 0), 0.05);

        StreamGeometry Line(IReadOnlyList<double> values, bool closeToBottom)
        {
            var step = w / (values.Count - 1);
            var pts = values.Select((v, i) => new Point(i * step, bottom - v / max * (bottom - top))).ToList();
            var g = new StreamGeometry();
            using (var s = g.Open())
            {
                s.BeginFigure(pts[0], closeToBottom);
                for (var i = 1; i < pts.Count; i++)
                {
                    var mid = new Point((pts[i - 1].X + pts[i].X) / 2, (pts[i - 1].Y + pts[i].Y) / 2);
                    s.QuadraticBezierTo(pts[i - 1], mid);
                }
                s.LineTo(pts[^1]);
                if (closeToBottom)
                {
                    s.LineTo(new Point(w, h));
                    s.LineTo(new Point(0, h));
                }
                s.EndFigure(closeToBottom);
            }
            return g;
        }

        if (up.Count >= 2)
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(178, warm.R, warm.G, warm.B)), 1.5, lineCap: PenLineCap.Round), Line(up, false));

        var fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(56, accent.R, accent.G, accent.B), 0),
                new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1)
            }
        };
        ctx.DrawGeometry(fill, null, Line(down, true));
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(accent), stroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), Line(down, false));
    }
}

/// <summary>
/// Пять полосок-индикатор остатка подписки, как индикатор сигнала (плитка «Подписка», порт Android
/// SubscriptionTile, b3c3350): ширина 5, высоты 8…16, промежуток 3. Filled — сколько заполнено;
/// Level — ok (primary), warm (меньше недели — tertiary) или danger (меньше трёх дней — error).
/// </summary>
public class SignalBars : StackPanel
{
    public static readonly StyledProperty<int> FilledProperty = AvaloniaProperty.Register<SignalBars, int>(nameof(Filled));
    public static readonly StyledProperty<string> LevelProperty = AvaloniaProperty.Register<SignalBars, string>(nameof(Level), "ok");

    public int Filled { get => GetValue(FilledProperty); set => SetValue(FilledProperty, value); }
    public string Level { get => GetValue(LevelProperty); set => SetValue(LevelProperty, value); }

    private readonly Border[] _bars = new Border[5];

    public SignalBars()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 3;
        for (var i = 0; i < _bars.Length; i++)
        {
            _bars[i] = new Border { Width = 5, Height = 8 + i * 2, CornerRadius = new CornerRadius(2.5), VerticalAlignment = VerticalAlignment.Bottom };
            Children.Add(_bars[i]);
        }
        Update();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FilledProperty || change.Property == LevelProperty) Update();
    }

    private void Update()
    {
        var on = Level switch { "danger" => "DangerBrush", "warm" => "TerracottaBrush", _ => "TealBrush" };
        for (var i = 0; i < _bars.Length; i++)
            _bars[i][!Border.BackgroundProperty] = new DynamicResourceExtension(i < Filled ? on : "TrackBgBrush");
    }
}
