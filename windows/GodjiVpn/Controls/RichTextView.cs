using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace GodjiVpn.Controls;

/// <summary>
/// Показывает FlowDocument из Utils/RichContent обычными TextBlock вместо RichTextBox. RichTextBox
/// раскладывает текст движком PTS, а тот на Windows 10 при некоторых эмодзи (строка при отрисовке
/// выходит другой длины, чем при раскладке) завершает процесс через Environment.FailFast —
/// перехватить это нельзя. Отчёт о сбое 2026-10-26: новости на «Подписке», Windows 10 21H1,
/// приложение закрывалось сразу после входа.
/// </summary>
public sealed class RichTextView : ContentControl
{
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document), typeof(FlowDocument), typeof(RichTextView),
        new PropertyMetadata(null, (d, e) => ((RichTextView)d).Content = e.NewValue is FlowDocument doc ? Rendered(doc) : null));

    public FlowDocument? Document
    {
        get => (FlowDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    // Разбор переносит строки документа в TextBlock, поэтому делается один раз на документ;
    // если тот же документ привязали к новому контейнеру, разметка переезжает туда.
    private static readonly ConditionalWeakTable<FlowDocument, UIElement> Cache = new();

    private static UIElement Rendered(FlowDocument doc)
    {
        var element = Cache.GetValue(doc, Render);
        if (element is FrameworkElement { Parent: ContentControl owner }) owner.Content = null;
        return element;
    }

    public static UIElement Render(FlowDocument doc)
    {
        var panel = new StackPanel();
        // Шрифт и цвет документа наследуются строками, как внутри RichTextBox.
        panel.SetResourceReference(TextElement.ForegroundProperty, "TextPrimaryBrush");
        panel.SetResourceReference(TextElement.FontFamilyProperty, "ManropeMediumFamily");
        TextElement.SetFontSize(panel, doc.FontSize);
        foreach (var block in doc.Blocks.ToList()) panel.Children.Add(RenderBlock(block));
        return panel;
    }

    private static UIElement RenderBlock(Block block) => block switch
    {
        Paragraph p => RenderParagraph(p),
        BlockUIContainer { Child: { } child } container => Detach(container, child),
        Table table => RenderTable(table),
        _ => new TextBlock { Text = new TextRange(block.ContentStart, block.ContentEnd).Text, TextWrapping = TextWrapping.Wrap }
    };

    private static TextBlock RenderParagraph(Paragraph p)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = p.Margin };
        if (!double.IsNaN(p.LineHeight)) text.LineHeight = p.LineHeight;
        // Только заданное явно: по умолчанию FlowDocument растягивает строки по ширине.
        if (p.ReadLocalValue(Block.TextAlignmentProperty) != DependencyProperty.UnsetValue) text.TextAlignment = p.TextAlignment;
        if (p.ReadLocalValue(TextElement.FontSizeProperty) != DependencyProperty.UnsetValue) text.FontSize = p.FontSize;
        if (p.ReadLocalValue(TextElement.FontWeightProperty) != DependencyProperty.UnsetValue) text.FontWeight = p.FontWeight;
        if (p.ReadLocalValue(TextElement.FontStyleProperty) != DependencyProperty.UnsetValue) text.FontStyle = p.FontStyle;
        if (p.Tag is string foregroundKey) text.SetResourceReference(TextBlock.ForegroundProperty, foregroundKey);
        foreach (var inline in p.Inlines.ToList())
        {
            p.Inlines.Remove(inline);
            text.Inlines.Add(inline);
        }
        return text;
    }

    private static UIElement Detach(BlockUIContainer container, UIElement child)
    {
        container.Child = null;
        return child;
    }

    private static Grid RenderTable(Table table)
    {
        var grid = new Grid { Margin = table.Margin };
        foreach (var _ in table.Columns) grid.ColumnDefinitions.Add(new ColumnDefinition());
        var rowIndex = 0;
        foreach (var row in table.RowGroups.SelectMany(g => g.Rows))
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var columnIndex = 0;
            foreach (var cell in row.Cells)
            {
                var content = new StackPanel();
                foreach (var block in cell.Blocks.ToList()) content.Children.Add(RenderBlock(block));
                var border = new Border { Padding = cell.Padding, BorderThickness = cell.BorderThickness, Child = content };
                border.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
                Grid.SetRow(border, rowIndex);
                Grid.SetColumn(border, columnIndex++);
                grid.Children.Add(border);
            }
            rowIndex++;
        }
        return grid;
    }
}
