# patient-original-creation-date — shipped notes

A patient brought over from a practice's previous software keeps the day their file was opened there.

## What it does

- `CreatePatientCommand.RegisteredOn` (optional calendar day). Omitted — which is what the patient form, the booking
  dialog's « Nouveau patient » and the devis form all do — means « today », exactly as before.
- The CSV import gained the column **« Inscrit le »** (aliases « Date de création », « Créé le », « Date
  d'inscription », « Patient depuis »…). It is the patient export's own heading, so a file this product wrote
  re-imports with its real dates (`PatientRegistrationDateTests` derives that from the export itself).
- `C:\dev\clinic-migration\migrate.mjs` sends it from the plan's `cree_le` column.

## Decisions that are easy to undo by accident

- ⚠️ **One rule, two doors.** `PatientRegistrationDate.Refusal` is asked by `PatientFromRequest.Build` (every
  creation) *and* by `PatientImportRowReader` (the CSV dry run), so the preview cannot promise a row the save refuses.
  Refused: a day in the future, before 1900, or before the date of birth.
- ⚠️ **Stored at noon in Tunis (11:00 UTC), never midnight.** Tunis midnight is 23:00 UTC the previous day, so a
  reader of the bare UTC date would print the day before. Capped at « now » so « today » never stores a future instant.
- ⚠️ **`Patient.RecordOriginalRegistration` only moves the date earlier.** A later date would claim the patient joined
  after the record existed.
- **No migration**: `Patients.CreatedAt` already exists. No backfill: patients created before this keep their dates.
- The audit row still says the patient was *created* on the import day — true, and the « Journal » is the place that
  must say so.

## Blast radius (written before the edit)

| # | Touching | Other consumers | Verdict |
|---|----------|-----------------|---------|
| 1 | `Patient.CreatedAt` gains a second writer | dashboard « Nouveaux patients » (`CountCreatedBetweenAsync`), `/patients` « Inscrits du … » filter + newest-first order, CSV export + dossier « Inscrit le » | must re-test — measured: 4 imported, « Nouveaux patients » ce mois = 1 (the one with no date); export prints the old dates |
| 2 | `CreatePatientCommand.RegisteredOn` | the form, the booking dialog, the devis form (none send it) | unaffected — optional, absent = today |
| 3 | `PatientFromRequest.Build` | `CreatePatientCommandHandler`, `ImportPatientsCommand` | must re-test — full suite green |
| 4 | `PatientImportField.RegisteredOn` | preview fields list, mapping JSON (by name), the web dialog (renders what the server lists) | unaffected — appended, served |
| 5 | archive / restore | `CreatedAt` is an ordinary archived column | unaffected |

Also fixed while in the file: the CSV dry run warned « Date de naissance absente : 30 ans par défaut », untrue since the
placeholder was retired.

## Verified (2026-11-24, local only)

- Full unit suite: 7 298 passed, 0 failed.
- Local test server built from this branch: the import tool created 4 patients with 2015 / 2011 / 2019 dates; the
  CSV import's dry run read « Inscrit le », and refused a future day and a day before the birth with the shared sentence.
