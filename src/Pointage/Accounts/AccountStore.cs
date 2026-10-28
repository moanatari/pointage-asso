using Dapper;
using Pointage.Data;

namespace Pointage.Accounts;

public enum Role { SuperAdmin, Manager }

public sealed record Account(long Id, string Username, Role Role, bool Active, DateTime CreatedAt);

public sealed class AccountStore(Db db, TimeProvider time)
{
    private const string Columns = "id AS Id, username AS Username, role AS Role, active AS Active, created_at AS CreatedAt";

    // Dapper ne convertit texte -> enum / entier -> bool que via des propriétés, pas via le constructeur d'un record.
    private sealed class Row
    {
        public long Id { get; set; }
        public string Username { get; set; } = "";
        public Role Role { get; set; }
        public bool Active { get; set; }
        public DateTime CreatedAt { get; set; }
        public Account ToAccount() => new(Id, Username, Role, Active, CreatedAt);
    }

    // Hachage factice : VerifyLogin coûte le même temps que l'identifiant existe ou non.
    private static readonly string DummyHash = Passwords.Hash("sans-importance");

    public long Create(string username, string password, Role role)
    {
        username = username.Trim();
        if (username.Length == 0) throw new ArgumentException("L'identifiant est obligatoire.");
        CheckPassword(password);
        using var c = db.Open();
        // La colonne est COLLATE NOCASE : « Marie » et « marie » sont le même identifiant.
        if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM accounts WHERE username=@username", new { username }) > 0)
            throw new ArgumentException("Cet identifiant est déjà utilisé.");
        return c.ExecuteScalar<long>(
            "INSERT INTO accounts(username, password_hash, role, active, created_at) VALUES(@username, @hash, @role, 1, @now); SELECT last_insert_rowid()",
            new { username, hash = Passwords.Hash(password), role = role.ToString(), now = time.Now() });
    }

    public Account? VerifyLogin(string username, string password)
    {
        using var c = db.Open();
        var row = c.QuerySingleOrDefault<(long Id, string Hash)?>(
            "SELECT id, password_hash FROM accounts WHERE username=@username AND active=1", new { username = username.Trim() });
        var ok = Passwords.Verify(password, row?.Hash ?? DummyHash);
        return ok && row is not null ? Get(row.Value.Id) : null;
    }

    public Account? Get(long id)
    {
        using var c = db.Open();
        return c.QuerySingleOrDefault<Row>($"SELECT {Columns} FROM accounts WHERE id=@id", new { id })?.ToAccount();
    }

    public IReadOnlyList<Account> List()
    {
        using var c = db.Open();
        return c.Query<Row>($"SELECT {Columns} FROM accounts ORDER BY username").Select(r => r.ToAccount()).ToList();
    }

    public void SetPassword(long id, string password)
    {
        CheckPassword(password);
        using var c = db.Open();
        c.Execute("UPDATE accounts SET password_hash=@hash WHERE id=@id", new { id, hash = Passwords.Hash(password) });
    }

    public void SetActive(long id, bool active)
    {
        using var c = db.Open();
        // Une seule instruction : le garde-fou « dernier super admin actif » et la mise à jour sont atomiques.
        var changed = c.Execute(
            """
            UPDATE accounts SET active=@active WHERE id=@id
              AND (@active=1 OR role<>'SuperAdmin' OR active=0
                   OR EXISTS(SELECT 1 FROM accounts o WHERE o.role='SuperAdmin' AND o.active=1 AND o.id<>@id))
            """, new { id, active = active ? 1 : 0 });
        if (changed == 0 && c.ExecuteScalar<long>("SELECT COUNT(*) FROM accounts WHERE id=@id", new { id }) > 0)
            throw new InvalidOperationException("Il doit rester au moins un super administrateur actif.");
    }

    public bool AnySuperAdmin()
    {
        using var c = db.Open();
        return c.ExecuteScalar<long>("SELECT COUNT(*) FROM accounts WHERE role='SuperAdmin' AND active=1") > 0;
    }

    // Utilisé par la commande reset-admin : récupère l'accès même si tous les comptes sont verrouillés.
    public long UpsertSuperAdmin(string username, string password)
    {
        username = username.Trim();
        using var c = db.Open();
        var id = c.QuerySingleOrDefault<long?>("SELECT id FROM accounts WHERE username=@username", new { username });
        if (id is null) return Create(username, password, Role.SuperAdmin);
        CheckPassword(password);
        c.Execute("UPDATE accounts SET password_hash=@hash, role='SuperAdmin', active=1 WHERE id=@id",
            new { id, hash = Passwords.Hash(password) });
        return id.Value;
    }

    private static void CheckPassword(string password)
    {
        if (password.Length < Passwords.MinLength)
            throw new ArgumentException($"Le mot de passe doit contenir au moins {Passwords.MinLength} caractères.");
    }
}
