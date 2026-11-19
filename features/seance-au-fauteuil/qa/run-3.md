# QA run 3 — Séance au fauteuil (« the fiche makes the note », 2026-11-19)

**Date:** 2026-11-19
**Cycle:** 3 of at most 3
**What changed since run 2 (owner's decision):** « Émettre et encaisser » is gone. A séance whose note is still a draft
is billed from its **fiche**, and the server deletes the draft in the same transaction
(`BillDentalRecordCommand.ReplacesDraftInvoiceId`, `?replacesDraft=`). « Facturer cette consultation » left the RDV
window. « Encaisser » is the one word at the desk. The refusal a draft causes now points to « À encaisser ».
**Plan:** features/seance-au-fauteuil/qa/plan.md — B3 and D1 rewritten (V3's draft is typed 5 DT under the fiche so the
walk can tell whose amount is billed); **D9** added (the RDV window raises no draft)
**Driver:** features/seance-au-fauteuil/qa/walk.mjs (`RUN=3`)
**Environment:** isolated — api :5096 (this worktree's Debug build with the new command, mine) · web :3096 `next dev`
(mine) · database **`clinic_seance_demo`** (re-copied from `clinic_management` today; the shared database was only read)
**Peer sessions live:** clinic-management-f7 (shell — owns :5099/:3099), anakin-9c (idle), anakin-89 (idle). None
shares this database or these ports.
**Status:** GREEN — 29/29

## Scenarios that changed

| ID | Outcome | Evidence |
|----|---------|----------|
| B3 | ✅ | « Fauteuil Trois1001 · à facturer 40,000 DT · Encaisser » → the dialog named the 35,000 DT draft → note 2026-0792 = the fiche's 40,000 DT, paid; the draft row deleted; the séance left the list |
| D1 | ✅ | « À clôturer », draft-only séance: « Encaisser » → note 2026-0794 from the fiche, the draft replaced, the row answered |
| D9 | ✅ | RDV window of a séance with no fiche: « Non facturé », no « Facturer cette consultation » |
| B2 | ✅ | billing without naming the draft is still refused (`seance_has_note`) |

Every other row (A1–A8, B1, B4–B7, C1–C4, D2–D8) ✅, unchanged from run 2b.

## Wire checks against the live server (`scratchpad` `wire-draft.mjs`, own patient)

| ID | Outcome | What |
|----|---------|------|
| W1 | ✅ | « À encaisser » row: `DraftNote`, `amountDue` 90 (the fiche), `draftTotal` 60 |
| W2 | ✅ | the dentist's re-save with « Payé » (the silent path): billing `Refused`, the draft still there |
| W3 | ✅ | billing without naming the draft: refused `seance_has_note` — « Un brouillon de note existe pour cette séance : rien n'est encaissé ici. Encaissez depuis « À clôturer » → « À encaisser », la note y est faite depuis la fiche. » |
| W4 | ✅ | a wrong draft id: refused `dental_record_draft_not_replaceable`, nothing written |
| W5 | ✅ | naming the draft: one note, 90 DT (the fiche), paid; the draft 404 |
| W6 | ✅ | the séance left « À encaisser » |

## Findings

None.

## Also run

- Unit suite, unfiltered: **7280 passed**, 0 failed (9 new: replace on confirm; refused on the silent path, on a wrong
  id, on an issued draft, beside an issued visit note, when a devis carries it, for another séance; a failed save keeps
  the draft; a draft naming the fiche is replaced too).
- `npx tsc --noEmit` ✅ · `npm run check:responsive` 91/91 ✅ · `npm run build` ✅
- e2e: **SEAT-13** added (the wire half of this rule); `check-coverage.mjs` — the same 64 untested tier-0 rows as `main`,
  nothing new. The e2e folder has no local TypeScript install; SEAT-13 runs in CI's `e2e` job.
