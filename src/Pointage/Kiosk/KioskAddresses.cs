using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Pointage.Kiosk;

// Adresses à saisir sur la borne : les IPv4 privées d'abord, le nom du poste en dernier
// (iPhone, iPad et la plupart des Android ne résolvent pas le nom d'un PC Windows).
public static class KioskAddresses
{
    public static IReadOnlyList<string> Urls(int? port)
    {
        var suffix = port is { } p ? $":{p}" : "";
        var urls = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var u in nic.GetIPProperties().UnicastAddresses)
                    if (u.Address.AddressFamily == AddressFamily.InterNetwork && IsPrivate(u.Address))
                        urls.Add($"http://{u.Address}{suffix}/borne");
            }
        }
        catch (NetworkInformationException)
        {
            // Une carte réseau défaillante ne doit pas faire échouer la page : on garde l'adresse par nom.
        }
        urls.Add(ByName(port));
        // Distinct : une carte peut exposer la même adresse plusieurs fois.
        return urls.Distinct().ToList();
    }

    public static string ByName(int? port) => $"http://{Environment.MachineName}{(port is { } p ? $":{p}" : "")}/borne";

    // Noms des réseaux connectés en profil « Public » : la règle du pare-feu (privé, domaine) ne s'y applique pas,
    // donc la borne n'atteint pas le poste par ce réseau.
    public static IReadOnlyList<string> PublicNetworks()
    {
        var names = new List<string>();
        if (!OperatingSystem.IsWindows()) return names;
        try
        {
            // NetworkListManager (COM de Windows) : ni paquet NuGet, ni PowerShell à lancer.
            dynamic nlm = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"))!)!;
            foreach (dynamic n in nlm.GetNetworks(1)) // 1 = NLM_ENUM_NETWORK_CONNECTED
                if ((int)n.GetCategory() == 0) names.Add((string)n.GetName()); // 0 = NLM_NETWORK_CATEGORY_PUBLIC
        }
        catch
        {
            // Détection indisponible : pas d'avertissement plutôt qu'une page en erreur.
        }
        return names;
    }

    public static bool IsPrivate(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168));
    }
}
