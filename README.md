# Pointage

Application de pointage des présences pour une association d'aide scolaire. Elle remplace le cahier de présence :

- les membres (surtout des enfants) signalent leur arrivée sur une **borne** en libre accès, en cherchant leur nom ou leur code ;
- le **responsable** valide ou refuse chaque arrivée d'un clic depuis son poste (pour qu'un enfant ne pointe pas à la place d'un autre), consulte les fiches, l'historique et les statistiques, et gère les membres (import CSV, carte imprimable, export RGPD, anonymisation, purge).

Les données personnelles restent **sur le poste du responsable** : aucune télémétrie, aucun cloud. Le seul accès réseau sortant est la recherche d'une nouvelle version sur GitHub.

> **Attention : il n'y a aucune sauvegarde automatique.** La base est un seul fichier, `C:\ProgramData\Pointage\pointage.db`. Si le PC tombe en panne, tout l'historique est perdu : copiez régulièrement ce fichier (service arrêté) sur une clé USB ou un disque externe. Voir les pistes dans `docs/ARCHITECTURE.md`.

## Installer

Sur le poste du responsable (Windows, toujours allumé quand les enfants arrivent) :

1. Télécharger `Pointage-Setup.exe` depuis la page [Releases](https://github.com/moanatari/pointage-asso/releases) du dépôt.
2. Lancer le fichier. Windows affiche « Windows a protégé votre ordinateur » (SmartScreen), car le programme n'est pas signé : cliquer sur **Informations complémentaires**, puis **Exécuter quand même**.
3. Suivre l'assistant. Il installe le programme, crée le service Windows `Pointage` (démarrage automatique) et ouvre le port 5080 du pare-feu pour les réseaux privé et domaine.
4. Le navigateur s'ouvre sur l'assistant de premier lancement : nom de la structure, identifiant et mot de passe du super admin (8 caractères minimum), durée de conservation, clavier de la borne. La dernière page affiche l'adresse à saisir sur la borne.
5. **Lier la borne** : voir `docs/borne.md`.

Les pages du responsable ne répondent que sur le poste lui-même (`http://localhost:5080/`). Depuis le réseau, seule la borne répond, et seulement si elle a été liée.

**Mise à jour** : quand une nouvelle version est publiée, le super admin la voit dans **Réglages > Mises à jour** et clique sur « Mettre à jour ». La base n'est jamais touchée par l'installateur.

### Mot de passe du super admin perdu

Sur le poste du responsable, ouvrir une console **en administrateur** et lancer :

```
"C:\Program Files\Pointage\Pointage.exe" reset-admin <identifiant>
```

Le programme demande un nouveau mot de passe (deux fois). Si l'identifiant n'existe pas, il est créé ; s'il existe, il redevient super admin actif.

## Démarrage en développement (5 minutes)

Prérequis : le [SDK .NET 10](https://dotnet.microsoft.com/download) (`dotnet --version` doit afficher 10.x).

```
git clone https://github.com/moanatari/pointage-asso.git
cd pointage-asso
dotnet test
dotnet run --project src/Pointage -- --Pointage:DataDir=$env:TEMP\pointage-dev
```

(`$env:TEMP` est la syntaxe PowerShell ; dans `cmd`, écrire `%TEMP%`.) Puis ouvrir `http://localhost:5080/` : l'assistant de premier lancement crée le compte. Sans `--Pointage:DataDir`, la base serait créée dans `C:\ProgramData\Pointage`, comme sur un vrai poste.

Pour comprendre le code : `docs/ARCHITECTURE.md`. Conventions et règles à ne pas enfreindre : `AGENTS.md`.

## Publier une version

Une fois `dev` fusionné dans `main` (Pull Request), poser un tag sur `main` :

```
git checkout main && git pull
git tag v1.2.3
git push --tags
```

Le tag doit être de la forme `vX.Y.Z`. GitHub Actions (`.github/workflows/build.yml`) lance les tests, publie `Pointage.exe`, compile l'installateur et crée la Release avec `Pointage-Setup.exe` et `Pointage-Setup.exe.sha256`. Les postes installés vérifient cette somme avant d'appliquer une mise à jour.

## Points à vérifier avant la première mise en service

Les tests automatiques passent, mais les points suivants n'ont **jamais été vérifiés sur du matériel réel** :

- la compilation de l'installateur avec Inno Setup (`installer/Pointage.iss`) n'a jamais été exécutée, y compris par la CI ;
- l'installation sur un poste de test (service, pare-feu, verrouillage du dossier `C:\ProgramData\Pointage`) ;
- une mise à jour silencieuse lancée par le service lui-même (qui tourne avec les droits système) ;
- la borne en mode kiosque Edge sur un second PC, y compris le clavier à l'écran et la conservation du cookie de liaison après redémarrage ;
- la vérification visuelle des écrans (responsable et borne).

Prévoir une première version de test (tag `v0.1.0`) installée sur un PC de test avant d'équiper le vrai poste.

## Documentation

- `docs/ARCHITECTURE.md` : les deux postes, les dossiers du code, cycle de vie d'une présence, migrations, mises à jour, pistes pour plus tard.
- `docs/borne.md` : préparer le PC de la borne.
- `docs/rgpd/` : modèles de registre des traitements et de notice d'information des familles.
- `docs/superpowers/specs/` : le document de conception d'origine.
