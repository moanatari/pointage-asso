using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pointage.Audit;
using Pointage.Members;
using Pointage.Presences;
using Pointage.Web;

namespace Pointage.Pages.Presences;

// Correction d'une présence. Seules les heures sont modifiables : la date reste celle du pointage,
// ce qui garantit qu'on ne crée pas par erreur un second passage non refusé le même jour.
public class EditModel(PresenceStore presences, MemberStore members, AuditLog audit) : PageModel
{
    public Presence Presence { get; private set; } = null!;
    public string MemberName { get; private set; } = "";
    public string? Error { get; private set; }
    [BindProperty] public TimeOnly? ArrivedTime { get; set; }
    [BindProperty] public TimeOnly? DepartedTime { get; set; }

    private bool Load(long id)
    {
        if (presences.Get(id) is not { } p) return false;
        Presence = p;
        MemberName = members.Get(p.MemberId)?.FullName ?? "";
        return true;
    }

    public IActionResult OnGet(long id)
    {
        if (!Load(id)) return NotFound();
        ArrivedTime = TimeOnly.FromDateTime(Presence.ArrivedAt);
        DepartedTime = Presence.DepartedAt is { } d ? TimeOnly.FromDateTime(d) : null;
        return Page();
    }

    public IActionResult OnPostSave(long id)
    {
        if (!Load(id)) return NotFound();
        if (ArrivedTime is not { } arrived)
        {
            Error = "L'heure d'arrivée est obligatoire.";
            return Page();
        }
        var arrivedAt = Presence.Day.ToDateTime(arrived);
        var departedAt = DepartedTime is { } d ? Presence.Day.ToDateTime(d) : (DateTime?)null;
        if (departedAt < arrivedAt)
        {
            Error = "Le départ ne peut pas être avant l'arrivée.";
            return Page();
        }
        presences.UpdateTimes(id, arrivedAt, departedAt);
        audit.Write(User.AccountId(), AuditActions.PresenceEdit, Presence.MemberId,
            $"Présence n° {id} : arrivée {Presence.ArrivedAt:HH:mm} → {arrivedAt:HH:mm} ; départ {Show(Presence.DepartedAt)} → {Show(departedAt)}");
        return RedirectToPage("/Members/Details", new { id = Presence.MemberId });
    }

    private static string Show(DateTime? t) => t?.ToString("HH:mm") ?? "—";

    public IActionResult OnPostValidate(long id) => Decide(id, PresenceStatus.Validated, AuditActions.Validate);

    public IActionResult OnPostRefuse(long id) => Decide(id, PresenceStatus.Refused, AuditActions.Refuse);

    private IActionResult Decide(long id, PresenceStatus status, string action)
    {
        if (!Load(id)) return NotFound();
        presences.Decide(id, status, User.AccountId());
        audit.Write(User.AccountId(), action, Presence.MemberId, $"Présence n° {id} : {Presence.Status.Label()} → {status.Label()}");
        return RedirectToPage("/Members/Details", new { id = Presence.MemberId });
    }

    public IActionResult OnPostDelete(long id)
    {
        if (!Load(id)) return NotFound();
        presences.Delete(id);
        audit.Write(User.AccountId(), AuditActions.PresenceDelete, Presence.MemberId,
            $"Présence n° {id} du {Presence.Day:dd/MM/yyyy} {Presence.ArrivedAt:HH:mm} (statut {Presence.Status.Label()}) supprimée");
        return RedirectToPage("/Members/Details", new { id = Presence.MemberId });
    }
}
