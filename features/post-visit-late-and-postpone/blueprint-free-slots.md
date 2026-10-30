# « Reporter » shows the free slots — blueprint

> Status: **blueprint** (2026-10-30, `/think-solution`). Option **A** chosen by the owner. Nothing implemented.
> Options page: https://claude.ai/artifact/7ddf59SwLkmbmADNeTxifH
> Same branch as the retard/report feature (not merged yet).

## Summary

The server lists a practitioner's free start times over a window, computed with **the same predicates the save
uses** (hours, break, congé, busy slots), so the list can never offer a time the save refuses. The « Reporter »
form shows the next open days with how many free slots each has, then the free times of the chosen day, with
« Autre heure… » keeping the typed time. The computing core is the `FreeSlotFinder` that
`features/patient-online-booking/blueprint.md` already plans; this builds it once for both.

## Decisions (reversible — say if wrong)

| Question | Choice | Why |
|---|---|---|
| Time step | quarter hours on the clock (:00 :15 :30 :45) | the agenda's drag snaps to 15; online booking plans the same |
| Window | the next 14 days; the calendar fetches any later single day | covers « la semaine prochaine » without a long list |
| Practitioner | the visit's own | changing practitioner stays an edit-dialog decision |
| Busy times | not listed; each day chip carries « N libres » / « Complet » / « Congé » | only what can be picked |
| A time not in the list | « Autre heure… » reveals today's `TimeField` | the existing « Reporter quand même » prompts still cover a deliberate overlap or closed hour |
| No hours configured anywhere | no list (`Unrestricted`); the typed time only, with one sentence | the server treats « no hours » as open around the clock; 96 chips help nobody |
| Today | only starts still ahead (next quarter hour) | a past time is refused anyway |
| Live refresh | none: fetched on open and on day change | the save stays the authority; a slot taken meanwhile meets the existing `slot_taken` refusal + « Reporter quand même ». The progress job broadcasts `appointments` every minute, which would re-render the grid under the user's finger |

## Server

### `Application/Features/Appointments/FreeSlotFinder.cs` (create)

A static class beside `AppointmentScheduling`, the same idiom (repositories passed in, no DI registration).

```csharp
public enum FreeDayState { Open, Full, Closed, Leave, Unrestricted }

public sealed record FreeDay(DateOnly Date, FreeDayState State, IReadOnlyList<TimeOnly> Starts);

public static class FreeSlotFinder
{
    public static readonly TimeSpan Step = TimeSpan.FromMinutes(15);
    public const int MaxDays = 31;

    /// Reads once, then computes. `nowUtc` is a parameter: never `DateTime.UtcNow` in here.
    public static async Task<IReadOnlyList<FreeDay>> FindAsync(
        IAppointmentRepository appointments, IDoctorRepository doctors, IClinicRepository clinics,
        IDoctorLeaveRepository leaves, Guid clinicId, Guid? doctorId, TimeSpan duration,
        DateOnly fromLocal, int days, Guid? excludeAppointmentId, DateTime nowUtc, CancellationToken ct);

    /// The pure core the unit tests drive (no repository).
    public static IReadOnlyList<FreeDay> Compute(
        EffectiveWorkingHours hours, IReadOnlyCollection<DoctorLeave> doctorLeaves,
        IReadOnlyCollection<Appointment> clinicWindow, Guid? doctorId, TimeSpan duration,
        DateOnly fromLocal, int days, Guid? excludeAppointmentId, DateTime nowUtc);
}
```

`FindAsync` reads:
- the clinic and (when named) the doctor → `WorkingHoursResolver.Resolve(doctor.WorkingHoursJson, clinic.WorkingHoursJson)`;
- the doctor's congés for the window → `IDoctorLeaveRepository.GetOverlappingAsync`;
- the **clinic-wide** appointments from `windowStartUtc - AppointmentScheduling.MaxCredibleAppointmentLength` to
  `windowEndUtc` → `GetByClinicIdAsync(..., doctorId: null)` — exactly `FindCollisionAsync`'s read.

`Compute`, for each local day and each quarter-hour start of that day:
1. day closed by the hours → `Closed` (no starts); `hours.Unrestricted` → `Unrestricted`;
2. congé covering the window's local day(s) → `Leave`;
3. `WorkingHoursResolver.IsWithin(hours, startUtc, duration, out _)` must be true (hours, break, same day);
4. no appointment with `Id != exclude && OccupiesSlot && CompetesFor(doctorId, a.DoctorId) && Overlaps(...)`;
5. start ≥ `nowUtc`.
An `Open` day with no start left → `Full`.

⚠️ **Reuse, never copy**: steps 3 and 4 call the existing public statics. The congé day rule is private today
(`AppointmentScheduling.FindLeaveCoveringAsync`): extract its two-line local-day computation into
`AppointmentScheduling.LocalDaysOf(DateTime startUtc, TimeSpan duration)` and make the private method use it, so
the booking guard and the finder share one rule.

### `Application/Features/Appointments/Queries/GetFreeSlotsQuery.cs` (create)

```csharp
public sealed record GetFreeSlotsQuery(Guid AppointmentId, DateOnly? From, int Days) : IRequest<Result<FreeSlotsDto>>;
```
- Resolves the caller's clinic (the `GetAppointmentQuery` idiom); an appointment of another clinic → not found.
- Takes the doctor, the duration and the exclusion from the appointment itself; `From` defaults to the clinic's
  today, `Days` clamped to 1–`MaxDays`.
- No realtime key: `RealtimeResourceResolver` derives keys from `.Commands` namespaces only.

### `Application/DTOs/FreeSlotsDto.cs` (create)

```csharp
public sealed class FreeSlotsDto
{
    public required IReadOnlyList<FreeDayDto> Days { get; init; }
}
public sealed class FreeDayDto
{
    public required string Date { get; init; }      // "2026-10-31", clinic-local
    public required string State { get; init; }     // the enum member's own name
    public required IReadOnlyList<string> Starts { get; init; }   // "09:15", clinic-local
}
```

### `API/Controllers/AppointmentsController.cs` (modify)

`[HttpGet("{id:guid}/free-slots")]` with `from` and `days` from the query string. The class policy
(`AnyClinicRole`) covers it — reception postpones visits. A GET, so the subscription gate never inspects it.

## Browser

- `web/lib/api/types.ts`, `web/lib/api/appointments.ts` — `FreeSlotsDto`, `appointmentsApi.freeSlots(id, { from, days })`.
- `web/lib/hooks/use-free-slots.ts` (create) — `{ data, loading, failed, reload }`; a failed read is **failed**,
  never an empty list (`check:responsive`'s `failed-read-as-empty`).
- `web/components/appointment-postpone-dialog.tsx` (modify) — `PostponeForm`:
  - the day chips come from the server: open days with « N libres », congé and full days shown and not clickable,
    closed days left out; the calendar fetches one day;
  - the chosen day's starts as a grid, grouped « Matin » / « Après-midi », the first free start marked;
  - « Autre heure… » reveals the current `TimeField`; the existing refusal + « Reporter quand même » path is unchanged;
  - « Reporter » disabled until a time is chosen, and labelled « Reporter à 10:30 »;
  - a failed read → `LoadFailureNotice` with « Réessayer », and « Autre heure… » still offered (§ 0);
  - `Unrestricted` → no grid, the time field, one sentence;
  - delete the client `nextOpenDays` / `isOpenOn` (they ignore the doctor's hours and congés).
- `web/components/post-visit-review-popup.tsx` (modify) — the postpone step's scrolling part goes in a `DialogBody`
  so the footer stays pinned at 730 px tall (`dialog-owns-one-scroller`).
- Touch: day chips `min-h-11`; time buttons `h-9 coarse:h-11` grown, never `.touch-target` (siblings a few px apart);
  the grid is `repeat(auto-fill, minmax(4rem, 1fr))`, so it needs no breakpoint inside the 448 px dialog.

## Pitfalls

- A visit that **starts before** the window and runs into it: the read must back off by `MaxCredibleAppointmentLength`.
- Filtering appointments by doctor in SQL drops the clinic-wide « créneau occupé » blocks: read clinic-wide, narrow
  with `CompetesFor`.
- Days are **Tunis** days (`ClinicClock`); a congé is matched on the local day, like the booking guard.
- The visit being moved must not block itself (`excludeAppointmentId`).
- `OccupiesSlot` decides which statuses hold a slot: never a status list written here.
- Doctor hours **replace** the clinic's (not intersected) — `Resolve` already does it; do not re-derive.
- `EffectiveWorkingHours.UnreadableAt` set → treat as the resolver does (falls through) and say nothing new.
- No `WorkingHoursResolver` test class exists today; the finder's tests become its first real coverage.

## Test strategy

**Unit — the full suite, unfiltered.**
- `FreeSlotFinderTests` (pure `Compute`): doctor hours replace the clinic's · break excluded to the minute ·
  back-to-back starts allowed (half-open) · a doctor-less block blocks every doctor · another doctor's visit does
  not · Cancelled / NoShow / retired free their slot · the moved visit does not block itself · a congé at 23:30 UTC
  covers the next Tunis day · closed day, full day, unrestricted · today only future starts (fixed `nowUtc`).
- **The agreement test** (the one that matters): over a grid of candidates on a fixture with hours, a break, a
  congé and mixed bookings, « listed free » ⇔ « `FindCollisionAsync` is null and `CheckWorkingHoursAsync`
  succeeds ». It fails the day either side drifts.
- `GetFreeSlotsQueryTests`: another clinic's appointment → not found; days clamped; doctor and duration taken from
  the visit.

**Browser — `/test-in-browser`, own records only.**
- popup Reporter: chips with counts, congé and full days not clickable, pick a time → moved;
- a slot taken between the list and the save → the named refusal + « Reporter quand même »;
- failed read (route-forced 500) → « Réessayer », « Autre heure… » still works;
- 320 px, 730 px tall (footer visible), tablet coarse (44 px chips);
- regression: the agenda ⋯ and « À clôturer » open the same form.

## After shipping

- `web/components/CLAUDE.md` row for `appointment-postpone-dialog.tsx`; `notes.md` gains « free slots come from
  the save's own rules ».
- `features/patient-online-booking/blueprint.md` (main checkout, not this worktree): note that `FreeSlotFinder`
  exists, with `Unrestricted` mapped to « no slots » on the public path.
