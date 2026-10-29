using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pointage.Audit;
using Pointage.Data;
using Pointage.Members;
using Pointage.Presences;
using Pointage.Stats;
using Pointage.Web;

namespace Pointage.Pages.Members;

public class DetailsModel(MemberStore members, PresenceStore presences, StatsService stats, AuditLog audit, TimeProvider time) : PageModel
{
    public Member Member { get; private set; } = null!;
    public MemberStats Stats { get; private set; } = null!;
    public IReadOnlyList<PresenceRow> History { get; private set; } = [];
    public DateOnly Today { get; private set; }

    public IActionResult OnGet(long id)
    {
        if (members.Get(id) is not { } m) return NotFound();
        Member = m;
        Today = time.Today();
        Stats = stats.ForMember(id);
        History = presences.ForMember(id, 50);
        return Page();
    }

    // En attente un jour passé : plus personne ne la validera, l'historique l'affiche « non validée ».
    public string StatusLabel(Presence p) => p.Status switch
    {
        PresenceStatus.Validated => "✅ validé",
        PresenceStatus.Refused => "❌ refusé",
        _ => p.Day < Today ? "non validée" : "⏳ en attente",
    };

    public IActionResult OnPostAnonymize(long id)
    {
        if (members.Get(id) is null) return NotFound();
        members.Anonymize(id);
        audit.Write(User.AccountId(), AuditActions.MemberAnonymize, id);
        return RedirectToPage(new { id });
    }

    public IActionResult OnPostDelete(long id)
    {
        if (members.Get(id) is null) return NotFound();
        members.Delete(id);
        // Le membre n'existe plus : le journal garde son identifiant, jamais son nom.
        audit.Write(User.AccountId(), AuditActions.MemberDelete, id);
        return RedirectToPage("Index");
    }
}
