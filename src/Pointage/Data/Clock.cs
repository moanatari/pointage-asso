namespace Pointage.Data;

// Une « journée » est le jour calendaire local du poste : on ne lit jamais l'heure UTC.
public static class Clock
{
    public static DateTime Now(this TimeProvider t) => t.GetLocalNow().DateTime;
    public static DateOnly Today(this TimeProvider t) => DateOnly.FromDateTime(t.Now());
}
