# QA run 1 — Mode discret

**Date:** 2026-10-03
**Cycle:** 1 of at most 3
**Plan:** features/money-discreet-mode/qa/plan.md (26 rows: A9 B7 C5 D7 — `B7-pre` is a setup step, not a row)
**Driver:** features/money-discreet-mode/qa/walk.mjs (own headless Chrome via playwright-core, fresh sign-in; the shared MCP profile was held by peer `pvr-presence` and was not touched)
**Environment:** docker + api :5000 + web :3000 `next dev` — all three started by me (clinic-management-2c) after the IDE crash; migration `AddClinicMoneyHidden` applied at API start
**Peer sessions live during the run:** clinic-management-1b (busy — adding « Reste à payer », announced edits to `BillingController` and `MoneyMaskGateMiddlewareTests`; no restart of my tiers), clinic-management-1c, clinic-management-a5, pvr-presence (browser profile)
**Widths looked at:** 320×640 · 390×844 · 1440×900 · 1536×730
**Schema:** `verify-schema` before and after the migration: the same 5 pre-existing DRIFT lines, nothing new
**Status:** GREEN

## Scenarios

| ID | Outcome | Evidence |
|----|---------|----------|
| A0 | ✅ | first card « Mode discret », « Masquer l'argent » enabled; shots/A0-1440.png |
| A1 | ✅ | rail `/caisse` links 1 → 0 with no reload; toast « L'argent est masqué sur tous les postes. »; shots/A1-hidden-1440.png |
| A1b | ✅ | dashboard: « L'activité » present, no « L'argent »; shots/A1b-dashboard-hidden.png |
| A2 | ✅ | 13 reads → 403 `money_hidden` (incl. the 4 CSV exports) |
| A2b | ✅ | `GET invoices?patientId=…` → 200 |
| A3 | ✅ | `money: null`, `receivables: null`, `trend: []`, activity + alerts present |
| A4 | ✅ | correct code → toast; rail back live, after reload, and `IsMoneyHidden = f` in SQL; shots/A4-shown.png |
| A8 | ✅ | fresh `/caisse` while hidden: card, 0 money-shaped strings sampled every 50 ms, 0 requests to billing/expenses/invoices; shots/A8-caisse-hidden.png |
| A10 | ✅ | journal: 2 rows, both `salma.benyoussef@…`: `IsMoneyHidden: non → oui`, `IsMoneyHidden: oui → non` |
| B1 | ✅ | step-up `show-money` with the correct password → 400 « Code de vérification incorrect. », no token |
| B2 | ✅ | `money/show` without a token → 403, still hidden |
| B3 | ✅ | wrong code → banner « Code de vérification incorrect. », dialog open, still hidden; shots/B3-wrong-code.png |
| B4 | ✅ | session role intercepted as `doctor`: no card, read-only note shown |
| B5 | ✅ | `auth/totp` intercepted as not enrolled: button disabled, link « Sécurité » → `/securite`; shots/B5-1440.png |
| B6 | ✅ | `/factures` and `/cheques` render « Section masquée » |
| B7 | ✅ | a second page on `/caisse` swapped to « Section masquée » 5 ms after the hide returned (realtime) |
| C1 | ✅ | 320: scrollWidth 320/320, button inside the card; shots/C1-320.png |
| C2 | ✅ | 320×640: bottom sheet top 223 / bottom 640, code field 352–400, « Confirmer » bottom 564; shots/C2-320-sheet.png |
| C3 | ✅ | 1536×730: « Confirmer » at 523 of 730; shots/C3-1536x730.png |
| C4 | ✅ | 390: bottom bar + drawer have no Factures/Caisse/Chèques; shots/C4-390-drawer.png |
| C5 | ✅ | second `money/hide` → 200 `true`, and only ONE audit row for the pair (see A10) |
| D1 | ✅ | `/caisse` shown: figures (160,000 DT) |
| D2 | ✅ | `/factures` shown: « Total encaissé » with a DT figure |
| D3 | ✅ | `/cheques` shown: renders, no card |
| D4 | ✅ | patient « Factures » tab while hidden: figures, no 403; shots/D4-patient-factures-hidden.png |
| D5 | ✅ | `/login` signed out: no `clinics/user-status` request |
| D6 | ✅ | « Informations du cabinet » still renders under the new card |
| D7 | ✅ | step-up for another action with a password → 200 with a token |

## Findings

None.

## Observations (off-plan)

- **A4 missed once, then passed three times.** On the first drive the correct code produced no toast and the DB
  stayed hidden; no wire log existed yet. A standalone probe and two later full drives (with request/response
  logging) all succeeded on the first press. Not reproduced, not filed. If it recurs, the wire log in `walk.mjs`
  A4 will say whether the step-up or the show refused.
- The « Mode discret » card is ~120 px tall at 1440 for one line of content — same `Card` + `CardContent p-4` shape
  as the « Rappels » card on the same page, so consistent, not chased.

## Probe bugs fixed during the run (not product defects)

| Row | Symptom | Real cause |
|-----|---------|------------|
| all | `EPERM mkdir …%20…` | built the shots path from a URL pathname; switched to `fileURLToPath` |
| C2 | sheet « below the viewport » (top 640) | measured before the slide-in started; now waits for the enter animation to finish |
| login | one drive timed out on the TOTP step | not reproduced; added a screenshot + page text on failure |

## State after the run

`IsMoneyHidden = false` for the QA clinic (verified in SQL). Two diagnostic probes mutated it between drives; the one
hide they left was reverted with SQL (`update "Clinics" set "IsMoneyHidden"=false`) — my own artefact, said here.
