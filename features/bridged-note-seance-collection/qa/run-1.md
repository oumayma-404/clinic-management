# Run 1 — bridged note séance collection

Environment: API restarted by this session with `AddPaymentDentalRecordId` applied; `next dev` restarted
(the previous one was 2 days old). Peers live: none on clinic-management (d6, a8, 29, dc idle). Fresh
throwaway fixtures (`arrange.mjs`) per launch. Three launches: #1 lost to a cold compile of `/patients/[id]`,
#2 and #3 are probe fixes (navigation retry, « ⋯ » menu for delete, save-response capture) — same run.

Widths looked at: 1440×900 (A1, A2 screenshots), 390×844 (C1).

| id | outcome | evidence |
|---|---|---|
| A1 | ✅ | « Prix du traitement 160,000 · Déjà payé 100,000 · Reste à payer 60,000 », no séance « Payé » — `shots/A1-1440.png` |
| A2 | ✅ / ⏭ | save 200, `treatmentCollection = {Collected, noteNumber 2026-0772, amountCollected 60, outstanding 0}`; UI after typing: « Reste à payer 0,000 » — `shots/last-save-93498.png`. ⏭ the toast: no toast was captured on the create door in 3 launches (captured fine on the update door, A4) — not separable from the probe in this run |
| A3 | ✅ | SQL: note Status 3 (Paid), 160,000; one live payment 60,000 tagged with séance 2's fiche; échéancier AmountPaid 0 |
| A4 | ✅ | reopened field reads « 60,000 »; re-save « Fiche de soins mise à jour »; still exactly one tagged payment |
| A5 | ❌ | see F1 |
| B1 | ✅ | 70 typed → « Il ne reste que 60,000 DT », save disabled |
| C1 | ✅ | 390 px: money row [37,353] inside dialog [0,390], page width 390 — `shots/C1-390.png` |
| D1 | ✅ | ordinary devis: « Prix 300,000 · Déjà payé 100,000 · Reste à payer 200,000 », no note mentioned |
| D2 | ✅ | « Ajouter au devis » absent on the bridged séance |
| D3 | ✅ | preview: « 60,000 DT encaissés à cette séance sur la note n° … (la note est conservée) » — `shots/D3-preview.png` |

## Findings

### F1 — major — séance 1's own fiche cannot be re-saved while the treatment runs

- **Repro:** fixture `main` (continuation of a billed séance). Patient page → « Modifier la fiche du 26/09 » →
  Enregistrer, nothing changed.
- **Expected:** saved (the fiche is billed on its own note at 160, 100 paid).
- **Actual:** refused — « Le montant payé (100,000 DT) dépasse le total de la séance (0,000 DT). Corrigez le
  montant, ou ajoutez l'acte qui manque. » (HTTP 400). The act is priced **0** as if the devis carried it,
  while the note 2026-07xx bills it at 160.
- **Pre-existing**: independent of this change (the act's 0 comes from the devis link on the reopened fiche,
  and no branch touched here sets it). Reached by the same continuation shape as the complaint.
- **Suspected owner:** `web/components/patient-record-modal.tsx:1251` (`markBilledOnPlan` on a reopened fiche
  whose own note represents the devis) and server `PlanCarriedActPricing` (zeroes the act on the fiche path).

## Observations (off-plan)

- 3 console 401s per launch — the sign-in page's first token probes, before login. Not investigated.

**Status: RED (1 finding)**

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files | Blast radius |
|---|---|---|---|---|
| F1 | confirmed (pre-existing) | a fiche billed on its **own** note that represents the devis still had its act treated as devis-carried → priced 0 on both sides | `PlanCarriedActPricing.cs` (new `billedOnANoteRepresentingThePlan`), `UpdateDentalRecordCommand.cs`, `patient-record-modal.tsx` (skip `markBilledOnPlan` when `isInvoiced` + bridge note) | create path unaffected (no note yet); mixed séance with an ordinary devis unaffected (gate needs the bridge note) |

**Gates after the last edit:** check:responsive ✅ (72) · tsc ✅ · build ✅ · dotnet test 4 855 ✅ ·
verify-schema: `Payments(DentalRecordId)` present, 6 pre-existing DRIFT lines none from these fixtures ·
reconcile-money: new counter 0.
**Re-run:** run-2.md — GREEN
