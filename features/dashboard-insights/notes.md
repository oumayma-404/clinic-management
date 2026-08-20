# dashboard-insights — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident.
Moved out of the root `CLAUDE.md` verbatim so it is no longer loaded into every session; the root
indexes it under **Architecture notes**. `spec.md` is what was asked for, `stories/` how it was built,
and this is what shipped.

## The dashboard is a composed read, not a KPI bag (`dashboard-insights`)

`GET /api/dashboard?period=Today|Week|Month`
returns four sections — comparable **Activité** (RDV honorés, nouveaux patients, **taux d'absence**, devis acceptés) and
**Argent** (encaissé, facturé, dépenses, net), the point-in-time **créances** total, the **À-traiter** counts across the
operational subsystems, and a 6-month collected **trend**. It closes the two items `features/live-dashboard/` explicitly
deferred (delta computation and card drill-down). A thin handler fans out to four **section readers**; two primitives do
the load-bearing work. **`DashboardPeriod`** is the *single* authority on period arithmetic — it derives the current
**and** previous bounds through `ClinicClock`, because a comparison whose halves came from two different rules is not a
comparison (the client used to send six boundaries; it now sends only a period key). ⚠️ Its `ToInclusive` is the last
**tick** of the window, not the next midnight: `ClinicClock.EndOfLocalDayUtc` is *exclusive* while the money reads are
inclusive on both ends, so the raw bound counts a midnight payment in **both** adjacent periods — finding #20 re-armed.
**`PeriodComparison`** is the one shape of a comparable figure, and distinguishes a real `0` from an *undefined* value:
a period with no appointments has **no** taux d'absence, and rendering `0 %` would assert perfect attendance.
⚠️ The readers are awaited **sequentially** — they share the request's `DbContext`, so `Task.WhenAll` throws.
**Every figure is a link to the filtered records it counted**, and the KPI→route mapping lives in exactly one place
(`web/lib/dashboard-links.ts`, an exhaustive `Record`, so a KPI with no destination is a `tsc` error). Making that true
required nine destinations to learn filters — including a genuinely new created-date filter on `/patients`, a
**date-range mode** on `/caisse` (day-only before), a **stage filter** on `/lab-orders` (it had none), and an expiring
filter on `/stock`. Two links carry a trap worth knowing: « Devis acceptés » counts by `AcceptedDate` and so filters on
`acceptedFrom`/`acceptedTo` (`from`/`to` bound *creation* — a different set of devis), and « Taux d'absence » sends
`status=NoShow,Cancelled` because that pair *is* the rate's numerator. `MoneyReadConsistencyTests` was extended to pin
dashboard-vs-caisse agreement, so the money section is now the **fourth** read held to the one figure.
