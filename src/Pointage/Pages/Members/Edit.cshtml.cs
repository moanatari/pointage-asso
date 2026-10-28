using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pointage.Audit;
using Pointage.Data;
using Pointage.Members;
using Pointage.Web;

namespace Pointage.Pages.Members;

// Création (sans id) et modification d'une fiche membre.
public class EditModel(MemberStore members, AuditLog audit, TimeProvider time) : PageModel
{
    [BindProperty(SupportsGet = true)] public long? Id { get; set; }
    [BindProperty] public string LastName { get; set; } = "";
    [BindProperty] public string FirstName { get; set; } = "";
    [BindProperty] public DateOnly? BirthDate { get; set; }
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public string? Email { get; set; }
    [BindProperty] public string? Comment { get; set; }
    [BindProperty] public string? ContactLastName { get; set; }
    [BindProperty] public string? ContactFirstName { get; set; }
    [BindProperty] public DateOnly? ContactBirthDate { get; set; }
    [BindProperty] public string? ContactPhone { get; set; }
    [BindProperty] public string? ContactEmail { get; set; }
    public IReadOnlyList<string> Errors { get; private set; } = [];

    public IActionResult OnGet()
    {
        if (Id is null) return Page();
        // Un membre anonymisé n'a plus de données à modifier.
        if (members.Get(Id.Value) is not { AnonymizedAt: null } m) return NotFound();
        (LastName, FirstName, BirthDate, Phone, Email, Comment) = (m.LastName!, m.FirstName!, m.BirthDate, m.Phone, m.Email, m.Comment);
        (ContactLastName, ContactFirstName, ContactBirthDate, ContactPhone, ContactEmail) =
            (m.Contact.LastName, m.Contact.FirstName, m.Contact.BirthDate, m.Contact.Phone, m.Contact.Email);
        return Page();
    }

    public IActionResult OnPost()
    {
        if (Id is { } id && members.Get(id) is not { AnonymizedAt: null }) return NotFound();
        var errors = new List<string>();
        if (BirthDate is null) errors.Add("La date de naissance est obligatoire.");
        // Create ne valide pas : on contrôle ici avant d'écrire.
        var input = new MemberInput(LastName ?? "", FirstName ?? "", BirthDate ?? default, Phone, Email, Comment,
            new ContactInput(ContactLastName, ContactFirstName, ContactBirthDate, ContactPhone, ContactEmail));
        errors.AddRange(input.Validate(time.Today()));
        if (errors.Count > 0)
        {
            Errors = errors;
            return Page();
        }
        if (Id is { } existing)
        {
            members.Update(existing, input);
            audit.Write(User.AccountId(), AuditActions.MemberUpdate, existing);
            return RedirectToPage("Details", new { id = existing });
        }
        var created = members.Create(input);
        audit.Write(User.AccountId(), AuditActions.MemberCreate, created.Id);
        return RedirectToPage("Details", new { id = created.Id });
    }
}
