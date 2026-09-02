# Feature Specification: Dépenses mensuelles

**Status:** APPROVED
**Type:** Small
**Created:** 2026-09-02
**Scope:** Full
**Feature:** A dépense can repeat every month, posts itself into la caisse, and can be modified or stopped.

## Overview
La caisse records dépenses one at a time, so a loyer, a salaire or a monthly credit repayment is re-typed
every month — and forgotten in the month nobody remembers. A dépense can now be marked « chaque mois » when
it is recorded, and a daily pass posts it again each month with no clicks. The recurrence is a
forward-looking instruction, not a fact about the past: stopping one (the credit is paid off) leaves every
month already recorded exactly as it is, and needs no reason typed.

## What Changes
- The « Nouvelle dépense » dialog carries one visible switch, **« Répéter chaque mois »**. Ticking it records
  the dépense being typed *and* creates the monthly series, in the one save. No extra field: the day of the
  month is the day of the date already typed.
- La caisse gains a **« Dépenses mensuelles »** card, above les dépenses, listing the active series
  (catégorie · montant · « le N de chaque mois » · mode) with **Modifier** and **Arrêter** on each row.
- A daily background pass posts each active series' missing months as ordinary `Expense` rows, dated in the
  cabinet's calendar. It catches up: a clinic PC switched off for three months gets three rows, one per month.
- Modifying a series changes **future** occurrences only. Every posted row stays an ordinary dépense —
  editable, and deletable under the existing AdminOnly rule — in the table right below.
- Stopping a series is one confirm and no motif. It posts nothing further, ever, including a month it missed
  while still active.

## Acceptance Criteria
- **AC-1:** Recording a dépense with « Répéter chaque mois » on creates one dépense row (visible in la caisse
  and the extrait immediately) and one active series; the series' day of the month is the date that was typed.
- **AC-2:** The daily pass posts, for each active series, one `Expense` per month between the series' last
  posted occurrence and the current clinic-local month inclusive — never a second row for a month it already
  posted, and never a row for a month before the series existed.
- **AC-3:** A posted occurrence is indistinguishable from a hand-typed dépense to every money read (caisse
  totals, extrait, dashboard, CSV export) and is editable and deletable by the same roles as any other. It
  carries a « mensuelle » marker in la caisse so a reader knows where it came from.
- **AC-4:** Deleting or editing one posted occurrence never changes the series and never causes the pass to
  re-post that month.
- **AC-5:** Modifying a series (catégorie, montant, mode, description, jour) leaves every already-posted row
  untouched; the next month posts with the new values.
- **AC-6:** Stopping a series posts nothing further, leaves all posted rows and every caisse figure unchanged,
  and removes it from « Dépenses mensuelles » — with no reason field anywhere in the flow.
- **AC-7:** A series set to the 31st posts on the last day of a shorter month, in the cabinet's calendar
  (`ClinicClock`) — never `DateTime.UtcNow`.
- **AC-8:** A posted occurrence broadcasts the existing `expenses` realtime key, so an open caisse refreshes
  without a reload.
- **AC-9:** At 320 px, « Dépenses mensuelles » renders as cards with no horizontal scroll, Modifier/Arrêter
  reachable from one menu at ≥44 px on a coarse pointer; the « Répéter chaque mois » switch sits in the
  existing dialog's normal flow (a sheet below `md:`) and is reachable without scrolling past the save button.

## API Contract
### POST /api/expenses  *(modified)*
Request adds: `{ repeatMonthly?: boolean }` — when true, the dépense is recorded and a monthly series is
created from the same fields. Response unchanged (`ExpenseDto`).

### GET /api/expenses/recurring
Response 200: `RecurringExpenseDto[]` — the clinic's **active** series only, `{ id, category, amount, method,
description, dayOfMonth, lastPostedMonth, version }`.

### PUT /api/expenses/recurring/{id}
Request: `{ category, amount, method, description, dayOfMonth, version }`
Response 200: `RecurringExpenseDto`
Errors: `400` same French refusals as a dépense (`ExpenseDay.RefuseFields`, montant ≤ 0, mode invalide),
`404 « Dépense mensuelle introuvable. »`, `409` on a stale `version`.

### POST /api/expenses/recurring/{id}/stop
Response 204. Idempotent — stopping an already-stopped series is not an error.
Errors: `404 « Dépense mensuelle introuvable. »`

All three are `AdminOrDoctor`, like the rest of the dépense surface. The hard `DELETE /api/expenses/{id}`
stays AdminOnly and is untouched.

## Data / Schema Changes
- **New entity `RecurringExpense`** (`AggregateRoot<Guid>`, clinic-scoped like `Expense`): `ClinicId`,
  `Category` varchar(100), `Amount` decimal(18,3), `Method`, `Description` varchar(1000) null,
  `DayOfMonth` int (1–31), `LastPostedMonth` date (the first of the last month posted), `CancelledAt` UTC
  null, `CreatedAt`, `UpdatedAt`. Stopped = `CancelledAt` set; nothing is ever deleted.
- **`Expense.RecurringExpenseId`** — nullable FK, `ON DELETE SET NULL` so stopping or removing a series can
  never cascade a posted dépense out of la caisse. Existing rows stay null.
- Commands and queries live in `Features/Expenses/` so no new realtime resource key is emitted.
- ⚠️ The migration must be checked for the scaffolded `AddColumn<uint>("xmin")` on both touched entities.

## Device Behaviour
- **Leading device:** desk, on la caisse.
- **Narrow width (< 640):** « Dépenses mensuelles » is a `CardList` (title = catégorie, fields = montant ·
  « le N » · mode), actions in one `DropdownMenu`. The existing dépense dialog is already a bottom sheet in
  `dvh`; the switch adds one row to its flow, above « Description ».
- **Touch:** every action is a real control, none hover-revealed; `coarse:` sizing to 44 px, as the dépenses
  table's own row actions already do. Floor: `~/.claude/skills/DEVICE-CONTRACT.md`.

## Out of Scope
- Any recurrence other than monthly (weekly, quarterly, annual, « every 2 months »).
- An end date, an occurrence count, or a « pause until » on a series — the only way to end one is Arrêter.
- A reason/motif on stop, a history of stopped series, or a screen listing them.
- Skipping or pre-editing a single future occurrence before it posts.
- Recurring *income*; recurring lab-order or stock postings.
- Turning an existing hand-typed dépense into a series retroactively (the switch is on the dépense being
  recorded; a series is otherwise created by recording the next one with the switch on).

## Edge Cases (Critical only)
- **Month already covered by the typed row:** the dépense saved with the switch on *is* that month's
  occurrence, so the pass starts at the following month even if the day of the month is still ahead.
- **PC off across a month boundary:** the pass posts every missed month on its next run, each dated in its own
  month, and the catch-up cannot double-post because `LastPostedMonth` advances per month posted.
- **Stopped mid-gap:** a series stopped while a month is unposted stays unposted — Arrêter means stop, not
  settle up.
- **One clinic's failure** must not stop the pass for the others (per-clinic try/catch, as `StockExpiryJob`).
- **The pass has no HTTP context**, so it must declare `UseSystemWide(reason)` and `RunAs` — without both it
  reads zero series in every clinic and logs a clean pass.
