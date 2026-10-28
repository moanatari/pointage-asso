using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Pointage.Accounts;
using Pointage.Audit;

namespace Pointage.Tests;

public class WebTests
{
    [Fact]
    public async Task NoSuperAdmin_RedirectsToSetup()
    {
        using var app = new AppFactory();
        var response = await app.CreateClient().GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/Setup", response.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Setup_WhenSuperAdminExists_Returns404()
    {
        using var app = new AppFactory();
        app.Services.GetRequiredService<AccountStore>().Create("admin", AppFactory.Password, Role.SuperAdmin);
        var response = await app.CreateClient().GetAsync("/Setup");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Setup_Submit_CreatesSuperAdminAndShowsKioskUrl()
    {
        using var app = new AppFactory();
        var client = app.CreateClient();
        var form = await client.GetStringAsync("/Setup");
        var response = await client.PostAsync("/Setup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["OrganizationName"] = "Club des Lilas",
            ["Username"] = "admin",
            ["Password"] = AppFactory.Password,
            ["Confirm"] = AppFactory.Password,
            ["RetentionMonths"] = "24",
            ["KioskKeyboard"] = "false",
            ["__RequestVerificationToken"] = AppFactory.AntiforgeryToken(form),
        }));
        Assert.Contains($"http://{Environment.MachineName}:5080/borne", await response.Content.ReadAsStringAsync());
        Assert.True(app.Services.GetRequiredService<AccountStore>().AnySuperAdmin());
        var settings = app.Services.GetRequiredService<Pointage.Settings.SettingsStore>().Get();
        Assert.Equal(("Club des Lilas", 24, false), (settings.OrganizationName, settings.RetentionMonths, settings.KioskKeyboard));
        // La session ouverte par l'assistant donne accès à l'accueil.
        var home = await client.GetAsync("/");
        Assert.Equal("/", home.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData("192.168.1.20")]
    [InlineData(null)]
    public async Task RemoteIp_ManagerPages_Return404(string? ip)
    {
        using var app = new AppFactory(ip);
        app.Services.GetRequiredService<AccountStore>().Create("admin", AppFactory.Password, Role.SuperAdmin);
        var client = app.CreateClient();
        foreach (var path in new[] { "/", "/Login", "/Setup", "/Members", "/Admin/Settings" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task RemoteIp_StaticCss_IsServed()
    {
        using var app = new AppFactory("192.168.1.20");
        var response = await app.CreateClient().GetAsync("/static/site.css");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReachesHome()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.SuperAdmin);
        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Déconnexion", await response.Content.ReadAsStringAsync());
        Assert.Contains(app.Services.GetRequiredService<AuditLog>().Recent(10), e => e.Action == AuditActions.Login);
    }

    [Fact]
    public async Task Login_WrongPassword_StaysOnLogin()
    {
        using var app = new AppFactory();
        app.Services.GetRequiredService<AccountStore>().Create("admin", AppFactory.Password, Role.SuperAdmin);
        var client = app.CreateClient();
        var form = await client.GetStringAsync("/Login");
        var response = await client.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "admin",
            ["Password"] = "mauvais-mdp",
            ["__RequestVerificationToken"] = AppFactory.AntiforgeryToken(form),
        }));
        Assert.Contains("Identifiant ou mot de passe incorrect", await response.Content.ReadAsStringAsync());
        Assert.Equal("/Login", (await client.GetAsync("/")).RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Manager_AdminPages_AreForbidden()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var response = await client.GetAsync("/Admin/Settings");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Réglages", await (await client.GetAsync("/")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task NavLogout_SignsOut()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        // Le jeton est lu dans la page d'accueil (formulaire de la barre de navigation), pas sur /Logout.
        var home = await client.GetStringAsync("/");
        var response = await client.PostAsync("/Logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AppFactory.AntiforgeryToken(home),
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/Login", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Equal("/Login", (await client.GetAsync("/")).RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task DeactivatedAccount_LiveCookie_IsSignedOut()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        Assert.Equal("/", (await client.GetAsync("/")).RequestMessage!.RequestUri!.AbsolutePath);

        var accounts = app.Services.GetRequiredService<AccountStore>();
        accounts.SetActive(accounts.List().Single(a => a.Role == Role.Manager).Id, false);

        var response = await client.GetAsync("/");
        Assert.Equal("/Login", response.RequestMessage!.RequestUri!.AbsolutePath);
    }
}
