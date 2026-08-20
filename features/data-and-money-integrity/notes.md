# data-and-money-integrity — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident.
Moved out of the root `CLAUDE.md` verbatim so it is no longer loaded into every session; the root
indexes it under **Architecture notes**. `spec.md` is what was asked for, `stories/` how it was built,
and this is what shipped.

## Optimistic concurrency, solution-wide (`data-and-money-integrity`)

`Entity<TId>.Version` is mapped in the
`ApplicationDbContext` loop onto PostgreSQL's **`xmin` system column**, giving all 38 entities a concurrency
token with **no schema change**. A losing write raises `DbUpdateConcurrencyException`, translated **once** in
`UnitOfWork.SaveChangesAsync` into `ConflictException` → **HTTP 409** with the canonical `{ error }` body.
Two things are easy to get wrong here: (a) the handler catch-alls must carry
`when (ex is not ConflictException)` or a 409 is flattened into a generic failure — only catches that
*return a Result* were filtered, since a log-only catch is a best-effort post-commit side effect that must
still swallow; (b) the check must run against the version **the user was editing**, so the six round-tripped
aggregates (Patient, Appointment, Invoice, TreatmentPlan, DentalRecord, Clinic) send `Version` back on the
update and the handler calls `IUnitOfWork.SetExpectedVersion` — a version of `0` means "not supplied" and
skips the check, which is what keeps the AI dispatcher, Google→App sync and the jobs working.
⚠️ The `AddConcurrencyToken` migration has a **deliberately empty `Up()`**: EF's differ emits 38 ×
`AddColumn<uint>("xmin")`, which PostgreSQL rejects (`column name "xmin" conflicts with a system column
name`). It is committed for its **model snapshot** only.

## Money is correctable, not immutable (`data-and-money-integrity`)

invoice payments and treatment-plan
installment payments can be **voided** (motif + actor + moment recorded; the row is kept and struck through,
and a reprinted receipt is stamped « REÇU ANNULÉ »). The installment side required an **event-sourced
`InstallmentPayment` ledger** — a single cumulative `AmountPaid` has nothing to void, and dating it by one
`LastPaidOn` also booked revenue into the wrong month. **Avoirs** are now readable, listable and printable,
and are netted in *both* branches of the revenue read and in the dashboard KPI. Issuing a **devis→facture**
bridge invoice **carries the plan's collected payments across**, with the read-side de-dup extended from
outstanding to cash — the two had to land together or the money is either doubled or erased.

## Patient records resist destruction (`data-and-money-integrity`)

deleting a patient is **refused** when
anything is attached (the message names the real counts), and **archiving** (`Patient.IsArchived`) is the
escape hatch. Contact details are genuinely optional — `Email`/`PhoneNumber` are nullable and the four
sentinel literals (`noemail@example.com`, `0000000000`, `unknown@example.com`, `000-000-0000`) are retired.
The `PUT /api/appointments/{id}` partial-update wipe is closed by generalizing the tri-state pattern to
`ProcedureTypeId`/`DoctorId`/`Notes`/`DoctorName`; `UpdatePatientCommand`'s contact fields use the same
mechanism, so a field can finally be **cleared** rather than only overwritten.
**And a patient can no longer be created twice by accident**: `CreatePatientCommandHandler` runs
`Features/Patients/PatientDuplicateIndex` (name+DOB · name when no DOB was supplied · phone through
`PhoneNumber.ToE164`, names folded through `SearchTerm.Normalize`) before it builds anything, refusing with
**`patient_duplicate`** so the client can offer « Créer quand même » (`AllowDuplicate`) — advisory, because two
people genuinely share names and the « Nouveau patient » form is also where a walk-in is registered with nothing
but one. ⚠️ **The matching was not new — its reach was.** It had lived as a private nested class of
`PatientImportPlanner`, so the CSV import (the least-used door) checked while the patient form and the appointment
dialog's inline « Nouveau patient » did not; it was **moved**, not copied, so « what counts as the same person » has
one answer. This is the `fixes-dont-propagate` shape again. On the client the load-bearing half is separate and
worse: `create-appointment-dialog`'s `performCreate` **re-ran `patientsApi.create` on every retry**, and it is
retried by design (slot-taken, out-of-hours and past-time all re-submit) — so one « créer quand même » on a taken
slot created the patient a second time. It now remembers the created id in a ref and reuses it, and the new-patient
fields go read-only once that patient is committed, since the record exists and the form can no longer reach it.

## `reconcile-money` (Local-mode console verb)

`dotnet run -- reconcile-money` prints a per-clinic
reconciliation — the two payment ledgers against their stored denormalizations, per-plan échéancier sums,
monthly collected computed the **old and the new way** (the line that proves the ledger migration moved no
closed month), orphan and sentinel counts, over-credited invoices and duplicate bridge invoices. Exit code
**0** clean / **1** couldn't run / **2** drift found; it never mutates. Run it before and after the migration
batch and diff.
