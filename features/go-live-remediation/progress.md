# Progress: Go-Live Remediation

**Started:** 2026-08-26
**Type:** Small (forced — the owner's explicit one-pass decision, spec § Edge Cases: « Criticals, majors and
~88 minors land together »)
**Branch:** `feature/go-live-remediation` (created from `feature/windows-desktop-app` HEAD)

## Status
- [x] Implementation
- [x] Quality checks (`check:responsive`, `tsc --noEmit`, `next build`, `dotnet build`)
- [ ] Tests (handled by `/test-small-feature`)

## Quality gate — what was actually run

| Gate | Result |
|---|---|
| `dotnet test ClinicManagement.UnitTests -c Release` | **0 failed, 3541 passed** — was **12 failed** before this pass (see below). ⚠️ Re-using one `BaseOutputPath` across runs reports a partial total (1013) from an incremental build; a fresh output path gives the true 3541. |
| `cd api && dotnet build ClinicManagement.sln --no-incremental` | **0 errors**, 55 warnings — byte-for-byte the pre-change baseline (CS8618 x84, CS8602 x12, CS8981 x4, CS8604 x4, CS8600 x4, CS0618 x2). None in a changed file. |
| `cd web && npx tsc --noEmit` | **clean** |
| `cd web && npm run check:responsive` | **21 / 21 pass** — `version-from-a-read`, `failed-read-as-empty` and the new `french-quote-binding` |
| `cd web && npm run build` | **compiles; all 38 static pages generate** |

⚠️ Builds go to a scratch `BaseOutputPath` (MEMORY: a running API locks `api/**/bin`, and Smart App Control
intermittently refuses freshly-built in-repo assemblies).

⚠️ **The QA pass's leftover server had to be stopped.** `npm run build` failed with
`EBUSY … rmdir .next\standalone`: PID 46356 (`.next/standalone/server.js`) and PIDs 20724 / 47268
(`next start -p 3000`) were still running from the QA run. `QA-GO-LIVE-REPORT.md` § 9 already flags them as
leftovers needing a restart. Those three were stopped and nothing else; **`npm run dev` is not running and needs
starting again.**

## Eye pass — NOT DONE, and that is a real gap

The device gate's mechanical half passed and every touched surface was re-read against
`.claude/rules/frontend-web.md` § 1–2, but **no browser walk was performed**, so no widths are claimed. The
surfaces that most want one, and why:

- **`dashboard-header.tsx`** — the bell and the avatar went from `size-9` to `coarse:size-11`. That is +16 px in a
  header that also holds the connectivity indicator and the search pill. **Check 320 px on a coarse pointer.**
- **`app/patients/[id]/page.tsx`** — three fiche row actions now `coarse:size-11`, so that table's rows grow to
  44 px on a tablet. **Check 820 px.**
- **`ui/dialog.tsx` / `ui/sheet.tsx`** — `DIALOG_CLOSE_BUTTON` moves the ✕ to a 44 px box under `coarse:` with a
  compensating offset so the glyph does not shift. **Check the ✕ has not drifted**, on a dialog and on the drawer.
- **`app/journal/page.tsx`** — a sixth filter; the grid went `lg:grid-cols-5` to `lg:grid-cols-3`.
  **Check 1024 px.**
- **`components/clinic-settings.tsx`** — the four « Modifier » buttons are admin-only now, with a read-only note
  above. **Check as a doctor.**

## Working tree note (start of session)

In-flight, UNRELATED work was already modified when this session started. It was **built on, never reverted**, and
must not be attributed to this feature: `.claude/rules/frontend-web.md`, `api/.idea/**/vcs.xml`,
`SubscriptionWarningJob.cs`, `console/tsconfig.json`, `follow-up/README.md`, `web/app/caisse/page.tsx`,
`web/app/factures/page.tsx`, `web/app/globals.css`, `web/components/app-shell.tsx`,
`web/components/appointment-calendar.tsx`, `web/components/caisse/cheques-table.tsx`,
`web/components/rappels/reminder-counters.tsx`, `web/components/ui/page-header.tsx`, `web/lib/zones.ts`,
`web/scripts/check-responsive.mjs`, `web/tsconfig.json` — plus ~80 untracked `landing-v2/**` marketing files,
`check.png`, `hero-new.png` and `QA-GO-LIVE-REPORT.md`. **None are staged by this feature.**

## Files Changed

**171 tracked files modified, 49 new** under `api/` + `web/` (+3 273 / −792). By group:

| Group | Where |
|---|---|
| The six criticals | `InvoicesController`, `document-editor-content.tsx`, `app/caisse/page.tsx`, `DashboardTrendReader`, `LocalInstallPaths`/`PgDumpBackupService`, lab-orders |
| Band A (blank clears) | `UpdateClinicRequest`/`UpdateClinicCommand`/`ClinicsController`, `UpdateProcedureTypeCommand`, `clinic-settings.tsx`, `procedure-type-form-modal.tsx`, `lib/api/clinics.ts` |
| Band B (`Version`) | 12 commands, 11 DTOs, 3 new single-authority mappers, 12 forms, `SetUser*Request` |
| Band C (failed read) | `user-management`, `reminder-log-table`, `treatment-plans-table`, `patients-table`, `patients/[id]`, `caisse`, `cheques-table` |
| Band D (no internals) | `lib/api/client.ts` (`looksTechnical` on the canonical body), `ListUsersQuery`, `WaitingListLimits`, `ProcedureTypeRefusals`, `notification-panel`, lab-order/expense catches |
| Band E (primitives) | `ui/dialog.tsx`, `ui/alert-dialog.tsx`, `ui/sheet.tsx`, **38 new `layout.tsx`** + `app/layout.tsx` + `app/page.tsx`, `dashboard-header`, `dashboard-sidebar`, `patients/[id]` |
| Audit trail | `CreatePatientCommand`, `AuditSaveChangesInterceptor`, `AuditActorProvider` + iface + `ProcessAuditActorProvider`, `LoginCommand`, `GetAuditEntriesQuery`, `AuditEntryRepository`, `AuditController`, `app/journal`, `lib/api/audit.ts` |
| Auth | `ITotpReplayGuard` + `TotpReplayGuard` (new), `LoginCommand`, `RateLimiting`, `AuthAttemptAccount`, `lib/api/client.ts`, `lib/auth/session.tsx` |
| Product decisions | `patient-record-modal` (ToppedUp), `odontogram`, `DashboardProcedureMixReader` + `procedure-mix-chart`, `securite` + `login`, `ReminderMessage` |
| Per-screen one-offs | `ExpenseDay` (new) + both expense commands + caisse form, `PatientRepository.ApplySearch` **+ 5 sibling repositories**, `patient-files-manager`, `appointments` + `create-appointment-dialog`, `patients-table`/`app/patients`, 3 catalogue commands + 3 controllers + 3 tables + 3 API modules, `procedure-types-table`, `ChangeUserRoleCommand`, `RemoveToothConditionCommand` + `OdontogramController`, `MessagingSenderState` + `GetReminderAllowanceQuery`, `NotificationRepository` + `ReminderLogDto` + `app/rappels`, dashboard preferences (DTO/query/command/hook), `connectivity.tsx`, `clinic-settings` |

## Auto-Approved Deviations

| Deviation | Reason |
|-----------|--------|
| `AuthorizationPolicies` untouched; the invoices gate is evaluated **in the action** via `IAuthorizationService` | AC-1 needs 403 on `GET /api/invoices` **and** 200 on `?patientId=` — the same route. An attribute cannot tell them apart, and splitting the route would move a URL every client calls. |
| `ErrorMessages.Forbidden` added | The 403 body needs a canonical French sentence; it matches the one `client.ts` already renders for a bare 403. |
| Focus-return extended to `ui/alert-dialog.tsx` and `ui/sheet.tsx` | Same Radix primitive, same defect. AC-19 names `ui/dialog.tsx`; leaving the two siblings is the exact « fixes don't propagate » shape. |
| The props spread moved **before** the focus handlers in all three | Spreading after them silently defeated the `props.onXAutoFocus?.(event)` chaining already written there. |
| One `MedicalDocumentMappingExtensions`, `PatientFileMappingExtensions`, `ClinicUserMappingExtensions` replacing **10** duplicated inline initialisers | Adding `Version` meant editing 4 + 3 + 3 identical blocks, and a missed one returns 0 = « not supplied » = the check silently off. The user mapper also found a **live** drift: only the list set `IsPendingActivation`. |
| `app/page.tsx` became a server shell; the dashboard body moved to `components/dashboard/dashboard-page.tsx` | A client component cannot export `metadata`, and the dashboard is the one route whose title *is* « Tableau de bord ». |
| `check-responsive.mjs`'s `READS` pattern widened to accept any `Api.get*` / `Api.list*` | A per-row action legitimately re-reads through `listLetterValues()` / `listPaged()`. The stricter pattern rejected the honest fix, and the alternative was a per-file exemption — which § 14 bans. |
| Both name orders added at **5 more** repositories, not just `PatientRepository` | `SqlSearch` documents that EF cannot translate a shared helper, so the pair must be inline. The rule is now written on `SqlSearch.EscapeString`. |
| `heldByAllowance` split into `heldByAllowance` + `heldBySender` (DTO + repo + page) | Folding a stopped number into « en attente de forfait » promises more messages for a problem more messages cannot fix. |
| `DashboardPreferencesDto.IsCustomised` added | « no row » and « a row hiding nothing » both serialised as an empty list, so « Tout afficher » could not be saved. Only the server can tell them apart. |
| `MessagingSender.From` gained `sendable` (defaulted `true`) | The two console/report callers have no channel config in scope and keep their answer; the clinic-facing caller now passes `resolved.WhatsAppConfigured`. |
| Dead `UpdateClinicCommand.IsChanging` helper removed | Its only caller became the `MatriculeFiscalSpecified` test. |
| Debug `_logger.LogInformation` trio removed from `UpdateProcedureTypeCommand`'s cost branch | The block was being rewritten for the tri-state; three log lines narrating `HasValue` on a hot path. |
| Test **infrastructure** adapted to compile: `DashboardTrendReaderTests`, `LoginCommandHandlerTests`, `ClinicTotpAuthTests`, `AuditLedgerReadTests`, `ChangeUserRoleCommandHandlerTests`, `ReminderAllowanceQueryTests`, `ClinicArchiveFakes` | Spec-pinned signature changes. Every new seam is stubbed **permissively** so each existing scenario keeps its original assertion. No new scenarios written — that is `/test-small-feature`'s job. |

## Significant Deviations

**DEV-1 — the touch fixes GROW the box (`coarse:size-11`); they do not apply `.touch-target`.**
The spec pins « `.touch-target` applied where it is missing » for the header bell/avatar, the rail toggle and the
fiche row actions. `.claude/rules/frontend-web.md` § 2 forbids exactly that for these controls: they sit in ROWS
4 px apart, and `.touch-target`'s 44 px pseudo-element overhangs its neighbours — the later sibling paints last
and **steals its neighbour's taps**, next to a « Supprimer la fiche de soins ». It also leaves the *measured* box
at 36 px, which is what AC-18 asks about. Per the skill's own rule (« where the repo's own rule file contradicts,
the repo wins ») the boxes grow instead. `.touch-target` was kept only where the control is genuinely isolated.
**Flagged here because the spec named the other mechanism.**

**DEV-2 — CORRECTED. The per-screen reports DO exist, and the minors were implemented.**
An earlier revision of this file claimed the ~88 minors « are not enumerated anywhere reachable ». That was
wrong, and it was wrong because only the repo and my own session scratchpad had been searched. The owner pushed
back (« how are 88minors not done!!! »); the full **4,433-line** `GO-LIVE-REPORT.md` plus 24 per-screen `.md`
files were in **another session's** scratchpad:

```
%LOCALAPPDATA%/Temp/claude/C--Users-Oumayma-Benkhalifa-Desktop-clinic-management/
  ca79795d-9285-49e2-8af6-afcfe248a874/scratchpad/qa/reports/
```

`QA-GO-LIVE-REPORT.md` in the repo is only the 306-line consolidated view. ⚠️ **That is a temp path and will
not survive indefinitely** — anything still wanted from it should be copied into the repo.

All 132 findings were then worked from the full report. The lesson that generalises: *a count in a summary is
not a licence to declare the detail unreachable.*

**DEV-3 — TOTP replay and the refresh partition are IN-MEMORY, per the spec's « no migration ».**
`TotpReplayGuard` and the session partition key both live in `IMemoryCache`, like the existing
`LoginAttemptTracker`. Known limit, documented on both classes: on a multi-instance hosted deployment a replay
routed to another instance is not caught. A durable spent-code table would need a migration, which
`Data / Schema Changes: None` forbids. Correct and complete for `SelfHostedLan` and for every replay that lands
on the same instance.

**DEV-4 — `ChangeUserRoleCommand` mints the `Doctors` row with a derived name and a generic specialty.**
The create path *requires* `DoctorInfo`; the promotion path has no form behind it (it is a rôle `Select` on a
row), so the name is split from the account's own `FullName` and the specialty is « Dentiste ». An unnamed
practitioner record would be unpickable in the séance form; « Mon profil » is where the dentist completes it.
Asking for the fields would mean a new dialog the spec does not describe.

## Third pass — the last 13 findings (settings · lab-orders · patient-files · fichiers)

Every item the second pass left open was re-verified **against the tree** rather than against the report — which
is how the four already-closed clusters below were found. Of 13:

| Finding | Outcome |
|---|---|
| fichiers — at 320 px the closing guillemet of the empty state wraps onto a line of its own | **Fixed, and propagated.** New `quoteFr()` in `lib/format.ts` binds both guillemets with a narrow no-break space (`U+202F`). The report named one site; the mechanism was at **59 sites across 30 files**, in two shapes — template literals and JSX text nodes. Held by a new `check:responsive` check, `french-quote-binding`, derived from the mechanism rather than from a file list. |
| lab-orders — « Nouveau bon » 36 px and each row's action trigger 36 px on a coarse pointer | **Fixed** (`coarse:h-11` / `coarse:size-11`, the patterns `/caisse` and `/waiting-list` already use). « Exporter » already carried `coarse:h-11` from the first pass. |
| lab-orders — the patient link in a card title measured 114×24 | **Not a defect.** `card-list.tsx` gives the title `after:absolute after:inset-0` inside a `relative` list item, so its hit area is the whole card. The report measured the anchor's own box. |
| lab-orders — FDI not validated: 99 stored, `ab` dropped in silence | **Fixed by wiring the authority that already existed.** `FdiTooth.IsValid` was used by `DentalRecordAct`, `ToothState` and `TreatmentPlanItem` — and not by `LabWorkOrder`. Added `FdiTooth.Refuse` / `NotAToothNumber`, guarded both domain doors and both commands, and gave the form a field-level message via a new `isFdiTooth()` derived from `MIXED_FDI` (no second range table). **Also collapsed `DentalRecordTooth`'s private duplicate** of the same range table — the exact shape `FdiTooth`'s own docstring said it had replaced. |
| lab-orders — the edit form pre-filled a cost as 133.25 | **Fixed** — `formatAmount` gives « 133,250 », on the one field the app spends effort teaching the comma on. |
| lab-orders — no laboratoire filter, and the linked fiche's nom is not searchable | **Fixed.** A `SupplierId` filter plus the fiche's nom in the SQL search predicate (a subquery, not a navigation — `LabWorkOrder` deliberately holds only `SupplierId`, because the bon prints the name it was raised with). New « Laboratoire » picker on the page; `SupplierPicker` gained an `emptyLabel` prop so a filter reads « Tous les laboratoires » instead of « Aucun ». |
| lab-orders — nothing marks a late bon, and the list cannot be ordered by « Prévu » | **Fixed.** New `LabOrderOverdue` states the rule once as an `Expression`: `CountOverdueAsync` translates it to SQL and the DTO mapping uses the same expression `.Compile()`d, so the dashboard's « Prothèses en retard : N » and the rows wearing a badge cannot be two different N. Served `isOverdue` badge on the « Prévu » date (table + card) and a « Trier par » control, both round-tripped through `useUrlFilters`. |
| patient-files — Grille/Liste 36 px · « Patient introuvable » repeated as its own body | Already closed in an earlier pass (verified). |
| patients — the create does not lead to the created patient's dossier | Already closed (verified: `router.push` to the new id, with a list refresh as the fallback). |
| patient-detail — the flag DTO returned a null `patientId` and `0001-01-01` | Already closed (verified: one `PatientFlag.ToDto()` in `PatientMappingExtensions`). |
| account — `/securite` never said the affirmative half of « peut-on le désactiver ? » | Already closed (verified, `securite/page.tsx:274`). |
| **settings — all 6 minors** | **All already closed**, verified one by one: the lunch break is a real shape end-to-end (`WorkingDay.breakFrom`/`breakTo`, `WorkingDayDto.BreakFrom`/`BreakTo`, `ValidateBreak`, and `WorkingHoursResolver` refusing an appointment inside it); `validateWorkingHours` is one copy called by both editors and it names the day; an empty clinic name is a field-level French message, not the wire key in a toast; the Médecins card's inputs carry `htmlFor`/`id`; the « identité documentaire » trigger has an `aria-label` naming the practitioner; and the agenda draws per-practitioner hours (`doctorHours ?? clinicHours`) and states « cabinet fermé » on the phone strip. |

⚠️ **A changed number worth naming: `LabOrderOverdue`'s cutoff is the start of the clinic-local day, not
« now ».** `ExpectedDate` holds a date stored at midnight, so the old `ExpectedDate < nowUtc` made a bon due
**today** late from 00:01 onwards. That was invisible while a count was the only surface, and immediately visible
once a row wears a badge. Both surfaces read the one file, so **the dashboard's « Prothèses en retard » number
now excludes bons due today** — a correction, but a changed number, so it is stated here rather than buried.

## Browser verification — the fixes were driven in a real browser

Signed in as the dev admin on `next dev` + a **freshly restarted** API, at 1440 / 390 / 320 px.

⚠️ **The trap that nearly invalidated the whole check.** The first pass of the walk showed **no « En retard »
badge at all**. The code was right; the *running API was the old binary*. Every `dotnet build` in this feature
used a scratch `-p:BaseOutputPath` (the MEMORY note about a running API locking `api/**/bin`), so `bin/Debug` —
which is what `dotnet run` serves — was never updated. **A browser check of any backend change must stop the
API, rebuild into `bin/Debug`, and restart it**, or the frontend is verified against last week's server and the
result looks like a frontend bug.

| Verified | Evidence |
|---|---|
| « En retard » badge on the right rows | Leila Gharbi (prévu 24 août) and Karim Hamdi (21 août) badged; Youssef Mrad (27 août) not; Fatma Zouari (`Reçu`) not. |
| The cutoff change | Sonia Trabelsi, **due today** (26 août), carries no badge — the old `ExpectedDate < nowUtc` would have flagged her. |
| `isOverdue` is served, not re-derived | `GET /api/lab-orders` returns `[Sonia false, Fatma false, Youssef false, Leila true, Karim true]`. |
| Laboratoire filter | `?supplierId=…` → 3 bons, and the picker trigger names « Laboratoire Ben Aissa » through `selectedFallback`. |
| The linked fiche's nom is searchable | « Ben Aissa » → **3 bons** (« 1–3 sur 3 »). The report measured « Aucun bon ne correspond » for this exact term: the three bons carry the free-text prothésiste « Labo Dentaire El Manar ». |
| Order by « Prévu » | `?sortBy=expected` → 18 · 21 · 24 · 26 · 27 août ascending, control seeded to « Date prévue ». |
| Cost pre-fill | Editing Leila's bon shows « **450,000** » (was `String(450)` → « 450 »). |
| FDI refused client-side | `99` → « Numéro FDI invalide : 11–48 (adulte) ou 51–85 (enfant) », `aria-invalid="true"`, `aria-describedby` wired, dialog held open. `ab` → the same message, instead of being dropped in silence. |
| FDI refused server-side | `PUT /api/lab-orders/{id}` with `toothNumber: 99`, bypassing the form → **400** `{"error":"Numéro de dent invalide. Utilisez la notation FDI : …"}`. |
| Coarse targets | This browser reports `pointer: fine`, so the `coarse:` rules cannot apply and the measured boxes are the fine ones (36/36/32) the QA agent also measured. What IS verifiable here: the classes are on the elements, and the served CSS contains `@media (pointer: coarse){ .coarse\:h-11{height:calc(var(--spacing)*11)} }` and the same for `.coarse\:size-11` — 44 px. **The coarse measurement itself was not reproducible in this harness.** |
| fichiers, 320 px | `« zzzznope »` renders as `«` `U+202F` `zzzznope` `U+202F` `»` (codepoints read off the DOM). A `Range` puts the opening guillemet, the term's last letter and the closing guillemet all at **y=468**. The report measured the `»` at y484 against a first line at y459 — a full line below. |
| The sweep, on a real dialog | `/fournisseurs` → Supprimer renders `Supprimer «[U+202F]Laboratoire Ben Aissa[U+202F]» ?` — the hand-edited JSX form. Cancelled; 4 rows intact. |
| settings — lunch break | 28 time inputs = 7 days × 4, with ids `clinic-hours-Monday-break-from` / `-break-to`, and the database really holds `"BreakFrom":"12:00","BreakTo":"14:00"` on every open day. |
| settings — hours name the day | Lundi 18:00 → 09:00 + Enregistrer → « **Lundi : l'heure de fermeture doit être postérieure à l'ouverture.** » No request sent, the typing kept. |
| settings — empty clinic name | Field-level « Le nom de la clinique est obligatoire. » with `aria-invalid="true"` and **no toast** — the wire key `name` is gone. |

**Nothing was left mutated.** Verified after the walk: the clinic name and `WorkingHoursJson` are byte-identical
(Monday `09:00–17:00`, break `12:00–14:00`), and the lab orders' tooth numbers are `45 · 36 · 11 · 26` + one
null — every `99` / `ab` attempt was refused rather than stored. The one supplier delete dialog was cancelled.

### Two defects the browser check itself found

**BV-1 — the badge broke the table's fit, and only a browser could say so.** Inline beside the date,
« En retard » pushed the lab-orders table **45 px past its container** at 1440 px and clipped the « Actions »
column — on a table an earlier pass had deliberately trimmed to fit a laptop. The document never scrolled
horizontally, so `check:responsive` stayed green and `tsc` had nothing to say: this was invisible to every
mechanical gate. Stacking the badge **under** the date returned the table to exactly 1086 px (0 overflow) and
costs only the badge's own width. Fixed, re-measured, and clean at 320 px too.

**BV-2 — `/lab-orders` wrote a `?search=` it could not read back.** `useUrlFilters` has always written
`search`, and nothing seeded it, so the screen produced links it discarded on the next load — and it then
*rewrote the URL to drop the term*, which is how it was caught: `?search=Ben Aissa` came back as an unfiltered
`/lab-orders`. Now seeded (both `search` and `debouncedSearch`, so the first read is already filtered rather
than fetching the whole list and refetching 300 ms later). Checked the three sibling screens for the same shape:
`patient-files-directory` seeds all three of its keys, `journal` seeds six of seven (`page` is not seeded — a
page-2 link opens on page 1, pre-existing and left alone), `appointments` seeds `doctorId` only.

## Fourth pass — the full 132-finding sweep, and the two it turned up

The previous passes worked from the report. This one walked **all 132 findings against the tree**, screen by
screen, on the reasoning that a finding believed closed and a finding verified closed are different things. 130
were confirmed closed (or are one of the 4 the spec puts out of scope). Two were not.

**FP-1 — `journal` #6: four audit types were still rendered with their CLR name.**
`AuditLabels.Entity()`'s `_ => entityType` fall-through is documented as deliberate, and the report's point was
that **these four are types this clinic has rows for today**: `BackupRun`, `ClinicRecoveryPoint`,
`ClinicSubscription`, `SubscriptionPeriod`. They appeared in English in the « Type » filter *and* in the
« Dossier » column of the one screen an owner reads to answer « qui a fait quoi ? ». Named:
« Sauvegarde », « Point de restauration », « Abonnement », « Période d'abonnement ». Verified live —
`GET /api/audit` now returns 23 types with **zero** fall-throughs.

**FP-2 (BV-3) — `/appointments` wrote `?date=` / `?view=` and read back neither.**
The same shape as BV-2, on the screen the report called the worst case. `useUrlFilters` wrote both keys; nothing
seeded either, so `/appointments?date=2026-08-31&view=week` opened on **today in Jour** — the shared link, the
reload and the « regarde le lundi 24 » message were all silently wrong, and the URL then rewrote itself to today
so the evidence erased itself. Two causes, both fixed:

- **No read half.** `seededDay()` / `seededView()` now seed `selectedDate` and `view` in their `useState`
  initialisers (lazy, so the first fetch is already for the right day — the same reasoning as `useUrlFilterSeed`),
  and `viewDecidedRef` starts `true` when the URL named a view, so the narrow-screen Jour default cannot
  overwrite an explicit one.
- **The deep-link effect re-consumed its own output.** Its `replaceState({}, "", "/appointments")` *was* the
  one-shot guard, and it stopped being one the moment `useUrlFilters` began writing `?date=` back: a second run
  read the durable state as a fresh deep link. `?view=` is omitted when it is the default, so a reloaded Semaine
  became `?date=…` with no view — exactly the shape that forces Jour. Now guarded by `deepLinkHandledRef`, and
  `?date=` forces Jour **only when the URL named no view**, so the dashboard's « Demain » link is unchanged.

⚠️ **The generalisable lesson, and it now has three instances (BV-2, FP-2, and `journal`'s unseeded `page`):**
`useUrlFilters` is a *write*. Every screen that mounts it must seed the same keys, or it manufactures links it
discards. Worth a derived check next to `version-from-a-read` — a screen calling `useUrlFilters({k: …})` and
never reading `k` from the query string is mechanically detectable.

### What this pass verified in the browser

Same stack (`next dev` + the hot-reloading API), 1440 / 390 / 320 px, signed in as the dev admin.

| Verified | Evidence |
|---|---|
| The lunch break is a real shape, end to end | 7 break-from inputs on the clinic card + 14 on the two practitioner cards; saving wrote `"BreakFrom":"12:00","BreakTo":"14:00"` on all five weekdays and left Saturday null. |
| Every pause rule refuses **by name** | half a break → « Lundi : indiquez le début ET la fin de la pause… »; inverted → « …la fin de la pause doit être postérieure à son début. »; outside the window → « …la pause doit être comprise entre 09:00 et 17:00. » |
| Inverted opening hours name the day | Lundi 18:00 → 09:00 → « **Lundi : l'heure de fermeture doit être postérieure à l'ouverture.** », no request sent, the typing kept. |
| The pause is **enforced**, with a French reason | `POST /api/appointments` at 12:30 → 400 « Le lundi, le cabinet est fermé de 12:00 à 14:00… tombe pendant cette pause. » (`outside_working_hours`). A **straddling** 11:45–12:15 is refused too — the overlap test, not containment. 09:00 is accepted. |
| The pause is **drawn** | Semaine at 1440 px: 8 hour rows × 7 columns, hatched exactly `XXXXX..` on rows 4 and 5 (12:00 and 13:00) — the five weekdays that carry the pause, Saturday (none) and Sunday clear. |
| The phone strip states the closure | 390 px: `dim.6` renders « fermé » under the date and its accessible name is « dimanche 6 septembre — **cabinet fermé** ». |
| Médecins card labels | All 5 fields per practitioner have a `label[for]`; the read-only « Identité documentaire » pair is a `role="group"` with `aria-labelledby`. |
| The fourth « Modifier » is named | « Modifier l'identité documentaire de **Salma Ben Youssef** » / « …de **Nadia Trabelsi** ». |
| `/securite` says the affirmative half | With `isRequired` intercepted to `false`: « Cette installation n'impose pas le second facteur : vous pouvez le désactiver… » **and** the « Désactiver » button. (This deployment requires it, so the branch is unreachable without the intercept — the API's `Deployment:Profile` was deliberately not changed.) |
| `/journal` type labels | 23 types, all French, `stillEnglish: []`. |
| The agenda URL round-trip | `?date=2026-08-31&view=week` → « 31 août – 6 sept. 2026 » in **Semaine**; bare `?date=2026-09-02` still lands in **Jour** (the dashboard link's behaviour, intact). |
| `/fichiers` at 320 px | « Aucun résultat pour «[U+202F]zzzznopeaucunpatient[U+202F]» » in **one** line box (`getClientRects().length === 1`). |
| The patient-flag DTO | `GET /api/patients/{id}` returns the flag with the real `patientId` and `createdAt: 2026-08-26T14:54:43.521388Z` — matching the row, no `Guid.Empty`, no `0001-01-01`. |
| « Patient introuvable » no longer repeats itself | With the read forced to 404: heading, then « Ce dossier a peut-être été supprimé, ou le lien n'est plus valable. », then « Réessayer » — the server's identical sentence is dropped. |
| Console | Only the two already-known app-wide `/api/connectivity` 404s, plus the 404 I injected. No page error. |

⚠️ **The Grille/Liste 44 px target is CSS-verified, not pixel-measured.** This browser reports `pointer: fine`
and CDP's `Emulation.setEmulatedMedia` does **not** emulate `pointer` (`matchMedia('(pointer: coarse)')` stays
false), so the `coarse:` rules cannot apply here. What is verified: `coarse:h-11 coarse:min-w-11` are both on the
element, and the served CSS contains `@media (pointer: coarse){ .coarse\:min-w-11{min-width:calc(var(--spacing)*11)} }`
— 44 px. The width, not just the height, was the finding: below `sm:` the label is `sr-only`, so the control is a
16 px icon in `px-3` = **36 px wide**, and the height fix alone left it short on the axis that was actually short.

**Nothing was left mutated.** The clinic's `WorkingHoursJson` was restored to exactly as found (`09:00–17:00`
Mon–Sat, Sunday closed, **every `BreakFrom`/`BreakTo` back to null**) — the pause was a test, not a
configuration decision to leave behind. The probe appointment (`fc109e86…`) was deleted; `PAUSE-PROBE` rows: 0.
The clinic name was restored and its card cancelled rather than saved. `settings-hours.png` removed from the repo
root — the other ~23 PNGs there are pre-existing marketing captures and were left alone.

⚠️ **Parallel session note.** This feature was also being extended from another session while this pass ran
(`quoteFr`, `french-quote-binding`, `FdiTooth.Refuse`, `LabOrderOverdue`, the lab-orders supplier filter, BV-1 and
BV-2 are all theirs). The two sets of changes are complementary and the full gate is green with both in the tree —
but **`git diff HEAD --numstat` before staging**, because neither session's file list is the whole diff.

## The six criticals, verified — and one of them was still broken

The browser pass above covered only this feature's **third**-pass findings. The criticals were closed in the
first pass and had never been exercised, so they were driven end-to-end here: real role tokens minted against
the API (the doctor and secretary accounts are password-only, so no TOTP is needed for them), plus the browser
for the two that are frontend defects.

| Critical | Verdict |
|---|---|
| **1.1** A secretary can read the whole invoice ledger | **CLOSED.** `GET /api/invoices?pageSize=200` as `qa.secretary` → **403** with the canonical French `{ error }`. The report measured **200 with 73 invoices**. `?patientId=` → **200, 7 invoices**, so reception keeps the read it actually needs; `/invoices/revenue` → 403; doctor and admin → 200 with 72. The gate is on the action, not the route. |
| **1.2** Reopening a saved document loses its content on the next save | **CLOSED, and this is the decisive test.** `/documents/prescription?id=…` now renders the stored medication (`QA-docs-Amoxicilline` · 500mg · 3 · 7), the patient and the date in « Mettre à jour » mode — the report had an *empty* form with no `GET` issued at all. Saving from it left `contentJson` **byte-identical** (251 chars before and after, `medications` non-empty) while `version` advanced, so a real write happened and destroyed nothing. The old bug PUT `{"medications":[],"renewals":""}`. |
| **1.3** `/caisse` races itself and shows another period's money | **CLOSED, under an induced race.** The month's reads were delayed 2.6 s so the superseded response landed *after* the day's. Screen: **14 220,000 / 275,500 / 13 904,500** — exactly the API's figures for 21 Aug (`cashIn 14220`, `cashOut 275.5`, `net 13904.5`), not the month's (`19776` / `1911` / `17805`). `requestGeneration` discarded the stale read, and it did not clear the live read's spinner either. |
| **1.4** Concurrent edit silently reverts money on a lab order | **CLOSED.** Fresh-version `PUT` → 200; a second `PUT` carrying the **stale** version and `cost: 1.111` → **409** with « Cet enregistrement a été modifié par quelqu'un d'autre… ». Cost still **210**. The report reproduced coût 77,500 → 14,000 under a success toast. |
| **also-a-blocker** « Encaissé » KPI disagrees with its own chart | **CLOSED.** `money.collected.current` = **19776** and `trend["2026-08"].collected` = **19776**. The report measured 19 910 vs 18 960 — a 950 DT gap. |
| **also-a-blocker** Every QuestPDF output dead process-wide | ⚠️ **WAS STILL BROKEN. Found and fixed here — see below.** |

### The PDF critical was not actually closed, and the browser is the only thing that could have said so

`POST /api/medical-documents/generate-pdf-download` still returned **400** — « Erreur lors du téléchargement du
PDF » — on the real « Télécharger PDF » button. The server's inner exception was exactly what the QA report had
diagnosed:

```
System.UnauthorizedAccessException: Access to the path
  '…\bin\Debug\net8.0\Backups\clinic-backup-20260804-130101' is denied.
  at QuestPDF.Drawing.FontManager.<RegisterLibraryDefaultFonts>g__SearchFontFiles|10_0()
  at QuestPDF.Drawing.FontManager..cctor()
```

**Why the first pass's fix was incomplete.** `LocalInstallPaths.DefaultBackupRoot` moved *new* backups to
`%ProgramData%/ClinicManagement/Backups`, which is right and necessary — but it does nothing about a folder
that is **already** under the install directory. This machine had three, and so would any clinic that had run
a backup before upgrading. The renderer stays dead there, permanently, with the fix "applied".

**Two things measured on the way, both worth keeping:**
- **Renaming the folder does not help.** `Backups` → `Backups.qa-disabled` + restart still 400: `FontManager`
  walks the **whole tree** under `AppContext.BaseDirectory`, not a folder of a given name. The exception then
  named `Backups.qa-disabled`, which is what proved it.
- **Moving it out of the tree fixes it instantly.** Same document, same build, folder moved one level up to
  `bin/`: **200, `application/pdf`, 49 627 bytes, `%PDF-`**, file delivered.

**The fix: `LegacyBackupRelocation`** (new, `ClinicManagement.Infrastructure`), called from `Program.cs`
immediately after the startup banner — before anything can touch QuestPDF. If a `Backups/` folder exists under
the install directory it is moved **whole** to `<DefaultBackupRoot>/legacy-install-dir` (with a `-2`, `-3` suffix
so a second upgrade cannot overwrite the first), and the move is logged as a warning in French. It never throws:
a clinic that cannot move the folder must still start, and it then loses PDFs — which is what it already had —
rather than its server.

⚠️ **The folder is moved, never enumerated.** Its *children* are the unreadable part, so
`Directory.GetDirectories` on it would throw the very exception this exists to avoid. A directory move needs
write access on the parent, which the app has.

**Verified from a cold start with the poisoned folder deliberately put back:**

```
[WRN] Sauvegardes héritées déplacées de …\bin\Debug\net8.0\Backups
      vers C:\ProgramData\ClinicManagement\Backups\legacy-install-dir
      — elles bloquaient la génération de PDF depuis le dossier d'installation.
```

…no `Backups/` left under the install directory, the three backups preserved under the new root, and
« Télécharger PDF » → **200 / application/pdf / 49 627 bytes / `%PDF-`**.

### ⚠️ Still not verified: the 51 majors

Six criticals and the third pass's 13 findings have now been exercised. **The ~51 majors have not been** — they
were closed in the first and second passes and remain code-reviewed only. That is the honest remaining gap, and
the QuestPDF result is the argument for closing it: a fix can be correct in the diff, pass every mechanical
gate, and still not work on the machine it ships to.

## The suite was red, and one of the twelve was a product bug

The unit tests had not been run in this feature at all — the first two passes wrote none and this one was told
not to, so nobody executed the ~3 500 that already existed. **12 were failing**, both causes introduced by the
earlier passes of this same feature. Neither is in a file this pass touched; both are fixed here rather than
left for `/test-small-feature`, because a red suite is not a missing test.

**T-1 — `ReminderSettingsMappings.cs:41` dereferenced a null `settings`. A PRODUCT bug, not a test one.**
`Version = settings.Version` was the **only** unguarded access in a method whose every other line reads
`settings?.` and whose own docstring promises « A clinic with no settings row (`settings` null) maps to an
all-inherit DTO … GET works even before anything is saved. » It arrived with band B's `version` round-trip.
Effect: a clinic with no `ClinicReminderSettings` row **500s** on the read behind « Rappels » — i.e. every
freshly-provisioned cabinet, on the screen it would open first. Three tests were reporting it
(`ReminderSettingsMappingsTests` directly with an NRE, and two handler tests as
`Assert.True(result.IsSuccess)` → false, because the handler's catch turned the NRE into a failed `Result`).
Now `settings?.Version ?? 0`, and **0 is the right token**: « not supplied », which skips the concurrency check —
correct for a row that does not exist yet.

⚠️ **The compiler had been saying so the whole time.** `warning CS8602: Dereference of a possibly null
reference` at `ReminderSettingsMappings.cs(41,23)` sits in the build output of every gate run in this file,
including the ones recorded above as « 0 errors, 56 warnings — none in a changed file ». It *was* in a changed
file. 56 pre-existing warnings is the noise floor that hid it, and « the count did not go up » is not the same
check as « none of them is mine ».

**T-2 — nine `AuditLedgerReadTests` failures from a stale Moq arity.** `IAuditEntryRepository.GetFilteredAsync`
gained the `string? userId` actor filter (the audit major « no actor filter exists at all »). All three
`Setup(...)` calls in the file were updated to nine `It.IsAny<>` matchers — and the three `.Callback<...>` type
lists beside them were left at eight. **Moq validates a callback's arity at run time, not compile time**, so the
file compiled and `progress.md` recorded it as « adapted to compile ». That is exactly the gap: adapted to
compile is not adapted to pass. Fixed in all three (two date-window captures + `CaptureAction`).

## Branch topology, checked rather than assumed

`feature/go-live-remediation` is cut from **`feature/windows-desktop-app`** (merge-base `5d1fafa4`, that
branch's own head), **not** from `main`. `main` is `9798b95d` « feature/stock-persistence », last commit
**2026-07-07**: **483 commits behind** this branch and **0 ahead**, so it contributes nothing and is ~7 weeks
stale. ⚠️ A PR from here to `main` would therefore carry the **whole `windows-desktop-app` line plus this
feature**, not the go-live remediation alone — worth deciding deliberately rather than discovering at review.

## The majors — verified at the wire, with two genuine holes found

44 findings carry a `[major]` heading in the full report. They were closed in the first two passes and had
never been executed. Exercised here with real role tokens (the doctor and secretary accounts are password-only).

### Closed, measured

| Finding | Evidence |
|---|---|
| **L656** a dépense's date was midnight in the *workstation's* timezone | A bare day `2026-08-19` stores `2026-08-18T23:00:00Z` — midnight **Tunisian**, so the caller's timezone cannot move it. |
| **L668** `POST /api/expenses` with no date stored `-infinity` | **400** `{"error":"Une date est requise pour cette dépense.","code":"expense_date_required"}`. |
| **L676** la caisse offered « Supprimer » to a praticien | Doctor `DELETE /api/expenses/{id}` → **403**. |
| **L683** a dépense had no concurrency control | Fresh PUT 200, stale PUT **409** French, amount still 1,500 DT. |
| **L849** the three catalogues had no concurrency control | **See the hole below** — now 409 on all three. |
| **L869** a deactivated catalogue entry could never be reactivated | All three: deactivate 204 → `POST /{id}/activate` 204 → `isActive: true`. |
| **L1584 / L2720** search missed the name order the screen displays | « Prénom Nom » and « Nom Prénom » both return 12. |
| **L1912** creating a record wrote a phantom « Modification » | A freshly created patient has **exactly one** audit row, `Insert`. |
| **L1944** `GET /api/audit` with no page params returned the whole ledger | 200 items of 1 669; `pageSize=100000` also clamped to 200. |
| **L1954** every human sign-in was journalled as « Tâche automatique » | Signed in as the doctor, then read the newest `User` row: actor `qa.doctor@ibnkhaldoun.test`, **`isSystemActor: false`**. |
| **L1968** no actor filter existed | `?userId=` narrows 1 680 → 6, and all six belong to that one actor. |
| **L2129** a « date prévue » before the « date d'envoi » was accepted | **400** « La date prévue ne peut pas être antérieure à la date d'envoi. » |
| **L2140** deleting a received bon orphaned its caisse dépense | Created a bon with a coût, marked it `Received` (dépense written, `expenseId` returned), deleted the bon → **0 matching dépenses left**. |
| **L2970 / L2989** clearing « Coût par défaut » / « Description » was a silent no-op | Both clear: `defaultCost` → null, description → empty, via the tri-state `defaultCostSpecified`. |
| **L3003** the delete confirmation lied about the outcome | The delete response now carries `archived`, so the screen can state which of the two happened. |
| **L3024** two server refusals printed English | Duplicate name → « Un acte nommé « … » existe déjà dans votre catalogue. »; an invalid value → French. ⚠️ The *price* upper bound was not reached — a duration check refuses first — so that half is untested. |
| **L3275** « en attente de forfait » counted holds a top-up cannot release | `heldByAllowance` and `heldBySender` are both served: the split shipped. ⚠️ Both are 0 on this data, so the split exists but its **arithmetic** is unexercised. |
| **L4270** a « Créneau souhaité » over 200 chars raised a raw EF exception | **400** « Le créneau souhaité ne peut pas dépasser 200 caractères. »; a note over 1 000 → its own French refusal. No EF text. |

### ⚠️ HOLE 1 — L849 was open on two of the three catalogues, and the client was never at fault

`/dental-acts` refused a stale write with 409. `/cnam-nomenclature` and `/medications` **accepted it with 200**.
Both commands already had `Version`, both handlers already called `SetExpectedVersion`, and both repositories were
identical — so the divergence was above them:

- `DentalActsController` binds **the command itself** from the body (`[FromBody] UpdateDentalActCommand`), so
  `Version` arrives.
- `CnamNomenclatureController` and `MedicationsController` bind a **request model** and hand-copy fields into the
  command — and `UpdateCnamEntryRequest`, `UpdateCnamLetterValueRequest` and `UpdateMedicationRequest` had **no
  `Version` property at all**. The browser sent it; the seam dropped it; the command received `0`, which this
  codebase documents as « not supplied » and *skips the check*.

⚠️ **The silent-skip default is what made it invisible.** `Version == 0` meaning "no check" is deliberate and
load-bearing (jobs and the Google→App sync rely on it), but it means a dropped field degrades to no protection
with no error anywhere. Both web clients were already sending `version` — the fix was applied at both ends of the
chain and missed the middle.

**Fixed**: `Version` added to the three request models (with the reason on the property) and copied in the three
mappings. Re-measured: version advances, replaying the old one now returns **409** with the French conflict
sentence, on all three. This one mattered — the CNAM nomenclature prices every reimbursement a patient is quoted.

### ⚠️ HOLE 2 — L1928 is CONFIRMED STILL OPEN: a real edit is journalled with an empty Détail

Both halves of the report reproduce exactly:
- A **phone-only** edit: the phone really changed (`+216 71 000 011` → `…022`), an `Update` row was written, and
  `changedFields` is **null** → « Modification · — ».
- A **mixed** edit (LastName + phone): `changedFields` = `"LastName"`. The phone change is **silently dropped from
  the same save**, so the journal is not merely incomplete, it is misleading about which fields moved.

**Root cause, measured** (temporary instrumentation in the interceptor, since removed):

```
AUDITDIAG nav=PhoneNumber targetNull=False state=Added owned=True
  props=… Value:mod=False:orig=+216 71 000 022:cur=+216 71 000 022
```

`ChangedProperties` walks `entry.References` for owned entries and filters on `IsModified` — but assigning a new
`PhoneNumber` value object **replaces the owned entry wholesale, so its state is `Added`, not `Modified`**. On an
`Added` entry every `IsModified` is `false` and `OriginalValue` already holds the *new* value. The walk therefore
yields nothing. The fix that was written is correct in shape and inert in practice.

⚠️ **And the obvious repair is wrong.** Naming the navigation whenever the owned entry is `Added` was measured
against an **unchanged** save (the same phone re-sent): the entry is *still* `Added` with `orig == cur`. So state
alone cannot distinguish « changed » from « re-sent identical », and that repair would print
`PhoneNumber; Email; Address; InsuranceInfo; CnamInfo` on **every** patient edit while falsely asserting the phone
moved — worse than the silence it replaces.

**The real fix is in the domain, not the interceptor**: the setters must not replace a value object when the
incoming value equals the current one. Then `Added` becomes a truthful signal, the interceptor's existing walk
starts working unchanged, and EF stops writing columns that did not change. It touches `Patient`'s five owned VOs
and changes when an entity is marked dirty, so it is **left for a decision rather than slipped into a
verification pass**.

### Not covered by this pass

Roughly 17 majors are **browser-only** and were not exercised here: the `/securite` enrolment dead end (L235,
L257), the agenda losing its day on create (L444), clearing « Du » destroying the caisse screen (L648), the phone
« Solde de la période » (L698), « Confirmer les données » as an unconfirmed bulk write (L884), the dashboard
« tout afficher » layout (L1217), the patient-files folder loss (L2514), a colleague's edit discarding an open
form (L2701), and the « a failed read renders as empty » family (L691, L2741, L3233, L3815, L4057, L4070, L2324).
That last family is the one `check:responsive`'s own `failed-read-as-empty` check covers mechanically, and it
passes — but a forced-500 walk is what would prove it.

⚠️ **L3558 (matricule fiscal cannot be cleared) and L3576 (settings ignore `version`) remain UNVERIFIED, not
closed.** `PUT /api/clinics` is **multipart** (the command carries a `Stream? LogoFile`), and both a JSON probe
and a multipart probe with PascalCase field names were rejected with « Le champ « name » n'est pas valide ». The
field casing the endpoint actually wants has to be taken from `clinics.ts` before those two mean anything. **Three
results in the first run of this batch were false positives from exactly this class of mistake** — a wrong field
name, or a probe that sent identical values so `xmin` never advanced and a 200 on replay was correct. Every
verdict above survived being re-run with the probe corrected.

## The browser-only majors — 11 closed, 1 partial, 5 still unverified

The 17 majors the wire pass could not reach, driven in the browser at 1440 and 390 px. Read failures were
induced by patching `window.fetch` to return a 500 and then navigating **client-side**, so the patch survives
(a full page load would wipe it).

| Finding | Verdict |
|---|---|
| **L648** clearing « Du » destroyed the whole caisse screen | **Closed.** The page renders; the header states « Période incomplète — choisissez une date de début ». |
| **L691** after a failed read the four figures read « 0,000 DT » | **Closed**, and it says so in the product's own words: « Les totaux de la caisse n'ont pas pu être chargés. Aucun montant n'est affiché : un « 0,000 DT » ici se lirait comme une journée sans mouvement. » + « Réessayer ». |
| **L698** on a phone « Solde de la période » was the first row *of the page* | **Closed.** `closingBalance` is 17 805 on page 1 **and** page 2, and the footer still reads « Solde de la période : 17 805,000 DT » after paging to rows 26–50. |
| **L2324** a failed patient read was announced as « Patient introuvable » with the raw error | **Closed, both halves.** It says « **Dossier non chargé** », and an injected `An error occurred while saving the entity changes. Npgsql… 42P01` is suppressed to « Une erreur est survenue lors du traitement de votre demande. » ⚠️ A *French* server sentence is still shown verbatim — correctly: `looksTechnical` is meant to pass those through, and an earlier probe of mine mistook that for a leak. |
| **L2741** under the error banner the list claimed « Aucun patient enregistré » | **Closed.** « La liste des patients n'a pas pu être chargée. **Ni le nombre de dossiers ni leur absence ne peuvent être affirmés tant qu'elle n'est pas lue.** » |
| **L3233** rappels stated no message had ever been sent | **Closed** — no such claim, no technical leak, retry offered. |
| **L3815** treatment-plans presented a failed read as « aucun devis » with a 0 total | **Closed** — no « Aucun devis », no zero total, retry offered. |
| **L4057 / L4070** users rendered as a cabinet with no staff, and printed the raw `error` | **Closed** — no false empty, no technical leak, retry offered. |
| **L1217** a layout saved as « Tout afficher » was overwritten by the defaults | **Closed.** PUT `{"hiddenKpis":[]}` → 200; after a full reload the stored value is still `[]` **with `isCustomised: true`**, and the chip reads « Personnaliser » rather than « Personnalisé ». That flag is the fix — it is what tells « hiding nothing » from « no row ». |
| **L884** « Confirmer les données » was a one-click irreversible bulk write | **Closed**, and the dialog states both things the report asked for: « … seront confirmées d'un coup — **pas seulement celles affichées ici**. Cette action est **irréversible** … ». **Zero writes fired before confirming**; cancelled. |
| **L2514** inside a folder the screen lost the folder | 🟡 **Partial.** The breadcrumb reads « Fichiers › Radiographie », which is direct evidence the children-of-current read is fixed — and the report attributes all three symptoms to that one line. The « Dossier » field and the move-destination list were **not** exercised: every folder in this clinic holds 0 files, and « Téléverser » opens a native file chooser rather than a dialog. |

### Still unverified — 5, and why

- **L444** creating a RDV makes the agenda leave the displayed day. **Not verified.** The agenda seeds correctly
  from `?date=` (« jeudi 10 septembre 2026 · 4 RDV »), but no create could be completed through the UI: the
  overlap guard intercepted 11:00 (« Ce praticien a déjà un rendez-vous à 11:15 ») and the working-hours guard
  intercepted 19:30 (« Le jeudi, le cabinet est ouvert de 09:00 à 17:00 … »). Both refusals are correct
  behaviour — and incidentally re-confirm the hours enforcement — but they mean the post-create navigation was
  never reached. Nothing was created; verified against the API afterwards.
- **L2701** a colleague editing another patient discards an open form. **Not verified** — needs a second actor's
  realtime broadcast while a dialog is open.
- **L235 / L257** the `/securite` voluntary-enrolment dead end and `/login?enrol=1`. **Not verified.**
- **L3588** a doctor is offered « Modifier » on all four settings cards. **Code-verified only**: every card is
  gated on `isClinicAdmin` in `clinic-settings.tsx`. Not exercised in a browser, because the MCP browser holds one
  storage state and the signed-in account is the admin.

⚠️ **`PUT /api/clinics` is multipart and its field names are still unconfirmed**, so L3558 (matricule fiscal
cannot be cleared) and L3576 (settings ignore `version`) remain unverified from the earlier batch too.

## Deferred to /test-small-feature

Every one of these is a **new** scenario the change enables, not an adaptation:

- `GET /api/invoices` 403 for a secretary; 200 with `?patientId=`.
- Reopening a document issues the GET; a save with no edits leaves `ContentJson` byte-identical.
- Each of the 12 band-B forms: a stale save yields 409, a French message, and the other writer's value intact.
- Band A: clearing settings' matricule/ville, procedure-types' coût and description; an omitted key unchanged.
- Band C: a forced 500/403 on each of the 7 screens gives a French failure + working retry, no zero, no empty state.
- Audit: exactly one entry on create; a phone-only edit records `PhoneNumber`; a sign-in attributed to the person;
  `GET /api/audit` bounded at 200; `?userId=` isolates one actor.
- TOTP: the same code twice — accepted once, second refusal indistinguishable from a wrong code.
- Refresh: two sessions behind one address each renew; a genuine refusal reaches « session expirée ».
- `ExpenseDay`: no date gives 400 `expense_date_required`; a bare day resolves to the Tunisian day from any timezone.
- `ApplySearch` and its 5 siblings: « Nom Prénom » and « Prénom Nom » both match, `unaccent` intact.
- `LabOrderDates`: a `date prévue` before the `date d'envoi` is refused on create and on update.
- Deleting a received lab order removes its caisse dépense in the same transaction.
- `MessagingSender.From` with `sendable: false` answers `NotConnected`, never `Ready`.
- `heldByAllowance` excludes blocked reasons 7 and 8; `heldBySender` counts exactly those.
- `DashboardTrendReader` counts both money tracks and agrees with `DashboardMoneyReader` for the same month.
- Catalogue reactivation (`POST /{id}/activate`) on all three catalogues.
- `RemoveToothConditionCommand`: a rule refusal is 400, a missing row is 404.
- `AuditSaveChangesInterceptor` summarises an `OwnsOne` change (phone-only, and mixed with a root field).
- `FdiTooth.Refuse`: null passes; 99 and 0 are refused; 11 / 48 / 51 / 85 pass — and a lab order refuses 99 on
  create and on update, while `DentalRecordTooth` still throws for the same number.
- `LabOrderOverdue`: the `Expression` and its compiled form agree over a table of (status, expectedDate) cases,
  and a bon expected **today** is not overdue while one expected yesterday is. This is the guard that keeps the
  two forms of one rule from drifting — the file's whole reason to exist.
- `GetByClinicIdAsync`: a `supplierId` narrows to one fiche; a search term matches the linked fiche's nom;
  `sortBy: expected` orders dateless bons last and ties break on `Id`.
- `/lab-orders` seeds `search` from the query string, and the term it seeds is the one the first read carries
  (BV-2) — the round-trip `useUrlFilters` writes must be one the screen can read back.
- **The `Version` seam, derived rather than listed**: every `Update*Request` model in `API/Models` that maps onto
  a command carrying `Version` must expose and copy it. A guard test over the two types (reflect the commands with
  a `Version` property, then assert the request model feeding each controller has one) is what stops the third
  catalogue from being missed next time — the defect was invisible precisely because `0` is a legal value.
- `LegacyBackupRelocation.Relocate()`: a `Backups/` folder under the base directory is moved and the path
  reported; a second call is a no-op; an existing `legacy-install-dir` yields `-2`; the install-relative
  degenerate case (no common-data folder) refuses to move onto itself; and a failure returns a message rather
  than throwing. **The regression that matters most is the one a unit test cannot reach** — that no unreadable
  directory is left anywhere under `AppContext.BaseDirectory`, because QuestPDF's `FontManager` walks all of it.
- `AuditLabels.Entity` answers French for **every** `EntityType` the interceptor can write — a derived test over
  the entity list, not a hand-kept copy, so the next entity cannot reintroduce the fall-through (FP-1).
- `/appointments` seeds `date` and `view` from the query string: `?date=X&view=week` opens on X in Semaine, a bare
  `?date=X` still opens in Jour, and the deep-link effect runs **once** so its own `useUrlFilters` output is not
  re-consumed as a fresh link (FP-2).
- `WorkingHoursSerializer.ValidateBreak`: both ends or neither, ordered, inside `[From, To]` — and
  `WorkingHoursResolver.IsWithin` refuses an appointment that **overlaps** the pause (straddling either edge), not
  only one wholly inside it.
- `summarizeWorkingHours` groups on the break too: two days sharing 09:00–17:00 where only one closes at midday
  are two lines, not one.
