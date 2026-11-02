using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pointage.Audit;
using Pointage.Data;
using Pointage.Presences;
using Pointage.Settings;
using Pointage.Web;

namespace Pointage.Pages;

// Accueil du responsable : arrivées et départs à valider ou refuser, présents et partis du jour.
public class IndexModel(PresenceStore presences, AuditLog audit, SettingsStore settings, TimeProvider time) : PageModel
{
    public IReadOnlyList<PresenceRow> Pending { get; private set; } = [];
    public IReadOnlyList<PresenceRow> PendingDepartures { get; private set; } = [];
    public IReadOnlyList<PresenceRow> Present { get; private set; } = [];
    public IReadOnlyList<PresenceRow> Departed { get; private set; } = [];
    public bool DepartureValidation { get; private set; }
    public DateOnly Today => time.Today();

    public void OnGet()
    {
        Pending = presences.PendingToday();
        PendingDepartures = presences.PendingDepartures();
        (Present, Departed) = presences.Today();
        DepartureValidation = settings.Get().DepartureValidation;
    }

    public IActionResult OnPostValidate(long id) => Decide(id, PresenceStatus.Validated, AuditActions.Validate);

    public IActionResult OnPostRefuse(long id) => Decide(id, PresenceStatus.Refused, AuditActions.Refuse);

    // Une arrivée refusée reste dans l'historique du membre (statut « refusé »).
    private IActionResult Decide(long id, PresenceStatus status, string action)
    {
        if (presences.Get(id) is { } p)
        {
            presences.Decide(id, status, User.AccountId());
            audit.Write(User.AccountId(), action, p.MemberId, $"Présence n° {id}");
        }
        return RedirectToPage();
    }

    public IActionResult OnPostValidateDeparture(long id) => DecideDeparture(id, true, AuditActions.DepartureValidate);

    public IActionResult OnPostRefuseDeparture(long id) => DecideDeparture(id, false, AuditActions.DepartureRefuse);

    // Refuser un départ remet le membre dans les présents.
    private IActionResult DecideDeparture(long id, bool validate, string action)
    {
        if (presences.Get(id) is { } p && presences.DecideDeparture(id, validate))
            audit.Write(User.AccountId(), action, p.MemberId, $"Présence n° {id}");
        return RedirectToPage();
    }

    public IActionResult OnPostValidateAll(long[] ids)
    {
        var count = presences.ValidateMany(ids, User.AccountId());
        audit.Write(User.AccountId(), AuditActions.Validate, details: $"{count} arrivée(s) validée(s) d'un coup");
        return RedirectToPage();
    }
}
