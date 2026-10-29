using Dapper;
using Pointage.Accounts;
using Pointage.Data;
using Pointage.Members;
using Pointage.Presences;
using Pointage.Stats;

namespace Pointage.Tests;

public class StatsTests
{
    private static readonly ContactInput NoContact = new(null, null, null, null, null);
    private static readonly DateOnly Today = new(2026, 10, 27); // mardi
    private static readonly Period October = new(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

    private readonly Db _db;
    private readonly Microsoft.Extensions.Time.Testing.FakeTimeProvider _time;
    private readonly PresenceStore _presences;
    private readonly MemberStore _members;
    private readonly StatsService _stats;
    private readonly long _admin;

    public StatsTests()
    {
        _db = TestDb.Create(out _time);
        _presences = new PresenceStore(_db, _time);
        _members = new MemberStore(_db, _time);
        _stats = new StatsService(_db, _time);
        _admin = new AccountStore(_db, _time).Create("marie", "motdepasse1", Role.Manager);
    }

    private Member NewMember(string last, string first) =>
        _members.Create(new MemberInput(last, first, new DateOnly(2015, 3, 12), null, null, null, NoContact));

    private static DateTime At(int y, int mo, int d, int h = 17, int mi = 0) => new(y, mo, d, h, mi, 0);

    [Fact] public void Periods_LastWeek_FromTuesday20261027_Is19To25() =>
        Assert.Equal(new Period(new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 25)), Periods.LastWeek(Today));

    [Fact] public void Periods_ThisWeek_FromTuesday20261027_Is26To27() =>
        Assert.Equal(new Period(new DateOnly(2026, 10, 26), Today), Periods.ThisWeek(Today));

    [Fact] public void Periods_ThisWeek_OnSunday_StartsPreviousMonday() =>
        Assert.Equal(new DateOnly(2026, 10, 19), Periods.ThisWeek(new DateOnly(2026, 10, 25)).From);

    [Fact] public void Periods_Yesterday_IsPreviousDay() =>
        Assert.Equal(new Period(new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 26)), Periods.Yesterday(Today));

    [Fact] public void Periods_LastMonth_FromOctober_IsSeptember() =>
        Assert.Equal(new Period(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), Periods.LastMonth(Today));

    [Fact] public void Periods_LastMonth_FromJanuary_IsPreviousDecember() =>
        Assert.Equal(new Period(new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 31)), Periods.LastMonth(new DateOnly(2026, 1, 15)));

    [Fact] public void Periods_ThisMonth_StartsOnFirst() =>
        Assert.Equal(new Period(new DateOnly(2026, 10, 1), Today), Periods.ThisMonth(Today));

    [Fact] public void SchoolYearStart_InOctober2026_Is20260901() =>
        Assert.Equal(new DateOnly(2026, 9, 1), Periods.SchoolYearStart(Today));

    [Fact] public void SchoolYearStart_InMarch2027_Is20260901() =>
        Assert.Equal(new DateOnly(2026, 9, 1), Periods.SchoolYearStart(new DateOnly(2027, 3, 10)));

    [Fact]
    public void ForPeriod_CountsOnlyValidated()
    {
        var a = NewMember("Dupont", "Lucas");
        var b = NewMember("Martin", "Alice");
        var c = NewMember("Durand", "Zoé");
        _presences.CheckIn(a.Id); // Pending
        _presences.CheckIn(b.Id);
        _presences.Decide(_presences.PendingToday().First(r => r.Presence.MemberId == b.Id).Presence.Id, PresenceStatus.Refused, _admin);
        _presences.AddManual(c.Id, At(2026, 10, 27), _admin); // Validated

        var s = _stats.ForPeriod(October);
        Assert.Equal(1, s.TotalPresences);
        Assert.Equal(1, s.DistinctMembers);
        Assert.Equal(new DayCount(Today, 1), Assert.Single(s.PerDay));
    }

    [Fact]
    public void ForPeriod_TwoPassagesSameDay_CountOnce()
    {
        var a = NewMember("Dupont", "Lucas");
        using (var c = _db.Open())
            foreach (var h in new[] { "17:00:00", "18:30:00" })
                c.Execute("INSERT INTO presences(member_id, day, arrived_at, status, source) VALUES(@id, '2026-10-27', @at, 'Validated', 'Manual')",
                    new { id = a.Id, at = "2026-10-27 " + h });

        var s = _stats.ForPeriod(October);
        Assert.Equal(1, Assert.Single(s.PerMember).Days);
        Assert.Equal(1, s.TotalPresences);
    }

    [Fact]
    public void ForPeriod_PerMemberOrderedByDaysThenName()
    {
        var martin = NewMember("Martin", "Alice");
        var dupont = NewMember("Dupont", "Lucas");
        var zed = NewMember("Zed", "Eva");
        _presences.AddManual(martin.Id, At(2026, 10, 5), _admin);
        _presences.AddManual(dupont.Id, At(2026, 10, 5), _admin);
        _presences.AddManual(zed.Id, At(2026, 10, 5), _admin);
        _presences.AddManual(zed.Id, At(2026, 10, 6), _admin);
        _presences.AddManual(martin.Id, At(2026, 11, 3), _admin); // hors période

        var s = _stats.ForPeriod(October);
        Assert.Equal(new[] { "Eva Zed", "Lucas Dupont", "Alice Martin" }, s.PerMember.Select(m => m.FullName));
        Assert.Equal(new[] { 2, 1, 1 }, s.PerMember.Select(m => m.Days));
        Assert.Equal(3, s.DistinctMembers);
        Assert.Equal(3, s.PerDay[0].Members);
        Assert.Equal(4, s.TotalPresences);
    }

    [Fact]
    public void ForPeriod_AnonymizedMember_UsesAnonymousName()
    {
        var a = NewMember("Dupont", "Lucas");
        _presences.AddManual(a.Id, At(2026, 10, 5), _admin);
        _members.Anonymize(a.Id);
        Assert.Equal($"Membre anonymisé #{a.Id}", Assert.Single(_stats.ForPeriod(October).PerMember).FullName);
    }

    [Fact]
    public void ForMember_PerWeekdayAndUsualArrival()
    {
        var a = NewMember("Dupont", "Lucas");
        _presences.AddManual(a.Id, At(2026, 10, 19, 16, 50), _admin); // lundi
        _presences.AddManual(a.Id, At(2026, 10, 26, 17, 0), _admin);  // lundi
        _presences.AddManual(a.Id, At(2026, 10, 27, 17, 10), _admin); // mardi

        var s = _stats.ForMember(a.Id);
        Assert.Equal(2, s.PerWeekday[DayOfWeek.Monday]);
        Assert.Equal(1, s.PerWeekday[DayOfWeek.Tuesday]);
        Assert.Equal(new TimeOnly(17, 0), s.UsualArrival);
        Assert.Equal(At(2026, 10, 27, 17, 10), s.LastVisit);
        Assert.Equal(3, s.DaysThisMonth);
        Assert.Equal(3, s.DaysThisSchoolYear);
    }

    [Fact]
    public void ForMember_EvenCount_TakesLowerMedian()
    {
        var a = NewMember("Dupont", "Lucas");
        _presences.AddManual(a.Id, At(2026, 10, 19, 16, 0), _admin);
        _presences.AddManual(a.Id, At(2026, 10, 20, 18, 0), _admin);
        Assert.Equal(new TimeOnly(16, 0), _stats.ForMember(a.Id).UsualArrival);
    }

    [Fact]
    public void ForMember_SchoolYearIncludesPreviousMonthsButMonthDoesNot()
    {
        var a = NewMember("Dupont", "Lucas");
        _presences.AddManual(a.Id, At(2026, 9, 15), _admin);
        _presences.AddManual(a.Id, At(2026, 10, 15), _admin);
        _presences.AddManual(a.Id, At(2026, 6, 15), _admin); // année scolaire précédente
        var s = _stats.ForMember(a.Id);
        Assert.Equal(1, s.DaysThisMonth);
        Assert.Equal(2, s.DaysThisSchoolYear);
    }

    [Fact]
    public void ForMember_NoPresence_LastVisitNull()
    {
        var s = _stats.ForMember(NewMember("Dupont", "Lucas").Id);
        Assert.Null(s.LastVisit);
        Assert.Null(s.UsualArrival);
        Assert.Equal(0, s.DaysThisMonth);
    }

    [Fact]
    public void Csv_StartsWithBomAndEscapesSemicolon()
    {
        var s = new PeriodStats(October, [], 1, 1, [new MemberCount(1, "Dupont; Jr", "ABC234", 2), new MemberCount(2, "Le \"Grand\"", "DEF567", 1)]);
        var csv = StatsCsv.ForPeriod(s);
        Assert.StartsWith("﻿Nom;Code;Jours de présence\r\n", csv);
        Assert.Contains("\"Dupont; Jr\";ABC234;2\r\n", csv);
        Assert.Contains("\"Le \"\"Grand\"\"\";DEF567;1\r\n", csv);
    }
}
