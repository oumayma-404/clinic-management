# Feature Specification: Treatment Plan Workspace — closing the plan loop

**Status:** APPROVED
**Challenged:** Yes
**Type:** Full
**Created:** 2026-07-26
**Scope:** Full
**Branch:** feature/windows-desktop-app (existing)
**Feature:** Turn the treatment plan from a write-only record into the readable, drivable spine of a patient's
care: the plan sees its own appointments, dental records and invoice; it has a real home (a workspace route
plus a patient-page card); its acts are ordered; and it can be amended after acceptance instead of being
cancelled and retyped. Includes every defect found at the plan seam — **nothing is deferred**, tests included.

## Overview

Four earlier features built the loop's *edges* — `dental-core` (the plan aggregate), `clinical-loop-integration`
(odontogram → plan, plan item → appointment, dental record → item done), `unified-billing-ledger`
(« Solde patient ») and `adoption-qa-b-money-trail` (devis → facture bridge). Every edge is a **one-way push**.
Not one is readable in reverse, so the plan cannot show where the patient actually is, and no surface renders
the loop as a whole.

Concretely: `Appointment.TreatmentPlanItemId` is written by exactly one command (`CreateAppointmentCommand.cs:182`)
and consumed by nothing. It *is* exposed on `AppointmentDto` (`AppointmentDto.cs:19`) and typed on the frontend
(`types.ts:151`), but **no component reads it** and **no repository method can query by it** — despite a DB index
existing for exactly that (`AppointmentConfiguration.cs:61`). So the plan's "Gérer" dialog offers **Planifier** on
every open act forever: click it twice and you get two appointments with no warning. `Invoice.TreatmentPlanId`
never reaches `InvoiceDto`, so a devis-born invoice is indistinguishable in `/factures`. The plan has no detail
route at all: it lives as a row in a global table plus a dialog, and on the patient page it is the **8th of 8
tabs**. Eight DTO fields (`doneDate`, `linkedDentalRecordId`, `codeActe`, `acceptedDate`, `cancellationReason`,
`notes`, `createdAt/updatedAt`, `lastMethod/lastPaidOn`) are already on the wire and rendered nowhere. And a
plan is frozen the instant it is accepted (`SetItems`/`SetInstallments` are `EnsureDraft()`-only), so the first
time treatment changes the only escape is Cancel + retype, losing the devis number, the échéancier and all
done-history.

Exploration also surfaced a **money bug**: `MarkTreatmentPlanItemDoneCommandHandler.cs:60-67` auto-completes the
plan when the last act is done, but `RecordInstallmentPayment` calls `EnsureActive()` (`TreatmentPlan.cs:159, 217`)
which rejects `Completed` — so a fully-treated plan can never be paid, while the UI still offers "Encaisser"
(`treatment-plans-table.tsx:468`, whose only excluded status is `Cancelled`). It is latent today only because the
auto-complete path has **no frontend caller** (`treatmentPlansApi.markItemDone` has zero callers) and the
record-driven path (`DentalRecordLinker.LinkPlanItemAsync`) does not auto-complete at all. The two defects mask
each other; fixing either alone breaks money collection.

And the challenge pass found that the plan/invoice de-duplication this feature's « Facturé » badge will expose
exists in **one of four** money reads. `GetPatientBillingSummaryQuery` de-dups; `GetReceivablesQuery`,
`GetCaisseSummaryQuery` and `GetDashboardStatsQuery` do not, and the two plan-repository aggregates additionally
still count **Draft** plans — so `adoption-qa-b-money-trail`'s B1 ("a Draft devis is not debt") was only
half-applied. « Solde patient » and « Créances » already disagree for the same patient. Aligning them is in
scope, because this feature is what makes the discrepancy visible.

This feature adds **no new aggregate and no new link mechanism**. It derives per-act workflow state from the
aggregates that already point at the plan, gives that state a home, orders the acts, lets an accepted plan
evolve, and makes the four money reads agree.

## What Changes

### Slice A — Read-back: the plan sees its own world (no migration)

- **Derived per-act workflow state.** A new batched repository read returns the clinic's appointments linked to
  a set of plan items; `TreatmentPlanItemDto` gains derived `ScheduledAppointmentId` / `ScheduledAt` /
  `ScheduledAppointmentStatus`. State is *derived, never stored* — cancelling or deleting the appointment
  correctly un-schedules the act, so no value is added to `TreatmentPlanItemStatus` (which stays `Planned`/`Done`).
  Four états, computed:

  | État | Rule | French copy | Primary action |
  |---|---|---|---|
  | `À planifier` | no live appointment | « À planifier » | **Planifier** |
  | `Planifié` | live appointment, **future** | « Planifié le 12/08 » | **Voir le RDV** |
  | `À enregistrer` | live appointment, **past**, act not Done | « À enregistrer (RDV 02/08) » | **Enregistrer la fiche** |
  | `Réalisé` | `item.Status == Done` | « Réalisé le 02/08 » | **Voir la fiche** |

  `À enregistrer` exists because « Planifié le 02/08 » is simply false for a visit that already happened, and
  because `NextAppointmentAt` (earliest *future*) already skips it — without the fourth état the header shows no
  next séance while an act claims to be scheduled. The app already tracks this state as a `PostVisitReview`
  `StaffNotification`; the plan would otherwise be the one surface that ignores it.
- **Derived plan-level progress.** `TreatmentPlanDto` gains `ItemsDone` / `ItemsTotal`, `NextAppointmentAt`, and
  `LinkedInvoiceId` / `LinkedInvoiceNumber` / `LinkedInvoiceStatus`.
- **One shared projection.** The derivation lives in a single co-located static helper
  (`TreatmentPlanWorkflowProjection`), mirroring `DentalRecordLinker` / `AppointmentPlanLink` /
  `TreatmentPlanItemPricing`; both plan queries call it. One appointments query + one invoice-links query per
  request — never per plan, never per patient.
- **`PatientPlanCard` on the patient page**, mounted beside the existing « Solde patient » card (above the tabs,
  not inside the 8th one). Plan selection priority: most recently accepted `Accepted`/`InProgress`, else most
  recently created `Draft`, else the card is not rendered at all (not an empty box). Multiple active plans →
  a "+N autres" link.
  - **Active card:** number, statut, progress, « Prochaine séance : mer. 12 août 09:00 », one primary action.
  - **Draft card:** « Devis — Brouillon », act count + total, « À accepter pour démarrer le suivi », and
    **Accepter** / **Ouvrir**. No progress bar (nothing can be done yet) and no « prochaine séance » (a draft's
    acts can't be booked). It must **never** show « Reste » — a Draft contributes 0 to « Solde patient » by
    design, and showing a balance would contradict that.
- **Duplicate work becomes impossible.** "Planifier" is hidden for an act that already has a live appointment;
  "Facturer le devis" is hidden once a linked non-cancelled invoice exists.
- **Realtime across all three feeding aggregates.** Every derived field comes from a *different* aggregate, and
  `RealtimeBroadcastBehavior` derives its key from the **command's** namespace — so cancelling an appointment
  broadcasts `"appointments"`, never `"treatmentplans"`. The workspace, `PatientPlanCard` and the plans table
  therefore each subscribe to **`[TreatmentPlans, Appointments, Invoices]`** in one `useClinicRealtime` call,
  following `patients/[id]/page.tsx:184-187`. Without this, a peer cancelling the RDV leaves the plan showing
  « Planifié » with "Planifier" hidden — an act that looks booked and cannot be booked.
- **Money bug fixed.** New `EnsurePayable()` (Accepted | InProgress | **Completed**) guards
  `RecordInstallmentPayment`; `EnsureActive()` continues to guard `MarkItemDone`. A `Completed` plan means *all
  acts done*, not *paid* — the clinical track closing must not close the financial track. Recording a payment on
  a `Completed` plan leaves it `Completed` (the existing `Accepted → InProgress` bump already only fires from
  `Accepted`).
- **Auto-complete unified.** The "all acts done ⇒ `Complete()`" rule moves **into** `TreatmentPlan.MarkItemDone`,
  so the record-driven path behaves identically to the command path; the duplicated block in
  `MarkTreatmentPlanItemDoneCommandHandler` is removed.
- **Invoice → plan navigation.** `InvoiceDto` exposes `TreatmentPlanId`; the invoices table shows a « Devis »
  badge linking back to the plan.
- **Record modal auto-links.** Opening the dental-record modal from an appointment that carries
  `treatmentPlanItemId` pre-selects that plan act instead of relying on the dentist remembering the dropdown.
- **Two FE contract fixes:** `types.ts` `TreatmentPlanDto.updatedAt` becomes `updatedAt?: string | null` — it is
  currently the only bare non-optional `updatedAt` in the file while every other nullable date, including
  `acceptedDate`, `doneDate` and `lastPaidOn` in the same cluster, uses `| null`. And the `/treatment-plans` date
  filter stops building timezone-naive strings (`${from}T00:00:00`) for comparison against UTC `CreatedAt`.
- **Hygiene while in the file:** the untranslated "Back to Patients" button on the patient page
  (`page.tsx:479`) becomes « Retour aux patients » — line 355 of the same file already says it correctly.

### Slice A2 — Make the four money reads agree (no migration)

Pre-existing, pulled into scope because slice A's « Facturé » badge is what exposes it: after slice A the
workspace says « Facturé — note 2026-0031 » while « Créances » still counts that patient for the plan amount.

- **`GetReceivablesQuery`, `GetCaisseSummaryQuery` and `GetDashboardStatsQuery` gain the same de-dup**
  `GetPatientBillingSummaryQuery.cs:73-85` already has: a plan represented by a linked non-Draft non-Cancelled
  invoice is counted through that invoice, never twice.
- **`ITreatmentPlanRepository.GetInstallmentOutstandingByPatientAsync` and
  `GetInstallmentCollectedBetweenAsync` exclude `Draft` plans**, finishing what `adoption-qa-b-money-trail` B1
  intended — they currently exclude only `Cancelled`, so a draft devis with a hand-built échéancier counts as
  debt in « Créances » and the dashboard while the summary correctly ignores it.
- **One shared de-dup helper** so a fifth money read cannot drift again.
- **Deliberately still out of scope**, both verified and both genuinely separate designs:
  - **Bridging loses already-collected installment money.** A plan with 800/1000 DT collected via échéances,
    once bridged and issued, reports 1000 DT outstanding (the bridge invoice's `AmountCollected` is 0) *and* the
    plan is suppressed — the 800 vanishes from « Solde patient ». Needs a payment-carry-over design.
  - **`GetDashboardStatsQuery` does not net out `CreditNote` refunds**, unlike `GetCaisseSummaryQuery` and
    `GetInvoiceRevenueQuery`.

  Both are recorded here so they are not lost, and neither is made worse by this feature.

### Slice C — The workspace: a home for the plan

- **New route `/treatment-plans/[id]`** — the first dynamic route in the plan area, `"use client"`, reading its
  param with `useParams()` and using the standard page shell (`ClinicGuard → DashboardSidebar → DashboardHeader
  → main`). Sections:
  - **Header** — devis number (or title) with « · révision N » when `RevisionNumber > 0`, patient name linking to
    the patient page, statut badge, a **hand-rolled** progress bar, `Total / Encaissé / Reste`, « Prochaine
    séance », and the plan's single primary action. There is no progress primitive in the codebase
    (`components/ui/` has 20 files, no `progress.tsx`, and no `@radix-ui/react-progress`), so it is two divs with
    `role="progressbar"` + `aria-valuenow`/`-valuemin`/`-valuemax`, guarded against `ItemsTotal === 0`.
  - **Actes** — ordered list; each row shows désignation, `CodeActe`, dents, coût, its état, and **exactly one**
    primary action per the état table above.
  - **Échéancier** — the existing installments table (Encaisser + reçu), with the « En retard » badge.
  - **Parcours** — a chronological feed built entirely from data already on the wire: plan created,
    `AcceptedDate`, per-act `ScheduledAt` and `DoneDate`, installment `LastPaidOn` + `LastMethod`, invoice
    issued. It reuses the **`notification-panel.tsx:75-105` shape** (`<ul className="divide-y divide-border">`
    with a circular icon badge per row and a French date) rather than inventing a vertical-line timeline — no
    timeline component exists, and reusing the app's established activity-feed idiom keeps it looking native.
    Also renders `Notes` and `CancellationReason`, which nothing displays today.
  - **Loading / not-found / error** follow `patients/[id]/page.tsx:326-362`: a state-driven French card rendered
    *outside* `ClinicGuard` — « Plan introuvable » + « Retour aux plans » on a 404 or garbage id, and
    « Chargement du plan de traitement... » while loading. There is no `notFound()` and no `not-found.tsx`
    anywhere in `web/`; do **not** copy `patients/[id]/files/page.tsx`, which has no error state at all and
    silently renders with `patient === null`.
  - **Back-navigation** is a ghost `Button` + `ArrowLeft` + `router.push("/treatment-plans")` — never
    `router.back()`, which has zero uses in the repo.
- **The plans table stops being the detail view.** The "Gérer" dialog (`treatment-plans-table.tsx:350-509`) and
  the 8-icon ghost action row are retired: a row links to the workspace and keeps only a small **labelled**
  dropdown (Modifier / Supprimer for a draft, PDF for everything). The patient page's plan tab lists plans and
  links out.
- **Stale appointment links become impossible.** `UpdateAppointmentCommand` accepts and re-validates
  `TreatmentPlanItemId` through the existing `AppointmentPlanLink`, calling the currently-dead
  `Appointment.SetTreatmentPlanItem` (zero callers today) — so rescheduling an appointment onto a different act
  updates the link rather than leaving a stale one.

### Slice B — Sequencing and an amendable plan (one migration)

- **`TreatmentPlanItem.SequenceNumber`** (int) so a 12-act plan reads in clinical order; the workspace reorders
  with up/down controls. New acts append at `max(SequenceNumber) + 1`.
- **Id-preserving `SetItems`.** The client echoes back the `Id` of unchanged lines and the domain reuses it,
  instead of `Guid.NewGuid()`-ing every item on every draft edit. This removes the reason
  `Appointment.TreatmentPlanItemId` has no FK, and fixes the latent dangling-link defect where editing a draft
  silently orphans an appointment or record link.
- **`AddItems` / `RemoveItem` on an accepted or in-progress plan.** Adding recomputes `TotalPlanned`. `RemoveItem`
  refuses an act that is already `Done`, **and refuses an act with a live appointment** —
  « Cet acte a un rendez-vous prévu le JJ/MM. Annulez ou déplacez le rendez-vous avant de retirer l'acte. »
  `Appointment.TreatmentPlanItemId` has no FK, so nothing at the DB level would catch the orphan: the row would
  point at a vanished id, the patient would still be booked (with reminders already sent) for work that no longer
  exists, and the derived état could never resolve. The app cannot know whether that patient should be un-booked
  or the slot re-purposed — that is a phone call, not a cascade. This mirrors `RemoveItem` refusing a `Réalisé`
  act: the domain declines when the outside world has already moved on.
- **An already-billed plan cannot be amended.** `AddItems` / `RemoveItem` / `ReviseInstallments` refuse while
  the plan has a linked non-cancelled `Invoice`, with
  « Ce devis est déjà facturé. Annulez la facture (ou émettez un avoir) avant de modifier le plan. »
  **This is a correctness requirement, not a convenience guard.** `GetPatientBillingSummaryQuery.cs:73-85`
  excludes any plan with a linked issued invoice from « Solde patient » — the invoice is taken to *represent*
  the plan, and the bridge invoice's lines froze at `SetLines` (`Invoice.cs:128` is `EnsureDraft()`-gated), with
  no re-sync command anywhere. Amending a billed plan would therefore make the added acts invisible in
  « Solde patient » and, after slice A2, in every other money read too: a silent **undercount**, the exact bug
  class `adoption-qa-b-money-trail` was written to eliminate. Blocking the amendment makes it impossible by
  construction, and the escape hatch already exists (cancel the invoice while unpaid, or issue an avoir once
  paid). Most amendments happen before billing, so the common case is unaffected.
- **`ReviseInstallments`** replaces the schedule on an accepted plan: paid installments keep their id and can
  never drop below their `AmountPaid`, and the schedule must still sum **exactly** to `TotalPlanned`. That last
  invariant is load-bearing beyond this feature: « Solde patient » uses `plan.Outstanding`
  (`TotalPlanned − Σ AmountPaid`) while « Créances » and the dashboard use
  `Σ (installment.Amount − installment.AmountPaid)`, and the two agree **only** while
  `Σ installment.Amount == TotalPlanned`. Today nothing re-enforces that after acceptance because nothing can
  change the total; this feature makes it changeable, so the invariant becomes mandatory. The **domain
  validates, the caller composes** — exactly as `SetInstallments` already delegates the millime remainder to the
  caller (`TreatmentPlan.cs:92-93`), and the frontend already has remainder-on-last-row logic
  (`treatment-plan-form-modal.tsx:262-288`).
- **`TreatmentPlan.RevisionNumber`** (int, default 0), bumped by every post-acceptance amendment. The devis PDF
  and the workspace header print « Devis 2026-0014 · révision 2 », so a patient holding an earlier printout can
  tell which version they signed — the devis PDF has **no snapshot and no immutability** today: it re-renders
  live from current entity state on every download, under the same number and the same `Le {date}`, and is
  stored nowhere (`GetDevisPdfQuery` injects no `IFileStorage`). The plan **number is never reused, suffixed or
  reassigned**; `SetAcceptedNumber` stays scoped to its numbering-collision retry.
- **`MarkDone` guard.** Re-marking a `Done` act no longer silently overwrites `DoneDate` /
  `LinkedDentalRecordId`; it is rejected when the item is already done and linked to a *different* dental record,
  and is a no-op when re-linked to the same one (idempotent for a record update).
- **`SetItems` no longer silently destroys the échéancier.** Wiping the schedule becomes explicit: `SetItems`
  throws if the plan has installments whose sum would no longer match, and the update handler resends the
  schedule (masked today only because the form always happens to).

## API Contract

### Changed — `GET /api/treatment-plans` and `GET /api/treatment-plans/{id}`
Response `TreatmentPlanDto` gains, all additive and all **derived** (never persisted):
```
itemsDone: int, itemsTotal: int,
nextAppointmentAt: datetime|null,
linkedInvoiceId: guid|null, linkedInvoiceNumber: string|null, linkedInvoiceStatus: string|null
```
plus persisted `revisionNumber: int` (slice B). Each `items[]` entry gains:
```
scheduledAppointmentId: guid|null, scheduledAt: datetime|null, scheduledAppointmentStatus: string|null,
sequenceNumber: int                                    // slice B
```
Errors unchanged (`400 { error }` for an invalid status filter; `404` for a missing/other-clinic plan).

### Changed — `POST` / `PUT /api/treatment-plans` (slice B)
`TreatmentPlanItemRequest` gains optional `id: guid|null`. When present and it matches an existing line on the
plan, that line keeps its id. Unknown ids are ignored (treated as a new line), never an error.

### New — `POST /api/treatment-plans/{id}/amend` · **`[Authorize(Policy = AdminOrDoctor)]`**
Add and/or remove acts on an accepted/in-progress plan, and revise the échéancier in the same call so the
schedule can never be left out of sync with the total.
Request: `{ addItems: TreatmentPlanItemRequest[], removeItemIds: guid[], installments: { id?: guid, dueDate, amount }[] }`
Response 2XX: `TreatmentPlanDto` (`revisionNumber` incremented)
Errors: `400 { error }` — plan is Draft or Cancelled · the plan has a linked non-cancelled invoice · an act to
remove is already réalisé · an act to remove has a live appointment · the schedule does not sum to the new total ·
an installment would drop below its `AmountPaid` · the new total is below `AmountPaid`.
`404` — plan not found / other clinic.
`installments` may be omitted when the amendment leaves `TotalPlanned` unchanged; it is **required** when the
total changes.

### New — `PUT /api/treatment-plans/{id}/installments` · **`[Authorize(Policy = AdminOrDoctor)]`**
Revise only the schedule (no act change).
Request: `{ installments: { id?: guid, dueDate, amount }[] }`
Response 2XX: `TreatmentPlanDto` · Errors: as above.

### New — `PUT /api/treatment-plans/{id}/items/order` · no policy (class-level `[Authorize]` only)
Request: `{ itemIds: guid[] }` — the acts in the desired order; the handler assigns `SequenceNumber` by position.
Response 2XX: `TreatmentPlanDto` · Errors: `400 { error }` if the set doesn't match the plan's acts exactly.

**Why these policies.** The repo gates exactly two classes of operation: `AdminOnly` for reference data and
settings, and `AdminOrDoctor` for the three operations that reverse or alter an already-issued financial document
(`InvoicesController.CancelInvoice`, `InvoicesController.CreateCreditNote`, `TreatmentPlansController.CancelPlan`).
`AuthorizationPolicies.cs:39` says so explicitly: *"Admins and doctors — e.g. cancelling an issued invoice."*
Amending a numbered devis changes what the patient owes, so it belongs to that class; reordering acts is cosmetic
and matches the unpoliced accept/complete. Enforcement is **controller-only, with no handler-level re-check** —
`CancelInvoiceCommand.cs:11-12` documents that as deliberate for this class.

### Changed — `PUT /api/appointments/{id}`
Request gains optional `treatmentPlanId` + `treatmentPlanItemId`. When both are present they are validated by
`AppointmentPlanLink` (same clinic + same patient + the act exists) and stored; when `treatmentPlanItemId` is
explicitly `null` the link is cleared. Omitting both leaves the link untouched (so existing callers are
unaffected). Errors: `400 { error }` for an unknown/cross-tenant act.

### Changed — invoice responses
`InvoiceDto` gains `treatmentPlanId: guid|null` (already persisted since
`20260724125528_AddInvoiceTreatmentPlanLink`, previously not exposed to any API consumer).

### Changed — money reads (slice A2, no contract change)
`GET /api/billing/receivables`, `GET /api/dashboard/stats` and the caisse summary keep their response shapes; only
their **figures** change, to match « Solde patient » for the same patient.

### Unchanged, deliberately
`POST /api/treatment-plans/{id}/items/{itemId}/done` stays as an authenticated seam with **no UI**. An act
reaches `Réalisé` only through a linked dental record — the evidence rule established by
`clinical-loop-integration` AC-6 — and this feature does not reintroduce an evidence-free toggle.

## Data / Schema Changes

- **`TreatmentPlanItems.SequenceNumber`** — new `int`, not null, `defaultValue: 0`. Existing rows get `0` and are
  then ordered by their current list position on first save.
- **`TreatmentPlans.RevisionNumber`** — new `int`, not null, `defaultValue: 0`. Existing plans read as revision 0
  (never amended), and the devis prints no revision mention at 0.
- Both columns land in **one** additive migration (`AddTreatmentPlanRevisionAndItemSequence`), styled on
  `20260724125528_AddInvoiceTreatmentPlanLink.cs`.
- **No other schema change.** Every derived field is computed at query time from `Appointment`,
  `TreatmentPlanItem` and `Invoice` rows that already exist. `Appointment.TreatmentPlanItemId` and
  `Invoice.TreatmentPlanId` stay plain no-FK columns.
- **New repository reads** (no new repository, no DI change — every repo is already `AddScoped`):
  - `IAppointmentRepository.GetByTreatmentPlanItemIdsAsync(clinicId, itemIds, ct)` — one batched query; empty
    input returns empty. Deliberately **no** `.Include` (unlike every other read in `AppointmentRepository`),
    since only id/date/status are needed. This is the first query to use the existing
    `IX_Appointments_TreatmentPlanItemId` index.
  - `IInvoiceRepository.GetTreatmentPlanLinksAsync(clinicId, ct)` → `IReadOnlyList<(Guid TreatmentPlanId,
    Guid InvoiceId, string? Number, InvoiceStatus Status)>` — a light tuple projection mirroring
    `GetInstallmentOutstandingByPatientAsync`, so a badge never loads `Lines` + `Payments`.
  - The two `ITreatmentPlanRepository` installment aggregates gain a `Draft` exclusion (slice A2).
- **New domain members:** `TreatmentPlan.EnsurePayable()`, `AddItems`, `RemoveItem`, `ReviseInstallments`,
  `SetItemOrder`, `RevisionNumber`; `Installment.SetAmount(decimal)` (guarded `amount >= AmountPaid`);
  `TreatmentPlanItem.SequenceNumber` + a guarded `MarkDone`. This aggregate uses **public constructors, not
  static factories** (`TreatmentPlan.cs:45`, `TreatmentPlanItem.cs:29`) — keep it that way.
- **No audit field is added.** No entity in this codebase records *who* mutated a financial document — not
  `Invoice`, `TreatmentPlan`, `Payment`, `Installment`, `Expense` or `CreditNote`, and not even `StockMovement`,
  the one append-only log. There is no audit table. Adding the first-ever actor field is a larger architectural
  change than this feature should make, so "who amended this devis" stays unanswerable; what the feature does
  give is `RevisionNumber` (how many times), `UpdatedAt` (when last) and `AdminOrDoctor` (which roles could).
  This is the prerequisite to name in any future audit requirement.

## Acceptance Criteria

**Read-back (slice A)**
- **AC-1:** A plan act with a linked future `Scheduled`/`Confirmed`/`InProgress` appointment reports
  `scheduledAppointmentId` + `scheduledAt` and renders « Planifié le JJ/MM »; an act with none renders
  « À planifier ».
- **AC-2:** An act whose only linked appointment is `Cancelled` or `NoShow` reports **no** scheduling data and
  returns to « À planifier », so it can be booked again.
- **AC-3:** An act with several linked appointments reports the earliest **future** live one; when all are in the
  past it reports the latest past live one (so a réalisé act still shows the visit it happened at).
- **AC-3a:** An act with a **past** live appointment that is not yet `Réalisé` renders « À enregistrer (RDV
  JJ/MM) » with "Enregistrer la fiche" as its primary action — never « Planifié » with a past date.
- **AC-4:** "Planifier" is not offered for an act that already has a live appointment; "Facturer le devis" is not
  offered once a linked non-cancelled invoice exists. Neither duplicate can be created from the UI.
- **AC-5:** `TreatmentPlanDto` reports `itemsDone`/`itemsTotal`, `nextAppointmentAt` and the linked-invoice
  triple; a plan with zero acts renders no progress bar and never divides by zero.
- **AC-6:** Listing N plans issues **one** appointments query and **one** invoice-links query in total — not one
  per plan and not one per patient. The pre-existing per-patient name lookup in `GetTreatmentPlansQueryHandler`
  is also collapsed to a single `GetByClinicIdAsync`.
- **AC-7:** The patient page shows a `PatientPlanCard` above the tabs. With an accepted/in-progress plan it shows
  number, statut, progress, prochaine séance and one primary action. With only a `Draft` plan it shows
  « Devis — Brouillon », act count, total and **Accepter**/**Ouvrir** — with no progress bar, no prochaine séance
  and **no « Reste »**. With no plan at all it renders nothing.
- **AC-8:** `InvoiceDto.treatmentPlanId` is populated for a devis-born invoice, and the invoices table links back
  to that plan.
- **AC-9:** Opening the dental-record modal from an appointment carrying `treatmentPlanItemId` pre-selects that
  plan act, with no manual dropdown step.
- **AC-9a:** The workspace, `PatientPlanCard` and the plans table each subscribe to
  `[TreatmentPlans, Appointments, Invoices]`. A peer cancelling a linked appointment, or issuing the linked
  invoice, updates every open plan surface without a manual reload.

**Money and lifecycle correctness (slice A)**
- **AC-10:** An installment payment can be recorded on a `Completed` plan; the plan stays `Completed` and
  `Outstanding` decreases. A `Draft` or `Cancelled` plan still rejects it.
- **AC-11:** Marking the last act done — **through either path** (`MarkTreatmentPlanItemDoneCommand` *or* a
  linked dental record) — moves the plan to `Completed`. The behaviour is identical for both.
- **AC-12:** `MarkTreatmentPlanItemDoneCommandHandler` no longer carries its own auto-complete block; the rule
  exists in exactly one place.

**Money-read consistency (slice A2)**
- **AC-12a:** For the same patient and the same data, « Solde patient », « Créances » and the dashboard's
  outstanding figure report the **same** total. A plan billed to an issued invoice is counted once, through the
  invoice, on all three.
- **AC-12b:** A `Draft` plan with a hand-built échéancier contributes 0 to « Créances », the caisse and the
  dashboard, matching « Solde patient ».
- **AC-12c:** The de-dup rule exists in exactly one shared helper; no money read reimplements it.

**Workspace (slice C)**
- **AC-13:** `/treatment-plans/{id}` renders header, actes, échéancier and parcours for a plan of the caller's
  clinic. A missing id, a garbage id, or another clinic's plan id renders the French « Plan introuvable » card
  with a « Retour aux plans » action — never data, never a blank page, never an unhandled throw.
- **AC-13a:** While loading, the route shows « Chargement du plan de traitement... » inside the standard shell;
  a failed fetch surfaces its message in the same card as AC-13.
- **AC-14:** Each act row offers exactly one primary action, correct for its état, and « Voir le RDV » /
  « Voir la fiche » navigate to the linked appointment / dental record.
- **AC-15:** The parcours shows plan creation, acceptance, each scheduling and completion, each installment
  payment with its method, and invoice issuance — in chronological order. `notes` and `cancellationReason` are
  displayed where present.
- **AC-16:** The plans table no longer contains a detail dialog or an unlabelled icon-only action row; rows
  navigate to the workspace and remaining actions are labelled.
- **AC-17:** `PUT /api/appointments/{id}` can move an appointment's plan-act link and can clear it; a
  cross-clinic or unknown act is rejected with `400 { error }`; omitting the fields leaves the link untouched.
- **AC-17a:** The progress bar carries `role="progressbar"` with `aria-valuenow`/`-valuemin`/`-valuemax`, and is
  absent (not zero-width) when `itemsTotal === 0`.

**Sequencing and amendment (slice B)**
- **AC-18:** Acts render in `SequenceNumber` order; reordering persists and survives reload. Ties fall back to
  the existing insertion order so a pre-migration plan does not reshuffle before its first reorder.
- **AC-19:** Editing a draft plan's lines preserves the `Id` of every echoed-back unchanged line, so an
  appointment or dental-record link to that act still resolves afterwards.
- **AC-20:** An act can be added to an accepted/in-progress plan; `TotalPlanned` increases, `RevisionNumber`
  increments, and the devis number, the paid installments and every réalisé act are all preserved. The new act
  receives `max(SequenceNumber) + 1`.
- **AC-21:** Removing an act that is `Réalisé` is rejected. Removing an act that has a **live appointment** is
  rejected, with a message naming the appointment date and the remedy. Removing an open, unbooked act is allowed
  and lowers `TotalPlanned`.
- **AC-22:** An amendment whose resulting total is below `AmountPaid` is rejected; a schedule that does not sum
  to the new `TotalPlanned` is rejected; an installment revised below its own `AmountPaid` is rejected. All three
  return `400 { error }` with a French message and commit nothing.
- **AC-22a:** Amending a plan that has a linked non-cancelled invoice is **rejected** — add, remove, and revise-
  schedule all refuse, nothing is committed, and the message names the remedy (cancel the invoice / issue an
  avoir). Cancelling that invoice then makes the plan amendable again. A plan whose only linked invoice is
  `Cancelled` is amendable.
- **AC-22b:** After any successful amendment, `Σ installment.Amount == TotalPlanned` still holds, so
  `plan.Outstanding` and `Σ (installment.Amount − AmountPaid)` agree — the two formulas the money reads use.
- **AC-22c:** `RevisionNumber` starts at 0, increments once per successful amendment, and appears on the devis
  PDF and the workspace header only when > 0. The plan `Number` is never reused, suffixed or reassigned.
- **AC-23:** Re-marking an already-`Done` act as done is a no-op when it links the same dental record, and is
  rejected when it links a different one — `DoneDate` is never silently overwritten.

**Cross-cutting**
- **AC-24:** Every new command and query is clinic-scoped: a cross-clinic plan, act, appointment or invoice id
  reads as « introuvable » and performs no write (asserted with `Times.Never`).
- **AC-24a:** `POST {id}/amend` and `PUT {id}/installments` carry `[Authorize(Policy = AdminOrDoctor)]`;
  `PUT {id}/items/order` carries no method-level policy; and a reflection test pins all three **plus** the
  existing `CancelPlan`, which nothing pins today.
- **AC-25:** New plan commands broadcast the `treatmentplans` realtime key (automatic via
  `RealtimeBroadcastBehavior`), and `treatmentplans` is pinned in `RealtimeResourceResolverTests`.
- **AC-26:** `dotnet build` is clean (0 errors / 0 new warnings); `npx tsc --noEmit` and `npm run build` are
  clean; the full unit suite passes.

## Out of Scope

- **Séances / visit grouping** — grouping acts into a numbered séance and booking one appointment covering N
  acts (Open Dental's "Planned Appointment"). It is a superset of this feature and needs partial-completion
  semantics (2 of 3 acts done) that do not exist; `SequenceNumber` here is deliberately a plain ordering field,
  not a séance id, so the later change is additive.
- **Per-act invoicing.** `Invoice.TreatmentPlanId` is plan-level, so « Facturé » stays a plan badge; a
  « facturer les actes réalisés » per-séance flow is a separate money-trust feature.
- **Collected installment money surviving the devis→facture bridge**, and **`GetDashboardStatsQuery` netting out
  `CreditNote` refunds** — both verified, both recorded in slice A2, both genuinely separate designs.
- **An audit trail / actor field** on financial documents — see Data / Schema Changes for the full rationale.
- **Frontend role gating.** The existing `AdminOrDoctor` endpoints (invoice cancel, avoir, plan cancel) are
  called unconditionally from the UI and rely on the server's 403; the new endpoints match that, rather than
  introducing a role check pattern the billing surfaces don't have.
- **Wider patient-page localization.** Fixed here: the one « Back to Patients » on the file this feature edits.
  Recorded, not fixed: `Back to Patient` (`patients/[id]/files/page.tsx:75`) and three raw enum names rendered as
  user-facing text — `{appointment.status}` (`page.tsx:1116`), `{flag.flagType}` (`:491`), `{file.fileType}`
  (`:1279`) — plus `formatFileSize` emitting `B`/`KB`/`MB` instead of `o`/`Ko`/`Mo` (`:393`). Those need
  `*-labels.ts` maps for three more enums: a localization pass, not the plan loop.
- **A backend odontogram → plan seed command.** Seeding stays the existing frontend prefill; the stale
  "seedable from the odontogram" wording in `api/ClinicManagement.API/CLAUDE.md` is corrected as documentation
  only.
- **Merging the two act catalogs** (`ProcedureType` vs CNAM `DentalActCode`) — pre-existing separate effort.
- **Treatment-plan phases** (urgence / assainissement / restauration / esthétique) as a first-class field —
  `SequenceNumber` covers ordering; named phases are a later, additive enhancement.
- **A devis PDF snapshot.** The devis re-renders live on every download and is stored nowhere;
  `RevisionNumber` is what lets a patient's printout be identified, not an archived copy.
- **Reintroducing an evidence-free « Réalisé » toggle** — explicitly excluded by
  `clinical-loop-integration` AC-6.
- **An `Avoir` for a devis.** `CreditNote` is scoped to `InvoiceId` only and has no `TreatmentPlanId`; the devis
  is not a fiscal document, so `RevisionNumber` is its correction mechanism.

## Edge Cases (Critical only)

- **Cancelled appointment un-schedules the act.** The highest-risk detail in the feature: if the derivation
  counted a `Cancelled`/`NoShow` appointment, the act would show « Planifié » forever *and* "Planifier" would
  stay hidden — making it permanently unbookable. Only `Scheduled`/`Confirmed`/`InProgress`/`Completed` count
  as live, and the same reasoning is why every plan surface must subscribe to the `appointments` realtime key.
- **Auto-complete and payability must ship together.** Making auto-complete reachable without `EnsurePayable`
  would lock a fully-treated plan out of collecting its remaining balance; adding `EnsurePayable` alone leaves
  plans unable to reach « Terminé » from the record path. Neither half is independently correct.
- **Plans will now reach « Terminé » by themselves.** This is a visible behaviour change: today a
  fully-treated plan stays `InProgress` until someone clicks Terminer. Required by AC-11, but it must be stated
  in the release note.
- **Amending an already-billed plan is blocked, not reconciled.** After slice A2 all four money reads treat a
  linked invoice as *representing* the plan. Rather than teach them to compute a plan-vs-invoice delta —
  re-complicating the money paths slice A2 just unified — the amendment is refused while a non-cancelled linked
  invoice exists. The dentist's path is: cancel the draft/issued-unpaid invoice, amend, re-bill; or, once paid,
  issue an avoir. Deliberately *not* offered: auto-cancelling the invoice as a side effect of amending — a plan
  edit must never silently void a numbered financial document.
- **Amending a plan with a fully-paid échéancier.** When every installment is paid and an act is added,
  `ReviseInstallments` must be able to *append* a new installment for the difference; it can never reduce a paid
  one. When the caller sends no schedule for a total that changed, the request is rejected rather than silently
  leaving `TotalPlanned` and the schedule out of sync.
- **Cancelling a bridge invoice re-opens the plan.** `CancelInvoiceCommand` does not clear
  `Invoice.TreatmentPlanId` (the property has `private set` and no mutator), and the money reads exclude
  cancelled invoices — so the plan silently re-enters the balance. That is the correct behaviour and is what
  makes the amendment block escapable; it is documented here because it is non-obvious.
- **`SequenceNumber` backfill.** Existing rows default to `0`; ordering must be stable for ties (fall back to the
  existing insertion order) so a pre-migration plan doesn't reshuffle on screen before its first reorder.
- **A plan act whose appointment was deleted.** Because state is derived, the act simply returns to
  « À planifier » — no orphan cleanup, no stored flag to repair. This is the reason for deriving rather than
  storing.
- **Draft plan items linked from an appointment.** Until AC-19 lands, a draft edit regenerates ids and orphans
  the link. Slice B fixes the cause; slices A and C must not add any new UI that links to a *draft* plan's acts
  (the existing "Planifier" is already gated to Accepted/InProgress, and the Draft `PatientPlanCard` offers only
  Accepter/Ouvrir).
- **Multiple active plans for one patient.** `PatientPlanCard` shows the most recently accepted and links to the
  rest; it never silently hides an in-progress plan.
- **Local mode / offline.** All new reads and writes are pure DB work with no internet dependency; the workspace
  and the card behave identically offline.

## Tests

**In scope for this feature — not deferred to `/test-small-feature`.** The plan area currently has **zero**
tests: there is no `Features/TreatmentPlans/` folder in `ClinicManagement.UnitTests` and no `Features/Billing/`
either, and `ITreatmentPlanRepository` appears only as a constructor filler in four unrelated classes. This is
greenfield.

Follow `Features/Invoices/IssueInvoiceCommandHandlerTests.cs`: fixed `aaaa…`/`bbbb…`/`cccc…` GUID constants,
mocks as readonly instance fields, a private `CreateHandler()` factory, `NullLogger<T>.Instance`,
`Mock<ICurrentClinicResolver>` for clinic scoping, `PascalCase_With_Underscores` method names, plain xUnit
`Assert.*` (**no FluentAssertions**), negative `Times.Never` verification, and an AC comment on every `[Fact]`.

- **`Domain/TreatmentPlanTests.cs`** (no mocks) — `EnsurePayable` allows Completed and rejects Draft/Cancelled
  (AC-10); `MarkItemDone` on the last act auto-completes (AC-11); `RemoveItem` refuses a Done act and refuses one
  with a live appointment (AC-21); `ReviseInstallments` rejects a below-`AmountPaid` installment, a non-matching
  sum, and a total below `AmountPaid` (AC-22); the `Σ installment.Amount == TotalPlanned` invariant survives an
  amendment (AC-22b); `RevisionNumber` increments once per amendment (AC-22c); `SetItems` preserves echoed ids
  (AC-19); `MarkDone` idempotence and different-record rejection (AC-23); `SetItemOrder` assigns by position
  (AC-18).
- **`Features/TreatmentPlans/TreatmentPlanWorkflowProjectionTests.cs`** — the full derivation table: no
  appointment → À planifier; future live appointment → Planifié with its date (AC-1); **Cancelled → À planifier**;
  NoShow → À planifier (AC-2); two appointments → earliest future wins, all-past → latest past (AC-3);
  **past live appointment + act not Done → À enregistrer** (AC-3a); Done act keeps its past visit date; linked
  non-cancelled invoice → Facturé, cancelled invoice → not (AC-5, AC-8).
- **`Features/TreatmentPlans/TreatmentPlanTenantIsolationTests.cs`** — copy the structure of
  `Features/Invoices/InvoiceTenantIsolationTests.cs` (`private void Authenticated()`, a `ForeignPlan()` builder,
  one `[Fact]` per verb asserting `IsFailure` **and** `Times.Never`, plus `List_Is_Scoped_To_Caller_Clinic`).
  Covers get / update / accept / complete / cancel / delete / mark-done / record-payment / amend / revise-
  installments / reorder (AC-24). This guard exists for every other money aggregate and the plan area is
  currently missing it entirely.
- **`Features/TreatmentPlans/AmendTreatmentPlanCommandHandlerTests.cs`** — happy path preserves number, paid
  installments and réalisé acts and bumps the revision (AC-20); every rejection path commits nothing (AC-22,
  AC-22a), including the billed-plan block and its release once the invoice is cancelled.
- **`Features/TreatmentPlans/GetTreatmentPlansQueryHandlerTests.cs`** — asserts the batched-read contract:
  `GetByTreatmentPlanItemIdsAsync` and `GetTreatmentPlanLinksAsync` are each called exactly once for a
  multi-plan, multi-patient list, and `GetByIdAsync` is **never** called per patient (AC-6).
- **`Features/Billing/MoneyReadConsistencyTests.cs`** (new folder) — one fixture, one patient, one plan bridged
  to an issued invoice: « Solde patient », receivables and dashboard outstanding all report the same figure
  (AC-12a); a Draft plan with an échéancier contributes 0 everywhere (AC-12b); the shared de-dup helper is the
  only implementation (AC-12c).
- **`Api/TreatmentPlansControllerAuthorizationTests.cs`** — the `MedicationsControllerAuthorizationTests` shape:
  a `string[]` of `AdminOrDoctor` actions (`CancelPlan`, `AmendPlan`, `ReviseInstallments`) pinned via
  `[Theory]`/`[MemberData]` + reflection on `AuthorizeAttribute.Policy`, an assertion that reorder carries no
  method-level policy, class-level `[Authorize]`, and `No_Endpoint_Is_Anonymous` (AC-24a). This also closes a
  pre-existing gap: nothing today pins `CancelPlan` to `AdminOrDoctor`.
- **`Features/Appointments/`** — extend the existing tests for the update-path plan link: move, clear, and
  cross-tenant rejection (AC-17). The four classes that pass a filler `Mock<ITreatmentPlanRepository>` need no
  behavioural change.
- **`Common/Behaviors/RealtimeResourceResolverTests.cs`** — add the `treatmentplans` `InlineData` (AC-25).
- **Frontend:** `web/` has no test runner and no working ESLint, so the gate is `npx tsc --noEmit` +
  `npm run build`, both clean (AC-26). FE acceptance criteria (AC-7, AC-9, AC-9a, AC-13, AC-13a, AC-14 – AC-16,
  AC-17a, AC-18) are covered by implementation plus manual verification.

**Running the suite here:** `dotnet test ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj --no-build`.
Smart App Control is ON on this machine and intermittently quarantines freshly built assemblies
(`0x800711C7`); a running API also locks the shared `bin`. Recorded workaround: build to scratch with
`-p:OutDir=<scratch>/utbuild/` then
`dotnet vstest <scratch>/utbuild/ClinicManagement.UnitTests.dll --TestCaseFilter:"FullyQualifiedName~TreatmentPlan"`.
`dotnet ef` has also failed here before (WDAC + held DLLs) — if it does, hand-author the migration plus its
`.Designer.cs` and the snapshot, then verify with `dotnet ef migrations has-pending-model-changes` once the app
is down, exactly as `features/clinical-loop-integration/progress.md:22, 59` records.

## Documentation to update on completion

- `api/ClinicManagement.API/CLAUDE.md` — correct the stale "seedable from the odontogram" claim (there is no
  backend seed command) and add the new plan routes with their policies.
- `api/ClinicManagement.Domain/CLAUDE.md` — `TreatmentPlanItem.SequenceNumber`, `TreatmentPlan.RevisionNumber`,
  the new `TreatmentPlan`/`Installment` methods, and the new repository methods.
- `api/ClinicManagement.Application/CLAUDE.md` — `TreatmentPlanWorkflowProjection` and the shared money de-dup
  helper.
- `web/CLAUDE.md` — the new `/treatment-plans/[id]` route, and correct the stale claim that
  `app/patients/loading.tsx` is a skeleton (it returns `null`).
- `web/components/CLAUDE.md` — `patient-plan-card.tsx`, `plan-workspace.tsx`, `plan-act-row.tsx`,
  `plan-timeline.tsx`, `plan-next-action.ts`, and the retirement of the plans-table "Gérer" dialog.
- `web/lib/CLAUDE.md` — add the missing `complete` member to the documented `treatmentPlansApi` surface.
- Root `CLAUDE.md` — the treatment-plan bullet, to say the loop is now bidirectional and the money reads agree.
