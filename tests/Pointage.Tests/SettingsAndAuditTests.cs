using System.Net;
using System.Net.Sockets;
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

    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    static async Task<string> PostPortAsync(HttpClient client, string port) =>
        WebUtility.HtmlDecode(await (await AppFactory.PostFormAsync(client, "/Admin/Settings", "/Admin/Settings?handler=Port",
            new() { ["Port"] = port })).Content.ReadAsStringAsync());

    [Fact]
    public async Task PortForm_Valid_SavesAuditsAndAsksManualRestart()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.SuperAdmin);
        var port = FreePort();

        var html = await PostPortAsync(client, port.ToString());

        Assert.Contains($"Port enregistré. Redémarrez l'application pour l'appliquer, puis ouvrez http://localhost:{port}/", html);
        Assert.Equal(port, app.Services.GetRequiredService<SettingsStore>().Get().Port);
        Assert.Contains(app.Services.GetRequiredService<AuditLog>().Recent(5), e => e.Details == $"Port 5080 → {port}");
    }

    [Theory]
    [InlineData("80")]
    [InlineData("70000")]
    [InlineData("")]
    public async Task PortForm_OutOfRange_ShowsErrorAndKeepsPort(string value)
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.SuperAdmin);

        var html = await PostPortAsync(client, value);

        Assert.Contains("Le port doit être un nombre entre 1024 et 65535.", html);
        Assert.Equal(5080, app.Services.GetRequiredService<SettingsStore>().Get().Port);
    }

    [Fact]
    public async Task PortForm_Occupied_ShowsError()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.SuperAdmin);
        var busy = new TcpListener(IPAddress.Any, 0);
        busy.Start();
        try
        {
            var port = ((IPEndPoint)busy.LocalEndpoint).Port;
            var html = await PostPortAsync(client, port.ToString());
            Assert.Contains($"Le port {port} est déjà utilisé sur ce poste.", html);
            Assert.Equal(5080, app.Services.GetRequiredService<SettingsStore>().Get().Port);
        }
        finally { busy.Stop(); }
    }

    [Fact]
    public async Task PortForm_Same_NoChange()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.SuperAdmin);

        var html = await PostPortAsync(client, "5080");

        Assert.Contains("Le port est déjà 5080.", html);
        Assert.DoesNotContain(app.Services.GetRequiredService<AuditLog>().Recent(5), e => e.Action == AuditActions.Settings);
    }

    [Fact]
    public async Task PortForm_Manager_Forbidden()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var token = AppFactory.AntiforgeryToken(await client.GetStringAsync("/"));

        var response = await client.PostAsync("/Admin/Settings?handler=Port", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Port"] = "8081", ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(5080, app.Services.GetRequiredService<SettingsStore>().Get().Port);
    }

    [Fact]
    public async Task LocalOnly_LocalhostOnOtherPort_Allowed()
    {
        using var app = new AppFactory();
        var client = app.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/Login");
        request.Headers.Host = "localhost:8081";

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
