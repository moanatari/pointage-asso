using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pointage.Audit;
using Pointage.Data;
using Pointage.Members;
using Pointage.Presences;
using Pointage.Web;

namespace Pointage.Pages.Presences;

// Ajout manuel (membre venu sans pointer) : en deux temps, on cherche le membre puis on saisit la date et l'heure.
public class AddModel(MemberStore members, PresenceStore presences, AuditLog audit, TimeProvider time) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public long? MemberId { get; set; }
    [BindProperty] public DateOnly? Day { get; set; }
    [BindProperty] public TimeOnly? Time { get; set; }
    public Member? Selected { get; private set; }
    public IReadOnlyList<Member> Results { get; private set; } = [];
    public string? Error { get; private set; }

    public void OnGet()
    {
        Load();
        Day = time.Today();
        Time = TimeOnly.FromDateTime(time.Now());
    }

    public IActionResult OnPost()
    {
        Load();
        if (Selected is null) return RedirectToPage();
        if (Day is not { } day || Time is not { } t)
        {
            Error = "La date et l'heure sont obligatoires.";
            return Page();
        }
        try
        {
            var id = presences.AddManual(Selected.Id, day.ToDateTime(t), User.AccountId());
            audit.Write(User.AccountId(), AuditActions.PresenceAdd, Selected.Id, $"Présence n° {id} ajoutée le {day:dd/MM/yyyy} à {t:HH:mm}");
        }
        catch (InvalidOperationException e)
        {
            Error = e.Message;
            return Page();
        }
        return RedirectToPage("/Members/Details", new { id = Selected.Id });
    }

    private void Load()
    {
        if (MemberId is { } id && members.Get(id) is { AnonymizedAt: null } m) Selected = m;
        else if (!string.IsNullOrWhiteSpace(Q)) Results = members.Search(Q, 20);
    }
}
