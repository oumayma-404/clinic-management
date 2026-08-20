# multi-act-appointments — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident.
Moved out of the root `CLAUDE.md` verbatim so it is no longer loaded into every session; the root
indexes it under **Architecture notes**. `spec.md` is what was asked for, `stories/` how it was built,
and this is what shipped.

## A séance is several acts, and the scalars are derived (`multi-act-appointments`)

an `Appointment` held **one**
`ProcedureTypeId`, so « détartrage + deux obturations » — one visit — could only be typed into the notes: invisible
to the colour, the duration, the fiche de soins proposal and the devis. It now owns an **`AppointmentProcedure`**
child collection, and `ProcedureTypeId`/`ProcedureDurationMinutes`/`ProcedureColorHex`/`TreatmentPlanItemId` are a
**derived snapshot of the first row** (`Appointment.SetProcedures` re-derives all four). Keeping the scalars is the
point: the agenda paints one colour, `ProcedureType.Appointments` is a real FK, and every existing read keys off
them — none had to learn about a list to stay correct.
⚠️ Four traps. (a) **`SetProcedureType` now means "this visit has exactly this one act"** — it replaces the list.
`UpdateProcedureTypeCommand` therefore calls **`RefreshProcedureSnapshot`** instead; the old call would have deleted
the other acts of every séance using a renamed procedure. (b) The devis read-back must group on
**`LinkedTreatmentPlanItemIds`**, not the scalar: two plan acts booked into one visit are two child links, and
keying on the scalar left the second reading « À planifier » forever, offering to book a visit that already exists
(`TreatmentPlanWorkflowProjection`, and `IAppointmentRepository.GetByTreatmentPlanItemIdsAsync` matches child rows).
(c) A child row's `ProcedureTypeId` is **nullable**: a hand-typed devis line has no catalog act, and refusing it
would mean a grouped séance carried only the links of the acts that happen to match the catalogue. Such a row takes
its name from the plan step's désignation and contributes **no** duration. (d) On the wire, `procedures` is
**tri-state on update** — omit it to leave the acts alone, `[]` to clear them; cancelling posts `{ status }` alone,
so conflating the two would delete every act on every cancellation (the same defect the `ProcedureTypeId` tri-state
fixed). Duration defaults to the **sum** of the acts, client- and server-side. **Grouping is a UI decision, not a
stored one**: there is no séance entity — two acts sharing an appointment *are* one séance, which is why the plan
can display the grouping (`plan-act-row`'s « séance de N actes ») with no extra field. In the plan workspace the
user ticks acts and chooses « Planifier ensemble » (one RDV) or « Planifier séparément » (one each), and mixed
splits fall out of repeating the gesture; `verify-schema` gained `appointment-act-rows`, pinning that no
appointment names an act with no row behind it.
