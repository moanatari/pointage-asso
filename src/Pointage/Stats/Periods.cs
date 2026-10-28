namespace Pointage.Stats;

// Bornes incluses. Les semaines commencent le lundi (usage français).
public readonly record struct Period(DateOnly From, DateOnly To);

public static class Periods
{
    public static Period Yesterday(DateOnly today) => new(today.AddDays(-1), today.AddDays(-1));

    private static DateOnly MondayOf(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    public static Period ThisWeek(DateOnly today) => new(MondayOf(today), today);

    public static Period LastWeek(DateOnly today)
    {
        var monday = MondayOf(today).AddDays(-7);
        return new(monday, monday.AddDays(6));
    }

    public static Period ThisMonth(DateOnly today) => new(new DateOnly(today.Year, today.Month, 1), today);

    public static Period LastMonth(DateOnly today)
    {
        var first = new DateOnly(today.Year, today.Month, 1);
        return new(first.AddMonths(-1), first.AddDays(-1));
    }

    // Année scolaire : du 1er septembre au 31 août.
    public static DateOnly SchoolYearStart(DateOnly today) => new(today.Month >= 9 ? today.Year : today.Year - 1, 9, 1);
}
