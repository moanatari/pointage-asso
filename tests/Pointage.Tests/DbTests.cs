using Dapper;
using Microsoft.Data.Sqlite;
using Pointage.Data;

namespace Pointage.Tests;

public class DbTests
{
    private static Db NewDb() =>
        new(Path.Combine(Directory.CreateTempSubdirectory("pointage-tests-").FullName, "pointage.db"));

    [Fact]
    public void Migrate_FreshDb_SetsUserVersionAndCreatesTables()
    {
        var db = NewDb();
        db.Migrate();
        using var c = db.Open();
        Assert.Equal(Db.SchemaVersion, c.ExecuteScalar<int>("PRAGMA user_version"));
        var tables = c.Query<string>("SELECT name FROM sqlite_master WHERE type='table'").ToHashSet();
        foreach (var t in new[] { "members", "presences", "accounts", "audit", "settings", "kiosks" })
            Assert.Contains(t, tables);
    }

    [Fact]
    public void Migrate_Twice_IsIdempotent()
    {
        var db = NewDb();
        db.Migrate();
        db.Migrate();
        using var c = db.Open();
        Assert.Equal(Db.SchemaVersion, c.ExecuteScalar<int>("PRAGMA user_version"));
    }

    [Fact]
    public void Migrate_OldDb_WritesTimestampedBackupFirst()
    {
        var db = NewDb();
        using (var raw = new SqliteConnection($"Data Source={db.Path};Pooling=False"))
        {
            raw.Execute("CREATE TABLE dummy(x); PRAGMA user_version=0");
        }
        db.Migrate();
        var dir = System.IO.Path.GetDirectoryName(db.Path)!;
        Assert.Single(Directory.GetFiles(dir, "pointage-*.bak"));
    }

    [Fact]
    public void Migrate_FreshDb_WritesNoBackup()
    {
        var db = NewDb();
        db.Migrate();
        Assert.Empty(Directory.GetFiles(System.IO.Path.GetDirectoryName(db.Path)!, "*.bak"));
    }

    [Fact]
    public void Dapper_DateOnlyAndTimeOnly_RoundTripAsText()
    {
        using var c = TestDb.Create(out _).Open();
        var (d, t) = c.QuerySingle<(DateOnly, TimeOnly)>("SELECT @d, @t", new { d = new DateOnly(2026, 10, 27), t = new TimeOnly(17, 5, 9) });
        Assert.Equal(new DateOnly(2026, 10, 27), d);
        Assert.Equal(new TimeOnly(17, 5, 9), t);
        Assert.Equal("2026-10-27", c.ExecuteScalar<string>("SELECT @d", new { d = new DateOnly(2026, 10, 27) }));
    }

    [Fact]
    public void ClockNow_WithSubSecondTime_RoundTripsThroughDb()
    {
        // Dapper n'applique pas le handler DateTime aux paramètres : une heure à la milliseconde
        // serait écrite avec ses fractions puis illisible par ParseExact.
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 17, 0, 0, 123, TimeSpan.Zero));
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        using var c = NewDb().Open();
        Assert.Equal(new DateTime(2026, 10, 27, 17, 0, 0), c.QuerySingle<DateTime>("SELECT @now", new { now = time.Now() }));
    }

    [Fact]
    public void DateTime_WithFractionalSeconds_IsReadBack()
    {
        // Une date qui ne vient pas de Clock.Now() (saisie, calculée) arrive en base avec ses fractions.
        var at = new DateTime(2026, 10, 27, 17, 0, 0, 123);
        using var c = NewDb().Open();
        c.Execute("CREATE TABLE t(at TEXT)");
        c.Execute("INSERT INTO t(at) VALUES(@at)", new { at });
        Assert.Equal(at, c.QuerySingle<DateTime>("SELECT at FROM t"));
    }
}
