using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pointage.Audit;
using Pointage.Members;
using Pointage.Presences;
using Pointage.Web;

namespace Pointage.Pages.Members;

// Export des données d'un membre (droit d'accès RGPD) : l'ouverture de la page est elle-même journalisée.
public class ExportModel(MemberStore members, PresenceStore presences, AuditLog audit) : PageModel
{
    public Member Member { get; private set; } = null!;
    public IReadOnlyList<PresenceRow> Presences { get; private set; } = [];

    public IActionResult OnGet(long id)
    {
        if (members.Get(id) is not { AnonymizedAt: null } m) return NotFound();
        Member = m;
        Presences = presences.ForMember(id, -1); // -1 : sans limite
        audit.Write(User.AccountId(), AuditActions.MemberExport, id);
        return Page();
    }
}
