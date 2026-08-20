# adoption-qa-l-residual-blockers — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident.
Moved out of the root `CLAUDE.md` verbatim so it is no longer loaded into every session; the root
indexes it under **Architecture notes**. `spec.md` is what was asked for, `stories/` how it was built,
and this is what shipped.

## A reminder queue that cannot starve, and never announces the wrong day (`adoption-qa-l` L3)

the outbox had
two individually-defensible decisions that together stopped the whole install sending. A row whose channel was
disabled or unconfigured was left `Pending` *on purpose* (« so it sends once the operator configures it ») and
the purge deliberately never deleted a `Pending` row — but the dispatch scan is `Pending && due`, **oldest
first**, `.Take(50)`, so unsendable rows accumulate at the *front* and past the batch size consume every tick
for ever. There was no clinic dimension either, so one practice starved the others.
The fix is a **new non-terminal status, `NotificationStatus.Blocked`**: the row survives and records why
(both original intentions) while leaving the scan, and `NotificationJob.ReviewBlockedRowsAsync` returns it to
the queue once the channel is sendable — so the status is not a one-way door. `GetDueForDispatchAsync` adds a
**per-clinic bound** (clinics served oldest-due-first, capped per tick; the single-clinic install keeps the flat
query it had), `ReminderScheduler` now checks sendability at **enqueue** on the appointment path too, and
« N rappels bloqués » is a counter + a filter chip on `/rappels` with the reason on each row.
⚠️ Two more things live here. **`ReminderSchedule.ComputeSendTimesUtc` returns every future tier**, not the
largest — it returned one `DateTime?` while the settings screen invited « Ex. 24, 6 », so for a no-show problem
the 6 h nudge was the one being discarded — with idempotency on **(appointment, channel, tier)** where the
tier's identity on the wire *is* its send instant, and a **quiet-hours floor** (21:00→08:00 clinic-local) that
moves a send **earlier first**: an 08:00 appointment booked ~22 h ahead resolved to 02:00, and 21:00 the evening
before reaches the patient whereas 08:00 *is* the appointment. And **`GoogleCalendarSyncService` finally has an
`IReminderScheduler`** — it called `Reschedule()` and committed straight through the repository, so a visit moved
in Google kept the reminder frozen at the old day; `ReminderMessage.AnnouncesStaleMoment` is the dispatcher-side
backstop that makes every *future* write-path omission harmless, and it shares its formatter with the scheduler
that writes the body so the two cannot drift.

## Backup is automatic, verified, and restorable (`adoption-qa-l` L4)

the entire protection used to be a button
someone had to remember, whose documented default destination **failed on a fresh install** (the service threw
when both the argument and `Backup:DefaultDestination` were empty — and the installer wrote that key as `""`),
that recorded nothing about when it last ran, and that was never verified readable. Now: an **hourly**
`BackupJob` (not daily — the hour lives on the clinic, and an hourly check also covers the PC switched off at
02:00), `pg_restore --list` **verification whose failure fails the backup**, a real install-relative default
destination with a **same-volume warning**, a **`BackupRuns` ledger** behind « Dernière sauvegarde réussie » +
`GET /api/backup/history` + an ensure/clear staleness notification, retention that prunes oldest-first and
**never empties the folder**, a pre-migration backup that **aborts the migration if it fails**, and a
**`restore-backup` console verb**. See `api/ClinicManagement.API/CLAUDE.md` for the verb's ordering guarantees
and `packaging/README.md` for the operator view. ⚠️ The installer now writes **two** config files split by
ownership (`appsettings.Install.json` machine-derived and rewritten · `appsettings.Production.json`
operator-owned and never truncated) — it used to truncate the operator's file on every upgrade.

## A cheque has an identity, and it travels with the money (`adoption-qa-l` L8, slice A)

post-dated cheques are
ubiquitous in Tunisian practice and `PaymentMethod.Cheque` was a **bare enum value** — `Payment`,
`InstallmentPayment`, `CreditNote` and `Expense` each carried `Amount`/`Method`/`PaidOn` and nothing else. For money
*out* the number could go in an expense's description; for money **in** there was no free-text field of any kind, so
« quel chèque, de quelle banque, encaissable quand ? » had nowhere to live. Both payment ledgers now carry
`ChequeNumber`/`ChequeBankName`/`ChequeDueDate`, and `Domain/ValueObjects/ChequeDetails.cs` is the **one** guard:
details on a non-cheque method are refused there, not by a CHECK constraint (a second copy of the rule whose failure
would be a 500 instead of the French refusal) — so `verify-schema` **verifies** the invariant instead
(`cheque-details-only-on-cheques`, over both ledgers), while the six columns and two partial indexes are diffed
against the catalog for free.
⚠️ **The load-bearing call site is the devis→facture bridge** (`IssueInvoiceCommand`): it carries an installment
payment onto the invoice, and a cheque left behind there would vanish from any « chèques à encaisser » view — the
plan side stops being counted the moment the bridge invoice is issued, so the row that still has to be banked would
become the one row nothing lists. `InstallmentPayment.ToChequeDetails()` rebuilds it **through** `ChequeDetails.For`,
re-checking the invariant on the way across rather than trusting it. ⚠️ Two smaller traps: the index filters key on
`ChequeDueDate IS NOT NULL`, **not** `Method = 1` (equally selective by the invariant, and the enum form would bake
an ordinal into SQL where no compiler checks it); and `ChequeDueDate` is a **calendar day** sent as a bare
`YYYY-MM-DD` — `toISOString()` would shift a cheque due on the 1st into the previous month. All three fields stay
**optional even for a cheque** (refusing money genuinely received to enforce a field is the wrong trade), so a
cheque with no due date is counted as its own group rather than dropped. Client side: `components/factures/cheque-fields.tsx`
is the single conditional sub-form and `chequePaymentFields()` the single payload builder — it clears the fields
when the method is not `Cheque`, which is what makes "the server refuses details on a cash payment" unreachable
rather than merely unlikely.

## Data comes back in as CSV, and the dry run is the product (`adoption-qa-l` L5, import half)

a dentist arriving
with 3 000 patients in a spreadsheet used to type them in by hand — the spec names that as the single thing that
stops most switchers. `POST /api/patients/import/preview` → mapping → `POST /api/patients/import`, both
**`AdminOrDoctor`**, multipart, scoped to patients. `Application/Common/Csv/CsvReader.cs` is the reader half of the
writer above it, and **nothing about it is symmetrical**: the writer controls its one shape, the reader is handed
whatever the previous software produced, so the **delimiter** (`;`/`,`/tab, counted on the header record only), the
**encoding** (invalid UTF-8 falls back to Latin-1 — a BOM-less Excel file on a French Windows is cp1252, and
decoding it as UTF-8 dies on the first « é ») and the line ending are all *detected*.
⚠️ **The preview is a `Query` and the commit a `Command`, and that is not stylistic**: `RealtimeBroadcastBehavior`
derives its key from the namespace, so a dry run in `Commands` would announce an import that has not happened. The
same mechanism is why the commit does **not** `Send` a `CreatePatientCommand` per row (3 000 broadcasts, 3 000
refetches in every open browser) — the *rules* are shared instead of the pipeline, by extracting
**`PatientFromRequest.Build`** out of `CreatePatientCommandHandler`, which is now its only other caller. That is
what makes the spec's « reuse `CreatePatientCommand`'s validation rather than a parallel path » literally true.
⚠️ Three more decisions worth knowing. (a) **One `SaveChangesAsync` per row**, because « all-or-nothing per *row*,
never a silent partial commit » is unachievable with one save for the file — one refused row would take the other
2 999 — with `IUnitOfWork.StopTracking` detaching each committed row so EF does not re-scan 3 000 entries on every
later save. (b) **Nothing is staged server-side**: both calls carry the file, so a mapping change re-runs the whole
dry run and the *identical* `PatientImportPlanner` produces both the preview and the commit — a preview built by
other code is a promise the commit need not keep. (c) **Duplicate matching is deliberately eager** (name+DOB, name
alone when the row supplies no DOB, or phone through `PhoneNumber.ToE164`) and skips by default, including matches
against *earlier rows of the same file*: a false positive costs one « Créer quand même » tick, while a false
negative is permanent — this product has **no patient merge and no soft delete**. Phones are normalised to `+216`
E.164 on the way in, which the hand-typed write path notably does **not** do (`PhoneNumber`'s ctor only trims); the
spec names that standing defect and forbids replicating it. The « Sexe » export column now writes « Homme »/« Femme »
through `PatientGender` (both directions in one file) — it had been emitting the raw `Male` into a French file, one
column over from a `YesNo` that was translated on purpose.

## Data leaves the product as CSV (`adoption-qa-l` L5, export half)

there were **zero** occurrences of `csv`
anywhere in the repo and zero of « Exporter » in `web/`, so the only way data left was a `pg_dump` readable by
PostgreSQL tooling and nothing else — the owner could not leave with their own data, or hand their accountant
anything. `Application/Common/Csv/` is the single authority: **UTF-8 with a BOM** and a **`;`** delimiter (Excel
on Windows reads BOM-less UTF-8 in the system codepage, and its list separator follows the fr locale — get
either wrong and the file is mojibake in one column), money through `InvoiceCalculator.RoundMoney` and dates
through `ClinicClock`. ⚠️ **An export re-sends the screen's own query with `paging: null`**, which the paging
primitive models as a first-class case — so « honours the current filters, exports the whole filtered set, never
the current page » is true by construction rather than by discipline. Money exports are `AdminOrDoctor`, matching
the reads they export. All nine lists carry the button — `/patients`, `/factures`, `/treatment-plans` and `/caisse`
in their `PageHeader`, and `/creances`, `/stock`, `/lab-orders`, `/appointments` and la caisse's « Dépenses » card
**beside the filters they export**, because those components own their own filter state and a copy lifted to page
level would be a second authority on what is on screen. ⚠️ One deliberate superset: the agenda's CSV covers the
whole window and every statut, since « Terminés »/« Annulés » *reveal* rather than narrow — honouring them would
make the ordinary export of a past week omit almost every appointment in it, and `Statut` is a column in the file.

## La caisse says *how* the money came in, and a cheque has somewhere to be chased (`adoption-qa-l` L8 slice B)

`CaisseSummaryDto.CashInByMethod` splits « Encaissé » per `PaymentMethod`, the « extrait » takes a `method` filter,
and **`GET /api/billing/cheques`** (`AdminOrDoctor`, `/cheques`) lists every cheque the clinic holds across *both*
payment ledgers, soonest-due first. Before it, four scalars summed across every method meant the owner closing the
drawer could not tell the notes in it from a post-dated cheque nobody has banked — the one distinction a cash count
is made against.
⚠️ **The breakdown is a `GROUP BY` sibling of the very SUMs that produce `CashIn`**, predicate for predicate — *not*
a grouping of the statement's rows, which carry voided payments and would make the lines silently disagree with the
total above them. All four methods are always present in enum order, zeros included: « Espèces 0,000 » on a day of
cheques is a true statement about the drawer, and an absent row is not a statement at all. ⚠️ The `method` filter is
applied **after** the running balance, beside the search term and for the identical reason. ⚠️ The cheques list
applies the **bridged-plan de-dup**, and there it is load-bearing rather than consistency theatre: the devis→facture
bridge carries a cheque onto the invoice, so without it one physical cheque is listed twice and the duplicate is
indistinguishable from a second genuine cheque of the same amount from the same bank. ⚠️ **A cheque leaves that list
only by being voided** — the product records a cheque's *receipt*, never its clearing at the bank — which is why the
four bucket counts (en retard / bientôt / plus tard / **sans date**) are the headline and the screen says so out
loud. On the client, the per-method figures **are** the filter's control (`cash-in-by-method.tsx`), the same
figure-links-to-its-records rule the dashboard follows.

## A reimbursement estimate now knows what is left (`adoption-qa-l` L10)

`Domain/Services/CnamPlafond` is the
single authority on the CNAM **annual ceiling** — the dependants barème, the dedicated soins-dentaires allowance,
and which act categories are *hors plafond* (prothèse) — and `GET /api/patients/{id}/cnam-ceiling` reports the
ceiling, what this clinic has consumed of it in the **clinic** year, and what remains. There were zero repo hits for
`plafond`/`ceiling` before it, so « Remboursement indicatif » told a patient who had exhausted their ceiling in
March exactly what it told one who had never claimed.
⚠️ **Every figure is an estimate for two independent reasons, and both are DTO fields rather than each screen's own
wording**: `ceilingIsDefault` (the 2024 amounts are two agreeing Tunisian outlets with no official CNAM page
retrieved, so they are a *default* that `CnamInfo.AnnualCeilingOverride` always beats) and `seesThisClinicOnly`
(the clinic counts only its own acts, so « reste » is an **upper bound**). ⚠️ Consumption is measured from **issued
invoices**, because nothing records a BS1 submission with an amount — so the figure lags a bulletin the caisse has
not paid and leads one it refused. ⚠️ `ComputeCeilingConsumptionAsync` is a member on the existing
`ICnamBillingCalculator`, not a second calculator, and it applies **no cap**: clamping to what the clinic charged
would under-report consumption on a discounted invoice and so over-state the ceiling left.

## An arrêt de travail is printed on the caisse's own form (`adoption-qa-l` L11)

`CnamArretTravailRenderer` is the
**second overlay renderer**, on `CnamBs1BulletinRenderer`'s pattern, stamping `DocumentTypes.ArretTravail` onto the
genuine CNAM **P 061** (`Assets/P61.pdf`). « arrêt de travail » previously appeared *once* in the whole repository —
as a description string on the generic certificat tile — so a dentist either hand-wrote it or printed a free-text
certificat the caisse does not accept.
⚠️ **`Assets/P61.pdf` is a normalised copy** of the bundled scan with the rotation baked into the content stream as
A4 landscape, so every coordinate in the renderer matches what a ruler on the printout measures. ⚠️ **Which of the
three bundled PDFs is current was settled by reading them** (`P61_2024.pdf`; `CMIATMP.pdf` is the AT/MP form, which
P 061's own header excludes) but **not** by an official publication — recalibration is expected if a caisse uses
another revision, and **printing onto real paper is still owed**. ⚠️ `ArretTravailValidation` applies the K-series
lessons from the start (mandatory duration/date, a **chosen** practitioner never `doctors[0]`, one of code
conventionnel / n° d'ordre), and the **motif is deliberately never printed**: P 061's front carries no diagnosis
field and is what the patient hands their employer. In the editor **`isOfficialForm`** now names what this document
and the BS1 share — an iframe PDF preview (so Print goes through the iframe), no practitioner fall-back, a pre-Save
gate, and **no Word export**, since a `.docx` of a pre-printed form is the letterhead alone.

## Money and clinical work know who earned them (`adoption-qa-l` L9)

`Invoice`, `TreatmentPlan` and `DentalRecord`
gained a nullable **`DoctorId`** with a real FK, `WaitingListEntry.PreferredDoctorId` became one, and
`Application/Common/PractitionerAttribution` is the single precedence rule that fills them — explicit → the visit's
practitioner → the caller's own `Doctor` record, each checked against the clinic's roster. `DoctorId` had existed on
exactly three entities, none of them carrying money, and `Features/Dashboard/` contained **zero** occurrences of
`Doctor`. A practitioner filter now narrows `/factures` and the dashboard's **Argent** section.
⚠️ **The caller is the *last* resort, not the first**: a secretary recording a dentist's work must not credit
themselves — and in the common single-dentist practice the owner *is* the caller, which is exactly where that
fall-back is right. ⚠️ **The attribution travels with the money and is never re-derived**: the fiche→facture and
devis→facture bridges copy the source's practitioner verbatim, because they bill work that already happened and
re-resolving would credit the *biller*. ⚠️ **Nullable means nullable** — historical rows and visits booked with no
practitioner have none, so an unattributed row is *excluded* under a filter rather than silently included (two
dentists' filtered totals must not exceed the clinic's). ⚠️ **The dashboard filter narrows two figures of five, and
the DTO says so** (`ClinicWideOutgoings`, `CollectedInvoicesOnly`): an expense has no practitioner, so a narrowed
« Net » would be one dentist's income minus everybody's costs. ⚠️ The migration **nulls orphaned
`PreferredDoctorId` values before adding its FK** — the column was unconstrained for the product's whole life, and
`AddForeignKey` over such a row aborts the upgrade after the schema is half-applied. This is **attribution, not
authorization**: per-practitioner data scoping is deliberately out of scope. `verify-schema` gained
`practitioner-attribution-backfill`, because a backfill is the one thing invisible to every other layer.
