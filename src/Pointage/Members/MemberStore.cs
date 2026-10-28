using Dapper;
using Microsoft.Data.Sqlite;
using Pointage.Data;

namespace Pointage.Members;

public sealed record ContactInput(string? LastName, string? FirstName, DateOnly? BirthDate, string? Phone, string? Email);

public sealed record MemberInput(string LastName, string FirstName, DateOnly BirthDate, string? Phone, string? Email, string? Comment, ContactInput Contact)
{
    public IReadOnlyList<string> Validate(DateOnly today)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(LastName)) errors.Add("Le nom est obligatoire.");
        if (string.IsNullOrWhiteSpace(FirstName)) errors.Add("Le prénom est obligatoire.");
        if (BirthDate > today) errors.Add("La date de naissance ne peut pas être dans le futur.");
        return errors;
    }
}

public sealed record Member(long Id, string Code, string? LastName, string? FirstName, DateOnly? BirthDate, string? Phone, string? Email,
    string? Comment, ContactInput Contact, DateTime CreatedAt, DateTime? AnonymizedAt)
{
    public string FullName => AnonymizedAt is null ? $"{FirstName} {LastName}" : $"Membre anonymisé #{Id}";

    // La borne n'affiche que le prénom et l'initiale du nom (jamais le nom complet).
    public string KioskLabel => string.IsNullOrEmpty(LastName) ? FirstName ?? "" : $"{FirstName} {LastName[0]}.";

    public int? AgeOn(DateOnly day)
    {
        if (BirthDate is not { } b) return null;
        var age = day.Year - b.Year;
        return day < b.AddYears(age) ? age - 1 : age;
    }
}

public sealed class MemberStore(Db db, TimeProvider time)
{
    private const string Columns =
        """
        id AS Id, code AS Code, last_name AS LastName, first_name AS FirstName, birth_date AS BirthDate, phone AS Phone, email AS Email,
        comment AS Comment, contact_last_name AS ContactLastName, contact_first_name AS ContactFirstName, contact_birth_date AS ContactBirthDate,
        contact_phone AS ContactPhone, contact_email AS ContactEmail, created_at AS CreatedAt, anonymized_at AS AnonymizedAt
        """;

    // Dapper ne remplit pas un record imbriqué (Contact) : on lit à plat puis on assemble.
    private sealed class Row
    {
        public long Id { get; set; }
        public string Code { get; set; } = "";
        public string? LastName { get; set; }
        public string? FirstName { get; set; }
        public DateOnly? BirthDate { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Comment { get; set; }
        public string? ContactLastName { get; set; }
        public string? ContactFirstName { get; set; }
        public DateOnly? ContactBirthDate { get; set; }
        public string? ContactPhone { get; set; }
        public string? ContactEmail { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? AnonymizedAt { get; set; }

        public Member ToMember() => new(Id, Code, LastName, FirstName, BirthDate, Phone, Email, Comment,
            new ContactInput(ContactLastName, ContactFirstName, ContactBirthDate, ContactPhone, ContactEmail), CreatedAt, AnonymizedAt);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static DynamicParameters Params(MemberInput i, string code) => new(new
    {
        code,
        lastName = i.LastName.Trim(), firstName = i.FirstName.Trim(), birthDate = i.BirthDate,
        phone = Clean(i.Phone), email = Clean(i.Email), comment = Clean(i.Comment),
        cLast = Clean(i.Contact.LastName), cFirst = Clean(i.Contact.FirstName), cBirth = i.Contact.BirthDate,
        cPhone = Clean(i.Contact.Phone), cEmail = Clean(i.Contact.Email),
        searchKey = SearchText.Normalize($"{i.FirstName} {i.LastName} {code}"),
    });

    public Member Create(MemberInput input)
    {
        using var c = db.Open();
        while (true)
        {
            var p = Params(input, MemberCode.New());
            p.Add("now", time.Now());
            try
            {
                var id = c.ExecuteScalar<long>(
                    """
                    INSERT INTO members(code, last_name, first_name, birth_date, phone, email, comment,
                      contact_last_name, contact_first_name, contact_birth_date, contact_phone, contact_email, search_key, created_at)
                    VALUES(@code, @lastName, @firstName, @birthDate, @phone, @email, @comment, @cLast, @cFirst, @cBirth, @cPhone, @cEmail, @searchKey, @now);
                    SELECT last_insert_rowid()
                    """, p);
                return Get(id)!;
            }
            // 2067 = SQLITE_CONSTRAINT_UNIQUE : le code tiré existe déjà, on en tire un autre.
            catch (SqliteException e) when (e.SqliteExtendedErrorCode == 2067) { }
        }
    }

    public void Update(long id, MemberInput input)
    {
        using var c = db.Open();
        var code = c.ExecuteScalar<string?>("SELECT code FROM members WHERE id=@id AND anonymized_at IS NULL", new { id });
        if (code is null) return; // un membre anonymisé ne reprend pas de données personnelles
        var p = Params(input, code);
        p.Add("id", id);
        c.Execute(
            """
            UPDATE members SET last_name=@lastName, first_name=@firstName, birth_date=@birthDate, phone=@phone, email=@email, comment=@comment,
              contact_last_name=@cLast, contact_first_name=@cFirst, contact_birth_date=@cBirth, contact_phone=@cPhone, contact_email=@cEmail,
              search_key=@searchKey
            WHERE id=@id
            """, p);
    }

    public Member? Get(long id)
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<Row>($"SELECT {Columns} FROM members WHERE id=@id", new { id })?.ToMember();
    }

    public IReadOnlyList<Member> Search(string query, int limit)
    {
        var words = SearchText.Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return [];
        var p = new DynamicParameters();
        p.Add("limit", limit);
        var where = new List<string>();
        for (var i = 0; i < words.Length; i++)
        {
            // Les mots viennent de l'utilisateur : %, _ et \ sont échappés pour ne pas servir de jokers.
            p.Add($"w{i}", "%" + words[i].Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
            where.Add($"search_key LIKE @w{i} ESCAPE '\\'");
        }
        using var c = db.Open();
        return c.Query<Row>(
            $"SELECT {Columns} FROM members WHERE anonymized_at IS NULL AND {string.Join(" AND ", where)} ORDER BY last_name COLLATE NOCASE, first_name COLLATE NOCASE LIMIT @limit", p)
            .Select(r => r.ToMember()).ToList();
    }

    public IReadOnlyList<Member> List()
    {
        using var c = db.Open();
        return c.Query<Row>($"SELECT {Columns} FROM members WHERE anonymized_at IS NULL ORDER BY last_name COLLATE NOCASE, first_name COLLATE NOCASE")
            .Select(r => r.ToMember()).ToList();
    }

    public bool Exists(string lastName, string firstName, DateOnly birthDate)
    {
        var last = SearchText.Normalize(lastName);
        var first = SearchText.Normalize(firstName);
        using var c = db.Open();
        // La date de naissance filtre déjà fortement ; la comparaison sans accents se fait en C# (SQLite ne sait pas).
        return c.Query<(string? Last, string? First)>(
            "SELECT last_name AS Last, first_name AS First FROM members WHERE birth_date=@birthDate AND anonymized_at IS NULL", new { birthDate })
            .Any(m => SearchText.Normalize(m.Last ?? "") == last && SearchText.Normalize(m.First ?? "") == first);
    }

    public void Delete(long id)
    {
        using var c = db.Open();
        c.Execute("DELETE FROM members WHERE id=@id", new { id }); // les présences partent par ON DELETE CASCADE
    }

    public void Anonymize(long id)
    {
        using var c = db.Open();
        // Le code et les présences restent : les statistiques passées ne changent pas.
        c.Execute(
            """
            UPDATE members SET last_name=NULL, first_name=NULL, birth_date=NULL, phone=NULL, email=NULL, comment=NULL,
              contact_last_name=NULL, contact_first_name=NULL, contact_birth_date=NULL, contact_phone=NULL, contact_email=NULL,
              search_key=NULL, anonymized_at=@now
            WHERE id=@id AND anonymized_at IS NULL
            """, new { id, now = time.Now() });
    }
}
