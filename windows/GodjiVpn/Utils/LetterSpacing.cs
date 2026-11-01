using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GodjiVpn.Utils;

/// <summary>
/// Межбуквенный интервал (letter-spacing) для TextBlock — нативного свойства в WPF нет.
/// Применяется как attached-свойство: <c>s:LetterSpacing.Spacing="1"</c>. Разрядка делается
/// TextEffect-ами (по одному на символ: i-й глиф сдвигается на i·spacing), поэтому сам текст
/// не меняется и продолжает работать с привязками. Используется стилем SectionLabel
/// (заголовки секций, 1.0.132: UPPERCASE + разрядка ~1 px).
/// </summary>
public static class LetterSpacing
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.RegisterAttached(
        "Spacing", typeof(double), typeof(LetterSpacing),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.Inherits, OnSpacingChanged));

    public static double GetSpacing(TextBlock element) => (double)element.GetValue(SpacingProperty);

    public static void SetSpacing(TextBlock element, double value) => element.SetValue(SpacingProperty, value);

    // Подписка на смену Text — в слабой таблице: не мешает сборщику убрать TextBlock,
    // если экран с ним исчезнет (живут они, впрочем, столько же, сколько приложение).
    private static readonly ConditionalWeakTable<TextBlock, EventHandler> Subscribed = new();

    private static void OnSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;

        if (Subscribed.TryGetValue(tb, out var existing))
        {
            DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock)).RemoveValueChanged(tb, existing);
            Subscribed.Remove(tb);
        }
        if ((double)e.NewValue > 0)
        {
            void Handler(object? _, EventArgs __) => Rebuild(tb);
            DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock)).AddValueChanged(tb, Handler);
            Subscribed.Add(tb, Handler);
        }
        Rebuild(tb);
    }

    private static void Rebuild(TextBlock tb)
    {
        var spacing = GetSpacing(tb);
        tb.TextEffects.Clear();
        var text = tb.Text;
        if (spacing <= 0 || string.IsNullOrEmpty(text)) return;

        // Сдвиг только символов после первого: расстояние между соседними вырастает на
        // spacing, начало строки остаётся на месте (для коротких заголовков этого довольно,
        // TextEffect — эффект отрисовки и на измерение ширины не влияет).
        for (var i = 1; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i - 1]) && char.IsWhiteSpace(text[i])) continue;
            tb.TextEffects.Add(new TextEffect
            {
                Transform = new TranslateTransform(i * spacing, 0),
                PositionStart = i,
                PositionCount = 1
            });
        }
    }
}
