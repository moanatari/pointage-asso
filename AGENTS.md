# Pointage : guide pour les agents de codage et les bénévoles

Pointage des présences d'une association (enfants, donc données de mineurs). ASP.NET Core Razor Pages, .NET 10, SQLite via Dapper, service Windows. Interface 100 % française ; identifiants de code en anglais ; commentaires en français qui expliquent le *pourquoi*. Vue d'ensemble : `README.md` et `docs/ARCHITECTURE.md`.

## Commandes

```
dotnet build
dotnet test                                                 # toute la suite (~150 tests, ~30 s)
dotnet test --filter "FullyQualifiedName~PresenceTests"     # une classe ; remplacer par un nom de test pour un seul test
dotnet run --project src/Pointage -- --Pointage:DataDir=$env:TEMP\pointage-dev   # PowerShell
```

- Sans `--Pointage:DataDir`, la base est créée dans `C:\ProgramData\Pointage` (celle d'une vraie installation) : toujours la rediriger en développement.
- L'application écoute sur `http://0.0.0.0:5080`. Premier lancement : `http://localhost:5080/` ouvre l'assistant (`/Setup`).
- Mot de passe perdu : `Pointage.exe reset-admin <identifiant>` (crée ou remet en super admin actif).
- Publier : `dotnet publish src/Pointage -c Release -o publish` (le `.exe` autonome win-x64 n'est produit qu'à la publication). L'installateur est compilé par la CI (`installer/Pointage.iss`).

## Architecture

Un projet applicatif `src/Pointage`, un projet de tests `tests/Pointage.Tests`. Pas de couches, pas d'interface à une seule implémentation.

| Dossier (`src/Pointage/`) | Rôle |
|---|---|
| `Data/` | `Db` (connexion, migrations, handlers Dapper), `Clock` (heure locale) |
| `Accounts/` | comptes responsables, hachage des mots de passe |
| `Members/` | membres, codes, recherche sans accents, import CSV (isolé dans `MemberCsvImport.cs`) |
| `Presences/` | pointages et leurs statuts ; la règle « une présence par jour » |
| `Stats/` | périodes et statistiques (présences validées, jours distincts), export CSV |
| `Kiosk/` | API de la borne et liaison par code temporaire |
| `Web/` | `LocalOnlyMiddleware`, `reset-admin`, aides sur l'utilisateur connecté |
| `Updates/` | recherche et installation des mises à jour GitHub |
| `Audit/`, `Settings/` | journal des actions, réglages |
| `Pages/` | Razor Pages du responsable (`Admin/` = super admin seulement) et de la borne (`Borne/`) |
| `wwwroot/static/` | CSS et le seul JavaScript (`kiosk.js`) |

Les stores (`MemberStore`, `PresenceStore`…) sont des singletons injectés ; une page appelle un store, puis `AuditLog`. Tests : `AppFactory` lance l'application complète sur une base temporaire, `TestDb.Create(out time)` donne une base migrée avec une horloge figée au mardi 2026-10-27 17:00.

## Conventions

- **Dapper et enums/booléens** : Dapper ne remplit pas un `record` par son constructeur pour du texte→enum ou entier→bool. Lire dans une classe privée `Row` (propriétés modifiables) puis la convertir en record (`ToAccount()`, `ToPresence()`…). Modèle : `AccountStore`.
- **Enums en texte** (nom exact en base) : passer `.ToString()` en paramètre Dapper.
- **Dates** : `Db` enregistre des handlers pour `DateOnly` (`yyyy-MM-dd`), `TimeOnly` (`HH:mm:ss`) et `DateTime` (`yyyy-MM-dd HH:mm:ss`). Dapper n'applique pas le handler `DateTime` aux paramètres : toujours obtenir l'heure par `time.Now()` / `time.Today()` (`Clock`), qui tronque à la seconde et lit l'heure locale. Ne jamais utiliser `DateTime.Now` ni l'heure UTC pour une valeur stockée.
- **Libellés français** : ne jamais afficher le nom d'une enum ou d'une action. Utiliser `PresenceStatus.Label()` et `AuditActions.Label()` ; toute nouvelle valeur reçoit son libellé.
- **Formulaires** : toujours `<form method="post" asp-page="..." asp-page-handler="...">` (tag helpers). Jamais d'attribut `action=` écrit à la main : le jeton antiforgery n'est pas ajouté et le POST échoue en 400.
- **Journal** : toute écriture faite par un responsable appelle `audit.Write(User.AccountId(), AuditActions.X, memberId, détails)`. Identifiant du membre, jamais son nom, dans le journal. Les pointages de la borne n'ont pas de compte et ne sont pas journalisés.
- **Migrations** : ajouter une entrée à la fin du tableau `Migrations` de `src/Pointage/Data/Db.cs` et incrémenter `SchemaVersion`. Ne jamais modifier une migration publiée. `Migrate()` copie la base en `.bak` avant de migrer une base existante.
- **Raccourcis assumés** : un commentaire `ponytail:` marque un choix volontairement simple, avec son plafond et la piste d'amélioration. Les lister : `grep -rn "ponytail:" src`. En ajouter un quand on coupe un coin en connaissance de cause.
- **Dépendances** : Dapper, Microsoft.Data.Sqlite, Hosting.WindowsServices, rien d'autre. Ne pas ajouter de paquet NuGet pour quelques lignes de code.
- **Texte** : fichiers `.ps1`, `.iss`, `.cmd` en CRLF et le reste en LF (`.gitattributes`) ; `.iss` et `.ps1` en UTF-8 avec BOM.

## Règles non négociables

1. **Aucune donnée personnelle ne quitte le poste** : pas de télémétrie, seul appel réseau sortant = API GitHub des Releases. Aucun fichier `.db`, `.sqlite`, `.bak` dans le dépôt (public).
2. **Pages du responsable en local uniquement** : `LocalOnlyMiddleware` répond 404 à toute adresse non bouclée, sauf `/borne`, `/api/borne/*` et `/static/*`. Toute nouvelle route hors de ces trois préfixes est donc protégée ; ne jamais élargir `IsPublicPath` sans test.
3. **Minimisation à la borne** : elle ne reçoit que prénom, initiale du nom, code et `hasPresenceToday` (type `KioskMember`). Jamais de nom complet, date de naissance ou coordonnées. Toutes les routes `/api/borne/*` (sauf `pair`) exigent un jeton de borne valide.
4. **Interface en français uniquement**, sans bibliothèque de traduction.
5. **Statistiques** : présences validées uniquement, en jours distincts. Une journée = jour calendaire local du poste.
6. Codes membres : 6 caractères de `ACDEFGHJKMNPQRTUVWXY34679`. Mots de passe : PBKDF2-SHA256, 600 000 itérations, 8 caractères minimum.
7. Pas de commentaire libre sensible dans les données : l'avertissement sous le champ commentaire doit rester.

## Git

- `main` = production, `dev` = intégration. Jamais de commit direct sur l'une ou l'autre.
- Une tâche = une branche `feature/<NN>-<nom-court>` créée depuis `dev`, fusionnée par Pull Request vers `dev`.
- Pour sortir une version : Pull Request `dev` → `main`, puis tag `vX.Y.Z` sur `main` (`git tag v1.2.3 && git push --tags`). La CI (`.github/workflows/build.yml`) lance les tests puis publie `Pointage-Setup.exe` et son `.sha256` dans une Release.
- Jamais de fusion de `main` vers `dev` : `main` ne reçoit que des fusions de `dev`, donc `dev` contient déjà tout ce qui est en production.
- Numéro de version : corrections seules → `Z` (`0.1.0` → `0.1.1`) ; nouvelle fonctionnalité → `Y` (`0.1.1` → `0.2.0`) ; `X` reste à 0 tant que l'application n'est pas jugée stable.
- La version vient du tag : la CI la passe à `dotnet publish` et à l'installateur. Ne pas modifier `<Version>` dans `Pointage.csproj`.
- Sur une Pull Request vers `main`, la CI compile aussi l'installateur : une erreur dans `installer/Pointage.iss` bloque la fusion au lieu de faire échouer la release au moment du tag.
- Le suivi du développement initial est dans `docs/superpowers/progress.md`.
