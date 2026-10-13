namespace GodjiVpn.Services;

/// <summary>
/// country — ровно как properties.name слоя стран в world-atlas (для подсветки на глобусе).
/// code — настоящий ISO 3166-1 alpha-2 (нужен для flagEmoji). ruName/ruPrep — русское название
/// в именительном/предложном падеже ("Германия"/"Германии" — для фраз вида "Ты в Германии").
/// Портировано 1:1 из geo/CountryGeoLookup.kt (канонический Android-источник).
/// </summary>
public sealed record CountryGeo(
    string Country, double Lat, double Lon, string Lang, string Code,
    string RuName, string RuPrep, string City);

/// <summary>Определяет страну сервера по подстроке в его remark (название сервера в подписке
/// почти всегда просто "Germany"/"Netherlands" и т.п., без структурированных данных).</summary>
public static class CountryGeoLookup
{
    private static readonly (string[] Aliases, CountryGeo Geo)[] Entries =
    [
        (["netherlands", "нидерланды", "amsterdam", "амстердам"], new CountryGeo("Netherlands", 52.37, 4.90, "nl", "NL", "Нидерланды", "Нидерландах", "Амстердам")),
        (["germany", "германия", "frankfurt", "франкфурт", "berlin", "берлин", "munich", "мюнхен"], new CountryGeo("Germany", 50.11, 8.68, "de", "DE", "Германия", "Германии", "Франкфурт")),
        (["finland", "финляндия", "helsinki", "хельсинки"], new CountryGeo("Finland", 60.17, 24.94, "fi", "FI", "Финляндия", "Финляндии", "Хельсинки")),
        // "lte" — узел автовыбора LTE у этого бэкенда всегда выходит через российскую сеть.
        (["russia", "россия", "moscow", "москва", "petersburg", "петербург", "спб", "lte"], new CountryGeo("Russia", 59.94, 30.31, "ru", "RU", "Россия", "России", "Санкт-Петербург")),
        (["turkey", "турция", "istanbul", "стамбул", "ankara", "анкара"], new CountryGeo("Turkey", 41.01, 28.98, "tr", "TR", "Турция", "Турции", "Стамбул")),
        (["united states", "usa", "сша", "new york", "нью-йорк", "los angeles", "лос-анджелес", "miami", "майами"], new CountryGeo("United States of America", 40.71, -74.01, "en", "US", "США", "США", "Нью-Йорк")),
        (["japan", "япония", "tokyo", "токио"], new CountryGeo("Japan", 35.68, 139.77, "ja", "JP", "Япония", "Японии", "Токио")),
        (["united kingdom", "англия", "великобритания", "london", "лондон", "uk"], new CountryGeo("United Kingdom", 51.51, -0.13, "en", "GB", "Великобритания", "Великобритании", "Лондон")),
        (["france", "франция", "paris", "париж"], new CountryGeo("France", 48.85, 2.35, "fr", "FR", "Франция", "Франции", "Париж")),
        (["poland", "польша", "warsaw", "варшава"], new CountryGeo("Poland", 52.23, 21.01, "pl", "PL", "Польша", "Польше", "Варшава")),
        (["sweden", "швеция", "stockholm", "стокгольм"], new CountryGeo("Sweden", 59.33, 18.07, "sv", "SE", "Швеция", "Швеции", "Стокгольм")),
        (["norway", "норвегия", "oslo", "осло"], new CountryGeo("Norway", 59.91, 10.75, "no", "NO", "Норвегия", "Норвегии", "Осло")),
        (["italy", "италия", "milan", "милан", "rome", "рим"], new CountryGeo("Italy", 45.46, 9.19, "it", "IT", "Италия", "Италии", "Милан")),
        (["spain", "испания", "madrid", "мадрид"], new CountryGeo("Spain", 40.42, -3.70, "es", "ES", "Испания", "Испании", "Мадрид")),
        (["ukraine", "украина", "kyiv", "kiev", "киев"], new CountryGeo("Ukraine", 50.45, 30.52, "uk", "UA", "Украина", "Украине", "Киев")),
        (["kazakhstan", "казахстан", "almaty", "алматы"], new CountryGeo("Kazakhstan", 43.24, 76.95, "kk", "KZ", "Казахстан", "Казахстане", "Алматы")),
        (["cyprus", "кипр"], new CountryGeo("Cyprus", 35.19, 33.38, "el", "CY", "Кипр", "Кипре", "Никосия")),
        (["latvia", "латвия", "riga", "рига"], new CountryGeo("Latvia", 56.95, 24.11, "lv", "LV", "Латвия", "Латвии", "Рига")),
        (["lithuania", "литва", "vilnius", "вильнюс"], new CountryGeo("Lithuania", 54.69, 25.28, "lt", "LT", "Литва", "Литве", "Вильнюс")),
        (["estonia", "эстония", "tallinn", "таллин"], new CountryGeo("Estonia", 59.44, 24.75, "et", "EE", "Эстония", "Эстонии", "Таллин")),
        (["switzerland", "швейцария", "zurich", "цюрих"], new CountryGeo("Switzerland", 47.38, 8.54, "de", "CH", "Швейцария", "Швейцарии", "Цюрих")),
        (["austria", "австрия", "vienna", "вена"], new CountryGeo("Austria", 48.21, 16.37, "de", "AT", "Австрия", "Австрии", "Вена")),
        (["czech", "чехия", "prague", "прага"], new CountryGeo("Czechia", 50.09, 14.42, "cs", "CZ", "Чехия", "Чехии", "Прага")),
        (["bulgaria", "болгария", "sofia", "софия"], new CountryGeo("Bulgaria", 42.70, 23.32, "bg", "BG", "Болгария", "Болгарии", "София")),
        (["romania", "румыния", "bucharest", "бухарест"], new CountryGeo("Romania", 44.43, 26.10, "ro", "RO", "Румыния", "Румынии", "Бухарест")),
        (["singapore", "сингапур"], new CountryGeo("Singapore", 1.35, 103.82, "en", "SG", "Сингапур", "Сингапуре", "Сингапур")),
        (["india", "индия"], new CountryGeo("India", 28.61, 77.21, "hi", "IN", "Индия", "Индии", "Дели")),
        (["canada", "канада", "toronto", "торонто"], new CountryGeo("Canada", 43.65, -79.38, "en", "CA", "Канада", "Канаде", "Торонто")),
        (["brazil", "бразилия"], new CountryGeo("Brazil", -23.55, -46.63, "pt", "BR", "Бразилия", "Бразилии", "Сан-Паулу")),
        (["south korea", "корея", "seoul", "сеул"], new CountryGeo("South Korea", 37.57, 126.98, "ko", "KR", "Южная Корея", "Южной Корее", "Сеул")),
        (["hong kong", "гонконг", "china", "китай"], new CountryGeo("China", 39.90, 116.41, "zh", "CN", "Китай", "Китае", "Пекин")),
        (["armenia", "армения", "yerevan", "ереван"], new CountryGeo("Armenia", 40.18, 44.51, "hy", "AM", "Армения", "Армении", "Ереван")),
        (["georgia", "грузия", "tbilisi", "тбилиси"], new CountryGeo("Georgia", 41.72, 44.79, "ka", "GE", "Грузия", "Грузии", "Тбилиси")),
        (["azerbaijan", "азербайджан", "baku", "баку"], new CountryGeo("Azerbaijan", 40.41, 49.87, "az", "AZ", "Азербайджан", "Азербайджане", "Баку")),
        (["ireland", "ирландия", "dublin", "дублин"], new CountryGeo("Ireland", 53.35, -6.26, "en", "IE", "Ирландия", "Ирландии", "Дублин")),
        (["belgium", "бельгия", "brussels", "брюссель"], new CountryGeo("Belgium", 50.85, 4.35, "fr", "BE", "Бельгия", "Бельгии", "Брюссель")),
        (["hungary", "венгрия", "budapest", "будапешт"], new CountryGeo("Hungary", 47.50, 19.04, "hu", "HU", "Венгрия", "Венгрии", "Будапешт")),
        (["greece", "греция", "athens", "афины"], new CountryGeo("Greece", 37.98, 23.73, "el", "GR", "Греция", "Греции", "Афины")),
        (["portugal", "португалия", "lisbon", "лиссабон"], new CountryGeo("Portugal", 38.72, -9.14, "pt", "PT", "Португалия", "Португалии", "Лиссабон")),
    ];

    public static CountryGeo? Find(string remark)
    {
        var lower = remark.ToLowerInvariant();
        foreach (var (aliases, geo) in Entries)
            if (aliases.Any(lower.Contains))
                return geo;
        return null;
    }

    /// <summary>Эмодзи-флаг из ISO alpha-2 через regional indicator symbols — без картинок,
    /// рендерится системным эмодзи-шрифтом (Segoe UI Emoji на Windows 10/11).</summary>
    public static string FlagEmoji(string isoCode)
    {
        if (isoCode.Length != 2) return "🌐";
        const int Base = 0x1F1E6;
        var first = Base + (char.ToUpperInvariant(isoCode[0]) - 'A');
        var second = Base + (char.ToUpperInvariant(isoCode[1]) - 'A');
        if (first is < 0x1F1E6 or > 0x1F1FF || second is < 0x1F1E6 or > 0x1F1FF) return "🌐";
        return char.ConvertFromUtf32(first) + char.ConvertFromUtf32(second);
    }
}
