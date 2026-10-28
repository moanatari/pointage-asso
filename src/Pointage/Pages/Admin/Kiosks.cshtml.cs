using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pointage.Audit;
using Pointage.Data;
using Pointage.Kiosk;
using Pointage.Web;

namespace Pointage.Pages.Admin;

// Liaison et révocation des bornes (super admin uniquement, via la convention du dossier /Admin).
public class KiosksModel(KioskPairing pairing, AuditLog audit, TimeProvider time) : PageModel
{
    [BindProperty] public string KioskName { get; set; } = "";
    public string? Code { get; private set; }
    public DateTime CodeExpiresAt { get; private set; }
    public string? Error { get; private set; }
    public IReadOnlyList<Pointage.Kiosk.Kiosk> Kiosks { get; private set; } = [];

    public void OnGet() => Kiosks = pairing.List();

    public IActionResult OnPostPair()
    {
        if (string.IsNullOrWhiteSpace(KioskName))
        {
            Error = "Le nom de la borne est obligatoire.";
            OnGet();
            return Page();
        }
        Code = pairing.NewPairingCode(KioskName);
        CodeExpiresAt = time.Now() + KioskPairing.CodeLifetime;
        // La liaison se termine sur la borne, sans compte : on trace ici qui l'a autorisée.
        audit.Write(User.AccountId(), AuditActions.KioskPair, details: $"Code de liaison émis pour la borne « {KioskName.Trim()} »");
        OnGet();
        return Page();
    }

    public IActionResult OnPostRevoke(long id)
    {
        pairing.Revoke(id);
        audit.Write(User.AccountId(), AuditActions.KioskRevoke, details: $"Borne n° {id}");
        return RedirectToPage();
    }
}
