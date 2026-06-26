# Feature Review: live-dashboard

**Status:** RESOLVED
**Challenged:** Yes (all 7 findings challenged before fixing)

## Resolution (2026-06-25)
- **Fixed #1** — added root `.gitignore` (`bin/`, `obj/`, logs) so the new test project's artifacts aren't committed. (Pre-existing 154 tracked bin/obj left as-is — out of scope.)
- **Fixed #2** — handler now defaults to a real Monday-based current week (`StartOfWeek` helper) instead of next-7-days.
- **Fixed #3** — frontend hook passes `{ weekStartsOn: 1 }` to `startOfWeek`/`endOfWeek`.
- **Fixed #4** — today & this-week counts exclude `Cancelled`/`NoShow` (new `excludeStatuses` repo param); the today list filters them out too. (User decision: exclude both.)
- **Deferred #5** — UTC-vs-local "now": needs an app-wide timezone strategy; fixing only here would desync counts from the appointments list. Separate ticket.
- **Skipped #6, #7** — match existing project conventions (Result→BadRequest; sequential queries required by non-thread-safe DbContext).

Quality after fixes: `dotnet build` 0 errors / no new warnings; unit tests 5/5 pass; frontend typecheck clean for feature files.


**Date:** 2026-06-25
**Parent Branch:** main
**Merge Base:** ab1beee
**Files Reviewed:** 13 feature files + new ClinicManagement.UnitTests project (~370 lines added). Changes are uncommitted in the working tree (mixed with unrelated pre-existing edits); review scoped to live-dashboard files only.

> **Agents skipped — reviewed inline.** The diff is small and was fully in working memory. The skill's four parallel agents are hard-coded with ROP/`Extensions.ROP` and Anakin-specific mandates that do not exist in this project (which uses MediatR `Result<T>`, EF repositories, `IClinicContext`). Running them would yield false findings, so all four mandates (Code Quality, error-handling/Result instead of ROP, Business Logic, Breaking Changes) were applied inline against this repo's actual conventions.

## Findings

### Finding 1
- **Severity:** Major
- **Category:** Code Quality
- **File:** api/ClinicManagement.UnitTests/ (bin/ + obj/)
- **Line:** n/a (new project)
- **Comment:** The new test project's `bin/` and `obj/` directories (~150 build artifacts: `Moq.dll`, `xunit.*.dll`, `testhost.exe`, etc.) are untracked and would be committed since there is no `.gitignore`. This bloats the repo and the PR. Add a `.gitignore` (at repo root or `api/`) covering `bin/`, `obj/`, or at minimum stage only source files (`*.cs`, `*.csproj`) when committing. (Note: the repo already tracks some `bin/obj` — a pre-existing bad practice — but the new project is a chance to avoid adding ~150 more binaries.)

### Finding 2
- **Severity:** Minor
- **Category:** Business Logic
- **File:** api/ClinicManagement.Application/Features/Dashboard/Queries/GetDashboardStatsQuery.cs
- **Line:** 62-64 (default `weekStart`/`weekEnd`)
- **Comment:** When the client omits the week range, the fallback is `weekStart = now.Date; weekEnd = weekStart.AddDays(7)` — i.e. "next 7 days from today", not the current calendar week. The frontend always passes `startOfWeek`/`endOfWeek`, so this is latent today, but any other caller (Swagger, a future consumer) would get a misleading "This Week" count. Either compute a proper current-week default server-side or document that the range params are required.

### Finding 3
- **Severity:** Minor
- **Category:** Business Logic
- **File:** web/lib/hooks/use-dashboard-stats.ts
- **Line:** 24-25 (`startOfWeek`/`endOfWeek`)
- **Comment:** `date-fns` `startOfWeek`/`endOfWeek` default to **Sunday** as the first day of the week. For a Tunisia/French-market clinic the working week typically starts Monday, so "This Week" may bucket days unexpectedly (and won't match a user's mental model). Pass `{ weekStartsOn: 1 }` to both (and keep it consistent if other week calculations are added later).

### Finding 4
- **Severity:** Minor
- **Category:** Business Logic
- **File:** api/ClinicManagement.Application/Features/Dashboard/Queries/GetDashboardStatsQuery.cs
- **Line:** 70-77 (today / this-week counts)
- **Comment:** `todaysAppointments` and `thisWeekAppointments` count **all** statuses, including `Cancelled` and `NoShow`. Depending on intent, a cancelled appointment arguably shouldn't inflate "Today's Appointments". The same applies to the dashboard list (`appointment-list.tsx` renders every status). Consider excluding `Cancelled`/`NoShow` from these counts (and/or visually de-emphasizing them in the list) — or confirm that "total scheduled incl. cancelled" is the intended definition.

### Finding 5
- **Severity:** Minor
- **Category:** Business Logic
- **File:** api/ClinicManagement.Application/Features/Dashboard/Queries/GetDashboardStatsQuery.cs
- **Line:** 60, 72-73 (`now = DateTime.UtcNow` for pending vs client-local today/week ranges)
- **Comment:** `upcomingPending` filters on `AppointmentDateTime >= DateTime.UtcNow` (true UTC), while today/week ranges come from the client as local wall-clock times sent without offset (the existing app convention, treated as UTC by the global converter). This mixes two "now" notions. It's an inherited app-wide UTC/local fuzziness (the appointments page has the same), and counts still match the list (the feature's goal), so impact is low — but worth a comment/normalization when the app eventually fixes timezone handling project-wide.

### Finding 6
- **Severity:** Suggestion
- **Category:** Code Quality
- **File:** api/ClinicManagement.API/Controllers/DashboardController.cs
- **Line:** 39-42 (`return BadRequest(result.Error)`)
- **Comment:** A stats-retrieval failure (e.g. DB error, user not resolvable) is a server-side problem but is surfaced as `400 BadRequest`. This matches the existing project convention (`AppointmentsController` does the same), so it's consistent — but semantically a `500`/`Problem()` would be more accurate for non-client errors. Optional; only worth changing if the team revisits the Result→HTTP mapping globally.

### Finding 7
- **Severity:** Suggestion
- **Category:** Code Quality
- **File:** api/ClinicManagement.Application/Features/Dashboard/Queries/GetDashboardStatsQuery.cs
- **Line:** 66-80 (five sequential awaits)
- **Comment:** The handler issues five sequential DB round-trips. They **must** stay sequential (the EF `DbContext` is not thread-safe, so do NOT parallelize them on the shared context), but if dashboard load latency becomes a concern they could be reduced — e.g. combine the three appointment counts into a single grouped query, or accept the current approach as fine for typical clinic data volumes. Acceptable as-is; noting for future optimization.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 1 |
| Minor | 4 |
| Suggestion | 2 |
| **Total** | 7 |
