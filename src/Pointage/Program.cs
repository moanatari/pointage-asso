using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Hosting.WindowsServices;
using Pointage.Accounts;
using Pointage.Audit;
using Pointage.Data;
using Pointage.Kiosk;
using Pointage.Members;
using Pointage.Presences;
using Pointage.Settings;
using Pointage.Stats;
using Pointage.Updates;
using Pointage.Web;

var builder = WebApplication.CreateBuilder(args);

// Récupération d'accès en ligne de commande : seul quelqu'un qui a la main sur le poste peut le faire.
if (args is ["reset-admin", var user])
{
    ResetAdmin.Run(OpenDb(builder.Configuration), user);
    return;
}

// Secours si le port choisi est bloqué : « Pointage.exe set-port <port> » (le reste des arguments, ex. --Pointage:DataDir, est ignoré ici).
if (args is ["set-port", var port, ..])
{
    Environment.ExitCode = NetworkPort.SetPortCommand(OpenDb(builder.Configuration), port);
    return;
}

// Sans effet avec « dotnet run » : n'agit que lorsque le programme est lancé comme service Windows.
builder.Host.UseWindowsService();

// Le port vient de la base (il survit aux mises à jour). Hors service (dev, tests), appsettings.json reste la source
// et la base n'est pas lue avant Build() : les tests y injectent DataDir tardivement.
if (WindowsServiceHelpers.IsWindowsService())
{
    var servicePort = new SettingsStore(OpenDb(builder.Configuration)).Get().Port;
    if (!NetworkPort.IsValid(servicePort)) servicePort = NetworkPort.Default;
    builder.WebHost.UseUrls($"http://0.0.0.0:{servicePort}");
    NetworkPort.SyncFirewall(servicePort);
}

// Base créée à la première résolution (et non pendant la configuration) : les tests y injectent leur dossier temporaire.
builder.Services.AddSingleton(sp => OpenDb(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AccountStore>();
builder.Services.AddSingleton<AuditLog>();
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<MemberStore>();
builder.Services.AddSingleton<PresenceStore>();
builder.Services.AddSingleton<StatsService>();
builder.Services.AddSingleton<KioskPairing>();
// Singleton (et non client typé transitoire) : la dernière version trouvée par le worker doit être vue par la page.
// ponytail: HttpClient gardé à vie, sans rotation DNS ; acceptable pour un appel par jour.
builder.Services.AddHttpClient(nameof(UpdateService));
builder.Services.AddSingleton(sp => ActivatorUtilities.CreateInstance<UpdateService>(sp,
    sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(UpdateService))));
builder.Services.AddHostedService<UpdateCheckWorker>();

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/");
    o.Conventions.AuthorizeFolder("/Admin", "SuperAdmin");
    o.Conventions.AllowAnonymousToPage("/Login");
    o.Conventions.AllowAnonymousToPage("/Setup");
    o.Conventions.AllowAnonymousToFolder("/Borne");
    // Aide accessible sans connexion (utile quand on a perdu son mot de passe) ; reste locale grâce à LocalOnlyMiddleware.
    o.Conventions.AllowAnonymousToFolder("/Aide");
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "pointage_auth";
    o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    o.SlidingExpiration = true;
    o.LoginPath = "/Login";
    // Un responsable connecté qui ouvre /Admin/* reçoit un 403 explicite (et non un 404).
    o.AccessDeniedPath = "/AccessDenied";
    // Le rafraîchissement automatique de l'accueil (/?auto=1) ne doit pas prolonger la session glissante,
    // sinon un poste laissé ouvert ne se déconnecterait jamais.
    o.Events.OnCheckSlidingExpiration = ctx =>
    {
        if (ctx.HttpContext.Request.Query.ContainsKey("auto")) ctx.ShouldRenew = false;
        return Task.CompletedTask;
    };
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
// .ps1 n'a pas de type MIME connu : sans cela, le fichier de la borne serait refusé (404).
var staticTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
staticTypes.Mappings[".ps1"] = "application/octet-stream";
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = staticTypes });
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();
app.MapKioskApi();
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
