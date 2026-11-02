# Passages multiples, départs, port et aide : plan de réalisation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** passages multiples par jour avec départs (validation optionnelle), borne à bouton contextuel, accueil Présents/Partis, noms cliquables, port réglable, adresse de la borne affichée, aide intégrée.

**Architecture :** tout reste dans `src/Pointage` selon l'organisation existante (stores singletons + Razor Pages). L'état « présent » se déduit des passages du jour ; une migration v2 ajoute `presences.departure_status`. Le port vit dans la table `settings`, lu au démarrage en mode service, avec synchronisation du pare-feu par `netsh`.

**Tech Stack :** .NET 10, ASP.NET Core Razor Pages, SQLite + Dapper, xUnit (`AppFactory`, `TestDb`), JavaScript sans bibliothèque (`kiosk.js`).

**Spec :** `docs/superpowers/specs/2026-11-02-passages-borne-aide-design.md` (à lire avant chaque tâche).

## Global Constraints

- Toutes les règles d'`AGENTS.md` s'appliquent ; en particulier : interface 100 % française, identifiants en anglais, commentaires français qui expliquent le pourquoi.
- Aucune nouvelle dépendance NuGet.
- Heure stockée uniquement via `time.Now()` / `time.Today()` (`Clock`).
- Enums stockées en texte avec `.ToString()` ; lecture Dapper via la classe privée `Row`.
- Toute nouvelle action de journal a un libellé dans `AuditActions.Label()` ; journal = id du membre, jamais le nom.
- Formulaires : `<form method="post" asp-page="..." asp-page-handler="...">`, jamais `action=`.
- Migration ajoutée à la fin de `Db.Migrations`, `SchemaVersion` incrémenté ; ne jamais modifier la migration 1.
- La borne ne reçoit que `Id`, `Label`, `Code`, `IsPresent` (`KioskMember`).
- `IsPublicPath` de `LocalOnlyMiddleware` n'est pas élargi (le script de borne passe par `/static`).
- Fichiers `.ps1`, `.iss`, `.cmd` en CRLF ; `.ps1`, `.iss` en UTF-8 avec BOM ; le reste en LF.
- Branches : une par groupe ci-dessous, créée depuis `dev` à jour, PR vers `dev`, fusion après relecture (autorisée). Jamais vers `main`.
- Commandes : `dotnet build`, `dotnet test` (toute la suite doit passer avant chaque commit de fin de tâche).

## Review Focus

1. **Minuit** : un enfant arrivé hier et jamais reparti doit être « absent » aujourd'hui (la borne propose « Arrivée »). → test `IsPresent_OpenPassageYesterday_IsFalse` (tâche 1).
2. **Départ pointé deux fois** (double appui, deux bornes) : le second appel ne modifie rien et renvoie `false`. → test `Depart_Twice_SecondReturnsFalseAndKeepsFirstTime` (tâche 1).
3. **Option de validation désactivée avec des départs en attente** : ils deviennent validés, sinon ils resteraient invisibles à jamais. → test `Settings_DisablingDepartureValidation_ValidatesPending` (tâche 2).
4. **Saisie de port invalide** (vide, texte, 80, 70000, port occupé) : message d'erreur, rien d'enregistré, pas de redémarrage. → tests de la tâche 7.
5. **Refus d'un départ après un retour de l'enfant** : impossible, le départ a été validé d'office ; `DecideDeparture` ne touche qu'un départ `Pending`. → test `DecideDeparture_AfterReArrival_DoesNothing` (tâche 1).

---

## Branche `feature/21-conception-passages` (déjà créée)

Contient la spec et ce plan. Pousser, ouvrir la PR vers `dev`, fusionner avant de commencer la tâche 1.

## Branche `feature/22-passages` — tâches 1 à 4

### Task 1 : modèle des passages (migration v2 et `PresenceStore`)

**Files :**
- Modify : `src/Pointage/Data/Db.cs` (migration 2, `SchemaVersion = 2`)
- Modify : `src/Pointage/Presences/PresenceStore.cs`
- Modify : `src/Pointage/Kiosk/KioskApi.cs` (appels renommés seulement ; le contrat change en tâche 3)
- Modify : `src/Pointage/Pages/Presences/Add.cshtml.cs` (retirer le `catch` devenu mort autour d'`AddManual`)
- Test : `tests/Pointage.Tests/PresenceTests.cs`, `tests/Pointage.Tests/DbTests.cs`

**Interfaces :**
- Produces :
  - `Presence` gagne un dernier paramètre `PresenceStatus? DepartureStatus` (`null` = pas de départ ; seuls `Pending` et `Validated` sont utilisés).
  - `enum CheckInResult { Created, AlreadyPresent }` (remplace `AlreadyToday`).
  - `CheckInResult CheckIn(long memberId, bool force = false)`
  - `long AddManual(long memberId, DateTime arrivedAt, long accountId)` — ne lève plus d'exception de jour pris.
  - `bool Depart(long memberId, bool needsValidation)`
  - `bool DecideDeparture(long presenceId, bool validate)`
  - `int ValidatePendingDepartures()`
  - `bool IsPresent(long memberId)` (remplace et supprime `HasPresenceToday`)
  - `IReadOnlyList<PresenceRow> PendingDepartures()`
  - `sealed record TodayBoard(IReadOnlyList<PresenceRow> Present, IReadOnlyList<PresenceRow> Departed)`
  - `TodayBoard Today()` et `static TodayBoard Split(IReadOnlyList<PresenceRow> rows)`
  - `UpdateTimes` inchangé en signature.

Migration 2, texte exact :

```sql
ALTER TABLE presences ADD COLUMN departure_status TEXT CHECK(departure_status IN ('Pending','Validated'));
UPDATE presences SET departure_status='Validated' WHERE departed_at IS NOT NULL;
```

Règles (spec § 1) :
- **Passage ouvert** = non refusé et `departed_at IS NULL`.
- `IsPresent` : le dernier passage non refusé **du jour** (`arrived_at DESC, id DESC`) est ouvert.
- `CheckIn` : dans la transaction existante (`BEGIN IMMEDIATE`), si `!force` et l'enfant est présent → `AlreadyPresent`. Sinon : `UPDATE presences SET departure_status='Validated' WHERE member_id=@m AND departure_status='Pending'`, puis insertion `Pending`/`Kiosk`.
- `AddManual` : même validation d'office des départs en attente, puis insertion `Validated`/`Manual` ; jamais de refus.
- `Depart` : sur le dernier passage ouvert du jour, `departed_at=now`, `departure_status = needsValidation ? 'Pending' : 'Validated'`. Retourne `false` si aucun passage ouvert.
- `DecideDeparture` : uniquement `WHERE id=@id AND departure_status='Pending'` ; valider → `'Validated'` ; refuser → `departed_at=NULL, departure_status=NULL`. Retourne `true` si une ligne a changé.
- `ValidatePendingDepartures` : tous les `Pending`, tous jours confondus.
- `UpdateTimes` : `departure_status` = `'Validated'` si `departedAt` non nul, sinon `NULL`.
- `PendingDepartures` : `departure_status='Pending'` et statut d'arrivée non refusé, tous jours, plus récents d'abord.
- `Today()` : passages non refusés du jour, passés à `Split`.
- `Split` : pour chaque membre, son passage le plus récent va dans `Present` s'il est ouvert **ou** si son départ est `Pending` ; tous les autres passages vont dans `Departed`. Les deux listes triées par `arrived_at DESC`.
- Supprimer `AllowsNewPresence` et son commentaire `ponytail:`.

- [ ] **Step 1 : adapter les tests existants et écrire les nouveaux tests** dans `PresenceTests.cs` :
  - `CheckIn_First_CreatesPending` : remplacer `HasPresenceToday` par `IsPresent`.
  - `CheckIn_WhilePresent_ReturnsAlreadyPresent` (remplace `CheckIn_Twice_ReturnsAlreadyToday`) : 2e appel → `AlreadyPresent`, une seule ligne.
  - `CheckIn_Concurrent_CreatesExactlyOne` : 7 × `AlreadyPresent`.
  - `CheckIn_AfterDepart_CreatesSecondPassage` : `CheckIn`, `Depart(_lucas, false)`, `CheckIn` → `Created`, `ForMember` = 2 lignes, `IsPresent` vrai.
  - `CheckIn_Force_WhilePresent_LeavesFirstOpen` : `CheckIn`, `CheckIn(_lucas, force: true)` → `Created` ; 2 lignes, toutes deux `DepartedAt == null`.
  - `CheckIn_ValidatesPendingDeparture` : `CheckIn`, `Depart(_lucas, true)`, `CheckIn` → la première ligne a `DepartureStatus == Validated`.
  - `AddManual_SameDayTwice_CreatesTwo` (remplace `AddManual_DayAlreadyTaken_Throws`).
  - `Depart_WithValidation_IsPending` : `DepartureStatus == Pending`, `DepartedAt == 17:00`, `IsPresent` faux.
  - `Depart_Twice_SecondReturnsFalseAndKeepsFirstTime` : avancer l'horloge de 5 min entre les deux ; 2e → `false`, `DepartedAt` = première heure.
  - `DecideDeparture_Refuse_ClearsDeparture` : `DepartedAt == null`, `DepartureStatus == null`, `IsPresent` vrai.
  - `DecideDeparture_AfterReArrival_DoesNothing` : `CheckIn`, `Depart(true)`, `CheckIn` ; `DecideDeparture(firstId, false)` → `false`, la première ligne garde son départ.
  - `ValidatePendingDepartures_ValidatesAll` : retourne 1, statut `Validated`.
  - `UpdateTimes_RemovingDeparture_ClearsStatus` et `UpdateTimes_SettingDeparture_IsValidated`.
  - `IsPresent_OpenPassageYesterday_IsFalse` : `CheckIn`, horloge +1 jour → `false`.
  - `Today_SplitsPresentAndDeparted` : Lucas `CheckIn` 9 h, `Depart(false)` 12 h, `CheckIn(force)` 14 h ; Emma `CheckIn` 9 h, `Depart(true)` 10 h ; Léo `CheckIn` 9 h, `CheckIn(force)` 13 h. Attendus : `Present` = Lucas 14 h, Emma (départ en attente), Léo 13 h ; `Departed` = Lucas 9 h–12 h, Léo 9 h (départ non pointé).
  - `Today_ExcludesRefused`.
  - Dans `DbTests.cs` : `Migrate_V1WithDeparture_BackfillsValidated` — créer une base au schéma v1 (exécuter la migration 1 seule et `PRAGMA user_version=1`), insérer une présence avec `departed_at`, lancer `Migrate()`, vérifier `departure_status='Validated'`.
- [ ] **Step 2 : lancer** `dotnet test --filter "FullyQualifiedName~PresenceTests|FullyQualifiedName~DbTests"` → échecs de compilation ou d'assertion attendus.
- [ ] **Step 3 : implémenter** la migration et les membres listés dans `Db.cs` / `PresenceStore.cs` ; ajouter `DepartureStatus` à `Row` et à `PresenceColumns` ; dans `KioskApi.cs`, appeler `IsPresent` et comparer à `CheckInResult.Created` (le nom de champ `HasPresenceToday` reste jusqu'à la tâche 3) ; nettoyer `Add.cshtml.cs`.
- [ ] **Step 4 : lancer** `dotnet test` → tout passe (ajuster `ManagerPagesTests` si un test supposait le refus d'un 2e ajout manuel).
- [ ] **Step 5 : commit** `feat: plusieurs passages par jour et statut de départ`

### Task 2 : réglages des départs

**Files :**
- Modify : `src/Pointage/Settings/SettingsStore.cs`, `src/Pointage/Pages/Admin/Settings.cshtml(.cs)`, `src/Pointage/Audit/AuditLog.cs`
- Test : `tests/Pointage.Tests/SettingsAndAuditTests.cs`

**Interfaces :**
- Consumes : `PresenceStore.ValidatePendingDepartures()`.
- Produces : `AppSettings(string OrganizationName, int RetentionMonths, bool KioskKeyboard, bool KioskDeparture, bool DepartureValidation)` ; `Default = new("", 12, true, true, false)` ; clé `departure_validation` (`"1"`/`"0"`). `AuditActions.DepartureValidate` (« Validation de départ »), `AuditActions.DepartureRefuse` (« Refus de départ »).

- [ ] **Step 1 : tests**
  - Test des valeurs par défaut existant : `KioskDeparture` vrai, `DepartureValidation` faux.
  - `Save_ThenGet_RoundTripsDepartureValidation`.
  - `Get_StoredDepartureZero_KeepsFalse` : clé `kiosk_departure` = `"0"` en base → `KioskDeparture` faux (installations existantes).
  - `Settings_DisablingDepartureValidation_ValidatesPending` (via `AppFactory`, super admin) : réglage activé, un départ `Pending`, POST des réglages avec `DepartureValidation=false` → le départ est `Validated`.
  - `AuditActions_Label_NewDepartureActions`.
- [ ] **Step 2 : lancer** `dotnet test --filter "FullyQualifiedName~SettingsAndAuditTests"` → échec.
- [ ] **Step 3 : implémenter.** Case à cocher « Faire valider les départs par un responsable » sous « Afficher le bouton « départ » à la borne ». `OnPost` : si l'option passe de vrai à faux, appeler `ValidatePendingDepartures()` ; ajouter « ; validation des départs oui/non » aux détails du journal. Construire `after` avec `before with { … }` pour ne pas écraser les champs absents du formulaire.
- [ ] **Step 4 : lancer** `dotnet test` → tout passe (adapter `PurgeTests` ligne 153 et `WebTests` si besoin).
- [ ] **Step 5 : commit** `feat: départ actif par défaut et validation des départs en option`

### Task 3 : borne contextuelle

**Files :**
- Modify : `src/Pointage/Kiosk/KioskApi.cs`, `src/Pointage/wwwroot/static/kiosk.js`, `src/Pointage/Pages/Borne/Index.cshtml`, `src/Pointage/wwwroot/static/kiosk.css` (style du lien secondaire)
- Modify : `AGENTS.md` (règle 3 : `isPresent` au lieu de `hasPresenceToday`)
- Test : `tests/Pointage.Tests/KioskTests.cs`

**Interfaces :**
- Consumes : `CheckIn(memberId, force)`, `Depart(memberId, needsValidation)`, `IsPresent`, `AppSettings.DepartureValidation`.
- Produces :
  - `KioskMember(long Id, string Label, string Code, bool IsPresent)` → JSON `isPresent`.
  - `CheckInRequest(long MemberId, bool Force)` pour `POST /api/borne/checkin` → `{ result: "created" | "already" }`.
  - `POST /api/borne/depart` → `{ ok: bool, pending: bool }` (`pending` = départ en attente de validation).
  - Boutons de `Borne/Index.cshtml` : `#arrive` « ✔ Oui, je suis arrivé », `#leave` « Je pars », `#arrive-again` (style lien) « Je viens d'arriver », `#no` « ✘ Non, retour ».

Affichage dans `choose(m)` :
- départ désactivé **ou** `!m.isPresent` → `#arrive` seul (+ `#no`) ;
- départ activé **et** `m.isPresent` → `#leave` (principal) et `#arrive-again` (+ `#no`) ; `#arrive-again` envoie `force: true`.

Messages exacts :
- arrivée créée : `Merci ${prénom}, ton arrivée est notée ! Va voir le responsable pour qu'il te valide.`
- `already` : `Tu es déjà noté comme présent.`
- départ, `pending` : `Au revoir ${prénom}, ton départ est noté ! Va voir le responsable pour qu'il le valide.`
- départ, sinon : message actuel ; échec : message actuel.

- [ ] **Step 1 : tests**
  - `Search_ReturnsOnlyMinimalFields` : la liste des propriétés JSON attendues devient `id, label, code, isPresent`.
  - `CheckIn_WhilePresent_ReturnsAlready_ThenForceCreates`.
  - `Depart_WithValidation_ReturnsPending` (réglage activé) et `Depart_WithoutValidation_ReturnsNotPending`.
  - `BornePage_HasArriveLeaveAndArriveAgainButtons` : le HTML de `/borne` contient `id="arrive"`, `id="leave"`, `id="arrive-again"`.
- [ ] **Step 2 : lancer** `dotnet test --filter "FullyQualifiedName~KioskTests"` → échec.
- [ ] **Step 3 : implémenter** l'API, la page et `kiosk.js` (les boutons restent tous désactivés dès le premier appui).
- [ ] **Step 4 : lancer** `dotnet test` → tout passe. Vérifier à la main avec `dotnet run --project src/Pointage -- --Pointage:DataDir=$env:TEMP\pointage-dev` : arrivée, départ, « Je viens d'arriver », départ désactivé.
- [ ] **Step 5 : commit** `feat: la borne propose l'arrivée ou le départ selon l'état du membre`

### Task 4 : accueil Présents / Partis / départs à valider

**Files :**
- Modify : `src/Pointage/Pages/Index.cshtml(.cs)`, `src/Pointage/Presences/PresenceStore.cs` (supprimer `ValidatedToday` devenu inutile)
- Modify : `docs/ARCHITECTURE.md` (règle des passages, `Depart`, `isPresent`, retirer la piste « plusieurs passages par jour »), `AGENTS.md` (tableau : `Presences/` = « passages, départs et leurs statuts »)
- Test : `tests/Pointage.Tests/ManagerPagesTests.cs`

**Interfaces :**
- Consumes : `PendingToday()`, `PendingDepartures()`, `Today()`, `DecideDeparture`, `AuditActions.DepartureValidate/DepartureRefuse`, `AppSettings.DepartureValidation`.
- Produces : handlers `OnPostValidateDeparture(long id)`, `OnPostRefuseDeparture(long id)`.

Sections dans l'ordre : « À valider (n) » (inchangée), « Départs à valider (n) » (affichée si l'option est active ou si la liste n'est pas vide ; heure de départ `HH:mm`, précédée de `dd/MM` si ce n'est pas aujourd'hui ; boutons Valider / Refuser), « Présents aujourd'hui (n) », « Partis aujourd'hui (n) ».
- Présents : `<a href="/Members/Details/{id}">Nom</a> – arrivé HH:mm`, suivi de ` (à valider)` si l'arrivée est `Pending`, de ` (départ à valider)` si le départ est `Pending`.
- Partis : `Nom – arrivé HH:mm – parti HH:mm`, ou `– départ non pointé` ; ` (à valider)` si l'arrivée est `Pending`.
- Journal : `audit.Write(User.AccountId(), AuditActions.DepartureValidate|DepartureRefuse, p.MemberId, $"Présence n° {id}")`, seulement si `DecideDeparture` a renvoyé `true`.

- [ ] **Step 1 : tests** (`AppFactory`, responsable connecté)
  - `Home_DepartedMember_ListedUnderPartisNotPresents` : HTML de « Partis aujourd'hui » contient le nom et `parti 17:…` ; « Présents aujourd'hui (0) ».
  - `Home_PendingArrival_ShownInPresentsWithMarker` : contient `(à valider)` dans la section Présents.
  - `Home_RefuseDeparture_MemberBackInPresents_AndAudited`.
  - `Home_ValidateDeparture_Audited`.
  - Adapter le test existant qui utilisait `ValidatedToday()` (ligne 54).
- [ ] **Step 2 : lancer** `dotnet test --filter "FullyQualifiedName~ManagerPagesTests"` → échec.
- [ ] **Step 3 : implémenter** page et documentation.
- [ ] **Step 4 : lancer** `dotnet test` → tout passe.
- [ ] **Step 5 : commit** `feat: accueil avec présents, partis et départs à valider`
- [ ] **Step 6 : fin de branche** — pousser, PR `feature/22-passages` → `dev`, relecture, fusion.

## Branche `feature/23-noms-cliquables` — tâche 5

### Task 5 : noms de membres cliquables

**Files :**
- Modify : `src/Pointage/Pages/History.cshtml` (tableau par membre : `m.MemberId` ; détail : `r.Presence.MemberId`), `src/Pointage/Pages/Admin/Purge.cshtml`, `src/Pointage/Pages/Index.cshtml` (section « À valider » : `<strong>` devient un lien)
- Test : `tests/Pointage.Tests/HistoryTests.cs`, `tests/Pointage.Tests/ManagerPagesTests.cs`

Lien : `<a href="/Members/Details/@id">@nom</a>`, y compris pour un membre anonymisé (décision : sa fiche existe et affiche déjà « anonymisé »). Exclus : la liste de choix de `/Presences/Add` et les titres de page.

- [ ] **Step 1 : tests** `History_MemberNamesLinkToDetails` (les deux tableaux contiennent `href="/Members/Details/{id}"`), `Home_PendingNameLinksToDetails`, `Purge_MemberNameLinksToDetails`.
- [ ] **Step 2 : lancer** les filtres correspondants → échec.
- [ ] **Step 3 : implémenter.**
- [ ] **Step 4 : lancer** `dotnet test` → tout passe.
- [ ] **Step 5 : commit** `feat: noms de membres cliquables dans toutes les listes` ; pousser, PR, fusion.

## Branche `feature/24-port` — tâches 6 et 7

### Task 6 : port en base, commande `set-port`, pare-feu au démarrage

**Files :**
- Create : `src/Pointage/Settings/NetworkPort.cs`
- Modify : `src/Pointage/Settings/SettingsStore.cs`, `src/Pointage/Program.cs`, `README.md` (commande de secours), `AGENTS.md` (commandes : `set-port`)
- Test : `tests/Pointage.Tests/SettingsAndAuditTests.cs` (ou nouveau `NetworkPortTests.cs`)

**Interfaces :**
- Produces :
  - `AppSettings` gagne `int Port` en dernier paramètre ; `Default` port `5080` ; clé `port`.
  - `static class NetworkPort` :
    - `const int Default = 5080;`
    - `static bool IsValid(int port)` → `1024 <= port <= 65535`
    - `static bool IsFree(int port)` → tentative `TcpListener(IPAddress.Any, port)`
    - `static void SyncFirewall(int port)` → exécute `netsh advfirewall firewall delete rule name="Pointage"` puis `netsh advfirewall firewall add rule name="Pointage" dir=in action=allow protocol=TCP localport={port} profile=private,domain remoteip=localsubnet` (arguments identiques à `installer/Pointage.iss`), attend la fin, n'échoue jamais (une erreur n'empêche pas le démarrage).
    - `static void RestartService()` → après ~2 s (le temps d'envoyer la page), tuer le processus (`Process.GetCurrentProcess().Kill()`) pour que la reprise sur échec du service (`restart/60000`) le relance. Commentaire `ponytail:` : relance en ~60 s ; piste : `sc failureflag` + arrêt propre si le délai gêne.
    - `static int SetPortCommand(Db db, string arg)` → code de sortie 0/1 ; messages console en français : succès `Port enregistré : {p}. Redémarrez le service (en administrateur) : sc stop Pointage puis sc start Pointage.` ; erreur `Le port doit être un nombre entre 1024 et 65535.`

`Program.cs` :
- `if (args is ["set-port", var port]) { Environment.ExitCode = NetworkPort.SetPortCommand(OpenDb(builder.Configuration), port); return; }` à côté de `reset-admin`.
- `if (WindowsServiceHelpers.IsWindowsService())` : lire `new SettingsStore(OpenDb(builder.Configuration)).Get().Port`, `builder.WebHost.UseUrls($"http://0.0.0.0:{port}")`, `NetworkPort.SyncFirewall(port)`. Hors service (dev, tests), `appsettings.json` reste la source : ne pas lire la base avant `Build()` (les tests injectent `DataDir` tardivement).

- [ ] **Step 1 : tests** `Port_DefaultIs5080`, `Port_SaveThenGet`, `IsValid_Bounds` (1023 faux, 1024 vrai, 65535 vrai, 65536 faux), `IsFree_OccupiedPort_IsFalse` (ouvrir un `TcpListener` sur port 0, lire le port attribué), `SetPortCommand_Valid_SavesAndReturns0`, `SetPortCommand_Invalid_Returns1AndKeepsPort` (`"abc"`, `"80"`).
- [ ] **Step 2 : lancer** → échec.
- [ ] **Step 3 : implémenter.**
- [ ] **Step 4 : lancer** `dotnet test` → tout passe ; `dotnet run --project src/Pointage -- set-port 8081 --Pointage:DataDir=$env:TEMP\pointage-dev` affiche le message de succès.
- [ ] **Step 5 : commit** `feat: port enregistré en base, commande set-port et pare-feu synchronisé`

### Task 7 : section « Port réseau » des réglages

**Files :**
- Modify : `src/Pointage/Pages/Admin/Settings.cshtml(.cs)`, `installer/Pointage.iss` (commentaire `ponytail:` sur le lien « Ouvrir Pointage » resté en 5080 ; CRLF + BOM), `docs/ARCHITECTURE.md` (ligne sur le port écrit à trois endroits), `docs/superpowers/progress.md` (retirer « Le port 5080 est écrit à trois endroits »)
- Test : `tests/Pointage.Tests/SettingsAndAuditTests.cs`

**Interfaces :**
- Consumes : `NetworkPort.IsValid/IsFree/RestartService`, `AppSettings.Port`.
- Produces : handler `OnPostPort()` avec `[BindProperty] public int? Port` (formulaire séparé, `asp-page-handler="Port"`), propriété `PortMessage`.

Comportement : avertissement fixe au-dessus du champ : « Réservé à la personne qui installe. Après un changement, chaque borne doit être reconfigurée avec la nouvelle adresse. »
- `Port` nul ou hors bornes → erreur `Le port doit être un nombre entre 1024 et 65535.`
- port identique → `Le port est déjà {p}.`, rien d'autre.
- `!IsFree(p)` → erreur `Le port {p} est déjà utilisé sur ce poste.`
- sinon : enregistrer, journal `AuditActions.Settings` avec `Port {ancien} → {nouveau}`, puis
  - en service : `Port enregistré. Pointage redémarre : dans environ une minute, ouvrez http://localhost:{p}/` puis `RestartService()` ;
  - hors service : `Port enregistré. Redémarrez l'application pour l'appliquer, puis ouvrez http://localhost:{p}/`.

- [ ] **Step 1 : tests** (`AppFactory`, super admin) `PortForm_Valid_SavesAuditsAndAsksManualRestart`, `PortForm_OutOfRange_ShowsErrorAndKeepsPort` (`80`, `70000`, vide), `PortForm_Occupied_ShowsError`, `PortForm_Same_NoChange`, `PortForm_Manager_Forbidden` (403), `LocalOnly_LocalhostOnOtherPort_Allowed` (en-tête `Host: localhost:8081` depuis `127.0.0.1` → page servie, pas 404).
- [ ] **Step 2 : lancer** → échec.
- [ ] **Step 3 : implémenter.**
- [ ] **Step 4 : lancer** `dotnet test` → tout passe.
- [ ] **Step 5 : commit** `feat: changement du port depuis les réglages` ; pousser, PR, fusion (la CI compile l'installateur sur la PR vers `main` seulement : relire le `.iss` à la main).

## Branche `feature/25-page-bornes` — tâche 8

### Task 8 : adresse de la borne et script téléchargeable

**Files :**
- Create : `src/Pointage/Kiosk/KioskAddresses.cs`
- Move : `scripts/borne-kiosque.ps1` → `src/Pointage/wwwroot/static/borne-kiosque.ps1` (`git mv`, CRLF + BOM conservés)
- Modify : `src/Pointage/Pages/Admin/Kiosks.cshtml(.cs)`, `src/Pointage/Pages/Setup.cshtml.cs` (remplacer le `5080` codé en dur), `src/Pointage/Program.cs` (`UseStaticFiles` avec `FileExtensionContentTypeProvider` : `.ps1` → `application/octet-stream`), `docs/borne.md` et `README.md` (nouveau chemin du script)
- Test : `tests/Pointage.Tests/KioskTests.cs`, `tests/Pointage.Tests/WebTests.cs`

**Interfaces :**
- Produces :
  - `static class KioskAddresses` :
    - `static IReadOnlyList<string> Urls(int? port)` → d'abord `http://{Environment.MachineName}{:port}/borne`, puis une URL par IPv4 privée des cartes `Up` (hors bouclage) ; `:port` omis si `port` est nul.
    - `static bool IsPrivate(IPAddress ip)` → 10/8, 172.16/12, 192.168/16.
  - Le port passé est `Request.Host.Port` (le responsable consulte la page via `localhost:<port>`).

Page Bornes, au-dessus du formulaire : « Adresse à saisir sur la borne : » puis la liste ; sous la liste : « Si le nom ne fonctionne pas sur la borne, utilisez une adresse IP et réservez-la dans la box. » Le code de liaison reste affiché comme aujourd'hui, avec l'adresse complète au lieu de « (adresse **/borne**) ».

- [ ] **Step 1 : tests** `IsPrivate_Ranges` (192.168.1.20 vrai, 172.20.0.1 vrai, 172.32.0.1 faux, 8.8.8.8 faux, 10.0.0.1 vrai), `Urls_FirstIsMachineName`, `KiosksPage_ShowsBorneUrl` (contient `/borne` et le nom de la machine), `Setup_Submit_CreatesSuperAdminAndShowsKioskUrl` (adapter), `KioskScript_DownloadableFromLan` (`AppFactory(LanIp)`, GET `/static/borne-kiosque.ps1` → 200, `application/octet-stream`).
- [ ] **Step 2 : lancer** → échec.
- [ ] **Step 3 : implémenter.**
- [ ] **Step 4 : lancer** `dotnet test` → tout passe.
- [ ] **Step 5 : commit** `feat: adresse complète de la borne et script de mode kiosque téléchargeable` ; pousser, PR, fusion.

## Branche `feature/26-aide` — tâche 9

### Task 9 : aide intégrée

**Files :**
- Create : `src/Pointage/Pages/Aide/Index.cshtml`, `PremiersPas.cshtml`, `Quotidien.cshtml`, `Membres.cshtml`, `Bornes.cshtml`, `Historique.cshtml`, `Administration.cshtml`, `Depannage.cshtml` (pages Razor sans modèle : `@page` seul)
- Modify : `src/Pointage/Program.cs` (`o.Conventions.AllowAnonymousToFolder("/Aide")`), `src/Pointage/Pages/Shared/_Layout.cshtml` (lien « Aide » après « Bornes », hors du bloc `SuperAdmin`), `src/Pointage/Pages/Login.cshtml` (lien « Aide »), `src/Pointage/Pages/Admin/Kiosks.cshtml` (encadré « Mettre la borne en mode kiosque » → `/Aide/Bornes#kiosque`), `docs/borne.md` (remplacé par un renvoi vers « Aide > Bornes » dans l'application), `README.md` (mention de l'aide), `docs/ARCHITECTURE.md` (dossier `Pages/Aide`)
- Test : `tests/Pointage.Tests/WebTests.cs`

Contenu : spec § 7, page par page. Chaque page : titre, sections courtes, étapes numérotées, liens « ← précédent · sommaire · suivant → » écrits en dur. Page Bornes : ancre `id="kiosque"` ; Windows/Edge avec le script (`/static/borne-kiosque.ps1`, à ouvrir depuis la borne puis lancer comme indiqué dans l'actuel `docs/borne.md`), Android (épinglage d'écran), iPad (Accès guidé). Les pages d'administration portent la mention « Réservé au super admin ». Reprendre tout le contenu utile de `docs/borne.md` avant de le remplacer. Vérifier chaque libellé de bouton et de menu cité contre l'interface finale (tâches 1 à 8).

- [ ] **Step 1 : tests** `Aide_AllPagesAnonymousLocal_Return200` (créer un super admin d'abord, sinon redirection vers `/Setup` ; client non connecté ; les 8 URL), `Aide_FromLan_Returns404`, `Layout_Manager_HasAideLink` (`href="/Aide"`), `Login_HasAideLink`, `Aide_Depannage_MentionsCommands` (contient `reset-admin` et `set-port`).
- [ ] **Step 2 : lancer** → échec.
- [ ] **Step 3 : implémenter** les pages et les liens.
- [ ] **Step 4 : lancer** `dotnet test` → tout passe ; parcourir l'aide dans le navigateur (`dotnet run`), suivre chaque lien.
- [ ] **Step 5 : commit** `feat: onglet Aide avec tutoriel et dépannage` ; mettre à jour `docs/superpowers/progress.md` (section de ce chantier) ; pousser, PR, fusion.
