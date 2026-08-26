# Feature Specification: Go-Live Remediation

**Status:** APPROVED
**Type:** Small
**Created:** 2026-08-26
**Scope:** Full
**Feature:** Close every defect found by the 24-screen pre-go-live QA pass (`QA-GO-LIVE-REPORT.md`).

## Overview

A parallel Playwright pass over all 24 authenticated screens, in three roles and five viewports, produced 6
criticals, 51 majors and ~88 minors. Most of the majors are not independent: they are five systemic bands where
correct behaviour already exists on some screens and was never carried to the others. This feature fixes the
bands at their shared seam, closes the six criticals individually, and clears the per-screen one-offs.

The source of truth for every item is `QA-GO-LIVE-REPORT.md` — §1 criticals, §2 second tier, §3 bands, §4
per-screen table, §5 app-wide, §10 the patient record.

## What Changes

**The six criticals**
- `GET /api/invoices` gains `AdminOrDoctor`, closing a secretary's read of the whole cabinet's ledger.
- Reopening a saved document loads it (`document-editor-content.tsx:359` no longer seeds `documentId` from the
  URL), so « Mettre à jour » can no longer PUT an empty body over a stored prescription.
- `/caisse?from=&to=` discards the stale default-month response, so the figures and the header always describe
  the same period.
- The lab-order form round-trips `version` (band B), so a concurrent edit can no longer revert a cost.
- The dashboard's 6-month chart counts treatment-plan installments, agreeing with its own KPI, la caisse and
  `/factures`.
- PDF generation is restored: the backup default no longer writes under the app base dir, so QuestPDF's
  `FontManager` cannot throw scanning it.

**Band A — a blank field must clear, not mean "unchanged"**
- An explicit empty value clears; an omitted key still means unchanged. Fixes settings (matricule fiscal, ville,
  gouvernorat), procedure-types (coût, description), lab-orders.

**Band B — `Version` round-trip**
- The ten forms that drop it now send it: settings, the three catalogues, lab-orders, users, waiting-list,
  mon-profil, patient-files, procedure-types, stock, documents.

**Band C — a failed read must read as a failure**
- Affected screens render a French error and a retry instead of an empty state or a zero: users, rappels,
  treatment-plans, patients, patient-detail, caisse (four money figures), cheques (four buckets).

**Band D — no raw server internals in the UI**
- Refusals branch on a `code` and render French: users `ListUsersQuery.cs:105`, waiting-list, procedure-types,
  chrome's notification panel, caisse's 400. Length limits move into the input and the command so a Postgres
  constraint never surfaces as an EF exception.

**Band E — shared primitives**
- `ui/dialog.tsx` returns focus to the trigger after Escape (nine screens).
- Every route exports `metadata`, so titles stop reading « Tableau de bord » (0 of 39 export it today).
- `.touch-target` applied where it is missing: the header bell and avatar (36×36), the rail collapse toggle
  (32×32), the drawer's ✕ (16 px tall), the fiche row's pencil/bin at 820 px.

**The audit trail** — no phantom « Modification » on create; a real edit records what changed including
`OwnsOne` fields; a human sign-in is attributed to the person, not « Tâche automatique »; the default
`GET /api/audit` page is bounded; an actor filter exists.

**Auth correctness** (both decided in, having first been proposed as out)
- A TOTP code is refused on its second presentation: a spent `(user, counter)` pair cannot be replayed inside
  its 30-second window (RFC 6238 §5.2).
- `POST /api/auth/refresh` is partitioned per session rather than per client address, so a practice behind one
  NAT address no longer shares a 30-per-300s bucket for silent renewal — and a 429 or a dead session surfaces
  « session expirée » with a route back to sign-in instead of silent 401s.

**Decided product questions** — a billed fiche accepts a further payment (`ToppedUp` becomes reachable); the
odontogram states « n états hors de cette vue » when a view hides charted teeth; « Répartition des actes » gains
an « autres » bucket so its figures reconcile; `/securite` states the affirmative case too; the reminders
wrong-day backstop also matches a `dd/mm` body, closing the hole that let a 30/08 appointment's reminder pass
unflagged.

**Remaining per-screen one-offs** as listed in §2 and §4: the caisse dépense date in the workstation's
timezone, `POST /api/expenses` accepting no date, lab-orders orphaning a caisse dépense and detaching its
séance, `date prévue` before `date d'envoi`, the cheques « Encaissés » total, catalogue reactivation,
« Confirmer les données » asking first, the procedure-types delete dialog telling the truth, the appointments
agenda holding its day after a create, the patients realtime unmount discarding an open edit, the
patient-files folder read, `ApplySearch` (see below), reminders not claiming readiness they lack, and the ~88
minors.

## Acceptance Criteria

- **AC-1:** As secretary, `GET /api/invoices` returns 403; `GET /api/invoices?patientId=<id>` still returns 200.
- **AC-2:** Opening a saved document issues `GET /api/medical-documents/{id}` and renders its stored content;
  saving without edits leaves `ContentJson` byte-identical.
- **AC-3:** `/caisse?from=X&to=Y` never renders figures from a period other than the one in its header, including
  when the default-month read is artificially delayed 2.5 s.
- **AC-4:** For each of the ten band-B forms, a save from a stale form returns 409, shows a French conflict
  message, and leaves the other writer's value in place.
- **AC-5:** Clearing a populated optional field in settings, procedure-types and lab-orders persists as cleared
  after a reload; an update payload that omits the key leaves the stored value unchanged.
- **AC-6:** On a forced 500, 403 and aborted main read, each band-C screen shows a French message and a working
  retry; no money figure renders `0,000 DT` and no list renders its empty state.
- **AC-7:** No UI surface renders a server response body verbatim; a >200-char « Créneau souhaité » is refused by
  a French field-level message with no EF exception text anywhere.
- **AC-8:** The dashboard KPI and its 6-month chart report the same figure for the same month, and both equal
  invoice payments + committed-plan installments as verified in SQL.
- **AC-9:** A document PDF downloads, the background PDF lands a `PatientFiles` row, and an email send writes a
  `DocumentEmails` row — with a backup present under the configured backup path.
- **AC-10:** Searching a patient by « Nom Prénom » and by « Prénom Nom » both return that patient, on
  `/patients`, `/fichiers` and the header lookup, with `unaccent` still applied.
- **AC-11:** Creating a record writes exactly one audit entry; editing only `phoneNumber` records that field in
  Détail; a sign-in is attributed to the signing-in user; `GET /api/audit` with no params returns at most 200
  rows; filtering by actor returns only that actor's entries.
- **AC-12:** Deleting a received lab order also removes or reassigns its caisse dépense, and the confirmation
  names what else will be affected; an edit preserves `AppointmentId`; a `date prévue` before `date d'envoi` is
  refused in French.
- **AC-13:** `POST /api/expenses` with no date returns 400; no `Expenses` row has an `-infinity` date. A dépense
  saved from a workstation in any timezone files on the Tunisian day the user picked.
- **AC-14:** A deactivated catalogue entry can be reactivated from the UI; « Confirmer les données » asks for
  confirmation naming the row count; the procedure-types delete dialog states what will actually happen.
- **AC-15:** Creating an appointment leaves the agenda on the day the RDV was created for, and the new RDV is
  visible without navigation.
- **AC-16:** A colleague's edit to another patient does not unmount an open « Modifier » dialog or discard its
  unsaved input.
- **AC-17:** Inside a patient folder the breadcrumb names the folder, the file's « Dossier » field shows it, and
  the move destination list offers sibling folders.
- **AC-18:** At 320 px every screen touched here keeps its card form with no horizontal scroll, and on a coarse
  pointer the header bell, avatar, rail collapse toggle, drawer close and fiche row actions all measure ≥ 44 px.
- **AC-19:** After Escape, focus returns to the control that opened the dialog, on every screen using
  `ui/dialog.tsx`.
- **AC-20:** Every route sets its own `<title>`; none reads « Tableau de bord » unless it is the dashboard.
- **AC-21:** A billed fiche with an outstanding balance accepts a further payment and reaches `ToppedUp`; the
  note's paid total increases and la caisse reflects it.
- **AC-22:** The same TOTP code presented twice is accepted once and refused the second time, with the refusal
  indistinguishable from a wrong code.
- **AC-23:** Two users behind one client address can each renew their session without exhausting a shared
  bucket; when renewal is genuinely refused the UI states « session expirée » and offers sign-in, rather than
  looping on silent 401s.
- **AC-24:** A view that hides charted teeth states how many are out of view; a reminder body carrying a `dd/mm`
  date is date-checked like a full one.
- **AC-25:** `sweep()` returns no layout complaints, `npm run check:responsive` passes, `npx tsc --noEmit` is
  clean, and `npm run build` succeeds.

## API Contract

### GET /api/invoices
Unchanged shape. Now `[Authorize(Policy = AdminOrDoctor)]`.
Errors: `403` for a secretary. `?patientId=` remains reachable by any clinic role.

### POST /api/expenses
Request: `{ …, expenseDate: string }` — now **required**.
Errors: `400 expense_date_required — Une date est requise pour cette dépense.`

### GET /api/audit
Adds `?userId=<id>` (filters by actor). Default page size bounded at 200 when no paging is supplied.

### POST /api/auth/login
Unchanged shape. A TOTP code already spent for this `(user, counter)` is refused.
Errors: `401 invalid_credentials` — deliberately indistinguishable from a wrong code, so a replay cannot be
used to learn that the code was otherwise valid.

### POST /api/auth/refresh
Unchanged shape. Rate-limit partition moves from the client address to the session, so one address no longer
caps a whole practice's silent renewals.
Errors: `429` unchanged in shape, but the client now renders « session expirée » with a sign-in route rather
than looping on 401s.

### DELETE /api/patients/{id}/odontogram/conditions/{conditionId}
A rule refusal now answers `400` (was `404`), body unchanged.

## Data / Schema Changes

None. No migration. The backup default path is configuration, not schema.

## Device Behaviour

- **Leading device:** tablet (the device this app is used on most), then desk, then phone.
- **Narrow width (< 640):** unchanged from what each screen already does — this feature fixes measurements and
  focus, it does not restructure any layout. Tables keep their existing card form; dialogs keep their sheet form.
- **Touch:** the five under-44 px targets in Band E get `.touch-target` (`globals.css:750`), which 20+ components
  already use. Floor inherited from `~/.claude/skills/DEVICE-CONTRACT.md`.

## Out of Scope

- Auth/session flows, onboarding (`/signup`, `/join`, `/setup`), and the vendor console on :3100 — never in the
  QA pass's scope.
- Surfacing the CNAM reimbursement estimate on the devis or invoice screens (decided: follow-up).
- Adding matricule fiscal / RIB to a fournisseur (decided: the current shape was deliberate, AC-1 of
  `stock-fournisseurs`).
- Seeding `DefaultFee` on the 100 dental acts — a data task needing a price list, not a code fix. The « Tarif »
  column keeps reading « — » until someone populates it.
- Showing the sent message body in the reminders log — a new surface with its own layout and privacy questions.
  ⚠️ **Follow-up to capture separately:** 19 of 22 `Sent` rows carry a body naming a different day than the
  appointment beside them. The wrong-day *rendering* was proven correct, so these are most likely residue of an
  earlier build or import — but that has not been established, and it is not settled by deciding not to display
  the text.
- Surfacing the CNAM estimate on the devis (above) and adding fournisseur fiscal fields (above).

## Edge Cases (Critical only)

- **Band A must not become "always clear".** An update payload that omits a key still means unchanged; only an
  explicit empty value clears. Conflating them is the bug in the opposite direction and would delete data.
- **Band B adds a 409 users have never seen.** Every one of the ten forms needs the conflict rendered as a
  French sentence that keeps the user's input, not a toast that discards it.
- **The audit fix must not lose history.** Removing the phantom « Modification » must not remove the genuine
  create entry beside it.
- **A `Version == 0` round-trip is still unprotected.** Jobs and the Google→App sync legitimately pass 0; the
  band-B change must not make those paths start failing.
- **TOTP replay protection constrains automated sign-in.** The QA harness currently mints several admin sessions
  inside one 30-second window, which only works because replay is unprotected. After this change any parallel
  test run must space admin sign-ins across windows or use distinct accounts.
- **The refresh partition must not become a bypass.** Keying on the session means an attacker with no session
  must still be bounded — the address ceiling has to remain for the unauthenticated case.
- **Scope is deliberately one pass.** Criticals, majors and ~88 minors land together by the owner's explicit
  decision. The review at the end must not let a security fix pass on the strength of the cosmetics around it.
