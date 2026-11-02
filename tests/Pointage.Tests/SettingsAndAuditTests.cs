using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Pointage.Accounts;
using Pointage.Audit;
using Pointage.Data;
using Pointage.Members;
using Pointage.Presences;
using Pointage.Settings;

namespace Pointage.Tests;

public class SettingsAndAuditTests
{
    [Fact]
    public void Settings_EmptyDb_ReturnsDefaults()
    {
        var s = new SettingsStore(TestDb.Create(out _)).Get();
        Assert.Equal(12, s.RetentionMonths);
        Assert.True(s.KioskKeyboard);
        Assert.True(s.KioskDeparture);
        Assert.False(s.DepartureValidation);
    }

    [Fact]
    public void Settings_SaveThenGet_RoundTrips()
    {
        var store = new SettingsStore(TestDb.Create(out _));
        var saved = new AppSettings("Asso Soleil", 24, false, true, true, 5080);
        store.Save(saved);
        store.Save(saved with { RetentionMonths = 6 });
        Assert.Equal(saved with { RetentionMonths = 6 }, store.Get());
    }

    [Fact]
    public void Save_ThenGet_RoundTripsDepartureValidation()
    {
        var store = new SettingsStore(TestDb.Create(out _));
        store.Save(AppSettings.Default with { DepartureValidation = true });
        Assert.True(store.Get().DepartureValidation);
        store.Save(AppSettings.Default with { DepartureValidation = false });
        Assert.False(store.Get().DepartureValidation);
    }

    [Fact]
    public void Get_StoredDepartureZero_KeepsFalse()
    {
        var db = TestDb.Create(out _);
        using (var c = db.Open())
            c.Execute("INSERT INTO settings(key, value) VALUES('kiosk_departure', '0')");
        Assert.False(new SettingsStore(db).Get().KioskDeparture);
    }

    [Fact]
    public async Task Settings_DisablingDepartureValidation_ValidatesPending()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.SuperAdmin);
        var settings = app.Services.GetRequiredService<SettingsStore>();
        settings.Save(AppSettings.Default with { DepartureValidation = true });
        var lucas = app.Services.GetRequiredService<MemberStore>()
            .Create(new MemberInput("Dupont", "Lucas", new DateOnly(2015, 3, 12), null, null, null, new ContactInput(null, null, null, null, null)));
        var presences = app.Services.GetRequiredService<PresenceStore>();
        presences.CheckIn(lucas.Id);
        Assert.True(presences.Depart(lucas.Id, true));
        Assert.Single(presences.PendingDepartures());

        (await AppFactory.PostFormAsync(client, "/Admin/Settings", "/Admin/Settings", new()
        {
            ["OrganizationName"] = "Asso", ["RetentionMonths"] = "12", ["KioskKeyboard"] = "true", ["KioskDeparture"] = "true", ["DepartureValidation"] = "false",
        })).EnsureSuccessStatusCode();

        Assert.False(settings.Get().DepartureValidation);
        Assert.Empty(presences.PendingDepartures());
    }

    [Fact]
    public void AuditActions_Label_NewDepartureActions()
    {
        Assert.Equal("Validation de départ", AuditActions.Label(AuditActions.DepartureValidate));
        Assert.Equal("Refus de départ", AuditActions.Label(AuditActions.DepartureRefuse));
    }

    [Fact]
    public void Audit_DeleteOlderThan_RemovesOnlyOldEntries()
    {
        var log = new AuditLog(TestDb.Create(out var time), time);
        log.Write(1, AuditActions.Login);
        time.Advance(TimeSpan.FromDays(40));
        log.Write(null, AuditActions.MemberCreate, 7, "détail");

        Assert.Equal(1, log.DeleteOlderThan(time.Now().AddDays(-30)));

        var left = Assert.Single(log.Recent(10));
        Assert.Equal(AuditActions.MemberCreate, left.Action);
        Assert.Equal(7, left.MemberId);
        Assert.Equal("détail", left.Details);
        Assert.Equal(time.Now(), left.At);
    }
}
