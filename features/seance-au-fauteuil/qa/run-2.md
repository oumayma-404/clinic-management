# QA run 2 — Séance au fauteuil (owner's review, 2026-11-19)

**Date:** 2026-11-19
**Cycle:** 2 of at most 3 — after the owner's review of the demo
**What changed since run 1:** « À encaisser » left the agenda (no pill, no wallet mark) and became a tab of
« À clôturer »; a draft note is issued in place (« Émettre et encaisser ») instead of a link to `?tab=invoices`, a tab
that does not exist; the « émise immédiatement » warning left « Facturer cette intervention ».
**Plan:** features/seance-au-fauteuil/qa/plan.md — A6, B3, B6, C3, D1 rewritten for the new place; **D8** added (the
agenda is as it was, for both roles)
**Driver:** features/seance-au-fauteuil/qa/walk.mjs — `RUN=2`, then the whole walk again as `RUN=2b`
**Environment:** isolated — api :5096 (this worktree's Debug build, mine) · web :3096 `next dev` (mine) · postgres
`clinic-postgres`, database **`clinic_seance_demo`** (a copy of `clinic_management`; the shared database was not
touched) · MinIO bucket `clinic-seance-demo`
**Roles:** the dentist `qa.doctor@ibnkhaldoun.test`, the desk `qa.secretary@ibnkhaldoun.test`, Mode discret by the admin
**Peer sessions live during the run:** clinic-management-f7 (shell — owns :5099/:3099), anakin-9c (idle), anakin-89
(idle). None shares this database or these ports.
**Widths looked at:** 320 · 390 · 820 · 1180 · 1440 · 1536×730 (screenshots read: D1, C3, D8, B3; the tab at all five
widths in a separate pass)
**Status:** GREEN — run 2b 28/28; run 2's one red row (A8) triaged as a probe timeout, below

## Scenarios (run 2b)

| ID | Outcome | Evidence |
|----|---------|----------|
| A1 | ✅ | « ⋯ » → « Fiche de soins » = `?addRecord=1&appointmentId=<V1>` |
| A2 | ✅ | footer « Enregistrer » + « Terminer la séance — 40,000 DT »; « Payé » empty |
| A3 | ✅ | stays open, « Enregistré à 10:17 »; 1 fiche |
| A4 | ✅ | saved, PDF fetched, `print()` called once; still 1 fiche |
| A5 | ✅ | closed; visit `Completed`; no note |
| A6 | ✅ | « À clôturer » → « À encaisser »: « Fauteuil Un0916 10:06 · à facturer 40,000 DT · Encaisser » |
| A7 | ✅ | note 2026-0796 + cheque 7781234 BIAT; the tab's count 9 → 8 without a reload |
| A8 | ✅ | reopened « Payé » = 40,000 (the desk's money); re-save: still 1 live payment |
| B1 | ✅ | « Tout payé » = 40,000; note paid at the chair |
| B2 | ✅ | a second note refused (`seance_has_note`) |
| B3 | ✅ | « Émettre et encaisser » → note 2026-0798 issued **and** paid, row gone, still on « À clôturer »; shots/B3-issue-and-pay-2b.png |
| B4 | ✅ | correction refused for the desk |
| B5 | ✅ | old note cancelled; the new one carries the cheque |
| B6 | ✅ | Mode discret: `to-collect` 403, no « À encaisser » tab |
| B7 | ✅ | « échéance du devis n° 2026-0957 · 100,000 DT » → installment payment tagged with the fiche |
| C1 | ✅ | 320 and 390: no overflow; save + « Tout payé » in view |
| C2 | ✅ | 1536×730: the save button on screen without scrolling |
| C3 | ✅ | 390: « À encaisser » tab, no sideways scroll, the row's action in view; shots/C3-list-390-2b.png |
| C4 | ✅ | after four saves: one act, the corrected price |
| D1 | ✅ | « À clôturer », draft-only séance: « Émettre et encaisser » → note 2026-0800, payment offered, row answered; shots/D1-a-cloturer-2b.png |
| D2 | ✅ | « Remplir la fiche de soins » → the visit's fiche door |
| D3 | ✅ | RDV window: « Fiche de soins » + « À encaisser 10,000 DT · note n° … » |
| D4 | ✅ | preview → « Imprimer »: `print()` once |
| D5 | ✅ | note PDF « Imprimer »: `print()` once |
| D6 | ✅ | « Au fauteuil » opens the running visit's fiche |
| D7 | ✅ | a mid-séance fiche leaves the visit open; the minute job completes it after its end |
| D8 | ✅ | agenda as it was — desk and dentist: 0 pill, 0 « à encaisser » mark; shots/D8-agenda-2b.png |

## Findings

None in the product.

## Run 2's red row — a probe timeout, not a defect

| Row | Symptom | Why it is not the product |
|-----|---------|---------------------------|
| A8 | the fiche dialog did not appear within 30 s after `?editRecord=` | (1) the same link, same data, opened by hand right after: dialog in **2 s**, « Payé » = 40,000; (2) **B5 opened the same link** later in the same run and passed; (3) run 2b, the whole walk again: A8 ✅. The page that owns `?editRecord=` is unchanged by this branch's review edits. |

## Probe changes (not product changes)

- B3 and D1 used to assert the **link** (`?tab=invoices`) and were green while the desk landed on the wrong tab. They
  now assert the outcome: a numbered note, the payment dialog, the row gone.
- `check:responsive` **N59** now fails on any `/patients/{id}?tab=` value the patient page does not have — proved red
  on the two old links before the fix.

## Also run

- `npx tsc --noEmit` ✅ · `npm run check:responsive` 91/91 ✅ · `npm run build` ✅ (beside the running `next dev`: Next 16
  keeps dev output in `.next/dev`)
- `e2e/scripts/check-coverage.mjs`: exit 1 with the same 64 untested tier-0 rows as `main`; nothing new.
- No backend file changed in this cycle, so the unit suite was not re-run (7271 green at the feature commit).
