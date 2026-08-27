# adoption-gaps-remediation — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident.
Moved out of the root `CLAUDE.md` verbatim so it is no longer loaded into every session; the root
indexes it under **Architecture notes**. `spec.md` is what was asked for, `stories/` how it was built,
and this is what shipped.

## Re-saving a fiche tops its note up, and la caisse's day is Tunisian (`adoption-gaps-remediation` Part 2)

the
bullet above shipped with two money leaks behind it. **(a)** The already-billed case was a flat refusal, so raising
« Montant payé » on a re-save — « le patient a fini de payer », the most ordinary edit there is — put nothing in the
till and said « enregistrée » in green. `CreateInvoiceFromDentalRecordCommand` is now **`BillDentalRecordCommand`**
returning a typed **`DentalRecordBillingResult`**: a higher amount records the *difference* as an additional payment
on the **same** note (`ToppedUp`), and lowering it, changing the acts after issue, or a note that is annulée /
entièrement créditée are refusals carrying a `Result.Code`. **(b)** The payment was booked as a hard-coded `Cash`,
so a séance settled by cheque never reached « Chèques à encaisser » and was counted under « dont espèces »; the
fiche now carries `PaymentMethod` + the three cheque fields, through the **existing** `ChequeDetails.For`.
⚠️ **The load-bearing refusal is in `UpdateDentalRecordCommand`, PRE-commit — not in the billing command.**
`DentalRecordAutoBilling` runs post-commit by design, so a refusal raised there arrives *after* the lowered amount
or the changed act list has been saved: the user reads a French message and the edit sticks anyway, leaving the
fiche permanently disagreeing with its own note. « Refusé » has to mean the save did not happen. Both doors call
the one **`DentalRecordBillingGuard`** and share **`DentalRecordBillingRefusals`**' wording, so guard and backstop
cannot drift. ⚠️ The `Contains("déjà facturée")` substring match is **deleted** — recovering an outcome by
matching French prose meant rewording a sentence silently changed behaviour, and `AlreadyBilled` is now a
*success* with an **informational** toast rather than a plain green one.
⚠️ **La caisse takes bare `YYYY-MM-DD` day keys now** (`Features/Billing/CaissePeriod`, the single authority on
period bounds for the summary, the extrait, both exports and the dépenses list). The browser used to compose the
instants itself — `new Date(day + "T00:00:00").toISOString()`, midnight in the **workstation's** timezone — so on a
machine set to anything but UTC+1 « la caisse du 3 août » covered a window offset by hours from the Tunisian day.
The clinic's day is a fact about the clinic; the browser is the one participant that cannot know it.
⚠️ And `ExpenseRepository`'s upper bound went `< to` → **`<= to`**: its three sibling ledgers are inclusive, so an
expense dated on the window's last tick fell out of the extrait while the payments beside it stayed in, breaking
`Σ movements == cashIn − refunds − cashOut` at a period boundary. Also here: **an échéancier payment can finally be
voided from the plan workspace** — the endpoint had shipped tested and with no caller at all, so a mis-keyed
installment payment was permanent while the identical mistake on an invoice payment was two clicks from being
corrected. And the « cancelling the bridge hands the money back » comment, which appeared in **three** places, is
corrected in all three: an invoice holding a non-voided payment **cannot be cancelled**, so the avoir is the only
route.
