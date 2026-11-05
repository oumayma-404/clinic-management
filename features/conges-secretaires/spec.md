# Feature Specification: Congés des secrétaires

**Status:** APPROVED
**Type:** Small
**Created:** 2026-11-05
**Scope:** Full
**Feature:** A secretary asks for a congé, the admin accepts or refuses, and she is told in the app and by e-mail.
**Part 1 of 3** of [`pointage-conges/blueprint.md`](../pointage-conges/blueprint.md) · then
[`pointage-borne`](../pointage-borne/spec.md) · [`pointage-obligatoire`](../pointage-obligatoire/spec.md).
Visuals: https://claude.ai/artifact/LJQDQvKRaLqCLmXvRJrrsF · ⚠️ build from `main`.

## Overview
Today a secretary's congé is agreed on paper or WhatsApp, with no record. In the app:
- She asks with dates, a type and a motif.
- The cabinet's admin answers.
- She is told in the app and by e-mail, with the motif word for word when it is refused.
- Each secretary has a history and a yearly balance.

It is off by default, per cabinet. Doctors keep their own congés (`DoctorLeave`), which this does not touch.

## What Changes
- **Paramètres → new « Équipe » card (admin):** a « Demandes de congé » switch, off by default.
- **Secretary:** a « Congés » rail entry when the switch is on. Its page shows her balance, « Demander un congé »,
  and her requests with their status.
- **Admin, « Utilisateurs »:** becomes tabs « Comptes · Congés ». « Congés » lists the requests to decide first,
  then all the others. The pending count shows on the tab and on the rail entry.
- **Admin decisions:** Accepter, with editable days · Refuser, motif required · « Ajouter une absence » (saved as
  accepted).
- **Admin, a secretary's file (`/users/[id]`):** her yearly allowance, the days taken and left, and her full
  history.

## Acceptance Criteria
- **AC-1:** The switch is off by default.
  - While it is off there is no « Congés » entry and no tab, and the API refuses with `team_module_off`.
  - Switching it off deletes nothing; switching it on again shows everything as before.
- **AC-2:** Only `secretary` accounts can submit. The form has:
  - Type: Congé payé · Maladie · Sans solde · Autre.
  - Du and Au: inclusive, cabinet-local.
  - Demi-journée: only when Du = Au.
  - Motif: required, ≤ 500.
  - « Au » before « Du » is refused on the field. More than 366 days is refused.
- **AC-3:** Before sending, the form states the day count. It is the cabinet's open days between the dates
  (Paramètres → Horaires), ½ for a demi-journée, and Monday–Saturday when no hours are set. For a Congé payé, when
  an allowance is set, it also states the balance after.
- **AC-4:** A request that overlaps another pending or accepted request of the same person is refused, naming the
  other one (« Vous avez déjà une demande du 10/11 au 12/11 »).
- **AC-5:** Every admin of the cabinet is notified (« Amira Ben Salah demande un congé : 10 → 12 nov. (3 j) »)
  through one notification with `TargetRole = admin`, never one row per admin.
- **AC-6:** The admin's request card shows the name, type, dates, days and motif. It also shows the balance after
  (Congé payé with an allowance set) and **who else is already on an accepted congé** on any of those days.
- **AC-7:** « Accepter »:
  - « Décompter » is pre-filled with the counted days and is editable: steps of 0.5, ≥ 0. A jour férié inside the
    dates is taken out here.
  - The accepted number is stored and never recomputed afterwards.
- **AC-8:** « Refuser » requires a motif. An empty motif is refused on the field and by the server
  (`rejection_reason_required`).
- **AC-9:** After a decision she gets a notification (pushed to her phone when the app is installed) and an
  e-mail.
  - Both name the dates and the days, and, when refused, quote the motif word for word.
  - No e-mail is sent when the sender is not configured or she has no address.
  - A failed e-mail never fails the decision.
- **AC-10:** She can cancel a request « En attente », or an accepted one whose first day has not come.
  - Cancelling an accepted one notifies the admins.
  - A started or past request cannot be cancelled (`leave_already_started`).
- **AC-11:** « Ajouter une absence » (admin, same fields) is saved as accepted and shows « Saisi par l'admin ».
  She is notified.
- **AC-12:** The file and her own page both show:
  - The allowance for the current year (admin-editable; empty means none).
  - « Pris » and « Restants » for the year, counting Congé payé only.
  - The history, newest first, with status, who decided and the motif.
  - A congé across 31 Dec → 2 Jan counts its days in the year each date falls in.
- **AC-13:** A change made on one device reaches every open page without a reload (realtime key `team`).
- **AC-14:** A request that another admin already decided answers 409. The dialog offers « Recharger »
  (`useConflict`).
- **AC-15 (device):** At 320 px:
  - Requests are stacked cards with no horizontal scroll, and every action can be reached at ≥ 44 px on a coarse
    pointer.
  - The request form and the refusal dialog open as bottom sheets, with their main button above the keyboard.
  - Below `lg` the « Congés » tab is cards.

## API Contract
### GET /api/team/leave-requests?status=&page=&pageSize= — AdminOnly
Response 200, paged:

```
{ items: [{ id, userId, userName, type, typeLabel, startDate, endDate, halfDay,
            reason, status, statusLabel, requestedDays, countedDays, decidedByName,
            decidedAt, rejectionReason, enteredByAdmin, balanceAfter, othersOff: string[],
            version }],
  total }
```

### GET /api/team/me/leave?year= — own account
Response 200: `{ allowance, taken, remaining, items: [...] }`

### POST /api/team/me/leave-requests — secretary
Request: `{ type, startDate, endDate, halfDay, reason }` → 201, one item.
Errors: `400 leave_invalid_range` · `400 leave_too_long` · `409 leave_overlap` · `403 team_module_off`

### POST /api/team/leave-requests/{id}/decision — AdminOnly
Request: `{ approve, countedDays?, rejectionReason?, version }` → one item.
Errors: `400 rejection_reason_required` · `409 leave_not_pending` · `409` stale version

### POST /api/team/leave-requests/{id}/cancel — requester or admin
Request: `{ version }` → one item. Errors: `409 leave_already_started`

### POST /api/team/users/{userId}/absences — AdminOnly
Request: same as the submit → 201.

### GET /api/team/users/{userId}/file?year= · PUT /api/team/users/{userId}/leave-allowance — AdminOnly
The PUT takes `{ allowanceDays: number | null, version }`.

### PUT /api/clinics/team-modules — AdminOnly
Request: `{ leaveRequests?, version }`. Tri-state: an omitted key means unchanged. Parts 2–3 add keys.

## Data / Schema Changes
- **New table `StaffLeaveRequests`** (audited, tenant-filtered, archived like any clinic table):
  - Who and when: `Id`, `ClinicId`, `UserId`, `Type` (int), `StartDate`, `EndDate` (`date`), `HalfDay`.
  - The request: `Reason` (≤ 500), `Status` (int), `RequestedDays` (numeric 5,1), `CountedDays` (numeric 5,1,
    null).
  - The decision: `DecidedByUserId`, `DecidedAt`, `RejectionReason` (≤ 500), `CancelledAt`, `CancelledByUserId`.
  - Other: `EnteredByAdmin`, `CreatedAt`, `UpdatedAt`, `xmin`.
  - Indexes: `(ClinicId, Status)` and `(ClinicId, UserId, StartDate)`.
- **`Users.AnnualLeaveAllowanceDays`:** numeric(4,1), null.
- **`Clinics.LeaveRequestsEnabled`:** bool, default false. `ClinicDto.leaveRequestsEnabled` must be mapped at
  **all 7** sites.
- **Enums (append-only):** `NotificationCategory` gains 4 members (requested, approved, rejected, cancelled), and
  `NotificationTargetKind` gains `StaffLeaveRequest`.

## Device Behaviour
- **Leading device:** phone (the secretary asks), desk (the admin decides).
- **Narrow width (< 640):** the lists become cards; the forms become bottom sheets in `dvh`.
- **Touch:** no hover-only action. Accept and refuse are visible buttons on each card.

## Out of Scope
- Clocking in and out (part 2) and the arrival lock (part 3).
- A jours fériés calendar: the admin adjusts « Décompter ».
- Payroll and exports to a pay tool.
- Doctors' congés (`DoctorLeave`, already on the agenda).
- Requests from doctor or admin accounts.

## Edge Cases (Critical only)
- **A secretary deactivated with pending requests:** they stay decidable and cancellable by the admin, and her
  history is kept.
- **Role changed away from `secretary`:** she loses the entry; her history stays in her file.
- **Two admins decide at once:** the second gets 409 (AC-14), never a double notification.
