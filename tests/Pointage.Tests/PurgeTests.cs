using System.Net;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Pointage.Accounts;
using Pointage.Audit;
using Pointage.Data;
using Pointage.Members;
using Pointage.Presences;
using Pointage.Settings;

namespace Pointage.Tests;

public class PurgeTests
{
    private static readonly ContactInput NoContact = new(null, null, null, null, null);
    private static readonly DateOnly Today = new(2026, 10, 27);

    private readonly Db _db;
    private readonly Microsoft.Extensions.Time.Testing.FakeTimeProvider _time;
    private readonly MemberStore _members;
    private readonly PresenceStore _presences;
    private readonly long _admin;

    public PurgeTests()
    {
        _db = TestDb.Create(out _time);
        _members = new MemberStore(_db, _time);
        _presences = new PresenceStore(_db, _time);
        _admin = new AccountStore(_db, _time).Create("marie", "motdepasse1", Role.Manager);
    }

    // Crée un membre « inscrit le jour donné » : l'horloge de test ne recule pas, on réécrit created_at.
    private Member CreateOn(DateOnly day, string firstName = "Lucas")
    {
        var m = _members.Create(new MemberInput("Dupont", firstName, new DateOnly(2015, 3, 12), null, null, null, NoContact));
        using var c = _db.Open();
        c.Execute("UPDATE members SET created_at=@at WHERE id=@id", new { id = m.Id, at = day.ToDateTime(new TimeOnly(9, 0)) });
        return m;
    }

    [Fact]
    public void MembersToPurge_LastVisitOlderThanRetention_IsSelected()
    {
        var old = CreateOn(Today, "Ancien");
        var recent = CreateOn(Today, "Recent");
        _presences.AddManual(old.Id, new DateTime(2025, 10, 26, 9, 0, 0), _admin);
        _presences.AddManual(recent.Id, new DateTime(2025, 10, 28, 9, 0, 0), _admin);
        Assert.Equal(old.Id, Assert.Single(_members.MembersToPurge(Today, 12)).Id);
    }

    [Fact]
    public void MembersToPurge_NeverCame_UsesCreatedAt()
    {
        var old = CreateOn(new DateOnly(2025, 10, 26), "Ancien");
        CreateOn(new DateOnly(2025, 10, 28), "Recent");
        Assert.Equal(old.Id, Assert.Single(_members.MembersToPurge(Today, 12)).Id);
    }

    [Fact]
    public void MembersToPurge_OnlyRefusedPresences_UsesCreatedAt()
    {
        var m = CreateOn(new DateOnly(2025, 10, 26));
        var id = _presences.AddManual(m.Id, new DateTime(2026, 10, 20, 9, 0, 0), _admin);
        _presences.Decide(id, PresenceStatus.Refused, _admin);
        Assert.Equal(m.Id, Assert.Single(_members.MembersToPurge(Today, 12)).Id);
    }

    [Fact]
    public void MembersToPurge_ExcludesAlreadyAnonymized()
    {
        var m = CreateOn(new DateOnly(2025, 1, 1));
        _members.Anonymize(m.Id);
        Assert.Empty(_members.MembersToPurge(Today, 12));
    }

    [Fact]
    public async Task Purge_Confirm_AnonymizesAndAudits()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.SuperAdmin);
        var members = app.Services.GetRequiredService<MemberStore>();
        var admin = app.Services.GetRequiredService<AccountStore>().List()[0].Id;
        var old = members.Create(new MemberInput("Dupont", "Lucas", new DateOnly(2015, 3, 12), null, null, null, NoContact));
        var recent = members.Create(new MemberInput("Martin", "Inès", new DateOnly(2012, 5, 1), null, null, null, NoContact));
        var presences = app.Services.GetRequiredService<PresenceStore>();
        presences.AddManual(old.Id, new DateTime(2020, 1, 1, 9, 0, 0), admin);
        presences.AddManual(recent.Id, DateTime.Now, admin);
        var audit = app.Services.GetRequiredService<AuditLog>();
        audit.Write(admin, AuditActions.Login);   // sera vieillie : hors conservation
        audit.Write(admin, AuditActions.Validate); // récente : doit rester
        using (var c = app.Services.GetRequiredService<Db>().Open())
            c.Execute("UPDATE audit SET at='2000-01-01 09:00:00' WHERE action=@a", new { a = AuditActions.Login });
        var backup = Path.Combine(Path.GetDirectoryName(app.Services.GetRequiredService<Db>().Path)!, "pointage-20200101-090000.bak");
        File.WriteAllText(backup, "ancienne copie");

        Assert.Contains("Anonymiser ces 1 membres", WebUtility.HtmlDecode(await client.GetStringAsync("/Admin/Purge")));
        (await AppFactory.PostFormAsync(client, "/Admin/Purge", "/Admin/Purge", [])).EnsureSuccessStatusCode();

        Assert.NotNull(members.Get(old.Id)!.AnonymizedAt);
        Assert.Null(members.Get(recent.Id)!.AnonymizedAt);
        Assert.Empty(members.MembersToPurge(DateOnly.FromDateTime(DateTime.Now), 12));
        var entry = Assert.Single(audit.Recent(50), e => e.Action == AuditActions.Purge);
        Assert.Contains("1", entry.Details);
        // Le journal suit la durée de conservation : l'ancienne entrée part, la récente et la purge restent.
        var actions = audit.Recent(50).Select(e => e.Action).ToList();
        Assert.DoesNotContain(AuditActions.Login, actions);
        Assert.Contains(AuditActions.Validate, actions);
        Assert.False(File.Exists(backup));
    }

    [Fact]
    public async Task Accounts_DeactivateLastSuperAdmin_ShowsMessage()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.SuperAdmin);
        var accounts = app.Services.GetRequiredService<AccountStore>();
        var admin = Assert.Single(accounts.List());

        var response = await AppFactory.PostFormAsync(client, "/Admin/Accounts", $"/Admin/Accounts?handler=Active&id={admin.Id}&active=false", []);

        Assert.Contains("Impossible : il doit rester au moins un super admin actif.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.True(accounts.Get(admin.Id)!.Active);
    }

    [Fact]
    public async Task Manager_AdminPages_Forbidden()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        foreach (var url in new[] { "/Admin/Settings", "/Admin/Purge", "/Admin/Accounts", "/Admin/Journal" })
        {
            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("/AccessDenied", response.RequestMessage!.RequestUri!.AbsolutePath);
            Assert.Contains("Accès réservé au super administrateur.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        }
    }

    [Fact]
    public void AuditLabel_KnownCodeIsFrench_UnknownFallsBackToCode()
    {
        Assert.Equal("Réglages", AuditActions.Label(AuditActions.Settings));
        Assert.Equal("Code.Inconnu", AuditActions.Label("Code.Inconnu"));
    }

    [Fact]
    public async Task Settings_Save_PersistsAndAudits()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.SuperAdmin);
        (await AppFactory.PostFormAsync(client, "/Admin/Settings", "/Admin/Settings", new()
        {
            ["OrganizationName"] = "Judo Club", ["RetentionMonths"] = "24", ["KioskKeyboard"] = "false", ["KioskDeparture"] = "true",
        })).EnsureSuccessStatusCode();

        Assert.Equal(new AppSettings("Judo Club", 24, false, true), app.Services.GetRequiredService<SettingsStore>().Get());
        Assert.Contains(app.Services.GetRequiredService<AuditLog>().Recent(10), e => e.Action == AuditActions.Settings);
        // « Réglages » figure déjà dans le menu : on vérifie un libellé absent de la mise en page, et que le code brut n'apparaît pas.
        app.Services.GetRequiredService<AuditLog>().Write(null, AuditActions.Purge);
        var journal = WebUtility.HtmlDecode(await client.GetStringAsync("/Admin/Journal"));
        Assert.Contains(">Purge RGPD<", journal);
        Assert.DoesNotContain(">Purge<", journal);
        Assert.DoesNotContain(">Settings<", journal);

        var bad = await AppFactory.PostFormAsync(client, "/Admin/Settings", "/Admin/Settings", new() { ["RetentionMonths"] = "0" });
        Assert.Contains("entre 1 et 120", WebUtility.HtmlDecode(await bad.Content.ReadAsStringAsync()));
        Assert.Equal(24, app.Services.GetRequiredService<SettingsStore>().Get().RetentionMonths);
    }
}
