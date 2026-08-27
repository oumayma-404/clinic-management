# Feature Specification: Document & CNAM Rendering Accuracy

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-21
**Scope:** BE
**Feature:** Fix a 1000× honoraires parse on the CNAM bulletin, stop non-doctor edits from wiping the cachet/ordre, and make the birth-date box consistent.

## Overview
Three rendering defects on official documents: (1) `FormatHonoraires` parses the CNAM BS1 honoraires with `NumberStyles.Any` + InvariantCulture, so a French-style `12,000` (= 12 DT) is read as 12000 and stamped 1000× too large on the genuine bulletin; (2) editing a document as a secretary/admin (no linked doctor) strips the previously-snapshotted cachet + CNOMDT ordre because `ApplyTo` removes the reserved keys then re-adds only the caller's own doctor values; (3) the "Date de naissance" box shows the age in the stored PDF but the date of birth in the downloaded PDF.

## What Changes
- `CnamBs1BulletinRenderer.FormatHonoraires` parses honoraires using Tunisian/French decimal semantics (comma = decimal separator), so `12,000` → `12.000` TND; dot-decimal input still parses correctly.
- `UpdateMedicalDocumentCommand`/`PractitionerRenderSnapshot` preserve the document's already-stored practitioner keys (cachet key + content-type, ordre number) and clinic city when the editing caller has no linked doctor, instead of stripping them.
- The patient-info box labeled "Date de naissance" renders the date of birth (not age) consistently in both the stored/background and downloaded PDFs.

## Acceptance Criteria
- **AC-1:** A honoraires value of `12,000` / `35,500` on the BS1 is stamped as `12.000` / `35.500` TND, not `12000` / `35500`.
- **AC-2:** Dot-decimal honoraires input (e.g. `12.000`) continues to render as `12.000`.
- **AC-3:** A secretary/admin editing a doctor's certificat keeps the doctor's cachet and CNOMDT ordre in the re-rendered PDF; a doctor editing their own document still re-applies their live cachet/ordre.
- **AC-4:** The "Date de naissance" box shows the date of birth (correctly labelled) in both the stored/background PDF and the downloaded PDF.

## Out of Scope
- The retired "note d'honoraires" document type (already rejected on the backend).
- Resolving cachet by a named issuing doctor in multi-doctor cabinets (no issuer FK by design).
