# QA run 1 — Continuation's first séance: price kept, « le patient n'a pas payé » correctable

**Date:** 2026-11-07
**Cycle:** 1 of at most 3
**Plan:** features/continuation-first-fiche-money/qa/plan.md (17 scenarios: A6 B3 C3 D5)
**Driver:** features/continuation-first-fiche-money/qa/walk.mjs (log: scratchpad `qa/walk-run1.log`, `qa/walk-run1-B2.log`)
**Environment:** ISOLATED — api :5093 (started 17:32, owned by me, branch 831ec26c) · web :3093 `next dev` (started by me) ·
database `clinic_qa_cff` on clinic-postgres (a copy of the local `clinic_pvl_qa`, migrated to main) · headless Chrome of
my own (not the MCP profile). Prod and the shared `clinic_management` untouched.
**Peer sessions live during the run:** clinic-management-4d (busy), clinic-management-77 (shell), clinic-management-be
(shell) — none on these ports or this database.
**Widths looked at:** 320×700 · 390×844 · 1440×900 · 1536×730 (screenshots opened and read)
**Status:** RED (3 findings: 1 major, 2 minor — all three pre-exist on main, all three sit on this flow)

## Scenarios

| ID | Outcome | Evidence |
|----|---------|----------|
| A1 | ✅ | footer « Enregistrer la séance — 300,000 DT », Payé « 100,000 », no « Sur la note »; shots/A1-1440.png |
| A2 | ✅ | toast « Fiche de soins mise à jour »; one note 2026-0785 300/100 PartiallyPaid, payment live, fiche 300/100 |
| A3 | ✅ | footer « Corriger la note — 300,000 DT »; dialog rows « payé 100 · 300 » / « payé 0 · 300 », « passe de 100 à 0 », « Il reste 300 », « sans paiement »; « Corriger » on screen at 1536×730; shots/A3-1536x730.png, A3-390.png |
| A4 | ✅ | toast « Note n° 2026-0790 émise — 0,000 DT payés »; 2026-0785 Cancelled + payment voided; 2026-0790 Issued 300/0 supersedes it; fiche 300/0; devis total 200, act 0, reste 200 — unchanged |
| A5 | ✅ | devis row « Note n° 2026-0790 … payé 0,000 DT · reste à payer 300,000 DT »; « Corriger » → patient page, fiche open, Payé 0; shots/A5-1440.png, A5-fiche-1440.png |
| A6 | ✅ | patient header « Reste à payer 500,000 DT » (300 note + 200 devis) |
| B1 | ✅ | dialog payé 100 → 50, « récupère les 50,000 DT », « Il reste 250 »; toast « … 50,000 DT payés »; new note 300/50, payment dated 2026-11-02 |
| B2 | ✅ (after a probe fix) | notes and fiche byte-identical before/after « Annuler » |
| B3 | ✅ | séance 2 act wears « Inclus dans le traitement », footer « Enregistrer la séance » (no price); shots/B3-1440.png |
| C1 | ✅ | at 320×700: dialog scrollWidth 320 = clientWidth, both buttons inside (y 596 / 640); shots/C1-320.png |
| C2 | ✅ | reopened: Payé 0, plain save at 300; still 2 notes, live one 300/0 |
| C3 | ✅ | at 390: `<main>` no sideways scroll, « Corriger » inside; shots/C3-390.png |
| D1 | ✅ | ordinary fiche 90 → 0: « sans paiement », « Il reste 90 »; NEW unpaid note 90/0 (main raised none) |
| D2 | ✅ | ordinary fiche 90 → 60: « passe de 90 à 60 »; new note 90/60 |
| D3 | ✅ | attached continuation: plain save at 300; note still attached, 300/100 |
| D4 | ✅ | /caisse « Corriger le montant… »: « recopiée en brouillon », no « Nouvelle note »; shots/D4-1440.png |
| D5 | ✅ | /factures « Corriger cette note » → P4's fiche open |

## Findings

### F-1 · major · « Payé » says the opposite of the button under it

- **Repro:** any billed fiche → lower « Payé » (P1: 100 → 0).
- **Expected:** the screen points at the way to do it — « Corriger la note ».
- **Actual:** a red line under « Payé » reads « 100,000 DT déjà payés sur la note · un montant payé ne se diminue pas
  ici — établissez un avoir. », while the footer button reads « Corriger la note — 300,000 DT ». It says « not here »
  about the thing the button does, and names the wrong document: an avoir records money handed back, which is what
  the correction dialog itself says.
- **Evidence:** shots/A3-1536x730.png (line under « Payé »).
- **Suspected owner:** `web/components/patient-record-modal.tsx:3143` (`lowersBilledAmount` branch).
- **Regression?** No — on main too. It is the wording Dr Hamdane met on this exact path.

### F-2 · minor · the devis still names the cancelled note after a correction

- **Repro:** after A4, open `/treatment-plans/D1`.
- **Expected:** the devis text names the note that bills séance 1 now (2026-0790), as the act row does.
- **Actual:** « La 1re séance du 02/11/2026 est facturée sur la note n° 2026-0785 (300.000 DT). » — a cancelled note.
  The text is stored in `TreatmentPlan.Notes` at continuation time and printed on the devis PDF too.
- **Evidence:** shots/A5-1440.png.
- **Suspected owner:** `ContinueRecordedActCommand.cs:293` writes the number into the plan's notes; nothing updates it.
- **Regression?** No — any correction of that note (possible on main with money kept) leaves it stale.

### F-3 · minor · after a correction the note row says « avec d'autres actes »

- **Repro:** after A4 (or B1), the devis page's money section.
- **Expected:** « Note n° 2026-0790 · Séance 1 · payé … » — that note bills only the root canal.
- **Actual:** « Séance 1 · avec d'autres actes · payé 0,000 DT … » and the headline « Prix du traitement … avec d'autres
  actes de la note ».
- **Evidence:** shots/C3-390.png.
- **Suspected owner:** `TreatmentPlanMappingExtensions.cs:71-86` — a replaced note gets `BilledActAmount = 0`
  (« could not be attributed », right for the figure) and `BillsOtherWork` then reads `300 − 0 > 0` as « bills other
  work ».
- **Regression?** No — on main for any correction with money kept; more reachable now.

## Observations (off-plan)

- 3 × « 401 Unauthorized » in the console on every launch, before sign-in (the session probe on `/login`). Not chased.

## Probe bugs fixed during the run (not product defects)

| Row | Symptom | Real cause |
|-----|---------|------------|
| B2 | « Abandonner » click timed out | the helper answered the discard prompt twice; the second click hit a prompt already closing. One answer now, and B2 compares before/after snapshots |

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files | Blast radius |
|---------|---------|------------|-------|--------------|
| F-1 | confirmed | the line predates « Corriger la note »: lowering « Payé » used to grey the button, and its sentence stayed when the button became the correction | `patient-record-modal.tsx` (line + stale docstring) · `DentalRecordBillingRefusals.PaymentLowered` (same wrong remedy, server side) | the server sentence is only shown outside the fiche (the fiche turns its code into the dialog); nothing pins it. The treatment-side « ne se diminue pas ici » is a real refusal with a real remedy (void on the devis) — left, deliberate |
| F-2 | confirmed | a note number + amount baked into free-text plan notes at continuation; nothing re-writes them on a correction. Its comment claimed the devis PDF prints them — false, `DevisPdfData` has no notes | `ContinueRecordedActCommand.cs` + 2 tests that pinned the number | notes are read on the devis page header and the devis form only; the live note is on the act row. Existing plans keep their old text (prod data, not touched) |
| F-3 | confirmed | `BillsOtherWork` compared the TTC with the share, which is 0 after a replacement (« not attributable »); a re-priced act or a timbre fiscal broke it the same way | `IInvoiceRepository`/`InvoiceRepository.GetMoneyByIdsAsync` (+ `LineCount`), `PlanCarriedInvoice`, `TreatmentPlanMappingExtensions`, and the twin at creation in `ContinueRecordedActCommand` | one production caller of the projection; its tests updated + 4 new cases (same price, re-priced, timbre, a real second line) |

**Gates after the last edit:** check:responsive 86/86 ✅ · tsc ✅ · build ✅ · dotnet test 6340/6340 ✅ ·
check-coverage exit 1 — pre-existing on `main` (64 untested tier-0 rows; this branch touches no `e2e/`)
**Re-run:** run-2.md — GREEN (59/59, 0 ⏭)
