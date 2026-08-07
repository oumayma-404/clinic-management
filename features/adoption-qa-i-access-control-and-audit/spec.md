# Spec: Adoption QA — I (access control and audit)

**Status:** APPROVED
**Type:** Small (single-theme multi-item pass — wiring existing policies, plus the ledger that makes them checkable)
**Created:** 2026-08-03
**Scope:** Full
**Branch:** new, off `main`
**Feature:** Make the three dead authorization policies real across the 20 unpoliced controllers, so a secretary can run reception without reading the practice's revenue — and add the audit ledger that lets the owner answer « qui a fait ça ? » afterwards.

> **Why this is one feature, not several.** Every item below answers the same question — *who may do what, and who did it*. The 2026-08-03 adoption review found this is the single defect that makes the product unsellable to any practice with staff, and that the machinery to fix it **already exists and is simply not applied**. This is wiring, not design.

## Context — what the review confirmed

- At the attribute level: **33 bare `[Authorize]`, 32 `AdminOnly`, 11 `AdminOrDoctor`**. `DoctorOnly`, `SecretaryOnly` and `DoctorOrSecretary` are defined in `AuthorizationPolicies.cs` and have **zero** usages.
- They stayed green because `UnitTests/Common/Authorization/AuthorizationPoliciesTests.cs` only asserts the policies *exist*.
- **20 controllers carry no policy at all**, including `BillingController`, `DashboardController`, `ExpensesController`, `PatientsController` (delete/archive), `OdontogramController`, `PatientMedicalHistoryController`, `DentalRecordsController` (read/write), `PatientFilesController`, `StockController`.
- It is **not** a hidden-menu-with-live-API case: `web/lib/nav.ts` ships « Tableau de bord » and the whole « Finances » group to every role, and `caisse/page.tsx`, `creances/page.tsx`, `factures/page.tsx` contain no `role` reference.
- **No audit trail of any kind exists**: zero repo hits for `CreatedBy`, `ModifiedBy`, `DeletedBy`, `AuditLog`, `IAuditable`, `SaveChangesInterceptor`. `Entity<TId>` carries only `Id` and `Version`. The only attributable actions in the product are voiding a payment and voiding an installment — even an avoir records no actor.

## What Changes

### I1 — The role matrix, decided once and applied

The load-bearing distinction, and the reason this is not simply "lock the money down": **a secretary must be able to take a payment and see one patient's balance — that is reception's job — but must not see clinic-wide aggregates.** Per-patient money: yes. Clinic-wide money: no.

| Endpoint | Policy | Why |
|---|---|---|
| `GET /api/dashboard` | `AdminOrDoctor` | Its Argent section *is* clinic revenue |
| `GET /api/billing/caisse`, `/caisse/ledger` | `AdminOrDoctor` | Totals + the extrait |
| `GET /api/billing/receivables` | `AdminOrDoctor` | Clinic-wide debt |
| `GET /api/invoices/revenue` | `AdminOrDoctor` | — |
| `GET /api/patients/{id}/billing-summary` | `DoctorOrSecretary` | **Kept open** — reception cannot collect without it |
| `POST /api/invoices/{id}/payments` | `DoctorOrSecretary` | **Kept open** — reception takes the payment |
| `GET/POST/PUT /api/expenses` | `AdminOrDoctor` | An expense moves the reported Net |
| `DELETE /api/expenses/{id}` | `AdminOnly` | Deleting one silently raises Net |
| `DELETE /api/patients/{id}` | `AdminOnly` | Hard delete |
| `POST /api/patients/{id}/archive`, `/unarchive` | `AdminOrDoctor` | Removes a patient from every list |
| `POST/PUT /api/patients/{id}/dental-records` | `AdminOrDoctor` | Clinical authorship |
| `GET /api/patients/{id}/dental-records` | `AdminOrDoctor` | *Fork below* |
| Odontogram `diagnose` / `removeCondition` | `AdminOrDoctor` | Charting is a clinical act |
| `PatientMedicalHistory`, `PatientFamilyHistory` (all) | `AdminOrDoctor` | Clinical |
| `PatientFiles` upload/list/download | `DoctorOrSecretary` | Reception scans documents in |
| `PatientFiles` delete | `AdminOrDoctor` | — |
| `DELETE /api/stock/{id}` | `AdminOnly` | Wipes the article's whole history (see J-series note) |
| Appointments, WaitingList, Recall-contact, Patients create/read/update | `DoctorOrSecretary` | Reception's actual job |

*Fork on `GET` dental-records:* **Recommended `AdminOrDoctor`** — clinical notes are the most sensitive text in the product and reception can tell a visit was billed from `AppointmentDto.InvoiceId`, which already exists. Alternative: `DoctorOrSecretary` read-only if the clinic wants reception to see acts for billing. Pick one and state it in `progress.md`.

### I2 — The coverage test becomes derived, not a list

`ControllerAuthorizationCoverageTests` exists and pins the `[AllowAnonymous]` allow-list. Extend it — **derived, never a hand-maintained expectation table** (that is the failure mode `verify-schema` and `RealtimeResourceResolverTests` were both written to avoid, and the reason three policies rotted unnoticed):

- Reflect over every controller action. Assert **every** action either carries an explicit policy or is on the small, named `[AllowAnonymous]` allow-list. A new action with no policy must fail the build.
- Assert the set of *defined* policies equals the set of *applied* policies, in **both directions** — this is exactly what would have caught `DoctorOnly`/`SecretaryOnly`/`DoctorOrSecretary`.
- Replace the three assertions that merely check a policy exists.

### I3 — Client-side mirror (presentation only; the server is authoritative)

- `web/lib/nav.ts`: gate « Tableau de bord » and the whole « Finances » group (`/factures`, `/caisse`, `/creances`) on `role !== "secretary"`. `buildNavSections` already takes `isAdmin` — widen it to the role rather than adding a second parameter.
- The three finance pages get the same Lock-card treatment the catalog pages already use, so a bookmarked URL is not a blank crash.
- ⚠️ This is **not** the fix. It is the polish on top of I1. Do not implement I3 without I1.

### I4 — Retire the orphaned AI-summary endpoint

`GET /api/patients/{id}/ai-summary` has **zero callers** in `web/` (the button was removed) and still ships full name, allergies, medical history, every family-history entry, every dental record with teeth and money, and *every* free-text note to `router.huggingface.co` — with no record cap, no consent flag, no audit of which patient was sent, class-level `[Authorize]` only (so a secretary can call it), **no connectivity gate** and **no `HttpClient` timeout**, so on an offline LAN install it hangs ~205 s before failing.

**Recommended: delete it** — the endpoint, `GetPatientAiSummaryQuery`, `PatientAiSummaryDto`, `patientsApi.getAiSummary` and the root `CLAUDE.md` claim that it is "on the patient detail page … connectivity-gated", which is false on both halves.
*Fork:* keep it, in which case it needs **all** of: `AdminOrDoctor`, an `IInternetProbe` gate, an `HttpClient` timeout, a record cap, and a per-call audit row. Deleting is cheaper and loses nothing that has a caller.

### I5 — Self-registration no longer mints a live account

`POST /api/auth/register` is `[AllowAnonymous]`; the only secret is a **6-character** clinic code over a 36-symbol alphabet, and `User.CreateLocalUser` creates the account **active** — no invitation, no approval, no pending state. Anyone who learns the code (a departed employee, someone who saw the settings screen) gets a working account and reads every patient record.

- Create self-registered accounts **inactive/pending**; an admin activates from `/users` (the screen and `SetUserActiveCommand` already exist).
- Surface a pending count on `/users`.
- Mitigations already present and to be kept: `admin` is not self-assignable, and the code is rotatable.

### I6 — The audit ledger

A `SaveChangesInterceptor` writing one row per mutated aggregate: actor (`IClinicContext` user id + email), `ClinicId`, entity type, entity id, action (Insert|Update|Delete), UTC timestamp, and — for deletes and status changes — a compact changed-field summary. Not a full temporal table; the goal is answering « qui a supprimé ce patient ? », « qui a annulé cette facture ? », « qui a effacé cette dépense ? ».

- Read via `GET /api/audit` (`AdminOnly`), filterable by entity type + date, **paged** through the existing `PagedResult<T>`/`PageRequest` primitives.
- The interceptor must be **fail-safe in one direction only**: an audit write failure must not roll back the clinical/money operation (same contract as `INotificationGenerator`), but it *must* log at Error.
- Jobs and CLI verbs have no user in scope — write the actor as the job name rather than skipping the row.

## Data / Schema Changes

- New table `AuditEntries` — `Id`, `ClinicId`, `UserId?`, `UserEmail?`, `EntityType`, `EntityId`, `Action`, `ChangedFields?` (text), `OccurredAt`. Indexed `(ClinicId, OccurredAt)` and `(EntityType, EntityId)`.
- EF migration. ⚠️ Per `ef-migration-scaffolding-hazards`: scaffold with `-p:BaseOutputPath=…` (a running API locks `bin/Debug`), never `--no-build`, and commit the model snapshot in the same commit or the next migration duplicates this one.
- Extend `verify-schema` with the new table's index expectations — it diffs the **EF model** against the catalogue, so a configuration-declared index is verified for free.
- No column changes to existing entities. `Entity<TId>` is deliberately **not** given `CreatedBy`/`ModifiedBy`: 38 entities would each need a write-path obligation, and any writer that forgets it produces an unattributed row indistinguishable from a legitimate one. The interceptor cannot be forgotten.

## API Contract

### GET /api/audit  (I6)
Query: `entityType?`, `entityId?`, `from?`, `to?`, `page?`, `pageSize?`
Response 2XX: `PagedResult<AuditEntryDto>` (newest-first)
Errors: `401` unauthenticated · `403` not admin

### POST /api/auth/register  (I5) — behaviour change, same contract
Response 2XX: unchanged shape, but the created account is **inactive**; the French message states an admin must activate it.

### Removed  (I4)
`GET /api/patients/{id}/ai-summary` — 404 after this change. No frontend caller exists.

## Out of Scope

- Per-practitioner data scoping ("this dentist sees only their own patients"). `Invoice`, `DentalRecord`, `TreatmentPlan`, `Payment` and `Expense` carry **no** `DoctorId` at all, and `Appointment` holds the only FK to `Doctors` in the entire EF model — that is a schema change, not a policy change. Deferred deliberately.
- Field-level redaction (showing a secretary a masked total).
- Retroactive audit rows for history already written.
- Cloud-mode `FallbackPolicy` (Local already fails closed; changing Cloud's is a separate blast radius).
- Reworking the three roles into permissions/claims. The closed set of three is sufficient for a cabinet.

## Edge Cases (Critical only)

- **A clinic whose only account is the owner must not lock itself out.** `ChangeUserRoleCommand` already refuses a self-demotion leaving no active admin — I5's pending state must not create a second path to the same outcome (a first-run `setup` admin is never pending).
- **Reception must still be able to take money after I1.** If `POST /api/invoices/{id}/payments` or `billing-summary` is gated to `AdminOrDoctor` by accident, the product becomes unusable at the front desk — this is the one row of the matrix to verify by hand.
- I2's both-directions assertion must run against the **compiled** attribute set, not source text, or a policy applied via a base class is missed.
- The interceptor must capture the actor **before** `SaveChangesAsync` resolves, and must not itself trigger a nested save.
- An audit row for a `Delete` must record the entity id even though the entity is gone from the change tracker afterwards.
- `TokenVersion` is bumped on role change; a secretary promoted mid-session must not keep the old policy for the token's lifetime (already handled — assert it).

## Testing

- Extend `ControllerAuthorizationCoverageTests` per I2 (the derived both-directions assertion is the highest-value test in this spec).
- New: audit-interceptor tests — one row per mutation, actor captured, failure swallowed but logged, job actor named.
- New: `SelfRegistrationCreatesInactiveAccountTests`.
- ⚠️ Per `smart-app-control-blocks-tests`: `dotnet test` fails at assembly load with `0x800711C7` on this machine (SAC is ON — environmental, not a defect). Write the tests; expect to verify them elsewhere or with SAC temporarily off.
- Frontend gate for I3: `npm run check:responsive` + `npx tsc --noEmit` + `npm run build`, then an eye pass at 320/390/820/1180/1440 px per `.claude/rules/frontend-web.md`.
