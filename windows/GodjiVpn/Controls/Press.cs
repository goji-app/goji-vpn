using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GodjiVpn.Controls;

/// <summary>Пружинное сжатие при нажатии — rememberPressScale() из Android одной строкой в XAML:
/// controls:Press.Scale="0.96". Отпускание возвращает масштаб с лёгким перелётом (BackEase).</summary>
public static class Press
{
    public static readonly DependencyProperty ScaleProperty = DependencyProperty.RegisterAttached(
        "Scale", typeof(double), typeof(Press), new PropertyMetadata(1.0, OnScaleChanged));

    public static double GetScale(DependencyObject d) => (double)d.GetValue(ScaleProperty);
    public static void SetScale(DependencyObject d, double value) => d.SetValue(ScaleProperty, value);

    private static void OnScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el) return;
        el.PreviewMouseLeftButtonDown -= OnDown;
        el.PreviewMouseLeftButtonUp -= OnUp;
        el.MouseLeave -= OnLeave;
        if ((double)e.NewValue >= 1.0) return;
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        if (el.RenderTransform is not ScaleTransform) el.RenderTransform = new ScaleTransform(1, 1);
        el.PreviewMouseLeftButtonDown += OnDown;
        el.PreviewMouseLeftButtonUp += OnUp;
        el.MouseLeave += OnLeave;
    }

    private static void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement el && el.IsEnabled) Animate(el, GetScale(el), TimeSpan.FromMilliseconds(80), null);
    }

    private static void OnUp(object sender, MouseButtonEventArgs e) => Release(sender);

    private static void OnLeave(object sender, MouseEventArgs e) => Release(sender);

    private static void Release(object sender)
    {
        if (sender is UIElement el)
            Animate(el, 1.0, TimeSpan.FromMilliseconds(260), new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut });
    }

    private static void Animate(UIElement el, double to, TimeSpan duration, IEasingFunction? easing)
    {
        if (el.RenderTransform is not ScaleTransform st || st.IsFrozen)
        {
            st = new ScaleTransform(1, 1);
            el.RenderTransform = st;
        }
        var anim = new DoubleAnimation(to, duration) { EasingFunction = easing };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }
}
