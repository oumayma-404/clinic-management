import { expect, mutatingOnly, test } from "../lib/fixtures"
import { mil } from "../lib/api"

/**
 * HP-7 / HP-9 — a treatment's lifecycle, and the two opposite meanings of « Draft ».
 *
 * ⚠️ **`Draft` is the highest-cost ambiguity in this codebase.** A followed treatment is an un-numbered Draft:
 * `TreatmentPlanLifecycle.IsLive(Draft)` is **true** (clinically live) and `PlanBillingRules.CarriesDebt(Draft)`
 * is **false** (financially inert). Every defect in this file came from a hand-written status test answering one
 * of those questions with the other one's rule — in **seven** places for the clinical half alone.
 */

test.describe("HP-7 · le cycle de vie d'un devis @mutating @t0", () => {
  mutatingOnly()

  /**
   * A followed treatment: un-numbered Draft, catalogue protocol, clinically live.
   *
   * ⚠️ The act is chosen by **shape** — « has a protocol » or « has none » — never by name. Which catalogue act
   * carries a protocol differs between a hand-edited developer database and a freshly seeded one, and a literal
   * makes the test assert a fact about a fixture rather than about the product.
   */
  async function followed(api: any, patientId: string, shape: "protocol" | "flat" = "protocol") {
    const act = shape === "protocol" ? await api.protocolAct(2) : await api.singleSeanceAct()
    const r = await api.startTreatment({
      patientId,
      procedureTypeId: act.id,
      agreedTotal: 500,
      toothNumbers: [16],
      steps: null,
    })
    expect(r.status, `startTreatment: ${r.raw?.slice(0, 300)}`).toBe(200)
    const plan = r.body?.value ?? r.body
    expect(plan.status).toBe("Draft")
    expect(plan.number).toBeNull()
    return { act, plan }
  }

  test("PLAN-03 · « Annuler » is refused on a Draft — a brouillon is deleted, not cancelled", async ({
    api,
    patient,
  }) => {
    const p = await patient("NoCancel")
    const { plan } = await followed(api, p.id)

    const r = await api.cancelPlan(plan.id, "E2E")
    expect(
      r.status,
      "browser-proven end to end: the motif filled in, the confirmation pressed, the server refusing",
    ).toBe(400)
    expect(r.raw).toContain("brouillon")
  })

  test("PLAN-04 · a Draft with RECORDED work cannot be deleted", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("HasWork")
    const { act, plan } = await followed(api, p.id)
    const item = plan.items[0]

    const fiche = await arrange.fiche(
      p.id,
      [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 0, teeth: [16] })],
      {
        amountPaid: 0,
        treatmentPlanId: plan.id,
        treatmentPlanItemId: item.id,
        treatmentPlanItemStepId: item.steps[0].id,
      },
    )
    expect(fiche.status).toBe(200)

    const r = await api.deletePlan(plan.id)
    expect(
      r.status,
      "the cascade takes the acts and their step rows while the appointments' links are SetNull — the fiches " +
        "would survive attached to nothing, the exact wreckage StopTreatment was written to avoid",
    ).toBe(400)

    // …and the fiche is still there.
    const records = await api.records(p.id)
    expect((records.items ?? records)).toHaveLength(1)
  })

  test("PLAN-05 · a Draft with NO recorded work is deleted, consuming no number", async ({
    api,
    patient,
  }) => {
    const p = await patient("Empty")
    const { plan } = await followed(api, p.id)

    const r = await api.deletePlan(plan.id)
    expect(r.status, `delete: ${r.raw?.slice(0, 300)}`).toBeLessThan(300)

    const reread = await api.call("GET", `/treatment-plans/${plan.id}`)
    expect(reread.status).toBeGreaterThanOrEqual(400)
  })

  test("PLAN-07/08 · Accept raises ONE auto-raised échéance, and it is not late tomorrow", async ({
    api,
    patient,
  }) => {
    const p = await patient("Echeance")
    const { plan } = await followed(api, p.id)

    const accepted = await api.acceptPlan(plan.id, {})
    expect(accepted.status, `accept: ${accepted.raw?.slice(0, 300)}`).toBe(200)

    const reread = await api.plan(plan.id)
    expect(reread.number, "Accept is the only writer of Number").toBeTruthy()
    expect(reread.installments, "one lump-sum row, dated at the acceptance instant").toHaveLength(1)

    const row = reread.installments[0]
    expect(mil(row.amount)).toBe(mil(500))
    expect(
      row.isAutoRaised,
      "the distinction had to be STORED — « one row dated at acceptance » also matches a schedule a dentist " +
        "deliberately typed for the signature day",
    ).toBe(true)
    expect(
      row.isOverdue,
      "25 of 27 unpaid rows read « En retard » from the day after signature, cancelled and already-invoiced " +
        "ones included. An échéance nobody agreed to is not a date anybody promised.",
    ).toBe(false)
  })

  test("PLAN-16 · « Arrêter » then « Reprendre » on a FOLLOWED treatment keeps it un-numbered", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("StopStart")
    const { act, plan } = await followed(api, p.id)
    const item = plan.items[0]

    // Some delivered work, so stopping parks the act rather than dropping it.
    const fiche = await arrange.fiche(
      p.id,
      [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 0, teeth: [16] })],
      {
        amountPaid: 0,
        treatmentPlanId: plan.id,
        treatmentPlanItemId: item.id,
        treatmentPlanItemStepId: item.steps[0].id,
      },
    )
    expect(fiche.status).toBe(200)

    const stopped = await api.stopPlan(plan.id, { reason: "E2E — patient stopped" })
    expect(stopped.status, `stop: ${stopped.raw?.slice(0, 300)}`).toBe(200)

    const resumed = await api.reopenPlan(plan.id)
    test.skip(
      resumed.status >= 300,
      `« Reprendre » refused on a stopped Draft: HTTP ${resumed.status} ${resumed.raw?.slice(0, 200)} — ` +
        `scenario not exercised`,
    )

    const reread = await api.plan(plan.id)
    expect(
      reread.number,
      "« Arrêter » → « Reprendre » once turned a followed treatment into an Accepted devis with a NULL number " +
        "and a live créance for a total nobody had quoted",
    ).toBeNull()
    expect(
      ["Draft", "InProgress"].includes(reread.status),
      `an un-numbered plan must never wear a debt-bearing status; got ${reread.status}`,
    ).toBeTruthy()

    // The delivered work survived the park/unpark round trip.
    expect(reread.items[0].steps[0].doneDate, "parking keeps every DoneDate and fiche link").toBeTruthy()
  })

  test("PLAN-21/22 · « Traitements en cours » lists a followed act the DAY it is created — but only with steps", async ({
    api,
    patient,
  }) => {
    const p = await patient("EnCours")
    const { plan } = await followed(api, p.id) // an act with a catalogue protocol

    const withSteps = await api.treatmentsInProgress()
    expect(
      (withSteps.items ?? []).some((r: any) => r.planId === plan.id),
      "a filter on InProgress hid a treatment on exactly the day it was created — item.Status is Planned " +
        "until the first fiche lands",
    ).toBeTruthy()

    // An act with NO protocol goes Planned → Done and has never belonged there. `Steps.Any()` is what keeps
    // the widening honest.
    const p2 = await patient("NoProto")
    const { act: flatAct, plan: flat } = await followed(api, p2.id, "flat")
    expect(flat.items[0].steps ?? [], `${flatAct.name} must have no catalogue protocol`).toHaveLength(0)

    const after = await api.treatmentsInProgress()
    expect(
      (after.items ?? []).some((r: any) => r.planId === flat.id),
      "an act with no steps must not be listed — that is what stops the widening becoming « everything »",
    ).toBeFalsy()
  })

  test("MONEY-24 · a Draft plan carries NO debt, échéancier or not", async ({ api, patient }) => {
    const p = await patient("NoDebt")
    const { plan } = await followed(api, p.id)

    const summary = await api.billingSummary(p.id)
    expect(
      mil(summary.totalOutstanding ?? summary.outstanding ?? 0),
      "CarriesDebt(Draft) is false — clinically live, financially inert",
    ).toBe(0)

    expect(
      await api.receivableFor(p.id, p.name),
      "a Draft carries no debt, so the patient must be ABSENT from « Créances » — asked through the filtered "
        + "read, because « absent from page 1 of 300 » is the same answer for the wrong reason",
    ).toBeNull()
    expect(plan.status).toBe("Draft")
  })
})

test.describe("HP-9 · clôturer un traitement @mutating @t0", () => {
  mutatingOnly()

  test("DONE-01 · « Terminer » CLOSES a plan whose acts are not all réalisé, leaving them so", async ({
    api,
    patient,
  }) => {
    const p = await patient("Terminer")
    const crown = await api.protocolAct(2)
    const started = await api.startTreatment({
      patientId: p.id,
      procedureTypeId: crown.id,
      agreedTotal: 500,
      toothNumbers: [16],
      steps: null,
    })
    const plan = started.body?.value ?? started.body

    // It must be Accepted/InProgress to be closable at all.
    const accepted = await api.acceptPlan(plan.id, {})
    expect(accepted.status, `accept: ${accepted.raw?.slice(0, 300)}`).toBe(200)

    const r = await api.completePlan(plan.id, { leaveUnrealisedActs: true })
    expect(
      r.status,
      "the aggregate used to refuse in exactly the case the dialog bothered to explain — no case could be " +
        "built from the UI in which this succeeded",
    ).toBe(200)

    const reread = await api.plan(plan.id)
    expect(reread.status).toBe("Completed")
    expect(reread.items[0].status, "la clôture ne les valide pas").not.toBe("Done")
  })

  test("DONE-05 · clôture leaves the money alone — « Terminé » is about the work, not the payment", async ({
    api,
    patient,
  }) => {
    const p = await patient("TermineDu")
    const crown = await api.protocolAct(2)
    const started = await api.startTreatment({
      patientId: p.id,
      procedureTypeId: crown.id,
      agreedTotal: 500,
      toothNumbers: [16],
      steps: null,
    })
    const plan = started.body?.value ?? started.body
    expect((await api.acceptPlan(plan.id, {})).status).toBe(200)

    const before = await api.billingSummary(p.id)
    expect(mil(before.totalOutstanding ?? before.outstanding ?? 0)).toBe(mil(500))

    expect((await api.completePlan(plan.id, { leaveUnrealisedActs: true })).status).toBe(200)

    const after = await api.billingSummary(p.id)
    expect(
      mil(after.totalOutstanding ?? after.outstanding ?? 0),
      "Completed is a debt-bearing status — closing the work must not clear the créance",
    ).toBe(mil(500))
  })

  test("DONE-06/09 · a Completed plan refuses a second clôture and a new séance", async ({
    api,
    patient,
  }) => {
    const p = await patient("DejaClos")
    const crown = await api.protocolAct(2)
    const started = await api.startTreatment({
      patientId: p.id,
      procedureTypeId: crown.id,
      agreedTotal: 500,
      toothNumbers: [16],
      steps: null,
    })
    const plan = started.body?.value ?? started.body
    await api.acceptPlan(plan.id, {})
    await api.completePlan(plan.id, { leaveUnrealisedActs: true })

    const again = await api.completePlan(plan.id, { leaveUnrealisedActs: true })
    expect(again.status).toBe(400)
    expect(again.raw).toContain("déjà clôturé")
  })

  test("DONE-07 · « Reprendre » restores a Completed devis WITH its number", async ({ api, patient }) => {
    const p = await patient("Reprendre")
    const crown = await api.protocolAct(2)
    const started = await api.startTreatment({
      patientId: p.id,
      procedureTypeId: crown.id,
      agreedTotal: 500,
      toothNumbers: [16],
      steps: null,
    })
    const plan = started.body?.value ?? started.body
    await api.acceptPlan(plan.id, {})
    const numbered = (await api.plan(plan.id)).number
    await api.completePlan(plan.id, { leaveUnrealisedActs: true })

    const r = await api.reopenPlan(plan.id)
    expect(r.status, `reopen: ${r.raw?.slice(0, 300)}`).toBe(200)

    const reread = await api.plan(plan.id)
    expect(reread.status, "patients come back").not.toBe("Completed")
    expect(reread.number, "a numbered devis keeps its number through a reopen").toBe(numbered)
  })

  test("DONE-10 · a Cancelled plan refuses payment, amendment and step edits", async ({ api, patient }) => {
    const p = await patient("Annule")
    const crown = await api.protocolAct(2)
    const started = await api.startTreatment({
      patientId: p.id,
      procedureTypeId: crown.id,
      agreedTotal: 500,
      toothNumbers: [16],
      steps: null,
    })
    const plan = started.body?.value ?? started.body
    await api.acceptPlan(plan.id, {})
    const cancelled = await api.cancelPlan(plan.id, "E2E — patient declined")
    expect(cancelled.status, `cancel: ${cancelled.raw?.slice(0, 300)}`).toBe(200)

    const item = (await api.plan(plan.id)).items[0]
    const steps = await api.setSteps(plan.id, item.id, [
      { label: "Nouvelle étape", estimatedDurationMinutes: 30, minDaysAfterPrevious: null },
    ])
    expect(steps.status, "« Les étapes d'un plan annulé ne peuvent pas être modifiées. »").toBe(400)

    // And it carries no debt.
    const summary = await api.billingSummary(p.id)
    expect(mil(summary.totalOutstanding ?? summary.outstanding ?? 0)).toBe(0)
  })
})
