import Foundation

/// Тексты новостей и ответов поддержки приходят в Telegram-подобной HTML-разметке
/// (<b>, <i>, <a href>, <br>) или в Markdown. Переводим в AttributedString (Markdown) —
/// без WebView и без тяжёлого HTML-импорта NSAttributedString.
enum RichText {
    static func attributed(_ source: String) -> AttributedString {
        let markdown = toMarkdown(source)
        let options = AttributedString.MarkdownParsingOptions(interpretedSyntax: .inlineOnlyPreservingWhitespace)
        return (try? AttributedString(markdown: markdown, options: options)) ?? AttributedString(plain(source))
    }

    static func plain(_ source: String) -> String {
        var text = source.replacingOccurrences(of: "<br\\s*/?>", with: "\n", options: [.regularExpression, .caseInsensitive])
        text = text.replacingOccurrences(of: "<[^>]+>", with: "", options: .regularExpression)
        text = decodeEntities(text)
        text = text.replacingOccurrences(of: "\\*\\*|__", with: "", options: .regularExpression)
        return text.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    static func toMarkdown(_ source: String) -> String {
        var text = source
        let replacements: [(String, String)] = [
            ("<br\\s*/?>", "\n"),
            ("</?(b|strong)>", "**"),
            ("</?(i|em)>", "_"),
            ("</?(s|strike|del)>", "~~"),
            ("</?code>", "`"),
            ("<a\\s+href=\"([^\"]*)\"[^>]*>(.*?)</a>", "[$2]($1)"),
            ("</?(p|div|span|u|tg-spoiler|blockquote|pre)[^>]*>", "")
        ]
        for (pattern, template) in replacements {
            text = text.replacingOccurrences(of: pattern, with: template, options: [.regularExpression, .caseInsensitive])
        }
        return decodeEntities(text)
    }

    private static func decodeEntities(_ s: String) -> String {
        s.replacingOccurrences(of: "&lt;", with: "<")
            .replacingOccurrences(of: "&gt;", with: ">")
            .replacingOccurrences(of: "&quot;", with: "\"")
            .replacingOccurrences(of: "&#39;", with: "'")
            .replacingOccurrences(of: "&nbsp;", with: " ")
            .replacingOccurrences(of: "&amp;", with: "&")
    }
}
