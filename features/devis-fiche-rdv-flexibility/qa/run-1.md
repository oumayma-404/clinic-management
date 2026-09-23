# Run 1 — wave 1 (2026-09-23)

**Environment:** API restarted by clinic-management-d9 on the wave-1 code (gen 24) · web `next dev` (owner bf, idle) ·
headless Chrome via playwright-core · peers live: anakin-35 (busy, other repo), all clinic sessions idle.
Fixtures: `arrange.mjs` on throwaway patients « QA? Flex 780727 ». F2 put in the pre-fix state (cancelled devis,
visit still linked) by one SQL update on that test devis.

| ID | Result | Evidence |
|----|--------|----------|
| A1 | ✅ | PUT 200 on a visit whose 1-act devis is Completed |
| A2 | ✅ | no « remettre au tarif » in the dialog (`shots/A1-visit-done-act.png`) |
| A3 | ✅ | PUT 200 on a visit still linked to a cancelled devis |
| A4 | ✅ | PUT 200; archived act still on the visit (SQL) |
| A5 | ✅ | devis act dropped when the patient is switched (`shots/A5-patient-switch.png`) |
| E1 | ✅ | implant visit saved; treatment count 6 → 6 |
| B1 | ✅ | re-save of the fiche on a Completed devis → 200, devis still Completed |
| B2 | ✅ | walk-in fiche re-saved twice → exactly 1 séance done |
| B3 | ✅ | fiche on a visit whose séance was removed → 200; visit kept on the act, step null |
| B6 | ✅ | « Séance » picker lists 3 pending séances; « Scellement » chosen → that step done (probe fixed, re-driven) |
| C1 | ✅ | « Aucun » → price back, save → act Planned, link null, devis reopened (`shots/C1-aucun.png`) |
| C3 | ✅ | hand-linked card shows « Aucun honoraire sur cette séance » |
| D1 | ✅ | cancel dialog names « Les rendez-vous prévus seront libérés »; visit Cancelled + unlinked |
| D2 | ✅ | delete dialog names it; visit Cancelled + unlinked |
| D3 | ✅ | (api) stop → parked act's visit Cancelled « Traitement arrêté », unlinked |
| D4 | ✅ | disregarded visit no longer read as a booking on the devis |
| C-320 | ✅ | fiche dialog inside 320 px (`shots/C-320-fiche-seance-picker.png`, eye-checked) |
| C-730 | ✅ | Enregistrer bottom at 653 px of 730 |
| R1 | ✅ | ordinary visit saves |
| R2 | ✅ | (api) create still books a live devis act |
| R3 | ✅ | (api) amend removing a booked act still cancels its visit |
| R4 | ✅ | booked devis act locked at 0 on the fiche; save marks it done |

Probe fix during the run: B6's first selector matched the inline catalogue's `role=option` rows behind the Select;
scoped to `[role=listbox]`. No source changed while the browser was open.

**Widths looked at:** 1440×900, 1536×730, 320×780.

**Status: GREEN**

## Eye pass (after the run)

Four changed surfaces (fiche + Séance picker, fiche after « Aucun », stop/cancel dialog, delete dialog) at 320×780 · 390×844 · 820×1024 · 1180×820 · 1440×900 · 1536×730 — 24 shots in `shots/eye/`; overflow measured on all 24 (none), 10 opened by eye covering every surface and every width band (320 · 390 · 820 · 1180 · 1440 · 1536×730). One fix: « Les rendez-vous prévus seront libérés » said plural for one visit → now « Le rendez-vous prévu sera libéré. » / « Les N rendez-vous prévus seront libérés. » (re-shot at 390).

## Observations (off-plan)

- The cancel dialog and the « Acte non terminé » helper line are long explanatory text — wave 4 (J) candidates under the no-explanation rule.
