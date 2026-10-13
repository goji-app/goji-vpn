using System.Net;
using System.Text.RegularExpressions;

namespace GodjiVpn.Utils;

/// <summary>
/// Текст новостей и описаний обновлений приходит в "Rich Markdown" + Telegram-HTML (см.
/// RichContent Windows-клиента). В macOS-клиенте — упрощённо: разметка снимается, остаётся
/// читаемый текст с абзацами и маркерами списков.
/// </summary>
public static partial class RichText
{
    public static string ToPlain(string? raw, int maxParagraphs = int.MaxValue)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw.Replace("\r\n", "\n");
        s = BrTag().Replace(s, "\n");
        s = MdLink().Replace(s, "$1");
        s = HtmlTag().Replace(s, "");
        s = WebUtility.HtmlDecode(s);
        s = Emphasis().Replace(s, "");
        s = Heading().Replace(s, "");
        s = ListMarker().Replace(s, "• ");
        s = Quote().Replace(s, "");
        s = MultiBlank().Replace(s, "\n\n").Trim();
        if (maxParagraphs == int.MaxValue) return s;
        var paragraphs = s.Split("\n\n");
        return paragraphs.Length <= maxParagraphs ? s : string.Join("\n\n", paragraphs.Take(maxParagraphs)) + "…";
    }

    public static int ParagraphCount(string? raw) => string.IsNullOrWhiteSpace(raw) ? 0 : ToPlain(raw).Split("\n\n").Length;

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)] private static partial Regex BrTag();
    [GeneratedRegex(@"!?\[([^\]]*)\]\([^)]*\)")] private static partial Regex MdLink();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex HtmlTag();
    [GeneratedRegex(@"\*\*|__|~~|\|\||==|`")] private static partial Regex Emphasis();
    [GeneratedRegex(@"(?m)^#{1,6}\s*")] private static partial Regex Heading();
    [GeneratedRegex(@"(?m)^\s*(?:[-*]|\d+\.)\s+(?:\[[ xX]\]\s*)?")] private static partial Regex ListMarker();
    [GeneratedRegex(@"(?m)^>\s?")] private static partial Regex Quote();
    [GeneratedRegex(@"\n{3,}")] private static partial Regex MultiBlank();
}
