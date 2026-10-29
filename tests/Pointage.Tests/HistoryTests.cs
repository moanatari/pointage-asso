using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Pointage.Accounts;
using Pointage.Members;
using Pointage.Presences;
using Pointage.Stats;

namespace Pointage.Tests;

public class HistoryTests
{
    private static readonly ContactInput NoContact = new(null, null, null, null, null);

    // L'horloge de l'application est la vraie horloge en test : on calcule la semaine attendue à partir de maintenant.
    private static Period LastWeekFromNow() => Periods.LastWeek(DateOnly.FromDateTime(DateTime.Now));

    private static Member CreateMember(AppFactory app, string last, string first) =>
        app.Services.GetRequiredService<MemberStore>()
            .Create(new MemberInput(last, first, new DateOnly(2015, 3, 12), null, null, null, NoContact));

    // Présences validées la semaine dernière : Martin vient deux jours, Bernard un seul.
    private static void SeedLastWeek(AppFactory app, Member martin, Member bernard)
    {
        var presences = app.Services.GetRequiredService<PresenceStore>();
        var accountId = app.Services.GetRequiredService<AccountStore>()
            .Create("marie-" + Guid.NewGuid().ToString("N")[..8], AppFactory.Password, Role.Manager);
        var monday = LastWeekFromNow().From;
        presences.AddManual(martin.Id, monday.ToDateTime(new TimeOnly(9, 0)), accountId);
        presences.AddManual(martin.Id, monday.AddDays(1).ToDateTime(new TimeOnly(9, 0)), accountId);
        presences.AddManual(bernard.Id, monday.ToDateTime(new TimeOnly(10, 0)), accountId);
    }

    [Fact]
    public async Task History_DefaultIsLastWeek()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var week = LastWeekFromNow();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/History"));

        Assert.Contains($"Historique du {week.From:dd/MM/yyyy} au {week.To:dd/MM/yyyy}", html);
    }

    [Fact]
    public async Task History_Csv_HasBomAndHeader()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var week = LastWeekFromNow();

        var response = await client.GetAsync("/History?handler=Csv&preset=lastweek");

        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.StartsWith("Nom;Code;Jours de présence", Encoding.UTF8.GetString(bytes)[1..]);
        Assert.Equal($"presences-{week.From:yyyy-MM-dd}-{week.To:yyyy-MM-dd}.csv", response.Content.Headers.ContentDisposition?.FileName);
        Assert.Contains(app.Services.GetRequiredService<Pointage.Audit.AuditLog>().Recent(10), e => e.Action == Pointage.Audit.AuditActions.HistoryExport);
    }

    [Fact]
    public async Task History_CustomReversedDates_AreSwapped()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/History?preset=custom&from=2026-10-25&to=2026-10-19"));

        Assert.Contains("Historique du 19/10/2026 au 25/10/2026", html);
    }

    [Fact]
    public async Task History_SortByName_OrdersByLastName()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        // Martin a deux venues et Bernard une : le tri par venues place Martin en tête, le tri par nom Bernard.
        SeedLastWeek(app, CreateMember(app, "Martin", "Lucas"), CreateMember(app, "Bernard", "Anne"));

        var byDays = WebUtility.HtmlDecode(await client.GetStringAsync("/History?preset=lastweek&sort=days"));
        var byName = WebUtility.HtmlDecode(await client.GetStringAsync("/History?preset=lastweek&sort=name"));

        // Un nom absent donnerait -1 : on vérifie la présence avant de comparer l'ordre.
        var martinByDays = byDays.IndexOf("Lucas Martin", StringComparison.Ordinal);
        var bernardByDays = byDays.IndexOf("Anne Bernard", StringComparison.Ordinal);
        Assert.True(martinByDays >= 0 && bernardByDays >= 0);
        Assert.True(martinByDays < bernardByDays);

        var martinByName = byName.IndexOf("Lucas Martin", StringComparison.Ordinal);
        var bernardByName = byName.IndexOf("Anne Bernard", StringComparison.Ordinal);
        Assert.True(martinByName >= 0 && bernardByName >= 0);
        Assert.True(bernardByName < martinByName);
    }

    [Fact]
    public async Task History_InvalidPresetAndSort_FallBackToLastWeek()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var week = LastWeekFromNow();

        var response = await client.GetAsync("/History?preset=zzz&sort=zzz");

        response.EnsureSuccessStatusCode();
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains($"Historique du {week.From:dd/MM/yyyy} au {week.To:dd/MM/yyyy}", html);
    }

    [Fact]
    public async Task History_CustomWithoutDates_FallsBackToLastWeek()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var week = LastWeekFromNow();

        var response = await client.GetAsync("/History?preset=custom");

        response.EnsureSuccessStatusCode();
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains($"Historique du {week.From:dd/MM/yyyy} au {week.To:dd/MM/yyyy}", html);
    }

    [Fact]
    public async Task History_WithData_ShowsCountsAndCorrectLink()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var martin = CreateMember(app, "Martin", "Lucas");
        SeedLastWeek(app, martin, CreateMember(app, "Bernard", "Anne"));
        var id = app.Services.GetRequiredService<PresenceStore>().ForMember(martin.Id, 1).Single().Presence.Id;
        var monday = LastWeekFromNow().From.ToString("dd/MM/yyyy");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/History?preset=lastweek"));

        Assert.Contains("3 présences · 2 membres différents", html);
        // Ligne de détail complète : date, membre, arrivée 09:00, pas de départ, statut validé (saisie manuelle).
        Assert.Matches($"<td>{Regex.Escape(monday)}</td>\\s*<td>Lucas Martin</td>\\s*<td>09:00</td>\\s*<td>—</td>\\s*<td>validé</td>", html);
        Assert.Contains($"href=\"/Presences/Edit/{id}\"", html);
    }
}
