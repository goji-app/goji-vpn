using GodjiVpn.Utils;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GodjiVpn.Controls;

/*
 * Бейджи «Goji Expressive» — статичные тональные таблетки Material 3 Expressive (порт Android
 * StatusBadges.kt, 163f5ab). В v5 они анимировались (комета по рамке, пульсирующая точка,
 * перелив и блик) — теперь это обычные плашки, которые рисуются один раз.
 */

/// <summary>Общая таблетка: фон, высота и ряд «значок + подпись» по центру.</summary>
public abstract class TonalPill : UserControl
{
    protected readonly StackPanel Row = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _body = new() { Padding = new Thickness(10, 0, 10, 0) };

    protected TonalPill(double height)
    {
        Height = height;
        VerticalAlignment = VerticalAlignment.Center;
        _body.CornerRadius = new CornerRadius(height / 2);
        _body.Child = Row;
        Content = _body;
    }

    protected void SetBackgroundKey(string key) => _body.SetResourceReference(Border.BackgroundProperty, key);

    protected static TextBlock Label(double size, string foregroundKey)
    {
        var t = new TextBlock { FontWeight = FontWeights.ExtraBold, VerticalAlignment = VerticalAlignment.Center };
        FontScale.Bind(t, TextBlock.FontSizeProperty, size);
        t.SetResourceReference(TextBlock.ForegroundProperty, foregroundKey);
        t.SetResourceReference(TextBlock.FontFamilyProperty, "ManropeFamily");
        return t;
    }
}

/// <summary>«АКТИВНА»: заливка primary, точка и подпись цвета onPrimary.</summary>
public class ActiveBadge : TonalPill
{
    private readonly TextBlock _label = Label(10, "SurfaceBrush");

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(ActiveBadge),
        new PropertyMetadata("АКТИВНА", (d, e) => ((ActiveBadge)d)._label.Text = (string)e.NewValue));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public ActiveBadge() : base(26)
    {
        SetBackgroundKey("TealBrush");
        var dot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Shape.FillProperty, "SurfaceBrush");
        _label.Text = Text;
        Row.Children.Add(dot);
        Row.Children.Add(_label);
    }
}

/// <summary>«Текущий» / «НОВАЯ»: tertiaryContainer (Reverse — primaryContainer).</summary>
public class HoloBadge : TonalPill
{
    private readonly TextBlock _icon = Label(9, "OnTertiaryContainerBrush");
    private readonly TextBlock _label = Label(9.5, "OnTertiaryContainerBrush");

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(HoloBadge),
        new PropertyMetadata("", (d, e) => ((HoloBadge)d)._label.Text = (string)e.NewValue));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(HoloBadge),
        new PropertyMetadata("★", (d, e) => ((HoloBadge)d).UpdateIcon()));

    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }

    public static readonly DependencyProperty ReverseProperty = DependencyProperty.Register(
        nameof(Reverse), typeof(bool), typeof(HoloBadge),
        new PropertyMetadata(false, (d, _) => ((HoloBadge)d).UpdateColors()));

    public bool Reverse { get => (bool)GetValue(ReverseProperty); set => SetValue(ReverseProperty, value); }

    public HoloBadge() : base(22)
    {
        _icon.Margin = new Thickness(0, 0, 5, 0);
        Row.Children.Add(_icon);
        Row.Children.Add(_label);
        UpdateIcon();
        UpdateColors();
    }

    private void UpdateIcon()
    {
        _icon.Text = Icon;
        _icon.Visibility = string.IsNullOrEmpty(Icon) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateColors()
    {
        SetBackgroundKey(Reverse ? "PrimaryContainerBrush" : "TertiaryContainerBrush");
        var fg = Reverse ? "OnPrimaryContainerBrush" : "OnTertiaryContainerBrush";
        _icon.SetResourceReference(TextBlock.ForegroundProperty, fg);
        _label.SetResourceReference(TextBlock.ForegroundProperty, fg);
    }
}
