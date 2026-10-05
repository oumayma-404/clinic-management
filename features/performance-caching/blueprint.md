# Performance — measure, stop duplicate work, one shared browser cache

Status: BLUEPRINT (chosen 2026-10-05: option « 1 + 2 » of `/think-solution`). No code written.

## Summary

The app is not slow because Postgres is slow. It asks the same questions many times: in the browser
(`user-status` 6× on `/appointments`, the acts catalogue re-fetched at 10 places, one SignalR socket per
component) and on the server (the caller's account is looked up twice per request). Nothing measures time
today.

The plan has two parts:

- **Part 1 (server):** measure, then remove duplicate work. No data cache.
- **Part 2 (browser):** add one shared in-memory query cache, cleared by the live updates the server already
  sends, behind one shared connection.

A server data cache (memory or Redis) is **rejected**:

- the account check must stay live;
- `clinic-subscription/spec.md:637` forbids caching access;
- console verbs and some jobs write without broadcasting;
- the LAN installer would need a new Windows service;
- Postgres runs on the same box.

## What this plan does NOT do (and why)

| Not done | Why |
|---|---|
| Server data cache / Redis | See above. One API process, so a cache would be coherent, but nothing is slow enough to pay the staleness risk on money and access |
| A shared cache for **lists** (appointments, patients, invoices, paged reads) | Those are the money/clinical surfaces, where a stale page is a wrong figure. Revisit with Part 1's numbers |
| Patient page reloading on **any** patient's change | The realtime event carries no patient id. Adding one changes the server contract (`RealtimeBroadcastBehavior`, `RealtimeResourceResolverTests`) and is its own change |
| Response compression on the LAN | API JSON on a wired LAN gains little, and compression over HTTPS needs a BREACH decision first |
| Blanket `AsNoTracking` on repositories | A handler that reads through a now-untracked method and then saves loses its write silently |
| Persisting the browser cache (IndexedDB) | Patient data on disk belongs to `features/offline-drafts` (DRAFT). `mobile-tablet-responsive/spec.md:458` rejected a *bundle* cache; an in-memory query cache is not that, but persisting it would be |
| `Server-Timing` header | It would show every browser the internal timings. The log is enough |

---

## Part 1 — Server: measure, then remove duplicate work

### 1.1 Request timing and query count (do first, before anything else)

**Files to create**
- `api/ClinicManagement.Infrastructure/Persistence/Interceptors/QueryCountingInterceptor.cs`: a
  `DbCommandInterceptor` that increments a scoped `RequestQueryMetrics` (command count plus summed command
  duration). Put it next to `AutomaticWriteInterceptor`, wherever that lives.
- `api/ClinicManagement.Infrastructure/Diagnostics/RequestQueryMetrics.cs`: scoped,
  `int Commands { get; }`, `TimeSpan DbTime { get; }`, `void Record(TimeSpan)`.
- `api/ClinicManagement.API/Middleware/RequestTimingMiddleware.cs`: a `Stopwatch` around `_next`. After the
  response it writes **one** log line: `{Method} {RouteTemplate} → {Status} in {ElapsedMs} ms · {Queries}
  queries ({DbMs} ms)`.
  - At `Information` when elapsed ≥ `Diagnostics:SlowRequestMs` (default 500), otherwise at `Debug`.

**Files to modify**
- `api/ClinicManagement.Infrastructure/Extensions.cs:109-114`: register `RequestQueryMetrics` as scoped and the
  interceptor as scoped (the `AutomaticWriteInterceptor` shape). Add it to the `AddInterceptors(...)` list.
- `api/ClinicManagement.API/Program.cs`: put `UseMiddleware<RequestTimingMiddleware>()` **before**
  `UseAuthentication` (≈ line 990), so the account lookup is inside the measured window.
- `api/ClinicManagement.Application/Common/Behaviors/LoggingBehavior.cs`: replace « Handling » + « Handled »
  with one line, « Handled {RequestName} in {ElapsedMs} ms ». That halves the log volume (1 346 of 2 622
  Information lines on 2026-10-04).

**Key rule**
- Log the **route template** (`(context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText`), never
  `Request.Path` or the query string.
  - `/hub` carries the bearer token in `access_token` (`Program.cs:270-280`).
  - Raw paths carry patient ids, and the log file is kept free of patient data on purpose (`Program.cs:249`).
- Skip `/hub/*`, `/health` and the YARP catch-all (proxied web pages).

**Baseline (written to `features/performance-caching/notes.md` § Before)**
Record each of these reads 5×, warm, on the dev DB, and write down the median ms and query count:

| Read | Why |
|---|---|
| `GET /api/dashboard` | ~55 sequential queries by code reading |
| `GET /api/appointments?from=…&to=…` (one day, one week) | agenda; no `(ClinicId, AppointmentDateTime)` index |
| `GET /api/clinics/user-status` | read 6× per `/appointments` load |
| `GET /api/notifications/unread-count` | on every page, plus a 60 s poll |
| `GET /api/patients/{id}` and the 10 parallel tab reads | heaviest screen |
| `GET /api/patient-files/{id}/preview` | once per thumbnail tile |

### 1.2 The account is looked up once per request (unconditional)

**Defect shape:** `RequestAccount` says the account is « resolved **once** per request », and it is, for the
middleware. Then `CurrentClinicResolver.cs:26` runs the same query again in about 262 handlers, and
`RealtimeBroadcastBehavior.cs:87` runs it a third time after every successful command. This is the
« fix wired to one call site » shape.

**File to modify:** `api/ClinicManagement.Infrastructure/Repositories/UserRepository.cs` → `GetByAuth0SubAsync`.

**Logic:**
1. Look in the change tracker first: `_context.Users.Local` for the entity whose `Id == auth0Sub`, with
   `State != Deleted`.
2. Return it **only if** `Entry(u).Reference(x => x.Clinic).IsLoaded` **and**
   `Entry(u).Collection(x => x.RecoveryCodes).IsLoaded`.
3. Otherwise run today's query, unchanged.

**Why the repository and not the resolver:**
- **One change point** covers every reader: the 201 `GetClinicIdAsync` callers, the 61 direct callers and the
  post-commit lookup.
- **No change in meaning.** A second tracked query would have returned *the same instance*, because EF never
  overwrites a tracked entity's values. So the only thing that changes is the round trip. That holds inside
  `/clinics/join` too, where a handler changes the tracked `ClinicId` and reads it back.
- **Why not `ITenantScope.ClinicId`.** Reading the clinic from there would change the answer in the join and
  create-clinic flows. The scope is single-assignment and keeps the clinic the request started with.

**The `IsLoaded` guard is not optional.** A `User` tracked by a read *without* the includes would hand back an
empty `RecoveryCodes`, which is the « 0 code inutilisé » trap (CLAUDE.md, « An unloaded collection navigation is
empty, not stale »).

**Expected effect:** −1 query on every authenticated GET and −2 on every write. Prove it with the 1.1 log.

### 1.3 Thumbnails, logo and cachet: revalidate, do not re-send (unconditional)

**Files to modify**
- `api/ClinicManagement.Application/Features/Files/Queries/DownloadPatientFilePreviewQuery.cs`:
  - Add `string? IfNoneMatch` to the query.
  - Compute `etag = "\"" + hex(SHA-256(servedKey))[..32] + "\""`. `servedKey` is either `PreviewStorageKey`
    or, on the fallback, `StorageKey`. Keys are unique per upload (`clinics/{id}/{guid}-{timestamp}`), so the
    content behind a key never changes.
  - **After** the patient/tenant check (≈ line 62), if `IfNoneMatch == etag`, return a not-modified result
    **without** `_fileStorage.DownloadAsync`.
  - Put `ETag` on `FileDownloadDto`.
- `api/ClinicManagement.API/Controllers/PatientFilesController.cs:353-377`:
  - Pass `Request.Headers.IfNoneMatch` to the query.
  - Map not-modified to `304`.
  - Set `ETag` and `Cache-Control: private, no-cache` on the 200.
- `ClinicsController.cs:548` (logo) and `DoctorsController.cs:83` (cachet): same pattern, with the ETag taken
  from the stored key.

**Rules**
- **`no-cache`, never `max-age`/`immutable`.**
  - `no-cache` means every view still goes through the server's role and tenant check.
  - A `max-age` would let the browser serve a radiograph to the *next* user of a shared reception PC without
    asking anyone. The browser cache is keyed on the URL, not on the token.
- **Never on `DownloadPatientFileQuery`.** Every download writes the access ledger (`PatientRecordAccessLedger`),
  and a 304 would be a copy taken with no row.
- **Never on PDFs.** They are re-rendered on each GET and carry live money.
- Check that the thumbnail fetch (`web/components/patients/files/file-thumbnail.tsx`) does not pass
  `cache: "no-store"` through `client.ts`. If it does, relax it **for this route only**.

**Saves:** the MinIO GET and the bytes on every revisited tile. It does not save the DB queries; the check
must run.

### 1.4 Gated on the 1.1 numbers (do only if the threshold is crossed)

| Step | Threshold | Design |
|---|---|---|
| Dashboard monthly loops | `GET /api/dashboard` median ≥ 300 ms | See below |
| Agenda index | the one-week range query ≥ 50 ms, or `EXPLAIN ANALYZE` shows a seq scan on `Appointments` | Add index `(ClinicId, AppointmentDateTime)` in `AppointmentConfiguration.cs` |

**Dashboard monthly loops (12 + 6 round trips → 3)**
- ⚠️ Read the tombstone first, `IInvoiceRepository.cs:93-99`. A month GROUP BY in SQL failed at runtime
  (`42883: function pg_catalog.timezone(unknown, interval) does not exist`) while every unit test passed. So **no
  timezone maths in SQL**.
- Design:
  - Extract the predicate of `GetCollectedBetweenAsync` into one private `IQueryable` builder. Add
    `GetCollectedPerWindowAsync(clinicId, IReadOnlyList<(DateTime From, DateTime ToInclusive)> windows)`.
  - The new method composes that builder, projects `(PaidOn, Amount)` over `[windows[0].From,
    windows[^1].ToInclusive]`, and buckets in C# against the bounds the caller computed through `ClinicClock`.
  - Do the same for `GetInstallmentCollectedBetweenAsync` (with `excludedPlanIds`) and
    `CountByStatusBetweenAsync`.
- **Same predicate by construction**, not by copy. This is the « mirrored set » shape (`regression-safety.md`
  § 2), and a copy drifts.
- Also read `GetTreatmentPlanLinksAsync` once (`DashboardMoneyReader.cs:54` and `DashboardTrendReader.cs:66`
  both read it) by passing it in from `GetDashboardQuery`.
- `dashboard-insights/spec.md:59` (« the reads stay live ») is respected: this is fewer round trips, not a
  snapshot.

**Agenda index**
- Migration: check the scaffold for the `AddColumn<uint>("xmin")` trap. Run `verify-schema` before and after.

---

## Part 2 — Browser: one shared cache, cleared by live updates

### 2.1 Dependency and provider

**Files to create**
- `web/lib/query/query-provider.tsx`: `QueryClientProvider` around one `QueryClient` per tab.
  - Defaults: `staleTime: 5 min`, `gcTime: 10 min`, `retry: 1`, `refetchOnWindowFocus: true`.
  - **Memory only, no persister.**
- `web/lib/query/keys.ts`: the query keys. **Rule:** a query declares which realtime resources make it stale,
  in `meta.realtime: RealtimeResourceKey[]`. Its first key segment is its owning resource.

**Files to modify**
- `web/package.json`: add `@tanstack/react-query` (v5; supports React 19).
- `web/app/layout.tsx:117-131`: mount `QueryProvider` **inside** `SessionProvider`, around
  `SubscriptionProvider` (which stays as it is; see the pitfalls), and mount `ClinicRealtimeProvider` just
  inside it.

### 2.2 One live connection per tab

**File to create:** `web/lib/realtime/clinic-realtime-provider.tsx`
- It owns the **only** `HubConnection` (`createClinicHubConnection()`) and a listener set.
- It starts when `useSession().user` exists. On sign-out or a change of user id it **stops** the connection
  **and calls `queryClient.clear()`**, so user B on a shared reception PC never sees user A's data.
- On `entityChanged(resource)`:
  - It notifies the listeners.
  - It calls `queryClient.invalidateQueries({ predicate: q => q.meta?.realtime?.includes(resource) }, {
    cancelRefetch: false })`.
  - `cancelRefetch: false` matters. Your own save's echo comes back to you, and it must join the fetch your
    success handler already started rather than cancel it and start a second.
- On reconnect: listeners get `undefined` (today's contract) and `invalidateQueries()` runs over everything.
- It keeps the first-connect retry loop (`use-clinic-realtime.ts:51-61`).

**File to modify:** `web/lib/realtime/use-clinic-realtime.ts`
- **Same signature.** The 35 call sites are untouched.
- The body subscribes to the provider's listener set instead of opening a connection.
- If no provider is in scope (login, join, setup), it is a no-op. That is what those pages get today when the
  hub is unreachable (« Additive (AC-5) »).

**Fixes for free:** the header's two sockets, the double socket in `patient-files-directory.tsx:166-167`, the
post-visit popup's socket on phones, and the socket rebuild on every sidebar click.

### 2.3 Shared reference reads (this plan's migration)

| Hook (new or rewritten) | Replaces | Key | `meta.realtime` |
|---|---|---|---|
| `web/lib/hooks/use-user-status.ts` (new) | 8 direct `clinicsApi.getUserStatus()` calls plus `useClinicAccess` and `useDoctors` internals | `["clinics","user-status"]` | `clinics`, `users`, `doctors` |
| `web/lib/hooks/use-procedure-types.ts` (new) | 10 `procedureTypesApi.list()` call sites | `["proceduretypes","all"]` | `proceduretypes` |
| `web/lib/hooks/use-medications.ts` (new) | 3 `medicationsApi.list()` call sites | `["medications","all"]` | `medications` |
| `web/lib/hooks/use-notifications.ts` (rewrite the unread count) | header fetch on every page mount | `["notifications","unread-count"]` | `notifications` |

**Call-site rules**
- A save that changes one of these calls `queryClient.invalidateQueries({ queryKey: [owner] })` in place of its
  own whole-list re-fetch:
  - `procedure-type-form-modal.tsx:93`
  - `stock-item-form-modal.tsx:101`
  - `supplier-form-dialog.tsx:74`
  - `suppliers-table.tsx:173`
- **Error ≠ empty** (`frontend-web.md` § 13).
  - The hooks return `{ data, error, isLoading, isRefetching, refetch }`.
  - A consumer renders its error state when `error && !data`.
  - When a refetch fails with data present, it keeps the data on screen.
  - It never renders « aucun » from `data === undefined`.
- `useDoctors` and `useClinicAccess` keep their **return shapes**, so their consumers do not change.

**`MoneyVisibilityProvider` (`lib/money-visibility/money-visibility-context.tsx`)**
- It reads the mask from `useUserStatus`.
- It keeps its forced `refresh()` (now `refetch()`), its retry-while-unknown timer (`:109`) and its focus and
  visibility re-reads (`:119-120`).
- « Mode discret » must still flip on every poste. The `clinics` broadcast drives it, through `meta.realtime`.

### 2.4 Adjacent defect, fixed in the same change

- `web/app/patients/[id]/page.tsx:1526-1544` (reported by exploration, **confirm first**): a `files` event
  reloads the folders but not the file list, because the effect's dependencies are `[patientId,
  currentFolderId]` only.
- Fix: put that read on a query keyed `["files", patientId, currentFolderId]` with `meta.realtime: ["files"]`.

### 2.5 Derived guards (in `web/scripts/check-responsive.mjs`, next free numbers N45–N47)

| Rule | Asserts |
|---|---|
| N45 `one-hub-connection` | `createClinicHubConnection(` appears only in `clinic-realtime-provider.tsx` (comments masked) |
| N46 `reference-reads-have-one-owner` | `clinicsApi.getUserStatus(`, `procedureTypesApi.list(` with no args, and `medicationsApi.list(` appear only in their hook file. Exemptions (`join/page.tsx`, `setup/page.tsx`, which run before a clinic exists) are listed **by name** in the rule, never inferred |
| N47 `query-cache-cleared-on-sign-out` | `clinic-realtime-provider.tsx` calls `queryClient.clear()` |

---

## Potential pitfalls

- **Browser half-migrated.** A component still calling `getUserStatus()` directly works and shows stale data
  after a change the cache saw. N46 is what catches it.
- **`SubscriptionProvider` is not migrated.** It has its own once-a-minute re-read
  (`subscription-context.tsx:88`), and `clinic-subscription/spec.md:637` forbids anything that could keep a paid
  cabinet locked. It does not go on a `staleTime`.
- **`useAuthToken` / tokens are never queries.** Tokens are re-acquired, not cached (`use-auth-token.ts:13-16`).
- **`staleTime` is a safety net, not the mechanism.** Console verbs (`platform-account --reset-password`,
  `reconcile-money`) and some jobs write without broadcasting. Five minutes plus a refetch on focus bounds that.
- **A 409 recovery reads fresh.** `useConflict`'s « Recharger » must `refetch()` (or `invalidateQueries`) and
  never return the cached copy. That would re-poison the dialog, which is the trap in CLAUDE.md.
- **Request ordering.** `requestIdRef` (`use-appointments.ts:40`, `use-paged-list.ts:69`) stays on the list
  hooks this plan does not migrate. TanStack replaces it only where it now owns the read.
- **Server 1.2.** If a future read tracks a `User` *without* the includes, the guard falls through to the DB.
  That is correct, just not faster. Never remove the guard to « make it hit ».
- **Server 1.3.** The 304 must come **after** the tenant check. An early ETag check before
  `patient.ClinicId == clinic` would confirm the existence of another clinic's file.
- **Server 1.1.** A `DbCommandInterceptor` must be scoped to the request's `DbContext`. The audit appender's
  separate scope (`AuditChainAppender.cs:62-73`) is counted in its own scope, not the request's. That is
  accepted and noted in the log line's meaning.

## Test strategy

**Backend (`BaseOutputPath=<temp> dotnet test … -c Release`, unfiltered)**
- `LoggingBehavior`: one line per request carrying elapsed ms; no « Handling » line.
- `RequestTimingMiddleware`:
  - logs the route template, and never a `?` or `/hub` path (assert on the captured message);
  - slow requests go to `Information`, fast ones to `Debug`.
- Preview query:
  - Matching `IfNoneMatch` → not-modified, and `IFileStorage.DownloadAsync` is **never** called.
  - **Wrong clinic + matching ETag** → « Fichier introuvable », not 304.
  - Stand-in vs fallback key → different ETags.
- `DownloadPatientFileQuery` is unchanged: no ETag, and the ledger is still written.
- `RecoveryCodeLoadingCoverageTests` stays green.
- 1.2 cannot be unit-tested (nothing in `UnitTests` touches a DB). Prove it with the 1.1 log: queries per GET
  drop by 1, and per write by 2.

**If 1.4 runs**
- `GET /api/dashboard` JSON on the dev DB, **before vs after, identical** (trend arrays byte for byte).
- `reconcile-money` exits 0.
- For the index: `verify-schema` diffed before and after; no `xmin` column in the migration.

**Web gate:** `npm run check:responsive` + `npx tsc --noEmit` + `npm run build`.

**Browser** (`/test-in-browser`, one plan, two *separately minted* sessions, never the owner's stored one)

| # | Scenario | Expected |
|---|---|---|
| A1 | Load `/appointments`, read the network panel | `user-status` ×1 (was 6), 1 WebSocket |
| A2 | Open/close/reopen the new-appointment dialog | catalogue fetched once |
| A3 | Sidebar to dashboard, then back | no `user-status` / unread-count refetch inside 5 min; socket not rebuilt |
| B1 | Session A renames an acte; session B has a dialog open | B's picker shows the new name without reload |
| B2 | Session A turns on « Mode discret » | B hides money |
| B3 | Session A uploads a file on patient X; B is on X's Fichiers | file appears (2.4) |
| C1 | Sign out, sign in as another user on the same tab | no flash of the first user's data |
| C2 | Stop the API's network (DevTools offline), then back | lists show the error state, never « aucun »; reconnect refetches |
| C3 | 409 on an edit dialog → « Recharger » | the fresh version loads, and the save goes through |
| D | Regression sweep from the blast-radius table | written before the first edit (`regression-safety.md` § 1) |

**`e2e/` hot-path suite** green; `node e2e/scripts/check-coverage.mjs` green.

## Order of work

1. 1.1 timing, then record the baseline.
2. 1.2 and 1.3, then record « after ».
3. 1.4, only for the rows that crossed their threshold.
4. 2.1 and 2.2 (provider plus one connection). Ship and test this before migrating reads; the gain stands
   alone.
5. 2.3, 2.4 and 2.5.
6. `notes.md` with before/after numbers, plus a pointer line in `CLAUDE.md` (« How the system works »).
