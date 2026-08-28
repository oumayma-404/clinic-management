# Progress: Copie automatique de l'archive sur le poste

**Started:** 2026-08-28
**Type:** Small
**Branch:** feature/clinic-archive-auto-copy

## Status
- [x] Implementation — all three layers
- [x] Quality checks (solution build · shell build · tsc · check:responsive 22/22 · next build)
- [ ] Tests (handled by /test-small-feature)

## Working tree note (start of session)
Unrelated and excluded from this feature's commits: `console/tsconfig.json`, `follow-up/README.md`,
`web/lib/dashboard/day-phrases.ts`, plus the untracked `landing-v2/` and `site/` marketing material and the
loose `*.png` at the repo root. None is touched here.

## Files Changed

**Domain**
- `Entities/ClinicArchiveGrant.cs` — new. CSPRNG secret + SHA-256, `IsUsable`/`MarkUsed`/`Revoke`.
- `Repositories/IClinicArchiveGrantRepository.cs` — new.

**Infrastructure**
- `Repositories/ClinicArchiveGrantRepository.cs` — new.
- `Persistence/Configurations/ClinicArchiveGrantConfiguration.cs` — new.
- `Persistence/ApplicationDbContext.cs` — `DbSet` + `HasQueryFilter`.
- `Persistence/ClinicArchiveScope.cs` — added to `Excluded`.
- `Auth/ArchiveGrantAuthorizer.cs` — new.
- `Extensions.cs` — two registrations.
- `Migrations/20260828145918_AddClinicArchiveGrants.cs` (+ Designer + snapshot).

**Application**
- `Features/Backup/ArchiveGrantDtos.cs`, `Commands/ArchiveGrantCommands.cs`,
  `Queries/ListArchiveGrantsQuery.cs`, `Common/Interfaces/IArchiveGrantAuthorizer.cs` — all new.

**API**
- `Controllers/BackupController.cs` — four endpoints + the `X-Archive-Grant` header constant.

**Web**
- `lib/api/backup.ts` — `archiveGrants` / `issueArchiveGrant` / `revokeArchiveGrant` + two DTOs.
- `components/backup/archive-grants-card.tsx` — new « Postes autorisés » card.
- `components/backup/clinic-archive-card.tsx` — mounts it, below the manual download it automates.

**Desktop shell** (`ClinicManagement.DesktopShell.sln` — a separate solution, built separately)
- `ArchiveCopySettings.cs` — settings + store, `archive-copy.json` beside `server.json`.
- `ArchiveCopyService.cs` — the pull: grant exchange, stream to `.part`, rename, prune, free-space, ACL, BitLocker.
- `ArchiveCopyWindow.xaml` / `.cs` — the setup window, reached from the WebView's right-click menu.
- `MainWindow.xaml.cs` — the menu item + `RunArchiveCopyIfDue()` on first successful navigation.
- `desktop/CLAUDE.md` — the three new files documented in the map.

**Integrity fix (outside this spec — see DEV-2)**
- `Persistence/ClinicArchiveStore.cs` — the export now runs in one `RepeatableRead` snapshot.

## Quality checks run
`dotnet build ClinicManagement.sln -c Release` (out-of-repo `BaseOutputPath`, per the SAC rule) —
**0 errors**, and **no warning naming any file this feature touched** (the solution's ~55 pre-existing
nullable warnings are unchanged). `dotnet ef migrations add` ran successfully — no WDAC block this time,
because the API was not running and the output path was redirected.

⚠️ **The generated migration emitted an `xmin` column and it was removed by hand** — the trap the root
`CLAUDE.md` documents and that `AddClinicSubscriptions` and `AddSuppliers` both hit. `Entity<T>.Version` maps
onto PostgreSQL's *system* column, so `CreateTable` with a real one fails with « column name "xmin" conflicts
with a system column name ». **The migration has not been applied to a database yet**, so that fix is reasoned
rather than observed; `verify-schema` before/after is still owed.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| `CreatedByUserId` is `string`, not `Guid` as the spec's Data section said | `User : AggregateRoot<string>` — its key *is* the auth subject. A `Guid` would not have compiled against `IUserRepository.GetByIdAsync(string)`. |
| No `IClinicClock` injected; handlers use `DateTime.UtcNow` | There is no such interface in this repo — `ClinicClock` is static and governs *clinic-local day* arithmetic. These are UTC instants, exactly like `ClinicSignup.CreatedAtUtc` and `ClinicRecoveryPoint.StartedAt`. |
| Three admin operations in one `ArchiveGrantCommands.cs` | One small CRUD surface over one table; three files would repeat the same caller-resolution guard three times. |

## Significant Deviations

**DEV-1 — a grant is exchanged for an ordinary access token, rather than being accepted directly by
`GET /api/backup/archive`.**
The spec's API Contract said the archive endpoint would take `X-Archive-Grant` "as an alternative to
`X-Step-Up-Confirmation`". Exploration showed that cannot work as written: an unattended pull has **no session**,
while the endpoint is `[Authorize(AdminOnly)]` and `BuildClinicArchiveQuery` resolves the cabinet from
`IClinicContext.GetUserId()` → `User.ClinicId`. Accepting the grant there would have required a parallel,
session-free path through the policy, the tenant filter, the access ledger and the `LastArchiveDownloadedAtUtc`
stamp — four places to re-implement and one to forget.

So `POST /api/backup/archive-grants/token` (anonymous) validates the grant and mints a **normal** short-lived
token on the issuing admin's identity. After that call nothing downstream knows the request began differently.
The header constant is still defined and still named `X-Archive-Grant`; it travels on the exchange rather than on
the download. **Impact:** one extra endpoint, one extra round trip per copy, and everything the spec's ACs assert
still holds. **Approved:** not yet — surfaced to the owner in the hand-off, since it changes a pinned contract.

**DEV-2 — the archive export now runs in one `RepeatableRead` transaction.** Outside this spec, raised by the
owner's « data should be consistent, integrity at all times » and approved by them in-session.
`ClinicArchiveStore.ExportAsync` walks ~35 tables in sequence under PostgreSQL's default `ReadCommitted`, which
gives **every statement its own snapshot** — so a cabinet working during an export could produce an archive with
an appointment whose patient the file does not contain, or a payment against an invoice snapshot older than it.
`ClinicArchiveScope`'s FK-ordered apply sequence assumes the rows were captured together, and nothing was making
that true. This feature is what makes it urgent: it moves archive-taking from a deliberate out-of-hours click to
an unattended schedule that runs mid-consultation.
**Safety checked before shipping it:** the export path is read-only (no `SaveChanges`, no `DbSet.Add`), so the
`await using` rollback-on-dispose loses nothing; the ledger row and the staff notification are both written
**before** the call and outside the transaction; and no caller has one open, so there is no nesting. `Serializable`
was rejected — a read-only transaction has nothing to serialize against and would only add retry paths.
⚠️ **Known operational cost, accepted:** a long-open `RepeatableRead` transaction holds a connection and blocks
`VACUUM` from reclaiming rows for its duration. On a large cabinet that is minutes. Correctness wins, but it is
worth watching on the shared hosted database once scheduled copies exist.

## Deferred — NOT done in this pass

- **Verifying a copy end to end.** The shell builds clean, starts without crashing and every XAML resource the
  new window asks for resolves (checked against `App.xaml`) — but **no copy has ever been run**. The grant flow
  has not been exercised against a live server, so AC-5/6/7/9's behaviour is written and reasoned, not observed.
  This is the single most valuable thing to do next, and it is one click: « Copie automatique de l'archive… » →
  paste a key → « Copier maintenant ».

- **The eye pass at 320 / 390 / 820 / 1180 / 1440.** `check:responsive` is green and the card follows the two-tree
  hinge, the 44 px floors and the `md:`-prefixed dialog widths — but I did not open a browser, so that half of the
  frontend gate is **owed, not done**. The surfaces to walk are the card's table↔cards hinge and the one-time-key
  dialog at 320 px, where the base64 secret is the longest unbreakable-looking string on the screen (it is
  `break-all`, deliberately, not truncated).
- Tests, per this skill's contract — `/test-small-feature`. The ones that matter most: AC-1 (a grant is refused by
  the restore endpoint), AC-4 (cross-clinic refusal asserted directly, not via the ambient filter), and the
  authorizer's deactivated/demoted-issuer path.

---

## End-to-end run — 2026-08-28, local dev stack

**Migration applied and verified.** The API auto-applied it at boot. PostgreSQL now holds exactly the eight
intended columns and **no `xmin`**, so the hand-removal held; both indexes exist and `SecretHash` is unique.
`verify-schema` went **7 drifts → 4**: the three that cleared are precisely
`ClinicArchiveGrants(ClinicId, CreatedAtUtc)`, `(SecretHash)` and `(ClinicId) -> Clinics` moving from
« MISSING in the database » to « present », and the four that remain (`audit-chain-intact`,
`key-ring-protection`, `messaging-month-covers-every-clinic`, `overlapping-appointment-pairs`) are byte-identical
before and after. **No new drift.**

**The flow, against the running API.** Admin login (with a TOTP enrolment and a forced password change on the
way), then:

| | Result |
|---|---|
| list grants, none yet | `[]` |
| issue a grant | 200 + the secret, once |
| the secret in the database | 64 hex of SHA-256; a search for the plaintext returns **0 rows** |
| exchange the grant, **no session at all** | 200 + an access token |
| a bogus grant | « Ce poste n'est pas autorisé. » |
| download the archive with bearer + grant | **200, 19 859 bytes** |
| the file itself | a real zip — 41 entries, `testzip()` clean, 40 tables, **206 rows** |
| `ClinicArchiveGrant.LastUsedAtUtc` | stamped |
| `Clinic.LastArchiveDownloadedAtUtc` | stamped ⇒ `ArchiveStale` clears itself |
| the audit ledger | a delivery row at the same instant |
| revoke | 204, and the row is **kept** with `RevokedAtUtc` set |
| the list response | carries no `secret` field at all |
| **a revoked grant** | **403 « Ce poste n'est pas autorisé. »** — the same sentence an unknown one gets (AC-3) |
| a second, un-revoked grant | still 200 — so the revocation was targeted, not the endpoint breaking |

All twelve checks pass. ⚠️ The archive rate-limit policy is a **5-minute window** and a retry loop keeps it
busy — budget for that when re-running this by hand, or the refusals read as failures.

**⚠️ A real defect the end-to-end caught, and it made the feature non-functional.** `DownloadArchive` still
called `RequireStepUp` unconditionally, so a grant-issued token got past `AdminOnly` and was then refused by the
step-up gate — which an unattended shell can never satisfy. DEV-1 had moved the grant onto the exchange and left
the download with no way through. Fixed by having the download accept `X-Archive-Grant` as an alternative to the
confirmation, which is what the spec's API Contract said in the first place and is the safer shape: **the grant
is re-checked at the download** rather than trusted from the exchange, so a revocation between the two calls
stops it and a leaked token alone carries no step-up power. The shell now sends both headers.

**AC-1 (a grant must never restore) — proven structurally.** `RestoreArchive` binds only
`X-Step-Up-Confirmation`, contains **zero** references to the grant header or the authorizer, and guards on the
*different* action `restore-clinic-archive`. A grant cannot reach it.

**⚠️ A build trap worth recording**, and it cost a false negative here: `dotnet build -p:BaseOutputPath=<temp>`
(the Smart-App-Control workaround) writes the assemblies to that temp path and **not** to `bin/Release`, so the
running API kept serving the old code and the first retest still failed. Build the host project to its own
`bin/` — after stopping it, or the copy step hits the lock — before believing any end-to-end result.

## Still not done

- **The shell's own file handling is NOT exercised** — `.part`→rename, retention, the free-space refusal and the
  folder ACL. The HTTP contract it performs is proven identical (same two headers, same order, via curl), but the
  local half is not. The blocker is mechanical: the shell is HTTPS-only by design and this dev API binds
  **HTTP 5000 only** — `Program.cs` pins Kestrel, so `ASPNETCORE_URLS` does not add a TLS listener. Driving it
  needs either the HTTPS front door or the hosted VPS.
- The web card's eye pass at 320 / 390 / 820 / 1180 / 1440.
- Tests — `/test-small-feature`.
