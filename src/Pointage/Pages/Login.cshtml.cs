using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pointage.Accounts;
using Pointage.Audit;
using Pointage.Web;

namespace Pointage.Pages;

public class LoginModel(AccountStore accounts, AuditLog audit) : PageModel
{
    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnPostAsync()
    {
        var account = accounts.VerifyLogin(Username, Password);
        if (account is null)
        {
            // Même message que l'identifiant existe ou non : on ne révèle pas quels comptes existent.
            Error = "Identifiant ou mot de passe incorrect.";
            return Page();
        }
        audit.Write(account.Id, AuditActions.Login);
        await HttpContext.SignInAsync(account);
        // Url.IsLocalUrl : un ReturnUrl forgé ne doit pas renvoyer vers un autre site.
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/");
    }
}
