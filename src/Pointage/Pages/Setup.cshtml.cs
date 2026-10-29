using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pointage.Accounts;
using Pointage.Settings;
using Pointage.Web;

namespace Pointage.Pages;

// Assistant de premier lancement : n'existe que tant qu'aucun super admin n'a été créé.
public class SetupModel(AccountStore accounts, SettingsStore settings) : PageModel
{
    [BindProperty] public string OrganizationName { get; set; } = "";
    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string Confirm { get; set; } = "";
    [BindProperty] public int RetentionMonths { get; set; } = AppSettings.Default.RetentionMonths;
    [BindProperty] public bool KioskKeyboard { get; set; } = AppSettings.Default.KioskKeyboard;
    public List<string> Errors { get; } = [];
    public string? KioskUrl { get; private set; }

    // Une fois l'installation faite, l'assistant ne doit plus permettre de créer un compte sans authentification.
    public IActionResult OnGet() => accounts.AnySuperAdmin() ? NotFound() : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (accounts.AnySuperAdmin()) return NotFound();
        if (string.IsNullOrWhiteSpace(OrganizationName)) Errors.Add("Le nom de la structure est obligatoire.");
        if (Password != Confirm) Errors.Add("Les deux mots de passe ne correspondent pas.");
        // Même borne que les Réglages : au-delà, le calcul de la date limite de purge déborde.
        if (RetentionMonths is < 1 or > 120) Errors.Add("La durée de conservation doit être comprise entre 1 et 120 mois.");
        if (Errors.Count > 0) return Page();

        long id;
        try { id = accounts.Create(Username, Password, Role.SuperAdmin); }
        catch (ArgumentException e)
        {
            Errors.Add(e.Message);
            return Page();
        }
        settings.Save(AppSettings.Default with
        {
            OrganizationName = OrganizationName.Trim(),
            RetentionMonths = RetentionMonths,
            KioskKeyboard = KioskKeyboard,
        });
        await HttpContext.SignInAsync(accounts.Get(id)!);
        KioskUrl = $"http://{Environment.MachineName}:5080/borne";
        return Page();
    }
}
