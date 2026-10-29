# Préparer la borne

La borne est un PC (ou une tablette Windows) qui affiche en plein écran la page `/borne` du poste du responsable. **Rien n'y est installé et aucune donnée n'y est stockée** : seul un cookie de liaison reste dans Edge.

## Avant de commencer

- Pointage est installé sur le poste du responsable et son assistant de premier lancement est terminé (voir le README).
- Les deux PC sont sur le même réseau local (le même Wi-Fi ou la même box).
- Le profil réseau du poste du responsable est **Privé** (Paramètres > Réseau et Internet > propriété du réseau). La règle de pare-feu de l'installateur n'ouvre le port 5080 que pour les réseaux privés et de domaine, pas pour un réseau « Public ».
- Vous connaissez l'adresse de la borne, affichée à la fin de l'assistant : `http://<nom-du-poste>:5080/borne`. Si le nom ne fonctionne pas depuis la borne, utilisez l'adresse IP du poste (commande `ipconfig` sur le poste, ligne « Adresse IPv4 ») : `http://192.168.1.20:5080/borne`. Pour que l'adresse IP ne change pas, la réserver dans la box (« bail DHCP statique »).

## Configurer Edge en mode kiosque

Sur le PC de la borne, **dans la session Windows qui servira de borne** (une session sans mot de passe ou avec ouverture automatique, pour qu'elle démarre toute seule) :

1. Copier le fichier `scripts/borne-kiosque.ps1` du dépôt sur le PC (clé USB, par exemple).
2. Ouvrir PowerShell (pas besoin d'être administrateur) et lancer, en remplaçant l'adresse :

   ```
   powershell -ExecutionPolicy Bypass -File .\borne-kiosque.ps1 -Url http://PC-ACCUEIL:5080/borne
   ```

   Le script vérifie l'adresse, trouve Microsoft Edge et crée dans le dossier « Démarrage » le raccourci `Borne Pointage`, qui lance :
   `msedge.exe --kiosk <adresse> --edge-kiosk-type=fullscreen --no-first-run`.
3. Fermer la session puis la rouvrir (ou redémarrer le PC) : Edge s'ouvre en plein écran sur la borne.

**Pourquoi ce mode et pas la « navigation publique » d'Edge ?** Celle-ci utilise une session InPrivate qui efface les cookies à la fermeture : la borne perdrait sa liaison à chaque redémarrage. Le script utilise un profil Edge normal, qui conserve le cookie.

Pour quitter le mode kiosque : `Alt+F4`. Pour le désactiver définitivement : supprimer le raccourci `Borne Pointage` du dossier Démarrage (`Win+R`, puis `shell:startup`).

## Lier la borne

La première fois, la borne affiche « Entrez le code de liaison ». Pour la lier :

1. Sur le poste du responsable, se connecter en **super admin** et ouvrir **Bornes** (menu du haut).
2. Saisir un nom (par exemple « Entrée ») et cliquer sur **Lier une borne** : un code à 6 chiffres s'affiche.
3. Le saisir sur la borne dans les **10 minutes** qui suivent. Il ne sert qu'une seule fois, et il est annulé après 5 erreurs.
4. La borne affiche alors « Bonjour ! Cherche ton prénom ». Elle apparaît dans la liste des bornes liées.

On peut lier plusieurs bornes. Pour retirer l'accès à une borne (PC volé, tablette perdue), cliquer sur **Révoquer** à côté de son nom dans la même page.

## Si la borne affiche à nouveau « Entrez le code de liaison »

La borne a perdu son cookie de liaison, ou elle a été révoquée. Rien n'est cassé : il suffit de la **relier** comme ci-dessus (nouveau code, saisie sur la borne).

Si cela se reproduit à chaque redémarrage, chercher la cause côté Edge :

- le mode utilisé est-il bien celui du script (raccourci `Borne Pointage`), et non une session InPrivate ou « navigation publique » ?
- Edge est-il configuré pour effacer les cookies à la fermeture (Paramètres > Confidentialité > « Choisir les éléments à effacer chaque fois que vous fermez le navigateur ») ? Désactiver cette option.
- un logiciel de nettoyage ou une stratégie de l'ordinateur supprime-t-il les cookies ?

## Si la borne affiche « Le pointage est indisponible »

Le poste du responsable ne répond pas : vérifier qu'il est allumé, que le service `Pointage` est démarré (console Services de Windows) et que les deux PC sont sur le même réseau. Une borne révoquée revient, elle, sur le formulaire de liaison.

## Réglages liés à la borne

Dans **Réglages** (super admin) : afficher ou masquer le clavier à l'écran (utile pour une tablette tactile sans clavier), et activer le bouton « Je pars » qui enregistre l'heure de départ.
