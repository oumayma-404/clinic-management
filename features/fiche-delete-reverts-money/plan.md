# Supprimer une fiche de soins annule tout ce qu'elle a produit

> Statut : **implémenté le 2026-09-21** — voir [`notes.md`](notes.md) pour ce qui a été décidé.
> Écrit après l'enquête du 2026-09-21 sur la plainte du
> Cabinet de Parodontologie et Implantologie Orale Dr Khaireddine Hamdane.

## 1. Ce qui s'est passé (mesuré sur la production, en lecture seule)

Un patient a payé **160 DT pour une extraction à 80 DT**, et personne ne pouvait le voir.

Rendez-vous `c70bc275`, devis 2026-0004, 14/09/2026 — trois fiches pour une seule séance :

| Heure | Action | Conséquence argent |
|---|---|---|
| 13:40:35 | fiche `02fa01a9` enregistrée | note **2026-0038**, 80 DT, **encaissée** |
| 13:47:09 | fiche `02fa01a9` **supprimée** | la note **reste payée** (seule la provenance des lignes est vidée) |
| 13:48:18 | fiche `dd264cbc` enregistrée | **+80 DT** sur l'échéancier du devis |
| 13:49:09 | fiche `dd264cbc` **supprimée** | les 80 DT **restent** |
| 13:49:23 | fiche `3fdd3e59` enregistrée | **+80 DT** encore sur l'échéancier |

Le 21/09 à 11:09, le praticien a annulé *un* des trois (« erreur de saisie »), puis a retiré un acte du
devis à 11:10 — et c'est ce retrait qui a déclenché la plainte, avec le refus
« X DT ont déjà été encaissés sur ce devis, pour un total de Y DT ».

⚠️ **Ce refus est correct.** `TreatmentPlan.EnsureTotalCoversCollected` a fait son travail. C'est le
symptôme qui a révélé le défaut, pas le défaut.

**État actuel du devis 2026-0004 : 200 prévu / 200 d'échéances / 200 encaissé — équilibré.**
**Ce qui reste faux : la note 2026-0038 (80 DT, Payée) facture le même acte que la ligne du devis.**

## 2. La cause

`DeleteDentalRecordCommandHandler`
(`api/ClinicManagement.Application/Features/Patients/Commands/DeleteDentalRecordCommand.cs:78`)
nettoie **trois** liens souples vers la fiche : l'acte du devis, l'étape du devis, la provenance des lignes
de facture. Son propre commentaire dit « the two soft links to this fiche ». **Il y en a six.**

| Lien (sans FK) | Nettoyé aujourd'hui ? | Orphelins en prod |
|---|---|---|
| `TreatmentPlanItem.LinkedDentalRecordId` | oui | 0 |
| `TreatmentPlanItemStep.LinkedDentalRecordId` | oui | 0 |
| `InvoiceLine.DentalRecordId` | oui | 0 |
| **`InstallmentPayment.DentalRecordId`** | **non** | **1 — 80,000 DT vivants** |
| **`Invoice.DentalRecordId`** (en-tête) | **non** | **5 — 4 660,000 DT encaissés** |
| `MedicalDocument.DentalRecordId` | **non** | 0 (12 documents exposés) |

`ToothStates`, `DentalRecordTeeth` et `DentalRecordActs` ont une **vraie FK en CASCADE** — ils se nettoient
tout seuls, ce qui explique leurs 0 orphelins.

Et le coup de grâce : `InstallmentPayment.DentalRecordId` **est la clé d'idempotence** de l'encaissement
(`TreatmentPlan.CollectedOnRecord`, `TreatmentPlan.cs:488`). Fiche supprimée → nouvelle fiche → nouvel id →
**on encaisse une deuxième fois**, sans erreur nulle part.

## 3. La règle à implémenter

> Supprimer une fiche annule tout ce que cette fiche a produit. Rien ne pend, aucun argent ne reste
> réclamé. Le praticien est **averti avant**, avec les montants et les documents nommés.

| Ce que porte la fiche | À la suppression |
|---|---|
| Encaissements sur un devis | annulés (`VoidPayment`, motif « fiche de soins du JJ/MM/AAAA supprimée ») |
| Note **brouillon** (sans numéro) | supprimée |
| Note **numérotée** | paiements annulés → `Invoice.Cancel(motif)`. Le numéro reste, le document lit « Annulée » |
| Actes / étapes du devis | déjà repassés en « prévu » — inchangé |
| Odontogramme, actes de la fiche | déjà en CASCADE — inchangé |
| Ordonnances | **le pointeur est vidé, le document reste** — voir R8 |

⚠️ `Invoice.Cancel` sanctionne déjà exactement cet enchaînement, dans son propre commentaire :
« *Voided payments do not count: a note whose only payments were data-entry errors was never really paid,
so cancelling it is legitimate.* » Le numéro n'est jamais supprimé, donc **la séquence reste sans trou**.

## 4. Blast radius

| # | Ce qu'on touche | Ce que c'est | Autres consommateurs | Verdict |
|---|---|---|---|---|
| 1 | `DeleteDentalRecordCommandHandler` | le seul point d'entrée de la suppression | `DentalRecordsController.DeleteDentalRecord` (AdminOrDoctor) | **must change** |
| 2 | `TreatmentPlan.VoidInstallmentPayment` | opération existante | `VoidInstallmentPaymentCommand` (bouton « Annuler l'encaissement ») | unaffected — nouvel appelant, même méthode |
| 3 | `Invoice.VoidPayment` | exige `creditedTotal` | `VoidInvoicePaymentCommand` | **must change** — le handler doit charger les avoirs |
| 4 | `Invoice.Cancel` | garde « paiements vivants » | `CancelInvoiceCommand` | unaffected — on annule les paiements d'abord |
| 5 | `CollectedOnRecord` | clé d'idempotence | `DentalRecordTreatmentCollection` | unaffected — le comportement redevient correct |
| 6 | Lectures caisse / créances / solde patient / dashboard | filtrent `!IsVoided` | 8 requêtes | **must re-test** |
| 7 | `PlanBillingRules.BilledPlanIds` | exclut un devis représenté par une note | solde patient, créances, caisse, dashboard | **must re-test** — voir R3 |
| 8 | Nouveau read « que va annuler cette suppression ? » | pour l'avertissement | modale de suppression | **must change** — nouveau |
| 9 | `reconcile-money` | rapport de dérive | verbe console | **must change** — voir R11 |

## 5. Ce qui peut mal tourner

Classé par gravité. **126 fiches portent de l'argent en production** — cette cascade se déclenchera souvent.

### R1 — Un chèque déjà encaissé en banque — CRITIQUE

`Installment.VoidPayment` et `Invoice.VoidPayment` **n'interdisent pas** d'annuler un paiement dont le
chèque porte `ChequeBankedOn`. La cascade retirerait donc de la caisse un chèque physiquement encaissé.

**Garde : refuser la suppression si un paiement à annuler porte `ChequeBankedOn`**, en nommant le chèque.
*(0 cas aujourd'hui — tous les encaissements liés à une fiche sont en espèces. La garde est pour demain.)*

### R2 — Un avoir déjà établi — CRITIQUE

`Invoice.VoidPayment(…, creditedTotal, …)` refuse si l'encaissé tomberait sous les avoirs déjà émis.
Le handler **injecte déjà `ICreditNoteRepository`** mais ne s'en sert que pour le garde-fou de facturation.

**Garde : charger Σ avoirs non annulés et le passer ; si l'annulation est refusée, refuser la suppression
avec la phrase de l'agrégat.** *(0 avoir en prod aujourd'hui.)*

### R3 — Le pont devis → facture — CRITIQUE, et le plus vicieux

`Invoice.Cancel` le dit lui-même : « *pour une facture issue d'un devis, les encaissements du devis y ont
été reportés à l'émission et ne repartent pas en arrière* ». Annuler une note **pontée**
(`TreatmentPlanId` non nul) fait ressortir le devis entier de `PlanBillingRules.BilledPlanIds` :
**son total complet revient dans « Solde patient » et « Créances » alors que l'argent reporté ne revient
pas au devis.** Le patient semble redevoir tout le traitement.

**3 notes sont dans cet état en production.**

**Décision requise** : détacher la note du devis avant de l'annuler (`Invoice.DetachFromTreatmentPlan`,
`Invoice.cs:308`), ou refuser la suppression pour ce cas. À ne pas trancher au clavier.

### R4 — La caisse d'un jour déjà lu — ÉLEVÉ

Annuler sort l'argent de l'extrait du **jour du paiement**, pas du jour d'aujourd'hui. Le principe est
accepté, mais l'avertissement **doit nommer le jour** (« cet argent sortira de la caisse du 14/09 »).

Note : le produit permet déjà exactement ce geste, sans aucune limite de date, depuis l'échéancier — la
cascade n'ajoute **aucun pouvoir financier nouveau**, elle fait au bon moment ce que l'utilisateur ferait
à la main.

### R5 — Le motif d'annulation ne doit pas être de la prose — ÉLEVÉ

CLAUDE.md : « Never recover an outcome by matching French prose ». Le motif est lisible par un humain,
mais tout code qui doit reconnaître « annulé par une suppression de fiche » doit brancher sur autre chose
qu'un `Contains("supprimée")`.

### R6 — Échec partiel — ÉLEVÉ

La cascade touche trois agrégats (devis, facture, fiche). Elle doit rester **dans la transaction
existante** (`BeginTransactionAsync` est déjà là). Ne jamais appeler au milieu un handler MediatR qui fait
son propre `SaveChangesAsync` — c'est le piège que `AmendTreatmentPlanCommand` documente.

### R7 — L'avertissement a besoin d'un read qui n'existe pas — ÉLEVÉ

Pour dire « 80 DT sur le devis 2026-0004 + note 2026-0038 », le navigateur doit pouvoir le demander.
Sans ce read, la cascade est **silencieuse** — exactement ce qu'on a dit qu'on ne ferait pas.
**Le read est dans le périmètre, pas en follow-up.**

### R8 — Les ordonnances — MOYEN

CLAUDE.md est explicite : la fiche **n'efface jamais** son ordonnance — « le papier est peut-être dans la
main du patient », et `DeleteMedicalDocumentCommand` est `AdminOrDoctor`. Donc « comme si la fiche n'avait
jamais existé » ne peut pas inclure l'ordonnance sans renverser une décision écrite.

**Proposition : vider `MedicalDocument.DentalRecordId` (sinon « Modifier » ne route plus, cf. CLAUDE.md)
et laisser le document.** 12 documents concernés. **Décision requise.**

### R9 — Concurrence — MOYEN

La cascade écrit sur le devis et la facture : tout formulaire ouvert dessus prendra un 409 au prochain
enregistrement. C'est le comportement voulu. Mais la suppression elle-même ne round-trip aucune version
(`Version == 0` = « non fourni »), donc elle ne sera jamais refusée pour conflit. Acceptable, à dire.

### R10 — Non-risques vérifiés

- **Pas de trou de numérotation** : un brouillon n'a pas de numéro ; une note numérotée garde le sien.
- **Σ échéances == TotalPlanned tient** : annuler baisse `AmountPaid`, jamais `Amount`.
- **Pas d'escalade de privilège** : la suppression est déjà `AdminOrDoctor`.
- **Pas de note multi-fiches** : 0 en production, donc pas de cas « annuler une note qui facture aussi une
  autre séance ». À **garder comme garde** malgré tout (refuser si la note porte une autre fiche).

### R11 — `reconcile-money` est aveugle à cette famille — MOYEN

Il vérifie total vs échéancier, avoirs excédentaires, devis à plusieurs notes — **pas** « cet argent
pointe-t-il vers une fiche qui existe ». Ajouter la vérification, sinon la classe reste indétectable.

## 6. Ordre de travail

1. Le read « ce que la suppression va annuler » (R7) — montants, devis, note, jour de caisse.
2. Les gardes qui **refusent** : chèque encaissé (R1), avoir bloquant (R2), note multi-fiches (R10).
3. La cascade dans la transaction existante (R6) : encaissements devis → paiements note → note.
4. R3 selon la décision.
5. La modale d'avertissement (en `sheet` sur mobile, cf. `.claude/rules/frontend-web.md`).
6. Le garde dérivé : un test qui énumère les liens souples vers `DentalRecord` et casse au septième.
7. `reconcile-money` (R11).

## 7. Vérification

- Suite complète `dotnet test` (jamais `--filter`), puis `check:responsive` + `tsc --noEmit` + `build`.
- `verify-schema` avant/après (aucune migration attendue, sauf décision sur R5).
- Parcours navigateur : supprimer une fiche avec argent devis / avec note brouillon / avec note numérotée /
  sans argent, puis relire caisse, solde patient, créances, dashboard (ligne 6 du blast radius).
- **Sur la base de dev, jamais sur la production.**

## 8. Les données déjà en production

Le correctif **ne répare rien de rétroactif**. À traiter par le praticien, dans l'application :

| Cas | Montant | Action |
|---|---|---|
| Note 2026-0038 (Hamdane) double-facture la ligne du devis 2026-0004 | 80,000 DT | avoir, par le praticien |
| 4 autres notes sans fiche | 4 580,000 DT | à vérifier une par une — **pas** des doubles facturations |
| 1 encaissement devis pointant une fiche supprimée | 80,000 DT | légitime (c'est le vrai paiement de l'acte) ; seule la provenance pend |

⚠️ Aucune écriture sur la production par l'assistant, jamais. Enquête menée uniquement en `SELECT`.
