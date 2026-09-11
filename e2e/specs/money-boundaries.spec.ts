import { expect, mutatingOnly, test } from "../lib/fixtures"
import { mil } from "../lib/api"

/**
 * HP-10 — the day boundary, and the devis→facture bridge.
 *
 * ⚠️ **Tunisia is UTC+1 and `ClinicClock` is the only thing that knows it.** A clinic's day runs
 * 23:00Z → 22:59:59.999…Z, so every money read has a window whose ends are the two instants a mistake shows up
 * at. The recorded shape: `EndOfLocalDayUtc` is the *next* midnight (exclusive) while every money read is
 * inclusive at both ends, so using it made **a midnight payment land in two adjacent periods** — counted twice
 * across a month boundary, with no error and no way to notice except by adding the days up by hand.
 *
 * ⚠️ **These tests read the window from the API rather than computing it.** `/billing/caisse` reports its own
 * `fromDate`/`toDate`, so a payment can be placed one minute inside each end without this file re-deriving
 * Tunisia's offset — which would be the same arithmetic under test, asserted against itself.
 */

test.describe("HP-10 · la frontière du jour @mutating @t0", () => {
  mutatingOnly()

  /** Books `amount` into the till at an exact UTC instant, through a fiche — the ordinary money path. */
  async function payAt(api: any, arrange: any, patientId: string, instant: Date, amount: number) {
    const act = await api.singleSeanceAct()
    const r = await arrange.fiche(
      patientId,
      [arrange.act({ procedureTypeId: act.id, name: act.name, cost: amount })],
      { amountPaid: amount, interventionDate: instant.toISOString() },
    )
    expect(r.status, `fiche at ${instant.toISOString()}: ${r.raw?.slice(0, 300)}`).toBe(200)
    return r.body?.value ?? r.body
  }

  const dayIso = (offset: number) => {
    const d = new Date()
    d.setDate(d.getDate() + offset)
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`
  }

  /** How many of this patient's movements the given clinic-local day holds. */
  async function movementsOn(api: any, day: string, patientId: string) {
    const ledger = await api.caisseLedger(day, day)
    return (ledger.movements ?? []).filter((m: any) => m.patientId === patientId)
  }

  test("MONEY-12 · a payment just after local midnight belongs to the NEW day, and only to it", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Minuit")
    const today = dayIso(0)
    const yesterday = dayIso(-1)

    // The clinic's own window for today — asked, not computed.
    const window = await api.caisse(today, today)
    const justAfterMidnight = new Date(window.fromDate)
    justAfterMidnight.setUTCMinutes(justAfterMidnight.getUTCMinutes() + 1)

    await payAt(api, arrange, p.id, justAfterMidnight, 20)

    const onToday = await movementsOn(api, today, p.id)
    const onYesterday = await movementsOn(api, yesterday, p.id)

    expect(onToday, `00:01 local must be today's money (${justAfterMidnight.toISOString()})`).toHaveLength(1)
    expect(mil(onToday[0].amount)).toBe(mil(20))
    expect(
      onYesterday,
      "…and must NOT also be yesterday's — one payment, one day. Two adjacent periods claiming it is the " +
        "EndOfLocalDayUtc defect",
    ).toHaveLength(0)
  })

  test("MONEY-13 · a payment just before local midnight belongs to the OLD day, and only to it", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Veille")
    const today = dayIso(0)
    const tomorrow = dayIso(1)

    const window = await api.caisse(today, today)
    // One minute inside the far end. `toDate` is the LAST TICK of the local day, inclusive — not the next
    // midnight, which is the whole distinction `LastTickOfLocalDayUtc` exists for.
    const justBeforeMidnight = new Date(window.toDate)
    justBeforeMidnight.setUTCMinutes(justBeforeMidnight.getUTCMinutes() - 1)

    await payAt(api, arrange, p.id, justBeforeMidnight, 30)

    const onToday = await movementsOn(api, today, p.id)
    const onTomorrow = await movementsOn(api, tomorrow, p.id)

    expect(onToday, `23:59 local must be today's money (${justBeforeMidnight.toISOString()})`).toHaveLength(1)
    expect(mil(onToday[0].amount)).toBe(mil(30))
    expect(onTomorrow, "…and must not leak into tomorrow").toHaveLength(0)
  })

  test("MONEY-12b · the day's TOTAL and its LEDGER agree at both ends of the window", async ({
    api,
    arrange,
    patient,
  }) => {
    /*
     * The totals and the lines are two different reads over the same window — « the lines and the totals always
     * describe the same period », as the controller puts it. A boundary that is inclusive in one and exclusive
     * in the other is invisible on an ordinary afternoon and wrong on exactly these two payments.
     */
    const p = await patient("DeuxBords")
    const today = dayIso(0)
    const window = await api.caisse(today, today)

    const start = new Date(window.fromDate)
    start.setUTCMinutes(start.getUTCMinutes() + 1)
    const end = new Date(window.toDate)
    end.setUTCMinutes(end.getUTCMinutes() - 1)

    const before = await api.caisse(today, today)
    await payAt(api, arrange, p.id, start, 20)
    await payAt(api, arrange, p.id, end, 30)
    const after = await api.caisse(today, today)

    expect(
      mil(after.cashIn) - mil(before.cashIn),
      "both boundary payments must be in the day's total exactly once",
    ).toBe(mil(50))

    const mine = await movementsOn(api, today, p.id)
    expect(mine, "and both must be in the ledger").toHaveLength(2)
    expect(mine.reduce((t: number, m: any) => t + mil(m.amount), 0)).toBe(mil(50))

    // The by-method cut is the same money again.
    const byMethod = (after.cashInByMethod ?? []).reduce((t: number, m: any) => t + mil(m.amount), 0)
    expect(byMethod).toBe(mil(after.cashIn))
  })
})

test.describe("HP-10 · le pont devis → note @mutating @t0", () => {
  mutatingOnly()

  /** An accepted devis for `total`, with nothing recorded against it yet. */
  async function acceptedPlan(api: any, patientId: string, total = 500) {
    const act = await api.protocolAct(2)
    const started = await api.startTreatment({
      patientId,
      procedureTypeId: act.id,
      agreedTotal: total,
      toothNumbers: [16],
      steps: null,
    })
    expect(started.status, `startTreatment: ${started.raw?.slice(0, 300)}`).toBe(200)
    const plan = started.body?.value ?? started.body
    const accepted = await api.acceptPlan(plan.id, {})
    expect(accepted.status, `accept: ${accepted.raw?.slice(0, 300)}`).toBe(200)
    return { act, plan: await api.plan(plan.id) }
  }

  const balanceOf = async (api: any, patientId: string) => {
    const s = await api.billingSummary(patientId)
    return mil(s.totalOutstanding ?? s.outstanding ?? 0)
  }

  test("MONEY-08/23 · a bridged plan is counted ONCE, through the note", async ({ api, patient }) => {
    const p = await patient("Pont")
    const { plan } = await acceptedPlan(api, p.id, 500)

    expect(await balanceOf(api, p.id), "the accepted devis owes its total").toBe(mil(500))

    // The devis→facture bridge. It produces a DRAFT invoice, which represents nothing yet…
    const bridged = await api.call("POST", `/invoices/from-plan/${plan.id}`, undefined)
    expect(bridged.status, `from-plan: ${bridged.raw?.slice(0, 300)}`).toBeLessThan(300)
    const note = bridged.body?.value ?? bridged.body

    expect(note.treatmentPlanId, "the bridge names the plan it came from").toBe(plan.id)
    expect(
      await balanceOf(api, p.id),
      "a Draft note represents nothing, so the plan still carries its own balance — counted once",
    ).toBe(mil(500))

    // …and once issued, the note speaks for the plan and the plan drops out.
    const issued = await api.call("POST", `/invoices/${note.id}/issue`, {})
    expect(issued.status, `issue: ${issued.raw?.slice(0, 300)}`).toBe(200)

    expect(
      await balanceOf(api, p.id),
      "still 500 — never 1 000. `BilledPlanIds` drops the plan so the note is the single claim",
    ).toBe(mil(500))

    const row = await api.receivableFor(p.id, p.name)
    expect(mil(row?.totalOutstanding ?? 0), "« Créances » must agree with « Solde patient »").toBe(mil(500))
  })

  test("MONEY-26 · a bridged plan's collections reach la caisse ONCE", async ({ api, patient, arrange }) => {
    const p = await patient("PontCaisse")
    const { plan } = await acceptedPlan(api, p.id, 500)

    // Collect 200 on the devis' échéancier.
    const installment = plan.installments[0]
    const paid = await api.call(
      "POST",
      `/treatment-plans/${plan.id}/installments/${installment.id}/payments`,
      { amount: 200, method: "Cash", paidOn: arrange.localDay(0) },
    )
    expect(paid.status, `installment payment: ${paid.raw?.slice(0, 300)}`).toBeLessThan(300)

    const day = arrange.localDay(0).slice(0, 10)
    const afterPlanPayment = await api.caisseLedger(day, day)
    const mine = (afterPlanPayment.movements ?? []).filter((m: any) => m.patientId === p.id)
    expect(mine, "one payment, one movement").toHaveLength(1)
    expect(mil(mine[0].amount)).toBe(mil(200))

    expect(await balanceOf(api, p.id), "500 quoted, 200 collected").toBe(mil(300))

    /*
     * ⚠️ Now bridge and issue. `PlanBillingRules` excludes a bridged plan's **collected cash as well as its
     * outstanding** — a deliberate reversal, correct only because the bridge carries the money across when the
     * invoice is issued. If it carried nothing, suppressing the plan would erase real money from the caisse
     * rather than de-duplicate it. So the till must read the same 200 afterwards: not 0, not 400.
     */
    const bridged = await api.call("POST", `/invoices/from-plan/${plan.id}`, undefined)
    expect(bridged.status, `from-plan: ${bridged.raw?.slice(0, 300)}`).toBeLessThan(300)
    const note = bridged.body?.value ?? bridged.body
    const issued = await api.call("POST", `/invoices/${note.id}/issue`, {})
    expect(issued.status, `issue: ${issued.raw?.slice(0, 400)}`).toBe(200)

    const afterBridge = await api.caisseLedger(day, day)
    const stillMine = (afterBridge.movements ?? []).filter((m: any) => m.patientId === p.id && !m.isVoided)
    expect(
      stillMine.reduce((t: number, m: any) => t + mil(m.amount), 0),
      "the 200 received is 200 in the till, before and after the bridge — never 0 and never 400",
    ).toBe(mil(200))

    // And the patient still owes exactly the remaining 300, on whichever document now owns it.
    expect(await balanceOf(api, p.id)).toBe(mil(300))
  })

  test("PLAN-14 · a bridged plan REFUSES an amendment, naming the reason", async ({ api, patient }) => {
    const p = await patient("PontAmend")
    const { plan } = await acceptedPlan(api, p.id, 500)

    const bridged = await api.call("POST", `/invoices/from-plan/${plan.id}`, undefined)
    const note = bridged.body?.value ?? bridged.body
    expect((await api.call("POST", `/invoices/${note.id}/issue`, {})).status).toBe(200)

    const r = await api.call("POST", `/treatment-plans/${plan.id}/amend`, {
      items: [{ id: null, designationFr: "Acte ajouté après coup", plannedCost: 120, toothNumbers: [] }],
    })

    /*
     * ⚠️ This refusal is the *statement* of the rule the whole continuation fix turns on: an act added to a
     * bridged plan « would be invisible in every balance ». `ContinueRecordedActCommand` reached that state by
     * another door and now cannot.
     */
    expect(r.status, `amend on a bridged plan: ${r.raw?.slice(0, 300)}`).toBe(400)
    expect(mil((await api.plan(plan.id)).totalPlanned), "the refusal must mean nothing was added").toBe(mil(500))
  })

  test("PLAN-15 · an act already réalisé cannot be removed from the devis", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("ActeFait")
    const { act, plan } = await acceptedPlan(api, p.id, 500)
    const item = plan.items[0]

    // Record every step, so the act is Done.
    for (const [i, step] of item.steps.entries()) {
      const fiche = await arrange.fiche(
        p.id,
        [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 0, teeth: [16] })],
        {
          amountPaid: 0,
          interventionDate: arrange.localDay(-item.steps.length + i),
          treatmentPlanId: plan.id,
          treatmentPlanItemId: item.id,
          treatmentPlanItemStepId: step.id,
        },
      )
      expect(fiche.status, `séance ${i + 1}: ${fiche.raw?.slice(0, 300)}`).toBe(200)
    }

    const reread = await api.plan(plan.id)
    expect(reread.items[0].status, "every step landed, so the act is finished").toBe("Done")

    // Amend the plan to drop that act.
    const r = await api.call("POST", `/treatment-plans/${plan.id}/amend`, { items: [] })
    expect(
      r.status,
      "an act with delivered work behind it cannot leave the devis — its fiches would point at nothing",
    ).toBe(400)
  })
})

test.describe("HP-6 · éditer une fiche — ce qui doit survivre @mutating @t0", () => {
  mutatingOnly()

  test("FEDIT-11 · editing ONLY the note keeps all three acts and all three prices", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("TroisActes")
    const a = await api.singleSeanceAct()
    const consult = await api.procedureNamed("Consultation / examen bucco-dentaire")
    const radio = await api.procedureNamed("Radiographie rétro-alvéolaire")

    const created = await arrange.fiche(
      p.id,
      [
        arrange.act({ procedureTypeId: a.id, name: a.name, cost: 70 }),
        arrange.act({ procedureTypeId: consult.id, name: consult.name, cost: 45 }),
        arrange.act({ procedureTypeId: radio.id, name: radio.name, cost: 25 }),
      ],
      { amountPaid: 0 },
    )
    expect(created.status).toBe(200)
    const fiche = created.body?.value ?? created.body
    expect(fiche.acts).toHaveLength(3)

    /*
     * ⚠️ `SetActs` **replaces the whole list**, and it regenerates every act id on every save — so there is no
     * before/after identity to diff and a client that resends a list omitting the prices restores each act to
     * its **tarif**, silently. The gesture here is the ordinary one: change the note, resend everything else
     * exactly as read.
     */
    const r = await api.updateRecord(p.id, fiche.id, {
      interventionDate: fiche.interventionDate,
      amountPaid: fiche.amountPaid,
      isAdultTeeth: true,
      notes: ["Le patient signale une sensibilité au froid."],
      importantNotes: [],
      version: fiche.version,
      acts: fiche.acts.map((x: any) => ({
        procedureTypeId: x.procedureTypeId,
        procedureName: x.procedureName,
        cost: x.cost,
        unitCost: x.unitCost,
        isPerTooth: x.isPerTooth,
        toothNumbers: x.toothNumbers ?? [],
        ponticToothNumbers: x.ponticToothNumbers ?? [],
        resultingCondition: x.resultingCondition,
        surfaces: x.surfaces,
        note: x.note,
      })),
    })
    expect(r.status, `re-save: ${r.raw?.slice(0, 300)}`).toBe(200)

    const saved = r.body?.value ?? r.body
    expect(saved.acts, "all three acts survive").toHaveLength(3)
    expect(
      saved.acts.map((x: any) => mil(x.cost)).sort((x: number, y: number) => x - y),
      "and each keeps its own price — 45 and 25 are not the tarifs of those acts",
    ).toEqual([mil(25), mil(45), mil(70)])
    expect(mil(saved.cost)).toBe(mil(140))
  })

  test("FEDIT-17 · deleting a fiche linked to a devis step un-marks the step and withdraws the chart", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("SupprFiche")
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

    // One fiche per step, so the act finishes and charts.
    let last: any = null
    for (const [i, step] of item.steps.entries()) {
      const r = await arrange.fiche(
        p.id,
        [
          arrange.act({
            procedureTypeId: act.id,
            name: act.name,
            cost: 0,
            teeth: [16],
            resultingCondition: act.resultingCondition,
          }),
        ],
        {
          amountPaid: 0,
          interventionDate: arrange.localDay(-item.steps.length + i),
          treatmentPlanId: plan.id,
          treatmentPlanItemId: item.id,
          treatmentPlanItemStepId: step.id,
        },
      )
      expect(r.status, `séance ${i + 1}: ${r.raw?.slice(0, 300)}`).toBe(200)
      last = r.body?.value ?? r.body
    }

    expect(await api.toothCondition(p.id, 16), "the finished act charted its end state").toBe(
      act.resultingCondition,
    )
    expect((await api.plan(plan.id)).items[0].status).toBe("Done")

    // Delete the LAST fiche — the one that finished the act.
    const del = await api.call("DELETE", `/patients/${p.id}/dental-records/${last.id}`)
    expect(del.status, `delete: ${del.raw?.slice(0, 300)}`).toBeLessThan(300)

    const reread = await api.plan(plan.id)
    expect(
      reread.items[0].status,
      "the act is no longer finished, because the séance that finished it is gone",
    ).not.toBe("Done")
    expect(
      reread.items[0].steps.at(-1).doneDate ?? null,
      "and its step is un-marked rather than left pointing at a deleted fiche",
    ).toBeNull()
    expect(
      await api.toothCondition(p.id, 16),
      "the chart's assertion is withdrawn with the evidence for it",
    ).toBeNull()
  })
})
