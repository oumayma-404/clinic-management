# Blast radius — treatment page QA gap 3 (items 1–19)

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `planItemStateLabel(item, state)` (new, `treatment-plan-labels.ts`) — « Suite à planifier » / « Suite prévue » once a séance is done | état word | `PlanActStateBadge` (row + card), `patient-plans-strip` `PlanActLine`; `ITEM_WORKFLOW_LABELS` untouched | must change — both readers call it |
| 2 | « Tout modifier (titre, notes, échéancier) » link | visible label | workspace only (menu item already « Tout modifier ») | unaffected |
| 3 | `PlanActSeances` trailing « + » — word « Séance » from `sm:`, withheld on a non-live plan | séance strip control | workspace header strip, table rows, card tree | must re-test — Arrêté / Terminé / live, 320–1440 |
| 4 | `seanceDay` (exported from `seance-strip.tsx`, same `dd/MM` rule the strip prints) | date format | séance popover (faite le / dès le), séances dialog chip; strip captions unchanged | unaffected — strip output identical |
| 5 | Money figure « Prix » → « Prix du traitement » | visible label | workspace « L'argent » only | unaffected |
| 6 | Stop dialog « Nouveau prix » only when the kept total differs | display | — | unaffected |
| 7 | `PlanActCostEditor` remise input « DT » suffix | display | row + card cost cell | must re-test — 320 card, 1180 table |
| 8 | État column shown only when a row has something to state (live plan, « Fait », « Mis de côté ») | table column + card status | `PlanActRow` (`showState`, `columnCount`), card `status` | must change — both trees read one predicate |
| 9 | Devis chip « Pas de devis » → plain muted text | display | workspace header only (patient card already plain) | unaffected |
| 10 | Échéancier fold: withheld when there is nothing to open (un-numbered with no row, closed with no row); numbered + amendable + 0 row still holds « Modifier l'échéancier » | fold render rule | workspace only; `ReviseInstallmentsModal` untouched | must re-test — Draft, numbered 0 row, billed |
| 11 | Billed « L'argent »: figure hint « sur la note … » dropped; « Facturé sur la note n° … » shown whenever billed | display | workspace only; e2e `Encaisser sur la note` link unchanged | must re-test — S1 |
| 12 | Sticky « N modifications » bar moved directly under « Actes » | layout | workspace only; same handlers | must re-test — X1 at 1440×900 and 1536×730 |
| 13 | `firstUnbookedStep(item)` (new export, `plan-next-action.ts`); `firstUnbookedStepId` unchanged | helper | workspace `nextAct`/`primaryAction`, patient card `cardNextAction`; `planNextAction` / `planHeadline` untouched (list, headline) | must re-test — T2 header + patient card |
| 14 | Séances dialog done row: name wraps, no grip, short date; new-row placeholder « Nom de la séance » | dialog layout | — | must re-test — 390 / 1440 / 730 tall |
| 15 | Form « Séances » caption removed | display | — | unaffected |
| 16 | `PlanActReorderControls` `coarse:size-11` | row/card control | table (vertical), card (horizontal) | must re-test — coarse 390 / 820 |
| 17 | Confirmation titles / bullets wording | display | e2e: none (grepped) | unaffected |
| 18 | « De côté » → « Mettre de côté » | visible label | row + card; aria-label unchanged | unaffected |

## Capabilities moved (none removed)

| Capability | Before | After |
|---|---|---|
| « + » add a séance on a stopped/finished treatment | strip « + » | « Modifier les séances » in each séance's popover (unchanged) and the séances dialog |
| État on a closed plan | always a column, often empty | column kept whenever a row states « Fait » / « Mis de côté » |
| Planifier a later séance while the next one is booked | séance popover « Planifier » only | also the header button and the patient card button |
</content>
</invoke>
