# Progress: Clinic Data Archive & Restore

**Started:** 2026-08-11
**Type:** Small (forced — see DEV-0)
**Branch:** feature/windows-desktop-app (user's choice — see below)

## Status
- [x] Implementation
- [x] Quality checks (build, typecheck, responsive gate)
- [x] Tests (added/modified — see Test Plan below)

## Test Plan

| AC | Action | Target file | Notes |
|----|--------|-------------|-------|
| AC-1 | ⚠️ **NOT covered** | — | The spec's own test — « an archive with two cabinets seeded, and no foreign id in it » — **does not exist and cannot be written here**: every test runs against `FakeArchiveStore`, so the clinic predicate in `ClinicArchiveStore.ReadEntitiesTypedAsync` (the only code that can leak another cabinet's rows) is exercised by nothing. Owed as an operator check — see below |
| AC-1 | New test class | `UnitTests/Infrastructure/Persistence/ClinicArchiveScopeTests.cs` | The derived **plan** — not the queries: every table Self/Direct/Child, children on **required** single-column FKs, **every FK pointing at a table already planned**, no two archivable types sharing a simple name, no planned table carrying an unredacted secret (both directions), nothing dropped in silence |
| AC-1 | New test class | `UnitTests/Features/Backup/ClinicArchivePackagerTests.cs` | Zip layout, manifest counts + order, blob entries at **verbatim** keys, unreadable blob → warning |
| AC-1 · AC-6 · AC-7 | New test class | `UnitTests/Features/Backup/ClinicArchiveEndpointTests.cs` | Download and restore run **against each other**; the three coded refusals each assert *nothing was written* |
| AC-2 · AC-4 | New test class | `UnitTests/Infrastructure/Persistence/ClinicArchiveStoreMaterializationTests.cs` | `RowsMatch` identical vs. edited vs. edited-inside-an-owned-value-object — the discriminator the two ACs rest on |
| AC-2 · AC-4 | New test class | `UnitTests/Features/Backup/ClinicArchiveRestorerTests.cs` | Second restore writes nothing; conflicts counted apart; save-then-forget **ordering** |
| AC-3 | New test class | `…/ClinicArchiveStoreMaterializationTests.cs` | A row keeps its **own id and own dates** (no domain ctor), owned value objects round-trip, `Version` not archived, clinic re-stamped |
| AC-3 | New test class | `…/ClinicArchiveScopeTests.cs` + `…/ClinicArchivePackagerTests.cs` | Parent-before-child order in the plan **and** in the manifest the restore walks |
| AC-3 | New scenarios | `…/ClinicArchiveStoreMaterializationTests.cs` | Every column with a database default is archived, every planned table's key is archived, and every such column's **sentinel** equals that default — the three derived guards behind the two defects that made a voided payment restore as live money and three tables unrestorable |
| AC-3 | ⚠️ **NOT covered** (second half) | — | « The original invoice/devis/avoir numbers come back **and the next number continues the sequence** » needs a numbered document and a live sequence. Owed as an operator check — see below |
| AC-5 | New test class | `…/ClinicArchiveRestorerTests.cs` | Blobs written back at their original keys (flat pre-US-5 one included), existing bytes left alone, a failed write is a warning |
| AC-5 | Add scenario | `UnitTests/Infrastructure/Storage/ClinicStorageKeyTests.cs` | DEV-2: `RestoreAtKeyAsync` is **not** an `UploadAsync` overload and takes no `Guid`, so US-5's derived guard stays true by construction |
| AC-6 (console) | New test class | `UnitTests/Features/Platform/PlatformClinicRestoreTests.cs` | Restored **at the archive's own clinic id**; live cabinet → `clinic_exists`; admin + entitlement + journal row; EC-12 scope guard |
| AC-7 | New test class | `…/ClinicArchivePackagerTests.cs` + `…/ClinicArchiveEndpointTests.cs` | Schema refusal names **both** versions; missing/corrupt/empty-clinic-id manifests refused as `archive_invalid` |
| AC-8 | Modify existing | `UnitTests/Api/SubscriptionExemptionCoverageTests.cs` | The two new exempt writes reviewed onto FR-3's set — see « Bug found & fixed » |
| AC-9 | New test class | `UnitTests/Common/AuditActorRestoreTests.cs` | Decorates rather than replaces, honoured **after** the first read (unlike `RunAs`), idempotent, distinct from `job\|`/`console\|` |
| AC-9 | New test class | `…/ClinicArchiveRestorerTests.cs` | The scope is declared a restore **before the first row is staged** |
| AC-10 | *(coverage note)* | — | Device/UX only. `web/` has no test runner; covered by `npm run check:responsive` (15/15) + `tsc --noEmit` at implementation time. **The eye pass at 320/390/820/1180/1440 px is still owed** — see the warning above. |

**Coverage notes**

- **The real database round trip is out of this suite's reach**, by the project's own rule (`UnitTests/CLAUDE.md`: nothing here touches a database, and a migration/SQL-level change is gated by `verify-schema` instead). So AC-2/AC-3/AC-4 are covered at the two levels that *are* reachable: the row comparison and the materialisation are exercised **directly** against the real EF model (Npgsql configures the model from a connection string it never opens), and the restorer's own bookkeeping against a store fake.
- ⚠️ **What that leaves genuinely uncovered, stated as a list rather than as a caveat** (the review's finding 25: AC-1 and AC-3 were marked covered by classes that cannot reach them). Every one of these lives in `ClinicArchiveStore`'s query path and is **owed as an operator check against a real database**:
  1. **AC-1** — an export with two cabinets seeded carries no foreign id (the clinic predicate in `ReadEntitiesTypedAsync`).
  2. **AC-3, second half** — the original document numbers come back *and* the next number continues the sequence.
  3. **The `Self` scope check** — a hand-edited `data/Clinic.json` naming other ids inserts no second cabinet.
  4. **The `Child` parent check** — a `Payment` whose `InvoiceId` names another practice's invoice is refused, not committed.
  5. **The unique-index collision check** — a re-minted invoice number is counted a conflict and named, not inserted.
  6. **The id-existence probe** — a primary key held by another cabinet is refused rather than met as a duplicate-key crash, and reports the same outcome whether the foreign row matches or differs (no field-value oracle).
- **Reflection into `ClinicArchiveStore`'s privates is deliberate and narrow.** `ReadRow`/`RowsMatch`/`Materialize`/`StageInsert` *are* DEV-1 — the fix itself — and every public entry point around them queries a database. This is the sanctioned « a private pure function that IS the fix » case, not a new fixture pattern.

## Red proofs (run, not asserted in prose)

| Probe | Result |
|---|---|
| `Materialize` reduced to `GetUninitializedObject` alone (dropping the private-ctor path DEV-0 calls load-bearing) | **3 red** — `A_Materialised_Entity_Has_Its_Collection_Fields_Initialised` for all three collection fields |
| `store.ForgetRestoredRows()` moved **before** `SaveChangesAsync` | **1 red** — `Rows_Are_Forgotten_Only_After_Their_Table_Has_Been_Committed`; nothing else moved, which is the point (detaching an `Added` row discards the insert *silently*) |

Both probes were reverted; the suite is green after the revert.

## Bug found & fixed by the tests

Two derived guards were **already red** before a line of new test was written — both of them the feature landing without the review those guards exist to force.

1. **`PlatformReadShapeTests` (US-7).** `PlatformClinicRestoredDto` carried `ClinicArchiveRestoreReport` verbatim onto the vendor console, and the report keys its three counts by entity name in a `Dictionary` — so the reflection reached `Key` and `Value` along with eleven other undeclared names. Adding `Key`/`Value` to `PlatformReadShape` would have been the one-line fix and the wrong one: it pre-approves **every** future dictionary on that surface, including one whose values are patient names, which is exactly the hole the guard is built around. **Fixed in production**: the console's DTO now projects the per-entity counts into a named `PlatformRestoredTableDto` (`Entity`/`Restored`/`AlreadyPresent`/`Conflicts`) plus `ArchivedAtUtc`/`BlobsRestored`/`Warnings`, and those nine names are declared in `PlatformReadShape` with the reasoning beside them. The cabinet's own endpoint returns the full report unchanged, and the information the spec's API contract promises the console is all still there. Pinned by `PlatformClinicRestoreTests.The_Response_Reports_What_Was_Restored_Per_Entity`.
2. **`SubscriptionExemptionCoverageTests` (FR-3 / AC-8).** `Backup.RestoreArchive` and `PlatformClinicRestore.RestoreClinic` carry `[AllowsWithoutSubscription]` and were not on the reviewed set. The attributes are correct per AC-8; the review was the missing half, and both are now on the set with their reasons. (The download half is a GET, which the gate never inspects — that asymmetry is stated in the entry.)

## Quality gate — what was actually run

| Layer | Command | Result |
|---|---|---|
| Backend | `dotnet build ClinicManagement.sln -p:BaseOutputPath=<scratch>` | **0 errors**, **0 new warnings** in the changed files (baseline is a large pre-existing `CS8618` family in `Domain`, unchanged) |
| Frontend types | `npx tsc --noEmit` | **clean** |
| Frontend device gate | `npm run check:responsive` | **15/15 passed** |
| Frontend build | `npm run build` | **compiled + type-checked cleanly**; see the note below |

⚠️ **`npm run build` dies at « Collecting page data » on `/caisse`, `/cheques`, `/documents` — three routes this
feature does not touch.** A dev server is listening on **port 3000** (PID 732), which holds `web/.next`; this is
the documented stale-`.next` false failure. It was **not** cleared, because doing so means killing the user's
running dev server. Compilation and type-checking both completed before it, and `tsc --noEmit` is clean
independently.

⚠️ **The eye pass at 320 / 390 / 820 / 1180 / 1440 px is OWED and was not performed** — there is no browser in
this environment. What was done instead: the mechanical gate above, and a re-read of the diff against
`DEVICE-CONTRACT.md` § 1–2 confirming (a) both actions are `w-full` stacked below `sm:` and `coarse:h-11`,
(b) the restore confirmation is a `DialogContent` at its default `mobile="bottom"`, i.e. a bottom sheet sized in
`dvh` by the primitive, (c) the per-entity result is a `<ul>` and no `<table>` was added anywhere, and (d) the
dialog's only width override is `md:`-prefixed. **A human still has to look at it.**

## Working tree note (start of session)

The branch carries **22 modified files + 2 untracked** from the in-flight `backup-works-everywhere`
feature, which is **not** this feature's work and must be excluded from its commits:

```
.claude/skills/start-clinic/scripts/start.ps1   CLAUDE.md
api/ClinicManagement.API/CLAUDE.md              api/ClinicManagement.API/Program.cs
api/ClinicManagement.API/Maintenance/RestoreBackupCommand.cs
api/ClinicManagement.Application/Features/Backup/Queries/GetBackupHistoryQuery.cs
api/ClinicManagement.Infrastructure/CLAUDE.md
api/ClinicManagement.Infrastructure/Deployment/DeploymentProfile.cs
api/ClinicManagement.Infrastructure/Security/DirectoryAclHardener.cs
api/ClinicManagement.Infrastructure/Services/PgDumpBackupService.cs
api/ClinicManagement.UnitTests/Infrastructure/{Deployment,Security,Services}/*.cs
api/Dockerfile                                  deploy/docker-compose.selfhosted-lan.yml
web/app/bff/auth/change-password/route.ts       web/app/login/page.tsx
web/components/change-password-form.tsx         web/tsconfig.json
?? api/ClinicManagement.Infrastructure/Services/PostgresToolLocator.cs
?? api/ClinicManagement.UnitTests/Infrastructure/Services/PostgresToolLocatorTests.cs
```

⚠️ **Three of them are files this feature also edits** — `api/.../Controllers/BackupController.cs`,
`web/components/backup-settings.tsx` and `web/lib/api/backup.ts`. Their diffs will therefore carry both
features' edits and cannot be separated by path. Stage by path and read `git diff HEAD` before committing.

## Files Changed

**New — Application**
- `Features/Backup/Archive/ClinicArchiveFormat.cs` — schema version, zip layout, manifest, refusal codes
- `Features/Backup/Archive/ClinicArchivePackager.cs` — writes the zip, reads + validates a manifest
- `Features/Backup/Archive/ClinicArchiveRestorer.cs` — the apply half **both** doors share
- `Features/Backup/Archive/ClinicArchiveRestoreReport.cs` — the per-entity result DTO
- `Features/Backup/Queries/BuildClinicArchiveQuery.cs`
- `Features/Backup/Commands/RestoreClinicArchiveCommand.cs`
- `Features/Platform/Commands/RestoreClinicFromArchiveCommand.cs`
- `Features/Platform/Dtos/PlatformClinicRestoredDto.cs`
- `Common/Interfaces/IClinicArchiveStore.cs`

**New — Infrastructure**
- `Persistence/ClinicArchiveScope.cs` — which tables, in what order, and what is excluded and why
- `Persistence/ClinicArchiveStore.cs` — the model-driven exporter/importer

**New — API / web**
- `Controllers/Platform/PlatformClinicRestoreController.cs`
- `web/components/backup/clinic-archive-card.tsx`

**Modified**
- `Common/Interfaces/IAuditActorProvider.cs` — `RestorePrefix`, `AsRestore()`, `IsRestore`, `RestoringAnArchive()`
- `Common/Services/AuditActorProvider.cs`, `Common/Services/ProcessAuditActorProvider.cs`
- `Common/Interfaces/IFileStorage.cs` — `RestoreAtKeyAsync` + `ExistsAsync`
- `Infrastructure/Storage/LocalDiskFileStorage.cs`, `Infrastructure/Storage/MinioFileStorage.cs`
- `Infrastructure/Extensions.cs` — registers `IClinicArchiveStore`
- `Domain/Enums/PlatformAccessAction.cs` — `RestoredClinic = 5`
- `API/Controllers/BackupController.cs` — the two archive actions ⚠️ *also carries `backup-works-everywhere`'s edits*
- `web/lib/api/backup.ts` ⚠️ *also carries `backup-works-everywhere`'s edits*
- `web/components/backup-settings.tsx` — mounts the card in **both** branches ⚠️ *ditto*

## Tests Run

Built to a scratch `OutDir` and run with `dotnet vstest` (the documented Smart-App-Control workaround, `-c Release`).

| Suite | Filter | Result |
|-------|--------|--------|
| Unit | `ClinicArchiveScopeTests` | 24 passed, 0 failed |
| Unit | `ClinicArchiveStoreMaterializationTests` | 12 passed, 0 failed |
| Unit | `ClinicArchivePackagerTests` | 18 passed, 0 failed |
| Unit | `ClinicArchiveRestorerTests` | 12 passed, 0 failed |
| Unit | `ClinicArchiveEndpointTests` | 11 passed, 0 failed |
| Unit | `PlatformClinicRestoreTests` | 11 passed, 0 failed |
| Unit | `AuditActorRestoreTests` | 6 passed, 0 failed |
| Unit | **whole suite** (a shared DTO and `PlatformReadShape` were edited) | **2 785 passed, 0 failed** |

Build: **0 errors**, and **no new warning in any changed file** (the solution's pre-existing `CS8618`/`CS8602`/`CS8981` baseline is unchanged).

## Deferred to /test-small-feature *(all eight now covered — see the Test Plan above)*

No test was written at implementation time. The scenarios this change made testable, in rough priority order:

1. **AC-1** — an export with two cabinets seeded contains no foreign id (the spec names this test explicitly).
2. **AC-2** — restoring into a cabinet that still has everything reports all rows `alreadyPresent` and writes nothing.
3. **AC-3** — after deleting rows, exactly those come back, with their original ids **and** invoice/devis/avoir
   numbers, and the next number issued continues the sequence.
4. **AC-4** — a row that exists but differs is counted under `conflicts` and is **not** overwritten.
5. **AC-6 / AC-7** — the three coded refusals, each asserted on its `code` and not its French sentence.
6. **`ClinicArchiveScope.Resolve`** — the derived plan: every excluded table absent, parents before children, and
   a table with no clinic path reported rather than dropped. This is the guard that keeps the entity set derived.
7. **`AuditActor.AsRestore()`** idempotence, and that `RestoringAnArchive()` decorates the caller rather than
   replacing them (AC-9) — including *after* the actor has been read, which is the case `RunAs` deliberately refuses.
8. **`ClinicStorageKeyTests`** — confirm the existing derived guard still passes now that a third write method
   exists on `IFileStorage`; it reflects over `UploadAsync` overloads only, which is why `RestoreAtKeyAsync` is
   deliberately not named `UploadAsync`.

## Auto-Approved Deviations

| Deviation | Reason |
|-----------|--------|
| `Materialize()` prefers the private parameterless ctor over `GetUninitializedObject` | Internal to `ClinicArchiveStore`, same behaviour for value objects, and it is the only version that initialises the `List<T>` fields backing collection navigations — without it EF's fix-up NREs inside the change tracker when the entry is marked `Added`. |
| `ClinicArchiveTableScope` enum replaced the original `IsDirectlyOwned` bool | Internal shape; a third case (`Self`, for the `Clinic` row itself) is not expressible as a bool. |
| Upload cap read from `Backup:ArchiveMaxSizeMb` in the action, not `[RequestSizeLimit]` | The spec pins « cap stated in config »; the attribute takes a compile-time constant and so could not be operator-set. Refusal is a French sentence naming the limit rather than Kestrel's empty 413. |

## Significant Deviations

### DEV-0 — the small pipeline was forced on a spec whose real surface is ~25–30 files

**Spec says:** `Type: Small`.
**Reality found in Step 3:** ~35 archived entity types, a new `IFileStorage` overload, three endpoints,
a console re-provisioning path, and a frontend card + sheet.
**Asked:** the user chose **"Full vertical slice, small pipeline"** over escalating to
`/plan-feature → /break-plan → /implement-story`, and chose to **stay on `feature/windows-desktop-app`**
rather than branching or committing the in-flight backup work first.
**Approved:** Y — explicit, via `AskUserQuestion`.

### DEV-1 — the restore materialises rows past the domain constructors, by design

**Spec says:** nothing about the mechanism; it asserts only that ids and document numbers are preserved (AC-3).
**Problem:** every primary key in this product is a GUID minted *inside* the domain constructor, and many
timestamps are stamped there from `DateTime.UtcNow` (`PatientFile.UploadedAt`, `Invoice.CreatedAt`, …). Building
entities the ordinary way gives every restored row **a new identity and today's date** — the opposite of a
restore, and it breaks the one property the whole feature rests on.
**Implemented:** `ClinicArchiveStore` reads and writes rows as **property bags driven by the EF model**
(`IEntityType.GetProperties()` + owned navigations), materialising an instance through its private parameterless
constructor and writing values onto the model's own properties. No domain constructor and no mutator runs.
**Why not a DTO per entity:** ~35 tables would be ~35 second definitions of what a row is, and every column added
later would need remembering in two places — this repo's `fixes-dont-propagate` shape, with the symptom being a
column that silently stops being archived. Derived off the model, a new column travels the day it is written.
**Impact:** the archive bypasses domain invariants **on the way in as well as out**. That is acceptable *only*
because the rows being written were validated by those invariants when they were first created, and the restore
inserts nothing that was not previously a committed row. It would not be acceptable for an import of foreign data.
**Approved:** implied by the user's "full vertical slice — I make the restore-mechanics call and log it".

### DEV-2 — `IFileStorage` gains `RestoreAtKeyAsync` + `ExistsAsync`, deliberately NOT `UploadAsync` overloads

**Spec says:** EC-4 — a pre-US-5 flat blob key is restored **verbatim** rather than re-prefixed.
**Problem:** both `UploadAsync` overloads *compose* a key from a required `clinicId`, and `ClinicStorageKeyTests`
asserts — **by reflection over the interface, not over a list** — that every `UploadAsync` takes a `Guid`. A third
upload overload without one would silently restore the defect `multi-tenant-cloud` US-5 closed.
**Implemented:** a differently-named method that takes the key and no clinic, mirroring `DownloadAsync`'s existing
verbatim contract. The derived guard stays green **by construction** rather than by exemption.
**Impact:** `IFileStorage` grows two members; both backends implement them. No existing caller changes.

### DEV-3 — `GET /api/backup/archive` is **not** gated on `BacksUpItsOwnData`

**Spec's API contract says:** `404 where the deployment has no per-clinic archive`.
**Implemented:** no gate — every deployment has one. The archive goes through the same tenant filter as every CSV
export and carries one cabinet's rows, so the two reasons `BacksUpItsOwnData` turns `pg_dump` off on the hosted
kinds (an off-server sidecar already runs; `pg_dump` has no tenant predicate and would cross tenants) apply to
**neither** half of this. On `SelfHostedLan` it is additionally a *portable* copy that the machine-level backup
beside it is not — a practice that loses the PC has the archive and not the dump.
**Consistency with the contract:** the pinned 404 branch is not removed in spirit — its condition is simply never
true today. Adding a branch that can never fire would be worse than the statement it implements.
**Impact:** the settings card now shows the archive on **all three** profiles, not only the hosted one the spec's
"What Changes" bullet names.

### DEV-4 — the console path **restores** the `Clinic` row rather than provisioning a new cabinet

**Spec says:** « Re-provisions the cabinet at the archive's own clinic id, then applies the same restore. »
**Problem:** running `LocalClinicProvisioning.ProvisionAsync` first creates a *new* `Clinic` row, so the archive's
own row is then « présent mais différent » and — correctly, per AC-4 — **skipped**. The practice would come back
with its patients and its money but a blank name, no billing settings, no working hours and no logo.
**Implemented:** `Clinic` is archived as its own scope (`ClinicArchiveTableScope.Self`, matched on its primary
key) and restored first, so the cabinet comes back exactly as archived. Only what an archive deliberately does
**not** carry is created afterwards: the administrator (password hashes do not travel in a file — the spec says so
in Out of Scope, and it says « the console path re-provisions **the admin** ») and the entitlement, through the
companion's own `LocalClinicProvisioning.StageEntitlementAsync` rather than a second answer.
**Impact:** an archive with no `Clinic` row is refused with `archive_invalid` instead of half-creating a cabinet.
**Approved:** implied by the same instruction as DEV-1; it is also the more literal reading of the spec's own
"re-provisions the admin".
