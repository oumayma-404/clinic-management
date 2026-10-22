# Performance — what shipped, and the numbers behind it

## Part 1 — server (measured 2026-10-05)

**Method.** Two API instances side by side on the dev database, same build config (Debug), both with the timing
middleware: **before** = this branch with `UserRepository`, `DownloadPatientFilePreviewQuery` and
`PatientFilesController` reverted to `HEAD`; **after** = the branch. Requests alternate before/after, 9 rounds
per read after one warm-up each, medians. Clinic « Ibn Khaldoun »: 2 229 patients, 984 appointments, 798
invoices. Admin account, `Diagnostics__SlowRequestMs=0`.

⚠️ **Alternating is the method, not a nicety.** A first back-to-back run (before, restart, after) measured the
*after* side 2–6× **slower** on every read, including reads the change cannot touch — the instance had just
cold-started while Rider and two build servers held the CPU. Only an interleaved run puts the same noise on both
sides.

| Read | Server ms before → after | SQL queries | DB ms |
|---|---|---|---|
| Dashboard (month) | 186 → 171 | 55 → 54 | 115 → 112 |
| Agenda, busiest day (106 RDV) | 48 → 39 | 5 → 4 | 22 → 17 |
| Agenda, that week (268 RDV) | 69 → 64 | 5 → 4 | 25 → 21 |
| Clinic info (`user-status`) | 12 → 9 | 4 → 3 | 9 → 6 |
| Notification count | 13 → 9 | 3 → 2 | 11 → 7 |
| Patient record | 12 → 9 | 3 → 2 | 9 → 6 |
| Patient balance | 16 → 11 | 5 → 4 | 13 → 9 |
| Patient fiches | 24 → 23 | 9 → 8 | 19 → 18 |
| Thumbnail | 20 → 16 | 4 → 3 | 10 → 7 |
| Thumbnail, unchanged (`If-None-Match`) | — → **14 ms, 304, 0 bytes** | — | — |

- **1.2 (account once): −1 query on every read**, measured, not inferred. On the small reads that is 25–30 % of
  the server time, because the account lookup was a large share of them.
- **1.3 (preview ETag):** a second view of a thumbnail answers 304 with no body. The check still runs (3 queries).
- **Recovery codes through the reused account:** « Sécurité » reported 8 unused, the database holds 8
  (`UserRecoveryCodes`, `UsedAt IS NULL`). Consuming or regenerating a code was **not exercised** — those spend
  the QA admin's second factor.

## 1.4 — both gates stayed closed

| Gate | Threshold | Measured | Decision |
|---|---|---|---|
| Dashboard rewrite | median ≥ 300 ms | 171–186 ms (cold, first run: 261 ms) | **not done** |
| Agenda index `(ClinicId, AppointmentDateTime)` | ≥ 50 ms in SQL, or a costly seq scan | `EXPLAIN ANALYZE`: seq scan over 994 rows, **1.7 ms** | **not done** |

- The dashboard is still the slowest read by 3× and the only one over 100 ms; its 55 queries are mostly the
  fixed six-month loops, so the count does not grow with the clinic — the time per query does.
- Under a busy machine it reached **1.5 s** (the cold run above). That is the case for a clinic's own Windows PC
  doubling as a reception workstation; re-measure there before deciding it never matters.
- The agenda's seq scan is the planner's correct choice at this size. Re-check at ~20 000 appointments.

## Measurement traps (each one cost a run)

- **A directly-launched `ClinicManagement.API.exe` writes its console in the OEM code page**, so `→` and `·` in
  the timing line arrive as `\032` and `\372`. The log *file* is UTF-8; `dotnet run` output was fine. A parser
  keyed on the literal arrow sees zero lines and reports nothing — not « no requests ».
- **A second instance must share the main API's Data Protection ring** (`DataProtection__KeyRingPath` → the main
  checkout's `api/ClinicManagement.API/.local/dataprotection-keys`). The path is relative to the process's
  working directory, and the ring also encrypts the admins' **TOTP secrets** — with its own ring the instance
  cannot sign the admin in, and its startup backfill would encrypt Google tokens the main API cannot read.
- **Disable the vendor console on a second instance** (`Console__Port=0`) — `Console:Port=5443` in the dev
  config is otherwise bound twice.

## Part 2 — browser (measured 2026-10-05, interleaved against the old code on the same database)

| What | Old code | New code |
|---|---|---|
| Live connections opened by loading `/appointments` | 4 | **1** |
| Live connections opened by loading a patient page | 7 | **1** |
| Live connections on a sidebar click | rebuilt | **0** |
| Live connection attempts on `/login` | every 5 s | **0** |
| `/clinics/user-status` reads on an `/appointments` load | 8 | **1** |
| `/procedure-types` reads over two « Nouveau » dialog opens | 2 | **≤ 1** |
| `user-status` + bell re-reads on a sidebar click | each | **0** |

**What is cached, and what deliberately is not.** One TanStack Query cache, in memory, per tab: the clinic status,
the act and medication catalogues, the bell's count. **Not** lists, records or money — a stale page there is a
wrong figure, and the subscription read keeps its own once-a-minute re-read because `clinic-subscription/spec.md`
forbids a cached refusal. Freshness is the broadcasts (`meta.realtime` → invalidate); the 5-minute stale time is a
net for the writers that do not broadcast (console verbs, some jobs).

**Decisions that are easy to undo by accident**
- **`hasClinic: false` is never served from cache.** Right after `/setup` or `/join` it is the stale answer, and a
  cached one would send a new cabinet straight back to `/setup`.
- **The cache is cleared on every change of identity** (N47) — a shared reception PC.
- **Forms that copy the status into editable state read it fresh** (`useFetchUserStatus`): a live cached read would
  re-seed clinic settings under the user's typing when a colleague saves.
- **Every cached query names its broadcasts** (N48), or a colleague's change is invisible for 5 minutes with no
  error.

**Measurement traps**
- **Playwright disables the browser's HTTP cache as soon as any `route()` is registered**, so a walk that stubs a
  request (the usual way past the post-visit pop-up) cannot test caching at all. These walks register none.
- **CDP reports no `webSocketClosed` for a socket torn down by a full navigation**: count sockets *created per
  load*, never an « open » set.
- **At desktop width the agenda's button is « Nouveau »**; `aria-label="Nouveau rendez-vous"` is the phone's « + ».
