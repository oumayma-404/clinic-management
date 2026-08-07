# Feature Specification: Audit Sections 3–10

**Status:** APPROVED
**Approved:** 2026-07-27
**Challenged:** Yes — 8-lens completeness pass across 4 reviewers (user flow, error & recovery, edge case, state &
data, scope boundary, consistency, integration, accessibility). 32 findings; all applied. It has **not** been through
`/challenge-spec`.
**Type:** Full
**Created:** 2026-07-27
**Scope:** Full-stack + packaging
**Source:** `CODEBASE_AUDIT_2026-07.md` §§ 3–10 — every finding re-verified against source on 2026-07-27
**Branch:** `feature/audit-sections-3-to-10` (off `feature/windows-desktop-app` @ `1932acf`, which contains **both**
the merged § 2 security work (PR #15) **and** the merged § 1 data-and-money work)
**Exploration:** [exploration.md](./exploration.md)
**Feature:** Close every remaining finding in the July 2026 audit — the operations that report success while doing
nothing, the finished features no UI can reach, the app being unusable on a phone, the English leaking through a
French product, the unindexed queries and the broken lint — together with the adjacent defects that would otherwise
re-break them, and the two genuine product builds (audit trail and CNAM reconciliation) the audit undersells as
🟡 bullets.

---

## Overview

Audit §§ 1 and 2 are done. § 1 stopped the app destroying records and billing money twice; § 2 stopped it exposing
patient records and the signing key to every local Windows account. What is left is everything between "the data is
correct" and "the product is finished."

Three of those gaps are not cosmetic:

1. **Four operations return HTTP 200 and show a success toast while nothing happens.** Marking a visit « Terminé »
   from Planifié does nothing. Cancelling a completed appointment does nothing. « Rappel envoyé à … » toasts even
   when no channel is configured — and snoozes the patient for 30 days having sent nothing. Two staff booking the
   same slot both succeed. These erode trust faster than a visible error, because the user has no way to know.

2. **Twelve finished server-side capabilities have no caller.** A devis can be amended — the workspace even renders
   a « révision » badge — but nothing can trigger it, so an accepted devis can only be cancelled and retyped, losing
   its number. An act marked réalisé by mistake permanently closes the plan, with no un-mark anywhere in Domain,
   Application, API or UI. A clinic that connects the wrong Google account is stuck. A staff member who joined with
   the wrong role is stuck. In Cloud mode the user-management page works perfectly and the nav link is hidden.

3. **The app is unusable on a phone.** `dashboard-sidebar.tsx` contains zero responsive classes, always renders, and
   defaults to `w-64` — 256 px of a 375 px viewport, with no drawer and no auto-collapse.

Then the long tail: raw English enum values (`"Noshow"`, `"Inprogress"`) shown next to a fully French status picker;
an appointment note persisted with the literal prefix `Type: `; five orphaned realtime keys so the salle d'attente —
the canonical two-user screen — never live-refreshes; a reminder outbox that sequential-scans a table that grows
forever; and `npm run lint` that has never run on a clean install, in a repo with no CI to notice.

Finally, two items the audit files under 🟡 P2 that are each a feature: **no audit trail of who changed what**, and
**a CNAM flow that stops at an estimate**. Both are in scope at full breadth by explicit decision (**R-2**).

### Scale note

This is by a wide margin the largest spec in the repo: **57 audit bullets** and **26 adjacent defects**, expanded to
**304 acceptance criteria** across eight subsystems, with 12 schema changes and 3 data migrations. For comparison,
`features/security-hardening` closed 14 findings and `features/data-and-money-integrity` closed 8.

It was kept as one feature and one story at the user's explicit request (**R-1**), and § 6.4 and § 6.5 were kept at
full breadth by explicit request (**R-2**, **R-3**) despite each being feature-sized on its own.

> **The traceability table is generated from the acceptance criteria, not maintained alongside them.** An earlier
> draft hand-wrote it and roughly half the rows drifted — six phantom AC numbers, four cross-wired rows, and two
> ranges that stopped before the ACs they claimed to cover. It is now derived mechanically and validated: every AC
> the table cites exists, every audit bullet maps to at least one AC, and no AC number is duplicated or orphaned.
> **Regenerate it after any edit that adds, removes or reorders an AC — do not hand-patch a row.**

To keep it implementable it is structured as **eight ordered parts**. Each part is a *vertical* increment — domain →
persistence → API → UI → tests — never a technical-layer grouping. Each ends at a clean build gate and is a natural
commit boundary, so `/implement-story` can land the story incrementally and resume at a part boundary.

**The parts are grouped by subsystem, not by the audit's sections.** The audit is organised by *symptom*, so its
sections interleave the same files — §§ 3, 5, 6, 7 and 8 all touch the two appointment dialogs. Grouping by symptom
would mean editing `edit-appointment-dialog.tsx` in four separate parts.

---

## Corrections to the audit

Twelve corrections, each verified. They change the count, the scope, or the fix itself.

Independently re-counted during the completeness pass: **§3=4 · §4=2 · §5=12 · §6=12 · §7=9 · §8=6 · §9=7 · §10=5
= 57.**

| # | Correction |
|---|---|
| **C-1** | **§ 6 has 12 bullets, not the index's 9.** The true §§ 3–10 total is **57**, not 54. The index row should read `— / 2 / 10 / — / 12`. This is the same undercount class the § 2 spec caught (index said 12, the section listed 14). |
| **C-1b** | **The audit's grand total is also wrong.** With § 2 = 14 and § 6 = 12, the real total is **77**, not the index's 74. Correcting only the § 6 row would leave the summary still wrong. |
| **C-11** | **§ 10.4 is headed "9 more" but enumerates 12** — 6 × CS8602, 2 × CS8600, 1 × CS8604, 1 × CS0618, 2 × CS8981. The nine are the *nullable* subset; the obsolete and naming warnings are the tail. AC-P5.10 covers all twelve. |
| **C-2** | **§ 4.1's stated symptom is not reachable through the UI.** Both callers already send browser-local bounds (`caisse/page.tsx:73-78`, `use-dashboard-stats.ts:24-31`); only the `??` server defaults are UTC. The real un-overridable day-shift is `payment-modal.tsx:36` (**A-20**). |
| **C-3** | **§ 6.2 is closed by the § 1 merge.** `GetDashboardStatsQuery.cs:115-118` now nets refunds via the same `GetRefundedBetweenAsync` the caisse uses. It becomes a regression AC, not a fix. |
| **C-4** | **§ 7.5 lists five paths, not six.** The likely intended sixth is `edit-appointment-dialog.tsx:130-132`. Roughly twelve further same-class swallows exist beyond the audit's list. |
| **C-5** | **§ 10.2's ICU claim is refuted.** The DLLs are `icu*67.dll` but `postgres.exe` reports FileVersion **16.9** — EDB genuinely bundles ICU 67.1 with PG 16. The probe-list defect stands; the directories are **gitignored**, so they are local artifacts, not vendored content. |
| **C-6** | **§ 10.3's 46 reconciles with § 2's 58 baseline.** 46 CS8618 + 6 CS8602 + 2 CS8600 + 1 CS8604 + 1 CS0618 + 2 CS8981 = 58. There is no discrepancy to investigate. |
| **C-7** | **§ 6.4 and § 6.5 are each feature-sized**, not 🟡 bullets. Sized honestly in P7 and P8. |
| **C-8** | **§ 5.9 is a one-token fix.** `/users/page.tsx` never checked `mode`; only the nav link does. |
| **C-9** | **§ 8.6 is one component.** `edit-patient-dialog.tsx` *is* the create form (mounted with `patient={null}`). |
| **C-10** | **§ 8.2 and § 8.6 both undercount** — seven more English strings and one more American placeholder. |

---

## Implementation order — load-bearing rules

Two orderings are **non-negotiable**. Everything else may be reordered.

> **Rule 1 — P1 before P2.** § 5.6 (a delete button for a fiche de soins) makes § 6.11 (orphaned soft links)
> *reachable*: the plan act stays `Done` pointing at a deleted row, and because there is no un-mark (§ 5.3) it can
> never be corrected. **§ 5.3, § 5.6 and § 6.11 must land in the same part, in that order** — the un-mark before the
> delete button before the cleanup. They are all in P2.

> **Rule 2 — P7a before the rest of P7 and all of P8.** The audit trail is the prerequisite everything else records
> into: AC-P7.26 audits a deliberately-created duplicate, AC-P7.35 audits an anonymization, and AC-P8.27 audits every
> claim, bordereau and reimbursement. Build the trail first.

**Superseded:** an earlier ordering rule gated §§ 4.1, 4.2, 6.2 and 6.8 behind the § 1 merge. § 1 merged on
2026-07-27 (HEAD `1932acf`); the gate is **removed** and those findings are implemented in normal order in P6.
See [exploration.md § 7](./exploration.md).

### Part map

| Part | Covers | Bullets | Verifiable by |
|---|---|:--:|---|
| **P1** Appointment lifecycle & booking | 3.1, 3.2, 3.4, 5.4, 6.1, 6.9, 8.1, 8.4 + A-1…A-7, A-10 | 8 | `dotnet vstest` + page walk |
| **P2** Finish what's built | 5.1, 5.2, 5.3, 5.5, 5.6, 5.7, 5.8, 5.9, 5.11, 5.12, 6.10, 6.11, 8.3 + A-8, A-9, A-11…A-14 | 13 | `dotnet vstest` + page walk |
| **P3** UX, accessibility & French | 3.3, 6.3, 7.1–7.9, 8.2, 8.6 | 13 | `tsc` + `npm run build` + manual |
| **P4** Stock, realtime & schema | 6.6, 6.7, 6.12, 9.1–9.6 + A-15…A-19 | 9 | `dotnet vstest` + migration |
| **P5** Build & tooling | 10.1–10.5 + A-24…A-26 | 5 | `npm run lint` + build |
| **P6** Money truth & timezone | 4.1, 4.2, 5.10, 6.2, 6.8, 8.5, 9.7 + A-20…A-23 | 7 | `dotnet vstest` |
| **P7** Audit trail, duplicate prevention & anonymize | 6.4 | 1 | `dotnet vstest` + migration |
| **P8** CNAM claims & reconciliation | 6.5 | 1 | `dotnet vstest` + migration |
| | **Total** | **57** | |

P1–P6 are mutually independent apart from Rule 1. P7 and P8 are ordered last by Rule 2 and by size.

---

## Traceability — audit bullet → part → acceptance criteria

| # | Sev | Audit finding | Part | ACs |
|---|:--:|---|:--:|---|
| 3.1 | 🟠 | « Terminé » does nothing from Planifié or Confirmé | P1 | AC-P1.1–4 |
| 3.2 | 🟠 | « Cancel » is enabled on a Completed appointment but does nothing | P1 | AC-P1.5–7, AC-P1.10–13 |
| 3.3 | 🟠 | « Rappel envoyé à … » toasts even when no channel is configured | P3 | AC-P3.1–6 |
| 3.4 | 🟠 | Double-booking is check-then-insert with no lock or constraint | P1 | AC-P1.14–16, AC-P1.18–20, AC-P1.22 |
| 4.1 | 🟠 | Caisse and dashboard run « aujourd'hui » from 01:00 to 01:00 | P6 | AC-P6.2–3 |
| 4.2 | 🔴 | Invoice and devis numbering take the year from `UtcNow.Year` | P6 | AC-P6.7–10 |
| 5.1 | 🟠 | Post-acceptance amendment of a devis is unreachable | P2 | AC-P2.1–4 |
| 5.2 | 🟡 | Installment schedules frozen at acceptance | P2 | AC-P2.5–7 |
| 5.3 | 🟡 | No un-do for a plan act marked réalisé | P2 | AC-P2.8–11 |
| 5.4 | 🟡 | Per-dentist working hours have no UI | P1 | AC-P1.25–26 |
| 5.5 | 🟡 | Google Calendar can be connected but never disconnected | P2 | AC-P2.33–35 |
| 5.6 | 🟡 | No UI to delete or void a fiche de soins | P2 | AC-P2.16–18 |
| 5.7 | 🟡 | No UI to delete a medical document | P2 | AC-P2.20 |
| 5.8 | 🟡 | No user role can ever be changed | P2 | AC-P2.23, AC-P2.26–27 |
| 5.9 | 🟡 | User management invisible in Cloud mode | P2 | AC-P2.28–29 |
| 5.10 | 🟢 | CNAM reimbursement math exists twice and will drift | P6 | AC-P6.15–17 |
| 5.11 | 🟢 | `PUT /api/doctors/{id}` has no caller | P2 | AC-P2.30–32 |
| 5.12 | 🟢 | `GET /api/procedure-types/colors` has no caller | P2 | AC-P2.36 |
| 6.1 | 🟠 | Working hours are advisory only | P1 | AC-P1.28–35 |
| 6.2 | 🟠 | Dashboard and caisse disagree after any refund | P6 | AC-P6.11 |
| 6.3 | 🟡 | Failed reminders are effectively invisible | P3 | AC-P3.7–11 |
| 6.4 | 🟡 | No audit trail of who changed what | P7 | AC-P7.1–36 |
| 6.5 | 🟡 | The CNAM flow stops at an estimate | P8 | AC-P8.1–30 |
| 6.6 | 🟡 | Stock expiry tracking is unreachable end-to-end | P4 | AC-P4.1–8 |
| 6.7 | 🟡 | Stock is never consumed by performing an act | P4 | AC-P4.9–14 |
| 6.8 | 🟡 | The invoice↔appointment link is never populated | P6 | AC-P6.12–14 |
| 6.9 | 🟡 | Recurring series: conflicts count-only, no edit path | P1 | AC-P1.36–39 |
| 6.10 | 🟡 | `LabWorkOrder.SetStatus` has no transition rules | P2 | AC-P2.38–41 |
| 6.11 | 🟡 | Deleting a dental record orphans its soft links | P2 | AC-P2.13–15 |
| 6.12 | 🟡 | `UpdateStockItemCommand` writes stock with no `StockMovement` | P4 | AC-P4.15, AC-P4.18–19 |
| 7.1 | 🟠 | The app is unusable on a phone | P3 | AC-P3.12–18 |
| 7.2 | 🟠 | A session hiccup in Local mode dumps the user on a Next 404 | P3 | AC-P3.19–21 |
| 7.3 | 🟡 | The patients list has no edit action | P3 | AC-P3.22–23 |
| 7.4 | 🟡 | The AI assistant speaks every reply aloud | P3 | AC-P3.24–26 |
| 7.5 | 🟡 | Error paths swallowed to `console.error` | P3 | AC-P3.27–33 |
| 7.6 | 🟡 | « Créer le dossier » has no in-flight disabled state | P3 | AC-P3.34 |
| 7.7 | 🟡 | `/patients` shows a blank screen instead of a skeleton | P3 | AC-P3.35–36 |
| 7.8 | 🟡 | Accessibility — gallery unreachable by keyboard, +5 more | P1, P3 | AC-P1.53–54, AC-P3.38–44 |
| 7.9 | 🟢 | Feedback pattern is inconsistent | P3 | AC-P3.37 |
| 8.1 | 🟠 | Raw English enum values shown as appointment status | P1 | AC-P1.41–45 |
| 8.2 | 🟡 | English buttons and headings in production UI | P3 | AC-P3.49, AC-P3.52–53 |
| 8.3 | 🟡 | The doctor specialty catalog is English-only | P2 | AC-P2.42–45 |
| 8.4 | 🟡 | Appointment notes persisted with the English prefix `Type: ` | P1 | AC-P1.47–52 |
| 8.5 | 🟡 | Currency formatting bypasses `formatDT` | P6 | AC-P6.18–20 |
| 8.6 | 🟢 | Patient form placeholders are American | P3 | AC-P3.54 |
| 9.1 | 🟠 | Five backend broadcast keys have no frontend subscriber | P4 | AC-P4.20–21, AC-P4.26 |
| 9.2 | 🟡 | `Doctor` and `StockItem` have no global query filter | P4 | AC-P4.27–29 |
| 9.3 | 🟡 | The reminder-outbox hot query is unindexed and unbounded | P4 | AC-P4.30–34 |
| 9.4 | 🟡 | `StockMovement.ClinicId` filtered but unindexed, no FK | P4 | AC-P4.35 |
| 9.5 | 🟡 | `StockItem.UnitPrice` is `decimal(18,2)` | P4 | AC-P4.36 |
| 9.6 | 🟡 | The recall list loads every patient and every appointment | P4 | AC-P4.41–43 |
| 9.7 | 🟡 | `GetReceivablesQuery` N+1 + heavy already-billed guard | P6 | AC-P6.21–23 |
| 10.1 | 🟡 | `npm run lint` is broken and CI never notices | P5 | AC-P5.1–3 |
| 10.2 | 🟡 | Packaging bootstrap pulls unpinned, unverified downloads | P5 | AC-P5.5–7 |
| 10.3 | 🟢 | 46 × CS8618 in `ClinicManagement.Domain` | P5 | AC-P5.9 |
| 10.4 | 🟢 | 12 more nullable/obsolete/naming warnings | P5 | AC-P5.10 |
| 10.5 | 🟢 | Startup seeding has no retry | P5 | AC-P5.13, AC-P5.16 |

### Adjacent defects — not in the audit, in scope here

Each would re-break, mask or block a listed finding. Per the repo's standing rule, they are fixed in the same spec.

| # | Sev | Defect | Part | Blocks |
|---|:--:|---|:--:|---|
| **A-1** | 🟠 | `Appointment.Confirm()` has no `Completed` guard — a finished visit can be pushed back to `Confirmé` | P1 | 3.2 |
| **A-2** | 🟠 | `Appointment.Reschedule()` force-sets `Status = Scheduled`, silently downgrading `Confirmé`/`En cours` | P1 | 3.1 |
| **A-3** | 🟡 | Overlap window is `[start − 1 day, start + duration]` — a long appointment starting >24 h earlier is never loaded | P1 | 3.4 |
| **A-4** | 🟡 | The overlap guard is skipped entirely when `DoctorId` is null | P1 | 3.4 |
| **A-5** | 🟠 | Working-hours JSON has **zero** validation — `[{"day":"Blorp","from":"99:99"}]` persists | P1 | 6.1, 5.4 |
| **A-6** | 🟡 | `edit-patient-dialog.tsx:474` can persist the literal `"Unknown"` gender | P1 | 8.1 |
| **A-7** | 🟡 | `CreateRecurringSeriesCommand.cs:230` leaks raw `ex.Message` (§ 2's sweep missed it) | P1 | 6.9 |
| **A-8** | 🟡 | `DeleteMedicalDocumentCommand.cs:98` — English **and** a raw exception leak | P2 | 5.7 |
| **A-9** | 🟡 | `DeleteDentalRecordCommand.cs:44` — `"Unable to resolve current clinic"`, English leak | P2 | 5.6 |
| **A-10** | 🟡 | `SetDoctorWorkingHoursCommand.cs:78` leaks raw `ex.Message` | P1 | 5.4 |
| **A-11** | 🟠 | `User.Update(role, email = null, fullName = null)` silently nulls email and full name; never validates the role | P2 | 5.8 |
| **A-12** | 🟠 | Delete-fiche and delete-document carry **no role policy** — any secretary can delete | P2 | 5.6, 5.7 |
| **A-13** | 🟡 | `POST /treatment-plans/{id}/items/{itemId}/done` has no role policy | P2 | 5.3 |
| **A-14** | 🟢 | `/procedure-types/colors` returns bare hexes with no names | P2 | 5.12 |
| **A-15** | 🟡 | `documents` is a 15th dead realtime key — declared, zero subscribers | P4 | 9.1 |
| **A-16** | 🟢 | `StockMovement.Reason` is never populated by either write site | P4 | 6.12 |
| **A-17** | 🟡 | `StockMovementType` has only `Consume`/`Restock` | P4 | 6.12 |
| **A-18** | 🟡 | No central decimal convention — all 27 money columns declare precision inline | P4 | 9.5 |
| **A-19** | 🟠 | `RealtimeResourceResolverTests` is a *contains* theory, not an exact set | P4 | 9.1 |
| **A-20** | 🟠 | `payment-modal.tsx:36` pre-fills the payment date from the **UTC** date — yesterday, 00:00–01:00 Tunis | P6 | 4.1 |
| **A-21** | 🟡 | `ResolveTunisiaTimeZone()` duplicated byte-for-byte in two files | P6 | 4.1, 4.2 |
| **A-22** | 🟡 | 5 × `DateTime.Today` in `AIActionService` — server-machine-local, a third convention | P6 | 4.1 |
| **A-23** | 🟡 | Same UTC-day defect, un-overridable, in four more reads | P6 | 4.1 |
| **A-24** | 🟠 | **`BackfillAsync` is never called in Local mode** — its only call site is inside `if (!isLocalAuthMode)` | P5 | 10.5 |
| **A-25** | 🟠 | The Local seeder is fire-and-forget `Task.Run`; its catch calls `StopApplication()` — a DB blip kills the service | P5 | 10.5 |
| **A-26** | 🟡 | No `.editorconfig`, no `Directory.Build.props`, no `TreatWarningsAsErrors` — 58 warnings can never fail a build | P5 | 10.3, 10.4 |

---

# Part P1 — Appointment lifecycle & booking integrity

> Everything that converges on the `Appointment` aggregate, the two appointment dialogs and the calendar. Eight
> audit bullets and nine adjacent defects live in the same six files, which is why they are one part.

## US-P1: Appointment status, booking and hours

**As a** dentist or secretary
**I want** every appointment action I take to either happen or tell me why it did not
**so that** I can trust the schedule without re-checking it in the database.

### The status machine is the root cause

`Domain/Entities/Appointment.cs` has nine transition methods with inconsistent guards. `Completed` is reachable from
`InProgress` only, and once reached has **no legal exit** — `Cancel`, `MarkAsNoShow` and `Reschedule` all throw, and
only `Confirm()` gets through, which is itself a bug (**A-1**). Meanwhile `MarkVisitCompleted()` is a *silent no-op*
when the status is wrong, and it is called from two live paths (`CreateDentalRecordCommand.cs:172`,
`CreateMedicalDocumentCommand.cs:290`), so saving a fiche de soins moves an appointment into that terminal state.

The command layer then adds a second, different machine: a `switch` in `UpdateAppointmentCommand` that falls through
without returning a failure. Fixing the two no-ops means making these one machine, not patching two cases.

**Acceptance Criteria:**

- **AC-P1.1:** Setting status to « Terminé » from `Scheduled`, `Confirmed` or `InProgress` succeeds and persists
  `Completed`. The `switch` in `UpdateAppointmentCommand` no longer falls through silently for any status value.
- **AC-P1.2:** A transition the domain refuses returns `Result.Failure` with a French message naming the current and
  target status — never HTTP 200. This is the whole point: **an impossible transition must be visible.**
- **AC-P1.3:** The legal transition set is declared in **one place** in the domain and is the only authority. The
  command layer asks the domain whether a transition is legal; it does not re-encode the rules in a `switch`.
- **AC-P1.4:** Completing from `Scheduled`/`Confirmed` fires the existing post-visit-review repoint
  (`UpdateAppointmentCommand.cs:394-403`), which is currently unreachable from those states.
- **AC-P1.5:** « Annuler le rendez-vous » on a `Completed` appointment succeeds — a visit auto-completed by saving a
  fiche is no longer a dead end.
- **AC-P1.6:** The cancel button's `disabled` condition is derived from the same legal-transition set as AC-P1.3, not
  from a hardcoded `status === "cancelled"`.
- **AC-P1.7:** Cancelling a `Completed` appointment cancels its post-visit review and voids unsent reminders, exactly
  as cancelling a scheduled one does.
- **AC-P1.8 (A-1):** `Appointment.Confirm()` refuses when the status is `Completed` or `Cancelled`. A finished visit
  cannot be pushed back to « Confirmé ».
- **AC-P1.9 (A-2):** `Appointment.Reschedule()` **preserves** `Confirmed` and `InProgress` instead of force-setting
  `Scheduled`. `NoShow` is explicitly **not** preserved — rescheduling a no-show returns it to `Scheduled`, and that
  is stated rather than left to the reader.

### What the new exits from `Completed` do to everything downstream

`Completed` is load-bearing in two other subsystems. `TreatmentPlanWorkflowProjection` lists it in `LiveStatuses`
and derives each plan act's état from it; `GetPatientsToRecallQuery` derives `lastVisit` from it. AC-P1.5 creates a
`Completed → Cancelled` transition that has never existed, so both must be given an answer.

- **AC-P1.10:** Cancelling a `Completed` appointment has a **stated** effect on the linked `TreatmentPlanItem`'s
  derived état, and the plan workspace shows the result without a reload. Silently reverting an act to
  « À planifier » is the kind of invisible state change this spec exists to eliminate.
- **AC-P1.11:** Cancelling a `Completed` appointment has a **stated** effect on the patient's `lastVisit` and
  therefore on whether they reappear on the relance list.
- **AC-P1.12:** `MarkVisitCompleted()`'s two callers — the fiche and the medical-document handlers — are post-commit,
  best-effort helpers whose catch only logs, and the fiche has **already committed** before they run. Under the new
  machine they distinguish *already `Completed`* (idempotent: still cancel the post-visit review, still broadcast)
  from *`Cancelled`/`NoShow`* (surfaced, not swallowed). Making the domain method throw there without this would
  jump over `CancelPostVisitReviewAsync` and leave the review notification prompting forever — trading a harmless
  no-op for a stuck loop.
- **AC-P1.13:** `PostVisitReviewCompletionTests` currently pins the silent-no-op contract and is rewritten to pin the
  new one. A test that still passes after AC-P1.12 is a test that was pinning the defect.

### Double-booking

- **AC-P1.14:** Two concurrent `POST /api/appointments` for the same practitioner and overlapping window result in
  exactly **one** appointment. The loser receives a French failure naming the clash, not a second row.
- **AC-P1.15:** The guarantee is enforced by the **database**, not by a re-read — a check-then-insert cannot be made
  safe by widening the check. A PostgreSQL exclusion constraint over `(DoctorId, [start, end))` is the expected
  mechanism; `Duration` is stored as `bigint` ticks, so the range needs a generated end column, and the migration
  creates the `btree_gist` extension.
  *Corrected 2026-07-28:* an earlier draft warned this needs superuser. It does not — the bundled PG 16 ships
  `btree_gist` with `trusted = true`, so the Local install's `clinic_user` can create it as database owner, and
  Cloud runs Postgres in-stack as the bootstrap superuser. There is no managed-Postgres deployment in this repo.
  The real residual is that the GiST index build takes `ACCESS EXCLUSIVE`, and in Local that runs **while Kestrel is
  already serving** (migrations are deferred and fire-and-forget there).
- **AC-P1.16:** The constraint is **partial** — `WHERE Status NOT IN (Cancelled, NoShow)`. Without the predicate a
  cancelled slot becomes permanently unbookable, and rebooking a cancelled slot is the single most common scheduling
  action in a clinic. It also matches the application guard, which has always excluded both.
- **AC-P1.17 (A-4):** `DoctorId IS NULL` is handled deliberately, not by accident. PostgreSQL's `=` never matches
  NULL, so an unassigned appointment is silently exempt unless the constraint coalesces. Either it participates or
  the exemption is a **stated, tested** rule with a reason.
- **AC-P1.18:** A constraint violation (`23P01`) is translated into AC-P1.14's French failure. § 1 translates only
  `DbUpdateConcurrencyException`, so today the loser would receive a 500.
- **AC-P1.19:** `POST /api/appointments/recurring` keeps **skip-and-report** semantics under the constraint.
  `CreateRecurringSeriesCommand` adds every occurrence and calls one `SaveChangesAsync`, so a single violation would
  abort the whole series instead of « N créés, 1 ignoré ». A per-occurrence savepoint or a pre-check-and-retry is
  required — the behaviour AC-P1.36 depends on must survive the constraint.
- **AC-P1.20:** The same guarantee holds for `PUT /api/appointments/{id}` when the schedule changes, and for the
  non-handler writers named in AC-P1.28.
- **AC-P1.21 (A-3):** The overlap scan loads any appointment whose window overlaps the candidate, regardless of how
  long before it started. The current `[start − 1 day, …]` window misses a long appointment beginning >24 h earlier.
- **AC-P1.22 (R-4):** Before the constraint is added, a pre-flight reports every pre-existing overlapping pair by id.
  It counts only pairs the **partial** constraint would reject — a cancelled-then-rebooked slot is legitimate history
  and must not abort the migration. The migration fails loud with the offending ids; it never deletes a row.

### Working hours — enforcement needs an editor and validation first

`Clinic.WorkingHoursJson` and `Doctor.WorkingHoursJson` are `text` columns with **no validation whatsoever**
(`WorkingHoursSerializer.Parse` returns `null` on a `JsonException` and checks nothing else). Enforcing booking
against them without first validating them means enforcing garbage, and the per-dentist editor (§ 5.4) is the only
way a clinic can set the hours the enforcement will use. All three land together.

- **AC-P1.23 (A-5):** `WorkingHoursSerializer` rejects an unknown weekday, an unparseable `HH:mm`, `From >= To`, and
  duplicate days. Invalid input is a `Result.Failure` with a French message, never a silently-persisted string.
- **AC-P1.24 (A-5):** Existing rows that fail the new validation are **not** made unreadable and **not** silently
  presented as "no hours configured" — because per AC-P1.30 that means *unrestricted booking*, so a clinic would
  lose the enforcement it believes it has and never be told. An unparseable stored value surfaces in the hours
  editor as « Horaires existants illisibles — veuillez les ressaisir » and is listed in a one-off startup report.
- **AC-P1.25 (5.4):** An admin can view and edit **each practitioner's** working hours from the clinic settings
  doctors roster, using the existing `GET`/`PUT /api/doctors/{id}/working-hours` endpoints. A doctor can edit their
  own from « Mon profil ».
- **AC-P1.26 (5.4):** An empty list clears the override and the UI says so explicitly — « Aucun horaire spécifique :
  les horaires du cabinet s'appliquent » — because an empty `PUT` silently means "clear" today
  (`SetDoctorWorkingHoursCommand.cs:65-67`) and nothing tells the user that.
- **AC-P1.27 (A-10):** `SetDoctorWorkingHoursCommand`'s catch no longer interpolates `ex.Message` into the response.
- **AC-P1.28 (6.1):** Booking outside the resolved working hours is **refused** with a French message naming the
  practitioner and the closed period — for create, update and every recurring occurrence.
- **AC-P1.29 (6.1):** **Every non-HTTP appointment writer is given an explicit, stated rule.**
  `GoogleCalendarSyncService` creates and reschedules appointments **directly through the repository**, bypassing
  both handlers; so do the waiting-list promote path and `AIActionService`. A guard placed only in the handlers
  leaves inbound Google as an unguarded backdoor, so AC-P1.28 would simply not be true. For each writer the spec
  states whether the refusal applies and what happens when it fires — skip-and-log, import-with-override-flag, or
  surface. Inbound Google **must not silently drop** the Sunday appointment the dentist typed into Google.
- **AC-P1.30 (6.1):** Resolution order is doctor override → clinic hours → "no hours configured". When no hours are
  configured anywhere, booking is unrestricted and **behaves exactly as today** — a clinic that never opens the
  settings screen sees no change. This is the majority case on day one.
- **AC-P1.31 (6.1):** An out-of-hours booking is possible **for any role that can book**, not only for admins — a
  secretary handling an emergency Sunday call must have a path, or the guard will be worked around by falsifying the
  time. The override is an explicit confirmation and is **recorded on the appointment**, never silently allowed.
- **AC-P1.32 (6.1):** A refusal returns the user to the dialog with every entry preserved and the override offered
  in place. A refusal that clears the form is a worse defect than the one being fixed.
- **AC-P1.33 (6.1):** The calendar grid renders only the resolved open hours instead of a flat `0..23`, and the
  hour pickers in both dialogs offer the same range. Out-of-hours existing appointments still render.
- **AC-P1.34 (6.1):** Changing clinic or doctor hours does **not** retroactively invalidate or hide existing
  appointments that now fall outside them.
- **AC-P1.35 (6.1):** The hours editor, the booking guard and the calendar read the **same** resolution helper. Two
  implementations that can disagree about whether the clinic is open is the defect, not the fix.

### Recurring series

- **AC-P1.36 (6.9):** `POST /api/appointments/recurring` returns the conflicting dates and the UI **lists them**,
  formatted `dd/MM/yyyy`, instead of rendering `conflicts.length`.
- **AC-P1.37 (6.9):** Each skipped occurrence offers a « Replanifier » action that opens the create dialog
  pre-filled with that date, so a skipped occurrence is recoverable without retyping the series.
- **AC-P1.38 (6.9):** The series conflict scan excludes `Cancelled` **and** `NoShow`, matching the single-appointment
  path. A past no-show no longer blocks an otherwise free slot.
- **AC-P1.39 (6.9):** The two overlap predicates are one shared helper — this is the defect recurring, and the two
  drifted precisely because they were copies.
- **AC-P1.40 (A-7):** `CreateRecurringSeriesCommand`'s catch no longer interpolates `ex.Message`.

### French status labels

- **AC-P1.41 (8.1):** A single `appointment-labels.ts` exports `APPOINTMENT_STATUS_LABELS` covering **all six**
  states — Planifié, Confirmé, En cours, Terminé, Annulé, Absent — following the existing `invoice-labels.ts` /
  `treatment-plan-labels.ts` pattern. There is no i18n framework in this repo and one is not being introduced.
- **AC-P1.42 (8.1):** `edit-appointment-dialog.tsx`, `appointment-list.tsx` and the patient history table all render
  through that map. The string-mangling `statusDisplay` at `edit-appointment-dialog.tsx:355` is deleted.
- **AC-P1.43 (8.1):** The status `Select`, the badge and the calendar legend all read the same map — today they are
  three separate hardcoded lists and only three of six states appear in the colour map.
- **AC-P1.44 (8.1):** No raw enum value reaches the screen. An unmapped value renders a French fallback, never
  `"Noshow"`.
- **AC-P1.45 (8.1):** « Sexe » renders through a `GENDER_LABELS` map — Homme / Femme / Autre — everywhere it is
  displayed, matching what the edit form already offers.
- **AC-P1.46 (A-6):** The stored gender set is closed. `"Unknown"` is either mapped to a French label or stopped at
  the write, and existing `"Unknown"` rows render as « Non renseigné » rather than raw.

### The `Type: ` prefix is stored data

`create-appointment-dialog.tsx:426` writes `` `Type: ${appointmentType}\n${notes}` `` into `Appointment.Notes`, and
`edit-appointment-dialog.tsx:193-196` parses it back out and re-persists it on every save. Changing the writer alone
orphans the type on every existing row. Worse, the dialog writes **both** `procedureTypeId` and the `Type: ` prefix
from two independent state fields — `appointmentType` is free text seeded from a preset name, not the selected
catalog row — so many existing rows carry both, and they can disagree.

- **AC-P1.47 (8.4):** The destination is decided in this spec, not left to implementation: the appointment type is
  carried by **`ProcedureTypeId`**, the field that already exists for it. It is no longer smuggled through `Notes`.
- **AC-P1.48 (8.4):** The migration handles **three row classes explicitly**, and discards nothing:
  · prefix present, no `ProcedureTypeId` → match the text to a catalog row in that clinic and set it;
  · prefix present, a **different** `ProcedureTypeId` already set → the existing id wins, and the free text is kept
    in `Notes` rather than deleted;
  · prefix matching **no** catalog row in that clinic → left in `Notes`, counted and reported.
- **AC-P1.49 (8.4):** A row whose notes do not match the prefix is untouched — a note a user legitimately began with
  `Type: ` must survive.
- **AC-P1.50 (8.4):** The migration is idempotent and reports a per-class row count. Per **R-5** it is irreversible,
  so the report is the only evidence of what it did.
- **AC-P1.51 (8.4):** The reader in `edit-appointment-dialog.tsx` is removed in the **same** change as the writer.
  Leaving either behind re-creates the defect on the next save.
- **AC-P1.52 (8.4):** No user-visible string in either appointment dialog is English — this closes the § 8.2 subset
  that lives in these two files (« Date & Time », « Cancel Appointment », « Close », « Cancel », « Clear »,
  « Duration set to N minutes »).

### Accessibility in the appointment dialogs

- **AC-P1.53 (7.8):** Every `<Label>` in the date/time block of both dialogs has an `htmlFor` and its control has a
  matching `id` — date picker, heure de début and heure de fin, in `create-` and `edit-appointment-dialog`. The rest
  of both files already does this correctly; only this block was missed.
- **AC-P1.54 (7.8):** The working-hours editor (AC-P1.25) and the recurring conflict list (AC-P1.36) meet the same
  accessibility bar AC-P3.38–3.44 sets for the pre-existing surfaces, and are included in the manual walk.

---

# Part P2 — Finish what's built

> Thirteen bullets, all the same shape: a working, authorized, tested server-side capability with **no caller**.
> Each is close to free to expose. Three of them interlock and must land in order — see Rule 1.

## US-P2a: The treatment-plan loop can be corrected

**As a** dentist
**I want** to amend an accepted devis, re-spread its échéancier and un-mark an act I ticked by mistake
**so that** a typo does not force me to cancel the devis and lose its number.

`POST /treatment-plans/{id}/amend` and `PUT /treatment-plans/{id}/installments` are both fully implemented,
`AdminOrDoctor`-gated, and validated down to "une échéance déjà encaissée ne peut pas être supprimée". Neither has a
caller. Worse, `TreatmentPlanItem.MarkDone` tells the user to « détachez-le de cette fiche » and **no detach exists
anywhere** — while marking the last item done auto-completes the plan (`TreatmentPlan.cs:276-279`) and `EnsureAmendable`
then refuses every amendment. One wrong fiche permanently closes a devis.

- **AC-P2.1 (5.1):** A « Modifier le devis » action on the plan workspace opens the existing plan form in amend mode
  and posts `addItems` / `removeItemIds` / `installments` to the existing endpoint.
- **AC-P2.2 (5.1):** It appears under the same condition as « Facturer le devis » — plan active and not yet billed.
- **AC-P2.3 (5.1):** Every existing server-side refusal surfaces as a readable French message in the form, not a
  toast behind an open dialog. Following the repo's established precedent for financial reversals, the UI **calls
  unconditionally and surfaces the 403** rather than gating client-side.
- **AC-P2.4 (5.1):** The « révision N » badge the workspace already renders becomes reachable — amending increments
  `RevisionNumber` exactly once per call, and the badge reflects it without a reload.
- **AC-P2.5 (5.2):** A « Modifier l'échéancier » action on the Échéancier card posts to
  `PUT /treatment-plans/{id}/installments`.
- **AC-P2.6 (5.2):** An installment that is already partly or fully paid cannot be deleted or reduced below what was
  collected — the server already enforces this; the UI must show which rows are locked and why, **before** submit.
- **AC-P2.7 (5.2):** A pure re-spread bumps `RevisionNumber`, and the UI says so, because the server does it.
- **AC-P2.8 (5.3):** A `TreatmentPlanItem` marked réalisé can be un-marked. A new domain method returns the item to
  `Planned` and clears `LinkedDentalRecordId`.
- **AC-P2.9 (5.3):** Un-marking an item on a plan that auto-completed **reopens the plan** to `Accepted`. Without
  this the un-mark is cosmetic — `EnsureAmendable` still refuses.
- **AC-P2.10 (5.3):** Un-marking is refused with a French message when the act is already billed on a non-cancelled
  invoice. Un-marking billed work would desynchronise the plan from the money.
- **AC-P2.11 (5.3):** A « Détacher » action appears on a `done` act row in the plan workspace, beside the existing
  « Voir la fiche ». The uncalled `markItemDone` client function is either wired or deleted — not left dead.
- **AC-P2.12 (A-13):** `POST /treatment-plans/{id}/items/{itemId}/done` and the new un-mark both carry an explicit
  role policy. Today the mark-done endpoint has none, so a secretary can close a devis act.

## US-P2b: Records and documents can be removed, and removal cleans up after itself

**As a** dentist
**I want** to delete a fiche de soins or an ordonnance I created by mistake
**so that** a wrong document does not stay in a patient's record permanently.

> **Rule 1 applies here.** § 5.6 makes § 6.11 reachable. Land AC-P2.13–2.15 (un-mark, from US-P2a) **before**
> AC-P2.16 (the delete button), and AC-P2.19–2.21 (the cleanup) in the same part.

- **AC-P2.13 (6.11):** Deleting a dental record clears `TreatmentPlanItem.LinkedDentalRecordId` on any act pointing
  at it and returns that act to `Planned`, reopening the plan if it had auto-completed.
- **AC-P2.14 (6.11):** Deleting a dental record clears `InvoiceLine.DentalRecordId` on any line pointing at it. The
  invoice, its amount and its number are **untouched** — the link is removed, the money is not.
- **AC-P2.15 (6.11):** The cleanup runs in the **same transaction** as the delete. A partial cleanup is the defect.
- **AC-P2.16 (5.6):** A « Supprimer » action on the fiche row deletes it, behind the repo's standard `AlertDialog`.
- **AC-P2.17 (5.6):** When the fiche is billed on a non-cancelled invoice, the confirmation says so explicitly and
  names the invoice number. The page already computes `invoicedDentalRecordIds`.
- **AC-P2.18 (5.6):** When the fiche is linked to a plan act, the confirmation says that the act will return to
  « prévu » — the user is told the consequence before confirming, not after.
- **AC-P2.19 (A-9):** `DeleteDentalRecordCommand`'s clinic-resolution failure message is French and leaks no
  exception text. It currently returns the English `"Unable to resolve current clinic"`.
- **AC-P2.20 (5.7):** A « Supprimer » action on the documents row deletes a medical document, behind the same
  `AlertDialog`. The blob is best-effort deleted exactly as the existing handler already does.
- **AC-P2.21 (A-8):** `DeleteMedicalDocumentCommand`'s catch returns a French message with no interpolated
  `ex.Message`. It currently returns `$"Error deleting medical document: {ex.Message}"`.
- **AC-P2.22 (A-12):** Both delete endpoints carry an explicit role policy. Today both controllers have class-level
  `[Authorize]` only, so any `secretary` can delete a fiche de soins or an ordonnance.

## US-P2c: Administrators can fix what they set up wrong

**As a** clinic administrator
**I want** to change a staff member's role, fix another practitioner's profile, and disconnect a wrong Google account
**so that** a mistake at onboarding is not permanent.

- **AC-P2.23 (5.8):** An admin can change a user's role between `admin`, `doctor` and `secretary` from the user
  management table.
- **AC-P2.24 (A-11):** The role value is **validated** against the closed set. `User.Update` currently accepts any
  string, including empty.
- **AC-P2.25 (A-11):** Changing a role does **not** null the user's email or full name. `User.Update(role)` currently
  defaults both to `null` and assigns them — a one-argument call silently wipes both fields.
- **AC-P2.26 (5.8):** An admin cannot remove their own admin role while they are the clinic's only active admin —
  the same self-lockout guard `SetUserActiveCommand` already applies to deactivation.
- **AC-P2.27 (5.8):** A role change takes effect on the target user's next request. § 1/§ 2 gave the token a
  `TokenVersion`; a role change must bump it or the old role stays live for the token's lifetime.
- **AC-P2.28 (5.9):** The « Utilisateurs » nav entry is visible to any clinic admin in **both** modes. The page and
  the controller already work in Cloud; only `mode === "local" &&` at `dashboard-sidebar.tsx:86` hid it.
- **AC-P2.29 (5.9):** In Cloud, « Réinitialiser le mot de passe » is hidden or disabled with an explanation, because
  `ResetUserPasswordCommand` correctly refuses for non-local accounts. List and activate/deactivate work in both.
- **AC-P2.30 (5.11):** An admin can edit another practitioner's CNOMDT number and cachet from the doctors roster,
  through the existing `PUT /api/doctors/{id}`.
- **AC-P2.31 (5.11):** A client function for that endpoint is added — none exists today, which is why the endpoint
  is unreachable by construction.
- **AC-P2.32 (5.11):** The existing own-or-admin handler guard is unchanged; a doctor editing themselves still goes
  through `/doctors/me`.
- **AC-P2.33 (5.5):** An admin can disconnect Google Calendar. A new endpoint calls the existing, uncalled
  `Clinic.ClearGoogleCalendarConnection()`.
- **AC-P2.34 (5.5):** It is `AdminOnly`, matching every other mutating action on that controller, and appears beside
  « Importer depuis Google » behind an `AlertDialog`.
- **AC-P2.35 (5.5):** After disconnecting, the connection status reads « Non connecté » and appointments stop being
  pushed. Already-synced appointments keep their `GoogleCalendarEventId` — disconnecting does not delete anything in
  the clinic's Google account.
- **AC-P2.36 (5.12):** The procedure-type colour palette comes from `GET /api/procedure-types/colors`.
- **AC-P2.37 (A-14):** Because that endpoint returns bare hex strings with **no names**, the French labels stay
  client-side and the endpoint is the authority for *which* colours are valid. The hardcoded array and its
  "must match backend" comment are deleted; a hex with no local label still renders, using the hex as its label.

## US-P2d: Lab orders and specialties behave

- **AC-P2.38 (6.10):** `LabWorkOrder.SetStatus` enforces a declared transition table. A `Fitted` order cannot be
  pushed back to `Sent`.
- **AC-P2.39 (6.10):** `ReceivedDate` is re-stamped when an order is received again after moving backwards through a
  legal path, rather than keeping the first date forever.
- **AC-P2.40 (6.10):** An illegal transition returns a French `Result.Failure`; the UI's status control offers only
  legal next states.
- **AC-P2.41 (6.10):** Existing rows in any state load and render — the new rules gate **transitions**, never reads.
- **AC-P2.42 (8.3):** Doctor specialties render in French everywhere they are shown — « Mon profil », the doctors
  roster, the appointment doctor picker, and the **printed** certificat médical and PDF/DOCX signature block.
- **AC-P2.43 (8.3):** Storage keys stay English and are **not** migrated, following the existing `weekdayLabelsFr`
  precedent in `setup-wizard.tsx`. A display-time map is the fix; a data migration is not.
- **AC-P2.44 (8.3):** The three duplicated specialty arrays (`clinic-settings`, `setup-wizard`, `join-wizard`) become
  one shared constant. They are byte-identical today and will drift.
- **AC-P2.45 (8.3):** A stored specialty with no French label renders verbatim rather than blank — a clinic that
  typed a custom value keeps it.

---

# Part P3 — UX, accessibility & French finish

> Almost entirely frontend. There is **no test runner in `web/`**, so the gate for this part is `npx tsc --noEmit`
> clean, `npm run build` clean (27/27 static pages), and a documented manual page walk.

## US-P3a: The app stops lying about reminders

**As a** secretary
**I want** « Rappel envoyé » to mean a reminder was actually sent
**so that** I do not leave a patient un-contacted for 30 days believing I reached them.

`SendRecallCommand` stamps `MarkRecallContacted(+30 days)` and returns success **unconditionally**, while
`ReminderScheduler.ScheduleRecallAsync` returns early when `EnabledChannels.Count == 0`. The command never learns.

- **AC-P3.1 (3.3):** `SendRecallCommand` learns whether a reminder was actually enqueued and returns that outcome.
- **AC-P3.2 (3.3):** When no channel is configured, the command **fails** with a French message directing the user to
  the reminder settings — and the patient is **not** marked contacted and **not** snoozed.
- **AC-P3.3 (3.3):** The recalls page surfaces that failure instead of toasting « Rappel envoyé à … ».
- **AC-P3.4 (3.3):** When a channel *is* configured, behaviour is unchanged — contacted stamp, 30-day snooze, success
  toast.
- **AC-P3.5 (3.3):** **Enqueuing is not sending.** A recall whose dispatch later reaches `Failed` clears the snooze
  and returns the patient to the relance list, and the staff notification says they must be re-contacted. Learning
  the outcome at enqueue-time only (AC-P3.1) would move the silent 30-day suppression one step later instead of
  removing it — which is the whole defect.
- **AC-P3.6 (3.3):** A partial send across two configured channels resolves to a **stated** patient state, not an
  implicit one: the patient is left on the list unless at least one channel actually succeeded.
- **AC-P3.7 (6.3):** A reminder that reaches `NotificationStatus.Failed` generates an in-app staff notification via
  the existing `INotificationGenerator` seam, deep-linking to the patient or appointment.
- **AC-P3.8 (6.3):** That notification is visible to the **secretary who books**, not only to admins.
- **AC-P3.9 (6.3):** `ReminderStatusDto` carries the patient's name and the appointment date. Today it carries a
  masked phone and nothing else, so « •••• 56 — Échec » is unactionable.
- **AC-P3.10 (6.3):** The phone stays masked. Adding the name is what makes the row actionable; unmasking is not
  required and is not done.
- **AC-P3.11 (6.3):** Generation failures never fail the dispatch job — best-effort, logged, exactly as the existing
  notification generator contract requires.

## US-P3b: The app is usable on a phone

**As a** dentist checking the day's schedule on my phone
**I want** the app to fit the screen
**so that** I do not lose two thirds of it to a sidebar I cannot collapse.

Scope is the navigation shell and the four named fixed-width offenders. A full responsive pass over every table,
dialog and the calendar grid is **out of scope** (see Out of Scope) — the calendar and the wide data tables are each
substantial on their own.

- **AC-P3.12 (7.1):** Below the `md:` breakpoint the sidebar is a slide-over drawer, closed by default, opened by a
  header control, and closing on navigation and on Escape. At `md:` and above the current behaviour is unchanged.
- **AC-P3.13 (7.1):** The drawer uses a shadcn `Sheet`. `vaul` and `@radix-ui/react-dialog` are already installed, so
  this adds **zero new dependencies**.
- **AC-P3.14 (7.1):** At 375 px no page scrolls horizontally at the body level. Wide content — tables, the calendar —
  scrolls inside its own container.
- **AC-P3.15 (7.1):** The header reflows below `md:` rather than overflowing.
- **AC-P3.16 (7.1):** The AI assistant panel is viewport-relative below `md:` instead of a fixed `w-96 h-[600px]`.
- **AC-P3.17 (7.1):** The document editor's fixed `w-[420px]` column stacks below `md:`.
- **AC-P3.18 (7.1):** The persisted collapse preference is not clobbered by the responsive behaviour — a user who
  collapsed the sidebar on desktop still finds it collapsed on desktop.
- **AC-P3.19 (7.2):** In Local mode, a session expiry redirects to `/login`, which exists, instead of `/auth/login`,
  which does not.
- **AC-P3.20 (7.2):** `returnTo` never points at a route with no page. After signing in the user lands on the page
  they were trying to reach, or the dashboard.
- **AC-P3.21 (7.2):** The redirect target is derived from the session mode, not hardcoded, so the Cloud path is
  unchanged.

## US-P3c: Every action gives feedback, and every failure is visible

- **AC-P3.22 (7.3):** The patients list has a working edit action. `EditPatientDialog` is already mounted; its state
  setters are never called.
- **AC-P3.23 (7.3):** Editing from the list refreshes the row without a full page reload.
- **AC-P3.24 (7.4):** The AI assistant does **not** speak automatically by default.
- **AC-P3.25 (7.4):** A persistent, discoverable toggle controls speech, and the preference survives a reload.
- **AC-P3.26 (7.4):** « Arrêter la lecture » still works while speech is in progress.
- **AC-P3.27 (7.5):** The patient-files page shows an error state instead of rendering the manager under the literal
  heading « Patient ».
- **AC-P3.28 (7.5):** The factures revenue KPIs distinguish "failed to load" from "—". They currently swallow the
  error without even a `console.error`.
- **AC-P3.29 (7.5):** File download from the patient page toasts on failure, matching what the same action already
  does in `patient-files-manager.tsx`.
- **AC-P3.30 (7.5):** File preview failure explains itself instead of silently closing the dialog.
- **AC-P3.31 (7.5):** A failed procedure-type load in **both** appointment dialogs shows why the « Sélectionner un
  type d'acte » list is empty. The create dialog does this deliberately today (`// Don't show error to user`); the
  edit dialog does it without even a comment (**C-4**).
- **AC-P3.32 (7.5):** All of these use the **existing** `showErrorToast(err, fallback)` helper at
  `web/lib/errors.ts:26`. This is applying a helper, not inventing one.
- **AC-P3.33 (7.5):** No user-initiated action in the app fails with only a `console.error`. The ~12 further
  same-class swallows found during exploration are fixed in the same pass or explicitly listed as intentional.
- **AC-P3.34 (7.6):** « Créer le dossier » is disabled while in flight. Double-clicking creates one folder. The
  Enter-key path is covered too — it calls the same handler.
- **AC-P3.35 (7.7):** `/patients` renders a skeleton matching the table's column layout instead of `null`.
- **AC-P3.36 (7.7):** The skeleton follows the only existing precedent — `stats-card.tsx:29-33`,
  `animate-pulse rounded bg-muted` with `aria-label="Chargement"`.
- **AC-P3.37 (7.9):** `clinic-settings.tsx` uses `sonner` like every other screen. The bespoke `fixed top-4 right-4`
  banner and its 4-second timer are deleted.

## US-P3d: Accessibility

- **AC-P3.38 (7.8):** Every clickable `<Card>` in the /documents gallery is keyboard-operable — `role="button"`,
  `tabIndex={0}`, Enter and Space handlers, and a visible focus ring.
- **AC-P3.39 (7.8):** The same applies to folder cards and file cards in the files manager.
- **AC-P3.40 (7.8):** The per-file delete button has an `aria-label`. It is icon-only with neither label nor title
  today, while the adjacent download button at least has a `title`.
- **AC-P3.41 (7.8):** The invoice cancellation reason has a real `<Label htmlFor>` — the avoir dialog forty lines
  below already does this correctly.
- **AC-P3.42 (7.8):** Interactive elements reachable by keyboard have a visible focus indicator.
- **AC-P3.43 (7.8):** **Every new interactive surface this spec creates meets the same bar** — P1's working-hours
  editor and conflict list, P2's eight new actions, P7's history, duplicate-warning and anonymize screens, and P8's five
  screens. §7.8 fixes accessibility for the surfaces that exist; the spec then adds roughly fourteen more, and the
  hardest ones are the new ones (an inline duplicate warning that must not trap the operator, a multi-select
  bordereau batcher).
- **AC-P3.44 (7.8):** The shadcn primitives those surfaces need are **added, not hand-rolled**. `web/components/ui/`
  today has no `radio-group`, no multi-select table pattern and no pagination primitive, so all three would otherwise
  be improvised three different ways.
- **AC-P3.45:** Every mutating action this spec adds is **disabled while in flight**, has a single effect on
  double-submit, confirms success with a French `sonner` toast, and surfaces failure via the existing
  `showErrorToast` with the dialog left open and its input intact. AC-P3.34 currently defines this for exactly one
  button, while the spec adds ~15 — including two that are irreversible or multi-table: anonymize and bordereau
  finalize.
- **AC-P3.46:** Async results are **announced**, not just rendered. `web/` contains one `aria-live` region in the
  entire codebase and zero `role="status"`, so today a screen-reader user learns nothing when an action completes.
- **AC-P3.47:** Anonymization's confirmation is a **type-to-confirm** gesture, not an ordinary `AlertDialog`. The repo
  has no such pattern, so AC-P7.31's "says so unambiguously" would otherwise resolve to the same two-button dialog
  used for deleting a procedure type.
- **AC-P3.48:** A documented manual walk covers **every screen this spec touches or creates** — not only P3's — at
  375 px and with the keyboard, and its result is recorded in `progress.md`. There is no automated frontend test to
  assert any of this, which is why the walk's scope is stated rather than assumed.

## US-P3e: French

- **AC-P3.49 (8.2):** Every user-visible English string in the audit's list is French — `clinic-settings.tsx`
  (« Loading clinic settings… », « Add Doctor », three « Cancel »), `procedure-types-table.tsx` (« Loading procedure
  types… », the « Procedure Types » card title), « Back to Patient », « Push », and the `file`/`files` pluralization.
- **AC-P3.50 (C-10):** The seven strings the audit missed are also French — `clinic-settings.tsx`'s « Share with
  coworkers to join this clinic », three « Edit », « Clinic Name », « Full Address », « Phone Number ».
- **AC-P3.51 (C-10):** File sizes render « o / Ko / Mo », not « B / KB / MB ».
- **AC-P3.52 (8.2):** `reminder-settings.tsx`'s « Phone Number ID » is decided explicitly — it names a Meta API field,
  so keeping it verbatim is legitimate, but the decision is recorded rather than left as an oversight.
- **AC-P3.53 (8.2):** A repo-wide sweep for user-visible English is run and its result recorded, so this is closed as
  a class rather than as a list of nine files.
- **AC-P3.54 (8.6):** Patient form placeholders are Tunisian — names, email, address, insurance provider and policy
  numbers. This is **one component**: `edit-patient-dialog.tsx` is also the create form (**C-9**). The audit missed
  « Hypertension, Type 2 Diabetes » at `:764`. The inline new-patient sub-form in `create-appointment-dialog.tsx` is
  covered too. `"Tunis"` and the phone hint are already correct and stay.

---

# Part P4 — Stock, realtime & schema

## US-P4a: Stock tells the truth about what is on the shelf

**As a** secretary managing supplies
**I want** expiry dates I enter to come back, and every stock change to appear in the ledger
**so that** the on-hand figure and the movement history agree.

`StockItem.ExpiryDate` and `BatchNumber` persist, the command accepts them, the client function accepts them — and
the restock dialog never sends them while `StockItemDto` never returns them. A write-only field with no writer and
no reader. Separately, `UpdateStockItemCommand` writes an absolute `CurrentStock` with **no** `StockMovement` row,
so the ledger stops reconciling permanently, with no way to repair it after the fact.

> **Correction to an earlier draft of this spec.** It claimed the entity "already models" expiry per restock. It does
> **not**: `ExpiryDate` and `BatchNumber` are two scalar columns on `StockItem` that `AddStock` **overwrites**, so a
> second restock destroys the first batch's date. Per-batch rows are therefore a schema change, listed as such.

- **AC-P4.1 (6.6):** Expiry and batch number are recorded **per batch**, on a child row, not as two scalar columns
  overwritten by the most recent restock.
- **AC-P4.2 (6.6):** The restock dialog captures expiry date and batch number and sends them.
- **AC-P4.3 (6.6):** `StockItemDto` returns the item's batches, and the stock table shows the earliest relevant
  expiry.
- **AC-P4.4 (6.6):** Consumption draws down batches in a **stated** order — earliest expiry first (FEFO) — so the
  displayed expiry is the one that actually matters.
- **AC-P4.5 (6.6):** An item at or past its expiry is visually distinguished, as low stock already is, and the
  highlight reflects the batch that is expiring rather than the last one entered.
- **AC-P4.6 (6.6):** An item approaching expiry generates the same class of in-app notification low stock does, with
  a configurable lead time.
- **AC-P4.7 (6.6):** Both fields stay optional. An item whose batches carry no expiry behaves exactly as today.
- **AC-P4.8 (6.6):** Existing `ExpiryDate`/`BatchNumber` values migrate into a single opening batch rather than being
  dropped.

### Stock is consumed by performing an act

`grep StockItem` across every feature outside `Features/Stock/` returns **zero** hits, and neither `ProcedureType`
nor `DentalActCode` links to a stock item. Consumption is 100% manual. This is the only bullet in P4 that needs
net-new data.

- **AC-P4.9 (6.7):** An act — `ProcedureType` and/or `DentalActCode` — can carry a **material list**: stock items
  and the quantity each act consumes.
- **AC-P4.10 (6.7):** Saving a fiche de soins consumes the material list for each act it records, writing a real
  `StockMovement` per item so the ledger stays reconcilable (AC-P4.15).
- **AC-P4.11 (6.7):** The link is **opt-in per act**. An act with no material list consumes nothing and behaves
  exactly as today — this is the majority case and must not regress.
- **AC-P4.12 (6.7):** Insufficient stock has a **stated** rule: recording the visit is never blocked by a stock
  shortfall — clinical work has already happened — the shortfall is recorded and surfaced as a low-stock
  notification. Stock is allowed to go negative rather than silently clamping to zero and losing the discrepancy.
- **AC-P4.13 (6.7):** Consumption is best-effort and post-commit, following the existing `INotificationGenerator`
  precedent: a stock-consumption failure logs at Error and **never** rolls back the fiche.
- **AC-P4.14 (6.7):** Material lists are editable by an admin from the act catalog, and are per-clinic like every
  other catalog in this app.
- **AC-P4.15 (6.12):** `UpdateStockItemCommand` writes a `StockMovement` row whenever `CurrentStock` changes.
- **AC-P4.16 (A-17):** `StockMovementType` gains a third member for a manual correction, so an adjustment is
  distinguishable from a consume or a restock in the ledger.
- **AC-P4.17 (A-16):** Every `StockMovement` write site populates `Reason`. The column, the ctor parameter and the DTO
  field all exist; all three current write sites pass `null`.
- **AC-P4.18 (6.12):** A concurrent consume is not silently overwritten. `StockItem` inherits `Entity<TId>.Version`
  from the § 1 merge, so the fix is to send and check the version — **not** to invent a second mechanism.
- **AC-P4.19 (6.12):** Σ movements reconciles with `CurrentStock` for any item whose history is entirely post-change.
  Pre-existing drift is reported, not silently rewritten.

## US-P4b: Realtime covers every screen that mutates

Five backend keys (`doctors`, `expenses`, `laborders`, `recall`, `waitinglist` — 17 commands) broadcast into the void,
and `documents` is declared in the frontend with no subscriber at all. `/waiting-list` — the canonical two-user screen
— emits on all four of its mutations and refreshes only local state.

- **AC-P4.20 (9.1):** `clinic-hub.ts` declares every key the backend can emit.
- **AC-P4.21 (9.1):** `/waiting-list`, `/lab-orders`, `/caisse`, `/recalls`, `/creances`, `/recurring-series`, the
  dashboard and « Mon profil » each subscribe to the keys their data depends on.
- **AC-P4.22 (A-15):** `documents` either gains a subscriber on the documents pages or is removed. A declared key
  with no listener is the same defect in the other direction.
- **AC-P4.23 (A-19):** `RealtimeResourceResolverTests` becomes a real contract test: it reflects over every
  `IRequest` in `*.Features.<Area>.Commands`, projects through the resolver, and asserts the resulting **set** equals
  a declared expected set. A new `Features/<X>/Commands` folder fails the build until `X` is classified.
- **AC-P4.24 (A-19):** The same test parses `web/lib/realtime/clinic-hub.ts` and asserts **both** directions — every
  emitted key has a listener, and every declared key has an emitter. Asserting C# against a C# copy of the frontend
  list is why five orphans survived a "contract" test.
- **AC-P4.25 (A-19):** Intentional emit-only or listen-only keys sit on a named allow-list constant with a rationale
  comment, following `ControllerAuthorizationCoverageTests`. An exemption is a reviewed decision, not an omission.
- **AC-P4.26 (9.1):** A peer's change on each of those screens refreshes the other user's view without a reload.

## US-P4c: The schema stops being internally inconsistent

- **AC-P4.27 (9.2):** `Doctor` and `StockItem` carry the clinic global query filter, matching the 17 roots that
  already do — including `StockItem`'s own child `StockMovements`, which is filtered while its parent is not.
- **AC-P4.28 (9.2):** The filter is fail-open in the same way as the existing 17
  (`!IsClinicScoped || X.ClinicId == ScopedClinicId`), so jobs, the CLI and auth flows keep working.
- **AC-P4.29 (9.2):** The stale comment at `ApplicationDbContext.cs:79-82` claiming filters apply only to
  Patient/Appointment/ProcedureType is corrected — 14 more roots have been filtered since it was written.
- **AC-P4.30 (9.3):** `Notifications` gains an index supporting `Status == Pending && ScheduledFor <= now`. The
  closest existing precedent is `InvoiceConfiguration.cs:83`'s `(EInvoiceStatus, EInvoiceNextAttemptAt)` — the same
  outbox shape.
- **AC-P4.31 (9.3):** The outbox query is **bounded** by a batch size, as `EInvoiceOutboxJob` already is with
  `TtnConfig.DispatchBatchSize`.
- **AC-P4.32 (9.3):** Sent and permanently-failed rows are purged on a retention window. There is no purge of any
  kind today, so the table grows forever.
- **AC-P4.33 (9.3):** The retention window is configurable and its default is stated in the spec, not buried.
- **AC-P4.34 (9.3):** Purging never deletes a `Pending` row — that is `VoidUnsentAsync`'s job, not retention's.
- **AC-P4.35 (9.4):** `StockMovement.ClinicId` gains an index, and an FK to `Clinic` following the
  `StockItemConfiguration.cs:21-26` template. The filter predicate appended to every read is unindexed today.
- **AC-P4.36 (9.5):** `StockItem.UnitPrice` becomes `decimal(18,3)`, matching the other 26 money columns. Existing
  values widen without loss.
> **Corrected 2026-07-28.** An earlier draft said a `ConfigureConventions` `HavePrecision(18,3)` alone would make
> § 9.5 unrepeatable. **It would do nothing.** All 29 decimal properties in this model carry an explicit
> `HasColumnType("decimal(…)")`, and `GetColumnType()` returns that annotation verbatim — bypassing facet-derived
> store types entirely. The differ would emit **zero** `AlterColumn` statements and `StockItem.UnitPrice` would stay
> at 2 decimals: the exact bug it looks like it fixes. The convention only bites once the redundant explicit calls
> are gone.

- **AC-P4.37 (A-18):** A `ConfigureConventions` override sets `HavePrecision(18, 3)` for `decimal` **and the 26
  redundant `HasColumnType("decimal(18,3)")` calls are deleted**, across 18 configuration files. Only then does the
  convention govern anything. There is no `ConfigureConventions` override in this DbContext today.
- **AC-P4.38 (A-18):** Exactly **three** properties deviate and each is handled explicitly, by name — there are no
  others: `StockItem.UnitPrice` `(18,2)` is **normalized** to the convention (that is § 9.5); `Clinic.VatRate` and
  `Invoice.VatRate` `(5,2)` **keep** their precision via a retained explicit annotation plus a comment saying why.
  They are rates, not money; a convention that silently widens a VAT rate is worse than the drift it fixes.
- **AC-P4.39 (A-18):** A model-level test asserts every mapped `decimal` resolves to `(18,3)` **except** the two
  annotated rate columns. This is the part that makes the fix durable — without it the next contributor re-adds an
  explicit `HasColumnType` and nothing notices.
- **AC-P4.40 (A-18):** The generated migration is read line by line before commit. It legitimately contains one
  `AlterColumn` for `StockItem.UnitPrice`; it must contain **no** `AddColumn<uint>("xmin")` (§ 1's differ hazard) and
  no unintended change to the two rate columns.
- **AC-P4.41 (9.6):** `GetPatientsToRecallQuery` bounds its reads by date and pushes the filter to SQL. It currently
  materialises every patient and every appointment the clinic has ever had and filters in memory.
- **AC-P4.42 (9.6):** The recall list returns the same patients before and after — this is a performance fix, and any
  behavioural change is a defect.
- **AC-P4.43 (9.6):** Archived patients stay excluded — the § 1 merge added `Patient.IsArchived` and already excludes
  them here.

---

# Part P5 — Build & tooling

> Nothing here is user-visible. Its value is that the next 57 findings get caught by a machine instead of an audit.

## US-P5: The build can fail

**As a** developer
**I want** lint to run and warnings to matter
**so that** a regression is caught before it ships rather than by the next audit.

**There is no CI in this repository.** No `.github/`, no pipeline of any kind. Nothing has ever run `npm run lint`
or `dotnet build` automatically. § 10.1's "CI never notices" is true in the strongest possible sense — which means
every gate in this spec is a gate a human must run.

- **AC-P5.1 (10.1):** `npm run lint` succeeds on a clean `npm ci`. `eslint` and `eslint-config-next` are added to
  `web/package.json` devDependencies, pinned — `eslint-config-next` to the exact Next version (`15.5.9`) since it
  ships in lockstep, and `eslint` to at least `^9.26.0`, the floor at which the `eslint/config` subpath used by
  `eslint.config.mjs:1` exists.
- **AC-P5.2 (10.1):** The existing violations are either fixed or explicitly waived in config with a rationale. A
  lint that passes because everything is disabled is not a gate.
- **AC-P5.3 (10.1):** `next.config.ts`'s `eslint.ignoreDuringBuilds: true` is removed **once** lint is clean, so the
  build fails on a lint error. Removing it before AC-P5.2 breaks the build for everyone.
- **AC-P5.4:** A CI workflow runs the backend build, the test suite and the frontend gate on push. This is **net-new
  scope** — § 10.1 only observes that CI does not notice — and it is listed as its own In Scope line rather than
  smuggled in under a 🟡 bullet. Without it, everything in this part decays back to where it started, and the ~40
  tests this spec adds will never run again after it lands. **If CI is declined (Q-9), this AC moves to Out of Scope
  in the same edit** — it is not satisfiable by writing a sentence about having decided.
- **AC-P5.5 (10.2):** `fetch-build-tools.ps1` pins one PostgreSQL version rather than probing five URLs in sequence
  and taking whatever responds.
- **AC-P5.6 (10.2):** Every downloaded artifact — PostgreSQL and `nssm-2.24.zip` — is checksum-verified, and a
  mismatch aborts loudly. There is no `Get-FileHash` in the script today.
- **AC-P5.7 (10.2):** The staged runtime's actual version is asserted against what the installer claims to bundle, so
  a silent fallback to an older PostgreSQL cannot ship.
- **AC-P5.8 (C-5):** The audit's ICU claim is **corrected in the audit file**: the tree is PostgreSQL 16.9 and EDB
  genuinely bundles ICU 67.1 with it. `packaging/build-output/` and `build-tools/` are gitignored, so they are local
  artifacts, not vendored content — the reproducibility gap is the real defect and it is what AC-P5.6–5.8 close.
- **AC-P5.9 (10.3):** The 46 CS8618 warnings in `ClinicManagement.Domain` are resolved — `required`, `= null!`, or a
  nullable annotation, chosen per property rather than blanket-suppressed. 21 files; `MedicalDocument.cs` alone has 8.
- **AC-P5.10 (10.4):** The remaining 12 are resolved: 6 × CS8602 (four of which are the same `result.Value.Id` pattern
  and are best fixed once, by adding `[MemberNotNullWhen(false, nameof(Value))]` to `Result.IsFailure`), 2 × CS8600,
  1 × CS8604, 1 × CS0618 (the obsolete Hangfire `UsePostgreSqlStorage(string)`), 2 × CS8981 (`addclinics`, in
  **Infrastructure**, not API as the audit states).
- **AC-P5.11 (A-26):** `TreatWarningsAsErrors` is enabled once the count reaches zero, via a single
  `Directory.Build.props` rather than six edited `.csproj` files. Today the 58 warnings can never fail a build.
- **AC-P5.12 (A-26):** An `.editorconfig` is added so style is enforced rather than remembered. Neither file exists
  today.
- **AC-P5.13 (10.5):** `SeedAllClinicsAsync` and `BackfillAsync` survive a transient database failure at boot —
  bounded retry, and a clear log line when they are skipped.
- **AC-P5.14 (A-24):** `BackfillAsync` **runs in Local mode**. Its only call site today is inside
  `if (!isLocalAuthMode)`, so no Local install has ever run it.
- **AC-P5.15 (A-25):** A seeding failure in Local mode no longer stops the Windows service. `DeferredStartupService`'s
  catch currently calls `StopApplication()`, so a transient DB blip during the seed takes the whole install down
  rather than skipping a backfill.
- **AC-P5.16 (10.5):** Seeding stays idempotent — it is a backfill, and running it twice must be a no-op.

---

# Part P6 — Money truth & timezone

> **The § 1 gate is lifted.** § 1 merged at `1932acf`; these findings are implemented in normal order. § 6.2 arrives
> already fixed and is a regression AC only.

## US-P6: Tunisia is UTC+1

**As a** clinic owner
**I want** « aujourd'hui » to mean today in Tunis and my invoice numbers to follow the fiscal year
**so that** a payment taken at 00:30 is not booked to yesterday and a January invoice is not numbered into last year.

Tunisia is UTC+1 year-round with no DST. The solution has **no clock abstraction** — 292 `DateTime.UtcNow`, 5
`DateTime.Today` (server-machine-local, a third convention), and two byte-identical private copies of
`ResolveTunisiaTimeZone()` used only for display formatting.

**Constraint:** `ApplicationDbContext` installs a `ValueConverter` on every `DateTime` and treats
`DateTimeKind.Unspecified` as UTC on write. Any local-day helper must return an explicit UTC instant via
`TimeZoneInfo.ConvertTimeToUtc`, never a bare local `DateTime`.

- **AC-P6.1 (A-21):** One shared clinic-timezone helper replaces the two duplicated private copies. § 4.1 and § 4.2
  must not add a third.
- **AC-P6.2 (4.1):** A local-day-boundary helper converts a Tunisian calendar day to an explicit UTC instant range,
  and is the single authority for "today" in a query.
- **AC-P6.3 (4.1):** `GetCaisseSummaryQuery` and `GetDashboardStatsQuery` default to the **local** day when no range
  is supplied. Both UI callers already send local bounds (**C-2**), so this closes the gap for direct API callers
  and any future server-side caller — and removes a trap rather than a visible bug.
- **AC-P6.4 (A-23):** The same treatment reaches the four un-overridable reads that share the defect —
  `GetPatientBillingSummaryQuery`, `GetReceivablesQuery`, `GetPatientsToRecallQuery`, `GetPatientAiSummaryQuery`.
- **AC-P6.5 (A-20):** `payment-modal.tsx` pre-fills the payment date from the **local** date. Today it uses
  `new Date().toISOString().slice(0,10)`, so between 00:00 and 01:00 Tunis it defaults to *yesterday* — the real,
  un-overridable § 4.1 symptom.
- **AC-P6.6 (A-22):** The 5 × `DateTime.Today` in `AIActionService` use the clinic timezone. They are currently
  server-machine-local, which silently depends on the clinic PC's OS setting.
- **AC-P6.7 (4.2):** Invoice numbering takes the year from the **clinic-local** date. A note d'honoraires issued at
  00:30 on 1 January is numbered into the new fiscal year.
- **AC-P6.8 (4.2):** Devis numbering does the same.
- **AC-P6.9 (4.2):** The tests pin a **fixed** year rather than recomputing `DateTime.UtcNow.Year`. § 1 already
  flagged that `IssueInvoiceCommandHandlerTests` recomputes the same expression the handler uses, so it can never
  detect a wrong-year defect and flakes across New Year.
- **AC-P6.10 (4.2):** The existing numbering-collision retry is unaffected — the year changes, the retry does not.
- **AC-P6.11 (6.2):** *(Regression only — closed by § 1.)* The dashboard's « encaissé » and the caisse's cash-in
  agree after a refund, and a test pins it so the § 1 fix cannot silently regress.
- **AC-P6.12 (6.8):** The invoice form sends `appointmentId` when the invoice is raised from an appointment context.
  The backend field already exists and nothing has ever populated it.
- **AC-P6.13 (6.8):** An invoice shows which visit it bills, and a visit shows its invoice.
- **AC-P6.14 (6.8):** The link is optional — an invoice raised without an appointment is unchanged.
- **AC-P6.15 (5.10):** The frontend calls `GET /api/cnam-nomenclature/reimbursement-estimate` instead of
  reimplementing the calculator. The client-side `CHILD_RATE`/`ADULT_RATE` duplicate is deleted.
- **AC-P6.16 (5.10):** The estimate stays editor-only — never persisted, never on the generated BS1 PDF — exactly as
  `cnam-nomenclature-lookup/spec.md` AC-5 requires. This is a de-duplication, not a behaviour change.
- **AC-P6.17 (5.10):** A failed estimate call degrades visibly rather than silently showing nothing.
- **AC-P6.18 (8.5):** `lab-orders/page.tsx` and `procedure-types-table.tsx` use `formatDT`. The latter is
  `toFixed(2)`, so it drops the millime as well as printing a period.
- **AC-P6.19 (8.5):** The `DollarSign` icon beside a dinar amount is replaced.
- **AC-P6.20 (8.5):** Dashboard counters use an explicit `fr-TN` locale rather than a bare `toLocaleString()` whose
  grouping follows the browser.
- **AC-P6.21 (9.7):** `GetReceivablesQuery` batch-loads patients instead of one `GetByIdAsync` per patient inside the
  merge loop.
- **AC-P6.22 (9.7):** The already-billed guard in `CreateInvoiceFromTreatmentPlanCommand` uses the light
  `GetTreatmentPlanLinksAsync` projection that exists for exactly this, instead of loading every invoice of the
  patient with its lines and payments to test one `TreatmentPlanId`.
- **AC-P6.23 (9.7):** Both keep identical behaviour. `MoneyReadConsistencyTests` stays green — and note its `Wire()`
  helper **hand-reimplements repository SQL in LINQ**, so any repository filter change must be mirrored there or the
  suite passes while production is wrong.

---

# Part P7 — Audit trail, duplicate prevention & anonymize

> **This is a feature, not a bullet (R-2).** The audit files it as one 🟡 P2 line below "the calendar is a flat
> 24-hour grid". It is three unrelated things: an audit trail, a patient merge, and archive/anonymize on delete. The
> archive limb is **already done** by the § 1 merge. **Merge was dropped after review** and replaced with duplicate
> *prevention* — see US-P7b for the reasoning. The trail and anonymize are built here at full scope.

## Where this builds on § 1

§ 1 landed the seams this part needs. **Build on them; do not duplicate them.**

| § 1 asset | Consequence |
|---|---|
| `Entity<TId>.Version` → PostgreSQL `xmin`, all 38 entities, **no schema change** | Do not add a version concept. The `AddConcurrencyToken` migration has a **deliberately empty `Up()`** because EF's differ emits 38 × `AddColumn<uint>("xmin")`, which PostgreSQL rejects. New migrations must not re-add them |
| `ConflictException` translated **once** in `UnitOfWork.SaveChangesAsync` → HTTP 409 | The audit writer must sit **outside** that catch, or an audit-write failure surfaces as a bogus 409 |
| `when (ex is not ConflictException)` on every catch that returns a `Result` | Every new handler here follows the same rule |
| `Payment` / `InstallmentPayment` carry `VoidedByUserId` + `VoidedByName` | The first actor fields in the codebase. The trail aligns with them; it does not record the same fact twice in a second shape |
| `Patient.IsArchived` + archive / unarchive / deletion-check | The archive limb of § 6.4 is **done** |

There is **no `ICurrentUserService`**. `IClinicContext.GetUserId()` returns the acting user's id with no new
dependency; the *name* needs an `IUserRepository` lookup — `VoidPaymentCommand`'s `ResolveActorNameAsync` is the
precedent. ~94 handlers inject only `ICurrentClinicResolver` and hold no `IClinicContext`.

## US-P7a: Every change to a patient, appointment, invoice or devis has an author

**As a** clinic owner
**I want** to see who changed what and when
**so that** a disputed record has an answer and a mistake has an origin.

**Storage decision:** one `AuditEntry` table — who, when, entity type + id, action, and a changed-fields JSON diff.
Opt-in per aggregate. **No change to `Entity<TId>`**, which matters because § 1 owns that class now, and because
`CreatedBy`/`UpdatedBy` columns answer "who last touched this" but never "what changed" — and `SetActs`, `SetItems`
and `ReviseInstallments` replace whole child collections, so last-writer is frequently meaningless.

- **AC-P7.1:** A single `AuditEntry` aggregate records clinic, actor user id, actor name snapshot, UTC timestamp,
  entity type, entity id, action, and the changed fields.
- **AC-P7.2:** The actor name is a **snapshot**, and the user id a soft FK-less link — users get deactivated and
  `User.Id` is a `string`. This is the shape § 1 already chose for payment voids.
- **AC-P7.3:** Scope is `Patient`, `Appointment`, `Invoice` and `TreatmentPlan`, plus their owned children —
  `PatientFlag`, `PatientFile`, `PatientFolder`, `PatientMedicalHistory`, `PatientFamilyHistory`, `InvoiceLine`,
  `Payment`, `TreatmentPlanItem`, `Installment`. A child mutation is attributed to its **root**.
- **AC-P7.4:** Entities outside that set are not audited, and the exclusion is declared in one reviewable place —
  not implied by an absent attribute.
- **AC-P7.5:** **The seam is inline, per handler — not a `SaveChanges` hook.** Each audited handler writes its
  `AuditEntry` explicitly before its own `SaveChangesAsync`, so the row commits in the **same transaction** as the
  mutation. A hook cannot satisfy this: `NotificationGenerator` shares the same scoped `DbContext` and issues its own
  post-commit `SaveChangesAsync` inside a swallowing catch, so it would flush staged audit rows outside the handler's
  transaction and swallow the failure — an audited change committing with no audit row, exactly what P7 prevents.
  This costs ~38 handler edits and that cost is accepted.
- **AC-P7.6:** **If the audit write fails, the mutation fails.** The user sees a French message and can retry; a
  clinical or financial record that saved with no trail is the outcome this part exists to prevent. Because the
  writer is inline rather than in a hook, it sits outside § 1's `DbUpdateConcurrencyException` catch and an audit
  failure is never mis-reported as a 409.
- **AC-P7.7:** `AuditEntry.ClinicId` is populated for **non-HTTP writers** too. `ICurrentClinicProvider` has no
  clinic in a job, and a `Guid.Empty` row would be invisible to AC-P7.16's clinic-filtered admin log — a silent hole
  in exactly the writes nobody is watching.
- **AC-P7.8:** The diff records field name, old value and new value, and **never** records a value the caller was not
  already authorised to read.
- **AC-P7.9:** A collection-replacing mutation records the net change — items added, items removed — not "the whole
  collection changed". A naive property diff emitting noise on every plan edit makes the trail unusable.
- **AC-P7.10:** Writes with no HTTP user — `AIActionService`, `GoogleCalendarSyncService`, `NotificationJob`, the
  e-invoice outbox, `ClinicCatalogSeeder`, the `reset-admin-password` verb — record a **named synthetic actor**, never
  a null one. "Nobody changed this" must not be a possible answer.
- **AC-P7.11:** `AuditEntry` is clinic-scoped and carries the global query filter, matching the 17 roots that already do.
- **AC-P7.12:** It is indexed on `(ClinicId, EntityType, EntityId, OccurredAt)` and on `(ClinicId, OccurredAt)`.
- **AC-P7.13:** A retention window purges old rows on a scheduled job, and its default is stated here. § 9.3 in this
  same spec is the reminder outbox growing forever unpurged; a table that gets a row per mutation must not repeat it.
- **AC-P7.14:** Audit rows are **append-only**. There is no edit and no delete other than retention.
- **AC-P7.15:** An « Historique » view on the patient, invoice, devis and appointment records shows the trail,
  newest first, in French, with the actor's name.
- **AC-P7.16:** An admin-only clinic-wide audit log is filterable by actor, entity type and date range, and is paged.
- **AC-P7.17:** Audit write failures never fail the user's operation *silently* — they are logged at Error and, if
  the write cannot be made transactional for a given path, that path is listed explicitly rather than quietly exempt.

## US-P7b: Duplicate patients stop being created

**As a** secretary booking a call
**I want** to be told when the patient I am typing already exists
**so that** the clinic never ends up with one person's history split across two records.

> **This replaced patient merge.** The audit's mandate for merge was a single sub-clause — *"No patient merge either"* —
> inside the § 6.4 bullet, and § 1 already answered the duplicate problem: its `ArchivePatientCommand` comment states
> that archive exists precisely so *"a duplicate patient with a single booking could never be removed from the list."*
> Merge only helps the rare case where **both** records already carry real history; archive handles the common case.
> Meanwhile the actual cause is unguarded: there is **zero duplicate detection anywhere** in the codebase, and
> `create-appointment-dialog.tsx:400` creates a patient inline from a name and a phone number **without ever
> requiring a search**. Preventing the duplicate is worth more than consolidating it afterwards, and costs a fraction.
> Merge is recorded in `follow-up/` with this reasoning, not discarded.

- **AC-P7.18:** Creating a patient warns when a similar patient already exists in the clinic, matching on name
  similarity plus date of birth, and — when present — on phone number or CNAM identifier.
- **AC-P7.19:** The warning is **non-blocking**. Two people genuinely do share a name; the operator can proceed, and
  the copy says so. A hard block would be worked around by misspelling the name, which is worse than a duplicate.
- **AC-P7.20:** Each suggested match is identifiable and openable — name, date of birth, last visit — so the operator
  can confirm it is the same person before deciding, following the deep-linked blocker list at
  `patients-table.tsx:363-380`.
- **AC-P7.21:** The check runs on the **booking dialog's inline sub-form too**, not only the full patient form. That
  path is the one that creates duplicates today, because it never asks the operator to search first.
- **AC-P7.22:** Matching tolerates the transliteration variance this clinic actually sees — « Mohamed » / « Mohammed »,
  accents, and inverted given/family name order. **The mechanism is a persisted normalized column** maintained on
  write (lower-cased, accents stripped, name parts ordered) with an ordinary index, plus a bounded in-memory distance
  check over the candidates it returns. Chosen because this schema has **no expression or functional index anywhere**
  and EF cannot express `lower(...)`, so the alternatives are raw SQL with no precedent (`pg_trgm`) or loading every
  patient per check — which is § 9.6 in this same spec. Existing rows are backfilled idempotently.
- **AC-P7.23:** Archived patients are included in the match, with their archived state shown. A duplicate of an
  archived patient is exactly the case where the operator needs to un-archive rather than create a third record.
- **AC-P7.24:** The check never blocks the form on a slow or failed lookup — it degrades to no warning, and the
  operator is not left unable to create a patient because a query timed out.
- **AC-P7.25:** The lookup is clinic-scoped and pinned by a `*TenantIsolationTests` case, like every other
  clinic-scoped read. A duplicate warning must never reveal that a name exists in another clinic.
- **AC-P7.26:** Proceeding past a warning is recorded by the audit trail (P7a), so a duplicate created deliberately
  is distinguishable from one created blindly.

## US-P7c: A patient can be anonymized

- **AC-P7.27:** An admin can anonymize an archived patient — identifying fields are replaced with a non-reversible
  placeholder while clinical and financial history is retained.
- **AC-P7.28:** **Anonymization scrubs that patient's existing audit diffs.** AC-P7.8 records old *and* new values
  for every audited field and AC-P7.14 makes those rows append-only, so without this every name, phone, address and
  CNAM id the patient ever had would survive in `AuditEntry`. Identifying values are replaced in place; the row, its
  actor and its timestamp are kept.
- **AC-P7.29:** That scrub is the **one stated exception** to AC-P7.14's append-only rule, and the scrub is itself
  audited. An undocumented exception to an append-only guarantee is worse than not having the guarantee.
- **AC-P7.30:** **The identity snapshots outside the patient row are handled.** `MedicalDocument.PatientName` is a
  denormalized snapshot on every ordonnance and certificat, so an anonymize that leaves it is not an anonymize. The
  rule for those snapshots is stated explicitly, alongside AC-P7.32's rule for files.
- **AC-P7.31:** **Anonymize is refused while an e-invoice is `Pending`** — or the TEIF `BuyerName` is snapshotted at
  queue time. `EInvoiceService` re-reads the patient at **dispatch** and sends `GetFullName()` as the legal buyer, so
  anonymizing first would transmit a placeholder onto a filed fiscal document.
- **AC-P7.32:** Uploaded files and documents belonging to that patient are handled by an explicit stated rule —
  deleted or retained — not left as an implicit side effect.
- **AC-P7.33:** **Anonymization is atomic**, and blob removal happens only **after** the database change commits. If
  the blob delete succeeded and the database write failed, the patient would be half-anonymized with clinical files
  already gone — the one state in this spec that no retry can repair. A failure leaves the patient fully intact with
  a French message.
- **AC-P7.34:** Invoices, payments, credit notes and their numbers survive intact — anonymizing a patient must not
  alter the accounting record.
- **AC-P7.35:** The anonymization itself is audited, recording who and when but not the removed values.
- **AC-P7.36:** An anonymized patient cannot be un-archived back into the active list.

---

# Part P8 — CNAM claims, bordereau & reconciliation

> **The largest single item in the audit (R-3).** Filed as one 🟡 P2 bullet. In scope at full breadth by explicit
> decision. It is net-new product in a regulated domain, and three previous specs deferred exactly this, independently:
> `cnam-bulletin-soins/spec.md:36,38`, `cnam-bs1-official-overlay/spec.md:39`, `cnam-nomenclature-lookup/spec.md:36`.

**What exists today:** a coefficient lookup (`CnamNomenclatureEntry`), a lettre-clé value table (`CnamLetterValue`),
the DCH catalog the money side actually uses (`DentalActCode`), patient CNAM identity (`CnamInfo`), two estimate
calculators, and the BS1 renderer that stamps a genuine PDF at calibrated coordinates. **Nothing submission-shaped
exists.** `MedicalDocument` has no status, no submission date, no reference number — its only lifecycle field is
`bool IsDraft`, and `cnam-bulletin-soins/spec.md:32` locked in "no schema change". `DentalActCode.RequiresAccordPrealable`
is stored and read by nothing.

**Two decisions were taken up front:**

1. **A CNAM reimbursement is its own entity, not a `Payment`.** It is third-party money, not patient cash-in. The
   `PaymentMethod` enum gains **no** member, so the caisse and the dashboard — two reads the audit already flags as
   having disagreed — are untouched, and the 5 `Enum.TryParse` sites, 4 entities and **two independent frontend
   duplications** of the enum stay as they are.
2. **The bordereau's data model is built now; the calibrated official overlay lands when the AP1/AP2 PDF is supplied.**
   BS1 needed a genuine form stamped at measured coordinates — a custom table "isn't accepted as a real Bulletin de
   soins" — and that was an entire feature. The asset is not in the repo.

## US-P8a: A bulletin becomes a tracked claim

- **AC-P8.1:** A `CnamClaim` aggregate records the clinic, the patient, the originating BS1 document, the acts
  claimed with their coefficients and lettres-clés, the amount claimed, the care date, and a status.
- **AC-P8.2:** The status lifecycle is declared and enforced as a transition table — not a bare assignment. § 6.10 in
  this same spec is `LabWorkOrder.SetStatus` being exactly that mistake.
- **AC-P8.3:** A claim is created from an existing BS1 document, carrying its acts across rather than retyping them.
- **AC-P8.4:** Creating a claim does not modify the `MedicalDocument`. `cnam-bulletin-soins` locked in no schema
  change there, and that holds.
- **AC-P8.5:** The amount claimed is computed by the **backend** calculator — the same one AC-P6.15 stops the
  frontend duplicating. The estimate and the claim must not be able to disagree.
- **AC-P8.6:** A claim is clinic-scoped, carries the global query filter, and is pinned by a `*TenantIsolationTests`
  case like every other clinic-scoped aggregate.
- **AC-P8.7:** `DentalActCode.RequiresAccordPrealable` is finally read — an act requiring accord préalable is flagged
  on the claim, and the clinic can record the accord's reference and date.
- **AC-P8.8:** A claim can be cancelled with a motif before submission, and cancelling is audited (P7).

## US-P8b: Claims are batched onto a bordereau

- **AC-P8.9:** A `CnamBordereau` aggregate records the clinic, a per-clinic sequential number, the period, its
  status, and its lines.
- **AC-P8.10:** Bordereau numbers use the same per-clinic filtered-unique-index pattern as invoice and devis numbers,
  and take their year from the **clinic-local** date — AC-P6.7's defect must not be reintroduced in a new sequence.
- **AC-P8.11:** An operator selects unsubmitted claims and batches them. A claim can be on at most one non-cancelled
  bordereau. **The enforcement mechanism is named**, because a partial unique index cannot do it: the cancelled flag
  lives on the parent `CnamBordereau` while the uniqueness is over `CnamBordereauLine.ClaimId`, so it needs either a
  denormalised line status kept in sync on cancel, or a trigger. An unenforceable constraint is not a constraint.
- **AC-P8.12:** Finalizing a bordereau freezes its lines and moves every claim to submitted.
- **AC-P8.13:** **A bordereau can be cancelled with a motif before it is submitted to CNAM**, returning its claims
  to unsubmitted and free to be re-batched. Batching is a bulk month-end action; without this, one mis-click
  permanently strands every claim on it. The cancellation is a declared transition in AC-P8.2's table and is audited.
- **AC-P8.14:** AC-P8.22's resubmission path is reconciled with AC-P8.11 — a rejected claim resubmitted onto a new
  bordereau while the original remains *submitted* rather than cancelled would violate the constraint as stated.
  Whether resubmission creates a new claim or amends the original is **Q-2**, and it must be answered before P8.
- **AC-P8.15:** A finalized bordereau is printable. Until the official AP1/AP2 asset exists this is a clinic-designed
  printable that carries every required field.
- **AC-P8.16:** When the AP1/AP2 PDF is supplied, a calibrated overlay renderer is added following
  `CnamBs1BulletinRenderer`, and **no data model changes** — that is what makes it a drop-in rather than a rewrite.
- **AC-P8.17:** The printable states plainly that it is not the official form until AC-P8.16 lands, so nobody submits
  a substitute believing it is accepted.

## US-P8c: What CNAM actually paid is recorded and reconciled

- **AC-P8.18:** A `CnamReimbursement` aggregate records the clinic, the bordereau, the claim, the amount received,
  the date, the payment reference, and free-text notes.
- **AC-P8.19:** It is **not** a `Payment` and does not appear in the caisse or the dashboard « encaissé ».
  `PaymentMethod` is unchanged.
- **AC-P8.20:** A reimbursement can be recorded per claim or spread across a bordereau's claims, and the allocation
  is explicit — never inferred.
- **AC-P8.21:** A partial payment is a first-class case, not an error. Claimed, received and outstanding are three
  separate figures.
- **AC-P8.22:** A rejection is recorded with its motif against the claim, and a rejected claim can be corrected and
  resubmitted onto a new bordereau, keeping a link to the original.
- **AC-P8.23:** A reconciliation screen shows, per bordereau, claimed vs received vs rejected vs outstanding, with
  per-claim detail.
- **AC-P8.24:** The patient record shows the patient's CNAM position — estimated, claimed, received — replacing the
  single indicative figure `PatientBillingSummaryDto.CnamReimbursable` shows today.
- **AC-P8.25:** The word « estimation » is used for the estimate and never for a claimed or received figure. Conflating
  them is the defect § 6.5 describes.
- **AC-P8.26:** Recording a reimbursement does **not** alter the patient's invoice balance. If the clinic wants the
  patient's balance reduced, that is a separate, deliberate action — not a side effect.
- **AC-P8.27:** Claims, bordereaux and reimbursements are all audited by P7.
- **AC-P8.28:** A claim belongs to exactly one patient and is never orphaned by an archive or an anonymization — an
  anonymized patient's claims keep their amounts and their bordereau links, matching AC-P7.34's treatment of invoices.
- **AC-P8.29:** All new realtime keys are declared in `clinic-hub.ts` and pass AC-P4.23's exact-set contract test —
  which will fail the build until they are, by design.
- **AC-P8.30:** Every new controller action is classified by the authorization coverage tests, which fail the build
  until it is.

---

## API Contract

Existing endpoints reached for the first time — **no contract change**, they only gain a caller:

| Endpoint | Part | Note |
|---|:--:|---|
| `GET`/`PUT /api/doctors/{id}/working-hours` | P1 | already `AdminOrOwn`-guarded in the handler |
| `POST /api/treatment-plans/{id}/amend` | P2 | `AdminOrDoctor` |
| `PUT /api/treatment-plans/{id}/installments` | P2 | `AdminOrDoctor` |
| `POST /api/treatment-plans/{id}/items/{itemId}/done` | P2 | **gains** a role policy (A-13) |
| `DELETE /api/patients/{patientId}/dental-records/{id}` | P2 | **gains** a role policy (A-12) |
| `DELETE /api/medical-documents/{id}` | P2 | **gains** a role policy (A-12) |
| `PUT /api/doctors/{id}` | P2 | needs a client function — none exists |
| `GET /api/procedure-types/colors` | P2 | returns bare hexes, no names (A-14) |
| `GET /api/cnam-nomenclature/reimbursement-estimate` | P6 | replaces the client-side duplicate |

New endpoints:

| Endpoint | Part | Policy |
|---|:--:|---|
| un-mark a plan act as réalisé | P2 | AdminOrDoctor |
| `PUT /api/users/{id}/role` | P2 | AdminOnly (inherited from the class attribute) |
| disconnect Google Calendar | P2 | AdminOnly |
| audit trail reads — per-entity and clinic-wide | P7 | per-entity: authenticated; clinic-wide: AdminOnly |
| patient merge (preview + commit) | P7 | AdminOnly |
| patient anonymize | P7 | AdminOnly |
| CNAM claim CRUD + cancel | P8 | AdminOrDoctor |
| CNAM bordereau create / batch / finalize / print | P8 | AdminOnly |
| record reimbursement, record rejection, reconciliation read | P8 | AdminOnly |

Changed contracts:

| Change | Part |
|---|:--:|
| Appointment status transitions return `Result.Failure` where they previously returned 200 — **this is the fix**, and any client relying on the silent success is relying on a lie | P1 |
| `POST /api/appointments` and the recurring endpoint gain an out-of-hours refusal, and an explicit admin override flag | P1 |
| `StockItemDto` gains expiry and batch | P4 |
| `ReminderStatusDto` gains patient name and appointment date; the phone stays masked | P3 |
| `PatientBillingSummaryDto`'s single indicative CNAM figure becomes estimated / claimed / received | P8 |

All failures use the canonical `{ "error": "…" }` body established by `graceful-error-handling`. New conflict paths
use the 409 § 1 established. No endpoint returns a raw `ex.Message`.

---

## Data / Schema Changes

| # | Change | Part |
|---|---|:--:|
| 1 | Appointment type moves out of the `Notes` prefix — column or reuse of `ProcedureTypeId` — **plus a data migration** | P1 |
| 2 | Exclusion constraint on `(DoctorId, [start, end))` for `Appointments`; needs a generated end column since `Duration` is `bigint` ticks | P1 |
| 3 | `Appointment` gains an out-of-hours override marker | P1 |
| 4 | **Per-batch stock rows** — expiry and batch move off `StockItem`'s two overwritten scalar columns onto a child, plus a migration folding existing values into one opening batch | P4 |
| 4b | **Act material list** — an act (`ProcedureType` / `DentalActCode`) → stock item + quantity join, per-clinic | P4 |
| 5 | `StockMovementType` gains a third member | P4 |
| 6 | `Notifications` gains a `(Status, ScheduledFor)` index | P4 |
| 7 | `StockMovement` gains a `ClinicId` index and an FK to `Clinic` | P4 |
| 8 | `StockItem.UnitPrice` `decimal(18,2)` → `(18,3)` | P4 |
| 9 | `AuditEntry` table + two indexes | P7 |
| 10 | `Patient` gains anonymization state | P7 |
| 11 | `CnamClaim`, `CnamBordereau`, `CnamBordereauLine`, `CnamReimbursement`, `CnamRejection` + a filtered unique index on the bordereau number | P8 |
| 12 | A denormalised line status (or trigger) so AC-P8.11's one-non-cancelled-bordereau-per-claim rule is actually enforceable — the cancelled flag is on the parent, the uniqueness is on the child | P8 |
| 13 | The `HavePrecision(18,3)` convention (AC-P4.37) produces an `AlterColumn` for **every** `decimal` in the schema. Its size is expected; `Clinic.VatRate` and `Invoice.VatRate` are annotated to stay `(5,2)` | P4 |

> ⚠️ **Migration hazards, both already recorded in this repo.**
> **(a)** `dotnet ef migrations add` silently emits an **empty** migration when the API is running. Stop the API
> first, read every generated file before committing, never pass `--no-build` (§ 1 plan R-3).
> **(b)** § 1's `AddConcurrencyToken` migration has a **deliberately empty `Up()`** because EF's differ emits
> 38 × `AddColumn<uint>("xmin")`, which PostgreSQL rejects. **Every new migration must be read for spurious `xmin`
> columns before committing.** The latest migration is `20260727174753_AddUserTokenVersion` plus § 1's; new ones
> must sort after them.

---

## Scope

### In Scope

- All 57 audit bullets in §§ 3–10, expanded so no finding hides inside a multi-part bullet.
- The 26 adjacent defects (A-1 … A-26) that would re-break, mask or block a listed finding.
- The four § 2 exception-leak residuals in files this spec edits (A-7 … A-10).
- § 6.4 at full scope: audit trail, patient merge, anonymize.
- § 6.5 at full scope: claims, bordereau, reconciliation — with the official AP1/AP2 overlay deferred to the asset.
- Corrections to `CODEBASE_AUDIT_2026-07.md` itself: the § 6 count (C-1) and the ICU claim (C-5).
- Backend tests for every backend AC; a documented manual walk for frontend ACs.

### Out of Scope

- **§ 2's remaining work.** The ~79 other `{ex.Message}` sites, the CSP flip to enforcing, the upload-form
  constraints and the never-created `ErrorMessageLeakGuardTests` belong to `features/security-hardening`. Only the
  four leaks in files this spec already edits are absorbed.
- **A full responsive pass.** P3 covers the navigation shell and the four named fixed-width offenders. The
  appointment calendar grid and the wide data tables are each substantial and are not attempted here.
- **An i18n framework.** There is none, and one is not being introduced. French stays hardcoded, via the existing
  co-located `*-labels.ts` map pattern.
- **A specialty data migration.** § 8.3 is fixed with a display-time map; storage keys stay English, following the
  existing `weekdayLabelsFr` precedent.
- **Electronic submission / télétransmission to CNAM.** P8 covers paper claims, batching and manual reconciliation.
  There is no CNAM API, and all three prior CNAM specs put télétransmission out of scope.
- **The calibrated AP1/AP2 overlay renderer.** Deferred to the asset (AC-P8.16). Everything it plugs into is built.
- **Frontend automated tests.** No runner exists. Standing one up is a prerequisite for FE coverage and is a
  feature of its own.
- **Reverting § 1 or § 2 decisions.** Where they deliberately chose a behaviour — `Invoice.Outstanding` ignoring
  credit notes, log-only catches still swallowing — that stands.

---

## Edge Cases

- **EC-1:** An appointment already `Completed` when P1 ships, whose status was reached through the old silent path.
  It must be cancellable and must render correctly. No backfill.
- **EC-2:** A clinic with no working hours configured anywhere. Booking is unrestricted and behaves exactly as today
  (AC-P1.30) — this is the majority case on day one and must not regress.
- **EC-3:** Existing working-hours JSON that fails the new validation. Reads as "not configured" and is reported,
  never throws on load (AC-P1.24).
- **EC-4:** An appointment that already sits outside the newly-enforced hours. It renders and can be edited; only
  new bookings are refused (AC-P1.34).
- **EC-5:** A note whose text legitimately begins with `Type: ` but was never written by the dialog. The migration
  must not eat it (AC-P1.49).
- **EC-6:** A plan act un-marked after its invoice was cancelled. Allowed — the guard is a non-cancelled invoice.
- **EC-7:** A dental record deleted while another user has the plan workspace open. The realtime broadcast refreshes
  them; the stale act must not be actionable.
- **EC-8:** An admin removing their own admin role as the only active admin — refused (AC-P2.26).
- **EC-9:** Disconnecting Google Calendar while a sync is in flight. The in-flight push fails cleanly; nothing in the
  clinic's Google account is deleted.
- **EC-10:** A recall sent when exactly one of two configured channels is down. Partial success must not report as
  full success, and must not snooze the patient for 30 days.
- **EC-11:** The drawer open on a viewport resized past `md:`. It must not leave the page in a stuck state.
- **EC-12:** A stock item whose two batches have different expiry dates (AC-P4.4).
- **EC-13:** Retention purging reminder rows while the dispatch job is mid-batch (AC-P4.34).
- **EC-14:** A payment recorded at 00:30 Tunis on the 1st of a month — the caisse, the dashboard and the payment
  modal's default date must all agree on which day and month it belongs to.
- **EC-15:** An invoice issued at 00:30 Tunis on 1 January — numbered into the new year (AC-P6.7).
- **EC-16:** Two people in the same clinic genuinely share a name and date of birth. AC-P7.19's warning is
  non-blocking precisely so this is possible; a hard block would be defeated by misspelling the name.
- **EC-17:** A duplicate of an **archived** patient. AC-P7.23 surfaces the archived match so the operator un-archives
  rather than creating a third record.
- **EC-18:** Anonymizing a patient with a non-cancelled invoice — the accounting record survives intact (AC-P7.34).
- **EC-19:** A bordereau finalized while one of its claims was cancelled in another session. Optimistic concurrency
  from § 1 covers the row; the batch must fail as a whole, not partially.
- **EC-20:** A CNAM reimbursement arriving for a claim on a cancelled bordereau.
- **EC-21:** A reimbursement exceeding the amount claimed — recorded and flagged, never silently clamped.
- **EC-22:** Enabling `TreatWarningsAsErrors` (AC-P5.11) while another branch is mid-flight. It must be the **last**
  step of P5, after the count reaches zero.
- **EC-23:** A slot cancelled and then re-booked — the single most common scheduling action. The partial exclusion
  constraint (AC-P1.16) must permit it, and AC-P1.22's pre-flight must not count it as a pre-existing violation.
- **EC-24:** A recurring series where one occurrence violates the constraint. The series must still create the
  others and report the one skipped (AC-P1.19) — today all occurrences share a single `SaveChangesAsync`, so without
  a savepoint the whole series aborts.
- **EC-25:** The dentist types a Sunday appointment straight into Google Calendar and it syncs inbound. Per AC-P1.29
  it must not be silently dropped by the working-hours guard into a swallowed catch.
- **EC-26:** A fiche de soins saved against an appointment that was cancelled after the visit. AC-P1.12 must still
  cancel the post-visit review, or the review notification prompts forever.
- **EC-27:** An audit write fails while a dentist is saving a fiche. Per AC-P7.6 the save fails with a French
  message — a clinician must be able to tell that their work did not save, and retry.
- **EC-28:** A patient is anonymized while one of their invoices is queued for TTN dispatch. Per AC-P7.31
  this is refused, or the buyer name was snapshotted at queue time — a filed fiscal document must not acquire a
  placeholder or a stranger's name.
- **EC-29:** A bordereau finalized with the wrong claims at month end. AC-P8.13 must let it be cancelled and its
  claims re-batched; otherwise one mis-click strands them permanently.
- **EC-30:** A stock item holding two batches with different expiry dates, consumed down past the first. AC-P4.4's
  FEFO order determines which date the alert and the highlight use.
- **EC-31:** An act's material list references a stock item that was later deleted. Recording the visit must not
  fail (AC-P4.13).
- **EC-32:** The duplicate-match lookup times out. Per AC-P7.24 the form still submits — an operator must never be
  unable to create a patient because a suggestion query was slow.

---

## Verification & Tests

Written into this spec rather than deferred, so the test work is scoped with the change.

| Area | Test type | What it pins |
|---|---|---|
| Appointment status machine | Domain + handler | Every legal and illegal transition, including A-1 and A-2 |
| Double-booking | Handler + DB constraint | Concurrent insert yields one row; A-3's long-prior appointment is detected |
| Working hours | Domain + handler | A-5's invalid JSON is refused; resolution order; the no-hours-configured no-op |
| `Type: ` migration | Migration test | Idempotent; a legitimate `Type: ` note is untouched |
| Status labels | — | No automated FE test exists; covered by the manual walk |
| Plan amend / revise / un-mark | Handler | Every existing `Result.Failure`; un-mark reopens an auto-completed plan |
| Dental-record delete cleanup | Handler | Both soft links cleared in one transaction (AC-P2.15) |
| Role change | Handler | A-11's null-wipe; the closed role set; self-lockout; `TokenVersion` bump |
| Recall no-op | Handler | AC-P3.2 — no channel means failure, no snooze, no contacted stamp |
| Realtime contract | **Reflection guard** | AC-P4.23/4.24 — exact set, both directions, parsing `clinic-hub.ts` |
| Query filters | Tenant isolation | New `*TenantIsolationTests` cases for `Doctor`, `StockItem`, `AuditEntry`, the CNAM aggregates |
| Stock ledger | Handler | Σ movements reconciles; the manual-adjustment type; `Reason` populated |
| Decimal convention | Model test | AC-P4.37 — every `decimal` property resolves to `(18,3)` unless annotated |
| Timezone | Unit | Local-day boundaries; **fixed year**, never `UtcNow.Year` (AC-P6.9) |
| Money reads | `MoneyReadConsistencyTests` | Stays green; `Wire()` mirrors any repository filter change |
| Audit trail | Handler + integration | Same-transaction commit; synthetic actor for non-HTTP writers; collection-diff shape |
| Duplicate detection | Handler | Transliteration variance (AC-P7.22); archived included (AC-P7.23); clinic-scoped (AC-P7.25); non-blocking (AC-P7.19) |
| CNAM | Domain + handler | Claim transition table; one-bordereau constraint; partial payment; over-payment flag |
| Authorization | Coverage guards | Every new action classified — these fail the build until it is |

**Conventions:** xUnit + Moq, no database, no FluentAssertions, `Pascal_Snake_Case` names, a class-level `<summary>`
and a per-test `// [AC-n]` comment, deterministic GUIDs (`aaaa…`/`bbbb…`) and fixed UTC dates.

### Quality gate (every part)

| Check | Command | Requirement |
|---|---|---|
| Backend build | `dotnet build api/ClinicManagement.sln --no-incremental` | 0 errors; **0 new** warnings in changed files |
| Frontend types | `cd web && npx tsc --noEmit` | 0 errors |
| Frontend build | `cd web && npm run build` | clean — expect 27/27 static pages |
| Tests | `dotnet build -p:OutDir=<scratch>/utbuild/` then `dotnet vstest` | pass |
| Lint | `cd web && npm run lint` | **from P5 onward only** — it cannot run before AC-P5.1 |

**Baselines to measure against, re-verified before asserting:** backend warnings **58** at the § 2 merge (the § 1
worktree reported 116 from a different base — re-measure). Suite **941 passed / 3 failed**; the 3 are
`ReminderSchedulerTests`, pure-Moq and unrelated.

**Environmental caveats:** `dotnet test` fails at assembly load with `0x800711C7` (Windows Smart App Control) — use
the `build -p:OutDir` + `vstest` path. **Never use `--no-build` after changing production code** — it runs stale DLLs
and produced a false negative during § 2. `MSB3021`/`MSB3027` are file locks from a running API, not compile errors.
The PDF-render tests share process-wide QuestPDF state and are order-sensitive — judge against repeated runs.
`packaging/` is operator-verified only and out of reach of every gate.

---

## Non-Functional Hints

- **Performance:** § 9.3, § 9.6 and § 9.7 are the performance items. The audit trail (P7) adds a row per mutation —
  its index and retention story are load-bearing, not polish.
- **Security:** three endpoints gain role policies they should always have had (A-12, A-13). The audit trail must
  never record a value the reader was not already authorised to see (AC-P7.8).
- **Accessibility:** keyboard operability and label association for the surfaces named in § 7.8. No wider WCAG audit.
- **Offline/Local mode:** every change must behave in both `Auth:Mode` values. A-24 and A-25 are Local-only defects.

## Dependencies

- **Blocking:** none. § 1 and § 2 are both merged into the base.
- **External input required:** the official **AP1/AP2 PDF** for AC-P8.16, and domain confirmation of CNAM
  reconciliation rules — partial payments, rejection motifs, resubmission, accord préalable, AP1 vs AP2. P8's data
  model is built without them; its official printable is not.
- **Coordination:** `CODEBASE_AUDIT_2026-07.md` is now tracked. § 1's spec also planned to rewrite four `CLAUDE.md`
  files, `packaging/README.md` and the audit — check what it already did before editing.

## Risks

| ID | Risk | L | I | Part | Mitigation |
|---|---|:-:|:-:|:--:|---|
| **R-1** | **One story is oversized.** 57 bullets, 26 adjacent defects, 8 subsystems, ~11 migrations — chosen deliberately by the user. Context exhaustion mid-story could leave a half-applied change | High | High | all | Parts are ordered and each ends committable. **Split at a part boundary** if a session runs long. Never leave a part half-applied, and never stop mid-part in P1 (the `Type:` migration), P4 (the precision change) or P7 |
| **R-2** | **§ 6.4 is still a feature inside a feature**, even after merge was dropped. Audit trail + duplicate prevention + anonymize is ~1 entity, 2 migrations, 4+ endpoints, 2 UI surfaces and 38 touched handlers | High | High | P7 | Sequenced last but one. P7a first is a hard rule. If it must be cut further, the trail alone is the defensible core |
| **R-3** | **§ 6.5 at full scope is larger still, and needs input the repo does not have** | High | High | P8 | The AP1/AP2 overlay is explicitly deferred to the asset. Everything else is buildable. Domain questions are listed in Open Questions and must be answered before P8 starts |
| **R-4** | **The exclusion constraint (AC-P1.15) may reject data that exists today.** Any pre-existing double-booking blocks the migration | Med | High | P1 | Detect and report overlapping pairs **before** adding the constraint; the migration must fail loud with the offending ids, never silently drop rows |
| **R-5** | **The `Type:` data migration is irreversible** and touches free text users wrote | Med | High | P1 | Idempotent, reports row counts, leaves non-matching notes untouched, and is verified against a restored backup before it runs on real data |
| **R-6** | **Enforcing working hours could stop a clinic booking** | Med | High | P1 | AC-P1.30's no-hours-configured no-op is the safety valve, plus AC-P1.31's admin override |
| **R-7** | **`TreatWarningsAsErrors` breaks other branches** | Med | Low | P5 | Last step of P5, after the count is zero. Announce it |
| **R-8** | **No CI means every gate is manual**, so this spec's own tests may never run again after it lands | High | Med | P5 | AC-P5.4 adds CI. If declined (Q-9), AC-P5.4 moves to Out of Scope in the same edit — it is not satisfiable by writing a sentence |
| **R-9** | **Frontend ACs have no automated verification** — P3 is almost entirely FE | High | Med | P3 | A documented manual walk (AC-P3.48) recorded in `progress.md`. Not equivalent to tests; stated plainly rather than papered over |
| **R-10** | **Scope creep.** A 57-finding spec invites "while we're here" | High | Med | all | The Out of Scope section is the boundary. Anything new goes to `follow-up/`, not into this story — the rule § 1 recorded as its own R-15 |

## Open Questions

Answered during requirements gathering:

- ✅ § 6.4 and § 6.5 are in scope at **full breadth**.
- ✅ The § 1 gate on §§ 4.1/4.2/6.2/6.8 is **removed** — § 1 merged.
- ✅ Branch is `feature/audit-sections-3-to-10` off `feature/windows-desktop-app`, PR back into it.
- ✅ § 7.1 covers the nav drawer and the four named offenders, not a full responsive pass.
- ✅ A CNAM reimbursement is **its own entity**, not a `Payment`; `PaymentMethod` is unchanged.
- ✅ The bordereau data model is built now; the calibrated AP1/AP2 overlay lands when the asset is supplied.
- ✅ The audit trail is **one `AuditEntry` table**, not columns on `Entity<TId>`.
- ✅ Patient merge is **pick-survivor with a per-field override screen**; the merged record is archived.

Still open — **P8 cannot start until these are answered:**

- **Q-1:** What are CNAM's actual rejection motifs, and is there a code list to model?
- **Q-2:** What are the resubmission rules after a rejection — new claim, or amended original?
- **Q-3:** Does the clinic operate tiers-payant, and if so does that change who the invoice bills?
- **Q-4:** What is the accord-préalable workflow — is it a request the clinic files, or a note on the claim?
- **Q-5:** AP1 vs AP2 — when is each used, and are they different forms or one form with a mode?
- **Q-6:** When CNAM reimburses, is the patient's balance expected to reduce automatically? AC-P8.26 assumes **no**.

**Resolved by approval — the spec was approved as written, so these take their in-spec default:**

- ✅ **Q-9 — CI is in scope.** AC-P5.4 stands. There is no CI in this repository at all, and without it the ~40 tests
  this spec adds will never run again after it lands. Reversing this means moving AC-P5.4 to Out of Scope explicitly.

Lower-priority, answerable in-flight:

- **Q-7:** Retention window for `AuditEntry` (P7) and for sent reminder rows (P4).
- **Q-8:** Expiry-alert lead time for stock (AC-P4.6).
- **Q-10:** Should `reminder-settings.tsx`'s « Phone Number ID » stay English as a Meta API field name (AC-P3.52)?

> **Approving this spec does not unblock P8.** Q-1 … Q-6 gate P8's *start*, not this approval — P8's data model is
> buildable without them, but its status transitions, its resubmission path (which as written would violate
> AC-P8.11) and its official printable are not. P1 … P7 are unaffected and can begin immediately.

## Documentation to update on completion

- Root `CLAUDE.md` — appointment status machine, working-hours enforcement, the audit trail, the CNAM claim
  subsystem, the realtime key contract.
- `api/ClinicManagement.Domain/CLAUDE.md` — new aggregates and the transition tables.
- `api/ClinicManagement.Application/CLAUDE.md` — new features and the clock helper.
- `api/ClinicManagement.UnitTests/CLAUDE.md` — the new guard tests; also correct the stale "~90 classes" (it is ~117)
  and the stale "references only Application".
- `web/CLAUDE.md` and `web/components/CLAUDE.md` — the responsive shell, the label-map convention, `Sheet`.
- `packaging/README.md` — pinned toolchain and checksums.
- `CODEBASE_AUDIT_2026-07.md` — tick every closed item; correct the § 6 index count (C-1) and the ICU claim (C-5).
