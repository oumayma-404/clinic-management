# audit-sections-3-to-10 — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident.
Moved out of the root `CLAUDE.md` verbatim so it is no longer loaded into every session; the root
indexes it under **Architecture notes**. `spec.md` is what was asked for, `stories/` how it was built,
and this is what shipped.

## `verify-schema` (Local-mode console verb)

the sibling gate for **schema** changes, added by
`audit-sections-3-to-10`. Nothing in the test project touches a database, so a migration is the one class of
change unit tests structurally cannot verify — an index can be missing, an exclusion constraint can be
non-partial, a backfill can cover zero rows, and the whole suite still passes. `dotnet run -- verify-schema`
reads the **EF model** (its declared indexes, FKs and decimal precisions) and diffs it against PostgreSQL's own
catalog, so a schema object added in a configuration file is verified for free — deliberately **not** a
hand-maintained expectation list, which is the failure mode the plan's R-9/R-13/R-14 all describe. On top of the
diff it asserts what the model cannot express: `btree_gist` is installed, the appointment exclusion constraint
exists **and is partial** (a non-partial one makes a cancelled slot permanently unbookable), the two VAT-rate
columns keep `(5,2)` while every other decimal is `(18,3)`, and the per-migration backfill row counts. Indexes
are matched on **table + ordered columns, never on name** (a hand-written migration's name legitimately differs
from EF's). Same exit codes and the same before/after-and-diff workflow as `reconcile-money`; read-only.
Logic in `Application/Common/Maintenance/SchemaVerificationService.cs`, both-sides reader in
`Infrastructure/Persistence/SchemaVerificationReader.cs`.

## Tunisia is UTC+1, and `ClinicClock` is the only thing that knows it (`audit-sections-3-to-10` P6)

the solution had
**no clock abstraction** — 292 × `DateTime.UtcNow`, 5 × `DateTime.Today` (the *server machine's* zone, a third
convention) and two byte-identical private copies of a timezone helper. `Application/Common/ClinicClock.cs` is now the
single authority: `ClinicToday`/`ClinicYear`, `StartOfLocalDayUtc`/`EndOfLocalDayUtc`, and the three P6 additions
`LastTickOfLocalDayUtc`/`LocalDayRangeUtc`/**`TodayRangeUtc`** ("what a query means by today"). It closed the audit's
only 🔴: **invoice, devis *and* avoir numbers took their year from `DateTime.UtcNow.Year`**, so a note issued at 00:30
on 1 January Tunis was numbered into the fiscal year that had just closed — and a document number is legal identity,
gapless per year, with no correcting it afterwards. Same root cause, six more places: la caisse's « aujourd'hui »
default, the four reads that take **no date arguments** at all (« Solde patient », « Créances », les relances, the AI
summary — so no caller could compensate), and the AI assistant's « demain ».
⚠️ Two traps worth knowing. (a) `EndOfLocalDayUtc` is the *next* midnight (**exclusive**) while every money read is
inclusive on both ends — use `LastTickOfLocalDayUtc`, or a midnight payment lands in **both** adjacent periods
(finding #20). (b) On the client the equivalent is **`todayLocalIso()`** (`web/lib/format.ts`), never
`new Date().toISOString().slice(0, 10)`: `toISOString` converts to UTC first, so for the first hour of every Tunisian
day it pre-filled *yesterday* — that was the one genuinely user-visible symptom, a payment taken at 00:30 booked to the
previous day and, on the 1st, the previous month.

## A visit knows whether it was billed (`audit-sections-3-to-10` P6)

`Invoice.AppointmentId` had existed since the
invoice was written — accepted by the command, returned by the DTO, mapped by EF — and **nothing had ever populated
it**, so « cette consultation a-t-elle été facturée ? » had no answer on any screen. The write side now validates the
id against the caller's clinic **and** the invoice's patient (a column nobody writes is a column nobody validates);
the read side is `IInvoiceRepository.GetAppointmentLinksAsync` → **`AppointmentInvoiceLinks`**, one batched projection
feeding `AppointmentDto.InvoiceId`/`InvoiceNumber`. It is a shared helper rather than inline code because both
appointment reads must agree on *which* invoice counts: a **cancelled** note does not bill the visit (« Facturé » with
no money behind it, and it would hide the action to raise a replacement), and an issued one beats a stray draft.
Unlike its two clinic-wide siblings (`GetTreatmentPlanLinksAsync`, `GetDentalRecordLinksAsync`) it is **bounded by the
caller's id set** — the agenda has a date window, and annotating one week must not read every appointment-linked
invoice the clinic has ever raised. ⚠️ Note what is still **not** money: a fiche de soins carries `Cost`/`AmountPaid`
that **no money read touches** — encaissements are invoice `Payment` rows plus plan `InstallmentPayment` rows, minus
avoirs, and nothing else. A visit is financially invisible until a note d'honoraires or an échéance exists.

## One CNAM calculator (`audit-sections-3-to-10` P6)

the reimbursement estimate existed **twice** — the tested
backend `CnamReimbursementCalculator` and a client-side copy in `web/lib/api/cnam-nomenclature.ts` with its own
`CHILD_RATE`/`ADULT_RATE`, guaranteed to drift the first time CNAM moved a rate or a band edge. The client copy is
deleted; the BS1 editor calls a new **batch** endpoint (`POST /api/cnam-nomenclature/reimbursement-estimates`). Batch,
not the existing single-act GET, because the editor shows a live estimate **per act row** — N requests per keystroke is
what made a client-side copy attractive in the first place. Each item carries its own `careDate` (the rate turns on age
*at the care date*, and a bulletin's acts can straddle a birthday). Still editor-only: never persisted, never on the
BS1 PDF.
