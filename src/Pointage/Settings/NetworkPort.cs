using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Pointage.Data;

namespace Pointage.Settings;

public static class NetworkPort
{
    public const int Default = 5080;

    public static bool IsValid(int port) => port is >= 1024 and <= 65535;

    public static bool IsFree(int port)
    {
        try
        {
            var l = new TcpListener(IPAddress.Any, port);
            l.Start();
            l.Stop();
            return true;
        }
        catch (SocketException) { return false; }
    }

    // Mêmes arguments que installer/Pointage.iss. Une erreur ne doit jamais empêcher le démarrage.
    public static void SyncFirewall(int port)
    {
        try
        {
            Netsh("advfirewall firewall delete rule name=\"Pointage\"");
            Netsh($"advfirewall firewall add rule name=\"Pointage\" dir=in action=allow protocol=TCP localport={port} profile=private,domain remoteip=localsubnet");
        }
        catch { }
    }

    static void Netsh(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("netsh", args) { CreateNoWindow = true, UseShellExecute = false });
        p?.WaitForExit(10000);
    }

    // ponytail: relance en ~60 s (reprise sur échec du service, restart/60000) ; piste : sc failureflag + arrêt propre si le délai gêne.
    public static void RestartService() =>
        _ = Task.Delay(2000).ContinueWith(_ => Process.GetCurrentProcess().Kill());

    public static int SetPortCommand(Db db, string arg)
    {
        if (!int.TryParse(arg, CultureInfo.InvariantCulture, out var port) || !IsValid(port))
        {
            Console.WriteLine("Le port doit être un nombre entre 1024 et 65535.");
            return 1;
        }
        var store = new SettingsStore(db);
        store.Save(store.Get() with { Port = port });
        Console.WriteLine($"Port enregistré : {port}. Redémarrez le service (en administrateur) : sc stop Pointage puis sc start Pointage.");
        return 0;
    }
}
