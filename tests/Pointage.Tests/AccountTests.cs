using Pointage.Accounts;

namespace Pointage.Tests;

public class AccountTests
{
    private static AccountStore NewStore() => new(TestDb.Create(out var time), time);

    [Fact]
    public void Hash_ThenVerify_RoundTrips()
    {
        var hash = Passwords.Hash("secret12");
        Assert.True(Passwords.Verify("secret12", hash));
        Assert.False(Passwords.Verify("autre", hash));
    }

    [Fact]
    public void Hash_HasExpectedFormat() =>
        Assert.StartsWith("pbkdf2-sha256$600000$", Passwords.Hash("secret12"));

    [Fact]
    public void Create_DuplicateUsernameIgnoringCase_Throws()
    {
        var store = NewStore();
        store.Create("Marie", "secret12", Role.Manager);
        Assert.Throws<ArgumentException>(() => store.Create("marie", "secret12", Role.Manager));
    }

    [Fact]
    public void Create_ShortPassword_Throws() =>
        Assert.Throws<ArgumentException>(() => NewStore().Create("marie", "1234567", Role.Manager));

    [Fact]
    public void VerifyLogin_InactiveAccount_ReturnsNull()
    {
        var store = NewStore();
        store.Create("admin", "secret12", Role.SuperAdmin);
        var id = store.Create("marie", "secret12", Role.Manager);
        Assert.NotNull(store.VerifyLogin("Marie", "secret12"));
        store.SetActive(id, false);
        Assert.Null(store.VerifyLogin("marie", "secret12"));
    }

    [Fact]
    public void SetActive_LastActiveSuperAdmin_Throws()
    {
        var store = NewStore();
        var id = store.Create("admin", "secret12", Role.SuperAdmin);
        Assert.Throws<InvalidOperationException>(() => store.SetActive(id, false));
        Assert.True(store.Get(id)!.Active);
    }

    [Fact]
    public void SetActive_SuperAdminWhenAnotherExists_Works()
    {
        var store = NewStore();
        var id = store.Create("admin", "secret12", Role.SuperAdmin);
        store.Create("autre", "secret12", Role.SuperAdmin);
        store.SetActive(id, false);
        Assert.False(store.Get(id)!.Active);
    }

    [Fact]
    public void UpsertSuperAdmin_ExistingManager_BecomesActiveSuperAdmin()
    {
        var store = NewStore();
        var id = store.Create("marie", "secret12", Role.Manager);
        store.Create("admin", "secret12", Role.SuperAdmin);
        store.SetActive(id, false);

        Assert.Equal(id, store.UpsertSuperAdmin("Marie", "nouveau123"));

        var a = store.Get(id)!;
        Assert.Equal(Role.SuperAdmin, a.Role);
        Assert.True(a.Active);
        Assert.NotNull(store.VerifyLogin("marie", "nouveau123"));
        Assert.True(store.AnySuperAdmin());
    }
}
