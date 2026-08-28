# patient-file-mirror — implementation record

Phase 2 of the local-copy work. Phase 1 (`clinic-archive-auto-copy`) put the whole record on the admin's laptop
as a `.zip` rebuilt every N days; this puts the patient files there as loose, browsable, per-patient folders,
current within the day.

## Working tree note (start of session)

Branch `feature/clinic-archive-auto-copy`, continued rather than branched again — this is the same body of work
and the same reviewer. **Concurrent uncommitted work by another author** was present in `web/` and
`console/tsconfig.json`, plus a large untracked `landing-v2/` tree; none of it was touched or staged. Files were
staged explicitly by path.

## What was built

**Backend — one read, no schema change.**

| File | Role |
|---|---|
| `Domain/Repositories/ClinicFileManifestRow.cs` | New. The projection: file id, patient id, patient name, file name, content type, size, upload time. No `StorageKey`. |
| `Domain/Repositories/IPatientFileRepository.cs` | `GetClinicManifestPageAsync(clinicId, paging, ct)` — the one read in this interface that is not about a single patient. |
| `Infrastructure/Repositories/PatientFileRepository.cs` | The join to `Patients`, `where file.ClinicId == clinicId`, ordered **ascending**. |
| `Application/Features/Backup/Queries/GetClinicFileManifestQuery.cs` | New. DTO + handler, on `ListArchiveGrantsQuery`'s shape, reusing `ArchiveGrantGuard.ResolveAdminClinicAsync`. |
| `API/Controllers/BackupController.cs` | `GET /api/backup/file-manifest`, `AdminOnly` (class), `AllowsWithoutSubscription`, the archive rate-limit policy. |

**No entity, no column, no migration** — `PatientFile` already carries `ClinicId`, `FileSize` and `UploadedAt`.
That is why `verify-schema` was not run: there is nothing for it to see.

**Shell.**

| File | Role |
|---|---|
| `ArchiveGrant.cs` | New. The grant→token exchange, lifted out of `ArchiveCopyService` so both callers share it. |
| `MirrorPathPlanner.cs` | New. Pure manifest → folder tree. Where AC-3 and AC-10 live. |
| `FileMirrorService.cs` | New. Walk the manifest, diff, pull what is missing. |
| `ArchiveCopySettings.cs` | `+ bool MirrorFiles`, defaulting false. |
| `ArchiveCopyWindow.xaml(.cs)` | The checkbox and its consequence; « Copier maintenant » now runs both. |
| `MainWindow.xaml.cs` | `StartFileMirrorIfEnabled()` — a 30-minute `DispatcherTimer`, guarded against overlap. |

## Decisions worth knowing

**The grant needed no extension.** `POST /api/backup/archive-grants/token` calls `auth.GenerateToken(issuer)` and
returns an **ordinary clinic-admin JWT**, so a manifest endpoint gated `AdminOnly` is reachable with nothing new,
and the per-file download works unchanged. That is the whole reason this phase is one read plus a shell service
rather than a second credential type.

⚠️ **The flip side, and it is phase 1's, not this one's:** that token can do anything else an admin can do that is
not step-up-gated, for 30 minutes. AC-1 of phase 1 still holds for the two gated actions — a grant presented to
the restore is refused — but « the grant authorises download only » is narrower in the docstring than in effect.
Raised with the owner; left as it is, deliberately, because narrowing it to a scoped token is a phase-1 change.

**Ascending order (AC-2)**, against every other list in the product. A caller walking pages is racing uploads:
newest-first pushes an unread row past the cursor each time somebody scans a document. This is the `OFFSET`-over-
a-shifting-set trap in its *inserting* form, which the repo's existing note only describes in its sorting form.

**The path is a pure function of the whole manifest (AC-3)**, which is what lets the mirror keep no index file:
freshness is « compute the path, does a file of that size sit there? ». A collision suffixes **both** sides, never
just the later arrival, or the path would depend on the order pages happened to arrive in.

**Size, not hash, is the freshness check (AC-4).** A hash means downloading the file to decide whether to download
it, and these rows are immutable — the product has upload and delete, no replace.

**The mirror never deletes (AC-6).** Stated to the user in the window. The folder can therefore outgrow the
cabinet; that is the accepted trade for a doctor's own copy.

**Dual-write on upload was scoped out** and the owner was told before approving. It needs a
`window.__clinicShell` bridge the desktop shell does not have plus changes to a `web/` browsers also serve, and it
covers only files uploaded *on that laptop*. The 30-minute pull covers every device with one mechanism.

## Verification

| Gate | Result |
|---|---|
| `dotnet build api/ClinicManagement.sln -c Release` | **0 errors**, 55 pre-existing warnings (unchanged count) |
| `dotnet build desktop/…Tests.sln -c Release` | **0 errors, 0 warnings** |
| `ClinicFileManifestQueryTests` (new, 8) | **8 passed** |
| `MirrorPathPlannerTests` (new, 14) | **14 passed** |
| `verify-schema` | n/a — no schema change |

**Tests were written here rather than deferred to `/test-small-feature`**, against that skill's default. Two
reasons: `MirrorPathPlanner` is the piece the feature's correctness rests on and it is pure, so it is cheap and
high-value to pin now; and AC-1 names a guard test explicitly.

⚠️ **The AC-1 test asserts the argument, not the result.** Nothing in `UnitTests` touches a database, so the EF
query filter is not in play and a test checking only the returned rows would pass against a handler reading every
clinic on the platform. What is checked is that the clinic reaching the repository is the one resolved from the
caller's own **account row** — which is the thing that would actually break.

### New: `desktop/ClinicManagement.DesktopShell.Tests`

The desktop solution had no test project. One was added (`net8.0-windows`, xUnit) and **wired into CI** — the
`desktop` job now runs `dotnet test` after its build. The shell itself still cannot be *run* there (no WebView2
runtime), so that job's name changed from « build » to « build + test » to say what it now covers.

## Still owed

- **A walk on the real VPS install.** Phase 1's equivalent found the step-up defect that no build could see; the
  same class of gap is open here. The specific things a walk would exercise and the tests cannot: the manifest
  against a real tenant filter, the 401 re-exchange (needs a run longer than 30 minutes), and the free-space
  refusal.
- The installer has not been rebuilt with any of this.
- Phase 1's own remaining items: the web card's eye pass at 320/390/820/1180/1440, and `/test-small-feature`.
