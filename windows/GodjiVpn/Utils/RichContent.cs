using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace GodjiVpn.Utils;

/// <summary>
/// Порт RichContent.kt (Android) — рендерер "Rich Markdown" + Telegram HTML-разметки, в
/// которой приходит content новостей (GET /api/broadcasts), 1:1 по шпаргалке синтаксиса,
/// которую использует бэкенд для составления рассылок. Поддержано: **жирный**/*курсив*/
/// ~~зачёркнутый~~/||спойлер (тап-раскрытие)||/==выделение==/`код`, теги &lt;b&gt;/&lt;i&gt;/
/// &lt;u&gt;/&lt;s&gt;/&lt;code&gt;/&lt;pre&gt;/&lt;a href&gt;/&lt;tg-spoiler&gt;/&lt;br&gt;,
/// заголовки #..######, списки (- / 1. / - [ ]), цитаты (&gt;), таблицы (|a|b|), разделитель
/// (---), сноски ([^id]/[^id]: текст), картинки ![]("подпись"), &lt;details&gt;&lt;summary&gt;
/// (через WPF Expander), &lt;tg-collage&gt; (сетка 2 колонки), &lt;tg-slideshow&gt; (карусель
/// с точками), &lt;tg-map lat lon zoom/&gt; (открывает Windows Maps через bingmaps: URI —
/// Windows не имеет geo:-схемы, которой пользуется Android).
///
/// $формула$/$$формула$$ — как и в Android-оригинале, без typesetting (отдельная библиотека
/// вроде JLaTeXMath/KaTeX непропорционально тяжела для новостной ленты), просто моноширинным
/// текстом.
/// </summary>
public static class RichContent
{
    /// <param name="collapsedBlocks">Сколько верхнеуровневых блоков показать до "Читать
    /// полностью" (int.MaxValue — не сворачивать).</param>
    /// <param name="onReadMore">Вызывается по клику на "Читать полностью".</param>
    public static FlowDocument Build(string raw, int collapsedBlocks = int.MaxValue, Action? onReadMore = null)
    {
        var (allBlocks, footnotes) = ParseDocument(raw.Replace("\r\n", "\n"));
        var doc = new FlowDocument { PagePadding = new Thickness(0), FontSize = FontScale.Of(12.5) };
        doc.SetResourceReference(TextElement.ForegroundProperty, "TextPrimaryBrush");
        doc.SetResourceReference(TextElement.FontFamilyProperty, "ManropeMediumFamily");

        var overflowing = allBlocks.Count > collapsedBlocks;
        var visible = overflowing ? allBlocks.Take(collapsedBlocks) : allBlocks;
        foreach (var block in visible) doc.Blocks.Add(block);

        if (overflowing && onReadMore != null) doc.Blocks.Add(ReadMoreBlock(onReadMore));

        if (footnotes.Count > 0 && !overflowing)
        {
            doc.Blocks.Add(Divider());
            foreach (var (id, text) in footnotes)
            {
                var p = NewParagraph();
                p.FontSize = FontScale.Of(10);
                p.SetResourceReference(TextElement.ForegroundProperty, "TextMutedBrush");
                p.Inlines.Add(new Run($"[{id}] {text}"));
                doc.Blocks.Add(p);
            }
        }
        return doc;
    }

    private static Block ReadMoreBlock(Action onReadMore)
    {
        var p = NewParagraph();
        var link = new Hyperlink(new Run("Читать полностью")) { TextDecorations = null, FontWeight = FontWeights.Bold, FontSize = FontScale.Of(11) };
        link.SetResourceReference(TextElement.ForegroundProperty, "TealDeepBrush");
        link.Click += (_, _) => onReadMore();
        p.Inlines.Add(link);
        return p;
    }

    // ── Верхний уровень: сноски + спецблоки (collage/slideshow/map/details) ────────────────

    private static readonly Regex FootnoteDefRegex = new(@"^\[\^([^\]]+)]:\s*(.+)$", RegexOptions.Multiline);
    private static readonly Regex CollageRegex = new(@"<tg-collage>([\s\S]*?)</tg-collage>", RegexOptions.IgnoreCase);
    private static readonly Regex SlideshowRegex = new(@"<tg-slideshow>([\s\S]*?)</tg-slideshow>", RegexOptions.IgnoreCase);
    private static readonly Regex DetailsRegex = new(@"<details>\s*<summary>([\s\S]*?)</summary>([\s\S]*?)</details>", RegexOptions.IgnoreCase);
    private static readonly Regex MapRegex = new(@"<tg-map\s+(-?[0-9.]+)\s+(-?[0-9.]+)(?:\s+(\d+))?\s*/>", RegexOptions.IgnoreCase);
    private static readonly Regex ImageUrlRegex = new(@"!\[([^\]]*)]\(([^\s)]+)(?:\s+""([^""]*)"")?\)");

    private static (List<Block>, Dictionary<string, string>) ParseDocument(string raw)
    {
        var footnotes = new Dictionary<string, string>();
        var withoutFootnotes = FootnoteDefRegex.Replace(raw, m => { footnotes[m.Groups[1].Value] = m.Groups[2].Value.Trim(); return ""; });

        var matches = new List<(int Start, int End, Func<Block> Build)>();
        foreach (Match m in CollageRegex.Matches(withoutFootnotes))
            matches.Add((m.Index, m.Index + m.Length, () => CollageBlock(ExtractImageUrls(m.Groups[1].Value))));
        foreach (Match m in SlideshowRegex.Matches(withoutFootnotes))
            matches.Add((m.Index, m.Index + m.Length, () => SlideshowBlock(ExtractImageUrls(m.Groups[1].Value))));
        foreach (Match m in DetailsRegex.Matches(withoutFootnotes))
            matches.Add((m.Index, m.Index + m.Length, () => DetailsBlock(m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim())));
        foreach (Match m in MapRegex.Matches(withoutFootnotes))
        {
            if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) continue;
            if (!double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) continue;
            var zoom = int.TryParse(m.Groups[3].Value, out var z) ? z : (int?)null;
            matches.Add((m.Index, m.Index + m.Length, () => MapBlock(lat, lon, zoom)));
        }
        matches.Sort((a, b) => a.Start.CompareTo(b.Start));

        // Не поддерживаем вложенность спецблоков — отбрасываем совпадения, начинающиеся внутри
        // уже занятого диапазона.
        var filtered = new List<(int Start, int End, Func<Block> Build)>();
        var lastEnd = -1;
        foreach (var m in matches)
        {
            if (m.Start <= lastEnd) continue;
            filtered.Add(m);
            lastEnd = m.End;
        }

        var blocks = new List<Block>();
        var cursor = 0;
        foreach (var m in filtered)
        {
            if (m.Start > cursor) blocks.AddRange(ParsePlainBlocks(withoutFootnotes[cursor..m.Start]));
            blocks.Add(m.Build());
            cursor = m.End;
        }
        if (cursor < withoutFootnotes.Length) blocks.AddRange(ParsePlainBlocks(withoutFootnotes[cursor..]));

        return (blocks, footnotes);
    }

    private static List<string> ExtractImageUrls(string inner)
    {
        var urls = ImageUrlRegex.Matches(inner).Select(m => m.Groups[2].Value).ToList();
        if (urls.Count == 0)
            urls.AddRange(inner.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("http")));
        return urls;
    }

    // ── Блочный уровень (обычный markdown-текст) ────────────────────────────────────────────

    private static readonly Regex HeadingRegex = new(@"^(#{1,6})\s+(.*)$");
    private static readonly Regex CheckRegex = new(@"^-\s+\[([ xX])]\s+(.*)$");
    private static readonly Regex ImageRegex = new(@"^!\[([^\]]*)]\(([^\s)]+)(?:\s+""([^""]*)"")?\)$");
    private static readonly Regex OrderedRegex = new(@"^(\d+)\.\s+(.*)$");
    private static readonly Regex TableSeparatorCell = new(@"^:?-{2,}:?$");

    private static List<Block> ParsePlainBlocks(string text)
    {
        var lines = text.Split('\n');
        var blocks = new List<Block>();
        var paragraphLines = new List<string>();

        void FlushParagraph()
        {
            var content = string.Join("\n", paragraphLines).Trim();
            paragraphLines.Clear();
            if (content.Length == 0) return;
            var p = NewParagraph();
            AddInlines(content, p.Inlines);
            blocks.Add(p);
        }

        var i = 0;
        while (i < lines.Length)
        {
            var raw = lines[i];
            var trimmed = raw.Trim();
            Match m;
            if (trimmed.Length == 0) { FlushParagraph(); i++; continue; }
            if (trimmed is "---" or "***" or "___") { FlushParagraph(); blocks.Add(Divider()); i++; continue; }
            if ((m = HeadingRegex.Match(trimmed)).Success)
            {
                FlushParagraph();
                var level = m.Groups[1].Value.Length;
                var p = NewParagraph();
                p.FontSize = FontScale.Of(level switch { 1 => 21, 2 => 18.5, 3 => 16.5, 4 => 14.5, _ => 13.5 });
                p.FontWeight = FontWeights.Bold;
                AddInlines(m.Groups[2].Value, p.Inlines);
                blocks.Add(p);
                i++; continue;
            }
            if (trimmed.StartsWith('>'))
            {
                FlushParagraph();
                var p = NewParagraph();
                p.FontStyle = FontStyles.Italic;
                p.Margin = new Thickness(10, 2, 0, 6);
                p.SetResourceReference(TextElement.ForegroundProperty, "TextMutedBrush");
                AddInlines(trimmed.TrimStart('>').Trim(), p.Inlines);
                blocks.Add(p);
                i++; continue;
            }
            if ((m = CheckRegex.Match(trimmed)).Success)
            {
                FlushParagraph();
                var isChecked = m.Groups[1].Value is "x" or "X";
                var p = NewParagraph();
                AddInlines((isChecked ? "☑  " : "☐  ") + m.Groups[2].Value, p.Inlines);
                blocks.Add(p);
                i++; continue;
            }
            if ((m = ImageRegex.Match(trimmed)).Success)
            {
                FlushParagraph();
                blocks.Add(ImageBlock(m.Groups[2].Value, m.Groups[3].Success ? m.Groups[3].Value : null));
                i++; continue;
            }
            if (trimmed.StartsWith("$$") && trimmed.EndsWith("$$") && trimmed.Length > 4)
            {
                // Формулы LaTeX ($.../$$...$$) — без полноценного typesetting (потребовал бы
                // отдельную библиотеку, непропорционально тяжело для новостной ленты), как и в
                // Android-оригинале: показываем как есть моноширинным текстом.
                FlushParagraph();
                var p = NewParagraph();
                p.TextAlignment = TextAlignment.Center;
                p.Inlines.Add(new Run(trimmed[2..^2].Trim()) { FontFamily = new FontFamily("Consolas") });
                blocks.Add(p);
                i++; continue;
            }
            if (trimmed.StartsWith("- "))
            {
                FlushParagraph();
                var p = NewParagraph();
                AddInlines("•  " + trimmed[2..].Trim(), p.Inlines);
                blocks.Add(p);
                i++; continue;
            }
            if ((m = OrderedRegex.Match(trimmed)).Success)
            {
                FlushParagraph();
                var p = NewParagraph();
                AddInlines($"{m.Groups[1].Value}.  {m.Groups[2].Value}", p.Inlines);
                blocks.Add(p);
                i++; continue;
            }
            if (trimmed.StartsWith('|') && trimmed.EndsWith('|') && trimmed.Length > 1)
            {
                FlushParagraph();
                var tableLines = new List<string> { trimmed };
                var j = i + 1;
                while (j < lines.Length)
                {
                    var t = lines[j].Trim();
                    if (!t.StartsWith('|') || !t.EndsWith('|') || t.Length <= 1) break;
                    tableLines.Add(t);
                    j++;
                }
                var rows = tableLines.Select(l => l.Trim('|').Split('|').Select(c => c.Trim()).ToList()).ToList();
                var header = rows.Count > 0 ? rows[0] : new List<string>();
                var dataRows = rows.Skip(1).Where(row => !row.All(c => TableSeparatorCell.IsMatch(c))).ToList();
                blocks.Add(TableBlock(header, dataRows));
                i = j; continue;
            }
            paragraphLines.Add(raw);
            i++;
        }
        FlushParagraph();
        return blocks;
    }

    private static Paragraph NewParagraph()
    {
        var p = new Paragraph { Margin = new Thickness(0, 0, 0, 6), LineHeight = 17 };
        p.SetResourceReference(TextElement.ForegroundProperty, "TextPrimaryBrush");
        return p;
    }

    private static Block Divider()
    {
        var border = new Border { Height = 1, Margin = new Thickness(0, 6, 0, 6) };
        border.SetResourceReference(Border.BackgroundProperty, "CardBorderBrush");
        return new BlockUIContainer(border) { Margin = new Thickness(0) };
    }

    private static Block ImageBlock(string url, string? caption)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 6) };
        TryLoadImage(url, img => { img.MaxHeight = 220; img.HorizontalAlignment = HorizontalAlignment.Left; panel.Children.Add(img); });
        if (!string.IsNullOrWhiteSpace(caption))
        {
            var cap = new TextBlock { Text = caption, FontSize = FontScale.Of(10), Margin = new Thickness(0, 3, 0, 0) };
            cap.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            panel.Children.Add(cap);
        }
        return new BlockUIContainer(panel) { Margin = new Thickness(0) };
    }

    private static void TryLoadImage(string url, Action<Image> onLoaded)
    {
        try
        {
            var image = new Image { Source = new BitmapImage(new Uri(url, UriKind.Absolute)), Stretch = Stretch.Uniform };
            onLoaded(image);
        }
        catch { /* битый/относительный URL — best effort, просто пропускаем картинку */ }
    }

    private static Block TableBlock(List<string> header, List<List<string>> rows)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 6) };
        var columnCount = Math.Max(header.Count, rows.Count > 0 ? rows.Max(r => r.Count) : 0);
        for (var c = 0; c < columnCount; c++) table.Columns.Add(new TableColumn());

        var group = new TableRowGroup();
        if (header.Count > 0)
        {
            var row = new TableRow();
            foreach (var cell in header) row.Cells.Add(TableCellFor(cell, bold: true));
            group.Rows.Add(row);
        }
        foreach (var dataRow in rows)
        {
            var row = new TableRow();
            foreach (var cell in dataRow) row.Cells.Add(TableCellFor(cell, bold: false));
            group.Rows.Add(row);
        }
        table.RowGroups.Add(group);
        return table;
    }

    private static TableCell TableCellFor(string text, bool bold)
    {
        var p = new Paragraph { FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, FontSize = FontScale.Of(11), Margin = new Thickness(0) };
        p.SetResourceReference(TextElement.ForegroundProperty, "TextPrimaryBrush");
        AddInlines(text, p.Inlines);
        var cell = new TableCell(p) { Padding = new Thickness(6, 4, 6, 4), BorderThickness = new Thickness(0, 0, 0, 1) };
        cell.SetResourceReference(TableCell.BorderBrushProperty, "CardBorderBrush");
        return cell;
    }

    private static Block DetailsBlock(string summaryRaw, string bodyRaw)
    {
        var header = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Bold, FontSize = FontScale.Of(12.5) };
        header.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        AddInlines(summaryRaw, header.Inlines);

        var innerDoc = new FlowDocument { PagePadding = new Thickness(0), FontSize = FontScale.Of(12.5) };
        foreach (var block in ParsePlainBlocks(bodyRaw)) innerDoc.Blocks.Add(block);
        var innerBox = new RichTextBox
        {
            IsReadOnly = true, IsDocumentEnabled = true, BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, Padding = new Thickness(14, 4, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        RichTextBoxBehavior.SetAttachedDocument(innerBox, innerDoc);

        var expander = new Expander { Header = header, Content = innerBox, Margin = new Thickness(0, 2, 0, 6) };
        return new BlockUIContainer(expander) { Margin = new Thickness(0) };
    }

    private static Block CollageBlock(List<string> urls)
    {
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 2, 0, 6) };
        foreach (var url in urls)
            TryLoadImage(url, img => { img.Stretch = Stretch.UniformToFill; img.Height = 90; img.Margin = new Thickness(2); grid.Children.Add(img); });
        return new BlockUIContainer(grid) { Margin = new Thickness(0) };
    }

    private static Block SlideshowBlock(List<string> urls)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 6) };
        if (urls.Count == 0) return new BlockUIContainer(panel) { Margin = new Thickness(0) };

        var index = 0;
        var image = new Image { Stretch = Stretch.Uniform, MaxHeight = 220, HorizontalAlignment = HorizontalAlignment.Left };
        var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };

        void Refresh()
        {
            TryLoadImage(urls[index], loaded => image.Source = loaded.Source);
            dots.Children.Clear();
            for (var d = 0; d < urls.Count; d++)
            {
                dots.Children.Add(new Ellipse
                {
                    Width = 6, Height = 6, Margin = new Thickness(3, 0, 3, 0),
                    Fill = new SolidColorBrush(d == index ? Color.FromRgb(0x07, 0x6A, 0x62) : Color.FromRgb(0xE7, 0xDF, 0xCA))
                });
            }
        }
        Refresh();
        panel.Children.Add(image);

        if (urls.Count > 1)
        {
            var nav = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            var prev = new Button { Content = "‹", Width = 30, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 8, 0) };
            var next = new Button { Content = "›", Width = 30, Cursor = Cursors.Hand };
            prev.Click += (_, _) => { index = (index - 1 + urls.Count) % urls.Count; Refresh(); };
            next.Click += (_, _) => { index = (index + 1) % urls.Count; Refresh(); };
            nav.Children.Add(prev);
            nav.Children.Add(next);
            panel.Children.Add(nav);
        }
        panel.Children.Add(dots);
        return new BlockUIContainer(panel) { Margin = new Thickness(0) };
    }

    private static Block MapBlock(double lat, double lon, int? zoom)
    {
        var text = new TextBlock
        {
            Text = $"📍 {lat.ToString(CultureInfo.InvariantCulture)}, {lon.ToString(CultureInfo.InvariantCulture)}",
            FontWeight = FontWeights.Bold, FontSize = FontScale.Of(11.5)
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TealDeepBrush");
        var border = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Cursor = Cursors.Hand, Margin = new Thickness(0, 2, 0, 6), Child = text };
        border.SetResourceReference(Border.BackgroundProperty, "TealTintBrush");
        // Windows не регистрирует Android-подобную схему geo: — Windows Maps (штатное
        // приложение) открывается через собственную URI-схему bingmaps:.
        border.MouseLeftButtonUp += (_, _) => OpenUrl(
            $"bingmaps:?cp={lat.ToString(CultureInfo.InvariantCulture)}~{lon.ToString(CultureInfo.InvariantCulture)}&lvl={zoom ?? 14}");
        return new BlockUIContainer(border) { Margin = new Thickness(0) };
    }

    // ── Инлайны ───────────────────────────────────────────────────────────

    private static readonly Regex LinkRegex = new(@"^\[([^\]]+)]\(([^)\s]+)\)");
    private static readonly Regex HtmlOpenTagRegex = new(@"^<([a-zA-Z][a-zA-Z0-9-]*)((?:\s+[a-zA-Z-]+=""[^""]*"")*)\s*/?>");
    private static readonly SolidColorBrush CodeBackground = new(Color.FromArgb(0x22, 0, 0, 0));
    private static readonly SolidColorBrush HighlightBackground = new(Color.FromArgb(0x55, 0x2F, 0xB3, 0x9A));
    private static readonly SolidColorBrush SpoilerBackground = new(Color.FromArgb(0xFF, 0x12, 0x31, 0x2C));

    private static void AddInlines(string text, InlineCollection target)
    {
        var buffer = new StringBuilder();
        void FlushBuffer() { if (buffer.Length > 0) { target.Add(new Run(buffer.ToString())); buffer.Clear(); } }

        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '<')
            {
                var afterTag = TryHtmlTag(text, i, target, FlushBuffer);
                if (afterTag != null) { i = afterTag.Value; continue; }
            }
            if (text.AsSpan(i).StartsWith("**"))
            {
                var end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end >= 0)
                {
                    FlushBuffer();
                    var bold = new Bold();
                    AddInlines(text[(i + 2)..end], bold.Inlines);
                    target.Add(bold);
                    i = end + 2;
                    continue;
                }
            }
            else if (text.AsSpan(i).StartsWith("~~"))
            {
                var end = text.IndexOf("~~", i + 2, StringComparison.Ordinal);
                if (end >= 0)
                {
                    FlushBuffer();
                    var span = new Span { TextDecorations = TextDecorations.Strikethrough };
                    AddInlines(text[(i + 2)..end], span.Inlines);
                    target.Add(span);
                    i = end + 2;
                    continue;
                }
            }
            else if (text.AsSpan(i).StartsWith("||"))
            {
                var end = text.IndexOf("||", i + 2, StringComparison.Ordinal);
                if (end >= 0)
                {
                    FlushBuffer();
                    AddSpoiler(text[(i + 2)..end], target);
                    i = end + 2;
                    continue;
                }
            }
            else if (text.AsSpan(i).StartsWith("=="))
            {
                var end = text.IndexOf("==", i + 2, StringComparison.Ordinal);
                if (end >= 0)
                {
                    FlushBuffer();
                    var span = new Span { Background = HighlightBackground };
                    AddInlines(text[(i + 2)..end], span.Inlines);
                    target.Add(span);
                    i = end + 2;
                    continue;
                }
            }
            else if (c == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end >= 0)
                {
                    FlushBuffer();
                    var run = new Run(text[(i + 1)..end]) { FontFamily = new FontFamily("Consolas"), Background = CodeBackground };
                    target.Add(run);
                    i = end + 1;
                    continue;
                }
            }
            else if (c == '$' && !text.AsSpan(i).StartsWith("$$"))
            {
                // Инлайн-формула LaTeX — как и блочная $$...$$ (см. ParsePlainBlocks), без
                // typesetting, просто моноширинным текстом.
                var end = text.IndexOf('$', i + 1);
                if (end >= 0)
                {
                    FlushBuffer();
                    target.Add(new Run(text[(i + 1)..end]) { FontFamily = new FontFamily("Consolas") });
                    i = end + 1;
                    continue;
                }
            }
            else if (text.AsSpan(i).StartsWith("[^"))
            {
                var end = text.IndexOf(']', i + 2);
                if (end >= 0)
                {
                    FlushBuffer();
                    var id = text[(i + 2)..end];
                    var run = new Run($"[{id}]") { FontSize = FontScale.Of(9), BaselineAlignment = BaselineAlignment.Superscript };
                    run.SetResourceReference(TextElement.ForegroundProperty, "TealDeepBrush");
                    target.Add(run);
                    i = end + 1;
                    continue;
                }
            }
            else if (c == '[')
            {
                var m = LinkRegex.Match(text, i);
                if (m.Success && m.Index == i)
                {
                    FlushBuffer();
                    var url = m.Groups[2].Value;
                    var link = new Hyperlink { TextDecorations = TextDecorations.Underline };
                    link.SetResourceReference(TextElement.ForegroundProperty, "TealDeepBrush");
                    AddInlines(m.Groups[1].Value, link.Inlines);
                    link.Click += (_, _) => OpenUrl(url);
                    target.Add(link);
                    i = m.Index + m.Length;
                    continue;
                }
            }
            else if (c == '*')
            {
                var end = text.IndexOf('*', i + 1);
                if (end >= 0)
                {
                    FlushBuffer();
                    var italic = new Italic();
                    AddInlines(text[(i + 1)..end], italic.Inlines);
                    target.Add(italic);
                    i = end + 1;
                    continue;
                }
            }
            buffer.Append(c);
            i++;
        }
        FlushBuffer();
    }

    /// <summary>Спойлер с тап-раскрытием — текст спрятан за сплошной заливкой (как в Android:
    /// цвет текста Transparent + фон Ink), по клику раскрывается насовсем.</summary>
    private static void AddSpoiler(string inner, InlineCollection target)
    {
        var span = new Span { Cursor = Cursors.Hand, Background = SpoilerBackground, Foreground = Brushes.Transparent };
        AddInlines(inner, span.Inlines);
        var revealed = false;
        span.MouseLeftButtonDown += (_, e) =>
        {
            if (revealed) return;
            revealed = true;
            span.Background = Brushes.Transparent;
            span.ClearValue(TextElement.ForegroundProperty);
            span.SetResourceReference(TextElement.ForegroundProperty, "TextPrimaryBrush");
            e.Handled = true;
        };
        target.Add(span);
    }

    /// <returns>Индекс сразу после обработанного тега, или null — если по этой позиции
    /// распознаваемого тега нет (тогда символ '&lt;' обрабатывается как обычный текст).</returns>
    private static int? TryHtmlTag(string text, int i, InlineCollection target, Action flushBuffer)
    {
        var openMatch = HtmlOpenTagRegex.Match(text[i..]);
        if (!openMatch.Success) return null;
        var tag = openMatch.Groups[1].Value.ToLowerInvariant();
        var attrs = openMatch.Groups[2].Value;
        var afterOpen = i + openMatch.Value.Length;

        if (tag == "br") { flushBuffer(); target.Add(new LineBreak()); return afterOpen; }
        if (openMatch.Value.EndsWith("/>")) return afterOpen;

        var closeTag = $"</{tag}>";
        var closeIdx = text.IndexOf(closeTag, afterOpen, StringComparison.OrdinalIgnoreCase);
        if (closeIdx < 0) return afterOpen;
        var inner = text[afterOpen..closeIdx];
        var next = closeIdx + closeTag.Length;
        flushBuffer();

        switch (tag)
        {
            case "b" or "strong":
                { var el = new Bold(); AddInlines(inner, el.Inlines); target.Add(el); break; }
            case "i" or "em":
                { var el = new Italic(); AddInlines(inner, el.Inlines); target.Add(el); break; }
            case "u" or "ins":
                { var el = new Underline(); AddInlines(inner, el.Inlines); target.Add(el); break; }
            case "s" or "strike" or "del":
                { var el = new Span { TextDecorations = TextDecorations.Strikethrough }; AddInlines(inner, el.Inlines); target.Add(el); break; }
            case "code":
                target.Add(new Run(inner) { FontFamily = new FontFamily("Consolas"), Background = CodeBackground });
                break;
            case "pre":
                target.Add(new Run(inner) { FontFamily = new FontFamily("Consolas") });
                break;
            case "tg-spoiler" or "spoiler":
                AddSpoiler(inner, target);
                break;
            case "a":
                {
                    var hrefMatch = Regex.Match(attrs, "href=\"([^\"]*)\"", RegexOptions.IgnoreCase);
                    var link = new Hyperlink { TextDecorations = TextDecorations.Underline };
                    link.SetResourceReference(TextElement.ForegroundProperty, "TealDeepBrush");
                    AddInlines(inner, link.Inlines);
                    if (hrefMatch.Success)
                    {
                        var url = hrefMatch.Groups[1].Value;
                        link.Click += (_, _) => OpenUrl(url);
                    }
                    target.Add(link);
                    break;
                }
            default:
                AddInlines(inner, target);
                break;
        }
        return next;
    }

    private static void OpenUrl(string url) => UrlLauncher.TryOpen(url);
}
