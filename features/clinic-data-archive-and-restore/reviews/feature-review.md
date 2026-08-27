# Feature Review: clinic-data-archive-and-restore

**Status:** INCOMPLETE
**Challenged:** No
**Date:** 2026-08-12
**Parent Branch:** main
**Merge Base:** 9798b95d31f55ee07f2ad5e0af5550c4c2831022
**Files Reviewed:** 35 files (+4859, −20)

## Scoping

The feature is **uncommitted**, and the working tree is shared with the in-flight sibling
`backup-works-everywhere`. The reviewable diff was therefore built from `progress.md`'s own "Files Changed"
list (not from `git status`), assembled via `git add -N` on this feature's new paths + `git diff HEAD` over an
explicit pathspec, then the index was restored (`git reset -q HEAD -- .`) — nothing was committed or staged.

**Excluded as another feature's work:** `PostgresToolLocator.cs` + its tests, `RestoreBackupCommand.cs`,
`PgDumpBackupService.cs`, `DeploymentProfile.cs`, `DirectoryAclHardener.cs`, `GetBackupHistoryQuery.cs`,
`Program.cs`, `Dockerfile`, `docker-compose.selfhosted-lan.yml`, the four `CLAUDE.md` files, `start.ps1`, and the
`web/` auth files (`login/page.tsx`, `change-password-form.tsx`, `bff/auth/change-password/route.ts`,
`tsconfig.json`).

**Three files carry both features' edits** and only the archive hunks were reviewed:
`api/ClinicManagement.API/Controllers/BackupController.cs`, `web/lib/api/backup.ts`,
`web/components/backup-settings.tsx`.

**Review method:** five agents (Code Quality & Architecture · Business Logic · Breaking Changes & Regression ·
Security & Tenant Isolation · Device & UX). ROP was dropped — this repo uses MediatR + `Result<T>`, not
`Extensions.ROP`. A dedicated Security agent was added because the feature exports an entire clinic's medical
and financial record set as an unencrypted file and accepts one back, writing rows past every domain
constructor on a database shared by every cabinet. Device & UX was mandatory (a `.tsx` file is in the diff).
Findings marked **[verified]** were independently confirmed against the source by the orchestrator.

**Two negative results, recorded so they are not re-litigated:** `RestoreTableAsync`'s `plan is null` and
`SingleGuidKey is null` branches are unreachable today (`CanRestore` uses the identical lookup; the only
composite PK in the model is `NotificationRead`, which is excluded), and `FindResolvedParentLink` picks the
correct ownership edge for every table in the current model with `ClinicArchivePlan.Warnings` empty.

---

## Findings

### Finding 1
- **Severity:** Critical
- **Category:** Business Logic
- **File:** api/ClinicManagement.Infrastructure/Persistence/ClinicArchiveStore.cs
- **Line:** 475
- **Anchor:** `ClinicArchiveStore.ArchivedProperties`
- **Comment:** **[verified]** `p.ValueGenerated == ValueGenerated.Never` is intended to exclude `xmin`, but EF sets `ValueGenerated.OnAdd` for every property configured with `HasDefaultValue(...)`. All such columns are therefore **neither archived, nor restored, nor compared by `RowsMatch`**: `Materialize` leaves the CLR default, `StageInsert` never assigns it, EF omits it from the INSERT, and the *database* default wins. Two are money: `Payment.IsVoided` and `InstallmentPayment.IsVoided` (`PaymentConfiguration.cs:35`, `InstallmentPaymentConfiguration.cs:42`, both `HasDefaultValue(false)`) — **a voided payment restores as live money**, inflating « Encaissé », la caisse, l'extrait, the dashboard's Argent section, the patient's balance and the console's `PlatformCollectedReader` figure, with the void's motif and actor columns preserved beside a `false` flag. Also lost, each replaced by its default: `Clinic.VatApplicable`/`VatRate` (→7)/`StampDutyEnabled`/`StampDutyAmount` (→1,000)/`RecallIntervalMonths` (→6), which makes DEV-4's stated justification for restoring the `Clinic` row rather than provisioning it false; `Patient.IsArchived` (archived patients un-archive, defeating the documented deletion escape hatch); `TreatmentPlanItem.SequenceNumber` (→0, every devis loses its act ordering); `TreatmentPlan.RevisionNumber` (→0, the counter identifying a patient's earlier printout); `StockItem.CurrentStock`/`MinimumStockLevel`/`MaximumStockLevel` (→0); `ProcedureType.IsActive`/`PatientFlag.IsActive` (deactivated rows restore active). Because `RowsMatch` uses the same predicate, a row differing *only* in these columns reports « déjà présent » — silent in every direction, and `MoneyReadConsistencyTests` cannot see it because no restore is involved. Fix: exclude only the concurrency token, `OnAddOrUpdate`, and computed columns — i.e. **keep** `OnAdd` properties carrying a default value (safe here because this repo sets `Id` to `ValueGeneratedNever()` in every other config) — and add a derived guard asserting every mapped scalar with a `HasDefaultValue` appears in `ReadRow`'s output.

### Finding 2
- **Severity:** Critical
- **Category:** Business Logic
- **File:** api/ClinicManagement.Infrastructure/Persistence/ClinicArchiveStore.cs
- **Line:** 134
- **Anchor:** `ClinicArchiveStore.RestoreTableAsync` (`TryReadGuid` over `key.Name`)
- **Comment:** **[verified]** Same root cause as Finding 1, but on the **primary key**, so it is a silent total loss. There is no global `ValueGeneratedNever` convention (`ApplicationDbContext.ConfigureConventions` sets only decimal precision), and three *archived* configs never declare it — `MedicalDocumentConfiguration`, `PatientMedicalHistoryConfiguration`, `PatientFamilyHistoryConfiguration` (the two others that omit it, `User` and `NotificationRead`, are excluded from the archive). EF's convention gives a single `Guid` key `ValueGenerated.OnAdd`, so `ArchivedProperties` omits `Id` for those three: the archive's rows carry no key, `TryReadGuid` returns false for every row, `byId` stays empty, `RestoreTableAsync` returns `Empty`, and `Accumulate` skips the zeros — so the entity appears in **none** of the report's three dictionaries. Net effect: **every ordonnance, certificat, lettre de liaison, bulletin CNAM and arrêt de travail, plus every antécédent médical and antécédent familial, is unrestorable**, while the manifest declares their row counts and the screen reports success. The antécédents are where **allergies** live — the API guide cites exactly that in justifying `AdminOrDoctor` on those deletes — so a restored patient file comes back looking complete having lost its allergy history, with nothing reporting it. `ClinicArchiveStoreMaterializationTests.The_Concurrency_Token_Is_Not_Archived` asserts `ContainsKey("Id")` for `Patient` only. Fix: Finding 1's filter change, plus a derived guard that every planned table's single-`Guid` PK is present in `ArchivedProperties`, and make an unreadable key a **warning** rather than a silent `Empty`.

### Finding 3
- **Severity:** Critical
- **Category:** Business Logic
- **File:** api/ClinicManagement.Infrastructure/Persistence/ClinicArchiveScope.cs
- **Line:** 148
- **Anchor:** `ClinicArchiveScope.Resolve` (the `Direct` pass)
- **Comment:** **[verified]** « Parents before children » is established for `Child`-scoped tables only. Every table with its own `ClinicId` is appended with **no regard for the foreign keys between Direct tables**, and `ClinicArchiveRestorer.ApplyAsync` commits one `SaveChangesAsync` **per table**, so EF's per-save topological sort cannot reorder across tables. Confirmed real FKs that are violated on a full restore — the total-loss case the feature exists for: `DentalRecord.PatientId` → `Patients` (`DentalRecordConfiguration.cs:107-110`, required + `Cascade`; `DentalRecord` is planned before `Patient`) fails **unconditionally**; `Appointment.PatientId` → `Patients` (`AppointmentConfiguration.cs:68-72`, nullable but enforced when set — nearly every appointment has a patient, so this is the most commonly hit); `MedicalDocument.PatientId` (`Restrict`), `LabWorkOrder.PatientId` (`Cascade`), `Appointment.DoctorId`/`ProcedureTypeId`, `DentalRecord.DoctorId`, and `PatientFile.FolderId` → `PatientFolders`. The restore aborts mid-way and leaves the cabinet holding whichever tables landed first. Note the defect does not even depend on EF's enumeration order being alphabetical: nothing in `Resolve` orders Direct tables against each other *at all*, so correctness rests on an undocumented `GetEntityTypes()` ordering. `ClinicArchiveScopeTests.Parents_Are_Planned_Before_Their_Children:131` guards only `Scope == Child`, so it is vacuous here while its own comment (lines 119-120) describes precisely the reachable failure. Fix: fold the Direct tables into the same fixpoint walk, admitting a table once every planned table it references is planned (`Clinic` seeded first, self-references skipped, deterministic tie-break), and extend the test to assert the property over every FK edge of every planned table. **Two of the originally-reported examples are wrong and should not be cited:** `CreditNote.InvoiceId` and `Invoice.PatientId` are bare properties with bare indexes and **no** `HasOne`/`HasForeignKey`, so there is no constraint to violate.

### Finding 4
- **Severity:** Critical
- **Category:** Business Logic
- **File:** api/ClinicManagement.Application/Features/Backup/Archive/ClinicArchiveRestorer.cs
- **Line:** 77
- **Anchor:** `ClinicArchiveRestorer.ApplyAsync`
- **Comment:** No transaction spans the per-table saves and **neither restore handler has a `try`/`catch`**, departing from the layer's convention (every other handler wraps its body, returns `Result.Failure`, and carries `when (ex is not ConflictException)`). Any failure at table *n* — an FK violation per Finding 3, a unique-index collision per Finding 9, a `NOT NULL` from Finding 1, a cancellation — commits tables 1..*n*−1, throws out of the handler, and surfaces through `ExceptionMiddleware` as a **generic 500**. The `ClinicArchiveRestoreReport` that is the endpoint's entire contract is built only on the success path, so the owner is left with a half-restored cabinet, no per-entity account of what landed, and `ErrorMessages.Generic` — while the spec's own framing (« Aucune donnée n'a été modifiée » on every refusal) implies this cannot happen. A `ConflictException` would also escape as a 409 that `web/lib/api/backup.ts` has no branch for. Fix: wrap the apply in one `IUnitOfWork.BeginTransactionAsync`/`Commit` (which also dissolves Finding 3, since one `SaveChanges` lets EF sort the inserts), or catch per table and return the partial report as a failure naming the table that stopped it.

### Finding 5
- **Severity:** Critical
- **Category:** Business Logic
- **File:** api/ClinicManagement.Application/Features/Platform/Commands/RestoreClinicFromArchiveCommand.cs
- **Line:** 163
- **Anchor:** `RestoreClinicFromArchiveCommandHandler.Handle`
- **Comment:** On the console path every remaining step runs **after** `ApplyAsync` has committed the cabinet's rows, and the door is single-use: the live-cabinet guard (line 142) keys on `_clinics.GetByIdAsync(manifest.ClinicId)`, which now returns the just-restored `Clinic` row. So any failure between line 163 and the `SaveChangesAsync` at line 201 — the "no `Clinic` row" refusal at 169, a lost race on the lowercased-email unique index, an entitlement `xmin` conflict, an access-ledger fault, a container restart — leaves the practice's patients, invoices and files committed with **no administrator, no entitlement (FR-13) and no journal row**, and every retry is answered `409 clinic_exists` (« Sa restauration se fait depuis « Paramètres » par son propre administrateur » — an administrator that was never created). The cabinet is then unrecoverable by either door short of deleting the row in SQL. The refusal at 169 is the clearest case, and `PlatformClinicRestoreTests.An_Archive_That_Puts_Back_No_Cabinet_Record_Cannot_Re_Create_One` passes on the wrong shape because the fake store keeps its restored rows while the test only asserts `Assert.Empty(_created)`/`Assert.Empty(_ledger.Rows)`. Fix: validate the archive's own `Clinic` row (present, and its `Id` equals `manifest.ClinicId`) **before** `ApplyAsync`, and open one transaction spanning the apply, the admin, the entitlement and the access entry — or treat « `Clinic` row exists but has no `User` » as resumable rather than as `clinic_exists`.

### Finding 6
- **Severity:** Critical
- **Category:** Security
- **File:** api/ClinicManagement.Infrastructure/Persistence/ClinicArchiveStore.cs
- **Line:** 384
- **Anchor:** `ClinicArchiveStore.StageInsert`
- **Comment:** **[verified]** The file's primary keys are trusted, so a clinic admin can insert arbitrary new `Clinic` rows. `StageInsert` re-stamps only a property literally named `ClinicId` of type `Guid` (line 391); `Clinic` has **no such property** (its identity is `Id`), and `ClinicConfiguration.cs:16` declares `Id` `ValueGeneratedNever()` so it *is* in `ArchivedProperties` and is written verbatim from `data/Clinic.json`. Attacker: any clinic admin (on `HostedMultiTenant`, self-signup makes any signup an admin on the shared database). Input: their own archive with the manifest untouched — so `RestoreClinicArchiveCommand`'s AC-6 `manifest.ClinicId == caller.ClinicId` check passes — and `data/Clinic.json` replaced by N objects each with a fresh GUID `Id`, chosen `Name`/billing settings, and `Code: null` (the unique index is partial on `"Code" IS NOT NULL`, so nulls are unlimited — verified). None of those ids exist, so every one is staged `Added`. Consequence: N permanent phantom cabinets, each with no `User` and no `ClinicSubscription` — they appear in the vendor's portfolio and summary strip, they turn `verify-schema`'s `every-clinic-has-an-entitlement` and `clinic-activity-snapshot-covers-every-clinic` red for ever (the schema gate is the *only* automated check a migration has), `ClinicActivityCounterJob` iterates them nightly, and nothing in the product deletes a `Clinic`. Unbounded, needs no knowledge of another tenant, and the endpoint carries `[AllowsWithoutSubscription]` so an expired read-only cabinet can do it too. Fix: for `ClinicArchiveTableScope.Self`, refuse any row whose primary key is not the target `clinicId` (there is exactly one legitimate row); more generally validate every row's key against the manifest's declared scope before staging.

### Finding 7
- **Severity:** Critical
- **Category:** Security
- **File:** api/ClinicManagement.Application/Features/Backup/Archive/ClinicArchiveRestorer.cs
- **Line:** 191
- **Anchor:** `ClinicArchiveRestorer.ReadAllTextAsync` (and `BufferAsync`, line 200)
- **Comment:** Unbounded decompression — a zip bomb takes the whole hosted backend down. `ApplyAsync` calls `ReadAllTextAsync(entry)` → `StreamReader.ReadToEndAsync()`, decompressing each `data/*.json` entry in full into one UTF-16 `string` (~2 GB of heap per 1 GB entry) before a row is parsed, and `JsonSerializer.Deserialize<List<JsonObject>>` doubles it. Nothing checks `ZipArchiveEntry.Length`, the entry count, or a compression ratio; the only gate is `IFormFile.Length` on the **compressed** upload, which a ~1 MB bomb deflated at ~1000:1 passes trivially. Attacker: any clinic admin. Consequence: OOM / container kill; on `HostedMultiTenant` one process serves every practice, so one crafted upload takes every cabinet offline. Repeatable — the global limiter partitions on the client address rather than the user, because `app.UseRateLimiter()` runs before `app.UseAuthentication()` so `RateLimiting.PartitionKey`'s `HttpContext.User` subject is always null. Fix: refuse before reading any entry whose `entry.Length` (and the summed total) exceeds a configured uncompressed budget or whose `entry.Length / entry.CompressedLength` exceeds a ratio ceiling; stream each entry through `JsonSerializer.DeserializeAsyncEnumerable` over a length-capped wrapper instead of `ReadToEndAsync`; spool `BufferAsync` to a temp file.

### Finding 8
- **Severity:** Critical
- **Category:** Device & UX
- **File:** web/components/backup/clinic-archive-card.tsx
- **Line:** 160
- **Anchor:** `ClinicArchiveCard`
- **Comment:** **[verified]** At 320–390 px the confirm is a bottom sheet, and `onOpenChange={(open) => !open && setPending(null)}` accepts **every** dismissal channel with no guard on `restoring`: outside tap, `Escape`, and the primitive's own close button (`dialog.tsx:125` defaults `showCloseButton = true`). Both footer buttons *are* correctly disabled in flight, so the only controls that still respond mid-restore are the three that close the sheet — on the operation whose own copy says « L'opération peut durer plusieurs minutes… Ne fermez pas cette page. » Consequence on a phone: a thumb brushes the scrim two minutes into a full-cabinet restore, the sheet vanishes, and the card behind it shows **nothing** — no spinner, no « Restauration en cours » — so the user reads the operation as cancelled, and their next move (navigate away, close the tab, press « Restaurer » again) genuinely abandons a request the backend is committing table by table. This compounds with Finding 15: the client aborts at 3 minutes anyway. The `catch` comment's promise that « the dialog stays open with the file still selected » is also false once dismissed — `pending` is null, so the refusal lands in a toast with the file gone. Fix: `if (!open && !restoring) setPending(null)`, `onEscapeKeyDown`/`onInteractOutside` `preventDefault()` and `showCloseButton={!restoring}` while restoring, **and** render an in-flight `role="status"` line on the card itself so a long operation is visible from where the user actually is.

### Finding 9
- **Severity:** Major
- **Category:** Business Logic
- **File:** api/ClinicManagement.Infrastructure/Persistence/ClinicArchiveStore.cs
- **Line:** 165
- **Anchor:** `ClinicArchiveStore.RestoreTableAsync` (`StageInsert` branch)
- **Comment:** Presence is decided on the **primary key alone**, so a row absent by id but colliding on a unique index is staged as an insert and takes down the whole restore instead of being reported. AC-3 names this explicitly (« sans gap ni **collision** »). Exposed indexes include `Invoices(ClinicId, Number)`, `CreditNotes(ClinicId, Number)`, `TreatmentPlans(ClinicId, Number)`, `ProcedureTypes(ClinicId, Name)`, `CnamNomenclatureEntries(ClinicId, Code)`, `DentalRecordTeeth(DentalRecordId, ToothNumber)`, plus `Medications`, `DentalActCodes`, `Doctors`. The scenario is the feature's own: rows are lost, the practice keeps working, `IssueInvoiceCommand`/`DevisNumbering` re-mint the freed number off `MAX+1`, and putting the archive back then violates `(ClinicId, Number)`. Today that is an unhandled `DbUpdateException` → 500 with no report. Fix: count a colliding row as a conflict and name it in `Warnings` (« la facture 2026-0042 existe déjà sous un autre identifiant »), never insert it.

### Finding 10
- **Severity:** Major
- **Category:** Business Logic
- **File:** api/ClinicManagement.Infrastructure/Persistence/ClinicArchiveStore.cs
- **Line:** 348
- **Anchor:** `ClinicArchiveStore.RowsMatch`
- **Comment:** **[verified]** The archived row was written **with** redaction applied while the live row is read with `redacted: null`, so for any cabinet with Google Calendar connected `JsonNode.DeepEquals(null, "1//…")` is false and the `Clinic` row is counted as a **conflict** on every restore of a row nobody touched. AC-2 (« a double restore is a no-op — every row reports déjà présent ») is therefore false for every Google-connected practice, and the owner is shown « 1 conflit sur Clinic » — a disagreement they can never resolve, because the archive is structurally incapable of carrying that value. It is also exactly the "phantom conflict on a row nobody touched" this method's own doc comment claims its serialized comparison cannot produce. `A_Redacted_Column_Is_Written_As_Null_Rather_Than_Omitted` pins that the columns are present-and-null, so the mismatch is guaranteed rather than incidental. Fix: pass `ClinicArchiveScope.Redacted.GetValueOrDefault(entityType.ClrType.Name)` into `ReadRow` here so both sides normalise identically, or skip redacted names in the comparison loop.

### Finding 11
- **Severity:** Major
- **Category:** Security
- **File:** api/ClinicManagement.Infrastructure/Persistence/ClinicArchiveStore.cs
- **Line:** 391
- **Anchor:** `ClinicArchiveStore.StageInsert` (the `ClinicId` re-stamp)
- **Comment:** Cross-tenant row insertion: a `Child`-scoped table's parent FK is written verbatim from the file. The re-stamp fires only on a `Guid ClinicId` property, and twelve archived tables have none — their only clinic identity *is* the attacker-supplied FK: `Payment`/`InvoiceLine`→`InvoiceId`, `Installment`/`TreatmentPlanItem`→`TreatmentPlanId`, `InstallmentPayment`→`InstallmentId`, `AppointmentProcedure`→`AppointmentId`, `StockBatch`/`ProcedureTypeMaterial`→`StockItemId`, `MedicationActiveIngredient`→`MedicationId`, `DentalRecordTooth`/`DentalRecordAct`→`DentalRecordId`, `PatientFlag`→`PatientId`. Input: their own archive with a `data/Payment.json` row carrying a fresh `Id`, a chosen `Amount`, and `InvoiceId` set to **another practice's** invoice. The FK is valid, query filters do not apply to inserts, and the row commits. Consequence: that payment enters clinic B's « Encaissé », extrait, dashboard, the patient's `solde patient`, possibly flipping the invoice to `Paid`, and the vendor's `PlatformCollectedReader` figure — not undoable except by an avoir. `DentalRecordAct`/`DentalRecordTooth` is worse in kind: clinical content written into another practice's fiche de soins. **Major rather than Critical only because it requires knowing a v4 GUID belonging to the victim** — unguessable by brute force, but routinely available to a former employee of the victim practice who has since signed up for their own clinic. The docstring at lines 377-381 calls the clinic id "belt and braces" re-stamped, which is materially false for these twelve tables. Fix: resolve each `Child` row's parent through the plan's `ForeignKeyProperty` and refuse any row whose parent's `ClinicId` is not the target clinic (one batched query per table).

### Finding 12
- **Severity:** Major
- **Category:** Security
- **File:** api/ClinicManagement.Application/Features/Backup/Archive/ClinicArchiveRestorer.cs
- **Line:** 91
- **Anchor:** `ClinicArchiveRestorer.ApplyAsync` → `RestoreBlobsAsync` (line 124)
- **Comment:** Blob writes use attacker-chosen keys and are decoupled from whether any row was restored. `ReadStorageKeys` parses the *whole* `data/<Table>.json` and returns every `StorageKey`/`CachetStorageKey`/`LogoUrl` in it regardless of whether the owning row was inserted, skipped, or counted a conflict; `RestoreBlobsAsync` hands each straight to `RestoreAtKeyAsync`, verbatim, with no check that it belongs to the caller's clinic. Input: a `data/PatientFile.json` row whose `Id` already exists (so no row is written and the report reads innocuously) and whose `StorageKey` is `clinics/<victim guid>/anything.pdf`, plus a matching `blobs/…` entry. Consequence: on MinIO the object is created inside another tenant's prefix in the shared bucket — the US-5 invariant that "an unprefixed key is not something a caller can write" is enforced on `UploadAsync` by requiring a `Guid`, and `RestoreAtKeyAsync` is the door around it. No object-count or byte quota either, so an archive listing N keys creates N objects. `LocalDiskFileStorage.RestoreAtKeyAsync` **is** root-contained by `ResolveWithinBase` (`Path.GetFullPath` + `StartsWith(base + separator)`, so `..`, absolute paths and drive letters cannot escape) but is not clinic-contained, so cross-prefix writes work there too. Fix: restrict the blob pass to keys carried by rows this operation actually inserted, and require each key to be `ClinicStorageKey`-prefixed with the target clinic or a flat pre-US-5 key the just-restored row itself holds.

### Finding 13
- **Severity:** Major
- **Category:** Business Logic
- **File:** api/ClinicManagement.Infrastructure/Storage/MinioFileStorage.cs
- **Line:** 203
- **Anchor:** `MinioFileStorage.ExistsAsync`
- **Comment:** Flagged independently by three agents. `catch (Exception ex) when (ex is not OperationCanceledException) → return false` reads *every* failure as "the object is not there" — a network blip, an expired credential, a bucket-policy refusal, a throttle, a 5xx. `RestoreBlobsAsync` (line 144) uses that single boolean as the whole of AC-5's "existing bytes are left alone", and `RestoreAtKeyAsync` writes with `PutObjectAsync`, which overwrites unconditionally. Consequence: during any MinIO instability a restore **silently overwrites a radiograph or scanned consent the practice replaced after the archive was taken**, and counts it in `BlobsRestored` as a success — the exact rollback of recent work the additive design exists to prevent. `ex` is bound and never used, so the diagnosis is discarded too. Fix: narrow the catch to the SDK's not-found signal (`ObjectNotFoundException` / a 404 `MinioException`) and let everything else propagate so `RestoreBlobsAsync`'s own catch records the French warning.

### Finding 14
- **Severity:** Major
- **Category:** Security
- **File:** api/ClinicManagement.Application/Common/Interfaces/IAuditActorProvider.cs
- **Line:** 102
- **Anchor:** `AuditActor.AsRestore` / `AuditActor.IsRestore`
- **Comment:** **[verified]** `restore|` is a write-only marker — nothing outside `AsRestore`'s own idempotence guard reads it, so AC-9 is not achieved and the vendor's dormancy signal is destroyed. `AsRestore()` **prepends** (`new($"{RestorePrefix}{UserId}", Email)`), which breaks both existing `StartsWith` predicates. (a) `AuditLabels.Actor:70` tests only `ProcessPrefix`, and `AsRestore()` preserves `Email`, so a restored row falls through to the email branch and renders in « Journal d'activité » as the named admin's own address with no marker — verbatim the outcome `RestoringAnArchive()`'s own docstring (lines 40-41) says it prevents: "three thousand `Insert` rows against a named colleague, on a day they typed nothing". On the console path the row is `restore|console|{guid}` carrying the *vendor's* email, so the practice's journal shows an outside address as if it were a colleague. (b) `PlatformCounterPass.CountsAsCabinetActivity:63-64` excludes `job|` and `console|` by `StartsWith`, and `restore|console|{guid}` matches **neither** — so the vendor restoring a dead cabinet makes it read as the *most active* practice in the portfolio the next morning (`writes`, `activeDays`, `appointments`, `patientsCreated`, `lastWriteAt` all fold the restored `Insert` rows), poisoning `sort=activity` and the `dormant` filter. That is precisely the "responding to the signal destroys the signal" failure the `console|` exclusion was written to prevent, at far greater magnitude. `PlatformCounterPassTests:184-187` only exercises undecorated actors. Fix: add a `RestorePrefix` branch to `AuditLabels.Actor` that unwraps the decoration and labels it, and exclude `RestorePrefix` in `CountsAsCabinetActivity`.

### Finding 15
- **Severity:** Major
- **Category:** Breaking Change
- **File:** web/components/backup/clinic-archive-card.tsx
- **Line:** 81
- **Anchor:** `ClinicArchiveCard.handleRestore`
- **Comment:** **[verified]** `backupApi.restoreArchive` goes through `apiPostFormData`, whose deadline is `TRANSFER_TIMEOUT_MS = 180_000` (3 minutes) — while this component's own dialog tells the user « L'opération peut durer plusieurs minutes sur un cabinet complet. » The UI explicitly warns that the operation outlasts its own timeout. A restore that exceeds 3 minutes is aborted **client-side** while the server keeps committing table after table: the user gets `showErrorToast`'s network wording (`ApiError(0)`, indistinguishable from « serveur injoignable »), the per-entity report is lost, and they cannot tell whether anything was written. Because the restore is additive a retry is safe in principle, but it hits the same wall — and a second concurrent restore races the still-running first one across the same rows. The download has the same 3-minute ceiling and, because the archive is fully built before a single byte is sent (Finding 16), the client sees no data for the entire build. Fix: give both archive calls their own longer deadline (a dedicated constant — `TRANSFER_TIMEOUT_MS` was itself split out of `REQUEST_TIMEOUT_MS` for exactly this reason), and make the report retrievable after the fact so a timed-out restore can still be reconciled.

### Finding 16
- **Severity:** Major
- **Category:** Business Logic
- **File:** api/ClinicManagement.Application/Features/Backup/Queries/BuildClinicArchiveQuery.cs
- **Line:** 92
- **Anchor:** `BuildClinicArchiveQueryHandler.Handle`
- **Comment:** **[verified]** The whole archive is built into a `MemoryStream` and then copied **again** by `buffer.ToArray()` (line 102), so a download costs ~2× the archive in contiguous large-object-heap allocations before `File(byte[], …)` retains it for the response. The configured ceiling is 1024 MB and the handler's own comment anticipates "a cabinet with twenty years of radiographs" — i.e. the sizes that OOM the shared hosted backend and take every other cabinet's requests with them, and a `byte[]` over 2 GB throws outright. AC-8 (« a cabinet must always be able to take its data out ») fails precisely for the practices with the most to lose. The docstring's justification for buffering is sound and unaffected — `ZipArchive` in Create mode seeks back to write directory records, and a mid-stream failure must not deliver a truncated 200 — but it argues for a **temp file**, not for RAM, and nothing justifies the `ToArray()`. Fix: build into a temp `FileStream` (`FileOptions.DeleteOnClose`) or `FileBufferingWriteStream` and return a stream result; carry a `Stream` on `ClinicArchiveFile` rather than `byte[]`.

### Finding 17
- **Severity:** Major
- **Category:** Security
- **File:** api/ClinicManagement.Application/Features/Backup/Archive/ClinicArchiveRestorer.cs
- **Line:** 105
- **Anchor:** `ClinicArchiveRestorer.ApplyAsync` — `Warnings = warnings.Concat(manifest.Warnings)`
- **Comment:** Attacker-authored strings from the uploaded zip are echoed onto the vendor's console as the server's own diagnostics. `manifest.Warnings` is deserialized straight out of the uploaded `manifest.json` and concatenated into the report, which on the console path becomes `PlatformClinicRestoredDto.Warnings` and is rendered on the console screen — unbounded in count and length. Whoever supplies the archive controls it, and this door exists precisely for the case where the cabinet's own staff are gone, so provenance is weak. Consequence: uploader-controlled French prose indistinguishable from the server's own refusals, a direct operator-spoofing path aimed at the vendor's `subscription-periods` write; and because the strings are arbitrary they can contain a patient's name, which makes the new `PlatformReadShape` entry's own justification ("French prose the SERVER composes… nothing a practice typed") false and defeats AC-7.2's closed-name-set guarantee that a *type* allow-list was rejected in favour of. React escapes the output, so this is spoofing and flooding, not XSS. Fix: do not carry `manifest.Warnings` into the report — recompute server-side what this build could not restore; at minimum drop it from the console DTO and from `PlatformReadShape`.

### Finding 18
- **Severity:** Major
- **Category:** Business Logic
- **File:** api/ClinicManagement.Application/Features/Backup/Archive/ClinicArchiveRestorer.cs
- **Line:** 75
- **Anchor:** `ClinicArchiveRestorer.ApplyAsync`
- **Comment:** The restore is additive per *row* with no notion of an aggregate, so children are re-inserted under a parent whose state disagrees with them. When an `Invoice` is a conflict (skipped because its lines were edited since the archive), its archived `InvoiceLine` rows that no longer exist are still absent-by-id and are re-inserted onto the live invoice — but `Invoice.TotalHt`/`TotalVat`/`TotalTtc` are **stored denormalisations** recomputed by `RecomputeTotals()`, and nothing recomputes them here, so the note d'honoraires permanently disagrees with the sum of its own lines. Same shape for `Payment`/`InstallmentPayment` re-inserted against a surviving `Invoice.AmountCollected` (which then under-reports, and combines with Finding 1 to over-report), and for `StockMovement` against a `CurrentStock` the restore did not carry. The report reads as success — « 1 conflit sur Invoice, 3 restaurés sur InvoiceLine ». Fix: skip and name a child whose parent was counted a conflict; where children of an untouched parent *are* restored, recompute the parent's derived totals or count it as needing attention.

### Finding 19
- **Severity:** Major
- **Category:** Security
- **File:** api/ClinicManagement.Infrastructure/Persistence/ClinicArchiveStore.cs
- **Line:** 356
- **Anchor:** `ClinicArchiveStore.RowsMatch` (with `ReadExistingTypedAsync`'s `IgnoreQueryFilters()`, line 322)
- **Comment:** Cross-tenant field-value oracle. `ReadExistingTypedAsync` deliberately calls `IgnoreQueryFilters()`, so the existence probe spans every cabinet on the shared database; `RowsMatch` then iterates **only the keys present in the attacker's archived JSON** and `continue`s on names absent from the live row. That makes a single-field probe possible: a row of `{"Id":"<victim guid>","LastName":"Trabelsi"}` reports `alreadyPresent: 1` when the guess is right and `conflicts: 1` when wrong, and those counts are returned per entity to the caller. Distinct ids batch, and because counts are additive an N-row file yields N independent bits. Consequence: confirm-or-deny of arbitrary column values on arbitrary rows platform-wide — a patient's surname, phone, `CnamInfo.IdentifiantUnique`, an invoice total — with no row modified, so nothing appears in the victim's journal. Major rather than Critical: a per-guess oracle, not a bulk read, and it needs the target id first. Fix: scope the existence probe to the target clinic for every table that can be scoped (`Self`/`Direct` need no `IgnoreQueryFilters` at all — only the console path's `Clinic` lookup does), resolve the parent's clinic for `Child` tables, or return aggregate totals only.

### Finding 20
- **Severity:** Major
- **Category:** Security
- **File:** api/ClinicManagement.API/Controllers/Platform/PlatformClinicRestoreController.cs
- **Line:** 53
- **Anchor:** `PlatformClinicRestoreController.RestoreClinic`
- **Comment:** The console door has no size gate at all — it checks only `archive.Length == 0` and never reads `Backup:ArchiveMaxSizeMb`, so the sibling endpoint's cap does not apply. Both actions carry `[DisableRequestSizeLimit]`, and nothing in `Program.cs`, `appsettings*.json` or `deploy/Caddyfile` sets `MaxRequestBodySize`, `MultipartBodyLengthLimit` or `FormOptions`. The residual bound is the framework's 128 MB `MultipartBodyLengthLimit` default — which also makes `BackupController.ValidateUpload`'s carefully-reasoned French refusal **dead code for every archive between 128 MB and the configured 1024 MB**: form binding throws `InvalidDataException` before the action body runs and the caller gets a generic 500 with no limit named, which is exactly the "Kestrel's own 413 with an empty body" outcome the comment at `BackupController.cs:36-44` says it exists to avoid. `Backup:ArchiveMaxSizeMb` also appears in no config file, and `GetValue<int>` throws on an unparseable value. Fix: set `MultipartBodyLengthLimit`/`MaxRequestBodySize` from the same config value in a filter that runs before binding, apply it to the console action too, and hand-parse the value rather than letting `GetValue<int>` throw.

### Finding 21
- **Severity:** Major
- **Category:** Security
- **File:** api/ClinicManagement.Infrastructure/Persistence/ClinicArchiveScope.cs
- **Line:** 56
- **Anchor:** `ClinicArchiveScope.Excluded` / `Redacted`
- **Comment:** Inclusion is derived and exclusion is hand-maintained, so the archive is **fail-open for every secret added from now on**. `Resolve` archives every non-owned entity with a path to a clinic unless its name is in a hand-written `HashSet`, and `Redacted` names two columns on one entity. The direction is inverted relative to the safe default: the day someone adds a second credential-bearing table (an SMTP profile, a per-clinic API key, a second OAuth token store) it is archived into a **deliberately unencrypted zip the operator guidance tells the practice to keep on a laptop**, with no compile error, no failing test and no warning. The suite does not close this — `Every_Table_Is_Planned_Excluded_Or_Reported` only checks that every table is accounted for *somewhere*, `What_An_Archive_Never_Carries_Is_Absent_From_The_Plan` is a hand-written `[InlineData]` list of today's names, and nothing asserts `Redacted` is complete. This is the derived-vs-listed lesson `TenantScopeFilterTests`, `RealtimeResourceResolverTests`, `PlatformReadShapeTests` and `verify-schema` already embody, applied in only one direction here. Fix: add a derived guard reflecting over every property of every *planned* entity whose name matches `Token|Secret|Key|Hash|Password|Credential|ApiKey|Encrypted|Refresh`, failing unless the entity is `Excluded` or the property `Redacted`, asserted in both directions so a stale allowance also fails.

### Finding 22
- **Severity:** Major
- **Category:** Security
- **File:** api/ClinicManagement.Application/Features/Backup/Archive/ClinicArchiveRestorer.cs
- **Line:** 48
- **Anchor:** `ClinicArchiveRestorer.ApplyAsync` — `auditActor.RestoringAnArchive()`
- **Comment:** Restored **child** rows produce no audit row at all, so the most sensitive half of a restore is unlogged. `AuditSaveChangesInterceptor.IsAuditable` walks the base chain for `AggregateRoot<>`; `Payment`, `InvoiceLine`, `Installment`, `InstallmentPayment`, `TreatmentPlanItem` and the other child types derive from `Entity<Guid>`. For an ordinary edit that is correct (one row per action, not eleven), but a restore inserts children *independently* of their parents — a parent still present is `alreadyPresent` and untouched while its missing children are staged. Consequence: a restore re-inserting four thousand `Payment` rows into invoices that still exist writes **zero** ledger rows — money reappears in la caisse, the extrait, the dashboard and every patient balance with nothing in « Journal d'activité », and `GET /api/audit` cannot answer "where did this payment come from?". This is also what makes Finding 11's cross-tenant insertion traceless: the injected row appears in neither practice's journal. Declaring the actor a restore is necessary but not sufficient — there is no row for the prefix to travel on. Fix: emit a summary `AuditEntry` per table from the restorer (actor, target clinic, entity, restored/skipped/conflicting counts, the manifest's `CreatedAtUtc`), independent of the interceptor's aggregate-root rule.

### Finding 23
- **Severity:** Major
- **Category:** Device & UX
- **File:** web/components/backup/clinic-archive-card.tsx
- **Line:** 233
- **Anchor:** `RestoreReportPanel`
- **Comment:** **[verified]** The row labels are raw English CLR type names, at every width. `ClinicArchiveScope` keys the plan on `ClrType.Name`, the manifest carries it as `ClinicArchiveTableCount.Entity`, the report's three dictionaries are keyed on it, and the panel prints `{entity}` verbatim — so a French cabinet owner reads « PatientMedicalHistory · 12 remis », « InstallmentPayment · 3 ignorés », « ToothState », « WaitingListEntry ». That breaks the frontend rule's « no English string reaches a user » on the screen read "at the moment an owner is most anxious", and defeats the report's stated purpose (« 3 conflits sur Patient » is only actionable if the reader recognises the noun). It also drives the sort: `localeCompare(a, b, "fr")` over English identifiers orders by nothing the reader can predict. Same defect at 1440 px as at 320 px. `PlatformRestoredTableDto.Entity` has it on the console side too. Fix: the repo's standing convention (English wire key + French display map, as `appointment-labels.ts` / `invoice-labels.ts` / `AuditLabels` / `SubscriptionLabels` do) — map server-side beside `AuditLabels`, which already holds French names for most of these types, return the label alongside the key, pass unknown keys through unchanged, and sort on the label.

### Finding 24
- **Severity:** Major
- **Category:** Device & UX
- **File:** web/components/backup/clinic-archive-card.tsx
- **Line:** 232
- **Anchor:** `RestoreReportPanel`
- **Comment:** At 320 px the per-entity row collapses the entity name to an ellipsis. Measured nesting on `/settings`: 320 − `AppShell p-4` (32) = 288 → `Card` border + `CardContent px-6` (50) = 238 → the archive box's `border p-3` (26) = 212 → the panel's `border p-3` (26) = 186 → `<ul>` border (2) = 184 → `<li> p-2` (16) = **168 px of row**. The name span is `flex-1` (`flex: 1 1 0%`), so its flex base size is **0** and it contributes nothing to line-breaking: the three count spans lay out first at content width, the third wraps to line 2, and the name grows into the ~12 px left on line 1 — `truncate` then renders just « … ». The ordinary two-count case leaves ~30 px (« Pat… »); at 390 px it is ~84 px. There is no `title` and no tap-to-reveal, so the identity half of « 3 conflits sur *Patient* » is unreachable by any means on the width AC-10 is written about. Fix: give the name its own line (`w-full` + `[overflow-wrap:anywhere]`, counts wrapping beneath) or drop `flex-1 truncate` and let the heading wrap the way `ui/card-list.tsx` deliberately does.

### Finding 25
- **Severity:** Major
- **Category:** Business Logic
- **File:** api/ClinicManagement.UnitTests/Features/Backup/ClinicArchiveEndpointTests.cs
- **Line:** 84
- **Anchor:** `ClinicArchiveEndpointTests.An_Admin_Downloads_An_Archive_Of_Their_Own_Cabinet`
- **Comment:** AC-1's prescribed test does not exist. The AC states the guarantee is « asserted by a test that builds an archive with two cabinets seeded and finds no foreign id in it »; every test here runs against `FakeArchiveStore`, which returns whatever was seeded and merely records `export:{clinicId}` — so the clinic predicate in `ClinicArchiveStore.ReadRowsTypedAsync`, the only code that can leak another cabinet's rows, is exercised by nothing. `ClinicArchiveScopeTests` covers the *plan*, not the queries. `progress.md` nonetheless marks AC-1 covered by these two classes, while its own "Deferred" section lists the two-cabinet test as item 1 and its coverage note admits "what no test here can prove is the SQL of `ReadRowsTypedAsync`/`ReadExistingTypedAsync`". Same gap for AC-3's second half (original invoice/devis/avoir numbers, and the next number continuing). Fix: add a test over a real relational provider for the two predicates, or stop claiming AC-1 and AC-3 are covered and record them as owed operator checks in the ACs' own rows.

### Finding 26
- **Severity:** Major
- **Category:** Code Quality
- **File:** api/ClinicManagement.Application/Features/Platform/Commands/RestoreClinicFromArchiveCommand.cs
- **Line:** 192
- **Anchor:** `RestoreClinicFromArchiveCommandHandler.Handle`
- **Comment:** The FR-5 access-ledger row is staged **after** `ApplyAsync` has committed the cabinet's rows, so it rides a separate, later transaction. Parts 4/5's established shape — quoted in this file's own class remarks — is "staging the ledger row before the single save is the only shape in which AC-4.7 and AC-7.3 are true of the same instant", and Part 3 settled that "an unattributable action must not aboutir". Here a failure on the final `SaveChangesAsync` leaves a fully restored cabinet with **no journal row naming the vendor account that restored it** — on the one console action that writes a practice's clinical records, at the moment nobody at the practice can observe it, which `PlatformAccessAction.RestoredClinic`'s own docstring calls "the heaviest row in that ledger". `PlatformClinicRestoreTests.The_Restore_Is_Recorded_In_The_Consoles_Own_Journal` asserts a guarantee the code does not hold. Overlaps Finding 5; the remedy is the same transaction. Fix: wrap the whole operation in an explicit `IUnitOfWork` transaction, or state in the class remarks that the ledger row is not atomic with the rows and test the failure ordering.

### Finding 27
- **Severity:** Minor
- **Category:** Code Quality
- **File:** api/ClinicManagement.Infrastructure/Persistence/ClinicArchiveStore.cs
- **Line:** 83
- **Anchor:** `ClinicArchiveStore.ForgetRestoredRows`
- **Comment:** `ChangeTracker.Clear()` is the exact thing this codebase's single authority for the same job rejects: `UnitOfWork.StopTracking` sets `Detached` on one entry, and its inline comment says why — "the import's loop must release the row it has just committed without releasing anything else the request is holding — the caller's own `User`". Both handlers do hold other tracked entities across the call, and `IClinicArchiveStore`'s doc claims this "drops the rows already committed", which is not what it does. It loses nothing **today** only because neither handler stages a write before `ApplyAsync` — but the console handler sits one refactor away from staging the admin or the ledger row earlier (which Findings 5 and 26 both recommend), at which point the insert would be discarded in silence with a reported success. Fix: have `StageInsert` record the entries it creates and detach exactly those (or route through `IUnitOfWork.StopTracking`, which the interface doc already cites as the precedent), and correct the doc comment.

### Finding 28
- **Severity:** Minor
- **Category:** Business Logic
- **File:** api/ClinicManagement.Application/Features/Platform/Commands/RestoreClinicFromArchiveCommand.cs
- **Line:** 142
- **Anchor:** `RestoreClinicFromArchiveCommandHandler.Handle` (live-cabinet guard)
- **Comment:** Both this `clinic_exists` guard and the cabinet path's AC-6 check (`RestoreClinicArchiveCommand.cs:115`) key on `manifest.ClinicId` — the archive's own *claim* — while the inserted `Clinic` row's identity comes from `data/Clinic.json`'s `Id`, which nothing validates against it. The archive is an unencrypted zip the practice holds, so it is untrusted input by the time it comes back; a hand-edited manifest therefore drives the guard on one id and the row on another. On the console path that produces Finding 5's unrecoverable state (`Clinic` inserted at id Y, every child re-stamped to X, `GetByIdAsync(X)` null, refusal after the commit). Fix: validate that the archive's single `Clinic` row's `Id` equals `manifest.ClinicId` before anything is staged, refusing `archive_invalid` otherwise.

### Finding 29
- **Severity:** Minor
- **Category:** Security
- **File:** api/ClinicManagement.Infrastructure/Persistence/ClinicArchiveScope.cs
- **Line:** 91
- **Anchor:** `ClinicArchiveScope.Redacted`
- **Comment:** `Clinic.Code` — the clinic join credential — is archived un-redacted into an unencrypted file. `Redacted[nameof(Clinic)]` covers `GoogleRefreshToken` and `GoogleCalendarId` but not `Code`, the six-character secret `POST /api/auth/register` accepts to attach a new account to a practice. On `SelfHostedLan` that door is open (`allowsSelfRegistration: true`), and that is exactly the profile whose archive is a portable file carried between machines. Anyone who obtains a copy — which the screen and the operator guide say plainly will be sitting unencrypted on the owner's PC or a USB stick — gets the join code for a live practice. **Minor** because self-registration now creates the account *pending* an admin's activation, so the code alone is not a working credential — but it is still the one column on `Clinic` that is a secret rather than a record, which is the stated criterion `GoogleRefreshToken` is redacted under. Fix: add `nameof(Clinic.Code)` to `Redacted[nameof(Clinic)]`.

### Finding 30
- **Severity:** Minor
- **Category:** Code Quality
- **File:** web/lib/api/backup.ts
- **Line:** 132
- **Anchor:** `backupApi.downloadArchive`
- **Comment:** **[verified]** The server computes the archive's name through `ClinicArchiveFormat.FileName(clinic.Name, ClinicClock.ClinicToday())` and sets it on the response, and this call throws it away: `apiGetBlob` returns only the body, so `clinic-archive-card.tsx:52` invents `archive-cabinet-${todayLocalIso()}.zip` — the cabinet slug the server's helper exists to produce reaches nobody, and **every cabinet's archive lands under the same name**. That defeats the exact scenario both restore handlers cite as the reason the clinic-id check exists ("a practice with two installations, or an owner helping a colleague, has two files in one Downloads folder whose names differ by a date"): the two files now differ only by the browser's `(1)` suffix. `client.ts:778` already has `apiGetFile` — "for a download whose filename the server dictates" — with `filenameFromDisposition` as the single parser, and `lib/api/export.ts` uses it for nine CSV exports against the same origin, so the doc comment's justification (« `Content-Disposition` is not readable from a cross-origin response ») is contradicted inside the same module tree. That comment also states the opposite of what the function does — it claims to return "the `Blob` **and** the server's own file name". Fix: use `apiGetFile('/backup/archive')`, pass the returned filename to `downloadBlob`, keep the local name as a fallback, and correct the comment.

### Finding 31
- **Severity:** Minor
- **Category:** Breaking Change
- **File:** api/ClinicManagement.Application/Features/Platform/PlatformAccessLabels.cs
- **Line:** 28
- **Anchor:** `PlatformAccessLabels.Action`
- **Comment:** `PlatformAccessAction.RestoredClinic = 5` is added with no case in this map, so the `_ => action.ToString()` fallback renders the raw CLR name. `console/components/access-log-list.tsx` displays `entry.actionLabel` verbatim in both the table and the card list, so the vendor's « Journal » shows the English **« RestoredClinic »** beside « Paiement enregistré », « Période annulée », « Cabinet suspendu » — for what the enum's own doc comment calls "the heaviest row in this ledger". The fallback is documented as intentional for a member added in a *later* part; this part is the one that adds the member. Fix: add `PlatformAccessAction.RestoredClinic => "Cabinet restauré"`.

### Finding 32
- **Severity:** Minor
- **Category:** Code Quality
- **File:** api/ClinicManagement.Application/Features/Backup/Archive/ClinicArchiveRestorer.cs
- **Line:** 172
- **Anchor:** `ClinicArchiveRestorer.ContentTypeFor`
- **Comment:** A private four-case extension→content-type switch beside the codebase's single authority on exactly that mapping: `Application/Common/Files/FileTypeCatalog`, whose entries carry `ContentType`. This is the `fixes-dont-propagate` shape — the catalog admits `.stl`, `.dcm`, `.ply`, `.obj` and more, all of which a restore now relabels `application/octet-stream`, and a widened catalog will not reach here. Fix: `FileTypeCatalog.TryGet(Path.GetExtension(storageKey).ToLowerInvariant())?.ContentType ?? "application/octet-stream"`, keeping the generic fallback for a genuinely unknown extension.

### Finding 33
- **Severity:** Minor
- **Category:** Code Quality
- **File:** api/ClinicManagement.Application/Common/Services/ProcessAuditActorProvider.cs
- **Line:** 46
- **Anchor:** `ProcessAuditActorProvider.RestoringAnArchive`
- **Comment:** The decoration is stored *in* `_actor`, so a later `RunAs(name)` (permitted whenever `Current` has not been read) overwrites it with a bare `AuditActor.Process(name)` and the restore mark is silently lost. `AuditActorProvider` does not have this hole — it keeps a separate `_restoring` flag and re-applies it in `Resolve()`. Two implementations of one interface differing on whether a declared restore survives is the divergence shape this repo names as its dominant defect. Fix: mirror the sibling — hold `private bool _restoring` and return `_restoring ? _actor.AsRestore() : _actor` from `Current`, leaving `RunAs` to set the identity only.

### Finding 34
- **Severity:** Minor
- **Category:** Device & UX
- **File:** web/components/backup/clinic-archive-card.tsx
- **Line:** 220
- **Anchor:** `RestoreReportPanel`
- **Comment:** Two problems about the result being *read*. (a) The `role="status"` container is mounted at the same moment its content arrives (`{report && <RestoreReportPanel …>}`), and inserting a live region with text already in it is announced unreliably — VoiceOver on iOS often says nothing, so for a multi-minute operation the user has looked away from, the outcome is announced by the toast alone and the warnings and conflicts never at all. Keep the region mounted so the insertion is a *change* inside it. (b) The panel is `border-success/25 bg-success-wash` with « Archive du … restaurée » regardless of outcome, so a restore that skipped 3 conflicting rows and carries 4 « ne fait pas partie des données que cette version sait restaurer » warnings paints green, with the amber lines nested *inside* the green box at 11 px — on a coarse pointer, scanning colour reads that as unqualified success, conflating « tout est revenu » with « il en manque ». Fix: tone the panel and its heading on `totalConflicts > 0 || warnings.length > 0` (« Restaurée avec des réserves »), leaving success for the clean case.

### Finding 35
- **Severity:** Minor
- **Category:** Device & UX
- **File:** web/components/backup/clinic-archive-card.tsx
- **Line:** 104
- **Anchor:** `ClinicArchiveCard`
- **Comment:** `text-2xs` (11 px, the hard floor) carries the card's entire body — the explanatory paragraph and the « Le fichier n'est pas chiffré » warning that the file's own docstring calls "the whole mitigation". In the same `CardContent` the neighbouring host-managed statement box (`backup-settings.tsx:237`) is `text-sm`, so within one card the type size is inverted against the stakes: the operational note is comfortable and the irreversible-privacy warning is at the smallest size the contract permits. At 200 % zoom on a phone, 11 px reflows to ~5–6 words a line for a four-line paragraph in a 284 px box — where an owner is deciding where an unencrypted copy of every patient record lands. `text-2xs` is dimensioned for badges and `<dt>` labels, not prose. Fix: `text-xs` for the description, `text-sm` for the unencrypted warning.

### Finding 36
- **Severity:** Minor
- **Category:** Device & UX
- **File:** web/components/backup/clinic-archive-card.tsx
- **Line:** 184
- **Anchor:** `ClinicArchiveCard`
- **Comment:** Both footer buttons carry `w-full … sm:w-auto` while `DialogFooter` already owns that decision at the `md:` hinge (`[&>*]:w-full md:[&>*]:w-auto`). At 640–767 px — landscape iPhone SE/8, an iPad in the wider Split View column — the footer is still a `flex-col-reverse` column while the caller's `sm:w-auto` asks the children to shrink to content, so the two buttons become ~110 px stacked at the inline-start edge instead of the full-width pair the primitive draws. Which wins is not determinable by inspection: `[&>*]:w-full` on the parent and `sm:w-auto` on the child are equal-specificity single-class rules in different variants, and `dialog.tsx:75-79`'s own docstring names that ambiguity as what AC-20 exists to remove ("Everything keys on `md:`, deliberately — not `sm:`"). Fix: delete `w-full` and `sm:w-auto` from both footer buttons, keeping only `coarse:h-11`. (The `md:max-w-lg` on `DialogContent` itself is correct and needs no change.)

### Finding 37
- **Severity:** Minor
- **Category:** Device & UX
- **File:** web/components/backup/clinic-archive-card.tsx
- **Line:** 163
- **Anchor:** `ClinicArchiveCard`
- **Comment:** `Restaurer « {pending?.name} » ?` puts an untrusted, arbitrarily long file name in `DialogTitle`, which is `text-lg font-semibold` with no `overflow-wrap`/`break-words`. The server's own name breaks at its hyphens, but a user-renamed file with no break opportunity — `ArchiveCabinetDentaireDrBenSalah20260811.zip` is an ordinary thing to type on Windows — is one unbreakable token ~330 px wide at 18 px inside a 288 px bottom sheet whose only overflow rule is `overflow-y-auto`. At 320 px the title overflows horizontally with no scroll container, taking the close button's `pe-8` gutter with it. Fix: `[overflow-wrap:anywhere]` on the title, or keep the title generic (« Restaurer cette archive ? ») and render the file name as a wrapping line in the description.

### Finding 38
- **Severity:** Minor
- **Category:** Code Quality
- **File:** web/lib/api/backup.ts
- **Line:** 113
- **Anchor:** `ARCHIVE_ERROR_CODES`
- **Comment:** **[verified]** The export has **zero consumers** — `clinic-archive-card.tsx` never imports it, and it appears nowhere else in `web/`. Its own doc comment instructs the reader to "branch on these, never on the message", but nothing branches on anything: the card's two `catch` blocks call `showErrorToast(err, …)` with a generic fallback, so `archive_clinic_mismatch` and `archive_schema_unsupported` are presented identically to a network failure. This is the pattern the repo documented for `isPaymentRequiredError` ("zero consumers for the whole life of the feature, and `web/` has no working ESLint to notice an unused export"). The backend codes themselves were verified to match byte-for-byte, so this is about the client never using them. Fix: either branch on the codes where it changes what the user is told and what the dialog does (a schema mismatch is not retryable with the same file; a clinic mismatch means the wrong file was picked), or delete the constant and add it back with its first real caller.

### Finding 39
- **Severity:** Minor
- **Category:** Code Quality
- **File:** api/ClinicManagement.Infrastructure/Persistence/ClinicArchiveStore.cs
- **Line:** 85
- **Anchor:** `ClinicArchiveStore.ReadStorageKeys`
- **Comment:** Two implementations of "which storage keys do this table's rows name" live in the same class — this one over `List<JsonObject>` and `CollectStorageKeys` (line 279) over `List<Dictionary<string, object?>>`, each with its own null/whitespace handling and only one de-duplicating. It also re-deserializes JSON the caller already holds, once per table per restore. Fix: keep one reader — extract the shared rule (`BlobProperties` lookup + non-blank + `Distinct(StringComparer.Ordinal)`) into one private helper both entry points call — so a change to what counts as a key cannot land on one side only.

### Finding 40
- **Severity:** Minor
- **Category:** Breaking Change
- **File:** api/ClinicManagement.UnitTests/Infrastructure/Storage/ClinicStorageKeyTests.cs
- **Line:** 47
- **Anchor:** `ClinicStorageKeyTests.Restoring_A_Blob_At_Its_Own_Key_Is_Not_An_Upload`
- **Comment:** `Assert.NotEqual(nameof(IFileStorage.UploadAsync), restore.Name)` compares two compile-time constants that can never be equal — a tautology that can never fail, so it adds nothing to the guard it claims to protect. The property worth pinning is the derived one, in the direction a regression arrives: reflect over `IFileStorage` for methods taking a `storageKey`-named parameter and assert none is named `UploadAsync`. The other two assertions (no `Guid`, has `storageKey`) are meaningful and should stay, and the pre-existing `Every_Upload_Overload_Requires_A_Clinic_Id` guard is genuinely unweakened by the two new members.

### Finding 41
- **Severity:** Minor
- **Category:** Security
- **File:** api/ClinicManagement.Infrastructure/Persistence/ClinicArchiveScope.cs
- **Line:** 122
- **Anchor:** `ClinicArchiveScope.Resolve` — `.GroupBy(e => e.ClrType.Name).Select(g => g.First())`
- **Comment:** The archive is keyed on the CLR *simple* name throughout — `Excluded.Contains`, the `GroupBy`, `CanRestore(string table)`, `RestoreTableAsync`'s lookup, `Redacted` and `BlobProperties` all match on the unqualified name. There is no collision in today's model, so this is latent, but the failure modes are the ones this feature cannot tolerate: two entities sharing a simple name in different namespaces would see one **silently dropped** by `Select(g => g.First())` (a restore that quietly puts back less than the practice had, invisible to the derived accounting test because the name *is* accounted for), a single `Excluded` entry would exclude both, and a manifest entry could route rows into the wrong entity type. Fix: key on `IEntityType.Name` (fully qualified) or the mapped table name, and assert in `ClinicArchiveScopeTests` that all candidate simple names are distinct so a future collision is a red test rather than a smaller archive.

### Finding 42
- **Severity:** Minor
- **Category:** Code Quality
- **File:** api/ClinicManagement.Infrastructure/Persistence/ClinicArchiveScope.cs
- **Line:** 69
- **Anchor:** `ClinicArchiveScope.Excluded`
- **Comment:** `UserDashboardPreference` is the one entry with no reason anywhere in the doc comment above, which opens "Every entry here is a decision; nothing is excluded merely because it was awkward" and then groups every other entry under a stated heading. It is also not covered by the `What_An_Archive_Never_Carries_Is_Absent_From_The_Plan` theory. Fix: add it to a stated paragraph ("personal interface state, not clinic work") and to the theory's `InlineData` set, so the file's own invariant stays true.

### Finding 43
- **Severity:** Suggestion
- **Category:** Code Quality
- **File:** api/ClinicManagement.Infrastructure/Persistence/ClinicArchiveStore.cs
- **Line:** 49
- **Anchor:** `ClinicArchiveStore` constructor
- **Comment:** `ClinicArchiveScope.Resolve(context.Model)` runs on every construction of this scoped service, and it is not cheap: a full `GetEntityTypes()` walk plus a fixpoint loop re-scanning `pending` and every FK per pass. `IModel` is immutable and effectively a singleton per `DbContextOptions`, so the plan can be memoised (`ConditionalWeakTable<IModel, ClinicArchivePlan>`), which also makes it a single observable object rather than one per request. Same for the reflection in `ReadRowsAsync` (line 178) and `ReadExistingAsync`: hoist the `GetMethod(...)` results into `static readonly MethodInfo` and cache closed generics in a `ConcurrentDictionary<Type, MethodInfo>`. The reflection *approach* is right — EF exposes no non-generic `Set(Type)`, and the shared-type route only works for shared-CLR-type entity types, which `ClinicArchiveScope` excludes.

### Finding 44
- **Severity:** Suggestion
- **Category:** Device & UX
- **File:** web/components/backup/clinic-archive-card.tsx
- **Line:** 111
- **Anchor:** `ClinicArchiveCard`
- **Comment:** Two primitives are hand-rolled, and the second would fix Finding 24. (a) The notice-box shape (`flex items-start gap-2 rounded-lg border … p-2.5` + a `mt-0.5 size-4 shrink-0` glyph + a paragraph) is now written **three times inside one rendered card** — the warning wash here, the neutral box in the dialog, and `backup-settings.tsx`'s own `managedByHost` box — at three different type sizes. `ui/form-error-banner.tsx` is the nearest primitive but is destructive-only and `aria-live`, so it genuinely does not cover a static warning: the right move is a small `ui/notice.tsx` (tone + icon + children) all three call, rather than a fourth copy the next feature makes. (b) The per-entity `<ul>` reimplements `ui/card-list.tsx`, which already renders a semantic list whose heading **wraps instead of truncating** (its docstring says so explicitly) and whose `fields` are `<dt>`/`<dd>` pairs — so a screen reader pairs « déjà présents » with its number instead of reading « Patient 12 30 3 », and the 168 px collapse cannot happen. Passing `items`/`title`/`fields`/`ariaLabel` satisfies AC-10's "a list, never a table" with the primitive that owns that rule.

### Finding 45
- **Severity:** Suggestion
- **Category:** Code Quality
- **File:** api/ClinicManagement.API/Controllers/Platform/PlatformClinicRestoreController.cs
- **Line:** 86
- **Anchor:** `PlatformClinicRestoreController.RestoreClinic`
- **Comment:** `Application.Features.Backup.Archive.ClinicArchiveFormat.ClinicExistsCode` is written fully qualified inline where every other controller imports the namespace; add the `using`. Related, line 58: `IFormFile archive` carries no `[FromForm]` while the two parameters beside it do — it binds correctly under the `[ApiController]` convention, but the asymmetry reads as an oversight, and `BackupController.RestoreArchive` has the same inconsistency. Make the three explicit.

---

## Cross-cutting notes

- **Findings 1 and 2 share one root cause** — the single predicate at `ClinicArchiveStore.cs:475`. Fixing that one line is the highest-leverage change in the review, and it must be paired with the two derived guards named there, because both symptoms are invisible to the current suite.
- **Findings 3, 4, 5, 9, 18 and 26 all point at the same structural gap:** the restore has no transaction and no aggregate awareness. A single `IUnitOfWork.BeginTransactionAsync` spanning the apply would dissolve Finding 3 outright (one `SaveChanges` lets EF sort the inserts), make Finding 4's partial commit impossible, and make Findings 5 and 26 atomic. That is one change to evaluate, not six.
- **The eye pass is still owed.** `progress.md` records the manual walk at 320/390/820/1180/1440 px as not performed (no browser in that environment). Findings 8, 24 and 36 are all things only that walk would have surfaced.
- **A real-database test is still owed.** Findings 1, 2, 3, 9 and 25 are each invisible to a suite in which nothing touches a database, and `ClinicArchiveScopeTests` plus the store fakes cannot reach any of them. This is the class of change `verify-schema` exists for on the schema side; the restore has no equivalent gate.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 8 |
| Major | 18 |
| Minor | 16 |
| Suggestion | 3 |
| **Total** | 45 |
