using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Pointage.Data;

namespace Pointage.Kiosk;

public sealed record Kiosk(long Id, string Name, DateTime PairedAt, DateTime? RevokedAt);

// Liaison d'une borne : le responsable émet un code court sur le poste, la borne l'échange contre un jeton durable.
// Singleton : un seul code en attente à la fois, gardé en mémoire (un redémarrage l'annule, ce qui est sans gravité).
public sealed class KioskPairing(Db db, TimeProvider time)
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    private const int MaxWrongAttempts = 5;

    private sealed record Pending(string Code, string KioskName, DateTimeOffset ExpiresAt)
    {
        public int WrongAttempts { get; set; }
    }

    private readonly Lock gate = new();
    private Pending? pending;

    public string NewPairingCode(string kioskName)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        lock (gate) pending = new Pending(code, kioskName.Trim(), time.GetUtcNow() + CodeLifetime);
        return code;
    }

    public string? Pair(string code)
    {
        string name;
        lock (gate)
        {
            // Horloge UTC pour l'expiration : un changement d'heure ne prolonge ni n'écourte le code.
            if (pending is null || time.GetUtcNow() > pending.ExpiresAt) { pending = null; return null; }
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(code), Encoding.UTF8.GetBytes(pending.Code)))
            {
                // Le code n'a que 6 chiffres : sans cette limite, il se devinerait depuis le réseau en quelques minutes.
                if (++pending.WrongAttempts >= MaxWrongAttempts) pending = null;
                return null;
            }
            name = pending.KioskName;
            pending = null; // usage unique
        }

        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        using var c = db.Open();
        // Seul le hachage est conservé : une copie de la base ne permet pas de se faire passer pour une borne.
        c.Execute("INSERT INTO kiosks(name, token_hash, paired_at) VALUES(@name, @hash, @now)",
            new { name, hash = Hash(token), now = time.Now() });
        return token;
    }

    public bool IsValid(string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        using var c = db.Open();
        return c.ExecuteScalar<long>("SELECT COUNT(*) FROM kiosks WHERE token_hash=@hash AND revoked_at IS NULL", new { hash = Hash(token) }) > 0;
    }

    public IReadOnlyList<Kiosk> List()
    {
        using var c = db.Open();
        return c.Query<Kiosk>("SELECT id AS Id, name AS Name, paired_at AS PairedAt, revoked_at AS RevokedAt FROM kiosks ORDER BY id DESC").AsList();
    }

    public void Revoke(long id)
    {
        using var c = db.Open();
        c.Execute("UPDATE kiosks SET revoked_at=@now WHERE id=@id AND revoked_at IS NULL", new { id, now = time.Now() });
    }

    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
