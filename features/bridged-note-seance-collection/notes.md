# A later séance of a treatment billed on a note collects on that note

Reported from production, 30/09/2026 (Dr Hamdane, devis 2026-0011, note 2026-0070).

## What happened

| When | What |
|---|---|
| 26/09 | Séance 1 recorded as an ordinary fiche: act 160 DT, « Payé » 100 → note 2026-0070 |
| 30/09 09:39 | « C'est la suite d'une séance précédente » → devis 2026-0011 (160 DT), note **attached** to it |
| 30/09 10:29 | Séance 2: typed 60 → refused (« Payé » dépasse le total de la séance, 0 DT) |
| 30/09 10:29:49 | 60 DT taken through the devis' « Encaisser » → on the note, untagged |

No money was lost. The fiche simply had no route: the treatment field is withheld when a note represents the
devis (its échéance never sees a payment), and the séance's own « Payé » cannot exceed its 0 total.

## What shipped

- **`CollectOnTreatmentCommand` collects on the representing note** when there is one. Same cumulative-figure
  rule as the échéancier path; the idempotence key is the new **`Payment.DentalRecordId`** (migration
  `AddPaymentDentalRecordId`, nullable, filtered index, no backfill — null means « not taken by another séance »).
  The note's own fiche is left alone: its « Payé » already is its collection.
- **The fiche** shows « Payé » + Prix / Déjà payé / Reste à payer from the **note's** figures
  (`linkedInvoiceTotal` / `linkedInvoiceOutstanding`) on a later séance of a bridged devis. « Ajouter au devis »
  stays withheld there (the bridge rule).
- **Reopening** reads the séance's collection back (`GetCollectedByDentalRecordAsync` now includes tagged note
  payments), so a re-save takes nothing twice.
- **Deleting that séance** voids only its tagged payments and keeps the note (`NoteCollectionReversal`); the
  preview lists them. `Every_Soft_Link_To_A_Fiche_Is_Accounted_For` names the seventh link.
- **`reconcile-money`'s `no-money-without-a-fiche`** counts tagged note payments whose fiche is gone.

## The adjacent defect, fixed in the same change

⚠️ **Money taken on a note outside its fiche made that fiche un-re-savable.** `DentalRecordBillingGuard.Check`
compared the fiche's « Payé » with the note's whole `AmountCollected`, so after a desk « Encaisser » (the route
this dentist took) re-saving séance 1 unchanged was refused as « lowering » — and the refusal is *correctable*,
so the modal offered « Corriger la note », which **voids every payment on it** and re-raises a note at 100 DT:
the 60 DT silently un-received. Measured on production the same day: **7** fiches in that state.

The floor is now `min(stored « Payé », what the note holds for this fiche)` — the update command passes the
pre-edit figure; the automatic re-bill passes the saved one; the manual « Facturer » door keeps the old rule
(no « before »). Money tagged to another séance is excluded from the first séance's figure
(`Invoice.CollectedExcludingOtherRecords`), in the guard and in `TopUpAsync`.

## Found by the browser pass, fixed in the same change

⚠️ **The continuation's FIRST séance could not be re-saved while the treatment ran.** Its act is linked to the
devis (step 1), so `PlanCarriedActPricing` and the fiche's `markBilledOnPlan` both treated it as devis-carried
and priced it 0 — while its own note bills it at 160 — so the unchanged fiche was refused with « Le montant payé
(100,000 DT) dépasse le total de la séance (0,000 DT) ». Neither side now zeroes an act whose fiche is billed on
its own note **and** that note represents the devis (`billedOnANoteRepresentingThePlan` / `isInvoiced` +
`billedOnInvoiceNumber`). An ordinary devis séance with its own note (a mixed séance) is untouched.

## Not changed

- A payment voided at the desk and then an unchanged re-save of its fiche still re-collects the difference
  (`TopUpAsync`: `requested − collected`). Pre-existing, not reached by this report.
