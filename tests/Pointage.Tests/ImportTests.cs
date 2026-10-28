using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Pointage.Accounts;
using Pointage.Audit;
using Pointage.Members;

namespace Pointage.Tests;

public class ImportTests
{
    private static readonly DateOnly Today = new(2026, 10, 27);
    private const string Header = "nom;prenom;date_naissance;telephone;email;commentaire\r\n";

    private static IReadOnlyList<ImportRow> Parse(string text, Func<string, string, DateOnly, bool>? exists = null) =>
        MemberCsvImport.Parse(text, exists ?? ((_, _, _) => false), Today);

    [Fact]
    public void Parse_ValidRow_BuildsInput()
    {
        var rows = Parse(Header + "Dupont;Lucas;12/03/2015;0600000000;a@b.fr;ok\r\n");

        var row = Assert.Single(rows);
        Assert.Equal(2, row.Line);
        Assert.Empty(row.Errors);
        Assert.False(row.Duplicate);
        Assert.Equal(new MemberInput("Dupont", "Lucas", new DateOnly(2015, 3, 12), "0600000000", "a@b.fr", "ok",
            new ContactInput(null, null, null, null, null)), row.Input);
    }

    [Fact]
    public void Parse_IsoDateAndContactColumns_AreRead()
    {
        var rows = Parse("nom;prenom;date_naissance;contact_nom;contact_prenom;contact_date_naissance;contact_telephone\n" +
                         "Dupont;Lucas;2015-03-12;Dupont;Anne;1980-01-02;0611111111\n");

        Assert.Equal(new ContactInput("Dupont", "Anne", new DateOnly(1980, 1, 2), "0611111111", null), rows[0].Input!.Contact);
    }

    [Fact]
    public void Parse_HeaderWithAccentsAndSpaces_IsRecognized()
    {
        var rows = Parse("Nom;Prénom;Date de naissance\r\nDupont;Inès;12/03/2015\r\n");

        Assert.Empty(rows[0].Errors);
        Assert.Equal("Inès", rows[0].Input!.FirstName);
        Assert.Equal(new DateOnly(2015, 3, 12), rows[0].Input!.BirthDate);
    }

    [Fact]
    public void Parse_MissingFirstName_ReportsError()
    {
        var rows = Parse(Header + "Dupont;;12/03/2015;;;\r\n");

        Assert.Equal(["Le prénom est obligatoire."], rows[0].Errors);
        Assert.False(rows[0].Duplicate);
    }

    [Theory]
    [InlineData("31/02/2015")]
    [InlineData("")]
    [InlineData("pas une date")]
    public void Parse_BadDate_ReportsError(string date)
    {
        var rows = Parse(Header + $"Dupont;Lucas;{date};;;\r\n");

        Assert.Equal(["Date de naissance invalide."], rows[0].Errors);
        Assert.Null(rows[0].Input);
    }

    [Fact]
    public void Parse_ExistingMember_IsDuplicate()
    {
        var rows = Parse(Header + "Dupont;Lucas;12/03/2015;;;\r\n", (l, f, d) => l == "Dupont" && f == "Lucas" && d == new DateOnly(2015, 3, 12));

        Assert.True(rows[0].Duplicate);
        Assert.Empty(rows[0].Errors);
    }

    [Fact]
    public void Parse_SamePersonTwiceInFile_SecondIsDuplicate()
    {
        var rows = Parse(Header + "Dupont;Inès;12/03/2015;;;\r\nDUPONT;ines;12/03/2015;;;\r\n");

        Assert.False(rows[0].Duplicate);
        Assert.True(rows[1].Duplicate);
    }

    [Fact]
    public void Parse_BlankLinesAndTrailingSemicolon_AreTolerated()
    {
        var rows = Parse("nom;prenom;date_naissance;\r\n\r\nDupont;Lucas;12/03/2015;\r\n;;;\r\n\r\nMartin;Léa;01/02/2014;\r\n");

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Empty(r.Errors));
        Assert.Equal([3, 6], rows.Select(r => r.Line));
    }

    [Fact]
    public void Parse_QuotedFieldWithSemicolon_IsOneField()
    {
        var rows = Parse(Header + "Dupont;Lucas;12/03/2015;;;\"vient le lundi; \"\"sauf\"\" vacances\"\r\n");

        Assert.Equal("vient le lundi; \"sauf\" vacances", rows[0].Input!.Comment);
    }

    [Fact]
    public void Decode_Windows1252_KeepsAccents()
    {
        // « Inès;Hélène » tel qu'enregistré par Excel en français (è = 0xE8, é = 0xE9).
        byte[] bytes = [0x49, 0x6E, 0xE8, 0x73, 0x3B, 0x48, 0xE9, 0x6C, 0xE8, 0x6E, 0x65];

        Assert.Equal("Inès;Hélène", MemberCsvImport.Decode(bytes));
    }

    [Fact]
    public void Decode_Utf8WithBom_StripsBom()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Inès;Hélène")).ToArray();

        Assert.Equal("Inès;Hélène", MemberCsvImport.Decode(bytes));
    }

    [Fact]
    public async Task ImportPage_PreviewThenConfirm_CreatesOnlyValidNewMembers()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);
        var members = app.Services.GetRequiredService<MemberStore>();
        members.Create(new MemberInput("Dupont", "Lucas", new DateOnly(2015, 3, 12), null, null, null, new ContactInput(null, null, null, null, null)));
        var csv = Header + "Dupont;Lucas;12/03/2015;;;\r\nMartin;Léa;01/02/2014;;;\r\n;Sans;01/02/2014;;;\r\n";

        // Aperçu : rien n'est créé.
        var upload = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "Upload", "membres.csv" },
            { new StringContent(AppFactory.AntiforgeryToken(await client.GetStringAsync("/Members/Import"))), "__RequestVerificationToken" },
        };
        var preview = WebUtility.HtmlDecode(await (await client.PostAsync("/Members/Import?handler=Preview", upload)).Content.ReadAsStringAsync());
        Assert.Contains("Le nom est obligatoire.", preview);
        Assert.Contains("Confirmer l'import", preview);
        Assert.Empty(members.Search("lea", 10));

        var response = await AppFactory.PostFormAsync(client, "/Members/Import", "/Members/Import?handler=Confirm", new() { ["Csv"] = csv });
        var done = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Contains("1 membre(s) importé(s)", done);
        Assert.Equal(2, members.List().Count);
        var audit = app.Services.GetRequiredService<AuditLog>().Recent(5);
        Assert.Contains(audit, a => a.Action == AuditActions.MemberImport && a.Details == "1");
    }

    [Fact]
    public async Task ImportPage_Template_HasBomAndHeader()
    {
        using var app = new AppFactory();
        var client = await app.LoggedInClientAsync(Role.Manager);

        var bytes = await client.GetByteArrayAsync("/Members/Import?handler=Template");

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal(MemberCsvImport.Template, Encoding.UTF8.GetString(bytes)[1..]);
    }
}
