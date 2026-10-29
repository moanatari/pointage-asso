# Architecture

Un seul programme, `Pointage.exe` (ASP.NET Core Razor Pages, .NET 10), installé uniquement sur le poste du responsable. Il tourne en service Windows, donc les enfants peuvent pointer même si personne n'est connecté : les arrivées attendent d'être validées. Le programme est publié en un seul `.exe` autonome (aucun runtime .NET à installer).

## Les deux postes

```
 Poste du responsable (Windows, toujours allumé)               Borne (autre PC / tablette)
+------------------------------------------------+            +-----------------------------+
| Service Windows « Pointage »  (port 5080)      |            | Edge en mode kiosque        |
|                                                |   HTTP     | http://<poste>:5080/borne   |
|  /borne, /api/borne/*, /static/*   <-----------+------------+  (rien d'installé,          |
|     (réseau local, borne liée seulement)       |  réseau    |   rien de stocké sauf le    |
|                                                |  local     |   cookie de liaison)        |
|  toutes les autres pages (responsable)         |            +-----------------------------+
|     => 404 hors adresse de bouclage            |
|                                                |
|  C:\ProgramData\Pointage\pointage.db (SQLite)  |
+------------------------------------------------+
        ^
        | http://localhost:5080/   navigateur du responsable
```

- Les pages du responsable (connexion et assistant compris) ne répondent qu'à une requête venant de la machine elle-même : `Web/LocalOnlyMiddleware.cs` renvoie 404 aux autres. Les seules exceptions sont les préfixes `/borne`, `/api/borne` et `/static`.
- Le programme écoute sur `0.0.0.0:5080` (`appsettings.json`, clé `Urls`). Le port 5080 est aussi écrit dans `installer/Pointage.iss` (règle de pare-feu et lien d'ouverture) et dans `Pages/Setup.cshtml.cs` (adresse affichée pour la borne) : pour le changer, modifier les trois.
- La base est `pointage.db` dans `C:\ProgramData\Pointage` (ou dans `Pointage:DataDir` si renseigné). L'installateur verrouille ce dossier à SYSTEM et aux administrateurs.

## Rôle de chaque dossier de `src/Pointage`

| Dossier | Contenu |
|---|---|
| `Program.cs` | Assemblage : services, authentification par cookie, pages protégées, ordre des middlewares, commande `reset-admin` |
| `Data/` | `Db` : ouverture de connexion, migrations, handlers Dapper pour les dates. `Clock` : `Now()` / `Today()` en heure locale, tronquées à la seconde |
| `Accounts/` | `AccountStore` (comptes, rôles `SuperAdmin` / `Manager`, garde-fou « dernier super admin »), `Passwords` (PBKDF2) |
| `Members/` | `MemberStore`, `MemberCode` (code à 6 caractères), `SearchText` (recherche sans accents ni casse), `MemberCsvImport` (import, volontairement isolé car appelé à évoluer) |
| `Presences/` | `PresenceStore` : pointage, validation, corrections, règle « une présence par jour » |
| `Stats/` | `Periods` (hier, semaine, mois, année scolaire du 1er septembre), `StatsService`, `StatsCsv` (CSV `;` UTF-8 avec BOM pour Excel) |
| `Kiosk/` | `KioskApi` (routes de la borne) et `KioskPairing` (code de liaison, jetons) |
| `Audit/` | `AuditLog` et `AuditActions` : journal des actions des responsables |
| `Settings/` | `SettingsStore` : nom de la structure, durée de conservation, clavier et bouton départ de la borne |
| `Updates/` | `UpdateService` (API GitHub, téléchargement vérifié) et `UpdateCheckWorker` (au démarrage puis toutes les 24 h) |
| `Web/` | `LocalOnlyMiddleware`, `ResetAdmin`, extensions sur l'utilisateur connecté |
| `Pages/` | Razor Pages : accueil, membres, présences, historique, connexion, assistant `Setup`. `Admin/` : réglages, comptes, bornes, purge, journal, mises à jour (super admin). `Borne/` : l'écran de la borne |
| `wwwroot/static/` | `site.css`, `kiosk.css`, `kiosk.js` (le seul JavaScript : recherche, clavier à l'écran, retour à l'accueil après 5 s ou 30 s d'inactivité) |

Les stores sont des singletons qui ouvrent une connexion SQLite par opération (`db.Open()`). Une page de `Pages/` appelle un store puis `AuditLog`. Les tests sont dans `tests/Pointage.Tests` : `AppFactory` démarre l'application complète sur une base temporaire et simule l'adresse du client (poste ou réseau).

## Cycle de vie d'une présence

Une ligne de la table `presences` = un passage (arrivée, départ facultatif). Statuts : `Pending`, `Validated`, `Refused` (affichés « en attente », « validé », « refusé » par `PresenceStatus.Label()`).

```
 borne : CheckIn ------> Pending --- Valider ---> Validated <--+
                            |                         |        | corrections
                            +------- Refuser ---> Refused <----+ (statut, heures)

 responsable : AddManual --> Validated   (directement, sans passer par Pending)
```

- La borne crée toujours une présence `Pending` (`PresenceStore.CheckIn`). Un ajout manuel par un responsable (`AddManual`, y compris pour un jour passé) est validé directement.
- Le responsable valide ou refuse depuis l'accueil (`Decide`, ou `ValidateMany` pour « Tout valider », limité aux arrivées affichées à l'écran). Le responsable qui décide et l'heure sont enregistrés.
- Un refus reste dans l'historique. Un membre refusé peut pointer à nouveau.
- Corrections (fiche du membre, écran `Presences/Edit`) : passer de validé à refusé et inversement, corriger les heures d'arrivée et de départ (la date ne change pas), ou supprimer. Chaque correction est inscrite au journal avec l'ancienne et la nouvelle valeur.
- Une présence encore `Pending` à la fin de la journée le reste (affichée « en attente » dans l'historique). Les statistiques ne comptent que les présences `Validated`, en jours distincts.
- La règle « une présence non refusée par jour » est vérifiée à un seul endroit, `PresenceStore.AllowsNewPresence`, dans la même transaction que l'insertion. Il n'y a volontairement pas de contrainte d'unicité en base.
- Le départ (`Depart`) n'est proposé que si le réglage « bouton départ » est activé ; il ne demande pas de validation.

## Accès local et liaison de la borne

1. Une borne non liée qui ouvre `/borne` voit seulement « Entrez le code de liaison ». Aucune recherche, aucun pointage.
2. Sur le poste, le super admin ouvre **Bornes** et clique sur « Lier une borne » : `KioskPairing.NewPairingCode` produit un code de 6 chiffres, valable 10 minutes, à usage unique, gardé en mémoire (un redémarrage l'annule). Cinq erreurs de saisie l'annulent.
3. La borne envoie le code à `/api/borne/pair`. Le serveur répond par un jeton aléatoire, posé dans le cookie `pointage_kiosk` (valable 10 ans, `HttpOnly`). Seul le **hachage SHA-256** du jeton est stocké (table `kiosks`).
4. Toutes les autres routes `/api/borne/*` vérifient le jeton (`KioskPairing.IsValid`) et répondent 401 sinon, par exemple pour une borne révoquée. Le super admin peut révoquer chaque borne dans **Bornes**.
5. La borne ne reçoit que `KioskMember` : id, « Prénom N. », code et `HasPresenceToday`.

Si le cookie est perdu, il suffit de relier la borne (voir `docs/borne.md`).

## Migrations

`Data/Db.cs` contient le tableau `Migrations` ; chaque entrée est un bloc SQL. La version appliquée est dans `PRAGMA user_version`. Au démarrage, `Migrate()` :

1. si la base existe et n'est pas vide, la copie en `pointage-AAAAMMJJ-HHMMSS.bak` dans le même dossier (filet de sécurité minimal, **pas une sauvegarde** ; la purge RGPD supprime ces copies, qui contiennent les membres tels qu'ils étaient) ;
2. applique chaque migration manquante dans une transaction, puis met à jour `user_version`.

Pour changer le schéma : ajouter une entrée **à la fin** du tableau et incrémenter `SchemaVersion`. Ne jamais modifier une migration déjà publiée, des bases existantes l'ont déjà appliquée. Ajouter un test dans `tests/Pointage.Tests/DbTests.cs`.

## Mises à jour

- `UpdateCheckWorker` appelle `UpdateService.CheckAsync` au démarrage puis toutes les 24 h. L'API interrogée est `https://api.github.com/repos/<Pointage:GitHubRepo>/releases/latest`. Sans internet, une erreur est journalisée et rien ne casse.
- Une version n'est proposée que si le tag (`vX.Y.Z`) est plus récent que la version du programme **et** que la Release contient `Pointage-Setup.exe` et `Pointage-Setup.exe.sha256`.
- **Réglages > Mises à jour** (super admin) : le bouton « Mettre à jour » appelle `UpdateService.ApplyAsync`. Les adresses doivent être en HTTPS sous `https://github.com/<dépôt>/releases/download/` ; le fichier est téléchargé dans un dossier privé `updates\<guid>` à côté de la base, son SHA-256 est comparé à celui publié, et seulement alors `Pointage-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART` est lancé.
- L'installateur (`installer/Pointage.iss`) arrête le service, remplace les fichiers, recrée le service et la règle de pare-feu, puis redémarre le service. Il ne touche jamais à la base ; les migrations s'appliquent au redémarrage. C'est le même installateur pour une première installation et pour une mise à jour.
- Pas de signature de code : d'où l'avertissement SmartScreen à la première installation.
- La CI (`.github/workflows/build.yml`) publie une Release à chaque tag `vX.Y.Z`. Voir le README.

## Pistes pour plus tard

Hors périmètre actuel (spec §11) :

- **Sauvegardes** : copie automatique quotidienne du fichier SQLite vers une clé USB, un disque externe ou un dossier réseau (avec rotation), ou un bouton « Télécharger une copie de la base » dans les réglages. Aujourd'hui, si le PC tombe en panne, tout l'historique est perdu.
- **Chiffrement de la base** (SQLCipher, clé protégée par DPAPI) et **BitLocker** sur le poste du responsable.
- **HTTPS** sur le réseau local (aujourd'hui HTTP : les échanges de la borne ne sont pas chiffrés).
- **Import CSV plus riche** : correspondance libre des colonnes, import Excel `.xlsx`, mise à jour des membres existants. Tout est dans `Members/MemberCsvImport.cs`.
- **Certificat de signature de code**, pour supprimer l'avertissement SmartScreen.
- **Plusieurs passages par jour** : la structure est prête (une ligne = un passage, pas de contrainte d'unicité en base, statistiques en jours distincts). Il reste à modifier la règle, marquée dans `PresenceStore` :

  ```csharp
  // ponytail: une présence par jour ; pour plusieurs passages (spec §5), ne garder que le refus d'une présence encore ouverte.
  private static bool AllowsNewPresence(...)
  ```

  puis à adapter la borne (message « Tu es déjà noté aujourd'hui. » dans `kiosk.js`), `HasPresenceToday` et `Depart`.
