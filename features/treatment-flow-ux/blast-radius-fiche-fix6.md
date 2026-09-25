# Blast radius — fiche: the treatment act left the séance (fix 6, 2026-09-25)

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | the devis-act mismatch condition (save guard in `patient-record-modal.tsx`) | client mirror of `PlanCarriedAct.NamesAnActTheFicheDoesNotHold` | the save guard only (grep: no other copy in web/ or e2e/) | must change — extracted to ONE helper, read by the save guard and the live notice |
| 2 | `TreatmentBand` (same file) | presentation of the linked treatment | the fiche modal only | must change — gains the notice + two actions (optional props) |
| 3 | `handlePlanItemLink(NO_PLAN_ITEM)` | « Changer › Sans traitement » | band menu, now the notice's second button | unaffected — called as is |
| 4 | `use-session-acts.ts` reducer: `applyAppointment` / `applyPlanItem` | open-time prefill of the carried act | the modal's two open effects | must re-test — their card builders are extracted (same code), and a new `restoreAct` reuses them |
| 5 | N14 (`addFromProcedure` carries `agreedCost`), N22 (rows grouped by plan item), N36 (act fields round-trip) | derived guards on these two files | — | unaffected — `restoreAct` takes a `BookedActPrefill` (agreedCost required by type) built from the already-grouped `bookedActs`; no act field added |
| 6 | the refusal banner that names no card (`pileSaveErrorRef`) | inline refusal | « Ajoutez au moins un acte » + the mismatch | must change — carries the two buttons for the mismatch only |
| 7 | server rule | `PlanCarriedAct` | save | unaffected — untouched, still the authority |
| 8 | review fix — `reinsertAct` reducer action | puts a saved act back via `actFromDto` | « Remettre » on a reopened fiche only | must re-test — same card the fiche loaded (N36's fields come through `actFromDto`), then `markBilledOnPlan` |
| 9 | review fix — « Remettre » ignores a booked row whose act is not the devis act | `restorePlanAct` | band notice + refusal banner (same handler) | unaffected elsewhere — the « Aussi prévu » chip keeps `restoreBookedAct` |
