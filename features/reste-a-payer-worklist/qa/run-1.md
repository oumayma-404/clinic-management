# QA run 1 — Reste à payer worklist

**Date:** 2026-10-03
**Cycle:** 1 of at most 3
**Plan:** features/reste-a-payer-worklist/qa/plan.md (20 scenarios: A9 B5 C5 D3)
**Driver:** features/reste-a-payer-worklist/qa/walk.mjs (+ scratchpad `api-checks.mjs` for the api rows)
**Environment:** api :5000 Release, restarted 18:15 by me (PID 40936, announced to 2c) · web :3000 `next dev` (owner clinic-management-2c, started 17:47) · postgres clinic-postgres
**Peer sessions live during the run:** clinic-management-1c, clinic-management-2c (idle from 18:14), clinic-management-a5
**Widths looked at:** 320×730 · 390×844 · 820×1024 · 1440×900 · 1536×730
**Status:** RED (2 findings: 1 major, 1 minor)

## Scenarios

| ID | Outcome | Evidence |
|----|---------|----------|
| A1 | ✅ | 4 tabs, badge 421 = API `dueCount` 421 |
| A2 | ✅ | « À relancer 68 425,700 DT 421 patients » / « En cours 163 345,000 DT 520 patients »; shots/A2-1440.png |
| A3 | ✅ | row 0 « Amine Trabelsi · Note du 14/08 non soldée · il y a 50 jours · 5,200 DT + 50,000 DT en cours », name links to fiche |
| A4 | ✅ | « 0 acte fait sur 1 · Séance le 4 oct. 2026 »; shots/A4-1440.png |
| A5 | ✅ | `tel:+21698123456` · `https://wa.me/21698123456` · `_blank` |
| A6 | ✅ | panel 864–1440, nothing outside; note 2026-0782 with its act, Total/Payé/reste, Encaisser |
| A7 | ✅ | partial payments (200 then 25) on the QA note: panel, row and card all updated with no reload |
| A8 | ✅ | api: 131 patients sampled, 0 mismatches row vs « Solde dû »; card totals = Σ rows |
| A9 | ✅ | api: amount desc, age asc, no duplicate across pages, totals not page-scoped, search narrows |
| B1 | ✅ | « Aucun patient ne correspond à « zzzznope » » + « Effacer la recherche » |
| B2 | ✅ | QA patient (no phone): no call / WhatsApp control |
| B3 | ✅ | 999 refused « Le paiement dépasse le reste dû (…) », dialog open |
| B4 | ✅ | `isMoneyHidden` intercepted → 3 tabs, no « Reste à payer » |
| B5 | ⏭ | no secretary credentials on the dev database |
| C1 | ❌ F-1 | no horizontal scroll, but the list cards' labels wrap word by word; shots/C1-320.png |
| C2 | ❌ F-1 / F-2 | cards' hint text runs under the amount; sort « Plus ancien » on two lines; shots/C2-820.png |
| C3 | ✅ | panel 390 px wide, Encaisser reachable; shots/C3-390.png |
| C4 | ✅ | panel 960–1536, 730 tall, own scroller, first document at y=195 |
| C5 | ✅ | split devis 2026-0098: « À régler maintenant 100,000 » under « À relancer », both parts on the row |
| D1 | ✅ | patient file Encaisser opens prefilled with the note's reste; Annuler closes |
| D2 | ✅ | api: every line carries `dueNow`/`acts`; `dueNow` never exceeds `outstanding` |
| D3 | ✅ | Séances / À compléter / Suites still render |

## Findings

### F-1 · major · the two list cards are cramped below ~450 px of card width

- **Repro:** `/a-cloturer` → « Reste à payer » at 820 (two cards side by side) or 320.
- **Expected:** label on one line, hint readable, amount clear of the text.
- **Actual:** « À relancer » / « En cours » break onto two lines, the hint stacks one word per line, and at 820 the hint text runs under « 163 345,000 DT ».
- **Evidence:** shots/C2-820.png, shots/C1-320.png; label line counts [2,2] at both widths.
- **Suspected owner:** `web/components/visits/reste-a-payer-list.tsx` `ListCard` — three columns at every width; the middle `minmax(0,1fr)` collapses beside the amount.
- **Regression?** No — new.

### F-2 · minor · the sort control wraps « Plus ancien »

- **Repro:** same tab at 1440 or 820.
- **Expected:** « Plus ancien » on one line.
- **Actual:** two lines inside a 36 px pill.
- **Evidence:** shots/A2-1440.png, shots/C2-820.png.
- **Suspected owner:** `reste-a-payer-list.tsx` — `ModeSegmented` given `sm:w-auto`, so its `flex-1` options shrink to min-content.
- **Regression?** No — new.

## Observations (off-plan)

- A tab badge starts at « 0 » until its list has loaded — the page's other badges do the same.

## Probe bugs fixed during the run (not product defects)

| Row | Symptom | Real cause |
|-----|---------|------------|
| — | script would not start | `URL.pathname` kept `%20`; switched to `fileURLToPath` |
| A1 | badge « 0 » | read before the list loaded; now waits for a non-zero count |
| A4 | header « not found » | CSS `uppercase` changes `innerText`; compared case-insensitively |
| B2, A7 | wrong row / wrong card total | search debounce not awaited; the card is search-scoped by design (AC-4) |
| A6, C4 | panel content cut at the right | screenshot taken mid slide-in; measured after it settles — 864–1440, nothing outside |
| all | empty JSON | the API access token expired; re-issued |
