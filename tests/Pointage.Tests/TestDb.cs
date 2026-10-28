using Microsoft.Extensions.Time.Testing;
using Pointage.Data;

namespace Pointage.Tests;

public static class TestDb
{
    // Base SQLite dans un dossier temporaire à part, déjà migrée. L'horloge est fixée
    // au mardi 2026-10-27 17:00 (heure locale = UTC) pour des tests déterministes.
    public static Db Create(out FakeTimeProvider time)
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pointage-tests-" + Guid.NewGuid().ToString("N")));
        var db = new Db(Path.Combine(dir.FullName, "pointage.db"));
        db.Migrate();
        time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 17, 0, 0, TimeSpan.Zero));
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        return db;
    }
}
