using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Hosting.WindowsServices;
using Pointage.Audit;
using Pointage.Presences;
using Pointage.Settings;
using Pointage.Web;

namespace Pointage.Pages.Admin;

public class SettingsModel(SettingsStore settings, PresenceStore presences, AuditLog audit) : PageModel
{
    [BindProperty] public string? OrganizationName { get; set; }
    [BindProperty, Range(1, 120, ErrorMessage = "La durée de conservation doit être comprise entre 1 et 120 mois.")]
    public int RetentionMonths { get; set; }
    [BindProperty] public bool KioskKeyboard { get; set; }
    [BindProperty] public bool KioskDeparture { get; set; }
    [BindProperty] public bool DepartureValidation { get; set; }
    [BindProperty] public int? Port { get; set; }
    public bool Saved { get; private set; }
    public string? PortMessage { get; private set; }
    public bool PortFailed { get; private set; }

    public void OnGet()
    {
        var s = settings.Get();
        (OrganizationName, RetentionMonths, KioskKeyboard, KioskDeparture, DepartureValidation, Port) = (s.OrganizationName, s.RetentionMonths, s.KioskKeyboard, s.KioskDeparture, s.DepartureValidation, s.Port);
    }

    public IActionResult OnPostPort()
    {
        // Pas de ModelState.IsValid : il couvre aussi les champs de l'autre formulaire, absents de cette requête.
        var asked = Port;
        ModelState.Clear(); // sinon le champ OrganizationName, absent de ce formulaire, produit une erreur anglaise
        OnGet();
        if (asked is not int p || !NetworkPort.IsValid(p))
            return PortResult("Le port doit être un nombre entre 1024 et 65535.", true);
        var before = settings.Get();
        if (p == before.Port) return PortResult($"Le port est déjà {p}.", false);
        if (!NetworkPort.IsFree(p)) return PortResult($"Le port {p} est déjà utilisé sur ce poste.", true);

        settings.Save(before with { Port = p });
        audit.Write(User.AccountId(), AuditActions.Settings, details: $"Port {before.Port} → {p}");
        Port = p;
        if (WindowsServiceHelpers.IsWindowsService())
        {
            var page = PortResult($"Port enregistré. Pointage redémarre : dans environ une minute, ouvrez http://localhost:{p}/", false);
            NetworkPort.RestartService();
            return page;
        }
        return PortResult($"Port enregistré. Il sera appliqué au prochain démarrage du service Pointage.", false);
    }

    IActionResult PortResult(string message, bool failed)
    {
        (PortMessage, PortFailed) = (message, failed);
        return Page();
    }

    public IActionResult OnPost()
    {
        var before = settings.Get();
        Port = before.Port;
        if (!ModelState.IsValid) return Page();
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
