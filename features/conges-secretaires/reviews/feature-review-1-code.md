# Feature Review: conges-secretaires

**Status:** COMPLETE
**Challenged:** Yes
**Date:** 2026-11-05
**Challenged Date:** 2026-11-05
**Parent Branch:** main
**Merge Base:** 3262d2e0 (= HEAD; feature is uncommitted — reviewed the working tree, diff assembled with `git add -N api web` then index restored)
**Files Reviewed:** 75 changed files (+6234 lines, −18 lines) — backend 3004 diff lines, frontend 2797, tests 1411. Excluded: migration `.Designer.cs`, `ApplicationDbContextModelSnapshot.cs`, `features/**`, `CLAUDE.md` (read from the repo by the migration reviewer instead).
**Review method:** 5 parallel agents adapted to this repo — Code Quality, Error Handling (MediatR + `Result<T>`, not ROP), Business Logic (spec AC-1…AC-15), Breaking Changes (migration vs snapshot/configs, mirrored sets, archive/relay scopes), Device & UX (frontend-web.md + DEVICE-CONTRACT.md). Out of scope told to all: pointage (parts 2–3), DEV-1/2/3, the kept PageHeader/Tabs.
**Note on main:** origin/main is 7000b0e0, **26 commits ahead** of the merge-base, with 4 migrations this branch does not have (verified with `git rev-list` / `git diff --name-only`).

## Challenge Summary

| Metric | Count |
|--------|-------|
| Original findings | 50 |
| Confirmed | 29 |
| Confirmed (adjusted) | 19 (3 of them merged into one finding) |
| Dismissed (false positive) | 1 |
| Dismissed (pre-existing) | 1 |
| **Final findings** | 46 |

**Dismissed:**
- Original #15 (an admin may decide her own request) — false positive: needs a secretary promoted while a request waits, and in the usual one-admin cabinet the proposed refusal would leave that request undecidable for good.
- Original #47 (one `ClinicDtoMapper` instead of seven `new ClinicDto`) — pre-existing: the seven hand-written sites predate this branch, and the spec itself asks for « all 7 sites ».

**Merged:** originals #2, #3 and #10 (reads after the commit inside the catch-all — Decide, Cancel, allowance) are one root cause and one fix → Finding 6.

## Findings

### Finding 1
- **Severity:** Major
- **Category:** Breaking Change
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Migrations/20261105114746_AddStaffLeaveRequests.cs
- **Line:** 1
- **Anchor:** `AddStaffLeaveRequests` / `ApplicationDbContextModelSnapshot.BuildModel`
- **Comment:** The branch sits on 3262d2e0; main has since gained `AddOnboardingSignInColumns`, `AddProductFeedback`, `AddStaffInvitations`, `AddFeedbackOtherAndTickedSteps`. This migration's Designer and the snapshot lack `ProductFeedback`, `StaffInvitation`, `UserFeedbackPrompt`, three new `User` columns and `TickedStepsCsv`. Both sides edited the snapshot, so a one-sided conflict resolution silently loses a model and the next `migrations add` duplicates tables. Ten other files changed on both sides (`User.cs`, `ApplicationDbContext.cs`, `user-management.tsx` +345 on main, `dashboard-header.tsx`, `clinic-settings.tsx`, `types.ts`…), and main's « Votre secrétaire » guide puts `data-guide` marks inside `UserManagement`, which now renders inside a tab. Fix: rebase on origin/main, delete this migration + Designer, take main's snapshot, re-scaffold (strip `xmin` by hand), `verify-schema` before/after, re-run the full suite + `check:responsive` + `tsc` + `next build`, re-walk `/users` incl. the guide.

### Finding 2
- **Severity:** Major
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/staff-leave/staff-leave-admin-panel.tsx
- **Line:** 140
- **Anchor:** `StaffLeaveAdminPanel` › history `ModeSegmented`
- **Comment:** At 320–375 px the four `flex-1` options can only shrink to their longest word (« Acceptées »), so the control is wider than the 288 px box and `<main>` scrolls sideways — the horizontal scrollbar is visible in `qa/shots/C3-320-admin.png`. QA C3 passed because `walk.mjs` measured `document.documentElement.scrollWidth`, while `<main>` is the scroller (a probe bug). Fix: `className="grid w-full grid-cols-2 gap-1 sm:flex sm:w-auto"` (the request dialog's pattern), and make `walk.mjs` measure `#contenu-principal`'s `scrollWidth − clientWidth`.

### Finding 3
- **Severity:** Major
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/staff-leave/staff-leave-request-list.tsx
- **Line:** 178
- **Anchor:** `StaffLeaveDecisionCards` › footer
- **Comment:** At 768–~880 px (iPad portrait) the rail is `w-64`, so `md:grid-cols-2` gives ~252 px cards, while `sm:flex` puts Annuler + Refuser + Accepter (~266 px+) on one row; with `justify-end` « Annuler » starts before the card's edge — clipped at `<main>`'s edge in the left column, over the neighbour in the right. The team strip (`staff-leave-admin-panel.tsx:114`, `sm:grid-cols-2`) breaks names mid-word at the same width. Fix: a container query (`@container` + `@sm:flex`) or `xl:grid-cols-2`.

### Finding 4
- **Severity:** Major
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/app/conges/page.tsx
- **Line:** 122
- **Anchor:** `CongesPage` › fixed « Demander un congé » CTA
- **Comment:** Below 640 px the fixed 48 px CTA sits 12 px above the bottom bar, and `AppShell` has no runway (`app-shell.tsx:151` says to re-add one when something floats); `<main>`'s `p-4` clears 16 px only. The last ~44 px — « Prochain congé »'s date line or the calendar legend — can never be scrolled clear. Fix: `contentClassName="space-y-6 pb-16 sm:pb-0"`.

### Finding 5
- **Severity:** Major
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/staff-leave/staff-leave-decision-dialogs.tsx
- **Line:** 262
- **Anchor:** `RejectLeaveDialog` (and `AcceptLeaveDialog`, 145)
- **Comment:** `onOpenChange` is passed straight through: a typed « Motif du refus » (or an edited « Jours à décompter ») is discarded without asking on Escape, outside tap, ✕ or Android back. The request dialog already uses `useDirtyGuard` + `DiscardChangesDialog`; do the same here, `markClean()` on success.

### Finding 6
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed (adjusted — was Major ×2 + Minor, merged)
- **File:** api/ClinicManagement.Application/Features/Team/Commands/DecideStaffLeaveRequestCommand.cs
- **Line:** 111
- **Anchor:** `DecideStaffLeaveRequestCommandHandler.Handle` · `CancelStaffLeaveRequestCommand.cs:90` · `SetLeaveAllowanceCommand.cs:72-73`
- **Comment:** In all three handlers a read runs after `SaveChangesAsync` inside the catch-all (the roster in Decide/Cancel; the clinic + history in the allowance). If it throws, a saved write is reported as « Erreur lors de l'enregistrement… », no `team` broadcast goes out (failure result), and in Decide/Cancel the bell row/e-mail are skipped and a retry is refused as « déjà reçu une réponse ». Fix: move those reads before `SetExpectedVersion`/`SaveChangesAsync`, as `RecordStaffAbsenceCommandHandler` already does at line 72.
- **Challenge note:** Merged originals #2, #3, #10 (one root cause, one fix). Severity lowered to Minor — it needs a database failure in the milliseconds between a commit and a one-row read; the stored data stays right.

### Finding 7
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** api/ClinicManagement.Application/Features/Team/Commands/SetLeaveAllowanceCommand.cs
- **Line:** 56
- **Anchor:** `SetLeaveAllowanceCommandHandler.Handle` (+ `ListStaffLeaveRequestsQuery`, `GetTeamLeaveOverviewQuery`, `GetStaffFileQuery`, `GetMyStaffLeaveQuery`, `PreviewStaffLeaveQuery`)
- **Comment:** AC-1: while the switch is off « the API refuses ». Only Submit/Decide/Cancel/RecordAbsence check `LeaveRequestsEnabled`; the allowance write and five reads answer normally. The UI matches: « Fiche » in `user-management.tsx` (582, 693) and `/users/[userId]` show the history and the « Droit annuel » editor with the module off (only « Ajouter une absence » is gated, page line 63). Fix: return `ModuleOff` from these handlers and hide the congé part of the file + the allowance editor when off.
- **Challenge note:** Severity lowered to Minor — the four writes that create or decide a congé are refused; what stays open is admin-only reads of the cabinet's own data and one allowance number.

### Finding 8
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** api/ClinicManagement.Application/Features/Team/StaffLeaveRules.cs
- **Line:** 125
- **Anchor:** `StaffLeaveRules.ShareInYear`
- **Comment:** A cross-year count is split by each year's share of open days. The spec's own example breaks it: 1 January is always a jour férié and AC-7 has the admin remove it in « Décompter ». Wed 30 Dec → Sat 2 Jan, 4 asked, 3 counted → real split 2026 = 2, 2027 = 1; the code gives 1.5 / 1.5 (and `StaffLeaveRulesTests.A_Split_Rounds_To_The_Half_Day…` pins 1.5). Cheapest fix: leave 1 January out of the open-day counts inside `ShareInYear` (fixed national holiday) — that gives 2 / 1 for this case with no schema change; a stored per-year count is the general fix.
- **Challenge note:** Severity lowered to Minor — only a congé spanning the new year whose count was reduced, and the error is half a day per year.

### Finding 9
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/Commands/CancelStaffLeaveRequestCommand.cs
- **Line:** 81
- **Anchor:** `CancelStaffLeaveRequestCommandHandler.Handle`
- **Comment:** `!leave.HoldsItsDays` answers « Cette demande a déjà reçu une réponse. » for an already **Cancelled** request too (second tab) — the wrong state is named. Fix: own sentence (« Cette demande est déjà annulée. ») or idempotent success; keep `NotPending` for `Rejected`.

### Finding 10
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/StaffLeaveNotices.cs
- **Line:** 28
- **Anchor:** `StaffLeaveNotices.Rejected` / `Email`
- **Comment:** AC-9: the bell row and e-mail name the dates **and the days**. The refused versions name no day count (bell line 29; e-mail details 64-67: Cabinet, Type, Dates, Motif). Fix: add `FrenchDayPeriod.Days(leave.RequestedDays)` to both.

### Finding 11
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Domain/Entities/StaffLeaveRequest.cs
- **Line:** 134
- **Anchor:** `CanBeCancelledOn` / `CancelStaffLeaveRequestCommandHandler` (line 77)
- **Comment:** The owner check lets a secretary withdraw an absence the **admin recorded** (`EnteredByAdmin`) before it starts — e.g. an imposed « Sans solde » week; the admins are notified, but only after it is gone. Fix: refuse the owner's cancel when `EnteredByAdmin`, and set `CanCancel` to match.

### Finding 12
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/Commands/RecordStaffAbsenceCommand.cs
- **Line:** 87
- **Anchor:** `RecordStaffAbsenceCommandHandler.Handle` → `StaffLeaveRules.OverlapSentence` (StaffLeaveRules.cs:35)
- **Comment:** `FindHoldingOverlapAsync` returns a waiting **or** accepted request, but the sentence always says « … a déjà un congé du … », which reads as accepted; the admin is not told to answer the waiting request instead. Fix: put the status in the sentence (« une demande en attente » vs « un congé accepté »).

### Finding 13
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** web/components/staff-leave/staff-leave-admin-panel.tsx
- **Line:** 130
- **Anchor:** `StaffLeaveAdminPanel` (pending section) / `useLeaveDialogs`
- **Comment:** The dialogs are mounted inside `StaffLeaveDecisionCards`; when another admin decides the **last** waiting request, the `team` refresh swaps the cards for `EmptyState` and the open dialog vanishes with no message. `StaffLeaveRows` on `/conges` does the same when a row moves from « à venir » to « Historique ». Fix: mount the dialogs once at panel/page level.

### Finding 14
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** web/components/staff-leave/staff-leave-request-list.tsx
- **Line:** 164
- **Anchor:** `StaffLeaveDecisionCards` (« Solde après »), also `staff-leave-request-dialog.tsx:229` (« Il restera »)
- **Comment:** Both figures are for the congé's **start** year but don't name it, while the team strip and the balance card show the current year — in December « 2 / 30 j » beside « Solde après 25 jours » reads as a contradiction. « Non suivi » on Maladie/Sans solde suggests the allowance is not tracked. Fix: « Solde {année} après » when the year differs from today's; « Ne compte pas » for other types.

### Finding 15
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** web/components/user-management.tsx
- **Line:** 582
- **Anchor:** `UserManagement` row actions (also 691)
- **Comment:** Spec edge case: a former secretary keeps her history in her file. « Fiche » shows only for `role === "secretary"`, so that file is reachable only through a row in « Toutes les demandes ». Fix: show it for any account with a congé history (or any account).

### Finding 16
- **Severity:** Minor
- **Category:** Breaking Change
- **Verdict:** Confirmed
- **File:** web/components/team-modules-card.tsx
- **Line:** 27
- **Anchor:** `TeamModulesCard.toggle`
- **Comment:** `PUT clinics/team-modules` writes the `Clinics` row, aging the `clinicVersion` that `clinic-settings.tsx` holds (378) and sends on every section save (440/579/617). The card discards the returned version, so flipping « Équipe » while a section is in edit raises « modifiés par quelqu'un d'autre » (381-386) for the admin's own change, and the next section save 409s. Fix: `onSaved(version)` → `setClinicVersion` (line 1555). `MoneyDiscreetCard` has the same shape (pre-existing).

### Finding 17
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/app/users/page.tsx
- **Line:** 99
- **Anchor:** `UsersTabs` › `TabsTrigger coarse:min-h-11`
- **Comment:** Coarse pointer: `TabsList` keeps `h-9` + `overflow-x-auto` (`ui/tabs.tsx:41`) while the trigger is `h-[calc(100%-1px)]` plus `min-h-11` — 44 px pills in a 36 px track: clipped, vertical wobble, 36 px hit area. Fix: `coarse:h-auto` on the `TabsList`.

### Finding 18
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/staff-leave/staff-leave-request-list.tsx
- **Line:** 309
- **Anchor:** `StaffLeaveHistory` › Jours `<TableCell numeric>`
- **Comment:** `numeric` adds `whitespace-nowrap` (`ui/table.tsx:205`), so « 2,5 jours décomptés (3 jours demandés) » (`staffLeaveDaysText`) is one unbreakable block that pushes the ⋯ column behind the fade at 1024–1280 px. Fix: keep `numeric` on the figure, put « (3 jours demandés) » on a wrapping second line.

### Finding 19
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/user-management.tsx
- **Line:** 693
- **Anchor:** `UserManagement` › table « Fiche » button
- **Comment:** At 1440 the Actions column already overflows (`qa/shots/D1-1440-comptes.png`: « Réinitia… » cut, « Désactiver » off-screen); a third nowrap button pushes the destructive action further out. Fix: link the name cell to `/users/{id}` instead; keep the card-menu item.
- **Challenge note:** The overflow itself pre-exists; this branch makes it worse, which is what is kept.

### Finding 20
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/staff-leave/staff-leave-decision-dialogs.tsx
- **Line:** 340
- **Anchor:** `CancelLeaveConfirm` title; aria-labels (`staff-leave-request-list.tsx:184`, `RowActions` 226)
- **Comment:** Admin context: « Annuler le congé du 16 au 18 novembre ? » and « Actions pour le congé … » don't say **whose** — two secretaries off on the same dates read identically. Fix: add `request.userName` when the viewer is the admin.

### Finding 21
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/staff-leave/staff-leave-admin-panel.tsx
- **Line:** 111
- **Anchor:** `StaffLeaveAdminPanel` › team strip / history filter
- **Comment:** If `useTeamLeaveOverview` fails or is loading, `members` is `[]` and « Équipe » just disappears (a failed read shown as nothing). After a filter change `useTeamRead` keeps the previous key's data, and line 152 only shows the loader when there is none — so « Refusées » shows the « Toutes » rows until the read lands. Fix: `LoadFailureNotice` with `team.refetch`; show the loader while `decided.loading`.

### Finding 22
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/staff-leave/staff-leave-request-list.tsx
- **Line:** 325
- **Anchor:** `StaffLeaveHistory` › `CardList`
- **Comment:** The table's « Secrétaire » cell links to her file (295); the card form below `lg` has no `href`, so phones/iPad portrait lose that path (the only one for a deactivated secretary). Fix: `href={(r) => (showName ? userHref(r) : undefined)}`.

### Finding 23
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/staff-leave/staff-leave-decision-dialogs.tsx
- **Line:** 160
- **Anchor:** `AcceptLeaveDialog` › − / + stepper
- **Comment:** The global `:focus-visible` outline (`globals.css:931`, 2 px offset) is drawn outside the raw `<button>`s and clipped by the wrapper's `overflow-hidden` on three sides, so keyboard focus barely shows. Fix: `focus-visible:bg-muted focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-inset focus-visible:outline-none`.

### Finding 24
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** web/components/staff-leave/staff-leave-decision-dialogs.tsx
- **Line:** 54
- **Anchor:** `parseDays` / `daysText`
- **Comment:** « half days, 0–366 » is written here and again inline in `AllowanceEditor` (`app/users/[userId]/page.tsx:198`); `366` appears three times; `daysText` and `String(allowance).replace(".", ",")` re-implement `formatStaffDays`. Fix: `MAX_STAFF_DAYS` + `parseStaffDays()` in `lib/staff-leave.ts`, used by both.

### Finding 25
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** web/lib/nav.ts
- **Line:** 228
- **Anchor:** `DAILY_SECTION_TITLE`
- **Comment:** Declared after `baseSections`, which still writes the literal `"Quotidien"` (line 55); renaming the section silently drops « Congés » from the secretary's menu (line 255). Fix: declare it above and use it at line 55.

### Finding 26
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/Queries/ListStaffLeaveRequestsQuery.cs
- **Line:** 122
- **Anchor:** `ListStaffLeaveRequestsQueryHandler.Handle`
- **Comment:** `StaffLeaveRequestsPageDto.PendingCount` is never read (the tab and rail read `pending-count`; `web/lib/api/team.ts:40` only declares it), yet every list call pays a `COUNT`. Remove it from both DTOs and the handler.

### Finding 27
- **Severity:** Suggestion
- **Category:** Error Handling
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Team/Commands/SetLeaveAllowanceCommand.cs
- **Line:** 60
- **Anchor:** `SetLeaveAllowanceCommandHandler.Handle` / `User.SetAnnualLeaveAllowance`
- **Comment:** The only handler in the feature that validates by catching `ArgumentException` and returning `ex.Message`; `User.SetAnnualLeaveAllowance` hard-codes « 366 » beside `MaxAnnualLeaveAllowanceDays`, which duplicates `StaffLeaveRequest.MaxDays`. Fix: `User.AllowanceRefusalFor(decimal?)` on one shared constant, checked before the setter.
- **Challenge note:** Lowered to Suggestion — the inner try wraps one domain call whose message is already the intended French sentence; nothing else can leak through it today.

### Finding 28
- **Severity:** Suggestion
- **Category:** Business Logic
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Team/Commands/RecordStaffAbsenceCommand.cs
- **Line:** 89
- **Anchor:** `RecordStaffAbsenceCommandHandler.Handle`
- **Comment:** A recorded absence is saved accepted with the computed count and no « Décompter », so one over a jour férié overstates « Pris ». Optional `countedDays` + the same stepper in the dialog would cover it.
- **Challenge note:** Lowered to Suggestion — spec AC-11 says « same fields » as the request and « saved as accepted »; the admin can also leave the férié out of the dates.

### Finding 29
- **Severity:** Suggestion
- **Category:** Business Logic
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** web/components/staff-leave/staff-leave-decision-dialogs.tsx
- **Line:** 322
- **Anchor:** `CancelLeaveConfirm.handleCancel`
- **Comment:** The cancel re-reads and sends the fresh version without checking the status is still the one the dialog names, so the version check protects nothing. Fix: as Accept/Refuse do — if `fresh.status !== request.status`, say so with « Recharger ».
- **Challenge note:** Lowered to Suggestion — the person's intent (withdraw this congé) is still honoured whichever status it reached; only the dialog's sentence goes stale.

### Finding 30
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** web/lib/realtime/clinic-hub.ts
- **Line:** 41
- **Anchor:** `RealtimeResource`
- **Comment:** The edit moved `DoctorLeaves`' comment onto the end of the new `Team` line, which now carries two `//` comments, the second describing the wrong key. Move it back to line 40.
- **Challenge note:** Lowered to Suggestion — comment only.

### Finding 31
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** web/lib/hooks/use-staff-leave.ts
- **Line:** 24
- **Anchor:** `useTeamRead`
- **Comment:** Line-for-line copy of `useLeavesRead` (`lib/hooks/use-doctor-leaves.ts:17`); only the key and type differ. One `useKeyedRead<T>(resource, key, read)` wrapped by both.
- **Challenge note:** Lowered to Suggestion — the refactor reaches a shipped hook outside this feature.

### Finding 32
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** web/app/conges/page.tsx
- **Line:** 26
- **Anchor:** `SectionTitle`
- **Comment:** The same section heading exists four times (here, `staff-leave-admin-panel.tsx:37`, two inline `<h2>` in `app/users/[userId]/page.tsx:89,106`). Export one from `staff-leave-bits.tsx`.
- **Challenge note:** Lowered to Suggestion — styling duplication only.

### Finding 33
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** web/components/staff-leave/staff-leave-request-list.tsx
- **Line:** 214
- **Anchor:** `RowActions`
- **Comment:** Both call sites (317, 340) pass `admin` as `true`, so the prop and the `decidable = admin && …` false branch are dead. Drop the prop.
- **Challenge note:** Lowered to Suggestion — dead parameter, no behaviour.

### Finding 34
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Team/Commands/DecideStaffLeaveRequestCommand.cs
- **Line:** 101
- **Anchor:** `DecideStaffLeaveRequestCommandHandler.Handle`
- **Comment:** Re-implements `StaffLeaveRequest.Reject`'s motif checks with different wording (« Indiquez le motif du refus. » vs « Le motif du refus est requis. »). A `StaffLeaveRequest.RejectionRefusalFor(reason)` used by both (the `CountRefusalFor` shape).
- **Challenge note:** Lowered to Suggestion — the handler checks first, so the entity's sentence is an unreachable backstop.

### Finding 35
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Team/Queries/PreviewStaffLeaveQuery.cs
- **Line:** 66
- **Anchor:** `PreviewStaffLeaveQueryHandler.Handle`
- **Comment:** A hand copy of the date-range rule beside `StaffLeaveRequest.RefusalFor`. Split `RangeRefusalFor(start, end)` out and call it here.
- **Challenge note:** Lowered to Suggestion — the preview answers « no count » rather than refusing, so a drift shows no figure, never a wrong save.

### Finding 36
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Team/Queries/ListStaffLeaveRequestsQuery.cs
- **Line:** 104
- **Anchor:** `ListStaffLeaveRequestsQueryHandler.Handle`
- **Comment:** `GetApprovedOverlappingAsync` runs once per pending row (N+1). One read over the min-start→max-end span, filtered in memory.
- **Challenge note:** Lowered to Suggestion — N is the waiting requests of one cabinet, a handful.

### Finding 37
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Infrastructure/Repositories/StaffLeaveRequestRepository.cs
- **Line:** 48
- **Anchor:** `ListAsync`
- **Comment:** The total re-encodes the `pendingOnly` filter (48-49) instead of reusing the one the `switch` applies (33-37); line 78 repeats `HoldsItsDays`' status set. Build the filtered query once and count it.
- **Challenge note:** Lowered to Suggestion — both copies agree today.

### Finding 38
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Domain/Entities/StaffLeaveRequest.cs
- **Line:** 60
- **Anchor:** `Covers`
- **Comment:** Dead code: `StaffLeaveRequest.Covers` (tests only — the real rule is `StaffLeaveRequestRepository.Touching`) and `FindHoldingOverlapAsync`'s `excludeId` (never passed). Remove or wire.
- **Challenge note:** Narrowed — the `Year` parameters of `GetMyStaffLeaveQuery` / `GetStaffFileQuery` are dropped from this finding: `?year=` is in the spec's API contract.

### Finding 39
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Team/Commands/CancelStaffLeaveRequestCommand.cs
- **Line:** 112
- **Anchor:** `CancelStaffLeaveRequestCommandHandler.Handle`
- **Comment:** `StaffLeaveRules.ClinicToday()` is read twice (80 and 112); reuse one `today`.
- **Challenge note:** Lowered to Suggestion — differs only across midnight, and only in the returned `CanCancel`.

### Finding 40
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Domain/Entities/StaffNotification.cs
- **Line:** 204
- **Anchor:** `ForStaffLeave`
- **Comment:** Accepts any `NotificationCategory`; a guard to the four `StaffLeave*` members would stop a mismatched row.
- **Challenge note:** Lowered to Suggestion, and the precedent claim is wrong: the other `For*` factories hard-code their one category rather than guard a parameter.

### Finding 41
- **Severity:** Suggestion
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/Queries/ListStaffLeaveRequestsQuery.cs
- **Line:** 71
- **Anchor:** `ListStaffLeaveRequestsQueryHandler.Handle`
- **Comment:** An unknown `status` silently means « all » (200 with the wrong rows). Treat only null/blank as all; refuse anything else.

### Finding 42
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/Commands/SubmitStaffLeaveRequestCommand.cs
- **Line:** 70
- **Anchor:** `SubmitStaffLeaveRequestCommandHandler.Handle`
- **Comment:** Lines 70-80 and `RecordStaffAbsenceCommand.cs:78-89` run the same four steps (`ParseType` → `RefusalFor` → `FindHoldingOverlapAsync` → `CountDays`). One `StaffLeaveRules.ValidateNewAsync(...)`; move `NotASecretary` (RecordStaffAbsenceCommand.cs:26) into `StaffLeaveRules`.

### Finding 43
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/StaffLeaveRules.cs
- **Line:** 163
- **Anchor:** `ResolveCallerAsync`
- **Comment:** Same body as `StaffChatRoster.ResolveCallerAsync` (StaffChatRoster.cs:52) with different refusal constants. One `CallerResolver` in `Common/` taking the two sentences.

### Finding 44
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** web/app/users/[userId]/page.tsx
- **Line:** 187
- **Anchor:** `AllowanceEditor`
- **Comment:** The effect copies the prop into state (initializer written twice, extra render) — use `key={String(allowance)}`. `file.role as UserRole` (124) could go by typing `StaffFileDto.role` as `UserRole`.

### Finding 45
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** web/components/staff-leave/staff-leave-decision-dialogs.tsx
- **Line:** 109
- **Anchor:** `AcceptLeaveDialog` / `RejectLeaveDialog`
- **Comment:** Both repeat re-read → still Pending? → conflict → decide → toast → close → `onChanged`, with identical `reload`. A `useLeaveDecision(request, onOpenChange, onChanged)` hook.

### Finding 46
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** web/app/users/page.tsx
- **Line:** 82
- **Anchor:** `UsersTabs`
- **Comment:** `usePendingLeaveCount` is mounted here and in `DashboardSidebar`: two requests + two subscriptions per `team` broadcast on `/users`. Read it once.

## Notes

- **Probe bug in `qa/walk.mjs`** (Finding 2): the C rows measured `document.documentElement.scrollWidth`; `<main id="contenu-principal">` is the scroller. Fix the probe with the source fix (rule now in `test-in-browser`'s probe-trap table).
- **Warning for `/apply-review-fixes`:** do not « fix » DEV-1/2/3 or the kept PageHeader/Tabs; Finding 28 is spec-accepted behaviour (AC-11) — optional.
- Clean, per the migration agent: migration ↔ Designer ↔ snapshot ↔ configs consistent on this base, no `xmin`; enums appended; `ReachesALockedPhone` complete; realtime key, `AuditLabels`, all 7 `ClinicDto` sites, TS `ClinicDto` updated; archive/relay/purge derived; both `INotificationGenerator` implementations updated; `DoctorLeaveRules.Period` output unchanged; gates block new writes by default.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 5 |
| Minor | 21 |
| Suggestion | 20 |
| **Total** | 46 |

## Fixes applied (2026-11-05, `/apply-review-fixes`)

**41 applied · 5 not applied.**

| # | Outcome | What changed |
|---|---------|--------------|
| 1 | fixed | rebased on origin/main 7000b0e0 without a commit; snapshot from main, migration re-scaffolded with the same `Up()` and id; `verify-schema` before/after; the merged « ⋯ » menu carries « Voir sa fiche et ses congés »; main's « Votre secrétaire » tour switches `/users` to « Comptes » |
| 2 | fixed | history filter is a 2×2 grid below `sm:` |
| 3 | fixed | cards and team strip hinge on their own width (`@container`), footer on `@sm:` |
| 4 | fixed | `/conges` gets `pb-16 sm:pb-0` while the fixed button shows |
| 5 | fixed | Accepter / Refuser go through `useDirtyGuard` + `DiscardChangesDialog` |
| 6 | fixed | roster / clinic / history read before the save in Decide, Cancel and the allowance; test `A_Roster_That_Cannot_Be_Read_Stops_The_Decision_Before_Anything_Is_Saved` |
| 7 | fixed | the five reads and the allowance refuse with `ModuleOff`; « Fiche » and the file page follow the switch |
| 8 | fixed, other remedy | the first year keeps its own open days, the second takes the rest — 1 Jan taken out comes off the new year; no schema change |
| 9 | fixed | « Cette demande est déjà annulée. » |
| 10 | fixed | refused bell row and e-mail name the days (not repeated for a half day) |
| 11 | fixed | `CanBeCancelledBy(byAdmin, today)`; `CanCancel` depends on who reads it |
| 12 | fixed | « a déjà une demande en attente … : répondez-y plutôt » vs « un congé accepté » |
| 13 | fixed | `useLeaveDialogs` is mounted by the page / panel, lists take `onOpen` |
| 14 | fixed | « Solde {année} après » outside the current year; « Ne compte pas » for other types |
| 15 | challenged | her file is reachable from every row of « Toutes les demandes » (table link, and now the card too — #22); a « Fiche » on doctor/admin accounts would open a congé file for accounts the spec excludes |
| 16 | fixed | `TeamModulesCard onSaved(version)` → `setClinicVersion`; the card's own broadcast is not read as a peer's |
| 17 | fixed | `TabsList coarse:h-auto` |
| 18 | fixed | the figure stays on one line, « (N jours demandés) » wraps under it |
| 19 | fixed | the name cell links to the file; the table « Fiche » button is gone |
| 20 | fixed | admin confirm and labels name the secretary |
| 21 | fixed | team strip: loader + `LoadFailureNotice`; another filter's rows are dropped until its read lands |
| 22 | fixed | history cards link to the file |
| 23 | fixed | stepper `focus-visible` ring inset |
| 24 | fixed | `MAX_STAFF_DAYS` + `parseStaffDays` in `lib/staff-leave.ts`, used by both dialogs |
| 25 | fixed | `DAILY_SECTION_TITLE` declared first and used by `baseSections` |
| 26 | fixed | `PendingCount` removed (C# + TS) |
| 27 | fixed | `User.AllowanceRefusalFor`, one constant |
| 28 | not applied | spec AC-11: « same fields », saved as accepted — an option, not a defect |
| 29 | fixed | a changed status closes the confirm and reloads instead of cancelling |
| 30 | fixed | comment back on `DoctorLeaves` |
| 31 | not applied | would refactor the shipped `use-doctor-leaves` hook, outside this feature |
| 32 | fixed | `StaffLeaveSectionTitle` in `staff-leave-bits.tsx`, four sites |
| 33 | fixed | `admin` prop removed |
| 34 | fixed | `StaffLeaveRequest.RejectionRefusalFor` |
| 35 | fixed | `StaffLeaveRequest.RangeRefusalFor`, used by the preview |
| 36 | fixed | one approved-overlap read per list |
| 37 | fixed | the filter is built once and counted; the `HoldsItsDays` status set stays written twice |
| 38 | fixed | `Covers` and `excludeId` removed |
| 39 | fixed | one `today` per cancel |
| 40 | fixed | `ForStaffLeave` refuses a non-congé category |
| 41 | fixed | « Filtre de statut inconnu. » |
| 42 | fixed | `StaffLeaveRules.ValidateNewAsync` |
| 43 | not applied | would edit StaffChat and Relay, outside this feature |
| 44 | fixed | `key` on the editor, `StaffFileDto.role: UserRole` |
| 45 | fixed | `useLeaveDecision` |
| 46 | not applied | sharing one count needs a shell-level context; two reads of one number |

**Gates after the last edit:** unit suite 6342/0 (6 skipped) · tsc ✅ · check:responsive 85/85 · browser `qa/run-4.md` GREEN 91/91 (with F1–F4 and the `<main>` scroll probe).
