using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Pointage.Kiosk;

// Adresses à saisir sur la borne : le nom du poste d'abord, puis les IPv4 privées (si le nom ne se résout pas).
public static class KioskAddresses
{
    public static IReadOnlyList<string> Urls(int? port)
    {
        var suffix = port is { } p ? $":{p}" : "";
        var urls = new List<string> { $"http://{Environment.MachineName}{suffix}/borne" };
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var u in nic.GetIPProperties().UnicastAddresses)
                if (u.Address.AddressFamily == AddressFamily.InterNetwork && IsPrivate(u.Address))
                    urls.Add($"http://{u.Address}{suffix}/borne");
        }
        return urls;
    }

    public static bool IsPrivate(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168));
    }
}
