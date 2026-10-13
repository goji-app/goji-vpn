using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace GodjiVpn.Controls;

/// <summary>
/// Кнопка подключения «Goji Expressive» — порт Android ConnectButton (ConnectScreen.kt, 163f5ab):
/// «печенье» Material 3 Expressive 64×64 с 8 мягкими волнами по краю (CookieShape).
/// Выключено — primaryContainer; подключение — печенье вращается (как LoadingIndicator M3E);
/// включено — заливка primary, волны сглаживаются почти в круг. Нажатие «вдавливает» волны —
/// форма морфится пружиной, а не переключается рывком. Без колец, свечения и теней: ничего не
/// перерисовывается само по себе, кроме поворота во время подключения.
/// </summary>
public class PowerButton : UserControl
{
    private const double Size = 64;
    private const int Lobes = 8;

    private readonly Grid _root = new() { Width = Size, Height = Size };
    private readonly Path _cookie = new() { Width = Size, Height = Size, Stretch = Stretch.None };
    private readonly Path _icon = new() { Width = 26, Height = 26, Stretch = Stretch.Uniform, IsHitTestVisible = false };
    private readonly RotateTransform _spin = new(0, Size / 2, Size / 2);
    private readonly RotateTransform _iconCounterSpin = new(0, 13, 13);
    private bool _pressed;

    public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(
        nameof(IsOn), typeof(bool), typeof(PowerButton), new PropertyMetadata(false, (d, _) => ((PowerButton)d).UpdateState()));

    public bool IsOn { get => (bool)GetValue(IsOnProperty); set => SetValue(IsOnProperty, value); }

    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(
        nameof(IsBusy), typeof(bool), typeof(PowerButton), new PropertyMetadata(false, (d, _) => ((PowerButton)d).UpdateState()));

    public bool IsBusy { get => (bool)GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }

    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(PowerButton));

    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }

    /// <summary>Глубина волн в долях радиуса (0 — ровный круг); анимируется пружиной.</summary>
    private static readonly DependencyProperty DepthProperty = DependencyProperty.Register(
        "Depth", typeof(double), typeof(PowerButton), new PropertyMetadata(0.055, (d, _) => ((PowerButton)d).RebuildShape()));

    public PowerButton()
    {
        Width = Size;
        Height = Size;
        Cursor = Cursors.Hand;
        HorizontalAlignment = HorizontalAlignment.Center;

        _cookie.RenderTransform = _spin;
        _root.Children.Add(_cookie);

        _icon.Data = (Geometry)Application.Current.FindResource("IconPower");
        _icon.RenderTransform = _iconCounterSpin;
        _root.Children.Add(_icon);
        Content = _root;

        MouseLeftButtonDown += (_, _) => { _pressed = true; CaptureMouse(); UpdateDepth(); };
        MouseLeftButtonUp += (_, e) =>
        {
            var inside = new Rect(RenderSize).Contains(e.GetPosition(this));
            ReleasePress();
            if (inside && Command?.CanExecute(null) == true) Command.Execute(null);
        };
        LostMouseCapture += (_, _) => ReleasePress();

        RebuildShape();
        Loaded += (_, _) => UpdateState();
    }

    private void ReleasePress()
    {
        if (!_pressed) return;
        _pressed = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        UpdateDepth();
    }

    private void UpdateState()
    {
        if (IsOn)
        {
            _cookie.SetResourceReference(Shape.FillProperty, "TealBrush");
            _icon.SetResourceReference(Shape.FillProperty, "SurfaceBrush");
        }
        else
        {
            _cookie.SetResourceReference(Shape.FillProperty, "PrimaryContainerBrush");
            _icon.SetResourceReference(Shape.FillProperty, "OnPrimaryContainerBrush");
        }

        if (IsBusy && !IsOn)
        {
            var spin = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(1800)) { RepeatBehavior = RepeatBehavior.Forever };
            var counter = new DoubleAnimation(0, -360, TimeSpan.FromMilliseconds(1800)) { RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(spin, 30);
            Timeline.SetDesiredFrameRate(counter, 30);
            _spin.BeginAnimation(RotateTransform.AngleProperty, spin);
            _iconCounterSpin.BeginAnimation(RotateTransform.AngleProperty, counter);
        }
        else
        {
            _spin.BeginAnimation(RotateTransform.AngleProperty, null);
            _iconCounterSpin.BeginAnimation(RotateTransform.AngleProperty, null);
            _spin.Angle = 0;
            _iconCounterSpin.Angle = 0;
        }
        UpdateDepth();
    }

    private void UpdateDepth()
    {
        var target = _pressed ? 0.09 : IsOn ? 0.025 : 0.055;
        // Пружина (dampingRatio 0.45 в Android) — ElasticEase с небольшим перелётом.
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 5, EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(DepthProperty, anim);
    }

    /// <summary>Контур «печенья»: круг радиуса r с волной depth·cos(lobes·t); внешний край волны
    /// касается границ 64×64 (CookieShape в Android ExpressiveShapes.kt).</summary>
    private void RebuildShape()
    {
        var depth = (double)GetValue(DepthProperty);
        const double c = Size / 2;
        var r = c / (1 + Math.Max(0, depth));
        const int steps = 144;
        var points = new Point[steps];
        for (var i = 0; i < steps; i++)
        {
            var t = i / (double)steps * 2 * Math.PI;
            var rr = r * (1 + depth * Math.Cos(Lobes * t));
            points[i] = new Point(c + rr * Math.Cos(t), c + rr * Math.Sin(t));
        }
        var figure = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = true };
        figure.Segments.Add(new PolyLineSegment(points.Skip(1), false));
        var geometry = new PathGeometry(new[] { figure });
        geometry.Freeze();
        _cookie.Data = geometry;
    }
}
