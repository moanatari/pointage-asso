using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Pointage.Accounts;

namespace Pointage.Tests;

// Application complète sur une base temporaire. TestServer ne renseigne pas l'IP du client :
// on la force pour simuler un appel depuis le poste (bouclage) ou depuis le réseau.
public sealed partial class AppFactory(string? remoteIp = "127.0.0.1") : WebApplicationFactory<Program>
{
    public const string Password = "motdepasse1";

    private readonly string dataDir = Path.Combine(Path.GetTempPath(), "pointage-web-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Pointage:DataDir", dataDir);
        // Dépôt vide : le worker de mises à jour ne fait aucun appel réseau pendant les tests.
        builder.UseSetting("Pointage:GitHubRepo", "");
        builder.ConfigureServices(s =>
        {
            s.AddSingleton<IStartupFilter>(new RemoteIpFilter(remoteIp));
            // Horloge du cookie de connexion pilotable : permet de tester la session glissante sans attendre.
            s.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, o => o.TimeProvider = AuthClock);
        });
    }

    public FakeTimeProvider AuthClock { get; } = new(DateTimeOffset.UtcNow);

    public async Task<HttpClient> LoggedInClientAsync(Role role)
    {
        var accounts = Services.GetRequiredService<AccountStore>();
        // Sans super admin, toute page redirige vers l'assistant : impossible de se connecter.
        if (role == Role.Manager && !accounts.AnySuperAdmin())
            accounts.Create("admin-" + Guid.NewGuid().ToString("N")[..8], Password, Role.SuperAdmin);
        var username = role + "-" + Guid.NewGuid().ToString("N")[..8];
        accounts.Create(username, Password, role);

        var client = CreateClient();
        var form = await client.GetStringAsync("/Login");
        var response = await client.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = username,
            ["Password"] = Password,
            ["__RequestVerificationToken"] = AntiforgeryToken(form),
        }));
        response.EnsureSuccessStatusCode();
        return client;
    }

    // Lit le jeton antiforgery dans le formulaire de la page, puis poste vers le gestionnaire voulu.
    public static async Task<HttpResponseMessage> PostFormAsync(HttpClient c, string pageUrl, string handlerUrl, Dictionary<string, string> fields)
    {
        var token = AntiforgeryToken(await c.GetStringAsync(pageUrl));
        return await c.PostAsync(handlerUrl, new FormUrlEncodedContent(new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = token }));
    }

    public static string AntiforgeryToken(string html) => TokenRegex().Match(html).Groups[1].Value;

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();

    private sealed class RemoteIpFilter(string? ip) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nxt) =>
            {
                ctx.Connection.RemoteIpAddress = ip is null ? null : IPAddress.Parse(ip);
                return nxt(ctx);
            });
            next(app);
        };
    }
}
