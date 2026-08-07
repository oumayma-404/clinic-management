# Feature Specification: Dashboard Insights & Drill-Through

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-28
**Scope:** Full
**Feature:** Make the dashboard informative — period comparison and breadth over the built subsystems — and make every figure a link that lands on the exact records it counted.

## Overview
This closes the two items `features/live-dashboard/` explicitly deferred: historical delta computation and card drill-down. Today's dashboard is seven counters with a single subject (four of them are appointments, as is the list beneath them), no baseline to compare any figure against, and four cards that land on `/appointments` unfiltered. It is replaced by four grouped sections — Activité, Argent, À traiter, Tendance — where every comparable figure carries its previous-period value and delta, and **every** figure is a link to the filtered view of its own records. Implementation detail lives in `blueprint.md` alongside this spec.

## What Changes
- `GET /api/dashboard?period=Today|Week|Month` replaces `GET /api/dashboard/stats`, returning the resolved period bounds plus four sections in one call.
- The server derives all four period bounds — current from/to **and** previous from/to — through `ClinicClock`; the client sends only the period key.
- **Activité** (comparable): RDV honorés · Nouveaux patients · Taux d'absence · Devis acceptés.
- **Argent** (comparable): Encaissé · Facturé · Dépenses · Net. Plus **Créances** as a point-in-time figure with no comparison.
- **À traiter** (point-in-time): Salle d'attente · Devis en attente de réponse · Patients à relancer · Prothèses en retard · Stock bas · Stock périme bientôt.
- **Tendance**: six clinic-local months of collected cash as a sparkline (first chart in the repo; `recharts` is already a dependency).
- A period selector (Aujourd'hui / Cette semaine / Ce mois) whose choice is held in `?period=` so it survives a refresh and is shareable.
- Nine destination surfaces learn to read filter parameters so each click lands on the counted records: `/appointments` (incl. a comma-separated multi-status param), `/patients` (new created-date filter), `/factures`, `/caisse` (new date-range mode), `/treatment-plans` (new accepted-date filter), `/lab-orders` (new status filter), `/stock` (new expiring filter), `/creances`, `/waiting-list`, `/recalls`.
- The existing "Rendez-vous du jour" `AppointmentList` is kept unchanged at the bottom.

## Acceptance Criteria
- **AC-1:** `GET /api/dashboard` returns the period bounds and the four sections for the caller's clinic. `GET /api/dashboard/stats`, `DashboardStatsDto`, `use-dashboard-stats.ts`, `dashboardApi.getStats` and `GetDashboardStatsQueryHandlerTests` are all deleted in the same commit.
- **AC-2:** All four period bounds are derived server-side from one authority. For a period ending 31 January the previous period is 1–31 December (not 1 January); the week starts Monday, matching the agenda.
- **AC-3:** Each of the eight comparable figures carries `current`, `previous` and `deltaPercent`. `deltaPercent` is `null` when `previous` is `0` or unavailable — never `∞` or `0`.
- **AC-4:** Taux d'absence is `(NoShow + Cancelled) ÷ total` over the period, and is `null` (rendered « — ») when the period had zero appointments — not `0 %`.
- **AC-5:** Créances is a point-in-time total and carries **no** comparison, in the response shape as well as the UI.
- **AC-6:** Over the same window and the same data, the dashboard's Encaissé / Dépenses / Net equal la caisse's `cashIn` / `cashOut` / `net`, and Créances equals « Créances ». The plan side of every money figure routes through `PlanBillingRules` billed-plan de-duplication.
- **AC-7:** Devis acceptés counts by `TreatmentPlan.AcceptedDate`, and clicking it opens `/treatment-plans?status=Accepted&acceptedFrom=&acceptedTo=` showing exactly the devis counted.
- **AC-8:** Clicking Taux d'absence opens `/appointments` with both statuses (`?status=NoShow,Cancelled`) and the show-cancelled view enabled, so the destination matches the rate's numerator.
- **AC-9:** Every remaining figure is a link whose destination shows the records it counted; a stale or nonsensical parameter lands on the unfiltered list, never a blank or broken state.
- **AC-10:** Each section keeps loading, failed-to-load (« Indisponible » + Réessayer) and real-value states distinct — a failed read never renders as « — » or `0`.
- **AC-11:** The dashboard live-refreshes on the `appointments`, `patients`, `invoices`, `treatmentplans`, `stock`, `waitinglist`, `recall`, `laborders` and `expenses` realtime keys. All nine already exist on both sides; the key contract is unchanged.

## API Contract
### GET /api/dashboard?period=Today|Week|Month
Clinic-scoped via `ICurrentClinicResolver`. `period` defaults to `Month`.
Response 2XX:
```
{
  period:      { key, from, toInclusive, previousFrom, previousToInclusive },
  activity:    { completedAppointments, newPatients, absenceRate, acceptedPlans },   // each { current, previous, deltaPercent }
  money:       { collected, invoiced, expenses, net },                               // each { current, previous, deltaPercent }
  receivables: { total },
  alerts:      { waitingList, draftPlans, patientsToRecall, overdueLabOrders,
                 lowStock, expiringStock, expiryAlertEnabled },
  trend:       [ { month: "2026-02", collected } ]
}
```
Errors: `401` (no/invalid token); `400 { "error": "<message>" }` when the clinic cannot be resolved or the read fails.

## Data / Schema Changes
**No migration, no entity or column change.** Read-side only. New *query* filters are added to existing repository signatures rather than as parallel methods: `IPatientRepository.GetByClinicIdAsync` gains `createdFrom`/`createdTo`, `ITreatmentPlanRepository.GetFilteredAsync` gains `acceptedFrom`/`acceptedTo`. New aggregate count/sum methods are listed in `blueprint.md` §6.

## Out of Scope
- « Actes réalisés » as a KPI — there is no clinic-wide dental-records list to drill into, and building one is out of scope. « Devis acceptés » takes its place.
- Per-doctor or per-procedure breakdowns; a custom (arbitrary) date-range picker; CSV/PDF export of any figure.
- Any cached/materialised snapshot of the dashboard — the reads stay live.
- Touching the notification feed, even where the À traiter section overlaps it (low stock, expiry). The feed is transient and per-user-read; the dashboard is standing state.

## Edge Cases (Critical only)
- **Inclusive vs. exclusive period end:** `ClinicClock.EndOfLocalDayUtc` returns the *next* midnight while `GetCollectedBetweenAsync` is inclusive on both ends, so the period's upper bound must be that instant minus one tick or a payment at exactly midnight is counted in both periods (finding #20, `GetCaisseSummaryQuery.cs:59`).
- **Expiry alert switched off:** `Clinic.StockExpiryLeadDays <= 0` means the clinic disabled it (`StockExpiryJob` already reads it this way) — the card renders nothing, not `0`.
- **Lab order with no `ExpectedDate`** can never be « en retard »; only `Sent` orders with a past expected date count.
- **A clinic with no history** (first month of use) has no previous period: every `previous` is `null`, every delta is `null`, and the trend series still returns six months with `0` for the empty ones rather than omitting them.
