# Blueprint: Dashboard Insights & Drill-Through

**Status:** BLUEPRINT (not yet a spec — feed to `/define-feature` or `/plan-feature`)
**Created:** 2026-07-28
**Produced by:** `/think-solution`
**Chosen option:** Composed dashboard endpoint with section readers (of 3 candidates)

---

## 1. Why this feature exists

`features/live-dashboard/` (2026-06-25) replaced the dashboard's fabricated numbers with five real
clinic-scoped counts. Its **Out of Scope** section deferred exactly two things:

> - Charts/graphs and any historical trend/delta computation ("+N from yesterday").
> - Making the cards clickable / drill-down navigation.

This feature closes both. It is the deferred half of `live-dashboard`, not a new direction.

### What is actually wrong with the dashboard today

The current screen is 7 counters + today's appointment list (`web/app/page.tsx`). The diagnosis is
**not** "too few numbers":

1. **It has one subject.** Four of the seven cards are about appointments (RDV du jour, En attente,
   Cette semaine) and so is the list beneath them. The other subsystems the product has actually
   built — salle d'attente, relances, bons de prothèse, devis, stock expiry, la caisse — are
   invisible on the first screen everyone looks at.
2. **No figure has context.** « Recettes 12 400 DT » carries no meaning without « vs. 9 800 DT le
   mois dernier ». A number with no baseline is decoration.
3. **Nothing is actionable.** It says *how many*, never *which ones*. `StatsCard` already supports
   `href`, but four of the seven cards land on `/appointments` **unfiltered** — you click
   « Cette semaine : 18 » and get today's day view. The card and its destination disagree.
4. **The one genuinely useful clinical KPI has never been surfaced.** Taux d'absence
   (no-show + annulé ÷ total) is fully derivable from data that has always been there.

So "more informative" resolves to three concrete things: **comparison context**, **breadth over the
built subsystems**, and **drill-through that lands on the exact filtered records**.

---

## 2. Approach

One `GET /api/dashboard` served by a thin handler that fans out to four **section reader** services —
Activity / Money / Alerts / Trend. This mirrors `TreatmentPlanWorkflowProjection`, the repo's existing
precedent for a composed derived read, rather than growing `GetDashboardStatsQuery` into a ~25-field
god-handler.

Two invariants carry the design:

- **`DashboardPeriod` is the single authority on period arithmetic.** It derives *all four* bounds —
  current from/to and previous from/to — through `ClinicClock`. A comparison whose two halves come
  from different authorities is worse than no comparison, and `AddMonths` end-of-month clamping is the
  exact trap already documented in `GetPatientsToRecallQuery` / `IPatientRepository.GetRecallCandidatesAsync`.
- **`PeriodComparison` is the single shape of a comparable figure.** `Current` / `Previous` /
  `DeltaPercent`, with one rounding authority and one representation of "no comparison available".
  Parallel `X` / `PreviousX` scalars are the anti-pattern the `InstallmentPayment` ledger was created
  to kill: no shared type means no shared rounding and delta arithmetic re-derived per card in TSX.

On the frontend, `web/lib/dashboard-links.ts` is the sole authority mapping a KPI key → route + query
params, following the standing English-key / French-label convention (`appointment-labels.ts`,
`invoice-labels.ts`, `treatment-plan-labels.ts`).

### Options rejected

| Option | Verdict | Why rejected |
|---|---|---|
| Independent endpoints per section (`/dashboard/activity`, `/money`, …) | ⚠️ Acceptable | Better failure isolation, but the period must be passed to each endpoint and can drift — four requests crossing midnight, or a `ClinicClock` read per handler, gives sections describing different windows. That is the one thing this feature exists to prevent. Also 4 hand-rolled loading/error/refetch triplets in a repo with no React Query. |
| Widen `DashboardStatsDto` in place | ⚠️ Acceptable, degrades fast | Smallest diff, purely additive contract — but turns a 60-line handler into a ~25-field flat bag with ~24 sequential awaits and no internal seams. Every future KPI edits the one handler and the one test class. |
| Cached `DashboardSnapshot` table + Hangfire job | ❌ Risky | Needs a migration + `verify-schema` extension, and introduces staleness into the one screen that already refreshes live over SignalR. Zero benefit at this data volume. |

---

## 3. Content plan

### Section « Activité » — comparable

| KPI | Definition | Destination |
|---|---|---|
| RDV honorés | `AppointmentStatus.Completed` in period | `/appointments?from=&to=&status=Completed` |
| Nouveaux patients | `Patient.CreatedAt` in period, non-archived | `/patients?createdFrom=&createdTo=` |
| **Taux d'absence** | `(NoShow + Cancelled) ÷ total` in period | `/appointments?from=&to=&status=NoShow` |
| Devis acceptés | `TreatmentPlanStatus.Accepted` in period | `/treatment-plans?status=Accepted&from=&to=` |

> **« Actes réalisés » was deliberately dropped.** It was the only proposed figure with **no
> destination page** — there is no clinic-wide dental-records list, and building one is out of scope.
> Shipping it would mean one card that violates the "every figure is clickable" contract. « Devis
> acceptés » replaces it: period-comparable, and it lands truthfully on a filter that already exists.

### Section « Argent » — comparable

| KPI | Definition | Destination |
|---|---|---|
| Encaissé | invoice payments + plan installments − avoirs, in period | `/factures?from=&to=` |
| Facturé | issued (non-Draft, non-Cancelled) invoice TTC in period | `/factures?from=&to=&status=Issued` |
| Dépenses | `Expense.Amount` in period | `/caisse?from=&to=` |
| Net | Encaissé − Dépenses | `/caisse?from=&to=` |

### Créances — point-in-time, **no** comparison

A live balance has no "last month". `PeriodComparison` must not be used here; the type distinction is
what stops someone inventing a meaningless delta. Destination: `/creances`.

### Section « À traiter » — point-in-time, no comparison

| KPI | Source | Destination |
|---|---|---|
| Salle d'attente | `WaitingListStatus.Waiting` count | `/waiting-list` |
| Devis en attente de réponse | `TreatmentPlanStatus.Draft` count | `/treatment-plans?status=Draft` |
| Patients à relancer | recall candidates count | `/recalls` |
| Prothèses en retard | `LabOrderStatus.Sent` past expected date | `/lab-orders?status=Sent` |
| Stock bas | low-stock count | `/stock?filter=low` |
| Stock périme bientôt | expiring within `Clinic.StockExpiryLeadDays` | `/stock?filter=expiring` |

### Section « Tendance »

6 clinic-local months of collected cash, as a `recharts` sparkline. **First chart in the repo** —
`recharts` is already a dependency with zero usages in `components/`.

### Kept as-is

The existing `AppointmentList` ("Rendez-vous du jour") stays at the bottom, unchanged.

---

## 4. Wire shape

DTO property names are **English**; every French string lives in `dashboard-labels.ts`.

```
GET /api/dashboard?period=Month        // Today | Week | Month

{
  "period": {
    "key": "Month",
    "from": "2026-06-30T23:00:00Z",          // clinic-local 1 Jul 00:00, as UTC
    "toInclusive": "2026-07-31T22:59:59.9999999Z",
    "previousFrom": "2026-05-31T23:00:00Z",
    "previousToInclusive": "2026-06-30T22:59:59.9999999Z"
  },
  "activity": {
    "completedAppointments": { "current": 84,  "previous": 71,   "deltaPercent":  18.3 },
    "newPatients":           { "current": 12,  "previous": 15,   "deltaPercent": -20.0 },
    "absenceRate":           { "current": 8.3, "previous": 11.9, "deltaPercent": -30.3 },
    "acceptedPlans":         { "current": 7,   "previous": 5,    "deltaPercent":  40.0 }
  },
  "money": {
    "collected": { "current": 12400.000, "previous": 9800.000, "deltaPercent": 26.5 },
    "invoiced":  { "current": 15200.000, "previous": 11300.000, "deltaPercent": 34.5 },
    "expenses":  { "current":  3100.000, "previous":  2950.000, "deltaPercent":  5.1 },
    "net":       { "current":  9300.000, "previous":  6850.000, "deltaPercent": 35.8 }
  },
  "receivables": { "total": 4820.500 },        // point-in-time, no comparison
  "alerts": {
    "waitingList": 2, "draftPlans": 5, "patientsToRecall": 9,
    "overdueLabOrders": 1, "lowStock": 3, "expiringStock": 2,
    "expiryAlertEnabled": true                 // false when StockExpiryLeadDays <= 0
  },
  "trend": [
    { "month": "2026-02", "collected": 9800.000 },
    { "month": "2026-03", "collected": 11200.000 }
  ]
}
```

`deltaPercent` is `null` when `previous` is `0` or unavailable — division by zero is "no comparison",
never `∞`. `absenceRate.current` is `null` (rendered « — ») when the period had zero appointments;
`0 %` would read as perfect attendance.

---

## 5. Files to create

### Application layer

| File | Purpose |
|---|---|
| `Features/Dashboard/DashboardPeriodKey.cs` | `enum { Today, Week, Month }`. Not magic strings. |
| `Features/Dashboard/DashboardPeriod.cs` | Pure record + `Resolve(key, nowUtc)`. **The only place period arithmetic lives.** |
| `Features/Dashboard/PeriodComparison.cs` | `record PeriodComparison(decimal? Current, decimal? Previous, decimal? DeltaPercent)` + `From(current, previous)`. |
| `Features/Dashboard/Queries/GetDashboardQuery.cs` | Thin handler. Resolves clinic via **`ICurrentClinicResolver`** (migrating off the legacy `IClinicContext` + `IUserRepository` idiom the old handler used), resolves the period, awaits the four readers **sequentially**, assembles the DTO. |
| `Features/Dashboard/Readers/IDashboardActivityReader.cs` + impl | The four activity comparisons. |
| `Features/Dashboard/Readers/IDashboardMoneyReader.cs` + impl | The four money comparisons + receivables. Computes `billedPlanIds` **once**. |
| `Features/Dashboard/Readers/IDashboardAlertsReader.cs` + impl | The six point-in-time counts. |
| `Features/Dashboard/Readers/IDashboardTrendReader.cs` + impl | 6-month collected series. |
| `DTOs/DashboardDto.cs` | `{ Period, Activity, Money, Receivables, Alerts, Trend }` + nested DTOs. |

### Frontend

| File | Purpose |
|---|---|
| `web/lib/api/dashboard.ts` *(modify)* | Add `get(period)`. Keep `getStats` only until cut-over. |
| `web/lib/hooks/use-dashboard.ts` | Replaces `use-dashboard-stats.ts`. No client-side date-fns ranges — the server owns the period. |
| `web/lib/dashboard-links.ts` | **The single drill-through authority.** Exhaustive `Record<DashboardKpiKey, (p: DashboardPeriod) => string>` so `tsc` fails if a KPI is added without a destination. |
| `web/components/dashboard/dashboard-labels.ts` | French labels + descriptions per KPI key. |
| `web/components/dashboard/kpi-card.tsx` | `StatsCard` + delta badge (`↑ 18,3 %` / `↓ 20,0 %` / « — »). Always a `Link`. |
| `web/components/dashboard/dashboard-section.tsx` | Titled group with its own loading / « Indisponible » + Réessayer / real states — reuse the `RevenueValue` three-state pattern at `app/factures/page.tsx:27`. |
| `web/components/dashboard/collected-trend-chart.tsx` | The recharts sparkline. **Load the `dataviz` skill first** — this sets the repo's chart conventions. |
| `web/components/dashboard/period-selector.tsx` | Aujourd'hui / Cette semaine / Ce mois. Writes `?period=` so the choice survives refresh and is shareable. |

### Files to modify

| File | Change |
|---|---|
| `API/Controllers/DashboardController.cs` | Add `GET api/dashboard`. Class-level `[Authorize]` unchanged. Keep `stats` until cut-over. |
| `Application/Extensions.cs` | Register the four readers (see §7). |
| `web/app/page.tsx` | Rewrite: period selector + four sections + trend + the kept `AppointmentList`. |
| 9 destination surfaces | See §8. |

### Files to delete (all in the cut-over commit, together)

`GetDashboardStatsQuery.cs`, `DashboardStatsDto.cs`, `DashboardController.GetStats`,
`web/lib/hooks/use-dashboard-stats.ts`, `dashboardApi.getStats`, `DashboardStats` in `types.ts`,
`GetDashboardStatsQueryHandlerTests.cs`.

> Verified: `StatsCard` and `useDashboardStats` each have **exactly one consumer** (`app/page.tsx`), so
> the cut-over leaves no orphan callers. A left-behind endpoint reads as still-supported — delete it in
> the same commit or not at all.

---

## 6. New repository methods

Domain interface + Infrastructure impl. All must be genuine SQL aggregates — never load entities to
count them. If any needs an expression shared with production code, follow
`RecallQueryTranslationTests` and assert the translation.

```csharp
// IAppointmentRepository — ONE GROUP BY replaces four COUNTs per period.
Task<IReadOnlyDictionary<AppointmentStatus, int>> CountByStatusBetweenAsync(
    Guid clinicId, DateTime from, DateTime toInclusive, CancellationToken ct = default);

// IPatientRepository
Task<int> CountCreatedBetweenAsync(
    Guid clinicId, DateTime from, DateTime toInclusive,
    bool includeArchived = false, CancellationToken ct = default);

// IPatientRepository — EXTEND the existing signature, do not add a parallel one.
// Backs the /patients?createdFrom=&createdTo= destination.
Task<IEnumerable<Patient>> GetByClinicIdAsync(
    Guid clinicId, bool includeArchived = false,
    DateTime? createdFrom = null, DateTime? createdTo = null, CancellationToken ct = default);

// ITreatmentPlanRepository
Task<int> CountByStatusBetweenAsync(
    Guid clinicId, TreatmentPlanStatus status,
    DateTime? from, DateTime? toInclusive, CancellationToken ct = default);

// IInvoiceRepository — « Facturé » must NOT reuse GetInvoiceRevenueQuery, which calls
// GetFilteredAsync and materialises whole Invoice aggregates with lines and payments.
Task<decimal> GetInvoicedBetweenAsync(
    Guid clinicId, DateTime from, DateTime toInclusive, CancellationToken ct = default);
Task<IReadOnlyList<(int Year, int Month, decimal Collected)>> GetCollectedByMonthAsync(
    Guid clinicId, DateTime fromUtc, DateTime toInclusiveUtc, CancellationToken ct = default);

// IStockItemRepository — counts, not entity loads (GetLowStockItemsAsync returns entities today).
Task<int> CountLowStockAsync(Guid clinicId, CancellationToken ct = default);
Task<int> CountExpiringSoonAsync(
    Guid clinicId, int leadDays, DateTime asOfUtc, CancellationToken ct = default);

// IWaitingListRepository
Task<int> CountActiveAsync(Guid clinicId, CancellationToken ct = default);

// ILabWorkOrderRepository — also backs /lab-orders?status=
Task<int> CountByStatusAsync(
    Guid clinicId, LabOrderStatus status, CancellationToken ct = default);
Task<int> CountOverdueAsync(Guid clinicId, DateTime asOfUtc, CancellationToken ct = default);
```

Reused unchanged (do **not** duplicate their logic):
`IInvoiceRepository.GetCollectedBetweenAsync` · `GetOutstandingByPatientAsync` ·
`GetTreatmentPlanLinksAsync` · `ITreatmentPlanRepository.GetInstallmentCollectedBetweenAsync` ·
`GetInstallmentOutstandingByPatientAsync` · `ICreditNoteRepository.GetRefundedBetweenAsync` ·
`IExpenseRepository.GetTotalBetweenAsync` · `IPatientRepository.GetRecallCandidatesAsync`.

---

## 7. Key signatures & wiring

```csharp
public sealed record DashboardPeriod(
    DashboardPeriodKey Key,
    DateTime From, DateTime ToInclusive,
    DateTime PreviousFrom, DateTime PreviousToInclusive)
{
    /// The ONE place period arithmetic lives. AddMonths clamping is handled here and nowhere else.
    /// ToInclusive is EndOfLocalDayUtc(...) minus one tick — see Pitfall 1.
    public static DashboardPeriod Resolve(DashboardPeriodKey key, DateTime nowUtc);
}

public interface IDashboardMoneyReader
{
    Task<DashboardMoneySection> ReadAsync(
        Guid clinicId, DashboardPeriod period, DateTime nowUtc, CancellationToken ct);
}
```

`Application/Extensions.cs`, alongside the existing `AddScoped<ICnamBillingCalculator, …>`:

```csharp
services.AddScoped<IDashboardActivityReader, DashboardActivityReader>();
services.AddScoped<IDashboardMoneyReader,    DashboardMoneyReader>();
services.AddScoped<IDashboardAlertsReader,   DashboardAlertsReader>();
services.AddScoped<IDashboardTrendReader,    DashboardTrendReader>();
```

`DashboardPeriod` and `PeriodComparison` are pure records — **not** registered. New repository impls go
in `Infrastructure`'s existing repository registration block.

**No migration.** **No realtime-contract change** — the dashboard will subscribe to `Stock`,
`WaitingList`, `Recall`, `LabOrders` and `Expenses` in addition to today's four, and all five keys
already exist on both sides, so `RealtimeResourceResolverTests` stays green untouched.

---

## 8. Drill-through: the destination work

This is the larger half of the feature and it lives in the destination pages, not the dashboard.
Scope decision: **full** — every figure lands filtered.

| KPI | Destination | Work | Size |
|---|---|---|---|
| RDV honorés · Taux d'absence | `/appointments?from=&to=&status=` | New param reading; focus the month view on `from`; auto-enable show-completed / show-cancelled | Moderate |
| Nouveaux patients | `/patients?createdFrom=&createdTo=` | **New backend filter** (`GetPatientsQuery` + repo) + page param reading + `PatientsTable` props | Moderate |
| Devis acceptés · Devis en attente | `/treatment-plans?status=&from=&to=` | Param reading into the **existing** `from`/`to`/`status` state | Easy |
| Encaissé · Facturé | `/factures?from=&to=&status=` | Param reading into the **existing** from/to/status state | Easy |
| Dépenses · Net | `/caisse?from=&to=` | ⚠️ **`/caisse` is single-day only** (`selectedDay`). Needs a date-**range** mode before a monthly KPI can land truthfully | Moderate |
| Créances | `/creances` | Nothing — the page holds no state; it *is* the whole list | None |
| Salle d'attente | `/waiting-list` | Nothing | None |
| Patients à relancer | `/recalls` | Nothing | None |
| Prothèses en retard | `/lab-orders?status=Sent` | ⚠️ **`/lab-orders` has no status filter at all.** Add one, then param reading | Moderate |
| Stock bas · périme | `/stock?filter=low\|expiring` | Low-stock filter exists in `stock-table.tsx`; an **expiring** filter is new. `clinic:deeplink` already handles same-route nav | Moderate |

### Conventions every destination must follow

- Read params from **`window.location.search` inside an effect**, then `history.replaceState` to clean
  the URL — never `useSearchParams` (it forces the page out of static prerendering and needs a
  Suspense boundary). Established at `app/patients/page.tsx:28`, `app/appointments/page.tsx:144`,
  `app/patients/[id]/page.tsx:323`.
- Already-on-that-route navigation does not remount: dispatch/listen on the existing
  `clinic:deeplink` window event (`app/appointments/page.tsx:162`).
- **Degrade gracefully** on a stale or nonsensical param — land on the unfiltered list, never a blank
  or broken state. Pattern: `openAppointmentById` (`app/appointments/page.tsx:127`).

---

## 9. Pitfalls

1. **`ClinicClock.EndOfLocalDayUtc` is *exclusive* (next midnight); `GetCollectedBetweenAsync` is
   *inclusive* (`>= from && <= to`).** Passing the exclusive bound as `to` double-counts a payment at
   exactly next-midnight — in **both** periods. This is finding #20, already documented at
   `GetCaisseSummaryQuery.cs:59`. `DashboardPeriod` must expose
   `ToInclusive = EndOfLocalDayUtc(...).AddTicks(-1)`, and the property name must say `Inclusive`.

2. **The two repositories disagree on interval convention.** `Invoice.GetCollectedBetweenAsync` is
   closed `[from, to]`; `Expense.GetTotalBetweenAsync` is half-open `[from, to)`
   (`ExpenseRepository.cs:45`). Both are correct with the `AddTicks(-1)` bound; neither is correct with
   the raw exclusive bound. Do not "fix" one without the other, and do not switch conventions mid-feature.

3. **Do not `Task.WhenAll` the readers.** They share one scoped `DbContext`, which is not thread-safe
   → `InvalidOperationException: A second operation started on this context`. Sequential is correct;
   the ~24 aggregates are indexed COUNT/SUM over a single clinic's rows.

4. **`MoneyReadConsistencyTests` is the real gate.** Compute `billedPlanIds` **once** in
   `DashboardMoneyReader` via `PlanBillingRules.BilledPlanIds` and pass it to **both**
   `GetInstallmentCollectedBetweenAsync` and `GetInstallmentOutstandingByPatientAsync`. Omitting it on
   either silently doubles or erases money. The dashboard, la caisse, « Créances » and « Solde
   patient » must agree over the same window from the same fixture.

5. **Carry over `[AC-12a]`.** `GetDashboardStatsQueryHandlerTests` has a billed-plan de-dup test
   (`Handle_Should_Exclude_Plans_Already_Billed_To_An_Invoice_From_Outstanding`). It must move into
   `DashboardMoneyReaderTests`, not disappear with the deleted class.

6. **Zero-denominator absence rate** must be `null` (« — »), not `0 %`. Applies to `Previous` too.

7. **`« Facturé »` scope.** Invoices only — a devis is a quote, not a billed amount. Match
   `GetInvoiceRevenueQuery`'s existing rule (non-Draft, non-Cancelled). State it in a comment or
   someone will "fix" it later.

8. **`« Devis en attente de réponse »` = `Draft`.** `Draft` is a devis not yet answered; `Accepted`
   with zero items done is a devis said yes to but not started. Different clinical states — pick
   `Draft` and make the card label and the `?status=` param agree.

9. **Stock expiry lead days is per-clinic and can be off.** `Clinic.StockExpiryLeadDays <= 0` means the
   alert is disabled (`StockExpiryJob` already encodes this). Render **nothing**, not `0` — hence
   `alerts.expiryAlertEnabled` on the wire.

10. **`recharts` in a `"use client"` page still renders server-side once.** Guard any `window` access;
    the chart must render from props only. See LEARNINGS: "Guard browser globals in any module
    importable server-side".

11. **The Alerts section partly overlaps the notification feed** (low stock, expiry). This is
    deliberate — the feed is transient and per-user-read, the dashboard is standing state — but say so
    in a comment on `DashboardAlertsReader` or the next reviewer files it as redundancy.

12. **Period boundaries move from client to server.** Today the client sends them
    (`use-dashboard-stats.ts`) so counts match the agenda. `ClinicClock` (Tunisia UTC+1, no DST) gives
    the same answer and removes a class of drift, but verify the day/week counts are unchanged against
    a real dataset before deleting the old endpoint.

13. **FE quality gate is `npx tsc --noEmit` + `npm run build`**, both clean. There is no frontend test
    runner and ESLint is not installed. The exhaustive `Record<DashboardKpiKey, …>` in
    `dashboard-links.ts` is deliberately the type-level substitute for a missing-destination test.

---

## 10. Test strategy

Backend only — `web/` has no test runner (LEARNINGS: "the FE quality gate is `tsc --noEmit` +
`npm run build`").

| Test class | Covers |
|---|---|
| `DashboardPeriodTests` | **Highest value.** 31 Jan → previous month is 1–31 Dec (not 1 Jan); 1 Mar → previous is all of Feb; week is Monday-based and matches `startOfWeek(weekStartsOn: 1)`; every bound is `DateTimeKind.Utc`; `ToInclusive` is one tick before the next clinic-local midnight. |
| `PeriodComparisonTests` | `previous == 0` → `DeltaPercent == null`; `previous == null` → `null`; sign correctness; rounding to one decimal. |
| `DashboardActivityReaderTests` | Per-status breakdown → DTO mapping; absence-rate denominator; **zero appointments → `null` rate, not `0`**; repo calls receive the exact period bounds (`It.Is<DateTime>`). |
| `DashboardMoneyReaderTests` | Carried-over `[AC-12a]`; a `Cancelled` bridge invoice does **not** exclude its plan; avoirs netted; both plan calls receive the same `billedPlanIds` content. |
| `DashboardAlertsReaderTests` | Each count clinic-scoped; empty → `0`; `StockExpiryLeadDays <= 0` → `expiryAlertEnabled == false` and no expiry count. |
| `DashboardTrendReaderTests` | 6 months returned even when some are empty (gaps filled with `0`, not omitted); clinic-local month bucketing. |
| `DashboardTenantIsolationTests` | Another clinic's rows contribute nothing to any section (the repo's first-class guard convention). |
| `MoneyReadConsistencyTests` *(extend)* | Dashboard `money.collected/expenses/net` == caisse `cashIn/cashOut/net`; `receivables.total` == « Créances ». Its mocks intentionally reimplement the repository SQL filters — follow that precedent, do not introduce a DB. |

Harness shape: nested private `Harness` / `Handler()` per `Features/Notifications/NotificationGenerationTests.cs`;
`NullLogger<T>.Instance`; fixed UTC `DateTime`s and deterministic GUIDs (`aaaa…` / `bbbb…`).
Spec-ID traceability comments on every test.

**Running the suite:** Smart App Control blocks freshly-built DLLs on this machine. Use
`dotnet build <UnitTests.csproj> -p:OutDir=<scratch>/` then `dotnet vstest <scratch>/ClinicManagement.UnitTests.dll`.

**Manual verification** (no automated FE coverage): click every card in all three periods and confirm
the destination shows the *same* records the number counted; confirm a stale param lands on an
unfiltered list, not a blank one.

---

## 11. Suggested story breakdown

1. **S1 — Period & comparison primitives.** `DashboardPeriodKey`, `DashboardPeriod`, `PeriodComparison`
   + their two test classes. Pure, no I/O, no dependencies. Ships the riskiest logic first.
2. **S2 — Repository aggregates.** All of §6, with translation assertions where warranted.
3. **S3 — Readers + composed query + controller.** The four readers, `GetDashboardQuery`,
   `DashboardDto`, DI wiring, all reader tests + tenant isolation + the extended
   `MoneyReadConsistencyTests`.
4. **S4 — Dashboard page.** `dashboard-links.ts`, `dashboard-labels.ts`, `kpi-card`,
   `dashboard-section`, `period-selector`, `use-dashboard`, rewritten `app/page.tsx`. Old endpoint
   still alive.
5. **S5 — Trend chart.** Load `dataviz` first. `collected-trend-chart.tsx`.
6. **S6 — Destinations, easy tier.** `/factures`, `/treatment-plans`, `/stock`.
7. **S7 — Destinations, moderate tier.** `/appointments`, `/caisse` range mode, `/lab-orders` status
   filter, `/patients` created-date filter (backend + frontend).
8. **S8 — Cut-over.** Delete the old endpoint, DTO, hook, api method, type and test class in one commit.

Per the standing `no-deferring-in-scope-work` rule: tests belong in the same story as the code they
cover, and any adjacent or masking defect found along the way is fixed in that story — not captured as
a follow-up.
