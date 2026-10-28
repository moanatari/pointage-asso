using System.Net;
using System.Text;
using Pointage.Accounts;
using Pointage.Stats;

namespace Pointage.Tests;

public class HistoryTests
{
    // L'horloge de l'application est la vraie horloge en test : on calcule la semaine attendue à partir de maintenant.
    private static Period LastWeekFromNow() => Periods.LastWeek(DateOnly.FromDateTime(DateTime.Now));

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
    }

    [Fact]
    public async Task History_CustomReversedDates_AreSwapped()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/History?preset=custom&from=2026-10-25&to=2026-10-19"));

        Assert.Contains("Historique du 19/10/2026 au 25/10/2026", html);
    }
}
