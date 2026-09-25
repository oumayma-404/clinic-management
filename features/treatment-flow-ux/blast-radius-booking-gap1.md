# Blast radius — booking, QA gap 1 fixes

Written before the first edit. Verdicts: `unaffected` · `must change` · `must re-test`.

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | split toggle label + variant (`appointment-acts-picker.tsx`) | « En 1 séance » / « En N séances » → « Tout en 1 séance » / « Répartir en N séances », ghost → outline | e2e `booking-browser.spec.ts` (BOOK-31/32/35/37), `browser-hot-paths.spec.ts` (BOOK-33/36); qa `walk.mjs` already expects the new labels | must change — e2e selectors |
| 2 | duration preset labels + `durationDisplay` (both dialogs) | « 15m / 1.5h » → `formatDurationFr` (« 15 min », « 1 h 30 ») | e2e `concurrency.spec.ts` `/^1h$/` ×4; `formatDurationFr` also read by recap + steps dialog (unchanged) | must change — e2e; values/onClick unchanged |
| 3 | « DT » inside 4 money inputs (picker) | prefix → suffix (`ps-7` → `pe-8`) | none (selectors use id / aria-label) | unaffected — same field, same handlers |
| 4 | 7 advisory confirms (create ×4, edit ×3) | title → question, body → `Consequences` bullets | e2e `lib/dialogs.ts` reads the title, `sawPrompt(/passé/)`, `sawPrompt(/existe peut-être déjà/)`; affirm labels « Continuer » / « Créer quand même » kept | must change — duplicate pattern if the title moves; must re-test BOOK-05/06/35 |
| 5 | `PlanStepOption.doneDate` (new optional) + `planItemToPreset` (shared `plan-next-action.ts`) | done séance caption « faite le 16/09 » | `stepOptions` read only by the picker; edit dialog copies `stepOptions` through untouched | unaffected — additive optional field |
| 6 | `font-mono` → `tabular-nums` | display only | picker, protocol editor, steps dialog, unfinished-acts-list | unaffected |
| 7 | steps dialog title « Séances · {act} » + consequence bullet | display only | qa `walk-2.mjs` W8 checks the dialog opens (no title match); `ProcedureStepsCell` unchanged | unaffected |
| 8 | `@container` on the preset row | one row when it fits, 3 + 3 below | both dialogs only | must re-test at 320 / 390 / 820 / 1440 · 730 tall |

## Capabilities moved

None. Every control keeps its handler, value and aria-label; only labels, variants and layout change.
