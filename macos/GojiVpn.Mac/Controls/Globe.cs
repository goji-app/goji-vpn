using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;

namespace GodjiVpn.Controls;

/// <summary>
/// Нативный глобус macOS-клиента — замена three.js-компонента &lt;goji-globe&gt; Windows-клиента
/// (WebView2 на macOS нет) по тем же правилам эталона v5 (GLOBE.md): ортографическая проекция,
/// береговые линии и границы из Android-ассета geo_globe.json, сетка 15°, атмосфера; при
/// подключении/на связи — контур страны узла со свечением, маркеры дома и узла (у узла —
/// расходящиеся кольца), низкая дуга с потоком бусин; поворот к стране кратчайшим путём; при
/// выключенном VPN — медленное вращение. Спутники — только на экране входа (Satellites).
/// </summary>
public class Globe : AnimatedControl
{
    public static readonly StyledProperty<string> StatusProperty = AvaloniaProperty.Register<Globe, string>(nameof(Status), "off");
    public static readonly StyledProperty<double> NodeLatProperty = AvaloniaProperty.Register<Globe, double>(nameof(NodeLat), 60.17);
    public static readonly StyledProperty<double> NodeLonProperty = AvaloniaProperty.Register<Globe, double>(nameof(NodeLon), 24.94);
    public static readonly StyledProperty<string?> NodeCountryProperty = AvaloniaProperty.Register<Globe, string?>(nameof(NodeCountry), "Finland");
    public static readonly StyledProperty<bool> SatellitesProperty = AvaloniaProperty.Register<Globe, bool>(nameof(Satellites));
    public static readonly StyledProperty<double> RadiusFactorProperty = AvaloniaProperty.Register<Globe, double>(nameof(RadiusFactor), 0.36);

    public string Status { get => GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    public double NodeLat { get => GetValue(NodeLatProperty); set => SetValue(NodeLatProperty, value); }
    public double NodeLon { get => GetValue(NodeLonProperty); set => SetValue(NodeLonProperty, value); }
    public string? NodeCountry { get => GetValue(NodeCountryProperty); set => SetValue(NodeCountryProperty, value); }
    public bool Satellites { get => GetValue(SatellitesProperty); set => SetValue(SatellitesProperty, value); }
    /// <summary>Радиус глобуса как доля меньшей стороны контрола.</summary>
    public double RadiusFactor { get => GetValue(RadiusFactorProperty); set => SetValue(RadiusFactorProperty, value); }

    private const double HomeLat = 55.75, HomeLon = 37.62;

    // Глобус крутится, только пока его видно и окно в фокусе: в свёрнутом/неактивном окне и на
    // других вкладках он замирает (рисовать его каждый кадр — заметная нагрузка на процессор).
    protected override bool IsContinuous => true;

    protected override bool ShouldRender =>
        IsEffectivelyVisible && Avalonia.Controls.TopLevel.GetTopLevel(this) is Avalonia.Controls.Window w &&
        w.IsActive && w.WindowState != Avalonia.Controls.WindowState.Minimized;

    private sealed record Palette(Color Ocean, double OceanOp, Color Land, double LandOp, Color Grid, double GridOp,
        Color Hi, Color Arc, Color Home, Color Atmo);

    private static readonly Palette Light = new(C("#E7E0CF"), 1, C("#0F4D45"), 0.55, C("#0F4D45"), 0.06,
        C("#00897E"), C("#D9714B"), C("#D9714B"), C("#00A79B"));
    private static readonly Palette Dark = new(C("#0A201D"), 0.9, C("#2F6F66"), 0.75, C("#00D4C4"), 0.07,
        C("#00E7D4"), C("#00E7D4"), C("#8B7CF6"), C("#00D4C4"));

    private static Color C(string hex) => Color.Parse(hex);

    // ── геоданные ──
    private sealed class GeoData
    {
        public required double[][] Coast { get; init; }
        public required double[][] Borders { get; init; }
        public required Dictionary<string, double[][][]> Countries { get; init; }
    }

    private static readonly Lazy<GeoData?> Geo = new(LoadGeo);

    private static GeoData? LoadGeo()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://GojiVpn/Assets/Geo/geo_globe.json"));
            using var doc = JsonDocument.Parse(stream);
            double[][] Segs(string name) => doc.RootElement.GetProperty(name).EnumerateArray()
                .Select(s => s.EnumerateArray().Select(v => v.GetDouble()).ToArray()).ToArray();
            var countries = new Dictionary<string, double[][][]>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in doc.RootElement.GetProperty("countries").EnumerateObject())
                countries[c.Name] = c.Value.EnumerateArray()
                    .Select(ring => ring.EnumerateArray().Select(p => new[] { p[0].GetDouble(), p[1].GetDouble() }).ToArray()).ToArray();
            return new GeoData { Coast = Segs("coast"), Borders = Segs("borders"), Countries = countries };
        }
        catch { return null; }
    }

    // ── состояние вращения ──
    private double _rotY = -0.3, _rotX = 0.55;
    private double _lastTime = -1;
    private double _highlight;
    private (double Lat, double Lon)? _flightFrom;
    private double _flightStart;
    private readonly Sat[] _sats;

    private sealed record Sat(double Radius, double TiltX, double TiltZ, double Phase, double Speed, double BlinkPhase, bool Red);

    public Globe()
    {
        IsHitTestVisible = false;
        var rnd = new Random(7);
        _sats = Enumerable.Range(0, 16).Select(i => new Sat(
            1.16 + i % 4 * 0.07 + rnd.NextDouble() * 0.03,
            (rnd.NextDouble() - 0.5) * 2.2,
            (rnd.NextDouble() - 0.5) * 1.2,
            rnd.NextDouble() * Math.PI * 2,
            (0.15 + rnd.NextDouble() * 0.24) * (i % 3 == 0 ? -1 : 1),
            rnd.NextDouble() * 6,
            i % 3 != 0)).ToArray();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if ((change.Property == NodeLatProperty || change.Property == NodeLonProperty) && Status != "off")
        {
            // Перелёт от прежнего узла к новому (как e3012ab в Android).
            var oldLat = change.Property == NodeLatProperty ? (double)change.OldValue! : NodeLat;
            var oldLon = change.Property == NodeLonProperty ? (double)change.OldValue! : NodeLon;
            if (Math.Abs(oldLat - NodeLat) > 0.01 || Math.Abs(oldLon - NodeLon) > 0.01)
            {
                _flightFrom = (oldLat, oldLon);
                _flightStart = Seconds;
            }
        }
    }

    private static double Rad(double deg) => deg * Math.PI / 180;

    /// <summary>Точка сферы (lat/lon, высота h в радиусах) → экран; z &gt; 0 — видимая сторона.</summary>
    private (Point P, double Z) Project(double lat, double lon, double h, Point c, double r)
    {
        var phi = Rad(lat);
        var lam = Rad(lon) + _rotY;
        var x = Math.Cos(phi) * Math.Sin(lam);
        var y = Math.Sin(phi);
        var z = Math.Cos(phi) * Math.Cos(lam);
        var ct = Math.Cos(_rotX);
        var st = Math.Sin(_rotX);
        var y2 = y * ct - z * st;
        var z2 = y * st + z * ct;
        return (new Point(c.X + r * h * x, c.Y - r * h * y2), z2);
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var hgt = Bounds.Height;
        if (w <= 0 || hgt <= 0) return;
        var pal = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? Dark : Light;
        var r = Math.Min(w, hgt) * RadiusFactor;
        var c = new Point(w / 2, hgt / 2);
        var now = Seconds;
        var dt = _lastTime < 0 ? 0 : Math.Clamp(now - _lastTime, 0, 0.2);
        _lastTime = now;

        var status = Status;
        var on = status == "on";
        var connecting = status == "connecting";
        UpdateRotation(dt, on || connecting);
        _highlight += ((on || connecting ? 1 : 0) - _highlight) * Math.Min(1, dt * 4);

        // Атмосфера.
        ctx.DrawEllipse(new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(WithA(pal.Atmo, 0), 0.62),
                new GradientStop(WithA(pal.Atmo, on ? 0.20 : 0.12), 0.72),
                new GradientStop(WithA(pal.Atmo, 0), 1)
            }
        }, null, c, r * 1.35, r * 1.35);

        if (Satellites) DrawSatellites(ctx, c, r, pal, back: true);

        // Океан с объёмной подсветкой.
        ctx.DrawEllipse(new RadialGradientBrush
        {
            Center = new RelativePoint(0.35, 0.3, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.35, 0.3, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.85, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.85, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(WithA(Lighten(pal.Ocean, 0.08), pal.OceanOp), 0),
                new GradientStop(WithA(pal.Ocean, pal.OceanOp), 0.6),
                new GradientStop(WithA(Darken(pal.Ocean, 0.12), pal.OceanOp), 1)
            }
        }, null, c, r, r);

        // Сетка 15°.
        var gridPen = new Pen(new SolidColorBrush(WithA(pal.Grid, pal.GridOp)), 1);
        var grid = new StreamGeometry();
        using (var g = grid.Open())
        {
            for (var lat = -75; lat <= 75; lat += 15) AddPolyline(g, Enumerable.Range(0, 73).Select(i => (lat * 1.0, -180 + i * 5.0)), c, r);
            for (var lon = -180; lon < 180; lon += 15) AddPolyline(g, Enumerable.Range(0, 37).Select(i => (-90 + i * 5.0, lon * 1.0)), c, r);
        }
        ctx.DrawGeometry(null, gridPen, grid);

        if (Geo.Value is { } geo)
        {
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(WithA(pal.Land, pal.LandOp)), 1), Segments(geo.Coast, c, r));
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(WithA(pal.Land, pal.LandOp * 0.5)), 0.8), Segments(geo.Borders, c, r));

            if (_highlight > 0.02 && NodeCountry is { } country && geo.Countries.TryGetValue(country, out var rings))
            {
                var outline = new StreamGeometry();
                using (var g = outline.Open())
                    foreach (var ring in rings)
                        AddPolyline(g, ring.Select(p => (p[1], p[0])), c, r, 1.004);
                var blink = on ? 1 : 0.5 + Math.Sin(now * 4) * 0.3;
                ctx.DrawGeometry(null, new Pen(new SolidColorBrush(WithA(pal.Hi, (0.28 + Math.Sin(now * 2.2) * 0.08) * _highlight)), 4.5), outline);
                ctx.DrawGeometry(null, new Pen(new SolidColorBrush(WithA(pal.Hi, blink * _highlight)), 1.6), outline);
            }
        }

        if (on || connecting) DrawRoute(ctx, c, r, pal, on, now);
        DrawFlight(ctx, c, r, pal, now);

        // Кромка шара.
        ctx.DrawEllipse(null, new Pen(new SolidColorBrush(WithA(pal.Atmo, 0.25)), 1), c, r, r);

        if (Satellites) DrawSatellites(ctx, c, r, pal, back: false);
    }

    private void UpdateRotation(double dt, bool locked)
    {
        if (locked)
        {
            // Цель — узел по центру; угол по Y — кратчайшим путём.
            var targetY = -Rad(NodeLon);
            var targetX = Rad(NodeLat);
            var d = targetY - _rotY;
            d -= Math.Round(d / (Math.PI * 2)) * Math.PI * 2;
            var k = Math.Min(1, dt * 3);
            _rotY += d * k;
            _rotX += (targetX - _rotX) * k;
        }
        else
        {
            _rotY += dt * 0.08;
            _rotX += (0.55 - _rotX) * Math.Min(1, dt * 1.2);
        }
    }

    private StreamGeometry Segments(double[][] segs, Point c, double r)
    {
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        foreach (var s in segs)
        {
            var (a, za) = Project(s[1], s[0], 1, c, r);
            var (b, zb) = Project(s[3], s[2], 1, c, r);
            if (za <= 0 || zb <= 0) continue;
            g.BeginFigure(a, false);
            g.LineTo(b);
            g.EndFigure(false);
        }
        return geometry;
    }

    private void AddPolyline(StreamGeometryContext g, IEnumerable<(double Lat, double Lon)> points, Point c, double r, double h = 1)
    {
        var open = false;
        foreach (var (lat, lon) in points)
        {
            var (p, z) = Project(lat, lon, h, c, r);
            if (z <= 0)
            {
                if (open) { g.EndFigure(false); open = false; }
                continue;
            }
            if (!open) { g.BeginFigure(p, false); open = true; }
            else g.LineTo(p);
        }
        if (open) g.EndFigure(false);
    }

    /// <summary>Точка на дуге большого круга между a и b с подъёмом над поверхностью.</summary>
    private static (double Lat, double Lon, double H) ArcPoint((double Lat, double Lon) a, (double Lat, double Lon) b, double t, double lift)
    {
        static (double X, double Y, double Z) V(double lat, double lon) =>
            (Math.Cos(Rad(lat)) * Math.Sin(Rad(lon)), Math.Sin(Rad(lat)), Math.Cos(Rad(lat)) * Math.Cos(Rad(lon)));
        var va = V(a.Lat, a.Lon);
        var vb = V(b.Lat, b.Lon);
        var dot = Math.Clamp(va.X * vb.X + va.Y * vb.Y + va.Z * vb.Z, -1, 1);
        var omega = Math.Acos(dot);
        double x, y, z;
        if (omega < 1e-6) (x, y, z) = va;
        else
        {
            var s1 = Math.Sin((1 - t) * omega) / Math.Sin(omega);
            var s2 = Math.Sin(t * omega) / Math.Sin(omega);
            x = va.X * s1 + vb.X * s2; y = va.Y * s1 + vb.Y * s2; z = va.Z * s1 + vb.Z * s2;
        }
        var lat = Math.Asin(Math.Clamp(y, -1, 1)) * 180 / Math.PI;
        var lon = Math.Atan2(x, z) * 180 / Math.PI;
        return (lat, lon, 1 + lift * Math.Sin(Math.PI * t));
    }

    private void DrawRoute(DrawingContext ctx, Point c, double r, Palette pal, bool on, double now)
    {
        var home = (HomeLat, HomeLon);
        var node = (NodeLat, NodeLon);
        // Дуга — тонкая, низкая; по ней поток из 9 бусин с яркой головой.
        var path = new StreamGeometry();
        using (var g = path.Open())
        {
            var open = false;
            for (var i = 0; i <= 64; i++)
            {
                var (lat, lon, h) = ArcPoint(home, node, i / 64.0, 0.12);
                var (p, z) = Project(lat, lon, h, c, r);
                if (z <= -0.1) { if (open) { g.EndFigure(false); open = false; } continue; }
                if (!open) { g.BeginFigure(p, false); open = true; } else g.LineTo(p);
            }
            if (open) g.EndFigure(false);
        }
        var arcAlpha = on ? 0.45 : 0.2 + Math.Sin(now * 5) * 0.1;
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(WithA(pal.Arc, arcAlpha)), 1.4), path);

        var head = now * (on ? 0.45 : 0.25) % 1;
        for (var i = 0; i < 9; i++)
        {
            var t = head - i * 0.022;
            if (t < 0 || t > 1) continue;
            var (lat, lon, h) = ArcPoint(home, node, t, 0.12);
            var (p, z) = Project(lat, lon, h, c, r);
            if (z <= -0.1) continue;
            var alpha = (1 - i / 9.0) * Math.Sin(t * Math.PI) * (on ? 1 : 0.7);
            var color = i == 0 ? Colors.White : pal.Arc;
            ctx.DrawEllipse(new SolidColorBrush(WithA(color, alpha)), null, p, i == 0 ? 2.4 : 1.8 - i * 0.1, i == 0 ? 2.4 : 1.8 - i * 0.1);
        }

        // Дом.
        var (hp, hz) = Project(HomeLat, HomeLon, 1.004, c, r);
        if (hz > 0) ctx.DrawEllipse(new SolidColorBrush(pal.Home), null, hp, 2.6, 2.6);
        // Узел: ядро, ореол, расходящиеся кольца.
        var (np, nz) = Project(NodeLat, NodeLon, 1.004, c, r);
        if (nz > 0)
        {
            for (var i = 0; i < 2; i++)
            {
                var q = (now * 0.55 + i * 0.5) % 1;
                var rr = 7 * (0.6 + q * 1.9);
                ctx.DrawEllipse(null, new Pen(new SolidColorBrush(WithA(pal.Hi, (on ? 0.75 : 0.5) * (1 - q))), 1.2), np, rr, rr);
            }
            ctx.DrawEllipse(new SolidColorBrush(WithA(pal.Hi, (on ? 0.42 : 0.25) + Math.Sin(now * 2.4) * 0.08)), null, np, 6.5, 6.5);
            ctx.DrawEllipse(new SolidColorBrush(pal.Hi), null, np, 3.5, 3.5);
        }
    }

    private void DrawFlight(DrawingContext ctx, Point c, double r, Palette pal, double now)
    {
        if (_flightFrom is not { } from) return;
        const double fly = 1.6, fade = 0.8;
        var el = now - _flightStart;
        if (el > fly + fade) { _flightFrom = null; return; }
        var to = (NodeLat, NodeLon);
        var x = Math.Min(1, el / fly);
        var p = x < 0.5 ? 2 * x * x : 1 - Math.Pow(-2 * x + 2, 2) / 2;
        var alpha = el <= fly ? 1 : Math.Max(0, 1 - (el - fly) / fade);
        var va = ArcPoint(from, to, 0, 0);
        var angle = Rad(Math.Abs(va.Lat - to.NodeLat) + Math.Abs(va.Lon - to.NodeLon)) / 2;
        var lift = 0.18 + 0.32 * Math.Min(1, angle / Math.PI);
        var path = new StreamGeometry();
        Point headPoint = default;
        using (var g = path.Open())
        {
            var steps = Math.Max(2, (int)(64 * p));
            var open = false;
            for (var i = 0; i <= steps; i++)
            {
                var (lat, lon, h) = ArcPoint(from, to, p * i / steps, lift);
                var (pt, z) = Project(lat, lon, h, c, r);
                headPoint = pt;
                if (z <= -0.2) { if (open) { g.EndFigure(false); open = false; } continue; }
                if (!open) { g.BeginFigure(pt, false); open = true; } else g.LineTo(pt);
            }
            if (open) g.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(WithA(pal.Hi, 0.9 * alpha)), 1.4), path);
        ctx.DrawEllipse(new SolidColorBrush(WithA(Colors.White, alpha)), null, headPoint, 3, 3);
    }

    private void DrawSatellites(DrawingContext ctx, Point c, double r, Palette pal, bool back)
    {
        var now = Seconds;
        foreach (var s in _sats)
        {
            var a = s.Phase + now * s.Speed;
            // Орбита в плоскости XZ, затем наклон плоскости (X, Z).
            double x = Math.Cos(a) * s.Radius, y = 0, z = Math.Sin(a) * s.Radius;
            (y, z) = (y * Math.Cos(s.TiltX) - z * Math.Sin(s.TiltX), y * Math.Sin(s.TiltX) + z * Math.Cos(s.TiltX));
            (x, y) = (x * Math.Cos(s.TiltZ) - y * Math.Sin(s.TiltZ), x * Math.Sin(s.TiltZ) + y * Math.Cos(s.TiltZ));
            var behind = z < 0 && x * x + y * y < 1;
            if (behind != back) continue;
            var p = new Point(c.X + r * x, c.Y - r * y);
            var depth = 0.7 + 0.3 * Math.Clamp(z / s.Radius, -1, 1);
            var body = 2.2 * depth;
            ctx.DrawRectangle(new SolidColorBrush(WithA(pal.Hi, 0.85 * depth)), null, new Rect(p.X - body * 3, p.Y - 0.8, body * 2, 1.6));
            ctx.DrawRectangle(new SolidColorBrush(WithA(pal.Hi, 0.85 * depth)), null, new Rect(p.X + body, p.Y - 0.8, body * 2, 1.6));
            ctx.DrawRectangle(new SolidColorBrush(WithA(C("#E9EEF2"), depth)), null, new Rect(p.X - body, p.Y - body, body * 2, body * 2));
            if (Math.Sin(now * 5 + s.BlinkPhase) > 0.6)
                ctx.DrawEllipse(new SolidColorBrush(s.Red ? C("#FF5A4E") : Colors.White), null, new Point(p.X, p.Y - body - 1), 1, 1);
        }
    }

    private static Color WithA(Color c, double a) => Color.FromArgb((byte)Math.Clamp(a * 255 * (c.A / 255.0), 0, 255), c.R, c.G, c.B);
    private static Color Lighten(Color c, double k) => Color.FromArgb(c.A, (byte)(c.R + (255 - c.R) * k), (byte)(c.G + (255 - c.G) * k), (byte)(c.B + (255 - c.B) * k));
    private static Color Darken(Color c, double k) => Color.FromArgb(c.A, (byte)(c.R * (1 - k)), (byte)(c.G * (1 - k)), (byte)(c.B * (1 - k)));
}
