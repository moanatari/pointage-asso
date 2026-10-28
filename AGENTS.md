# Pointage

Application ASP.NET Core Razor Pages (.NET 10) de pointage des présences pour une association. Interface en français ; identifiants de code en anglais ; commentaires en français qui expliquent le *pourquoi*.

## Commandes

- `dotnet build`
- `dotnet test`
- `dotnet run --project src/Pointage`

## Règles du projet

- Aucune donnée personnelle ne quitte le poste : pas de télémétrie, seul appel réseau autorisé = API GitHub des Releases. Aucun fichier `.db`/`.sqlite`/`.bak` dans le dépôt.
- Toutes les routes sauf `/borne`, `/api/borne/*` et `/static/*` renvoient 404 hors adresse de bouclage.
- La borne ne reçoit que prénom, initiale du nom, code et `hasPresenceToday`.
- Codes membres : 6 caractères de l'alphabet `ACDEFGHJKMNPQRTUVWXY34679`.
- Mots de passe : PBKDF2-SHA256, 600 000 itérations, sel de 16 octets, 8 caractères minimum.
- Heure locale du poste ; une journée = jour calendaire local.
- Statistiques : présences validées uniquement, en jours distincts.
- Un projet applicatif (`src/Pointage`), un projet de tests (`tests/Pointage.Tests`). Pas d'interface à une seule implémentation, pas de paquet NuGet hors Tech Stack. Requêtes via Dapper.
- Enums stockées en texte (nom exact) ; passer `.ToString()` aux paramètres Dapper.

## Base de données

Une migration = une nouvelle entrée du tableau `Migrations` dans `src/Pointage/Data/Db.cs` (et `SchemaVersion` incrémenté). Ne jamais modifier une migration publiée.

## Git

`main` = production (tags `vX.Y.Z`), `dev` = intégration. Chaque tâche sur `feature/<NN>-<nom-court>` créée depuis `dev`, fusionnée par Pull Request. Jamais de commit direct sur `main` ni `dev`. À la fin de chaque tâche, mettre à jour `docs/superpowers/progress.md`.
