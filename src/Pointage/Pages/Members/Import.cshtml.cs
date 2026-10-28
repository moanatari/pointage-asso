using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pointage.Audit;
using Pointage.Data;
using Pointage.Members;
using Pointage.Web;

namespace Pointage.Pages.Members;

// Import en deux temps : l'aperçu n'écrit rien, la confirmation ré-analyse le texte renvoyé par le formulaire.
public class ImportModel(MemberStore members, AuditLog audit, TimeProvider time) : PageModel
{
    private const int MaxBytes = 1024 * 1024;

    [BindProperty] public IFormFile? Upload { get; set; }
    [BindProperty] public string? Csv { get; set; }
    public IReadOnlyList<ImportRow>? Rows { get; private set; }
    public string? Error { get; private set; }
    public int? Created { get; private set; }

    public void OnGet() { }

    // Le BOM permet à Excel d'ouvrir le modèle en UTF-8 avec les accents corrects.
    public IActionResult OnGetTemplate() =>
        File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(MemberCsvImport.Template)).ToArray(), "text/csv", "modele-membres.csv");

    public async Task<IActionResult> OnPostPreviewAsync()
    {
        if (Upload is null || Upload.Length == 0) Error = "Choisissez un fichier CSV.";
        else if (Upload.Length > MaxBytes) Error = "Le fichier dépasse 1 Mo.";
        else
        {
            using var ms = new MemoryStream();
            await Upload.CopyToAsync(ms);
            Csv = MemberCsvImport.Decode(ms.ToArray());
            Rows = Parse(Csv);
        }
        return Page();
    }

    public IActionResult OnPostConfirm()
    {
        if (string.IsNullOrEmpty(Csv) || Csv.Length > MaxBytes)
        {
            Error = "Fichier absent ou trop volumineux.";
            return Page();
        }
        // Le formulaire peut avoir été modifié : on ne fait confiance qu'au texte, ré-analysé et revérifié.
        var created = 0;
        foreach (var row in Parse(Csv))
        {
            if (row.Input is null || row.Errors.Count > 0 || row.Duplicate) continue;
            members.Create(row.Input);
            created++;
        }
        audit.Write(User.AccountId(), AuditActions.MemberImport, details: created.ToString());
        Csv = null;
        Created = created;
        return Page();
    }

    private IReadOnlyList<ImportRow> Parse(string text) => MemberCsvImport.Parse(text, members.Exists, time.Today());
}
