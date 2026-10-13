using System.IO;
using System.Text.Json.Nodes;

namespace GodjiVpn.Services;

/// <summary>Длина оплаченного периода подписки — для кольца дней, которое убывает каждый день
/// (остаток / длина периода), как util/SubscriptionPeriod.kt на Android. Даты начала периода
/// бэкенд не отдаёт, поэтому длину запоминаем сами по каждой подписке: остаток вырос (продление) —
/// новый период, кольцо снова полное; подписку видим впервые — ближайший стандартный срок, не
/// меньше остатка (121 день → 180).</summary>
public static class SubscriptionPeriod
{
    private static readonly int[] StandardDays = { 1, 3, 7, 14, 30, 60, 90, 180, 365, 730 };
    private static readonly bool Disabled = Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW") == "1";
    private static readonly object Gate = new();

    private static string FilePath => SecretFile.PathFor("subscription-period.json");

    public static int TotalDays(long subscriptionId, int daysLeft)
    {
        var fallback = StandardDays.FirstOrDefault(d => d >= daysLeft, daysLeft);
        if (Disabled) return Math.Max(Math.Max(fallback, daysLeft), 1);
        lock (Gate)
        {
            JsonObject root;
            try { root = File.Exists(FilePath) ? JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? new() : new(); }
            catch { root = new(); }
            var keyTotal = $"total_{subscriptionId}";
            var keyLast = $"last_{subscriptionId}";
            var savedTotal = root[keyTotal]?.GetValue<int>() ?? 0;
            var lastDays = root[keyLast]?.GetValue<int>() ?? -1;
            var total = savedTotal <= 0 || lastDays < 0 ? fallback
                : daysLeft > lastDays ? daysLeft
                : savedTotal;
            total = Math.Max(Math.Max(total, daysLeft), 1);
            if (total != savedTotal || daysLeft != lastDays)
            {
                root[keyTotal] = total;
                root[keyLast] = daysLeft;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                    File.WriteAllText(FilePath, root.ToJsonString());
                }
                catch { /* не критично: в худшем случае кольцо посчитается от стандартного срока */ }
            }
            return total;
        }
    }

    public static void Clear()
    {
        if (Disabled) return;
        try { File.Delete(FilePath); } catch { }
    }
}
