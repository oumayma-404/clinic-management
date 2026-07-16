# Feature Review: appointment-scheduling-ux

**Status:** COMPLETE
**Challenged:** Yes
**Date:** 2026-07-16
**Challenged Date:** 2026-07-16
**Parent Branch:** feature/windows-desktop-app (feature lives in the working tree, not in commits)
**Merge Base:** n/a — working-tree review (all four feature files are uncommitted `??`/` M`; a merge-base diff shows nothing for them)
**Files Reviewed:** 4 files — `web/lib/hooks/use-appointment-overlap.ts` (new, 91 lines) + `appointment-calendar.tsx`, `create-appointment-dialog.tsx`, `edit-appointment-dialog.tsx` (modified, +431/-280)

## Challenge Summary

| Metric | Count |
|--------|-------|
| Original findings | 3 |
| Confirmed | 2 |
| Confirmed (adjusted) | 1 |
| Dismissed (false positive) | 0 |
| Dismissed (pre-existing) | 0 |
| **Final findings** | 3 |

All three findings verified against the full source (not just the diff). No dismissals. Finding 1's cited line was re-anchored from 310 to 312 (the `movedToPast` definition it describes; line 310 is the `buildAppointmentDateTime()` call feeding it).

## Findings

### Finding 1
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed (adjusted — line re-anchored 310 → 312)
- **File:** web/components/edit-appointment-dialog.tsx
- **Line:** 312
- **Anchor:** `EditAppointmentDialog.handleUpdate` (`movedToPast`)
- **Comment:** AC-2 says "editing an already-past appointment without changing its time does not nag." `movedToPast` compares `buildAppointmentDateTime().getTime() !== originalStart.getTime()`, but `buildAppointmentDateTime` zeroes seconds/ms (`setHours(h, m, 0, 0)`, line 124) while `originalStart = parseISO(appointment.appointmentDateTime)` (line 311) keeps the stored seconds. So an already-past appointment whose stored start has non-zero seconds (e.g. one imported via Google→App sync) reports `movedToPast === true` on save even when the user changed nothing — a spurious past-time confirmation. Narrow in practice (in-app appointments are minute-granular / :00 seconds), but a precise deviation from the AC. Fix: compare on minute granularity — normalize `originalStart` with `setSeconds(0,0)` before the `getTime()` comparison, or compare the date + hour + minute fields directly.
- **Challenge note:** Verified against source: `buildAppointmentDateTime` (line 121–126) zeroes seconds; `originalStart` (line 311) is a raw `parseISO`. The defect is real. Severity Minor retained (in-app data is minute-granular so the trigger requires externally-sourced sub-minute starts). Line corrected 310 → 312 to point at the `movedToPast` comparison rather than the `buildAppointmentDateTime()` call.

### Finding 2
- **Severity:** Suggestion
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** web/components/create-appointment-dialog.tsx
- **Line:** 371
- **Anchor:** `CreateAppointmentDialog.handleSubmit`
- **Comment:** The past-time guard uses `appointmentDateTime.getTime() < Date.now()` where `buildAppointmentDateTime` zeroes seconds. Booking the *current* minute (e.g. it's 14:30:45 and the user books 14:30) yields `14:30:00 < 14:30:45` → the confirmation dialog fires for what the user perceives as "now". Technically matches the AC-2 wording ("earlier than the current moment"), but is minor UX friction. If undesired, floor `Date.now()` to the start of the current minute before the comparison so the current slot isn't treated as past.
- **Challenge note:** Verified at line 371 (`if (appointmentDateTime && appointmentDateTime.getTime() < Date.now())`). Accurately described; kept as a Suggestion (behavior conforms to the literal AC-2 wording, so this is an optional UX polish, not a defect).

### Finding 3
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** web/lib/hooks/use-appointment-overlap.ts
- **Line:** 7
- **Anchor:** `parseDurationToMinutes`
- **Comment:** `parseDurationToMinutes` is now duplicated a third time — identical copies live in `appointment-calendar.tsx` (line 35) and `edit-appointment-dialog.tsx` (line 50). This new hook is a good moment to extract the single helper to a shared util (e.g. `web/lib/utils.ts` or a small `web/lib/appointments.ts`) and import it in all three places. DRY; the comment on line 6 ("Mirrors the calendar's helper") already acknowledges the duplication.
- **Challenge note:** Verified all three duplication sites: `use-appointment-overlap.ts:7`, `edit-appointment-dialog.tsx:50`, `appointment-calendar.tsx:35`. Confirmed as a Suggestion.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 0 |
| Minor | 1 |
| Suggestion | 2 |
| **Total** | 3 |
