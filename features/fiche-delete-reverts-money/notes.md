# Supprimer une fiche annule ce qu'elle a encaissé, et le dit avant

> Shipped 2026-09-21. The plan and the risk analysis are in [`plan.md`](plan.md); this is what was decided and
> why, written for whoever meets the area next.

## Un patient a payé 160 DT pour une extraction à 80 DT

Rendez-vous `c70bc275`, devis 2026-0004, Cabinet de Parodontologie Dr Khaireddine Hamdane, 14/09/2026 —
mesuré en lecture seule sur la production le 21/09 :

| Heure | Action | Argent |
|---|---|---|
| 13:40:35 | fiche `02fa01a9` enregistrée | note **2026-0038**, 80 DT, encaissée |
| 13:47:09 | fiche `02fa01a9` **supprimée** | la note reste **Payée** |
| 13:48:18 | fiche `dd264cbc` enregistrée | **+80 DT** sur l'échéancier |
| 13:49:09 | fiche `dd264cbc` **supprimée** | les 80 DT **restent** |
| 13:49:23 | fiche `3fdd3e59` enregistrée | **+80 DT** encore |

Six écritures, six réponses 200, aucune erreur. Le praticien l'a découvert **par hasard** une semaine plus
tard, en retirant un acte du devis : le refus « X DT ont déjà été encaissés sur ce devis, pour un total de
Y DT » — qui est `TreatmentPlan.EnsureTotalCoversCollected` faisant exactement son travail.

⚠️ **Le refus n'était pas le bug, c'était le seul symptôme.** Sans ce retrait d'acte, personne n'aurait
jamais vu les 80 DT en trop. C'est la raison d'être de la vérification ajoutée à `reconcile-money`.

## La cause : six liens souples, trois nettoyés

`DeleteDentalRecordCommandHandler` nettoyait l'acte du devis, l'étape du devis et la provenance des lignes de
facture. Son propre commentaire disait « **the two** soft links to this fiche ». Il y en a six, et les trois
qu'il ignorait sont ceux qui portent l'argent et les documents.

Le mécanisme n'est pas l'argent, c'est **l'identité** : `InstallmentPayment.DentalRecordId` est la clé
d'idempotence de `TreatmentPlan.CollectedOnRecord` — ce qui fait qu'un ré-enregistrement de fiche ne prend
l'argent qu'une fois. Supprimez la fiche, la fiche suivante a un id neuf, la clé repart de zéro, et le premier
paiement reste.

## Ce qui a été décidé

> **Supprimer une fiche annule tout ce que cette fiche a produit, et le praticien est averti avant, avec les
> montants nommés.**

| Ce que porte la fiche | À la suppression | Pourquoi |
|---|---|---|
| Encaissements sur un devis | annulés, motif « Fiche de soins du JJ/MM/AAAA supprimée » | la ligne est **gardée et marquée**, avec l'auteur ; rien n'est effacé |
| Note **brouillon** | supprimée | aucun numéro n'a été consommé — `Invoice.Cancel` refuse un brouillon en toutes lettres |
| Note **numérotée** | paiements annulés → `Cancel` | le numéro reste, le document lit « Annulée » : **la séquence ne perd aucun rang** |
| Ordonnances | **conservées**, pointeur vidé | voir plus bas |
| Actes / étapes du devis | inchangé (déjà fait) | |
| Odontogramme, actes, dents | inchangé — vraies FK en CASCADE | |

⚠️ **Annuler une note numérotée est légitime, et `Invoice.Cancel` le dit lui-même** : « *Voided payments do
not count: a note whose only payments were data-entry errors was never really paid, so cancelling it is
legitimate.* » D'où l'ordre : on annule les paiements, **puis** la note. L'inverse est refusé par l'agrégat.

⚠️ **La cascade n'ajoute aucun pouvoir financier nouveau.** `VoidInstallmentPaymentCommand` n'a aucune limite
de date et le bouton « Annuler l'encaissement » existe depuis toujours — c'est exactement le geste que le
praticien a fini par faire à la main. La cascade le fait au bon moment, avec un motif qui s'explique.

## Les trois refus, et pourquoi ils refusent plutôt que d'essayer

Tous les trois sont évalués **avant l'ouverture de la transaction**. De l'argent à moitié défait est le seul
résultat que personne ne peut lire.

| Refus | Ce qui arriverait sans lui |
|---|---|
| **Chèque déjà encaissé en banque** | ni `Installment.VoidPayment` ni `Invoice.VoidPayment` ne l'interdisent : on sortirait de la caisse un chèque physiquement encaissé |
| **Avoir déjà établi** | `Invoice.VoidPayment` refuse **par paiement**, au milieu de la cascade ; la question posée d'avance sur l'ensemble transforme un plantage à mi-chemin en un refus qui nomme le remède |
| **Note facturant une autre séance** | 0 cas dans le produit et 0 en base — donc la garde est gratuite, et le jour où une note portera deux sources, annuler tout effacerait une facturation légitime |

## Le pont devis → facture, qui est le piège le plus vicieux

`PlanBillingRules.BilledPlanIds` retire un devis entier de « Solde patient », « Créances », la caisse et le
dashboard dès qu'une note vivante le nomme. Annuler une note **pontée** sans relâcher le lien ferait
**réapparaître le total complet du devis** dans ces quatre lectures — alors que l'argent reporté ne revient
pas, `Invoice.Cancel` étant formel : « les encaissements du devis y ont été reportés à l'émission et ne
repartent pas en arrière ». Le patient semblerait redevoir tout le traitement.

**Les deux moitiés sont relâchées**, jamais une seule : `Invoice.DetachFromTreatmentPlan` *et*
`TreatmentPlan.DetachNote`. Relâcher un seul côté est précisément comment ce lien est devenu write-once.
Les actes libérés restent à **0** — `DetachNote` dit pourquoi : rien ici ne sait ce qu'ils valent, et inventer
un prix est la décision du dentiste.

## L'ordonnance survit, délibérément

« Comme si la fiche n'avait jamais existé » s'arrête ici, et c'est un choix. Une fiche **n'efface jamais** son
ordonnance : le papier est peut-être déjà chez le patient, et la détruire est son propre verbe `AdminOrDoctor`
pris exprès, pas un effet de bord d'un rangement.

Ce qui ne peut pas survivre, c'est le **pointeur** : « Modifier » route sur `MedicalDocumentDto.DentalRecordId`
parce qu'un document appartenant à une fiche est recomposé au prochain enregistrement de cette fiche — et une
fiche supprimée n'en a pas. `MedicalDocument.ReleaseFromDentalRecord()` le vide ; le document redevient
ordinaire et reste modifiable.

## L'avertissement partage le code de la suppression

`DentalRecordDeletionReversal.InspectAsync` **décide**, `ApplyAsync` exécute ce qu'elle a décidé, et
`GET /patients/{id}/dental-records/{id}/deletion-preview` sert la même décision à la modale. Un avertissement
assemblé à partir de sa propre relecture de la base est libre de mentir — « 80 DT » à l'écran et 160 DT
annulés, ou un refus que la modale n'a jamais mentionné.

⚠️ **La modale promettait le contraire de ce qui se passe maintenant.** Elle disait « la note d'honoraires,
son numéro et son montant ne changent pas : seul le lien vers la fiche est retiré ». Garder cette phrase aurait
été pire que ne rien dire.

⚠️ **« Supprimer » est désactivé tant que l'aperçu n'est pas arrivé**, et absent sur un refus (« Retour » est
alors la seule sortie). Presser avant l'aperçu, c'est presser sans avoir été prévenu du coût — le défaut même
qu'on corrige.

⚠️ La preview est gardée `AdminOrDoctor`, **comme le verbe qu'elle prévisualise** : elle nomme des montants
encaissés et le numéro d'une note.

## Les gardes laissées derrière

- **`Every_Soft_Link_To_A_Fiche_Is_Accounted_For`** — réflexion sur `Domain/Entities` pour tout
  `DentalRecordId` / `LinkedDentalRecordId`, comparé dans **les deux sens** à une liste écrite à la main.
  Un septième lien casse le test le jour où il est déclaré. C'est le test qui aurait trouvé ce défaut des
  années plus tôt.
- **`reconcile-money` → `no-money-without-a-fiche`** — le rapport était vert pendant toute la période où le
  patient payait deux fois, parce que chaque grand livre s'accordait avec lui-même. « Cet argent pointe-t-il
  vers une fiche qui existe » n'était simplement jamais demandé.

## Ce qui reste en production, et qui n'est pas à nous

Le correctif ne répare rien de rétroactif.

| Cas | Montant | Qui |
|---|---|---|
| Note 2026-0038 double-facture la ligne « Extraction simple » du devis 2026-0004 | **80,000 DT** | avoir, par le praticien |
| 4 autres notes sans fiche (dont 2 déjà annulées à 0) | 4 580,000 DT | à vérifier une par une — **pas** des doubles facturations |
| 1 encaissement devis pointant une fiche supprimée | 80,000 DT | légitime : c'est le vrai paiement de l'acte, seule la provenance pend |

⚠️ Enquête menée **uniquement en `SELECT`** sur la production. Aucune écriture, jamais.
