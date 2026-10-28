using Microsoft.AspNetCore.Mvc.RazorPages;
using Pointage.Accounts;
using Pointage.Audit;
using Pointage.Members;

namespace Pointage.Pages.Admin;

public record JournalLine(DateTime At, string Account, string Action, string? Member, string? Details);

public class JournalModel(AuditLog audit, AccountStore accounts, MemberStore members) : PageModel
{
    public IReadOnlyList<JournalLine> Lines { get; private set; } = [];

    public void OnGet()
    {
        var names = accounts.List().ToDictionary(a => a.Id, a => a.Username);
        Lines = audit.Recent(200).Select(e => new JournalLine(
            e.At,
            e.AccountId is { } a ? names.GetValueOrDefault(a, $"compte n° {a}") : "—",
            AuditActions.Label(e.Action),
            e.MemberId is { } m ? members.Get(m)?.FullName ?? $"membre supprimé n° {m}" : null,
            e.Details)).ToList();
    }
}
