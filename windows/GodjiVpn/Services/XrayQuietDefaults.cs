namespace GodjiVpn.Services;

/// <summary>Батарея и диск (2026-10-19, порт Android). Бэкенд присылает в профиле loglevel
/// "debug" с dnsLog — ядро писало строку на каждое соединение и DNS-запрос, — и
/// burstObservatory, опрашивающий все узлы раз в минуту. Журнал оставляем на уровне
/// предупреждений, а слишком частый опрос узлов поднимаем до раза в 5 минут (реже, чем задал
/// бэкенд, не делаем). Мёртвый узел раньше заметит проверка связи и переподключит.</summary>
public static class XrayQuietDefaults
{
    private const int MinObservatoryIntervalSeconds = 5 * 60;

    public static void Apply(System.Text.Json.Nodes.JsonObject config)
    {
        var log = config["log"] as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject();
        log["loglevel"] = "warning";
        log["dnsLog"] = false;
        config["log"] = log;

        if (config["burstObservatory"] is System.Text.Json.Nodes.JsonObject burst)
        {
            var ping = burst["pingConfig"] as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject();
            RaiseInterval(ping, "interval");
            burst["pingConfig"] = ping;
        }
        if (config["observatory"] is System.Text.Json.Nodes.JsonObject observatory)
            RaiseInterval(observatory, "probeInterval");
    }

    private static void RaiseInterval(System.Text.Json.Nodes.JsonObject holder, string field)
    {
        var raw = holder[field]?.GetValue<string>();
        var seconds = ParseDurationSeconds(raw);
        // Без значения у Xray действует дефолт (1 минута у burstObservatory) — тоже поднимаем.
        if (seconds is null || seconds < MinObservatoryIntervalSeconds)
            holder[field] = $"{MinObservatoryIntervalSeconds / 60}m";
    }

    /// <summary>"1m", "30s", "1m30s", "500ms", "2h" → секунды; null, если строка пустая или непонятная.</summary>
    public static double? ParseDurationSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        var matches = System.Text.RegularExpressions.Regex.Matches(text, @"(\d+(?:\.\d+)?)(ms|s|m|h)");
        if (matches.Count == 0 || string.Concat(matches.Select(m => m.Value)) != text) return null;
        double total = 0;
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            var n = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            total += m.Groups[2].Value switch { "ms" => n / 1000, "s" => n, "m" => n * 60, _ => n * 3600 };
        }
        return total;
    }
}
