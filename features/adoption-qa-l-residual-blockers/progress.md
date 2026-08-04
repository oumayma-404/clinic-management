# Progress: Adoption QA — L (residual blockers and gaps)

**Started:** 2026-08-03
**Type:** Small (forced multi-item pass — 11 themes in one spec, by explicit request)
**Branch:** `feature/audit-sections-3-to-10` (user instruction: "use this branch and do not stop" — the spec's
"new, off `main`" was overridden, and the tree carries ~211 in-flight files from specs I/J/K that the L items
build on, so branching off `main` would have removed the ground under this spec)

## Status
- [x] **L1 — Clinical safety (a, b, c, d) — DONE, quality gate green**
- [x] **L2 — Scheduling integrity (a, b) — DONE, both blockers closed**
- [x] **L3 — Reminders (a, b, c, d) — DONE, both blockers closed**
- [x] **L4 — Backup / restore (a–g) — DONE in code; the L4e/f/g operator rehearsal is NOT done (see below)**
- [x] **L5 — Import / export — DONE. Export wired on all nine lists; import built end to end (preview + commit)**
- [~] **L6 — Relance worklist — DROPPED by the user** (« i do not care about relance at all »). Not deferred:
      struck from this spec. The backend stays intact and unreached — see « Verified state of L6 » in session 3.
- [~] L7 — Imaging bridge — the **error-surfacing half is DONE** (session 3); the bridge itself is untouched and
      still blocked on the desktop-shell decision
- [x] **L8 — Cheque tracking — slice A (session 3) + slice B (session 4) both DONE**
- [x] **L9 — Per-practitioner attribution — DONE** (session 4; migration + backfill + both filters + verify-schema)
- [x] **L10 — CNAM plafond — DONE** (session 4)
- [x] **L11 — Arrêt de travail — DONE in code** (session 4; the coordinate calibration was verified against the real
      form by replay-and-eyeball, but **printing onto real paper is still owed** — see session 4)
- [x] Quality checks for L1 + L2 (`dotnet build`, `npx tsc --noEmit`, `npm run check:responsive`, `npm run build`)
- [ ] Tests (handled by `/test-small-feature`)

> **Every item of this spec is now built except L7's imaging bridge.** L1–L5 and L8–L11 are done (sessions 1–4);
> **L6 was struck** by the user, not deferred. See « Session 4 » at the end of this file for L8 slice B, L10, L11
> and L9.
>
> **What is genuinely left:**
> 1. **L7's imaging bridge** — blocked on the question the spec itself calls decisive: a browser cannot launch a
>    local process, so it must go through the `desktop/` WebView2 shell or a small local helper, and that choice
>    decides the design. Its small half (the misleading upload-error surfacing) landed in session 3.
> 2. **Three things no code in this environment can do**, each named precisely in the session-4 quality table:
>    `dotnet run -- verify-schema` and `-- reconcile-money` before/after the two new migrations (no PostgreSQL
>    here); the **L4e/f/g operator rehearsal** still outstanding from session 1; and **printing the arrêt de
>    travail onto the real CNAM P 061** to confirm the calibration by eye.
> 3. **Tests** — `/test-small-feature`'s job. Sessions 1–4 each list what they left; session 4's list is the
>    longest and starts with a smoke test for the P 061 renderer, which is the one thing SAC prevented here.
>
> Nothing is committed — see "Not committed" below.

## Working tree note (start of session)
The tree was already dirty when this session began — 211 changed paths, all of them the in-flight work of the
preceding specs (`audit-sections-3-to-10`, `liaison-norms-and-document-email`, `ordonnance-certificat-norms`,
`agenda-phone-ux`, `adoption-qa-i/j/k`). Notable untracked areas that are **not** this feature:
`api/.../Features/DocumentEmails/`, `DocumentEmail*` entity/repo/config/migration,
`ProcedureTypeCategories`, `web/components/send-document-email-dialog.tsx`, `web/lib/zones.ts`,
`web/components/ui/empty-state.tsx`, `features/adoption-qa-{i,j,k}/`, `features/landing-website/`.
No commit is made by this skill, so nothing is staged; the list is recorded so a later commit can exclude it.

⚠️ **A second session was editing the same tree during this one.** `clinic-settings.tsx`,
`invoice-form-modal.tsx`, `patient-record-modal.tsx`, `treatment-plan-form-modal.tsx` and
`plan-workspace.tsx` changed under me mid-edit (a `formatAmount`/`parseAmountInput` sweep, and the removal of
`getAiSummary`), and one of my imports was briefly duplicated as a result (fixed). `tsc` was transiently red
from *their* missing import in `clinic-settings.tsx`; it went green on its own. Re-verify the gate before
committing.

## Files Changed

### L1a — Mixed dentition is chartable (the spec's one Blocker in L1)
| File | Change |
|---|---|
| `web/lib/dentition.ts` | New `DentitionView` (`adult`/`child`/**`mixed`**) + labels, `dentitionViewFor`, `dentitionViewForTeeth`. The `Dentition` doc block no longer describes the mixed stage as an accepted limitation. |
| `web/components/tooth-multiselect.tsx` | New **`MIXED_TEETH`** quadrants (each deciduous tooth interleaved immediately distal to its permanent successor — 55 beside 15 … 51 beside 11), plus derived `MIXED_FDI` and three exhaustive `Record`s: `TEETH_BY_VIEW`, `FDI_BY_VIEW`, `ARCH_QUADRANTS_BY_VIEW`. |
| `web/components/dentition-view-switch.tsx` | **New.** The Adulte / Enfant / Mixte segmented control (`coarse:py-3`, grown not overlaid — the segments are adjacent). |
| `web/components/record-tooth-chart.tsx` | Prop `isAdult: boolean` → **`view: DentitionView`** (a boolean has no third state). |
| `web/components/patient-record-modal.tsx` | `isAdultView` (derived, no control) → `chosenView` state + late-binding `seededView`. Seed reads the record's **own teeth**, so a fiche spanning both dentitions reopens on Mixte; reset to the seed on every open. `viewTeeth`/quadrants/« Toute la bouche »/the à-traiter bulk-select and the hidden-act count all key off the view. |
| `web/components/odontogram.tsx` | Same switch, above the tabs so both charts agree. Seed widens to Mixte when a **charted** tooth lies outside the patient's stored dentition, so an existing diagnosis can never be hidden by the default. |
| `web/components/tooth-arch-layout.tsx` | **The 768–1023 px gate.** `isNarrow` was `(max-width: 767px)` while the thing deciding how much room the arch needs (`coarse:min-w-11`) is a *pointer* query — so a coarse-pointer tablet portrait drew both arches with 44 px cells and offered no switch. Now `(max-width: 767px), (max-width: 1023px) and (pointer: coarse)`. |
| `web/components/patient-summary-modal.tsx` | Two `isAdult` call sites → `view="adult"` / `view="child"`. |

### L1b — An allergy can be cleared
Backend was already done in an earlier session (`UpdatePatientCommand.cs` — `Address` tri-state + the shared
`ToDto()` response). This session closed the **client half**:
| File | Change |
|---|---|
| `web/components/edit-patient-dialog.tsx` | `medicalHistory` / `allergies` send `.trim()`, not `.trim() \|\| undefined` (`JSON.stringify` drops `undefined`, which the handler reads as "leave alone" — so a wrong-patient penicillin allergy was permanent). `address` sends **`null`** when every box is blank, never `undefined`. `flagNotes` → `.trim()`. `savedPatient` is now **the server's response**, not `{ ...patient, ...updateData }` — the optimistic spread showed a state the server never stored. |
| `web/lib/api/types.ts` | `PatientDto.address` widened to `\| null`; tri-state documented on `address`, `medicalHistory`, `allergies`. |
| `web/lib/api/patients.ts` | `create`'s `address` accepts `null` so one expression serves create and update. |

Payload audit (the spec's ⚠️ "audit the whole literal"): `emergencyContact*`, `referredBy`, `notes`,
`importantNotes`, `email`, `phoneNumber`, `cnamInfo`, `version` were already correct. `insuranceInfo` sends
`undefined` when blank, which the handler's `else` branch **clears** — correct by a different mechanism,
documented server-side; left as is.

### L1c — A failed read never renders as data
| File | Change |
|---|---|
| `web/components/ui/load-failure.tsx` | **New shared primitive** `LoadFailureNotice` (`banner` / `inline`). `EmptyState`'s own doc block already said "could not load … has its own treatment with a « Réessayer »" — that treatment existed **once**, inline in the patient page, which is precisely why six other surfaces reached for `.catch(() => setX([]))`. |
| `web/app/patients/[id]/page.tsx` | Antécédents **médicaux** and **familiaux** routed through `renderSectionEmpty` (also fixes their third state: they asserted « aucun » while loading). `loadFilesForFolder` records `"files"` in `failedSections` instead of `.catch(() => [])`. `SectionLoadFailure` now delegates to the primitive. |
| `web/components/patient/patient-notes-strip.tsx` | New `recordsFailed` / `onRetryRecords`; a failed fiches read no longer prints « Aucune alerte pour ce patient » on the band under the patient's name. `notice` renders **above** what did load and replaces `emptyLabel`. |
| `web/components/dashboard-header.tsx` | Patient search distinguishes failed from empty (`searchFailed` + a `searchRetry` token) — « Aucun patient trouvé » about a twelve-year patient was the fastest route to a file in the product. |
| `web/scripts/check-responsive.mjs` | **New derived check `failed-read-as-empty`** — bans `.catch(() => [])`, `.catch(() => ({}))` and `.catch(() => setX([]))` in `app/` + `components/`. Single-expression handlers only (a multi-statement body may log or set a flag; reading inside it is where a grep starts guessing) and `.catch(() => null)` deliberately untouched. No per-file exemptions. **Proven to fail against the pre-fix tree: 7 hits** (see below). |
| `web/components/odontogram.tsx` | Catalogue failure recorded, not emptied — with no catalogue every odontogram plan seed's cost falls back to **0**, so « Créer un plan depuis l'odontogramme » silently quoted free treatment. Inline notice beside that action. |
| `web/components/patient-record-modal.tsx` | Catalogue failure recorded; notice above `ActSlot`, because an empty picker is what pushes the dentist into a free-text act with no tarif and no état résultant. |
| `web/components/treatment-plans/plan-workspace.tsx` | Same, on the Actes card: with no catalogue « Planifier » books a visit with no procédure. |
| `web/components/treatment-plans/treatment-plan-form-modal.tsx` | Its three picker reads (`dentalActs`, `procedureTypes`, `patients`) moved to one `Promise.allSettled` loader + one `pickersFailed` banner. |
| `web/components/factures/invoice-form-modal.tsx` | `patientsFailed` + `patientsReload`, mirroring the file's existing `proceduresFailed`. The patients read was **split into its own effect** so retrying cannot re-seed the line rows (the reason the procedures effect already documents). |
| `web/components/document-editor-content.tsx` | `loadPatients` records failure instead of `console.error` + `setPatients([])`; « Aucun patient disponible » no longer stands in for a dead read. |

**The check, before the fixes** (`node scripts/check-responsive.mjs --only=failed-read-as-empty`):
```
✗ failed-read-as-empty P1
    components/factures/invoice-form-modal.tsx:175            .catch(() => setPatients([]))
    components/odontogram.tsx:142                             .catch(() => setProcedureTypes([]))
    components/patient-record-modal.tsx:188                   .catch(() => setProcedureTypes([]))
    components/treatment-plans/plan-workspace.tsx:126         .catch(() => setProcedureTypes([]))
    components/treatment-plans/treatment-plan-form-modal.tsx:220  .catch(() => setActs([]))
    components/treatment-plans/treatment-plan-form-modal.tsx:226  .catch(() => setProcedureTypes([]))
    components/treatment-plans/treatment-plan-form-modal.tsx:233  .catch(() => setPatients([]))
```
It passes now.

### L1d — Surface allergies where the decision is made
| File | Change |
|---|---|
| `web/components/patient/patient-alert-panel.tsx` | **New shared** « Alertes médicales » panel (allergies · active flags · antécédents), extracted from the inline block in `patient-record-modal`. Two bugs fixed on the way out: the flag badge printed the raw enum (« HighPriority ») instead of `patientFlagLabel`, and the allergy line used `red-700` + a hand-maintained `dark:` twin instead of `text-destructive`. |
| `web/components/document-editor-content.tsx` | Renders the panel for **every** document type once a patient is selected — the ordonnance is written here and read `patient.allergies` nowhere. Also **prefills** the liaison letter's « Traitement en cours et allergies connues » from the patient's allergies + antécédents (fill-if-empty, same rule as the ordre number and the sexe). |
| `web/components/patient-summary-modal.tsx` | Renders the panel **above** the identity card. It previously showed no allergies, flags or antécédents at all, and it is the one-click quick look from the patients list and the phone ⋯ menu. |
| `web/components/patient-record-modal.tsx` | Inline block replaced by the shared panel. |

### L2a — Rescheduling a no-show re-marked it absent (Blocker)
| File | Change |
|---|---|
| `web/components/edit-appointment-dialog.tsx` | **The root cause.** New `hydratedStatus`, and the payload sends `status` **only when the user changed it** (`status !== hydratedStatus ? status : undefined`). The Select is seeded from the appointment, so a user who only moved the date still left it holding « Absent » and the form re-asserted it. |
| `api/.../Commands/UpdateAppointmentCommand.cs` | **Defence in depth, order-independent.** The status block is now guarded on `newStatus != oldStatus` — the status the *caller was looking at* — not on `appointment.Status`, which the date block above has already moved. `Reschedule()` correctly drops `NoShow → Scheduled`; the old comparison then found `Scheduled → NoShow` legal and marked the patient absent again. A statement about the *request* cannot be re-broken by reordering the blocks, which the client fix alone cannot promise for the other callers (Google sync, the AI dispatcher, jobs). |

Three knock-ons disappear with the status, none needing its own fix — which is what confirms the root cause was
the right thing to change:
- the outbound reminder took `VoidForAppointmentAsync` instead of being re-enqueued for the new day (it now falls
  to the `dateChanged` branch);
- the hard collision guard **skips `NoShow`**, so the freed slot could be given away twice;
- so did the working-hours check.

A genuine `Scheduled → NoShow` still works (posted ≠ hydrated), and reactivating a cancelled appointment still
routes through `Reactivate` — both traced through the handler by hand, per the spec's edge cases.

### L2b — A recurring series booked over existing patients (Blocker)
| File | Change |
|---|---|
| `api/.../Appointments/AppointmentScheduling.cs` | **The one authority, rewritten.** `if (!doctorId.HasValue) return null;` — "an unassigned busy slot belongs to nobody" — is replaced by **`CompetesFor`**: an unassigned booking is not *nobody's*, it is **everybody's** (that is what a « créneau occupé » block *is*). A candidate with no practitioner collides with anything in the clinic; a candidate with one collides with that practitioner's bookings **and** the clinic's unassigned ones. The scan is now fetched clinic-wide over the same narrow window and narrowed by `CompetesFor` in memory. `MaxCredibleAppointmentLength` and `CompetesFor` made public (one constant, two readers). `SlotTakenMessage` gained a second wording — « déjà occupé au cabinet » — chosen from the **colliding** row, because « pour ce praticien » is simply false about a block. |
| `api/.../Commands/CreateRecurringSeriesCommand.cs` | The `existing` load and **both** collision branches were gated on `request.DoctorId.HasValue`, which was *always* false (the form had no such field) — so all of it was dead code and the outcome panel's « conflits » list was unreachable. Gates removed, `CompetesFor` asked instead, window backed off by the shared constant. New **`AllowOverlap`** property (see below). A forced occurrence gets `MarkBookedWithOverlap()`, which is the term in the DB exclusion constraint's predicate that makes the write possible. |
| `api/.../Commands/UpdateAppointmentCommand.cs` | Removed `&& appointment.DoctorId.HasValue` from the collision condition — a **second copy** of the gate. Removing it from the helper alone would have left this path exempt and the two disagreeing about one rule. |
| `web/app/recurring-series/page.tsx` | New **required Praticien field** (`useDoctors`), sent as `doctorId`; validation + a one-line reason under the field. `handleSubmit` split into `submit(allowOverlap)`, and the outcome panel gained « **Créer malgré N conflits** » — the caller `allowOverlap` never had. Conflicts heading no longer says « pour ce praticien ». |
| `web/components/create-appointment-dialog.tsx` | The « créneau occupé » banner said « Aucun patient ne pourra être assigné à cette période » — a promise the product could not keep (the block prevented nothing, and the DB constraint is predicated on `DoctorId IS NOT NULL`). Two truthful wordings now, scoped to the practitioner or to the whole cabinet, and neither claims an absolute: the guard is advisory and overridable by « Continuer quand même ». |

**`allowOverlap` was wired, not deleted** (the spec's fork). It had existed on `CreateRecurringSeriesPayload` with
no counterpart on the command, so the client had been sending it into a void — unnoticed because the check it
overrides was itself dead. Wiring is the right half: a series is now the writer whose occurrences collide *most*
often, and the recurring path would otherwise be the only one of the three with no override. It ships **with its
caller** in the same change, which is the `Clinic.SetStockExpiryLeadDays` failure the spec names explicitly.

⚠️ **The guard deliberately diverges from the database constraint, one-directionally.** The exclusion constraint
cannot see either unassigned case, so this refuses a *superset* of what the constraint refuses — never the
reverse. The spec's edge case ("must not reject a slot the DB constraint would allow") is about the **status** set,
and `OccupiesSlot` is still the only answer to that. Recorded as DEV-3.

Existing tests were checked rather than assumed: `AppointmentOverlapOverrideTests` mocks
`GetByClinicIdAsync` with `It.IsAny<Guid?>()` for the doctor parameter and books a real practitioner against a
same-practitioner clash, so `doctorId: null` still matches the mock and `CompetesFor` still returns true. No
regressions; no test edits needed.

### L3a — The queue no longer starves silently (Blocker)
The fix is the spec's **recommended** fork: a distinct non-terminal status, which is the only shape that keeps
*both* original intentions (the row survives and records why · it stops occupying the queue).
| File | Change |
|---|---|
| `Domain/Enums/NotificationStatus.cs` | New **`Blocked = 4`**, documented as the resolution of two individually-correct decisions that together starved the queue. |
| `Domain/Entities/Notification.cs` | `MarkAsBlocked(reason)` + **`Unblock()`**. `MarkAsBlocked` deliberately does **not** touch `RetryCount` — no attempt was made, and spending the budget would let a misconfiguration silently exhaust a reminder's retries. |
| `Domain/Repositories/INotificationRepository.cs` | `GetPendingNotificationsAsync(take)` → **`GetDueForDispatchAsync(batchSize, perClinicBound)`**, plus **`GetBlockedForReviewAsync(take)`**. `ReminderLogCounts` gains `Blocked`. The purge's doc now states why `Blocked` was out of scope from birth: the predicate names the two *terminal* statuses rather than excluding `Pending`. |
| `Infrastructure/Repositories/NotificationRepository.cs` | The per-clinic fair share. One `GROUP BY` gives « which clinics have work waiting and how old their oldest row is »; clinics are then served **oldest-due-first**, each capped at `perClinicBound`, and the merged result is re-ordered by due time. ⚠️ The **single-clinic install keeps the original flat query** (`backlog.Count <= 1`) — a fair share between one participant is the whole batch, and the loop would only add a round trip to prove it. Blocked count added to the counters. |
| `Infrastructure/Services/RemindersConfig.cs` | `PerClinicDispatchBound` (20 against a batch of 50 → no clinic can take more than 40 % of a tick) and `QuietHoursLocal` (see L3c). |
| `Infrastructure/Services/ReminderScheduler.cs` | **`IsSendable` now gates the appointment path too**, not only the recall path — the spec's third bullet. Its doc block records the trade honestly: the « channel configured in the meantime » case that justified the old behaviour is now served by `Unblock()` on the rows already in the table, which is strictly better than creating rows that can only be parked. |
| `API/BackgroundJobs/NotificationJob.cs` | The three « leave it Pending » returns become `BlockAsync(reason)` with a French reason (unsupported channel · channel disabled for this clinic · credentials missing). New **`ReviewBlockedRowsAsync`** runs after the dispatch loop, bounded by the same batch size, and unblocks rows whose channel is sendable again — reading the resolved settings' own `SmsConfigured`/`WhatsAppConfigured`, so « why is this blocked? » and « why won't it send? » cannot be different answers. |
| `Application/DTOs/ReminderStatusDto.cs`, `ReminderLogDto.cs`, `Features/Clinics/Queries/*` | `ReminderDeliveryStatus.Blocked`, the `Blocked` counter, the mapper arm, and `blocked` as a tolerated `status` filter value. |
| `web/lib/api/reminder-settings.ts` | `'blocked'` on the status union; `blocked: number` on the log DTO. |
| `web/components/rappels/reminder-log-table.tsx` | « Bloqué » label, amber pill/stripe, and a new **`REASON_CLASS`**: the reason line's tone follows the **status**, so « le canal SMS n'est pas configuré » is not printed in the same red as a patient who was never reached. |
| `web/app/rappels/page.tsx` | Fourth counter « **Bloqués** » + its filter chip + the `?status=blocked` deep link. `KpiGrid` goes `columns={4}` with `sm:grid-cols-2 lg:grid-cols-4` — four figures at 320 px would be four 80 px columns and « Envoyés aujourd'hui » does not fit in one. |

⚠️ **A `Blocked` row is also voided on cancel/reschedule** (`VoidUnsentAsync` now matches `Pending or Blocked`).
Required, not tidy: a parked row still carries the body and send time frozen at enqueue, so surviving a move it
would later be unblocked and announce the old hour — L3b's defect re-entering through L3a's door.

### L3b — A moved appointment no longer sends the old day (Blocker)
Both halves, per the spec: the write path *and* the dispatcher backstop.
| File | Change |
|---|---|
| `Infrastructure/Services/GoogleCalendarSyncService.cs` | **`IReminderScheduler` injected** — it was not in this class at all. A Google-side **move** now void-and-re-enqueues (`RescheduleRemindersAsync`, and only when the *time* changed: a notes-only edit leaves the queued reminder correct and re-enqueuing would reset a tier already reached); a Google-side **create** now enqueues. A busy slot with no patient is skipped rather than passed a null id. |
| `Infrastructure/Services/ReminderMessage.cs` | **New.** One formatter for the moment a reminder states, **shared with the scheduler that writes it** — a second copy of the format string would make every reminder read as stale. `AnnouncesStaleMoment` returns true only when the body carries both a `dd/MM/yyyy` and an `HH:mm` token and the current moment is absent, so a clinic whose custom wording omits `{date}` has nothing to be wrong about and keeps its reminders. |
| `API/BackgroundJobs/NotificationJob.cs` | The backstop: a stale row is **failed with a French reason and surfaced**, not dropped silently. Failed rather than blocked, because nothing an operator configures makes a stale body true; surfaced (unlike the cancelled/no-show void) because the patient **is** still expected and no other part of the product would say they got nothing. |
| `Application/Features/Appointments/Commands/CancelRecurringSeriesCommand.cs` | The missing `VoidForAppointmentAsync`, per cancelled occurrence, post-commit. Cancelling a 12-visit series produced months of « Échecs (7 j) » for reminders that were *correctly* suppressed — which is how a dentist learns to ignore the counter that warns about the real ones. |

### L3c — Multi-tier reminders send every tier (Major)
| File | Change |
|---|---|
| `Infrastructure/Services/ReminderSchedule.cs` | `ComputeSendTimeUtc → **ComputeSendTimesUtc**`, returning `IReadOnlyList<ReminderSendTime>` — one entry per future tier, earliest first, plus the `PromptLeadHours = 0` fallback. Duplicated and non-positive tiers are ignored (the settings field is hand-typed « 24, 6 »). |
| ” | **The quiet-hours floor.** Default 21:00→08:00 clinic-local, configurable, equal bounds = off. ⚠️ It moves a send **earlier first** (to the window's own start) and only then later (to its end, if that still clears min-lead) — for the motivating case, an 08:00 appointment booked ~22 h ahead, pulling back to 21:00 the evening before reaches the patient whereas pushing to 08:00 *is* the appointment. A tier it cannot place is dropped, never moved into the window. |
| `Infrastructure/Services/ReminderScheduler.cs` | One row per **(sendable channel × tier)**. **Idempotency on (appointment, channel, tier)** — and the tier's identity on the wire *is* its send instant, so no column was needed. ⚠️ The voided ids are threaded from `VoidUnsentAsync` into the enqueue: `RemoveAsync` only *stages* the delete, so the dedup read still sees those rows (EF resolves the query onto the same tracked, `Deleted` instances) and a dedup counting them would skip re-creating exactly the reminders the reschedule exists to replace. |

### L3d — The assistant reports the clinic's hour (Major)
| File | Change |
|---|---|
| `Infrastructure/Services/AIActionService.cs` | One private **`FormatClinicMoment`** (`ClinicClock.ToClinicLocal` + `dd/MM/yyyy 'à' HH:mm`) replaces the raw-UTC ISO prints at **four** sites, not the three the spec names: `list_appointments`, the disambiguation list before a cancel, the cancel confirmation — **and the create confirmation**, which had the same defect wearing a long-month format. Plus `FormatClinicDay` for the bare filter echo, which is a date and needs no conversion. |

### Existing tests updated (not deferred)
| File | Change |
|---|---|
| `UnitTests/Infrastructure/Services/ReminderScheduleTests.cs` | Rewritten for the multi-tier contract: every future tier returned, ordering, a past tier dropped while the rest survive, duplicate/non-positive tiers ignored, and four quiet-hours cases including a **full-day sweep** asserting nothing ever lands inside the window and a sweep asserting no tier resolves before « now ». |
| `UnitTests/Api/NotificationJobTests.cs` | The due-scan mock renamed + the per-clinic bound; the two « leaves the row Pending » tests become « **parks** the row » (Blocked, reason recorded, retry budget untouched); six new tests — stale body refused, accurate body still sent, a blocked row unblocked when the channel becomes sendable, a blocked row that stays blocked while it cannot (so the review pass cannot become an unblock/reblock cycle), and the per-clinic bound being asked for. |
| `UnitTests/Features/Recall/RecallDeliveryTruthTests.cs` | Same repository-mock rename. |

### L4 — Backup, restore, and proof it ran (closes the 4th sale-blocker)

**Schema (one migration, `20260803211130_AddBackupRunsAndSchedule`).** New `BackupRuns` table (indexed
`(ClinicId, StartedAt)`, FK cascade to `Clinics`) + four `Clinic` columns.
⚠️ **Two deliberate edits to what `dotnet ef` scaffolded**, both load-bearing and both recorded in the migration's
own doc block: (a) the differ emitted `xmin = table.Column<uint>(…rowVersion…)` inside `CreateTable`, which
PostgreSQL **rejects** — the same trap that forced `AddConcurrencyToken` to ship with an empty `Up()`; (b) the four
int columns scaffolded as `defaultValue: 0`, which would have shipped the feature **switched off** for every
existing clinic with a retention of **0** — the single value the pruner's floor exists to survive. Defaults now come
from `Clinic.DefaultBackup*` so the column and the constructor cannot disagree.
`dotnet ef migrations add` **did work** here (contrary to `AddProcedureTypeCategory`'s note) — the model snapshot
and Designer are EF-generated, only `Up()` was hand-edited.

| Item | File | Change |
|---|---|---|
| **L4a** | `Domain/Entities/Clinic.cs` | `BackupEnabled` · `BackupHourLocal` (clinic-local) · `BackupRetentionCount` · `BackupStaleAfterHours`, one `SetBackupSettings` mutator (they are one decision; a per-field setter invites a screen that saves half of it) + `DefaultBackup*` constants. |
| | `API/BackgroundJobs/BackupJob.cs` | **New.** Per clinic: run if due → prune to the retention count → evaluate staleness. Not connectivity-gated (the output is a local file). ⚠️ **Registered `Cron.Hourly`, not daily** — the hour lives on the *clinic*, so one daily cron could honour only one clinic's choice, and it also serves the case a fixed 02:00 cron never can: **a clinic PC switched off overnight is backed up the first hour it is on**. `IsDueAsync` asks three questions cheapest-first (already succeeded in the clinic's own day? · an attempt inside the 6 h quiet window? · has its hour arrived?) so an hourly job neither double-backs-up nor writes twenty failure rows a day. |
| | `API/Program.cs`, `Infrastructure/Extensions.cs` | The recurring registration + `IBackupRunRepository`. |
| | `Application/Features/Backup/Commands/SetBackupScheduleCommand.cs` + `PUT /api/backup/schedule` | **The caller the four columns ship with.** The spec names `SetStockExpiryLeadDays` as the failure not to repeat — zero production callers, so the expiry window has been permanently 30 days; I confirmed that by grep before writing this (`SetStockExpiryLeadDays` still has none). |
| **L4b** | `Infrastructure/Services/PgDumpBackupService.cs` | **`ResolveDestinationRoot`** — argument → `Backup:DefaultDestination` → **`LocalInstallPaths.Resolve("Backups")`**. It used to *throw* when both were empty **while the installer wrote the key as `""`**, so the documented « leave it blank » path failed on every fresh install. Exposed on `IBackupService` because two other things must name the same folder (the settings panel's promise, and the printed restore command) — a second resolution rule is a printed path that does not match where the file went. Plus the **same-volume warning**, and `Warning` now *accumulates* (two warnings can apply at once; a single assignment silently dropped one). |
| | `packaging/server/clinic-server.iss` | `{app}\api\Backups` created **and hardened** with the other data directories — a backup is a full copy of every patient record. |
| **L4c** | `PgDumpBackupService.VerifyDumpAsync` | **`pg_restore --list` on the output, and a failed verification is a failed backup** (the existing catch deletes the partial folder). Records the **object count**, not a boolean. `pg_restore.exe` is found beside the configured `pg_dump.exe` (`Backup:PgRestorePath` overrides); if it genuinely is not there the backup **fails** rather than reporting an unverified success — a success that means less than it says is what L4c removes. |
| **L4d** | `Domain/Entities/BackupRun.cs`, `Enums/BackupOutcome.cs`, `Repositories/IBackupRunRepository.cs` + impl | The ledger. ⚠️ **No « Skipped » outcome**: a destination on a removable drive absent at 02:00 records a **`Failed`** run, because in a history list a skipped run is indistinguishable from a successful one. `MarkSucceeded` **requires** the verified count — the only way to keep « unverified is not a success » true is to make the proof impossible to omit at the call site. `GetLastSuccessfulAsync` orders on `CompletedAt` (a long dump that started earlier and finished later would otherwise report the wrong last-success). |
| | `BackupNowCommand`, `GetBackupHistoryQuery`, `GET /api/backup/history` | The manual path records a run too (⚠️ the `Running` row is **committed before** the dump starts, so a crash mid-backup leaves a visible row rather than none) and clears the staleness alert. The history read also returns the **resolved default destination** and the schedule, which is what lets the panel print both. |
| | `INotificationGenerator.Ensure/ClearBackupStaleAsync` + `NotificationCategory.BackupStale` + `NotificationTargetKind.BackupSettings` | An **ensure/clear pair**, exactly like `EnsureStockExpiringSoonAsync` and for the identical reason — staleness is crossed by the passage of time, so a fire-once call would write one alert per day for ever. Idempotency matches the **stable message prefix**, not the whole message (which carries a ticking elapsed count). ⚠️ **Two wordings**: « aucune sauvegarde » on a clinic that never had one, and a clinic that has **never** backed up is measured **from its creation** — the alarming version firing on a clinic created five minutes ago is how an alert gets dismissed for ever. |
| | `StaffNotification.RestateStockExpiry` → **`Restate`** | It was never about stock; it is the one operation « ensure » needs beyond create, and a second byte-identical mutator is how two categories start disagreeing about what restating means. |
| | `web/components/backup-settings.tsx` (rewritten), `web/lib/api/backup.ts`, `notification-panel.tsx`, `dashboard-header.tsx` | The card now **leads with « Dernière sauvegarde réussie »** rather than with the button; adds the schedule form, the history list (failures included, reason in the row), and the restore command. The bell's new category gets its icon, its tone and its deep link. |
| **L4e** | `API/Startup/InstallConfiguration.cs` (**new**), `Program.cs`, the 7 console verbs, `clinic-server.iss` | **See DEV-5** — the config is split by *ownership* (`appsettings.Install.json` installer-owned and always rewritten · `appsettings.Production.json` operator-owned, written once when absent and never truncated) instead of merging JSON in Pascal. Any existing operator file is copied to `.bak-<timestamp>` before anything happens, and the generated operator file now carries **`Security:EnableHsts`**, `Cors:AllowedOrigins`, `Hosting:TrustPort` and the reminder defaults — every key the README tells operators to hand-edit. |
| **L4f** | `clinic-server.iss` — `PrepareToInstall` + `StopClinicServices` | `[Files]` copied the whole `api\`, `web\` and `node\` trees **while the services were still running**; the only teardown lived in `SetupAppServices`, which runs from `ssPostInstall`, i.e. *after* the copy. Neither `.iss` had a `PrepareToInstall`, `CloseApplications` or `ServicesStopped` of any kind. Stops API then web, waits (`sc stop` returns when the control is *accepted*, not when the process has exited), and is deliberately tolerant of « service not found » (a first install has none). |
| | `API/Startup/DeferredStartupService.cs` | **Backs up before migrating, and aborts the migration if that backup fails** — through the same `StartupDiagnostics.ReportFatal` + `StopApplication` channel an unreachable database uses, so the DB is left exactly as it was. ⚠️ Only when `GetPendingMigrationsAsync` is non-empty (every ordinary restart runs this path, and dumping on each one would turn a service restart into a minutes-long operation), and **skipped on a first-run database with no applied migrations** — refusing to install because it cannot back up an empty database would be absurd. |
| **L4g** | `API/Maintenance/RestoreBackupCommand.cs` (**new**), `Program.cs` | `restore-backup <folder> [--force]`. **Every refusal happens before anything is destroyed**: validate the folder (`database.dump` present, `pg_restore --list` non-empty) → **refuse while the app is listening** → **safety dump** into `clinic-pre-restore-*` (deliberately *not* a `clinic-backup-*` name, so retention can never prune the one copy that exists to survive a regretted decision) → `pg_restore --clean --if-exists --no-owner` → `files/` (refused into a non-empty target without `--force`) → **bump every `TokenVersion`** → French summary, exit 0/1. Credentials come from the install's configuration, so no password is typed. The app-running check reads **TCP listeners**, not a service query: the app legitimately runs three ways and only one has a service name. |
| | `packaging/README.md` | The « Backup (in-app) » / « Restore (manual) » sections rewritten; **« There is no in-app restore » is gone**, replaced by the verb, its ordering guarantees and the automatic-backup contract. |
| | `verify-schema` (`ISchemaVerificationReader`, `SchemaVerificationReader`, `SchemaVerificationService`) | New **`backup-schedule-backfill`** check. The `BackupRuns` index and FK are diffed for free (the service reads the EF model), so only what the model cannot express was added: no clinic may be left with a retention or staleness threshold of **0**. Nullable, so before the columns exist it reads « not applicable » rather than a reassuring 0. |

### Existing tests updated + added (L4)
| File | Change |
|---|---|
| `UnitTests/Features/Backup/BackupNowCommandHandlerTests.cs` | Ctor widened; the success DTO now carries a verified count. **Three new tests**: a success records `Succeeded` + the count + clears the staleness alert; a **failure is recorded with its reason** and does *not* clear the alert (nothing about the data got safer); and the `Running` row is committed **before** the dump starts. |
| `UnitTests/Common/Maintenance/SchemaVerificationServiceTests.cs` | The twelve positional `DataMigrationCounts` constructions gained the new count, plus **two new tests** — a non-zero retention count is drift, and a null reads « not applicable ». |

### L5 — Data portability: **export** (the import half is not built — see "Resume here")

| File | Change |
|---|---|
| `Application/Common/Csv/CsvTable.cs` | **New.** The single CSV authority + `CsvCell`. Three decisions make the file open correctly on a Tunisian clinic's PC, and each is the difference between « it works » and an unusable file: **UTF-8 with a BOM** (Excel on Windows reads BOM-less UTF-8 in the system codepage — « Béchir » → « BÃ©chir », in a product whose every label is French), **`;`** as the delimiter (Excel's list separator follows the fr locale; a comma-delimited file opens as one column) and **CRLF**. ⚠️ Money goes through `InvoiceCalculator.RoundMoney` with **no thousands separator** — a space is what makes a spreadsheet treat the cell as *text* and refuse to sum the column, which is the entire reason an accountant asked for the file. Dates go through `ClinicClock`, or through `CalendarDay` (no conversion) for a value that is already a calendar day — converting a date of birth would simply make it wrong. |
| `Application/Common/Csv/ExportTables.cs` | **New.** One shape per list: patients · factures · créances · extrait de caisse · dépenses · rendez-vous · devis · stock · bons de prothèse. Pure and static, so a test can assert the header row and the money format with no controller. Two calls worth knowing: the caisse statement uses **separate Entrée/Sortie columns** and a voided row carries **neither** (a single signed column would make a void indistinguishable from a zero), and an appointment's acts are joined from **`Procedures`**, not the scalar — otherwise an exported « détartrage + 2 obturations » reads as one act. |
| `API/Controllers/ApiControllerBase.cs` | `Csv(table, baseName)` — the media type, the charset and the **clinic-local dated filename** stated once. An owner exports the same list repeatedly, and two files called `patients.csv` in one Downloads folder is how the wrong one reaches the accountant. |
| 7 controllers, 9 endpoints | `GET /patients/export`, `/invoices/export`, `/expenses/export`, `/treatment-plans/export`, `/lab-orders/export`, `/stock/export`, `/appointments/export`, `/billing/receivables/export`, `/billing/caisse/ledger/export`. ⚠️ **Each re-sends the screen's own query with no paging**, which the paging primitive already models as a first-class case — so « honours the filters, exports the whole filtered set, never the page » is true *by construction* rather than by discipline: the export never sees a page. Money-bearing ones (`invoices`, `treatment-plans`, `receivables`, `caisse/ledger`) are **`AdminOrDoctor`**, matching the reads they export — an unrestricted CSV would reopen spec I's hole from a different door. |
| `web/lib/api/export.ts`, `web/components/ui/export-button.tsx` | **New.** One authenticated blob fetch and one button for all nine. The **filename comes from the server** (`Content-Disposition`, preferring the RFC 5987 form so accents survive) rather than being re-derived — a second authority on it would be a second chance to use the browser's UTC date and name the file after yesterday. Downloads through `downloadBlob`, which is not a convenience: on iOS Safari `<a download>` on a `blob:` URL is **ignored**, so a hand-rolled anchor would silently deliver nothing on the tablet at the chair. |
| `/patients`, `/factures`, `/treatment-plans`, `/caisse` | Wired. ⚠️ `/treatment-plans` sends **both** date pairs (`from`/`to` bound creation, `acceptedFrom`/`acceptedTo` bound acceptance — dropping either exports a different set of devis from the one on screen), and `/caisse` deliberately does **not** send the free-text `search`: « Solde de la période » is computed over the whole window *before* filtering, so a text-filtered file would carry a running-balance column that sums to nothing. |
| **Not wired:** `/creances`, `/stock`, `/lab-orders`, `/appointments`, and `/depenses` | Their filters live inside a child table component rather than at page level, so each needs the button in the table's own toolbar or the state lifted. The endpoints exist and work; only the affordance is missing. Stated here rather than left to be discovered. |

## Quality checks (L5)
| Check | Result |
|---|---|
| `dotnet build ClinicManagement.API.csproj` | **0 errors** |
| `npx tsc --noEmit` | **0 errors** |
| `npm run check:responsive` | **11/11 pass** |
| `npm run build` | **success** |
| Tests | **NONE written for L5.** The spec asks for a round-trip (export → import → identical set), which cannot be written until the import half exists. What *is* worth testing independently and is not yet: `CsvTable`'s quoting (delimiter, embedded quote, newline, leading/trailing space) and `CsvCell.Money`'s three decimals + comma — both pure, both a `[Theory]`. |
| Eye pass | **NOT DONE.** For L5: the « Exporter » button beside « Ajouter un patient » at 320 px (it is `compact`, so it should collapse to the icon and the pair must not wrap into two rows), and the toast on a 403 from a money export. |

## Quality checks (L4)
| Check | Result |
|---|---|
| `dotnet build ClinicManagement.API.csproj` | **0 errors**, 14 pre-existing warnings |
| `dotnet build ClinicManagement.UnitTests.csproj` | **0 errors** |
| `dotnet vstest` (the SAC workaround — **it works right now**) | **1799 passed · 29 failed**, and every failure is outside this work. See « The suite actually ran » below: it caught three real defects in L3/L4/L5 code, all now fixed. |
| `npx tsc --noEmit` | **0 errors** |
| `npm run check:responsive` | **11/11 pass** |
| `npm run build` | **success** (`rm -rf .next` first) |
| `dotnet run -- verify-schema` | **NOT RUN** — needs a live PostgreSQL, which this environment has none of. It is the *only* gate for the migration, so it must be run before and after applying it and the outputs diffed. The new `backup-schedule-backfill` line is the one to look at. |
| **L4e/f/g operator verification** | **NOT DONE, and cannot be done here.** The spec states it plainly: install, configure, upgrade in place and confirm the operator config survived; confirm the services stopped before the copy; and **rehearse a full restore** (backup → wipe → restore → log in → verify a patient, an invoice and a stored file). None of that is CI-runnable and none of it was performed — there is no Windows installer build, no PostgreSQL and no Inno Setup in this environment. ⚠️ **`ISCC` has also not compiled the edited `.iss`**, so the Pascal in `PrepareToInstall` / `WriteInstallConfig` / `EnsureOperatorConfig` is unverified by anything. Per `inno-setup-brace-comment-bug` the new `{ }` comment blocks were written **without** any `{app}`/`{sys}` token inside them, which is the known compile-breaker, but that is reasoning rather than a compile. |
| Eye pass | **NOT DONE.** For L4 the surface is « Paramètres → Sauvegarde »: the headline banner, the three-up schedule row at 320 px (`sm:grid-cols-3`, so one-up below), and the restore `<pre>` — a Windows path plus a command must scroll **inside its own box**, never widen the page. |

## Quality checks (L3)
| Check | Result |
|---|---|
| `dotnet build ClinicManagement.API.csproj` (whole production graph) | **0 errors**, 14 warnings — all pre-existing, none in a file this session touched |
| `dotnet vstest` | Not run at the time of L3 — see « The suite actually ran » below; it was run at the end of the session and L3's own defect (the quiet-hours boundary) surfaced there. |
| `dotnet build ClinicManagement.UnitTests.csproj` | **2 errors, neither mine**: `AuditInterceptorTests.cs` (`CS1736`, a `Guid` as a default parameter value) and `InvoiceDebtIsAgedTests.cs` (`CS0246 ReceivableDto`) — both **untracked files belonging to the concurrent session**, which parked and un-parked them under me mid-build (see below). Every file this session touched compiles. |
| `npx tsc --noEmit` | **0 errors** |
| `npm run check:responsive` | **11/11 pass** |
| Eye pass | **NOT DONE.** For L3 the surface to look at is `/rappels`: four counters at 320 px (two-up, and « Envoyés aujourd'hui » must not clip) and five filter chips wrapping to two rows without the row losing its 44 px touch targets. |

⚠️ **The concurrent session is renaming test files to `.ktestrun` to run subsets.** Three of the files this
session had to update (`NotificationJobTests`, `ReminderScheduleTests`, `RecallDeliveryTruthTests`) were parked
*while* the first build ran, which is why that build reported 0 errors against an interface change that must have
broken them. The edits were made against the parked names and survived the rename back. **Do not read a green
test-project build as coverage without checking `ls` for `.ktestrun` files first.**

## Quality checks (L1)
Run from `web/` — per `.claude/rules/frontend-web.md` § 14 this is the whole gate (no test runner, no working
ESLint, no CI in `web/`).

| Check | Result |
|---|---|
| `npx tsc --noEmit` | **0 errors** |
| `npm run check:responsive` | **11/11 pass**, including the new `failed-read-as-empty` |
| `npm run build` | **success** (`rm -rf .next` first — a stale cache produced the `pages-manifest.json` ENOENT the rule file documents) |
| Eye pass at 320 / 390 / 820 / 1180 / 1440 px | **NOT DONE — outstanding for L1.** No browser in this environment; the mechanical check plus a re-read of the diff against § 1 is all that was possible. The one to look at first is the **Mixte arch**: 13 cells per quadrant, so ~1144 px + gaps on a coarse pointer, inside `ToothArchLayout`'s `overflow-x-auto` — it must scroll, never clip, and teeth 18/48 must be reachable at the scroll origin (`arch-clipping`). Second, the widened `isNarrow` gate at **820 px on a touch device**: the Maxillaire/Mandibule switch must now appear there. |

## Quality checks (L2)

| Check | Result |
|---|---|
| `dotnet build ClinicManagement.Application.csproj` | **0 errors, 0 warnings** |
| `dotnet build ClinicManagement.UnitTests.csproj` (compiles the whole graph incl. API) | **0 errors**, 14 warnings — all pre-existing, all in files not touched here (`ProcedureTypesController`, `MedicalDocumentsController`, `Program.cs`; the repo's `CS8602`/`CS8600`/`CS0618` baseline). Built with `-p:BaseOutputPath` per `ef-migration-scaffolding-hazards`. |
| `npx tsc --noEmit` | **Every L2 file clean.** ⚠️ The run is **not** green overall: 8 errors, all inside `web/components/document-editor-content.tsx`, all from the **concurrent session's** in-flight CNAM-picker refactor (`cnamNomenclatureFailed`, `setCnamNomenclatureReload`, `selectNomenclatureEntry`, `toLocalIso` — symbols they are adding, none of them mine). The line numbers moved between two runs minutes apart, i.e. they are still editing. Not touched: half-fixing someone's live refactor is worse than reporting it. **Re-run before committing.** |
| `npm run check:responsive` | **11/11 pass** |
| `npm run build` | Not re-run after L2 — it type-checks, so it cannot pass while the file above is red. It was green after L1. |
| Eye pass | **NOT DONE.** For L2 the surfaces to look at are the recurring dialog's new **Praticien** field and its outcome panel's two-button footer (`gap-2`, must not overflow at 320 px), and the reworded « créneau occupé » banner — the new copy is ~2× longer, so check it wraps cleanly in the warning box at 320 px. |

## ✅ The suite actually ran — and it caught three real defects

⚠️ **Correction to the note that used to be here** (and to the L1/L2 sections above, which say the build is the
only available signal): `dotnet test` still fails at assembly load, but the documented **`dotnet vstest`
workaround does work right now** — SAC's verdict is time-varying, exactly as `api/…UnitTests/CLAUDE.md` warns.
Do not take "the build is clean" as the gate again without trying it:

```bash
dotnet build ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj -p:OutDir=<scratch>/
dotnet vstest <scratch>/ClinicManagement.UnitTests.dll --logger:"console;verbosity=minimal"
```

**Final run: 1799 passed · 29 failed · 1828 total.**

**Three real defects it caught in this session's own code — none of which any other check could see:**

1. **`CsvTable` wrote no BOM.** `new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)` only changes what
   `GetPreamble()` returns — **`GetBytes` never emits it** — so every export was a BOM-less UTF-8 file that Excel
   on Windows reads in the system codepage, turning « Béchir » into « BÃ©chir ». The file is valid UTF-8 and opens
   correctly in everything that is not Excel, which is precisely why it would have shipped.
2. **The quiet-hours floor moved a send from one quiet instant to another.** The window is `[21:00, 08:00)`, so
   its own start hour *is* quiet; pulling a 01:00 tier back to exactly 21:00 satisfied the code and violated the
   rule. Caught by the full-day sweep (`Never_Places_A_Send_Inside_Quiet_Hours`), not by the single motivating
   case — which passed. The target is now 20:59.
3. **`ExportPlans` arrived unclassified.** `TreatmentPlansControllerAuthorizationTests`' drift guard fired, which
   is exactly what it was written for. A CSV of every devis with what each patient owes is the clinic-wide money
   read in a more portable form than the screen, so it is `AdminOrDoctor` and now named in the test's own table.

**Two of this session's own tests were wrong rather than the code:**
`Pulls_A_Send_That_Lands_In_Quiet_Hours_Back_To_The_Evening_Before` asserted 21:00 (see #2), and the two
`ReminderSchedulerTests` enqueue fixtures build their appointment from `DateTime.UtcNow` — so with the shipped
21:00–08:00 default a tier lands inside the window **at some times of day**, making the suite pass or fail
depending on when it runs. The harness now switches quiet hours off (equal bounds, the documented way) and the
floor is covered where the instants are fixed. Worth knowing: that is a *third* instance of the "a failing test
here was a stale fixture" pattern the test guide records.

**One pre-existing expectation was inverted deliberately:** `PgDumpBackupServiceTests.Missing_destination_fails_loud`
pinned « no destination → throw », which is the L4b defect itself — it threw for a configuration the installer
produced (`""`) while the UI promised the blank field worked. It is now
`No_Destination_Falls_Back_To_The_Install_Folder_Instead_Of_Refusing`, plus `An_Explicit_Destination_Wins`.

### The 29 remaining failures are not from this work
All 29 are list-query handler tests whose **mocked repositories do not apply the filters the production code now
delegates to SQL** (`GetCnamNomenclatureQueryHandlerTests` ×7, `GetMedicationsQueryHandlerTests` ×7,
`GetStockItemsQueryHandlerTests` ×2, `GetTreatmentPlansQueryHandlerTests` ×3, `CreditNoteReadTests` ×3, three
`*TenantIsolationTests` list cases, `PatientContactOptionalTests.Search_Survives_A_Patient_With_No_Phone`) plus
three `LiaisonRenderContentTests` failing on a **French label change** (« Motif » → « Motif de la liaison »).
Nothing this session touched is in those files or their production counterparts, and `git status` confirms those
areas carry in-flight modifications from other specs. They look like the same stale-fixture shape as above and are
worth a pass of their own — but they were red before this work and are red for reasons unrelated to it.

⚠️ **The concurrent session duplicated L1c's answer.** They added a local `HistoryLoadFailure` component inside
`edit-patient-dialog.tsx` — the same "failed read is not an empty state" treatment this session extracted into the
shared `ui/load-failure.tsx`. Two components for one fact is exactly the shape L1c set out to remove. Worth
reconciling before merge: `HistoryLoadFailure` should become a `LoadFailureNotice` call.

## Not committed
Per user preference this skill commits nothing. When committing, stage **by path** from the "Files Changed"
tables above — `git diff HEAD --numstat` first: the tree holds ~240 changed paths from other specs *and* from a
concurrent session.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| `RecordToothChart`'s prop renamed `isAdult` → `view` (both call sites updated) | The spec asks for a third view; a boolean cannot carry one. Internal prop, two call sites, same rendering for the two old values. |
| Mixed arch built by **interleaving** deciduous teeth into the permanent arch rather than adding a second row | Keeps `ToothArchLayout` unchanged (one row per arch) and puts every tooth at its anatomical position. Appending 55–51 after 11 would place baby teeth across the midline — a chart whose entire job is to say *which tooth* must not state an anatomy that does not exist. Sets are disjoint, so no FDI number appears twice. |
| `renderSectionEmpty` reused for the two antécédents sub-sections (a banner inside a compact card) | The page's own three-state helper; a lighter variant would be a second answer to the same question. |
| Folder-files failure reuses the existing `"files"` section flag rather than a new one | The same tab body renders both reads, so two flags would be two ways to say one thing and the tab would have to pick. |
| `LoadFailureNotice` extracted as a `ui/` primitive instead of copying the patient page's block | `EmptyState` already documents this as the third state; the treatment existing in exactly one file is what produced all seven `.catch(() => setX([]))` sites. |
| `PatientAlertPanel` extracted as a shared component instead of a third inline copy | Same reasoning; L1d needs it on three surfaces. |
| `document-editor-content`'s `loadPatients` (`console.error` + `setPatients([])`) fixed although the new check does not flag multi-statement handlers | Same defect, same file, open at the time; leaving it would have meant « Aucun patient disponible » still standing in for a dead read on the screen L1d was hardening. |
| L2a guards the status block on `newStatus != oldStatus` rather than reordering status-before-date (the spec offered either) | The comparison is a statement about the **request**, so it cannot be re-broken by a future reordering; moving the blocks would have re-derived correctness from execution order. Same observable behaviour for every legal transition — traced by hand through all four (genuine `→ NoShow`, reschedule out of `NoShow`, cancelled reactivation, cancelled no-op). |
| `MaxCredibleAppointmentLength` and `CompetesFor` promoted from `private` to `public` on `AppointmentScheduling` | The recurring command loads one window for the whole series and must back it off by the same amount. A second literal there is how a widened window silently applies to one path only. |
| `SlotTakenMessage` gained a second wording instead of a new parameter | Derived from `collision.DoctorId`, so all existing call sites keep working unchanged. |
| L2b's clinic-wide check applied to **create** and **update** as well as recurring | It lives in the one shared helper, which is what the spec asked for; the alternative is a per-caller opt-in, i.e. the drift the helper was extracted to end. It is also what makes a « créneau occupé » block finally block. |

## Significant Deviations

**DEV-1 — L1c's mechanical check forced 7 fixes the spec did not enumerate.**
*Spec:* L1c names five `.catch(() => [])` sites and requires a **derived** check, proven to fail first, with
**no per-file exemptions**.
*Found:* the shape actually shipped in this tree is `.catch(() => setX([]))`, and after fixing the five named
sites the derived check still failed on **7 more** (5 files): the act/CNAM catalogues and patient lists behind
the record modal, the odontogram, the plan workspace, the devis editor and the invoice editor.
*Chosen:* fix all seven. A per-file allow-list is banned by the rule file and by the spec; shipping the check
red contradicts § 14 ("a check that is red from birth is a check that dies"); and each site is a real instance —
an empty act catalogue is what makes a dentist type a free-text act with no tarif, and an empty odontogram
catalogue silently prices a devis at 0.
*Impact:* L1c touched 12 files instead of 6, and added one `ui/` primitive. No API or behaviour change on the
success path — every change is failure-path only.
*Approved:* not asked (the user's standing instruction for this spec is "do not stop"); recorded here for review.

**DEV-2 — L1b's backend half was already in the working tree.**
`UpdatePatientCommand.cs` already carried the `Address` tri-state and the shared `ToDto()` response, with
comments naming "the L1b payload audit". So a previous session had done the server side and left the client
sending `.trim() || undefined`. Only the client half was written this session. Worth knowing when reviewing:
the backend diff for L1b is **not** from this session.

**DEV-3 — L2b's guard is deliberately stricter than the database constraint.**
*Spec edge case:* "the clinic-wide collision check must not reject a slot the **DB constraint** would allow, or the
guard and the constraint disagree in the opposite direction. Reuse `OccupiesSlot`."
*Reality:* the constraint is predicated on `DoctorId IS NOT NULL`, so it can never see an unassigned booking — and
an unassigned booking is precisely what a « créneau occupé » block is. Any clinic-wide check therefore *must* refuse
slots the constraint would allow; that is the blocker, not a side effect.
*Chosen:* accept the divergence and make it one-directional and explicit. `OccupiesSlot` remains the single answer
on the **status** question (which is what the edge case is really guarding — a cancelled slot must stay rebookable),
so the two can never disagree about a booking that reaches the database: this guard refuses a superset, never the
reverse. It stays advisory (`SlotTakenCode` + `AllowOverlap`), and a forced row is stamped `BookedWithOverlap`,
which is the constraint's own exemption term.
*Impact:* two unassigned bookings that overlap now prompt for confirmation where they previously did not. That is
the intended behaviour change and the reason the banner copy in `create-appointment-dialog` was rewritten.
*Approved:* not asked (standing "do not stop"); recorded for review.

**DEV-5 — L4e splits the config by ownership instead of merging JSON.**
*Spec:* « Write only when absent; otherwise **merge** missing keys and leave existing values. »
*Chosen:* two files — `appsettings.Install.json` (installer-owned, machine-derived, always rewritten) loaded
*beneath* `appsettings.Production.json` (operator-owned, written once when absent, never truncated, backed up to
`.bak-<timestamp>` before anything happens).
*Why:* a JSON merge would have to be implemented in Inno Setup's Pascal, which has no JSON parser — and the merge
has no correct answer for a key the operator **deliberately removed** (the spec's own edge case): re-adding it
overrides their decision, skipping it means a genuinely new key never arrives. The split makes the question
disappear, and it is strictly stronger on the requirement that matters — the operator file is now never written a
second time at all. New installer-owned keys still arrive on every upgrade, which a write-once operator file could
not have delivered.
*Cost:* one new API type (`Startup/InstallConfiguration.cs`) and one extra config layer, threaded through
`Program.cs` **and all seven console verbs** — a verb reading one layer fewer would resolve a different connection
string from the app it is maintaining.
*Approved:* not asked (standing "do not stop"); recorded for review.

**DEV-6 — L3a's enqueue gate trades one behaviour for another, deliberately.**
`EnqueueRemindersAsync` now refuses to create a row for a channel that cannot send, per the spec's third bullet.
The behaviour that loses: a reminder enqueued while SMS was unconfigured no longer exists to be sent if SMS is
configured an hour later. `IsSendable`'s own doc block used to state that case as the reason the appointment path
did *not* check. It is preserved for the rows already in the table (`Notification.Unblock()`), which is where the
value actually was; creating rows that can only be parked is not a feature.

**DEV-4 — a concurrent session is editing the same tree, and `tsc` is red because of it.**
Not a deviation in the work, but it changes what the gate proves. See "Quality checks (L2)" and the working-tree
note: 8 errors, all in `document-editor-content.tsx`, all from symbols another session is mid-way through adding.
Every file this session touched typechecks. **Re-run `npx tsc --noEmit` and `npm run build` before committing**, and
reconcile their duplicated `HistoryLoadFailure` with `ui/load-failure.tsx`.

## Deferred to `/test-small-feature`
Per the spec's Testing section, for L1:
- `UpdatePatientClearsAllergiesTests` — both directions (`""` clears, an omitted key leaves unchanged) **and**
  the shared-branch case (clearing `Allergies` must not blank `MedicalHistory` — they share one
  `if (… != null || … != null)` and one `UpdateMedicalHistory(a, b)` call).
- A test **asserting** `DentalRecordActParser` permits mixed dentition, so a future tidy-up cannot narrow the
  server back to what the old UI could express.
- Address tri-state on update (`null` clears, omitted keeps) — the L1b payload audit's second finding.

For L2:
- Rescheduling out of `NoShow` **preserves `Scheduled`** — the blocker itself — *and* now reaches the collision
  guard (the old code skipped it because `Status == NoShow`). Two assertions, one scenario.
- A genuine `Scheduled → NoShow` still works, so the guard did not simply disable the transition.
- A reschedule out of `NoShow` **re-enqueues** the reminder rather than voiding it (`RescheduleForAppointmentAsync`,
  not `VoidForAppointmentAsync`).
- A recurring series with **no** `DoctorId` reports conflicts — the assertion the dead code made impossible.
- `AppointmentScheduling.CompetesFor` as a unit: unassigned-vs-anything, named-vs-same, named-vs-other (no
  collision), named-vs-unassigned (collision). Four cases, one table.
- `AllowOverlap = true` on a series **creates** the colliding occurrence, still reports it, and stamps
  `BookedWithOverlap` — without that flag the row cannot pass the exclusion constraint.

---

# Session 2 — 2026-08-04 · L5 finished (export's last five buttons + the whole import half)

**Branch:** `feature/audit-sections-3-to-10` (unchanged — same standing instruction).

## Working tree note (start of session 2)
397 changed paths, still the in-flight work of specs I/J/K plus the mobile-redesign commits that have since
landed (`cc8352d`, `1dfb51b`, `0f15214`, `91e7d52`, `d877ecd`). Nothing was staged; stage by path.

## Files Changed — L5 export, the five that were « Not wired »

| File | Change |
|---|---|
| `web/components/creances/receivables-table.tsx` | « Exporter » beside the search box it exports, sending `debouncedSearch` (the value the request actually carried, not the keystroke). At 320 px the input takes its own row and the button wraps under it. |
| `web/components/stock-table.tsx` | Button at the end of the card header's filter cluster, sending all four filters read from the **same variables `loadItems` sends** — same `debouncedSearch`, same `undefined`-when-off shape. |
| `web/app/lab-orders/page.tsx` | This page owns its filters, so the button sits in the header cluster beside « Nouveau bon » (`search` + `status`). |
| `web/app/caisse/page.tsx` | A **second** export on the page — the « Dépenses » card's own, `/expenses/export` with `from`/`to`/`search`. ⚠️ Unlike the extrait's it **does** send the search term, because the expenses list is loaded narrowed by it; the extrait cannot, since « Solde de la période » is computed before filtering. |
| `web/components/appointment-calendar.tsx` | The agenda's export lives **on the calendar**, not the page: `startDate`/`endDate` are derived there from `view` + `selectedDate` + (on a phone's Mois) `monthsAhead`, and a page-level copy of that derivation would disagree the moment the phone lazily loaded another month. Same `startOfDay`/`endOfDay`/`toISOString` transform `useAppointments` applies. |

**The placement rule this settles, stated once:** an export button goes **where the filter state lives**. Four
pages hold their filters at page level and put it in `PageHeader`; five hold them inside a table component and put
it in that component's own toolbar. Lifting a copy of the filters to satisfy a placement convention would create a
second authority on « what is on screen », and the one property the file must have is that it *is* the list.

## Files Changed — L5 import (new)

| File | Change |
|---|---|
| `Application/Common/Csv/CsvReader.cs` | **New.** The reader half of `CsvTable`, in the same folder so the two halves of the round trip are read together — and deliberately **not** symmetrical with it. The writer controls its one shape; the reader is handed whatever the previous software produced, so three things are *detected*: the **delimiter** (`;`/`,`/tab, counted outside quotes on the **header record only** — counting the whole file lets one address containing a comma outvote the real delimiter; ties go to `;`), the **encoding** (strict UTF-8, falling back to **Latin-1** on `DecoderFallbackException` — a BOM-less file saved by Excel on a French Windows is cp1252 and dies on the first « é »; `throwOnInvalidBytes` is the whole mechanism, without it .NET substitutes U+FFFD and « Béchir » silently becomes « B?chir »), and the line ending. RFC 4180 quoting via a one-pass state machine that tracks the physical line, so every row can name where it came from. A blank trailing line is dropped (every spreadsheet writes one); a file over the 5 000-row cap sets `Truncated`, **surfaced not obeyed**. |
| `Application/Features/Patients/Import/PatientImportFields.cs` | **New.** The 18 mappable fields = **exactly the writable half of `ExportTables.Patients`**, which is what makes « export → import → identical set » a property of the design rather than a coincidence (« Archivé » and « Inscrit le » are the two export columns absent, both the product's own bookkeeping). English tokens, French labels. Header auto-detection folds through `SearchTerm.Normalize` — the existing accent authority, so the import cannot disagree with patient search about « Prénom » vs « prenom ». ⚠️ **Longest alias wins and a column is claimed once**: « Téléphone » vs « Téléphone d'urgence » — a first-match scan would file a relative's number as the patient's and then send that relative every reminder. |
| `Application/Features/Patients/PatientGender.cs` | **New.** Both directions of the `Male` ↔ « Homme » map, server-side. The L5 export was writing the raw token into a French file one column over from a `YesNo` translated on purpose. Both directions in one file because a formatter without its parser would make this product's own export the one file it cannot re-read. `Parse` returns **null** rather than `Unknown` for an unreadable value, so the caller can tell « the column said nothing » from « the column said something I could not read » — the second earns a row warning. |
| `Application/Features/Patients/Import/PatientImportRowReader.cs` | **New.** Row + mapping → a `CreatePatientCommand` + French errors + French **warnings**. Pure and static, so the dry run and the commit read a row through identical code. The warnings channel exists because every one of them is a place `CreatePatientCommand` is deliberately *lenient* (a partial address becomes `null`, an empty date of birth becomes « 30 ans ») and lenience nobody is told about is how 3 000 patients arrive with a birth year the practice never supplied. ⚠️ **`MM/dd/yyyy` is deliberately absent** from the accepted date formats: indistinguishable from `dd/MM/yyyy` for the first twelve days of every month, so accepting both would silently move a birthday for two thirds of a practice. ⚠️ **Phones are normalised to `+216` E.164 on the way in**, which the hand-typed write path does *not* do — the spec names that standing defect and forbids replicating it. The patient's own bad number is an **error**; a relative's is a **warning** kept verbatim, because nothing dispatches to it and refusing a clinical record over « 71 555 (bureau) » is the worse trade. |
| `Application/Features/Patients/PatientFromRequest.cs` | **New — extracted, not copied,** out of `CreatePatientCommandHandler`, which is now its other caller. See DEV-7. |
| `Application/Features/Patients/Import/PatientImportPlanner.cs` | **New.** Bytes + mapping + the clinic's `PatientIdentity` list → a decision per row. **The one implementation both endpoints use**, which is the entire value of the dry run: a preview built by different code is a promise the commit need not keep. Duplicate matching is deliberately **eager** — name+DOB, name alone when the row supplies no DOB, or phone — and rows are matched against **each other** as well as against the database. The asymmetry is the design: a false positive costs one checkbox, a false negative is permanent (no merge, no soft delete, and deleting the loser is refused as soon as anything attaches to it). An out-of-range mapping index is **refused**, since it means the client is mapping against different headers than this file has; a missing required field is refused for the *file*, because 3 000 identical row errors is a worse way to say « you have not mapped Nom ». |
| `Application/Features/Patients/Import/PatientImportMapping.cs` | **New.** Plan → wire, one mapper for both endpoints so the report after an import is the same shape as the preview taken before it. ⚠️ **Invalid outranks duplicate**: offering « Créer quand même » on a row with an unreadable date of birth would offer an action guaranteed to fail. The delimiter is *named* (« point-virgule (;) ») because a tab is invisible in a UI and a mis-detected delimiter is the likeliest reason an import looks broken. |
| `Application/DTOs/PatientImportDtos.cs` | **New.** `PatientImportRowOutcome` (Ready/Duplicate/Invalid + Created/Skipped/Failed), the row, the preview, the field descriptor, the result. |
| `Application/Features/Patients/Queries/PreviewPatientImportQuery.cs` | **New.** The dry run. ⚠️ **A query even though the endpoint is a POST** — `RealtimeBroadcastBehavior` derives its key from the namespace, so a dry run in `.Commands` would tell every open client the patient list had changed for an import that has not happened. Same shape and reasoning as the batch CNAM estimate. |
| `Application/Features/Patients/Commands/ImportPatientsCommand.cs` | **New.** The commit — a command, so the whole file emits **one** `patients` broadcast. One `SaveChangesAsync` **per row** (spec: « all-or-nothing per **row**, never a silent partial commit » — one save for the file would let one refused row take the other 2 999), each committed row detached afterwards. A row that fails at the database is logged, detached (or it would be retried on the *next* row's save and reported as that row failing) and given its own report line. `CreateAnywayLines` defaults to **empty** = skip every duplicate. |
| `Application/Common/Interfaces/IUnitOfWork.cs` · `Infrastructure/Persistence/UnitOfWork.cs` | **`StopTracking(entity)`.** Needed by nothing but the import: 3 000 committed rows left tracked means EF re-scanning every entry on each later save (~4.5 M property comparisons). `Detached` on the entry, **not** `ChangeTracker.Clear()`, which would also release the caller's own `User` lookup the next row needs. ⚠️ Doc block states the trap: called on an uncommitted `Added` entry it discards the insert with no exception. |
| `Domain/Repositories/IPatientRepository.cs` · `Infrastructure/Repositories/PatientRepository.cs` | **`PatientIdentity`** + **`GetIdentitiesAsync`** — the clinic's identity index in one read. A projection, not aggregates: materialising every `Patient` with its flags and both history collections to compare a name is § 9.6 in a new place. **Archived included** (an archived record is still that person's file). |
| `Application/Common/Csv/ExportTables.cs` | The « Sexe » column now goes through `PatientGender.Label`. |
| `API/Models/PatientImportRequest.cs` | **New.** ⚠️ `mapping` is a **JSON string** and `createAnywayLines` a comma-separated list: a multipart form cannot carry a nested object, and the per-key workaround (`mapping[LastName]=0`) binds **silently and partially** — a mistyped key becomes a column simply not imported, i.e. 3 000 blank telephones rather than an error. |
| `API/Controllers/PatientsController.cs` | `POST import/preview` + `POST import`, both **`AdminOrDoctor`**, 8 MB cap, file buffered whole (the reader needs the header and the data together to detect the delimiter and encoding). A malformed `mapping` is **refused**, not silently re-detected — re-detecting would import against a different mapping than the one the operator was shown. An unparseable `createAnywayLines` entry is dropped, because that list only ever *widens* what gets created and the safe failure is to skip the duplicate. |
| `web/lib/api/patient-import.ts` | **New.** `preview` + `commit`. Its four types live here, not `types.ts` (one reader; the shapes only make sense beside the calls — `paging.ts`'s reasoning). |
| `web/components/patients/import-patients-dialog.tsx` | **New.** Three steps, one `File`, one component, `mobile="sheet"` + `md:max-w-4xl`. ⚠️ **Every mapping change re-runs the dry run on the server**; the client never re-derives outcomes locally, because duplicate matching needs the clinic's whole patient list. Duplicates unticked by default *with the reason stated*; the button carries the count it will create (the irreversible part deserves a number); the result step lists **every** line, not only the failures — « 2 947 créés » is only believable beside the lines it did not create, and those are what the operator fixes and re-imports. |
| `web/lib/nav.ts` | **`isAdminOrDoctor(role)`** — the one place that comparison is written client-side for gated *actions*. A **positive** test, not `!hidesClinicWideMoney(role)`: same partition today, different questions, and the safe direction differs (an unknown role must **hide** a bulk-write affordance). |
| `web/app/patients/page.tsx` | « Importer » beside « Exporter », gated on `isAdminOrDoctor`; the dialog is mounted only for a role that may use it. Reloads the list in place rather than navigating — an import creates many patients, so there is no single record to open. |

## Quality checks (L5, session 2)
| Check | Result |
|---|---|
| `dotnet build ClinicManagement.sln` | **0 errors**, 14 warnings — all pre-existing, none in a file this session touched |
| `dotnet build ClinicManagement.API.csproj --no-incremental`, output scoped to the changed files | **one** hit, `PatientsController.cs:275` (`CS8602` on `result.Value.Id` in the pre-existing `CreatePatient` action — verified identical at `git show HEAD:…`, only the line number moved). **0 new warnings.** |
| `dotnet build ClinicManagement.UnitTests.csproj` | **0 errors** — the `IUnitOfWork` addition and the `CreatePatientCommandHandler` rewiring broke no test infrastructure (`UnitOfWork` is the interface's only implementation; the test project mocks it with Moq). |
| `npx tsc --noEmit` | **0 errors** |
| `npm run check:responsive` | **11/11 pass** |
| `npm run build` | **success** (`rm -rf .next` first) |
| Tests | **NONE written** — `/test-small-feature`'s job. See « Deferred » below; the round trip the spec asks for is writable for the first time. |
| Eye pass | **NOT DONE — no browser in this environment.** Per § 14 that is stated rather than claimed. The surfaces to walk are named below. |

## Eye pass still owed (L5, session 2)
Named precisely so it can be done without re-deriving what changed:
- **`/patients` header at 320 px** — « Exporter » + « Importer » + « Ajouter un patient ». Both new controls are
  `size="sm"` with `sr-only sm:not-sr-only` labels, so below `sm:` the row is two icons and one labelled primary;
  confirm it does not wrap into three rows and that the two `touch-target` overlays do **not** overhang each other
  (they are adjacent siblings — the § 2 trap; if they do, they need `coarse:size-10` instead).
- **The import dialog at 320 / 390 / 820 / 1180 / 1440 px** — the mapping grid is `sm:grid-cols-2`, so one-up
  below; the row cards must wrap their status + checkbox cluster rather than pushing the page sideways; and the
  footer's count button must stay on screen with the keyboard open (the body is `DialogBody`'s flex column, which
  is what § 5 requires, but only an eye pass proves it).
- **`/stock` and `/creances` card headers at 320 px** — a control joined each filter cluster.
- **The agenda toolbar at 390 px and 820 px** — the export button joined a row that already wraps to several lines.
- **La caisse's « Dépenses » header at 320 px** — the title, its count badge and the new button in one
  `justify-between` row.

## Auto-Approved Deviations (session 2)
| Deviation | Reason |
|-----------|--------|
| The five remaining export buttons sit **inside the components that own their filters**, not in `PageHeader` like the first four | The alternative is lifting a copy of four filter values (stock) or an internally-derived date window (the agenda) to page level — a second authority on « what is on screen », where the file's one required property is that it *is* the list. The agenda case is not merely stylistic: `monthsAhead` is phone-only lazy loading internal to the calendar, so a page-level derivation would be *wrong*, not just duplicated. |
| The agenda's CSV is a deliberate **superset** of the screen — the whole window, every statut | « Terminés »/« Annulés » *reveal* rather than narrow (the grid hides them by default), so honouring them would make the ordinary export of a past week omit almost every appointment in it. `Statut` is a column in the file, so nothing is hidden. Stated in the code and in the root guide as the one place in L5 where the file is not exactly the screen. |
| `ExportTables.Patients` now writes « Homme »/« Femme » instead of the stored `Male`/`Female` | The same rule `CsvCell.YesNo`'s own doc block states (« a French file for French readers; `True`/`False` is not a translation ») applied to the one column that had escaped it. One new file, both directions, and the import parses both spellings so the round trip works either way. No API or schema change. |
| Row **warnings** added as a channel distinct from errors | Not in the spec's wording, but every warning marks a place `CreatePatientCommand` is deliberately lenient (partial address → `null`, empty DOB → « 30 ans », which also decides the dentition). Silent lenience at 3 000 rows is exactly the class of defect this spec keeps documenting. |
| A **name-only** duplicate kind (a name match where the row supplies no DOB to disagree with) also flags the row | The spec asks for « a name+DOB **or** phone match ». A row with neither a DOB nor a phone would then never be flagged at all, and a name collision is precisely when the operator must decide. Skip-by-default plus an explicit per-row override makes a false positive cost one tick, against a permanent split record. |
| `isAdminOrDoctor` added to `lib/nav.ts` rather than reusing `hidesClinicWideMoney` | Same partition today; different question, and the safe default differs (unknown role → hide a bulk-write action, vs. hide four doors that do not open). Reusing a money-named predicate for a patient-import button is how the next reader concludes the import is a money screen. |

## Significant Deviations (session 2)

**DEV-7 — the import reuses `CreatePatientCommand`'s *rules* by extracting them, not by sending the command.**
*Spec:* « ⚠️ **Reuse `CreatePatientCommand`'s validation** rather than a parallel path, or imported rows bypass
rules every hand-typed row obeys. »
*The obvious reading* is one `mediator.Send(new CreatePatientCommand{…})` per row. That is not viable here:
every command passes through `RealtimeBroadcastBehavior`, so a 3 000-row file emits **3 000** SignalR broadcasts on
the `patients` key and every open browser in the practice refetches the patient list 3 000 times.
*Chosen:* extract the whole construction-and-validation body of `CreatePatientCommandHandler` into
**`PatientFromRequest.Build(command, clinicId)`** — **moved verbatim**, not copied — and call it from both. The
handler keeps only what an import does differently: resolving the clinic (once, not per row), persisting, and the
inline medical/family-history entries, which come from the patient form and have no CSV column. The import is
itself a command, so the file produces exactly **one** broadcast.
*Impact:* `CreatePatientCommandHandler` shrank by ~105 lines with no behavioural change — the one textual
difference is the literal `"Unknown"` becoming `PatientGender.Unknown` (same string), and `Failure(built.Error)`
became `FailureFrom(built)`, which is the existing helper and additionally preserves `Result.Code`. The private
`SignaledFlagDescription` const moved to `PatientFromRequest` as a public one. There is still exactly **one**
answer to « what does creating a patient validate? », which is the requirement.
*Approved:* not asked (standing « do not stop »); recorded for review. **This is the one change in session 2 that
touches an existing core write path, so it is the one to read first.**

**DEV-8 — `IUnitOfWork` gained a member for a single caller.**
*Chosen:* `StopTracking(object entity)`. The per-row-save shape the spec's own requirement forces
(« all-or-nothing per **row** ») leaves every committed row in the change tracker, and EF re-scans all tracked
entries on each subsequent save — ~4.5 M property comparisons over a 3 000-row file, for work already finished.
*Why not something narrower:* the only alternatives were a batched save (which cannot give per-row atomicity) or
reaching for `DbContext` from the Application layer (banned). `UnitOfWork` is the interface's **only**
implementation, so no fake or test double needed changing.
*Risk, stated in the doc block:* called on an uncommitted `Added` entry it discards the insert **silently** — no
exception, no row, and a report that says « créé ». The import calls it only after a successful `SaveChangesAsync`
(and in the catch, deliberately, so a failed row is not retried on the next row's save and misattributed).
*Approved:* not asked (standing « do not stop »); recorded for review.

**DEV-9 — the file is uploaded twice; there is no server-side staging.**
*Chosen:* both `import/preview` and `import` carry the file and the mapping; the commit re-reads and re-matches
from scratch through the same `PatientImportPlanner`.
*Why:* a staging table needs an owner, a lifetime and a pruner nobody would write, and its rows would outlive the
browser tab that created them. Re-sending costs one upload of a text file the client already holds, and it makes
the preview↔commit agreement **structural** rather than a claim about two code paths.
*Consequence the client must honour, and does:* `createAnywayLines` is keyed on the **file line number**, not a
server-side row id that would not survive the round trip — and a mapping change clears the ticks, because a
different « Nom » column makes a given line a different patient.
*Approved:* not asked; recorded for review.

**DEV-10 — this session touched 22 files, past the small-feature envelope.**
Recorded rather than escalated, for the reason session 1 already documents: this spec was authored as eleven themes
in one deliberately-forced Type-Small pass with a standing user instruction (« use this branch and do not stop »),
and the resume instruction for this session was « continue where u left off ». The two remaining L5 items were the
next ones in the spec's own delivery order, and the import is a feature in its own right — the spec says so
itself. **The escalation question still live is L9**, whose migration across three aggregates the spec itself
flags as « the largest item here … do it last, or split it out ».

## Deferred to `/test-small-feature` (session 2)
The spec's L5 line — « round-trip: export → import → identical set. Duplicate, bad-phone and bad-date rows
rejected per-row with reasons » — is writable for the first time, and every piece of it is pure:

- **`CsvReaderTests`** — delimiter detection (`;`, `,`, tab, and a header sampled past a *quoted newline*); the
  cp1252 fallback (Latin-1 bytes for « Béchir » must not become « B?chir »); a BOM stripped so the first header
  matches its own auto-detection; RFC 4180 (embedded delimiter, `""`, a newline inside a quoted field, a quote
  appearing mid-field as data); a short row reading as blanks rather than an error; `Truncated` at the cap.
- **`PatientImportFieldsTests`** — the « Téléphone » vs « Téléphone d'urgence » discrimination, in **both** header
  orders. That is the ordering-dependent bug longest-alias-wins + claim-once exists to prevent, and it is
  invisible to any test that only checks the happy order.
- **`PatientImportRowReaderTests`** — each accepted date format; `MM/dd/yyyy` **rejected** (pin the deliberate
  refusal, so a future « helpful » addition fails); a future DOB and a pre-1900 DOB; a non-Tunisian patient phone
  as an **error** vs. a non-Tunisian *emergency* phone as a **warning** kept verbatim; a partial address and a
  partial insurance block each warning and importing nothing; a 9-digit CNAM identifiant warning and importing
  as-is; `PatientGender.Parse` over « Homme » / « M » / « female » / « » / « Docteur ».
- **`PatientImportPlannerTests`** — the four duplicate kinds; **within-file** duplicates (row 3 flagged against
  row 2, and row 4 flagged too even if row 2 is to be skipped); phone matching across `20 123 456` vs
  `+216 20 12 34 56`; an out-of-range mapping index refused; a missing required field refused for the file.
- **`PatientImportRoundTripTests`** — `ExportTables.Patients(dtos)` → `CsvReader` → `PatientImportRowReader`,
  asserting field-for-field equality including an accented name, a « Homme » cell and a `null` phone. This is what
  keeps the export and the import from drifting, and it is the spec's own acceptance criterion.
- **`ImportPatientsCommand`** scenarios (Moq): a duplicate not in `CreateAnywayLines` is **Skipped** and nothing is
  saved for it; the same line *in* the list is **Created**; an invalid row is **Invalid** and the rows after it
  still import (the « never abandons the file » guarantee); a `SaveChangesAsync` throw on row 2 leaves rows 1 and 3
  created and row 2 `Failed`.
- **`PatientFromRequest`** — a characterisation test that the extracted builder still applies the phone rule, the
  blank-means-blank contacts, the all-four-parts address, the `DentitionRules` fallback **from the defaulted DOB**,
  and the « Signaler ce patient » flag. DEV-7 moved code into a new home; this is what pins the move as
  behaviour-preserving.

---

# Session 3 — 2026-08-04 · L6 dropped by the user · L8 slice A (cheque tracking)

**Branch:** `feature/audit-sections-3-to-10` (unchanged).

## Scope decisions taken this session (user-directed)

1. **L6 (relance worklist) is DROPPED, not deferred.** The user's words: « i do not care about relance at all, so
   if's to fix relance drop that point ». All seven L6 sub-items are struck from this spec's remaining work — the
   screen, the do-not-contact flag, the `recallIntervalMonths` input, the dashboard KPI, the notification
   re-point, bulk send, and surfacing the send refusal. ⚠️ **The backend is untouched and still complete**
   (`RecallController`'s five endpoints, `GetPatientsToRecallQuery`'s four-reason aggregation, `SendRecallCommand`'s
   correct French refusals), and `recallsApi` still has **zero callers**. Anyone reviving this should read the
   « Verified state of L6 » table below rather than re-deriving it.
2. **L8 was split, on the user's choice**, after exploration put the whole item at ~24 files. **Slice A** (this
   session) is cheque tracking proper; **slice B** is the per-method caisse breakdown, the ledger `method` filter and
   the cheques-due view — see « Slice B, not built » below.
3. **The cheque due date stays optional**, on the user's choice, rather than being required when the method is
   `Cheque`. The consequence is deliberate and belongs to slice B: a cheque with no due date is *counted and listed
   as its own group*, never silently dropped, because that is exactly the money-lost case the view exists for.
4. **The migration is hand-authored** (`dotnet ef` is WDAC-blocked here, `0x800711C7`), on the user's choice, with
   `verify-schema` extended. See DEV-11.

### Verified state of L6 at the moment it was dropped
Checked rather than taken from the spec, so a later session need not repeat it:

| Claim | Verified |
|---|---|
| `web/app/recalls/` does not exist | ✅ `ls` — no such directory |
| `recallsApi` has zero callers | ✅ grep across `web/` finds only its own module |
| `Patient` has no do-not-contact field | ✅ zero hits for `donotcontact`/`optout`/`consent` on the entity |
| `recallIntervalMonths` has no UI | ✅ **zero** occurrences anywhere in `web/` |
| The dashboard KPI's data still flows | ✅ `DashboardAlertsReader.CountPatientsToRecallAsync` still computes it, `DashboardAlertsDto.PatientsToRecall` and `types.ts` still carry it; only the card and the `DashboardKpiKeys` *write*-set entry were removed |
| The failed-recall notification lands on a dead end | ✅ `dashboard-header.tsx:207` pushes `/rappels?status=failed` — the delivery log, which cannot list the patient to re-contact |

## Files Changed — L8 slice A

| File | Change |
|---|---|
| `Domain/ValueObjects/ChequeDetails.cs` | **New.** Number, bank, due date — and **the single guard**: `For(method, …)` refuses details on any method but `Cheque` and returns null when nothing was supplied. It exists as a *type* rather than six loose nullable columns because two ledgers need the same rule and the guard-at-each-call-site shape is how the next write path forgets it. ⚠️ Deliberately **not** an EF-owned type: both entities flatten it to plain columns, since the number is searched and the due date sorted on, each on its own. `DueDate` is a **calendar day** (no zone conversion), exactly like an échéance's. |
| `Domain/Entities/Payment.cs` · `InstallmentPayment.cs` | Three nullable columns each, set only through a `ChequeDetails?` ctor parameter. `InstallmentPayment` also gains **`ToChequeDetails()`** — one caller, the bridge; see DEV-12. |
| `Domain/Entities/Invoice.cs` · `Installment.cs` · `TreatmentPlan.cs` | `RecordPayment` / `RecordInstallmentPayment` take an optional `ChequeDetails`. Defaulted, so no existing caller changed shape. |
| `Infrastructure/.../PaymentConfiguration.cs` · `InstallmentPaymentConfiguration.cs` | The three columns (50 / 200 / timestamptz) + a **partial** index on the due date on **both** ledgers. ⚠️ The filter is `"ChequeDueDate" IS NOT NULL AND NOT "IsVoided"`, **not** `"Method" = 1`: equally selective by the domain invariant, and the enum form would bake `PaymentMethod.Cheque`'s ordinal into SQL — a magic number in the one place no compiler checks it. |
| `Infrastructure/Migrations/20260804120000_AddChequeDetailsToPayments.cs` + `.Designer.cs` + snapshot | Purely additive: six nullable columns, two partial indexes, **no backfill, no row rewritten**. A cheque recorded before today legitimately has no number — different from « we have no cheques », which is why they are nullable rather than defaulted to `''`. See DEV-11 for how it was authored and verified. |
| `Application/Features/Invoices/Commands/RecordPaymentCommand.cs` | Three request properties + one `ChequeDetails.For` call. The handler already caught `ArgumentException` → French `Result.Failure`, so the invariant's refusal becomes a 400 with no new plumbing. |
| `Application/Features/TreatmentPlans/Commands/RecordInstallmentPaymentCommand.cs` | Same three properties, same one call. |
| `Application/Features/Invoices/Commands/CreateInvoiceFromDentalRecordCommand.cs` | Same, but the `ChequeDetails.For` call is placed with the **pre-flight**, not at the `RecordPayment` line — this command's whole shape is that every refusal happens *before* a gapless number is consumed, and building it inside the transaction would leave a numbered, unpaid note behind for a mis-set form field. |
| `Application/Features/Invoices/Commands/IssueInvoiceCommand.cs` | **The load-bearing one.** The devis→facture bridge now carries the cheque across — see DEV-12. |
| `Domain/Repositories/IInvoiceRepository.cs` · `ITreatmentPlanRepository.cs` + both impls | `CaissePaymentRow` / `CaisseInstallmentPaymentRow` gain the three fields (defaulted, so no other construction site changed) and both row projections read them. |
| `Application/DTOs/CaisseMovementDto.cs` · `GetCaisseLedgerQuery.cs` | The extrait carries the cheque. ⚠️ The DTO's doc block states the one thing a reader could get wrong: `ChequeDueDate` is **not** `OccurredOn` — a post-dated cheque is received (and appears in the till) on the day it is handed over; the money arrives later. The statement's free-text search now also matches the cheque number and bank (« où est passé le chèque 4512 ? » is asked out loud). |
| `Application/Common/Interfaces/ISchemaVerificationReader.cs` · `Infrastructure/.../SchemaVerificationReader.cs` · `Application/Common/Maintenance/SchemaVerificationService.cs` | **`cheque-details-only-on-cheques`**, over both ledgers in one figure. The clearest illustration of what belongs in that service: the migration's *shape* is diffed against the catalog for free and named nowhere, while the *invariant* — deliberately not a CHECK constraint — has to be verified rather than enforced twice. |
| `web/components/factures/cheque-fields.tsx` | **New.** The one conditional sub-form + **`chequePaymentFields()`**, the one payload builder. ⚠️ **The method check lives in the builder, not at the call sites**: it clears the fields when the method is not `Cheque`, so a value typed and then abandoned (the user switches back to « Espèces ») can never be submitted — while staying on screen in case they switch back. `chequeDueDate` goes over the wire as a bare `YYYY-MM-DD`; `toISOString()` would shift a cheque due on the 1st into the previous month. |
| `web/components/factures/payment-modal.tsx` · `web/components/treatment-plans/installment-payment-modal.tsx` | Mount `ChequeFields` when the method is `Cheque`; spread `chequePaymentFields(...)` into the request; reset on open. |
| `web/lib/api/invoices.ts` · `treatment-plans.ts` · `types.ts` | The three optional request fields on both payment requests, and the three on `CaisseMovementDto`. |
| `web/components/caisse/caisse-ledger-table.tsx` | A cheque row names itself — « n° 4512873 · BIAT · encaissable le 15/09/2026 » — through **one** `chequeSummary()` used by **both** trees (card list and `<table>`), since a cheque described differently in each is the drift `ConventionPrompt` documents. Returns `null` when there is nothing to say, so the field is omitted rather than printed as « — ». |
| `web/components/patient-files-manager.tsx` | **The L7 freebie**, and worse than the spec described: the upload `catch` did not misread `errorData.message` — it **discarded the response entirely** and hardcoded « vérifiez votre connexion ». So a file the server refused on its PDF/PNG/JPEG allow-list was reported as a network failure, and the user retried the same DICOM repeatedly while the one fact that explained it sat unread in the body. Now `getErrorMessage(error, …)`, which falls back to the connection wording only when there genuinely is no message. |

## Quality checks (L8 slice A)
| Check | Result |
|---|---|
| `dotnet build ClinicManagement.sln` | **0 errors**, 14 warnings — the same count as before this session |
| `dotnet build ClinicManagement.sln --no-incremental`, output scoped to the 18 changed backend files | **empty — 0 new warnings** |
| `npx tsc --noEmit` | **0 errors** |
| `npm run check:responsive` | **11/11 pass** |
| `npm run build` | **success** (`rm -rf .next` first; exit 0) |
| `dotnet run -- verify-schema` | **NOT RUN — no PostgreSQL in this environment.** It is the *only* gate this migration has, so it must be run before and after applying it and the outputs diffed; the line to look at is the new `cheque-details-only-on-cheques`. |
| `dotnet run -- reconcile-money` | **NOT RUN**, same reason. The spec asks for it around L8. ⚠️ The change adds no arithmetic and touches no sum — the columns are descriptive — so no drift is *expected*, which is exactly why the before/after diff is worth having as evidence rather than as an assumption. |
| Tests | **NONE written** — `/test-small-feature`'s job. See « Deferred » below. |
| Eye pass | **NOT DONE — no browser in this environment.** Stated per § 14 rather than claimed. Surfaces named below. |

## Eye pass still owed (L8 slice A)
- **Both payment dialogs at 320 / 390 / 820 px, with « Chèque » selected.** The cheque block is
  `sm:grid-cols-2`, so one-up below `sm:`; check the block appearing does not push the submit button off a
  landscape phone (~380 px of height) — both dialogs are `mobile="sheet"` with the footer outside the scroll
  container, which is what § 5 requires, but only an eye pass proves it.
- **The same, switching the method back to « Espèces »** — the block must disappear and the payment must submit
  clean (the builder clears it; this is the visual half of that).
- **The extrait at 320 px and at 820 px** with a cheque row: the summary line sits under « Mode » in the table
  tree and as its own field in the card tree, and must not widen either past its container.
- **A file-upload refusal on `/patients/[id]/files`** — the toast should now carry the server's French reason.

## Auto-Approved Deviations (session 3)
| Deviation | Reason |
|-----------|--------|
| The three fields are carried as a `ChequeDetails` **value object** into the ctors, not as three loose parameters | The spec says « add optional `ChequeNumber`, `BankName` and `ChequeDueDate` to the two payment ledgers », and the *columns* are exactly that. What it does not say is where the « only for a cheque » rule lives, and two ledgers × four write paths is precisely the shape where a per-call-site guard gets forgotten. No API change, no schema difference. |
| A partial index on `ChequeDueDate` added **now**, though nothing reads it until slice B | The migration is the expensive thing to redo, and `verify-schema` had to be extended once either way. Adding the index in a second migration later would mean two hand-authored migrations where one suffices. |
| `IssueInvoiceCommand` (the bridge) rebuilds the details **through `ChequeDetails.For`** rather than copying the three fields | Re-checks the invariant on the way across instead of trusting the source row. Costs one method; means a corrupted row cannot silently propagate to the invoice ledger. |
| The extrait's search now also matches the cheque number and bank | Two extra arguments to the existing `SearchTerm.Matches` call. The statement is the only screen listing every movement, so it is where « où est passé le chèque 4512 ? » gets answered. |
| `ChequeFields` does **not** self-hide on a non-cheque method | The caller decides whether to render it, so a caller cannot mount it and silently show nothing. The *payload* is cleared centrally instead, which is the half that must not be forgotten. |
| The L7 error-surfacing fix landed here, in an L8 diff | ~5 lines, actively misleading in production, and entirely independent of the imaging-bridge design question that blocks the rest of L7. Flagged to the user before doing it. |

## Significant Deviations (session 3)

**DEV-11 — the migration is hand-authored, and `verify-schema` is its only gate.**
`dotnet ef` cannot load a freshly-built assembly on this machine (Smart App Control / WDAC, `0x800711C7`), and
this environment has no PostgreSQL either. Per the skill's rule the delta is small enough to hand-write — six
nullable columns and two partial indexes — so `Up`/`Down`, the paired `.Designer.cs` and the model snapshot were
written by hand. The Designer was derived **mechanically** from the just-updated snapshot by pattern substitution
(add `using …Migrations;`, add the `[Migration]` attribute, rename the class, rename `BuildModel` →
`BuildTargetModel`), not retyped: both files carry the same 10 `Cheque*` occurrences, and Infrastructure compiles.
⚠️ **It must be regenerated with the EF tool in an unrestricted environment before merge**, and
`dotnet run -- verify-schema` must be run before and after applying it with the outputs diffed. The service was
extended so that run has something to say about this change specifically.
*Approved:* yes — the user chose « Hand-author it + extend verify-schema » when asked.

**DEV-12 — the devis→facture bridge carries the cheque, and this is the finding of the session.**
Exploration turned up exactly **one** `new Payment(` and **one** `new InstallmentPayment(` in the solution, so the
write side was smaller than feared. But one of the four `RecordPayment` call sites is `IssueInvoiceCommand.cs:235`,
the bridge that carries a plan's collected installment payments onto the invoice at issue.
*Why it matters:* the plan side stops being counted the moment the bridge invoice is issued (`PlanBillingRules`
excludes a billed plan from every money read). So a cheque left behind at that hop would **vanish from any
« chèques à encaisser » view entirely** — the row that still has to be banked becoming the one row nothing lists.
The failure would be silent, and it would look like the cheque was never recorded.
*Chosen:* `InstallmentPayment.ToChequeDetails()`, called by the bridge. One method with one caller, justified in
its own doc block so it does not read as speculative generality.
*Impact:* none on any existing behaviour — the bridge previously passed no cheque because none existed.
*Approved:* not asked; recorded for review. It is the hop the spec's own file list does not mention.

**DEV-13 — a build-required fix to 12 scenario-test call sites.**
`DataMigrationCounts` is a positional `record`, so adding the eleventh count broke every construction site in
`SchemaVerificationServiceTests.cs` (12 of them) at compile time — the solution build compiles the test project, so
the 0-errors gate fails until they compile. Each was patched by **appending one argument**, preserving its original
scenario exactly: `0` (clean) everywhere, and `null` on the one test whose premise is « a count whose subject does
not exist yet », where a null is the faithful value. No assertion was touched and no scenario was added.
⚠️ Worth noting for slice B: **every new count breaks all 12 sites again.** The durable fix is `CleanCounts with
{ … }` per test, which would also make each test name its own facet — deliberately *not* done here, because
remapping 12 positional argument lists by hand is exactly where a silent scenario change would slip in. That is a
test-file refactor and belongs to `/test-small-feature`.
*Approved:* auto-approved by the skill's build-required-compile-fix rule; recorded as one line per its instruction.

## Slice B — not built (L8's remaining half)
Deliberately left, with the reasoning already worked out so it need not be re-derived:
- **A per-method breakdown on `CaisseSummaryDto`** — the spec's « cash-only figure », so the owner can separate what
  is physically in the drawer from a cheque not yet banked. ⚠️ **Do not compute it by summing the statement's
  rows**: `GetPaymentsBetweenAsync` returns voided rows (the statement strikes them through) while
  `GetCollectedBetweenAsync` drops them, so summing rows silently disagrees with `CashIn` unless `!IsVoided` is
  re-applied. The safe shape is **two new `GROUP BY` repository methods, predicate-for-predicate identical to their
  existing SUM siblings** — the same relationship `GetPaymentsBetweenAsync` has to `GetCollectedBetweenAsync` — so
  `Σ breakdown == CashIn` holds by construction and the figure `MoneyReadConsistencyTests` pins is not touched.
  Cash-in only: an expense's method is already visible per row in the dépenses table, and the spec's sentence is
  about money *in* that has not cleared.
- **A `method` filter on `GET /billing/caisse/ledger`** — apply it **where the search filter is applied**, i.e.
  *after* the running balance is computed over the whole window. Filtering earlier would print a « Solde de la
  période » column that sums to nothing. Defaulting it to null is also what keeps `CaisseLedgerTests`' invariant
  (`Σ movements == cashIn − refunds − cashOut == net`) intact, since that test reads the ledger unfiltered.
- **A « chèques à encaisser » view**, by `ChequeDueDate`, over **both** ledgers (the partial indexes for it already
  exist). It must list cheques with **no** due date as their own counted group — that is the decision the user took
  this session, and dropping them silently would hide the very rows the view exists for.

## Deferred to `/test-small-feature` (session 3)
- **`ChequeDetailsTests`** — `For` returns null when all three are blank; trims and blank-to-nulls the two strings;
  **throws for every non-`Cheque` method** when any one of the three is supplied (a `[Theory]` over Cash/Card/
  Transfer × each field, since a per-field guard is exactly what could be got wrong); allows a cheque with only a
  due date, and one with no due date at all (the user's chosen optionality, worth pinning so a future « helpful »
  tightening fails).
- **`Invoice.RecordPayment` / `Installment.RecordPayment`** — the details land on the row; a null `cheque` leaves
  all three columns null; the fields survive a void (the row is kept, so its cheque identity must be too).
- **The bridge (DEV-12)** — `IssueInvoiceCommand` carries number, bank **and** due date from an installment payment
  onto the invoice payment. This is the one the session's reasoning says is most valuable, and the one nothing else
  would catch.
- **`RecordPaymentCommand` / `RecordInstallmentPaymentCommand` / `CreateInvoiceFromDentalRecordCommand`** — cheque
  details on a `Cash` method return a French `Result.Failure` rather than throwing; and for the fiche path
  specifically, that the refusal happens **before** a number is consumed (assert no invoice was issued), since that
  is why the call was placed in the pre-flight.
- **`GetCaisseLedgerQuery`** — a cheque movement carries the three fields through to the DTO; the search term
  matches on the cheque number.
- **`chequeSummary` / `chequePaymentFields`** would be worth unit tests if `web/` had a runner. It does not — that
  is a stated fact of this repo, not a gap to fill here.

---

# Session 4 — 2026-08-04 · L8 slice B · L10 · L11 · L9 — **every remaining item of this spec**

**Branch:** `feature/audit-sections-3-to-10` (unchanged — same standing instruction).

**What is now closed.** L1–L5 and L7's error-surfacing half were already done; L6 was struck by the user. This
session built **L8 slice B, L10, L11 and L9**, in the spec's own delivery order. The only thing left in the whole
file is **L7's imaging bridge**, which is blocked on a design question no code can answer — see the bottom.

## L8 slice B — the per-method breakdown, the ledger filter, and « chèques à encaisser »

| File | Change |
|---|---|
| `Domain/Repositories/PaymentMethodTotal.cs` | **New.** The shape of a `GROUP BY "Method"` over a payment ledger, in its own file because **both** ledgers project into it and the caller merges the two. |
| `IInvoiceRepository` · `ITreatmentPlanRepository` + both impls | `GetCollectedByMethodBetweenAsync` / `GetInstallmentCollectedByMethodBetweenAsync` (the breakdown) and `GetChequePaymentsAsync` / `GetInstallmentChequePaymentsAsync` (the cheques). ⚠️ The two breakdown reads are **`GROUP BY` siblings of the very SUMs** that produce `CashIn`, predicate for predicate — *not* a grouping of the extrait's rows, which carry voided payments and would make the lines silently disagree with the total above them. The cheque reads are the first users of the two partial indexes slice A shipped, and their date bounds are on the **due date**, never `PaidOn`. |
| `CaisseSummaryDto` + `CaisseMethodTotalDto` · `GetCaisseSummaryQuery` | `CashInByMethod`, with **all four methods always present in enum order, zeros included** — a day of cheques alone must still show « Espèces 0,000 », which is the figure the person closing the till is looking for. `MergeMethodTotals` enumerates `PaymentMethod` rather than the returned rows, so a method added to the enum appears with no edit and cannot be silently omitted from a breakdown the total is supposed to equal. |
| `GetCaisseLedgerQuery` + `BillingController` | A `method` filter on the extrait, applied **after** the running balance — beside the search term and for the identical reason (« Solde de la période » is a fact about a movement's place in the window). An unrecognised value is ignored, not refused. ⚠️ A movement with **no** method (a legacy avoir) legitimately leaves the list under any filter. |
| `GetChequesDueQuery` · `ChequesDueDto` · `GET /api/billing/cheques` (+ `/export`) | « Chèques à encaisser » over both ledgers, soonest-due first, `AdminOrDoctor`. ⚠️ **The bridged-plan de-dup is load-bearing here, not consistency theatre**: `IssueInvoiceCommand` carries a bridged plan's cheque onto the invoice (slice A's DEV-12), so without it one physical cheque is listed twice and the duplicate is indistinguishable from a second genuine cheque of the same amount from the same bank. |
| `web/components/caisse/cash-in-by-method.tsx` | **New.** The « dont » row — and **each figure is also the control that filters the extrait to the movements behind it**, which is the dashboard's figure-links-to-its-records rule applied to a decomposition. A separate « Mode » Select would have put the number and the way to inspect it in different places. Chips **grow** (`coarse:py-2.5`) rather than wearing `.touch-target`: on a wrapped row an overlaid hit area overhangs its neighbours and the later sibling steals their taps (§ 2). |
| `web/app/caisse/page.tsx` | The breakdown row, the removable « Mode » chip (§ 13) with the caveat that the totals above stay all-methods, and `isFiltered` now counts the method filter — otherwise a period with no cheques would render the first-run « aucun mouvement » invite about a till that took money all day. |
| `web/app/cheques/page.tsx` · `web/components/caisse/cheques-table.tsx` · `lib/nav.ts` | The screen, gated exactly like the other three Finances pages (added to `SECRETARY_HIDDEN_HREFS`). Four bucket figures on one `KpiGrid`, `_LG` hinge (eight columns), export beside the filter it exports. |

⚠️ **The one limitation, stated on the screen and not hidden:** the product records the *receipt* of a cheque, not
its clearing at the bank, so **a cheque leaves this list only by being voided** — one banked last year is still
listed. That is why the four bucket counts are the headline and the order is by due date: « En retard » is the
actionable set. Recording the banking is a column, a command and a write path this slice does not add. A cheque
with **no** due date is its own counted group (the user's session-3 decision), never dropped.

## L10 — the CNAM annual plafond

| File | Change |
|---|---|
| `Domain/Services/CnamPlafond.cs` | **New.** The single authority: the dependants barème (450 / 675 / 900 / 1 125 / 1 350), the `DentalAllowance` (150), the three supplements **as declared constants the UI quotes rather than applies**, and `ConsumesCeiling(category)` — « Prothèse » is hors plafond, and an **unknown** category consumes, because failing to count an act inflates the remaining figure, which is the exact over-promise L10 removes. |
| `Domain/ValueObjects/CnamInfo.cs` + config + migration `20260804123000_AddCnamAnnualCeiling` | `DependantCount` and `AnnualCeilingOverride`, both clamped away from non-positive values in the ctor: a **ceiling of 0 would report every patient as fully consumed**, i.e. « CNAM refuses this patient », and that is what a blank numeric input arrives as. |
| `ICnamBillingCalculator.ComputeCeilingConsumptionAsync` + impl | A member on the existing interface, not a second calculator — it resolves the same acts against the same catalogue through the same `CnamReimbursementCalculator`, and a separate implementation would be the second authority over a reimbursement figure that § 5.10 exists to prevent. ⚠️ It applies **no cap**, unlike `ComputeAsync`: clamping to what the clinic charged would under-report consumption on a discounted invoice and so over-state the ceiling left. |
| `CnamCeilingDto` · `GetPatientCnamCeilingQuery` · `GET /api/patients/{id}/cnam-ceiling` | Ceiling, consumed, remaining, hors-plafond, for a **clinic** year (`ClinicClock`, so a 00:30 Tunis invoice on 1 January does not reset a ceiling an hour early). ⚠️ On the class policy (`AnyClinicRole`), beside « Solde patient »: it is **per-patient** money, and « combien reste-t-il ? » is asked at the desk with the patient standing there. |
| `web/lib/cnam.ts` · `edit-patient-dialog.tsx` · `components/cnam/cnam-ceiling-notice.tsx` · `document-editor-content.tsx` | A display **mirror** of the barème (never a second authority — every reported figure comes from the endpoint), the two inputs on the patient's CNAM block with a live barème preview, and the notice under the BS1 estimate. |

⚠️ **Both reasons the figure is an estimate are carried as DTO fields**, so the caveat lives beside the number
instead of being each screen's own wording: `ceilingIsDefault` (the 2024 amounts are two agreeing Tunisian outlets
with **no official CNAM page retrieved** — so they are a *default* the per-patient override always beats) and
`seesThisClinicOnly` (the clinic counts only its own acts, so « reste » is an **upper bound**). The spec's stated
blocker — K10's pre-2021 tariffs — was already cleared by `adoption-qa-k`, confirmed before building.
⚠️ **Consumption is measured from issued invoices**, because the product records no BS1 submission carrying an
amount. That makes the figure lag a bulletin the caisse has not yet paid and lead one it refused; stated in the DTO
because no caller could compensate.

## L11 — arrêt de travail on the official CNAM P 061

**Which form, settled rather than assumed.** The feature folder bundles three scans and the spec flags the choice
as unverified. **`P61_2024.pdf`** is the one — « P 061 — DEMANDE D'INDEMNITÉ DE MALADIE » over « CERTIFICAT MÉDICAL
D'ARRÊT DE TRAVAIL ». `CMIATMP.pdf` is a *different* form (the AT/MP certificate) and P 061 says so in its own
header, so the two are not alternatives; `p61.pdf` is the same P 061 in an older printing (it still carries
« INDEMNITÉ DE COUCHES » and « Période initiale / Prolongation », which the 2024 revision drops). ⚠️ Still a
judgement: no official CNAM publication was retrieved.

| File | Change |
|---|---|
| `Infrastructure/Assets/P61.pdf` + csproj | A **normalised** copy of the bundled scan: the original is an A4 *portrait* page whose form content runs sideways, and this one has the rotation **baked into the content stream** as A4 landscape — so every coordinate in the renderer matches what a ruler on the printout measures, and nothing depends on how PdfSharp treats `/Rotate`. |
| `Infrastructure/Services/CnamArretTravailRenderer.cs` | **New**, on `CnamBs1BulletinRenderer`'s pattern. Fills **both** panels: the practitioner's certificate *and* the « assuré social » identity half, because every field there is data the product already holds and an identifiant unique copied by hand is where a digit gets lost. Signature/cachet spaces left blank. `ArretTravailTraumaCause` resolves to **one** value, so « two causes ticked » is unrepresentable, and hospitalisation is a **tri-state** — ticking « Non » by default would assert a clinical fact nobody entered on a form that decides an indemnity. |
| `Application/Features/Documents/ArretTravailKeys.cs` | The content keys **declared once**, because the editor writes them, validation reads them and the renderer stamps from them — and a key spelled differently in one of the three degrades *silently*. |
| `ArretTravailValidation.cs` + both document handlers | The K-series gate applied **from the start**: duration (present, numeric, ≤ 180), start date, a named practitioner, and at least one of code conventionnel / n° d'ordre (one, never both — a non-conventionné dentist still has a CNOMDT number). ⚠️ **The motif is deliberately not required and never printed**: P 061's front carries no diagnosis field and is what the patient hands their employer. |
| `DocumentTypes.ArretTravail` · `DocumentFileNaming` · `PdfGenerationService` | The token, the filename, and the dispatch — which **fails fast** on a missing asset rather than falling through to the generic renderer, since a free-text « certificat » in place of the official form is precisely what the caisse refuses and it would look like a success. |
| `web/lib/arret-travail.ts` · `app/documents/page.tsx` · `document-editor-content.tsx` | The stored-value sets + French labels, a new tile, and the editor branch. **`isOfficialForm`** now names what the BS1 and the arrêt actually share — server-rendered PDF preview (so Print goes through the iframe, K4), **no `doctors[0]` fall-back**, a pre-Save gate, and **no Word export** (a `.docx` of a pre-printed form is the letterhead alone — the K-series defect that produced a success toast). The certificat tile no longer claims to cover an arrêt de travail; it never could. |

⚠️ **The coordinates were calibrated, not guessed.** Measured from the scan's own rules, dotted baselines and
checkbox strokes (PyMuPDF pixel analysis), then the **same coordinate map** was stamped onto the real asset with
representative values and rendered at 190 dpi and inspected — three iterations until every field landed in its box.
The harness is `scratchpad/calibrate_p61.py`. **Printing onto real paper is still owed** and no test can substitute.
⚠️ **The C# renderer itself could not be executed here**: a throwaway harness referencing Infrastructure was blocked
by Smart App Control (`0x800711C7`, the documented environmental blocker). What *was* verified: it compiles, and the
asset is a plain PDF 1.7 with a classic xref table, no object streams and no encryption — the same structural family
as `BS1.pdf`, which PdfSharp already reads. **A smoke test asserting `Render` returns a non-empty PDF is the first
thing `/test-small-feature` should write.**

## L9 — per-practitioner attribution

**Schema** (`20260804112446_AddPractitionerAttribution`, EF-generated, `Up()` hand-extended with the backfill):
nullable `DoctorId` + FK + index on `Invoices`, `TreatmentPlans` and `DentalRecords`; and
`WaitingListEntries.PreferredDoctorId` promoted to a **real FK**.

⚠️ **The orphan cleanup before that FK is required, not defensive.** `PreferredDoctorId` has been an unconstrained
`uuid` for the product's whole life, so nothing prevented it holding an id from another clinic or one whose `Doctor`
row was deleted — and adding the FK over such a row **fails the migration on the operator's database, after the
schema is half-applied**. Nulling them first is the difference between « three queue entries forget a preference »
and « the upgrade will not install ». That is exactly the cost of the bare Guid the spec cites this column to show.

**The backfill** attributes only what is *knowable*: a fiche and an invoice from their own appointment, a devis from
the **earliest** appointment booked against its acts (`DISTINCT ON … ORDER BY` — without an ordering, two runs on
the same data could attribute the same devis to two different dentists), and then a bridge invoice from its devis.
Everything else stays null, because inventing a practitioner silently credits one dentist with another's work.

| File | Change |
|---|---|
| `Application/Common/PractitionerAttribution.cs` | **New.** The one precedence rule — explicit → the visit's → the caller's own `Doctor` record — with every candidate checked against the clinic's roster, so a cross-clinic id falls through instead of being stored. ⚠️ **The caller is last, not first**: a secretary recording a dentist's work must not credit themselves, and in the single-dentist practice the owner *is* the caller, which is exactly where that fall-back is right. |
| `CreateInvoiceCommand` · `CreateDentalRecordCommand` · `CreateTreatmentPlanCommand` | The three write paths, each with an optional `DoctorId` on the request. **The columns ship with their callers**, which is the `SetStockExpiryLeadDays` failure the spec names. |
| `CreateInvoiceFromDentalRecordCommand` · `CreateInvoiceFromTreatmentPlanCommand` | ⚠️ **The attribution travels with the money and is not re-derived.** Both commands bill work that already happened, so they copy the source's practitioner verbatim; re-resolving would let the *biller* (often reception) be credited. The devis→facture hop is where an attribution is most easily lost, because nothing else copies anything between those two aggregates. |
| `InvoiceDto` + `InvoiceMappingExtensions` | `DoctorId` + `DoctorName` (the mapper's new parameter is defaulted, so ~20 existing call sites are untouched). |
| `IInvoiceRepository` + impl | A `doctorId` filter on `GetFilteredAsync`, `GetCollectedBetweenAsync` and `GetInvoicedBetweenAsync` — **in SQL**, because in the handler it would mean « hers among these 25 » and hide her invoices on every other page. ⚠️ The cash filter is on the **invoice**, not the payment: whoever took the cash at the desk did not earn the work. ⚠️ An **unattributed** row is *excluded* under a filter, not silently included — otherwise two dentists' filtered totals would exceed the clinic's. |
| `DashboardMoneyReader` · `DashboardMoneyDto` · `GetDashboardQuery` · `DashboardController` | The Argent filter — see DEV-15 for the two figures it deliberately does not narrow. |
| `web/app/factures/page.tsx` + `invoices-table.tsx` · `web/app/page.tsx` + `use-dashboard.ts` | Both filters, each offered **only when the practice has more than one practitioner** (on a solo practice the control has one meaningful value and reads as broken), each stating that historical rows are unattributed. The dashboard's control sits **inside** the Argent section, not beside the period selector — it narrows that section only, and page-level placement would imply it applied to « RDV honorés » too. |
| `verify-schema` (`ISchemaVerificationReader`, reader, service) | **`practitioner-attribution-backfill`** — rows whose appointment names a practitioner while the row itself is unattributed. The columns, indexes and four FKs are diffed against the catalog for free by the model read; a **backfill** is the one thing invisible to every layer, because an invoice whose practitioner was knowable and simply not copied renders as « non attribué », indistinguishable from one that genuinely has none. |

**Out of scope, per the spec:** per-practitioner **data scoping**. This is attribution — who earned a figure.

## Quality checks (session 4)
| Check | Result |
|---|---|
| `dotnet build ClinicManagement.sln --no-incremental` | **0 errors.** 58 warnings on a full solution rebuild, **all pre-existing** — verified by scoping the output to every file this session touched, which came back **empty** (0 new warnings). The « 14 » in earlier sessions was the API-project scope; a full rebuild also surfaces Domain's ~44 `CS8618`. |
| `dotnet vstest` (the SAC workaround — it works today) | **1799 passed · 29 failed · 1828 total — byte-identical to the session-1 baseline.** ⚠️ It caught **9 real breakages of my own** first (see below); all nine are fixed and the failing *classes* now match the recorded baseline exactly. |
| `npx tsc --noEmit` | **0 errors** |
| `npm run check:responsive` | **11/11 pass** |
| `npm run build` | **success** (`rm -rf .next` first). The only warning is the pre-existing `@auth0/nextjs-auth0` Edge-Runtime `crypto` note. |
| `dotnet run -- verify-schema` | **NOT RUN — no PostgreSQL here.** It is the *only* gate for two migrations. Run it before and after applying them and diff; the lines to read are the new **`practitioner-attribution-backfill`** and (from slice A) `cheque-details-only-on-cheques`. |
| `dotnet run -- reconcile-money` | **NOT RUN**, same reason. The spec asks for it around L8 and L9. ⚠️ Neither change touches an arithmetic path — L8 slice B adds a `GROUP BY` beside an existing SUM, L9 adds a dimension — so **no figure should move**, which is precisely why the before/after diff is worth having as evidence rather than as an assumption. |
| Eye pass | **NOT DONE — no browser in this environment.** Stated per § 14 rather than claimed. Surfaces named below. |

### ✅ The suite caught nine breakages of my own
Worth recording, because none was visible to the compiler:
1. **Seven** failures across `CaisseLedgerTests` and `MoneyReadConsistencyTests` — the caisse summary now reads the
   per-method breakdown, and Moq returns **`null`** for an unstubbed `Task<IReadOnlyList<T>>`, which the merge
   dereferences. Every assertion in both files collapsed to « `Result.IsSuccess == false` », i.e. the money
   invariants were *not being checked at all* while looking like ordinary failures. Stubbed in the shared `Wire()`
   arranger (not in a helper) because several tests build the handler inline.
2. **Two** in `DashboardTrendReaderTests` / `DashboardTenantIsolationTests` — Moq requires a `Callback` delegate
   whose signature matches the method **exactly**, so adding `doctorId` to a `Setup` matcher without adding it to
   the paired lambda silently stops the callback firing and the captured window is never recorded.

⚠️ **A mechanical sweep over the test project needed reverting twice**: `GetFilteredAsync` exists on the audit, plan
and procedure-type repositories too, and `ReadAsync` on all four dashboard readers — none of which gained a
parameter. Both over-matches were caught by the compiler, but they are why the per-site fixes in this session are
enumerated by file and line rather than applied by regex.

## Eye pass still owed (session 4)
- **La caisse at 320 / 390 / 820 px** — the « dont » chip row wraps (four chips × label + amount); confirm the chips
  do not overhang each other on a coarse pointer, and that the « Mode » filter chip + its caveat sentence sit above
  the extrait without pushing the table sideways.
- **`/cheques` at 320 / 390 / 820 / 1180 / 1440 px** — four `KpiGrid` figures two-up at 320 px, and the eight-column
  table must be **cards** below `lg:` (820 px is the width that hinge exists for).
- **The patient dialog's CNAM block at 320 px and 820 px** — the two ceiling fields are `sm:grid-cols-2` and each
  carries a prose hint; the amber estimate note must not push the dialog's footer off a landscape phone.
- **The BS1 editor with a patient selected** — `CnamCeilingNotice` renders above the acts; check the « dépasse le
  reste disponible » state and the failed-read « Réessayer ».
- **`/documents/arret-travail` at 320 / 390 / 820 px** — the practitioner Select, the two-up duration row, the
  « Sorties autorisées » `<details>`, and the live P 061 preview iframe beside it.
- **`/factures` and the dashboard's Argent section with two practitioners in the roster** — the Praticien Select
  appears, the « antérieures à la mise à jour » note wraps, and the Argent scope caveat is legible at 320 px.

## Auto-Approved Deviations (session 4)
| Deviation | Reason |
|-----------|--------|
| The caisse breakdown chips **are** the extrait's method filter, rather than four read-only cells plus a separate « Mode » Select | The figure-links-to-its-records rule the dashboard already follows. A separate control would put the number and the way to inspect it in different places, and the spec asks for both a breakdown and a `method` filter — this is one affordance for both. |
| `/cheques` got a CSV export the spec's L5 list does not name | The nine L5 lists were enumerated before this screen existed, and « take the list to the bank » is the use case. ~20 lines, and a new money list *without* one would be the only list in the product missing it. |
| The three CNAM supplements (+100 ascendant, +100 enfant handicapé, +150 grossesse) are **quoted** beside the override field rather than modelled | Each turns on a fact the product does not record, and three more nullable columns holding facts nobody maintains is how a setting ships with no caller. Naming the amounts lets an admin compute the real ceiling once and type it in — which the calculation then trusts absolutely. |
| The bundled P 061 scan is **re-saved rotated** into `Assets/P61.pdf` rather than rotated at draw time | Every coordinate in the renderer then matches a ruler on the printout, and nothing depends on PdfSharp's `/Rotate` handling. One transform, done once, recorded in the csproj comment and the renderer's doc block. |
| `isOfficialForm` replaced four `documentType === "bulletin-cnam"` comparisons in the editor | The two overlay documents share every mechanism the four free-form ones do not (iframe preview → iframe Print, no `doctors[0]`, a pre-Save gate, no Word export). Naming the actual reason beats a predicate that happens to select the same two types. |
| `ICnamBillingCalculator` gained a member instead of a new calculator being written | It resolves the same acts against the same catalogue through the same per-act calculator; a separate implementation would be a second authority over a reimbursement figure, which is the § 5.10 defect. |
| `InvoiceMappingExtensions.ToDto` gained a **defaulted** `doctorName` | Keeps ~20 call sites unchanged, and an unattributed invoice and one whose caller did not resolve names both render « non attribué » — the honest reading of both. |
| The L9 filters are hidden on a single-practitioner clinic | A control with exactly one meaningful value reads as broken, and the single-dentist practice is the common Tunisian case. |
| `lib/api/patients.ts`'s inline `cnamInfo` literal replaced by the shared `CnamInfo` type | It was a re-listed copy of the same shape, so L10's two fields typechecked on the update path and failed on create. A copy of a shape is a copy that goes one field out of date. |

## Significant Deviations (session 4)

**DEV-14 — L11's form choice is a judgement, and it is recorded as one.**
*Spec:* « ⚠️ Which of the three bundled PDFs is the current official form is **unverified** — settle that before
calibrating. »
*Settled how:* by reading all three. `P61_2024.pdf` is titled « CERTIFICAT MÉDICAL D'ARRÊT DE TRAVAIL »;
`CMIATMP.pdf` is the AT/MP certificate and P 061's own header says it does not cover those situations, so they are
complementary rather than alternative; `p61.pdf` is the same P 061 in an older printing. The 2024 file is therefore
the one, and the reasoning lives in the renderer's doc block so a future reader does not re-derive it.
*What remains unverified:* no official CNAM publication was retrieved. An operator who finds the caisse using
another revision must recalibrate — the renderer says so.
*Approved:* not asked (standing « do not stop »); recorded for review.

**DEV-15 — the L9 dashboard filter narrows two figures of five, deliberately, and says so on screen.**
*Spec:* « A **practitioner filter** on the dashboard's Argent section and on `/factures`. »
*The problem the spec does not address:* an expense has no practitioner. Rent and salaries belong to the practice,
so « Net » under a filter would be one dentist's income minus the whole clinic's costs — a figure that means
nothing and *looks like a loss*. Créances are owed to the practice too.
*Chosen:* narrow « Encaissé » and « Facturé »; leave Dépenses, Net and Créances clinic-wide, and carry that as
**two DTO fields** (`ClinicWideOutgoings`, `CollectedInvoicesOnly`) so the page labels them rather than each screen
inventing its own wording. A filtered « Encaissé » is also invoice-payments-only, because the plan ledger's SUM
gained no filter here — mixing one filtered ledger with one unfiltered one overstates by an unknown amount, which
is worse than a stated scope.
*Approved:* not asked; recorded for review. **This is the L9 decision to read first.**

**DEV-16 — the L9 migration nulls orphaned `PreferredDoctorId` values before adding the FK.**
Not in the spec, and the migration fails without it: the column has been unconstrained for the product's whole
life, so a row may point at another clinic's doctor or a deleted one, and `AddForeignKey` over such a row aborts the
upgrade **after** the columns are already added. Three queue entries losing a preference is the correct trade.
*Approved:* not asked; recorded for review.

**DEV-17 — the model snapshot regained three entities a hand-written one had dropped.**
`dotnet ef` worked this session (SAC's verdict is time-varying), so both migrations are tool-generated. Regenerating
the snapshot revealed that the hand-written one committed with session 3's cheque migration had **lost**
`AuditEntries`, `BackupRuns` and `DocumentEmails` — present in the model, absent from the snapshot. That is exactly
the `ef-migration-scaffolding-hazards` failure mode: the *next* migration would have tried to re-create tables that
already exist. The snapshot is EF-generated and complete again.
⚠️ `AddCnamAnnualCeiling` was **renumbered by hand** to `20260804123000` so it sorts *after* session 3's
hand-chosen `20260804120000`: the EF tool stamped it with the wall clock (10:35), and its Designer snapshot
describes the model *including* the cheque columns, so leaving it ordered first would make the next
`migrations add` diff against a model state that never existed.
*Approved:* not asked; recorded for review.

**DEV-18 — nine build-required test fixes, each preserving its original scenario.**
Per the skill's rule, and enumerated rather than swept: two harnesses gained the per-method breakdown stubs
(empty lists = the pre-change totals), three `Callback` lambdas gained the new parameter as a discard, five Moq
`Setup` matchers gained `It.IsAny<Guid?>()`, four handler-ctor sites gained a doctor repository + clinic context
mocked to « empty roster, no caller doctor » (which is what leaves the aggregate unattributed, as before), and the
twelve positional `DataMigrationCounts` constructions gained a `0` — **DEV-13's warning, realised for the second
time**. No assertion was changed and no scenario added. The durable fix for that last one is still
`CleanCounts with { … }` per test, and it still belongs to `/test-small-feature`.

## What is left in this spec — L7's imaging bridge only
Everything else is built or struck. The bridge is blocked on the question the spec itself flags as decisive:
**a browser cannot launch a local process**, so it must go through the `desktop/` WebView2 shell or a small local
helper, and that choice decides the design. Its *small* half — the file-upload error surfacing that discarded the
server's French refusal and hardcoded « vérifiez votre connexion » — was done in session 3.

## Deferred to `/test-small-feature` (session 4)
- **`CnamPlafondTests`** — the barème per dependant count and past its last band; a non-positive override ignored;
  `ConsumesCeiling` over « Prothèse » / « prothese » / « Prothèses » / null / unknown (the fold is what makes an
  open category survivable, and « unknown consumes » is the safe direction worth pinning).
- **`GetPatientCnamCeilingQueryHandlerTests`** — the clinic year (not the UTC one); a per-invoice care date so a
  patient who turned 19 mid-year gets two rates; hors-plafond reported and not deducted; `Remaining` floored at 0
  with `Exhausted` true; `CeilingIsDefault` false only for a **positive** override.
- **`ArretTravailValidationTests`** — every refusal, and specifically that a **motif is not required**; the
  « one of code conventionnel / ordre » rule in all four combinations; the outings both-or-neither rule; `> 180` days.
- **`CnamArretTravailRendererTests`** — the smoke test named above (a non-empty 2-page PDF from the real asset),
  plus the two tri-states: no hospitalisation answer ticks **neither** box, and an unrecognised trauma cause ticks
  none rather than throwing.
- **`PractitionerAttributionTests`** — the precedence in all three positions; a cross-clinic candidate falling
  through to the next source rather than being stored; `Guid.Empty` treated as none.
- **The two attribution bridges** — `CreateInvoiceFromDentalRecordCommand` and
  `CreateInvoiceFromTreatmentPlanCommand` carry the source's `DoctorId` verbatim and do **not** re-derive it from
  the caller. This is the L9 equivalent of slice A's DEV-12 and the one nothing else would catch.
- **Per-method breakdown** — `Σ CashInByMethod == CashIn` over a fixture with all four methods; all four present
  with zeros on a single-method day; a **voided** payment excluded from both (the reason the breakdown is a
  `GROUP BY` sibling of the SUM and not a grouping of the statement's rows).
- **`GetChequesDueQuery`** — the four buckets partition the set (counts sum to `TotalCount`); an undated cheque is
  returned under a bounded window and counted in `Undated`; a **bridged** plan's cheque appears exactly once.
- **`GetCaisseLedgerQuery`** — the `method` filter leaves `RunningBalance` untouched; an unrecognised value is
  ignored; a movement with no method drops out under any filter.
