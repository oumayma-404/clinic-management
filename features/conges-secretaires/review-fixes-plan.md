# Review fixes — congés ease of use (2026-11-09)

Source: `reviews/feature-review.md` (40 challenged findings, F1–F40). Owner: fix F1–F33 + suggestions F35, F36, F40,
and **add « Journée · Matin · Après-midi »** for a demi-journée (F39). Skip F34 (cross-year both shares — chosen on
purpose), F37 (refusal codes — DEV-1), F38 (clamp — prescribed pattern). Everything stays **uncommitted**.

Owner's rules: no helper/caption paragraphs under controls (statuses, errors, refusals, empty states may carry text) ·
never state a next step as a fact · never remove a capability for space · one-line code comments, never a finding
number in code · French UI, `quoteFr` for quoted values. Earlier deliberate choices stay: a request beyond the balance is
allowed (it shows negative), « Reste » excludes « En attente », « Solde après » counts the credits a monthly rule adds
by the congé's first day — but now **states that day** (« Il restera 9 j au 6 juillet »).

## Ledger decisions (backend)
- **F1** Uses dated before the first account day (first rule, opening or adjustment) are history (`absorbed: true`),
  never debits.
- **F2** When no rule is in force on 31/12, the next rule's carry choice decides (« perdus » → expire).
- **F4** A congé starting in an earlier year is read on that year's 31/12, before its expiry line.
- **F5** A monthly rule starting in a year already credited by a yearly rule earns nothing until the next 1 January.
- **F6** « Pris » counts every accepted day of the year; `takenInOpening` = the part already inside the opening;
  absorbed lines show in the relevé greyed (`absorbed`), without moving the balance.
- **F9** `lostAtYearEnd` (to take before 31/12 under « perdus »), `lostLastYear`, a `LostLastYear` informational line
  at the top of the new year's relevé; past years readable through the existing `year` parameter.
- **F3/F14** `balanceAfterOn`, `balanceAfterWithPending` on the request DTO and the preview.

## Wire contract — `web/lib/api/team.ts` is already written to it
`StaffLeaveHalfDayPart` ('Morning'|'Afternoon') · request DTO + `halfDayPart`, `balanceAfterOn`,
`balanceAfterWithPending`, `othersPending`, `cancelledByName`, `cancelledBySelf`, `cancelReason` · balance DTO +
`nextRule`, `lostAtYearEnd`, `lostLastYear`, `takenInOpening` · movement kind `LostLastYear` + `absorbed` · team member
+ `nextHalfDayPart`, `pendingDays` · preview + `noOpenDay`, `balanceAfterOn`, `balanceAfterWithPending`, `overlap` ·
input + `halfDayPart` · `cancel(id, version, reason?)` (reason required for an admin cancelling someone else's; an
admin may cancel any accepted congé/absence whatever its date) · `recordAbsence(…, countedDays?)` ·
`GET /team/users/{id}/leave-rule-preview?days&accrual&carryOver&since` → balance as if saved · refusal
`leave_no_open_day` for a 0-day request · migration: `StaffLeaveRequests.HalfDayPart` int null.

## Who owns what (parallel work — disjoint files)

| Owner | Files | Findings |
|---|---|---|
| Backend agent | `api/**` | F1–F6, F9 (server), F10, F11 (`nextRule`), F12 (motif), F13 (e-mail wording), F14, F16 (bell/e-mail wording), F17, F19 (`pendingDays`), F22, F26 (overlap in preview), F27, F29 (bell/e-mail date style), F36, F39 (column + notices), F7 (rule preview endpoint) |
| FE « Mes congés » agent | `web/app/conges/**`, `web/components/staff-leave/{staff-leave-balance-card,staff-leave-request-dialog,staff-leave-statement,staff-leave-month-calendar,staff-leave-bits}.tsx`, `web/lib/staff-leave.ts`, `web/lib/hooks/use-staff-leave.ts` | F3, F6 (card), F8, F9 (statement year stepper, LostLastYear line, « À prendre avant le 31/12 »), F11 (balance card), F13, F14, F15, F16, F17 (bits), F20/F22 (the request dialog in admin mode: a secretary picker + « Jours à décompter »), F23 (request dialog), F26, F27, F28, F29 (bits/statement/calendar labels), F33, F39 (dialog + calendar), F40 (pill on `Badge`) |
| FE doctor agent | `web/components/staff-leave/{staff-leave-account-card,staff-leave-admin-panel,staff-leave-decision-dialogs,staff-leave-request-list}.tsx`, `web/app/users/**` (congé parts), `web/components/team-modules-card.tsx` (congés part only), `web/components/dashboard/**` (pending-congés link), `web/lib/guides/**` (a congés tour) | F6 (file boxes), F7 (rule dialog default + live result), F10, F11 (rule line), F12, F17 (cards), F18, F19, F20 (button in the Congés tab), F21, F23 (decision + rule dialogs), F24, F25, F29 (cancel confirm wording), F30, F31, F32, F35, F36, F39 (cards say Matin/Après-midi), F40 (team strip `Progress`) |

## Blast radius (must re-test rows go to the QA plan's tier D)

| # | Touching | Other consumers | Verdict |
|---|---|---|---|
| 1 | `StaffLeaveLedger.Fold` | balance card, relevé, « Solde après », team strip, decision e-mail | must change together — one fold, all readers |
| 2 | `StaffLeaveRequestDto` (+ fields) | /conges, Congés tab, file, bell rows | must change — FE reads the new fields |
| 3 | `staffDaysLabel` / day wording (`lib/staff-leave.ts`) | every congé surface | must re-test |
| 4 | Migration `HalfDayPart` | demo DB only; verify-schema | must re-test |
| 5 | `CancelStaffLeaveRequestCommand` (admin any date + motif) | secretary self-cancel rules (unchanged) | must re-test both |
| 6 | `staff-leave-statement.tsx` (year stepper) | /conges and the file | must re-test both |
