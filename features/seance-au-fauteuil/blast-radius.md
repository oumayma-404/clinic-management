# Blast radius — séance au fauteuil

`regression-safety.md` § 1. Verdicts: `unaffected` · `must change` (in this change) · `must re-test` (tier D).

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | Fiche create/update → `FicheVisitCompletion` | when a fiche ends its visit | post-visit popup, « À clôturer », « Au fauteuil », status chart | must re-test (D2, D6) |
| 2 | `AppointmentProgressJob` end pass | fiche-bearing visit → Terminé | agenda statuses, closure worklist, taux d'absence | must re-test (D7) |
| 3 | « Payé » mirror removed | client default | e2e money specs, `components/CLAUDE.md`, fiche-merge paiement section | must change — docs done; no e2e spec relied on it (grep) |
| 4 | `DentalRecordDto` + `CollectedOnNote`, versions | additive fields | modal, patient page, fiche-merge, e2e | must re-test (A8) — « Payé » now hydrates from it |
| 5 | `finishSeance` payload key | new save field | N49 | must change — classified `derived` |
| 6 | `VisitClosureReader` issued-only billing term | shared read | worklist, dashboard chip | must re-test (D1) |
| 7 | `InvoiceLinkChoice.ByKey` | shared rule, 5 readers | agenda badge, continuation ×2, unfinished acts, plan projection | unaffected — the rule itself is unchanged; only `VisitClosureReader` now reads the number |
| 8 | `BillDentalRecordCommand` (`seance_has_note`, carry) | the one fiche→note writer | « Facturer cette intervention », auto-billing, « À clôturer » Encaisser | must re-test (A7, B1, B2, B5) |
| 9 | `UpdateDentalRecordCommand` correction | money write | caisse ledger, « Chèques », reconcile-money | must re-test (B5) |
| 10 | `RecordInstallmentPaymentCommand.DentalRecordId` | additive field | devis workspace, « Reste à payer », `CollectOnTreatment` idempotency | must re-test (B7) |
| 11 | `BillDentalRecordDialog` cheque fields | shared dialog | « À clôturer », patient page, resolver | must re-test (A7) |
| 12 | Preview / invoice PDF print → `printPdfBlob` | shared print path | séance row, Documents tab, fiche, /factures | must re-test (A4, D4, D5) |
| 13 | `appointment-calendar.tsx`, `edit-appointment-dialog.tsx` | shared agenda UI | PR #116 (cabinet de groupe) edits the same files | must re-test (A1, D3); coordinate the merge |
| 14 | `ficheHref` at 9 call sites | one composer | every « open the fiche » door | must re-test (A1, D2, D6) — N57 holds it |
| 15 | `InstallmentPaymentModal` `dentalRecordId` prop | shared dialog | devis workspace, « Reste à payer » | unaffected — optional prop, absent keeps the body unchanged |

## Owner's review, 2026-11-19 — « À encaisser » moves, the fiche makes the note

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 16 | `app/appointments/page.tsx`, `appointment-calendar.tsx` | agenda | back to `main` byte for byte | must re-test (D8) — and PR #116 no longer overlaps on the calendar |
| 17 | `ToCollectList` (was `ToCollectButton`), `useSeancesToCollect` (+`failed`) | the desk's list and its read | « À clôturer » only now | must re-test (A6, A7, B3, B7, C3) |
| 18 | `BillDentalRecordCommand.ReplacesDraftInvoiceId` + `ITreatmentPlanRepository` dependency | money write, one transaction | the from-dental-record endpoint (desk, patient page « Facturer »), the fiche's auto-billing (never sets it) | must change: the two test classes that build the handler; must re-test B2, B3, D1, W1–W6 |
| 19 | `FindVisitOnlyNoteAsync` → `FindVisitOnlyNotesAsync` | « one note per séance » | that command only | unaffected — same refusal, now aware of a draft beside an issued note |
| 20 | `SeanceToCollectDto.DraftTotal`; DraftNote `AmountDue` = the fiche's | wire field + meaning | `use-seance-collection`, `to-collect-list`, RDV window, SEAT-03/09 | must change: the query test; must re-test B3, D3 |
| 21 | `BillDentalRecordDialog.replacesDraft` | shared dialog | « À clôturer », « À encaisser », RDV window, patient page | unaffected where absent — optional prop, the request is unchanged without it |
| 22 | RDV window « Facturer cette consultation » removed | a door that raised drafts | `InvoiceFormModal` (still used by `invoices-table`) | must re-test D3, D9 |
| 23 | `?tab=` deep links to the patient page | mirrored set | every `/patients/{id}?tab=` link | N59 derives the set from `PATIENT_TABS` |
