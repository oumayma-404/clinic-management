# Pre-go-live QA report — clinic-management

**Date:** 2026-08-26 · **Build under test:** production (`next build` + `next start`), not `next dev`
**Method:** 24 screens, one Playwright agent per screen, three roles (admin · doctor · secretary), five viewports (320/390/820/1180/1440)
**Coverage:** 24 of 24 screens vetted, plus a deeper second pass on `/patients/[id]` (§10).

Per-screen detail lives in `reports/<screen>.md` under the QA working directory; this file is the consolidated view.

---

## Verdict

**Not ready to go live — but closer than the raw numbers suggest.**

Four things must be fixed first (§1). Beyond those, the domain logic and the money are in better shape than expected: money reconciles to the millime once both payment tracks are counted, the CNAM multipliers that price every reimbursement match the database *and* the JORT convention exactly, the tri-state DTO trap that could have dropped a séance's acts is clean on every path tested, and paged reads do not duplicate or skip rows.

The defects cluster into four narrow, systemic bands (§3). Each band is one fix applied consistently, not dozens of separate patches.

| Severity | Count |
|---|---|
| **Critical** | **6** |
| **Major** | **51** |
| **Minor** | **~88** |
| **Question** (unproven, needs a decision) | **11** |
| **App-wide** (found before the fan-out) | **3** |
| **Total** | **~159** |

Minor counts are ±5: agents grouped cosmetics differently, some filing "minor ×6" as one line.

⚠️ **Roughly 44 first-wave findings were retracted** during a vetting pass, including 5 of the original 8 "criticals". Everything below survived being re-tested from a fresh page load. Causes of the false ones are in §7 — they matter for how you read any future automated QA run.

---

## 1. Blockers — fix before go-live

### 1.1 [critical] A secretary can read the entire cabinet's invoice ledger
**Screen:** factures · **Where:** `InvoicesController.cs:77`

`GET /api/invoices?pageSize=200` with a secretary's own token returns **200 — 73 invoices, 11 patients, 30 215,200 DT billed.** Meanwhile `/factures` shows her « Accès restreint », the rail rows are hidden, and `revenue`, `export`, `/billing/caisse`, `/billing/cheques` all correctly 403.

The `AdminOrDoctor` gate was applied to `revenue` (line 113) and `export` (line 49) — and not to the unfiltered list, which inherits `AnyClinicRole`. `?patientId=…`, which is what reception actually needs, already works, so closing this costs no capability.

`ARCHITECTURE.md` says of the hidden nav rows: *"This is presentation, not security. The server is authoritative."* On this endpoint it is not.

> Verified closed on the sibling screen: `GET /api/billing/cheques` → 403 for a secretary, gate on the list itself (`BillingController.GetChequesDue:226`). So this is one endpoint, not a pattern of open endpoints.

### 1.2 [critical] Reopening a saved document loses its content on the next save
**Screen:** documents · **Where:** `document-editor-content.tsx:359`, reached from `web/app/patients/[id]/page.tsx:411`

Reopening a saved document (via `?id=`, or the patient file's Documents tab) renders an **empty** form in « Mettre à jour » mode, and **no `GET /api/medical-documents/{id}` is ever issued.** `useState(urlDocumentId)` seeds the state, so the loading effect's guard `urlDocumentId !== documentId` is already false on first render.

Then, from that empty form, « Mettre à jour » PUTs `{"medications":[],"renewals":""}` and reports success. **The stored prescription is really gone** — verified in the database before and after.

A prescription silently emptied by opening and saving it is the most dangerous single defect in this report.

### 1.3 [critical] `/caisse?from=&to=` races itself and shows the wrong period's money
**Screen:** caisse

`loadData()` fires for the default month on mount, then again for the URL period, and nothing discards the first. **2 of 6 natural loads displayed 20 300,000 DT / 25 movements for 26→21 Aug under the header « Vendredi 21 Août 2026 ».** Deterministic when the month read is delayed 2.5 s. This is the dashboard's own drill-through path, so it is on a route users actually take.

### 1.4 [critical] Concurrent edit silently reverts money on a lab order
**Screen:** lab-orders

The form carries no `version`, so a second save overwrites the first: dent 47→vide, **coût 77,500 → 14,000 DT**, notes emptied — under a « Bon mis à jour » toast. Reproduced twice.

This is the most damaging instance of the concurrency band in §3.2, which is why it is a blocker while the other instances are majors.

### Also worth treating as a blocker

**[critical] The dashboard's « Encaissé » KPI disagrees with its own chart** (dashboard). KPI 19 910 DT, the 6-month chart's August column 18 960 — a 950 DT gap. `DashboardTrendReader:33` sums invoice payments only; the KPI adds treatment-plan installments. **The KPI is right.** Investigated from four sides: la caisse, `/factures` and `/treatment-plans` all count installments and all agree; the chart is the sole outlier, and its stated reason ("no per-payment date") is now stale because `InstallmentPayments.PaidOn` exists and la caisse already buckets by it.

**[major] Every QuestPDF output is dead process-wide** (documents). `generate-pdf-download` 400, background PDF never lands, email queue 400 with no `DocumentEmails` row. Cause: the `FontManager` static constructor throws `UnauthorizedAccessException` scanning `bin/…/Backups/clinic-backup-*` — **a folder the product's own backup default writes under the app base dir** — and `TypeInitializationException` is CLR-cached, so the first failure kills PDFs for the life of the process. Environment-triggered but product-caused. Official-form (P 061/BS1) preview uses a different renderer and is fine.

---

## 2. Second tier — fix soon

### Money and clinical integrity
- **[major] A dépense's date is midnight in the *workstation's* timezone** (caisse). Same form, two contexts: Africa/Tunis sends `2026-08-19T23:00Z` → files on 20/08 ✓; Asia/Dubai sends `20:00Z` → files on **19/08** ✗. The agent's own words: *"the read side was fixed for exactly this (AC-6); the write side wasn't."* The read side is provably correct — four payments at 23:59 / last tick / 00:00 / 00:01 local each land in exactly one caisse day.
- **[major] `POST /api/expenses` with no date returns 200 and stores `-infinity`** (caisse) — a row belonging to no caisse period, ever.
- **[major] Deleting a received lab order orphans its caisse expense** (lab-orders). 3 orphans totalling 661,750 DT found in `Expenses`, while the confirmation dialog mentions only the bon.
- **[major] A « date prévue » before the « date d'envoi » is accepted** (lab-orders) — no rule in the form, the handler or the domain. Also poisons `CountOverdueAsync`.
- **[major] Any edit from the lab-orders screen detaches the linked séance** — `appointmentId` is absent from the payload and the command is replace-semantics, so `AppointmentId` → null and « Voir le RDV » disappears.
- **[major] The « Encaissés » tab's header Total is the *outstanding* total** (cheques) — « Chèques encaissés (2) · Total : 2 975,000 DT » over two rows summing 280,000 DT.

### The audit trail cannot answer what it exists to answer
All five on journal:
- **Creating a record writes a phantom « Modification » nobody made** — `CreatePatientCommand.cs:233-234` saves twice unconditionally. 19 of 22 « Patient/Modification » rows are these ghosts.
- **A real edit is journalled with an empty Détail.** Changing only `phoneNumber` gives « Modification · — », because `Summarize()` reads root properties while PhoneNumber/Email/Address/Insurance/Cnam are `OwnsOne` entries. A mixed edit printed `LastName; Notes` and **silently dropped the phone change from the same save.**
- **Every human sign-in is journalled as « Tâche automatique » (`job|unknown`)** — 329 of 1 868 rows, ~18%. The ledger asserts a process did what a person did.
- **`GET /api/audit` with no page params returns the entire ledger** (1 950 rows) while `?pageSize=100000` *is* correctly clamped to 200.
- **No actor filter exists at all** — « qu'a fait cette personne ? » cannot be asked across 79 pages; `?userId=` is silently ignored server-side.

### Access control and identity
- **[major] Promoting an existing account to « Médecin » creates no linked `Doctors` row** (users, `ChangeUserRoleCommand.cs`). Verified 0 in the DB, twice. The *create* path on the same screen requires and writes one. A promoted dentist therefore has no cachet, no n° CNOMDT on ordonnances, and money unattributed.
- **[major] No doctor or secretary can voluntarily enrol a second factor by any route** (account). `/securite:180` links to a bare `/login`, which bounces a signed-in user back to `/appointments`; the exemption for exactly this exists at `app/login/page.tsx:34-44` — *its comment names the symptom verbatim* — and is applied to the same screen's other link on line 209, not this one. The fallback `/login?enrol=1` reached by URL has `material` null, so it says « saisissant la clé ci-dessous » with no key below it.
- **[major] A doctor is offered « Modifier »/« Enregistrer » on all four settings cards** while both PUTs 403 (settings). Nothing is written — the boundary holds — but they lose their typing. Same shape on procedure-types (« Supprimer » shown to a praticien, `DELETE` is AdminOnly).

### Data that cannot be un-set
- **[major] A matricule fiscal, once saved, can never be cleared** (settings). A blank field binds to `null`, `null` means "leave unchanged", and the clear reports « Paramètres de facturation enregistrés. » before the old value returns. Ville and Gouvernorat share the mechanism.
- **[major] Clearing « Coût par défaut » is a silent no-op** (procedure-types) — so **an act can never be un-priced anywhere in the product.** Clearing « Description » likewise (the client's `|| undefined` drops the key from the PUT).
- **[major] A deactivated catalogue entry can never be reactivated** (catalogues). All three entities have an `Activate()` method nothing calls; the row stays listed with only « Modifier », and an edit-save leaves `IsActive = false`.

### Destructive actions that misrepresent themselves
- **[major] « Confirmer les données » is a one-click, bulk, irreversible write with no confirmation** (catalogues). One click moved 27 nomenclature entries **and the 5 letter values** from « à vérifier » to confirmed. No dialog, no inverse endpoint. Deactivating a *single* row does ask — the cheap action is guarded and the expensive one is not.
- **[major] The delete confirmation says « désactiver … archivé » with a « Désactiver » button, and the server hard-`DELETE`s an unused act** (procedure-types). The client branches on `isActive` (always true in this list); the server branches on usage. The archive half is correct — an act on a future appointment is archived and the appointment survives.

### Workflow and data-loss paths
- **[major] Creating an appointment makes the agenda leave your day** and jump back to the current week (appointments). The RDV is created but invisible to whoever just booked it. Reproduced 3× from fresh loads.
- **[major] A colleague editing *any other patient* silently discards your unsaved edit** (patients). `useClinicRealtime` bumps `refreshKey`, which re-keys `PatientsTable`, which owns the dialog — so it unmounts rather than closes and `useDirtyGuard` never fires. `page.tsx:38` + `:154`.
- **[major] Inside a folder the patient-files screen loses the folder** (patient-files, `patient-files-manager.tsx:141`). It fetches the *children* of the current folder, always empty. Three symptoms from one line: no folder name in the breadcrumb, a blank « Dossier » field, and a destination list offering only « Aucun dossier » — so a file in a folder can only be moved back to the root, never to a sibling.
- **[major] Search misses a patient typed in the order the UI displays them** (fichiers, patients). `ApplySearch` (`PatientRepository.cs:165`) concatenates `FirstName + " " + LastName` while cards render « Nom Prénom »: « Hamdi Karim » → 0 results, « Karim Hamdi » → 1. Confirmed server-side. **The product's own CSV export header is `Nom;Prénom`, so the app's own export order is unsearchable in the app.** The header lookup is *not* affected in practice — it renders `firstName lastName`.
- **[major] Coarse-pointer tablets lose the post-visit prompt entirely, 4 of 5 loads** (chrome, `post-visit-review-popup.tsx:247`). The dialog is deliberately suppressed and the toast never fires; the `data-sheet-open` early return has no retry, and `AppShell`'s sheet holds that attribute for ~25 ms at load, so a warm fetch loses the race.
- **[major] The reminders screen claims it is ready to send when it cannot** (rappels). The forfait pill says « Prêt à envoyer » and the connection card says reminders "partent normalement", while `whatsAppEffectiveStatus: "not_configured"` and every queued WhatsApp reminder is `Blocked`. `MessagingSender.From` never asks whether an access token exists.
- **[major] « 2 rappels sont en attente de forfait »** also counts `MessagingTemplateNotReady` and `MessagingNumberStopped` (rappels) — proven with a `BlockedReason=8` row the log itself badges « numéro ».
- **[major] A « Créneau souhaité » over 200 chars is refused with a raw English EF exception in a French toast** (waiting-list): *"An error occurred while saving the entity changes."* No `maxLength` on the input, no length check in the command — only `varchar(200)` in Postgres. `Note` (1000) shares the shape.
- **[major] `ListUsersQuery.cs:105` prints the server's raw exception message in English** (users) — the only one of five sibling handlers that does; the other four return a generic French sentence and log. Same shape on procedure-types (« A procedure type with the name 'Détartrage' already exists », plus a raw EF sentence for an out-of-range price, with no upper bound on price at all — 999 999 999 accepted).
- **[major] The notification panel prints the raw server error string with no retry** (chrome).
- **[major] Saving a dashboard layout via « Tout afficher » is discarded on the next load** (dashboard). The write lands (`HiddenKpisCsv` = `''`), but `use-dashboard-preferences.ts` only trusts a *non-empty* stored set, so the defaults are re-applied over it.

---

## 3. The systemic patterns — where the real leverage is

Four bands account for most of the 51 majors. In every one, the correct behaviour already exists somewhere in the codebase and was not carried to its siblings. `CLAUDE.md` names this as the repo's dominant defect shape; this pass rediscovered it independently, screen by screen.

### 3.1 A blank field means "leave unchanged", so data cannot be un-set
`null` binds from an empty input and the handler treats `null` as "not supplied".
**Confirmed:** settings (matricule fiscal, ville, gouvernorat) · procedure-types (coût, description) · lab-orders.
**Confirmed absent:** patient-detail (clearing really clears) · stock (writes `NULL`) · fournisseurs (clearing notes writes `NULL`) · documents.

### 3.2 `Version` is not round-tripped, so optimistic concurrency is a no-op
The protection is built end-to-end — `Version` on the entity, the concurrency check in the handler — and the form does not send it. `Version == 0` means "not supplied" and skips the check.
**Without it (≈10):** settings · the three catalogues · lab-orders · users · waiting-list · mon-profil · patient-files · procedure-types · stock (`stock-item-form-modal.tsx` omits it although `UpdateStockItemCommand.Version` + `SetExpectedVersion` are fully wired) · documents.
**Correctly 409/400 (7):** factures · treatment-plans · patients · patient-detail · appointments · fournisseurs · a-cloturer.

### 3.3 A failed read renders as an empty state or as zeros
The user cannot tell a broken server from an empty clinic.
**Affected:** users (« Utilisateurs 0 », « Aucun code défini ») · rappels (« Aucun message pour le moment ») · treatment-plans (« Aucun devis… » + « 0 devis ») · patients · patient-detail (« Patient introuvable » — a transient 500 is indistinguishable from a deleted patient) · **caisse (four money figures assert « 0,000 DT » — the worst instance)** · cheques (four bucket tiles claim zero exposure over 3 085 DT of real cheques).
**Correct (French error + retry):** waiting-list · fichiers · fournisseurs · a-cloturer · stock · documents.

### 3.4 Raw server internals reach the user
English text and raw EF Core exception messages surface in a French UI: users (`ListUsersQuery.cs:105`), waiting-list, procedure-types, chrome's notification panel, caisse (a 400). The canonical `{ error }` contract and the French-frontend rule exist; these are the call sites that do not honour them.

### A fifth, smaller one
**Focus is not returned to the trigger after Escape** on nine screens — likely one fix in the shared `ui/dialog.tsx` primitive.

---

## 4. Per-screen results

| Screen | Verdict | Crit | Maj | Min | Q |
|---|---|---|---|---|---|
| documents | **not ready** | 2 | 2 | 2 | 0 |
| caisse | **not ready** | 1 | 7 | 8 | 0 |
| factures | **not ready** | 1 | 0 | 2 | 0 |
| lab-orders | ready with fixes | 1 | 3 | 6 | 0 |
| dashboard | ready with minors | 1 | 1 | 0 | 1 |
| procedure-types | **not ready** | 0 | 4 | 8 | 0 |
| journal | ready with majors | 0 | 5 | 2 | 0 |
| patients | ready with minors | 0 | 4 | 1 | 0 |
| catalogues (×3) | ready with minors | 0 | 3 | 1 | 2 |
| rappels | ready with minors | 0 | 3 | 6 | 2 |
| settings | ready with minors | 0 | 3 | 6 | 0 |
| users | ready with minors | 0 | 3 | 6 | 0 |
| account (×3) | ready with minors | 0 | 2 | 1 | 1 |
| cheques | ready with minors | 0 | 2 | 2 | 0 |
| chrome | ready with minors | 0 | 2 | 5 | 1 |
| stock | ready with minors | 0 | 1 | 6 | 0 |
| appointments | ready with minors | 0 | 1 | 4 | 0 |
| patient-detail | ready with minors | 0 | 1 | 4 | 1 |
| patient-files | ready with minors | 0 | 1 | 4 | 0 |
| waiting-list | ready with minors | 0 | 1 | 5 | 0 |
| fichiers | ready with minors | 0 | 1 | 2 | 0 |
| treatment-plans | ready with minors | 0 | 1 | 2 | 2 |
| a-cloturer | ready with minors | 0 | 0 | 3 | 0 |
| fournisseurs | ready with minors | 0 | 0 | 3 | 1 |

---

## 5. App-wide (found during setup, before the screen pass)

1. **[major] `GET /api/connectivity` 404s on every page load, for every role**, logging a console error each time (8–22 per short session — it polls). Connectivity awareness is a `SelfHostedLan` capability; on `HostedMultiTenant` the endpoint is absent, but the client probes anyway, so the indicator's state derives from a failure rather than from the deployment capability.
2. **[major] `GET /api/googlecalendar/status` 403s for a secretary** on every `/appointments` load, and the client logs an unhandled `ApiError` rather than handling it. The server is correctly authoritative; the presentation half is un-gated.
3. **[minor, security] A TOTP code is accepted twice inside its 30-second window.** Two `POST /api/auth/login` calls with the *same* `totpCode` both returned 200 with a valid token. RFC 6238 §5.2: *"the verifier MUST NOT accept the second attempt of the OTP."* This product makes a second factor mandatory for administrators, so the factor's own guarantee is what is weakened. Filed minor because the window is short and an attacker needs the code; the correctness gap is unambiguous.

**Also noted by four separate agents:** **no page in `web/app` exports `metadata` — 0 of 39** — so every screen carries the root `<title>` « Gestion Clinique — Tableau de bord ». Every browser tab and every bookmark says the wrong thing. One cheap global fix.

---

## 6. What was proven clean

Worth recording, because these are the things that would have been most expensive to get wrong.

- **The tri-state DTO trap does not fire on appointments.** Acts survived all four write paths — edit-dialog status-only, agenda quick-status `{status}`-alone, cancel, and drag — plus a time-only edit. `/a-cloturer` independently confirmed it through its own path. Also clean on procedure-types (materials survive a parent rename) and catalogues (active-ingredient links survive).
- **Money reconciles exactly, once both tracks are counted.** For 01–31/08: invoice-only 19 290 + committed-plan installments 950 = 20 240 = the API, precisely; refunds 100, cashOut 1 911, net 18 229, and Σ byMethod = 20 240. Cheques: `sum(4 buckets) == sum(items) == groups.total ==` the DB over both ledgers (3 085,000 / 18).
- **The CNAM letter values match the database *and* `CnamConventionTariffs` (JORT 2021-014):** CD 30.000 · CDS 45.000 · D 3.000 · RD 2.000 · VD 10.000. These multipliers price every reimbursement in the product.
- **The Tunisian day boundary holds on every read.** Four payments at 23:59 / last tick / 00:00 / 00:01 local each appear in exactly one caisse day; a two-day range equals the sum of the two days. `/rappels` renders a 2026-08-27 23:30Z appointment as `28/08/2026 00:30`. The dashboard sends `2026-08-24T23:00:00Z → 2026-08-25T22:59:59.9999999Z` for « Aujourd'hui » — last tick, not next midnight.
- **Pagination is an exact partition everywhere it was tested.** patients (34 ids over a 12-patient block sharing *both* names, forward and reverse, 0 dupes 0 missing) · journal (71 pages, 1 755 distinct for 1 755) · factures (25+25+23 = 73) · catalogues (27, 26, 102) · cheques · fichiers · waiting-list · a-cloturer.
- **The recovery-code regression did not recur.** The DB holds 8 unused codes and `/securite` says 8, in both places — the defect that once read « 0 code inutilisé » over eight live codes.
- **Role gating holds on both layers** for users, journal, the three catalogues, cheques and abonnement: absent from the rail, refusal card on the URL, and the API 403 with no side effect. `/factures` (§1.1) is the exception.
- **The upload door is solid** (patient-files): the picker's `accept` is byte-identical to `/api/meta/upload-policy`; bypassing the client with a direct request got 400 + a French `{error}` + no row for `.exe`, `.xyz`, `.html`, `.svg`, signature mismatch, no extension, empty, `.png.exe`, a type lie, and 25 Mo+; a `../../` name is accepted and *sanitised*, which is correct.
- **Stock arithmetic is exact.** `CurrentStock − Σ StockBatches.RemainingQuantity = 0` for all 8 baseline articles and after every write; FEFO draws the dated lot first; over-consumption is refused (400, never negative).
- **Working hours are respected by the agenda.** Saturday closed renders « samedi 29 août — cabinet fermé » with the column hatched, and the API refuses booking probes with the correct French reason.
- **The device contract largely holds.** `sweep()` returned zero complaints at 320/390/820/1180/1440 on a majority of screens; tables take card form on phones; dialogs become full-height sheets. Authoritative coarse-pointer chrome measurements: search pill 44 · bottom-bar tabs 64×56 (320) / 78×56 (390) · drawer rows 44 · rail rows 44 @820. Failures are narrow: the bell and avatar at 36×36 (`dashboard-header.tsx:411,449` doesn't use the `.touch-target` class `globals.css:750` defines and 20+ components use), the rail collapse toggle at 32×32, and the drawer's ✕ at 16 px tall.

---

## 7. Coverage gaps and caveats

**Not covered anywhere in this pass:** auth and session flows (`/login`, `/change-password`, lockout, session lock, the client version gate); onboarding (`/signup`, `/join`, `/setup`); the vendor console on :3100. All three were explicitly out of scope.

**The BFF token-refresh path is untested on every screen.** The harness stubs `/bff/auth/token` because `POST /api/auth/refresh` carries no email, so its rate-limit partition falls back to the client address — one bucket of 30 per 300 s shared by every agent, which a parallel pass empties in minutes. That limiter is itself worth a look: a whole practice behind one NAT address shares that bucket for silent token renewal, and when it trips the client shows no « session expirée » and offers no re-login.

**Also not covered:** most screens' doctor-role pass; realtime propagation between two browsers on several screens; the Exporter CSV bodies; Word export and print-to-OS; `doctor-document-identity-dialog`; a real backup run (impossible on `managedByHost`) and every restore path (deliberately forbidden mid-pass); a non-Tunisian viewer timezone on read surfaces.

**A deeper second pass on `/patients/[id]`** — odontogram write round-trip, the fiches pager boundary, archive/restore, triple fiche re-save — completed after the rest of this report; its results are in §10 and are included in the totals.

**On the ~44 retractions.** They matter for reading any future automated run. Three causes:
1. An agent stubbed a failure with its own `{"error":"boom"}` body, then reported "the UI shows a raw non-French string" — its own injected string. This produced the most false findings.
2. Agents compared money against `SELECT sum FROM "Payments"` alone, missing that this product has two money tracks and `InstallmentPayments` are genuinely money received. Three agents lost findings to this one number. One also used `Method = 2` believing it was Cheque; it is Card.
3. First-match locators: `expectText().first()` matching the hidden mobile CardList; an empty state rendering as one `<TableRow>` and being counted as data; a tab locator reading the odontogram's own tabs instead of the page's.

Two environment traps also invalidated the first wave entirely and are fixed in the harness: a storageState is good for one run only (the refresh token rotates on every exchange), and `@media (pointer: coarse)` does not match on `hasTouch` alone — Chromium needs the mobile emulation flag, without which every tap-target measurement is taken against desktop rules.

---

## 8. Suggested order of work

1. **§1.1** — close `GET /api/invoices` to a secretary. One attribute.
2. **§1.2** — the document reopen/save data loss. One `useState` seed.
3. **§1.3** — the caisse period race. Discard the stale response.
4. **§3.2** — round-trip `Version` in the ~10 forms that drop it. This alone retires the lab-orders critical and several majors.
5. **§3.1** and **§3.3** — the two silent-data bands, each a shared-helper fix plus call-site sweep.
6. **§2 journal** — the audit trail, as a group. It is the record you would rely on if anything else went wrong.
7. **§3.4**, the focus-return primitive, and the missing `metadata` exports — cheap, global, high visibility.

For bands 3.1–3.4, a derived check in the test suite is worth more than the individual patches: each band has screens that already do it right, so the check is "every screen that writes must do what these screens do."

---

## 9. Environment state

The QA run leaves the machine mid-pass:

- **Frontend:** a production build serving on **:3000**. The user's own `npm run dev` was stopped to make that build and **needs restarting** when the pass ends.
- **Database:** dirty. `salma.benyoussef@cabinet-ibnkhaldoun.tn` is temporarily promoted to `admin`; `qa.doctor@ibnkhaldoun.test` and `qa.secretary@ibnkhaldoun.test` were created (password `QaAudit2026!y`); agents left `QA-*`-prefixed rows. One agent also deleted all 111 `NotificationReads` rows.
  Restore with: `docker exec clinic-postgres pg_restore -U clinic_user -d clinic_management --clean --if-exists /tmp/qa-baseline.dump`
  That baseline was taken before the pass and reverts all of the above, including the role promotion.
- A stray `next start -p 3100` (vendor console) may still be running; harmless, out of scope.

---

## 10. Deep second pass on `/patients/[id]` — the medical record

The first pass rated this screen "ready with minors" but left six things unreached. Because it is the most
clinically important screen in the product, a second agent was sent at exactly those gaps: 14 scenarios as
admin + secretary, over nine iterations.

**Verdict: ready. Zero criticals, zero majors.** Every clinical write on this screen round-trips exactly.

### Findings

- **[minor]** `DELETE /api/patients/{id}/odontogram/conditions/{id}` on a *treatment*-sourced state answers a
  **rule refusal with HTTP 404**: `404 {"error":"Seul un diagnostic peut être retiré ici ; un acte réalisé se
  modifie via sa fiche."}`. `OdontogramController.RemoveCondition` passes `Status404NotFound` as the fallback
  for every failure code. No user impact today — the UI never issues it for such a row, and renders the body
  rather than the status.
- **[question]** Pressing « Adulte » explicitly drops a charted deciduous tooth (55) from the chart with no
  notice. The *default* view correctly widens to Mixte; only an explicit switch hides it. What would settle it:
  should the chart say "n états hors de cette vue"?

### Proven clean — the things that would have been most expensive to get wrong

- **The odontogram stores exactly what is charted.** 5 teeth, 5 different conditions, faces on one, charted
  through the popover and verified after a full reload against `ToothStates` in SQL (`18|1|MO`, `24|2`, `25|3`,
  `36|4`, `46|7`, `Source=1`). No contamination of adjacent teeth 24/25, no stray state.
- **The dentition switch writes nothing** — byte-identical snapshot across Adulte→Enfant→Mixte→Adulte.
  Enfant = 20 cells all in 51–85; Mixte = 52 = 32 + 20; charting tooth 55 in Enfant stored **55, not 15**.
- **An act with an « État résultant » writes all three places consistently**: `DentalRecordActs.ToothNumbers
  = [16,17]`, `DentalRecordTeeth` 16 + 17, and two `Treatment` states — and re-saving left 9 states → 9.
- **The fiches pager is an exact partition**: 7 fiches, page 1 = PD-G…PD-C, page 2 = PD-B/PD-A, back identical.
  `DentalRecordRepository` has no unique tiebreak, so 7 same-date fiches were forced deliberately; 10 reads
  produced 1 distinct order — not reproducible as a defect, but the guard is absent and worth adding.
- **Archive → « Restaurer »**: band and motif shown, census byte-identical while archived, restore clears it
  without a reload, census still identical, patient searchable again.
- **Fiche re-saves behave**: 3 unchanged saves on an unpaid fiche → 0 invoices and nothing changed; `Payé = 16`
  → exactly one note `2026-0070` at 16/16; two further re-saves → informational « déjà facturée », still one
  note, and the note text survived all six saves. Over-payment disables the save with a French reason.
- **Histories and allergies persist verbatim** — accents, apostrophe, em-dash, œ, 479 characters; only a
  trailing space trimmed. Flags add/remove correctly with the motif pre-filled on reopen.
- **The tri-state trap does not fire here**: a phone-only edit left records, tooth states, both histories,
  flags, allergies and invoices string-for-string identical. Twice.
- **Fiche-level optimistic concurrency works** (not covered by the first pass): a colleague's PUT after the
  modal opened → **409**, the dialog stays open, a French conflict message, and the colleague's note is kept.
- **The secretary is offered nothing the API refuses**, and everything she is offered works (charting a
  diagnosis; `Payé = 15` reached la caisse as note `2026-0072`).

Not covered: the BFF refresh path (harness-stubbed), a `Version == 0` unprotected path (none reachable), the
four sibling dialogs owned by other screens, a multi-act fiche with disjoint tooth sets, and a deciduous *act*
(only the diagnosis half was exercised). Three findings in an intermediate run were the agent's own baseline
and arithmetic artefacts and are retracted in its report.

**Bottom line:** the patient record is the strongest screen in this pass. Nothing on it loses, corrupts or
mis-attributes a clinical value, and both cross-cutting failure bands (§3.1 blank-means-unchanged, §3.2 missing
`Version`) are provably absent here.
