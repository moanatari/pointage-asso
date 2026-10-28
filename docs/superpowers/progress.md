# Suivi du développement

Plan : `docs/superpowers/plans/2026-10-28-pointage-presences.md`. Spec : `docs/superpowers/specs/2026-10-28-pointage-presences-design.md`.

**Pour reprendre :**
1. Lire ce fichier.
2. `git checkout dev && git pull`.
3. Reprendre la première tâche qui n'est pas « fait ». Si sa branche existe déjà, repartir de cette branche.

Mode d'exécution : par sous-agents (superpowers:subagent-driven-development). Chaque tâche est relue par un relecteur Sonnet avant sa PR vers `dev`, et une relecture finale de l'ensemble est faite par Opus.

| # | Tâche | Modèle | État | Branche | PR | Notes |
|---|-------|--------|------|---------|----|-------|
| 1 | Solution, base et migrations | sonnet | fait | feature/01-socle | PR #1 | .bak si base existante non vide et user_version < SchemaVersion |
| 2 | Comptes, mots de passe, journal, réglages | sonnet | fait | feature/02-comptes | PR #2 | handler Dapper DateTime ajouté ; motif Row pour enums/bools |
| 3 | Membres | sonnet | fait | feature/03-membres | PR #3 | Create ne valide pas : appeler MemberInput.Validate avant |
| 4 | Présences | sonnet | fait | feature/04-presences | PR #4 | règle du jour dans AllowsNewPresence ; corrections sans revérification (décision) |
| 5 | Statistiques | sonnet | fait | feature/05-stats | PR #5 | PerWeekday contient les 7 jours ; tri PerMember jours puis nom puis prénom |
| 6 | Web : accès local, setup, connexion | opus | fait | feature/06-web | PR #6 | pages responsable 404 hors loopback ; lecture DateTime tolère fractions ; AccessDeniedPath à faire en T11 |
| 7 | Borne : liaison, API, kiosque | opus | fait | feature/07-borne | PR #7 | parcours navigateur réel à faire ; KioskPair journalisé à l'émission du code |
| 8 | Écrans du responsable | sonnet | fait | feature/08-ecrans | PR #8 | Edit présence : heures seulement ; PresenceStatus.Label() pour les libellés FR ; rendu visuel à vérifier |
| 9 | Historique | haiku | fait | feature/09-historique | PR #9 | anonymisés triés en tête avec sort=name (mineur) |
| 10 | Import CSV | sonnet | fait | feature/10-import | PR #10 | doublons dans le fichier aussi ignorés ; confirm re-analyse côté serveur |
| 11 | Réglages, comptes, purge, journal | sonnet | fait | feature/11-admin | PR #11 | AccessDenied 403 pour les responsables ; libellés FR du journal (AuditActions.Label) |
| 12 | Mises à jour GitHub | opus | fait | feature/12-mises-a-jour | PR #12 | dossier de téléchargement privé par mise à jour ; ACL du dossier de données → tâche 13 |
| 13 | Installateur, CI, script de la borne | sonnet | fait | feature/13-installateur | PR #13 | iss non compilé localement ; test VM à faire ; ACL ProgramDataPointage SYSTEM+Admins |
| 14 | Documentation | sonnet | fait | feature/14-documentation | PR #14 | AGENTS.md réécrit (passe /init) ; README liste ce qui reste à vérifier |

États possibles : à faire · en cours · en relecture · PR ouverte · fait.

## État final (2026-10-28)

Les 14 tâches sont terminées et fusionnées dans `dev` (PR #1 à #14). La relecture finale de l'ensemble (Opus) a relevé 4 points importants, corrigés dans `feature/15-correctifs-finaux` :
- « Tout valider » : seuls les ids affichés sont validés ;
- le rafraîchissement automatique ne prolonge plus la session ;
- une seule mise à jour à la fois (`SetupMutex`) ;
- notice RGPD aux familles.

158 tests passent.

**Reste à faire avant la première mise en production (`dev` → `main`, puis tag `v0.1.0`)** : les vérifications sur une machine de test listées dans le README.
- Compiler l'installateur : la CI le fera au premier tag.
- Faire une installation réelle.
- Tester une mise à jour silencieuse lancée par le service.
- Tester la borne sur un 2ᵉ PC en mode kiosque Edge, y compris quand le serveur est éteint au démarrage.
- Faire une vérification visuelle des écrans.

### Décisions prises pendant le développement
- flux de branches — chaque tâche sur feature/NN-nom depuis dev, push, PR vers dev, fusion (merge commit) une fois la relecture propre ; jamais de fusion vers main sans l'utilisateur — demandé explicitement (« fonctionner en github flow ») — si faux : PR à annuler par revert sur dev.
- modèles par tâche selon docs/superpowers/progress.md (validé par l'utilisateur) ; relecteurs sonnet ; relecture finale opus — si faux : coût plus élevé ou relecture moins fine.
- .bak quand le fichier existe, non vide, et user_version < SchemaVersion (le plan disait 0 < v, contradictoire avec son propre test) — protège aussi une base v0 non vide — si faux : un .bak superflu.
- la mise à jour de docs/superpowers/progress.md (Important du relecteur) est faite par le contrôleur dans chaque PR — c'est de la tenue de suivi, pas du code — si faux : aucun.
- utilisateur a autorisé la fusion des PR vers dev (PR #1 fusionnée 5c56a20). Tâche 14 : ajouter une passe type /init pour enrichir AGENTS.md/CLAUDE.md (demande utilisateur).
- Decide/UpdateTimes ne revérifient pas la règle du jour — corrections volontaires du responsable, et les stats comptent des jours distincts donc aucun double compte — si faux : deux présences non refusées le même jour visibles dans l'historique.
- KioskPair journalisé à l'émission du code par l'admin (Pair() n'a ni compte ni nom) — trace qui a lancé la liaison — si faux : pas de trace de l'instant d'utilisation du code.
- un tiers sur le réseau peut invalider le code en cours par 5 essais faux — accepté, l'admin en regénère un, et c'est le prix de la protection anti-force brute — si faux : gêne ponctuelle à la liaison.
- /Presences/Edit ne modifie que les heures (pas la date) — la spec dit « corriger l'heure d'arrivée ou de départ », et cela évite les doublons de jour — si faux : pour changer de jour, supprimer puis ajouter une présence.
- libellés d'actions du journal en anglais promus en Important (contrainte globale « interface en français ») — si faux : un peu plus de code de libellés.
- UpdateService en singleton avec HttpClient nommé au lieu d'AddHttpClient<UpdateService> (client typé = transitoire, le Latest du worker n'atteindrait jamais la page) — si faux : HttpClient sans rotation DNS, négligeable pour 1 appel/jour.
- le verrouillage ACL de C:\ProgramData\Pointage (SYSTEM + Administrateurs seulement, protège aussi la base) est confié à l'installateur, tâche 13 — à inclure dans le brief de la tâche 13 — si faux : un utilisateur local Windows non admin peut lire la base des membres.
- T13 — pas d'installation réelle du service/pare-feu sur le poste de dev (test d'installation laissé à l'humain sur VM) ; ajout d'ACL ProgramData\Pointage et d'un .gitattributes sans renormalisation — si faux : test d'installation à faire par l'utilisateur.
- tâche 14 passée de haiku à sonnet — elle inclut désormais la passe type /init (compréhension de tout le code) demandée par l'utilisateur — si faux : coût un peu supérieur.

### Améliorations différées (jugées « peut attendre » par la relecture finale)
- Tri des noms accentués (Élodie après Z), à corriger en premier : c'est visible.
- Refuser les dates futures dans « Ajouter une présence ».
- Neutraliser les formules Excel dans l'export CSV (préfixer `'` devant `=+-@`).
- Renouveler le cookie de la borne à chaque visite (Chrome plafonne à 400 jours).
- `ValidateMany` : se limiter au jour courant.
- Installateur : forcer le propriétaire de `C:\ProgramData\Pointage` (`icacls /setowner`).
- Le port 5080 est écrit à trois endroits et `appsettings.json` est écrasé à chaque mise à jour.
- Fiche membre : statistiques sur une période au choix.
- Afficher sur la page Réglages le nombre de membres à purger.
- Renormaliser les fins de ligne (`git add --renormalize .`) dans un commit séparé.
