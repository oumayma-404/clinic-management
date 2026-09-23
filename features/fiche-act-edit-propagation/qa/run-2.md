# Run 2 — « changer l'acte d'une fiche, et que tout suive » (after cycle 1 fixes)

**Status: GREEN** — both findings fixed and verified; no row that was green in run 1 regressed.

| | |
|---|---|
| Date | 2026-09-23 |
| Environment | API pid 43192 (this branch, Debug — unchanged since run 1, both fixes were frontend) · `next dev` restarted after the build (pid 53568) · same Postgres |
| Widths looked at | **1036 × 703** (the driven window) and the run-1 **1536 × 730** shots for comparison |
| Writes | **none.** SQL after the run: 3 fiches on the QA patient, acts unchanged, **0 records with `UpdatedAt` in the last 3 h**. Every save in this run was refused or cancelled. |

## The two findings

| # | Was | Now | Evidence |
|---|---|---|---|
| **F-1** *blocker* | « Aucun » in « Acte planifié » snapped back to « 2026-0395 · Suite » the instant it was chosen, so the remedy the new refusal names was unreachable | Select reads « **Aucun** » and **stays** after a 3 s settle; refusal banner cleared; « Enregistrer — 60,000 DT » enabled | `shots/run2-F1-aucun-sticks.png` |
| **F-2** *major* | re-picking the SAME act on a devis-carried card made the button read « Enregistrer — 30,000 DT » above a card saying « Aucun honoraire sur cette séance » | button reads plain « **Enregistrer** »; « Aucun honoraire » kept; no price field; « 1 acte » with no money | snapshot 08-02-32 |

## Regression sweep — every run-1 green row, re-driven

| # | Outcome | Evidence |
|---|---|---|
| A6 · billed fiche, act swapped at the SAME money | ✅ | refused; « Corriger la note de cette séance » opened; SQL still `Gingivectomie @ 50.000` |
| A7 · devis-carried card releases on an act change | ✅ | « Aucun honoraire » gone, « Montant forfaitaire » 60,000, total « 1 acte · 60,000 DT » |
| A8 · saving that state is refused | ✅ | covered by F-1's row — the banner appeared, then cleared on the Select change |
| B1 · re-picking the same act does NOT release | ✅ | see F-2 above |
| B3 · the refusal clears when « Acte planifié » changes | ✅ | banner count 0 after the change |
| B4 · deep link on a visit that already has a fiche | ✅ | `?addRecord=1&appointmentId=8d8f4328…` → « **Modifier** la fiche médicale » on the existing Gingivectomie fiche |
| C3 · the modal at the owner's window height | ✅ | footer and « Enregistrer » on screen, no horizontal scroll |
| D6 · « Corriger la note » reachable past the refusal | ✅ | the dialog opened with both figures |

## Still ⏭, with the same reasons as run 1

- **A1–A3** — not reachable on this data: every past visit with a fiche is `Completed`, so `canRecordVisit`
  withholds the two buttons. Fix 1's reachable door is the deep link (B4), which passes.
- **A4/A5, C1, C2** — need a fiche this pass would have to write; every fiche in reach is billed or
  devis-carried, and money/clinical rows stay read-only.
- **B3's save half** — would rewrite a shared devis-linked fiche and unmark its plan step while a peer session
  audits that subsystem. The part my change owns (the remedy being reachable) is verified above.
- **B5, C4** — server-side; covered by `PlanCarriedActMismatchTests`, the derived caller guard and
  `DentalRecordBilledActsUnchangedTests`, and a wire test would need a separate session family.
- **D1, D2, D5** — prefill regressions, not driven.
- **D3** — « Facturer » writes money on a patient this pass did not create.

## Observations carried forward (not fixed — off-plan)

1. With 2+ fiches on one visit the deep link opens an arbitrary one (`ficheForVisit` uses `.find()`).
2. The inline refusal shows the headline; the remedy is toast-only. Consistent with every other refusal here.
3. Session `storageState` expired mid-row twice and had to be refreshed — probe, not product.

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files |
|---|---|---|---|
| F-1 | confirmed | the record-hydration effect had `linkedPlanItemId` in its deps with `if (… !== NO_PLAN_ITEM) return`, so choosing « Aucun » re-fired it. Its own comment said « never overwrite a choice already made » — the intent was right and the test could not express it, because « Aucun » **is** a choice | `patient-record-modal.tsx` (a `hydratedPlanLinkRef` keyed on the record id, reset on open) |
| F-2 | confirmed | `actFromDto` sets `unitCostLocked: false` on every reopened act (deliberately), so a same-act re-pick fell through to `pt.defaultCost` | `use-session-acts.ts` (`billedOnPlan` is read first in the pricing expression) |

**Guard added:** `check:responsive` **N44 `carried-act-keeps-its-zero`** — brace-matches `applyProcedure`'s
body, masks comments, and requires **two** reads of `.billedOnPlan` (the release test and the pricing test);
the assignment `billedOnPlan: false` carries no dot and does not count. ⚠️ Its first version asked only whether
the identifier appeared and **passed a deliberate violation** — re-proved red and green after tightening.

**Gates after the last edit:** `check:responsive` ✅ 72/72 · `tsc --noEmit` ✅ · `npm run build` ✅ ·
`dotnet test` ✅ 4759 passed / 0 failed.

---

# Run 3 — full eye pass + the tier-D rows run 2 left ⏭

**Status: GREEN**, after one more finding fixed (F-3, below).

## Eye pass — the five widths, on the fiche modal with 3 acts

Probe: page-level sideways scroll, plus every element inside the dialog whose box falls outside the viewport,
**excluding** descendants of a legitimate `overflow-x-auto` scroller (the tooth arch).

| Width | Page scrolls sideways | Elements outside the dialog |
|---|---|---|
| 320 × 730 | no (320 / 320) | 0 (the 34 hits are the tooth arch inside its own scroller — the sanctioned § 11 pattern, left edge at x 37 so nothing is clipped) |
| 390 × 844 | no | 0 |
| 820 × 1024 | no | 0 |
| 1180 × 820 | no | 0 |
| 1440 × 900 | no | 0 |

Shots: `eye-320-fiche-3actes.png`, `eye-820-fiche-3actes.png`.

## F-3 — the one thing the eye pass found

**major · fixed · verified.** At 320 px the new refusal banner rendered **below the fold inside the modal's
own scroller**, and the toast had already been dismissed — so pressing « Enregistrer » looked like it did
nothing.

Measured before: banner at **y 456–472**, scrollport **102–437** → 19 px out of view, `toastPresent: false`.
Measured after: **y 261–277** inside 102–437 → `visibleInScroller: true`. Shots
`eye-320-refusal-banner.png` (before) and `eye-320-refusal-visible-after-fix.png` (after).

*Cause, and it is mine:* `refuseSave` scrolls to `actsAnchorRef` — the acts pile. Every pre-existing refusal
passes an **act key**, so its message renders *on a card* that the scroll centres. My devis-mismatch refusal
passes `null`, and that banner renders **after** the pile. *Fix:* the pile-level banner gets its own ref and
is the scroll target when no card is named, deferred one frame so it exists.

## Tier D — the rows run 2 left ⏭

| # | Row | Outcome | Evidence |
|---|---|---|---|
| **D1** | `applyProcedure` ← `applyAppointment` | ✅ | visit booked with 3 acts prefilled **all three**: « 3 actes · 210,000 DT » = 40 + **150** + 20, the 150 being the *negotiated* price, not the 200,000 catalogue tarif |
| **D2** | `applyProcedure` ← `addFromProcedure` | ✅ | deleting « Radiographie rétro-alvéolaire » → « 2 actes · 190,000 DT » and the act offered back as a chip; pressing it → back to « 3 actes · 210,000 DT », chip gone |
| **D4** | the two visit buttons at 820 px | ✅ | both rendered « Enregistrer la fiche » buttons inside their scrollers, no page sideways scroll; label correct for an undocumented visit |
| **D5** | `PlanItemPrefill` ← `handlePlanItemLink` | ✅ | linking « 2026-0525 · Composite (dents 24) » by hand carries the désignation, tooth 24, « Aucun honoraire sur cette séance » and « Encaissé sur le traitement » |
| **C1** | the plan prefill carries `procedureTypeId` | ✅ | **proved indirectly and decisively**: that devis line points at the *Détartrage* catalogue act, so « Changer d'acte » → Détartrage is a **same-act** re-pick. It did **not** release the card (« Aucun honoraire » kept, no price field), which is only possible if the prefilled act carried the catalogue id. Before the fix it was null and the release would have fired |
| **D3** | `Check` ← `BillDentalRecordCommand` | ⏭ **unreachable from the UI, verified** | that command only reaches `Check` when a note already exists (`LoadAsync` non-null), and the patient page withholds « Facturer cette intervention » on an already-billed fiche in **both** trees (`!invoicedDentalRecordIds.has(record.id)`, `app/patients/[id]/page.tsx:2730` and `:2859`). Covered by `A_Caller_That_Is_Not_Editing_The_Work_Supplies_Neither_Side` |

## Writes

**None.** Every save in this run was refused or escaped; the only mutations were in-form state.

## Gates after the last edit

`check:responsive` ✅ 72/72 · `tsc --noEmit` ✅ · `npm run build` ✅ · `dotnet test` ✅ **4759 / 0**
