using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Pointage.Accounts;
using Pointage.Audit;
using Pointage.Kiosk;
using Pointage.Members;
using Pointage.Presences;

namespace Pointage.Tests;

public class KioskTests
{
    private const string LanIp = "192.168.1.30";

    private static KioskPairing NewPairing(out Microsoft.Extensions.Time.Testing.FakeTimeProvider time) => new(TestDb.Create(out time), time);

    private static long CreateLucas(AppFactory app) =>
        app.Services.GetRequiredService<MemberStore>()
            .Create(new MemberInput("Dupont", "Lucas", new DateOnly(2015, 3, 12), "0600000000", "a@b.fr", "allergie",
                new ContactInput("Dupont", "Anne", null, "0611111111", null))).Id;

    // Client d'une borne liée par le vrai parcours : code émis côté responsable, puis POST /api/borne/pair.
    private static async Task<HttpClient> PairedClientAsync(AppFactory app)
    {
        var code = app.Services.GetRequiredService<KioskPairing>().NewPairingCode("Entrée");
        var client = app.CreateClient();
        (await client.PostAsJsonAsync("/api/borne/pair", new { code })).EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public void Pair_ValidCode_ReturnsTokenOnce()
    {
        var pairing = NewPairing(out _);
        var code = pairing.NewPairingCode("Entrée");
        Assert.Matches("^[0-9]{6}$", code);
        var token = pairing.Pair(code);
        Assert.NotNull(token);
        Assert.True(pairing.IsValid(token));
        Assert.Null(pairing.Pair(code));
        Assert.Equal("Entrée", Assert.Single(pairing.List()).Name);
    }

    [Fact]
    public void Pair_AfterTenMinutes_ReturnsNull()
    {
        var pairing = NewPairing(out var time);
        var code = pairing.NewPairingCode("Entrée");
        time.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
        Assert.Null(pairing.Pair(code));
    }

    [Fact]
    public void Pair_FiveWrongAttempts_InvalidatesCode()
    {
        var pairing = NewPairing(out _);
        var code = pairing.NewPairingCode("Entrée");
        var wrong = code == "000000" ? "111111" : "000000";
        for (var i = 0; i < 5; i++) Assert.Null(pairing.Pair(wrong));
        Assert.Null(pairing.Pair(code));
    }

    [Fact]
    public void Revoke_MakesTokenInvalid()
    {
        var pairing = NewPairing(out _);
        var token = pairing.Pair(pairing.NewPairingCode("Entrée"));
        pairing.Revoke(Assert.Single(pairing.List()).Id);
        Assert.False(pairing.IsValid(token));
        Assert.NotNull(Assert.Single(pairing.List()).RevokedAt);
    }

    [Fact]
    public async Task Search_WithoutToken_Returns401()
    {
        using var app = new AppFactory(LanIp);
        var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/borne/search?q=lucas")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/borne/checkin", new { memberId = 1 })).StatusCode);
        client.DefaultRequestHeaders.Add("Cookie", "pointage_kiosk=faux-jeton");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/borne/config")).StatusCode);
    }

    [Fact]
    public async Task Pair_WrongCode_Returns400()
    {
        using var app = new AppFactory(LanIp);
        app.Services.GetRequiredService<KioskPairing>().NewPairingCode("Entrée");
        var response = await app.CreateClient().PostAsJsonAsync("/api/borne/pair", new { code = "abc" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Pair_SetsLongLivedStrictHttpOnlyCookie()
    {
        using var app = new AppFactory(LanIp);
        var code = app.Services.GetRequiredService<KioskPairing>().NewPairingCode("Entrée");
        var response = await app.CreateClient().PostAsJsonAsync("/api/borne/pair", new { code });
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("pointage_kiosk=", cookie);
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=strict", cookie);
        var expires = DateTime.Parse(Regex.Match(cookie, "expires=([^;]+)").Groups[1].Value);
        Assert.True(expires > DateTime.UtcNow.AddYears(9));
    }

    [Fact]
    public async Task Search_ReturnsOnlyMinimalFields()
    {
        using var app = new AppFactory(LanIp);
        var id = CreateLucas(app);
        var client = await PairedClientAsync(app);

        Assert.Equal("[]", await client.GetStringAsync("/api/borne/search?q=l"));
        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/borne/search?q=luc"));
        var m = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal(["id", "label", "code", "hasPresenceToday"], m.EnumerateObject().Select(p => p.Name));
        Assert.Equal(id, m.GetProperty("id").GetInt64());
        Assert.Equal("Lucas D.", m.GetProperty("label").GetString());
        Assert.False(m.GetProperty("hasPresenceToday").GetBoolean());
    }

    [Fact]
    public async Task Checkin_TwiceViaApi_ReturnsAlready()
    {
        using var app = new AppFactory(LanIp);
        var id = CreateLucas(app);
        var client = await PairedClientAsync(app);

        async Task<string?> CheckIn() =>
            (await (await client.PostAsJsonAsync("/api/borne/checkin", new { memberId = id })).Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("result").GetString();

        Assert.Equal("created", await CheckIn());
        Assert.Equal("already", await CheckIn());
        Assert.Equal(PresenceStatus.Pending, Assert.Single(app.Services.GetRequiredService<PresenceStore>().PendingToday()).Presence.Status);
        Assert.Contains("\"hasPresenceToday\":true", await client.GetStringAsync("/api/borne/search?q=lucas"));
    }

    [Fact]
    public async Task Checkin_UnknownOrAnonymizedMember_Returns404()
    {
        using var app = new AppFactory(LanIp);
        var id = CreateLucas(app);
        app.Services.GetRequiredService<MemberStore>().Anonymize(id);
        var client = await PairedClientAsync(app);

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/borne/checkin", new { memberId = id })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/borne/checkin", new { memberId = 999 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/borne/depart", new { memberId = 999 })).StatusCode);
        Assert.Empty(app.Services.GetRequiredService<PresenceStore>().PendingToday());
    }

    [Fact]
    public async Task Depart_And_Config_ViaApi()
    {
        using var app = new AppFactory(LanIp);
        var id = CreateLucas(app);
        var client = await PairedClientAsync(app);

        Assert.Equal("""{"keyboard":true,"departure":false}""", await client.GetStringAsync("/api/borne/config"));
        Assert.Equal("""{"ok":false}""", await (await client.PostAsJsonAsync("/api/borne/depart", new { memberId = id })).Content.ReadAsStringAsync());
        await client.PostAsJsonAsync("/api/borne/checkin", new { memberId = id });
        Assert.Equal("""{"ok":true}""", await (await client.PostAsJsonAsync("/api/borne/depart", new { memberId = id })).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RevokedKiosk_Returns401()
    {
        using var app = new AppFactory(LanIp);
        var client = await PairedClientAsync(app);
        var pairing = app.Services.GetRequiredService<KioskPairing>();
        pairing.Revoke(Assert.Single(pairing.List()).Id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/borne/config")).StatusCode);
    }

    [Fact]
    public async Task BornePage_FromRemoteIp_IsServed()
    {
        using var app = new AppFactory(LanIp);
        var response = await app.CreateClient().GetAsync("/borne");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Entrez le code de liaison", await response.Content.ReadAsStringAsync());

        var paired = await (await PairedClientAsync(app)).GetStringAsync("/borne");
        Assert.DoesNotContain("Entrez le code de liaison", paired);
        Assert.Contains("/static/kiosk.js", paired);
    }

    [Fact]
    public async Task SuperAdmin_GeneratesCode_AndIsAudited()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.SuperAdmin);
        var form = await client.GetStringAsync("/Admin/Kiosks");
        var response = await client.PostAsync("/Admin/Kiosks?handler=Pair", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["KioskName"] = "Entrée",
            ["__RequestVerificationToken"] = AppFactory.AntiforgeryToken(form),
        }));
        var code = Regex.Match(await response.Content.ReadAsStringAsync(), "class=\"pairing-code\">([0-9]{6})<").Groups[1].Value;
        Assert.NotNull(app.Services.GetRequiredService<KioskPairing>().Pair(code));
        Assert.Contains(app.Services.GetRequiredService<AuditLog>().Recent(10), e => e.Action == AuditActions.KioskPair);
    }

    // Première vraie page d'administration : un responsable ne doit ni la voir ni émettre de code.
    [Fact]
    public async Task Manager_CannotUseKioskAdmin()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        Assert.False((await client.GetAsync("/Admin/Kiosks")).IsSuccessStatusCode);

        var home = await client.GetStringAsync("/"); // jeton antiforgery valide, pris ailleurs
        var response = await client.PostAsync("/Admin/Kiosks?handler=Pair", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["KioskName"] = "Entrée",
            ["__RequestVerificationToken"] = AppFactory.AntiforgeryToken(home),
        }));
        Assert.False(response.IsSuccessStatusCode);
        Assert.DoesNotContain(app.Services.GetRequiredService<AuditLog>().Recent(10), e => e.Action == AuditActions.KioskPair);
    }
}
