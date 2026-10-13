namespace GodjiVpn.Utils;

public static class RemarkText
{
    /// <summary>Remark из подписки часто начинается с флага-эмодзи (иногда с "⚡" следом,
    /// вроде "🇩🇪 Germany" или "🇸🇴⚡ Автовыбор серверов EU") — раз флаг показываем отдельным
    /// бейджем (см. CountryGeoLookup.FlagEmoji), из заголовка его убираем, чтобы не дублировать.
    /// Портировано 1:1 из util/RemarkText.kt.
    ///
    /// Кодпоинты нужно перебирать НАПРЯМУЮ (как Kotlin-оригинал через text.codePoints()), а не
    /// через StringInfo.GetTextElementEnumerator — тот группирует flag-эмодзи (два regional
    /// indicator подряд) в ОДИН grapheme-элемент, из-за чего проверка "это два regional
    /// indicator подряд" ниже всегда проваливалась и функция молча возвращала текст как есть
    /// (реальный симптом — заголовки узлов вида "DE Германия" вместо "Германия").</summary>
    public static string StripLeadingFlag(string text)
    {
        var codePoints = new List<int>();
        var utf16Lengths = new List<int>();
        var i = 0;
        while (i < text.Length)
        {
            var len = char.IsSurrogatePair(text, i) ? 2 : 1;
            codePoints.Add(char.ConvertToUtf32(text, i));
            utf16Lengths.Add(len);
            i += len;
        }

        var idx = 0;
        while (idx < codePoints.Count && codePoints[idx] <= 0xFFFF && char.IsWhiteSpace((char)codePoints[idx])) idx++;

        static bool IsRegionalIndicator(int cp) => cp is >= 0x1F1E6 and <= 0x1F1FF;

        if (idx + 1 >= codePoints.Count || !IsRegionalIndicator(codePoints[idx]) || !IsRegionalIndicator(codePoints[idx + 1]))
            return text;

        idx += 2;
        while (idx < codePoints.Count && (codePoints[idx] == '⚡' || codePoints[idx] == 0xFE0F ||
                                           (codePoints[idx] <= 0xFFFF && char.IsWhiteSpace((char)codePoints[idx]))))
            idx++;

        var utf16Start = utf16Lengths.Take(idx).Sum();
        return text[utf16Start..];
    }
}
