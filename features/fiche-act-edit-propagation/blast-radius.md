# Blast radius — « changer l'acte d'une fiche, et que tout suive »

Three reported symptoms, one sentence each:

1. **Le rendez-vous** — rouvrir la séance depuis le RDV recompose une **deuxième** fiche préremplie avec
   l'acte *booké*, au lieu d'ouvrir la fiche déjà enregistrée. L'ancien acte réapparaît, et un second
   enregistrement crée une fiche de plus (5 rendez-vous en portent déjà 2 à 4 sur la base de dev).
2. **La note d'honoraires** — `DentalRecordBillingGuard.Check` ne compare que **l'argent**, donc à prix égal
   les actes changent sur la fiche et la note garde les anciens libellés. Silencieux.
3. **Le devis** — « Changer d'acte » garde `billedOnPlan`, garde le lien `treatmentPlanItemId`, et
   `PlanCarriedAct.IndexIn` ne trouve plus rien : le devis marque **l'ancien** acte fait, et le nouveau acte
   s'enregistre à **0 DT**. Racine adjacente : `applyPlanItem` ne pose jamais `procedureTypeId`, donc une fiche
   ouverte sur une étape de devis enregistre un acte **hors catalogue** (ligne live `a0b5d12d`).

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `DentalRecordBillingGuard.Snapshot` | projection record | `UpdateDentalRecordCommand`, `BillDentalRecordCommand` | must change — new member, both callers construct nothing (built in `LoadAsync`) |
| 2 | `DentalRecordBillingGuard.Check` | shared guard, 2 call sites | update cmd (act edit) · bill cmd (no act edit) | must change — new optional param, bill cmd passes none |
| 3 | `DentalRecordInvoiceLines.For` | the one authority on fiche→lines | billing cmd | unaffected — read-only reuse, no signature change |
| 4 | `PlanCarriedActPricing.ImposeAsync` | return type | `CreateDentalRecordCommand`, `UpdateDentalRecordCommand` | must change — both must read the new refusal |
| 5 | `PlanCarriedAct.IndexIn` | shared rule | `PlanCarriedActPricing`, `ToothChartingRules` | unaffected — untouched; a new sibling predicate is added beside it |
| 6 | `applyProcedure` (`use-session-acts.ts`) | act reducer helper | `pickProcedure`, `addFromProcedure`, `applyAppointment` | must re-test all 3 — the clear is gated on an identity change, so a first pick is unaffected |
| 7 | `PlanItemPrefill` / `applyPlanItem` | plan→fiche prefill | `planItemPrefill` (2 call sites: appointment effect, `handlePlanItemLink`) | must change — carries `procedureTypeId` now |
| 8 | `appointmentVisitState` / `openVisitRecord` | patient page | 2 buttons + the `?addRecord=1` deep link | must change — all three doors |
| 9 | `DentalRecordBillingRefusals` | mirrored refusal set (code + sentence + `Correctable`) | `DentalRecordAutoBilling.IsRefusal`, the modal's 409/correction path | must change — a new code must be classified in both |

**Not touched, deliberately:** `Appointment.SetProcedures` and `AppointmentProcedure`. The booking records what
was *agreed* (act, `AgreedCost`, `TreatmentPlanItemId`, `TreatmentPlanItemStepId`); rewriting it from the fiche
would destroy the devis link `TreatmentsInProgressReader` and `PlanBookingRelease` read, and the negotiated
price. Symptom 1 is fixed by **reading the fiche that exists**, not by rewriting the booking.

**Gates:** unfiltered `dotnet test` · `npm run check:responsive` + `tsc --noEmit` + `npm run build`.
