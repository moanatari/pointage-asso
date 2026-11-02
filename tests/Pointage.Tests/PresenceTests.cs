using Pointage.Accounts;
using Pointage.Data;
using Pointage.Members;
using Pointage.Presences;

namespace Pointage.Tests;

public class PresenceTests
{
    private static readonly ContactInput NoContact = new(null, null, null, null, null);

    private readonly Db _db;
    private readonly Microsoft.Extensions.Time.Testing.FakeTimeProvider _time;
    private readonly PresenceStore _store;
    private readonly long _lucas;
    private readonly long _admin;

    public PresenceTests()
    {
        _db = TestDb.Create(out _time);
        _store = new PresenceStore(_db, _time);
        _lucas = new MemberStore(_db, _time).Create(new MemberInput("Dupont", "Lucas", new DateOnly(2015, 3, 12), null, null, null, NoContact)).Id;
        _admin = new AccountStore(_db, _time).Create("marie", "motdepasse1", Role.Manager);
    }

    [Fact]
    public void CheckIn_First_CreatesPending()
    {
        Assert.Equal(CheckInResult.Created, _store.CheckIn(_lucas));
        var p = Assert.Single(_store.PendingToday()).Presence;
        Assert.Equal(PresenceStatus.Pending, p.Status);
        Assert.Equal(PresenceSource.Kiosk, p.Source);
        Assert.Equal(new DateOnly(2026, 10, 27), p.Day);
        Assert.True(_store.IsPresent(_lucas));
    }

    [Fact]
    public void CheckIn_WhilePresent_ReturnsAlreadyPresent()
    {
        _store.CheckIn(_lucas);
        Assert.Equal(CheckInResult.AlreadyPresent, _store.CheckIn(_lucas));
        Assert.Single(_store.ForMember(_lucas, 10));
    }

    [Fact]
    public void CheckIn_AfterRefused_CreatesNew()
    {
        _store.CheckIn(_lucas);
        _store.Decide(_store.PendingToday().Single().Presence.Id, PresenceStatus.Refused, _admin);
        Assert.False(_store.IsPresent(_lucas));
        Assert.Equal(CheckInResult.Created, _store.CheckIn(_lucas));
        Assert.Equal(2, _store.ForMember(_lucas, 10).Count);
    }

    [Fact]
    public void CheckIn_Concurrent_CreatesExactlyOne()
    {
        var results = new CheckInResult[8];
        Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i => results[i] = _store.CheckIn(_lucas));
        Assert.Equal(1, results.Count(r => r == CheckInResult.Created));
        Assert.Equal(7, results.Count(r => r == CheckInResult.AlreadyPresent));
        Assert.Single(_store.ForMember(_lucas, 10));
    }

    [Fact]
    public void CheckIn_2359Then0001_AreTwoDays()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 10, 27, 23, 59, 0, TimeSpan.Zero));
        Assert.Equal(CheckInResult.Created, _store.CheckIn(_lucas));
        _time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(CheckInResult.Created, _store.CheckIn(_lucas));
        var days = _store.ForMember(_lucas, 10).Select(r => r.Presence.Day).ToList();
        Assert.Equal([new DateOnly(2026, 10, 28), new DateOnly(2026, 10, 27)], days);
    }

    [Fact]
    public void AddManual_PastDay_IsValidated()
    {
        var id = _store.AddManual(_lucas, new DateTime(2026, 10, 20, 14, 30, 0), _admin);
        var p = _store.Get(id)!;
        Assert.Equal(PresenceStatus.Validated, p.Status);
        Assert.Equal(PresenceSource.Manual, p.Source);
        Assert.Equal(new DateOnly(2026, 10, 20), p.Day);
        Assert.Equal(_admin, p.DecidedBy);
        Assert.NotNull(p.DecidedAt);
    }

    [Fact]
    public void AddManual_SameDayTwice_CreatesTwo()
    {
        _store.AddManual(_lucas, new DateTime(2026, 10, 20, 14, 30, 0), _admin);
        _store.AddManual(_lucas, new DateTime(2026, 10, 20, 16, 0, 0), _admin);
        Assert.Equal(2, _store.ForMember(_lucas, 10).Count);
    }

    [Fact]
    public void Decide_RefuseThenValidate_KeepsLastDecisionAndAuthor()
    {
        var other = new AccountStore(_db, _time).Create("paul", "motdepasse2", Role.Manager);
        _store.CheckIn(_lucas);
        var id = _store.PendingToday().Single().Presence.Id;
        _store.Decide(id, PresenceStatus.Refused, _admin);
        _time.Advance(TimeSpan.FromMinutes(5));
        _store.Decide(id, PresenceStatus.Validated, other);
        var row = Assert.Single(_store.ValidatedToday());
        Assert.Equal(PresenceStatus.Validated, row.Presence.Status);
        Assert.Equal(other, row.Presence.DecidedBy);
        Assert.Equal("paul", row.DecidedByName);
        Assert.Equal(_time.Now(), row.Presence.DecidedAt);
        Assert.Empty(_store.PendingToday());
    }

    [Fact]
    public void ValidateMany_OnlyValidatesGivenPendingIds_NotLateArrivals()
    {
        var other = new MemberStore(_db, _time).Create(new MemberInput("Martin", "Léa", new DateOnly(2014, 1, 2), null, null, null, NoContact)).Id;
        _store.CheckIn(_lucas);
        var shown = _store.PendingToday().Select(r => r.Presence.Id).ToList(); // ce que la page a affiché
        _store.CheckIn(other); // arrivée après le rendu de la page
        Assert.Equal(1, _store.ValidateMany(shown, _admin));
        Assert.Equal(PresenceStatus.Validated, _store.Get(shown[0])!.Status);
        var late = Assert.Single(_store.PendingToday());
        Assert.Equal(other, late.Presence.MemberId);
    }

    [Fact]
    public void ValidateMany_SkipsAlreadyDecided()
    {
        _store.CheckIn(_lucas);
        var id = _store.PendingToday().Single().Presence.Id;
        _store.Decide(id, PresenceStatus.Refused, _admin); // refusée entre-temps par un autre responsable
        Assert.Equal(0, _store.ValidateMany([id], _admin));
        Assert.Equal(PresenceStatus.Refused, _store.Get(id)!.Status);
        Assert.Equal(0, _store.ValidateMany([], _admin));
    }

    [Fact]
    public void Depart_SetsDepartedAtOnTodaysPresence()
    {
        _store.CheckIn(_lucas);
        _time.Advance(TimeSpan.FromHours(1));
        Assert.True(_store.Depart(_lucas, false));
        Assert.Equal(_time.Now(), _store.ForMember(_lucas, 1).Single().Presence.DepartedAt);
        Assert.False(_store.Depart(_lucas, false)); // déjà parti
    }

    [Fact]
    public void Depart_NoPresence_ReturnsFalse() => Assert.False(_store.Depart(_lucas, false));

    [Fact]
    public void UpdateTimes_MovesDay()
    {
        var id = _store.AddManual(_lucas, new DateTime(2026, 10, 20, 14, 30, 0), _admin);
        _store.UpdateTimes(id, new DateTime(2026, 10, 21, 9, 0, 0), new DateTime(2026, 10, 21, 11, 0, 0));
        var p = _store.Get(id)!;
        Assert.Equal(new DateOnly(2026, 10, 21), p.Day);
        Assert.Equal(new DateTime(2026, 10, 21, 11, 0, 0), p.DepartedAt);
        Assert.Single(_store.ForPeriod(new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 21)));
        Assert.Empty(_store.ForPeriod(new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 20)));
    }

    [Fact]
    public void Delete_RemovesPresence()
    {
        var id = _store.AddManual(_lucas, new DateTime(2026, 10, 20, 14, 30, 0), _admin);
        _store.Delete(id);
        Assert.Null(_store.Get(id));
    }

    [Fact]
    public void PendingToday_IncludesNameAndAge()
    {
        _store.CheckIn(_lucas);
        var row = Assert.Single(_store.PendingToday());
        Assert.Equal("Lucas Dupont", row.MemberName);
        Assert.Equal(11, row.Age);
        Assert.Null(row.DecidedByName);
    }

    private long NewMember(string last, string first) =>
        new MemberStore(_db, _time).Create(new MemberInput(last, first, new DateOnly(2015, 1, 1), null, null, null, NoContact)).Id;

    // Le lendemain : FakeTimeProvider ne remonte pas le temps depuis 17 h.
    private void At(int hour) => _time.SetUtcNow(new DateTimeOffset(2026, 10, 28, hour, 0, 0, TimeSpan.Zero));

    // ForMember trie du plus récent au plus ancien : Last() = premier passage.
    private Presence First(long memberId) => _store.ForMember(memberId, 10).Last().Presence;

    [Fact]
    public void CheckIn_AfterDepart_CreatesSecondPassage()
    {
        _store.CheckIn(_lucas);
        _store.Depart(_lucas, false);
        Assert.Equal(CheckInResult.Created, _store.CheckIn(_lucas));
        Assert.Equal(2, _store.ForMember(_lucas, 10).Count);
        Assert.True(_store.IsPresent(_lucas));
    }

    [Fact]
    public void CheckIn_Force_WhilePresent_LeavesFirstOpen()
    {
        _store.CheckIn(_lucas);
        Assert.Equal(CheckInResult.Created, _store.CheckIn(_lucas, force: true));
        var rows = _store.ForMember(_lucas, 10);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Null(r.Presence.DepartedAt));
    }

    [Fact]
    public void CheckIn_ValidatesPendingDeparture()
    {
        _store.CheckIn(_lucas);
        _store.Depart(_lucas, true);
        _store.CheckIn(_lucas);
        Assert.Equal(PresenceStatus.Validated, First(_lucas).DepartureStatus);
    }

    [Fact]
    public void AddManual_ValidatesPendingDeparture()
    {
        _store.CheckIn(_lucas);
        _store.Depart(_lucas, true);
        _store.AddManual(_lucas, new DateTime(2026, 10, 27, 18, 0, 0), _admin);
        Assert.Equal(PresenceStatus.Validated, First(_lucas).DepartureStatus);
    }

    [Fact]
    public void Depart_WithValidation_IsPending()
    {
        _store.CheckIn(_lucas);
        Assert.True(_store.Depart(_lucas, true));
        var p = _store.ForMember(_lucas, 1).Single().Presence;
        Assert.Equal(PresenceStatus.Pending, p.DepartureStatus);
        Assert.Equal(new DateTime(2026, 10, 27, 17, 0, 0), p.DepartedAt);
        Assert.False(_store.IsPresent(_lucas));
    }

    [Fact]
    public void Depart_WithoutValidation_IsValidated()
    {
        _store.CheckIn(_lucas);
        _store.Depart(_lucas, false);
        Assert.Equal(PresenceStatus.Validated, First(_lucas).DepartureStatus);
    }

    [Fact]
    public void Depart_Twice_SecondReturnsFalseAndKeepsFirstTime()
    {
        _store.CheckIn(_lucas);
        _store.Depart(_lucas, true);
        var first = _time.Now();
        _time.Advance(TimeSpan.FromMinutes(5));
        Assert.False(_store.Depart(_lucas, true));
        Assert.Equal(first, First(_lucas).DepartedAt);
    }

    [Fact]
    public void DecideDeparture_Validate_IsValidated()
    {
        _store.CheckIn(_lucas);
        _store.Depart(_lucas, true);
        Assert.True(_store.DecideDeparture(First(_lucas).Id, true));
        Assert.Equal(PresenceStatus.Validated, First(_lucas).DepartureStatus);
        Assert.False(_store.IsPresent(_lucas));
    }

    [Fact]
    public void DecideDeparture_Refuse_ClearsDeparture()
    {
        _store.CheckIn(_lucas);
        _store.Depart(_lucas, true);
        Assert.True(_store.DecideDeparture(First(_lucas).Id, false));
        var p = First(_lucas);
        Assert.Null(p.DepartedAt);
        Assert.Null(p.DepartureStatus);
        Assert.True(_store.IsPresent(_lucas));
    }

    [Fact]
    public void DecideDeparture_AfterReArrival_DoesNothing()
    {
        _store.CheckIn(_lucas);
        _store.Depart(_lucas, true);
        var firstId = First(_lucas).Id;
        _store.CheckIn(_lucas);
        Assert.False(_store.DecideDeparture(firstId, false));
        Assert.NotNull(_store.Get(firstId)!.DepartedAt);
    }

    [Fact]
    public void ValidatePendingDepartures_ValidatesAll()
    {
        _store.CheckIn(_lucas);
        _store.Depart(_lucas, true);
        Assert.Equal(1, _store.ValidatePendingDepartures());
        Assert.Equal(PresenceStatus.Validated, First(_lucas).DepartureStatus);
    }

    [Fact]
    public void PendingDepartures_ExcludesRefusedArrival()
    {
        var emma = NewMember("Petit", "Emma");
        _store.CheckIn(_lucas);
        _store.Depart(_lucas, true);
        _store.CheckIn(emma);
        _store.Depart(emma, true);
        _store.Decide(First(emma).Id, PresenceStatus.Refused, _admin);
        Assert.Equal(_lucas, Assert.Single(_store.PendingDepartures()).Presence.MemberId);
    }

    [Fact]
    public void UpdateTimes_RemovingDeparture_ClearsStatus()
    {
        _store.CheckIn(_lucas);
        _store.Depart(_lucas, true);
        var id = First(_lucas).Id;
        _store.UpdateTimes(id, new DateTime(2026, 10, 27, 9, 0, 0), null);
        Assert.Null(_store.Get(id)!.DepartureStatus);
    }

    [Fact]
    public void UpdateTimes_SettingDeparture_IsValidated()
    {
        _store.CheckIn(_lucas);
        var id = First(_lucas).Id;
        _store.UpdateTimes(id, new DateTime(2026, 10, 27, 9, 0, 0), new DateTime(2026, 10, 27, 11, 0, 0));
        Assert.Equal(PresenceStatus.Validated, _store.Get(id)!.DepartureStatus);
    }

    [Fact]
    public void IsPresent_OpenPassageYesterday_IsFalse()
    {
        _store.CheckIn(_lucas);
        _time.Advance(TimeSpan.FromDays(1));
        Assert.False(_store.IsPresent(_lucas));
    }

    [Fact]
    public void Today_SplitsPresentAndDeparted()
    {
        var emma = NewMember("Petit", "Emma");
        var leo = NewMember("Martin", "Léo");
        At(9);
        _store.CheckIn(_lucas);
        _store.CheckIn(emma);
        _store.CheckIn(leo);
        At(10);
        _store.Depart(emma, true);
        At(12);
        _store.Depart(_lucas, false);
        At(13);
        _store.CheckIn(leo, force: true);
        At(14);
        _store.CheckIn(_lucas, force: true);

        var board = _store.Today();
        Assert.Equal(
            [(_lucas, 14), (leo, 13), (emma, 9)],
            board.Present.Select(r => (r.Presence.MemberId, r.Presence.ArrivedAt.Hour)).ToList());
        Assert.Equal(PresenceStatus.Pending, board.Present.Single(r => r.Presence.MemberId == emma).Presence.DepartureStatus);
        var departed = board.Departed.Select(r => (r.Presence.MemberId, r.Presence.ArrivedAt.Hour, r.Presence.DepartedAt?.Hour)).ToList();
        Assert.Equal(2, departed.Count);
        Assert.Contains((_lucas, 9, (int?)12), departed);
        Assert.Contains((leo, 9, (int?)null), departed);
    }

    [Fact]
    public void Today_ExcludesRefused()
    {
        _store.CheckIn(_lucas);
        _store.Decide(First(_lucas).Id, PresenceStatus.Refused, _admin);
        var board = _store.Today();
        Assert.Empty(board.Present);
        Assert.Empty(board.Departed);
    }
}
