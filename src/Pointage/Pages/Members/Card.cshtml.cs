using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pointage.Members;
using Pointage.Settings;

namespace Pointage.Pages.Members;

public class CardModel(MemberStore members, SettingsStore settings) : PageModel
{
    public Member Member { get; private set; } = null!;
    public string Organization { get; private set; } = "";

    public IActionResult OnGet(long id)
    {
        if (members.Get(id) is not { AnonymizedAt: null } m) return NotFound();
        Member = m;
        Organization = settings.Get().OrganizationName;
        return Page();
    }
}
