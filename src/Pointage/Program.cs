using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Pointage.Accounts;
using Pointage.Audit;
using Pointage.Data;
using Pointage.Members;
using Pointage.Presences;
using Pointage.Settings;
using Pointage.Stats;
using Pointage.Web;

var builder = WebApplication.CreateBuilder(args);

// Récupération d'accès en ligne de commande : seul quelqu'un qui a la main sur le poste peut le faire.
if (args is ["reset-admin", var user])
{
    ResetAdmin.Run(OpenDb(builder.Configuration), user);
    return;
}

// Sans effet avec « dotnet run » : n'agit que lorsque le programme est lancé comme service Windows.
builder.Host.UseWindowsService();

// Base créée à la première résolution (et non pendant la configuration) : les tests y injectent leur dossier temporaire.
builder.Services.AddSingleton(sp => OpenDb(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AccountStore>();
builder.Services.AddSingleton<AuditLog>();
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<MemberStore>();
builder.Services.AddSingleton<PresenceStore>();
builder.Services.AddSingleton<StatsService>();

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/");
    o.Conventions.AuthorizeFolder("/Admin", "SuperAdmin");
    o.Conventions.AllowAnonymousToPage("/Login");
    o.Conventions.AllowAnonymousToPage("/Setup");
    o.Conventions.AllowAnonymousToFolder("/Borne");
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "pointage_auth";
    o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    o.SlidingExpiration = true;
    o.LoginPath = "/Login";
    // Relu à chaque requête : un compte désactivé perd l'accès tout de suite, même avec un cookie encore valide.
    o.Events.OnValidatePrincipal = async ctx =>
    {
        var account = ctx.HttpContext.RequestServices.GetRequiredService<AccountStore>().Get(ctx.Principal!.AccountId());
        if (account is { Active: true }) return;
        ctx.RejectPrincipal();
        await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    };
});
builder.Services.AddAuthorizationBuilder().AddPolicy("SuperAdmin", p => p.RequireRole(nameof(Role.SuperAdmin)));

var app = builder.Build();

// Migration au démarrage plutôt qu'à la première requête : une erreur de base se voit tout de suite.
app.Services.GetRequiredService<Db>();

// En premier : aucune page du responsable ne doit répondre à une autre machine, même pas par une erreur.
app.UseMiddleware<LocalOnlyMiddleware>();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();
app.Run();

static Db OpenDb(IConfiguration config)
{
    var dir = config["Pointage:DataDir"];
    if (string.IsNullOrWhiteSpace(dir))
        dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Pointage");
    Directory.CreateDirectory(dir);
    var db = new Db(Path.Combine(dir, "pointage.db"));
    db.Migrate();
    return db;
}

public partial class Program;
