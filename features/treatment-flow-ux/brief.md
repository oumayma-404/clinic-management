# Treatment flow UX — implementation brief (shared by every worker)

Owner's ask (2026-09-24): the treatment flow (RDV with a multi-séance act → its fiches de soins → the treatment
page → the patient file) must be **obvious for doctors and assistants who are not good with tech**. The app
explains too much; nobody reads it. Important info in **bold**, easy to scan, and a flow so intuitive it needs no
explaining. **UI/UX only** — no backend change. The approved proposal (18 items, before/after) is
`features/treatment-flow-ux/proposal.html` — open it and read the items you own. The owner said « do all ».

## The three rules

1. **One word per idea** — vocabulary table below. Apply it to every visible string in the files you own.
2. **Facts in bold, no instructions.** A figure + 3 words replaces a sentence.
   - **Goes:** every instruction (« Cochez… », « Utilisez… », « Touchez… », « Choisissez… », « Laissez vide si… »),
     every « how the system works » sentence, every hint/caption under a control, key-hint lines.
   - **Stays (shortened):** figures, dates, statuses, counts, errors, **named refusals with their remedy**, empty
     states (the three kinds), confirmations of irreversible/money consequences (as 2–3 bold bullets), safety
     panels (« À vérifier avant de prescrire », medical alerts).
   - If a control needs a sentence, the control is wrong: rename the label, change the control. No caption.
3. **One séance strip everywhere** — `components/treatment-plans/seance-strip.tsx` (already written, read it):
   `SeanceStrip` (steps, `currentStepIds` + `currentLabel` for « ce RDV » / « aujourd'hui », `size`, `onStepClick`
   or `wrapStep` to make séances buttons / popover triggers, `trailing`), `SeancePips` (dots only — always put
   words beside them), `seanceSummary(steps)` (« Empreinte prévue le 28/09 » / « Scellement à planifier »),
   `seanceCaption`, `seanceState`. Accepts any `{id,label,doneDate?,scheduledAt?,earliestOn?,note?}` — a protocol
   row with `note: "+7 j"` works too. Renders nothing for a step-less act (a one-sitting act looks as before).

## Vocabulary (visible strings only — never rename code symbols or API fields)

| Before | After |
|---|---|
| Plan · Plan de traitement · Traitement suivi · « Retour aux plans » · « Tous les plans » | **Traitement** (« ← Traitements », « Tous les traitements ») |
| Sans devis · En cours · sans devis · Éditer le devis · Accepter le devis · À accepter | Devis = the paper only: chip `planDevisLabel(plan)` → « Devis n° 2026-0677 » / « Pas de devis »; the numbering action is **« Créer le devis »** |
| Étape · protocole · « la 1re » (user-facing) | **Séance** (« Séance 2 · Empreinte »). Keep « étape » only where the catalogue edits a protocol if a rename would be ambiguous |
| Réalisé · faite · Terminé · Travail terminé · Traitement clôturé | **Fait** (act) / **faite** (séance) · **Terminé** (treatment) |
| Total convenu · Prix convenu · Prix du traitement (N séances) · Chiffré sur le devis | **Prix du traitement** |
| Encaissé sur le traitement · Encaissé · Régler le devis | **Payé** (figure) · **Encaisser** (button) |
| Solde dû · Reste · Reste à encaisser · Reste dû | **Reste à payer** |
| Passer la créance en perte · Passer en perte · Créance abandonnée | **Ne plus réclamer le reste** (action) · **Non réclamé** (badge) |
| Fiche médicale · dossier médical · compte rendu de visite | **Fiche de soins** |
| Détacher la fiche | **Remettre à faire** |
| forfait · « / dent » | **par dent · pour tout** |
| Acte planifié (facultatif) | **Traitement** |

Already done in `treatment-plan-labels.ts` (do not redo): badge `planStatusLabel` (Draft → « À commencer » / « En
cours »; Accepted → « À commencer »; WrittenOff → « Non réclamé »), act états « Prévu » / « Fait », `planDevisLabel`,
`treatmentName(plan)` (« Couronne · dent 16 »), `teethSuffix`, `planDisplayName` (no more « Plan … »),
`PLAN_NEXT_ACTION_LABELS.accept` = « Créer le devis », `.open` = « Voir le traitement ».

## Non-negotiables (this repo's rules — read `.claude/rules/frontend-web.md` in full before editing)

- **No capability is removed.** Before deleting any control, list what it does; afterwards it lives in a fold,
  a « ⋯ » menu, a popover on the séance, or « Tout modifier ». Put that list in your blast-radius file.
- **Never state the next step as a fact.** No `count + 1` rank, no bare fraction; words beside visible figures
  (« faite », « à planifier », « prévue le »). N31 holds it; `sr-only` words do not count.
- **Money/clinical semantics unchanged.** Same requests, same payloads, same tri-state rules (omitted = unchanged),
  same `useConflict` 409 recovery, same confirmations before irreversible writes. If you move a save to a new
  place, it must build the exact payload the old place built (every act, every step incl. `minDaysAfterPrevious`,
  teeth, pontics, version token) — reuse the existing builder, never re-list fields.
- Device contract: usable at 320 px; 44 px targets on `coarse:`; heavy dialog → sheet below `md:`; `dvh` not
  `vh`; `DialogBody` for the scroller; prefixed `md:max-w-*` on dialogs; no `text-[Npx]`; logical `ps-/pe-/ms-/me-`;
  `Button` is `whitespace-nowrap shrink-0` (a long label needs `basis-*` + `flex-wrap` or a shorter visible label +
  `aria-label`); icon-only controls carry `aria-label` naming the thing. Check dialogs you touch at 730 px tall.
- Code comments: one line for new comments, match the file's density; when you remove a behaviour a ⚠️ comment
  documents, update or trim that comment — never leave a comment describing text that no longer renders.
- No English string reaches a user.

## Coordination (several workers edit this tree at the same time)

- Edit **only the files you own** (listed in your task). Files owned by nobody but touched by several —
  `web/scripts/check-responsive.mjs`, `e2e/**`, `web/app/patients/[id]/page.tsx`,
  `components/treatment-plans/plan-next-action.ts` — are **Edit-tool only, small targeted replacements, re-Read
  right before editing**, never `Write` over them.
- **Never** run `npm run build`, start any server, open any browser, or run any `git` command that writes
  (`add`, `commit`, `stash`, `checkout`, `reset`). Read-only git (`diff`, `status`) is fine.
- Gate you run, in `web/`: `npx tsc --noEmit` and `npm run check:responsive`. Another worker's file may be red
  mid-edit — fix only errors in your files, re-run, and report any red that is not yours.
- A `check:responsive` guard that fails because a string you legitimately changed moved: keep the invariant, update
  the guard's matching minimally (Edit), and say so in your report. **Never weaken or exempt a guard.**
- `e2e/`: grep it for every visible string you change or remove, and update the assertion (Edit). Report each.
- Write your blast-radius table (`.claude/rules/regression-safety.md` § 1) to
  `features/treatment-flow-ux/blast-radius-<area>.md` **before** your first edit.
- Do not edit any `CLAUDE.md`; report doc-relevant changes and the coordinator updates docs.

## Report (terse — the coordinator reads it, not the owner)

A table: item id → done / partial (why) → files. Then: capabilities moved (from → to) · guards touched · e2e specs
touched · anything you could not do · any red in files you do not own.
