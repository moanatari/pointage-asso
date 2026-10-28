# Pointage des présences : document de conception

Date : 2026-10-28
Statut : à valider

Légende : **[décidé]** = choix du porteur de projet ; **[choix du concepteur]** = décision prise pendant la conception, à vérifier ; le reste est laissé au développeur.

---

## 1. Intention

Une association d'aide scolaire accueille surtout des enfants (parfois des adultes), sur des créneaux en fin de journée ou l'après-midi, jusqu'à une quinzaine de personnes en même temps. Aujourd'hui, le responsable note les présences dans un cahier.

L'application remplace ce cahier :

- **Les membres** signalent eux-mêmes leur arrivée sur une borne Windows en libre accès.
- **Le responsable** valide chaque arrivée d'un clic depuis son poste. Cette validation existe pour **empêcher qu'un enfant pointe pour un autre** (beaucoup d'enfants viennent parce que leurs parents les y obligent). Le responsable voit qui est présent, consulte la fiche et les statistiques de chaque membre, et liste qui était là hier, la semaine dernière, etc.

La venue est **libre** [décidé] : il n'y a pas de jours prévus, donc pas de notion d'absence. On compte seulement ceux qui sont venus.

## 2. Objectifs, non-objectifs, anti-objectifs

**Objectifs**
- Pointer son arrivée à la borne en quelques secondes, même pour un enfant.
- Valider ou refuser les arrivées très facilement côté responsable.
- Statistiques de présence par membre et par période.
- Respect du RGPD (données de mineurs).
- Code simple, documenté et lisible par un bénévole de niveau moyen en C# **et** par des agents de codage [décidé].
- Mises à jour depuis GitHub [décidé].

**Non-objectifs (pour l'instant)**
- Sauvegardes automatiques [décidé] (voir §11, pistes futures).
- Chiffrement de la base et BitLocker [décidé] (voir §11).
- Plusieurs sites partagés : chaque bâtiment a sa propre installation, indépendante [choix du concepteur].
- Gestion de jours ou de créneaux prévus et détection d'absences [décidé].
- Interface en plusieurs langues : français uniquement [choix du concepteur].

**Anti-objectifs (échec même si « ça marche »)**
- Une donnée personnelle qui quitte le poste du responsable (cloud, télémétrie, borne qui stocke des données, données dans le dépôt GitHub).
- Une borne qui affiche des informations personnelles au-delà du prénom, de l'initiale du nom et du code.
- Un écran du responsable accessible depuis un autre appareil du réseau.
- Un code que le prochain bénévole ne comprend pas : couches inutiles, frameworks lourds, magie.

## 3. Contraintes

- Windows. Deux postes sur le même réseau local : le **poste responsable**, toujours allumé quand les enfants arrivent [décidé], et la **borne**.
- Toutes les données personnelles restent sur le poste responsable [décidé].
- Dépôt GitHub **public** [décidé].
- Accès du responsable par identifiant et mot de passe, plusieurs comptes, avec un super admin [décidé].
- Un programme d'installation qui configure le premier compte et les réglages [décidé].
- Simple, robuste, maintenable [décidé].

## 4. Architecture

**[choix du concepteur, validé dans la conversation]**

- **Un seul programme**, installé **uniquement sur le poste responsable** : une application web ASP.NET Core (C#, .NET 10 LTS) avec des **Razor Pages** (HTML généré côté serveur, très peu de JavaScript). Ni Blazor, ni framework JavaScript (React, etc.).
- Il tourne en **service Windows** et démarre avec le PC, indépendamment de toute session ouverte. Les enfants peuvent donc pointer même si aucun responsable n'est connecté : les arrivées attendent d'être validées.
- Il est publié en **un seul `.exe` autonome** (self-contained, single-file) : aucun runtime .NET à installer.
- **Base de données : SQLite**, un seul fichier dans `C:\ProgramData\<NomApp>\`, jamais dans le dossier du code. Pas de chiffrement [décidé].
- **La borne** n'est qu'**Edge en mode kiosque** (plein écran verrouillé) qui ouvre `http://<poste-responsable>:<port>/borne`. Rien n'y est installé et rien n'y est stocké.
- **Le responsable** utilise l'application dans son navigateur, sur le poste responsable (`http://localhost:<port>/`).

**Séparation réseau [choix du concepteur]** :
- Toutes les pages et routes du responsable (connexion comprise) ne répondent **qu'aux requêtes venant de la machine elle-même** (adresse de bouclage). Depuis une autre adresse, elles renvoient 404.
- Depuis le réseau, seules la page de la borne et ses appels répondent, et **uniquement pour une borne liée** (voir « Liaison de la borne » ci-dessous).
- Le port par défaut est à choisir par le développeur (5080 par exemple) et reste modifiable dans la configuration.

**Liaison de la borne [décidé : principe d'un code temporaire]** : le Wi-Fi peut être partagé, donc un téléphone sur le réseau ne doit pas pouvoir utiliser la borne.
- Une borne non liée qui ouvre `/borne` voit seulement « Entrez le code de liaison ». Elle n'a accès ni à la recherche ni au pointage.
- Sur le poste responsable, « Lier une borne » affiche un **code temporaire** (6 chiffres, valable environ 10 minutes, à usage unique) [choix du concepteur pour le format].
- Une fois le code saisi, le serveur remet à la borne un **jeton aléatoire long**, conservé dans un cookie persistant d'Edge [choix du concepteur]. Toutes les routes de la borne exigent ce jeton.
- Les réglages listent les bornes liées (nom et date de liaison), et chacune peut être **révoquée**. On peut lier plusieurs bornes.
- Point d'attention pour le guide de la borne : le mode kiosque choisi doit **conserver les cookies**. Le mode « navigation publique » d'Edge utilise une session InPrivate qui les efface, donc il faut privilégier `msedge --kiosk <url> --edge-kiosk-type=fullscreen` avec un profil normal. Si le cookie est perdu, il suffit de relier la borne.

**Accès aux données** : au développeur de choisir entre EF Core et Dapper/ADO.NET. Critère : le moins de concepts possible pour un bénévole, et des **migrations de schéma automatiques au démarrage** (indispensables pour les mises à jour).

## 5. Données

### Membre
- **Obligatoires [décidé]** : nom, prénom, date de naissance.
- **Optionnels [décidé]** : téléphone, e-mail, commentaire, et **une** personne à contacter (nom, prénom, date de naissance, téléphone, e-mail, tous optionnels).
- **Code membre [décidé]** : 6 caractères alphanumériques, générés aléatoirement, uniques.
  - [choix du concepteur] Alphabet en majuscules **sans caractères ambigus** : pas de `0 O 1 I L 5 S 2 Z 8 B`, ce qui laisse par exemple `ACDEFGHJKMNPQRTUVWXY34679`.
  - La saisie à la borne ignore la casse.
  - Le code n'est pas un secret : il sert à trouver sa fiche rapidement. La protection contre la fraude, c'est la validation.
- Enfants et adultes : même fiche. L'âge, calculé à partir de la date de naissance, est affiché côté responsable. Il n'y a pas de règles différentes.
- Sous le champ commentaire, un avertissement permanent : « Ne pas y noter de données sensibles (santé, situation familiale…) ».

### Présence (un pointage)
- Membre, date et heure d'arrivée, statut (**en attente**, **validé**, **refusé**), responsable qui a validé ou refusé, et date et heure de cette décision.
- Origine : borne, ou ajout manuel par un responsable. Un ajout manuel est validé directement.
- Heure de départ : optionnelle (voir §6).
- **Un refus reste dans l'historique** avec la mention « refusé » [décidé].
- [choix du concepteur] Un membre a au plus **une présence non refusée par jour**. S'il pointe une deuxième fois, la borne affiche « Déjà noté aujourd'hui » sans créer de doublon. S'il a été refusé, il peut pointer à nouveau.
  - **Évolution prévue [décidé]** : pouvoir pointer plusieurs fois dans une même journée (aller-retours), chaque passage ayant son arrivée et son départ éventuel. Pour que ce changement reste petit :
    - la règle « une présence par jour » est vérifiée **à un seul endroit du code**, sans contrainte d'unicité (membre + jour) dans la base ;
    - chaque ligne de présence représente déjà **un passage** (arrivée + départ optionnel), pas une journée ;
    - les statistiques comptent des **jours de présence distincts**, pour qu'elles restent justes quand un membre aura plusieurs passages dans la même journée.
- [choix du concepteur] Une présence encore « en attente » à la fin de la journée le reste, et l'historique l'affiche comme « non validée ». **Les statistiques ne comptent que les présences validées.**
- Une « journée » correspond au jour calendaire, à l'heure locale du poste.

### Compte responsable
- Identifiant, mot de passe **haché** (PBKDF2 ou `PasswordHasher` d'ASP.NET Identity, jamais en clair) et rôle (**super admin** ou **responsable**).

### Journal
- Qui (responsable), quoi (validation, refus, création, modification, suppression ou anonymisation de membre, export, purge, réglages, création de compte), quand, et sur quel membre (identifiant, pas de nom copié dans le journal).

### Réglages
- Nom de la structure, durée de conservation (1 an par défaut [décidé]), clavier de la borne affiché ou non (affiché par défaut [décidé]), bouton départ activé ou non (désactivé par défaut [choix du concepteur]).

## 6. La borne

**Parcours en 3 étapes [décidé via maquette]** :

1. **Chercher** : « Bonjour ! Qui es-tu ? » et une seule barre de recherche, qui accepte un nom, un prénom ou le code. Les résultats s'affichent dès 2 caractères.
   - Chaque résultat montre **uniquement le prénom, l'initiale du nom et le code** [décidé], par exemple « Lucas D. · K7MXQ4 ».
   - **Clavier à l'écran** (A–Z, 0–9, effacer) affiché par défaut et masquable dans les réglages [décidé] : certaines bornes seront tactiles, d'autres non.
   - [choix du concepteur] La recherche ignore les accents et la casse (« ines » trouve « Inès »). Elle affiche au plus une dizaine de résultats. Le code ne s'affiche qu'en résultat de recherche, jamais en liste complète.
2. **Confirmer** : « C'est bien toi ? », le prénom, l'initiale du nom et le code en grand, puis deux gros boutons : « Oui, je suis arrivé » et « Non, retour ».
3. **Confirmation** : « Merci Lucas, ton arrivée est notée ! Va voir le responsable pour qu'il te valide. » L'écran revient à l'accueil au bout d'environ 5 secondes.

- [choix du concepteur] L'écran revient aussi à l'accueil après environ 30 secondes d'inactivité, quelle que soit l'étape.
- **Départ (optionnel) [décidé, désactivé par défaut]** : quand il est activé, l'étape 2 propose aussi « Je pars » si le membre a une présence aujourd'hui. Cela enregistre l'heure de départ, sans validation.
- Gros boutons, gros textes, utilisables au doigt.
- La borne ne reçoit jamais la date de naissance, les coordonnées ni le nom complet.
- Si le poste responsable ne répond pas, la borne affiche un message clair : « Le pointage est indisponible, signale-toi au responsable ».

## 7. Poste du responsable

**Connexion** par identifiant et mot de passe, avec une session par cookie et une déconnexion automatique après une période d'inactivité (durée au choix du développeur).

**Rôles [décidé]** :
- **Responsable** : tout, sauf la purge et les réglages.
- **Super admin** : tout, plus les comptes, les réglages, la purge et les mises à jour.
- Il doit toujours rester au moins un super admin.

**Écrans [décidé via maquettes]** :

- **Accueil**, qui s'actualise automatiquement toutes les quelques secondes (un simple rafraîchissement périodique suffit) :
  - « À valider » : nom complet, âge, heure d'arrivée, et les boutons **Valider**, **Refuser** et **Tout valider** ;
  - « Présents aujourd'hui » : nom et heure. Un clic ouvre la fiche ;
  - bouton « Ajouter une présence » pour pointer quelqu'un à la main, **y compris pour un jour passé** [choix du concepteur]. La règle « une présence non refusée par jour » s'applique au jour choisi.
- **Corrections [choix du concepteur]** : depuis l'historique ou la fiche, un responsable peut passer un pointage de validé à refusé (et inversement), corriger l'heure d'arrivée ou de départ, ou supprimer un pointage fait par erreur. Le pointage garde son statut actuel dans un simple champ. Chaque correction est inscrite dans le journal, avec l'ancienne et la nouvelle valeur.
- **Membres** :
  - recherche, création, modification et suppression ;
  - **anonymisation** : les données personnelles sont effacées, les présences restent rattachées à un membre anonyme, ce qui conserve les statistiques globales ;
  - **export des données d'un membre** (droit d'accès RGPD), dans un format lisible : HTML imprimable ou CSV ;
  - **import CSV simple [décidé : un début d'import, à faire évoluer]** :
    - on télécharge un fichier modèle (séparateur `;`, UTF-8 avec ou sans BOM). Colonnes obligatoires : nom, prénom, date de naissance (`JJ/MM/AAAA`). Les colonnes optionnelles reprennent les champs de la fiche ;
    - un aperçu affiche les lignes valides, les lignes en erreur avec leur raison, et les doublons (même nom, prénom et date de naissance qu'un membre existant), qui sont ignorés ;
    - on confirme, puis un code est généré pour chaque nouveau membre ;
    - l'import est inscrit au journal. Le code est isolé dans un seul fichier pour pouvoir le modifier facilement plus tard [choix du concepteur pour le format] ;
  - **impression de sa carte** avec nom, prénom et code, sur une simple page HTML imprimable [choix du concepteur].
- **Fiche membre** :
  - identité, âge et personne à contacter ;
  - dernière venue, venues ce mois-ci et depuis le début de l'année scolaire (septembre) [choix du concepteur], ou sur une période au choix ;
  - répartition par jour de la semaine et heure d'arrivée habituelle (médiane) ;
  - historique des pointages (validé ou refusé, par qui).
- **Historique / Statistiques** :
  - période au choix : hier, cette semaine, semaine dernière, ce mois, mois dernier, ou dates au choix ;
  - nombre de présences par jour, total des présences et nombre de membres différents ;
  - liste de qui est venu et combien de fois, triable par nom ou par nombre de venues ;
  - **export CSV** compatible avec Excel en français (séparateur `;`, UTF-8 avec BOM) [choix du concepteur].
  - Les graphiques peuvent rester très simples (barres en HTML et CSS). Pas de bibliothèque de graphiques nécessaire.
- **Réglages** (super admin) : comptes des responsables (création, désactivation, réinitialisation du mot de passe), durée de conservation, clavier de la borne, bouton départ, purge RGPD, mises à jour et version installée.

## 8. RGPD [décidé : suivre ces recommandations]

Les déclarations à la CNIL n'existent plus depuis 2018. Ce qui suit répond aux obligations actuelles.

- **Minimisation à la borne** : voir §6.
- **Droit d'accès et d'effacement** : export, suppression et anonymisation d'un membre (§7).
- **Durée de conservation** : 1 an par défaut [décidé], réglable à l'installation et dans les réglages.
  - [choix du concepteur] Un membre est concerné quand sa **dernière venue**, ou sa date de création s'il n'est jamais venu, est plus ancienne que cette durée.
  - L'écran de purge **liste les membres concernés** ; le super admin confirme, et ces membres sont **anonymisés**. La purge n'est jamais automatique et silencieuse.
  - Le journal applique la même durée de conservation.
- **Journal des actions** : voir §5.
- **Avertissement** sous le champ commentaire : voir §5.
- **Documentation hors logiciel** (dans `docs/rgpd/`) :
  - un modèle de **registre des traitements** d'une page ;
  - un modèle de **fiche d'information pour les familles** ;
  - un rappel que le guide RGPD de la CNIL pour les associations fait référence. Le concepteur n'est pas juriste.

## 9. Installation

**Poste responsable [choix du concepteur]** :

- Un **installateur Windows** `Setup.exe`, par exemple avec Inno Setup (gratuit, script texte lisible), publié dans les Releases GitHub. Il :
  - copie le programme dans `Program Files` ;
  - enregistre le service Windows en démarrage automatique ;
  - ouvre le port dans le pare-feu Windows pour le réseau local ;
  - ouvre le navigateur sur l'assistant de premier lancement.
- **Assistant de premier lancement**, tant qu'aucun super admin n'existe. Il n'est accessible que depuis le poste lui-même, et il demande :
  - le nom de la structure ;
  - l'identifiant et le mot de passe du super admin (saisi deux fois, longueur minimale à fixer) ;
  - la durée de conservation (1 an par défaut) ;
  - si le clavier de la borne est affiché ;
  - puis il affiche **l'adresse à saisir sur la borne** (`http://<nom-ou-IP-du-poste>:<port>/borne`).
- **Mot de passe du super admin perdu** [choix du concepteur] : une commande à lancer sur le poste en administrateur (par exemple `<NomApp>.exe reset-admin`) permet d'en redéfinir un. Seul quelqu'un ayant un accès physique au poste peut le faire.

**Borne** :
- Un guide pas à pas dans `docs/borne.md`, avec un script PowerShell si c'est faisable, pour configurer Edge en mode kiosque (accès affecté de Windows, ou raccourci Edge `--kiosk`) sur l'adresse de la borne.

## 10. Mises à jour

**[choix du concepteur]**

- Le programme interroge l'API GitHub (dernière Release du dépôt public) au démarrage, puis une fois par jour. Sans internet, rien ne casse : il ne propose simplement pas de mise à jour.
- Le super admin voit « Version X disponible » dans les réglages, avec un bouton **Mettre à jour**.
- Pour appliquer la mise à jour, on réutilise **le même installateur** : le programme télécharge le `Setup.exe` de la Release et le lance en mode silencieux. L'installateur arrête le service, remplace les fichiers et le redémarre. Il n'y a donc qu'un seul mécanisme, pour l'installation comme pour la mise à jour.
- **La base n'est jamais touchée par l'installateur.** Les migrations de schéma s'appliquent au démarrage.
- [choix du concepteur] Avant toute migration, le programme copie le fichier de base (`.bak` horodaté, à côté de l'original). C'est un filet de sécurité minimal contre une mise à jour ratée, pas une sauvegarde.
- **Vérification obligatoire** du fichier téléchargé : la Release publie la somme SHA-256 du `Setup.exe`, et le programme refuse d'installer un fichier dont la somme ne correspond pas. Le téléchargement ne se fait qu'en HTTPS depuis le dépôt configuré.
- **Pas de certificat de signature de code** [choix du concepteur : association bénévole, sans budget]. Le README explique comment passer l'avertissement de Windows SmartScreen lors de la première installation. La mise à jour silencieuse lancée par le service, qui tourne avec les droits système, doit être testée sur un vrai Windows 11.
- **GitHub Actions** : quand un tag `vX.Y.Z` est poussé, une action compile, lance les tests, produit le `.exe` et le `Setup.exe`, puis les attache à la Release.

## 11. Pistes pour plus tard (hors périmètre)

- **Sauvegardes** : copie automatique quotidienne du fichier SQLite vers une clé USB, un disque externe ou un dossier réseau (avec rotation), ou un bouton « Télécharger une copie de la base » dans les réglages. Aujourd'hui, si le PC tombe en panne, **tout l'historique est perdu**.
- **Chiffrement** de la base (SQLCipher, clé protégée par DPAPI) et **BitLocker** sur le poste responsable.
- HTTPS sur le réseau local.
- Import CSV plus riche (correspondance de colonnes libre, import Excel `.xlsx`, mise à jour des membres existants).
- Certificat de signature de code.
- Plusieurs passages par jour (voir §5, la structure est déjà prévue pour).

## 12. Code, documentation et tests

L'objectif est qu'un bénévole ou un agent de codage puisse reprendre le projet [décidé].

- **README.md** (en français) : ce que fait l'application, comment l'installer, et comment lancer le projet en développement en 5 minutes.
- **docs/ARCHITECTURE.md** : schéma des deux postes, organisation des dossiers, cycle de vie d'une présence, fonctionnement des mises à jour.
- **AGENTS.md** (et/ou `CLAUDE.md`) : conventions, commandes de build et de test, règles à ne pas enfreindre (pas de données personnelles hors du poste, pages du responsable accessibles en local uniquement, minimisation à la borne).
- **Commentaires en français** qui expliquent le *pourquoi*. Noms de code en anglais ou en français, au choix du développeur, mais cohérents dans tout le projet.
- **Un seul projet applicatif** plus un projet de tests. Pas de découpage en couches, pas d'interfaces à une seule implémentation, le moins de dépendances NuGet possible.
- **Tests** (xUnit), sur les points sensibles :
  - génération et unicité des codes ;
  - règle « une présence par jour » ;
  - calculs de statistiques ;
  - hachage des mots de passe ;
  - **refus des pages du responsable depuis une adresse non locale** ;
  - refus des routes de la borne sans jeton valide, ou avec un jeton révoqué ; expiration et usage unique du code de liaison ;
  - import CSV : lignes valides, lignes en erreur, doublons ;
  - sélection des membres concernés par la purge ;
  - migrations sur une base existante.
- `.gitignore` qui exclut tout fichier `.db` ou `.sqlite` et les dossiers de données.

## 13. Laissé au développeur

- EF Core ou Dapper/ADO.NET (critère au §4).
- Port par défaut, durée de session, longueur minimale des mots de passe.
- Nom définitif de l'application.
- Style visuel (sobre, lisible, gros contrastes à la borne).
- Fréquence d'actualisation de l'écran d'accueil du responsable.
- Organisation précise des dossiers et des fichiers.
