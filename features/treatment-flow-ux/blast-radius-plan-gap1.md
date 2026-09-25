# Blast radius — treatment page QA gap 1 fixes (items 1–14)

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `SeanceStrip` layout (min width per séance, gap, scroll whenever it overflows, right-edge fade) | shared primitive | booking picker (`appointment-acts-picker` ×2), fiche (`patient-record-modal`), catalogue (`procedure-type-steps-dialog` ×2, incl. the `procedure-types-table` cell), treatment page | must re-test — every strip at 320/390/820/1440; a narrow box now scrolls instead of squeezing |
| 2 | `SeanceStrip` `inactive` prop · `seanceCaption(step, now, inactive)` | additive prop / optional 3rd arg | same 5 + `seanceSummary` (not changed) | unaffected — defaults keep today's captions |
| 3 | `seanceEarliestAhead` (new export) | « dès le » only while the date is ahead | `plan-act-row` badge + popover, `seanceCaption` | must change together — one test for the three |
| 4 | `PlanActStateBadge` (`planLive` prop) | état badge | workspace card `status`, `PlanActRow` état cell | must change — both call sites pass it |
| 5 | `PlanActPrimaryAction` fallback text | « Devis annulé » / « Traitement arrêté »… | workspace card `primaryAction`, `PlanActRow` sub-row | must change — `hasActions` in the row asks the same test so no empty sub-row |
| 6 | `PlanActStepsAction` / `EditAction` / `WithdrawAction` labels visible at every width | row controls | table sub-row (lg+, already visible), card tree (moved off the header row) | must re-test — card tree 320/390/820 |
| 7 | `planActCardFields` label « Coût » → « Prix » · table head · form « Coût (DT) » · `parsePlanLines` « Coût invalide » | visible strings | e2e: none (grep `Coût`) | unaffected elsewhere |
| 8 | `PlanActSelectionBox` + header checkbox `rounded-[4px]` | call-site class | `ui/checkbox.tsx` untouched (its `rounded-sm` is 8 px on 16 px = a circle app-wide — reported, not mine) | unaffected |
| 9 | `seanceCountLabel(done,total,closed)` · `planSeanceCountLabel` passes `!isPlanLive` | count words | workspace header, `treatment-plans-table` (list) | must re-test — list row of an Arrêté/Annulé plan now « Aucune séance faite » |
| 10 | Card spacing (`gap-4`, header `gap-0`) | call-site classes on the 3 workspace cards | `ui/card.tsx` untouched | unaffected |
| 11 | Sticky bar error line + « Recharger » button | `inlineConflict` render only | same `discardInline()+onChanged()` action | must re-test — 409 and a non-409 refusal |
| 12 | `isPlaceholderTitle` (new export, `treatmentName` uses it) | title regex, one owner | form modal hydration | unaffected — same regex |
| 13 | Form title: placeholder stored title opens empty; empty ⇒ stored title sent back | amend + draft-update payload | `buildAmendRequest` (unchanged), in-place editor (unchanged) | must re-test — no rename, « Aucune modification » still fires on a no-op |
| 14 | `actRemovalPlan` reason strings | named refusal | form modal (bin), `buildAmendRequest`, N38 (owner check only) | unaffected — condition untouched |
| 15 | Settle modal landing list hidden for one auto-raised row | display | — | unaffected — payload unchanged |
| 16 | Revise modal locked-row sentence (payée / entamée) | display | — | unaffected |
| 17 | `confirmBill` button « Facturer » · undo dialog first bullet | display | e2e: none | unaffected |

## Capabilities moved (none removed)

| Capability | Before | After |
|---|---|---|
| Découper en séances · Modifier · De côté (card tree) | card header row, icon-only below `sm:` | own wrapping row under the fields, labelled |
| Row fallback « Devis annulé » etc. on a closed plan | row text | dropped — the header badge states it (no action lost) |
| Remettre à faire (séances dialog, done row) | icon ↺ | labelled ghost button, same confirm |
