namespace GodjiVpn.Services;

public sealed record Greeting(string Hi, string Transliteration, string Language);

/// <summary>Приветствие на языке страны подключения, показывается поверх глобуса на Connect-
/// экране. Портировано 1:1 из geo/Greetings.kt.</summary>
public static class Greetings
{
    private static readonly Dictionary<string, Greeting> Table = new()
    {
        ["en"] = new Greeting("Hello", "хелло", "английский"),
        ["zh"] = new Greeting("你好", "ни хао", "китайский"),
        ["hi"] = new Greeting("नमस्ते", "намастэ", "хинди"),
        ["es"] = new Greeting("¡Hola!", "ола", "испанский"),
        ["ar"] = new Greeting("مرحبا", "мархаба", "арабский"),
        ["fr"] = new Greeting("Bonjour", "бонжур", "французский"),
        ["pt"] = new Greeting("Olá", "ола", "португальский"),
        ["ru"] = new Greeting("Привет", "", "русский"),
        ["de"] = new Greeting("Hallo", "халло", "немецкий"),
        ["ja"] = new Greeting("こんにちは", "коннитива", "японский"),
        ["tr"] = new Greeting("Merhaba", "мерхаба", "турецкий"),
        ["ko"] = new Greeting("안녕하세요", "аннёнхасэё", "корейский"),
        ["nl"] = new Greeting("Hallo", "халло", "нидерландский"),
        ["it"] = new Greeting("Ciao", "чао", "итальянский"),
        ["fi"] = new Greeting("Hei", "хэй", "финский"),
        ["pl"] = new Greeting("Cześć", "чещч", "польский"),
        ["sv"] = new Greeting("Hej", "хэй", "шведский"),
        ["no"] = new Greeting("Hei", "хэй", "норвежский"),
        ["et"] = new Greeting("Tere", "тере", "эстонский"),
        ["lv"] = new Greeting("Sveiki", "свейки", "латышский"),
        ["lt"] = new Greeting("Labas", "лабас", "литовский"),
        ["cs"] = new Greeting("Ahoj", "агой", "чешский"),
        ["bg"] = new Greeting("Здравей", "здравей", "болгарский"),
        ["ro"] = new Greeting("Bună", "буна", "румынский"),
        ["el"] = new Greeting("Γεια σου", "я су", "греческий"),
        ["hu"] = new Greeting("Szia", "сиа", "венгерский"),
        ["uk"] = new Greeting("Привіт", "привит", "украинский"),
        ["ka"] = new Greeting("გამარჯობა", "гамарджоба", "грузинский"),
        ["hy"] = new Greeting("Բարև", "барев", "армянский"),
        ["az"] = new Greeting("Salam", "салам", "азербайджанский"),
        ["kk"] = new Greeting("Сәлем", "сэлем", "казахский"),
    };

    public static Greeting ForLang(string? lang) =>
        (lang != null && Table.TryGetValue(lang, out var g)) ? g : Table["en"];
}
