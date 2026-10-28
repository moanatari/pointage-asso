# Pointage des présences : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remplacer le cahier de présence de l'association. Les membres pointent leur arrivée sur une borne (Edge en mode kiosque) et le responsable valide, consulte et exporte les présences depuis son poste.

**Architecture:** Une seule application ASP.NET Core en Razor Pages, publiée en un `.exe` autonome et lancée comme service Windows sur le poste responsable. Les données sont dans une base SQLite placée dans `C:\ProgramData\Pointage\`. Les pages du responsable ne répondent qu'aux requêtes locales ; la borne, liée par un code temporaire, appelle quelques routes `/api/borne/*`. Les mises à jour se font en relançant l'installateur Inno Setup, téléchargé depuis les Releases GitHub.

**Tech Stack:** .NET 10 (LTS), ASP.NET Core Razor Pages, Microsoft.Data.Sqlite + Dapper, Microsoft.Extensions.Hosting.WindowsServices, xUnit, Microsoft.AspNetCore.Mvc.Testing, Microsoft.Extensions.TimeProvider.Testing (tests seulement), Inno Setup 6, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-10-28-pointage-presences-design.md`. Les tâches y renvoient par numéro de section (§).

## Global Constraints

- Aucune donnée personnelle ne quitte le poste responsable : pas de télémétrie, pas d'appel réseau hormis l'API GitHub des Releases, et aucun fichier `.db`/`.sqlite`/`.bak` dans le dépôt.
- Toutes les routes, sauf `/borne`, `/api/borne/*` et `/static/*`, renvoient **404** quand la requête ne vient pas de l'adresse de bouclage (§4).
- La borne ne reçoit que le prénom, l'initiale du nom, le code et `hasPresenceToday` : jamais la date de naissance, les coordonnées ni le nom complet (§6).
- Alphabet des codes membres : `ACDEFGHJKMNPQRTUVWXY34679`, sur 6 caractères (§5).
- Mots de passe hachés en PBKDF2-SHA256 (600 000 itérations, sel de 16 octets), au format `pbkdf2-sha256$600000$<selB64>$<hashB64>`. Longueur minimale : 8 caractères.
- Heure locale du poste. Une « journée » correspond au jour calendaire local.
- Les statistiques ne comptent que les présences **validées**, en **jours distincts** (§5).
- Interface en français uniquement. Identifiants de code en anglais, commentaires en français qui expliquent le *pourquoi*.
- Un projet applicatif (`src/Pointage`) et un projet de tests (`tests/Pointage.Tests`). Pas d'interface à une seule implémentation. Pas d'autre paquet NuGet que ceux du Tech Stack.
- Port par défaut `5080`, durée de session du responsable `30 min` glissantes, code de liaison de la borne valable `10 min`, à usage unique, invalidé après `5` essais faux.
- Réglages par défaut : conservation `12` mois, clavier de la borne affiché, bouton départ désactivé.
- Dépôt GitHub pour les mises à jour : clé de configuration `Pointage:GitHubRepo`, valeur par défaut `moanatari/user-caller`.

## Review Focus

1. **Noms accentués ou composés** (« Inès », « Jean-Pierre », « N'Diaye ») : une recherche « ines », « jean pierre » ou « ndiaye » doit les trouver. Test dans la tâche 3.
2. **Double appui sur « Oui »** à la borne, ou deux requêtes simultanées : il ne doit y avoir qu'une seule présence. Test dans la tâche 4.
3. **Passage de minuit** : un pointage à 23:59 et un autre à 00:01 tombent sur deux jours différents, et la règle du jour se base sur l'heure locale. Test dans la tâche 4.
4. **CSV enregistré par Excel en français** (Windows-1252, lignes vides, `;` en fin de ligne) : l'import doit lire correctement les accents. Test dans la tâche 10.
5. **Compte désactivé dont la session est encore ouverte**, ou suppression du dernier super admin : l'accès doit être coupé à la requête suivante, et le dernier super admin actif ne doit pas pouvoir être désactivé. Tests dans les tâches 2 et 6.

---

## Structure des fichiers

```
Pointage.slnx
.gitignore                              (complété : bin/ obj/ *.db *.sqlite *.bak)
README.md  AGENTS.md  CLAUDE.md         (CLAUDE.md = « @AGENTS.md »)
src/Pointage/
  Pointage.csproj                       net10.0, Version 0.1.0
  Program.cs                            composition, middlewares, commande reset-admin
  appsettings.json                      Urls, Pointage:DataDir, Pointage:GitHubRepo
  Data/Db.cs                            connexion, migrations (PRAGMA user_version), .bak avant migration
  Data/Clock.cs                         heure locale depuis TimeProvider
  Accounts/Passwords.cs  Accounts/AccountStore.cs
  Audit/AuditLog.cs
  Settings/SettingsStore.cs
  Members/MemberCode.cs  Members/SearchText.cs  Members/MemberStore.cs  Members/MemberCsvImport.cs
  Presences/PresenceStore.cs
  Stats/Periods.cs  Stats/StatsService.cs  Stats/StatsCsv.cs
  Kiosk/KioskPairing.cs  Kiosk/KioskApi.cs
  Updates/UpdateService.cs  Updates/UpdateCheckWorker.cs
  Web/LocalOnlyMiddleware.cs  Web/UserExtensions.cs  Web/ResetAdmin.cs
  Pages/…                               (Razor Pages, détaillées par tâche)
  wwwroot/static/site.css  wwwroot/static/kiosk.js  wwwroot/static/kiosk.css
tests/Pointage.Tests/
  Pointage.Tests.csproj
  TestDb.cs                             base temporaire + FakeTimeProvider
  AppFactory.cs                         WebApplicationFactory avec IP distante réglable
  *Tests.cs                             un fichier par tâche
installer/Pointage.iss
scripts/borne-kiosque.ps1
.github/workflows/build.yml
docs/ARCHITECTURE.md  docs/borne.md  docs/rgpd/registre-des-traitements.md  docs/rgpd/information-familles.md
```

---

### Task 1 : Solution, base de données et migrations

**Files:**
- Create: `Pointage.slnx`, `src/Pointage/Pointage.csproj`, `src/Pointage/Program.cs` (minimal pour l'instant), `src/Pointage/appsettings.json`, `src/Pointage/Data/Db.cs`, `src/Pointage/Data/Clock.cs`, `tests/Pointage.Tests/Pointage.Tests.csproj`, `tests/Pointage.Tests/TestDb.cs`, `AGENTS.md`, `CLAUDE.md`
- Modify: `.gitignore`
- Test: `tests/Pointage.Tests/DbTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class Db(string path)` avec `string Path`, `SqliteConnection Open()` (connexion ouverte, `PRAGMA foreign_keys=ON`, `busy_timeout=5000`), `void Migrate()`, `const int SchemaVersion = 1`. Le constructeur statique enregistre des `SqlMapper.TypeHandler` Dapper pour `DateOnly` (`yyyy-MM-dd`) et `TimeOnly` (`HH:mm:ss`).
  - `public static class Clock { DateTime Now(this TimeProvider t); DateOnly Today(this TimeProvider t); }` (heure locale).
  - `TestDb.Create(out FakeTimeProvider time)` : `Db` dans un fichier temporaire, déjà migré, avec une horloge fixée au **mardi 2026-10-27 17:00** heure locale (`time.SetLocalTimeZone(TimeZoneInfo.Utc)`).
  - Les enums sont stockées en **texte, avec leur nom exact** (`"Pending"`, `"SuperAdmin"`…) ; on passe `.ToString()` aux paramètres Dapper.

- [ ] **Step 1 : créer la solution et les projets**

`dotnet new sln --format slnx -n Pointage`, `dotnet new web -o src/Pointage`, `dotnet new xunit -o tests/Pointage.Tests`, références et paquets du Tech Stack. Dans `appsettings.json` :

```json
{ "Urls": "http://0.0.0.0:5080",
  "Pointage": { "DataDir": "", "GitHubRepo": "moanatari/user-caller" } }
```

Un `DataDir` vide signifie `%ProgramData%\Pointage`.

- [ ] **Step 2 : écrire les tests qui doivent échouer**

```csharp
[Fact] public void Migrate_FreshDb_SetsUserVersionAndCreatesTables()
// user_version == Db.SchemaVersion ; les tables members, presences, accounts, audit, settings, kiosks existent
[Fact] public void Migrate_Twice_IsIdempotent()
[Fact] public void Migrate_OldDb_WritesTimestampedBackupFirst()
// base créée avec user_version=0 et une table factice -> après Migrate, un fichier "pointage-*.bak" existe à côté
[Fact] public void Migrate_FreshDb_WritesNoBackup()
```

- [ ] **Step 3 : lancer les tests et vérifier qu'ils échouent**

Run : `dotnet test` · Expected : échec à la compilation (`Db` n'existe pas).

- [ ] **Step 4 : implémenter `Db` avec la migration 1**

Les migrations sont un tableau de chaînes SQL indexé par version. On applique chaque migration `> user_version` dans une transaction, puis on fixe le `user_version`. On fait d'abord une copie `pointage-yyyyMMdd-HHmmss.bak` quand le fichier existe et que `0 < user_version < SchemaVersion`. Schéma de la version 1 :

```sql
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
```

- [ ] **Step 5 : lancer les tests et vérifier qu'ils passent**

Run : `dotnet test` · Expected : 4 tests réussis.

- [ ] **Step 6 : écrire `AGENTS.md`, `CLAUDE.md` et compléter `.gitignore`**

`AGENTS.md` contient les commandes (`dotnet build`, `dotnet test`, `dotnet run --project src/Pointage`), les Global Constraints ci-dessus en version courte, et la règle « une migration = une nouvelle entrée du tableau, ne jamais modifier une migration publiée ». `CLAUDE.md` contient uniquement `@AGENTS.md`.

- [ ] **Step 7 : commit**

```bash
git add -A && git commit -m "feat: solution, base SQLite et migrations"
```

---

### Task 2 : Comptes, mots de passe, journal et réglages

**Files:**
- Create: `src/Pointage/Accounts/Passwords.cs`, `src/Pointage/Accounts/AccountStore.cs`, `src/Pointage/Audit/AuditLog.cs`, `src/Pointage/Settings/SettingsStore.cs`
- Test: `tests/Pointage.Tests/AccountTests.cs`, `tests/Pointage.Tests/SettingsAndAuditTests.cs`

**Interfaces:**
- Consumes: `Db`, `Clock`.
- Produces:
  - `public enum Role { SuperAdmin, Manager }`
  - `public sealed record Account(long Id, string Username, Role Role, bool Active, DateTime CreatedAt)`
  - `public static class Passwords { const int MinLength = 8; string Hash(string password); bool Verify(string password, string hash); }` (comparaison en temps constant avec `CryptographicOperations.FixedTimeEquals`)
  - `public sealed class AccountStore(Db db, TimeProvider time)` :
    - `long Create(string username, string password, Role role)` : lève `ArgumentException` si l'identifiant est déjà pris ou si le mot de passe est trop court ;
    - `Account? VerifyLogin(string username, string password)` : renvoie `null` si l'identifiant ou le mot de passe est faux, ou si le compte est inactif ;
    - `Account? Get(long id)`, `IReadOnlyList<Account> List()`, `void SetPassword(long id, string password)` ;
    - `void SetActive(long id, bool active)` : lève `InvalidOperationException` si plus aucun super admin actif ne resterait ;
    - `bool AnySuperAdmin()` ;
    - `long UpsertSuperAdmin(string username, string password)` : utilisé par `reset-admin`, crée le compte ou le réactive et le passe super admin.
  - `public sealed record AuditEntry(long Id, DateTime At, long? AccountId, string Action, long? MemberId, string? Details)`
  - `public sealed class AuditLog(Db db, TimeProvider time)` : `void Write(long? accountId, string action, long? memberId = null, string? details = null)`, `IReadOnlyList<AuditEntry> Recent(int limit)`, `int DeleteOlderThan(DateTime cutoff)`. Les actions sont des constantes de `public static class AuditActions` : `Validate, Refuse, PresenceEdit, PresenceDelete, PresenceAdd, MemberCreate, MemberUpdate, MemberDelete, MemberAnonymize, MemberExport, MemberImport, Purge, Settings, AccountCreate, AccountUpdate, KioskPair, KioskRevoke, Update, Login`.
  - `public sealed record AppSettings(string OrganizationName, int RetentionMonths, bool KioskKeyboard, bool KioskDeparture)`, avec `AppSettings.Default = new("", 12, true, false)`.
  - `public sealed class SettingsStore(Db db)` : `AppSettings Get()`, `void Save(AppSettings s)`.

- [ ] **Step 1 : écrire les tests qui doivent échouer**

```csharp
[Fact] public void Hash_ThenVerify_RoundTrips()            // Verify("secret12", Hash("secret12")) == true ; Verify("autre", …) == false
[Fact] public void Hash_HasExpectedFormat()                 // StartsWith("pbkdf2-sha256$600000$")
[Fact] public void Create_DuplicateUsernameIgnoringCase_Throws()  // "Marie" puis "marie" -> ArgumentException
[Fact] public void Create_ShortPassword_Throws()            // "1234567" -> ArgumentException
[Fact] public void VerifyLogin_InactiveAccount_ReturnsNull()
[Fact] public void SetActive_LastActiveSuperAdmin_Throws()  // Review Focus 5
[Fact] public void SetActive_SuperAdminWhenAnotherExists_Works()
[Fact] public void UpsertSuperAdmin_ExistingManager_BecomesActiveSuperAdmin()
[Fact] public void Settings_EmptyDb_ReturnsDefaults()       // RetentionMonths == 12, KioskKeyboard == true, KioskDeparture == false
[Fact] public void Settings_SaveThenGet_RoundTrips()
[Fact] public void Audit_DeleteOlderThan_RemovesOnlyOldEntries()
```

- [ ] **Step 2 : lancer les tests et vérifier qu'ils échouent** · Run : `dotnet test` · Expected : échec à la compilation.

- [ ] **Step 3 : implémenter les quatre classes listées dans Interfaces**

Les réglages sont stockés dans la table `settings`, une ligne par propriété (`organization_name`, `retention_months`, `kiosk_keyboard`, `kiosk_departure`).

- [ ] **Step 4 : lancer les tests et vérifier qu'ils passent** · Run : `dotnet test` · Expected : tous réussis.

- [ ] **Step 5 : commit** · `git commit -am "feat: comptes, mots de passe, journal et réglages"` (après `git add`).

---

### Task 3 : Membres (codes, recherche, fiche, anonymisation)

**Files:**
- Create: `src/Pointage/Members/MemberCode.cs`, `src/Pointage/Members/SearchText.cs`, `src/Pointage/Members/MemberStore.cs`
- Test: `tests/Pointage.Tests/MemberTests.cs`

**Interfaces:**
- Consumes: `Db`, `Clock`.
- Produces:
  - `public static class MemberCode { const string Alphabet = "ACDEFGHJKMNPQRTUVWXY34679"; const int Length = 6; string New(); string Normalize(string input); }`. `New` utilise `RandomNumberGenerator.GetItems` ; `Normalize` supprime les espaces et met en majuscules.
  - `public static class SearchText { string Normalize(string s); }` : supprime les accents (décomposition `FormD` puis retrait des `NonSpacingMark`), met en minuscules, remplace `-`, `'` et `’` par rien et ramène les espaces multiples à un seul.
  - `public sealed record ContactInput(string? LastName, string? FirstName, DateOnly? BirthDate, string? Phone, string? Email)`
  - `public sealed record MemberInput(string LastName, string FirstName, DateOnly BirthDate, string? Phone, string? Email, string? Comment, ContactInput Contact)` avec `IReadOnlyList<string> Validate(DateOnly today)`. Messages d'erreur exacts : `"Le nom est obligatoire."`, `"Le prénom est obligatoire."`, `"La date de naissance ne peut pas être dans le futur."`.
  - `public sealed record Member(long Id, string Code, string? LastName, string? FirstName, DateOnly? BirthDate, string? Phone, string? Email, string? Comment, ContactInput Contact, DateTime CreatedAt, DateTime? AnonymizedAt)`, avec :
    - `string FullName` (`"Prénom Nom"`, ou `"Membre anonymisé #<id>"`) ;
    - `string KioskLabel` (`"Lucas D."`) ;
    - `int? AgeOn(DateOnly day)`.
  - `public sealed class MemberStore(Db db, TimeProvider time)` :
    - `Member Create(MemberInput input)` : génère un code unique et recommence en cas de collision ;
    - `void Update(long id, MemberInput input)`, `Member? Get(long id)` ;
    - `IReadOnlyList<Member> Search(string query, int limit)` : ignore les membres anonymisés. Chaque mot normalisé de la requête doit apparaître dans `search_key = SearchText.Normalize($"{first} {last} {code}")` ;
    - `IReadOnlyList<Member> List()` : tous les membres non anonymisés, triés par nom puis prénom ;
    - `bool Exists(string lastName, string firstName, DateOnly birthDate)` : comparaison sur les valeurs normalisées ;
    - `void Delete(long id)` : supprime aussi les présences, par cascade ;
    - `void Anonymize(long id)` : met à `NULL` tous les champs personnels et `search_key`, et renseigne `anonymized_at`. Le code et les présences sont conservés.

- [ ] **Step 1 : écrire les tests qui doivent échouer**

```csharp
[Fact] public void New_Code_UsesOnlyAlphabetAndLength6()   // 1000 tirages
[Fact] public void Normalize_Code_UppercasesAndTrims()     // " k7mxq4 " -> "K7MXQ4"
[Fact] public void Validate_MissingNames_ReturnsBothMessages()
[Fact] public void Validate_FutureBirthDate_ReturnsMessage()
[Theory]                                                    // Review Focus 1
[InlineData("ines", "Inès", "Morel")]
[InlineData("jean pierre", "Jean-Pierre", "Martin")]
[InlineData("ndiaye", "Awa", "N'Diaye")]
[InlineData("MOR in", "Inès", "Morel")]
public void Search_IgnoresAccentsCaseAndPunctuation(string q, string first, string last)
[Fact] public void Search_ByCode_FindsMember()             // recherche "k7mx" sur le code du membre
[Fact] public void Search_ExcludesAnonymized()
[Fact] public void KioskLabel_IsFirstNameAndInitial()      // "Lucas D."
[Fact] public void AgeOn_BeforeBirthday_IsOneLess()        // né le 2015-03-12 -> 11 ans le 2026-10-27, 10 ans le 2026-03-11
[Fact] public void Anonymize_ClearsPersonalFieldsKeepsRow()
[Fact] public void Exists_MatchesIgnoringAccentsAndCase()
```

- [ ] **Step 2 : lancer les tests et vérifier qu'ils échouent** · Run : `dotnet test --filter MemberTests` · Expected : échec à la compilation.

- [ ] **Step 3 : implémenter les classes listées dans Interfaces**

- [ ] **Step 4 : lancer les tests et vérifier qu'ils passent** · Run : `dotnet test --filter MemberTests` · Expected : tous réussis.

- [ ] **Step 5 : commit** · `git add -A && git commit -m "feat: membres, codes et recherche"`

---

### Task 4 : Présences (pointage, validation, corrections)

**Files:**
- Create: `src/Pointage/Presences/PresenceStore.cs`
- Test: `tests/Pointage.Tests/PresenceTests.cs`

**Interfaces:**
- Consumes: `Db`, `Clock`, `MemberStore` (dans les tests, pour créer des membres).
- Produces:
  - `public enum PresenceStatus { Pending, Validated, Refused }`
  - `public enum PresenceSource { Kiosk, Manual }`
  - `public enum CheckInResult { Created, AlreadyToday }`
  - `public sealed record Presence(long Id, long MemberId, DateOnly Day, DateTime ArrivedAt, DateTime? DepartedAt, PresenceStatus Status, PresenceSource Source, long? DecidedBy, DateTime? DecidedAt)`
  - `public sealed record PresenceRow(Presence Presence, string MemberName, int? Age, string? DecidedByName)`
  - `public sealed class PresenceStore(Db db, TimeProvider time)` :
    - `CheckInResult CheckIn(long memberId)` : depuis la borne, heure actuelle, statut `Pending` ;
    - `long AddManual(long memberId, DateTime arrivedAt, long accountId)` : statut `Validated`, `decided_by = accountId`. Lève `InvalidOperationException("Ce membre a déjà une présence ce jour-là.")` si la règle du jour l'interdit ;
    - `bool Depart(long memberId)` : renseigne `departed_at` sur la dernière présence non refusée du jour qui n'a pas encore de départ ; renvoie `false` s'il n'y en a pas ;
    - `void Decide(long presenceId, PresenceStatus status, long accountId)` : sert aussi aux corrections (validé ↔ refusé) ;
    - `int ValidateAllPendingToday(long accountId)` ;
    - `void UpdateTimes(long presenceId, DateTime arrivedAt, DateTime? departedAt)` : met aussi à jour `day` ;
    - `void Delete(long presenceId)`, `Presence? Get(long id)` ;
    - `IReadOnlyList<PresenceRow> PendingToday()` (ordre d'arrivée décroissant), `IReadOnlyList<PresenceRow> ValidatedToday()` ;
    - `IReadOnlyList<PresenceRow> ForMember(long memberId, int limit)`, `IReadOnlyList<PresenceRow> ForPeriod(DateOnly from, DateOnly to)` (tous les statuts, pour l'historique) ;
    - `bool HasPresenceToday(long memberId)` (non refusée).
  - **La règle du jour se trouve à un seul endroit** : `private static bool AllowsNewPresence(SqliteConnection c, SqliteTransaction tx, long memberId, DateOnly day)` renvoie vrai s'il n'existe aucune présence non refusée ce jour-là. À marquer avec `// ponytail: une présence par jour ; pour plusieurs passages (spec §5), ne garder que le refus d'une présence encore ouverte.`
  - `CheckIn` et `AddManual` font la vérification et l'insertion dans **une seule transaction** (`BeginTransaction()`, qui produit un `BEGIN IMMEDIATE` avec Microsoft.Data.Sqlite).

- [ ] **Step 1 : écrire les tests qui doivent échouer**

```csharp
[Fact] public void CheckIn_First_CreatesPending()
[Fact] public void CheckIn_Twice_ReturnsAlreadyToday()                // 1 seule ligne
[Fact] public void CheckIn_AfterRefused_CreatesNew()
[Fact] public void CheckIn_Concurrent_CreatesExactlyOne()             // Review Focus 2 : 8 appels en Parallel.For -> 1 Created, 7 AlreadyToday
[Fact] public void CheckIn_2359Then0001_AreTwoDays()                  // Review Focus 3 : avancer le FakeTimeProvider de 2 minutes
[Fact] public void AddManual_PastDay_IsValidated()
[Fact] public void AddManual_DayAlreadyTaken_Throws()
[Fact] public void Decide_RefuseThenValidate_KeepsLastDecisionAndAuthor()
[Fact] public void ValidateAllPendingToday_IgnoresYesterday()
[Fact] public void Depart_SetsDepartedAtOnTodaysPresence()
[Fact] public void Depart_NoPresence_ReturnsFalse()
[Fact] public void UpdateTimes_MovesDay()
[Fact] public void PendingToday_IncludesNameAndAge()                  // MemberName "Lucas Dupont", Age 11
```

- [ ] **Step 2 : lancer les tests et vérifier qu'ils échouent** · Run : `dotnet test --filter PresenceTests` · Expected : échec à la compilation.

- [ ] **Step 3 : implémenter `PresenceStore`**

- [ ] **Step 4 : lancer les tests et vérifier qu'ils passent** · Run : `dotnet test --filter PresenceTests` · Expected : tous réussis.

- [ ] **Step 5 : commit** · `git add -A && git commit -m "feat: présences, validation et corrections"`

---

### Task 5 : Statistiques et périodes

**Files:**
- Create: `src/Pointage/Stats/Periods.cs`, `src/Pointage/Stats/StatsService.cs`, `src/Pointage/Stats/StatsCsv.cs`
- Test: `tests/Pointage.Tests/StatsTests.cs`

**Interfaces:**
- Consumes: `Db`, `Clock`, `PresenceStore` et `MemberStore` (dans les tests).
- Produces:
  - `public readonly record struct Period(DateOnly From, DateOnly To)` (bornes incluses)
  - `public static class Periods` :
    - `Period Yesterday(DateOnly today)`, `Period ThisWeek(DateOnly today)` (du lundi à aujourd'hui), `Period LastWeek(DateOnly today)` (du lundi au dimanche) ;
    - `Period ThisMonth(DateOnly today)` (du 1er à aujourd'hui), `Period LastMonth(DateOnly today)` ;
    - `DateOnly SchoolYearStart(DateOnly today)` : le 1er septembre de l'année en cours si le mois est septembre ou plus tard, sinon celui de l'année précédente.
  - `public sealed record DayCount(DateOnly Day, int Members)`
  - `public sealed record MemberCount(long MemberId, string FullName, string Code, int Days)`
  - `public sealed record PeriodStats(Period Period, IReadOnlyList<DayCount> PerDay, int TotalPresences, int DistinctMembers, IReadOnlyList<MemberCount> PerMember)` :
    - `PerDay` liste uniquement les jours qui ont au moins une présence ;
    - `TotalPresences` est la somme des couples membre-jour ;
    - `PerMember` est trié par `Days` décroissant, puis par nom.
  - `public sealed record MemberStats(DateTime? LastVisit, int DaysThisMonth, int DaysThisSchoolYear, IReadOnlyDictionary<DayOfWeek, int> PerWeekday, TimeOnly? UsualArrival)`. `UsualArrival` est la médiane de la première arrivée de chaque jour ; pour un nombre pair, on prend l'élément inférieur.
  - `public sealed class StatsService(Db db, TimeProvider time)` : `PeriodStats ForPeriod(Period p)`, `MemberStats ForMember(long memberId)`.
  - `public static class StatsCsv { string ForPeriod(PeriodStats s); }` :
    - BOM UTF-8, séparateur `;`, fins de ligne `\r\n` ;
    - en-tête `Nom;Code;Jours de présence` ;
    - les valeurs qui contiennent `;`, `"` ou un retour à la ligne sont entourées de guillemets, et les `"` internes sont doublés.
  - **Seules les présences `Validated` comptent, en jours distincts.**

- [ ] **Step 1 : écrire les tests qui doivent échouer**

```csharp
[Fact] public void Periods_LastWeek_FromTuesday20261027_Is19To25()
[Fact] public void Periods_ThisWeek_FromTuesday20261027_Is26To27()
[Fact] public void Periods_LastMonth_FromOctober_IsSeptember()
[Fact] public void SchoolYearStart_InOctober2026_Is20260901() ; [Fact] public void SchoolYearStart_InMarch2027_Is20260901()
[Fact] public void ForPeriod_CountsOnlyValidated()                     // 1 Pending + 1 Refused + 1 Validated -> TotalPresences 1
[Fact] public void ForPeriod_TwoPassagesSameDay_CountOnce()            // insérer 2 lignes validées le même jour via SQL direct -> Days 1
[Fact] public void ForPeriod_PerMemberOrderedByDaysThenName()
[Fact] public void ForMember_PerWeekdayAndUsualArrival()               // lun 16:50, lun 17:00, mar 17:10 -> Monday 2, Tuesday 1, UsualArrival 17:00
[Fact] public void ForMember_NoPresence_LastVisitNull()
[Fact] public void Csv_StartsWithBomAndEscapesSemicolon()              // nom "Dupont; Jr" -> "\"Dupont; Jr\""
```

- [ ] **Step 2 : lancer les tests et vérifier qu'ils échouent** · Run : `dotnet test --filter StatsTests` · Expected : échec à la compilation.

- [ ] **Step 3 : implémenter les trois fichiers** (agrégations en SQL : `COUNT(DISTINCT member_id)` par `day`, etc.)

- [ ] **Step 4 : lancer les tests et vérifier qu'ils passent** · Run : `dotnet test --filter StatsTests` · Expected : tous réussis.

- [ ] **Step 5 : commit** · `git add -A && git commit -m "feat: statistiques et export CSV"`

---

### Task 6 : Application web : accès local, assistant d'installation, connexion, `reset-admin`

**Files:**
- Create: `src/Pointage/Web/LocalOnlyMiddleware.cs`, `src/Pointage/Web/UserExtensions.cs`, `src/Pointage/Web/ResetAdmin.cs`, `src/Pointage/Pages/_ViewImports.cshtml`, `src/Pointage/Pages/_ViewStart.cshtml`, `src/Pointage/Pages/Shared/_Layout.cshtml`, `src/Pointage/Pages/Setup.cshtml(.cs)`, `src/Pointage/Pages/Login.cshtml(.cs)`, `src/Pointage/Pages/Logout.cshtml(.cs)`, `src/Pointage/Pages/Index.cshtml(.cs)` (vide pour l'instant), `src/Pointage/wwwroot/static/site.css`, `tests/Pointage.Tests/AppFactory.cs`
- Modify: `src/Pointage/Program.cs`
- Test: `tests/Pointage.Tests/WebTests.cs`

**Interfaces:**
- Consumes: toutes les classes des tâches 1 à 5.
- Produces:
  - `Program.cs` :
    - si `args is ["reset-admin", var user]`, lance `ResetAdmin.Run(db, user)`, puis sort ;
    - sinon, `UseWindowsService()` ;
    - lit `Pointage:DataDir` (vide = `%ProgramData%\Pointage`), crée le dossier et migre la base ;
    - enregistre en singletons `Db`, `TimeProvider.System` et toutes les classes Store/Service ;
    - authentification par cookie (`pointage_auth`, `ExpireTimeSpan = 30 min`, `SlidingExpiration`, `LoginPath = /Login`), politique `"SuperAdmin"` ;
    - conventions Razor : `AuthorizeFolder("/")`, `AuthorizeFolder("/Admin", "SuperAdmin")`, `AllowAnonymousToPage("/Login")`, `AllowAnonymousToPage("/Setup")`, `AllowAnonymousToFolder("/Borne")` ;
    - ajoute `public partial class Program;` pour les tests.
  - `LocalOnlyMiddleware` :
    - `public static bool IsPublicPath(PathString p)` : `/borne`, `/api/borne` et `/static` ;
    - une requête non publique dont `RemoteIpAddress` est `null` ou non-loopback reçoit un 404 ;
    - quand `!accounts.AnySuperAdmin()`, toute requête non publique autre que `/Setup` est redirigée vers `/Setup`.
  - Cookie : l'événement `OnValidatePrincipal` recharge le compte et rejette la session (`RejectPrincipal` + `SignOutAsync`) si le compte est inactif.
  - `public static class UserExtensions { long AccountId(this ClaimsPrincipal u); }` (claim `NameIdentifier`). Le rôle est porté par le claim `Role`.
  - `/Setup` : formulaire (nom de la structure, identifiant, mot de passe ×2, conservation en mois, clavier de la borne). Il renvoie 404 si un super admin existe déjà. Une fois validé, il crée le compte, enregistre les réglages, connecte l'utilisateur et affiche `http://<Environment.MachineName>:5080/borne`.
  - `ResetAdmin.Run(Db db, string username)` lit le mot de passe deux fois sans l'afficher (`Console.ReadKey(true)`), puis appelle `UpsertSuperAdmin`.
  - `AppFactory(string? remoteIp = "127.0.0.1")` : `WebApplicationFactory<Program>` avec un `DataDir` temporaire et un `IStartupFilter` qui force `RemoteIpAddress`. Il expose `Services`, `CreateClient()` et `Task<HttpClient> LoggedInClientAsync(Role role)`. Pour le rôle `Manager`, il crée d'abord un super admin, sinon tout redirige vers `/Setup` ; ensuite il crée le compte et se connecte.
  - `_Layout` : barre de navigation « Accueil | Membres | Historique | Réglages (super admin uniquement) | Déconnexion ».

- [ ] **Step 1 : écrire les tests qui doivent échouer**

```csharp
[Fact] public async Task NoSuperAdmin_RedirectsToSetup()
[Fact] public async Task Setup_WhenSuperAdminExists_Returns404()
[Fact] public async Task RemoteIp_ManagerPages_Return404()           // remoteIp "192.168.1.20" : /, /Login, /Setup, /Members, /Admin/Settings -> 404
[Fact] public async Task RemoteIp_StaticCss_IsServed()               // /static/site.css -> 200
[Fact] public async Task Login_ValidCredentials_ReachesHome()
[Fact] public async Task DeactivatedAccount_LiveCookie_IsSignedOut() // Review Focus 5 : connecté, SetActive(false) via Services, GET / -> redirection /Login
```

- [ ] **Step 2 : lancer les tests et vérifier qu'ils échouent** · Run : `dotnet test --filter WebTests` · Expected : échec.

- [ ] **Step 3 : implémenter `Program.cs`, le middleware, les pages Setup, Login et Logout, le layout et `ResetAdmin`**

La connexion réussie est inscrite au journal (`AuditActions.Login`). Les formulaires utilisent l'antiforgery par défaut des Razor Pages.

- [ ] **Step 4 : lancer les tests et vérifier qu'ils passent** · Run : `dotnet test` · Expected : tous réussis.

- [ ] **Step 5 : vérifier à la main**

Run : `dotnet run --project src/Pointage`, puis ouvrir `http://localhost:5080`. Expected : l'assistant s'affiche ; après validation, la page d'accueil apparaît.

- [ ] **Step 6 : commit** · `git add -A && git commit -m "feat: accès local, assistant d'installation et connexion"`

---

### Task 7 : Borne (liaison, API, page kiosque)

**Files:**
- Create: `src/Pointage/Kiosk/KioskPairing.cs`, `src/Pointage/Kiosk/KioskApi.cs`, `src/Pointage/Pages/Borne/Index.cshtml(.cs)`, `src/Pointage/wwwroot/static/kiosk.js`, `src/Pointage/wwwroot/static/kiosk.css`, `src/Pointage/Pages/Admin/Kiosks.cshtml(.cs)`
- Modify: `src/Pointage/Program.cs` (`app.MapKioskApi()`)
- Test: `tests/Pointage.Tests/KioskTests.cs`

**Interfaces:**
- Consumes: `MemberStore.Search`, `Member.KioskLabel`, `PresenceStore.CheckIn/Depart/HasPresenceToday`, `SettingsStore`, `AuditLog`.
- Produces:
  - `public sealed record Kiosk(long Id, string Name, DateTime PairedAt, DateTime? RevokedAt)`
  - `public sealed class KioskPairing(Db db, TimeProvider time)`, en singleton, qui garde en mémoire un seul code en attente :
    - `string NewPairingCode(string kioskName)` : 6 chiffres via `RandomNumberGenerator.GetInt32`, valable 10 min, remplace le code précédent ;
    - `string? Pair(string code)` : renvoie un jeton de 32 octets en Base64Url, ou `null` si le code est faux, expiré ou déjà utilisé. Après 5 essais faux, le code est invalidé. En base, on ne stocke que le hachage SHA-256 hexadécimal du jeton ;
    - `bool IsValid(string? token)`, `IReadOnlyList<Kiosk> List()`, `void Revoke(long id)`.
  - `KioskApi.MapKioskApi(this WebApplication app)`. Toutes les routes exigent un cookie `pointage_kiosk` valide (sinon 401), sauf `pair` :
    - `POST /api/borne/pair` avec `{ "code": "123456" }` : 200 et pose le cookie (`HttpOnly`, `SameSite=Strict`, `Expires` dans 10 ans), ou 400 ;
    - `GET /api/borne/search?q=` : moins de 2 caractères → `[]` ; sinon au plus 8 résultats `[{ "id", "label", "code", "hasPresenceToday" }]`, **aucun autre champ** ;
    - `POST /api/borne/checkin` avec `{ "memberId" }` : `{ "result": "created" | "already" }` ;
    - `POST /api/borne/depart` avec `{ "memberId" }` : `{ "ok": true | false }` ;
    - `GET /api/borne/config` : `{ "keyboard": bool, "departure": bool }`.
  - `/Borne` : page unique servie par Razor (sans données). Si le cookie est absent ou invalide, elle affiche le formulaire « Entrez le code de liaison ». Le reste est géré par `kiosk.js`.
  - `kiosk.js` (vanilla JS, sans bibliothèque) :
    - étape 1 : barre de recherche, appel de l'API à chaque frappe (250 ms d'attente), clavier à l'écran A–Z, 0–9 et effacer si `keyboard` ;
    - étape 2 : « C'est bien toi ? », avec « ✔ Oui, je suis arrivé », « ✘ Non, retour » et « Je pars » si `departure && hasPresenceToday`. Les boutons sont désactivés dès le premier appui ;
    - étape 3 : « Merci <prénom>, ton arrivée est notée ! Va voir le responsable pour qu'il te valide. » ou « Tu es déjà noté aujourd'hui. », puis retour à l'accueil après 5 s ;
    - retour à l'accueil après 30 s d'inactivité ;
    - si un `fetch` échoue, affiche « Le pointage est indisponible, signale-toi au responsable. » ; si la réponse est 401, recharge la page (retour au formulaire de liaison).
  - `/Admin/Kiosks` (super admin) : saisie du nom de la borne, puis « Lier une borne » affiche le code en grand avec son heure d'expiration ; liste des bornes avec « Révoquer ». Les liaisons et révocations sont inscrites au journal.

- [ ] **Step 1 : écrire les tests qui doivent échouer**

```csharp
[Fact] public void Pair_ValidCode_ReturnsTokenOnce()                 // second Pair(code) -> null
[Fact] public void Pair_AfterTenMinutes_ReturnsNull()
[Fact] public void Pair_FiveWrongAttempts_InvalidatesCode()          // puis le bon code -> null
[Fact] public void Revoke_MakesTokenInvalid()
[Fact] public async Task Search_WithoutToken_Returns401()            // remoteIp "192.168.1.30"
[Fact] public async Task Search_ReturnsOnlyMinimalFields()           // JSON : clés exactement id,label,code,hasPresenceToday ; label "Lucas D."
[Fact] public async Task Checkin_TwiceViaApi_ReturnsAlready()
[Fact] public async Task BornePage_FromRemoteIp_IsServed()           // GET /borne -> 200
```

- [ ] **Step 2 : lancer les tests et vérifier qu'ils échouent** · Run : `dotnet test --filter KioskTests` · Expected : échec.

- [ ] **Step 3 : implémenter `KioskPairing`, `KioskApi`, la page `/Borne` et la page `/Admin/Kiosks`**

- [ ] **Step 4 : lancer les tests et vérifier qu'ils passent** · Run : `dotnet test` · Expected : tous réussis.

- [ ] **Step 5 : vérifier à la main**

Lier une borne depuis `http://localhost:5080/Admin/Kiosks`, puis ouvrir `http://<IP du poste>:5080/borne` dans un autre navigateur. Saisir le code, chercher un membre et pointer. Expected : la présence apparaît en base avec le statut `Pending`, et le clavier à l'écran fonctionne.

- [ ] **Step 6 : commit** · `git add -A && git commit -m "feat: borne, liaison et pointage"`

---

### Task 8 : Écrans du responsable (accueil, membres, fiche, présences)

**Files:**
- Create :
  - `src/Pointage/Pages/Index.cshtml(.cs)` (remplace la page vide)
  - `src/Pointage/Pages/Presences/Add.cshtml(.cs)`, `src/Pointage/Pages/Presences/Edit.cshtml(.cs)`
  - `src/Pointage/Pages/Members/Index.cshtml(.cs)`, `Members/Edit.cshtml(.cs)`, `Members/Details.cshtml(.cs)`, `Members/Card.cshtml(.cs)`, `Members/Export.cshtml(.cs)`
- Test: `tests/Pointage.Tests/ManagerPagesTests.cs`

**Interfaces:**
- Consumes: `PresenceStore`, `MemberStore`, `StatsService.ForMember`, `AuditLog`, `UserExtensions.AccountId`.
- Produces les pages suivantes. Chaque action qui écrit est inscrite au journal avec l'`AuditActions` correspondante.
  - `/` (Accueil) :
    - `<meta http-equiv="refresh" content="10">` ;
    - colonne « À valider (n) » : nom complet, âge et heure, avec les handlers POST `OnPostValidate(long id)`, `OnPostRefuse(long id)` et `OnPostValidateAll()` ;
    - colonne « Présents aujourd'hui (n) » : nom et heure, avec un lien vers la fiche ;
    - lien « Ajouter une présence ».
  - `/Presences/Add` :
    - recherche du membre, puis saisie de la date et de l'heure (aujourd'hui et maintenant par défaut) ;
    - en cas d'`InvalidOperationException`, le message s'affiche sur la page.
  - `/Presences/Edit/{id}` :
    - modification des heures d'arrivée et de départ, changement de statut (Validé / Refusé) et suppression ;
    - le journal enregistre l'ancienne et la nouvelle valeur dans `details`.
  - `/Members` : recherche (via `MemberStore.Search` dès 1 caractère, sinon `List()`), un tableau « Nom, Prénom, Âge, Code » et un bouton « Nouveau membre ».
  - `/Members/Edit/{id?}` :
    - formulaire de la fiche (§5) ;
    - les champs de date sont des `<input type="date">` ;
    - sous le commentaire, ce texte fixe : « Ne pas y noter de données sensibles (santé, situation familiale…). » ;
    - les erreurs de `MemberInput.Validate` s'affichent.
  - `/Members/Details/{id}` :
    - identité, âge et contact ;
    - les tuiles « Dernière venue », « Ce mois-ci », « Depuis septembre » ;
    - les barres par jour de la semaine (Lun à Ven, plus Sam/Dim seulement s'ils sont non nuls), en `div` avec une largeur en %, et l'« Heure d'arrivée habituelle » ;
    - l'historique `ForMember(id, 50)` avec le statut (✅ validé / ❌ refusé / ⏳ en attente / « non validée » pour une présence en attente d'un jour passé), l'auteur et un lien Corriger ;
    - les boutons Modifier, Imprimer la carte, Exporter les données, Anonymiser et Supprimer. Les deux derniers demandent une confirmation (`onsubmit="return confirm(…)"`).
  - `/Members/Card/{id}` : page imprimable (CSS `@media print`) avec le nom de la structure, le prénom, le nom et le code en grand.
  - `/Members/Export/{id}` : page HTML imprimable avec tous les champs et toutes les présences du membre, inscrite au journal (`MemberExport`).

- [ ] **Step 1 : écrire les tests qui doivent échouer**

```csharp
[Fact] public async Task Home_ValidatePending_MovesToPresent()      // CheckIn via Services, POST handler Validate, puis GET / contient le nom sous "Présents aujourd'hui"
[Fact] public async Task Home_Refuse_IsKeptAsRefusedAndAudited()
[Fact] public async Task CreateMember_MissingFirstName_ShowsError() // la réponse contient "Le prénom est obligatoire."
[Fact] public async Task Anonymize_RemovesFromSearch()
[Fact] public async Task Export_WritesAuditEntry()
```

Pour les POST, le helper de test lit le jeton antiforgery dans le formulaire de la page (`__RequestVerificationToken`). Ajouter `Task<HttpResponseMessage> PostFormAsync(HttpClient c, string pageUrl, string handlerUrl, Dictionary<string,string> fields)` dans `AppFactory.cs`.

- [ ] **Step 2 : lancer les tests et vérifier qu'ils échouent** · Run : `dotnet test --filter ManagerPagesTests` · Expected : échec.

- [ ] **Step 3 : implémenter les pages**

- [ ] **Step 4 : lancer les tests et vérifier qu'ils passent** · Run : `dotnet test` · Expected : tous réussis.

- [ ] **Step 5 : vérifier à la main** : créer 3 membres, pointer à la borne, puis valider et refuser. Expected : l'accueil se met à jour dans les 10 s, et la fiche affiche les statistiques.

- [ ] **Step 6 : commit** · `git add -A && git commit -m "feat: écrans du responsable"`

---

### Task 9 : Historique et statistiques par période

**Files:**
- Create: `src/Pointage/Pages/History.cshtml(.cs)`
- Test: `tests/Pointage.Tests/HistoryTests.cs`

**Interfaces:**
- Consumes: `Periods`, `StatsService.ForPeriod`, `PresenceStore.ForPeriod`, `StatsCsv.ForPeriod`.
- Produces : `/History?preset=yesterday|thisweek|lastweek|thismonth|lastmonth|custom&from=&to=`. La période par défaut est `lastweek`.
  - Les boutons de période, et deux `<input type="date">` pour `custom`. Si `from > to`, les dates sont inversées.
  - Les barres par jour, « N présences · M membres différents », et le tableau « Qui est venu » (triable par nom ou par nombre de venues via `?sort=name|days`).
  - Le détail des pointages de la période (tous les statuts) avec un lien Corriger.
  - Le handler `OnGetCsv` renvoie `File(bytes, "text/csv; charset=utf-8", "presences-<from>-<to>.csv")`.

- [ ] **Step 1 : écrire les tests qui doivent échouer**

```csharp
[Fact] public async Task History_DefaultIsLastWeek()              // le titre contient les dates du lundi au dimanche de la semaine précédente
[Fact] public async Task History_Csv_HasBomAndHeader()           // octets EF BB BF, puis "Nom;Code;Jours de présence"
[Fact] public async Task History_CustomReversedDates_AreSwapped()
```

- [ ] **Step 2 : lancer les tests et vérifier qu'ils échouent** · Run : `dotnet test --filter HistoryTests` · Expected : échec.

- [ ] **Step 3 : implémenter la page**

- [ ] **Step 4 : lancer les tests et vérifier qu'ils passent** · Run : `dotnet test` · Expected : tous réussis.

- [ ] **Step 5 : commit** · `git add -A && git commit -m "feat: historique et statistiques par période"`

---

### Task 10 : Import CSV de membres

**Files:**
- Create: `src/Pointage/Members/MemberCsvImport.cs`, `src/Pointage/Pages/Members/Import.cshtml(.cs)`
- Test: `tests/Pointage.Tests/ImportTests.cs`

**Interfaces:**
- Consumes: `MemberInput`, `MemberStore.Exists/Create`, `SearchText.Normalize`, `AuditLog`.
- Produces:
  - `public sealed record ImportRow(int Line, MemberInput? Input, IReadOnlyList<string> Errors, bool Duplicate)`
  - `public static class MemberCsvImport` :
    - `const string Template = "nom;prenom;date_naissance;telephone;email;commentaire;contact_nom;contact_prenom;contact_date_naissance;contact_telephone;contact_email\r\n"` ;
    - `string Decode(byte[] bytes)` : essaie l'UTF-8 strict (`throwOnInvalidBytes: true`, BOM retiré), et en cas d'échec passe à Windows-1252 (`Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)`, intégré au framework) ;
    - `IReadOnlyList<ImportRow> Parse(string text, Func<string, string, DateOnly, bool> exists, DateOnly today)` :
      - les en-têtes sont reconnus via `SearchText.Normalize`, ce qui accepte « Prénom » ou « Date de naissance » (les espaces, `_` et `-` sont ignorés à la comparaison) ;
      - les colonnes inconnues sont ignorées et les lignes vides sautées ;
      - un `;` final est toléré ;
      - les dates sont au format `dd/MM/yyyy` ou `yyyy-MM-dd` ;
      - les erreurs viennent de `MemberInput.Validate`, plus `"Date de naissance invalide."` ;
      - les champs entre guillemets peuvent contenir des `;` (un petit analyseur à états suffit : pas de dépendance).
    - Un commentaire en tête de fichier indique que ce fichier est volontairement isolé pour pouvoir le faire évoluer (spec §7).
  - `/Members/Import` :
    1. Lien « Télécharger le modèle » (handler `OnGetTemplate`, BOM UTF-8).
    2. Envoi du fichier : la page affiche l'aperçu (lignes OK, lignes en erreur avec leurs raisons, doublons ignorés) et garde le texte décodé dans un `<textarea hidden name="Csv">`.
    3. « Confirmer l'import » : le texte est analysé à nouveau, seules les lignes OK et non dupliquées sont créées, puis le journal enregistre `MemberImport` avec le nombre de membres créés.
    - La taille de fichier est limitée à 1 Mo.

- [ ] **Step 1 : écrire les tests qui doivent échouer**

```csharp
[Fact] public void Parse_ValidRow_BuildsInput()
[Fact] public void Parse_HeaderWithAccentsAndSpaces_IsRecognized()       // "Nom;Prénom;Date de naissance"
[Fact] public void Parse_MissingFirstName_ReportsError()
[Fact] public void Parse_BadDate_ReportsError()
[Fact] public void Parse_ExistingMember_IsDuplicate()
[Fact] public void Parse_BlankLinesAndTrailingSemicolon_AreTolerated()  // Review Focus 4
[Fact] public void Parse_QuotedFieldWithSemicolon_IsOneField()
[Fact] public void Decode_Windows1252_KeepsAccents()                    // octets 1252 de "Inès;Hélène" -> "Inès;Hélène" ; Review Focus 4
[Fact] public void Decode_Utf8WithBom_StripsBom()
```

- [ ] **Step 2 : lancer les tests et vérifier qu'ils échouent** · Run : `dotnet test --filter ImportTests` · Expected : échec.

- [ ] **Step 3 : implémenter `MemberCsvImport` et la page**

- [ ] **Step 4 : lancer les tests et vérifier qu'ils passent** · Run : `dotnet test` · Expected : tous réussis.

- [ ] **Step 5 : commit** · `git add -A && git commit -m "feat: import CSV de membres"`

---

### Task 11 : Réglages, comptes, purge RGPD et journal (super admin)

**Files:**
- Create: `src/Pointage/Pages/Admin/Settings.cshtml(.cs)`, `src/Pointage/Pages/Admin/Accounts.cshtml(.cs)`, `src/Pointage/Pages/Admin/Purge.cshtml(.cs)`, `src/Pointage/Pages/Admin/Journal.cshtml(.cs)`
- Modify: `src/Pointage/Members/MemberStore.cs` (ajouter `MembersToPurge`)
- Test: `tests/Pointage.Tests/PurgeTests.cs`

**Interfaces:**
- Consumes: `SettingsStore`, `AccountStore`, `AuditLog`, `MemberStore.Anonymize`.
- Produces:
  - `IReadOnlyList<Member> MemberStore.MembersToPurge(DateOnly today, int retentionMonths)` : membres non anonymisés dont la dernière présence non refusée (sinon `created_at`) est antérieure à `today.AddMonths(-retentionMonths)`.
  - `/Admin/Settings` : nom de la structure, durée de conservation en mois (de 1 à 120), clavier de la borne, bouton départ. Les liens mènent vers Comptes, Bornes, Purge, Journal et Mises à jour. Chaque enregistrement est inscrit au journal (`Settings`).
  - `/Admin/Accounts` :
    - liste des comptes ;
    - création (identifiant, mot de passe, rôle) ;
    - « Réinitialiser le mot de passe », « Désactiver » / « Réactiver ». L'`InvalidOperationException` levée pour le dernier super admin s'affiche avec ce message : « Impossible : il doit rester au moins un super admin actif. »
  - `/Admin/Purge` :
    - liste des membres concernés (nom et dernière venue) et bouton « Anonymiser ces N membres » ;
    - lors de la confirmation, la liste est **recalculée côté serveur**, chaque membre est anonymisé, puis `AuditLog.DeleteOlderThan(now.AddMonths(-retention))` est appelé et une entrée `Purge` est écrite avec le nombre de membres.
  - `/Admin/Journal` : les 200 dernières entrées (date, compte, action, membre via `FullName`, détails).

- [ ] **Step 1 : écrire les tests qui doivent échouer**

```csharp
[Fact] public void MembersToPurge_LastVisitOlderThanRetention_IsSelected()   // aujourd'hui 2026-10-27, conservation 12, dernière venue 2025-10-26 -> sélectionné ; 2025-10-28 -> non
[Fact] public void MembersToPurge_NeverCame_UsesCreatedAt()
[Fact] public void MembersToPurge_OnlyRefusedPresences_UsesCreatedAt()
[Fact] public void MembersToPurge_ExcludesAlreadyAnonymized()
[Fact] public async Task Purge_Confirm_AnonymizesAndAudits()
[Fact] public async Task Accounts_DeactivateLastSuperAdmin_ShowsMessage()
[Fact] public async Task Manager_AdminPages_Forbidden()              // rôle Manager : /Admin/Settings et /Admin/Purge -> 403 ou redirection AccessDenied
```

- [ ] **Step 2 : lancer les tests et vérifier qu'ils échouent** · Run : `dotnet test --filter PurgeTests` · Expected : échec.

- [ ] **Step 3 : implémenter `MembersToPurge` et les quatre pages**

- [ ] **Step 4 : lancer les tests et vérifier qu'ils passent** · Run : `dotnet test` · Expected : tous réussis.

- [ ] **Step 5 : commit** · `git add -A && git commit -m "feat: réglages, comptes, purge RGPD et journal"`

---

### Task 12 : Mises à jour depuis GitHub

**Files:**
- Create: `src/Pointage/Updates/UpdateService.cs`, `src/Pointage/Updates/UpdateCheckWorker.cs`, `src/Pointage/Pages/Admin/Update.cshtml(.cs)`
- Modify: `src/Pointage/Program.cs` (`AddHttpClient<UpdateService>`, `AddHostedService<UpdateCheckWorker>`)
- Test: `tests/Pointage.Tests/UpdateTests.cs`

**Interfaces:**
- Consumes: `IConfiguration["Pointage:GitHubRepo"]`, `Db.Path` (pour placer le dossier `updates` à côté de la base), `AuditLog`.
- Produces:
  - `public sealed record ReleaseInfo(Version Version, string SetupUrl, string Sha256Url)`
  - `public sealed class UpdateService(HttpClient http, IConfiguration config, Db db)` :
    - `static Version Current` : version de l'assembly ;
    - `ReleaseInfo? Latest { get; }` : dernière version plus récente trouvée, ou `null` ;
    - `Task CheckAsync(CancellationToken ct)` : appelle `GET https://api.github.com/repos/{repo}/releases/latest` avec le `User-Agent: Pointage`, prend les assets nommés exactement `Pointage-Setup.exe` et `Pointage-Setup.exe.sha256`, et avale les erreurs réseau (en les journalisant via `ILogger`) ;
    - `Task ApplyAsync(ReleaseInfo r, CancellationToken ct)` :
      - refuse toute URL qui ne commence pas par `https://github.com/{repo}/releases/download/` ;
      - télécharge les deux fichiers dans `<DataDir>\updates\` ;
      - vérifie le hachage, et lève `InvalidOperationException("Le fichier téléchargé est corrompu ou modifié.")` s'il ne correspond pas ;
      - lance `Pointage-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART` (`UseShellExecute = true`) ;
    - `static bool IsNewer(string tag, Version current)` : accepte `v1.2.3` ou `1.2.3` ;
    - `static bool HashMatches(Stream file, string sha256FileContent)` : le contenu peut être `<hex>` ou `<hex>  <nom>` ; la comparaison ignore la casse.
  - `UpdateCheckWorker : BackgroundService` : appelle `CheckAsync` au démarrage, puis toutes les 24 h (`PeriodicTimer`).
  - `/Admin/Update` :
    - affiche la version installée et, s'il y en a une, « Version X disponible » avec le bouton « Mettre à jour » ;
    - le bouton écrit l'entrée de journal `Update`, puis appelle `ApplyAsync` et affiche « Mise à jour en cours, la page reviendra dans une minute. » ;
    - un bouton « Vérifier maintenant » est aussi disponible.

- [ ] **Step 1 : écrire les tests qui doivent échouer**

```csharp
[Theory] [InlineData("v1.2.0","1.1.9",true)] [InlineData("1.2.0","1.2.0",false)] [InlineData("v0.9","1.0.0",false)] [InlineData("pas-une-version","1.0.0",false)]
public void IsNewer_Works(string tag, string current, bool expected)
[Fact] public void HashMatches_WithFileNameSuffix_True()
[Fact] public void HashMatches_Different_False()
[Fact] public async Task Check_ParsesGitHubJson()            // HttpMessageHandler factice qui renvoie un JSON de release avec les 2 assets -> Latest.Version == 9.9.9
[Fact] public async Task Check_NetworkError_LeavesLatestNull()
[Fact] public async Task Apply_ForeignUrl_Throws()           // SetupUrl "https://evil.example/x.exe" -> InvalidOperationException, rien n'est lancé
```

- [ ] **Step 2 : lancer les tests et vérifier qu'ils échouent** · Run : `dotnet test --filter UpdateTests` · Expected : échec.

- [ ] **Step 3 : implémenter `UpdateService`, `UpdateCheckWorker` et la page**

- [ ] **Step 4 : lancer les tests et vérifier qu'ils passent** · Run : `dotnet test` · Expected : tous réussis.

- [ ] **Step 5 : commit** · `git add -A && git commit -m "feat: mises à jour depuis GitHub"`

---

### Task 13 : Installateur, publication et script de la borne

**Files:**
- Create: `installer/Pointage.iss`, `.github/workflows/build.yml`, `scripts/borne-kiosque.ps1`
- Modify: `src/Pointage/Pointage.csproj` (`<RuntimeIdentifier>win-x64</RuntimeIdentifier>`, `<SelfContained>true</SelfContained>`, `<PublishSingleFile>true</PublishSingleFile>`, `<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>` dans un groupe conditionné à la publication)

**Interfaces:**
- Consumes: le binaire publié `Pointage.exe`, la commande `reset-admin` et le port `5080`.
- Produces:
  - `installer/Pointage.iss` :
    - `AppVersion={#MyAppVersion}` (passé par `/DMyAppVersion=`), `DefaultDirName={autopf}\Pointage`, `OutputBaseFilename=Pointage-Setup`, `PrivilegesRequired=admin` ;
    - `PrepareToInstall` : `sc.exe stop Pointage`, puis attente de l'arrêt (au plus 30 s) ;
    - `[Run]` :
      - `sc.exe create Pointage binPath= "\"{app}\Pointage.exe\"" start= auto`, dont l'échec est ignoré quand le service existe déjà ;
      - `sc.exe failure Pointage reset= 86400 actions= restart/60000` ;
      - `netsh advfirewall firewall delete rule name="Pointage"`, puis `add rule name="Pointage" dir=in action=allow protocol=TCP localport=5080 profile=private,domain` ;
      - `sc.exe start Pointage` ;
      - ouverture de `http://localhost:5080/`, avec le flag `postinstall skipifsilent` ;
    - `[UninstallRun]` : arrêt et suppression du service, puis suppression de la règle de pare-feu. **La base dans `C:\ProgramData\Pointage` est conservée.**
  - `.github/workflows/build.yml` (`runs-on: windows-latest`) :
    - à chaque push et PR : `dotnet test` ;
    - sur un tag `v*` :
      - `dotnet publish src/Pointage -c Release -o publish -p:Version=${tag#v}` ;
      - `choco install innosetup -y` ;
      - `iscc /DMyAppVersion=… /DSourceDir=publish installer/Pointage.iss` ;
      - calcul du hachage avec `(Get-FileHash Pointage-Setup.exe -Algorithm SHA256).Hash.ToLower() > Pointage-Setup.exe.sha256` ;
      - `gh release create $tag Pointage-Setup.exe Pointage-Setup.exe.sha256 --generate-notes`.
  - `scripts/borne-kiosque.ps1 -Url http://<poste>:5080/borne` : crée dans le dossier Démarrage de l'utilisateur un raccourci vers `msedge.exe --kiosk <Url> --edge-kiosk-type=fullscreen --no-first-run`. Il **n'utilise pas** le mode « navigation publique » (InPrivate), qui effacerait le cookie de liaison (spec §4).

- [ ] **Step 1 : vérifier la publication en local**

Run : `dotnet publish src/Pointage -c Release -o publish` · Expected : un `publish/Pointage.exe` unique. Lancé avec un `DataDir` temporaire (`$env:Pointage__DataDir="…"`), il répond sur le port 5080.

- [ ] **Step 2 : écrire `Pointage.iss` et le compiler en local** (si Inno Setup est installé)

Run : `iscc /DMyAppVersion=0.1.0 /DSourceDir=..\publish installer\Pointage.iss` · Expected : `installer\Output\Pointage-Setup.exe`.

- [ ] **Step 3 : tester l'installation sur une VM ou un PC Windows de test**

Expected :
- le service `Pointage` démarre automatiquement ;
- `http://localhost:5080` affiche l'assistant ;
- depuis un autre PC, `/borne` répond et `/` renvoie 404 ;
- une réinstallation silencieuse conserve la base.

- [ ] **Step 4 : écrire `build.yml` et `borne-kiosque.ps1`**

- [ ] **Step 5 : commit** · `git add -A && git commit -m "build: installateur, publication GitHub et script de la borne"`

---

### Task 14 : Documentation

**Files:**
- Create: `README.md`, `docs/ARCHITECTURE.md`, `docs/borne.md`, `docs/rgpd/registre-des-traitements.md`, `docs/rgpd/information-familles.md`
- Modify: `AGENTS.md` (le compléter si des conventions sont apparues pendant le développement)

**Interfaces:**
- Consumes: tout ce qui précède.
- Produces :
  - `README.md` (en français) :
    - ce que fait l'application ;
    - comment l'installer : télécharger `Pointage-Setup.exe`, passer l'avertissement SmartScreen (« Informations complémentaires → Exécuter quand même »), suivre l'assistant, puis lier la borne ;
    - mot de passe super admin perdu (`"C:\Program Files\Pointage\Pointage.exe" reset-admin <identifiant>` dans une console administrateur) ;
    - démarrage en développement en 5 minutes (SDK .NET 10, `dotnet run`, `dotnet test`) ;
    - publier une version (`git tag v1.2.3 && git push --tags`).
  - `docs/ARCHITECTURE.md` :
    - schéma texte des deux postes ;
    - rôle de chaque dossier de `src/Pointage` ;
    - cycle de vie d'une présence (`Pending` → `Validated` / `Refused`, corrections) ;
    - accès local et liaison de la borne ;
    - migrations ;
    - mises à jour ;
    - section « Pistes pour plus tard », reprise du §11 de la spec : sauvegardes, chiffrement SQLCipher + DPAPI, BitLocker, HTTPS, import enrichi, signature du code, plusieurs passages par jour (avec le commentaire `ponytail:` de `PresenceStore`).
  - `docs/borne.md` : préparation du PC de la borne, utilisation de `scripts/borne-kiosque.ps1`, liaison, et ce qu'il faut faire si la borne affiche à nouveau « Entrez le code de liaison ».
  - `docs/rgpd/registre-des-traitements.md` : une page à compléter (finalité, base légale, catégories de personnes dont des mineurs, données, destinataires, durée de conservation, mesures de sécurité), avec un lien vers le guide CNIL des associations.
  - `docs/rgpd/information-familles.md` : un modèle de notice pour les familles (données collectées, finalité, durée, droits, contact).

- [ ] **Step 1 : rédiger les cinq documents**

- [ ] **Step 2 : vérifier**

Suivre le README « démarrage en développement » à partir d'un clone propre (`git clone . ../pointage-check`, puis les commandes du README). Expected : l'application démarre et `dotnet test` passe.

- [ ] **Step 3 : commit** · `git add -A && git commit -m "docs: README, architecture, borne et modèles RGPD"`
