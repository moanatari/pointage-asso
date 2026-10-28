using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pointage.Accounts;
using Pointage.Audit;
using Pointage.Web;

namespace Pointage.Pages.Admin;

public class AccountsModel(AccountStore accounts, AuditLog audit) : PageModel
{
    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public Role NewRole { get; set; } = Role.Manager;
    public string? Error { get; private set; }
    public IReadOnlyList<Account> Accounts { get; private set; } = [];

    public void OnGet() => Accounts = accounts.List();

    public IActionResult OnPostCreate() => Try(() =>
    {
        var id = accounts.Create(Username ?? "", Password ?? "", NewRole);
        audit.Write(User.AccountId(), AuditActions.AccountCreate, details: $"Compte n° {id} « {Username!.Trim()} » ({NewRole.Label()})");
    });

    public IActionResult OnPostPassword(long id, string newPassword) => Try(() =>
    {
        accounts.SetPassword(id, newPassword ?? "");
        audit.Write(User.AccountId(), AuditActions.AccountUpdate, details: $"Compte n° {id} : mot de passe réinitialisé");
    });

    public IActionResult OnPostActive(long id, bool active) => Try(() =>
    {
        accounts.SetActive(id, active);
        audit.Write(User.AccountId(), AuditActions.AccountUpdate, details: $"Compte n° {id} : {(active ? "réactivé" : "désactivé")}");
    });

    // Succès : redirection (évite de rejouer le POST) ; échec : la page se réaffiche avec le message.
    private IActionResult Try(Action action)
    {
        try
        {
            action();
            return RedirectToPage();
        }
        catch (ArgumentException e) { Error = e.Message; }
        catch (InvalidOperationException) { Error = "Impossible : il doit rester au moins un super admin actif."; }
        OnGet();
        return Page();
    }
}

public static class RoleExtensions
{
    // Libellé français : l'interface n'affiche jamais le nom de l'enum.
    public static string Label(this Role r) => r == Role.SuperAdmin ? "super admin" : "responsable";
}
