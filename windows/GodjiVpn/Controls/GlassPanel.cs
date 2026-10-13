using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GodjiVpn.Controls;

public enum GlassLevel { Normal, Strong, Faint }

public enum GlassShadow { None, Card, Strong, Pill }

/// <summary>
/// Поверхность «Goji Expressive» (Material 3 Expressive) — порт Android CardStyle.kt
/// (godjiCard/godjiGlassStrong/godjiGlassPill/godjiGlassFlat): плотная тональная заливка
/// уровня surfaceContainer или Tint, у выбранного — акцентная обводка 2px. Имя и свойства
/// прежнего стекла v5 сохранены, чтобы не трогать разметку экранов.
///
/// CornerRadius зажимается до половины меньшей стороны — CornerRadius="999" даёт капсулу/круг,
/// без искажений, которые давал бы обычный Border с радиусом больше половины высоты.
/// Цвета — DP, выставленные неявным стилем из App.xaml через DynamicResource, поэтому смена
/// темы перерисовывает все панели сама, без ручной подписки.
/// </summary>
public class GlassPanel : Decorator
{
    private readonly DrawingVisual _overlay = new();

    public GlassPanel()
    {
        AddVisualChild(_overlay);
        SnapsToDevicePixels = false;
    }

    #region layout / shape properties

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(GlassPanel),
        new FrameworkPropertyMetadata(26.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsArrange));

    public double CornerRadius { get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding), typeof(Thickness), typeof(GlassPanel),
        new FrameworkPropertyMetadata(new Thickness(), FrameworkPropertyMetadataOptions.AffectsMeasure));

    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(GlassLevel), typeof(GlassPanel),
        new FrameworkPropertyMetadata(GlassLevel.Normal, FrameworkPropertyMetadataOptions.AffectsRender));

    public GlassLevel Level { get => (GlassLevel)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }

    public static readonly DependencyProperty ShadowProperty = DependencyProperty.Register(
        nameof(Shadow), typeof(GlassShadow), typeof(GlassPanel),
        new FrameworkPropertyMetadata(GlassShadow.Card, FrameworkPropertyMetadataOptions.AffectsRender));

    public GlassShadow Shadow { get => (GlassShadow)GetValue(ShadowProperty); set => SetValue(ShadowProperty, value); }

    /// <summary>Цвет поверх стекла (TealTint, TerracottaTint, баннеры…). null — чистое стекло.</summary>
    public static readonly DependencyProperty TintProperty = DependencyProperty.Register(
        nameof(Tint), typeof(Brush), typeof(GlassPanel),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Tint { get => (Brush?)GetValue(TintProperty); set => SetValue(TintProperty, value); }

    /// <summary>Акцентная обводка 1.5px (выбранный сервер, текущий тариф).</summary>
    public static readonly DependencyProperty AccentBorderProperty = DependencyProperty.Register(
        nameof(AccentBorder), typeof(bool), typeof(GlassPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool AccentBorder { get => (bool)GetValue(AccentBorderProperty); set => SetValue(AccentBorderProperty, value); }

    /// <summary>Обрезать ли содержимое по скруглённой форме (строки с подсветкой до краёв).</summary>
    public static readonly DependencyProperty ClipContentProperty = DependencyProperty.Register(
        nameof(ClipContent), typeof(bool), typeof(GlassPanel),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsArrange));

    public bool ClipContent { get => (bool)GetValue(ClipContentProperty); set => SetValue(ClipContentProperty, value); }

    #endregion

    #region recipe colors (выставляются неявным стилем App.xaml из темы)

    private static DependencyProperty ColorProp(string name) => DependencyProperty.Register(
        name, typeof(Color), typeof(GlassPanel),
        new FrameworkPropertyMetadata(Colors.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GlassColorProperty = ColorProp(nameof(GlassColor));
    public static readonly DependencyProperty GlassStrongColorProperty = ColorProp(nameof(GlassStrongColor));
    public static readonly DependencyProperty GlassFaintColorProperty = ColorProp(nameof(GlassFaintColor));
    public static readonly DependencyProperty SpotColorProperty = ColorProp(nameof(SpotColor));
    public static readonly DependencyProperty GlossColorProperty = ColorProp(nameof(GlossColor));
    public static readonly DependencyProperty RimAColorProperty = ColorProp(nameof(RimAColor));
    public static readonly DependencyProperty RimBColorProperty = ColorProp(nameof(RimBColor));
    public static readonly DependencyProperty RimCColorProperty = ColorProp(nameof(RimCColor));
    public static readonly DependencyProperty RimDColorProperty = ColorProp(nameof(RimDColor));
    public static readonly DependencyProperty InnerTopColorProperty = ColorProp(nameof(InnerTopColor));
    public static readonly DependencyProperty InnerBottomColorProperty = ColorProp(nameof(InnerBottomColor));
    public static readonly DependencyProperty ShadowColorProperty = ColorProp(nameof(ShadowColor));
    public static readonly DependencyProperty AccentColorProperty = ColorProp(nameof(AccentColor));

    public Color GlassColor { get => (Color)GetValue(GlassColorProperty); set => SetValue(GlassColorProperty, value); }
    public Color GlassStrongColor { get => (Color)GetValue(GlassStrongColorProperty); set => SetValue(GlassStrongColorProperty, value); }
    public Color GlassFaintColor { get => (Color)GetValue(GlassFaintColorProperty); set => SetValue(GlassFaintColorProperty, value); }
    public Color SpotColor { get => (Color)GetValue(SpotColorProperty); set => SetValue(SpotColorProperty, value); }
    public Color GlossColor { get => (Color)GetValue(GlossColorProperty); set => SetValue(GlossColorProperty, value); }
    public Color RimAColor { get => (Color)GetValue(RimAColorProperty); set => SetValue(RimAColorProperty, value); }
    public Color RimBColor { get => (Color)GetValue(RimBColorProperty); set => SetValue(RimBColorProperty, value); }
    public Color RimCColor { get => (Color)GetValue(RimCColorProperty); set => SetValue(RimCColorProperty, value); }
    public Color RimDColor { get => (Color)GetValue(RimDColorProperty); set => SetValue(RimDColorProperty, value); }
    public Color InnerTopColor { get => (Color)GetValue(InnerTopColorProperty); set => SetValue(InnerTopColorProperty, value); }
    public Color InnerBottomColor { get => (Color)GetValue(InnerBottomColorProperty); set => SetValue(InnerBottomColorProperty, value); }
    public Color ShadowColor { get => (Color)GetValue(ShadowColorProperty); set => SetValue(ShadowColorProperty, value); }
    public Color AccentColor { get => (Color)GetValue(AccentColorProperty); set => SetValue(AccentColorProperty, value); }

    #endregion

    #region visual children (контент + верхний слой с кромкой)

    protected override int VisualChildrenCount => base.VisualChildrenCount + 1;

    protected override Visual GetVisualChild(int index)
    {
        var baseCount = base.VisualChildrenCount;
        return index < baseCount ? base.GetVisualChild(index) : _overlay;
    }

    #endregion

    #region layout

    protected override Size MeasureOverride(Size constraint)
    {
        var p = Padding;
        var ph = p.Left + p.Right;
        var pv = p.Top + p.Bottom;
        if (Child == null) return new Size(ph, pv);
        Child.Measure(new Size(Math.Max(0, constraint.Width - ph), Math.Max(0, constraint.Height - pv)));
        var d = Child.DesiredSize;
        return new Size(d.Width + ph, d.Height + pv);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child != null)
        {
            var p = Padding;
            var inner = new Rect(p.Left, p.Top,
                Math.Max(0, arrangeSize.Width - p.Left - p.Right),
                Math.Max(0, arrangeSize.Height - p.Top - p.Bottom));
            Child.Arrange(inner);
            if (ClipContent)
            {
                // Clip задаётся в координатах ребёнка, а они сдвинуты не только на Padding, но
                // и на его собственный Margin/выравнивание — иначе скругление срезало бы угол
                // содержимого (первую букву заголовка у карточки с <StackPanel Margin="14,13">).
                // Итоговый сдвиг известен сразу после Arrange.
                var r = EffectiveRadius(arrangeSize);
                var off = VisualTreeHelper.GetOffset(Child);
                Child.Clip = new RectangleGeometry(new Rect(-off.X, -off.Y, arrangeSize.Width, arrangeSize.Height), r, r);
            }
            else
            {
                Child.ClearValue(ClipProperty);
            }
        }
        return arrangeSize;
    }

    private double EffectiveRadius(Size size) =>
        Math.Max(0, Math.Min(CornerRadius, Math.Min(size.Width, size.Height) / 2));

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        var size = RenderSize;
        var r = EffectiveRadius(size);
        var shape = new RectangleGeometry(new Rect(size), r, r);
        return shape.FillContains(hitTestParameters.HitPoint) ? new PointHitTestResult(this, hitTestParameters.HitPoint) : null;
    }

    #endregion

    #region rendering

    /// <summary>«Goji Expressive» (Material 3 Expressive, порт Android CardStyle.kt 163f5ab):
    /// плотная тональная заливка нужного уровня (Normal — surfaceContainer, Strong — High,
    /// Faint — Highest) или Tint, без теней, бликов, кромок и внутренних теней. Рисуется один
    /// раз и сама по себе не перерисовывается. Свойства рецепта стекла (Spot/Gloss/Rim/Inner/
    /// Shadow) и Shadow остались ради совместимости разметки, но больше ничего не рисуют.</summary>
    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize;
        if (size.Width <= 0 || size.Height <= 0) return;
        var r = EffectiveRadius(size);
        var shape = new RectangleGeometry(new Rect(size), r, r);
        shape.Freeze();

        var fill = Level switch
        {
            GlassLevel.Strong => GlassStrongColor,
            GlassLevel.Faint => GlassFaintColor,
            _ => GlassColor
        };
        dc.DrawGeometry(Tint ?? Frozen(new SolidColorBrush(fill)), null, shape);

        RenderOverlay(size, r);
    }

    /// <summary>Акцентная обводка 2px (выбранный сервер, текущий тариф) — отдельным слоем ПОВЕРХ
    /// контента, иначе подсветка строк до краёв её перекрывала бы.</summary>
    private void RenderOverlay(Size size, double r)
    {
        using var dc = _overlay.RenderOpen();
        if (!AccentBorder) return;
        var accent = new Pen(Frozen(new SolidColorBrush(AccentColor)), 2);
        accent.Freeze();
        var ar = new Rect(1, 1, Math.Max(0, size.Width - 2), Math.Max(0, size.Height - 2));
        dc.DrawRoundedRectangle(null, accent, ar, Math.Max(0, r - 1), Math.Max(0, r - 1));
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    #endregion
}
