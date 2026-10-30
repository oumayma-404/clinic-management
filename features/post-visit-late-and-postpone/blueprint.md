# Retard et report — blueprint

> Status: **blueprint** (2026-10-29, `/think-solution`). Option **A** chosen by the owner. Nothing implemented.
> Options page (4 options, mockups, cases): https://claude.ai/artifact/KegSgmBkdg6T52NNtwEhms
> Size: ~20 files, backend + frontend + one migration. A small feature, not a quick fix.

## What it does

- The « Séance terminée » popup asks **Plus tard · Reporter · Absent · Venu**, on one line.
- **Reporter** → a date + heure → the RDV moves through the existing reschedule (statut « Planifié », popup moved,
  rappel re-sent, Google). **Pas encore de date** → the patient goes on the Liste d'attente and the RDV is cancelled
  with the motif « Reporté par le patient ». Neither counts as an absence.
- **Retard** = minutes late, optional. Recorded after « Venu » in the popup, from the agenda ⋯ (live or after),
  from « À clôturer » ⋯ « Venu en retard… », or in the edit dialog. Shown as « +15 » on the agenda block.
- **A retard never moves the RDV.** Recorded during the séance, it pushes « Séance passée », « À clôturer » and the
  popup to booked end + retard, once.
- **« Absent » can become « Terminé »**: the very late patient who turns up after being marked absent.

## Decisions (reversible — say if wrong)

| Decision | Value | Why |
|---|---|---|
| Field | `Appointment.LateArrivalMinutes` (`int?`, 1–240, null = not recorded) | « en retard » already means an overdue échéance or treatment; a distinct name keeps the two apart in code |
| Who may carry it | `InProgress` · `AwaitingClosure` · `Completed` | the patient is or was there. Every exit to another statut erases it |
| A retard writes no statut | popup: like its « Venu » (writes nothing). « À clôturer »: sent with `Completed`, like its own « Venu » | each surface keeps the presence semantics it already has |
| A recorded retard answers « venu ? » in the popup | it opens on the fiche step | asking again is the nag the client reported |
| The booked slot never grows | `AppointmentEndDateTime` (trigger), the exclusion constraint and `FindCollisionAsync` untouched | growing it collides with the next patient |
| Chips | 5 · 10 · 15 · 20 · 30 · Autre | one tap for the usual cases |
| Retard writes send no `version` | like the popup's « Absent » | a single-field fact; a 409 on a one-tap chip is noise |
| « Reporter » sends the `version` | through `useConflict` | it moves a booking a colleague may be moving too |
| « Reporter » offered from | `Scheduled` · `Confirmed` · `InProgress` · `AwaitingClosure` | `Completed` is a date correction (edit dialog) · `NoShow` keeps its absence · `Cancelled` reactivates |
| « Pas encore de date » order | waiting-list entry first, then the cancel | the safe half lands first, as `waitingListApi.promote` already orders it: never a cancelled RDV with nobody on the list |
| Popup footer | no icons | ≈ 345 px without, ≈ 411 px with, in a 400 px footer (`h-9 px-4` / `has-[>svg]:px-3`, Geist 14 px). Re-measure in the eye pass |
| Tablet toast | Venu · Absent · Reporter via `toast.custom`; the retard from the agenda ⋯ | sonner's `toast()` holds two buttons |
| « Absent » → « Terminé » | allowed; the slot is re-checked first | the plan links `PlanBookingRelease` cleared at « Absent » are **not** restored |

## Part 1 — The retard (domain + API)

- `api/ClinicManagement.Domain/Entities/Appointment.cs`
  - `LateArrivalMinutes` + `RecordLateArrival(int? minutes)`: refuses outside `LateArrivalStatuses` or outside
    1–240 (French `InvalidOperationException`); `null` clears on any statut.
  - `ExpectedEnd(start, duration, lateArrivalMinutes)` (static), `ExpectedEndUtc`, `HasEndedBy(nowUtc)`: **the one
    « end of séance » rule**.
  - Erase it in `Cancel`, `MarkAsNoShow`, `Reactivate`, and in `Reschedule` whenever the start moves or the statut
    falls back to `Scheduled`. Invariant: `LateArrivalMinutes != null ⇒ Status ∈ LateArrivalStatuses`.
- `Features/Appointments/Commands/UpdateAppointmentCommand.cs`
  - Tri-state `LateArrivalMinutes` + `[JsonIgnore] LateArrivalMinutesSpecified` (the `Notes` shape).
  - Applied **after** the date move and the statut switch, so `{ status: "Completed", lateArrivalMinutes }` lands
    and `{ status: "NoShow", lateArrivalMinutes }` is refused.
  - The post-commit review branch for `Scheduled/Confirmed/InProgress` passes `appointment.ExpectedEndUtc`. The
    « just completed → now » branch stays `now`.
  - DTO projection.
- `CreateAppointmentCommand.cs`, `CreateRecurringSeriesCommand.cs`: review end → `ExpectedEndUtc` (same value at
  creation; one rule, one place). Create's projection.
- `Queries/GetAppointmentsQuery.cs`, `Queries/GetAppointmentQuery.cs`: projection.
- `DTOs/AppointmentDto.cs`: `int? LateArrivalMinutes`.
- `Infrastructure/Repositories/AppointmentRepository.cs`: the two in-memory residuals (`GetRunningNotStartedAsync`,
  `GetElapsedOpenAsync`) → `HasEndedBy`. SQL windows unchanged (30-day lookback ≫ 240 min).
- `Features/Appointments/VisitClosure.cs` + `VisitClosureReader.cs`: `VisitClosureInput` gains
  `int? LateArrivalMinutes = null` (defaulted, so every existing caller and test compiles); `EndUtc` →
  `Appointment.ExpectedEnd(...)`.
- `Common/Csv/ExportTables.cs`: a « Retard (min) » column.
- Migration `AddAppointmentLateArrivalMinutes` (nullable `int`).
- `web/lib/api/types.ts` (`lateArrivalMinutes?: number | null`), `web/lib/api/appointments.ts` (update payload,
  tri-state doc like its neighbours).
- `web/components/appointment-labels.ts`: `LATE_ARRIVAL_STATUSES` (mirror of the domain set) +
  `POSTPONABLE_STATUSES` (UI only).

## Part 2 — Shared pieces (create)

- `web/components/appointment-late-arrival-chips.tsx` — `LateArrivalChips`: the chips + « Autre » (numeric,
  1–240), optional « Aucun ». A control only; each host decides when it saves.
- `web/components/appointment-postpone-form.tsx` — `PostponeForm`: the next open days as chips
  (`lib/working-hours.ts`) + a calendar + `TimeField` (24 h) + the `useAppointmentOverlap` status; future only.
  Branches on `slot_taken` / `outside_working_hours` → the server's sentence + « Reporter quand même » (merged
  overrides, the create dialog's `CreateOverrides` shape). « Pas encore de date » holds a `createdEntryIdRef`.
  Body + footer only, so it fits inside the popup's own dialog.
- `web/components/appointment-postpone-dialog.tsx` — `PostponeAppointmentDialog`: `Dialog mobile="sheet"` around
  the form; reads the appointment on open (fresh version).

## Part 3 — Hosts (modify)

- `web/components/post-visit-review-popup.tsx`
  - Step `"postpone"`. Step 1 footer: Plus tard (ghost, `sm:me-auto`) · Reporter · Absent · Venu, no icons.
  - `ResolvedVisit` keeps the fetched `AppointmentDto` (version, start, duration, doctor, `lateArrivalMinutes`).
  - Fiche step when `cameFor === id || alreadyCompleted || lateArrivalMinutes != null`.
  - Fiche step: « Signaler un retard » (ghost, clock) → chips → saves `{ lateArrivalMinutes }` at once; then shows
    « Retard de 15 min » with a clear control.
  - Reporter / no-date success: `setDismissed(true)` **without** `snoozeAll()`, the « Absent » rule.
  - Tablet: a `toast.custom` card with the same `id` / `duration` / `onDismiss` / `onAutoClose` → `handleLater`;
    « Reporter » dismisses the toast, then opens `PostponeAppointmentDialog`.
- `web/components/appointment-quick-actions.tsx`: « Reporter… » (`POSTPONABLE_STATUSES`) opens the dialog as a
  **sibling** of the popover; a « Retard » row with the chips (`LATE_ARRIVAL_STATUSES`, on the stored statut)
  saves at once.
- `web/components/visits/visit-closure-list.tsx`: a ⋯ on Presence rows, both trees, named after the patient →
  « Venu en retard… » (one write: `{ status: "Completed", lateArrivalMinutes }`) and « Reporter… ».
- `web/components/edit-appointment-dialog.tsx`: the chips (with « Aucun ») under the statut radios while the
  **selected** statut is in `LATE_ARRIVAL_STATUSES`; sent only when changed and admitted, otherwise omitted.
- `web/components/appointment-calendar.tsx`: a « +15 » pill (clock icon, `aria-label` « Arrivé avec 15 min de
  retard ») on the grid block and the phone block; not on month chips.

## Part 4 — « Absent » → « Terminé »

- `Appointment.AllowedTransitions[NoShow]` += `Completed`. `MarkVisitCompleted` still answers `Contradicted` for
  `NoShow`: a fiche alone does not overturn a person's « Absent ».
- `UpdateAppointmentCommand`: the collision guard also runs when a statut change makes the row hold its slot again
  (from `NoShow`/`Cancelled` to any slot-holding statut), honouring `AllowOverlap`. Reactivation gets the same named
  refusal; today it falls to the 23P01 backstop's bare 409.
- Nothing else moves: the review re-creates itself on « became Completed », reminders stay voided, the closure row
  returns at « Fiche ».

## Key signatures (sketch)

```csharp
// Appointment.cs
public int? LateArrivalMinutes { get; private set; }
public const int MaxLateArrivalMinutes = 240;
public static readonly IReadOnlyCollection<AppointmentStatus> LateArrivalStatuses =
    new[] { AppointmentStatus.InProgress, AppointmentStatus.AwaitingClosure, AppointmentStatus.Completed };
public static DateTime ExpectedEnd(DateTime startUtc, TimeSpan duration, int? lateArrivalMinutes);
public DateTime ExpectedEndUtc => ExpectedEnd(AppointmentDateTime, Duration, LateArrivalMinutes);
public bool HasEndedBy(DateTime nowUtc) => ExpectedEndUtc <= nowUtc;
public void RecordLateArrival(int? minutes);

// UpdateAppointmentCommand.cs
public int? LateArrivalMinutes { get => _lateArrivalMinutes; set { _lateArrivalMinutes = value; LateArrivalMinutesSpecified = true; } }
[JsonIgnore] public bool LateArrivalMinutesSpecified { get; private set; }
```

```ts
// appointment-labels.ts
export const LATE_ARRIVAL_STATUSES = ["InProgress", "AwaitingClosure", "Completed"] as const
export const POSTPONABLE_STATUSES = ["Scheduled", "Confirmed", "InProgress", "AwaitingClosure"] as const

// appointment-late-arrival-chips.tsx
export function LateArrivalChips(props: {
  value: number | null; onChange: (minutes: number | null) => void; withNone?: boolean; disabled?: boolean
})

// appointment-postpone-form.tsx
export function PostponeForm(props: {
  appointment: AppointmentDto; onDone: (outcome: "moved" | "waiting-list") => void; onBack?: () => void
})

// appointment-postpone-dialog.tsx
export function PostponeAppointmentDialog(props: {
  appointmentId: string; open: boolean; onOpenChange: (open: boolean) => void; onDone?: () => void
})
```

No DI change: no new service, no new `Features/<Area>` folder (so no new realtime key), no new endpoint.

## Blast radius (first pass — copy into `blast-radius.md` before the first edit)

| # | Touching | Other consumers | Verdict |
|---|---|---|---|
| 1 | `Appointment` new column + 4 erasing mutators | job, both update paths, archive | must change — invariant test |
| 2 | `UpdateAppointmentCommand` tri-state field | every caller omits it today | unaffected — omitted = unchanged |
| 3 | `AppointmentDto` projections | `GetAppointments`, `GetAppointment`, `Create`, `Update` | must change — all four, source-scan guard |
| 4 | « end of séance » | job residuals ×2, review timing ×4, `VisitClosureInput.EndUtc` | must change — all through `ExpectedEnd` (the « now » branch stays) |
| 5 | the booked slot | trigger column, exclusion constraint, `FindCollisionAsync`, duplicate-merge overlap | unaffected — deliberately not touched |
| 6 | `AllowedTransitions[NoShow]` | `ManualNextStatusesFrom` → 4 projections, `MANUALLY_SETTABLE_STATUSES` fallback | must change — `AppointmentStatusTransitionTests`, `AppointmentManualStatusTests` |
| 7 | collision guard trigger (« regains its slot ») | reactivation of a cancelled RDV | must re-test |
| 8 | `PlanBookingRelease` on « Absent » | « Absent » → « Terminé » | unaffected — links stay released, said in notes |
| 9 | `post-visit-review-popup.tsx` | `e2e/lib/goto.ts` (« Plus tard » by name), `e2e/specs/booking-browser.spec.ts` | must change · the e2e: must re-test |
| 10 | `appointment-quick-actions.tsx` + calendar block | agenda grid, phone agenda | must change |
| 11 | `visit-closure-list.tsx` | table + card trees | must change — both trees |
| 12 | `edit-appointment-dialog.tsx` payload | its 409 / overlap / hours confirmations | must change — send only when changed |
| 13 | `ExportTables.Appointments` | its header test, if one pins the columns | must change |
| 14 | clinic archive (`ClinicArchiveStore`, EF-metadata-driven) | restore of an older archive | unaffected — new scalar travels; old archives restore null |
| 15 | PC de secours copy | its row feed | must re-test |

15 rows: past the « ~12 » scope signal on purpose, which is why this is a small feature and not a quick fix.

## Pitfalls

- The retard is applied **last** in the handler. Applied before the statut switch, `{ Completed, 15 }` from
  `Scheduled` would be refused and `{ NoShow, 15 }` would land and then be erased.
- Never fold the retard into the booked slot (trigger column, exclusion constraint, `FindCollisionAsync`, the
  duplicate-merge overlap). The slot is what blocks the chair.
- `ExpectedEndUtc` has no setter, so EF ignores it; confirm the model snapshot did not pick it up.
- Migration: the `xmin` scaffold trap; build with `BaseOutputPath=<temp>`; `verify-schema` before and after.
- A catch that returns a `Result` keeps `when (ex is not ConflictException)`.
- The chips send no version **on purpose**. Do not « fix » them into `useConflict`.
- `PostponeForm` re-enters on each confirmation (slot taken, out of hours): merged overrides, and
  `createdEntryIdRef` so a retry never adds a second Liste d'attente entry.
- In the ⋯ popover, render the dialog as a **sibling**: Radix unmounts the content on close, and a dialog inside
  it dies in the same tick.
- `DialogFooter` does not wrap from `md:`: a fifth control or a longer label overflows rather than wraps.
  Re-measure at 448 px whenever a label changes.
- « Plus tard » keeps its exact name: `e2e/lib/goto.ts` clicks it.
- Toast buttons reach 44 px by growing (`h-11`), never `.touch-target`: three siblings side by side.
- Date chips come from the clinic's open days, never hard-coded weekends. Build the ISO the way the edit dialog does.
- A devis visit marked « Absent » lost its plan links; « Terminé » does not restore them, so the fiche links the act
  by hand. Say it in `notes.md`.
- Branch on `slot_taken` / `outside_working_hours`, never on the French sentence.

## Test strategy

**Unit — the full suite, unfiltered**

- `AppointmentLateArrivalTests`: only the three statuts admit it; 1–240; null clears anywhere; each eraser;
  `ExpectedEnd`; the invariant over every statut × every mutator.
- `UpdateAppointmentLateArrivalTests`: omitted = unchanged; null = cleared; set on `AwaitingClosure` keeps the
  statut; `{ Completed, 15 }` from `AwaitingClosure`; `{ NoShow, 15 }` refused; `Scheduled` refused; `InProgress`
  re-times the review to `ExpectedEndUtc`; `AwaitingClosure` does not.
- `HasEndedBy` drives both job residuals; `VisitClosureRules.IsClosable` is false at the booked end when a retard
  pushes it.
- Transition tables updated; `NoShow → Completed` and `Cancelled → Scheduled` into a taken slot return `slot_taken`
  and honour `AllowOverlap`.
- `LateArrivalStatusMirrorTests` (both directions, the `OdontogramConditionMirrorTests` shape); the projection
  source scan extended to `LateArrivalMinutes`.
- `ExportTables` header test, if one pins the columns.

**Gates:** `verify-schema` diff around the migration · `check:responsive` + `tsc --noEmit` + `npm run build` ·
`node e2e/scripts/check-coverage.mjs`.

**Browser — `/test-in-browser`, own test records only**

| Row | Check |
|---|---|
| P1 | Popup step 1 on one line at 448 px, in a 730 px-tall window |
| P2 | Reporter to a free slot → agenda moved, popup gone, row gone from « À clôturer » |
| P3 | Reporter into a taken slot → named clash + « Reporter quand même » |
| P4 | Pas encore de date → « Annulé » + motif, entry on the Liste d'attente; a forced cancel failure then retry adds no second entry |
| P5 | Venu → Signaler un retard 15 → « +15 » on the block, no second popup |
| P6 | Live: a 5-min RDV starting now, retard 5 → « Séance passée » and the popup only after 10 min, popup on the fiche step |
| P7 | « À clôturer » ⋯ Venu en retard → the row moves to « Fiche » |
| P8 | Edit dialog: change, « Aucun » clears, statut → Absent hides the chips and the save erases the retard |
| P9 | Absent → Terminé on a free slot, then on a re-booked one |
| P10 | Tablet (coarse): toast with 3 answers at 44 px; Reporter opens the sheet |
| D | Create/edit dialogs' overlap + hours confirmations · agenda drag-move · reactivating a cancelled RDV · « Plus tard » snooze · `booking-browser.spec.ts` |

## After shipping

- `web/components/CLAUDE.md` rows for the popup, the quick actions, the closure list and the calendar block.
- `features/post-visit-late-and-postpone/notes.md` (what shipped, the « never moves » and « slot never grows » rules).
- One line in the root `CLAUDE.md` « Architecture notes ».
