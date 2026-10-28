using Dapper;
using Pointage.Data;
using Pointage.Members;

namespace Pointage.Stats;

public sealed record DayCount(DateOnly Day, int Members);

public sealed record MemberCount(long MemberId, string FullName, string Code, int Days);

public sealed record PeriodStats(Period Period, IReadOnlyList<DayCount> PerDay, int TotalPresences, int DistinctMembers,
    IReadOnlyList<MemberCount> PerMember);

public sealed record MemberStats(DateTime? LastVisit, int DaysThisMonth, int DaysThisSchoolYear,
    IReadOnlyDictionary<DayOfWeek, int> PerWeekday, TimeOnly? UsualArrival);

// Seules les présences validées comptent, en jours distincts : plusieurs passages le même jour = 1.
public sealed class StatsService(Db db, TimeProvider time)
{
    private sealed class MemberRow
    {
        public long Id { get; set; }
        public string Code { get; set; } = "";
        public string? LastName { get; set; }
        public string? FirstName { get; set; }
        public DateTime? AnonymizedAt { get; set; }
        public int Days { get; set; }
    }

    // Dapper ne passe pas par le constructeur positionnel d'un record (DateOnly) : lecture à plat puis assemblage.
    private sealed class CountRow
    {
        public DateOnly Day { get; set; }
        public int Members { get; set; }
    }

    private sealed class DayRow
    {
        public DateOnly Day { get; set; }
        public DateTime First { get; set; }
        public DateTime Last { get; set; }
    }

    public PeriodStats ForPeriod(Period p)
    {
        using var c = db.Open();
        var perDay = c.Query<CountRow>(
            """
            SELECT day AS Day, COUNT(DISTINCT member_id) AS Members FROM presences
            WHERE status='Validated' AND day BETWEEN @From AND @To GROUP BY day ORDER BY day
            """, new { p.From, p.To }).Select(r => new DayCount(r.Day, r.Members)).ToList();
        var rows = c.Query<MemberRow>(
            """
            SELECT m.id AS Id, m.code AS Code, m.last_name AS LastName, m.first_name AS FirstName, m.anonymized_at AS AnonymizedAt,
              COUNT(DISTINCT p.day) AS Days
            FROM presences p JOIN members m ON m.id=p.member_id
            WHERE p.status='Validated' AND p.day BETWEEN @From AND @To GROUP BY m.id
            """, new { p.From, p.To });

        var cmp = StringComparer.InvariantCultureIgnoreCase;
        var perMember = rows
            .Select(r => (r, Name: new Member(r.Id, r.Code, r.LastName, r.FirstName, null, null, null, null,
                new ContactInput(null, null, null, null, null), default, r.AnonymizedAt).FullName)) // même libellé que Member.FullName
            .OrderByDescending(x => x.r.Days).ThenBy(x => x.r.LastName ?? x.Name, cmp).ThenBy(x => x.r.FirstName ?? "", cmp)
            .Select(x => new MemberCount(x.r.Id, x.Name, x.r.Code, x.r.Days)).ToList();
        return new(p, perDay, perDay.Sum(d => d.Members), perMember.Count, perMember);
    }

    public MemberStats ForMember(long memberId)
    {
        using var c = db.Open();
        var days = c.Query<DayRow>(
            """
            SELECT day AS Day, MIN(arrived_at) AS First, MAX(arrived_at) AS Last FROM presences
            WHERE member_id=@memberId AND status='Validated' GROUP BY day
            """, new { memberId }).ToList();

        var today = time.Today();
        var monthStart = Periods.ThisMonth(today).From;
        var yearStart = Periods.SchoolYearStart(today);
        var perWeekday = Enum.GetValues<DayOfWeek>().ToDictionary(d => d, d => days.Count(x => x.Day.DayOfWeek == d));

        // Médiane des premières arrivées ; pour un nombre pair, l'élément inférieur.
        var firsts = days.Select(x => TimeOnly.FromDateTime(x.First)).Order().ToList();
        return new(
            days.Count == 0 ? null : days.Max(x => x.Last),
            days.Count(x => x.Day >= monthStart && x.Day <= today),
            days.Count(x => x.Day >= yearStart && x.Day <= today),
            perWeekday,
            firsts.Count == 0 ? null : firsts[(firsts.Count - 1) / 2]);
    }
}
