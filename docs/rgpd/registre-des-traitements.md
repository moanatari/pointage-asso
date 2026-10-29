# Registre des traitements : pointage des présences

> Modèle à compléter par la structure, qui est le **responsable de traitement**. Le concepteur du logiciel n'est pas juriste : ce document est un point de départ, pas un avis juridique. Le guide de référence est celui de la CNIL pour les associations : <https://www.cnil.fr/fr/associations> (rubrique « Associations : les règles de base » et modèle de registre).
>
> Les déclarations à la CNIL n'existent plus depuis 2018 ; en revanche, le registre est obligatoire (article 30 du RGPD) et doit être tenu à jour. Remplacer les passages entre crochets `[ ... ]`.

| | |
|---|---|
| Structure | [nom, adresse] |
| Responsable de traitement | [nom, fonction, coordonnées] |
| Référent données personnelles (facultatif) | [nom, coordonnées] |
| Date de création / dernière mise à jour du registre | [JJ/MM/AAAA] |

## Traitement : suivi des présences aux activités

**Finalité (pourquoi ?)**
Noter qui est venu à l'association et quand, remplacer le cahier de présence, établir des statistiques de fréquentation (par membre et par période) et pouvoir joindre la personne à contacter en cas de besoin. [compléter si besoin : bilan pour un financeur, etc.]

**Base légale (sur quel fondement ?)**
[À choisir et justifier. Le plus souvent : intérêt légitime de l'association à suivre sa fréquentation et à assurer la sécurité des enfants accueillis ; ou mission d'intérêt public, ou consentement des familles, selon la situation.]

**Catégories de personnes concernées**
- Enfants et adolescents accueillis (**mineurs**).
- Adultes accueillis.
- Personnes à contacter indiquées par les membres (le plus souvent un parent).
- Responsables et bénévoles ayant un compte sur le logiciel (identifiant, journal de leurs actions).

**Catégories de données**
- Membres : nom, prénom, date de naissance (obligatoires) ; téléphone, e-mail, commentaire libre (facultatifs) ; code membre généré par le logiciel.
- Personne à contacter : nom, prénom, date de naissance, téléphone, e-mail (tous facultatifs).
- Présences : date et heure d'arrivée, heure de départ éventuelle, statut (en attente, validé, refusé), origine (borne ou ajout manuel), responsable qui a décidé et heure de la décision.
- Comptes : identifiant, empreinte du mot de passe (jamais le mot de passe), rôle.
- Journal : date, responsable, action, identifiant du membre concerné.
- Aucune donnée sensible n'est prévue. Le champ « commentaire » porte l'avertissement « Ne pas y noter de données sensibles (santé, situation familiale…) » : les responsables doivent le respecter.

**Destinataires (qui y a accès ?)**
- Les responsables et bénévoles de la structure ayant un compte. [préciser le nombre et le rôle]
- Aucun transfert à un tiers, aucun hébergeur : les données restent sur le poste du responsable. Le logiciel n'envoie aucune donnée à l'extérieur (son seul accès à Internet est la recherche d'une nouvelle version sur GitHub).
- À la borne, un membre ne voit que les prénoms, l'initiale du nom et le code de ceux qu'il recherche.
- [Ajouter tout destinataire extérieur éventuel : financeur, mairie, etc. ; sinon écrire « aucun ».]

**Durée de conservation**
Un membre dont la dernière venue (ou, s'il n'est jamais venu, la date de création) remonte à plus de [12] mois est proposé à la purge : ses données personnelles sont alors **anonymisées** (les présences restent pour les statistiques, sans lien avec une personne). La durée est un réglage du logiciel (1 an par défaut). Le journal des actions suit la même durée. La purge est lancée par le super admin (menu Réglages > Purge RGPD) ; elle n'est pas automatique : [indiquer à quelle fréquence la structure la fait, par exemple chaque début d'année scolaire].

**Mesures de sécurité**
- Données stockées uniquement sur un poste de la structure, dans un dossier réservé aux administrateurs et au service.
- Pages du responsable accessibles uniquement depuis ce poste ; elles ne répondent pas depuis le réseau.
- Accès par identifiant et mot de passe (8 caractères minimum, stockés sous forme d'empreinte), déconnexion automatique après inactivité, rôles « responsable » et « super admin ».
- Borne liée par un code temporaire à usage unique, révocable ; la borne ne conserve aucune donnée.
- Journal des actions des responsables (validations, corrections, exports, suppressions, purge, réglages).
- Droit d'accès : export des données d'un membre ; droit à l'effacement : suppression ou anonymisation d'un membre. Les données effacées sont écrasées dans le fichier de la base, et la purge supprime aussi les copies de secours faites avant une mise à jour (`pointage-*.bak`).
- Limites connues, à traiter par la structure : [aucune sauvegarde automatique ; base non chiffrée ; réseau local en HTTP]. Mesures complémentaires : [session Windows protégée, BitLocker, sauvegarde sur support externe rangé sous clé, etc.].

**Information des personnes**
Les familles reçoivent la notice `information-familles.md` (à remettre à l'inscription). Les adultes reçoivent la même notice.

**Droits des personnes**
Accès, rectification, effacement, limitation, opposition : exercés auprès de [contact]. Réponse sous un mois. Réclamation possible auprès de la CNIL (<https://www.cnil.fr/plaintes>).
