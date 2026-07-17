# Feature Specification: Facturation — Facturer une intervention (pré-remplir depuis un dental record)

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-17
**Scope:** FE
**Feature:** Depuis la fiche d'un patient, un bouton « Facturer cette intervention » ouvre une facture brouillon pré-remplie à partir d'un acte (dental record), avec garde anti-doublon.

## Overview
La feature `facturation-note-honoraires` supporte déjà côté backend le lien optionnel `DentalRecordId` sur une facture (stocké à la création, `DentalRecord` jamais modifié), mais l'UI ne l'utilise pas : on crée les factures manuellement. On câble l'AC-4 de cette feature côté frontend — un bouton par ligne d'acte pré-remplit le formulaire de facture existant — et on ajoute une garde empêchant de facturer deux fois le même acte. Purement frontend, aucun changement backend/schéma.

## What Changes
- Sur l'onglet des actes du patient (section « Dental Records » de la fiche patient), ajout d'une action « Facturer cette intervention » par ligne d'acte.
- L'action ouvre le `InvoiceFormModal` existant en mode création, pré-rempli : patient du dossier + une ligne (désignation = `procedureType` de l'acte, quantité = 1, PU HT = `cost` de l'acte), toujours éditable avant enregistrement.
- Le brouillon créé conserve le lien `DentalRecordId` (transmis via le contrat `CreateInvoiceRequest` existant).
- Garde anti-doublon : l'action est désactivée pour un acte déjà rattaché à une facture **non annulée**. L'état « déjà facturé » est dérivé côté client des factures du patient (`invoicesApi.list({ patientId })` → `InvoiceDto.dentalRecordId`), sans nouvel endpoint.
- Le formulaire de facture accepte désormais des lignes pré-remplies + un `dentalRecordId` optionnels (uniquement en création, pas en édition).

## Acceptance Criteria
- **AC-1:** Depuis un acte non encore facturé, un clic sur « Facturer cette intervention » ouvre un brouillon pré-rempli (patient + une ligne : désignation = type d'acte, quantité 1, PU HT = coût de l'acte), modifiable avant enregistrement.
- **AC-2:** Le brouillon créé via ce flux persiste le lien `DentalRecordId` vers l'acte source.
- **AC-3:** Garde anti-doublon — l'action est désactivée (avec un indicateur « Facturé ») pour un acte déjà lié à une facture non annulée ; une facture liée **annulée** ne bloque pas une nouvelle facturation.
- **AC-4:** `DentalRecord.Cost` / `DentalRecord.AmountPaid` ne sont jamais modifiés ni supprimés par ce flux.
- **AC-5:** Le flux ne concerne que la **création** d'un nouveau brouillon ; l'édition, la numérotation, l'émission et le paiement des factures restent inchangés. Aucun changement backend/schéma.

## Out of Scope
- Pré-remplissage depuis un `Appointment` (le lien existe côté backend, non câblé ici).
- Facturation groupée de plusieurs actes en une facture.
- Une colonne/tableau complet de statut de facturation sur les actes (au-delà de l'indicateur « Facturé » de la garde).
- Tout filtre backend par `dentalRecordId` (la garde est calculée côté client).
- Reprise du prix depuis le catalogue `ProcedureType.DefaultCost`.

## Edge Cases (Critical only)
- **Facture liée annulée** : ne compte pas comme « déjà facturé » — l'acte reste facturable.
- **Coût de l'acte = 0** : facturation autorisée ; la ligne pré-remplie a un PU HT de 0, éditable.
- **Chargement des factures échoué** (pour la garde) : ne pas bloquer la fiche patient ; en cas d'échec, l'action reste disponible (dégradation sûre) plutôt que faussement désactivée.
