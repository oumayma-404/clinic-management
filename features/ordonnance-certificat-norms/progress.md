# Progress: Ordonnance et certificat aux normes

**Started:** 2026-07-31
**Type:** Small
**Branch:** feature/audit-sections-3-to-10 (user: "start implementing in this branch now")

## Status
- [x] Implementation
- [x] Quality checks (backend build, frontend typecheck)
- [ ] Tests (handled by /test-small-feature)

## Working tree note
This branch now carries **three** features' uncommitted work: `mobile-tablet-responsive` (pre-existing),
`liaison-norms-and-document-email` (previous session) and this one. Two files are shared across features:
- `web/components/document-editor-content.tsx` — liaison work + this feature
- `web/components/treatment-plans/plan-workspace.tsx` — responsive work + the document-email work

Committing in feature order is the only way to separate them.

## Files Changed (9)

### Backend (7)
- **`Infrastructure/Services/DocumentIdentity.cs`** (new) — the single authority on the norm-mandated identity
  block. `PrescriberLines` (adresse, tél, **email**, praticien + qualité, **N° CNOMDT**) and `PatientLines`
  (nom, date de naissance, **sexe**, **poids**, médecin traitant). Every line omitted when unset.
- `Infrastructure/Services/PrescriptionContent.cs` (new) — the ordonnance body: per-line **voie** + **quantité**
  and the per-document **renouvellement** mention. Absorbed the legacy-string and malformed-JSON fallbacks that
  were three catch/else branches inlined in the renderer.
- `Infrastructure/Services/CertificatTextBuilder.cs` — finality clause added to `MandatoryMention`; `Build` lost
  its `ordreNumber` and `clinicAddress` parameters.
- `Infrastructure/Services/PdfGenerationService.cs` — `ComposeHeader`/`ComposePatientInfo` delegate to
  `DocumentIdentity` (+ a `RenderIdentityLine` helper); prescription case delegates to `PrescriptionContent`
  (−60 lines); certificat call site updated.
- `Application/Features/Documents/PractitionerRenderSnapshot.cs` — fifth reserved key `clinicEmail`, wired
  through `ApplyTo` (strip-then-write), `ReadFrom`, `OrElse`, `ResolveAsync` and `HasAny`.
- `Application/Features/Documents/Queries/GetPractitionerRenderSnapshotQuery.cs` + `MedicalDocumentPdfMapping.cs`
  + `Common/Models/MedicalDocumentPdfData.cs` — carry `ClinicEmail`, `PatientSex`, `PatientWeightKg`.
- `API/Controllers/MedicalDocumentsController.cs` — `ClinicEmail` added to the null-then-overlay block (2 lines).

### Frontend (1)
- `web/components/document-editor-content.tsx` — `MedicationLine` gains `route`/`quantity`; new
  `formatMedicationLine` + `formatRenewalMention` helpers; `renewals`/`patientSex`/`patientWeightKg` on the form
  with a fill-if-empty sexe prefill; save/hydrate/reset/render paths; editor inputs; preview mirrors the
  identity block, the renewal mention and the de-duplicated certificat prose.

### Tests (1, build-required)
- `UnitTests/Infrastructure/Services/CertificatTextBuilderTests.cs` — see DEV-2.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Extracted `formatMedicationLine` on the client | The preview and the Word export each already carried their own copy of the line formatting. Adding voie + quantité would have made **three** implementations of what a prescription line says (four counting the server). This removes a pre-existing duplication rather than adding to one. |
| `formatRenewalMention` mirrors the server's rule client-side | Same reason: the preview and Word export both need it, and the alternative is two inline copies. |
| Header separator `-` → `—` on the practitioner line | Fell out of routing the line through `DocumentIdentity`; typographically correct for the surrounding French text. |
| Weight unit appended only when absent | A dentist typing « 32 kg » must not get « 32 kg kg »; one typing « 32 » should still read as kilograms. |

## Significant Deviations

### DEV-1 — The certificat keeps the registering body in its prose, only the number moves
**Spec (AC-6) said:** the certificat prints the ordre number once, "in the identity block, not in the prose".
**Implemented:** the attestation sentence still reads « inscrit(e) à l'Ordre National des Médecins Dentistes
(CNOMDT), certifie avoir examiné ce jour … » — the registering **body** is named; only the **number** and the
cabinet address moved to the shared header.
**Why:** naming the body inline is the legal form of a certificate's attestation, not a duplicate of the number.
Stripping it to a bare « Je soussigné(e), Docteur X, médecin dentiste, certifie… » would have weakened the
document to tidy up an architecture. AC-6 as written says *number*, so this satisfies it — flagged because the
prose is not what the blueprint's wording implied.

### DEV-2 — One certificat test scenario retargeted
`Certificat_Without_Ordre_Uses_Placeholder` asserted that a missing ordre falls back to « sous le n° [Numéro] ».
That behaviour is deliberately gone: the prose no longer states a number, so there is no placeholder. It is now
`Certificat_Prose_Does_Not_State_The_Ordre_Number`, asserting the new behaviour. The guarantee it protected —
a missing ordre must not print an empty label — moved to `DocumentIdentity`, which omits the whole line;
covering that belongs to `/test-small-feature`.
`Certificat_Renders_Mandatory_Deontological_Mention` had its expected string updated to include the finality
clause (the spec pins it). The other four cases were untouched and still pass on their original assertions.

### DEV-3 — Legacy `doctorOrderNumber` fallback moved, not dropped
The certificat branch used to fall back to the hand-typed `content["doctorOrderNumber"]` when no snapshot ordre
existed. That fallback now lives in `DocumentIdentity.PrescriberLines`. Moving the number to the header without
carrying the fallback would have **silently erased the ordre from every legacy certificat** — worth naming
because it is invisible in a diff that only adds a line.

## Quality checks
- **Backend:** `dotnet build ClinicManagement.sln --no-incremental` → **0 errors, 57 warnings**, which is exactly
  the pre-existing baseline this repo's earlier feature work documented. The five warnings my grep surfaced in
  `MedicalDocumentsController.cs` (lines 113/122/161/201/207) are pre-existing multipart-parsing nullability
  warnings; `git diff -U0` confirms my change to that file is two lines, at 288 and 295.
- **Frontend:** `npx tsc --noEmit` → clean. ESLint is not installed in this repo and `next.config.ts` disables it
  during build, so `tsc` is the gate. `next build` was not run (it writes to the `.next` the running dev server
  owns).
- No migration — verified `Patient.Gender` and `Clinic.Email` already exist and nothing was added to the schema.

## What /test-small-feature should cover
- `DocumentIdentity`: a line is **omitted** (not blank-labelled) when its value is unset — the guarantee DEV-2
  moved here; the legacy `doctorOrderNumber` fallback (DEV-3); the weight-unit rule; identical output across all
  four document types (AC-2).
- `PrescriptionContent`: voie/quantité/renouvellement formatting; « non »/« 0 » → non-renouvelable; a legacy
  string blob and malformed JSON both degrade to one verbatim line (AC-4).
- `PractitionerRenderSnapshot`: a client-supplied `clinicEmail` is stripped and replaced (AC-8);
  `OrElse` preserves a document's stored email when the editing caller has no doctor record.
- `MedicalDocumentPdfMapping`: `patientSex`/`patientWeightKg` survive the ContentJson round-trip (AC-7).
- ⚠️ Existing suites likely to need attention: `LiaisonRenderContentTests`, `GenericDocumentRenderTests` and
  `PractitionerRenderSnapshotTests` all touch the surfaces changed here.
