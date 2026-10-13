using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GodjiVpn.Utils;

/// <summary>
/// Терпимое чтение чисел и логических полей из ответов личного кабинета. Бэкенд время от
/// времени меняет типы полей: например, device_limit стал null у безлимитных тарифов (сайт
/// читает его как device_limit ?? null). Для обычного int такой null — исключение, и падал
/// разбор всего /api/subscriptions: приложение молча оставалось без тарифа, срока и трафика.
/// Здесь null — значение по умолчанию, числа принимаются и строкой, и дробными.
/// </summary>
public static class LenientJson
{
    public static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new IntConverter());
        options.Converters.Add(new LongConverter());
        options.Converters.Add(new DoubleConverter());
        options.Converters.Add(new BoolConverter());
        return options;
    }

    private static double? ReadNumber(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.Number => reader.GetDouble(),
        JsonTokenType.String => double.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null,
        JsonTokenType.True => 1,
        JsonTokenType.False => 0,
        _ => SkipValue(ref reader)
    };

    private static double? SkipValue(ref Utf8JsonReader reader)
    {
        // null — значение по умолчанию; объект/массив на месте числа — тоже, а не падение.
        reader.Skip();
        return null;
    }

    private sealed class IntConverter : JsonConverter<int>
    {
        public override bool HandleNull => true;

        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var i)) return i;
            var v = ReadNumber(ref reader);
            return v is { } d && !double.IsNaN(d) ? (int)Math.Clamp(Math.Round(d), int.MinValue, int.MaxValue) : 0;
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }

    private sealed class LongConverter : JsonConverter<long>
    {
        public override bool HandleNull => true;

        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var l)) return l;
            var v = ReadNumber(ref reader);
            return v is { } d && !double.IsNaN(d) ? (long)Math.Clamp(Math.Round(d), long.MinValue, long.MaxValue) : 0;
        }

        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }

    private sealed class DoubleConverter : JsonConverter<double>
    {
        public override bool HandleNull => true;

        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            ReadNumber(ref reader) ?? 0;

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }

    private sealed class BoolConverter : JsonConverter<bool>
    {
        public override bool HandleNull => true;

        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Number => reader.GetDouble() != 0,
            JsonTokenType.String => reader.GetString() is { } s && (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1"),
            _ => SkipValue(ref reader) != null
        };

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);
    }
}
