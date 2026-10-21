# Feature Review: doctor-conges

**Status:** COMPLETE
**Challenged:** Yes
**Date:** 2026-10-20
**Challenged Date:** 2026-10-20
**Parent Branch:** main
**Merge Base:** 59e88ec9c7b399b6d0018ce73cbb8b5dcaccfd6e
**Files Reviewed:** 46 changed files (api + web), ~3 470 diff lines; excluded: migration `.Designer.cs` + `ApplicationDbContextModelSnapshot.cs` (read directly), `features/**`.
**Review method:** working-tree review (feature uncommitted in its own worktree) — unified diff via `git add -N` + `git diff HEAD`, index restored. Six agents: Code Quality, Error Handling (MediatR + `Result<T>`, not ROP), Business Logic, Breaking Changes + migration, Device & UX, Frontend correctness. Already-decided items excluded from every mandate.

## Challenge Summary

| Metric | Count |
|--------|-------|
| Original findings | 31 |
| Confirmed | 26 |
| Confirmed (adjusted) | 3 |
| Dismissed (false positive) | 2 |
| Dismissed (pre-existing) | 0 |
| **Final findings** | 29 |

Dismissed:
- *Feature-slice cycle Appointments ↔ Doctors* — false precedent claim: `Features/Appointments` already imports other feature folders (`AppointmentInvoiceLinks.cs`, `RevertCalendarImportRunCommand.cs`) and `TreatmentPlans`/`Dashboard` import `Appointments`; cross-slice references are the norm here.
- *Add `[AC-n]` tags to the new test classes* — conflicts with the owner's test rule (AC traceability lives in the test **name** and in `progress.md` § Test Plan; `// [AC-n]` markers are banned).

## Findings

### Finding 1
- **Severity:** Critical
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/components/appointment-calendar.tsx
- **Line:** 1185
- **Anchor:** `AppointmentCalendar` — `setHourGridOffsetTop` effect; ribbon at ≈3299
- **Comment:** The congé ribbon is inside the sticky header that blocks are positioned from (`top = hourGridOffsetTop + …`, ≈1400 / ≈1697). The offset is re-measured only on `[view, isNarrow, mounted, loading, hourHeight]`, and `useAppointments` flips `loading` on the FIRST load only (later loads set `refetching`, `use-appointments.ts:57`). So paging ‹ › onto or off a congé week, a congé saved/deleted via realtime, or the leaves reply landing after the appointments adds/removes ~24 px (≈34 on a coarse pointer) of header with no re-measure: every block and the drag band are drawn ~20–30 min off their hour while the « maintenant » line (DOM offset) is right. On phone Semaine the block shows no time, so position is the only cue. The effect's own comment says it re-measures « on everything that changes the header ». Fix: a `ResizeObserver` on the sticky header driving `setHourGridOffsetTop`.

### Finding 2
- **Severity:** Major
- **Category:** Frontend / Device & UX
- **Verdict:** Confirmed
- **File:** web/components/appointment-calendar.tsx
- **Line:** 850
- **Anchor:** `AppointmentCalendar` — `useClinicLeaves(...)`
- **Comment:** Only `leaves` is destructured; `failed`/`refetch` are dropped. A failed congé read draws no ribbon, no hatch, no « congé » header — the same picture as « nobody is away », so the desk can promise a slot with an absent practitioner (frontend-web § 13: a failed read must never render as empty data). Fix: add it to the existing error banner — « Les congés n'ont pas pu être chargés » + Réessayer → `refetch`.

### Finding 3
- **Severity:** Major
- **Category:** Breaking Change
- **Verdict:** Confirmed
- **File:** web/components/mon-profil-content.tsx
- **Line:** 98
- **Anchor:** `MonProfilContent` — `useClinicRealtime(RealtimeResource.Doctors, …)`; card at 293
- **Comment:** Every congé write broadcasts `doctors` (commands live in `Features.Doctors`). Mon profil bumps `reloadKey` on any `doctors` broadcast → `setLoading(true)` (65) → the whole page becomes « Chargement du profil… » (171), unmounting the congé card and its dialog and resetting the ordre number, cachet and working hours being edited. Congés make that broadcast routine (reception records them all day), and the user's own save from this page triggers it too. Fix: give the congé commands their own area (`Features/DoctorLeaves` → `doctorleaves` key, declared in `clinic-hub.ts`, listened to only by the congé hooks).

### Finding 4
- **Severity:** Major
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/components/doctor-leaves/doctor-leaves-card.tsx
- **Line:** 147
- **Anchor:** `DoctorLeavesCard` — `<DoctorLeaveDialog>` / `<DeleteLeaveConfirm>`; also `AppointmentCalendar`'s dialog ≈3788
- **Comment:** Neither caller refetches after its own create/update/delete; the screen updates only via the SignalR echo. The repo convention (and `useFreshVersion`'s own doc, case 3: « the parent refreshes on success ») is that the success handler refreshes and realtime is additive. With the hub down/reconnecting, a saved congé never appears, a deleted one stays, and reopening the stale row hydrates old dates with the fresh version — Enregistrer then silently reverts the user's own edit. Fix: `onSaved` on `DoctorLeaveDialog` and `onDeleted` on the card's confirm, wired to the hook's `refetch`.

### Finding 5
- **Severity:** Major
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/appointment-calendar.tsx
- **Line:** 2722
- **Anchor:** `AppointmentCalendar` — agenda bar + « Congé » button (≈2999)
- **Comment:** Regression on the primary device: at 820 px (iPad portrait, rail open, admin) `qa/shots/A1-820.png` shows « Nouveau » ending ≈14 px past the card edge and the period title at 0 px; before the 36 px button + 8 px gap the bar fitted with a ~30 px title. Likely worse at 768 and 1024 (the QA `F1-bar` row only measured 1280+). Fix: tighten the bar below a measured container width (e.g. `@max-[52rem]:gap-1`) or let the title take its own row under ~31rem; extend `F1-bar` to 768/820/1024.

### Finding 6
- **Severity:** Major
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/appointment-calendar.tsx
- **Line:** 890
- **Anchor:** `renderLeaveChip`
- **Comment:** The ribbon chip is 32 px on a coarse pointer (`coarse:min-h-8`), under the 44 px floor — and for a secretary it is the only way to change or delete a congé (Paramètres is admin-only, Mon profil lists only her own). Two congés on one day stack 2 px apart (`gap-0.5`), so a mis-tap opens the wrong one. Fix: `coarse:min-h-11` on the chip (grow it; no `.touch-target` on stacked siblings).

### Finding 7
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Doctors/Commands/CreateDoctorLeaveCommand.cs
- **Line:** 71
- **Anchor:** `CreateDoctorLeaveCommandHandler.Handle`; same tail in `UpdateDoctorLeaveCommandHandler.Handle` ≈73–81
- **Comment:** `AppointmentsDuringAsync` runs after `SaveChangesAsync` inside the catch-all: a failure there reports « Erreur lors de l'enregistrement du congé. » for a saved congé, suppresses the realtime broadcast, and the retry is refused as overlapping the congé just saved. Fix: count before the commit (it does not need the new row), in one helper shared by both handlers.

### Finding 8
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Domain/Entities/DoctorLeave.cs
- **Line:** 49
- **Anchor:** `DoctorLeave.RefusalFor`
- **Comment:** Non-nullable `DateOnly` binds a missing key to `0001-01-01`, which passes `RefusalFor`: POST `{}` saves a congé on 01/01/0001 and PUT moves one there. `PaymentDateRules` refuses the same trap. Fix: refuse `default` / a date before a floor with « Les dates du congé sont requises. ».

### Finding 9
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Doctors/Commands/CreateDoctorLeaveCommand.cs
- **Line:** 61
- **Anchor:** `CreateDoctorLeaveCommandHandler.Handle` (+ update path)
- **Comment:** The overlap rule is check-then-insert with no database constraint behind it; two simultaneous saves for one practitioner both pass and store overlapping congés, after which every edit of either is refused. `EX_Appointments_NoDoubleBooking` is the sibling precedent (`btree_gist` installed). Fix: an exclusion constraint on `(ClinicId, DoctorId, daterange(StartDate, EndDate, '[]'))`, mapped to the existing overlap sentence.

### Finding 10
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Doctors/DoctorLeaveRules.cs
- **Line:** 89
- **Anchor:** `DoctorLeaveRules.AppointmentsDuringAsync`
- **Comment:** The « N rendez-vous sont déjà prévus » count includes « créneau occupé » blocks booked for that practitioner (no patient) — exactly how a doctor marked an absence before this feature. Fix: also require `PatientId != null`.

### Finding 11
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** web/components/appointment-calendar.tsx
- **Line:** 864
- **Anchor:** `AppointmentCalendar` — `leaveClosesDay` / `isOnLeave`
- **Comment:** In a cabinet with ONE active practitioner and no filter, `visibleLeaves` is every congé in the window — including a retired practitioner's (the clinic read returns them for history) — so their recorded congé hatches the remaining dentist's working days « congé ». Fix: when closing because of the single active practitioner, count only that practitioner's congés.

### Finding 12
- **Severity:** Minor
- **Category:** Frontend / Business Logic
- **Verdict:** Confirmed
- **File:** web/components/agenda-phone-header.tsx
- **Line:** 406
- **Anchor:** `AgendaPhoneHeader` — 7-day strip (`isDayOpen` = `isDayOpenOnAgenda`)
- **Comment:** In phone Jour, a congé day reads « fermé » / « cabinet fermé » (the cabinet is open), and the leaves window is the selected day only (`startDate`/`endDate` ≈836/842), so only the day on screen shows closed and the others flip as you tap. Fix: fetch the strip's week in Jour and pass congé state separately, rendering « congé » / « praticien en congé ».

### Finding 13
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** web/components/clinic-settings.tsx
- **Line:** 1200
- **Anchor:** `ClinicSettings` — « Congés de ce praticien »
- **Comment:** AC-1 lets a secretary manage any practitioner's congés, but she has no list: the spec says « the full list lives » in Paramètres → Médecins, and Paramètres is hidden from secretaries (owner's call, 2026-10-04); Mon profil lists only her own. A mistyped year can only be found by paging the agenda.
- **Challenge note:** kept at Minor — the spec's premise (reception can reach Paramètres) is false; the agenda marker does let her edit, so it is a findability gap, not a missing capability. Owner to decide where the list goes.

### Finding 14
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Domain/Entities/DoctorLeave.cs
- **Line:** 46
- **Anchor:** `DoctorLeave.Overlaps`
- **Comment:** `Overlaps` is only called by tests; the real predicate is written twice in `DoctorLeaveRepository` (`GetOverlappingAsync`, `FindOverlappingAsync`). Fix: remove it from the entity and share one predicate helper in the repository (or have the repository use it).

### Finding 15
- **Severity:** Minor
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/lib/hooks/use-doctor-leaves.ts
- **Line:** 42
- **Anchor:** `useLeavesRead` — `.catch` → `setFailed(true)`
- **Comment:** `failed` is set even when a good earlier list is held, and the card renders `failed` over the list (`doctor-leaves-card.tsx:103`) — one failed realtime refetch replaces a valid list with an error. Fix: show the list plus an inline retry when `leaves.length > 0`, or set `failed` only when nothing was read for the current key.

### Finding 16
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/doctor-leaves/doctor-leave-dialog.tsx
- **Line:** 325
- **Anchor:** `DoctorLeaveDialog` — `DialogFooter`
- **Comment:** The primitive's `md:justify-end` (`ui/dialog.tsx:331`) beats the call site's `sm:justify-between`, so from 768 px red « Supprimer » sits 8 px from « Annuler »; at 640–767 the spacer adds an empty row. Fix: `md:justify-between`, spacer `hidden md:block`, inner row `md:flex-row`.

### Finding 17
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/agenda-phone-header.tsx
- **Line:** 200
- **Anchor:** `AgendaPhoneHeader` — title; congé button ≈218
- **Comment:** At 320 px on any day but today, the new 40 px button cuts the title to « nove… » (`qa/shots/C3-320-week.png`) — nothing else names the month. Fix: a short month format below 360 px.

### Finding 18
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/appointment-calendar.tsx
- **Line:** 2414
- **Anchor:** `renderMonthView`
- **Comment:** Desktop Mois draws congé chips above the visit chips (inside the `overflow-hidden` cell) without counting them against `MONTH_CELL_MAX_CHIPS` (2370); the phone branch does. On short rows (1536×730, coarse iPad) visit chips are cut and « +N autres » clipped. Fix: slice to `MONTH_CELL_MAX_CHIPS - dayLeaves.length`.

### Finding 19
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/clinic-settings.tsx
- **Line:** 1202
- **Anchor:** `ClinicSettings` — « Congés de ce praticien » summary
- **Comment:** Copies the `.touch-target` summary of « Horaires de ce praticien » (1192) 4 px below it (`mt-1`); on a coarse pointer the two 44 px overlays overhang and the later one steals the bottom of « Horaires » (frontend-web § 2). Fix: drop `touch-target` on both and grow the box (`py-2 coarse:py-3.5`).

### Finding 20
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/doctor-leaves/doctor-leaves-card.tsx
- **Line:** 114
- **Anchor:** `DoctorLeavesCard` — « Congés passés » summary
- **Comment:** `text-xs` + `coarse:py-3` = 40 px on a coarse pointer, under 44 px. Fix: `coarse:py-3.5`.

### Finding 21
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Doctors/DoctorLeaveRules.cs
- **Line:** 88
- **Anchor:** `DoctorLeaveRules.AppointmentsDuringAsync`
- **Comment:** Loads every appointment of the practitioner in the window with Patient/ProcedureType/Procedures included, to produce a count and a date, and re-filters on `DoctorId` that the repository already filtered (`AppointmentRepository.cs:60`). Fix: a projection read, or at least drop the redundant filter.
- **Challenge note:** lowered Minor → Suggestion — one read per congé save, bounded by one practitioner's year.

### Finding 22
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Doctors/Commands/UpdateDoctorLeaveCommand.cs
- **Line:** 63
- **Anchor:** `UpdateDoctorLeaveCommandHandler.Handle` (+ `DeleteDoctorLeaveCommand` ≈48–50)
- **Comment:** The congé ownership lookup and « Congé introuvable. » are copied in update and delete; the retired-practitioner sentence is inline in create. Fix: `DoctorLeaveRules.ResolveLeaveAsync(...)`, sentences beside `OverlapSentence`.
- **Challenge note:** lowered Minor → Suggestion — three lines duplicated, no behavioural divergence today.

### Finding 23
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Doctors/Commands/DeleteDoctorLeaveCommand.cs
- **Line:** 45
- **Anchor:** `DeleteDoctorLeaveCommandHandler.Handle`
- **Comment:** `Result.Failure(access.Error ?? "Praticien introuvable.")` re-wraps the failure with an unreachable fallback while create/update use `FailureFrom`. Fix: return the failure as-is.
- **Challenge note:** lowered Minor → Suggestion — `ResolveEditableAsync` sets no code today, so nothing is lost yet.

### Finding 24
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Appointments/AppointmentScheduling.cs
- **Line:** 201
- **Anchor:** `AppointmentScheduling.CheckWorkingHoursAsync`
- **Comment:** It now checks the congé first; name and summary still say working hours only. Rename (e.g. `CheckAvailabilityAsync`) or add one summary line.

### Finding 25
- **Severity:** Suggestion
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/Controllers/DoctorsController.cs
- **Line:** 119
- **Anchor:** `DoctorsController.GetLeaves` / `UpdateLeave` / `DeleteLeave`
- **Comment:** `GetLeaves` maps every failure (a server error included) to 404, while update/delete answer 400 for « Praticien introuvable. » / « Congé introuvable. ». DEV-1 covers range/length/overlap/not-own only. Fix: a not-found code mapped to 404 on all three, or record it in DEV-1.

### Finding 26
- **Severity:** Suggestion
- **Category:** Breaking Change
- **Verdict:** Confirmed
- **File:** web/components/clinic-settings.tsx
- **Line:** 1206
- **Anchor:** `ClinicSettings` — `<details>` → `<DoctorLeavesCard embedded />`
- **Comment:** Children of a closed `<details>` still mount: Paramètres → Médecins issues one `GET /doctors/{id}/leaves` per practitioner on load and on every `doctors` broadcast, unexpanded. Fix: mount on open.

### Finding 27
- **Severity:** Suggestion
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** web/components/doctor-leaves/doctor-leave-dialog.tsx
- **Line:** 49
- **Anchor:** `canEditAnyLeave`
- **Comment:** A role predicate exported from a component; `web/lib/auth/can.ts` holds the shared UI role gates (`isAdmin`, `canReverseFinancials`). Move it there.

### Finding 28
- **Severity:** Suggestion
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/doctor-leaves/doctor-leaves-card.tsx
- **Line:** 74
- **Anchor:** `DoctorLeavesCard` — « Supprimer le congé » menu item
- **Comment:** Hand-copies a destructive item; `DropdownMenuItem` has `variant="destructive"` (focus wash + dark colours, `ui/dropdown-menu.tsx:69`). Use it.

### Finding 29
- **Severity:** Suggestion
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/doctor-leaves/doctor-leaves-card.tsx
- **Line:** 108
- **Anchor:** `DoctorLeavesCard` — empty branch
- **Comment:** One grey line « Aucun congé prévu. » beside `ui/empty-state.tsx`. Use `<EmptyState size="compact" …>` with the add button as action.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 1 |
| Major | 5 |
| Minor | 14 |
| Suggestion | 9 |
| **Total** | 29 |

## Fixes applied (/apply-review-fixes, 2026-10-20)

| # | Outcome | What changed |
|---|---------|--------------|
| 1 | fixed | `ResizeObserver` on the hour grid's wrapper re-measures the offset when the header grows (ribbon) — RV-blocks: 0 px |
| 2 | fixed | agenda banner « Les congés des praticiens n'ont pas pu être chargés » + Réessayer — RV-leaves-failed |
| 3 | fixed | congé commands/queries/rules moved to `Features/DoctorLeaves` → realtime key `doctorleaves` (declared in `clinic-hub.ts`); only the congé hooks listen — RV-profil |
| 4 | fixed | `onChanged` on the dialog, `onDeleted` on the card's confirm → each caller refetches its own list |
| 5 | fixed | bar `@container` moved to a wrapper; Aujourd'hui/Filtres/view labels hinge on a 56rem bar; under 40rem the title takes its own line — F1-bar green 768→1920 |
| 6 | fixed | ribbon chip `coarse:min-h-11`; desktop Mois shows a label instead (the day opens Jour) so the taller chip cannot crowd a month cell |
| 7 | fixed | the visit count runs before the save; `DoctorLeaveRules.SaveResult` builds the response for both handlers |
| 8 | fixed | `RefusalFor` refuses missing dates (« Les dates du congé sont requises. ») + test |
| 9 | deferred | `follow-up/doctor-leave-overlap-constraint.md` — chosen fix: exclusion constraint + `ConstraintName`-aware 409 in `UnitOfWork` |
| 10 | fixed | count requires `PatientId != null` + test with a « créneau occupé » block |
| 11 | fixed | with one active practitioner, only their own congé closes a day |
| 12 | fixed | Jour reads the whole week of congés; the phone strip says « congé » / « praticien en congé » — RV-strip |
| 13 | fixed (owner's call) | « Tous les congés » in the agenda « ⋯ » menu and the phone panel, admin + secretary: a sheet listing each practitioner's congés with Ajouter / Modifier / Supprimer (`all-leaves-dialog.tsx`) — RV-all-leaves |
| 14 | fixed | `DoctorLeave.Overlaps` removed; one `Touching` predicate in the repository |
| 15 | fixed | a failed refresh over an already-read list keeps the list (`readKey`); the card shows the error only with nothing to show |
| 16 | fixed | footer `md:justify-between`, spacer `hidden md:block`, row `md:flex-row` |
| 17 | fixed | phone header title: « d MMM » / « MMM » below 360 px |
| 18 | fixed | desktop Mois slices visit chips by `MONTH_CELL_MAX_CHIPS - dayLeaves.length` — RV-mois |
| 19 | fixed | both settings summaries grow their box (`coarse:py-3.5`), no `.touch-target` |
| 20 | fixed | « Congés passés » `coarse:py-3.5` |
| 21 | skipped | one read per save, bounded by one practitioner's year; the extra `DoctorId` filter is defensive and free |
| 22 | fixed | `DoctorLeaveRules.ResolveLeaveAsync` + shared sentences |
| 23 | fixed | delete returns the failure as-is (`return access;`) |
| 24 | fixed | guard summary now says « congé first, then working hours » (no rename — three handlers and the tests call it) |
| 25 | fixed | `DoctorLeaveRules.NotFoundCode` → 404 on all congé routes via `LeaveStatus`; other refusals stay 400 |
| 26 | fixed | `DoctorLeavesDisclosure` mounts the card only once opened |
| 27 | fixed | `canEditAnyLeave` moved to `lib/auth/can.ts` |
| 28 | fixed | `DropdownMenuItem variant="destructive"` |
| 29 | fixed | `EmptyState size="compact"` « Aucun congé prévu » |

**Gates after the last edit:** unit suite 5 985 passed / 0 failed / 6 skipped (unfiltered) · check:responsive 81/81 · tsc · build · browser walk run-6 GREEN (53 ✅) · API rows 13/13 ✅.
