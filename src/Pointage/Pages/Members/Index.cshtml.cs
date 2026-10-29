using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pointage.Data;
using Pointage.Members;

namespace Pointage.Pages.Members;

public class IndexModel(MemberStore members, TimeProvider time) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    public IReadOnlyList<Member> Results { get; private set; } = [];
    public DateOnly Today { get; private set; }

    public void OnGet()
    {
        Today = time.Today();
        Results = string.IsNullOrWhiteSpace(Q) ? members.List() : members.Search(Q, 200);
    }
}
