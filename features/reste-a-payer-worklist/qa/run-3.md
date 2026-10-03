# QA run 3 — Reste à payer worklist

**Date:** 2026-10-03
**Cycle:** 3 of at most 3
**Plan:** features/reste-a-payer-worklist/qa/plan.md (20 scenarios + 8 layout probes)
**Driver:** features/reste-a-payer-worklist/qa/walk.mjs
**Environment:** api :5000 (mine, PID 40936) · web :3000 `next dev` (owner clinic-management-2c, hot reload) · postgres clinic-postgres
**Peer sessions live during the run:** clinic-management-1c, clinic-management-2c, clinic-management-a5
**Widths looked at:** 320×730 · 390×844 · 820×1024 · 1440×900 · 1536×730 (screenshots read: A2, C1, C2, C3, C4, C5, B4)
**Status:** GREEN (28 ✅, 1 ⏭)

## Scenarios

| ID | Outcome | Evidence |
|----|---------|----------|
| A1 | ✅ | 4 tabs, badge 421 = `dueCount` |
| A2 (+tabs, sort) | ✅ | totals match the API; four tabs inside the strip; sort on one line |
| A3 | ✅ | reason, age, amount, fiche link, cross amount |
| A4 | ✅ | « N acte(s) fait(s) sur M » + next step |
| A5 | ✅ | `tel:+216…`, `https://wa.me/216…`, `_blank`, no pre-filled text |
| A6 (+geometry) | ✅ | panel 864–1440, nothing outside; documents, acts, Total/Payé/reste, Encaisser |
| A7 | ✅ | partial payment: panel, row and card updated with no reload |
| A8 / A9 / D2 | ✅ | api rows (run 1): 0 mismatches on 131 patients; sort, paging, search; line fields present |
| B1 | ✅ | filtered empty state, « Effacer la recherche » restores 25 rows |
| B2 | ✅ | no-phone patient: no call / WhatsApp |
| B3 | ✅ | over-payment refused, dialog open |
| B4 | ✅ | money hidden → 3 tabs |
| B5 | ⏭ | no secretary credentials on the dev database |
| C1 (+cards, tabs) | ✅ | 320: no horizontal scroll, labels on one line, tabs wrap |
| C2 (+cards, sort, tabs) | ✅ | 820: cards, no overlap, four tabs on two rows (shots/C2-820.png) |
| C3 | ✅ | 390: panel full width, Encaisser reachable |
| C4 (+geometry) | ✅ | 1536×730: panel 960–1536, own scroller |
| C5 | ✅ | split devis: « À régler maintenant » + both parts on the row |
| D1 | ✅ | patient file « Encaisser » still opens the payment dialog prefilled |
| D3 | ✅ | the other three tabs render |

## Fixes applied (cycle 2)

| Finding | Verdict | Root cause | Files | Blast radius |
|---------|---------|------------|-------|--------------|
| F-3 | confirmed (regression, row #8) | `sm:flex-nowrap` turned the strip's wrap off from 640 px; three tabs fit, four do not | `web/app/a-cloturer/page.tsx:260` | page-only; 1440 unchanged (one row), 820 and below wrap |

**Probe added:** `tabStripFits` at 320 / 820 / 1440 — the strip's own overflow, not each tab's label.
**Gates after the last edit:** check:responsive 72/72 ✅ · tsc 0 ✅ (backend untouched since the 4877-test run)

## Test data

Patient « QA RestePaiement » + note 2026-0782 created through the product; after the run its 7 test payments were
voided and the note cancelled through the product (`revert.mjs`). The patient record remains.
