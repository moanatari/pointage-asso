# Passages multiples, départs, port et aide : conception

Date : 2026-11-02. Suite de `2026-10-28-pointage-presences-design.md`.

## Pourquoi

Les enfants repartent seuls. Un responsable peut recevoir un appel (« est-ce que Léa est là ? ») et doit répondre d'un coup d'œil, avec l'heure d'arrivée et de départ. Aujourd'hui :

- le départ est désactivé par défaut et l'accueil liste comme « présents » des enfants déjà partis ;
- un enfant ne peut avoir qu'une présence par jour, alors qu'il peut venir, repartir et revenir ;
- certains lieux bloquent le port 5080, qu'on ne peut changer qu'à la main dans `appsettings.json` (écrasé à chaque mise à jour) ;
- un bénévole n'a aucune aide intégrée, ni pour le quotidien ni pour les cas de secours.

Réussite : l'accueil dit vrai à tout moment (qui est là, qui est parti, à quelle heure), la borne propose la bonne action sans réfléchir, l'installateur peut adapter le port sur place, et un bénévole trouve seul comment faire dans l'aide.

## Objectifs

1. Départ à la borne actif par défaut ; validation des départs par un responsable en option (désactivée par défaut).
2. Plusieurs passages (arrivée → départ) par enfant et par jour.
3. Borne : un bouton selon l'état de l'enfant.
4. Accueil : « Présents aujourd'hui » ne montre que ceux qui sont là ; nouvelle section « Partis aujourd'hui ».
5. Noms de membres cliquables vers la fiche dans toutes les listes.
6. Port réglable depuis l'interface, avec commande de secours.
7. Page Bornes : adresse complète de la borne et aide au mode kiosque.
8. Onglet « Aide » : pages entre tutoriel et aide-mémoire, avec FAQ de dépannage.

## Non-objectifs

- HTTPS : on reste en HTTP (voir § Port et HTTPS). L'aide l'explique.
- Pas de colonne « présent oui/non » : l'état se déduit des présences du jour (deux informations pourraient se contredire).
- Pas de changement des statistiques : elles comptent déjà des jours distincts de présences validées (règle 5 d'`AGENTS.md`). Trois passages validés le même jour = un jour.
- Pas de bibliothèque de documentation, de Markdown rendu côté serveur ni de recherche dans l'aide.

## Anti-objectifs (échec même si « ça marche »)

- La borne reçoit davantage que prénom, initiale, code et un booléen d'état (règle 3).
- Un départ pointé par erreur fait disparaître un enfant présent alors que la validation des départs est activée.
- Changer le port rend l'interface injoignable sans moyen simple de revenir en arrière.
- Une route d'aide devient joignable depuis le réseau.

## 1. Passages et départs

### Modèle

Une ligne de `presences` = un passage (arrivée, départ éventuel). La contrainte « une présence par jour » (`AllowsNewPresence`, commentaire `ponytail:` dans `PresenceStore`) disparaît ; le schéma l'autorisait déjà (pas d'index unique).

États d'un enfant pour la journée, déduits de son **dernier passage non refusé du jour** :

| Dernier passage du jour | État borne | Accueil |
|---|---|---|
| aucun (ou seulement des refusés) | absent | — |
| arrivée sans départ (arrivée validée ou en attente) | présent | Présents |
| départ en attente de validation | parti (pour la borne) | Présents + « Départs à valider » |
| départ pointé (validé ou validation désactivée) | absent | Partis |

Un passage plus ancien resté sans départ (l'enfant a oublié de badger puis est revenu) s'affiche dans « Partis aujourd'hui » avec « départ non pointé » ; le responsable peut corriger l'heure via la page existante `/Presences/Edit`.

### Validation des arrivées

Inchangée : **toute** arrivée pointée à la borne est « en attente », y compris un retour l'après-midi. « Tout valider » reste limité aux ids affichés.

### Validation des départs (option)

Nouveau réglage « Faire valider les départs par un responsable », désactivé par défaut.

- Désactivé : le départ pointé à la borne est pris en compte immédiatement.
- Activé : le départ pointé est « en attente ». À l'accueil, liste « Départs à valider » (au-dessus ou à côté de « À valider », au choix du constructeur), avec Valider / Refuser. Tant qu'il est en attente, l'enfant reste dans « Présents » avec la mention « (départ à valider) ». **Refuser efface l'heure de départ** : l'enfant redevient présent. Valider et refuser un départ sont journalisés (nouvelles `AuditActions` avec libellé français).
- **Une nouvelle arrivée (borne ou manuelle) valide automatiquement le départ encore en attente d'un passage précédent du même enfant** : s'il revient, c'est qu'il était bien parti (décision de l'utilisateur). Le refus n'est donc possible que tant que l'enfant n'a pas rebadgé, ce qui évite deux passages ouverts en même temps. Cette validation automatique n'a pas de compte et n'est pas journalisée (comme les pointages de la borne).
- Un départ saisi ou corrigé par un responsable (`/Presences/Edit`) est réputé validé.
- Basculer le réglage n'affecte pas les départs déjà enregistrés. Choix du constructeur pour les départs encore en attente au moment où l'on désactive l'option (les considérer comme validés est le plus simple).

Combinaison arrivée / départ d'un même passage (décision de Claude) : les deux statuts sont indépendants, sauf qu'**un passage dont l'arrivée est refusée disparaît partout** (y compris de « Départs à valider »), comme aujourd'hui pour une arrivée refusée. Un passage terminé dont l'arrivée est encore en attente apparaît à la fois dans « À valider » et dans « Partis » (avec « (à valider) »).

Stockage : une migration (v2) ajoute ce qu'il faut pour distinguer un départ en attente d'un départ validé (par exemple une colonne `departure_status` nullable). Forme exacte laissée au constructeur, en respectant les conventions Dapper/enums d'`AGENTS.md`.

### Départ actif par défaut

`AppSettings.Default.KioskDeparture` passe à `true`. Les installations existantes ont déjà la valeur `0` enregistrée par l'assistant : **elles la gardent** (décision de Claude, annoncée sans objection). L'aide indique où l'activer.

## 2. Borne

- La recherche renvoie, pour chaque membre, `isPresent` (remplace `hasPresenceToday`) selon le tableau ci-dessus. Mettre à jour la règle 3 d'`AGENTS.md` et le type `KioskMember`.
- Écran de confirmation :
  - absent → bouton principal « Arrivée » (texte actuel « C'est moi ! » ou équivalent au choix) ;
  - présent → bouton principal « Départ », plus un petit lien secondaire « Je viens d'arriver » qui crée une nouvelle arrivée sans clore le passage précédent ;
  - si le départ est désactivé dans les réglages, comportement actuel : seule l'arrivée est proposée ; une seconde arrivée alors que l'enfant est présent répond « Tu es déjà noté » comme aujourd'hui.
- API : `/api/borne/checkin` refuse une arrivée si l'enfant est présent, sauf demande explicite (paramètre du type `force: true` envoyé par « Je viens d'arriver »). La vérification et l'insertion restent dans une même transaction (double appui).
- Messages : départ en attente de validation → « Ton départ est noté, va voir le responsable pour qu'il le valide » ; sinon message actuel.
- Ajout manuel par un responsable (`/Presences/Add`) : plusieurs passages par jour autorisés, sans vérification de chevauchement (décision de Claude).

## 3. Accueil

Ordre des sections : « À valider » (arrivées), « Départs à valider » (si l'option est active), « Présents aujourd'hui », « Partis aujourd'hui ».

- **Présents aujourd'hui (n)** : un enfant par ligne (n = nombre d'enfants), nom cliquable, heure d'arrivée du passage en cours, mention « (à valider) » si cette arrivée est en attente, « (départ à valider) » si son départ l'est.
- **Partis aujourd'hui (n)** : chaque passage terminé du jour, nom cliquable, « arrivé 09:02 – parti 12:15 » ; passages anciens restés ouverts : « arrivé 09:02 – départ non pointé ». Les passages refusés n'apparaissent pas. Un enfant revenu apparaît à la fois dans Présents (passage en cours) et dans Partis (passages précédents).
- Le rafraîchissement automatique (10 s) reste.

## 4. Noms cliquables

Chaque nom de membre affiché dans une liste ou un tableau mène à `/Members/Details/{id}` : accueil (toutes les sections), Historique (tableau par membre et détail des pointages), Purge (liste des membres à purger). Exceptions : la liste de choix de `/Presences/Add` (ses liens servent à choisir le membre) et les titres de pages déjà centrés sur un membre. Ne jamais lier un membre anonymisé (sa fiche n'a plus de nom) : choix du constructeur entre ne pas lier et lier quand même.

## 5. Port et HTTPS

### HTTPS : non

Les pages du responsable ne répondent qu'en local (`LocalOnlyMiddleware`) : mot de passe et données ne passent jamais sur le réseau. Seule la borne passe par le Wi-Fi, avec prénom, initiale, code et jeton. Le HTTPS sur réseau local demande un certificat auto-signé à installer sur chaque borne, qui provoque des avertissements et expire : source de pannes disproportionnée pour une association. À expliquer dans l'aide (FAQ).

### Port réglable

- Le port est enregistré dans la table `settings` (clé `port`, défaut 5080), lu au démarrage avant de lancer Kestrel (`Program.cs` ouvre déjà la base pour `reset-admin`). Il ne dépend plus d'`appsettings.json`, donc survit aux mises à jour. Un `--urls`/`Urls` explicite en ligne de commande peut garder la priorité pour le développement (choix du constructeur).
- **Réglages** (super admin) : section « Port réseau » avec un avertissement (« réservé à la personne qui installe ; les bornes devront être reconfigurées »). Validation : entier entre 1024 et 65535, port libre sur le poste (tentative d'écoute). À l'enregistrement : journal, puis la page affiche la nouvelle adresse (`http://localhost:<port>/`) et le délai d'attente, puis l'application redémarre d'elle-même.
- **Pare-feu** : l'application, lorsqu'elle tourne en service Windows, met la règle `Pointage` en accord avec son port **à chaque démarrage** (mêmes commandes `netsh` que l'installateur : suppression puis ajout, `profile=private,domain remoteip=localsubnet`). Le service tourne en compte système, il en a le droit. Ainsi l'installateur peut continuer à poser sa règle 5080 : le service la corrige en démarrant. En `dotnet run` (hors service), ne rien toucher au pare-feu.
- **Redémarrage** : mécanisme au choix du constructeur ; il doit aboutir sans action humaine. Piste la plus simple : arrêter le processus avec un code d'erreur, la reprise automatique du service (`sc failure ... restart/60000`) le relance en 60 s ; l'écran le dit. Attention : un service qui signale un arrêt propre n'est pas relancé ; ajouter `sc failureflag Pointage 1` dans `installer/Pointage.iss` ou forcer la sortie du processus sans arrêt propre.
- Hors service Windows (`dotnet run`, tests sous `AppFactory`) : la section reste utilisable, enregistre le port et affiche « redémarrez l'application à la main » au lieu de redémarrer (décision de Claude).
- **Secours** : `Pointage.exe set-port <port>` (à côté de `reset-admin`), qui enregistre le port, puis indique de redémarrer le service (ou le redémarre). Documenté dans l'aide et le README.
- Remplacer les `5080` codés en dur : `Setup.cshtml.cs` (adresse de la borne), lien « Ouvrir Pointage » de l'installateur (peut rester 5080 : il ne sert qu'à la première installation ; à noter dans un commentaire `ponytail:` sinon), `appsettings.json`. Retirer l'item « port écrit à trois endroits » des améliorations différées de `progress.md`.
- `LocalOnlyMiddleware` compare `Host.Host` (sans le port) : rien à changer, mais un test doit couvrir une requête sur un port autre que 5080.

## 6. Page Bornes

- Afficher l'adresse complète à saisir sur la borne : `http://<nom-du-poste>:<port>/borne` et, en dessous, une adresse par IPv4 privée du poste (`http://192.168.1.20:<port>/borne`), obtenues par `System.Net.NetworkInformation` (cartes actives, hors boucle locale). Mention : « si le nom ne marche pas, utilisez l'adresse IP ; réservez-la dans la box ».
- Encadré « Mettre la borne en mode kiosque » avec lien vers la page d'aide Bornes.
- Le script `scripts/borne-kiosque.ps1` doit être récupérable depuis la borne : le servir en `/static/borne-kiosque.ps1` (chemin déjà public, aucune donnée) et donner le lien dans l'aide (décision de Claude). Garder CRLF + BOM.

## 7. Aide

### Lecteur

Le bénévole responsable : du quotidien (valider, gérer les membres) jusqu'à la configuration complète après l'installation ; aussi aide-mémoire des commandes et des cas de secours (FAQ).

### Forme

- Onglet « Aide » dans le menu, après « Bornes », **visible par tous les responsables**. Les pages d'administration y restent lisibles avec la mention « réservé au super admin ».
- Pages Razor statiques sous `Pages/Aide/` : un sommaire (`/Aide`) et une page par thème, liées entre elles (précédent / suivant + retour au sommaire). Textes courts, étapes numérotées, pas d'images obligatoires.
- **Accessibles sans connexion** (décision de Claude) : la FAQ sert justement quand on a perdu son mot de passe. Lien « Aide » sur la page de connexion. Elles restent **locales uniquement** (hors des préfixes publics de `LocalOnlyMiddleware`) ; un test le vérifie. Aucune donnée de membre dans ces pages.
- Les noms de boutons et de menus cités doivent correspondre exactement à l'interface.

### Pages

1. **Premiers pas** : l'assistant, le premier compte, les réglages à vérifier, lier une première borne.
2. **Au quotidien** : l'accueil et ses sections, valider/refuser arrivées et départs, « Tout valider », ajouter une présence à la main, corriger une heure, répondre à « est-ce que X est là ? », l'enfant qui a oublié de badger son départ.
3. **Membres** : ajouter, modifier, import CSV (format attendu), codes et carte, anonymisation, export des données d'un membre.
4. **Bornes** : adresse à saisir, lier, révoquer, mode kiosque (Windows/Edge avec le script ; Android : épinglage d'écran ; iPad : Accès guidé), réglages clavier/départ, « la borne redemande le code ».
5. **Historique et statistiques** : périodes, ce qui est compté (présences validées, jours distincts), export CSV.
6. **Administration** : comptes, réglages (dont validation des départs et port), journal, purge et durée de conservation, mises à jour.
7. **Dépannage / FAQ** : mot de passe perdu (`reset-admin`), changer le port / port bloqué (`set-port`), la borne ne se connecte pas (réseau privé, pare-feu, IP), « Le pointage est indisponible », pourquoi HTTP et pas HTTPS, où sont les données (`C:\ProgramData\Pointage`) et les sauvegardes `.bak`, redémarrer le service.

`docs/borne.md` : son contenu migre dans la page d'aide Bornes ; le fichier renvoie vers l'aide intégrée ou est supprimé (choix du constructeur), pour éviter deux versions divergentes.

## Contraintes

Toutes celles d'`AGENTS.md`, en particulier : interface en français, libellés via `Label()`, formulaires par tag helpers, journalisation des écritures d'un responsable (id du membre, jamais le nom), migration ajoutée en fin de tableau avec `SchemaVersion` incrémenté, aucune nouvelle dépendance NuGet, horloge via `Clock`. Branches `feature/<NN>-…` depuis `dev` (prochain numéro : 21) ; découpage en plusieurs branches laissé au plan.

## Laissé au constructeur

Forme exacte du stockage du statut de départ ; libellés précis des boutons de la borne ; mécanisme de redémarrage ; mise en page de l'accueil et des pages d'aide ; découpage en tâches/branches ; liaison ou non des membres anonymisés ; sort de `docs/borne.md`.

## Décisions de Claude (à vérifier par l'utilisateur)

- Pas de colonne « présent » : état déduit des passages.
- Les installations existantes gardent le départ désactivé si c'était enregistré.
- Ajout manuel : plusieurs passages par jour, sans contrôle de chevauchement.
- Un départ en attente compte comme « parti » pour la borne (elle propose « Arrivée »), mais l'enfant reste dans « Présents » à l'accueil jusqu'à validation.
- Pare-feu resynchronisé par le service à chaque démarrage plutôt que par l'installateur.
- Aide accessible sans connexion (locale uniquement), lien depuis la page de connexion.
- Script de mode kiosque servi en `/static/borne-kiosque.ps1`.
