# caisse-extrait — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident.
Moved out of the root `CLAUDE.md` verbatim so it is no longer loaded into every session; the root
indexes it under **Architecture notes**. `spec.md` is what was asked for, `stories/` how it was built,
and this is what shipped.

## La caisse has a statement, and it is a read (`caisse-extrait`)

`GET /api/billing/caisse/ledger` returns the
**« extrait de caisse »** — every movement behind the totals, oldest first, with a running period balance. Before it,
la caisse showed three figures over a table of **expenses only**: the money-*out* side was itemised while
« Encaissé », the bigger number, was opaque, and no screen anywhere listed what made it up.
⚠️ **There is no `CashMovement` table, deliberately.** `GetCaisseLedgerQuery` merges the four ledgers that already
exist (invoice `Payment`, devis `InstallmentPayment`, `CreditNote`, `Expense`) through the **same repository
predicates the totals sum** — the two row-level reads (`GetPaymentsBetweenAsync`,
`GetInstallmentPaymentsBetweenAsync`) are predicate-for-predicate copies of their sum siblings, including the
billed-plan de-dup. A movement table written by each money path is double bookkeeping: the day one write site
forgets, the statement and the totals disagree and nothing can say which is right. Reading the same rows makes
**`Σ movements == cashIn − refunds − cashOut == net`** an assertion a test holds (`CaisseLedgerTests`), which the
table design cannot offer. A **voided** row is listed with its motif and actor and excluded from the balance
(§ 1 keeps a void visible, struck through); `RunningBalance` is **window-relative** and labelled
« Solde de la période », not an account balance.
**`CaisseSummaryDto.CashIn` is now gross and `Refunds` is its own field** — it used to absorb avoirs silently,
which stopped working the moment a statement listed a refund as money leaving: the lines could not sum to the
total above them. The dashboard's Argent section gained the same split in the same change (the two are held equal
by `MoneyReadConsistencyTests`), so `Net = Collected − Refunds − Expenses` on both. A refund-only window now reads
honestly (`cashIn` 0, negative net) instead of reporting a *negative cash-in*, which is not a thing a till has.

## A session's payment reaches the till (`caisse-extrait`)

`POST /api/invoices/from-dental-record/{id}` prices a
fiche de soins' acts, **issues** the note d'honoraires and — when `paidNow` is supplied — records that payment, in
**one transaction**. It closes the trap named in the invoice↔visit note above: `DentalRecord.AmountPaid` was read
by nothing but the fiche's own display, so a dentist could type an amount, see it on screen, and it would never
reach la caisse, the dashboard or the patient's balance. Cash lives in exactly two ledgers and the fix is to make
the fiche produce a real payment on a real numbered document — **not** to teach a fourth read about a fourth source.
⚠️ Two things to know. (a) Unlike the devis bridge this does **not** produce a draft: a payment requires an
`Issued` invoice, so a **gapless number is consumed** and a mis-keyed amount is corrected by an **avoir**, never an
edit — which is why every validation (amount, method, date, over-payment against the TTC the invoice *will* freeze
via `InvoiceCalculator.Compute`) runs **before** the transaction opens. (b) The per-tooth pricing rule (quantity ×
unit price vs. one flat fee) **moved** out of the browser into `DentalRecordInvoiceLines` — it lived inline in the
patient page to seed a form, and two implementations of how recorded work becomes money is the § 5.10 defect in a
new place. The old prefilled `InvoiceFormModal` path on the fiche is replaced by `bill-dental-record-dialog.tsx`,
which shows the acts read-only and lets the server price them.
