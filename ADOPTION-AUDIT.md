# Adoption audit — the view from a Tunisian dentist

Read-only exploration of the shipped code, assessed as a small Tunisian dental practice
deciding whether to adopt this product. Every finding below was verified against source;
anchors are `path:line`. Nothing here is inferred from names, comments or `CLAUDE.md`.

**Verdict.** The clinical spine is genuinely closed and usable day to day. The money side
has real leaks that cost dinars. CNAM — the single reason a Tunisian practice would buy
this rather than a generic agenda — is a printer, not a tracker.

---

## 1. What actually works, end to end

| Loop | Status | Anchor |
|---|---|---|
| RDV → agenda → statuts (6, with a real transition table) | closed | `Domain/Entities/Appointment.cs:141` |
| Fiche de soins → odontogramme (acts chart teeth, treating a tooth closes its diagnosis) | closed | `Features/Patients/DentalRecordActParser.cs:73` |
| Odontogramme → devis → planification → retour à l'état de l'acte | closed, état **derived** not stored | `Features/TreatmentPlans/TreatmentPlanWorkflowProjection.cs:55` |
| Fiche → stock (décrément automatique, FEFO, delta seul à l'édition) | closed | `Common/Services/StockConsumptionService.cs:98` |
| Salle d'attente → RDV (promote-and-book) | closed | `web/app/waiting-list/page.tsx:298` |
| Bons de prothèse — cycle de vie des étapes | closed (but see §5) | `Domain/Entities/LabWorkOrder.cs:106` |
| Documents (ordonnance, certificat, liaison, BS1, arrêt de travail) — liste par patient, réimpression | closed | `web/app/patients/[id]/page.tsx:1267` |
| Avoirs + annulation d'un paiement de facture | reachable in UI | `web/components/factures/invoices-table.tsx:392` |

**Tunisian money arithmetic is correct.** TVA 7 % sur le HT seul, timbre 1,000 DT ajouté au
TTC et **exclu de la base TVA**, arrondi au millime (3 décimales) partout, réglages figés à
l'émission. Numérotation `AAAA-NNNN` sans trou, par clinique et par année, l'année venant de
`ClinicClock` (UTC+1) et non d'UTC.
`Domain/Services/InvoiceCalculator.cs:26`, `Features/Billing/Commands/IssueInvoiceCommand.cs:84`,
`Domain/Entities/Clinic.cs:180`, `web/lib/format.ts:25`

---

## 2. Money leaks — these cost dinars

### 2.1 🔴 Money added to an already-billed fiche never reaches la caisse
Fiche à 400 DT, 200 encaissés → facture de 200. Le patient règle le solde la semaine
suivante, le praticien passe « Montant payé » à 400. La fiche affiche 400 ; la facture, la
caisse, le solde patient et le tableau de bord restent à 200. Le toast est un succès vert
ordinaire — l'échec est délibérément muet.
`Common/Services/DentalRecordAutoBilling.cs:86`, `web/components/patient-record-modal.tsx:523`
Rien ne réconcilie `DentalRecord.AmountPaid` avec `Invoice.AmountCollected`.

### 2.2 🔴 L'auto-facturation force « espèces »
`Method = nameof(PaymentMethod.Cash)`, sans champs chèque, alors que la commande sous-jacente
les accepte. Une séance réglée par chèque est comptée dans « dont espèces » et n'apparaît
jamais dans « Chèques à encaisser ». Seule la boîte de dialogue explicite « Facturer cette
intervention » permet de choisir le mode.
`Common/Services/DentalRecordAutoBilling.cs:64`

### 2.3 🔴 Un paiement d'échéancier ne peut pas être annulé depuis l'interface
`VoidInstallmentPaymentCommand` existe, l'endpoint existe, le wrapper client existe
(`web/lib/api/treatment-plans.ts:194`) — **zéro appelant** dans tout `web/`. Un reçu de devis
mal saisi est définitif : l'argent reste dans la caisse et dans les chèques à encaisser pour
toujours. L'équivalent sur une *facture* est, lui, accessible.

### 2.4 🔴 Une facture-passerelle (devis → facture) ayant repris un paiement est incannulable
`Invoice.Cancel` refuse toute facture portant un paiement non annulé, et les échéances
reprises sont non annulées par construction. Le commentaire au-dessus du code affirme
l'inverse (« self-correcting — cancelling the bridge hands the money straight back to the
plan track »). Une passerelle émise sur le mauvais devis est irrécupérable ; seul un avoir
reste possible, et il ne rend pas l'argent au devis.
`Domain/Entities/Invoice.cs:416`, `Features/Billing/Commands/IssueInvoiceCommand.cs:184`

### 2.5 🔴 Les chèques n'ont pas de notion d'encaissement
`Payment`/`InstallmentPayment` portent trois colonnes chèque — numéro, banque, échéance. Pas
de `BankedOn`, pas de statut. Un chèque ne quitte la liste qu'en annulant le paiement,
c'est-à-dire en affirmant qu'il n'a jamais été reçu.
`Domain/Entities/Payment.cs:62`, `Features/Billing/Queries/GetChequesDueQuery.cs:30`,
`web/app/cheques/page.tsx:34`

Et cela se cumule : `CashIn` compte le chèque à la date de **réception**, donc un carnet de
chèques postdatés étalé sur six mois entre intégralement dans les encaissements du jour où il
est remis. Le « Net » de la caisse affirme un liquide que le cabinet ne détient pas. Deux mois
plus tard, « Chèques à encaisser » est une liste de chèques majoritairement déjà banqués, sans
moyen de les distinguer.

### 2.6 🟠 La fenêtre de la caisse est construite sur le jour calendaire du **navigateur**
`rangeBounds` fabrique `new Date(\`${startDay}T00:00:00\`)` en heure locale du poste et envoie
le minuit **suivant** comme `to` — or le serveur traite `to` comme **inclusif** dans les cinq
lectures d'argent. C'est exactement le défaut que `GetCaisseSummaryQuery.cs:65` documente et
évite côté serveur, ré-armé côté client : un paiement à minuit compte dans deux journées. Sur
un poste qui n'est pas à UTC+1, toute la recette du jour bascule dans la caisse voisine.
`web/app/caisse/page.tsx:123`

### 2.7 🟠 Les dépenses utilisent une borne haute exclusive, seules de tous les registres
`ExpenseDate < to` contre `PaidOn <= to` / `RefundedOn <= to` partout ailleurs. Σ(extrait) peut
donc différer du total affiché au-dessus, à une frontière de période — la seule propriété que
l'extrait de caisse existe pour rendre vérifiable.
`Infrastructure/Repositories/ExpenseRepository.cs:41,64`

---

## 3. CNAM — le plus gros manque fonctionnel

Ce qui existe est bon : le BS1 s'imprime sur le vrai formulaire à coordonnées calibrées,
la nomenclature et la valeur de la lettre-clé sont par clinique, l'estimation de
remboursement est calculée par acte avec l'âge **à la date des soins**.
`Infrastructure/Services/CnamBs1BulletinRenderer.cs`,
`Features/Documents/BulletinCnamValidation.cs`

**Mais rien n'enregistre qu'un bulletin a été déposé, ni qu'il a été remboursé.**
`MedicalDocument` porte `IsDraft`, `FileId`, `AppointmentId` — pas de `SubmittedAt`, pas de
`PaidAt`, pas de montant, pas de référence CNAM.
`Domain/Entities/MedicalDocument.cs:5`

Les deux questions pour lesquelles ce flux existe — « ai-je déposé ce bulletin ? », « la CNAM
m'a-t-elle payé ? » — n'ont aucune réponse dans le produit. C'est un trou plus large que celui
du plafond.

**Le plafond annuel est calculé sur les seules factures de *ce* cabinet**, et le code le dit
lui-même. Un patient soigné dans deux cabinets lit un plafond qui ignore la moitié de son
année — et le chiffre lui est présenté au comptoir.
`Features/Patients/Queries/GetPatientCnamCeilingQuery.cs:97`

---

## 4. Ajustement au contexte tunisien

**Correct :** TND à 3 décimales partout (`fr-TN`, `Intl`, `(18,3)` en base, `"#,##0.000 DT"`
sur les PDF) · TVA + timbre · identifiant CNAM à 10 chiffres, régimes et liens de parenté ·
24 gouvernorats · `dd/MM/yyyy`, `HH:mm`, semaine commençant lundi · semaine par défaut
lundi–**samedi**.

**Manquant :**

- 🔴 **Pas de séance coupée / pause déjeuner.** `WorkingDay` est `{ day, enabled, from, to }` —
  **une seule plage par jour**. Une fermeture 12h–14h est inexprimable : soit la journée reste
  ouverte à la réservation sur le déjeuner, soit il faudrait deux fiches clinique, que le
  modèle ne permet pas. L'agenda ombre un seul bloc continu. Les horaires de Ramadan tombent
  dans le même trou.
  `web/lib/working-hours.ts:4`, `Common/Services/WorkingHoursResolver.cs:139`,
  `web/components/appointment-calendar.tsx:67`
- 🔴 **Aucun champ CIN** sur le patient. Aucun identifiant national nulle part, sauf
  `BuyerNationalId` dans le modèle e-facture TEIF, qui n'est pas alimenté depuis la fiche
  patient.
- 🟠 **Aucun jour férié.** Zéro occurrence de holiday / férié dans tout le dépôt.
- 🟠 **Aucun arabe, aucun RTL.** `<html lang="fr">` seul ; pas de champ nom en arabe. Déclaré
  hors périmètre dans les règles internes du dépôt.
- 🟡 Téléphones : l'import normalise en `+216XXXXXXXX`, la saisie manuelle stocke la chaîne
  brute (« 20 123 456 » avec ses espaces). Même règle, deux comportements de stockage — connu
  et documenté comme défaut.
  `Features/Patients/Import/PatientImportRowReader.cs:141`, `Domain/ValueObjects/PhoneNumber.cs:16`
- 🟡 La liste des gouvernorats est dupliquée : `web/lib/tunisia.ts:3` d'un côté, un littéral
  identique redéclaré dans `web/components/clinic-settings.tsx:67` de l'autre.

---

## 5. Construit mais inatteignable

Fonctionnalités entièrement écrites côté serveur, sans aucune porte d'entrée pour
l'utilisateur.

- 🔴 **Journal d'activité — `GET /api/audit`.** Filtres complets, `AdminOnly`, alimenté par un
  intercepteur EF. **Aucun module client, aucune page, aucun lien.** Le propriétaire ne peut
  pas répondre à « qui a supprimé ce patient ? ». `API/Controllers/AuditController.cs:39`
- 🔴 **Tout le sous-système de relance / recall — 6 endpoints.** Le module client existe
  (`web/lib/api/recalls.ts:5`) avec **zéro importateur**. Conséquence : `Clinic.RecallIntervalMonths`
  est paramétrable par commande mais par aucun écran. `API/Controllers/RecallController.cs`
- 🟠 **`GET /api/outbox`** — la profondeur des trois files (rappels, e-factures, e-mails) avec
  l'âge de la plus vieille ligne en attente. La question « est-ce que mes rappels partent ? »
  n'a pas d'écran. `API/Controllers/OutboxController.cs:43`
- 🟠 **`Clinic.StockExpiryLeadDays`** — `SetStockExpiryLeadDays` n'est **appelé nulle part** dans
  la solution. Pas de commande, pas d'endpoint, pas d'UI : toute clinique est figée sur les 30
  jours par défaut, et le « mettre à 0 pour désactiver l'alerte » documenté est inatteignable.
  `Domain/Entities/Clinic.cs:79`
- 🟠 **Notifications push** : aucun client n'enregistre jamais un jeton d'appareil
  (`POST /api/push-devices` sans appelant, web comme mobile), alors que l'interface annonce la
  capacité. `web/lib/api/push-devices.ts:37`
- 🟡 Morts côté client : `invoicesApi.listAvoirs` (les avoirs se créent et se téléchargent un à
  un, mais ne se listent jamais), `useAuthenticatedApi()`,
  `GET /cnam-nomenclature/reimbursement-estimate` (singulier), `GET /googlecalendar/redirect-uri`,
  les 4 endpoints `/api/trust/*`.

---

## 6. Deux points dangereux

### 6.1 🔴 TTN « El Fatoora » est inatteignable, et le mode par défaut ment
Trois verrous vérifiés :
1. **Aucune surface d'administration pour l'identité de signature.** Les réglages n'exposent
   que l'interrupteur et Sandbox/Production ; les quatre colonnes d'identité ne sont écrites
   par rien — le code le dit :
   `Common/Maintenance/SchemaVerificationService.cs:400`.
2. **Le repli exige un `.local/teif-signing.pfx` et des variables d'environnement** que personne
   n'a et qu'aucune UI ne demande. Sans le PFX, la facture est **garée en `Queued`
   indéfiniment**, visible uniquement d'un admin via `/api/outbox`. La clinique voit
   « en attente » pour toujours, sans instruction. `Infrastructure/Services/TtnIdentityProvider.cs:100`
3. **Le défaut est un faux.** `ttnEnvironment` vaut « Sandbox » par défaut et
   `SandboxTtnClient` renvoie `Validated` avec un identifiant fabriqué → la facture s'affiche
   « télétransmise à TTN » avec un cachet QR, et devient **définitivement incannulable** sur la
   foi d'une réponse simulée. `Infrastructure/Services/SandboxTtnClient.cs:51`, `Domain/Entities/Invoice.cs:355`

### 6.2 🔴 Un patient de passage créé depuis l'agenda est enregistré à 30 ans
Le « Nouveau patient » en ligne de la boîte de rendez-vous n'envoie que nom, prénom,
téléphone — pas de date de naissance. Le serveur substitue `DateTime.UtcNow.AddYears(-30)` et
la stocke comme une date ordinaire, sans marqueur « inconnue ». Conséquences réelles : la
liste affiche « 30 ans », l'odontogramme choisit la denture adulte, et la bande d'âge CNAM
applique **60 % au lieu de 70 % à un enfant**. La saisie complète, elle, exige la date de
naissance — c'est la seule porte qui fabrique cette valeur.
`web/components/create-appointment-dialog.tsx:515`, `Features/Patients/PatientFromRequest.cs:83`

---

## 7. Défauts mineurs

- **Les bons de prothèse sont une île.** `LabWorkOrder` ne porte que `PatientId` et un numéro
  de dent : pas d'`AppointmentId`, pas de `TreatmentPlanId`, pas de `DentalRecordId`. Et aucune
  référence aux bons depuis la fiche patient ou l'agenda — la seule surface est la page
  `/lab-orders`. La couronne du devis, la séance de pose et le bon ne se rencontrent jamais.
  Son `Cost` n'atteint ni la facturation ni les dépenses. `Domain/Entities/LabWorkOrder.cs:15`
- **« Unknown » stocké et affiché comme nom de mutuelle** : saisir seulement un numéro de police
  déclenche le repli anglais sur l'autre champ, rendu tel quel sur la fiche patient.
  `web/components/edit-patient-dialog.tsx:656,771`
- **Factures, devis et avoirs partagent le format `AAAA-NNNN` sur trois séquences indépendantes** :
  `2026-0007` peut être les trois à la fois, sans préfixe de série sur le document imprimé.
- **Seule la recherche remet la pagination à la page 1** ; tout autre filtre (patients signalés,
  dates de création) refait la page 4 du nouvel ensemble. `web/lib/hooks/use-paged-list.ts:69`
- **Aucune action rapide de statut sur l'agenda** : pas de « Arrivé / En cours / Terminé » sur la
  vue calendrier, il faut ouvrir la boîte d'édition. Et pas de bouton « créer la fiche » depuis
  le rendez-vous — on y accède par la notification post-visite ou par la bande « visites non
  documentées » de la fiche patient.
- **Données écrites que rien ne lit** : `Appointment.BookedOutsideWorkingHours` (4 sites
  d'écriture, zéro lecture), `MedicalDocumentDto.AppointmentId` (servi, jamais affiché — aucun
  écran ne dit quelle visite a produit une ordonnance), `WaitingListEntry.ResultingAppointmentId`.
  `WaitingListEntry.Cancel()` / `WaitingListStatus.Cancelled` sont inatteignables : la seule
  suppression est physique.
- `ClinicContext.EnsureClinicAccess` est du code mort qui porte la dernière chaîne anglaise
  destinée à l'utilisateur ; s'il était appelé, « Access denied… » atteindrait le toast tel quel.
  `Common/Services/ClinicContext.cs:83`

---

## 8. Vérifié sain (pas de défaut)

- Contrainte d'exclusion des rendez-vous : `Status NOT IN (5,6)` correspond bien à
  `Cancelled`/`NoShow` ; `AppointmentEndDateTime` est maintenu par trigger, donc une
  modification de durée ne peut pas désynchroniser la plage.
  `Migrations/20260730090331_AllowAcknowledgedOverlap.cs:49`
- Plus aucun `new Date().toISOString().slice(0,10)` dans `web/`.
- Correspondance enum → français complète, valeurs inconnues passées telles quelles.
- Arrondis : une seule autorité (`InvoiceCalculator.RoundMoney`), `AmountCollected` recalculé
  depuis les lignes vivantes et jamais incrémenté, montant sub-millime refusé et non stocké à 0.
- Aucun placeholder : zéro TODO/FIXME/« À venir »/`alert(`/`console.log` dans le code applicatif.
- Toutes les routes `web/app/` existent et sont atteignables ; les 20 entrées de navigation
  pointent toutes vers une page réelle.

---

## 9. Ce qu'il faudrait avant de le mettre en production dans un cabinet

Par ordre de coût pour le praticien :

1. Colmater les fuites d'argent : §2.1, §2.2, §2.3 — trois corrections petites et localisées.
2. Donner un état de dépôt/remboursement au bulletin CNAM (§3) — c'est la valeur du produit.
3. Ajouter l'encaissement du chèque (§2.5) et découpler la date de caisse de la date de
   réception.
4. Séance coupée dans les horaires (§4) — sans quoi l'agenda ment tous les midis.
5. Exposer le journal d'audit et les relances déjà construits (§5) — coût quasi nul, valeur
   immédiate.
6. Neutraliser le faux « Validated » du bac à sable TTN (§6.1) et la date de naissance
   fabriquée (§6.2) — deux corrections d'une ligne chacune contre des dégâts irréversibles.
