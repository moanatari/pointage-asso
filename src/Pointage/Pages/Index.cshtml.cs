using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pointage.Audit;
using Pointage.Presences;
using Pointage.Web;

namespace Pointage.Pages;

// Accueil du responsable : les arrivées du jour à valider ou refuser, et les présents.
public class IndexModel(PresenceStore presences, AuditLog audit) : PageModel
{
    public IReadOnlyList<PresenceRow> Pending { get; private set; } = [];
    public IReadOnlyList<PresenceRow> Present { get; private set; } = [];

    public void OnGet()
    {
        Pending = presences.PendingToday();
        Present = presences.ValidatedToday();
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

    public IActionResult OnPostValidateAll()
    {
        var count = presences.ValidateAllPendingToday(User.AccountId());
        audit.Write(User.AccountId(), AuditActions.Validate, details: $"{count} arrivée(s) validée(s) d'un coup");
        return RedirectToPage();
    }
}
