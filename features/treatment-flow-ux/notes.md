# Treatment flow UX — what shipped (2026-09-24)

UI only, no API change. The 18 items of `proposal.html` (P1–P3, R1–R4, F1–F5, T1–T5, L1). QA: `qa/plan.md`,
`qa/walk.mjs` (run 2 green on every product row; D1 is a probe that cannot read the agenda menu — its
screenshot shows it open). Blast-radius tables: `blast-radius-{booking,fiche,plan,patient}.md`.

## The three rules it applies

1. **One word per idea.** Traitement (never « plan »), Devis = the paper only (`planDevisLabel`: « Devis n° … » /
   « Pas de devis »), Séance, Fait/faite, Prix du traitement, Payé / Encaisser, Reste à payer, Non réclamé,
   Fiche de soins, Remettre à faire, par dent · pour tout.
2. **Facts in bold, no instructions.** Every « Cochez… / Utilisez… / Touchez… » and every « how the system works »
   sentence is gone. Kept, shortened: figures, dates, statuses, errors, named refusals with their remedy, empty
   states, safety panels, and irreversible/money consequences as 2–3 bold bullets (`plan-consequences.tsx`).
3. **One séance strip everywhere** — `components/treatment-plans/seance-strip.tsx`. Replaced seven counters.

## Decisions that are easy to undo by accident

- **The status badge says where the WORK stands; the filter says what the DEVIS is.** `planStatusLabel`: Draft →
  « À commencer » / « En cours » (with recorded work), Accepted → « À commencer ». `PLAN_STATUS_LABELS` (filter +
  chips) keeps them apart: Draft « Sans devis », Accepted « Devis accepté ». One map for both made the filter
  « À commencer » drop every un-numbered treatment wearing that very badge.
- **`treatmentName(plan)` ignores placeholder titles** (« Plan de traitement », « Devis », « Traitement ») and
  derives « Couronne · dent 16 » from the acts. The stored default title for 2+ acts stays « Plan de
  traitement » (`derivedPlanTitle`): a stored « Couronne + 1 acte » goes stale the day a third act is added.
- **In-place price/remise edits on the treatment page save through `buildAmendRequest`**
  (`plan-amend-payload.ts`, shared with the full form) as ONE amendment; each remise then goes through
  `setItemDiscount` with the version the previous call returned (the amend has no remise field). `inPlace: true`
  omits a blank title so a blank stored title is not renamed. A partial success is toasted before the failure.
- **Unsaved in-place edits are guarded on every channel**: other writes, links, tab close, and the browser Back
  (a history entry the page owns while dirty — `useDirtyGuard`'s technique; confirming leaves with `go(-2)`).
- **« Remettre à faire » is offered on the LAST done séance only** — it is `detachOutcome` / `Unmark`, one séance
  per press. Every done séance also offers « Modifier les séances » (the dialog where an earlier séance recorded by
  mistake is corrected).
- **Every booking door resolves the catalogue act by name as a fallback** (`presetProcedureTypeId` in
  `appointment-acts-picker.tsx`, fed by `PresetPlanAct.designationFr`). Only the treatment page used to, so an act
  saved before `procedureTypeId` booked with no colour/duration/prefill from the patient card and the lists.
- **The fiche's « Changer ▾ » menu IS the two old Selects** (same `handlePlanItemLink` / `setChosenStepId`
  values); « Sans traitement » is the old « Aucun » and is still the only way out of the automatic devis add.
- **The continuation is inline in the booking dialog** (R3): the continuable-acts read still includes acts not
  ticked « non terminé » (a sort, never a filter) — those sit behind « Suite d'une séance précédente… ».
  « Planifier la suite » on /a-cloturer re-reads them and opens ONE dialog with the row added.
- **Phone fiche footer**: below `sm:` the labels sit above « Payé aujourd'hui » and « Mode » so both share one
  row, and the three figures are one `grid-cols-3` row. The sticky band left the acts area ~110 px at 320×720.
- `plan-step-strip.tsx` was deleted (no consumer left). N31 still has candidates (`seanceCountLabel`, the
  detach dialog's count); if the last one goes, re-point the tripwire rather than keeping dead code for it.

## Added by the full gap pass (plan-2, 2026-09-24/25)

- **« Inclus dans le traitement »**, never « Payé sur le traitement »: the tag sat beside « Déjà payé 0,000 DT » and
  read as money received. « Sur la note n° N » when a note holds it.
- **A started stepped act reads « Suite à planifier » / « Suite prévue · date »** (`planItemStateLabel`): « À
  planifier » under a séance « faite le 16/09 » read as « not started ».
- **The header and the patient card offer « Planifier : {first unbooked séance} »** even when the next séance is
  already booked — the same rule the booking dialog preselects with (`firstUnbookedStep`), so booking ahead is one
  press.
- **A treatment that is not live** (arrêté, non réclamé, annulé) says « non faite », never « à planifier »; its
  « + Séance » and empty État column are withheld; its count reads « Aucune séance faite » / « 1 séance sur 3 faite ».
- **The in-row edit bar sits right under « Actes »**, not at the page foot: a sticky bar covers only what comes
  before it, so « L'argent » is never under it. A 409 is one line in that bar with a real « Recharger ».
- **The fiche footer on a phone**: cheque fields fold into one row « Chèque : n° · banque · date ▸ »
  (`ChequeFields fold`, the fiche only — the payment modals keep them open); the money area is capped at 38dvh below
  `md:`; a mixed séance labels « Payé (séance) » / « Payé (traitement) » / « Reste à payer (séance) ».
- **`ui/checkbox.tsx` is `rounded-[4px]`**: `rounded-sm` resolved to 8 px on a 16 px box, i.e. a circle, read as a
  séance dot in the acts table. App-wide shape change only.
- **The séance strip and the tooth arch fade on the side that hides content**, only while they overflow
  (ResizeObserver + scroll) — a cut « Médicatio » or a sliced tooth 27 gave no hint that it scrolls.
- Vocabulary also moved: rail « Traitements » (was « Traitements et devis »), patient tab + section « Fiches de
  soins », « Nouvelle fiche de soins », « Prix » / « Prix par défaut » (not « Coût »), durations « 1 h 30 », « Tout en 1
  séance » / « Répartir en N séances », post-visit prompt « Séance terminée » (composed client-side).
- Test accounts: each `arrange-2.mjs` run creates a throwaway secretary « QA Secrétaire {ts} »; deactivate them after.
- **« Suite d'une séance précédente… » is always there** while the booking can continue a séance (a patient, no
  devis act): shown only with rows, the door read as removed. Empty → « Aucune séance passée pour ce patient. »
- **A treatment act in the booking has « Séances ▾ »** (the split act's own look): tick chips for this RDV + « Ajouter
  une séance au traitement », which opens the treatment page's séances window over the booking
  (`use-booking-plan-steps.tsx`, `AdminOrDoctor` only, live plans only). Tapping the strip still works.
- **The fiche says it the moment the treatment's act leaves the séance** (changed with « Changer d'acte » or
  deleted): « « Couronne » n'est plus dans cette séance » on the band, with « Remettre « Couronne » » and « Séance
  sans traitement » — also under the save refusal, which used to keep its remedy in a toast. One rule for both
  (`missingPlanAct`, the client mirror of `PlanCarriedAct.NamesAnActTheFicheDoesNotHold`); « Remettre » is the
  reducer's `restoreAct`, built from the open prefill's own two steps (`planItemCard` → `bookedActCard`), and the
  replacement act stays. With no act left it speaks only once the act had been there (no flash on open). The
  « Aussi prévu » chip for a row of the LINKED devis now re-adds it the same way — it used to re-add it as a new act,
  marked « Ajouté au traitement », i.e. quoted twice.

## Known, left as they are (pre-existing, outside this change)

Agenda toolbar truncation at 1440 · dialog title focus ring (focus lands on the title by design) · two dialog
backdrop styles (Dialog vs AlertDialog primitives) · native date inputs showing « dd/mm/yyyy » on an English Chrome
· « À clôturer » wording (« Venue » chip vs « Venu » button, header count) · white teeth on the dark theme · the
catalogue table still scrolls inside its box between ~1024 and 1150 px.

- **Review fixes (2026-09-25).** The edit dialog carries the version its own séances-window save left on the visit
  (removing a séance booked there rewrites the visit's rows — the next « Enregistrer » was a 409 whose
  « Recharger » threw away every edit). « Remettre » on a reopened fiche puts back the act as it was SAVED (a reopen
  has no appointment, so the devis-line rebuild lost état, faces, note, bridge roles, « non terminé » and teeth), and
  never a booked row whose act is not the devis act. `useDirtyGuard` answers only for the top-most open guard, so a
  window over a dialog closes alone on back and typing in it does not dirty the one below. The picker's index-keyed
  editors and typed totals move with the rows on every row writer (`commitRows`).

## Open

- `e2e/scripts/check-coverage.mjs` fails identically at HEAD (64 tier-0 rows untested) — not this change.
- A booking can still follow only one treatment (unchanged).
