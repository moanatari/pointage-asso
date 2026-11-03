using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Pointage.Accounts;
using Pointage.Audit;
using Pointage.Members;
using Pointage.Presences;

namespace Pointage.Tests;

public class ManagerPagesTests
{
    private static Member CreateLucas(AppFactory app) =>
        app.Services.GetRequiredService<MemberStore>()
            .Create(new MemberInput("Dupont", "Lucas", new DateOnly(2015, 3, 12), "0600000000", "a@b.fr", null,
                new ContactInput("Dupont", "Anne", null, "0611111111", null)));

    // Razor encode les caractères accentués en entités HTML : on les décode avant de comparer.
    private static async Task<string> Text(HttpResponseMessage r) => WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());

    [Fact]
    public async Task Home_ValidatePending_MovesToPresent()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var lucas = CreateLucas(app);
        var presences = app.Services.GetRequiredService<PresenceStore>();
        presences.CheckIn(lucas.Id);
        var id = Assert.Single(presences.PendingToday()).Presence.Id;

        Assert.Contains("À valider (1)", WebUtility.HtmlDecode(await client.GetStringAsync("/")));

        (await AppFactory.PostFormAsync(client, "/", $"/?handler=Validate&id={id}", [])).EnsureSuccessStatusCode();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/"));
        Assert.Contains("À valider (0)", html);
        var present = html[html.IndexOf("Présents aujourd'hui (1)", StringComparison.Ordinal)..];
        Assert.Contains("Lucas Dupont", present);
        Assert.Equal(PresenceStatus.Validated, presences.Get(id)!.Status);
    }

    [Fact]
    public async Task Home_Refuse_IsKeptAsRefusedAndAudited()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var lucas = CreateLucas(app);
        var presences = app.Services.GetRequiredService<PresenceStore>();
        presences.CheckIn(lucas.Id);
        var id = Assert.Single(presences.PendingToday()).Presence.Id;

        (await AppFactory.PostFormAsync(client, "/", $"/?handler=Refuse&id={id}", [])).EnsureSuccessStatusCode();

        Assert.Equal(PresenceStatus.Refused, presences.Get(id)!.Status);
        Assert.Empty(presences.Today().Present);
        Assert.Empty(presences.Today().Departed);
        var entry = Assert.Single(app.Services.GetRequiredService<AuditLog>().Recent(10), e => e.Action == AuditActions.Refuse);
        Assert.Equal(lucas.Id, entry.MemberId);
    }

    private static string Section(string html, string title)
    {
        var start = html.IndexOf(title, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Section absente : {title}");
        var end = html.IndexOf("</section>", start, StringComparison.Ordinal);
        return html[start..end];
    }

    [Fact]
    public async Task Home_DepartedMember_ListedUnderPartisNotPresents()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var lucas = CreateLucas(app);
        var presences = app.Services.GetRequiredService<PresenceStore>();
        presences.CheckIn(lucas.Id);
        presences.Decide(presences.PendingToday().Single().Presence.Id, PresenceStatus.Validated, 1);
        presences.Depart(lucas.Id, false);
        var left = presences.Today().Departed.Single().Presence.DepartedAt!.Value;

        var html = await Text(await client.GetAsync("/"));

        Assert.Contains("Présents aujourd'hui (0)", html);
        var partis = Section(html, "Partis aujourd'hui (1)");
        Assert.Contains("Lucas Dupont", partis);
        Assert.Contains($"parti {left:HH:mm}", partis);
    }

    [Fact]
    public async Task Home_PendingArrival_ShownInPresentsWithMarker()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        app.Services.GetRequiredService<PresenceStore>().CheckIn(CreateLucas(app).Id);

        var html = await Text(await client.GetAsync("/"));

        Assert.Contains("(à valider)", Section(html, "Présents aujourd'hui (1)"));
    }

    [Fact]
    public async Task Home_PendingNameLinksToDetails()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var lucas = CreateLucas(app);
        app.Services.GetRequiredService<PresenceStore>().CheckIn(lucas.Id);

        var html = await Text(await client.GetAsync("/"));

        Assert.Contains($"<a href=\"/Members/Details/{lucas.Id}\">Lucas Dupont</a>", Section(html, "À valider (1)"));
    }

    [Fact]
    public async Task Purge_MemberNameLinksToDetails()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.SuperAdmin);
        var old = CreateLucas(app);
        var admin = app.Services.GetRequiredService<AccountStore>().List()[0].Id;
        app.Services.GetRequiredService<PresenceStore>().AddManual(old.Id, new DateTime(2020, 1, 1, 9, 0, 0), admin);

        var html = await Text(await client.GetAsync("/Admin/Purge"));

        Assert.Contains($"<a href=\"/Members/Details/{old.Id}\">Lucas Dupont</a>", html);
    }

    [Fact]
    public async Task Home_PendingDepartureNameLinksToDetails()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var (_, memberId) = PendingDeparture(app);

        var html = await Text(await client.GetAsync("/"));

        Assert.Contains($"<a href=\"/Members/Details/{memberId}\">Lucas Dupont</a>", Section(html, "Départs à valider (1)"));
    }

    private static (long Id, long MemberId) PendingDeparture(AppFactory app)
    {
        var presences = app.Services.GetRequiredService<PresenceStore>();
        var lucas = CreateLucas(app);
        presences.CheckIn(lucas.Id);
        presences.Depart(lucas.Id, true);
        return (presences.PendingDepartures().Single().Presence.Id, lucas.Id);
    }

    [Fact]
    public async Task Home_RefuseDeparture_MemberBackInPresents_AndAudited()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var (id, memberId) = PendingDeparture(app);
        Assert.Contains("Départs à valider (1)", await Text(await client.GetAsync("/")));

        (await AppFactory.PostFormAsync(client, "/", $"/?handler=RefuseDeparture&id={id}", [])).EnsureSuccessStatusCode();

        var presences = app.Services.GetRequiredService<PresenceStore>();
        Assert.Empty(presences.PendingDepartures());
        Assert.Contains("Présents aujourd'hui (1)", await Text(await client.GetAsync("/")));
        var entry = Assert.Single(app.Services.GetRequiredService<AuditLog>().Recent(10), e => e.Action == AuditActions.DepartureRefuse);
        Assert.Equal(memberId, entry.MemberId);
    }

    [Fact]
    public async Task Home_ValidateDeparture_Audited()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var (id, memberId) = PendingDeparture(app);

        (await AppFactory.PostFormAsync(client, "/", $"/?handler=ValidateDeparture&id={id}", [])).EnsureSuccessStatusCode();

        Assert.Empty(app.Services.GetRequiredService<PresenceStore>().PendingDepartures());
        Assert.Contains("Partis aujourd'hui (1)", await Text(await client.GetAsync("/")));
        var entry = Assert.Single(app.Services.GetRequiredService<AuditLog>().Recent(10), e => e.Action == AuditActions.DepartureValidate);
        Assert.Equal(memberId, entry.MemberId);
    }

    [Fact]
    public async Task Home_ValidateAll_ValidatesOnlyDisplayedArrivals()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var presences = app.Services.GetRequiredService<PresenceStore>();
        var lucas = CreateLucas(app);
        var lea = app.Services.GetRequiredService<MemberStore>()
            .Create(new MemberInput("Martin", "Léa", new DateOnly(2014, 1, 2), null, null, null, new ContactInput(null, null, null, null, null)));
        presences.CheckIn(lucas.Id);
        var shown = Assert.Single(presences.PendingToday()).Presence.Id;
        var html = await client.GetStringAsync("/"); // rendu de la page : une seule arrivée affichée
        Assert.Contains($"name=\"ids\" value=\"{shown}\"", html);

        presences.CheckIn(lea.Id); // arrivée après le rendu
        var response = await client.PostAsync("/?handler=ValidateAll", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ids"] = shown.ToString(), ["__RequestVerificationToken"] = AppFactory.AntiforgeryToken(html),
        }));
        response.EnsureSuccessStatusCode();

        Assert.Equal(PresenceStatus.Validated, presences.Get(shown)!.Status);
        Assert.Equal(lea.Id, Assert.Single(presences.PendingToday()).Presence.MemberId);
    }

    [Fact]
    public async Task Home_AutoRefresh_DoesNotRenewSession_ButNormalRequestDoes()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        app.AuthClock.Advance(TimeSpan.FromMinutes(20)); // au-delà de la moitié de la session : le renouvellement est dû

        var auto = await client.GetAsync("/?auto=1");
        auto.EnsureSuccessStatusCode();
        Assert.False(auto.Headers.TryGetValues("Set-Cookie", out var renewed) && renewed.Any(c => c.StartsWith("pointage_auth=")));

        var normal = await client.GetAsync("/");
        normal.EnsureSuccessStatusCode();
        Assert.Contains(normal.Headers.GetValues("Set-Cookie"), c => c.StartsWith("pointage_auth="));
    }

    [Fact]
    public async Task CreateMember_MissingFirstName_ShowsError()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var response = await AppFactory.PostFormAsync(client, "/Members/Edit", "/Members/Edit", new()
        {
            ["LastName"] = "Martin", ["FirstName"] = "", ["BirthDate"] = "2012-05-01",
        });
        Assert.Contains("Le prénom est obligatoire.", await Text(response));
        Assert.Empty(app.Services.GetRequiredService<MemberStore>().List());
    }

    [Fact]
    public async Task CreateMember_Valid_CreatesAndAudits()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var response = await AppFactory.PostFormAsync(client, "/Members/Edit", "/Members/Edit", new()
        {
            ["LastName"] = "Martin", ["FirstName"] = "Inès", ["BirthDate"] = "2012-05-01",
        });
        response.EnsureSuccessStatusCode();
        var member = Assert.Single(app.Services.GetRequiredService<MemberStore>().List());
        Assert.Equal("Inès Martin", member.FullName);
        Assert.Contains("Inès Martin", await Text(response)); // redirigé vers la fiche
        Assert.Contains(app.Services.GetRequiredService<AuditLog>().Recent(10), e => e.Action == AuditActions.MemberCreate && e.MemberId == member.Id);
    }

    [Fact]
    public async Task Anonymize_RemovesFromSearch()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var lucas = CreateLucas(app);
        Assert.Contains("Dupont", await Text(await client.GetAsync("/Members?q=lucas")));

        var url = $"/Members/Details/{lucas.Id}";
        (await AppFactory.PostFormAsync(client, url, url + "?handler=Anonymize", [])).EnsureSuccessStatusCode();

        Assert.Empty(app.Services.GetRequiredService<MemberStore>().Search("lucas", 10));
        Assert.DoesNotContain("Dupont", await Text(await client.GetAsync("/Members?q=lucas")));
        Assert.Contains(app.Services.GetRequiredService<AuditLog>().Recent(10), e => e.Action == AuditActions.MemberAnonymize && e.MemberId == lucas.Id);
    }

    [Fact]
    public async Task Export_WritesAuditEntry()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var lucas = CreateLucas(app);
        var response = await client.GetAsync($"/Members/Export/{lucas.Id}");
        Assert.Contains("0600000000", await Text(response));
        Assert.Contains(app.Services.GetRequiredService<AuditLog>().Recent(10), e => e.Action == AuditActions.MemberExport && e.MemberId == lucas.Id);
    }

    [Fact]
    public async Task EditPresence_ShowsFrenchStatus_AndAuditsInFrench()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var lucas = CreateLucas(app);
        var presences = app.Services.GetRequiredService<PresenceStore>();
        var id = presences.AddManual(lucas.Id, new DateTime(2026, 10, 20, 9, 30, 0), 1); // validée d'office
        var url = $"/Presences/Edit/{id}";

        var html = await Text(await client.GetAsync(url));
        Assert.Contains("statut actuel : validé", html);
        Assert.DoesNotContain("Validated", html);

        (await AppFactory.PostFormAsync(client, url, url + "?handler=Refuse", [])).EnsureSuccessStatusCode();
        var entry = Assert.Single(app.Services.GetRequiredService<AuditLog>().Recent(10), e => e.Action == AuditActions.Refuse);
        Assert.Contains("validé → refusé", entry.Details);
    }

    [Fact]
    public async Task Card_ShowsFirstNameAndCode()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var lucas = CreateLucas(app);
        var html = await Text(await client.GetAsync($"/Members/Card/{lucas.Id}"));
        Assert.Contains("Lucas", html);
        Assert.Contains(lucas.Code, html);
    }

    [Fact]
    public async Task AddPresence_Twice_CreatesTwoPassages()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var lucas = CreateLucas(app);
        var fields = new Dictionary<string, string> { ["MemberId"] = lucas.Id.ToString(), ["Day"] = "2026-10-20", ["Time"] = "09:30" };
        var url = $"/Presences/Add?memberId={lucas.Id}";
        (await AppFactory.PostFormAsync(client, url, "/Presences/Add", fields)).EnsureSuccessStatusCode();
        (await AppFactory.PostFormAsync(client, url, "/Presences/Add", fields)).EnsureSuccessStatusCode();
        Assert.Equal(2, app.Services.GetRequiredService<PresenceStore>().ForMember(lucas.Id, 10).Count);
    }

    [Fact]
    public async Task EditPresence_DepartureBeforeArrival_ShowsError_ValidSaves()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var lucas = CreateLucas(app);
        var presences = app.Services.GetRequiredService<PresenceStore>();
        var id = presences.AddManual(lucas.Id, new DateTime(2026, 10, 20, 9, 30, 0), 1);
        var url = $"/Presences/Edit/{id}";

        var bad = await AppFactory.PostFormAsync(client, url, url + "?handler=Save", new() { ["ArrivedTime"] = "10:00", ["DepartedTime"] = "09:00" });
        Assert.Contains("Le départ ne peut pas être avant l'arrivée.", await Text(bad));
        Assert.Equal(new DateTime(2026, 10, 20, 9, 30, 0), presences.Get(id)!.ArrivedAt);

        (await AppFactory.PostFormAsync(client, url, url + "?handler=Save", new() { ["ArrivedTime"] = "10:00", ["DepartedTime"] = "11:15" })).EnsureSuccessStatusCode();
        var saved = presences.Get(id)!;
        Assert.Equal(new DateTime(2026, 10, 20, 10, 0, 0), saved.ArrivedAt);
        Assert.Equal(new DateTime(2026, 10, 20, 11, 15, 0), saved.DepartedAt);
        var entry = Assert.Single(app.Services.GetRequiredService<AuditLog>().Recent(10), e => e.Action == AuditActions.PresenceEdit);
        Assert.Contains("09:30", entry.Details);
        Assert.Contains("10:00", entry.Details);
    }
}
