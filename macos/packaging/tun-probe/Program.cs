// Проба готовности туннеля тем же способом, что VpnEngine.WaitForTunAsync: ищет интерфейс
// с адресом 172.19.0.1 через System.Net.NetworkInformation. Код выхода 0 — найден за $1 секунд.
using System.Net;
using System.Net.NetworkInformation;

var timeout = TimeSpan.FromSeconds(args.Length > 0 ? int.Parse(args[0]) : 40);
var expected = IPAddress.Parse("172.19.0.1");
var deadline = DateTime.UtcNow + timeout;
while (DateTime.UtcNow < deadline)
{
    var match = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n =>
        n.OperationalStatus is OperationalStatus.Up or OperationalStatus.Unknown &&
        n.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(expected)));
    if (match != null)
    {
        Console.WriteLine($"OK: .NET видит туннель {match.Name} ({match.OperationalStatus})");
        return 0;
    }
    await Task.Delay(250);
}
Console.WriteLine("FAIL: .NET не нашёл интерфейс с 172.19.0.1. Интерфейсы:");
foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
    Console.WriteLine($"  {n.Name} {n.OperationalStatus} " +
                      string.Join(",", n.GetIPProperties().UnicastAddresses.Select(a => a.Address)));
return 1;
