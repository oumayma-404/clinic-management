# Feature Specification: Ordonnance et certificat aux normes

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-31
**Scope:** Full
**Feature:** Complete the legally-mandated mentions on the ordonnance and the certificat, and make the identity block one shared authority instead of something each document type re-derives.

## Overview
An ordonnance currently omits the prescriber's **CNOMDT ordre number** (already snapshotted, printed only by the
certificat — inside its prose), the cabinet's **email**, and the patient's **sexe** — all mandatory under
R.5132-3 for listes I/II medicines, which covers most antibiotics and analgesics a dentist prescribes. Its
medication lines carry no **voie d'administration**, **quantité** or **renouvellement**. The certificat is
otherwise compliant but lacks the finality clause « pour faire valoir ce que de droit ».

The root cause is structural: identity has no shared owner, so the certificat smuggles the ordre into its body
text while the header has nowhere to put it. This makes identity one authority rendered for **every** type, then
adds each document's own missing body fields.

## What Changes
- A single identity authority composes the norm-mandated identity lines and is rendered for **all** document
  types: prescriber (nom, spécialité, **n° CNOMDT**, adresse, téléphone, **email**) and patient (nom, date de
  naissance, **sexe**, **poids** when supplied).
- The certificat stops repeating the ordre number and address in its prose — the header carries them. It keeps
  « Je soussigné(e) … certifie avoir examiné ce jour », which is the *faits médicaux personnellement constatés*
  attestation, not identity.
- The certificat's mandatory mention gains the finality clause: « … et remis en main propre **pour faire valoir
  ce que de droit.** »
- Each medication line gains an optional **voie d'administration** and **quantité** (nb de boîtes / unités);
  the ordonnance gains an optional **renouvellement** mention (incl. « non renouvelable »).
- The patient's **poids** is captured per-document on the ordonnance and snapshotted onto it — never stored on
  the patient, because a stale weight that looks verified is worse than a blank one.
- **Every** norm value is snapshotted into the document's `ContentJson` at create/update, so the unauthenticated
  background `PdfGenerationJob` renders the same bytes as the download path.
- Nothing new is ever required. Every added field renders only when filled — same rule as the liaison.

## Acceptance Criteria
- **AC-1:** An ordonnance PDF shows the prescriber's nom, spécialité, n° CNOMDT, adresse, téléphone and email;
  each line is **absent entirely** (no blank label) when its value is unset.
- **AC-2:** The identity block is byte-identical across ordonnance, liaison, certificat and bulletin CNAM — one
  composer, not four call sites.
- **AC-3:** An ordonnance PDF shows the patient's nom, date de naissance and sexe; poids appears only when
  supplied on that document.
- **AC-4:** A medication line renders its voie and quantité when set and omits them when not; a legacy line
  (no voie/quantité) renders exactly as it does today.
- **AC-5:** The renouvellement mention appears once per ordonnance when set, never per line.
- **AC-6:** The certificat prints the ordre number **once** (in the identity block, not in the prose) and its
  mention ends with « pour faire valoir ce que de droit. »
- **AC-7:** A document re-rendered by `PdfGenerationJob` carries every norm value — ordre, email, sexe, date de
  naissance, poids, voie, quantité, renouvellement — with no live doctor/clinic/patient lookup.
- **AC-8:** A client-supplied `clinicEmail` in a create/update payload is **stripped** and replaced by the
  server-resolved value, like the existing four reserved keys.
- **AC-9:** Saving an ordonnance or certificat with none of the new fields filled still succeeds.

## Data / Schema Changes
- **No migration.** `Patient.Gender` and `Clinic.Email` already exist; every new value rides in `ContentJson`.
- `PractitionerRenderSnapshot` gains a fifth reserved key `clinicEmail` — resolved server-side, stripped from
  client payloads, and counted by `HasAny`.
- New non-reserved `ContentJson` keys: `patientSex`, `patientWeightKg`, `renewals`; per medication line, `route`
  and `quantity`.
- `MedicalDocumentPdfData` gains `ClinicEmail`, `PatientSex`, `PatientWeightKg`.
  ⚠️ `PatientAge` is **not** renamed despite holding a formatted date de naissance: it is a persisted
  `MedicalDocument` column *and* a field on the body the client posts to `generate-pdf-download`, so a rename
  costs a migration and a wire-contract change for no behavioural gain. A comment at each site records it.

## Out of Scope
- The **CNAM P61 / AT22 arrêt-de-travail overlay** — a separate feature, blocked on obtaining the official form
  PDFs and a coordinate-calibration pass (the BS1 renderer is its precedent).
- Storing poids or taille on `Patient`.
- Enforcing or validating any norm field — all remain optional.
- The *dires du patient* convention (conditional + quotes) — editorial guidance, not a field.
- Reworking `ContentJson` into typed per-type content models.

## Edge Cases (Critical only)
- A cabinet with no email / a practitioner with no CNOMDT number: those lines are simply absent, and nothing
  fails — the same degradation the cachet already has.
- A secretary or admin editing a doctor's document must not overwrite the issuing practitioner's identity:
  `clinicEmail` follows the existing `ReadFrom`/`OrElse` preservation path, not the caller's own record.
- A legacy document whose `ContentJson` has none of the new keys renders exactly as it does today.
- `Patient.Gender` is non-nullable but historical rows may hold a free-text value — it is printed verbatim, never
  mapped or refused.
- A poids typed as a non-numeric string is printed as entered rather than refused: the field is a mention on a
  document, not an input to a calculation.
