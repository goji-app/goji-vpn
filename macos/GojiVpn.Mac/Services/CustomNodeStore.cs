using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodjiVpn.Models;

namespace GodjiVpn.Services;

/// <summary>
/// Узлы, добавленные вручную по своему JSON-профилю (клиентский конфиг вроде тех, что
/// подписка отдаёт на каждый узел — тот же формат, который WriteXrayConfig/PingService уже
/// принимают как ConnectPayloadJson), а не полученные с backend-подписки. Хранятся отдельно и
/// переживают обновление подписки — SubscriptionRepository.RefreshAsync() их не трогает,
/// объединение со списком подписки происходит уже в ServersViewModel.
/// </summary>
public sealed class CustomNodeStore
{
    private static string StatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GodjiVpn", "custom-nodes.json");

    private sealed class Record
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Host { get; set; } = "";
        public int Port { get; set; }
        public string ConnectPayloadJson { get; set; } = "";
    }

    private List<Record> _records;

    public event Action? Changed;

    public CustomNodeStore() => _records = Load();

    public IReadOnlyList<VlessNode> Nodes => _records
        .Select(r => new VlessNode { Id = r.Id, Name = r.Name, Host = r.Host, Port = r.Port, ConnectPayloadJson = r.ConnectPayloadJson })
        .ToList();

    /// <summary>Принимает сырой JSON клиентского профиля (как есть, тот же формат, что и у
    /// узлов подписки) — вытаскивает host/port/remarks из первого прокси-outbound'а для
    /// отображения, сам JSON сохраняется целиком без изменений и позже идёт в WriteXrayConfig/
    /// PingService точно как обычный узел подписки. Три структуры settings, как и в
    /// SubscriptionService.ParseJsonProfiles (держать в синхроне при добавлении протокола —
    /// раньше здесь узнавался только vnext, и вставка Trojan/Shadowsocks/Hysteria-профиля
    /// вручную отвергалась с "это не клиентский профиль xray", хотя xray.exe их прекрасно
    /// понимает): vnext (VLESS/VMess), servers (Trojan/Shadowsocks), settings.address
    /// напрямую (Hysteria v2).</summary>
    public (bool Success, string? Error) Add(string rawJson)
    {
        JsonObject? config;
        try { config = JsonNode.Parse(rawJson)?.AsObject(); }
        catch (Exception ex) { return (false, $"Невалидный JSON: {ex.Message}"); }
        if (config == null) return (false, "Невалидный JSON");

        var outbounds = config["outbounds"]?.AsArray();
        var proxyOutbound = outbounds?.FirstOrDefault(o =>
            o?["settings"]?["vnext"] != null || o?["settings"]?["servers"] != null || o?["settings"]?["address"] != null);
        var settings = proxyOutbound?["settings"];
        string? host = null;
        int port = 443;
        if (settings?["vnext"]?.AsArray()?.FirstOrDefault() is { } vnext)
        {
            host = vnext["address"]?.GetValue<string>();
            port = vnext["port"]?.GetValue<int>() ?? 443;
        }
        else if (settings?["servers"]?.AsArray()?.FirstOrDefault() is { } server)
        {
            host = server["address"]?.GetValue<string>();
            port = server["port"]?.GetValue<int>() ?? 443;
        }
        else if (settings?["address"] != null)
        {
            host = settings["address"]?.GetValue<string>();
            port = settings["port"]?.GetValue<int>() ?? 443;
        }
        if (string.IsNullOrWhiteSpace(host))
            return (false, "В JSON не найден адрес сервера (outbounds[].settings) — это не клиентский профиль xray");
        var remarks = config["remarks"]?.GetValue<string>();
        var name = string.IsNullOrWhiteSpace(remarks) ? host : remarks;

        _records.Add(new Record
        {
            Id = "custom-" + Guid.NewGuid().ToString("N")[..8],
            Name = name,
            Host = host,
            Port = port,
            ConnectPayloadJson = rawJson
        });
        Save();
        Changed?.Invoke();
        return (true, null);
    }

    public void Remove(string id)
    {
        if (_records.RemoveAll(r => r.Id == id) > 0)
        {
            Save();
            Changed?.Invoke();
        }
    }

    private static List<Record> Load()
    {
        try
        {
            if (!File.Exists(StatePath)) return new List<Record>();
            return JsonSerializer.Deserialize<List<Record>>(File.ReadAllText(StatePath)) ?? new List<Record>();
        }
        catch { return new List<Record>(); }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(_records));
        }
        catch { /* лучшее усилие — при сбое просто не сохранится до следующего изменения */ }
    }
}
