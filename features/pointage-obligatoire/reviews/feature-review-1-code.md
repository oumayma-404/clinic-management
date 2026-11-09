# Feature Review: pointage-obligatoire

**Status:** COMPLETE
**Challenged:** Yes
**Date:** 2026-11-06
**Challenged Date:** 2026-11-06
**Parent Branch:** feature/pointage-obligatoire → HEAD 13302491 (part 2, already reviewed and committed)
**Merge Base:** 7000b0e0 (origin/main fetched 2026-11-06 — 0 commits ahead, no stale base)
**Files Reviewed:** 81 files (+2839 / −106), working tree vs HEAD, assembled with `git add -N` (new files as all-`+` hunks).
Excluded: migration `.Designer.cs`, `ApplicationDbContextModelSnapshot.cs` (read directly by the breaking-changes agent), `features/**`.
**Review method:** 5 parallel agents — Code Quality · Error Handling + gate security (MediatR + `Result<T>`, not ROP) ·
Business Logic vs AC-1..AC-10 · Breaking Changes + migration + cross-boundary field tracing · Device & UX (rendering files in the diff).
Duplicates merged: no-borne lock (3 agents), journal exclusions (3), schéma counter not saved (3), dropped re-read (3),
`clinics` broadcast (2), subscription clock (2).
Not reported (deliberate): DEV-1..DEV-11, the owner's polish (one headline, « Annuler » in the footer, CSV « Corrigé · Déclaré »).
**Traced and consistent (no finding):** `declaredPreviousDeparture` body → `PunchRequest` → `PunchCommand`; 409 body → `ApiError.details` →
`previousDayOf`; `ClockStatusDto` C# ↔ TS (7 props); `AttendanceLockEnabled` on all 7 `ClinicDto` sites + `TeamModulesDto` (tri-state);
declared-departure DTO fields → TS → panel → CSV; enum members appended (24, 11) and classified; migration ↔ snapshot; archive and
PC-de-secours copy are column-generic; 423 never retried nor read as auth; e2e/CI never turn the pointage on; no gate bypass
(hubs, `Map*`, BFF, exemptions all checked).

## Challenge Summary

| Metric | Count |
|--------|-------|
| Original findings | 41 |
| Confirmed | 32 |
| Confirmed (adjusted) | 9 |
| Dismissed (false positive) | 0 |
| Dismissed (pre-existing) | 0 |
| **Final findings** | 41 |

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 5 |
| Minor | 21 |
| Suggestion | 15 |

Every finding was read against the working-tree source (30+ lines of context, comments included). Nine were lowered
Minor → Suggestion (invisible to the user, spec wording, pre-existing routing pattern, or cosmetic). Findings are
renumbered by severity; original numbers are in each Challenge note where they moved.

## Findings

### Finding 1
- **Severity:** Major
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/AttendanceLockRules.cs
- **Line:** 44
- **Anchor:** `public static bool IsLocked(AttendanceLockFacts facts) =>`
- **Comment:** Nothing lifts the lock when the cabinet has no active borne. Secretaries are tracked by default and `AttendanceLockEnabled` defaults to true, so an admin who turns « Pointage » on during opening hours before registering a borne — or retires the last borne to replace it — locks every secretary at once; the lock itself says « Aucune borne n'est encore installée » and only offers « Se déconnecter ». Same « a lock nobody can lift » reasoning as DEV-3. Fix: add a `HasActiveBorne` fact (one indexed `Any`, after the cheap switches) and test it.
- **Challenge note:** Verified — `IsLocked` has no borne fact, `team-modules-card.tsx` lets « Pointage » turn on with no borne, and the lock sub-switch reads « on » by default. `GetMyClockStatusQuery` already reads the active bornes for the lock screen, so the fact is cheap to add; it must go in the evaluator so the 423 and the screen still ask the same rule (AC-1).

### Finding 2
- **Severity:** Major
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/AttendanceLockRules.cs
- **Line:** 29
- **Anchor:** `public static readonly IReadOnlyCollection<string> NotWorkInTheJournal = new[]`
- **Comment:** « Dernière action » counts writes that are not work. `DeviceRegistration.Refresh` is audited on every app open with push on, and `StaffLeaveRequest` is a congé asked from home (AC-3 allows it). Tapping tonight's « Départ non pointé » push at 19:20 makes tomorrow's declaration step and the admin's sheet read « dernière action 19:20 » — the reminder manufactures the evidence. Fix: exclude `DeviceRegistration` and `StaffLeaveRequest` (ideally derived from the entities behind the `[AllowsBeforeClockIn]` writes) and assert it in the punch tests.
- **Challenge note:** Verified for `DeviceRegistration`: it is an `AggregateRoot`, absent from `AuditSaveChangesInterceptor.ExcludedEntityTypes`, and `RegisterPushDeviceCommand` calls `Refresh` (writes `LastSeenAt`) on every same-device registration — so it produces an audit row under her id. `StaffLeaveRequest` is a judgement call (it is an app action, just not cabinet work); the owner decides.

### Finding 3
- **Severity:** Major
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** web/components/team/clock-in-gate.tsx
- **Line:** 86
- **Anchor:** `const covering =`
- **Comment:** AC-3 keeps « Congés » open while locked and the server exempts `me/leave-requests` + cancel, but the gate covers every route except chrome-less paths and `/mon-profil` (only without a schéma), so `/conges` is always under the full-screen lock and the exemption is unreachable. Ill at home at 09:30, she cannot ask for today off. Fix: don't cover `/conges`, or add a « Demander un congé » link in the lock card like « Créer mon schéma ».
- **Challenge note:** Verified — `/conges` is a real route (`web/app/conges`) and `covering` does not exclude it. The spec conflicts with itself: AC-2 says « every screen of her account shows » the lock, AC-3 says « Congés » stays open, and DEV-11's premise (« exempt writes kept to what a locked screen can reach ») is false for congés. Owner decides; the fix must uncover `/conges` only, never other screens.

### Finding 4
- **Severity:** Major
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/team/clock-in-gate.tsx
- **Line:** 118
- **Anchor:** `<DialogPrimitive.Content data-clock-in-gate=""`
- **Comment:** The gate renders no `DialogPrimitive.Overlay`, so there is no scroll lock / `data-scroll-locked`, and the post-visit prompt's « is something modal open? » check and staff chat's `OPEN_MODAL` check do not see it. The post-visit Dialog can then open as the newest Radix layer, under the lock visually (z-50 vs z-55) but owning focus and pointer events: her first tap dismisses the invisible prompt (snoozing reviews until midnight) or presses one of its buttons. Fix: render an Overlay at z-[55] and expose `covering` from the context so the post-visit prompt and chat stand down.
- **Challenge note:** Verified in `node_modules/@radix-ui/react-dialog`: `RemoveScroll` (which sets `data-scroll-locked`) lives in `DialogOverlayImpl` only. `post-visit-review-popup.tsx:498` and `feedback-prompt-card.tsx:36` read `data-scroll-locked`/`data-sheet-open`; `staff-chat-provider.tsx:28` matches `data-slot="dialog-content"`, which the gate does not carry.

### Finding 5
- **Severity:** Major
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/team/clock-in-gate.tsx
- **Line:** 121
- **Anchor:** `onEscapeKeyDown={(e) => e.preventDefault()}`
- **Comment:** No `onOpenAutoFocus`, so Radix focuses the first button: « Se déconnecter » on an ordinary device (ring visible in shots C1/C2). The lock often rises from a 423 on a save or the minute check, so her next Enter signs her out and throws away the form DEV-10 exists to keep. On a borne, `autoFocus` on the pad sends digits she is still typing as a schéma try. Fix: focus the title (`tabIndex=-1`) on open as `ui/dialog.tsx` does, and drop `autoFocus` on the pad inside the lock.
- **Challenge note:** Verified — Radix FocusScope runs `focusFirst(removeLinks(...))`, so even the no-schéma lock skips « Créer mon schéma » and lands on « Se déconnecter »; `ui/dialog.tsx:230-251` focuses the title instead. `SchemaPad` takes keys 1–9 and Enter (`schema-pad.tsx:125-138`) and `punch-flow.tsx:207` passes `autoFocus`. Line re-anchored 118 → 121.

### Finding 6
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/Commands/PointageCommands.cs
- **Line:** 244
- **Anchor:** `return Result<PunchOutcome>.Success(new PunchOutcome(null, new PunchRefusal(`
- **Comment:** After a correct schéma, `ClearSchemaFailures()` / the rehash run in memory, then the `previous_departure_missing` refusal and the `declared_departure_invalid` failure return without `SaveChangesAsync`, so the reset is lost. 4 wrong tries → right schéma → « Retour » → one slip locks her schéma for 15 min. Fix: save `person` before returning either refusal; `A_Line_Left_Open_Yesterday_Asks…` asserts `SaveChangesAsync` Never and must assert the saved counter instead.
- **Challenge note:** Verified — lines 231–234 mutate `person`, lines 243–252 return before line 263's `SaveChangesAsync`; `PointageBorneTests.cs` asserts `SaveChangesAsync` `Times.Never` on this path.

### Finding 7
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** web/components/team/clock-in-gate.tsx
- **Line:** 53
- **Anchor:** `if (!enabled || inFlight.current) return`
- **Comment:** A refresh requested while one is in flight is dropped, not queued. If the minute/focus/423 read started just before her punch committed, the `team` broadcast refresh is discarded and the stale `locked: true` keeps the lock up to 60 s under « Arrivée enregistrée » (AC-2). Also: turning the pointage off mid-read lets the old answer land after the effect cleared it. Fix: a `pending` flag that runs one more read in `finally`, and ignore answers arriving after `enabled` went false.
- **Challenge note:** Verified, and the second race is worse than it reads: once `enabled` is false, `refresh` returns early, so a stale `locked: true` that lands after `setStatus(null)` keeps the lock up until reload or sign-out.

### Finding 8
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** web/components/team/clock-in-gate.tsx
- **Line:** 83
- **Anchor:** `useClinicRealtime(RealtimeResource.Team, () => void refresh())`
- **Comment:** The lock switch (`SetTeamModulesCommand`) and the opening hours (`UpdateClinicCommand`) broadcast `clinics`, and `enabled` watches only `attendanceEnabled`. After the admin turns « Bloquer l'app… » off, her screen stays locked up to 60 s while the server already accepts her saves. Fix: also refresh on `RealtimeResource.Clinics`.

### Finding 9
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** web/components/team/clock-in-gate.tsx
- **Line:** 142
- **Anchor:** `<PunchFlow`
- **Comment:** The lock's pad passes no `onEndingRefusal`. After `schema_locked` (5 wrong tries) `PunchFlow` sets `closing` for good, so once the 15-min schéma lock ends she still cannot retry from the lock — only sign out or reload. Fix: key/reset the flow when the status is re-read, or offer « Réessayer ».
- **Challenge note:** Verified — `punch-flow.tsx:110-113` sets `closing` and nothing in the gate resets it; the minute refresh re-renders the same `PunchFlow` instance.

### Finding 10
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/Commands/PointageCommands.cs
- **Line:** 210
- **Anchor:** `var open = await _entries.FindLatestOpenAsync(clinic.Id, person.Id, cancellationToken);`
- **Comment:** AC-8 declares only the latest open line. With two earlier days open (lines left before this ships, or an admin « Ajouter un pointage » without departure), the arrival declares the newer one and lands; the older stays « Départ non pointé » until a later morning — one forgotten day per arrival. Fix: resolve the oldest open line first and repeat the 409 until none is left; add a two-open-lines test.
- **Challenge note:** Was Finding 11.

### Finding 11
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/team/sign-out-prompt.tsx
- **Line:** 88
- **Anchor:** `<Button variant="outline" onClick={() => onOpenChange(false)}>`
- **Comment:** The footer cannot see the punch in flight. « Annuler » during the departure request unmounts `PunchFlow`, whose `mounted` ref drops `onDone`: the departure is saved, she is told nothing and stays signed in. « Se déconnecter sans pointer » has no busy state, and the ✕ during the 2 s confirmation plus the timer each call `logout()`. Fix: lift a `busy` flag from `PunchFlow` to disable both footer buttons, and guard `logout` with a `signingOut` ref.
- **Challenge note:** Was Finding 14. Verified — the ✕ path calls `logout()` without closing the dialog, so the 2 s effect stays armed and calls it again.

### Finding 12
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/team/sign-out-prompt.tsx
- **Line:** 68
- **Anchor:** `<div role="status" className="py-6">`
- **Comment:** The `role="status"` box is mounted together with its text, which screen readers rarely announce, while the focused pad unmounts and focus falls to the container — a screen-reader user most likely hears nothing before the logout 2 s later. Fix: keep one live region mounted from the start and write into it, or focus the « Départ enregistré » title.
- **Challenge note:** Was Finding 15.

### Finding 13
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/team/clock-in-gate.tsx
- **Line:** 148
- **Anchor:** ``toast.success(`Arrivée enregistrée à ${result.at}.`)``
- **Comment:** The unlock is announced only by a toast fired while the gate is still mounted, and Radix modal mode puts `aria-hidden` on the rest of the page, toaster included. Fix: `void onPunched().then(() => toast.success(…))`, or a `role="status"` line inside the gate.
- **Challenge note:** Was Finding 16.

### Finding 14
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/team/sign-out-prompt.tsx
- **Line:** 75
- **Anchor:** `<DialogBody>`
- **Comment:** The pad is a fixed 288 px square, and the dialog (85 % height on `md:`, 90 % sheet on a phone) loses ~165 px to header/footer/padding. Under ~470 px tall (landscape phone, the 380 px case) the pad never fits, and a stroke cannot scroll because the pad is `touch-none` — the departure cannot be drawn there. Fix: cap the pad by height in this host, e.g. `max-w-[min(18rem,40dvh)]` passed through to `SchemaPad`.
- **Challenge note:** Was Finding 17. Verified caps: `ui/dialog.tsx:98` (`max-h-[90dvh]`) and `:124` (`md:max-h-[85dvh]`); `schema-pad.tsx:166` (`aspect-square w-full max-w-72 touch-none`). Only reachable on a borne device with a short viewport.

### Finding 15
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/team/punch-flow.tsx
- **Line:** 182
- **Anchor:** `disabled={busy || !declared || tooEarly}`
- **Comment:** `TimeInput` commits only on the 4th digit or on blur, so typing « 930 » leaves « Valider » grey and Enter does nothing (a disabled submit blocks implicit submission), with no reason shown. Fix: enable « Valider » once the field has digits and validate on submit; the blur before the click already commits.
- **Challenge note:** Was Finding 18. Verified in `ui/time-field.tsx:108-114`.

### Finding 16
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/team/punch-flow.tsx
- **Line:** 135
- **Anchor:** `onPointerDown={onActivity}`
- **Comment:** On the borne only a pointer press resets the 30 s idle timer; typing the hour on the on-screen or a physical keyboard does not. If recalling yesterday's departure takes more than 30 s, the panel jumps back to the tiles, her answer is lost and she redraws the schéma. Fix: also call `onActivity` on the time field's `onChange`/`onKeyDown` (or the form's `onInput`).
- **Challenge note:** Was Finding 19. Verified `app/pointage/page.tsx:23` (`IDLE_MS = 30_000`) and `:271` (`onActivity={armIdle}`).

### Finding 17
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/team/clock-in-gate.tsx
- **Line:** 124
- **Anchor:** `className="fixed inset-0 z-[55] flex h-dvh overflow-y-auto bg-background p-4 outline-none print:hidden"`
- **Comment:** At z-[55] the lock sits above `ClientVersionGate` (z-50), which should be the top-most takeover; a minimum-version bump during a locked session hides « Mise à jour requise » under the lock and the borne punch fails with a hidden 426. Fix: raise `ClientVersionGate` above 55, or don't cover once a 426 was seen.
- **Challenge note:** Was Finding 20. Verified `client-version-gate.tsx:88` (`fixed inset-0 z-50`).

### Finding 18
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/team/clock-in-gate.tsx
- **Line:** 95
- **Anchor:** `{covering && <ClockInGate status={status} onPunched={refresh} />}`
- **Comment:** `StaffChatNotices` (z-[60]) hides only for the session lock, so chat notices draw above this lock and stay clickable; their quick replies are chat writes (not exempt) and come back 423, and « Ouvrir » opens the panel under the lock. Fix: expose `covering` from `ClockStatusContext` and have `StaffChatNotices` return null while it is true (same seam as Finding 4).
- **Challenge note:** Was Finding 21. Verified `staff-chat-notices.tsx:36` (`if (locked || …) return null` — session lock only) and `:41` (`z-[60]`).

### Finding 19
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/team-modules-card.tsx
- **Line:** 86
- **Anchor:** `<div className="flex items-center justify-between gap-3 ps-4">`
- **Comment:** The « Bloquer l'app… » row sits 12 px under « Pointage » with no `border-t pt-3` like its siblings; switch centres are 36 px apart, so the 44 px coarse-pointer areas overlap by 8 px and a thumb on « Pointage »'s lower edge flips the lock for every secretary. Fix: `border-t pt-3` (or `coarse:pt-2`) like the rows above.
- **Challenge note:** Was Finding 22. Verified — `CardContent` is `space-y-3`, `Switch` is `h-6` with `touch-target`.

### Finding 20
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/BackgroundJobs/AttendanceDepartureReminderJob.cs
- **Line:** 59
- **Anchor:** `foreach (var clinic in await _clinics.GetAllAsync())`
- **Comment:** `GetAllAsync` loads every clinic with `.Include(c => c.Users)`, tracked, 288 times a day, while the job needs only `AttendanceEnabled` and `WorkingHoursJson`; each reminder save then change-tracks that whole set. Fix: a narrow no-tracking read of attendance-enabled clinics (Id + hours); compute `dayStart` once before the loop.
- **Challenge note:** Was Finding 23. Verified `ClinicRepository.cs:38-43`.

### Finding 21
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/AttendanceLockEvaluator.cs
- **Line:** 92
- **Anchor:** `SubscriptionStateReader.Read(subscription, ClinicClock.ClinicToday()).AllowsWrites`
- **Comment:** Every fact is judged against the `nowUtc` passed in except the subscription, which reads the wall clock; around Tunis midnight one evaluation mixes two days, and a fixed-date test gets this fact from the real date. Fix: thread `nowUtc` and use `ClinicClock.ClinicToday(nowUtc)`.
- **Challenge note:** Was Finding 24. `ClinicClock.ClinicToday(DateTime? nowUtc = null)` already takes it.

### Finding 22
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.UnitTests/Api/AttendanceLockMiddlewareTests.cs
- **Line:** 23
- **Anchor:** `private static readonly string AllDay = "[" + string.Join(",",`
- **Comment:** The « always inside opening hours » fixture ends at `23:59` and `IsInsideOpeningHours` uses `< To`, so `A_Write_Before_Her_Arrival_Is_Refused_423…` (and `ClockStatusQueryTests.cs:24`'s copy) fails from 23:59:00 to 23:59:59 Tunis time. Root cause: the middleware and `GetMyClockStatusQuery` read `DateTime.UtcNow` with no seam. Fix: give both a « now » input as the job has, and pin the tests to a fixed instant.
- **Challenge note:** Was Finding 25. Verified `AttendanceLockRules.cs:78` (`< hours.To`), `AttendanceLockMiddleware.cs:34` and `GetMyClockStatusQuery.cs:60` (`DateTime.UtcNow`).

### Finding 23
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Domain/Entities/AttendanceEntry.cs
- **Line:** 133
- **Anchor:** `/// <summary>` (second of two, above `public void DeclareDeparture(`)
- **Comment:** `DeclareDeparture` was inserted between `Correct`'s « Corriger » `<summary>` and `Correct`, so `DeclareDeparture` carries two summaries and `Correct` none. Fix: move the « Corriger » block back above `public void Correct(`.
- **Challenge note:** Was Finding 26. Verified — summaries at lines 129 (« Corriger ») and 133, `DeclareDeparture` at 137, `Correct` at 152 with no doc. Re-anchored 134 → 133.

### Finding 24
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Domain/Repositories/IAuditEntryRepository.cs
- **Line:** 79
- **Anchor:** `/// When <paramref name="userId"/> last saved something in the inclusive window, ignoring the entity types named —`
- **Comment:** Same splice: `GetLastSaveAtAsync` sits between `GetActivityRowsAsync`'s doc and its signature, so it inherits the vendor-console text and `GetActivityRowsAsync` has none. Fix: move the new method and its summary above that block.
- **Challenge note:** Was Finding 27. Verified lines 65–91. Re-anchored 80 → 79.

### Finding 25
- **Severity:** Minor
- **Category:** Breaking Changes
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/CLAUDE.md
- **Line:** 93
- **Anchor:** ``| `api/pointage` | `PointageController` | **The borne** (`pointage-borne`): `GET status` and `POST punch`, both `[AllowAnonymous]` ``
- **Comment:** Still lists two anonymous actions; misses the authenticated `GET me/clock-status` (`AnyClinicRole`, reads the borne cookie), the class-level `[AllowsBeforeClockIn]`, and the 409 `previous_departure_missing` / 400 `declared_departure_invalid` codes. Fix: update the row.
- **Challenge note:** Was Finding 34. The middleware-order paragraph (line 249) already names `AttendanceLockMiddleware`; only the controller row is stale.

### Finding 26
- **Severity:** Minor
- **Category:** Breaking Changes
- **Verdict:** Confirmed
- **File:** web/lib/CLAUDE.md
- **Line:** 16
- **Anchor:** ``- **`onRelayRefused`** (`client.ts`, `clinic-pc-copy` D20)``
- **Comment:** Lists every client refusal hook but not the new `onClockInRequired` (423 `clock_in_required`) nor `ApiError.details`. Fix: add both beside `onRelayRefused`.
- **Challenge note:** Was Finding 35.

### Finding 27
- **Severity:** Suggestion
- **Category:** Business Logic
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.API/BackgroundJobs/AttendanceDepartureReminderJob.cs
- **Line:** 74
- **Anchor:** `await _notificationGenerator.AttendanceDepartureMissingAsync(`
- **Comment:** AC-6 « once per line » rests on the bell row, which the generator writes inside `SafelyAsync` (errors swallowed) while the push decorator still fans out. If the insert fails, the push goes, the next tick finds no row, and she is pushed again every 5 min until midnight. Fix: push only when the row was written, or dedupe on the push outbox too.
- **Challenge note:** Was Finding 10. Verified (`NotificationGenerator.cs:758-770`, `SafelyAsync` catches all; decorator `:244-252` fans out unconditionally). Lowered Minor → Suggestion: it needs the notification insert to fail persistently while the push-delivery insert, in the same database, keeps succeeding — and the same unconditional fan-out is the decorator's existing pattern for every category.

### Finding 28
- **Severity:** Suggestion
- **Category:** Business Logic
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Team/Commands/PointageCommands.cs
- **Line:** 237
- **Anchor:** `if (!departure && open is not null && open.LocalDay < today)`
- **Comment:** The spec (Out of Scope) and the blueprint say the lock lifts during an internet cut and « the hours are declared the next morning », but only an open line is ever declared. If the cut began before her arrival, no line exists, nothing is asked next morning, and the sheet shows nothing for that day. Fix: narrow the spec wording to « an arrival already pointed », or add a missing-day declaration.
- **Challenge note:** Was Finding 12. Lowered Minor → Suggestion: the sentence sits in the spec's `## Out of Scope` (« A borne that works during an internet cut »), so a missing-day declaration was never promised as an AC; the realistic fix is the spec wording.

### Finding 29
- **Severity:** Suggestion
- **Category:** Breaking Changes
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** web/public/sw.js
- **Line:** 58
- **Anchor:** `const url = conversation`
- **Comment:** The bell row (`targetKind` `Attendance`) opens `/mon-profil`, but the phone push opens `/`: the push payload carries only `conversation`/`appointmentId`, and `sw.js` and the Android shell route on those two only. Fix: route `category === "AttendanceDepartureMissing"` to `/mon-profil` in `sw.js`, or add a generic `url` to the payload for both shells.
- **Challenge note:** Was Finding 13. Re-anchored 63 → 58. Lowered Minor → Suggestion: every push category without a conversation or an appointment already lands on `/` (pre-existing routing), and the reminder's real action — punching the departure — happens at the borne, not on any app screen.

### Finding 30
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Team/Queries/GetMyClockStatusQuery.cs
- **Line:** 53
- **Anchor:** `var switches = await _clinics.GetAttendanceSwitchesAsync(me.ClinicId, cancellationToken);`
- **Comment:** Polled every minute, on focus and on every `team` broadcast by every signed-in screen; for a tracked secretary it reads the switches and today's lines, then `_lock.IsLockedAsync` reads the switches and `HasLineOnAsync` again. Fix: let the evaluator take facts already known so each poll does not run the same queries twice.
- **Challenge note:** Was Finding 28. Lowered Minor → Suggestion: two small indexed reads per tracked secretary per minute; untracked accounts and other cabinets return at line 54 before any of it.

### Finding 31
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Team/AttendanceLockRules.cs
- **Line:** 17
- **Anchor:** `public const string PreviousDepartureMissing = "Votre départ d'hier n'a pas été pointé. Indiquez votre heure de départ.";`
- **Comment:** Always says « d'hier », but the open line can be Friday seen on Monday; `punch-flow.tsx` works around it with its own `dayPhrase`, so one refusal has two wordings. Fix: make the sentence day-neutral or build it from `open.LocalDay`; align the client's `tooEarly` sentence with `DeclaredDepartureInvalid`.
- **Challenge note:** Was Finding 29. Lowered Minor → Suggestion: the web never renders this sentence — `punch-flow.tsx:101-107` turns the 409 into the declaration step from `previousDayOf(err.details)` and prints its own heading — so no user reads the wrong « hier ». It is still the API's error text, and AC-8 quotes « d'hier » literally.

### Finding 32
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Team/AttendanceLockRules.cs
- **Line:** 55
- **Anchor:** `public static (TimeSpan From, TimeSpan To)? HoursOn(DateOnly day, string? workingHoursJson)`
- **Comment:** Fourth hand-written copy of « find the weekday in `WorkingHoursJson` and parse From/To » (`WorkingHoursResolver.IsWithin`, `RelayAlertRules.OpenedAtUtc`, `RelayUpdateHours.Window`). The rules around it differ; the lookup should be one helper on `WorkingHoursSerializer` (e.g. `TryGetDay`).
- **Challenge note:** Was Finding 30. Verified copies at `WorkingHoursResolver.cs:124-135` and `RelayAlertRules.cs:187-191`. Lowered Minor → Suggestion: each copy wraps a different rule (break, unset hours, `to <= from`), so this is a refactor of shared parsing, not a divergent business rule.

### Finding 33
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** web/components/team/punch-flow.tsx
- **Line:** 37
- **Anchor:** `function dayPhrase(dayKey: string): string {`
- **Comment:** The `du …` branch repeats `attendanceDayLabel` (`lib/api/pointage.ts:242`). Fix: `` `du ${attendanceDayLabel(dayKey)}` ``.
- **Challenge note:** Was Finding 31. The agent's « already imported here » is wrong (punch-flow imports other names from that module, not this one); the duplication is real. Lowered Minor → Suggestion — one format string.

### Finding 34
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Team/AttendanceLockEvaluator.cs
- **Line:** 48
- **Anchor:** `public async Task<AttendanceLockFacts> FactsAsync(User user, DateTime nowUtc, CancellationToken cancellationToken)`
- **Comment:** Public, yet returns placeholders for every fact it never read (an admin reports `AttendanceEnabled: true`, `InsideOpeningHours: true` whatever the cabinet says). Only `IsLockedAsync` uses it in production. Fix: make it private (or name/document it as « facts read until the lock lifted ») and test through `IsLockedAsync`.
- **Challenge note:** Was Finding 32. Lowered Minor → Suggestion: the early exit is the class's documented design (« the first one that does ends the read »); only the visibility misleads.

### Finding 35
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.UnitTests/Features/Team/PointageBorneTests.cs
- **Line:** 279
- **Anchor:** `public async Task A_Declared_Hour_Not_After_The_Arrival_Is_Refused_By_Name_And_Nothing_Is_Written(string declared)`
- **Comment:** Two of the four cases (`"8h30"`, `"24:10"`) are unreadable hours, not hours before the arrival, so the name hides the malformed-input branch. Fix: split into two theories or rename.
- **Challenge note:** Was Finding 33. Lowered Minor → Suggestion — naming only; both branches are covered.

### Finding 36
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/BackgroundJobs/AttendanceDepartureReminderJob.cs
- **Line:** 55
- **Anchor:** `if (_relayLocal.IsPcDeSecours)`
- **Comment:** Unreachable: the only profile where `IsPcDeSecours` is true has `runsClinicJobs: false`, so the job is never registered there, and the Program.cs comment « (the job checks) » credits the wrong guard. Drop it, or keep it with a one-line « deliberate extra safety » note.
- **Challenge note:** Verified `DeploymentProfile.cs:581-583` (`mirrorsCloudClinic: true` · `runsClinicJobs: false`) and `Program.cs:1289-1296`.

### Finding 37
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Common/Interfaces/IRelayLocalStatus.cs
- **Line:** 15
- **Anchor:** `bool IsPcDeSecours => false;`
- **Comment:** A default interface body lets a future implementation silently answer « not a PC de secours », which on a real one would lock secretaries no borne can reach. One production implementation exists: make it abstract and update the test fake.

### Finding 38
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/Middleware/AttendanceLockMiddleware.cs
- **Line:** 56
- **Anchor:** `private static bool IsRead(string method) =>`
- **Comment:** Fourth identical private `IsRead` (+ `ApiPrefix`) after `DemoReadOnlyMiddleware`, `RelayLeaseGateMiddleware`, `SubscriptionGateMiddleware`. One shared helper keeps every write gate agreeing on what a read is.

### Finding 39
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Team/Commands/PointageCommands.cs
- **Line:** 244
- **Anchor:** `return Result<PunchOutcome>.Success(new PunchOutcome(null, new PunchRefusal(`
- **Comment:** Hand-builds what the file's `Refused(...)` helper (line 296) does, only because the helper lacks `PreviousDay`. Add `PreviousDayDto? previousDay = null` to `Refused` and call it.

### Finding 40
- **Severity:** Suggestion
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/team/attendance-panel.tsx
- **Line:** 308
- **Anchor:** `<span className="rounded bg-warning-wash px-1 text-2xs font-medium text-warning-ink">Déclaré</span>`
- **Comment:** On a corrected + declared line (shot A14) the chip comes after the full-width correction reason and wraps away from the amber hour it describes. Render it right after « Corrigé ».
- **Challenge note:** Reachable: an admin correction made while the line was still open, then her declaration, leaves both `IsCorrected` and `IsDeclared` true.

### Finding 41
- **Severity:** Suggestion
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** web/components/team/attendance-panel.tsx
- **Line:** 201
- **Anchor:** `hint={hoursHint(totals)}`
- **Comment:** « dont 1 départ déclaré · sans la journée en cours » in a `StatStrip className="grid-cols-3"` wraps to ~5 lines at 320 px and misaligns « Mes présences ». Put the declared count in its own cell, or drop the `grid-cols-3` override so the strip's own ladder applies.
- **Challenge note:** The `grid-cols-3` override (line 196) bypasses `StatStrip`'s two-column base ladder; the longer hint is what part 3 added.
