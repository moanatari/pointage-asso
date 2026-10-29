using Dapper;
using Pointage.Members;

namespace Pointage.Tests;

public class MemberTests
{
    private static readonly ContactInput NoContact = new(null, null, null, null, null);

    private static MemberInput Input(string first = "Lucas", string last = "Dupont", string birth = "2015-03-12") =>
        new(last, first, DateOnly.Parse(birth), "0600000000", "a@b.fr", "allergie", new ContactInput("Dupont", "Anne", null, "0611111111", null));

    private static MemberStore NewStore() => new(TestDb.Create(out var time), time);

    [Fact]
    public void New_Code_UsesOnlyAlphabetAndLength6()
    {
        for (var i = 0; i < 1000; i++)
        {
            var code = MemberCode.New();
            Assert.Equal(MemberCode.Length, code.Length);
            Assert.All(code, c => Assert.Contains(c, MemberCode.Alphabet));
        }
    }

    [Fact]
    public void Normalize_Code_UppercasesAndTrims() => Assert.Equal("K7MXQ4", MemberCode.Normalize(" k7mxq4 "));

    [Fact]
    public void Validate_MissingNames_ReturnsBothMessages()
    {
        var errors = Input(first: " ", last: "").Validate(new DateOnly(2026, 10, 27));
        Assert.Equal(["Le nom est obligatoire.", "Le prénom est obligatoire."], errors);
    }

    [Fact]
    public void Validate_FutureBirthDate_ReturnsMessage() =>
        Assert.Equal(["La date de naissance ne peut pas être dans le futur."], Input(birth: "2026-10-28").Validate(new DateOnly(2026, 10, 27)));

    [Theory]
    [InlineData("ines", "Inès", "Morel")]
    [InlineData("jean pierre", "Jean-Pierre", "Martin")]
    [InlineData("ndiaye", "Awa", "N'Diaye")]
    [InlineData("MOR in", "Inès", "Morel")]
    public void Search_IgnoresAccentsCaseAndPunctuation(string q, string first, string last)
    {
        var store = NewStore();
        store.Create(Input("Autre", "Personne"));
        var created = store.Create(Input(first, last));
        Assert.Equal([created.Id], store.Search(q, 10).Select(m => m.Id));
    }

    [Fact]
    public void Search_ByCode_FindsMember()
    {
        var store = NewStore();
        var m = store.Create(Input());
        Assert.Equal([m.Id], store.Search(m.Code[..4].ToLowerInvariant(), 10).Select(x => x.Id));
    }

    [Fact]
    public void Search_ExcludesAnonymized()
    {
        var store = NewStore();
        var m = store.Create(Input());
        store.Anonymize(m.Id);
        Assert.Empty(store.Search("lucas", 10));
        Assert.Empty(store.Search(m.Code, 10));
    }

    [Fact]
    public void Search_TreatsLikeWildcardsAsText()
    {
        var store = NewStore();
        store.Create(Input());
        Assert.Empty(store.Search("%", 10));
        Assert.Empty(store.Search("_", 10));
    }

    [Fact]
    public void KioskLabel_IsFirstNameAndInitial() => Assert.Equal("Lucas D.", NewStore().Create(Input()).KioskLabel);

    [Fact]
    public void AgeOn_BeforeBirthday_IsOneLess()
    {
        var m = NewStore().Create(Input(birth: "2015-03-12"));
        Assert.Equal(11, m.AgeOn(new DateOnly(2026, 10, 27)));
        Assert.Equal(10, m.AgeOn(new DateOnly(2026, 3, 11)));
    }

    [Fact]
    public void Create_Then_Update_PersistsFieldsAndRefreshesSearch()
    {
        var store = NewStore();
        var m = store.Create(Input());
        Assert.Equal("Anne", m.Contact.FirstName);
        store.Update(m.Id, Input("Léa", "Durand"));
        var updated = store.Get(m.Id)!;
        Assert.Equal("Léa Durand", updated.FullName);
        Assert.Equal(m.Code, updated.Code);
        Assert.Empty(store.Search("lucas", 10));
        Assert.Single(store.Search("lea", 10));
    }

    [Fact]
    public void List_IsSortedByLastThenFirstName()
    {
        var store = NewStore();
        store.Create(Input("Zoé", "Martin"));
        store.Create(Input("Alice", "Martin"));
        store.Create(Input("Paul", "Adam"));
        Assert.Equal(["Paul", "Alice", "Zoé"], store.List().Select(m => m.FirstName));
    }

    [Fact]
    public void Anonymize_ClearsPersonalFieldsKeepsRow()
    {
        var db = TestDb.Create(out var time);
        var store = new MemberStore(db, time);
        var m = store.Create(Input());
        using (var c = db.Open())
            c.Execute("INSERT INTO presences(member_id, day, arrived_at, status, source) VALUES(@id, '2026-10-27', '2026-10-27 17:00:00', 'Validated', 'Manual')", new { id = m.Id });

        store.Anonymize(m.Id);

        var a = store.Get(m.Id)!;
        Assert.Equal(m.Code, a.Code);
        Assert.Null(a.LastName);
        Assert.Null(a.FirstName);
        Assert.Null(a.BirthDate);
        Assert.Null(a.Phone);
        Assert.Null(a.Email);
        Assert.Null(a.Comment);
        Assert.Equal(NoContact, a.Contact);
        Assert.NotNull(a.AnonymizedAt);
        Assert.Equal($"Membre anonymisé #{m.Id}", a.FullName);
        Assert.Null(a.AgeOn(new DateOnly(2026, 10, 27)));
        using var c2 = db.Open();
        Assert.Null(c2.ExecuteScalar<string?>("SELECT search_key FROM members WHERE id=@id", new { id = m.Id }));
        Assert.Equal(1, c2.ExecuteScalar<long>("SELECT COUNT(*) FROM presences WHERE member_id=@id", new { id = m.Id }));
        Assert.Empty(store.List());
    }

    [Fact]
    public void Delete_RemovesMemberAndPresences()
    {
        var db = TestDb.Create(out var time);
        var store = new MemberStore(db, time);
        var m = store.Create(Input());
        using var c = db.Open();
        c.Execute("INSERT INTO presences(member_id, day, arrived_at, status, source) VALUES(@id, '2026-10-27', '2026-10-27 17:00:00', 'Pending', 'Kiosk')", new { id = m.Id });

        store.Delete(m.Id);

        Assert.Null(store.Get(m.Id));
        Assert.Equal(0, c.ExecuteScalar<long>("SELECT COUNT(*) FROM presences"));
    }

    [Fact]
    public void Exists_MatchesIgnoringAccentsAndCase()
    {
        var store = NewStore();
        store.Create(Input("Inès", "Morel", "2014-05-01"));
        Assert.True(store.Exists("MOREL", "ines", new DateOnly(2014, 5, 1)));
        Assert.False(store.Exists("Morel", "Ines", new DateOnly(2014, 5, 2)));
        Assert.False(store.Exists("Morel", "Léa", new DateOnly(2014, 5, 1)));
    }
}
