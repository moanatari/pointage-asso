using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Pointage.Accounts;

namespace Pointage.Web;

public static class UserExtensions
{
    public static long AccountId(this ClaimsPrincipal u) =>
        long.Parse(u.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);

    // Partagé par la connexion et l'assistant : un seul endroit décide du contenu du cookie.
    public static Task SignInAsync(this HttpContext ctx, Account account) =>
        ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, account.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, account.Username),
            new Claim(ClaimTypes.Role, account.Role.ToString()),
        ], CookieAuthenticationDefaults.AuthenticationScheme)));
}
