# Design — Patient page « Plans de traitement » summary strip

**Status:** APPROVED — implemented
**Replaces:** `web/components/treatment-plans/patient-plan-card.tsx`
**Mockup:** [`mockups/01-plan-strip.html`](mockups/01-plan-strip.html)

## Process note

This design was produced **without a spec** and **without live-browser exploration**, both deliberately:

- There is no `features/patient-plans-summary/spec.md` — the request was a focused redesign of one existing
  component, not a new capability. Sending it through `/define-feature` first would have cost more than the change.
- This repo has no `agent-browser`, no `scripts/start-dev.sh`, and no pre-existing `screenshots/` folder, so the
  skill's browser steps do not apply. Per its own fallback, the design language was derived by **reading source**
  (`app/globals.css` tokens, `treatment-plan-labels.ts` badge palette, `plan-next-action.ts`, `plan-progress-bar.tsx`)
  plus the user's screenshot of the current card. The mockup therefore uses the project's real token values and real
  badge classes rather than approximations.

## Problem

The current card spends ~250 px on four facts about **one** plan, and misrepresents the rest.

| # | Defect | Cause |
|---|---|---|
| 1 | A 0 %-progress plan renders a full-width grey slab carrying no information | `PlanProgressBar` returns null only when `total <= 0`, not when `done === 0` |
| 2 | « 0/2 actes **réalisé** » — wrong agreement | Pluralises on `itemsDone` (`0` → no *s*); French agrees with *actes* |
| 3 | « Reste » is never shown, though it is the money question staff have | Card prints `amountPaid / totalPlanned` only |
| 4 | Finished and cancelled plans are **unrepresentable** | `otherCount` filters out `Completed` *and* `Cancelled`, so « +N autres » cannot count them |
| 5 | Three competing controls (2 buttons + a text link) for one decision | No single primary action |

## Decisions

| Decision | Choice | Why |
|---|---|---|
| Density | **Two-row strip, `border-y`, no card** | ~76 px vs ~250 px. A band in the page flow rather than an object floating on it — consistent with the dashboard de-boxing (`KpiGrid`). |
| Leads with | **The next action**, largest text | The only line that says what to *do*, and it re-derives as visit times pass (`planItemState`). Money and progress are context for it. |
| Progress | **Segmented act pips**, bar fallback past 12 acts | A pip per act encodes *four* states (réalisé / planifié / séance passée sans fiche / à planifier) in less space than a bar encodes one. Crucially it still shows the plan's **shape** at 0 done, which is exactly where the bar degenerated into a slab. Past ~12 acts pips smear, so it falls back to a 3 px bar + fraction. |
| Other plans | **Status chips, every status** | Answers "and the finished ones?" in one short line. Each chip opens the plans tab filtered. |
| Actions | **One primary**, from `planNextAction` | Replaces Accepter/Voir/+N. « Tous les plans » becomes a quiet text link. |

## Data

Everything comes from `TreatmentPlanDto` as it already is — **no API change, no new endpoint, no migration**.

| Shown | Source |
|---|---|
| Number, statut, révision | `number`, `status`, `revisionNumber` |
| Next action + its count | `planNextAction(plan)` + `plan.items.filter(planItemState === …)` |
| Act pips | `items[].status`, `items[].scheduledAt` via `planItemState` |
| Reste / Encaissé | `outstanding`, `amountPaid`, `totalPlanned` |
| Prochaine séance | `nextAppointmentAt` |
| Facturé — F… | `linkedInvoiceNumber` |
| Status chips | `plans[]` grouped by `status` |

## States

| State | Behaviour |
|---|---|
| Draft | No pips, no « Reste » — a draft devis is not debt (contributes 0 to « Solde patient » by design). Shows act count + total; action is « Accepter le devis ». |
| Active, nothing booked | Pips all hollow — shows the plan's shape. « Aucune séance planifiée ». |
| Séance passed, no fiche | That pip is amber-ringed; action is « Enregistrer la fiche ». |
| Acts done, money owed | Pips all filled; « Reste » in `--destructive`; action becomes « Encaisser ». |
| > 12 acts | Pips → 3 px bar + fraction. |
| No active plan, has history | **Strip still renders** with « Aucun plan en cours » + chips. "This patient was treated and it finished" is information; the current card renders nothing here. |
| No plans at all | Renders nothing (unchanged). |
| Single plan | Chips and « Tous les plans » omitted rather than showing « 0 autre ». |
| Narrow (375 px) | Wraps; nothing truncates. |

## Accessibility

- Pips are decorative (`aria-hidden`); the fraction « 1/2 actes » is the accessible value, and the pip legend is
  rendered as text. This replaces `PlanProgressBar`'s `role="progressbar"` — a progressbar whose `aria-valuemax`
  could be 0 was the reason that component had to special-case empty plans at all.
- State is never carried by colour alone: each pip state has a distinct fill/ring treatment, and the counts are in text.
- Chips are links with their own accessible text (« 2 plans terminés »), not bare numbers.
- One primary `<button>`; « Tous les plans » is a real link.

## Resolved

**No caption.** The reviewer chose to keep « Plan 2026-0045 » alone — the `ClipboardCheck` icon and the words
« Plan de traitement » go, and line 1 keeps the width.

## Implemented as

| File | Role |
|---|---|
| `web/components/treatment-plans/patient-plans-strip.tsx` **(new)** | The band. `patient-plan-card.tsx` is deleted. |
| `web/components/treatment-plans/plan-act-pips.tsx` **(new)** | Pips + legend, with the >12-act fallback to `PlanProgressBar`. |
| `web/components/treatment-plans/plan-next-action.ts` | Gains `planHeadline` (the *counted* next action) and `planStatusCounts` (chips, every statut). |
| `web/app/patients/[id]/page.tsx` | Import + usage renamed. Same three props, so the call site is otherwise unchanged. |
| `web/components/CLAUDE.md` | Rows updated. |

Renamed rather than edited in place: it is not a card any more, and the whole blast radius was two code references.

Two deviations from the mockup, both deliberate:

- **The history-only state offers « Voir les plans », not « Nouveau devis ».** The mockup implied creation, but this
  page has no blank-devis entry point outside the plans tab — the only create paths are the odontogram seed and the
  tab itself. A button that promises to create and instead navigates is worse than one that says where it goes.
- **Pip colours are an inline `boxShadow` map, not Tailwind arbitrary values.** The ring widths differ per état and
  one needs a palette colour; in Tailwind v4 that is `var(--color-amber-500)`, and v3's `theme(colors.amber.500)`
  resolves to nothing — rendering an *invisible* pip rather than an obviously broken one. The style map has no such
  failure mode and needs no build to confirm.

`PlanProgressBar` is **unchanged**, so the plan workspace carries no behaviour change from this redesign.

Verified: `tsc --noEmit` clean, production build green (27/27 pages). **Not yet viewed in a browser with real
patient data** — the states above are reasoned from the DTO, not observed.
