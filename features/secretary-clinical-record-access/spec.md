# Secretary access to the clinical record

**Type:** Small
**Status:** Implemented
**Branch:** `feature/audit-sections-3-to-10`

## The report

> « What we have for the assistant/secretary view is what the assistant needs to see, but on the patient page a lot
> of things appear with a warning, you are not allowed to edit this. I have talked to an actual dentist, and he told
> me that the assistant should be able to see all the patient info, including the medical records; moreover, the
> assistant/secretary can add the patient's medical record sometimes, so I do not think we should block that role for
> these edits. Secretary cannot see factures and money related, but can see and edit the patient page whole. »

## What was actually happening

The warning is **`web/lib/api/client.ts:103`** — « Vous n'avez pas les droits nécessaires pour cette action. » — the
client's 403 fallback for a body-less refusal. ASP.NET's authorization pipeline returns that **before any handler
runs**, which is why it appeared repeatedly on one page load rather than on a click.

Five entire controllers behind the patient page were `AdminOrDoctor`, **`GET`s included**:

| Surface | Controller | Was |
|---|---|---|
| Dossiers médicaux (fiches de soins) | `DentalRecordsController` | `AdminOrDoctor` |
| Odontogramme (strip + tab) | `OdontogramController` | `AdminOrDoctor` |
| Antécédents médicaux | `PatientMedicalHistoryController` | `AdminOrDoctor` |
| Antécédents familiaux | `PatientFamilyHistoryController` | `AdminOrDoctor` |
| Documents (ordonnance, certificat, BS1, arrêt de travail) | `MedicalDocumentsController` | `AdminOrDoctor` |

The client was barely involved: the whole patient page carried **one** role gate (two delete buttons). Nothing was
hidden and nothing said « lecture seule » — the page rendered and then its requests failed.

## The finding that decided the approach

The boundary was already incoherent, and had been since it shipped. `AuthorizationPolicies` chartered
`AdminOrDoctor` as *"clinic-wide money **and clinical authorship / clinical free text**"*, but:

- `PUT /api/patients/{id}` is `AnyClinicRole` and writes **`Allergies`, `MedicalHistory`, `Notes`,
  `ImportantNotes`** (`Patient.cs:31-32,55,58`; `UpdatePatientCommand.cs:257-297`).
- `POST /api/patients` is `AnyClinicRole` and **inserts `PatientMedicalHistory` child rows**
  (`CreatePatientCommand.cs:170-187`) — rows whose own controller was `AdminOrDoctor`.

So a secretary could always type a patient's allergies and medical history through « Modifier », and was refused
*reading* the same information one tab over. This is not a new privilege being granted; it is the half of an
inconsistency that was drawn in the wrong place.

`features/adoption-qa-i-access-control-and-audit/spec.md:39,47` shows the `GET dental-records` fork was recognised
and decided the strict way, with the recorded reason being *"reception can tell a visit was billed from
`AppointmentDto.InvoiceId`"* — a claim about billing status, not about whether reception needs the clinical record.
`progress.md:40-44` logged the choice. **This feature reverses that logged decision, on a practising dentist's
account of who fills the record in.**

## Decisions taken (asked and answered)

| Question | Decision |
|---|---|
| Approach | Re-charter `AdminOrDoctor`; clinical record → `AnyClinicRole` for read + create + update |
| Ordonnances / certificats / BS1 / arrêt de travail | **Full create + edit**, and fix the cachet resolution (below) |
| Per-patient money on the patient page (« Factures », « Plan de traitement ») | **Unchanged** — per-patient yes, clinic-wide no, exactly as before |
| Archiver/Désarchiver, patient file delete | **Left gated** — out of scope, revisit if raised |

## The charter after this change

`AnyClinicRole` — reception's job, per-patient money, **the patient's clinical record (read + record, never
delete)**, and the shared reads.

`AdminOrDoctor` — clinic-wide money · the corrective money operations · **deleting from the clinical record** ·
the bulk/lifecycle patient operations (CSV import, archiver).

**Record yes, erase no.** The four deletes keep `AdminOrDoctor` and are now the *only* gate on them:

- a fiche de soins (detaches the invoice lines and devis acts built from it),
- a medical document (**and its blob** — nothing to restore),
- an antécédent médical (**this is where an allergy lives**),
- an antécédent familial.

`OdontogramController.RemoveCondition` is deliberately **open**: it removes a charted *diagnosis* — charting's own
undo for a mis-clicked tooth — and cannot touch a treatment entry, which is edited through the fiche that produced
it.

## Why the wider write surface is accountable, not merely wider

Two mechanisms already existed and are what make this safe:

- **`AuditSaveChangesInterceptor`** stamps the actor on every mutated aggregate root, readable at `GET /api/audit`.
- **`PractitionerAttribution.Resolve`** puts the caller **last**, and a secretary has no `Doctor` record — so a
  reception-recorded fiche is credited to the visit's dentist, or left honestly `null`. Never to reception.

## The second half: whose cachet a document carries

Opening document authorship would otherwise have shipped a silent defect.
`PractitionerRenderSnapshot.ResolveAsync` resolved the cachet + n° d'ordre CNOMDT from the **caller's** `Doctor`
record. An ordonnance typed by reception would render with **no** practitioner identity at all — no cachet, no ordre
— silently, on the one class of document whose purpose is to carry them.

`ResolveAsync` now takes an **`IssuingDoctorId`** and resolves **chosen practitioner → caller → none**, which is
`PractitionerAttribution`'s rule one candidate shorter and for the same reason. The chosen id is **tenant-checked**
against the clinic roster; a foreign, stale or `Guid.Empty` id *falls through* rather than resolving.

⚠️ **No schema change, no migration, no `verify-schema` entry.** The code's own note said fixing this *"would
require a persisted DoctorId (out of scope here)"*. It did not: the **resolved** snapshot has always been persisted
into the document's `ContentJson` by `ApplyTo`, and preserved across edits by `ReadFrom` + `OrElse`. What was
missing was a **selector on the request** — which the editor already computed (`selectedDoctorId`) and simply never
sent.

⚠️ **`IssuingDoctorId` is a selector, not a value**, and that is the whole security argument. The controller keeps
clearing the four reserved snapshot values plus `clinicEmail` from any client payload, because `DoctorCachetKey` is a
storage key the unauthenticated `PdfGenerationJob` later dereferences. An id is checked against the caller's own
roster; a key would be trusted.

⚠️ On the update path the resolution stays **inside** the existing `user != null` guard: the background PDF job
feeds a stored document back through the same handler with no caller, and must preserve its snapshot verbatim.

## Adjacent defects fixed in the same change

1. **The `doctors[0]` fall-back is gone for every document type.** K3 scoped its removal to the BS1 on the grounds
   that the wrong name only costs a rejected claim there, and that removing it elsewhere would change what three
   other types print as a side effect of a CNAM fix. Both premises assumed the caller was a practitioner, so
   `currentUserDoctor` answered first and the guess was nearly unreachable. With reception authoring, the guess
   became the **normal** path — and on a prescription it is an attribution to a dentist who did not write it. The
   defaulting effect still pre-fills the caller's own record and still pre-fills a single-practitioner cabinet;
   only the ≥2-practitioner-with-no-linked-doctor case now requires an explicit choice.
2. **The phone tree hid « Ouvrir le document ».** `app/patients/[id]/page.tsx` gated the *whole* dropdown on
   `canDeleteClinicalRecords` in the card-list tree while the table gated only its delete button — so a secretary
   lost read access to a document on a phone and kept it on a desktop. § 0 of the device contract: no capability
   removed by a layout decision.

## Files changed

**Policies** — `DentalRecordsController`, `OdontogramController`, `PatientMedicalHistoryController`,
`PatientFamilyHistoryController`, `MedicalDocumentsController` (class → `AnyClinicRole`; two new explicit
`AdminOrDoctor` deletes on the history controllers).

**Charter** — `Application/Common/Authorization/AuthorizationPolicies.cs` (both policy doc blocks + the class-level
summary + the two `AddPolicy` comments).

**Practitioner resolution** — `Features/Documents/PractitionerRenderSnapshot.cs`,
`Features/Documents/Queries/GetPractitionerRenderSnapshotQuery.cs`,
`Features/Documents/Commands/CreateMedicalDocumentCommand.cs`, `.../UpdateMedicalDocumentCommand.cs`,
`Common/Models/MedicalDocumentPdfData.cs`, `API/Models/CreateMedicalDocumentRequest.cs`,
`API/Controllers/MedicalDocumentsController.cs` (both the multipart binder and the render overlay).

**Frontend** — `components/document-editor-content.tsx`, `lib/api/medical-documents.ts`,
`app/patients/[id]/page.tsx`.

**Tests** — new `UnitTests/Api/ClinicalRecordAccessTests.cs`; extended
`UnitTests/Api/ClinicalRecordDeletionAuthorizationTests.cs` and
`UnitTests/Features/Documents/PractitionerRenderSnapshotTests.cs`.

**Docs** — root `CLAUDE.md`, `api/ClinicManagement.API/CLAUDE.md`,
`api/ClinicManagement.Application/CLAUDE.md`, `web/CLAUDE.md`, `web/components/CLAUDE.md`.

## Gate results

| Gate | Result |
|---|---|
| `dotnet build` (solution) | **0 errors**, 58 warnings (all pre-existing) |
| `dotnet vstest` (full suite) | **1835 passed / 24 failed / 1859** |
| Baseline at HEAD, same command | **1816 passed / 24 failed / 1840** — the *same* 24 |
| `npx tsc --noEmit` | clean |
| `npm run check:responsive` | all 11 checks pass |
| `npm run build` | clean (needed a `.next` wipe first — a stale turbopack chunk) |

The 24 failures are **pre-existing and unrelated** — verified by stashing this change and re-running: identical set
(CnamNomenclature + Medications query filtering, Stock, two tenant-isolation list tests, `CreditNoteReadTests`,
`LiaisonRenderContentTests`). They are the stale-fixture shape `UnitTests/CLAUDE.md` warns about: mocked repositories
returning everything while the filters moved into SQL. **Not fixed here** — out of scope, and they belong to whoever
owns the paging/SQL-search work.

## Still owed

- **The manual pass as a `secretary` account** has not been run — no seeded secretary login was available in this
  session. The walk to do: open a patient → every tab loads with no refusal → add a fiche de soins and confirm the
  **dentist** is the attributed practitioner, not the secretary → chart a tooth → add an antécédent, then confirm
  **delete** is refused → create an ordonnance choosing a practitioner and confirm the rendered PDF carries **that
  practitioner's** cachet and n° d'ordre → then confirm `/factures`, `/caisse`, `/creances`, `/cheques` and `/` still
  refuse while « Solde patient » and taking a payment still work.
- **An eye pass at 320/390/820/1180/1440 px** on the Documents tab's card list (the menu-gate fix) and the document
  editor's practitioner `Select`.
- **`features/LEARNINGS.md` is not updated** — every one of its 38 entries is still `Discovered in:
  windows-desktop-app`, so nothing from any adoption-QA feature has ever been captured there. This change's lesson
  (a policy whose *charter text* contradicts an adjacent open endpoint is a boundary that protects nothing) belongs
  in it, along with I1's own.
