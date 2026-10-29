using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Pointage.Audit;
using Pointage.Data;
using Pointage.Members;
using Pointage.Presences;
using Pointage.Stats;
using Pointage.Web;

namespace Pointage.Pages;

public class HistoryModel(StatsService stats, PresenceStore presences, MemberStore members, TimeProvider time, AuditLog audit) : PageModel
{
    private static readonly string[] Presets = ["yesterday", "thisweek", "lastweek", "thismonth", "lastmonth", "custom"];

    [BindProperty(SupportsGet = true)] public string Preset { get; set; } = "lastweek";
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public string Sort { get; set; } = "days";

    public DateOnly Today { get; private set; }
    public Period Current { get; private set; }
    public PeriodStats Stats { get; private set; } = null!;
    public IReadOnlyList<MemberCount> PerMember { get; private set; } = [];
    public IReadOnlyList<PresenceRow> Rows { get; private set; } = [];

    // Période encodée dans les liens (tri, export) pour qu'ils restent sur la même période.
    public string PeriodQuery { get; private set; } = "";

    public void OnGet()
    {
        Resolve();
        Stats = stats.ForPeriod(Current);
        PerMember = Sort == "name" ? SortByLastName(Stats.PerMember) : Stats.PerMember;
        Rows = presences.ForPeriod(Current.From, Current.To);
    }

    public IActionResult OnGetCsv()
    {
        Resolve();
        // Le fichier contient les noms de tous les membres venus sur la période : sa sortie est tracée.
        audit.Write(User.AccountId(), AuditActions.HistoryExport, details: $"{Current.From:yyyy-MM-dd} → {Current.To:yyyy-MM-dd}");
        var bytes = Encoding.UTF8.GetBytes(StatsCsv.ForPeriod(stats.ForPeriod(Current)));
        return File(bytes, "text/csv; charset=utf-8", $"presences-{Current.From:yyyy-MM-dd}-{Current.To:yyyy-MM-dd}.csv");
    }

    // En attente un jour passé : plus personne ne la validera, on l'affiche « non validée ».
    public string StatusLabel(Presence p) =>
        p.Status == PresenceStatus.Pending && p.Day < Today ? "non validée" : p.Status.Label();

    private void Resolve()
    {
        Today = time.Today();
        if (!Presets.Contains(Preset)) Preset = "lastweek";
        if (Sort is not ("name" or "days")) Sort = "days";
        Current = Preset switch
        {
            "yesterday" => Periods.Yesterday(Today),
            "thisweek" => Periods.ThisWeek(Today),
            "thismonth" => Periods.ThisMonth(Today),
            "lastmonth" => Periods.LastMonth(Today),
            // Dates inversées : on les remet dans l'ordre plutôt que de renvoyer une période vide.
            "custom" when From is { } f && To is { } t => f <= t ? new Period(f, t) : new Period(t, f),
            _ => Periods.LastWeek(Today),
        };
        PeriodQuery = $"preset={Preset}&from={Current.From:yyyy-MM-dd}&to={Current.To:yyyy-MM-dd}";
    }

    // Le tri par défaut du service est par nombre de venues ; le tri par nom doit utiliser le nom de famille.
    private IReadOnlyList<MemberCount> SortByLastName(IReadOnlyList<MemberCount> list)
    {
        var lastNames = members.List().ToDictionary(m => m.Id, m => m.LastName ?? "");
        var cmp = StringComparer.InvariantCultureIgnoreCase;
        return list.OrderBy(m => lastNames.GetValueOrDefault(m.MemberId, ""), cmp).ThenBy(m => m.FullName, cmp).ToList();
    }
}
