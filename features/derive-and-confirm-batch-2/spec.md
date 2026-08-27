# Spec: Derive & Confirm — Batch 2 (compound actions, derivations, de-clutter)

**Status:** APPROVED
**Type:** Small (forced multi-item pass — user requested "continue in one pass, you have the specs")
**Branch:** feature/windows-desktop-app (existing)

Continuation of the approved "Derive & confirm" blueprint. This batch covers the additive,
low/moderate-risk items. Two items are deliberately deferred (see bottom).

## What Changes

### P2-C — Free de-clutter (safe, mostly deletion)
- Delete the orphaned global pages `web/app/records/` and `web/app/files/` (unreachable — no nav
  link / no `router.push` / no `<Link>`; sidebar already documents their removal). Keep the live
  `/patients/[id]/files` route and shared `PatientSummaryModal` / `patientFilesApi`.
- Single invoice entry: remove the "Note d'honoraires" card from the `/documents` gallery so an
  invoice is created only from `/factures` + the patient factures tab. Remove the now-unused
  `honoraires-launcher.tsx` if nothing else references it.
- Refresh stale nav/route tables in `web/CLAUDE.md` + `web/components/CLAUDE.md`.

### P2-A — "Nouvelle ordonnance" on the patient page
- Add a "Nouvelle ordonnance" action on the patient documents tab that opens the document editor
  with the patient preset (no leave-and-re-search).

### P2-B — "Renouveler / Dupliquer" a prescription
- In the document editor, when viewing a saved ordonnance, add a "Renouveler" action: new document
  (new id), today's date, same patient + same meds — preserves history instead of overwriting.

### P1-B — Waiting-list "Promouvoir" creates the appointment
- "Promouvoir" opens the create-appointment dialog prefilled from the entry (patient, preferred
  doctor, note) → on success, auto-promote the entry with the new appointment id. One gesture, no
  patient re-entry.

### P1-D — "Émettre + encaisser" one step
- On a draft invoice, a compound action that issues then immediately opens the payment modal
  (already prefilled full outstanding / Cash / today). Existing separate Émettre / paiement stay.

### P1-E — Auto-queue El Fatoora on issue for enabled clinics
- After `IssueInvoiceCommand` succeeds, best-effort enqueue the e-invoice **iff** the clinic has TTN
  enabled and the invoice `CanSubmitToElFatoora`. Never fail issuance; no stuck→Failed row when not
  configured. The minutely `EInvoiceOutboxJob` dispatches.

### P1-A — Completed visit raises a post-visit action prompt
- When an appointment transitions to `Completed`, ensure the post-visit-review prompt is present so
  staff are nudged to record/bill/book-next. Only if this does not duplicate the review already
  scheduled at create time (verified during implementation; adjusted or deferred if it would).

## Acceptance Criteria
1. `/records` and `/files` routes no longer exist; app builds; no dangling imports.
2. Invoices can no longer be started from `/documents`; `/factures` + patient tab still work.
3. "Nouvelle ordonnance" on the patient page opens the editor with the patient already selected.
4. "Renouveler" produces a new saved ordonnance dated today with the same meds; the original is
   unchanged.
5. Promoting a waiting-list entry books the appointment from the entry (no re-typed patient) and
   marks the entry Promoted.
6. "Émettre + encaisser" issues then opens the payment modal in one action.
7. Issuing an invoice for a TTN-enabled clinic (with a submittable invoice) queues the e-invoice;
   a non-enabled/non-submittable clinic queues nothing and issuance still succeeds.
8. `dotnet build` + `npx tsc --noEmit` + `npm run build` clean (0 errors / 0 new warnings).

## Deferred to their own passes (with reason)
- **P0-3 (Facturer + reconcile):** `Invoice` has no `TreatmentPlanId` column → needs a migration +
  devis↔invoice dedup in "Solde patient" + a double-count regression test. Money-trust sensitive;
  deserves a focused pass, not a blind batch edit.
- **P1-C (CNAM code record→invoice):** `DentalRecordAct` carries no dental-act/CNAM code today →
  needs a record-act model change (entity + migration + record UI) first. Its own feature.
- **P2-D (multi-tooth batch diagnosis):** optional UI-only enhancement, lowest priority.

## Tests
Deferred to `/test-small-feature`.
