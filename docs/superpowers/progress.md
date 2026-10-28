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
| 3 | Membres | sonnet | à faire | | | |
| 4 | Présences | sonnet | à faire | | | |
| 5 | Statistiques | sonnet | à faire | | | |
| 6 | Web : accès local, setup, connexion | opus | à faire | | | |
| 7 | Borne : liaison, API, kiosque | opus | à faire | | | |
| 8 | Écrans du responsable | sonnet | à faire | | | |
| 9 | Historique | haiku | à faire | | | |
| 10 | Import CSV | sonnet | à faire | | | |
| 11 | Réglages, comptes, purge, journal | sonnet | à faire | | | |
| 12 | Mises à jour GitHub | opus | à faire | | | |
| 13 | Installateur, CI, script de la borne | sonnet | à faire | | | |
| 14 | Documentation | haiku | à faire | | | |

États possibles : à faire · en cours · en relecture · PR ouverte · fait.
