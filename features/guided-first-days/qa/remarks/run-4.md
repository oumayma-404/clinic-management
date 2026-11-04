# QA run 4 — every guide a tour, back to the dashboard, the next step highlighted

**Plan:** `qa/remarks/plan.md` (rows A15, A16, E1–E3 added; D1/A9, B1, A4, A14, A11 rewritten for tours) ·
**Driver:** `walk.mjs` (tour rows spliced in; the click-and-save rows they replace are gone with the behaviour) ·
**Log:** `walk-run4.log`
**Environment:** API :5097 (unchanged since run 3), web :3097 rebuilt (standalone), DB `clinic_gfd_all`. Two
cabinets: `qa-rk-*` (the walk) and `qa-rk2-*` (A11's « ordinary work »).

**Status: RED (1 finding)** — 60 ✅ · 1 ❌ · 1 ⏭ (D5, as before)

## Findings

### F-1 · minor · the « cabinet prêt » celebration is skipped when the last step was celebrated before

- **Repro:** tour « Première fiche de soins » with no patient → « Terminer » (celebrated) → untick it → finish every
  other step → run the fiche tour again to « Terminer » — it is now the last open step.
- **Expected:** back on `/`, the card gone, « 🎉 Votre cabinet est prêt. » with confetti.
- **Observed:** the card is gone and no « prêt » message appears (`shots/A14-ready-1536x730.png`).
- **Cause:** `guide-provider.tsx` `finish` gates `celebrateReady()` on `markCelebrated(owner, stepKey)` — the STEP's
  once-only mark, already spent by the first « Terminer ». The « prêt » moment needs its own mark.
- **Owner:** `web/components/guide/guide-provider.tsx` (finish) and `web/lib/getting-started-celebration.ts`.

## Everything else

A1–A3, A7–A9, A12, A13, A15, A16, B1–B6, C1, C2, D1–D4, E1–E3, A4, A5, A11 green — including every tour reaching
`/`, the highlighted row and its percentage (43 → 57 → 71 → 86 %), no patient / account / visit / fiche ever
created by a tour, and the second cabinet's ordinary-work celebration.
