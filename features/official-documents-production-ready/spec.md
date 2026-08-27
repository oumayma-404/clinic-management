# Feature Specification: Official Documents — Production-Ready for a Tunisian Dental Cabinet

**Status:** APPROVED
**Challenged:** Yes
**Created:** 2026-07-20
**Scope:** Full-stack (frontend + backend + DB migration)
**Feature:** Make the /documents subsystem legally and fiscally correct for a Tunisian dental cabinet — compliant honoraires, valid certificats with a practitioner cachet, structured lettres de liaison to external confrères, and an admin-managed CNAM nomenclature — plus the localization/correctness fixes the documents feature is missing.

## Overview

A devil's-critic review found the documents feature ships several legally or fiscally invalid outputs: a "Note d'honoraires" that violates Tunisian invoicing law, certificats missing a mandatory deontological mention (and silently losing their order number), lettres de liaison that cannot address an external confrère, and a CNAM act catalog that is hardcoded and self-flagged "PENDING VERIFICATION". No document carries a practitioner cachet/signature, and every generic document is stamped "Paris, le …" with euro amounts. This feature closes those gaps while reusing the app's existing compliant Invoice pipeline, image-upload plumbing, and CRUD patterns rather than building parallel systems. The CNAM BS1 official-form overlay and the Invoice/El Fatoora pipeline are already correct and are out of scope except where the verified nomenclature feeds BS1 acts.

## User Stories

- **US-1 — Compliant honoraires.** As a dentist, when I choose "Note d'honoraires" in /documents I get the cabinet's real, fiscally compliant invoice (TND, matricule fiscal, sequential number, TVA, timbre, El Fatoora), not a euro-denominated non-compliant document.
- **US-2 — Valid certificat + cachet.** As a dentist, I issue a certificat médical that carries the mandatory deontological mention, the correct ordre (CNOMDT) number, and my personal cachet/signature — and none of that data is lost when the PDF is regenerated.
- **US-3 — Structured lettre de liaison.** As a dentist, I write a lettre de liaison addressed to an external confrère/specialist (free name/address/specialty) using guided clinical fields, and it prints on the cabinet letterhead with my cachet.
- **US-4 — Trustworthy CNAM nomenclature.** As a cabinet admin, I manage the CNAM dental act catalog (codes, lettres clés, coefficients) from an admin screen so the codes staff put on bulletins are verified; the reimbursement estimate reflects the correct age-based CNAM rates.
- **US-5 — Correct localization & no data loss.** As any user, the generated documents show the cabinet's city (not "Paris") and Tunisian dinars, filenames are correct, and edits I make are not silently dropped.

## Functional Requirements

### FR-1 — Honoraires routes to a compliant Invoice
- FR-1.1 The "Note d'honoraires" card in /documents no longer opens the document editor. It opens a patient selection step, then the existing invoice draft form (`InvoiceFormModal`) with the chosen patient pre-selected.
- FR-1.2 The draft is **pre-filled with the patient's not-yet-invoiced dental records** (reusing the patient-page "Facturer cette intervention" seeding: one act line per uninvoiced record, `designation` = procedure, `unitPriceHt` = cost). The user can adjust lines before saving. Records already invoiced (non-cancelled) are excluded.
- FR-1.3 Submitting creates a **draft** invoice via the existing invoice pipeline (no number consumed). Numbering (`AAAA-NNNN`), TVA, and timbre are applied at a **separate issue step** exactly as for invoices created from /factures; nothing about the Invoice pipeline changes. After creation the user lands on the invoice (Factures context) where the draft can be issued — the honoraires flow does not auto-issue.
- FR-1.4 The `honoraires` document type is removed from the editor's type handling and from the Word/print/PDF paths. No new `honoraires` `MedicalDocument` records are created.
- FR-1.5 Existing (legacy) honoraires documents already generated remain accessible as patient files; they are not migrated or deleted.

### FR-2 — Certificat médical correctness
- FR-2.1 The certificat is **lightly generalized**: it carries a free **objet/motif** body (e.g. présence, soins en cours, aptitude), with the **repos médical** block (start date + rest duration) as one *optional* use rather than the only template. When the repos fields are filled, the rest sentence is rendered; when empty, only the objet/motif is rendered.
- FR-2.2 The objet/motif, order number, start date, and rest duration entered for a certificat are persisted to the document body and survive background PDF regeneration (fixes the save-vs-render field mismatch — all certificat fields use one consistent content schema across save and render).
- FR-2.3 The rendered certificat includes the mandatory mention **"Certificat établi à la demande de l'intéressé(e) et remis en main propre."** above the signature block.
- FR-2.4 The ordre label reads **"Ordre National des Médecins Dentistes (CNOMDT)"** (not "Ordre des Médecins"), on both the form and the rendered document.
- FR-2.5 The practitioner's ordre number is stored on the practitioner's profile and pre-filled automatically, instead of being retyped for each certificat.

### FR-3 — Practitioner signature / cachet (all generated clinical documents)
- FR-3.1 Each practitioner can upload a signature/cachet image on their own profile; it is stored per-doctor (its content type persisted so it renders correctly, unlike the logo). Upload/replace/remove of a doctor's cachet is restricted to **that doctor (their own record) or an admin** — one user cannot set another practitioner's cachet.
- FR-3.2 The prescription, certificat, and lettre de liaison PDFs render the issuing practitioner's cachet in the signature area. If no cachet is uploaded, the document falls back to today's empty signature line (no error).
- FR-3.3 The practitioner's cachet reference and ordre number are **snapshotted onto the document at creation time** (consistent with the existing patient/clinic/doctor snapshot pattern) so the unauthenticated background PDF job can render them without a live doctor lookup.
- FR-3.4 The CNAM BS1 continues to leave the physical stamp/signature region blank (unchanged). The Invoice PDF is unchanged (it carries the El Fatoora QR as cachet électronique).

### FR-4 — Lettre de liaison to an external confrère
- FR-4.1 The recipient is an **external** confrère/specialist: free-text name, specialty, and address — no longer selected from the clinic's own doctors.
- FR-4.2 The body is composed of discrete guided fields: motif, examen clinique, examen radiologique, actes réalisés, and prescriptions (with posologie/durée). All fields are optional except recipient name; empty fields are omitted from the rendered letter.
- FR-4.3 The letter renders on the cabinet letterhead with the practitioner cachet (per FR-3) and is downloadable/printable/attachable to the patient file like other documents. Automated delivery to the confrère is out of scope.

### FR-5 — CNAM nomenclature catalog (DB-backed, admin-managed, global)
- FR-5.1 The CNAM dental act catalog moves from hardcoded in-code data to a **global** (not clinic-scoped) database-backed catalog, seeded with entries (code acte, désignation FR, lettre clé, coefficient, catégorie, active flag). The seed is **provisional**: entries/catalog are flagged "à vérifier" and a one-time admin banner warns the data must be confirmed against the current CNAM dentist convention before clinical reliance. Nothing is blocked; the flag clears once an admin confirms.
- FR-5.2 The **valeur de la lettre clé (VLC)** — the dinar value per lettre clé (CD/CDS/VD/D/RD…) used in the reimbursement estimate — is also **admin-managed** (a small companion set of values, editable from the same admin screen) rather than a hidden hardcoded map. VLC values are seeded provisionally under the same "à vérifier" flag.
- FR-5.3 All authenticated users can read the catalog + VLC values (the bulletin editor consumes them). Create/update/deactivate of catalog entries and VLC values is restricted to admins via the existing AdminOnly policy.
- FR-5.4 An admin-only management screen lists, creates, edits, and deactivates catalog entries and edits VLC values (mirroring the procedure-types screen pattern).
- FR-5.5 The reimbursement estimate = coefficient × VLC × rate, where the rate uses the July-2021 CNAM dental rates: **70% for patients aged 4–18 (inclusive), 60% otherwise**, based on the patient's age at the care date. It remains an editor-only indicative figure, is never persisted or printed, and is labelled "estimation indicative, non contractuelle." If the patient DOB is unknown, the non-child rate applies.

### FR-6 — Localization & correctness fixes
- FR-6.1 Generated clinical documents show the cabinet's city + date (e.g. "Tunis, le …") derived from clinic data — never a hardcoded "Paris". No euro symbol appears on any clinical document; monetary values use Tunisian dinar (millimes, 3 decimals) conventions consistent with the invoice path.
- FR-6.2 A re-saved `bulletin-cnam` gets the correct French filename (fix the missing mapping in the update path).
- FR-6.3 The live-preview blocks are made **non-editable** (the structured left-hand form is the single source of truth), so no user edit is silently discarded. (The one preview field that currently writes back — the liaison content box — is superseded by FR-4's structured fields.)

## API Endpoints

New (CNAM nomenclature — replaces the read-only in-code provider):
- `GET /api/cnam-nomenclature` — list catalog entries (any authenticated user). *(Exists today; now DB-backed. Response shape unchanged: `{ codeActe, designationFr, lettreCle, coefficient, category }` + `id`, `isActive`.)*
- `POST /api/cnam-nomenclature` — create entry. **AdminOnly.**
- `PUT /api/cnam-nomenclature/{id}` — update entry. **AdminOnly.**
- `DELETE /api/cnam-nomenclature/{id}` — deactivate/delete entry. **AdminOnly.**

Modified:
- Practitioner/doctor update endpoint gains an ordre number field and a multipart cachet image upload (mirroring the clinic logo `[FromForm]` pattern). A read endpoint serves the cachet image stream with its persisted content type.
- Medical-document create/update: persists the certificat order-number/start-date/duration correctly and the doctor cachet-key + ordre-number snapshot.

No changes to the Invoice endpoints (reused as-is).

## Data / Schema Changes

- **Doctor** (aggregate root): add `OrderNumber` (nullable string, CNOMDT registration no.) and cachet fields — cachet storage key (nullable string) + cachet content type (nullable string). Migration + update the doctor create/update flow and profile UI.
- **New global entity `CnamNomenclatureEntry`**: `CodeActe`, `DesignationFr`, `LettreCle`, `Coefficient`, `Category`, `IsActive`, a provisional/"à vérifier" flag, timestamps. **No `ClinicId`**; excluded from the clinic global query filter. Unique index on `CodeActe`. Seeded via migration.
- **VLC values** (valeur de la lettre clé): a small global admin-managed set keyed by lettre clé (e.g. `LettreCle` → dinar value), also provisional-flagged and seeded. May be a lightweight entity/table or a keyed config row — sized during planning; the requirement is that it is admin-editable, not hardcoded.
- **MedicalDocument**: certificat content persists `objet/motif`, `doctorOrderNumber`, `startDate`, `duration` consistently; add doctor cachet-key + ordre-number to the snapshot the renderer reads. (Prefer storing in the existing snapshot/`ContentJson` rather than adding a `DoctorId` FK, to preserve the entity's snapshot design.)
- No change to the Invoice schema.

## Scope

### In Scope
- The five functional requirement groups above (honoraires redirect, certificat correctness, per-doctor cachet, structured liaison, CNAM catalog + reimbursement, localization/bug fixes).
- Feeding the admin-managed nomenclature into the existing BS1 acts lookup.

### Out of Scope
- The CNAM BS1 official-form overlay renderer and `Assets/BS1.pdf` (already correct — untouched except consuming verified nomenclature).
- The Invoice / TTN El Fatoora pipeline internals (reused unchanged).
- Cryptographic / legally-qualified e-signatures — the cachet is a scanned image, not a qualified electronic signature.
- **Multiple fully-typed certificat templates** (separate présence / soins / aptitude / arrêt templates) — FR-2 does a light generalization (free objet/motif + optional repos) only; a full multi-template certificat builder is a later feature.
- Automated delivery (email/portal) of the lettre de liaison copy to the confrère.
- Migrating or re-rendering legacy euro-denominated honoraires documents.
- An admin UI for CNAM entries in Cloud mode where no admin user exists (the seed still provides correct data; editing needs a Local admin).

## Edge Cases
- **Background render without auth**: the Hangfire PDF job runs unauthenticated and the document has no `DoctorId` — cachet + ordre number must come from the document snapshot, not a live lookup (FR-3.3).
- **No cachet uploaded**: documents render with the plain signature line, no error (FR-3.2).
- **Cachet snapshot vs later change**: the snapshot stores the doctor's cachet storage key; because the key is deterministic per-doctor, re-uploading a cachet updates the image seen by re-rendered documents (acceptable — same practitioner's current stamp). If the cachet blob is missing/deleted at render time, fall back to the plain signature line, never fail the render.
- **VLC/rate with a lettre clé that has no VLC value**: the reimbursement estimate is omitted for that act (shown as "—"), not computed as zero.
- **Cachet content type**: must be persisted per upload; the existing logo path hardcodes `image/png` and must not be copied blindly.
- **Reimbursement age boundaries**: 4 and 18 are inclusive in the child band; unknown DOB → non-child rate (FR-5.4).
- **CNAM catalog in Cloud (no admin)**: reads work for everyone; writes are unreachable without an admin — acceptable, seed carries provisional data and the "à vérifier" flag simply stays set.
- **Duplicate `CodeActe`**: rejected by unique constraint with a clear French error.
- **Honoraires card with no patient / patient from another clinic**: patient selection is required and tenant-checked by the existing invoice create path.
- **Legacy liaison documents** referencing an internal recipient doctor: remain viewable via their stored snapshot; only new letters use the external-recipient model.

## Non-Functional Hints
- Reuse existing patterns verbatim: `InvoiceFormModal` for honoraires, the clinic-logo upload plumbing for the cachet, and the `ProcedureType` CRUD stack for the CNAM catalog. No new architectural patterns.
- Follow the repo's inline `Result.Failure` validation convention (no FluentValidation) and French user-facing messages.

## Dependencies
- Existing Invoice pipeline (`InvoiceFormModal`, `POST /api/invoices`, issue/El Fatoora).
- `IFileStorage` (Local disk / MinIO) for the cachet blob.
- `AuthorizationPolicies.AdminOnly` for catalog writes.
- CNAM dental nomenclature + coefficients + VLC values (seeded provisionally; source data to be reconciled against the current CNAM dentist convention and admin-confirmed before clinical reliance).

## Open Questions
- **Doctor profile UI location**: where do practitioners edit their own profile today (settings vs a dedicated screen)? The cachet upload + ordre number field attach there — to be confirmed at planning time.
- **Verified nomenclature source data** (resolved approach, data still pending): the seed ships as **provisional** ("à vérifier", FR-5.1/5.2) so the feature is usable without blocking; the exact codes/coefficients/VLC values must still be reconciled against the current CNAM dentist convention and confirmed by an admin before clinical reliance. Supplying that source document at planning time would let the seed ship already-verified.
