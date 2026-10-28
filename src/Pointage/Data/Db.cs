using System.Data;
using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Pointage.Data;

public sealed class Db(string path)
{
    // Ne jamais modifier une migration publiée : en ajouter une nouvelle à la fin.
    private static readonly string[] Migrations =
    [
        """
        CREATE TABLE members(id INTEGER PRIMARY KEY, code TEXT NOT NULL UNIQUE,
          last_name TEXT, first_name TEXT, birth_date TEXT, phone TEXT, email TEXT, comment TEXT,
          contact_last_name TEXT, contact_first_name TEXT, contact_birth_date TEXT, contact_phone TEXT, contact_email TEXT,
          search_key TEXT, created_at TEXT NOT NULL, anonymized_at TEXT);
        -- Pas de contrainte UNIQUE(member_id, day) : plusieurs passages par jour sont prévus (spec §5).
        CREATE TABLE presences(id INTEGER PRIMARY KEY, member_id INTEGER NOT NULL REFERENCES members(id) ON DELETE CASCADE,
          day TEXT NOT NULL, arrived_at TEXT NOT NULL, departed_at TEXT,
          status TEXT NOT NULL CHECK(status IN ('Pending','Validated','Refused')),
          source TEXT NOT NULL CHECK(source IN ('Kiosk','Manual')), decided_by INTEGER REFERENCES accounts(id), decided_at TEXT);
        CREATE INDEX ix_presences_day ON presences(day);
        CREATE INDEX ix_presences_member ON presences(member_id, day);
        CREATE TABLE accounts(id INTEGER PRIMARY KEY, username TEXT NOT NULL UNIQUE COLLATE NOCASE, password_hash TEXT NOT NULL,
          role TEXT NOT NULL CHECK(role IN ('SuperAdmin','Manager')), active INTEGER NOT NULL DEFAULT 1, created_at TEXT NOT NULL);
        CREATE TABLE audit(id INTEGER PRIMARY KEY, at TEXT NOT NULL, account_id INTEGER, action TEXT NOT NULL, member_id INTEGER, details TEXT);
        CREATE TABLE settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE kiosks(id INTEGER PRIMARY KEY, name TEXT NOT NULL, token_hash TEXT NOT NULL UNIQUE, paired_at TEXT NOT NULL, revoked_at TEXT);
        """,
    ];

    public const int SchemaVersion = 1;

    static Db()
    {
        SqlMapper.AddTypeHandler(new DateOnlyHandler());
        SqlMapper.AddTypeHandler(new TimeOnlyHandler());
    }

    public string Path { get; } = path;

    public SqliteConnection Open()
    {
        var c = new SqliteConnection($"Data Source={Path}");
        c.Open();
        // foreign_keys est propre à chaque connexion ; busy_timeout évite les « database is locked » entre requêtes concurrentes.
        c.Execute("PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000");
        return c;
    }

    public void Migrate()
    {
        // Vérifié avant Open(), qui créerait le fichier : une base neuve n'a rien à sauvegarder.
        var hadContent = File.Exists(Path) && new FileInfo(Path).Length > 0;
        using var c = Open();
        var version = c.ExecuteScalar<int>("PRAGMA user_version");
        if (version >= SchemaVersion) return;

        // Copie de secours avant de toucher à des données existantes.
        if (hadContent)
            File.Copy(Path, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!,
                $"pointage-{DateTime.Now:yyyyMMdd-HHmmss}.bak"), overwrite: true);

        for (var v = version; v < SchemaVersion; v++)
        {
            using var tx = c.BeginTransaction();
            c.Execute(Migrations[v], transaction: tx);
            // PRAGMA n'accepte pas de paramètre ; v est un entier interne, pas de risque d'injection.
            c.Execute($"PRAGMA user_version={v + 1}", transaction: tx);
            tx.Commit();
        }
    }

    private sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override void SetValue(IDbDataParameter p, DateOnly v) => p.Value = v.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        public override DateOnly Parse(object v) => DateOnly.ParseExact((string)v, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private sealed class TimeOnlyHandler : SqlMapper.TypeHandler<TimeOnly>
    {
        public override void SetValue(IDbDataParameter p, TimeOnly v) => p.Value = v.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        public override TimeOnly Parse(object v) => TimeOnly.ParseExact((string)v, "HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
