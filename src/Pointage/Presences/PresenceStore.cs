using Dapper;
using Microsoft.Data.Sqlite;
using Pointage.Data;
using Pointage.Members;

namespace Pointage.Presences;

public enum PresenceStatus { Pending, Validated, Refused }

public static class PresenceStatusExtensions
{
    // Libellé français : l'interface n'affiche jamais le nom de l'enum.
    public static string Label(this PresenceStatus s) => s switch
    {
        PresenceStatus.Validated => "validé",
        PresenceStatus.Refused => "refusé",
        _ => "en attente",
    };
}

public enum PresenceSource { Kiosk, Manual }

public enum CheckInResult { Created, AlreadyToday }

public sealed record Presence(long Id, long MemberId, DateOnly Day, DateTime ArrivedAt, DateTime? DepartedAt,
    PresenceStatus Status, PresenceSource Source, long? DecidedBy, DateTime? DecidedAt);

public sealed record PresenceRow(Presence Presence, string MemberName, int? Age, string? DecidedByName);

public sealed class PresenceStore(Db db, TimeProvider time)
{
    // Dapper ne remplit pas un record imbriqué ni les enums via un constructeur : lecture à plat puis assemblage.
    private sealed class Row
    {
        public long Id { get; set; }
        public long MemberId { get; set; }
        public DateOnly Day { get; set; }
        public DateTime ArrivedAt { get; set; }
        public DateTime? DepartedAt { get; set; }
        public PresenceStatus Status { get; set; }
        public PresenceSource Source { get; set; }
        public long? DecidedBy { get; set; }
        public DateTime? DecidedAt { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateOnly? BirthDate { get; set; }
        public DateTime? AnonymizedAt { get; set; }
        public string? DecidedByName { get; set; }

        public Presence ToPresence() => new(Id, MemberId, Day, ArrivedAt, DepartedAt, Status, Source, DecidedBy, DecidedAt);

        public PresenceRow ToRow()
        {
            // Un Member minimal réutilise FullName (anonymisation comprise) et AgeOn.
            var m = new Member(MemberId, "", LastName, FirstName, BirthDate, null, null, null,
                new ContactInput(null, null, null, null, null), default, AnonymizedAt);
            return new(ToPresence(), m.FullName, m.AgeOn(Day), DecidedByName); // âge à la date de la présence
        }
    }

    private const string PresenceColumns =
        "p.id AS Id, p.member_id AS MemberId, p.day AS Day, p.arrived_at AS ArrivedAt, p.departed_at AS DepartedAt, p.status AS Status, " +
        "p.source AS Source, p.decided_by AS DecidedBy, p.decided_at AS DecidedAt";

    // ponytail: une présence par jour ; pour plusieurs passages (spec §5), ne garder que le refus d'une présence encore ouverte.
    private static bool AllowsNewPresence(SqliteConnection c, SqliteTransaction tx, long memberId, DateOnly day) =>
        c.ExecuteScalar<long>("SELECT COUNT(*) FROM presences WHERE member_id=@memberId AND day=@day AND status<>'Refused'",
            new { memberId, day }, tx) == 0;

    // Vérification et insertion dans une seule transaction (BEGIN IMMEDIATE) : deux appuis simultanés ne créent qu'une présence.
    private bool TryInsert(long memberId, DateTime arrivedAt, PresenceStatus status, PresenceSource source, long? accountId, out long id)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        var day = DateOnly.FromDateTime(arrivedAt);
        id = 0;
        if (!AllowsNewPresence(c, tx, memberId, day)) return false;
        id = c.ExecuteScalar<long>(
            """
            INSERT INTO presences(member_id, day, arrived_at, status, source, decided_by, decided_at)
            VALUES(@memberId, @day, @arrivedAt, @status, @source, @accountId, @decidedAt);
            SELECT last_insert_rowid()
            """,
            new
            {
                memberId, day, arrivedAt, status = status.ToString(), source = source.ToString(), accountId,
                decidedAt = accountId is null ? (DateTime?)null : time.Now(),
            }, tx);
        tx.Commit();
        return true;
    }

    public CheckInResult CheckIn(long memberId) =>
        TryInsert(memberId, time.Now(), PresenceStatus.Pending, PresenceSource.Kiosk, null, out _) ? CheckInResult.Created : CheckInResult.AlreadyToday;

    public long AddManual(long memberId, DateTime arrivedAt, long accountId) =>
        TryInsert(memberId, arrivedAt, PresenceStatus.Validated, PresenceSource.Manual, accountId, out var id)
            ? id
            : throw new InvalidOperationException("Ce membre a déjà une présence ce jour-là.");

    public bool Depart(long memberId)
    {
        using var c = db.Open();
        return c.Execute(
            """
            UPDATE presences SET departed_at=@now WHERE id=(
              SELECT id FROM presences WHERE member_id=@memberId AND day=@today AND status<>'Refused' AND departed_at IS NULL
              ORDER BY arrived_at DESC, id DESC LIMIT 1)
            """, new { memberId, today = time.Today(), now = time.Now() }) > 0;
    }

    public void Decide(long presenceId, PresenceStatus status, long accountId)
    {
        using var c = db.Open();
        c.Execute("UPDATE presences SET status=@status, decided_by=@accountId, decided_at=@now WHERE id=@presenceId",
            new { presenceId, status = status.ToString(), accountId, now = time.Now() });
    }

    // « Tout valider » ne valide que les arrivées que le responsable a vues à l'écran (ids envoyés par la page),
    // jamais celles arrivées depuis : sinon la validation humaine ne protège plus de la fraude.
    public int ValidateMany(IEnumerable<long> ids, long accountId)
    {
        using var c = db.Open();
        return c.Execute("UPDATE presences SET status='Validated', decided_by=@accountId, decided_at=@now WHERE status='Pending' AND id IN @ids",
            new { accountId, ids = ids.ToArray(), now = time.Now() });
    }

    public void UpdateTimes(long presenceId, DateTime arrivedAt, DateTime? departedAt)
    {
        using var c = db.Open();
        c.Execute("UPDATE presences SET day=@day, arrived_at=@arrivedAt, departed_at=@departedAt WHERE id=@presenceId",
            new { presenceId, day = DateOnly.FromDateTime(arrivedAt), arrivedAt, departedAt });
    }

    public void Delete(long presenceId)
    {
        using var c = db.Open();
        c.Execute("DELETE FROM presences WHERE id=@presenceId", new { presenceId });
    }

    public Presence? Get(long id)
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<Row>($"SELECT {PresenceColumns} FROM presences p WHERE p.id=@id", new { id })?.ToPresence();
    }

    public bool HasPresenceToday(long memberId)
    {
        using var c = db.Open();
        return c.ExecuteScalar<long>("SELECT COUNT(*) FROM presences WHERE member_id=@memberId AND day=@today AND status<>'Refused'",
            new { memberId, today = time.Today() }) > 0;
    }

    public IReadOnlyList<PresenceRow> PendingToday() => Query("p.day=@today AND p.status='Pending'", new { today = time.Today() });

    public IReadOnlyList<PresenceRow> ValidatedToday() => Query("p.day=@today AND p.status='Validated'", new { today = time.Today() });

    public IReadOnlyList<PresenceRow> ForMember(long memberId, int limit) => Query("p.member_id=@memberId", new { memberId }, limit);

    public IReadOnlyList<PresenceRow> ForPeriod(DateOnly from, DateOnly to) => Query("p.day BETWEEN @from AND @to", new { from, to });

    // Secondes seulement en base : id DESC départage deux arrivées de la même seconde.
    private IReadOnlyList<PresenceRow> Query(string where, object p, int limit = -1)
    {
        using var c = db.Open();
        return c.Query<Row>(
            $"""
            SELECT {PresenceColumns}, m.first_name AS FirstName, m.last_name AS LastName, m.birth_date AS BirthDate,
              m.anonymized_at AS AnonymizedAt, a.username AS DecidedByName
            FROM presences p JOIN members m ON m.id=p.member_id LEFT JOIN accounts a ON a.id=p.decided_by
            WHERE {where} ORDER BY p.arrived_at DESC, p.id DESC LIMIT {limit}
            """, p).Select(r => r.ToRow()).ToList();
    }
}
