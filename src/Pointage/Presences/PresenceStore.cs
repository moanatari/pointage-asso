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

public enum CheckInResult { Created, AlreadyPresent }

// Une ligne = un passage (arrivée, départ éventuel). DepartureStatus : null = pas de départ, sinon Pending ou Validated.
public sealed record Presence(long Id, long MemberId, DateOnly Day, DateTime ArrivedAt, DateTime? DepartedAt,
    PresenceStatus Status, PresenceSource Source, long? DecidedBy, DateTime? DecidedAt, PresenceStatus? DepartureStatus);

public sealed record PresenceRow(Presence Presence, string MemberName, int? Age, string? DecidedByName);

public sealed record TodayBoard(IReadOnlyList<PresenceRow> Present, IReadOnlyList<PresenceRow> Departed);

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
        public PresenceStatus? DepartureStatus { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateOnly? BirthDate { get; set; }
        public DateTime? AnonymizedAt { get; set; }
        public string? DecidedByName { get; set; }

        public Presence ToPresence() => new(Id, MemberId, Day, ArrivedAt, DepartedAt, Status, Source, DecidedBy, DecidedAt, DepartureStatus);

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
        "p.source AS Source, p.decided_by AS DecidedBy, p.decided_at AS DecidedAt, p.departure_status AS DepartureStatus";

    // Présent = son dernier passage non refusé du jour est ouvert (pas de départ). Un départ en attente = parti pour la borne (présent à l'accueil, voir Split).
    private static bool IsPresent(SqliteConnection c, SqliteTransaction? tx, long memberId, DateOnly today) =>
        c.ExecuteScalar<long?>(
            """
            SELECT departed_at IS NULL FROM presences WHERE member_id=@memberId AND day=@today AND status<>'Refused'
            ORDER BY arrived_at DESC, id DESC LIMIT 1
            """, new { memberId, today }, tx) == 1;

    private long Insert(SqliteConnection c, SqliteTransaction tx, long memberId, DateTime arrivedAt, PresenceStatus status,
        PresenceSource source, long? accountId)
    {
        // Une nouvelle arrivée (pas antidatée avant le départ) confirme le départ précédent encore en attente : s'il revient, c'est qu'il était bien parti.
        // Le refus d'un départ n'est donc possible qu'avant un nouveau passage, ce qui évite deux passages ouverts.
        c.Execute("UPDATE presences SET departure_status='Validated' WHERE member_id=@memberId AND departure_status='Pending' AND departed_at <= @arrivedAt",
            new { memberId, arrivedAt }, tx);
        return c.ExecuteScalar<long>(
            """
            INSERT INTO presences(member_id, day, arrived_at, status, source, decided_by, decided_at)
            VALUES(@memberId, @day, @arrivedAt, @status, @source, @accountId, @decidedAt);
            SELECT last_insert_rowid()
            """,
            new
            {
                memberId, day = DateOnly.FromDateTime(arrivedAt), arrivedAt, status = status.ToString(), source = source.ToString(), accountId,
                decidedAt = accountId is null ? (DateTime?)null : time.Now(),
            }, tx);
    }

    // Vérification et insertion dans une seule transaction (BEGIN IMMEDIATE) : deux appuis simultanés ne créent qu'un passage.
    // force : la borne a confirmé une nouvelle arrivée alors que l'enfant semble encore là (départ oublié).
    public CheckInResult CheckIn(long memberId, bool force = false)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        if (!force && IsPresent(c, tx, memberId, time.Today())) return CheckInResult.AlreadyPresent;
        Insert(c, tx, memberId, time.Now(), PresenceStatus.Pending, PresenceSource.Kiosk, null);
        tx.Commit();
        return CheckInResult.Created;
    }

    // Saisie d'un responsable : jamais refusée, même si l'enfant a déjà un passage ce jour-là.
    public long AddManual(long memberId, DateTime arrivedAt, long accountId)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        var id = Insert(c, tx, memberId, arrivedAt, PresenceStatus.Validated, PresenceSource.Manual, accountId);
        tx.Commit();
        return id;
    }

    public bool Depart(long memberId, bool needsValidation)
    {
        using var c = db.Open();
        return c.Execute(
            """
            UPDATE presences SET departed_at=@now, departure_status=@status WHERE id=(
              SELECT id FROM presences WHERE member_id=@memberId AND day=@today AND status<>'Refused'
              ORDER BY arrived_at DESC, id DESC LIMIT 1) AND departed_at IS NULL
            """,
            new
            {
                memberId, today = time.Today(), now = time.Now(),
                status = (needsValidation ? PresenceStatus.Pending : PresenceStatus.Validated).ToString(),
            }) > 0;
    }

    // Seulement un départ encore en attente : déjà validé d'office par un retour, il n'est plus refusable.
    // Refuser efface le départ : l'enfant redevient présent.
    public bool DecideDeparture(long presenceId, bool validate)
    {
        using var c = db.Open();
        var set = validate ? "departure_status='Validated'" : "departed_at=NULL, departure_status=NULL";
        return c.Execute($"UPDATE presences SET {set} WHERE id=@presenceId AND departure_status='Pending'", new { presenceId }) > 0;
    }

    public int ValidatePendingDepartures()
    {
        using var c = db.Open();
        return c.Execute("UPDATE presences SET departure_status='Validated' WHERE departure_status='Pending'");
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

    // Un départ saisi ou corrigé par un responsable est réputé validé.
    public void UpdateTimes(long presenceId, DateTime arrivedAt, DateTime? departedAt)
    {
        using var c = db.Open();
        c.Execute("UPDATE presences SET day=@day, arrived_at=@arrivedAt, departed_at=@departedAt, departure_status=@departureStatus WHERE id=@presenceId",
            new
            {
                presenceId, day = DateOnly.FromDateTime(arrivedAt), arrivedAt, departedAt,
                departureStatus = departedAt is null ? null : PresenceStatus.Validated.ToString(),
            });
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

    public bool IsPresent(long memberId)
    {
        using var c = db.Open();
        return IsPresent(c, null, memberId, time.Today());
    }

    // Tous jours confondus : un départ oublié la veille doit rester visible pour être décidé.
    public IReadOnlyList<PresenceRow> PendingDepartures() => Query("p.departure_status='Pending' AND p.status<>'Refused'", new { });

    public TodayBoard Today() => Split(Query("p.day=@today AND p.status<>'Refused'", new { today = time.Today() }));

    // Le passage le plus récent de chaque membre est « présent » s'il est ouvert ou si son départ attend une validation
    // (un départ pointé par erreur ne doit pas faire disparaître l'enfant). Tout le reste est « parti »,
    // y compris un passage ancien resté sans départ (départ non pointé).
    public static TodayBoard Split(IReadOnlyList<PresenceRow> rows)
    {
        var sorted = rows.OrderByDescending(r => r.Presence.ArrivedAt).ThenByDescending(r => r.Presence.Id).ToList();
        var present = sorted.DistinctBy(r => r.Presence.MemberId)
            .Where(r => r.Presence.DepartedAt is null || r.Presence.DepartureStatus == PresenceStatus.Pending)
            .Select(r => r.Presence.Id).ToHashSet();
        return new(sorted.Where(r => present.Contains(r.Presence.Id)).ToList(), sorted.Where(r => !present.Contains(r.Presence.Id)).ToList());
    }

    public IReadOnlyList<PresenceRow> PendingToday() => Query("p.day=@today AND p.status='Pending'", new { today = time.Today() });

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
