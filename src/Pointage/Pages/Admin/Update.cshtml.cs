using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pointage.Audit;
using Pointage.Updates;
using Pointage.Web;

namespace Pointage.Pages.Admin;

public class UpdateModel(UpdateService updates, AuditLog audit) : PageModel
{
    public ReleaseInfo? Latest => updates.Latest;
    public string? Message { get; private set; }

    public void OnGet() { }

    public async Task OnPostCheckAsync(CancellationToken ct)
    {
        await updates.CheckAsync(ct);
        if (updates.Latest is null) Message = "Aucune nouvelle version trouvée.";
    }

    public async Task OnPostApplyAsync(CancellationToken ct)
    {
        // Version relue côté serveur, jamais prise dans le formulaire : le navigateur ne choisit pas ce qui est installé.
        if (updates.Latest is not { } release)
        {
            Message = "Aucune nouvelle version à installer.";
            return;
        }
        audit.Write(User.AccountId(), AuditActions.Update, details: $"Version {release.Version}");
        try
        {
            await updates.ApplyAsync(release, ct);
            Message = "Mise à jour en cours, la page reviendra dans une minute.";
        }
        // Hachage refusé, réseau, disque ou lancement impossible : on affiche la cause au lieu d'une page d'erreur.
        catch (Exception e)
        {
            Message = "Échec de la mise à jour : " + e.Message;
        }
    }
}
