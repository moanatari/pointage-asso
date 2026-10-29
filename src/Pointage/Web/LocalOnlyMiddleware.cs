using System.Net;
using Pointage.Accounts;

namespace Pointage.Web;

// Les données des enfants ne sont lisibles que sur le poste lui-même : depuis le réseau,
// seules la borne et les fichiers statiques répondent, tout le reste fait comme s'il n'existait pas.
public sealed class LocalOnlyMiddleware(RequestDelegate next)
{
    public static bool IsPublicPath(PathString p) =>
        p.StartsWithSegments("/borne") || p.StartsWithSegments("/api/borne") || p.StartsWithSegments("/static");

    public Task InvokeAsync(HttpContext ctx, AccountStore accounts)
    {
        if (IsPublicPath(ctx.Request.Path)) return next(ctx);

        // Le Host est vérifié aussi : une page web piégée peut faire pointer son propre domaine sur 127.0.0.1
        // (DNS rebinding), ses requêtes viennent alors bien de la boucle locale.
        if (!IsLoopback(ctx.Connection.RemoteIpAddress) || !IsLocalHost(ctx.Request.Host.Host))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        // Tant que l'assistant n'a pas créé de super admin, il est la seule page utilisable.
        if (!ctx.Request.Path.StartsWithSegments("/Setup") && !accounts.AnySuperAdmin())
        {
            ctx.Response.Redirect("/Setup");
            return Task.CompletedTask;
        }

        return next(ctx);
    }

    // IP inconnue = refus : on ne laisse jamais passer faute d'information.
    private static bool IsLoopback(IPAddress? ip) =>
        ip is not null && IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip);

    private static bool IsLocalHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host is "127.0.0.1" or "[::1]";
}
