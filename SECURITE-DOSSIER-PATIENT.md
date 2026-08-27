# Sécurité du dossier patient

**Document destiné aux cabinets partenaires**

Comment les données de vos patients sont protégées, qui peut y accéder, et ce qui garantit
qu'elles restent les vôtres.

| | |
|---|---|
| **Version** | 1.0 |
| **Date** | 16 août 2026 |
| **Portée** | Service hébergé et installation locale |

---

## L'essentiel

- Les données de votre cabinet sont cloisonnées : aucune autre clinique ne peut y accéder, et
  cela est vérifié automatiquement à chaque livraison.
- Les comptes administrateurs sont protégés par une double authentification obligatoire.
- Toutes les communications sont chiffrées, du poste de travail jusqu'à la base de données.
- Chaque modification du dossier est enregistrée dans un journal infalsifiable.
- Les sauvegardes sont automatiques, chiffrées, et chacune est vérifiée lisible.
- Vous pouvez exporter l'intégralité de vos données à tout moment, sans nous le demander.

---

## 1. Objet de ce document

Vous nous confiez le dossier médical de vos patients. C'est la donnée la plus sensible qu'un
cabinet détient, et vous êtes en droit de savoir précisément comment elle est traitée.

Ce document décrit les protections en place, en langage clair plutôt qu'en jargon technique. Il
est rédigé pour être lu par un praticien, et pour pouvoir être transmis à votre conseil juridique
ou à votre délégué à la protection des données.

---

## 2. Chaque cabinet est cloisonné

**Plusieurs cabinets utilisent la même plateforme. Aucun ne voit les patients d'un autre.**

Ce cloisonnement n'est pas une règle appliquée écran par écran, où un oubli passerait inaperçu.
Il est appliqué au niveau de la base de données elle-même : toute lecture est restreinte au
cabinet de l'utilisateur connecté, et une requête qui n'aurait pas déclaré à quel cabinet elle
appartient ne renvoie **aucune ligne** plutôt que toutes.

| | |
|---|---|
| **L'identité du cabinet n'est jamais déclarée par le navigateur** | Elle est déterminée par le serveur à partir du compte connecté. Un poste de travail ne peut donc pas demander à consulter un autre cabinet, quelle que soit la requête envoyée. |
| **Les fichiers suivent la même règle** | Radiographies, documents scannés et pièces jointes sont rangés sous une adresse propre à votre cabinet, imposée par le système au moment de l'enregistrement. |
| **Vérifié automatiquement, pas par relecture** | Des contrôles automatisés parcourent l'ensemble des tables à chaque livraison et bloquent la mise en production si une seule d'entre elles n'est pas cloisonnée. |
| **Notre propre outil d'administration ne lit aucun dossier** | La console interne qui nous sert au suivi des abonnements ne peut retourner qu'une liste fermée d'informations — jamais un nom de patient, un acte ou une note clinique. Cette limite est imposée par le code lui-même. |

---

## 3. Qui accède à quoi

**Trois rôles, et un principe simple : on peut enregistrer, on ne peut pas effacer.**

Chaque écran de l'application est rattaché à un rôle précis. Le secrétariat dispose de tout ce
qu'il faut pour tenir l'agenda, accueillir un patient, encaisser un règlement et compléter le
dossier — parce que c'est son métier. En revanche, la suppression d'une fiche de soins, d'un
document médical ou d'un antécédent est réservée au praticien ou à l'administrateur.

| Rôle | Portée |
|---|---|
| **Administrateur** | Gestion des comptes, paramètres du cabinet, journal d'activité, et l'ensemble des opérations dont l'effet ne se lit sur aucun écran par la suite. |
| **Dentiste** | L'intégralité du dossier clinique, la facturation du cabinet, les devis et les corrections comptables. |
| **Secrétaire** | Agenda, patients, encaissements, dossier clinique en saisie et en consultation. Les totaux financiers du cabinet ne lui sont pas ouverts. |

> Un compte désactivé cesse de fonctionner **dès la requête suivante**, sans attendre l'expiration
> de sa session. Le départ d'un collaborateur se traite donc en une seule action, immédiatement.

---

## 4. Identification des utilisateurs

**Un mot de passe volé ne suffit pas à entrer dans votre cabinet.**

| | |
|---|---|
| **Double authentification obligatoire** | Sur le service hébergé, tout compte administrateur doit présenter un code temporaire depuis son téléphone en plus de son mot de passe. Aucune session n'est ouverte avant que ce second facteur soit configuré. Les dentistes et secrétaires peuvent l'activer volontairement. |
| **Huit codes de secours** | Remis une seule fois lors de la configuration, ils permettent de se reconnecter en cas de perte du téléphone. Chacun ne sert qu'une fois. Un administrateur du cabinet peut par ailleurs réinitialiser le second facteur d'un collègue. |
| **Les mots de passe ne sont jamais conservés** | Seule une empreinte cryptographique est enregistrée, calculée par un algorithme conçu pour résister aux tentatives de retrouver le mot de passe d'origine. Personne — nous compris — ne peut lire le mot de passe d'un utilisateur. |
| **Douze caractères minimum** | Vérifié par le serveur au moment où un mot de passe est défini, et non par le navigateur seul. |
| **Les tentatives sont limitées** | Le nombre d'essais est plafonné par compte, ce qui rend une attaque par force brute inopérante — sans qu'une erreur de frappe d'un collègue ne bloque tout le cabinet. |
| **Sessions maîtrisées** | Chaque connexion ouvre une session distincte. La réutilisation d'un identifiant de session périmé est détectée et met fin à cette session — sur ce seul appareil, sans déconnecter le reste du cabinet — et l'utilisateur en est informé. |

---

## 5. Chiffrement des communications

**Rien ne circule en clair, ni sur internet, ni à l'intérieur de nos serveurs.**

| | |
|---|---|
| **Entre votre poste et le service** | Connexion HTTPS sur toute la durée de la session, dans les deux sens. Le navigateur reçoit en outre une instruction valable un an lui interdisant de revenir à une connexion non chiffrée, même si un lien le lui demandait. |
| **À l'intérieur du serveur** | Les échanges entre l'application, la base de données et le stockage des fichiers sont eux aussi chiffrés. Une écoute du réseau interne ne révèle rien. L'application **refuse de démarrer** si l'un de ces échanges n'est pas protégé. |
| **Sur une installation locale au cabinet** | Le serveur génère son propre certificat au premier démarrage. Seul le port chiffré est ouvert sur le réseau du cabinet ; la liaison non chiffrée reste confinée à la machine elle-même. Une page dédiée permet d'installer le certificat sur les téléphones et tablettes — profil Apple, certificat Android et QR code — avant même la première connexion. |
| **Un certificat douteux est refusé** | Sur toutes les applications — navigateur, poste Windows, Android, iPhone — un certificat non valide interrompt la connexion et affiche un message explicite. Il n'existe aucun moyen de passer outre. |

---

## 6. Protection des données conservées

**Les identifiants les plus sensibles sont chiffrés individuellement, avec des clés conservées
séparément des données qu'elles protègent.**

Cela concerne les seconds facteurs d'authentification, les identifiants des services de rappel par
SMS et WhatsApp, la connexion à Google Agenda et les identifiants techniques du cabinet. Chacun
dispose de sa propre clé dérivée : un élément chiffré ne peut pas être lu par le code qui en
traite un autre.

| | |
|---|---|
| **Un déchiffrement impossible refuse l'opération** | Le système ne bascule jamais vers une solution dégradée. Pour un second facteur, « clé illisible » ne devient jamais « connexion sans second facteur » : l'accès est refusé et une procédure de récupération est indiquée. |
| **Les clés sont détenues séparément** | La clé qui protège un contenu n'est jamais conservée au même endroit que ce contenu, et fait l'objet d'une garde formalisée avec un dépositaire nommé et une copie de secours distincte. |
| **Rotation sans perte** | Lorsqu'une clé est remplacée, la précédente reste disponible en déchiffrement. Un renouvellement ne rend jamais illisible ce qui a déjà été enregistré. |

---

## 7. Traçabilité

**Chaque modification du dossier est enregistrée — qui, quoi, quand — et le registre ne peut pas
être retouché.**

Le journal d'activité est consultable par l'administrateur du cabinet. Il couvre les créations,
les modifications et les suppressions, avec le nom de l'auteur et l'horodatage.

| | |
|---|---|
| **Registre chaîné** | Chaque entrée porte une signature cryptographique calculée à partir d'elle-même et de celle qui la précède, au moyen d'un secret que la base de données ne contient pas. Modifier ou supprimer une ligne rompt la chaîne, et la rupture est détectable — y compris par quelqu'un disposant d'un accès complet à la base. |
| **Aucune API d'écriture** | Le journal n'expose aucun moyen de créer, modifier ou supprimer une entrée. Il ne s'écrit qu'automatiquement, en marge des opérations qu'il décrit. |
| **Les exports complets sont nominatifs** | Le téléchargement de l'intégralité du dossier d'un cabinet exige une nouvelle saisie du mot de passe et fait l'objet d'un enregistrement nominatif. Si cet enregistrement ne peut pas être écrit, le téléchargement n'a pas lieu. |
| **Aucune donnée patient dans les journaux techniques** | Les journaux d'exploitation ne comportent ni nom, ni élément identifiant. Un contrôle automatisé le vérifie à chaque livraison. |

---

## 8. Sauvegardes et récupération

**Une sauvegarde qu'on n'a jamais pu relire n'est pas une sauvegarde. Chacune des nôtres est
vérifiée.**

| | |
|---|---|
| **Automatiques, sans intervention** | Aucune manipulation n'est demandée au cabinet. Sur une installation locale, l'heure de passage est réglable et une alerte signale toute sauvegarde qui n'aurait pas abouti. |
| **Chiffrées avant de quitter le serveur** | La copie hors site et le flux de restauration en continu sont chiffrés à la source. Une copie interceptée ou égarée reste illisible. |
| **Vérifiées à chaque passage** | Chaque sauvegarde est relue et contrôlée immédiatement après son écriture. Un échec de cette vérification fait échouer la sauvegarde plutôt que de la déclarer réussie. |
| **Points de restauration quotidiens** | Une copie des enregistrements de votre cabinet est constituée chaque jour et les sept dernières sont conservées, ce qui permet de revenir en arrière sur une suppression accidentelle sans interrompre le service. |
| **Restauration additive** | Remettre une sauvegarde en place réintègre ce qui manque sans écraser ce qui a été saisi depuis. Le travail réalisé après la copie est préservé. |

---

## 9. Vos données restent les vôtres

**À tout moment, sans nous solliciter et sans justification, vous pouvez sortir l'intégralité de
votre dossier.**

| | |
|---|---|
| **Exports courants** | Patients, factures, devis, caisse, créances, stock, prothèses et agenda s'exportent en CSV, lisible par Excel et par votre comptable. L'export respecte les filtres affichés à l'écran et porte sur l'ensemble des résultats, pas sur la page en cours. |
| **Archive complète du cabinet** | Un seul téléchargement produit une archive contenant l'ensemble de vos enregistrements et de vos fichiers. C'est une copie complète et autonome de votre dossier. |
| **Documents officiels** | Ordonnances, certificats, bulletins de soins CNAM, arrêts de travail et notes d'honoraires sont produits en PDF et conservés avec le dossier du patient. |
| **Aucune donnée n'est effacée sans vous** | La suppression d'un patient est refusée dès lors que des éléments y sont rattachés ; l'archivage tient lieu de mise à l'écart. Le dossier médical ne disparaît pas par inadvertance. |

---

## 10. Hébergement et destination des données

**Où vos données résident est une décision contractuelle, et le logiciel la fait respecter
techniquement.**

L'application tient une liste des destinations autorisées pour toute donnée sortante —
hébergement, sauvegarde hors site, services de messagerie. **Elle refuse de démarrer si une
destination non déclarée est configurée.** Un transfert vers un service tiers ne peut donc pas
être ajouté discrètement.

Le lieu d'hébergement retenu pour votre cabinet, ainsi que les sous-traitants techniques auxquels
nous recourons, figurent dans votre contrat de service. Nous nous inscrivons dans le cadre de la
**loi organique n° 2004-63** relative à la protection des données à caractère personnel et des
obligations de l'INPDP en matière de données de santé.

> L'application ne transmet aucun texte rédigé dans votre cabinet — note clinique, nom de patient,
> antécédent — à un service d'intelligence artificielle ou d'analyse tiers. Aucune fonctionnalité
> du produit ne le fait.

---

## 11. Ce qui relève de votre cabinet

**La sécurité est partagée. Voici les cinq points sur lesquels nous comptons sur vous.**

- **Un compte par personne.** Un compte partagé rend le journal d'activité inexploitable : on sait
  ce qui a été fait, mais plus par qui.
- **Conservez vos codes de secours.** Ils ne sont affichés qu'une fois. Rangez-les hors du
  téléphone qui génère les codes.
- **Désactivez les comptes des partants.** L'effet est immédiat, mais l'action vous appartient.
- **Verrouillez les postes de la salle de soins.** Une session ouverte sur un écran visible du
  patient reste le point d'exposition le plus courant d'un cabinet.
- **Téléchargez une archive de temps en temps.** Nous sauvegardons pour vous ; une copie que vous
  détenez vous-même reste la meilleure garantie d'indépendance.

---

## 12. Nous contacter

Pour toute question relative à ce document, à un incident de sécurité, ou pour une demande
formulée par un patient au titre de la protection de ses données, adressez-vous à votre
interlocuteur habituel. Nous répondons aux demandes documentées et vous accompagnons dans les
échanges avec l'INPDP si votre cabinet est sollicité.

---

*Ce document décrit les protections en vigueur à sa date de publication. Il est revu à chaque
évolution significative du produit et la version applicable vous est communiquée.*

*Sécurité du dossier patient — version 1.0, 16 août 2026.*
