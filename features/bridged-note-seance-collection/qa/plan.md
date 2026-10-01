# QA plan — a later séance collects on the note that represents the devis

Fixtures: `arrange.mjs` (throwaway patients « QABridge … », over the API). Driver: `walk.mjs`, one launch.

## Blast radius (regression-safety § 1)

| # | Touching | Other consumers | Verdict |
|---|---|---|---|
| 1 | `Payment` + `Invoice.RecordPayment(dentalRecordId)` | 5 RecordPayment callers | unaffected — optional last param; correction carry copies it |
| 2 | `CollectOnTreatmentCommand` (bridged branch) | fiche create/update post-commit | must re-test — ordinary devis séance still hits the échéancier (D1) |
| 3 | `DentalRecordBillingGuard.Check` floor | update pre-commit, BillDentalRecord TopUp | must re-test — séance 1 re-save (A5), lowering still refused (unit) |
| 4 | `GetCollectedByDentalRecordAsync` | patient page history, fiche hydration | must re-test — séance 2 reopens with 60 (A4) |
| 5 | `DentalRecordDeletionReversal` | delete + preview | must re-test — preview lists the note collection (D3) |
| 6 | `patient-record-modal` `collectsOnTreatment` | « Ajouter au devis », séance « Payé » withhold | must re-test — D2 |
| 7 | `reconcile-money` orphan check | verb only | unaffected — unit-tested |

## Scenarios

| id | tier | layer | steps | expected |
|---|---|---|---|---|
| A1 | A | browser | open fiche from séance 2 (bridged) at 1440 | a « Payé » field `#collected-on-plan` is shown; Prix 160,000 · Déjà payé 100,000 · Reste à payer 60,000; no séance « Payé (séance) » |
| A2 | A | browser | type 60, save | save enabled; toast names « note n° » of séance 1's note; no refusal toast |
| A3 | A | sql | read the note + payment | note Status Paid, AmountCollected 160; one payment 60 tagged with séance 2's fiche id; échéancier AmountPaid 0 |
| A4 | A | browser | reopen séance 2's fiche, save unchanged | field reads 60; save gives no warning; SQL: still exactly one tagged payment |
| A5 | A | browser | reopen séance 1's fiche (the note's own), save unchanged | no refusal (« ne se diminue pas » / « Corriger la note » absent); fiche saved |
| B1 | B | browser | fresh bridged fixture #2, séance 2 fiche, type 70 | « Il ne reste que 60,000 DT à payer » shown; save disabled |
| C1 | C | browser | A1 at 390×844 | money row within the dialog (no horizontal overflow), three figures visible |
| D1 | D | browser | ordinary devis (no note), séance 2 fiche | label « Payé aujourd'hui »/« Payé (traitement) »; Reste = devis outstanding (200) |
| D2 | D | browser | bridged séance 2 fiche | « Ajouter au devis » absent |
| D3 | D | browser | bridged: open delete preview of séance 2 fiche after A2, then cancel | lists « 60,000 DT encaissés à cette séance sur la note n° … (la note est conservée) » |
