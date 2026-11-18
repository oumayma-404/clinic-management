# Séance au fauteuil — what shipped

A dentist, after a demo: « l'ordonnance s'écrit pendant la séance, et je veux l'imprimer sur place », « je remplis la
fiche au fur et à mesure », « je saisis les actes, c'est la secrétaire qui encaisse ». The owner picked **option A —
la fiche vivante** and **« Payé » starts empty + « Tout payé »** (2026-11-18). The design is `blueprint.md`.

## What a user sees

| Where | What |
|---|---|
| Agenda « ⋯ », RDV window header, « Au fauteuil » card, patient page | **« Fiche de soins »** opens the séance's fiche at any moment of the day, not only after the slot |
| Fiche footer, séance still running | **« Enregistrer »** saves and stays open (« Enregistré à 14:32 »); **« Terminer la séance — 120,000 DT »** saves, closes and marks the visit « Terminé » |
| Fiche footer, séance over | One button, « Enregistrer la séance — X » (unchanged) |
| Fiche « Prescription » | **« Imprimer l'ordonnance »** / **« Imprimer la demande d'examens »**: saves the fiche, then prints the real PDF. The save toast also offers « Imprimer » |
| Fiche « Payé » | Starts **empty** on a new fiche; **« Tout payé »** fills the total. Reopened, it shows what the séance's note holds, **desk payments included** |
| Agenda | **« À encaisser · N »** (amber) above the agenda when today has séances to collect; a wallet mark on those blocks. Hidden under « Mode discret » |
| « À encaisser » list | Time · patient · what (« à facturer », « reste sur la note n° … », « note en brouillon, à émettre », « échéance du devis n° … ») · amount · **« Encaisser »** |
| RDV window | « À encaisser · 120,000 DT · [Encaisser] » for a séance with a saved fiche and money still due |
| « Facturer cette intervention » | Cheque fields (n°, banque, date) when the method is Chèque |
| « À clôturer » | A séance whose only note is a **draft** now asks for its money: « Émettre la note » opens the patient's Factures |

## The rules that are easy to undo by accident

### Saving a fiche does not end the visit
`FicheVisitCompletion.ShouldComplete` = « Terminer la séance » **or** the slot has ended (`Appointment.HasEndedBy`,
retard included). Create and update both ask it. A save in the middle of a running séance leaves the visit
« En cours » — it used to become « Terminé » on the first save, so the agenda said the patient had left while they
were still in the chair. The post-visit review is withdrawn on create (the fiche is being written) and on any save
that completes.

### The minute job completes a visit that has a fiche
`AppointmentProgressJob`'s end pass: a patient visit whose slot ended **and that has a fiche** becomes « Terminé »;
without one it becomes « Séance passée » as before. The fiche is the evidence the patient came. One batched fiche
read per clinic per pass.

### « Payé » is never the total by default
The mirror (« Payé » copied from the total until edited) is gone. It recorded money nobody took whenever the dentist
did not touch the field, and the secretary then collected it a second time. Empty means « nothing taken at the
chair »; the auto-billing raises no note for it.

### The desk's money is read back, never written onto the fiche
`DentalRecordDto.CollectedOnNote` = `Invoice.CollectedExcludingOtherRecords(fiche)` on the issued note whose lines
bill the fiche — the billing guard's own figure. `fiche-merge.ts` hydrates « Payé » from it, and the modal's
« déjà payé » floor is the guard's (`min(stored « Payé », note)`), so « Corriger la note » appears exactly when the
server would refuse.

### One note per séance
`BillDentalRecordCommand` refuses with `seance_has_note` when the visit already holds a live note that names **no
fiche** (the RDV window's « Facturer cette consultation »). A note bound to another fiche of the same visit does not
block: a visit may carry several fiches. The auto-billing reports it as a refusal, not a failure.

### A draft note does not settle « À clôturer »
`VisitClosureReader` counts **issued** notes only, through the visit link (`InvoiceLinkChoice` prefers the numbered
note, so a null number means a draft) and through the fiche link. The four other `InvoiceLinkChoice.ByKey` readers
are unchanged on purpose: they name the document, and naming a draft is right there.

### « Corriger la note » carries the desk's payments
`NoteCorrectionCarry.Plan` (pre-commit, in `UpdateDentalRecordCommand`) lists the retired note's live rows in the
order they came, each clamped to what is left of the corrected « Payé ». `BillDentalRecordCommand` re-records them on
the replacement with their own date, method, cheque and bank mark; only the rest of « Payé » is new money. So a desk
cheque stays the same cheque and every day of la caisse nets to zero. « Payé » set to 0 carries nothing (« le
patient n'a pas payé »). Refused before anything is written:
- a **deposited cheque** the carry would not move in full (`PaymentBankedCode`);
- a note **attached to a devis** or holding **another séance's** money (`dental_record_note_carries_treatment`) — the
  replacement would be attached to neither.

The same order and clamp as the note-level « Corriger » (`IssueInvoiceCommand`), with « Payé » as the ceiling.

### A secretary cannot correct a note from the fiche
`correction_requires_doctor`: a correction voids payments, which the note's own « Corriger » keeps for doctor/admin.

### A devis séance is collected on its échéance, tagged with the fiche
« À encaisser » lists a devis séance only when its devis has money **due now** (`PatientDebtLine.DueNow`, « Reste à
payer »'s « À relancer »), one row per devis per day. « Encaisser » opens the échéance with
`RecordInstallmentPaymentCommand.DentalRecordId` set, so `TreatmentPlan.CollectedOnRecord` counts it and the
dentist's re-save of « Encaissé sur le traitement » adds only the difference.

### One composer, one print path
`lib/visits/fiche-door.ts` builds every « open the fiche » link (`check:responsive` N57); `lib/print.ts`'s
`printPdfBlob` is the only way a PDF reaches a printer (N58). On a tablet it hands the PDF to the system viewer, so
the toast is worded from its return value.

## Decided against

- **A draft fiche state**: no print before the end, and every fiche reader would have to learn to hide drafts.
- **A per-cabinet switch**: every flow twice, and the money traps stay live in the default mode.
- **A separate ordonnance button**: two writers of one legal paper.
- **Printing the aperçu unsaved**: a prescription with no record behind it.
- **Autosave**: silent saves would hit the billing guards and 409s in the background.
- **A bell row per séance for reception**: the live pill already says it, without a second list to clear.

## Left as it was, on purpose

- `CreateMedicalDocumentCommand` still completes a visit when a document is created for it: a document written
  during the séance is rare, and changing it would leave a document-only visit asking « venu ? ».
- `DentalRecordPostVisitCompletionTests.cs.deferred` stays parked; `FicheVisitCompletionTests` covers the rule.
