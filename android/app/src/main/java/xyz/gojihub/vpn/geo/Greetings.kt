package xyz.gojihub.vpn.geo

/** [useSerifFont] — false для языков не на латинице/кириллице (китайский, японский,
 *  корейский, хинди, грузинский, армянский, греческий): у декоративного Instrument Serif,
 *  которым в остальных случаях рисуется приветствие на "Главной", просто нет глифов для этих
 *  письменностей — при отрисовке им текст незаметно подменялся системным шрифтом, выпадая из
 *  общего стиля заголовка. Экран берёт этот флаг и переключает fontFamily на обычный. */
data class Greeting(val hi: String, val transliteration: String, val language: String, val useSerifFont: Boolean = true)

/** Приветствие на языке страны подключения — прямое перенесение словаря из макета. */
object Greetings {
    private val table = mapOf(
        "en" to Greeting("Hello", "хелло", "английский"),
        "zh" to Greeting("你好", "ни хао", "китайский", useSerifFont = false),
        "hi" to Greeting("नमस्ते", "намастэ", "хинди", useSerifFont = false),
        "es" to Greeting("¡Hola!", "ола", "испанский"),
        "ar" to Greeting("مرحبا", "мархаба", "арабский", useSerifFont = false),
        "fr" to Greeting("Bonjour", "бонжур", "французский"),
        "pt" to Greeting("Olá", "ола", "португальский"),
        "ru" to Greeting("Привет", "", "русский"),
        "de" to Greeting("Hallo", "халло", "немецкий"),
        "ja" to Greeting("こんにちは", "коннитива", "японский", useSerifFont = false),
        "tr" to Greeting("Merhaba", "мерхаба", "турецкий"),
        "ko" to Greeting("안녕하세요", "аннёнхасэё", "корейский", useSerifFont = false),
        "nl" to Greeting("Hallo", "халло", "нидерландский"),
        "it" to Greeting("Ciao", "чао", "итальянский"),
        "fi" to Greeting("Hei", "хэй", "финский"),
        "pl" to Greeting("Cześć", "чещч", "польский"),
        "sv" to Greeting("Hej", "хэй", "шведский"),
        "no" to Greeting("Hei", "хэй", "норвежский"),
        "et" to Greeting("Tere", "тере", "эстонский"),
        "lv" to Greeting("Sveiki", "свейки", "латышский"),
        "lt" to Greeting("Labas", "лабас", "литовский"),
        "cs" to Greeting("Ahoj", "агой", "чешский"),
        "bg" to Greeting("Здравей", "здравей", "болгарский"),
        "ro" to Greeting("Bună", "буна", "румынский"),
        "el" to Greeting("Γεια σου", "я су", "греческий", useSerifFont = false),
        "hu" to Greeting("Szia", "сия", "венгерский"),
        "uk" to Greeting("Привіт", "привит", "украинский"),
        "ka" to Greeting("გამარჯობა", "гамарджоба", "грузинский", useSerifFont = false),
        "hy" to Greeting("Բարև", "барев", "армянский", useSerifFont = false),
        "az" to Greeting("Salam", "салам", "азербайджанский"),
        "kk" to Greeting("Сәлем", "сэлем", "казахский"),
    )

    fun forLang(lang: String?): Greeting = table[lang] ?: table.getValue("en")
}
