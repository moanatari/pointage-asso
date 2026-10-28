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
        Assert.Empty(presences.ValidatedToday());
        var entry = Assert.Single(app.Services.GetRequiredService<AuditLog>().Recent(10), e => e.Action == AuditActions.Refuse);
        Assert.Equal(lucas.Id, entry.MemberId);
    }

    [Fact]
    public async Task Home_ValidateAll_ValidatesEveryPending()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var presences = app.Services.GetRequiredService<PresenceStore>();
        presences.CheckIn(CreateLucas(app).Id);
        (await AppFactory.PostFormAsync(client, "/", "/?handler=ValidateAll", [])).EnsureSuccessStatusCode();
        Assert.Empty(presences.PendingToday());
        Assert.Single(presences.ValidatedToday());
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
    public async Task AddPresence_Twice_ShowsErrorSecondTime()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var lucas = CreateLucas(app);
        var fields = new Dictionary<string, string> { ["MemberId"] = lucas.Id.ToString(), ["Day"] = "2026-10-20", ["Time"] = "09:30" };
        var url = $"/Presences/Add?memberId={lucas.Id}";
        (await AppFactory.PostFormAsync(client, url, "/Presences/Add", fields)).EnsureSuccessStatusCode();
        var second = await AppFactory.PostFormAsync(client, url, "/Presences/Add", fields);
        Assert.Contains("Ce membre a déjà une présence ce jour-là.", await Text(second));
        Assert.Single(app.Services.GetRequiredService<PresenceStore>().ForMember(lucas.Id, 10));
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
