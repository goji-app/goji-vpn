package xyz.gojihub.vpn.geo

import xyz.gojihub.vpn.i18n.AppLanguage

/**
 * [country] — ровно как в properties.name слоя стран (geo_globe.json), чтобы можно было
 * напрямую подсветить нужный полигон на глобусе. [lang] — код для подбора приветствия.
 * [code] — настоящий ISO 3166-1 alpha-2 (не первые две буквы названия — "Netherlands".take(2)
 * даёт "NE", а не "NL") — нужен, чтобы правильно собрать эмодзи-флаг через flagEmoji().
 * [ruName] / [ruPrep] — русское название страны в именительном и предложном падеже
 * ("Германия" / "Германии" — для фраз вида "Ты в Германии"), т.к. везде в интерфейсе
 * страна должна отображаться по-русски, а не как ключ [country] для геометки глобуса.
 * [enCity] / [zhName] / [zhCity] — то же самое (город/страна) для англ./кит. интерфейса
 * (переключатель языка в Настройках) — [country] уже готовое английское название страны,
 * поэтому отдельного enName нет.
 */
data class CountryGeo(
    val country: String, val lat: Double, val lon: Double, val lang: String, val code: String,
    val ruName: String, val ruPrep: String, val city: String,
    val enCity: String, val zhName: String, val zhCity: String
) {
    fun displayName(appLang: AppLanguage): String = when (appLang) {
        AppLanguage.RU -> ruName
        AppLanguage.EN -> country
        AppLanguage.ZH -> zhName
    }

    fun displayCity(appLang: AppLanguage): String = when (appLang) {
        AppLanguage.RU -> city
        AppLanguage.EN -> enCity
        AppLanguage.ZH -> zhCity
    }

    /** "Город, Страна" в текущем языке интерфейса. */
    fun displayCityCountry(appLang: AppLanguage): String = "${displayCity(appLang)}, ${displayName(appLang)}"
}

/**
 * Название сервера в подписке (remark из vless-ссылки) — обычный текст вроде
 * "Amsterdam · NL" или "Стамбул", без структурированной страны/координат. Определяем
 * страну перебором алиасов (город/страна на рус. и англ.) по подстроке. Не нашли —
 * узел просто не получает геометку (не участвует в глобусе/приветствии), это ожидаемо.
 */
object CountryGeoLookup {

    // [city] — русское название конкретного города дата-центра, соответствующего lat/lon
    // записи (сами remark в подписке почти всегда содержат только страну — "Germany",
    // "Netherlands" и т.п., без города, — поэтому это не парсинг из remark, а подпись уже
    // заложенных в таблицу координат конкретного хаба).
    private val entries: List<Pair<List<String>, CountryGeo>> = listOf(
        listOf("netherlands", "нидерланды", "amsterdam", "амстердам") to CountryGeo("Netherlands", 52.37, 4.90, "nl", "NL", "Нидерланды", "Нидерландах", "Амстердам", "Amsterdam", "荷兰", "阿姆斯特丹"),
        listOf("germany", "германия", "frankfurt", "франкфурт", "berlin", "берлин", "munich", "мюнхен") to CountryGeo("Germany", 50.11, 8.68, "de", "DE", "Германия", "Германии", "Франкфурт", "Frankfurt", "德国", "法兰克福"),
        listOf("finland", "финляндия", "helsinki", "хельсинки") to CountryGeo("Finland", 60.17, 24.94, "fi", "FI", "Финляндия", "Финляндии", "Хельсинки", "Helsinki", "芬兰", "赫尔辛基"),
        // "lte" — узел автовыбора LTE у этого бэкенда всегда выходит через российскую сеть
        // (см. GoDji-панель), поэтому геометка/флаг/приветствие для него — Russia.
        listOf("russia", "россия", "moscow", "москва", "petersburg", "петербург", "спб", "lte") to CountryGeo("Russia", 59.94, 30.31, "ru", "RU", "Россия", "России", "Санкт-Петербург", "Saint Petersburg", "俄罗斯", "圣彼得堡"),
        listOf("turkey", "турция", "istanbul", "стамбул", "ankara", "анкара") to CountryGeo("Turkey", 41.01, 28.98, "tr", "TR", "Турция", "Турции", "Стамбул", "Istanbul", "土耳其", "伊斯坦布尔"),
        listOf("united states", "usa", "сша", "new york", "нью-йорк", "los angeles", "лос-анджелес", "miami", "майами") to CountryGeo("United States of America", 40.71, -74.01, "en", "US", "США", "США", "Нью-Йорк", "New York", "美国", "纽约"),
        listOf("japan", "япония", "tokyo", "токио") to CountryGeo("Japan", 35.68, 139.77, "ja", "JP", "Япония", "Японии", "Токио", "Tokyo", "日本", "东京"),
        listOf("united kingdom", "англия", "великобритания", "london", "лондон", "uk") to CountryGeo("United Kingdom", 51.51, -0.13, "en", "GB", "Великобритания", "Великобритании", "Лондон", "London", "英国", "伦敦"),
        listOf("france", "франция", "paris", "париж") to CountryGeo("France", 48.85, 2.35, "fr", "FR", "Франция", "Франции", "Париж", "Paris", "法国", "巴黎"),
        listOf("poland", "польша", "warsaw", "варшава") to CountryGeo("Poland", 52.23, 21.01, "pl", "PL", "Польша", "Польше", "Варшава", "Warsaw", "波兰", "华沙"),
        listOf("sweden", "швеция", "stockholm", "стокгольм") to CountryGeo("Sweden", 59.33, 18.07, "sv", "SE", "Швеция", "Швеции", "Стокгольм", "Stockholm", "瑞典", "斯德哥尔摩"),
        listOf("norway", "норвегия", "oslo", "осло") to CountryGeo("Norway", 59.91, 10.75, "no", "NO", "Норвегия", "Норвегии", "Осло", "Oslo", "挪威", "奥斯陆"),
        listOf("italy", "италия", "milan", "милан", "rome", "рим") to CountryGeo("Italy", 45.46, 9.19, "it", "IT", "Италия", "Италии", "Милан", "Milan", "意大利", "米兰"),
        listOf("spain", "испания", "madrid", "мадрид") to CountryGeo("Spain", 40.42, -3.70, "es", "ES", "Испания", "Испании", "Мадрид", "Madrid", "西班牙", "马德里"),
        listOf("ukraine", "украина", "kyiv", "kiev", "киев") to CountryGeo("Ukraine", 50.45, 30.52, "uk", "UA", "Украина", "Украине", "Киев", "Kyiv", "乌克兰", "基辅"),
        listOf("kazakhstan", "казахстан", "almaty", "алматы") to CountryGeo("Kazakhstan", 43.24, 76.95, "kk", "KZ", "Казахстан", "Казахстане", "Алматы", "Almaty", "哈萨克斯坦", "阿拉木图"),
        listOf("cyprus", "кипр") to CountryGeo("Cyprus", 35.19, 33.38, "el", "CY", "Кипр", "Кипре", "Никосия", "Nicosia", "塞浦路斯", "尼科西亚"),
        listOf("latvia", "латвия", "riga", "рига") to CountryGeo("Latvia", 56.95, 24.11, "lv", "LV", "Латвия", "Латвии", "Рига", "Riga", "拉脱维亚", "里加"),
        listOf("lithuania", "литва", "vilnius", "вильнюс") to CountryGeo("Lithuania", 54.69, 25.28, "lt", "LT", "Литва", "Литве", "Вильнюс", "Vilnius", "立陶宛", "维尔纽斯"),
        listOf("estonia", "эстония", "tallinn", "таллин") to CountryGeo("Estonia", 59.44, 24.75, "et", "EE", "Эстония", "Эстонии", "Таллин", "Tallinn", "爱沙尼亚", "塔林"),
        listOf("switzerland", "швейцария", "zurich", "цюрих") to CountryGeo("Switzerland", 47.38, 8.54, "de", "CH", "Швейцария", "Швейцарии", "Цюрих", "Zurich", "瑞士", "苏黎世"),
        listOf("austria", "австрия", "vienna", "вена") to CountryGeo("Austria", 48.21, 16.37, "de", "AT", "Австрия", "Австрии", "Вена", "Vienna", "奥地利", "维也纳"),
        listOf("czech", "чехия", "prague", "прага") to CountryGeo("Czechia", 50.09, 14.42, "cs", "CZ", "Чехия", "Чехии", "Прага", "Prague", "捷克", "布拉格"),
        listOf("bulgaria", "болгария", "sofia", "софия") to CountryGeo("Bulgaria", 42.70, 23.32, "bg", "BG", "Болгария", "Болгарии", "София", "Sofia", "保加利亚", "索非亚"),
        listOf("romania", "румыния", "bucharest", "бухарест") to CountryGeo("Romania", 44.43, 26.10, "ro", "RO", "Румыния", "Румынии", "Бухарест", "Bucharest", "罗马尼亚", "布加勒斯特"),
        listOf("singapore", "сингапур") to CountryGeo("Singapore", 1.35, 103.82, "en", "SG", "Сингапур", "Сингапуре", "Сингапур", "Singapore", "新加坡", "新加坡"),
        listOf("india", "индия") to CountryGeo("India", 28.61, 77.21, "hi", "IN", "Индия", "Индии", "Дели", "Delhi", "印度", "德里"),
        listOf("canada", "канада", "toronto", "торонто") to CountryGeo("Canada", 43.65, -79.38, "en", "CA", "Канада", "Канаде", "Торонто", "Toronto", "加拿大", "多伦多"),
        listOf("brazil", "бразилия") to CountryGeo("Brazil", -23.55, -46.63, "pt", "BR", "Бразилия", "Бразилии", "Сан-Паулу", "Sao Paulo", "巴西", "圣保罗"),
        listOf("south korea", "корея", "seoul", "сеул") to CountryGeo("South Korea", 37.57, 126.98, "ko", "KR", "Южная Корея", "Южной Корее", "Сеул", "Seoul", "韩国", "首尔"),
        listOf("hong kong", "гонконг", "china", "китай") to CountryGeo("China", 39.90, 116.41, "zh", "CN", "Китай", "Китае", "Пекин", "Beijing", "中国", "北京"),
        listOf("armenia", "армения", "yerevan", "ереван") to CountryGeo("Armenia", 40.18, 44.51, "hy", "AM", "Армения", "Армении", "Ереван", "Yerevan", "亚美尼亚", "埃里温"),
        listOf("georgia", "грузия", "tbilisi", "тбилиси") to CountryGeo("Georgia", 41.72, 44.79, "ka", "GE", "Грузия", "Грузии", "Тбилиси", "Tbilisi", "格鲁吉亚", "第比利斯"),
        listOf("azerbaijan", "азербайджан", "baku", "баку") to CountryGeo("Azerbaijan", 40.41, 49.87, "az", "AZ", "Азербайджан", "Азербайджане", "Баку", "Baku", "阿塞拜疆", "巴库"),
        listOf("ireland", "ирландия", "dublin", "дублин") to CountryGeo("Ireland", 53.35, -6.26, "en", "IE", "Ирландия", "Ирландии", "Дублин", "Dublin", "爱尔兰", "都柏林"),
        listOf("belgium", "бельгия", "brussels", "брюссель") to CountryGeo("Belgium", 50.85, 4.35, "fr", "BE", "Бельгия", "Бельгии", "Брюссель", "Brussels", "比利时", "布鲁塞尔"),
        listOf("hungary", "венгрия", "budapest", "будапешт") to CountryGeo("Hungary", 47.50, 19.04, "hu", "HU", "Венгрия", "Венгрии", "Будапешт", "Budapest", "匈牙利", "布达佩斯"),
        listOf("greece", "греция", "athens", "афины") to CountryGeo("Greece", 37.98, 23.73, "el", "GR", "Греция", "Греции", "Афины", "Athens", "希腊", "雅典"),
        listOf("portugal", "португалия", "lisbon", "лиссабон") to CountryGeo("Portugal", 38.72, -9.14, "pt", "PT", "Португалия", "Португалии", "Лиссабон", "Lisbon", "葡萄牙", "里斯本"),
    )

    fun find(remark: String): CountryGeo? {
        val lower = remark.lowercase()
        return entries.firstOrNull { (aliases, _) -> aliases.any { lower.contains(it) } }?.second
    }

    /** Эмодзи-флаг из ISO alpha-2: каждая буква превращается в regional indicator symbol
     *  (пара из них сама по себе и есть флаг в Unicode — без картинок/ресурсов). */
    fun flagEmoji(isoCode: String): String {
        if (isoCode.length != 2) return "🌐"
        val base = 0x1F1E6
        val first = base + (isoCode[0].uppercaseChar() - 'A')
        val second = base + (isoCode[1].uppercaseChar() - 'A')
        if (first !in 0x1F1E6..0x1F1FF || second !in 0x1F1E6..0x1F1FF) return "🌐"
        return String(Character.toChars(first)) + String(Character.toChars(second))
    }
}
