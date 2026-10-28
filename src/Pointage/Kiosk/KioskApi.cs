using Pointage.Members;
using Pointage.Presences;
using Pointage.Settings;

namespace Pointage.Kiosk;

// API de la borne, joignable depuis le réseau. Elle ne renvoie que le strict nécessaire pour se reconnaître
// (prénom, initiale, code) : jamais le nom complet, la date de naissance ni les coordonnées.
public static class KioskApi
{
    public const string CookieName = "pointage_kiosk";

    public sealed record PairRequest(string? Code);
    public sealed record MemberRequest(long MemberId);
    // Type dédié plutôt que Member : aucun champ supplémentaire ne peut fuiter par mégarde.
    public sealed record KioskMember(long Id, string Label, string Code, bool HasPresenceToday);

    public static void MapKioskApi(this WebApplication app)
    {
        app.MapPost("/api/borne/pair", (PairRequest req, KioskPairing pairing, HttpContext ctx, TimeProvider time) =>
        {
            var token = pairing.Pair(req.Code ?? "");
            if (token is null) return Results.BadRequest();
            // Pas de Secure : la borne parle au poste en HTTP sur le réseau local. Strict suffit contre les requêtes d'autres sites.
            ctx.Response.Cookies.Append(CookieName, token, new CookieOptions
            {
                HttpOnly = true, SameSite = SameSiteMode.Strict, IsEssential = true, Expires = time.GetUtcNow().AddYears(10),
            });
            return Results.Ok();
        });

        var api = app.MapGroup("/api/borne").AddEndpointFilter(async (ctx, next) =>
            ctx.HttpContext.RequestServices.GetRequiredService<KioskPairing>().IsValid(ctx.HttpContext.Request.Cookies[CookieName])
                ? await next(ctx)
                : Results.Unauthorized());

        api.MapGet("/search", (string? q, MemberStore members, PresenceStore presences) =>
            (q?.Trim().Length ?? 0) < 2
                ? []
                : members.Search(q!, 8).Select(m => new KioskMember(m.Id, m.KioskLabel, m.Code, presences.HasPresenceToday(m.Id))).ToList());

        // Un id inconnu ou anonymisé est refusé : la borne ne doit pas pouvoir créer de présence pour n'importe quel id.
        api.MapPost("/checkin", (MemberRequest req, MemberStore members, PresenceStore presences) =>
            IsCheckable(members, req.MemberId)
                ? Results.Ok(new { result = presences.CheckIn(req.MemberId) == CheckInResult.Created ? "created" : "already" })
                : Results.NotFound());

        api.MapPost("/depart", (MemberRequest req, MemberStore members, PresenceStore presences) =>
            IsCheckable(members, req.MemberId) ? Results.Ok(new { ok = presences.Depart(req.MemberId) }) : Results.NotFound());

        api.MapGet("/config", (SettingsStore settings) =>
        {
            var s = settings.Get();
            return new { keyboard = s.KioskKeyboard, departure = s.KioskDeparture };
        });
    }

    private static bool IsCheckable(MemberStore members, long id) => members.Get(id) is { AnonymizedAt: null };
}
