import Foundation

/// Страна узла, определённая по тексту remark из подписки (порт CountryGeoLookup.kt).
/// [country] — английское имя, совпадает с ключом полигона в geo_globe.json.
struct CountryGeo: Codable, Equatable, Hashable {
    let country: String
    let lat: Double
    let lon: Double
    let lang: String
    let code: String
    let ruName: String
    let ruPrep: String
    let city: String
    let enCity: String

    var displayName: String { AppLanguage.isRussian ? ruName : country }
    var displayCity: String { AppLanguage.isRussian ? city : enCity }
    var flag: String { CountryGeoLookup.flag(code) }
}

enum CountryGeoLookup {
    private static let entries: [([String], CountryGeo)] = [
        (["netherlands", "нидерланды", "amsterdam", "амстердам"], CountryGeo(country: "Netherlands", lat: 52.37, lon: 4.90, lang: "nl", code: "NL", ruName: "Нидерланды", ruPrep: "Нидерландах", city: "Амстердам", enCity: "Amsterdam")),
        (["germany", "германия", "frankfurt", "франкфурт", "berlin", "берлин", "munich", "мюнхен"], CountryGeo(country: "Germany", lat: 50.11, lon: 8.68, lang: "de", code: "DE", ruName: "Германия", ruPrep: "Германии", city: "Франкфурт", enCity: "Frankfurt")),
        (["finland", "финляндия", "helsinki", "хельсинки"], CountryGeo(country: "Finland", lat: 60.17, lon: 24.94, lang: "fi", code: "FI", ruName: "Финляндия", ruPrep: "Финляндии", city: "Хельсинки", enCity: "Helsinki")),
        // «lte» — узел автовыбора LTE у этого бэкенда выходит через российскую сеть.
        (["russia", "россия", "moscow", "москва", "petersburg", "петербург", "спб", "lte"], CountryGeo(country: "Russia", lat: 59.94, lon: 30.31, lang: "ru", code: "RU", ruName: "Россия", ruPrep: "России", city: "Санкт-Петербург", enCity: "Saint Petersburg")),
        (["turkey", "турция", "istanbul", "стамбул", "ankara", "анкара"], CountryGeo(country: "Turkey", lat: 41.01, lon: 28.98, lang: "tr", code: "TR", ruName: "Турция", ruPrep: "Турции", city: "Стамбул", enCity: "Istanbul")),
        (["united states", "usa", "сша", "new york", "нью-йорк", "los angeles", "лос-анджелес", "miami", "майами"], CountryGeo(country: "United States of America", lat: 40.71, lon: -74.01, lang: "en", code: "US", ruName: "США", ruPrep: "США", city: "Нью-Йорк", enCity: "New York")),
        (["japan", "япония", "tokyo", "токио"], CountryGeo(country: "Japan", lat: 35.68, lon: 139.77, lang: "ja", code: "JP", ruName: "Япония", ruPrep: "Японии", city: "Токио", enCity: "Tokyo")),
        (["united kingdom", "англия", "великобритания", "london", "лондон", "uk"], CountryGeo(country: "United Kingdom", lat: 51.51, lon: -0.13, lang: "en", code: "GB", ruName: "Великобритания", ruPrep: "Великобритании", city: "Лондон", enCity: "London")),
        (["france", "франция", "paris", "париж"], CountryGeo(country: "France", lat: 48.85, lon: 2.35, lang: "fr", code: "FR", ruName: "Франция", ruPrep: "Франции", city: "Париж", enCity: "Paris")),
        (["poland", "польша", "warsaw", "варшава"], CountryGeo(country: "Poland", lat: 52.23, lon: 21.01, lang: "pl", code: "PL", ruName: "Польша", ruPrep: "Польше", city: "Варшава", enCity: "Warsaw")),
        (["sweden", "швеция", "stockholm", "стокгольм"], CountryGeo(country: "Sweden", lat: 59.33, lon: 18.07, lang: "sv", code: "SE", ruName: "Швеция", ruPrep: "Швеции", city: "Стокгольм", enCity: "Stockholm")),
        (["norway", "норвегия", "oslo", "осло"], CountryGeo(country: "Norway", lat: 59.91, lon: 10.75, lang: "no", code: "NO", ruName: "Норвегия", ruPrep: "Норвегии", city: "Осло", enCity: "Oslo")),
        (["italy", "италия", "milan", "милан", "rome", "рим"], CountryGeo(country: "Italy", lat: 45.46, lon: 9.19, lang: "it", code: "IT", ruName: "Италия", ruPrep: "Италии", city: "Милан", enCity: "Milan")),
        (["spain", "испания", "madrid", "мадрид"], CountryGeo(country: "Spain", lat: 40.42, lon: -3.70, lang: "es", code: "ES", ruName: "Испания", ruPrep: "Испании", city: "Мадрид", enCity: "Madrid")),
        (["ukraine", "украина", "kyiv", "kiev", "киев"], CountryGeo(country: "Ukraine", lat: 50.45, lon: 30.52, lang: "uk", code: "UA", ruName: "Украина", ruPrep: "Украине", city: "Киев", enCity: "Kyiv")),
        (["kazakhstan", "казахстан", "almaty", "алматы"], CountryGeo(country: "Kazakhstan", lat: 43.24, lon: 76.95, lang: "kk", code: "KZ", ruName: "Казахстан", ruPrep: "Казахстане", city: "Алматы", enCity: "Almaty")),
        (["cyprus", "кипр"], CountryGeo(country: "Cyprus", lat: 35.19, lon: 33.38, lang: "el", code: "CY", ruName: "Кипр", ruPrep: "Кипре", city: "Никосия", enCity: "Nicosia")),
        (["latvia", "латвия", "riga", "рига"], CountryGeo(country: "Latvia", lat: 56.95, lon: 24.11, lang: "lv", code: "LV", ruName: "Латвия", ruPrep: "Латвии", city: "Рига", enCity: "Riga")),
        (["lithuania", "литва", "vilnius", "вильнюс"], CountryGeo(country: "Lithuania", lat: 54.69, lon: 25.28, lang: "lt", code: "LT", ruName: "Литва", ruPrep: "Литве", city: "Вильнюс", enCity: "Vilnius")),
        (["estonia", "эстония", "tallinn", "таллин"], CountryGeo(country: "Estonia", lat: 59.44, lon: 24.75, lang: "et", code: "EE", ruName: "Эстония", ruPrep: "Эстонии", city: "Таллин", enCity: "Tallinn")),
        (["switzerland", "швейцария", "zurich", "цюрих"], CountryGeo(country: "Switzerland", lat: 47.38, lon: 8.54, lang: "de", code: "CH", ruName: "Швейцария", ruPrep: "Швейцарии", city: "Цюрих", enCity: "Zurich")),
        (["austria", "австрия", "vienna", "вена"], CountryGeo(country: "Austria", lat: 48.21, lon: 16.37, lang: "de", code: "AT", ruName: "Австрия", ruPrep: "Австрии", city: "Вена", enCity: "Vienna")),
        (["czech", "чехия", "prague", "прага"], CountryGeo(country: "Czechia", lat: 50.09, lon: 14.42, lang: "cs", code: "CZ", ruName: "Чехия", ruPrep: "Чехии", city: "Прага", enCity: "Prague")),
        (["bulgaria", "болгария", "sofia", "софия"], CountryGeo(country: "Bulgaria", lat: 42.70, lon: 23.32, lang: "bg", code: "BG", ruName: "Болгария", ruPrep: "Болгарии", city: "София", enCity: "Sofia")),
        (["romania", "румыния", "bucharest", "бухарест"], CountryGeo(country: "Romania", lat: 44.43, lon: 26.10, lang: "ro", code: "RO", ruName: "Румыния", ruPrep: "Румынии", city: "Бухарест", enCity: "Bucharest")),
        (["singapore", "сингапур"], CountryGeo(country: "Singapore", lat: 1.35, lon: 103.82, lang: "en", code: "SG", ruName: "Сингапур", ruPrep: "Сингапуре", city: "Сингапур", enCity: "Singapore")),
        (["india", "индия"], CountryGeo(country: "India", lat: 28.61, lon: 77.21, lang: "hi", code: "IN", ruName: "Индия", ruPrep: "Индии", city: "Дели", enCity: "Delhi")),
        (["canada", "канада", "toronto", "торонто"], CountryGeo(country: "Canada", lat: 43.65, lon: -79.38, lang: "en", code: "CA", ruName: "Канада", ruPrep: "Канаде", city: "Торонто", enCity: "Toronto")),
        (["brazil", "бразилия"], CountryGeo(country: "Brazil", lat: -23.55, lon: -46.63, lang: "pt", code: "BR", ruName: "Бразилия", ruPrep: "Бразилии", city: "Сан-Паулу", enCity: "Sao Paulo")),
        (["south korea", "корея", "seoul", "сеул"], CountryGeo(country: "South Korea", lat: 37.57, lon: 126.98, lang: "ko", code: "KR", ruName: "Южная Корея", ruPrep: "Южной Корее", city: "Сеул", enCity: "Seoul")),
        (["hong kong", "гонконг", "china", "китай"], CountryGeo(country: "China", lat: 39.90, lon: 116.41, lang: "zh", code: "CN", ruName: "Китай", ruPrep: "Китае", city: "Пекин", enCity: "Beijing")),
        (["armenia", "армения", "yerevan", "ереван"], CountryGeo(country: "Armenia", lat: 40.18, lon: 44.51, lang: "hy", code: "AM", ruName: "Армения", ruPrep: "Армении", city: "Ереван", enCity: "Yerevan")),
        (["georgia", "грузия", "tbilisi", "тбилиси"], CountryGeo(country: "Georgia", lat: 41.72, lon: 44.79, lang: "ka", code: "GE", ruName: "Грузия", ruPrep: "Грузии", city: "Тбилиси", enCity: "Tbilisi")),
        (["azerbaijan", "азербайджан", "baku", "баку"], CountryGeo(country: "Azerbaijan", lat: 40.41, lon: 49.87, lang: "az", code: "AZ", ruName: "Азербайджан", ruPrep: "Азербайджане", city: "Баку", enCity: "Baku")),
        (["ireland", "ирландия", "dublin", "дублин"], CountryGeo(country: "Ireland", lat: 53.35, lon: -6.26, lang: "en", code: "IE", ruName: "Ирландия", ruPrep: "Ирландии", city: "Дублин", enCity: "Dublin")),
        (["belgium", "бельгия", "brussels", "брюссель"], CountryGeo(country: "Belgium", lat: 50.85, lon: 4.35, lang: "fr", code: "BE", ruName: "Бельгия", ruPrep: "Бельгии", city: "Брюссель", enCity: "Brussels")),
        (["hungary", "венгрия", "budapest", "будапешт"], CountryGeo(country: "Hungary", lat: 47.50, lon: 19.04, lang: "hu", code: "HU", ruName: "Венгрия", ruPrep: "Венгрии", city: "Будапешт", enCity: "Budapest")),
        (["greece", "греция", "athens", "афины"], CountryGeo(country: "Greece", lat: 37.98, lon: 23.73, lang: "el", code: "GR", ruName: "Греция", ruPrep: "Греции", city: "Афины", enCity: "Athens")),
        (["portugal", "португалия", "lisbon", "лиссабон"], CountryGeo(country: "Portugal", lat: 38.72, lon: -9.14, lang: "pt", code: "PT", ruName: "Португалия", ruPrep: "Португалии", city: "Лиссабон", enCity: "Lisbon"))
    ]

    static func find(_ remark: String) -> CountryGeo? {
        let lower = remark.lowercased()
        return entries.first { aliases, _ in aliases.contains { lower.contains($0) } }?.1
    }

    /// Узел-регион вроде «Автовыбор серверов EU»: страны нет, но есть флаг Евросоюза.
    /// Отдельно от `find`, чтобы глобус и «Вы в …» не получили выдуманную страну.
    static func isEuRegion(_ remark: String) -> Bool {
        remark.range(of: #"(^|[^\p{L}])EU([^\p{L}]|$)|европ|europe"#, options: [.regularExpression, .caseInsensitive]) != nil
    }

    /// Эмодзи-флаг из ISO alpha-2 (пара regional indicator symbols).
    static func flag(_ code: String) -> String {
        let upper = code.uppercased()
        guard upper.count == 2 else { return "🌐" }
        var result = ""
        for scalar in upper.unicodeScalars {
            guard (65...90).contains(scalar.value),
                  let indicator = Unicode.Scalar(0x1F1E6 + scalar.value - 65) else { return "🌐" }
            result.unicodeScalars.append(indicator)
        }
        return result
    }

    /// Убирает эмодзи-флаг в начале названия узла («🇩🇪 Германия» → «Германия»).
    static func stripLeadingFlag(_ name: String) -> String {
        var rest = String.UnicodeScalarView()
        var skipping = true
        for scalar in name.unicodeScalars {
            if skipping, (0x1F1E6...0x1F1FF).contains(scalar.value) || scalar.properties.isEmojiPresentation || scalar.value == 0xFE0F {
                continue
            }
            skipping = false
            rest.append(scalar)
        }
        let stripped = String(rest).trimmingCharacters(in: .whitespaces)
        return stripped.isEmpty ? name : stripped
    }
}

struct Greeting {
    let hi: String
    let transliteration: String
    let language: String
    let useSerif: Bool
}

/// Приветствие на языке страны подключения (порт Greetings.kt).
enum Greetings {
    private static let table: [String: (String, String, String, String, Bool)] = [
        // код: (привет, транслитерация, язык по-русски, язык по-английски, serif)
        "en": ("Hello", "хелло", "английский", "English", true),
        "zh": ("你好", "ни хао", "китайский", "Chinese", false),
        "hi": ("नमस्ते", "намастэ", "хинди", "Hindi", false),
        "es": ("¡Hola!", "ола", "испанский", "Spanish", true),
        "fr": ("Bonjour", "бонжур", "французский", "French", true),
        "pt": ("Olá", "ола", "португальский", "Portuguese", true),
        "ru": ("Привет", "", "русский", "Russian", true),
        "de": ("Hallo", "халло", "немецкий", "German", true),
        "ja": ("こんにちは", "коннитива", "японский", "Japanese", false),
        "tr": ("Merhaba", "мерхаба", "турецкий", "Turkish", true),
        "ko": ("안녕하세요", "аннёнхасэё", "корейский", "Korean", false),
        "nl": ("Hallo", "халло", "нидерландский", "Dutch", true),
        "it": ("Ciao", "чао", "итальянский", "Italian", true),
        "fi": ("Hei", "хэй", "финский", "Finnish", true),
        "pl": ("Cześć", "чещч", "польский", "Polish", true),
        "sv": ("Hej", "хэй", "шведский", "Swedish", true),
        "no": ("Hei", "хэй", "норвежский", "Norwegian", true),
        "et": ("Tere", "тере", "эстонский", "Estonian", true),
        "lv": ("Sveiki", "свейки", "латышский", "Latvian", true),
        "lt": ("Labas", "лабас", "литовский", "Lithuanian", true),
        "cs": ("Ahoj", "агой", "чешский", "Czech", true),
        "bg": ("Здравей", "здравей", "болгарский", "Bulgarian", true),
        "ro": ("Bună", "буна", "румынский", "Romanian", true),
        "el": ("Γεια σου", "я су", "греческий", "Greek", false),
        "hu": ("Szia", "сия", "венгерский", "Hungarian", true),
        "uk": ("Привіт", "привит", "украинский", "Ukrainian", true),
        "ka": ("გამარჯობა", "гамарджоба", "грузинский", "Georgian", false),
        "hy": ("Բարև", "барев", "армянский", "Armenian", false),
        "az": ("Salam", "салам", "азербайджанский", "Azerbaijani", true),
        "kk": ("Сәлем", "сэлем", "казахский", "Kazakh", true)
    ]

    static func forLang(_ lang: String?) -> Greeting {
        let entry = table[lang ?? ""] ?? table["en"]!
        let ru = AppLanguage.isRussian
        return Greeting(
            hi: entry.0,
            transliteration: ru ? entry.1 : "",
            language: ru ? entry.2 : entry.3,
            useSerif: entry.4
        )
    }
}
