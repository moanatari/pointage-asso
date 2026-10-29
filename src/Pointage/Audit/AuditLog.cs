using Dapper;
using Pointage.Data;

namespace Pointage.Audit;

public static class AuditActions
{
    public const string Validate = nameof(Validate);
    public const string Refuse = nameof(Refuse);
    public const string PresenceEdit = nameof(PresenceEdit);
    public const string PresenceDelete = nameof(PresenceDelete);
    public const string PresenceAdd = nameof(PresenceAdd);
    public const string MemberCreate = nameof(MemberCreate);
    public const string MemberUpdate = nameof(MemberUpdate);
    public const string MemberDelete = nameof(MemberDelete);
    public const string MemberAnonymize = nameof(MemberAnonymize);
    public const string MemberExport = nameof(MemberExport);
    public const string MemberImport = nameof(MemberImport);
    public const string HistoryExport = nameof(HistoryExport);
    public const string Purge = nameof(Purge);
    public const string Settings = nameof(Settings);
    public const string AccountCreate = nameof(AccountCreate);
    public const string AccountUpdate = nameof(AccountUpdate);
    public const string KioskPair = nameof(KioskPair);
    public const string KioskRevoke = nameof(KioskRevoke);
    public const string Update = nameof(Update);
    public const string Login = nameof(Login);

    // Libellés français pour l'interface : le code reste en anglais, un code inconnu s'affiche tel quel.
    public static string Label(string action) => action switch
    {
        Validate => "Validation",
        Refuse => "Refus",
        PresenceEdit => "Correction de présence",
        PresenceDelete => "Suppression de présence",
        PresenceAdd => "Ajout de présence",
        MemberCreate => "Création de membre",
        MemberUpdate => "Modification de membre",
        MemberDelete => "Suppression de membre",
        MemberAnonymize => "Anonymisation",
        MemberExport => "Export des données",
        MemberImport => "Import CSV",
        HistoryExport => "Export de l'historique",
        Purge => "Purge RGPD",
        Settings => "Réglages",
        AccountCreate => "Création de compte",
        AccountUpdate => "Modification de compte",
        KioskPair => "Liaison de borne",
        KioskRevoke => "Révocation de borne",
        Update => "Mise à jour",
        Login => "Connexion",
        _ => action,
    };
}

public sealed record AuditEntry(long Id, DateTime At, long? AccountId, string Action, long? MemberId, string? Details);

public sealed class AuditLog(Db db, TimeProvider time)
{
    public void Write(long? accountId, string action, long? memberId = null, string? details = null)
    {
        using var c = db.Open();
        c.Execute("INSERT INTO audit(at, account_id, action, member_id, details) VALUES(@at, @accountId, @action, @memberId, @details)",
            new { at = time.Now(), accountId, action, memberId, details });
    }

    public IReadOnlyList<AuditEntry> Recent(int limit)
    {
        using var c = db.Open();
        return c.Query<AuditEntry>(
            "SELECT id AS Id, at AS At, account_id AS AccountId, action AS Action, member_id AS MemberId, details AS Details FROM audit ORDER BY id DESC LIMIT @limit",
            new { limit }).AsList();
    }

    public int DeleteOlderThan(DateTime cutoff)
    {
        using var c = db.Open();
        return c.Execute("DELETE FROM audit WHERE at < @cutoff", new { cutoff });
    }
}
