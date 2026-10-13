using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace GodjiVpn.Utils;

/// <summary>
/// RichTextBox.Document — обычное CLR-свойство, а не DependencyProperty, поэтому
/// Document="{Binding ...}" в XAML падает рантайм-исключением ("Binding можно задать только
/// в параметре DependencyProperty"). Attached property — стандартный WPF-обход: сама
/// AttachedDocument уже настоящий DependencyProperty и поддерживает Binding, а в callback
/// просто прокидывает значение в реальный RichTextBox.Document.
/// </summary>
public static class RichTextBoxBehavior
{
    public static readonly DependencyProperty AttachedDocumentProperty =
        DependencyProperty.RegisterAttached("AttachedDocument", typeof(FlowDocument), typeof(RichTextBoxBehavior),
            new PropertyMetadata(null, OnAttachedDocumentChanged));

    public static void SetAttachedDocument(RichTextBox box, FlowDocument? value) => box.SetValue(AttachedDocumentProperty, value);
    public static FlowDocument? GetAttachedDocument(RichTextBox box) => (FlowDocument?)box.GetValue(AttachedDocumentProperty);

    /// <summary>Если ItemsControl (список карточек новостей) пересобирает визуальное дерево
    /// под быстрым relayout (например resize/maximize окна, пока не осела первичная
    /// раскладка), может на мгновение существовать два RichTextBox-контейнера для одного и
    /// того же элемента списка — оба пытаются получить один и тот же FlowDocument. Сам WPF
    /// кидает тогда ArgumentException ("документ уже принадлежит другому RichTextBox"),
    /// необработанное — валит всё приложение (см. crash.log, живой репро при maximize на
    /// вкладке "Подписка"). Явно освобождаем документ у предыдущего владельца перед
    /// присвоением новому, plus try/catch как последний рубеж — контент одной карточки
    /// новости не настолько критичен, чтобы ронять всё окно.</summary>
    private static void OnAttachedDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RichTextBox box) return;
        var newDoc = (FlowDocument?)e.NewValue;
        if (newDoc?.Parent is RichTextBox previousOwner && !ReferenceEquals(previousOwner, box))
            previousOwner.Document = new FlowDocument();
        try { box.Document = newDoc ?? new FlowDocument(); }
        catch (ArgumentException) { /* см. комментарий выше — не критично, пропускаем */ }
    }
}
