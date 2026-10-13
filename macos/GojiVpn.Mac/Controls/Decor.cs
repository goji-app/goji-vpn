using GodjiVpn.Utils;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace GodjiVpn.Controls;

/// <summary>
/// Базовый контрол, перерисовывающийся по тику общих часов (GlassClock). Декоративные
/// наследники двигаются MotionDuration секунд после появления/смены состояния (Restart) и
/// замирают в конечном кадре; непрерывные (IsContinuous — глобус, спиннер) перерисовываются,
/// пока их видно (ShouldRender).
/// </summary>
public abstract class AnimatedControl : Control
{
    private double _start;
    private bool _finalDrawn = true;

    /// <summary>Сколько секунд длится движение после Restart (декор — несколько циклов).</summary>
    protected virtual double MotionDuration => 0;

    /// <summary>Движение без конца, пока контрол виден (глобус, спиннер подключения).</summary>
    protected virtual bool IsContinuous => false;

    /// <summary>Нужно ли сейчас рисовать кадры непрерывного контрола.</summary>
    protected virtual bool ShouldRender => IsEffectivelyVisible;

    protected bool MotionDone => !IsContinuous && GlassClock.Now - _start >= MotionDuration;

    /// <summary>Время анимации от последнего Restart; у декора упирается в MotionDuration.</summary>
    protected double Seconds => IsContinuous ? GlassClock.Now - _start : Math.Min(GlassClock.Now - _start, MotionDuration);

    public void Restart()
    {
        _start = GlassClock.Now;
        _finalDrawn = false;
        InvalidateVisual();
        GlassClock.Kick();
    }

    internal ClockState OnClockTick()
    {
        if (IsContinuous)
        {
            if (!ShouldRender) return ClockState.Idle;
            InvalidateVisual();
            return ClockState.Drawn;
        }
        if (!MotionDone)
        {
            InvalidateVisual();
            return ClockState.Drawn;
        }
        if (!_finalDrawn)
        {
            _finalDrawn = true;
            InvalidateVisual();
        }
        return ClockState.Done;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        GlassClock.Subscribe(this);
        Restart();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        GlassClock.Unsubscribe(this);
    }

    protected IBrush Res(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : fallback;

    protected Color ResColor(string key, Color fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var v) && v is Color c ? c : fallback;
}

/// <summary>
/// Кнопка подключения «Goji Expressive» — порт Android ConnectButton (ConnectScreen.kt, 163f5ab):
/// «печенье» Material 3 Expressive 64×64 с 8 мягкими волнами по краю. Выключено —
/// primaryContainer; подключение — печенье вращается; включено — заливка primary, волны
/// сглаживаются почти в круг. Нажатие «вдавливает» волны — форма морфится пружиной. Без колец,
/// свечения и теней: кадры рисуются только во время морфа и вращения.
/// </summary>
public class PowerButton : AnimatedControl
{
    public static readonly StyledProperty<bool> IsOnProperty = AvaloniaProperty.Register<PowerButton, bool>(nameof(IsOn));
    public static readonly StyledProperty<bool> IsBusyProperty = AvaloniaProperty.Register<PowerButton, bool>(nameof(IsBusy));
    public static readonly StyledProperty<ICommand?> CommandProperty = AvaloniaProperty.Register<PowerButton, ICommand?>(nameof(Command));

    public bool IsOn { get => GetValue(IsOnProperty); set => SetValue(IsOnProperty, value); }
    public bool IsBusy { get => GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }
    public ICommand? Command { get => GetValue(CommandProperty); set => SetValue(CommandProperty, value); }

    private const double Size = 64;
    private const int Lobes = 8;
    private bool _pressed;
    private double _depth = 0.055;
    private double _velocity;
    private double _lastFrame = -1;
    private double _angle;

    public PowerButton()
    {
        Width = Size;
        Height = Size;
        HorizontalAlignment = HorizontalAlignment.Center;
        Cursor = new Cursor(StandardCursorType.Hand);
        PointerPressed += (_, _) => { _pressed = true; Restart(); };
        PointerReleased += (_, e) =>
        {
            var inside = new Rect(Bounds.Size).Contains(e.GetPosition(this));
            _pressed = false;
            Restart();
            if (inside && e.InitialPressMouseButton == MouseButton.Left && Command?.CanExecute(null) == true) Command.Execute(null);
        };
        PointerCaptureLost += (_, _) => { _pressed = false; Restart(); };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsOnProperty || change.Property == IsBusyProperty) Restart();
    }

    // Вращается, пока идёт подключение; пружина формы успокаивается примерно за секунду.
    protected override bool IsContinuous => IsBusy && !IsOn;
    protected override double MotionDuration => 1.2;

    private double TargetDepth => _pressed ? 0.09 : IsOn ? 0.025 : 0.055;

    public override void Render(DrawingContext ctx)
    {
        var now = Seconds;
        var dt = _lastFrame < 0 || now < _lastFrame ? 0 : Math.Min(now - _lastFrame, 0.05);
        _lastFrame = now;
        // Пружина (dampingRatio 0.45, stiffness 380 в Android): x'' = -k(x - x0) - c·x'.
        const double k = 380, c = 2 * 0.45 * 19.5;
        _velocity += (-k * (_depth - TargetDepth) - c * _velocity) * dt;
        _depth += _velocity * dt;
        if (MotionDone) { _depth = TargetDepth; _velocity = 0; }
        if (IsContinuous) _angle = (_angle + dt * 200) % 360; else _angle = 0;

        var fill = IsOn ? Res("TealBrush", Brushes.Teal) : Res("PrimaryContainerBrush", Brushes.LightGreen);
        var iconFill = IsOn ? Res("SurfaceBrush", Brushes.White) : Res("OnPrimaryContainerBrush", Brushes.Black);
        var cx = Bounds.Width / 2;
        var cy = Bounds.Height / 2;
        var r = Math.Min(cx, cy) / (1 + Math.Max(0, _depth));
        var rot = _angle * Math.PI / 180;
        var g = new StreamGeometry();
        using (var s = g.Open())
        {
            const int steps = 144;
            for (var i = 0; i < steps; i++)
            {
                var t = i / (double)steps * 2 * Math.PI;
                var rr = r * (1 + _depth * Math.Cos(Lobes * t));
                var pt = new Point(cx + rr * Math.Cos(t + rot), cy + rr * Math.Sin(t + rot));
                if (i == 0) s.BeginFigure(pt, true); else s.LineTo(pt);
            }
            s.EndFigure(true);
        }
        ctx.DrawGeometry(fill, null, g);

        if (this.TryFindResource("IconPower", ActualThemeVariant, out var icon) && icon is Geometry iconGeometry)
        {
            var b = iconGeometry.Bounds;
            var scale = 26 / Math.Max(b.Width, b.Height);
            var m = Matrix.CreateTranslation(-b.X - b.Width / 2, -b.Y - b.Height / 2) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(cx, cy);
            using (ctx.PushTransform(m))
                ctx.DrawGeometry(iconFill, null, iconGeometry);
        }
    }
}

/// <summary>Полоса прогресса 8px (трафик, загрузка обновления): дорожка TrackBg и плоская заливка
/// primary, плавно догоняющая значение. «Goji Expressive»: бегущий блик v5 убран.</summary>
public class ShimmerBar : AnimatedControl
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<ShimmerBar, double>(nameof(Value));
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    private double _shown;

    public ShimmerBar() => Height = 8;

    // Заливка догоняет новое значение примерно за секунду, потом полоса статична.
    protected override double MotionDuration => 1.2;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty) Restart();
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0) return;
        _shown = MotionDone ? Math.Clamp(Value, 0, 1) : _shown + (Math.Clamp(Value, 0, 1) - _shown) * 0.2;
        ctx.DrawRectangle(Res("TrackBgBrush", Brushes.LightGray), null, new RoundedRect(new Rect(0, 0, w, h), h / 2));
        var fw = w * _shown;
        if (fw < 1) return;
        ctx.DrawRectangle(Res("TealBrush", Brushes.Teal), null, new RoundedRect(new Rect(0, 0, Math.Max(fw, h), h), h / 2));
    }
}

/// <summary>Кольцо "осталось N дней" 68×68: дуга Teal (доля days/30) по дорожке, ядро RingCore
/// с числом и "ДНЕЙ".</summary>
public class DaysRing : Panel
{
    public static readonly StyledProperty<int> DaysProperty = AvaloniaProperty.Register<DaysRing, int>(nameof(Days));
    public int Days { get => GetValue(DaysProperty); set => SetValue(DaysProperty, value); }

    private readonly TextBlock _number = new() { FontWeight = FontWeight.ExtraBold, [!TextBlock.FontSizeProperty] = FontScale.For(19), HorizontalAlignment = HorizontalAlignment.Center };

    public DaysRing()
    {
        Width = 68;
        Height = 68;
        Children.Add(new Ring(this));
        var core = new Border { Width = 54, Height = 54, CornerRadius = new CornerRadius(27) };
        core[!Border.BackgroundProperty] = new DynamicResourceExtension("RingCoreBrush");
        Children.Add(core);
        var label = new TextBlock { Text = "ДНЕЙ", FontWeight = FontWeight.Bold, [!TextBlock.FontSizeProperty] = FontScale.For(7.5), HorizontalAlignment = HorizontalAlignment.Center };
        label[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondaryBrush");
        _number[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimaryBrush");
        Children.Add(new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _number, label }
        });
        _number.Text = "0";
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DaysProperty)
        {
            _number.Text = Days.ToString();
            foreach (var ch in Children) if (ch is Ring r) r.InvalidateVisual();
        }
    }

    private sealed class Ring : Control
    {
        private readonly DaysRing _owner;

        public Ring(DaysRing owner) => _owner = owner;

        public override void Render(DrawingContext ctx)
        {
            var size = Math.Min(Bounds.Width, Bounds.Height);
            var r = size / 2 - 3.5;
            var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
            var track = this.TryFindResource("TrackBgBrush", ActualThemeVariant, out var t) && t is IBrush tb ? tb : Brushes.LightGray;
            var teal = this.TryFindResource("TealBrush", ActualThemeVariant, out var b) && b is IBrush bb ? bb : Brushes.Teal;
            ctx.DrawEllipse(null, new Pen(track, 7), c, r, r);
            var fraction = Math.Clamp(_owner.Days / 30.0, 0, 1);
            if (fraction <= 0) return;
            if (fraction >= 0.999)
            {
                ctx.DrawEllipse(null, new Pen(teal, 7), c, r, r);
                return;
            }
            Point P(double deg) => new(c.X + r * Math.Cos(deg * Math.PI / 180), c.Y + r * Math.Sin(deg * Math.PI / 180));
            var g = new StreamGeometry();
            using (var s = g.Open())
            {
                s.BeginFigure(P(-90), false);
                s.ArcTo(P(-90 + 360 * fraction), new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise);
                s.EndFigure(false);
            }
            ctx.DrawGeometry(null, new Pen(teal, 7, lineCap: PenLineCap.Round), g);
        }
    }
}

/// <summary>Общая тональная таблетка бейджей «Goji Expressive» (порт Android StatusBadges.kt,
/// 163f5ab): фон, высота и ряд «значок + подпись». В v5 бейджи анимировались (комета по рамке,
/// пульсирующая точка, перелив) — теперь это обычные плашки, которые рисуются один раз.</summary>
public abstract class TonalPill : Border
{
    protected readonly StackPanel Row = new() { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };

    protected TonalPill(double height)
    {
        Height = height;
        VerticalAlignment = VerticalAlignment.Center;
        CornerRadius = new CornerRadius(height / 2);
        Padding = new Thickness(10, 0);
        Child = Row;
    }

    protected void SetBackgroundKey(string key) => this[!BackgroundProperty] = new DynamicResourceExtension(key);

    protected static TextBlock Label(double size, string foregroundKey)
    {
        var t = new TextBlock { FontWeight = FontWeight.ExtraBold, [!TextBlock.FontSizeProperty] = FontScale.For(size), VerticalAlignment = VerticalAlignment.Center };
        t[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(foregroundKey);
        return t;
    }
}

/// <summary>«АКТИВНА»: заливка primary, точка и подпись цвета onPrimary.</summary>
public class ActiveBadge : TonalPill
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<ActiveBadge, string>(nameof(Text), "АКТИВНА");
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    private readonly TextBlock _label = Label(10, "SurfaceBrush");

    public ActiveBadge() : base(26)
    {
        SetBackgroundKey("TealBrush");
        var dot = new Avalonia.Controls.Shapes.Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
        dot[!Avalonia.Controls.Shapes.Shape.FillProperty] = new DynamicResourceExtension("SurfaceBrush");
        _label.Text = Text;
        Row.Children.Add(dot);
        Row.Children.Add(_label);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty) _label.Text = Text;
    }
}

/// <summary>«Текущий» / «НОВАЯ»: tertiaryContainer (Reverse — primaryContainer).</summary>
public class HoloBadge : TonalPill
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<HoloBadge, string>(nameof(Text), "");
    public static readonly StyledProperty<string> IconProperty = AvaloniaProperty.Register<HoloBadge, string>(nameof(Icon), "★");
    public static readonly StyledProperty<bool> ReverseProperty = AvaloniaProperty.Register<HoloBadge, bool>(nameof(Reverse));

    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public bool Reverse { get => GetValue(ReverseProperty); set => SetValue(ReverseProperty, value); }

    private readonly TextBlock _icon = Label(9, "OnTertiaryContainerBrush");
    private readonly TextBlock _label = Label(9.5, "OnTertiaryContainerBrush");

    public HoloBadge() : base(22)
    {
        Row.Spacing = 5;
        Row.Children.Add(_icon);
        Row.Children.Add(_label);
        Update();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == IconProperty || change.Property == ReverseProperty) Update();
    }

    private void Update()
    {
        _label.Text = Text;
        _icon.Text = Icon;
        _icon.IsVisible = !string.IsNullOrEmpty(Icon);
        SetBackgroundKey(Reverse ? "PrimaryContainerBrush" : "TertiaryContainerBrush");
        var fg = Reverse ? "OnPrimaryContainerBrush" : "OnTertiaryContainerBrush";
        _icon[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(fg);
        _label[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(fg);
    }
}
