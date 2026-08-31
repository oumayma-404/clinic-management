# Conservation et effacement des données

**Version 1.0 — 2026-08-31.** À relire avec un conseil juridique tunisien avant d'être annexé à un contrat.

Ce document répond à une question qu'un contrôle INPDP pose tôt : **combien de temps gardez-vous quoi, et
pourquoi ?** Il est écrit à partir de ce que le code fait réellement — chaque durée ci-dessous est vérifiable
dans une classe nommée — et il sépare ce que le logiciel **borne déjà** de ce que le **cabinet doit décider**.

> ⚠️ `GO-LIVE.md:286` porte cette tâche depuis le début, non cochée : « Define **retention and deletion** — note
> the product has **no patient merge and no soft delete**… Your policy has to match what the software actually
> does. » Ce fichier est la moitié « ce que le logiciel fait ». L'autre moitié — la durée que le cabinet retient
> pour le dossier médical — ne peut être écrite que par le cabinet et son conseil.

---

## 1. Ce que le logiciel borne déjà, tout seul

| Donnée | Durée | Où c'est écrit |
|---|---|---|
| Rappels envoyés (`Notification`, dont le texte porte le nom du patient) | **90 jours**, lignes terminales seulement | `NotificationJob` · `RemindersConfig` |
| Notifications poussées vers les téléphones (`PushDelivery`) | **30 jours**, lignes terminales seulement | `PushDispatchJob` · `PushConfig` |
| Inscriptions de cabinet non confirmées (nom, e-mail, téléphone, adresse d'un prospect) | **30 jours** | `SignUpClinicCommand` |
| Demandes de réinitialisation de mot de passe | **30 jours** après consommation | `RequestPasswordResetCommand` |
| Pièces jointes des documents envoyés par e-mail | supprimées dès que l'envoi est terminal | `DocumentEmailJob` |
| Sauvegardes locales | par nombre (`Clinic.BackupRetentionCount`) | `BackupJob` |
| Points de restauration | 7 conservés | `ClinicRecoveryPointJob` |
| Journaux techniques du serveur | **30 jours** (`Serilog:RetainedFileCountLimit`) | `Program.cs` |
| Autorisation de copie d'un poste | **90 jours sans usage** | `ClinicArchiveGrant.IdleLifetime` |

Ces durées sont appliquées par des tâches de fond ; elles n'exigent aucune action du cabinet.

---

## 2. Ce qui n'est borné par rien — et la position à tenir

| Donnée | État | Position proposée |
|---|---|---|
| **Dossier patient** (identité, antécédents, allergies, odontogramme, fiches de soins, documents, fichiers) | conservé sans limite | **C'est le bon comportement par défaut.** Un dossier médical se conserve : l'effacer expose le cabinet en cas de litige et prive le patient de son propre historique. Ce qu'il faut, ce n'est pas une purge — c'est une **durée écrite**, décidée par le cabinet avec son conseil, et un geste pour l'appliquer quand elle échoit. Ce geste n'existe pas encore (§ 4). |
| **Factures, règlements, plans de traitement** | conservés sans limite | Alignés sur les obligations comptables et fiscales du cabinet, qui sont plus longues que la plupart des durées médicales. À conserver. |
| **Journal d'activité** (`AuditEntry`) | conservé sans limite | **À conserver au moins aussi longtemps que le dossier qu'il décrit.** Un journal purgé avant son dossier laisse un dossier dont plus personne ne peut dire qui l'a modifié — c'est précisément ce que le chaînage cryptographique existe pour empêcher. ⚠️ Le journal **contient des noms** : la ligne d'une suppression enregistre les valeurs identifiantes de l'enregistrement supprimé. Effacer un patient laisse donc son nom dans un registre inaltérable et permanent. C'est un fait à assumer par écrit, pas à découvrir pendant un contrôle. |
| **Journal d'accès de l'éditeur** (`PlatformAccessEntry`) | conservé sans limite | À conserver : c'est la preuve de ce que le sous-traitant a regardé. |
| **Corps des e-mails de documents** (`DocumentEmail.Body`, destinataire, objet) | conservé sans limite | **À borner.** La pièce jointe est déjà supprimée à l'envoi ; garder indéfiniment le corps et l'adresse du destinataire n'apporte rien après quelques mois. Une durée de 12 mois, alignée sur les rappels, serait cohérente. Non implémenté (§ 4). |

---

## 3. Effacement : ce que le logiciel permet réellement

- **Un patient jamais soigné peut être supprimé.**
- **Un patient rattaché à quoi que ce soit ne peut pas l'être** — la suppression est refusée dès qu'un
  rendez-vous, une facture, une fiche, un document, un fichier, un antécédent, un signalement, un bon de
  laboratoire ou une entrée de liste d'attente y est rattaché (15 catégories, `PatientDeletionBlockers`).
  En pratique : **tout patient venu au fauteuil est indélébile.**
- **L'archivage tient lieu de mise à l'écart** : le patient quitte les listes, les recherches et les sélecteurs,
  et rien n'est détruit. C'est réversible.
- **Il n'existe aucune anonymisation.** Ni pseudonymisation, ni caviardage.
- **Un effacement n'atteint pas les sauvegardes ni les points de restauration** — par construction, et cela doit
  être dit au patient qui le demande plutôt que découvert ensuite.

C'est une position défendable pour un dossier médical, mais **elle doit être écrite** : un patient qui demande
l'effacement a droit à une réponse motivée, pas à un silence.

---

## 4. Ce qui manque pour tenir cette politique

Un aveu, pas une liste de souhaits. Rien de ce qui suit n'existe aujourd'hui.

1. **Aucun geste n'applique une durée au dossier patient.** Quand la durée retenue échoit, il n'y a rien à
   cliquer. Il faudrait au minimum un rapport « dossiers dont la durée est échue » avant d'envisager une purge.
2. **Aucune anonymisation.** C'est la seule réponse possible à une demande d'effacement qu'on ne peut pas
   satisfaire par la suppression — remplacer l'identité et garder les actes.
3. **Aucun export du dossier d'un seul patient.** Le droit d'accès ne peut donc pas être servi par une
   fonctionnalité : il faut assembler à la main depuis une dizaine d'écrans. C'est aussi ce qu'un patient
   demande couramment en changeant de dentiste.
4. **Aucun consentement n'est enregistré**, nulle part dans le modèle. Enregistrer un numéro de téléphone
   inscrit automatiquement le patient aux rappels SMS/WhatsApp, et **ni le patient ni le cabinet ne peuvent en
   exempter quelqu'un**.
5. **Le corps des e-mails de documents n'est jamais purgé** (§ 2).

Les points 2, 3 et 4 exigent une migration de schéma et sont suivis dans
`follow-up/security-remediation-progress.md`.

---

## 5. À remplir par le cabinet

| Question | Réponse du cabinet |
|---|---|
| Durée de conservation du dossier médical après le dernier soin | _(à décider avec le conseil de l'ordre et un conseil juridique)_ |
| Durée de conservation des pièces comptables | _(obligations fiscales tunisiennes)_ |
| Qui répond à une demande d'accès, de rectification ou d'opposition | _(nom, fonction)_ |
| Délai de réponse visé | _(la loi fixe un délai ; le respecter suppose de savoir qui s'en charge)_ |
| Où est consignée la décision quand un effacement est refusé | _(un refus motivé doit être traçable)_ |
