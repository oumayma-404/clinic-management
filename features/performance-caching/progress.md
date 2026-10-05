# Progress: Performance — measure, stop duplicate work, one shared browser cache

**Started:** 2026-10-05
**Type:** Small (slice 1 of 2: blueprint Part 1, server)
**Branch:** feature/performance-caching (worktree `.claude/worktrees/performance-caching`, branched from
`feature/security-remediation` HEAD `4c4de3f6`, which is 213 commits ahead of `main`)

## Status
- [x] Implementation — Part 1: 1.1 timing, 1.2 account lookup once, 1.3 preview ETag
- [x] Quality checks — `dotnet build ClinicManagement.sln -c Release` (temp `BaseOutputPath`): 0 errors, no warning in a changed file (64 pre-existing)
- [x] Blast radius closed (11 rows: 1 changed — the docs; 3 to re-test — rows 4, 6, 9)
- [x] Tests — 4 classes (3 new, 1 extended), full unfiltered suite green; see « Test Plan »
- [x] Baseline numbers — before/after A/B in `notes.md` (2026-10-05)
- [ ] Part 2 (browser cache) — next slice
- [x] Browser QA — `qa/run-1.md`, GREEN (8 rows: 3 api, 5 browser)

## Blast Radius
| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `LoggingBehavior` | MediatR pipeline log | registered in `Application/Extensions.cs`; no test, no script reads its text | unaffected — log text only |
| 2 | `UserRepository.GetByAuth0SubAsync` | the caller's account read | `RequestAccount`, `CurrentClinicResolver` (≈201 handlers), 61 direct callers, `RealtimeBroadcastBehavior` post-commit | unaffected — returns the instance a tracked re-query would have returned; falls back to the query when the includes are not loaded |
| 3 | same | — | `RecoveryCodeLoadingCoverageTests` (source scan for `Include(u => u.RecoveryCodes)`) | unaffected — the include stays in the fallback query |
| 4 | same | — | second-factor paths (consume / regenerate recovery codes, TOTP) | must re-test — wire, on a real DB |
| 5 | `ApplicationDbContext` interceptor list | EF options | `AuditSaveChangesInterceptor`, `AutomaticWriteInterceptor`; jobs and console verbs (same DI) | unaffected — a command interceptor, separate from save interceptors; hand-built contexts (design-time factory, tests) do not get it |
| 6 | `Program.cs` pipeline | middleware order | source-scan tests (`SubscriptionGateMiddlewareTests`, `AccountStateEnforcementTests`, …) | must re-test — full suite |
| 7 | `FileDownloadDto` | download DTO | `DownloadPatientFileQuery` (never sets the new fields) | unaffected — additive |
| 8 | `DownloadPatientFilePreviewQuery` | preview query | controller (object initializer), tests | unaffected — new optional property |
| 9 | preview response headers (`ETag`, `Cache-Control: private, no-cache`, 304) | HTTP | `apiGetBlob` → `file-thumbnail.tsx` pool, in-app viewer; desktop WebView2, mobile WebViews | must re-test — a thumbnail still paints after reload, and a 304 is turned into a cached 200 by the browser |
| 10 | `PatientFileAccessCoverageTests` | names the preview route as exempt from the ledger | — | unaffected — no ledger change |
| 11 | `api/*/CLAUDE.md` | docs | middleware order (API), interceptor list (Infrastructure), behaviours (Application) | must change |

## Working tree note (start of session)
- Built in a fresh worktree off HEAD, so none of the main checkout's 168 dirty files are here.
- A peer session (`clinic-management-0b`) was live in the main checkout. No branch was switched there.

## Files Changed
- `api/ClinicManagement.Infrastructure/Persistence/RequestQueryMetrics.cs` (new)
- `api/ClinicManagement.Infrastructure/Persistence/QueryCountingInterceptor.cs` (new)
- `api/ClinicManagement.API/Middleware/RequestTimingMiddleware.cs` (new)
- `api/ClinicManagement.Infrastructure/Extensions.cs`
- `api/ClinicManagement.API/Program.cs`
- `api/ClinicManagement.Application/Common/Behaviors/LoggingBehavior.cs`
- `api/ClinicManagement.Infrastructure/Repositories/UserRepository.cs`
- `api/ClinicManagement.Application/Features/Files/Queries/DownloadPatientFileQuery.cs` (`FileDownloadDto`)
- `api/ClinicManagement.Application/Features/Files/Queries/DownloadPatientFilePreviewQuery.cs`
- `api/ClinicManagement.API/Controllers/PatientFilesController.cs`
- `api/ClinicManagement.API/CLAUDE.md`, `api/ClinicManagement.Infrastructure/CLAUDE.md`,
  `api/ClinicManagement.Application/CLAUDE.md`

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| `RequestQueryMetrics` + the interceptor live in `Infrastructure/Persistence/`, not a new `Diagnostics/` folder | Next to `AutomaticWriteInterceptor`, the existing shape |
| `LoggingBehavior` keeps its « Handling » line (blueprint said drop it) and only adds the elapsed ms | A « Handling » with no « Handled » names the request in flight at a hang; halving log volume is not worth that |
| The tracked-user lookup uses `Local.FindEntry` with `AutoDetectChangesEnabled` off for the read | `DbSet.Local`'s getter runs `DetectChanges` over the whole request, which the query it replaces never did — same answer, no new side effect |

## Significant Deviations
1. **DEV-1 — no `spec.md`; `blueprint.md` is the spec.** The owner chose « 1 + 2 » in `/think-solution` and
   invoked `/implement-small-feature` on it. Part 1 alone is ~10 files, Part 1 + 2 ≈ 40, so this pass is
   **Part 1 only** (the blueprint's own order of work: server first, then numbers, then the browser). Approved: Y
   (owner's choice + the blueprint's order).
2. **DEV-2 — logo and cachet dropped from 1.3.** Both are written at a **fixed path, overwritten in place**
   (`UploadAsync(..., "logo")`, `"doctors/{id}/cachet"`), so a tag derived from the key would answer « not
   modified » for ever after a logo change. A correct tag needs a storage version read (a new `IFileStorage`
   member in two backends and the fakes) for two small images. Previews are safe: `PreviewStorageKey` and
   `StorageKey` are set only at construction (`PatientFile.cs:81,128`), so the bytes behind a served key never
   change. Approved: Y (narrows scope, removes a stale-logo defect).
3. **DEV-3 — 1.4 not done: both gates stayed closed.** Dashboard median 171–186 ms (< 300 ms), agenda query 1.7 ms in SQL. Numbers in `notes.md`.

## Deferred to /test-small-feature
- `LoggingBehavior`: one line per request, carrying elapsed ms; no « Handling » line.
- `RequestTimingMiddleware`: logs the route template and never a `?` or a `/hub` path; slow requests go to
  `Information`, fast ones to `Debug`; non-`/api` paths are not logged.
- Preview query:
  - a matching `IfNoneMatch` → `NotModified`, and `IFileStorage.DownloadAsync` is never called;
  - **wrong clinic + matching tag** → « Fichier introuvable », not `NotModified`;
  - stand-in key vs fallback key → different tags.
- Download query: no `ETag`, ledger still written.
- Full unfiltered suite (rows 3, 6, 10).

## Next
1. Restart the API on this branch (announce first if a peer owns it — `shared-stack.md` § 3), with
   `Diagnostics__SlowRequestMs=0` so every request is logged. Record the blueprint's six reads in `notes.md`
   § Before/After.
2. Decide 1.4 from those numbers.
3. Part 2 (browser cache), as its own slice.

## Test Plan
| Blueprint item | Action | Target file | Covers |
|----|--------|-------------|-------|
| 1.1 timing | New class | `UnitTests/Api/RequestTimingMiddlewareTests.cs` | route template only (no path id, no `access_token`) · status + query count · Debug under threshold, Information at it · logged even when the request throws · non-`/api` paths pass through unlogged |
| 1.1 handler timing | New class | `UnitTests/Common/Behaviors/LoggingBehaviorTests.cs` | « Handling » before the handler runs, « Handled … in N ms » after |
| 1.2 account once | New class | `UnitTests/Infrastructure/Persistence/UserRepositoryTrackedCallerTests.cs` | tracked + both includes → same instance, no DB · either include missing → DB read · `Deleted` → DB read · untracked → DB read · `AutoDetectChangesEnabled` restored both ways. A `DbConnectionInterceptor` throws on any connection, so « no query » is asserted, not assumed |
| 1.3 preview ETag | Add scenarios | `UnitTests/Features/Files/PatientFilePreviewTests.cs` | strong 32-hex tag · matching tag → `NotModified`, storage read once only · stale tag → bytes + current tag · stand-in vs small original → different tags · **other cabinet + matching tag → « Fichier introuvable. »** |

**Coverage notes (no unit surface)**
- `QueryCountingInterceptor` — EF calls it only on a real command; its count is checked by the live log during the baseline run (below). `RequestQueryMetrics` is exercised through the middleware test.
- Download query carries no ETag — it never sets one (code read); the ledger path is unchanged and `FilesTenantIsolationTests` still passes.
- Blast-radius rows 3, 6, 10 (`RecoveryCodeLoadingCoverageTests`, the `Program.cs` source-scan tests, `PatientFileAccessCoverageTests`) — covered by the full suite.

**Red-proof:** made the account guard return any tracked row, and put a « matching tag → 304 » check before the tenant check. Rebuilt: 5 tests failed (both « one include missing » cases, « marked for deletion », « other cabinet », « stale tag »). Files restored.

## Tests Run
| Suite | Filter | Result |
|-------|--------|--------|
| Unit (targeted) | the 4 classes above + `RecoveryCodeLoadingCoverageTests` | 39 passed, 0 failed |
| Unit (full, unfiltered) | none | **4 935 passed, 0 failed, 6 skipped** (skips were already there) |

Run with `dotnet build … -p:OutDir=<scratch>/utbuild/` + `dotnet vstest`, the Smart App Control-safe recipe.

## Deferred — needs the stack (shared with peers) — DONE 2026-10-05, see `notes.md`
- Row 4: read path verified (8 unused codes = 8 in DB); consume/regenerate **not exercised** (would spend the QA admin's codes).
- Row 9: **browser-verified** — `qa/run-1.md` GREEN: 16/16 thumbnails paint on first load (200), after a full reload (16 × 304) and for a second user in the same browser (16 × 304); viewer and download unchanged.

- Baseline numbers (blueprint § 1.1): restart the API on this branch with `Diagnostics__SlowRequestMs=0`.
- Blast-radius row 4: second-factor paths (recovery code consume / regenerate) on a real database.
- Blast-radius row 9: a thumbnail still paints after reload, and a second load answers 304 (`curl -I -H "If-None-Match: …"`).
