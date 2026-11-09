# Blueprint — « Droit à congé » : règle + relevé calculé

**Owner's pick (2026-11-06):** Option 1 of `/think-solution` — a rule per secretary plus one-off entries, everything
else (days acquired, days lost, the statement) **derived on read** from those entries and the accepted congés.
Rejected: a nightly job writing movements (« pris » would exist twice; a missed night loses days; a rule change cannot
fix the past) · more fields on the account (no history; changing 20 → 25 rewrites past years).

## What the doctor and the secretary get

| Who | Sees / does |
|---|---|
| Doctor, her file (`/users/[id]`) | **Règle** : « 20 j par an » or « 2 j par mois », from a date, « non pris : perdus au 31/12 » or « reportés » · **Solde de départ** (« il lui reste 10 j au 06/11/2026 ») · **Ajouter / retirer des jours** (motif) · **Relevé** (every line, with « Annuler » + motif on her own entries) |
| Secretary, `/conges` | Reste · acquis · pris · en attente · « +2 j le 1er décembre » · **total pris depuis le début** · the same relevé, read-only |
| Both | the « Solde après » on a request uses the same fold, so the form, the card and the relevé never disagree |

Decisions taken (reversible): a request beyond the balance is **not refused** (today's behaviour — the doctor decides,
« Solde après » shows it negative) · a **negative** balance carries into the next year (a debt is not wiped by the
31/12 expiry) · the period is the **calendar year** (the owner's own example).

## Model

### `StaffLeaveAllowanceEntry : AggregateRoot<Guid>` (Domain/Entities) — append-only, audited
`MessagingAllowanceEntry`'s shape: no mutator for the figures; a mistake is **cancelled with a motif** and stays
visible struck through.

| Field | Notes |
|---|---|
| `ClinicId`, `UserId` (string) | filtered on `ClinicId` like `StaffLeaveRequest` |
| `Kind` | `Rule = 1` · `Opening = 2` · `Adjustment = 3` (appended-only enum, `HasConversion<int>`) |
| `Days` (decimal 4,1) | halves only. Rule: 0–`StaffLeaveRequest.MaxDays` (Year) / 0–31 (Month). Opening: ≥ 0. Adjustment: ≠ 0, ± |
| `Frequency` (Rule only) | `Year = 1` · `Month = 2` |
| `CarryOver` (Rule only) | false = « non pris perdus au 31/12 » |
| `OnDay` (DateOnly) | Rule: first credit day — **1 Jan** (Year) or **1st of a month** (Month), refused otherwise · Opening / Adjustment: the day it holds |
| `Reason` | required on Adjustment (≤ 500) |
| `CreatedByUserId`, `CreatedByName`, `CreatedAt` | |
| `CancelledAt?`, `CancelledByName?`, `CancelReason?` | `Cancel(reason, by, nowUtc)`; cancelling twice throws → 409 |

Factories: `Rule(...)`, `Opening(...)`, `Adjustment(...)`, each validating and throwing French messages; static
`RefusalFor...` helpers so handlers refuse with a `Result` before constructing (the `AttendanceTerminal.NameRefusalFor`
pattern).

### `StaffLeaveLedger` (Domain/Services) — pure, total, **clock-free**
`SubscriptionLedger` / `MessagingAllowanceLedger` precedent: `asOf` is a parameter, the fold orders its inputs itself.

```
StaffLeaveStatement Fold(
    IReadOnlyCollection<StaffLeaveLedgerEntry> entries,   // non-cancelled, projected from the entity
    IReadOnlyCollection<StaffLeaveUse> uses,              // accepted congé payé, one row per (request, year share)
    IReadOnlyCollection<StaffLeaveUse> pending,           // waiting congé payé, same split
    DateOnly asOf)

record StaffLeaveUse(Guid RequestId, DateOnly Day, decimal Days, DateOnly Start, DateOnly End)
record StaffLeaveYear(int Year, decimal Acquired, decimal Upcoming, decimal CarriedIn, decimal Taken,
                      decimal Pending, decimal Expired, decimal Remaining)
record StaffLeaveMovement(DateOnly Day, StaffLeaveMovementKind Kind, decimal Days, decimal Balance,
                          Guid? EntryId, Guid? RequestId)   // Kind: Credit · Opening · Adjustment · Taken · Expired
record StaffLeaveStatement(IReadOnlyList<StaffLeaveYear> Years, IReadOnlyList<StaffLeaveMovement> Movements,
                           decimal TakenTotal, StaffLeaveRuleView? RuleInForce, DateOnly? NextCreditDay,
                           decimal? NextCreditDays)
```

Rules of the fold — one running balance, walked in date order:

1. **Credits** come from the rule in force on each credit day: the latest non-cancelled `Rule` whose `OnDay` ≤ that
   day. Year → one credit on 1 Jan; Month → one on the 1st. Generated from the first rule's `OnDay` up to 31 Dec of
   `asOf`'s year; those after `asOf` are `Upcoming`, never in the balance.
2. **Opening** sets the balance to its `Days` on its `OnDay`; credits and uses dated **before** it are listed as
   history and not counted (the figure already includes them). The latest opening wins.
3. **Adjustments** move the balance on their day.
4. **Uses**: an accepted congé's share in a year is dated on its start day (or 1 Jan for the second year's share) —
   the existing `StaffLeaveRules.DaysInYear` split, unchanged. A planned (future) accepted congé already counts
   against its year: the doctor accepted it, its days are spoken for.
5. **Expiry** on 31 Dec of each completed year whose rule in force says « perdus »: `Expired = max(0, balance)`, the
   balance drops to 0 (or stays negative). `CarryOver` → nothing expires, `CarriedIn` next year.
6. `Remaining(year)` = the balance after every movement of that year so far + that year's future accepted uses.
   `TakenTotal` = every accepted day ever, opening or not.

Invariant the tests hold: the relevé's last balance **equals** `Remaining` of the current year (the caisse-ledger
`The_Movements_Sum_To_The_Caisse_Totals` idea).

## Application (Features/Team — realtime `team`, nothing new to declare)

| Piece | Change |
|---|---|
| `IStaffLeaveAllowanceRepository` + impl | `GetForUserAsync(clinicId, userId)` and `GetForClinicAsync(clinicId)` — **not paged** (a fold over a page is not a fold), ordered `OnDay, CreatedAt, Id` · `GetByIdAsync` (tracked) · `AddAsync` |
| `StaffLeaveRules.Balance` / `BalanceAfter` | become thin wrappers over `StaffLeaveLedger.Fold` with `ClinicClock.ClinicToday()`; **all 5 + 2 call sites move together** (`GetMyStaffLeaveQuery`, `GetStaffFileQuery`, `GetTeamLeaveOverviewQuery`, `SetLeaveAllowanceCommand`→ replaced, `ListStaffLeaveRequestsQuery`, `PreviewStaffLeaveQuery`) — the team overview reads the clinic's entries **once** and groups them |
| `StaffLeaveBalanceDto` | keeps `Year`, `Taken`, `Remaining`, `Pending`; `Allowance` becomes « acquis + à venir cette année »; adds `Acquired`, `Upcoming`, `CarriedIn`, `Expired`, `TakenTotal`, `Rule { days, frequency, carryOver, since }`, `NextCredit { day, days }`. `Remaining` stays **null** with no rule and no opening — never a balance it cannot know |
| `StaffLeaveStatementDto` + `GetStaffLeaveStatementQuery` | `GET team/users/{id}/leave-statement?year=` (AdminOnly) and `GET team/me/leave-statement` (her own) |
| Commands (AdminOnly, module on, target in clinic) | `SetLeaveRuleCommand { userId, days, frequency, carryOver, since }` · `SetOpeningLeaveBalanceCommand { userId, days, onDay }` · `AdjustLeaveBalanceCommand { userId, days, onDay, reason }` · `CancelLeaveAllowanceEntryCommand { entryId, reason }` — catch-alls `when (ex is not ConflictException)` |
| Removed | `SetLeaveAllowanceCommand`, `PUT .../leave-allowance`, `User.AnnualLeaveAllowanceDays` (unreleased — branch only) |

## Migration `AddStaffLeaveAllowanceEntries`
- Create the table (FK `Clinics` cascade, index `(ClinicId, UserId, OnDay)`).
- **Backfill before the drop** (hand-ordered — the scaffolder puts `DropColumn` first): each user with
  `AnnualLeaveAllowanceDays` → one `Rule(Year, days, CarryOver=false, OnDay = 1 Jan of the current year)`. That is
  exactly today's behaviour (a yearly figure, reset each 1 January), so no balance moves.
- Then drop `AnnualLeaveAllowanceDays`. Check the scaffold for an `xmin` `AddColumn`.
- Archive / PC de secours: the entity has a `ClinicId` and is in neither exclusion list, so it travels like
  `StaffLeaveRequest` — confirm with the scope guard tests.

## Web

| Screen | Change |
|---|---|
| `/users/[userId]` | « Droit annuel » editor → **« Droit à congé »** card: the rule in one line + « Modifier » (dialog: Jours · par an / par mois · à partir du [1er du mois / 1er janvier] · non pris : perdus / reportés) · « Solde de départ » · « Ajouter ou retirer des jours » · the **Relevé** (table from `lg:`, cards below; « Annuler » + motif on manual lines) |
| `/conges` | `staff-leave-balance-card.tsx`: Reste in large, then acquis · pris · en attente · « +2 j le 1er décembre » · « Total pris depuis le début » · « Voir le relevé » |
| Team strip, request cards | same DTO; « Solde après » unchanged in meaning |
| `lib/api/staff-leave.ts` | new calls + types; French labels for `Year`/`Month` in one map |

## Pitfalls

- **Half days everywhere** — validate `Days * 2` integral on every entry and every input (`parseAmountInput`-style).
- **Clinic day, never UTC** — `ClinicClock.ClinicToday()` is the `asOf`; a credit « le 1er » is the Tunisian 1st.
- **Rule `OnDay` alignment** — 1 Jan / 1st of month, or the fold would credit on odd days.
- **Opening vs earlier congés** — an accepted congé before the opening day must not be subtracted twice.
- **Shared helper with 7 call sites** (shape 3) — move them in one commit; grep `AnnualLeaveAllowanceDays` and
  `\.Allowance\b` in `web/` too.
- **No job, no Hangfire** — nothing to miss, nothing to catch up; the 1st of the month « happens » on the next read.
- **Team overview** — one read for the clinic, not one per secretary.

## Tests

- `StaffLeaveLedgerTests` (Domain, fixed dates, no clock): yearly credit on 1 Jan · monthly credits · upcoming not in
  balance · opening hides earlier uses and credits · adjustment ± · expiry removes only a positive remainder · carry-over
  keeps it · negative carries · a newer rule applies from its day only · cancelled entries ignored · congé across
  31/12 split · `TakenTotal` · **last movement balance == Remaining** · same input, same output.
- Command tests: admin only · module off · other cabinet's account 404 · halves refused · misaligned rule day refused ·
  motif required · cancel twice 409.
- Query tests: team overview groups one read; the three balance call sites agree on one fixture.
- Browser QA: set a monthly rule + opening on Ines, accept a congé, see the relevé and the secretary's card agree; 320
  px and 1536×730; the relevé's card form.
