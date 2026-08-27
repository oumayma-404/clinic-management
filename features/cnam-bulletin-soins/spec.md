# Feature Specification: CNAM — Bulletin de soins (BS1), first slice

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-17
**Scope:** Full
**Feature:** Record a patient's CNAM identity and generate a faithful "Bulletin de soins CNAM (BS1)" PDF from the existing documents module.

## Overview
First slice of CNAM support for the Tunisian reimbursement pathway (*système de remboursement / filière privée*). Clinics can capture a patient's CNAM identity, add CNAM identifiers to the clinic/doctor, and produce a printable **Bulletin de remboursement des frais de soins (BS1)** — a new document type sitting alongside Ordonnance, Lettre de liaison, Note d'honoraires, and Certificat médical. The bulletin reproduces the official BS1 layout and is pre-filled from patient data and the patient's dental records; the doctor completes the CNAM `code acte` / `cotation` by hand. No electronic submission — this produces a document the patient files with CNAM.

## What Changes
- Patient records gain an optional **CNAM identity** block (in the existing insurance section): Identifiant Unique; régime (CNSS | CNRPS | Convention bilatérale); assuré social (prénom, nom, adresse, code postal — may differ from the patient); lien du malade à l'assuré (assuré lui-même | conjoint | enfant + rang | ascendant + père/mère). All optional.
- Clinic settings gain an optional **Matricule Fiscal**; each doctor gains an optional **Code professionnel de santé** (CNAM provider code). Both print on the bulletin.
- New document type **`bulletin-cnam`** appears in the `/documents` gallery ("Bulletin de soins CNAM") and opens in the existing editor, reusing the create/edit/list/save + PDF-download flow.
- The bulletin editor lets the user: choose the care type (**APCI** + code APCI | **MO** | **Hospitalisation** | **Suivi de grossesse**); review the auto-filled assuré/malade + clinic/doctor identity; and build an **acts table** (Date · Dent(s) · Code acte · Cotation · Honoraires) pre-filled from the patient's dental records over a chosen date range, with rows editable/addable/removable. Code acte + Cotation are typed manually.
- PDF generation renders a **faithful replica of the official 2-page BS1**: CNAM header + bilingual (FR/AR) section labels, Assuré/Malade boxes, care-type checkboxes, the dental-relevant act tables (Consultations et visites, Actes médicaux, Consultations et actes de soins dentaires, Prothèses dentaires) with the FDI tooth diagram, and the footnotes. Honoraires shown in TND (millimes).

## Acceptance Criteria
- **AC-1:** A patient can be saved with any subset of CNAM identity fields (all optional); the record persists and reloads them. Existing patients with no CNAM data are unaffected.
- **AC-2:** Clinic **Matricule Fiscal** and per-doctor **Code professionnel de santé** can be saved in settings and reload correctly.
- **AC-3:** `/documents` shows a fifth card "Bulletin de soins CNAM"; clicking it opens the editor with the patient's CNAM identity, clinic, and doctor pre-filled.
- **AC-4:** Selecting a patient + a date range pre-fills the acts table from that patient's dental records (Date, Dent(s), Honoraires); rows can be edited, added, and removed; Code acte and Cotation are entered manually.
- **AC-5:** Care type is selectable (APCI + code | MO | Hospitalisation | Suivi de grossesse) and is reflected on the PDF.
- **AC-6:** "Télécharger PDF" produces a faithful official-BS1 PDF with all captured identity, care-type, and act data; unfilled fields render as blank lines. Saving persists the bulletin as a `bulletin-cnam` MedicalDocument (content in `ContentJson`) and, like other types, queues the PDF into the patient's files.
- **AC-7:** All new labels are in French; the four existing document types are byte-for-byte unchanged in behavior. Cloud and Local modes both work.

## Data / Schema Changes
- **Patient** — new optional owned value object **`CnamInfo`** (IdentifiantUnique, Regime, AssureFirstName, AssureLastName, AssureAddress, AssurePostalCode, MaladeLien, MaladeLienRang), all nullable. EF config + migration. `InsuranceInfoDto` / patient request+DTO extended.
- **Clinic** — new nullable `MatriculeFiscal` (string). Migration; `ClinicDto` + update command/request extended.
- **Doctor** — new nullable `CodeProfessionnelSante` (string). Migration; `DoctorPersonalInfoDto` + `UpdateDoctorsCommand` extended.
- **MedicalDocument** — **no schema change**. New `DocumentType` value `"bulletin-cnam"`; `ContentJson` carries `careType`, `apciCode`, the acts array (serialized), and a snapshot of the CNAM identity. `MedicalDocumentPdfData.Content` (Dictionary<string,string>) carries the same, following the existing "array serialized as JSON string" pattern (cf. medications/procedures).

## Out of Scope
- Official CNAM **nomenclature database** of code acte / cotation (manual entry only).
- Electronic submission / télétransmission to CNAM; tiers-payant flows.
- Biologie, Pharmacie, Accouchement/Hospitalisation act tables; vignette image handling.
- Bordereaux batch generation & reconciliation (AP1/AP2); any accounting/invoicing module.
- Backfilling CNAM identity for existing patients (they simply start empty).

## Edge Cases (Critical only)
- **Malade = the insured**: when lien is "assuré lui-même", the assuré identity defaults to the patient's own name/address (no double entry).
- **Dental record has no cost / no teeth**: the pre-filled row shows blank Honoraires / Dent(s); still editable.
- **No doctor `Code professionnel de santé` set**: bulletin still generates with that field blank (AC-1/AC-6 lenient rule).
