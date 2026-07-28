# Follow-up: Patient merge

**Created:** 2026-07-28
**Origin:** `features/audit-sections-3-to-10/` — dropped from P7 during design review, deliberately
**Status:** Not started · design already done
**Priority:** Low until a clinic actually reports split history

## Why it was dropped

Merge entered scope because § 6.4 of `CODEBASE_AUDIT_2026-07.md` mentions it in **one sub-clause** —
*"No patient merge either"* — inside the audit-trail bullet, and because § 6.4 was scoped at full breadth. Nobody
argued for it on its merits.

Three things came out of reviewing that:

1. **§ 1 already answered the common case.** `ArchivePatientCommand`'s own comment states archive exists precisely so
   *"a duplicate patient with a single booking could never be removed from the list."* A duplicate spotted soon after
   creation is archived, not merged.
2. **The actual cause was un-guarded.** There is **zero duplicate detection anywhere** in the codebase, and
   `create-appointment-dialog.tsx:400` creates a patient inline from a name and phone number **without requiring a
   search first**. Prevention is cheaper than consolidation and stops the problem at source. That became
   **AC-P7.18–7.26** in the spec.
3. **Merge is the most expensive item in P7** — 16 child types reparented in one transaction, a bespoke conflict UI,
   blob relocation, FK-less soft-link handling, plus duplicate-child resolution. § 1 killed it for the same reason:
   *"Merge is a separate problem with its own conflict rules."*

## When it becomes worth building

Only when **both** records already carry real history — which happens when two staff each unknowingly "own" one
record for months. Two reasons it matters more in dentistry than elsewhere:

- **The odontogram is cumulative.** A tooth chart split across two records is actively misleading: the dentist
  reading record A does not see the extraction charted in record B. Archiving one record *hides* that history rather
  than consolidating it.
- **« Solde patient » is computed per patient row** (`GetPatientBillingSummaryQuery`). Two records means two balances,
  so the clinic either under-collects or chases a debt already paid on the other file.

**Trigger to revisit:** a clinic reports a patient whose history is split across two records, or the duplicate-warning
telemetry from AC-P7.26 shows operators regularly proceeding past the warning.

## What already exists

The design is **done and reviewed** — do not redo it:

- **Mockup:** [`mockups/01-fusion-patients.html`](./mockups/01-fusion-patients.html) — a four-step wizard
  (Survivant → Champs → Dents → Vérifier) reusing `setup-wizard.tsx`'s step indicator verbatim.
- **Field chooser:** radio pair per conflicting field, survivor pre-selected — except where the survivor's value is
  empty, in which case the *other* side is pre-selected and flagged « Seule copie » in amber, so "offered, not
  silently dropped" is the default rather than something the operator must notice.
- **Odontogram:** needs **no new chart code**. `RecordToothChart` is presentational and `ToothPaint` already encodes
  two states per tooth — `color` as fill, `existingColor` as a 3px outline, dashed when the prior state was a
  diagnosis. Merge extends that grammar with an amber ring for unresolved teeth.
- **Duplicate children** surface in step 4 with a resolution each, using the blocker `<ul>` markup from
  `patients-table.tsx:363-380`. The two-appointments-in-one-slot case is what the exclusion constraint (spec
  AC-P1.15) would otherwise reject mid-transaction.

## Requirements captured before the drop

The dropped ACs, so nothing has to be re-derived:

- Operator nominates the survivor; child counts shown, because that is the real decision input.
- Every child row reparents — appointments, dental records, tooth states, invoices, treatment plans, files, folders,
  medical documents, flags, medical and family histories, notifications, recurring series, waiting-list entries, lab
  orders, recall state. **Exhaustive list pinned by a reflection test** — a child type added later and forgotten is
  silent data loss.
- **Reflection cannot see two things**, and both must be handled explicitly: file-storage blob keys (prefixed with
  the patient id in MinIO and on local disk) and FK-less soft links (`AuditEntry.EntityId`, `StaffNotification`
  deep-links, `InvoiceLine.DentalRecordId`, `TreatmentPlanItem.LinkedDentalRecordId`, `CnamClaim`).
- One transaction. A partial merge is unrecoverable.
- Invoice and devis numbers are **never** rewritten — they are legal identifiers.
- The merged record is archived with a reason naming the survivor, cross-linked both ways, never deleted.
- Refused when either record is already archived, or the two are in different clinics.
- Refused while an e-invoice is `Pending` — `EInvoiceService` re-reads the patient at dispatch and sends
  `GetFullName()` as the TEIF legal buyer, so merging would transmit the survivor's name onto a filed fiscal document.
- The merge writes a single `AuditEntry` recording both patient ids, the actor, and every field override chosen.
  **The audit trail is a hard prerequisite** — an unlogged merge is unauditable by construction.

## Dependencies

- **Audit trail (spec P7a)** must exist first.
- Best done *after* duplicate prevention has been live long enough to show whether split-history cases still occur.
