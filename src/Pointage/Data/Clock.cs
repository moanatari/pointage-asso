namespace Pointage.Data;

// Une « journée » est le jour calendaire local du poste : on ne lit jamais l'heure UTC.
public static class Clock
{
    // Tronqué à la seconde : c'est la précision stockée en base, et Dapper n'applique pas le handler DateTime
    // aux paramètres (les fractions seraient écrites telles quelles puis illisibles à la relecture).
    public static DateTime Now(this TimeProvider t)
    {
        var now = t.GetLocalNow().DateTime;
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
    }
    public static DateOnly Today(this TimeProvider t) => DateOnly.FromDateTime(t.Now());
}
