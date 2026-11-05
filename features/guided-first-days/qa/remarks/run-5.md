# QA run 5 — re-run after F-1

**Plan / driver:** unchanged from run 4 (`plan.md`, `walk.mjs`) · **Log:** `walk-run5.log`
**Fix under test:** F-1 — « Votre cabinet est prêt » has its own once-only mark (`markReadyCelebrated`), used by both
the end of a tour (`guide-provider.tsx`) and the card noticing the last step (`getting-started-card.tsx`).
**Environment:** web :3097 rebuilt after the fix; API and DB as run 4. `tsc` clean, `check:responsive` 82/82.

**Status: GREEN** — 61 ✅ · 0 ❌ · 1 ⏭ (D5: no console account on this DB)

| Finding | Run 4 | Run 5 |
|---------|-------|-------|
| F-1 « prêt » skipped when the last step was celebrated before | ❌ A14 | ✅ A14 « 🎉 Votre cabinet est prêt. », card gone |

Every other row of the plan re-ran green, none regressed. Looked at: `shots/A9-celebration-1440.png` (the highlighted
row « On attaque l'étape suivante ! Votre cabinet est configuré à 43 % »), `shots/B1-empty-1440.png` (the fiche tour
with no patient: one sentence + « Terminer »), `shots/A4-*-1536x730.png` (the fiche tour at the owner's height).
