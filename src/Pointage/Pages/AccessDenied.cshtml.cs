using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Pointage.Pages;

// Cible de AccessDeniedPath : un responsable qui ouvre /Admin/* voit un 403 explicite plutôt qu'un 404.
public class AccessDeniedModel : PageModel
{
    public void OnGet() => Response.StatusCode = StatusCodes.Status403Forbidden;
}
