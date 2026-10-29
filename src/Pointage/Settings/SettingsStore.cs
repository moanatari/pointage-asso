using System.Globalization;
using Dapper;
using Pointage.Data;

namespace Pointage.Settings;

public sealed record AppSettings(string OrganizationName, int RetentionMonths, bool KioskKeyboard, bool KioskDeparture)
{
    public static readonly AppSettings Default = new("", 12, true, false);
}

public sealed class SettingsStore(Db db)
{
    public AppSettings Get()
    {
        using var c = db.Open();
        var v = c.Query<(string Key, string Value)>("SELECT key, value FROM settings").ToDictionary(r => r.Key, r => r.Value);
        var d = AppSettings.Default;
        return new AppSettings(
            v.GetValueOrDefault("organization_name", d.OrganizationName),
            v.TryGetValue("retention_months", out var m) && int.TryParse(m, CultureInfo.InvariantCulture, out var months) ? months : d.RetentionMonths,
            v.TryGetValue("kiosk_keyboard", out var k) ? k == "1" : d.KioskKeyboard,
            v.TryGetValue("kiosk_departure", out var dep) ? dep == "1" : d.KioskDeparture);
    }

    public void Save(AppSettings s)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        foreach (var (key, value) in new[]
        {
            ("organization_name", s.OrganizationName),
            ("retention_months", s.RetentionMonths.ToString(CultureInfo.InvariantCulture)),
            ("kiosk_keyboard", s.KioskKeyboard ? "1" : "0"),
            ("kiosk_departure", s.KioskDeparture ? "1" : "0"),
        })
            c.Execute("INSERT INTO settings(key, value) VALUES(@key, @value) ON CONFLICT(key) DO UPDATE SET value=excluded.value", new { key, value }, tx);
        tx.Commit();
    }
}
