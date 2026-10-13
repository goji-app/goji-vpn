using System.Runtime.InteropServices;
using System.Text;

namespace GodjiVpn.Services;

/// <summary>
/// Имя текущей Wi-Fi через Native Wifi API (wlanapi.dll). Результат:
///  - null — сейчас нет подключённой Wi-Fi (кабель, нет сети, служба WLAN отсутствует);
///  - ""   — Wi-Fi подключена, но имя недоступно. С Windows 11 24H2 SSID отдаётся только
///           при доступе классических приложений к геолокации (иначе ERROR_ACCESS_DENIED) —
///           тот же смысл, что разрешение на геолокацию в Android.
/// </summary>
public static class WifiInfo
{
    private const int OpcodeCurrentConnection = 7; // wlan_intf_opcode_current_connection
    private const int StateConnected = 1;          // wlan_interface_state_connected
    private const int InterfaceInfoSize = 16 + 512 + 4; // GUID + WCHAR[256] + WLAN_INTERFACE_STATE
    private const int SsidOffset = 4 + 4 + 512;         // isState + wlanConnectionMode + strProfileName

    [DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanQueryInterface(IntPtr clientHandle, ref Guid interfaceGuid, int opCode, IntPtr reserved,
        out int dataSize, out IntPtr data, IntPtr opcodeValueType);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);

    public static string? CurrentSsid()
    {
        IntPtr handle = IntPtr.Zero, list = IntPtr.Zero;
        try
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out handle) != 0) return null;
            if (WlanEnumInterfaces(handle, IntPtr.Zero, out list) != 0) return null;

            var count = Marshal.ReadInt32(list);
            var connected = false;
            for (var i = 0; i < count; i++)
            {
                var item = list + 8 + i * InterfaceInfoSize;
                var state = Marshal.ReadInt32(item + 16 + 512);
                if (state != StateConnected) continue;
                connected = true;

                var guid = Marshal.PtrToStructure<Guid>(item);
                if (WlanQueryInterface(handle, ref guid, OpcodeCurrentConnection, IntPtr.Zero, out _, out var data, IntPtr.Zero) != 0)
                    continue;
                try
                {
                    var length = Math.Clamp(Marshal.ReadInt32(data + SsidOffset), 0, 32);
                    if (length == 0) continue;
                    var bytes = new byte[length];
                    Marshal.Copy(data + SsidOffset + 4, bytes, 0, length);
                    var ssid = Encoding.UTF8.GetString(bytes).Trim();
                    if (ssid.Length > 0) return ssid;
                }
                finally { WlanFreeMemory(data); }
            }
            return connected ? "" : null;
        }
        catch
        {
            // wlanapi.dll отсутствует (Windows Server без WLAN) и т.п. — считаем, что Wi-Fi нет.
            return null;
        }
        finally
        {
            if (list != IntPtr.Zero) WlanFreeMemory(list);
            if (handle != IntPtr.Zero) WlanCloseHandle(handle, IntPtr.Zero);
        }
    }
}
