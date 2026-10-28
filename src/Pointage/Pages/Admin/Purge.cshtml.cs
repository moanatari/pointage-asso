using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pointage.Audit;
using Pointage.Data;
using Pointage.Members;
using Pointage.Settings;
using Pointage.Web;

namespace Pointage.Pages.Admin;

public class PurgeModel(MemberStore members, SettingsStore settings, AuditLog audit, TimeProvider time) : PageModel
{
    public int RetentionMonths { get; private set; }
    public IReadOnlyList<(Member Member, DateOnly? LastVisit)> Candidates { get; private set; } = [];
    public int? Purged { get; private set; }

    public void OnGet() => Load();

    public IActionResult OnPost()
    {
        // Liste recalculée ici et non lue dans le formulaire : on n'anonymise jamais ce que le navigateur prétend.
        var retention = settings.Get().RetentionMonths;
        var toPurge = members.MembersToPurge(time.Today(), retention);
        foreach (var m in toPurge) members.Anonymize(m.Id);
        // Le journal suit la même durée de conservation que les membres.
        audit.DeleteOlderThan(time.Now().AddMonths(-retention));
        audit.Write(User.AccountId(), AuditActions.Purge, details: $"{toPurge.Count} membre(s) anonymisé(s)");
        Purged = toPurge.Count;
        Load();
        return Page();
    }

    private void Load()
    {
        RetentionMonths = settings.Get().RetentionMonths;
        Candidates = members.MembersToPurge(time.Today(), RetentionMonths).Select(m => (m, members.LastVisit(m.Id))).ToList();
    }
}
