# Feature Specification: Facturation — Note d'honoraires numérotée

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-17
**Scope:** Full
**Feature:** Vraie facture / note d'honoraires tunisienne (numérotée, TVA + timbre, PDF), suivi de l'encaissement, et vue recettes.

## Overview
Le cabinet n'a aucun module de facturation structuré : l'argent vit dans `DentalRecord.Cost/AmountPaid` (note clinique) et dans un document « honoraires » stocké en JSON non requêtable. On introduit une entité **Facture** (aggregate root clinic-scoped, calqué sur `ProcedureType`) qui devient le **document fiscal de référence** : numérotée séquentiellement par an, avec lignes d'actes, TVA + timbre fiscal configurés au niveau cabinet et figés à l'émission, totaux en dinar tunisien, suivi des paiements, et export PDF « NOTE D'HONORAIRES » localisé Tunisie. Une vue Recettes et une tuile dashboard exposent le chiffre encaissé/facturé. Public : médecins/dentistes tunisiens (UI FR, TND).

## What Changes
- Nouvelle entité `Invoice` (aggregate root, `ClinicId`, filtre EF global) + enfants *owned* `InvoiceLine` et `Payment`.
- Numérotation séquentielle par cabinet **réinitialisée par année**, format `AAAA-NNNN`, attribuée à l'**émission** (pas au brouillon), sans trou et sûre en concurrence.
- Création d'une facture ex nihilo **ou** pré-remplie depuis un `DentalRecord` / `Appointment` (lien optionnel conservé) et/ou depuis le catalogue `ProcedureType.DefaultCost`.
- Lignes = actes uniquement (désignation, quantité, PU HT) — **jamais** de diagnostic/pathologie (secret médical).
- Réglages facturation au niveau cabinet : `MatriculeFiscal` (déjà présent sur `Clinic`, réutilisé), TVA applicable + taux (défaut 7 %, 0 = exonéré), timbre fiscal on/off + montant (défaut 1,000 DT). Édités via la gestion cabinet existante.
- Calcul figé à l'émission : Total HT → TVA (**taux unique cabinet appliqué au total HT**) → timbre fiscal → **Total TTC**, encapsulé dans un helper de calcul du Domain (testable).
- Cycle de vie : `Brouillon` → `Émise` → `Partiellement payée` / `Payée`, ou `Annulée`. Brouillon supprimable ; facture émise **non supprimable** (annulation avec motif, numéro conservé).
- Enregistrement de paiements (montant, date, mode : espèces / chèque / carte / virement) qui met à jour le montant encaissé et le statut.
- Export PDF via `IPdfGenerationService` (QuestPDF) : montants en **TND**, en-tête = identité cabinet + matricule fiscal (retirer le « Paris » et le `€`/`$` codés en dur), patient, numéro, date, tableau des actes, TVA, timbre, Total TTC, mentions légales note d'honoraires.
- Vue Recettes : liste des factures filtrable (période, patient, statut) + totaux (facturé / encaissé / reste à recouvrer), et tuile « Recettes » (encaissé du mois) sur le dashboard.
- Monnaie : nouveaux champs money en `decimal(18,3)` (millimes) ; affichage FR en DT/TND partout.

## Acceptance Criteria
- **AC-1:** Créer un brouillon de facture (patient + lignes) ne consomme aucun numéro ; il reste supprimable.
- **AC-2:** À l'émission, la facture reçoit un numéro `AAAA-NNNN` unique par cabinet, séquentiel et sans trou ; deux émissions concurrentes n'obtiennent jamais le même numéro.
- **AC-3:** Les totaux respectent HT → +TVA (taux cabinet figé, ou 0 si exonéré) → +timbre → TTC, arrondis au millime ; le timbre par défaut est 1,000 DT et n'est ajouté que s'il est activé.
- **AC-4:** Une facture peut être créée pré-remplie à partir d'un `DentalRecord`/`Appointment` ; le lien optionnel est stocké et `DentalRecord.Cost/AmountPaid` n'est **ni modifié ni supprimé**.
- **AC-5:** Enregistrer un paiement met à jour le montant encaissé et fait passer le statut à Partiellement payée (< TTC) ou Payée (≥ TTC) ; le sur-paiement est refusé ou plafonné (voir edge cases).
- **AC-6:** Une facture émise ne peut pas être supprimée ; elle ne peut qu'être annulée (motif requis) et conserve son numéro. Annulation autorisée aux rôles **admin** et **doctor** uniquement.
- **AC-7:** Création / émission / enregistrement de paiement sont autorisés à tout utilisateur authentifié du cabinet (dont **secretary**).
- **AC-8:** Le PDF affiche montants en TND, matricule fiscal du cabinet, numéro, date, actes, TVA, timbre, Total TTC, sans aucun `€`/`$` ni « Paris », et sans révéler de pathologie.
- **AC-9:** La vue Recettes filtre par période/patient/statut et affiche total facturé, total encaissé, reste à recouvrer ; le dashboard affiche l'encaissé du mois courant.
- **AC-10:** Toutes les factures et requêtes recettes sont strictement isolées par cabinet (filtre global `ClinicId`).

## API Contract
Sous `/api/invoices` (contrôleur `InvoicesController : ApiControllerBase`, `[Authorize]`, erreurs via `HandleFailure` → `{ error }`).
- `POST /api/invoices` — créer un brouillon. Req: `{ patientId, dentalRecordId?, appointmentId?, lines: [{ designation, quantity, unitPriceHt }] }`. Resp 201: `InvoiceDto`.
- `PUT /api/invoices/{id}` — modifier un brouillon (lignes/patient). 4XX si non-brouillon.
- `POST /api/invoices/{id}/issue` — émettre (attribue le numéro, fige TVA/timbre + totaux). Resp: `InvoiceDto`.
- `POST /api/invoices/{id}/payments` — enregistrer un paiement. Req: `{ amount, method, paidOn }`. Resp: `InvoiceDto`.
- `POST /api/invoices/{id}/cancel` — annuler (admin/doctor). Req: `{ reason }`. 403 sinon.
- `DELETE /api/invoices/{id}` — supprimer un brouillon uniquement. 4XX si émise.
- `GET /api/invoices/{id}` / `GET /api/invoices` (filtres `from,to,patientId,status`) — détail / liste.
- `GET /api/invoices/{id}/pdf` — PDF de la note d'honoraires (Blob).
- `GET /api/invoices/revenue?from&to` — `{ totalInvoiced, totalCollected, outstanding }`.
- `GET /api/dashboard/stats` — étendu avec `monthlyRevenueCollected` (decimal).
- Réglages cabinet TVA/timbre exposés via l'endpoint de mise à jour cabinet existant.

## Data / Schema Changes
- **`Invoice`** (aggregate, `decimal(18,3)` pour les champs money) : `Id`, `ClinicId`, `PatientId`, `DentalRecordId?`, `AppointmentId?`, `Number?` (null tant que brouillon), `IssueDate?`, `Status` (enum `InvoiceStatus`: Draft/Issued/PartiallyPaid/Paid/Cancelled), `VatApplicable` (bool, figé), `VatRate` (decimal, figé), `StampDutyAmount` (decimal, figé, 0 si off), `CancellationReason?`, `CreatedAt`/`UpdatedAt`. Totaux calculés/persistés : `TotalHt`, `TotalVat`, `TotalTtc`, `AmountCollected`.
- **`InvoiceLine`** (owned) : `Id`, `InvoiceId`, `Designation`, `Quantity`, `UnitPriceHt` (`decimal(18,3)`), `LineTotalHt`.
- **`Payment`** (owned) : `Id`, `InvoiceId`, `Amount` (`decimal(18,3)`), `Method` (enum `PaymentMethod`: Cash/Cheque/Card/Transfer), `PaidOn`.
- **`Clinic`** — nouveaux champs : `VatApplicable` (bool, défaut false), `VatRate` (decimal, défaut 7), `StampDutyEnabled` (bool, défaut true), `StampDutyAmount` (`decimal(18,3)`, défaut 1,000). `MatriculeFiscal` déjà existant — réutilisé.
- `IInvoiceRepository` + impl (pas de `SaveChanges` — UoW), `.Include(Lines, Payments)`. DbSet + migration. `Invoice` ajouté au filtre `ClinicId` dans `ApplicationDbContext` (comme `ProcedureType`).
- Frontend : `web/lib/api/invoices.ts` + types miroir ; composant création/édition + enregistrement paiement + bouton PDF ; page/onglet Factures (fiche patient + route `/factures`) ; vue Recettes ; tuile dashboard. Devise DT/TND, labels FR.

## Out of Scope
- **CNAM** (bordereaux AP1/AP2, APCI, filière privée, retenue à la source CNAM) — feature séparée.
- **Facturation électronique TTN / format TEIF** (obligatoire 2026) — évolution future.
- **Caisse complète** : reçus PDF séparés, rapprochement de caisse, multi-devises.
- **Comptabilité / grand-livre / journal**, avoirs/notes de crédit au-delà de la simple annulation, retenue à la source automatique.
- **Multi-taux TVA par ligne** (un seul taux cabinet retenu).
- **Suppression/migration** de `DentalRecord.Cost/AmountPaid` — conservés tels quels (note clinique).

## Edge Cases (Critical only)
- **Concurrence de numérotation** : l'attribution du numéro à l'émission doit être atomique (compteur par cabinet+année verrouillé / contrainte d'unicité + retry) — jamais de doublon ni de trou.
- **Sur-paiement** : un paiement portant l'encaissé au-delà du TTC est refusé (`Result.Failure`) ; l'égalité passe la facture à Payée.
- **Chevauchement money à deux endroits** : la fiche de l'acte (`DentalRecord`) doit indiquer le statut de facturation de la facture liée (le cas échéant) pour éviter la double-saisie ; la facture reste la source de vérité fiscale.
- **Exonéré (TVA 0 / non applicable)** : `TotalVat = 0`, la ligne TVA n'apparaît pas (ou 0 %) sur le PDF ; le timbre s'applique quand même s'il est activé.
- **Annulation** : conserve numéro, lignes et totaux figés ; interdit tout nouveau paiement ; exclue des « à recouvrer ».
- **Isolation cabinet** : aucune facture d'un autre cabinet visible/modifiable (filtre global + garde tenant sur enfants via la facture).
