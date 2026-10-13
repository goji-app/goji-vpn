namespace GodjiVpn.Utils;

/// <summary>Русские формы множественного числа — порт ruDaysWord (Android Strings.kt).</summary>
public static class RuPlural
{
    public static string Form(int n, string one, string few, string many)
    {
        var mod100 = Math.Abs(n) % 100;
        var mod10 = mod100 % 10;
        if (mod100 is >= 11 and <= 14) return many;
        return mod10 switch
        {
            1 => one,
            >= 2 and <= 4 => few,
            _ => many
        };
    }

    public static string Days(int n) => Form(n, "день", "дня", "дней");
}
