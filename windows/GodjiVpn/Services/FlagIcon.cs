namespace GodjiVpn.Services;

/// <summary>
/// WPF (в отличие от Android/браузеров) не умеет составлять пару regional-indicator
/// кодпоинтов эмодзи-флага в картинку — TextBlock показывает голый текстовый фоллбэк вроде
/// "DE" вместо флага (подтверждённое ограничение старого текстового стека WPF, не баг в
/// коде). Вместо эмодзи используем настоящие PNG-флаги, вшитые в Assets/Images/Flags —
/// офлайн, тем же принципом, что и для глобуса (см. Assets/Globe/vendor).
/// </summary>
public static class FlagIcon
{
    private static readonly HashSet<string> Available = new(StringComparer.OrdinalIgnoreCase)
    {
        "nl", "de", "fi", "ru", "tr", "us", "jp", "gb", "fr", "pl", "se", "no", "it", "es",
        "ua", "kz", "cy", "lv", "lt", "ee", "ch", "at", "cz", "bg", "ro", "sg", "in", "ca",
        "br", "kr", "cn", "am", "ge", "az", "ie", "be", "hu", "gr", "pt"
    };

    /// <returns>pack-URI картинки флага, или null — если для этого ISO-кода флага нет
    /// (тогда UI должен показать generic-фоллбэк, например 🌐).</returns>
    public static string? ImagePath(string? isoCode)
    {
        if (string.IsNullOrEmpty(isoCode) || !Available.Contains(isoCode)) return null;
        return $"pack://application:,,,/Assets/Images/Flags/{isoCode.ToLowerInvariant()}.png";
    }
}
