using Pointage.Audit;
using Pointage.Data;
using Pointage.Settings;

namespace Pointage.Tests;

public class SettingsAndAuditTests
{
    [Fact]
    public void Settings_EmptyDb_ReturnsDefaults()
    {
        var s = new SettingsStore(TestDb.Create(out _)).Get();
        Assert.Equal(12, s.RetentionMonths);
        Assert.True(s.KioskKeyboard);
        Assert.False(s.KioskDeparture);
    }

    [Fact]
    public void Settings_SaveThenGet_RoundTrips()
    {
        var store = new SettingsStore(TestDb.Create(out _));
        var saved = new AppSettings("Asso Soleil", 24, false, true);
        store.Save(saved);
        store.Save(saved with { RetentionMonths = 6 });
        Assert.Equal(saved with { RetentionMonths = 6 }, store.Get());
    }

    [Fact]
    public void Audit_DeleteOlderThan_RemovesOnlyOldEntries()
    {
        var log = new AuditLog(TestDb.Create(out var time), time);
        log.Write(1, AuditActions.Login);
        time.Advance(TimeSpan.FromDays(40));
        log.Write(null, AuditActions.MemberCreate, 7, "détail");

        Assert.Equal(1, log.DeleteOlderThan(time.Now().AddDays(-30)));

        var left = Assert.Single(log.Recent(10));
        Assert.Equal(AuditActions.MemberCreate, left.Action);
        Assert.Equal(7, left.MemberId);
        Assert.Equal("détail", left.Details);
        Assert.Equal(time.Now(), left.At);
    }
}
