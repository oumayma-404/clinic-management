# Feature Specification: CNAM BS1 Official-Form Overlay

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-20
**Scope:** BE
**Feature:** Generate the CNAM « Bulletin de soins » (BS1) by stamping the patient/assuré/act data at precise coordinates onto CNAM's actual official BS1 PDF, so the printed output is an acceptable official document — replacing today's custom-drawn (non-official) table.

## Overview
`bulletin-cnam` currently renders a custom QuestPDF table that does not match the official CNAM BS1, so it isn't accepted as a real Bulletin de soins. This feature overlays the same data onto CNAM's genuine printable BS1 (a 2-page bilingual FR/AR form, now bundled at `Infrastructure/Assets/BS1.pdf`) using PDFsharp, producing a fill-and-print form the patient submits to a CNAM agency. Only the `bulletin-cnam` output changes; all other document types keep their QuestPDF rendering.

## What Changes
- Bundle the official **`BS1.pdf`** as an Infrastructure asset (already added; copied to build output).
- Add **PDFsharp** (MIT) to open the BS1 and draw text/marks onto its pages.
- For `DocumentType == "bulletin-cnam"`, `IPdfGenerationService` produces the PDF via a new BS1-overlay renderer instead of the custom QuestPDF table; the old `bulletin-cnam` drawing is removed.
- A coordinate map (calibrated to the bundled BS1) positions each field on the two landscape pages.

## What gets filled (only the dentist-relevant regions)
- **Assuré social (page 1):** Identifiant Unique (digit boxes), Régime (tick CNSS / CNRPS / Convention bilatérale), Prénom, Nom, Adresse, Code postal — from `Patient.CnamInfo`.
- **Le malade (page 1):** lien (tick L'assuré social / Le conjoint / L'enfant / L'ascendant) + rang, Prénom, Nom, Date de naissance, N° tél portable — from `CnamInfo` + `Patient`.
- **Consultations et actes de soins dentaires table (page 1, 6 rows):** per act → Date, Dent, Code acte, Cotation, Honoraires, Code professionnel de santé (`Doctor.CodeProfessionnelSante`). Cachet/signature left blank (physical stamp).
- **Cadre de soins (page 2):** tick APCI / MO / Hospitalisation / Suivi de grossesse (from `careType`) + "Préciser le code APCI" (`apciCode`).

## Acceptance Criteria
- **AC-1:** Downloading a `bulletin-cnam` document returns a PDF that **is** the official BS1 with the fields above filled in the correct boxes; blanks render empty (no `null`/placeholder), and checkbox fields mark exactly the selected option.
- **AC-2:** Field positions match the official layout (calibrated to the bundled BS1); **no extra headers, titles, watermarks, or decorations** are added — output is the official form + data only.
- **AC-3:** Honoraires are formatted in **TND with 3 decimals** (millimes), no currency symbol, consistent with the recorded act cost.
- **AC-4:** When acts exceed the **6** rows of the dental table, **additional BS1 page copies are appended** so no act is dropped (nothing silently truncated).
- **AC-5:** A patient with no CNAM identity still produces the form (identity blank, acts filled); an act missing code/cotation still prints its date/dent/honoraires.
- **AC-6:** If the `BS1.pdf` asset is missing/unreadable, generation **fails fast** with a clear French operator error — never a blank or malformed PDF (no fallback to the old custom table).

## Data / Schema Changes
- None. Uses the existing `Patient.CnamInfo`, `Doctor.CodeProfessionnelSante`, and the `bulletin-cnam` `MedicalDocument` content (careType, apciCode, acts JSON) already collected by the editor.

## Out of Scope
- The document editor, nomenclature lookup, and act pre-fill are unchanged.
- The tooth diagram and the **Prothèses dentaires** table are left blank (reference only — our acts aren't classified as prosthetic).
- All non-dental sections of the BS1 (medical/paramedical/biologie/consultations-visites/pharmacie/accouchement/vignettes) stay blank.
- No electronic/online CNAM submission — paper fill-and-print only. The other document types are untouched.

## Edge Cases (Critical only)
- **Act count > 6:** append BS1 copies per AC-4.
- **Missing BS1 asset:** fail fast per AC-6.
- **Régime / lien value not one of the known options:** leave the checkboxes unticked rather than guessing.
