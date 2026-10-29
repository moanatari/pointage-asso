using Microsoft.AspNetCore.Mvc.RazorPages;
using Pointage.Kiosk;

namespace Pointage.Pages.Borne;

// Page sans données : tout passe ensuite par /api/borne, qui revérifie le jeton à chaque appel.
public class IndexModel(KioskPairing pairing) : PageModel
{
    public bool Paired { get; private set; }

    public void OnGet() => Paired = pairing.IsValid(Request.Cookies[KioskApi.CookieName]);
}
