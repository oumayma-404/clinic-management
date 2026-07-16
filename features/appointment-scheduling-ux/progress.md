# Progress: Appointment Scheduling UX Enhancements

**Started:** 2026-07-16
**Type:** Small
**Branch:** feature/windows-desktop-app (user chose to use the current branch; unrelated prior work excluded from this feature's commits)

## Status
- [x] Implementation
- [x] Quality checks (typecheck, build) — `npx tsc --noEmit` clean; `npm run build` ✓ compiled successfully
- [x] Tests (handled by /test-small-feature) — no FE test harness in repo; ACs covered by the typecheck/build gate + manual browser verification (see Test Plan below)

## Test Plan
This is a **Scope: FE**, client-only feature. The `web/` project has **no test framework** — no vitest/jest/playwright/testing-library in `package.json`, no `*.test.*`/`*.spec.*`/`*.feature` files, and ESLint is not even installed (deliberate repo choice, see `features/LEARNINGS.md`). There is therefore no unit-test surface for the React component behavior these ACs describe (initial scroll position, overlap detection, past-time confirmation dialog, block rendering geometry, combobox filtering). Per `/test-small-feature` guidance for FE-only changes with no FE harness, each AC is **accounted for via coverage notes**, not a contrived test — standing up a full vitest+RTL+jsdom stack is out of scope for a small feature and would reverse a deliberate repo decision.

| AC | Coverage | Notes |
|----|----------|-------|
| AC-1 (center-on-now scroll) | Typecheck/build gate + manual | Runtime scroll positioning; no static-analysis assertion possible without a DOM harness. Verify in browser: load `/appointments` on a day/week containing today → now-line centered; on a past/future day → 8 AM. |
| AC-2 (past-time confirm dialog) | Typecheck/build gate + manual | Create & edit dialogs compile; behavior (submit past time → confirm dialog; edit only when start *moved* to past) is manual/browser. |
| AC-3 (inline overlap warning) | Typecheck/build gate + manual | `use-appointment-overlap.ts` typechecks; amber inline warning, cancelled-ignored, Occupé-counts, edit-excludes-self are manual/browser. |
| AC-4 (single-block rendering) | Typecheck/build gate + manual | Absolute-overlay geometry (once per appt, proportional height, min-height) is visual — manual/browser. |
| AC-5 (searchable patient combobox) | Typecheck/build gate + manual | `cmdk` combobox compiles; type-to-filter + selection parity with old Select is manual/browser. |

**Gate results (re-run this session):**

## Tests Run
| Suite | Command | Result |
|-------|---------|--------|
| Typecheck | `npx tsc --noEmit` (in `web/`) | Exit 0 — clean |
| Build | `npm run build` (in `web/`) | ✓ compiled successfully; all routes built (`/appointments` 36.9 kB) |
| FE unit/integration | — | N/A — no FE test framework in repo (see Test Plan) |
| Postman/Newman | — | Skipped (user preference) |
| E2E | — | Skipped (no E2E harness; small feature) |

## Working tree note (start of session)
Pre-existing uncommitted/untracked changes unrelated to this feature (exclude from commits):
- `.gitignore`, `api/ClinicManagement.API/ClinicManagement.API.csproj`, `api/ClinicManagement.API/appsettings.json`
- `define-small-feature-prompt.md`, `features/LEARNINGS.md`
- `features/notification-center/**` (progress.md, retrospective.md, reviews/feature-review.md)
- `packaging/server/clinic-server.iss`, `web/Dockerfile`
- `CLINIC-FEATURES-OVERVIEW.md`

## Files Changed
- `web/lib/hooks/use-appointment-overlap.ts` (new) — advisory overlap-detection hook (AC-3): fetches the selected day's appointments once per day, returns a French warning naming the first conflict; fetch failure silently disables; cancelled ignored; busy ("Occupé") slots count.
- `web/components/appointment-calendar.tsx` — AC-1 (center-on-now initial scroll, else 8 AM) + AC-4 (each appointment rendered once via an absolute overlay, positioned by start minute, sized by duration with a minimum height). Removed obsolete `getAppointmentsForHourSlot`/`getAppointmentStyle` and now-unused date-fns imports.
- `web/components/create-appointment-dialog.tsx` — AC-2 (past-time confirmation), AC-3 (inline overlap warning), AC-5 (searchable patient combobox replacing the plain Select).
- `web/components/edit-appointment-dialog.tsx` — AC-2 (past-time confirmation, only when start is *moved* to a past time), AC-3 (inline overlap warning excluding the edited appointment).

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Overlap warning + past-time dialog text written in French | Matches surrounding user-facing clinic text ("Créneau occupé", "non synchronisé", connectivity messages); app is Tunisia-targeted. Internal-only wording choice, spec left it generic. |
| Reordered `isCurrentTimeVisible` memo above the initial-scroll effect | Required so the effect can list it as a dependency without a TDZ error. Same behavior. |
| Removed pre-existing unused date-fns imports (`setHours`/`setMinutes`/`isSameDay`) left dead by the rendering rework | Scout-rule cleanup within the file being rewritten; no behavior change. |

## Significant Deviations
None.

## Notes
- FE quality gate is `npx tsc --noEmit` + `npm run build` (no vitest/jest, ESLint not installed — see features/LEARNINGS.md). Postman/Newman never run per user preference.
- Week-view overlay columns are positioned with `calc()` against the scroll container; a scrollbar-width right-edge offset is possible (same containing-block semantics as the existing current-time line) — cosmetic, to confirm in manual verification.
