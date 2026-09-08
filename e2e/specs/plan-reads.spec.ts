import { expect, mutatingOnly, test } from "../lib/fixtures"
import { mil } from "../lib/api"

/**
 * HP-4 / HP-7 / HP-11 — the reads that describe a treatment, and the guard that protects delivered work.
 *
 * ⚠️ **Every defect in this file came from a hand-written status test.** « Is this treatment still being carried
 * out » and « does it carry money » are different questions with different answers for the same status, and
 * `Draft` is where they diverge: `TreatmentPlanLifecycle.IsLive(Draft)` is **true** and
 * `PlanBillingRules.CarriesDebt(Draft)` is **false**. Since « Suivre ce traitement », an un-numbered `Draft` is
 * usually a treatment being carried out *right now* — so a read that calls it « a quote nobody answered » is
 * inviting the practice to chase a patient about a document that was never handed over. `Accept` is the only
 * writer of `Number`, which is what makes « un-numbered » decisive.
 */

test.describe("HP-7 · un traitement suivi n'est pas un devis sans réponse @mutating @t0", () => {
  mutatingOnly()

  /** A followed treatment whose first séance is recorded `daysAgo` ago, so it looks « quiet ». */
  async function followedAndRecorded(api: any, arrange: any, patientId: string, daysAgo: number) {
    const act = await api.protocolAct(2)
    const started = await api.startTreatment({
      patientId,
      procedureTypeId: act.id,
      agreedTotal: Number(act.defaultCost) || 500,
      toothNumbers: [16],
      steps: null,
    })
    expect(started.status, `startTreatment: ${started.raw?.slice(0, 300)}`).toBe(200)
    const plan = started.body?.value ?? started.body
    const item = plan.items[0]

    const fiche = await arrange.fiche(
      patientId,
      [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 0, teeth: [16] })],
      {
        amountPaid: 0,
        interventionDate: arrange.localDay(-daysAgo),
        treatmentPlanId: plan.id,
        treatmentPlanItemId: item.id,
        treatmentPlanItemStepId: item.steps[0].id,
      },
    )
    expect(fiche.status, `fiche: ${fiche.raw?.slice(0, 300)}`).toBe(200)
    return { act, plan: await api.plan(plan.id) }
  }

  test("PLAN-24 · a followed treatment gone quiet is never « devis présenté, jamais répondu »", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Silence")
    // Comfortably past the 14-day threshold the worklist chases on.
    const { plan } = await followedAndRecorded(api, arrange, p.id, 40)

    expect(plan.number, "a followed treatment has no number, so no quote was ever presented").toBeNull()

    const recalls = await api.get("/patients/recalls?pageSize=300")
    const rows = recalls.body?.items ?? recalls.body?.value?.items ?? recalls.body ?? []
    const mine = (Array.isArray(rows) ? rows : []).filter((r: any) => r.patientId === p.id)

    /*
     * ⚠️ The claim, not merely the presence. Being chased for a *stalled treatment* is right — that is the
     * other half of `RecallWorklistRules` and it is why this asserts on the REASON. What is false is « devis
     * présenté, jamais répondu » about a plan that was never numbered: `Accept` is the only writer of `Number`,
     * so that quote cannot exist. Both halves of the worklist got this wrong at once — a stalled followed
     * treatment was never chased, while every followed treatment older than 14 days was reported as unanswered.
     */
    const unanswered = mine.filter((r: any) =>
      /jamais répondu|sans réponse|unanswered/i.test(`${r.reason ?? ""} ${r.reasonLabel ?? ""} ${r.kind ?? ""}`),
    )
    expect(
      unanswered,
      `a treatment with no number cannot be an unanswered quote. Rows: ${JSON.stringify(mine).slice(0, 400)}`,
    ).toHaveLength(0)
  })

  test("PLAN-25 · the dashboard does not count followed treatments as quotes awaiting an answer", async ({
    api,
    arrange,
    patient,
  }) => {
    const before = await api.get("/dashboard")
    const countOf = (body: any) =>
      Number(
        body?.alerts?.unansweredDrafts ??
          body?.value?.alerts?.unansweredDrafts ??
          body?.alerts?.devisPendingResponse ??
          body?.value?.alerts?.devisPendingResponse ??
          0,
      )
    const wasCounting = countOf(before.body)

    const p = await patient("Dashboard")
    // A followed treatment WITH recorded work — the case `CountUnansweredDraftsAsync` narrows to exclude.
    await followedAndRecorded(api, arrange, p.id, 3)

    const after = await api.get("/dashboard")

    /*
     * ⚠️ `DashboardAlertsReader` counted `Status == Draft` outright, which put this week's séances under a
     * heading inviting the practice to chase patients with nothing to answer. The identical premise had already
     * been corrected in `RecallWorklistRules` and this copy was missed — the « fixes don't propagate » shape,
     * one layer over. Asserted as a delta so the figure's own baseline (real drafts on this database) does not
     * have to be known.
     */
    expect(
      countOf(after.body),
      "a Draft with recorded work is live clinical work, not a quote awaiting an answer",
    ).toBe(wasCounting)
  })

  test("PLAN-21 · …but it IS listed under « Traitements en cours »", async ({ api, arrange, patient }) => {
    // The positive half, so PLAN-24/25 cannot pass by the treatment being invisible everywhere.
    const p = await patient("Visible")
    const { plan } = await followedAndRecorded(api, arrange, p.id, 3)

    const live = await api.treatmentsInProgress()
    expect(
      (live.items ?? []).some((r: any) => r.planId === plan.id),
      "clinically live and financially inert — it must appear where the WORK is tracked",
    ).toBeTruthy()
  })
})

test.describe("HP-11 · quelles dents ce traitement concerne @mutating @t0", () => {
  mutatingOnly()

  test("PLAN-26 · the « traitement en cours » ring reads the DEVIS LINE, not the fiche's teeth", async ({
    api,
    arrange,
    patient,
  }) => {
    /*
     * ⚠️ **The union was tried and measured wrong.** `TreatmentPlanItemDto.treatedToothNumbers` is the whole
     * SÉANCE's teeth, not the act's — it is derived from the fiches an act's steps produced, and a fiche records
     * every tooth of the visit. So an « Extraction simple » quoted on 13 and 43 whose first séance was recorded
     * on a fiche naming 13, 27, 36, 37, 43 put a « traitement en cours » ring on **three teeth with no
     * treatment on them**.
     *
     * `teethUnderTreatment` therefore reads the devis LINE first and falls back to the treated teeth only when
     * the line names none — the opposite priority to `openPlanItems`, which seeds a fiche's chart and rightly
     * prefers the teeth actually worked on.
     */
    const p = await patient("Dents")
    const act = await api.protocolAct(2)

    // Quoted on 13 and 43 only.
    const started = await api.startTreatment({
      patientId: p.id,
      procedureTypeId: act.id,
      agreedTotal: Number(act.defaultCost) || 500,
      toothNumbers: [13, 43],
      steps: null,
    })
    expect(started.status).toBe(200)
    const plan = started.body?.value ?? started.body
    const item = plan.items[0]

    // The séance touched five teeth — the extraction plus a scaling elsewhere in the mouth.
    const fiche = await arrange.fiche(
      p.id,
      [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 0, teeth: [13, 27, 36, 37, 43] })],
      {
        amountPaid: 0,
        treatmentPlanId: plan.id,
        treatmentPlanItemId: item.id,
        treatmentPlanItemStepId: item.steps[0].id,
      },
    )
    expect(fiche.status, `fiche: ${fiche.raw?.slice(0, 300)}`).toBe(200)

    const reread = await api.plan(plan.id)
    const line = reread.items[0]

    expect(
      [...(line.toothNumbers ?? [])].sort((a: number, b: number) => a - b),
      "the devis line still names only what was quoted",
    ).toEqual([13, 43])

    /*
     * The fiche's teeth are legitimately wider — that is the whole point, and the reason a union is wrong. This
     * asserts the two are DIFFERENT, so the client rule has something to distinguish; the frontend half of the
     * rule (`teethUnderTreatment`) is held by `check:responsive`.
     */
    const treated = [...(line.treatedToothNumbers ?? [])].sort((a: number, b: number) => a - b)
    expect(
      treated.length,
      `treatedToothNumbers is the whole SÉANCE's teeth (${treated.join(", ")}) — if it ever equals the devis ` +
        `line, this scenario has stopped exercising the distinction`,
    ).toBeGreaterThan(2)
    for (const stray of [27, 36, 37]) {
      expect(treated, `the fiche's own teeth include ${stray}`).toContain(stray)
      expect(line.toothNumbers ?? [], `…and the devis line must NOT`).not.toContain(stray)
    }
  })
})

test.describe("HP-4 · détacher une étape que facture une note @mutating @t0", () => {
  mutatingOnly()

  test("STEP-06/07 · the refusal names a remedy that WORKS, and following it clears the refusal", async ({
    api,
    arrange,
    patient,
  }) => {
    /*
     * ⚠️ **The refusal that survived the remedy it demanded.** « Détacher la fiche » refused with « Annulez la
     * facture (ou émettez un avoir) avant de corriger »; the dentist issued the avoir, retried, and got the
     * identical refusal — because the guard asked `RepresentsItsPlan(Status)` alone and a credit note is a
     * **separate aggregate** that leaves the invoice `Paid`. And « Annulez » was not available either:
     * `Invoice.CanCancel` refuses a note carrying a live payment. **Both remedies the message named were
     * unreachable**, so a fiche attached to the wrong step was permanent and the act could never be re-recorded.
     *
     * `Snapshot.IsSpent` / `StillBillsTheWork` is the fix, and this is the test that a remedy exists.
     */
    const p = await patient("Detacher")
    const act = await api.protocolAct(2)
    const started = await api.startTreatment({
      patientId: p.id,
      procedureTypeId: act.id,
      agreedTotal: Number(act.defaultCost) || 500,
      toothNumbers: [16],
      steps: null,
    })
    const plan = started.body?.value ?? started.body
    const item = plan.items[0]
    const step = item.steps[0]

    // A fiche on step 1, carrying real money so its note holds a live payment.
    const fiche = await arrange.fiche(
      p.id,
      [
        arrange.act({ procedureTypeId: act.id, name: act.name, cost: 0, teeth: [16] }),
        // A second, independently-billable act, so the séance has a total to collect.
        arrange.act({ name: "Consultation de contrôle", cost: 60 }),
      ],
      {
        amountPaid: 60,
        treatmentPlanId: plan.id,
        treatmentPlanItemId: item.id,
        treatmentPlanItemStepId: step.id,
      },
    )
    expect(fiche.status, `fiche: ${fiche.raw?.slice(0, 300)}`).toBe(200)

    const invoices = await api.invoices(`?patientId=${p.id}&pageSize=20`)
    expect(invoices.items, "the fiche's payment raised its note").toHaveLength(1)
    const note = invoices.items[0]
    expect(mil(note.amountCollected)).toBe(mil(60))

    // ── The refusal. ──
    const refused = await api.call(
      "POST",
      `/treatment-plans/${plan.id}/items/${item.id}/steps/${step.id}/undone`,
      {},
    )
    expect(
      refused.status,
      `detaching a step a live note bills must be refused: ${refused.raw?.slice(0, 300)}`,
    ).toBe(400)

    // ⚠️ The remedy it names must be the one that EXISTS. With money collected, cancelling is impossible
    // (`CanCancel`), so the message must say « avoir » — and say it for the whole remaining amount.
    expect(refused.raw, `the refusal must name a reachable remedy: ${refused.raw?.slice(0, 400)}`).toMatch(
      /avoir/i,
    )
    const cancelAttempt = await api.cancelInvoice(note.id, { reason: "E2E — is this even possible?" })
    expect(
      cancelAttempt.status,
      "…and cancelling really is unavailable, which is why naming it was the original defect",
    ).toBe(400)

    // ── Follow the remedy: a FULL avoir. ──
    const avoir = await api.avoir(note.id, { amount: 60, reason: "E2E — following the named remedy" })
    expect(avoir.status, `avoir: ${avoir.raw?.slice(0, 300)}`).toBeLessThan(300)

    // ── And the refusal must be gone. This is the assertion the defect failed. ──
    const afterAvoir = await api.call(
      "POST",
      `/treatment-plans/${plan.id}/items/${item.id}/steps/${step.id}/undone`,
      {},
    )
    expect(
      afterAvoir.status,
      `a fully-credited note bills nothing, so there is nothing left to protect — the refusal must clear. ` +
        `Got: ${afterAvoir.raw?.slice(0, 400)}`,
    ).toBeLessThan(300)

    const reread = await api.plan(plan.id)
    expect(
      reread.items[0].steps[0].doneDate ?? null,
      "the step is back to « à venir », so the act can be re-recorded",
    ).toBeNull()
  })
})
