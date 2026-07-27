# Codebase Audit — Clinic Management

**Date:** 2026-07-27 · **Branch:** `feature/windows-desktop-app` @ `22b37a1`
**Scope:** `api/` (.NET 8), `web/` (Next.js 15), `desktop/`, `packaging/`
**Method:** parallel read-only exploration across 5 areas, every finding traced to source. Speculative items were dropped — everything below was read in the code and the highest-impact ones were re-verified by hand.

---

## Severity legend

| | Level | Meaning |
|:--:|---|---|
| 🔴 | **P0** | Data loss, money wrong, or PHI exposed. Fix before any real clinic uses this. |
| 🟠 | **P1** | User is actively misled — the UI reports success while nothing happened, or figures disagree between screens. |
| 🟡 | **P2** | Real defect or missing capability a clinic will hit in normal use. |
| 🟢 | **P3** | Polish, consistency, hygiene. |

## Index

| # | Category | 🔴 | 🟠 | 🟡 | 🟢 | Total |
|---|---|:--:|:--:|:--:|:--:|:--:|
| 1 | [Data loss & money correctness](#1-data-loss--money-correctness) | 6 | 2 | — | — | **8** |
| 2 | [Security](#2-security) | 5 | 4 | 3 | — | **12** |
| 3 | [Silent no-ops — the UI lies](#3-silent-no-ops--the-ui-lies) | — | 4 | — | — | **4** |
| 4 | [Timezone — Tunisia is UTC+1](#4-timezone--tunisia-is-utc1) | 1 | 1 | — | — | **2** |
| 5 | [Built but unreachable](#5-built-but-unreachable-backend-with-no-ui) | — | 1 | 8 | 3 | **12** |
| 6 | [Product gaps a real clinic hits](#6-product-gaps-a-real-clinic-hits) | — | 2 | 7 | — | **9** |
| 7 | [Frontend UX](#7-frontend-ux) | — | 2 | 6 | 1 | **9** |
| 8 | [French localization](#8-french-localization) | — | 1 | 4 | 1 | **6** |
| 9 | [Realtime, schema & performance](#9-realtime-schema--performance) | — | 1 | 6 | — | **7** |
| 10 | [Build & tooling](#10-build--tooling) | — | — | 2 | 3 | **5** |
| | **Total** | **12** | **18** | **36** | **8** | **74** |

---

## 1. Data loss & money correctness

> The eight items here either destroy records or produce a wrong number on a document a patient pays against.

- [ ] 🔴 **Deleting a patient hard-deletes their entire appointment history.**
  Two EF configurations declare the same relationship with opposite delete behavior. `AppointmentConfiguration` says `SetNull` (comment: "busy slots"), `PatientConfiguration` says `Cascade` — and `PatientConfiguration` wins under `ApplyConfigurationsFromAssembly`. The model snapshot confirms `Cascade` is what shipped, and `DeletePatientCommand` is live and reachable from the UI.
  `Infrastructure/Persistence/Configurations/PatientConfiguration.cs:125` · `AppointmentConfiguration.cs:72` · snapshot `Migrations/ApplicationDbContextModelSnapshot.cs:1977`
  → *Direction:* delete the duplicate `HasMany(p => p.Appointments)` block from `PatientConfiguration`.

- [ ] 🔴 **Any partial `PUT /api/appointments/{id}` silently wipes the procedure type, snapshot duration and colour.**
  An omitted `procedureTypeId` binds to `null`, `null != appointment.ProcedureTypeId` evaluates true, and the handler calls `SetProcedureType(null, null, null)`. The tri-state guard that was written for `treatmentPlanItemId` was never applied to this field. The cancel button in the edit dialog posts `{status:"cancelled"}` on its own — so cancelling an appointment also erases its act.
  `Application/Features/Appointments/Commands/UpdateAppointmentCommand.cs:198` · caller `web/components/edit-appointment-dialog.tsx:326`

- [ ] 🔴 **Devis→facture re-bills money the patient already paid.**
  The bridge seeds invoice lines at full `PlannedCost` with `AmountCollected` starting at 0, and `GetPatientBillingSummaryQuery` then drops the plan from the balance entirely once billed. A 1 000 DT plan with 600 DT already collected on its échéancier shows 1 000 DT owing (plus TVA + timbre) the moment the invoice is issued.
  `Application/Features/Invoices/Commands/CreateInvoiceFromTreatmentPlanCommand.cs:102` · de-dup at `Application/Features/Billing/Queries/GetPatientBillingSummaryQuery.cs:79`

- [ ] 🔴 **A recorded payment can never be corrected.**
  `Invoice` exposes `RecordPayment` but no void/remove/reverse. A treatment-plan installment payment has no avoir path at all. A mistyped amount is permanent with zero correction route.
  `Domain/Entities/Invoice.cs:195` · `Domain/Entities/TreatmentPlan.cs:243`

- [ ] 🔴 **Avoirs are write-only — once issued you can never see one again.**
  `CreateCreditNoteCommand` is the only code in the solution that touches `CreditNote`. There is no query, no list, no PDF, and no field on `InvoiceDto`. The clinic cannot retrieve the avoir's number, motif or amount, cannot hand it to the patient, and cannot tell that an invoice already has one.
  `Application/DTOs/InvoiceDto.cs:3` · endpoint `API/Controllers/InvoicesController.cs:182`

- [ ] 🔴 **Zero optimistic concurrency anywhere in the solution.**
  No `RowVersion`, no `IsConcurrencyToken`, no `DbUpdateConcurrencyException` in Domain, Application or Infrastructure. Two concurrent `POST /invoices/{id}/payments` both pass the over-payment guard and both insert a `Payment` row, while `AmountCollected` keeps only the last writer's value — `Outstanding` then disagrees permanently with the sum of payment rows. Same last-write-wins on patients, plans and invoices.
  `Domain/Entities/Invoice.cs:203` · `Application/Features/Patients/Commands/UpdatePatientCommand.cs:200`

- [ ] 🟠 **Installment revenue is booked into the wrong month.**
  The repository sums the *cumulative* `AmountPaid` filtered on `LastPaidOn`. An échéance paid 400 DT on 5 Jan and 600 DT on 3 Feb reports **0 DT** in January's caisse/dashboard and **1 000 DT** in February's.
  `Infrastructure/Repositories/TreatmentPlanRepository.cs:98`

- [ ] 🟠 **Every patient without contact details shares one fake identity.**
  The owned `Email`/`PhoneNumber` value-object columns are `IsRequired()`, so the create handler substitutes the literals `noemail@example.com` and `0000000000`. This poisons every email/phone-based lookup, dedupe and reminder path with a shared sentinel.
  `Application/Features/Patients/Commands/CreatePatientCommand.cs:101` · `Infrastructure/Persistence/Configurations/PatientConfiguration.cs:39`

---

## 2. Security

> Items 1–4 are specific to the offline-Windows installer and expose patient records and the JWT signing key to every local account on the clinic PC.

### Local Windows install — filesystem exposure

- [ ] 🔴 **The installer grants `BUILTIN\Users` Full Control over the entire PostgreSQL data directory — and never revokes it.**
  `icacls … /grant "*S-1-5-32-545:(OI)(CI)F"` is issued so de-privileged `initdb` can run. Every local non-admin account on the clinic server ends up with read/write over the whole cluster holding all patient records.
  `packaging/server/clinic-server.iss:261`

- [ ] 🔴 **`{app}\api\.local` is readable by every local user — including the HS256 JWT signing key.**
  The `[Dirs]` entry only *adds* a `service-modify` ACE under `{autopf}`; the inherited `Users: Read & Execute` survives. Any local user can read the per-install signing key and forge an admin token for any clinic.
  `packaging/server/clinic-server.iss:49` · key written at `Infrastructure/Auth/LocalAuthConfig.cs:117`

- [ ] 🔴 **`{app}\api\Files` — every uploaded radiograph and scan — is readable by every local user.** Same add-only ACE problem.
  `packaging/server/clinic-server.iss:50`

- [ ] 🔴 **Plaintext DB passwords land under Program Files with inherited `Users: Read`.**
  `appsettings.Production.json` carries the connection-string password; `.local\db-credentials` carries both the `clinic_user` **and** the `postgres` superuser passwords.
  `packaging/server/clinic-server.iss:223` · `:187`

### Auth & session

- [ ] 🔴 **No rate limiting anywhere — an unauthenticated LAN client can lock out the entire clinic.**
  `AddRateLimiter`/`UseRateLimiter` are absent from `Program.cs`. Login relies solely on a per-account 5-attempt / 15-minute lockout, so anyone on the LAN can permanently lock out every staff account including the admin.
  `api/ClinicManagement.API/Program.cs:116` · `Domain/Entities/User.cs:8`

- [ ] 🟠 **The BFF hands the raw 12-hour JWT to browser JavaScript**, so the HttpOnly `local_session` cookie buys no XSS protection. There is no `jti` denylist, no refresh, and no token version — a stolen token stays valid for its full lifetime, and a *voluntary* password change does not revoke it (the enforcement middleware only revokes deactivated / must-change accounts).
  `web/app/bff/auth/token/route.ts:16`

### Authorization gaps — all reachable by a plain `secretary`

- [ ] 🟠 **`PUT /api/clinics` has no role policy** — any authenticated user can change the clinic's legal billing settings (matricule fiscal, TVA applicable + rate, timbre fiscal). These values are frozen onto every invoice issued afterwards. The handler admin-gates *only* the TTN toggle.
  `API/Controllers/ClinicsController.cs:198` · handler gate `Application/Features/Clinics/Commands/UpdateClinicCommand.cs:87`

- [ ] 🟠 **Three more unguarded admin surfaces:**
  · `PUT /api/clinics/doctors` — rewrite the practitioner roster — `ClinicsController.cs:177`
  · procedure-type catalog writes incl. prices + `initialize-defaults` — `ProcedureTypesController.cs:61` *(the CNAM / dental-act / medication catalogs are all correctly `AdminOnly`; this one was missed)*
  · `PUT /api/patients/recalls/settings` — clinic-wide recall interval, despite a doc comment claiming "Admin-editable" — `RecallController.cs:43`

- [ ] 🟠 **Any staff member can rewrite any practitioner's schedule** — `SetDoctorWorkingHoursCommand` checks same-clinic only, not own-or-admin. Its sibling `UpdateDoctorProfileCommand` *does* check.
  `Application/Features/Doctors/Commands/SetDoctorWorkingHoursCommand.cs:44` · cf. `UpdateDoctorProfileCommand.cs:93`

- [ ] 🟡 **Catalog mutators skip the per-handler `ClinicId` check entirely**, relying on the fail-open EF query filter. A token minted without a `clinic_id` claim (the Auth0 `app_metadata` push is best-effort and swallowed) makes the filter inactive and lets an admin edit another clinic's catalog rows by id.
  `Features/DentalActs/Commands/DeactivateDentalActCommand.cs:35` · `Features/CnamNomenclature/Commands/UpdateCnamEntryCommand.cs:42` · plus `UpdateDentalActCommand`, `{Update,Deactivate}MedicationCommand`

### Config, uploads, headers

- [ ] 🟠 **`MinIO:AccessKey`/`SecretKey` are still committed as `minioadmin`/`minioadmin`,** and the DI check treats "non-empty" as configured — so a Cloud deploy that forgets the env vars silently authenticates with default credentials instead of failing loud like every other scrubbed secret.
  `api/ClinicManagement.API/appsettings.json:57` · `Infrastructure/Extensions.cs:102`

- [ ] 🟡 **Patient-file upload accepts any client-declared content type** — no allow-list, no magic-byte check, no size cap — and echoes it verbatim on download. The doctor-cachet path does all three (PNG/JPEG allow-list, magic bytes, 2 MB cap), so the safe pattern already exists in the repo.
  `Application/Features/Files/Commands/UploadPatientFileCommand.cs:93` · cf. `Features/Doctors/Commands/UpdateDoctorProfileCommand.cs:128`

- [ ] 🟡 **No global security response headers** — no HSTS, CSP, `X-Content-Type-Options` or `X-Frame-Options`. The only `nosniff` in the codebase is set inline on the single cachet endpoint.
  `api/ClinicManagement.API/Program.cs` · `API/Controllers/DoctorsController.cs:61`

- [ ] 🟡 **Raw exception messages are interpolated into client-facing failures** and returned verbatim as the 400 body by `ApiControllerBase.HandleFailure`.
  `CreateAppointmentCommand.cs:240` · `UpdateAppointmentCommand.cs:463` · `DeletePatientCommand.cs:61` · `DeleteDentalRecordCommand.cs:68` · `GetDashboardStatsQuery.cs:126` · `MarkNotificationReadCommand.cs:71`

> **Verified clean** (do not re-investigate): all 30 controllers carry class-level `[Authorize]`; the only `[AllowAnonymous]` actions are `Auth.{mode,login,setup,register}`, `Connectivity.Get`, `GoogleCalendar.Callback`. Setup is loopback-gated *and* closes once any user exists. Google OAuth uses a 32-byte state with server cache + HttpOnly double-submit. Hangfire is loopback-only in both modes. `LocalDiskFileStorage.ResolveWithinBase` blocks traversal. No raw or interpolated SQL anywhere. No token in `localStorage`. BFF cookies are HttpOnly + SameSite=Lax with `AUTH_COOKIE_SECURE=true` set by the installer. Passwords use PBKDF2 via `PasswordHasher<User>` (Identity 8.0 — 100k iterations, SHA-512).

---

## 3. Silent no-ops — the UI lies

> Every item here returns HTTP 200 and shows a success toast while the operation did not happen. These erode trust in the whole app faster than a visible error would.

- [ ] 🟠 **« Terminé » in the status dropdown does nothing from Planifié or Confirmé.**
  The handler applies `Completed` only when the current status is `InProgress` — the other cases fall through the `switch` and the endpoint still returns 200 Success.
  `Application/Features/Appointments/Commands/UpdateAppointmentCommand.cs:249` · UI offers it unconditionally at `web/components/edit-appointment-dialog.tsx:674`

- [ ] 🟠 **« Cancel Appointment » is enabled on a Completed appointment but does nothing.**
  The button is disabled only when already `cancelled`, while the `Cancelled` branch skips `Completed`. The confirm dialog reports success and nothing changes. A visit auto-completed by saving a fiche de soins (`Appointment.MarkVisitCompleted`) is therefore a terminal state with no exit.
  `UpdateAppointmentCommand.cs:256` · `web/components/edit-appointment-dialog.tsx:703` · `Domain/Entities/Appointment.cs:100`

- [ ] 🟠 **« Rappel envoyé à … » toasts even when no channel is configured.**
  `SendRecallCommand` marks the patient contacted and snoozes them 30 days regardless, while `ReminderScheduler.ScheduleRecallAsync` silently returns early on `EnabledChannels.Count == 0`. The patient is now suppressed from the recall list for a month, having been sent nothing.
  `Application/Features/Recall/Commands/SendRecallCommand.cs:59` · `web/app/recalls/page.tsx:198`

- [ ] 🟠 **Double-booking is check-then-insert with no lock, transaction or unique constraint.**
  Two concurrent bookings for the same practitioner and slot both pass the overlap scan and both commit.
  `CreateAppointmentCommand.cs:157` · `UpdateAppointmentCommand.cs:322`

---

## 4. Timezone — Tunisia is UTC+1

> One systemic root cause, two visible consequences. Tunisia is UTC+1 year-round with no DST, so every UTC day boundary is one hour late.

- [ ] 🟠 **The caisse and the dashboard run "aujourd'hui" from 01:00 to 01:00 local.**
  Every payment recorded between local midnight and 01:00 is booked to the previous day — and, on the 1st, the previous month.
  `Application/Features/Billing/Queries/GetCaisseSummaryQuery.cs:59` · `Application/Features/Dashboard/Queries/GetDashboardStatsQuery.cs:73`

- [ ] 🔴 **Invoice and devis numbering take the year from `DateTime.UtcNow.Year`.**
  A note d'honoraires issued between 00:00 and 01:00 Tunisian time on 1 January is numbered into the *previous* fiscal year's sequence — a numbering break on a legal document.
  `Application/Features/Invoices/Commands/IssueInvoiceCommand.cs:72` · `Application/Features/TreatmentPlans/Commands/AcceptTreatmentPlanCommand.cs:61`

---

## 5. Built but unreachable (backend with no UI)

> Working, tested server-side capability that no frontend code path can trigger. Each is close to free to expose.

- [ ] 🟠 **Post-acceptance amendment of a devis is fully built and completely unreachable.**
  `treatmentPlansApi.amend` has zero callers, so `POST /treatment-plans/{id}/amend` is dead. An accepted devis can only be cancelled and retyped, losing its number. The workspace even renders a "révisions" badge for a feature the UI cannot trigger.
  `web/lib/api/treatment-plans.ts:119` · `API/Controllers/TreatmentPlansController.cs:98` · badge at `web/components/treatment-plans/plan-workspace.tsx:167`

- [ ] 🟡 **Installment schedules are frozen at acceptance** — `PUT /treatment-plans/{id}/installments` / `reviseInstallments` has no caller, so a patient who renegotiates payment terms cannot be accommodated.
  `web/lib/api/treatment-plans.ts:126` · `TreatmentPlansController.cs:108`

- [ ] 🟡 **No un-do for a plan act marked réalisé.**
  `TreatmentPlanItem.MarkDone` refuses re-linking and tells the user to "détachez-le de cette fiche" — but no detach or unmark operation exists in the domain, application, API or UI. Because marking an item done auto-completes the plan, one wrong fiche permanently closes a devis.
  `Domain/Entities/TreatmentPlanItem.cs:97` · `Domain/Entities/TreatmentPlan.cs:274`
  *(`markItemDone` — `web/lib/api/treatment-plans.ts:115` — is also uncalled; acts can only be marked réalisé as a side effect of saving a fiche.)*

- [ ] 🟡 **Per-dentist working hours have no UI at all** — `doctorsApi.getWorkingHours`/`setWorkingHours` are never called; only clinic-wide hours are editable.
  `web/lib/api/doctors.ts:38` · `API/Controllers/DoctorsController.cs:66,74` · clinic-wide form at `web/components/clinic-settings.tsx:923`

- [ ] 🟡 **Google Calendar can be connected but never disconnected** — `Clinic.ClearGoogleCalendarConnection()` has zero callers and `GoogleCalendarController` exposes no disconnect endpoint. A clinic that connects the wrong Google account is stuck.
  `Domain/Entities/Clinic.cs:171`

- [ ] 🟡 **No UI to delete or void a fiche de soins** — the endpoint exists, `dentalRecordsApi.delete` is never called. *(And if it were, it would leave `TreatmentPlanItem.LinkedDentalRecordId` and `InvoiceLine.DentalRecordId` dangling — these are deliberately FK-less.)*
  `API/Controllers/DentalRecordsController.cs:70` · `Domain/Entities/DentalRecord.cs:68`

- [ ] 🟡 **No UI to delete a medical document** — a wrong ordonnance stays in the patient's record permanently.
  `API/Controllers/MedicalDocumentsController.cs:224`

- [ ] 🟡 **No user role can ever be changed.** `User.Update(role, …)` has no caller and `UsersController` exposes only reset-password and activate/deactivate. A staff member who joined with the wrong role is stuck.
  `Domain/Entities/User.cs:75`

- [ ] 🟡 **User management is invisible in Cloud mode.** `UsersController` is not mode-gated, but the nav entry is `mode === "local" && isAdmin`, so an Auth0 clinic admin can never list or deactivate staff.
  `web/components/dashboard-sidebar.tsx:86`

- [ ] 🟢 **CNAM reimbursement math exists twice and will drift** — `GET /api/cnam-nomenclature/reimbursement-estimate` has no caller; the frontend reimplements the calculator client-side and only *mentions* the endpoint in a comment.
  `API/Controllers/CnamNomenclatureController.cs:51` · `web/lib/api/cnam-nomenclature.ts:53`

- [ ] 🟢 **`PUT /api/doctors/{id}` has no caller** — only `/doctors/me` is used, so an admin cannot fix another practitioner's CNOMDT number or cachet.
  `API/Controllers/DoctorsController.cs:42`

- [ ] 🟢 **`GET /api/procedure-types/colors` has no caller** — the palette is duplicated as a hardcoded array with a "must match backend" comment.
  `API/Controllers/ProcedureTypesController.cs:127` · `web/components/procedure-type-form-modal.tsx:30`

---

## 6. Product gaps a real clinic hits

- [ ] 🟠 **Working hours are advisory only.** The calendar is a flat 24-hour grid, and neither `CreateAppointmentCommand` nor `UpdateAppointmentCommand` validates against clinic or doctor hours. Booking 03:00 on a closed Sunday is accepted silently.
  `web/components/appointment-calendar.tsx:21`

- [ ] 🟠 **The dashboard and the caisse disagree after any refund** — the caisse nets avoirs out of cash-in, the dashboard's « encaissé » does not. Neither `GetPatientBillingSummaryQuery` nor `GetReceivablesQuery` reference credit notes at all.
  `GetCaisseSummaryQuery.cs:78` vs `GetDashboardStatsQuery.cs:98`

- [ ] 🟡 **Failed reminders are effectively invisible.** The only surface is admin-gated — so the secretary who books can't see it — buried in Settings, and shows a masked phone with no patient name or appointment: « •••• 56 — Échec ». No in-app notification is generated on `NotificationStatus.Failed`.
  `Application/Features/Clinics/Queries/GetClinicReminderStatusQuery.cs:59` · `Application/DTOs/ReminderStatusDto.cs:17`

- [ ] 🟡 **No audit trail of who changed what.** The only history in the system is `StockMovement`. Patient, appointment, invoice and treatment-plan mutations record no actor. No patient merge either, and patient delete is a hard delete with no archive or anonymize.
  `Infrastructure/Persistence/ApplicationDbContext.cs:40` · `Features/Patients/Commands/DeletePatientCommand.cs:49`

- [ ] 🟡 **The CNAM flow stops at an estimate.** `PatientBillingSummaryDto.CnamReimbursable` is explicitly "indicative"; there is no bordereau, no feuille de soins submission, no "CNAM reimbursed X on Y" entity, and no CNAM `PaymentMethod` (only Cash/Cheque/Card/Transfer). A clinic cannot reconcile what CNAM actually paid.
  `Application/DTOs/PatientBillingSummaryDto.cs:23` · `Domain/Enums/PaymentMethod.cs`

- [ ] 🟡 **Stock expiry tracking is unreachable end-to-end.** `ExpiryDate`/`BatchNumber` persist and `stockApi.restock` accepts them, but the restock dialog never sends them and `StockItemDto` never returns them — no expiry column, no alert, no way to read back what was entered.
  `Domain/Entities/StockItem.cs:17` · `web/components/stock-table.tsx:159` · `Application/DTOs/StockItemDto.cs:6`

- [ ] 🟡 **Stock is never consumed by performing an act.** No `StockItem` reference exists in any feature outside `Features/Stock/`, and neither `ProcedureType` nor `DentalActCode` links to a stock item. Consumption is 100% manual.
  `web/components/stock-table.tsx:156`

- [ ] 🟡 **The invoice↔appointment link is never populated.** `CreateInvoiceRequest.appointmentId` exists but no UI ever sets it — the form sends only `patientId`/`lines`/`dentalRecordId` — so "which visit does this facture bill?" is unanswerable.
  `web/lib/api/invoices.ts:32` · `web/components/factures/invoice-form-modal.tsx:190`

- [ ] 🟡 **Recurring series: conflicts are reported as a count only, and there is no edit path.** The backend returns the conflicting dates in `RecurringSeriesResultDto.Conflicts`, the UI renders `conflicts.length` — so skipped occurrences are unrecoverable.
  `web/app/recurring-series/page.tsx:163`
  *Related:* the series conflict scan excludes only `Cancelled`, not `NoShow` (unlike the single-appointment path), so a past no-show blocks an otherwise free slot — `CreateRecurringSeriesCommand.cs:182`

- [ ] 🟡 **`LabWorkOrder.SetStatus` is a bare assignment with no transition rules** — a `Fitted` order can be pushed back to `Sent`, and `ReceivedDate` is stamped only on the first `Received`, so a re-received order keeps a stale date.
  `Domain/Entities/LabWorkOrder.cs:92`

- [ ] 🟡 **Deleting a dental record orphans its soft links.** `InvoiceLine.DentalRecordId` and `TreatmentPlanItem.LinkedDentalRecordId` keep pointing at the deleted row — the plan act stays `Done`, linked to nothing.
  `Features/Patients/Commands/DeleteDentalRecordCommand.cs:61`

- [ ] 🟡 **`UpdateStockItemCommand` writes an absolute `CurrentStock` with no `StockMovement` row**, so the ledger written by consume/restock stops reconciling with on-hand, and a concurrent consume is silently overwritten.
  `Features/Stock/Commands/UpdateStockItemCommand.cs:77`

---

## 7. Frontend UX

- [ ] 🟠 **The app is unusable on a phone.** `dashboard-sidebar.tsx` contains **zero** responsive classes (no `sm:`/`md:`/`lg:` anywhere in the file), always renders, and defaults to expanded at `w-64` — on a 375 px viewport it consumes 256 px with no drawer and no auto-collapse. The header is equally breakpoint-free, and the AI assistant is a hard `w-96 h-[600px]` fixed panel, wider and taller than a small phone screen.
  `web/components/dashboard-sidebar.tsx:125` · `dashboard-header.tsx:141` · `ai-chat.tsx:676` · also `document-editor-content.tsx:1640` (fixed `w-[420px]` column)

- [ ] 🟠 **A session hiccup in Local mode dumps the user on a Next 404.** `ClinicGuard` hard-redirects to `/auth/login`, which does not exist as a page in Local mode (only `app/bff/auth/*`), and `returnTo=/auth/login` re-lands there after a successful sign-in.
  `web/components/clinic-guard.tsx:33`

- [ ] 🟡 **The patients list has no edit action.** `EditPatientDialog` is mounted in the table but `setSelectedPatient`/`setEditDialogOpen(true)` are never called anywhere — unreachable dead UI.
  `web/components/patients-table.tsx:296`

- [ ] 🟡 **The AI assistant speaks every reply aloud automatically**, with no persistent mute — the only control is an "Arrêter la lecture" button that exists solely while speech is in progress.
  `web/components/ai-chat.tsx:302`

- [ ] 🟡 **Six error paths swallowed to `console.error` with no toast and no retry:**
  · patient-files page renders the manager with no error state — `web/app/patients/[id]/files/page.tsx:33`
  · Factures revenue KPIs silently show "—" — `web/app/factures/page.tsx:35`
  · file download on the patient page fails silently *while the same action toasts in `patient-files-manager.tsx:243`* — `web/app/patients/[id]/page.tsx:498`
  · file preview just closes the dialog — `web/app/patients/[id]/page.tsx:470`
  · procedure-type load in the booking dialog, deliberately ("Don't show error to user"), leaving an unexplained empty « Sélectionner un type d'acte » — `web/components/create-appointment-dialog.tsx:280`

- [ ] 🟡 **« Créer le dossier » has no in-flight disabled state** — a double-click creates two folders.
  `web/components/patient-files-manager.tsx:602`

- [ ] 🟡 **`/patients` shows a blank screen instead of a skeleton** — the suspense boundary returns `null`.
  `web/app/patients/loading.tsx:1`

- [ ] 🟡 **Accessibility — the /documents gallery is entirely unreachable by keyboard.** Template tiles are `<Card onClick=…>` with no `role`, `tabIndex` or key handler; same for folder and file cards in the files manager.
  `web/app/documents/page.tsx:100` · `patient-files-manager.tsx:475,526`
  *Also:* date-picker and hour/minute `<Label>`s in both appointment dialogs have no `htmlFor` and their controls have no `id` (`create-appointment-dialog.tsx:733`); the per-file delete button is icon-only with no `aria-label` (`patient-files-manager.tsx:562`); the invoice cancellation reason has only a placeholder, no `<Label>` (`invoices-table.tsx:504`).

- [ ] 🟢 **Feedback pattern is inconsistent** — clinic settings uses a bespoke in-page banner with a 4 s timer while every other screen in the app uses `sonner` toasts.
  `web/components/clinic-settings.tsx:134`

---

## 8. French localization

- [ ] 🟠 **Raw English enum values are shown to users as appointment status** — "Scheduled", "Noshow", "Inprogress" — sometimes right next to a fully French status `Select`.
  `web/components/edit-appointment-dialog.tsx:355` · patient history `web/app/patients/[id]/page.tsx:1170` · dashboard `web/components/appointment-list.tsx:73`
  *Same class:* « Sexe » displays the stored "Male"/"Female"/"Other" while the edit form offers Homme/Femme/Autre — `web/app/patients/[id]/page.tsx:1438`

- [ ] 🟡 **English buttons and headings in production UI:** "Cancel Appointment" / "Close" (`edit-appointment-dialog.tsx:707`), "Date & Time" in both dialogs (`create-appointment-dialog.tsx:727`), "Cancel" / "Clear" / "Duration set to N minutes" (`create-appointment-dialog.tsx:1098,985,975`), "Loading clinic settings…" / "Add Doctor" / 3× "Cancel" (`clinic-settings.tsx:502,869,734`), most of `procedure-types-table.tsx:104,117,129`, "Back to Patient" (`web/app/patients/[id]/files/page.tsx:77`), "Push" (`appointment-calendar.tsx:116`), "file"/"files" pluralization (`patient-files-manager.tsx:486`).

- [ ] 🟡 **The doctor specialty catalog is English-only** ("Dentist", "Orthodontist", "Oral Surgeon"…) and is *persisted* then displayed verbatim in Mon profil and the appointment doctor picker.
  `clinic-settings.tsx:64` · `setup-wizard.tsx:23` · `join-wizard.tsx:16` · surfaced at `mon-profil-content.tsx:136`

- [ ] 🟡 **Appointment notes are persisted with the English prefix `Type: `** and displayed that way in the notes column — this is data, not just display.
  `web/components/create-appointment-dialog.tsx:426`

- [ ] 🟡 **Currency formatting bypasses `formatDT` in two places**, printing `150.000 DT` with a period among `150,000 DT` everywhere else — and one uses a `DollarSign` icon for dinars.
  `web/app/lab-orders/page.tsx:83` · `web/components/procedure-types-table.tsx:198`
  *Also:* dashboard counters use `toLocaleString()` with no locale, so grouping follows the browser (`web/app/page.tsx:23`); file sizes render "B/KB/MB" not "o/Ko/Mo" (`patient-files-manager.tsx:350`).

- [ ] 🟢 **Patient form placeholders are American, not Tunisian** — "John", "Doe", "john.doe@email.com", "123 Main Street", "Penicillin, Shellfish", "Blue Cross Blue Shield", "BCBS-123456789", "Group-12345".
  `web/components/edit-patient-dialog.tsx:571`

---

## 9. Realtime, schema & performance

- [ ] 🟠 **Five backend broadcast keys have no frontend subscriber — the salle d'attente never live-refreshes.**
  `RealtimeResourceResolver` derives keys from the command namespace, so the API emits `expenses`, `laborders`, `waitinglist`, `recall` and `doctors`, but `clinic-hub.ts` lists only 14 keys and none of these. `/waiting-list` — the canonical two-user screen — plus `/lab-orders`, `/caisse`, `/recalls`, `/creances` and the dashboard call no `useClinicRealtime` at all.
  `web/lib/realtime/clinic-hub.ts:14`
  **And the contract test won't catch it:** `RealtimeResourceResolverTests` pins only the 14 keys the frontend already has, so the five orphans are unpinned and stay silently broken — `api/ClinicManagement.UnitTests/Common/Behaviors/RealtimeResourceResolverTests.cs:35`

- [ ] 🟡 **`Doctor` and `StockItem` are clinic-owned but have no global query filter**, while `StockItem`'s own child `StockMovements` *is* filtered — an internally inconsistent pair. 17 other roots are filtered.
  `Infrastructure/Persistence/ApplicationDbContext.cs:97`
  *(Stale comment at `:79` claims filters apply only to Patient/Appointment/ProcedureType.)*

- [ ] 🟡 **The reminder-outbox hot query is unindexed and unbounded.** `Status == Pending && ScheduledFor <= now` runs every minute with no index (`Notifications` has only the auto FK indexes), and sent rows are never purged — a sequential scan over a table that grows forever.
  `Infrastructure/Repositories/NotificationRepository.cs:26` · `NotificationConfiguration.cs`

- [ ] 🟡 **`StockMovement.ClinicId` carries a global query filter but has no index** and no FK to `Clinic`, so the filter predicate appended to every read is unindexed.
  `Infrastructure/Persistence/Configurations/StockMovementConfiguration.cs:24`

- [ ] 🟡 **`StockItem.UnitPrice` is `decimal(18,2)` while every other money column is `(18,3)`** — the Tunisian millime is silently truncated on stock valuation.
  `Infrastructure/Persistence/Configurations/StockItemConfiguration.cs:56`

- [ ] 🟡 **The recall list loads every patient and every appointment the clinic has ever had** — no date bounds — and filters in memory.
  `Application/Features/Recall/Queries/GetPatientsToRecallQuery.cs:52`

- [ ] 🟡 **Two more query inefficiencies:** `GetReceivablesQuery` issues one `GetByIdAsync` per patient inside the merge loop (N+1) — `:87`; the "already billed" guard loads every invoice of the patient *with lines and payments* to test one `TreatmentPlanId`, when the light `GetTreatmentPlanLinksAsync` projection exists for exactly this — `CreateInvoiceFromTreatmentPlanCommand.cs:81`.

> **Verified clean:** both recurring Hangfire jobs are correctly hardened — `[DisableConcurrentExecution(600)]` + `[AutomaticRetry(3)]`, per-row try/catch so one bad row can't abort a batch, per-row commit, bounded reminder retries. No missing-migration gap: `CreditNotes`, `StockMovements`, `ToothStates`, `Expenses` all trace to concrete migrations, and invoice/credit-note/plan numbers plus per-clinic catalog codes all carry proper filtered unique indexes.

---

## 10. Build & tooling

- [ ] 🟡 **`npm run lint` is broken on a clean install and CI never notices.**
  `eslint.config.mjs` imports `eslint/config`, `eslint-config-next/core-web-vitals` and `eslint-config-next/typescript`, but neither `eslint` nor `eslint-config-next` is in `web/package.json` devDependencies → `ERR_MODULE_NOT_FOUND`. Masked because `next.config.ts:15` sets `eslint.ignoreDuringBuilds: true`.
  `web/eslint.config.mjs:1` · `web/package.json:63` · `web/next.config.ts:15`

- [ ] 🟡 **The packaging bootstrap pulls unpinned, unverified toolchain downloads.** `fetch-build-tools.ps1` probes EDB PostgreSQL 16.x URLs by trial-and-error and fetches `nssm-2.24.zip` with no checksum — a clean-machine build takes whatever currently responds. The vendored `build-output/_runtimes/postgres/bin` currently ships **ICU 67 DLLs (PG 13-era)**, not 16.
  `packaging/fetch-build-tools.ps1:39,69`

- [ ] 🟢 **`dotnet build` is error-clean** (the 6 `MSB3021`/`MSB3027` are file locks from the API already running, not code) but emits **46 × CS8618** uninitialized non-nullable properties, all in `ClinicManagement.Domain` — e.g. `ValueObjects/PhoneNumber.cs:9`, `Entities/User.cs:30`, `ValueObjects/Address.cs:13`.

- [ ] 🟢 **9 more nullable/obsolete warnings:** 6 × CS8602 possible-null deref in controllers (`MedicalDocumentsController.cs:112,160,200`, `AppointmentsController.cs:78`, `PatientsController.cs:73`, `ProcedureTypesController.cs:71`), 2 × CS8600, 1 × CS8604 (`Swagger/FileUploadOperationFilter.cs:77`), 1 × CS0618 obsolete Hangfire `UsePostgreSqlStorage(string)` (`Program.cs:244`), 2 × CS8981 lowercase type name (`Migrations/20251225104419_addclinics.cs:9`).

- [ ] 🟢 **Startup seeding has no retry.** `SeedAllClinicsAsync` and `BackfillAsync` run inline at boot with no job wrapper — a transient DB blip skips the backfill until the next restart.
  `api/ClinicManagement.API/Program.cs:465,471`

> `npx tsc --noEmit` in `web/` is **clean — zero TypeScript errors**. Packaging is otherwise sound: ports are `#define`d not inlined, build artifacts are gitignored, WebView2 is detected and offline-installed, and the desktop shell's server address is user-configurable with no hardcoded host.

---

## Suggested order of attack

| Wave | Items | Why |
|---|---|---|
| **1 — Stop the bleeding** | §1 all 8 · §2 items 1–5 · §4 item 2 | Data destruction, wrong money on legal documents, PHI readable by any local account. |
| **2 — Stop lying to the user** | §3 all 4 · §2 items 6–10 · §6 items 1–2 | Every one of these currently reports success while doing nothing, or shows two different numbers for the same thing. |
| **3 — Make it usable** | §7 items 1–2 · §5 items 1–5 · §9 item 1 | Mobile is unusable, five finished features are unreachable, the salle d'attente doesn't refresh. |
| **4 — Finish the French** | §8 all | The app is sold as French; raw English enums and `Type: ` in stored data undercut that. |
| **5 — Hygiene** | §9 remainder · §10 all | Indexes, precision, warnings, pinned build tooling. |

---

*Findings were verified by reading the implementation, not inferred from names, comments or `CLAUDE.md`. Where a doc or comment disagreed with the code, the code is reported. Nothing in this report is speculative — items that could not be confirmed were dropped rather than hedged.*
