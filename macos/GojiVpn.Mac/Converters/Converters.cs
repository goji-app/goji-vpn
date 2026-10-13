using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace GodjiVpn.Converters;

/// <summary>"avares://…png" → Bitmap (флаги стран, логотип) с кэшем.</summary>
public sealed class AssetImageConverter : IValueConverter
{
    public static readonly AssetImageConverter Instance = new();
    private static readonly Dictionary<string, Bitmap> Cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || path.Length == 0) return null;
        if (Cache.TryGetValue(path, out var cached)) return cached;
        try
        {
            using var stream = AssetLoader.Open(new Uri(path));
            var bitmap = new Bitmap(stream);
            Cache[path] = bitmap;
            return bitmap;
        }
        catch { return null; }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Общие конвертеры для разметки.</summary>
public static class Conv
{
    public static readonly IValueConverter NotNullOrEmpty =
        new FuncValueConverter<object?, bool>(v => v is string s ? s.Length > 0 : v != null);

    public static readonly IValueConverter IsNullOrEmpty =
        new FuncValueConverter<object?, bool>(v => v is string s ? s.Length == 0 : v == null);

    public static readonly IValueConverter Not = new FuncValueConverter<bool, bool>(v => !v);

    public static readonly IValueConverter NonZero = new FuncValueConverter<int, bool>(v => v != 0);

    public static readonly IValueConverter IsZero = new FuncValueConverter<int, bool>(v => v == 0);

    /// <summary>int == ConverterParameter (номер вкладки/страницы).</summary>
    public static readonly IValueConverter IntEquals = new IntEqualsConverter();

    /// <summary>Строка входит в список через запятую из ConverterParameter (уровень качества
    /// канала → класс цвета: "excellent,good").</summary>
    public static readonly IValueConverter StringIn = new StringInConverter();

    private sealed class StringInConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is string v && (parameter?.ToString() ?? "").Split(',').Contains(v);

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class IntEqualsConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is int i && int.TryParse(parameter?.ToString(), out var p) && i == p;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
