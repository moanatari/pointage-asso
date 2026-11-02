using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pointage.Audit;
using Pointage.Presences;
using Pointage.Settings;
using Pointage.Web;

namespace Pointage.Pages.Admin;

public class SettingsModel(SettingsStore settings, PresenceStore presences, AuditLog audit) : PageModel
{
    [BindProperty] public string OrganizationName { get; set; } = "";
    [BindProperty, Range(1, 120, ErrorMessage = "La durée de conservation doit être comprise entre 1 et 120 mois.")]
    public int RetentionMonths { get; set; }
    [BindProperty] public bool KioskKeyboard { get; set; }
    [BindProperty] public bool KioskDeparture { get; set; }
    [BindProperty] public bool DepartureValidation { get; set; }
    public bool Saved { get; private set; }

    public void OnGet()
    {
        var s = settings.Get();
        (OrganizationName, RetentionMonths, KioskKeyboard, KioskDeparture, DepartureValidation) = (s.OrganizationName, s.RetentionMonths, s.KioskKeyboard, s.KioskDeparture, s.DepartureValidation);
    }

    public IActionResult OnPost()
    {
        if (!ModelState.IsValid) return Page();
        var before = settings.Get();
        var after = before with
        {
            OrganizationName = (OrganizationName ?? "").Trim(), RetentionMonths = RetentionMonths,
            KioskKeyboard = KioskKeyboard, KioskDeparture = KioskDeparture, DepartureValidation = DepartureValidation,
        };
        settings.Save(after);
        // Sans validation, les départs encore en attente n'ont plus personne pour les décider : on les valide.
        if (before.DepartureValidation && !after.DepartureValidation) presences.ValidatePendingDepartures();
        // Les réglages comptent pour le RGPD (durée de conservation) : on garde l'ancienne et la nouvelle durée.
        audit.Write(User.AccountId(), AuditActions.Settings, details: $"Conservation {before.RetentionMonths} → {after.RetentionMonths} mois ; structure « {after.OrganizationName} » ; clavier {(after.KioskKeyboard ? "oui" : "non")} ; départ {(after.KioskDeparture ? "oui" : "non")} ; validation des départs {(after.DepartureValidation ? "oui" : "non")}");
        Saved = true;
        return Page();
    }
}
