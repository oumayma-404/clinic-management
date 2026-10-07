# QA run 1 — Fiche de soins « Recharger » reconciles instead of overwriting

**Date:** 2026-10-07 · **Plan:** [plan.md](plan.md) · **Driver:** [walk.mjs](walk.mjs)
**Environment:** shared stack, all UP and owned by peer `clinic-management-cd` (busy) — nothing restarted.
web :3000 = `next dev --turbopack` on this working tree (up since 2026-10-06 23:29), API :5000 unchanged by
the diff. Own headless Chrome (`playwright-core`, channel `chrome`), not the shared MCP profile. Peers live:
`clinic-management-cd` (busy), `clinic-management-33`, `anakin-e1`, `anakin-27`.
Two browser contexts = two session families of the QA account (A = the user, realtime blocked so its list is
stale; B = the colleague) + one API token for arranging.
**Test data created:** patients `QA Merge-muxuieot` (`928927ae…`, drive 1) and `QA Merge-muxuo7gk`
(`8882fdb1…`, drive 2), each with two fiches and one demande d'examens. Left in place (dev database).

## Scenarios (drive 2 — same file as drive 1, after two probe fixes)

| ID | Outcome | Evidence |
|----|---------|----------|
| A1 | ✅ | B saved 77 → A's stale save refused with « Recharger » → after it: banner gone, **no** notice, price 77,000, « note de A » kept → A saved |
| A1s | ✅ | SQL `Cost=77`, `Notes` holds « note initiale » and « note de A » |
| A2 | ✅ | B saved 88, A typed 55 → refused → notice « Modifié entre-temps par quelqu'un d'autre : « Actes »… », price 88,000 → A typed 66, saved — `shots/A2-notice-1536x730.png` |
| A2s | ✅ | SQL `Cost=66` |
| A3 | ✅ | B saved examen « Panoramique B »; A typed « Bilan A » → refused → notice names « Ordonnance »; section holds « Panoramique B », not « Bilan A » — `shots/A3-ordonnance.png` |
| B1 | ✅ | ordinary open: no notice; edit saved with no 409 |
| B2 | ✅ | A's list stale (no realtime); B saved 99; A opened from the row → price 99,000 after the open-time read, no notice; A's save went through with no 409; SQL `Cost=99` + A's note |
| C1 | ✅ | unchanged re-save: toast; `Cost` 66 and act count unchanged |
| C2 | ✅ | at 320×700 the notice wraps inside the sheet; dialog body horizontal overflow ≤ 0 — `shots/C2-notice-320.png` |
| D1 | ✅ | « Nouvelle fiche » (create path): act picked from the catalogue, saved; one more fiche in SQL |
| D2 | ✅ | devis-carried fiche `0ec0a407…` (no note of its own) opens with its « Inclus dans le traitement » tag; no notice |
| D3 | ✅ | fiche `b7033384…` with an ordonnance: « Prescription » summary names the médicament; no notice |

**Widths looked at:** 1440×900 (A1, A3, B1, B2, C1, D1–D3), 1536×730 (A2), 320×700 (C2). Screenshots read.

## Findings

None.

## Triage log (probe, not product — fixed in the script, re-driven in the same run)

| Drive-1 red | Cause |
|---|---|
| A2, A3 « notice names … » | the notice DID name the section; `quoteFr` binds « » with narrow no-break spaces and the probe's regex used plain spaces |
| D2 « tag missing » | the chosen fiche carries its **own** note d'honoraires (« Facturé — reste à payer 60,000 DT »), and a note that represents its devis shows no « Inclus » tag by design; re-pointed at a carried fiche with no note (`shots/D2-carried.png` now holds drive 2's capture) |

## Not exercised

| Area | Why |
|---|---|
| « Corriger la note » | numbered-note correction = money (verification.md § 7) |
| A colleague collecting money | creates a real note + payment; the payment section uses the same merge path as A1/A2 |

**Status: GREEN** (12 scenario rows, tiers A–D, 0 findings)
