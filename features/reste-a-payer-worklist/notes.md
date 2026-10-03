# Reste à payer — what shipped

A 4th tab in « À clôturer » listing every patient who owes, split into **À relancer** (due now) and **En cours**
(not due yet), with a patient panel, « Appeler » / « WhatsApp », and the existing « Encaisser » windows.

## Decisions that are easy to undo by accident

- **The server classifies every amount, per document, in `PatientDebtLines.Project`.** `DueNow` is a whole
  unpaid note (payable on issue) or, for a devis, the outstanding of the échéances `InstallmentLateness` calls
  late — capped at the devis's own reste so `due + running == Outstanding` even when the échéancier has drifted.
  An auto-raised lump-sum échéance is late only once the work is finished, which is what puts a finished or
  stopped treatment in « À relancer » and an ongoing one in « En cours ». No browser code sums money.
- **Only the LATE part of a devis is « À relancer »** (owner's call, 2026-10-03). A devis with 400 late and 400
  not yet due shows in both lists, each with its own part.
- **One selection rule, two readers.** `PatientDebtSelection.Select` (live notes + debt-bearing, non-bridged
  devis) was extracted from « Solde patient »; the worklist calls the same two functions per patient, so a row
  equals that patient's « Solde dû » by construction, not by two reads agreeing.
- **Candidates come from « Créances »' two light reads**, then only those patients' documents are loaded
  (`GetByPatientIdsAsync` ×2). A patient whose plan outstanding is positive while its échéancier sums to 0 (the
  `plan-schedule-balances` drift) is missed — the same blind spot « Créances » has.
- **Open to every role**, unlike « Créances » (`AdminOrDoctor`): reception chases and collects this money.
  The Créances screen and its CSV stay as they were.
- **Hidden under « Mode discret »**: `[HiddenWhenMoneyMasked]` on the endpoint, and the tab is withheld while
  `useMoneyVisibility().moneyShown` is false.
- **« Encaisser » is the patient file's own path**, moved into `useDebtLineCollection` so the two screens share
  one re-read-then-open implementation. Partial payments are what those dialogs already allowed.
- **No pre-written WhatsApp message**; no call or WhatsApp button without a stored dialable number.
