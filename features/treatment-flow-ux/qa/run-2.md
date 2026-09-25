# QA run 2 — treatment flow UX

**Date:** 2026-09-24 · **Plan:** `plan.md` · **Driver:** `walk.mjs run2` (same script as run 1, + 4 rows for fixes)
**Environment:** api :5000 (clinic-management-09), web `next dev` :3000 restarted fresh by clinic-management-f1,
docker (investigate-phone). Peers live: all idle (clinic-management-bf told of the web restart, agreed).
**Widths looked at:** 1440×900, 1536×730, 390×844, 320×720 (screenshots in `shots/run2/`, read).
**Gate after the last edit:** `check:responsive` 72/72 · `tsc --noEmit` clean · `next build` green (isolated copy,
the live dev server holds `web/.next`) · `e2e/scripts/check-coverage.mjs` identical to HEAD (pre-existing red).

## Result

| Tier | Rows | ✅ | ❌ | ⏭ |
|---|---|---|---|---|
| A | A1–A15 | all | 0 | 0 |
| B | B1–B5 | B1–B4 | 0 | B5 (no destructive action offered on a plan with work — by design) |
| C | C1–C3 | all | 0 | 0 |
| D | D1–D6 | D2–D6 | D1 (probe: menu items not exposed as `menuitem`; the screenshot shows the menu open with « Annulé » / « Supprimer ») | 0 |

## Fixed between run 1 and run 2

| # | Finding (run 1 eye pass + code review) | Fix |
|---|---|---|
| 1 | Fiche at 320×720: sticky money band left the acts area ~110 px | labels above fields below `sm:`, 3 figures on one row — now 354 px |
| 2 | Added act: new price printed twice (line + « Prix ») | folded into the Prix figure (~~250~~ 310) |
| 3 | Edit RDV « ⋯ » squeezed to ~16 px by `[&>*]:w-auto` | `min-w-9` |
| 4 | Séance focus ring clipped « faite » | ring offset |
| 5 | Catalogue strip widened the name column | `max-w-md` |
| 6 | Stop dialog money in mono | plain tabular figures |
| 7 | Default title for 2+ acts became « X + N actes » (stored, goes stale) | back to « Plan de traitement »; in-place save omits a blank title |
| 8 | Browser Back lost unsaved in-row edits | page-owned history entry, asks first |
| 9 | Done séance had no route to the séances dialog | « Modifier les séances » on done séances |
| 10 | Partial save (price ok, remise failed) reported as total failure | success toast for what landed |
| 11 | Patient-card booking lost the name→catalogue fallback | resolved in the booking picker for every door |
| 12 | Header « 0 séance sur 3 faite » | `seanceCountLabel` |
| 13 | « 1 séances » on the catalogue | singular |
| 14 | Refusal pointed to « sur sa ligne » for multi-séance acts | names the last done séance, one press each |
| 15 | Filter « À commencer » hid Draft treatments wearing that badge | filter labels describe the devis (« Sans devis » / « Devis accepté ») |
| 16 | Séance editor open-state keyed by index jumped acts on removal | shifted with the rows |

**Status: GREEN** (one probe row, verified by screenshot).
