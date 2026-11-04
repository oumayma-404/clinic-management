# QA run 3 — ticking a step by hand, and « Terminé » on the phone window

**Plan:** `qa/remarks/plan.md` (rows A12–A14, B6 added; B3 re-pointed at the tick endpoint) · **Driver:** same
`walk.mjs` · **Logs:** `walk-run3.log` (first drive), `walk-run3b.log` (after the last source edit: the jokes'
« ? » / « ! » / guillemets bound with U+202F)
**Environment:** API :5097 restarted on the new code; the unshipped migration was regenerated as
`20261104114332_AddFeedbackOtherAndTickedSteps` (the throwaway DB had the old one rolled back by hand: history row
deleted, three columns dropped — test DB only), web :3097 rebuilt (standalone). Backend 6147 ✅ / 0 ❌,
`check:responsive` 82/82, `tsc` clean, build green — all after the last edit.

**Status: GREEN** — 56 ✅ · 0 ❌ · 1 ⏭ (D5, as before)

## New rows

| ID | Outcome | Evidence |
|----|---------|----------|
| A12 | ✅ | « Terminé » → « Remettre à faire : Votre téléphone », `TickedStepsCsv` has `phoneApp`, celebrated; closing with ✕ ticks it too |
| A13 | ✅ | circle on « Papier à en-tête » → done + « 🎉 C'est fait : Papier à en-tête » + stored; untick → « Me guider » back, not stored — `shots/A13-ticked-1440.png` |
| A14 | ✅ | ticking the last two → « 🎉 Votre cabinet est prêt. », the card leaves — `shots/A14-ready-1440.png` |
| B3 | ✅ | `PUT …/securityCode/tick` → 400 `getting_started_step_not_tickable` |
| B6 | ✅ | no tick control on « Code de sécurité » |

Every earlier row re-ran green (A1–A11, B1–B5, C1–C2, D1–D4).

## Findings

None. One wording fix made during the pass (not a defect report): the letterhead joke broke its line after « —
the jokes' guillemets and « ? » / « ! » are now bound with U+202F.
