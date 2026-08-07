# Spec: Adoption QA — L (residual blockers and gaps)

**Status:** APPROVED
**Type:** Small (forced multi-item pass — the whole remainder of the 2026-08-03 adoption review in one spec, by request)
**Created:** 2026-08-03
**Scope:** Full
**Branch:** new, off `main`
**Feature:** Everything the review found that specs I/J/K deliberately left out — backup that runs and can be restored, three clinical-safety defects, reminders that arrive saying the right day, two scheduling-integrity blockers, data portability, the relance worklist, an imaging bridge, cheque tracking, per-practitioner attribution, the CNAM plafond, and the arrêt de travail.

> ⚠️ **Sizing, stated once and honestly.** This is **eleven themes**, and it includes a schema change across six aggregates (L9) and a novel device integration (L7) — it is materially larger than the Type-Small specs a–k. It is written as one spec by explicit request. To make that safe, the items below are in **strict delivery order**, each is independently shippable, and the order is by *(harm × frequency) ÷ cost* — so stopping after any item leaves a coherent, better product rather than a half-migration. **L1–L4 are the ones that close blockers.** If time runs out, that is where it should run out.
>
> `/implement-small-feature` should be run **per item**, not once for the file.

---

## L1 — Clinical safety (3 defects) · closes 1 blocker

Do this first: smallest, cheapest, and the only group where being wrong can harm a patient. All three share one failure mode — **the screen makes a confident clinical assertion the data does not support**.

### L1a — Mixed dentition is chartable (Blocker)

`record-tooth-chart.tsx:149` draws `isAdult ? ADULT_TEETH : CHILD_TEETH` — **one set** — and `patient-record-modal.tsx:121` *derives* the view (`isAdultView = record ? record.isAdultTeeth : isAdultDentition(patient?.dentition)`) rather than offering it; the Adulte/Enfant switch was removed. So an 8-year-old's carie on **36** cannot be recorded. Same in `odontogram.tsx:144`.

⚠️ **This is the inverse of this codebase's usual pattern — the server is right and the UI cannot express it.** `DentalRecordActParser.cs:14-18`: *"A session is NOT restricted to a single dentition… a mixed-dentition visit (a permanent 36 alongside a deciduous 75) is recordable. The record's `IsAdultTeeth` flag is a **display hint, not a constraint**."*
⚠️ The regulatory research raised the severity: the **official BS1 form carries a full FDI odontogram including deciduous teeth** (51–55, 61–65, 71–75, 81–85) and instructs that it is « indispensable d'indiquer la dent traitée ». The caisse's own paper expects coding this UI cannot produce. Mixed dentition ≈ ages 6–12, which is also the better-reimbursed CNAM band (70 %, ages 4–18).

- Restore the Adulte/Enfant control as **user state**, not a computed constant.
- **Recommended:** add a third « Mixte » view showing permanent *and* deciduous positions, since that is what both the clinic and the BS1 form actually are. *Fork:* toggle-only (closes the blocker) vs. toggle + Mixte (closes it properly).
- **Keep the reason the derivation existed:** a fiche saved on baby teeth must *reopen* on baby teeth or its acts all read as "other dentition" and the chart opens empty. **Seed** the view from `record.isAdultTeeth` (or Mixte when its acts span both) — seeding ≠ locking.
- `IsAdultTeeth` stays a display hint. Do not make it a constraint.

### L1b — An allergy can be cleared (Major, patient-safety)

In the **same object literal, three lines apart** (`edit-patient-dialog.tsx:568-579`): `notes` and `importantNotes` send `.trim()` under a comment explaining *"Always present (possibly `""`), so emptying the box clears the stored value instead of reading as 'leave it alone'"* — then `medicalHistory` and `allergies` send `.trim() || undefined`. `JSON.stringify` drops `undefined`, and `UpdatePatientCommand.cs:226-231` treats absent as keep. A penicillin allergy typed on the wrong patient is **permanent**, and the optimistic spread shows it as gone until the refetch restores it, so the user believes it worked.

- Send `.trim()` for both, exactly like their neighbours.
- Apply the **server's returned patient**, not the local spread, so the UI cannot show a state the server rejected.
- ⚠️ **Audit the whole payload while the file is open** — two fields were missed in a literal whose neighbours were correct.

### L1c — A failed read never renders as data (Major ×4)

`.catch(() => [])` at `patients/[id]/page.tsx:2381` and `:2404` (« Aucun antécédent médical » / « familial » — the card checked before extracting a tooth from someone on Sintrom), `:702` (`loadFilesForFolder` — an empty folder for one holding radiographs), `PatientNotesStrip`'s `dentalRecords` feed (`:1161`), and `dashboard-header.tsx:86` (« Aucun patient trouvé » for a twelve-year patient — the fastest route to a file in the product).

⚠️ **The page already has the machinery** — `attempt()` (`:612`), `failedSections`, `sectionFailed()` (`:929`), `SectionLoadFailure` (`:189`) — and applies it to the five tab bodies but not these. Its own doc block at `:161-169` states the rule it violates.

- Route all five through it. Search distinguishes **empty** from **failed**, with a retry.
- ⚠️ **Add a `check:responsive` rule for `.catch(() => [])` in `app/` and `components/`.** The rules name this pattern twice and `components/CLAUDE.md` once, and five instances still shipped — prose has demonstrably failed. **Derive it (grep the pattern feeding `useState`); never a per-file allow-list.** Confirm it **fails against the current tree** before the fixes land, so the check is proven rather than assumed. *This sub-item is what stops L1c recurring.*

### L1d — Surface allergies where the decision is made

- **The ordonnance never reads `patient.allergies`** — `document-editor-content.tsx` offers an empty « Traitement en cours et allergies connues » textarea the prescriber retypes. **Prefill it.** Clamoxyl and Augmentin both carry `Amoxicilline` as a structured DCI (`MedicationCatalogSeed.cs:41-42`) and prescribing either to a penicillin-allergic patient raises nothing.
- **`patient-summary-modal.tsx` omits allergies, flags and chronic conditions entirely** (grep for `allerg`/`flags`/`medicalHistory` returns nothing) — and it is the one-click quick look from the patients list and the phone ⋯ menu. Add them; the full page and the fiche modal already do.

---

## L2 — Scheduling integrity (2 blockers)

### L2a — Rescheduling a no-show re-marks it absent (Blocker)

`edit-appointment-dialog.tsx:365` always re-posts the hydrated status verbatim (`status: status`, lower-cased at `:218`). `UpdateAppointmentCommand` applies the **date first** — `Reschedule()` correctly flips `NoShow → Scheduled` (`Appointment.cs:389-392`) — then the status block at `:374` sees posted `"noshow"` ≠ `Scheduled`, finds `Scheduled → NoShow` legal (`Appointment.cs:148`), and calls `MarkAsNoShow()` again. Two knock-ons in the same handler: the reminder routes to `VoidForAppointmentAsync` (`:584-587`), and **the collision + working-hours guard is skipped** because `Status == NoShow` (`:461-463`) — so someone else can be booked on top.

- **Recommended: the client stops posting an unchanged status.** Send `status` only when the user actually changed it — that is the root cause and it fixes the class, not the instance.
- **Also** make the handler order-independent: apply status *before* the date, or ignore a posted status equal to the pre-edit one. Defence in depth, because the client is not the only caller.
- Re-verify the collision guard runs after a reschedule out of `NoShow`.

### L2b — A recurring series books over existing patients (Blocker)

Both collision branches in `CreateRecurringSeriesCommand.cs:206,214` are gated on `request.DoctorId.HasValue`, and `existing` is only loaded under the same condition (`:183`). `recurring-series/page.tsx:178-188` builds the payload **without** `doctorId` — the create form has no practitioner field at all — so `DoctorId` is always null and no overlap is ever tested. The DB cannot catch it either: `EX_Appointments_NoDoubleBooking` is predicated on `DoctorId IS NOT NULL`. The outcome panel's conflict list (`:241-257`) is unreachable code.

- Add a **practitioner field** to the recurring create form (required, like the single-appointment dialog).
- Run the collision check **regardless** of `DoctorId` — a clinic-wide slot check when no practitioner is named. Route it through the shared `AppointmentScheduling` so guard and constraint cannot diverge.
- `allowOverlap` is declared on the client payload (`appointments.ts:20`) with **no counterpart on the command** — either wire it or delete it.
- ⚠️ **Same root cause, same fix, one more place:** a « créneau occupé » block booked with no practitioner blocks **nothing** (`create-appointment-dialog.tsx:759` promises « Aucun patient ne pourra être assigné à cette période »). Fix it here — it is the only way to protect a lunch break, and L2b's clinic-wide check is exactly what it needs.

---

## L3 — Reminders that arrive, and say the right day (2 blockers)

### L3a — The queue starves silently, for the whole install (Blocker)

The most interesting defect in the review, because **both halves are individually defensible**. A row whose channel is disabled after enqueue (`NotificationJob.cs:172-178`) or whose credentials are missing (`NotConfigured`, `:210-213`) is left `Pending` **on purpose** — "so it sends once the operator configures the channel". `PurgeTerminalOlderThanAsync` (`NotificationRepository.cs:37-46`) **deliberately** never deletes `Pending` — "deleting one here would leave a patient un-contacted with nothing recording why". But the scan is `Pending && ScheduledFor <= now`, `OrderBy(ScheduledFor)`, `.Take(50)` (`:28-33`). Unsendable rows accumulate at the **front** and, past the batch size, consume every tick forever. No clinic filter either, so on a shared install one clinic starves the others.

- **Recommended: a distinct non-terminal status** (`Blocked` / `AwaitingConfiguration`) excluded from the dispatch scan but never purged. It keeps both original intentions — the row survives and records why, *and* it stops occupying the queue.
  *Fork:* skip-in-query (`WHERE channel IN <enabled>`) — cheaper, but re-derives sendability in SQL and drifts from `ResolvedReminderSettings`.
- Add a **per-clinic** bound to the scan so one clinic cannot starve another.
- `EnqueueRemindersAsync` (`ReminderScheduler.cs:150-178`) enqueues on `EnabledChannels` with **no `IsSendable` check** — only the recall path checks it. Check it at enqueue, so the unsendable row is never created.
- Surface a « N rappels bloqués » count on `/rappels` with the reason. A silent queue is the whole problem.

### L3b — A moved appointment sends the old day (Blocker)

`GoogleCalendarSyncService.cs:520-524` calls `appointment.Reschedule(...)` and commits straight through the repository; **`IReminderScheduler` is not injected into that class at all**. The body and `ScheduledFor` are frozen at enqueue (`ReminderScheduler.cs:170-177`), and the dispatcher's safety net (`NotificationJob.cs:135-145`) re-checks only the appointment **status**, never its **time** — so a moved appointment is still "active" and the stale reminder sends. Appointments *created* in Google (`:744`) enqueue no reminder at all.

- Inject `IReminderScheduler` into the sync service: void-and-re-enqueue on a Google-side move, enqueue on a Google-side create.
- **Also harden the dispatcher**: re-check the appointment's **time** against the reminder's `ScheduledFor`/body before sending. That is the backstop that makes every *future* write-path omission harmless.
- `CancelRecurringSeriesCommand.cs:95-104` calls `appointment.Cancel(...)` with **no** `VoidForAppointmentAsync` (the file references no reminder type), so cancelling a 12-visit series produces months of red « Échecs (7 j) » for reminders deliberately cancelled — which trains the dentist to ignore the counter that warns about real failures. Add the void.

### L3c — Multi-tier reminders send one message (Major)

`ReminderSchedule.ComputeSendTimeUtc` returns a **single** `DateTime?` — the largest future tier wins, the rest are discarded — and `EnqueueRemindersAsync` creates one row per **channel**, not per tier. The settings UI invites « Séparez les paliers (heures) par des virgules », placeholder « Ex. 24, 6 » (`reminder-settings.tsx:705-719`) with no statement that only one fires. For a no-show problem the 6 h nudge is the one that works.

- Return **all** future tiers; enqueue one row per (channel × tier).
- ⚠️ Idempotency must key on **(appointment, channel, tier)** or the minutely job double-sends.
- ⚠️ **Add a quiet-hours floor.** `ComputeSendTimeUtc` will fall through and send at 02:00 for an 08:00 appointment booked ~22 h ahead. Clinic-local, configurable, default no sends 21:00–08:00.

### L3d — The assistant reports the wrong hour (Major)

`AIActionService.cs:865, 986, 1018` print `AppointmentDateTime.ToString("yyyy-MM-dd HH:mm")` — the **raw UTC instant**, in ISO order — for `list_appointments`, the disambiguation list **before a cancel**, and the cancel confirmation. Tunisia is UTC+1, so every hour is wrong by one and the dentist can confirm cancelling the wrong slot. Convert through `ClinicClock.ToClinicLocal` and format `dd/MM/yyyy 'à' HH:mm`, as `ReminderScheduler.cs:265-266` and `NotificationGenerator.cs:404-407` already do.

---

## L4 — Backup, restore, and proof it ran · closes the 4th sale-blocker

Today the entire protection is a button someone must remember to press, whose documented default path **fails on a fresh install**, that records nothing about when it last ran, writes by default to the same disk as the live data, is never verified readable, is erased by every upgrade, and can only be restored by someone comfortable in PowerShell. The forum research found « no manual backups » named by practising dentists as non-negotiable.

⚠️ **The capability already exists in this repo and was not carried across.** `deploy/` ships nightly cron (`BACKUP_CRON=0 2 * * *`, `deploy/backup/entrypoint.sh:6-12`) **plus** a wal-g PITR sidecar (`deploy/postgres/pitr-entrypoint.sh:10,24-26`). None of it is in the Windows installer — so the deployment aimed at a dentist with **no** IT staff is manual-only.

- **L4a — Unattended.** A daily `BackupJob` in Hangfire beside the existing four. ⚠️ **Not** connectivity-gated (same reasoning as `StockExpiryJob`: the output is local, so it must work offline). Clinic-editable hour (default 02:00 clinic-local via `ClinicClock`), enabled flag, and retention count (default 7). **The pruner prunes oldest-first, matches only `clinic-backup-<timestamp>` names, and never deletes the last surviving backup.** `DisableConcurrentExecution` so two runs cannot overlap.
- **L4b — A real default destination.** `PgDumpBackupService.cs:69` throws when `destinationRoot` is empty; the fallback `Backup:DefaultDestination` is written as `""` by the installer (`clinic-server.iss:338`) — while `backup-settings.tsx:99-101` says « Laissez le champ vide pour utiliser le dossier par défaut du serveur. » Write a real install-relative default via `LocalInstallPaths` (a service's CWD is `System32`), and fall back rather than throw. Replace the free-text path with a picker, or at minimum validate existence + writability **before** the run. **Warn prominently when the destination is on the same volume as the database.**
- **L4c — Verified readable.** `pg_dump` exiting 0 is not proof; the service only measures folder size (`:156`). Run **`pg_restore --list`** on the output and require a non-empty TOC — fast, read-only, no target DB needed. A failed verification is a **failed backup** (the existing path already deletes the partial folder). Record the object count; 3 tables where the schema has 38 is a detectable disaster.
- **L4d — Proof, and a nag.** New `BackupRun` rows (`ClinicId`, `StartedAt`, `CompletedAt?`, `Outcome`, `DestinationPath`, `SizeBytes?`, `VerifiedObjectCount?`, `Error?`, `Trigger`). `GET /api/backup/history` (`AdminOnly`, paged). `backup-settings.tsx` shows **« Dernière sauvegarde réussie : <date> »** as its headline above the button. A staleness `StaffNotification` for admins past a threshold (default 48 h) — ⚠️ modelled as an **ensure/clear pair**, not fire-once, exactly like `EnsureStockExpiringSoonAsync`, because staleness is crossed by the passage of time. Currently zero repo hits for `LastBackup`/`BackupHistory`; the result lives in component-local `useState`.
- **L4e — An upgrade stops destroying config.** `WriteProductionConfig` writes with `SaveStringToFile(..., False)` — truncate — unconditionally from `ssPostInstall` (`:651`), with no `if not FileExists` guard, **although the author used that exact idiom 25 lines away** to gate `initdb` (`:371`). Write only when absent; otherwise **merge** missing keys and leave existing values. Copy to `.bak-<timestamp>` first. Emit `Security:EnableHsts` and every key the README tells operators to hand-edit (`README.md:345-347, 399`) — a generator that writes fewer keys than the file legitimately holds is the bug. ⚠️ Per `inno-setup-brace-comment-bug`, `{app}`/`{sys}` inside `.iss` `{ }` comments break ISCC — use `//`.
- **L4f — Stop copying over a running service; back up before migrating.** `[Files]` (`:68-74`) copies before the only teardown (`sc stop`, `:501-508`) which runs at `:659`; no `PrepareToInstall`/`CloseApplications`/`ServicesStopped` exists in either `.iss`. Add one. Then **take an automatic backup before applying migrations** (`DeferredStartupService.cs:55` runs them *after* Kestrel is serving; the README notes the last migration is "lossy by design" with an empty `Down()`, so "rollback means restoring this backup"). If that backup fails, **abort the migration**, non-zero exit via `StartupDiagnostics`.
- **L4g — A restore the owner can perform.** **Recommended: a console verb**, `dotnet run -- restore-backup <folder>`, matching `reset-admin-password` / `reconcile-money` / `verify-schema`. Not HTTP: a restore runs with the app **stopped**, so an endpoint inside the app it replaces is the wrong shape. It must refuse if the service is running; validate the folder (`database.dump` present, `pg_restore --list` non-empty) **before touching anything**; read credentials from the encrypted machine-bound `.local/db-credentials` so no password is typed; **take a safety dump first**; `pg_restore --clean --if-exists`; copy `files/` back; French summary; exit 0/1. Add a settings panel that prints the command with the resolved path filled in. `packaging/README.md:93` currently states "There is no in-app restore."
  ⚠️ **Rehearse it and record the rehearsal.** Nothing in the repo shows a restore has ever been performed. backup → wipe → restore → log in → verify a patient, an invoice and a stored file, in `progress.md`. **No test can substitute.**

---

## L5 — Data portability: import and export

- **Import.** No `CsvHelper`/`EPPlus`/`ClosedXML`/`NPOI` in any of the five `.csproj`; no `papaparse`/`xlsx` in `web/package.json`; no import endpoint in any of the 33 controllers; no `Import*`/`Bulk*` command in any of the 27 feature areas. **A dentist arriving with 3 000 patients in a spreadsheet types them in by hand — this alone stops most switchers.**
  - Scope to **patients only** for this pass: CSV upload → column mapping UI → **dry-run preview with per-row validation** → commit. Report per-row outcome; never partial-commit silently.
  - ⚠️ **Reuse `CreatePatientCommand`'s validation** rather than a parallel path, or imported rows bypass rules every hand-typed row obeys.
  - ⚠️ **Duplicate handling must be explicit**, because `CreatePatientCommand` performs **no existence check of any kind** and `Patient.cs:67-72` states "this app has no merge and no soft delete". Offer skip / create-anyway per row, defaulting to skip on a name+DOB or phone match.
  - ⚠️ Phone numbers arrive in every format. Normalise through `PhoneNumber.ToE164` on the way in — and note the standing defect that the *write* path stores raw (`PhoneNumber.cs:16`), which the import must not replicate.
- **Export.** Zero occurrences of `csv`/`xlsx`/`excel`/`papaparse`/`sheetjs` in the repo, and zero of « Exporter » in `web/`. The `pg_dump` is readable only by PostgreSQL tooling — **the owner cannot leave with their data in usable form**, and cannot hand their accountant anything.
  - CSV export on `/patients`, `/factures`, `/creances`, `/caisse` (including the extrait), `/appointments`, `/treatment-plans`, `/stock`, `/lab-orders`.
  - ⚠️ **Export must honour the current filters and must export the whole filtered set, not the current page.** Reuse the repository query with `paging: null` — the primitive already models exactly this case.
  - ⚠️ Money columns use the product's own formatting authority (millimes, comma). Never `toFixed(2)`.
  - ⚠️ Gate exports containing money on `AdminOrDoctor` — an unrestricted CSV export would reopen spec I's hole from a different door.

---

## L6 — The relance worklist gets a screen

The richest stranded subsystem in the app, and **the only item in this spec that adds revenue rather than protecting it.** `RecallController` serves a due list aggregating **four** reasons — `OverdueInstallment`, `StalledPlan`, `UnansweredDevis`, `OverdueVisit` — with `dueSince`/`daysOverdue`/`detail` per reason, plus `settings` GET/PUT, `{id}/contacted`, `{id}/snooze`, `{id}/send`. `GetPatientsToRecallQuery` is bounded and SQL-filtered. **`recallsApi` has zero callers**; `app/recalls/` does not exist; the dashboard's card was deleted while `alerts.patientsToRecall` is still computed and on the wire; `recallIntervalMonths` has no input anywhere.

- Build `/recalls`: the due list (one row per patient with **every** reason, most urgent first — the DTO is deliberately per-patient because snooze lives on the patient), phone-to-call affordance, « Marquer comme contacté », « Reporter », and « Envoyer ».
- Add `recallIntervalMonths` to clinic settings — it currently has no UI at all.
- Restore the dashboard KPI **and its link**, per `dashboard-links.ts`'s exhaustive-`Record` contract.
- ⚠️ `SendRecallCommand` already refuses correctly when no channel is configured (it used to snooze 30 days having queued nothing) — surface that French failure, don't swallow it.
- ⚠️ The failed-recall notification currently deep-links to `/rappels?status=failed`, a page that **cannot list the patient to re-contact**. Re-point it here.
- ⚠️ Bulk send needs a cap and a confirmation naming the count.
- ⚠️ **There is no consent / opt-out field on `Patient`** (zero hits for `optout`/`DoNotContact`/`consent`), and today the only way to stop messaging someone is to delete their phone number — which also removes them from recall candidacy. **Add a do-not-contact flag** and honour it here and in `ReminderScheduler`. Without it, a worklist with bulk send is a complaint generator.

---

## L7 — An imaging bridge

⚠️ **This is the item most likely to decide a demo, and the only one in this spec that is genuinely novel work.** Julie, LOGOSw and Open Dental all have one; this app has nothing. Per the industry taxonomy, a **bridge** passes a patient identifier to the imaging application (VixWin, Sopro, Durr DBSWIN, Carestream), which opens that patient's images — *"almost every practice management software system and most imaging systems can be set up to work this way"*, and TWAIN panoramics are near plug-and-play. **Direct integration is normally same-vendor only; native means writing a capture pipeline. A bridge is the achievable and expected form.**

- Per-clinic configuration: imaging application executable path + an argument template with `{patientId}`, `{lastName}`, `{firstName}`, `{dob}` placeholders.
- A « Radiographies » action on the patient page that launches it. ⚠️ A browser cannot launch a local process — this must go through the **desktop shell** (`desktop/`, WebView2) or a small local helper. **That constraint decides the design and must be settled before any code.**
- Cloud mode: the action is absent, not broken.
- ⚠️ **Do not attempt image ingestion in this pass.** The server allow-list is PDF/PNG/JPEG (`FileContentValidation.cs:27`), so DICOM/TIFF from an imaging centre is refused today — and its refusal is reported as a connection error, discarding the server's real French reason (`patient-files-manager.tsx:156-162` re-reads `errorData.message` from a body that only carries `{ error }`). **Fix that error surfacing here** — it is small and it is actively misleading — and leave DICOM support to its own feature.
- **Recommended:** ship the *configuration + launch* only, and validate with one real sensor console before widening. A bridge that works for one vendor beats a framework that works for none.

---

## L8 — Cheque tracking and a cash-only total

Post-dated cheques are ubiquitous in Tunisia and `PaymentMethod.Cheque` is a **bare enum value**: `Payment`, `InstallmentPayment`, `CreditNote` and `Expense` carry `Amount`/`Method`/`PaidOn` and nothing else. Zero repo hits for `chequeNumber`/`numeroCheque`/`bankName`/`banque`. For money *out* the number can go in the expense description; for money **in** there is no free-text field at all.

- Add optional `ChequeNumber`, `BankName` and `ChequeDueDate` to the two payment ledgers, shown only when the method is `Cheque`.
- **A cash-only figure.** `CaisseSummaryDto` is four scalars summed across all methods with no grouping, and `GET /api/billing/caisse/ledger` takes no `method` filter (`BillingController.cs:79-86`) — so the owner cannot separate what is physically in the drawer from a cheque not yet banked. Add a per-method breakdown to the summary and a `method` filter to the ledger.
- ⚠️ Adding fields must not disturb `CaisseLedgerTests`' invariant `Σ movements == cashIn − refunds − cashOut == net`.
- **A cheques-due view** (by `ChequeDueDate`) is the natural payoff — a post-dated cheque nobody banks is money lost.

---

## L9 — Per-practitioner attribution ⚠️ schema change

⚠️ **The largest item here and the only one requiring a migration across several aggregates. Do it last, or split it out.**

`DoctorId` exists on exactly three entities: `Appointment.cs:11` (the **only FK to `Doctors` in the entire EF model**, `AppointmentConfiguration.cs:79-87`), `RecurringAppointment.cs:14`, and `WaitingListEntry.PreferredDoctorId:16` — which is **not even an FK** (`WaitingListEntryConfiguration.cs:38` is a bare `builder.Property`). **`Invoice` has no practitioner reference at all**, nor do `InvoiceLine`, `Payment`, `TreatmentPlan`, `Installment`, `InstallmentPayment`, `DentalRecord` or `Expense`. `Patient` carries no doctor assignment. `Features/Dashboard/` contains **zero** occurrences of `Doctor` across all four readers. `MedicalDocument` stores `DoctorName`/`DoctorSpecialty` as free-text snapshots, and `PractitionerRenderSnapshot.cs:154` says why: *"resolving by the named issuer would require a persisted DoctorId (out of scope here)."*

- Add nullable `DoctorId` to `Invoice`, `DentalRecord` and `TreatmentPlan` — the three that answer "who earned this". **Nullable, and backfilled from the linked appointment where one exists**; historical rows legitimately have none.
- Make `WaitingListEntry.PreferredDoctorId` a real FK.
- A **practitioner filter** on the dashboard's Argent section and on `/factures`.
- ⚠️ **Do not attempt per-practitioner *data scoping*** ("this dentist sees only their own patients") in the same pass. That is an authorization model on top of a schema change, and spec I explicitly deferred it. Attribution (reporting) first; scoping is a separate decision with its own blast radius.
- ⚠️ Run `reconcile-money` before and after; extend `verify-schema` for the new columns and FKs.

---

## L10 — The CNAM annual plafond

Zero repo hits for `plafond`/`annualLimit`/`ceiling` in any CNAM file. `CnamReimbursementCalculator.Estimate:40` is `coefficient × VLC × rate` with **no cap and no knowledge of what the patient has consumed this year**, so « Remboursement indicatif » over-promises for anyone near their ceiling — and the disclaimer names only the age rate.

Sourced figures to build against, effective **1 February 2024** (⚠️ **Likely**, two Tunisian outlets in agreement; no official CNAM page retrieved — confirm before shipping numbers): **450 DT** insured alone; **675 / 900 / 1 125 / 1 350 DT** at 1/2/3/4+ dependants; **+150 DT dedicated to soins dentaires externes**; +100 DT per dependent parent; +100 DT per dependent disabled child; +150 DT pregnancy. **Cone Beam is hors plafond**, and so is a **dental prosthesis** (since April 2019).

- Per-patient year-to-date consumed, per clinic year, with the plafond and dependants configurable on `CnamInfo`.
- Show remaining ceiling beside the estimate; mark hors-plafond acts as not consuming it.
- ⚠️ **Fix spec K's item K10 first.** The seeded tariffs are the pre-2021 ones (`D = 1.200` where the convention says **3,000 DT**), so every estimate is already understated by 60–75 %. A ceiling computed on wrong tariffs is worse than none.
- ⚠️ The clinic sees only its **own** acts — a patient treated elsewhere has consumed ceiling this app cannot see. **Label it an estimate and say why**, or it becomes a confident wrong number.

---

## L11 — Arrêt de travail on the official form

`features/cnam-arret-travail-overlay/` contains **only `assets/`** (`CMIATMP.pdf`, `p61.pdf`, `P61_2024.pdf`) — no spec, no progress, no code. Repo-wide, `arrêt de travail` appears **once**, as a description string on the generic certificat tile (`documents/page.tsx:50`). `DocumentTypes` declares five types and none is one.

- A second overlay renderer on the same pattern as `CnamBs1BulletinRenderer`, against the bundled form: patient identity, dates from/to, **number of days**, motif, practitioner identity + code, cachet/signature space.
- A new `DocumentTypes` member, an editor branch, and the same K-series lessons applied from the start: **mandatory-field validation before save**, a **practitioner picker** (never `doctors[0]`), a **working Print path** (the `ref` must not live in one branch of a ternary), **no empty Word export**, and amounts/dates in fr-TN format.
- ⚠️ Coordinate calibration is manual and unverifiable by any test in this repo. Print onto the real form and check by eye; record it.
- ⚠️ Which of the three bundled PDFs is the current official form is **unverified** — settle that before calibrating.

---

## Data / Schema Changes

- **L4:** new `BackupRuns` table (indexed `(ClinicId, StartedAt DESC)`); `Clinic` gains `BackupEnabled`, `BackupHourLocal`, `BackupRetentionCount`, `BackupStaleAfterHours`.
- **L6:** `Patient` gains a do-not-contact flag; `Clinic.RecallIntervalMonths` needs no column (it exists) — only a UI.
- **L7:** `Clinic` gains imaging executable path + argument template.
- **L8:** `Payment` and `InstallmentPayment` gain `ChequeNumber?`, `BankName?`, `ChequeDueDate?`.
- **L9:** `Invoice`, `DentalRecord`, `TreatmentPlan` gain nullable `DoctorId` + FK; `WaitingListEntry.PreferredDoctorId` becomes a real FK.
- **L10:** `CnamInfo` gains dependants count / plafond inputs; a per-patient YTD store or a derived query.
- **L11:** a new `DocumentTypes` member (token only).
- **L1, L2, L3, L5** need **no** schema change.
- ⚠️ **Every new setting must ship with a caller in the same change.** `Clinic.SetStockExpiryLeadDays` shipped with **zero** production callers and its window has been permanently 30 days ever since — that is the precise failure this spec must not repeat.
- ⚠️ Per `ef-migration-scaffolding-hazards`: `-p:BaseOutputPath=…`, never `--no-build`, commit the model snapshot with each migration. Extend **`verify-schema`** per table/index/FK; run **`reconcile-money`** before and after L8 and L9.

## API Contract

| Endpoint | Item | Note |
|---|---|---|
| `GET /api/backup/history` | L4d | `AdminOnly`, `PagedResult<BackupRunDto>` |
| `POST /api/backup` | L4c/d | Same shape; gains `verifiedObjectCount`; a dump failing `pg_restore --list` now **fails** where it previously succeeded |
| `PUT /api/clinics/{id}` | L4a, L6, L7 | Widened: backup schedule, recall interval, imaging config |
| `POST /api/patients/import` (+ `/import/preview`) | L5 | `AdminOrDoctor`; multipart; preview is a **dry run** |
| `GET /api/{patients,invoices,expenses,…}/export` | L5 | CSV; honours filters; money endpoints `AdminOrDoctor` |
| `GET /api/billing/caisse/ledger` | L8 | Gains a `method` filter |
| `GET /api/billing/caisse` | L8 | Gains a per-method breakdown |
| `POST /api/invoices/{id}/payments` | L8 | Accepts optional cheque fields |
| `GET /api/patients/recalls` etc. | L6 | **Already exist** — this is the missing client |
| Console `restore-backup <folder>` | L4g | Exit 0 restored / 1 refused |

## Out of Scope

- **Point-in-time recovery / WAL archiving** on Windows (L4) — `deploy/` has wal-g for Cloud; PITR on a single PC is a much larger operational commitment.
- **Automatic off-site upload** of backups. ⚠️ **This is a legal question before a technical one:** loi 2004-63 **art. 52** makes transferring personal data outside Tunisia conditional on **INPDP authorisation**, penalty 1 year + 5 000 DT, and whether the EU is on the adequacy list is **unverified**. A second *local* destination (USB/NAS) is the safe version.
- **DICOM/TIFF ingestion and an image viewer** (zoom, pan, annotation, before/after) — L7 ships the bridge only.
- **Per-practitioner data scoping** — L9 is attribution/reporting, not authorization.
- **Structured medical-alert flags** (anticoagulant, grossesse, endocardite) — `PatientFlagType` has five values of which the write path only ever creates `HighPriority` with the literal « Patient signalé », so the enum is decorative. Natural successor to L1.
- **A DCI-vs-allergy blocking check** — L1d prefills so the prescriber *sees* it; real cross-checking needs structured allergies.
- **Odontogram fidelity** (per-surface fill, bridge spans, diagnose dedupe) and the `DentalRecordLinker` over-deletion that hard-deletes every diagnosis on a treated tooth regardless of surface. Real, and dependent on how surfaces get modelled.
- **CIN, duplicate-patient detection and merge.**
- **Consolidating `CnamNomenclatureEntry` into `DentalActCode`** (near-duplicates; see spec K).
- **Split-shift working hours and public holidays** — a weekday is modelled as exactly one `{from,to}` window and there are zero repo hits for `holiday`/`férié`.
- **Drag-and-drop reschedule, quarter-hour slots, per-practitioner agenda columns, an « arrivé » status, and a Google→App scheduled job.**
- **A Tunisian SMS provider adapter** — `HttpSmsSender` posts a fixed generic shape and no provider is named anywhere; L3 fixes the queue, not the gateway.
- **Arabic UI** and paediatric dosage forms.
- **Revenue by act type**, and the invoice draft form previewing TVA/timbre before an irreversible issue.

## Edge Cases (Critical only)

- **L1a:** an existing fiche must reopen showing **its own** acts — a deciduous-only record must never open on an empty adult arch. That regression is what the derivation protected against and is the one thing to check by hand. A Mixte view must not duplicate an FDI number, and must hold the `coarse:min-w-11` floor — the arch **already** overflows at 820 px (~444 px of content for a ~749 px arch), and the arch toggle's gate is a `max-width:767px` **width** query while cell size is a **pointer** query, so the whole 768–1023 px band packs 44 px cells as if there were desktop room. Fix that gate here.
- **L1b:** `""` must clear **and** an omitted key must still mean unchanged — both directions need a test. Clearing an allergy must not blank `MedicalHistory`: they share one `if (… != null || … != null)` branch and one `UpdateMedicalHistory(a, b)` call.
- **L1c:** empty-vs-failed must be visually distinct **at 320 px**, where both are otherwise the same blank rectangle. The new check must not fire on a legitimate non-render `.catch` — narrow the pattern, never add a per-file exemption.
- **L2a:** after fixing, a genuine `Scheduled → NoShow` must still work, and rescheduling out of `NoShow` must now hit the collision guard.
- **L2b:** the clinic-wide collision check must not reject a slot the **DB constraint** would allow, or the guard and the constraint disagree in the opposite direction. Reuse `OccupiesSlot`.
- **L3a:** migrating existing stuck `Pending` rows to the new status is a **data migration** — count them first and report it; silently re-statusing rows that represent un-contacted patients is exactly what the original comment warned against.
- **L3c:** idempotency on (appointment, channel, **tier**), or the minutely job double-sends every tier.
- **L4:** the pruner must never empty the folder, and must never delete a folder whose name it does not recognise. A destination on a **removable** drive absent at 02:00 must record a **failed** run, not silently skip — a skipped run is indistinguishable from success in a history list. The staleness alert must not fire on a brand-new install (different message). L4e's merge must not resurrect a key the operator **deliberately removed**. L4f aborting must leave the DB exactly as it was. L4g must invalidate live sessions (bump `TokenVersion`) or a restored user keeps a token minted against newer state, and `files/` restore into a non-empty target is a refusal unless `--force`.
- **L5:** an import must be all-or-nothing per **row**, never a silent partial commit, and must reuse `CreatePatientCommand`'s validation. Export must never emit the current page when a filter implies the whole set.
- **L8:** the new fields must not disturb `Σ movements == cashIn − refunds − cashOut == net`.
- **L9:** the backfill must not invent a practitioner where none is knowable — nullable means nullable, and every read must tolerate it.
- **L10:** a patient treated elsewhere has consumed ceiling this app cannot see. Label the figure an estimate and say why.

## Testing

Per item, in delivery order. ⚠️ Per `smart-app-control-blocks-tests`, `dotnet test` fails at assembly load with `0x800711C7` on this machine (SAC is ON — environmental, not a defect): write the tests, verify with SAC off or elsewhere.

- **L1** — `UpdatePatientClearsAllergiesTests` (both directions, and the shared-branch case); a test **asserting** `DentalRecordActParser` permits mixed dentition, so a future tidy-up cannot narrow the server back to the old UI; the new `.catch(() => [])` check, **proven to fail against the current tree first**.
- **L2** — reschedule-out-of-NoShow preserves `Scheduled` and hits the collision guard; a recurring series with no `DoctorId` reports conflicts.
- **L3** — a `Blocked` row does not occupy the dispatch batch; a Google-side move voids and re-enqueues; the dispatcher refuses a reminder whose time no longer matches; per-tier idempotency; quiet hours.
- **L4** — `BackupJobTests` (no overlap, retention oldest-first, never empties, failure recorded); `BackupVerificationTests` (truncated/zero-byte dump fails); staleness ensure/clear with no daily duplicate and the new-install case. **L4e/f/g are operator-verified, not CI-runnable** — install, configure, upgrade in place and confirm config survived, confirm the service stopped before the copy, and **rehearse a full restore**, all recorded in `progress.md`.
- **L5** — round-trip: export → import → identical set. Duplicate, bad-phone and bad-date rows rejected per-row with reasons.
- **L6** — the four recall reasons each produce a row; snooze/contacted honoured; do-not-contact excluded from **both** recall and `ReminderScheduler`.
- **L8** — money invariants hold with cheque fields present; per-method totals sum to `cashIn`.
- **L9** — `reconcile-money` diff clean before/after; `verify-schema` for the new columns and FKs.
- **L10, L11** — manual: print onto the real forms and check by eye. **No test in this repo can assert paper.**
- **Frontend gate, every item that touches `web/`:** `npm run check:responsive` + `npx tsc --noEmit` + `npm run build`, then an eye pass at 320/390/820/1180/1440 px per `.claude/rules/frontend-web.md`. There is no test runner in `web/` — that is the whole gate.
