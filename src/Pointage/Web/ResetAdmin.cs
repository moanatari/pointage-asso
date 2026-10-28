using System.Text;
using Pointage.Accounts;
using Pointage.Data;

namespace Pointage.Web;

// « Pointage.exe reset-admin <identifiant> » : redonne l'accès quand le mot de passe du super admin est perdu.
public static class ResetAdmin
{
    public static void Run(Db db, string username)
    {
        var password = ReadPassword("Nouveau mot de passe : ");
        if (password != ReadPassword("Confirmez le mot de passe : "))
        {
            Console.Error.WriteLine("Les deux mots de passe ne correspondent pas.");
            Environment.ExitCode = 1;
            return;
        }
        try
        {
            new AccountStore(db, TimeProvider.System).UpsertSuperAdmin(username, password);
            Console.WriteLine($"Le super administrateur « {username.Trim()} » peut se connecter avec ce mot de passe.");
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine(e.Message);
            Environment.ExitCode = 1;
        }
    }

    // ReadKey(true) n'affiche pas les touches : le mot de passe n'apparaît jamais à l'écran.
    private static string ReadPassword(string prompt)
    {
        Console.Write(prompt);
        var sb = new StringBuilder();
        while (Console.ReadKey(true) is var key && key.Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; }
            else if (!char.IsControl(key.KeyChar)) sb.Append(key.KeyChar);
        }
        Console.WriteLine();
        return sb.ToString();
    }
}
