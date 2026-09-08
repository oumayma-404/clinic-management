import { expect, mutatingOnly, test } from "../lib/fixtures"
import { mil } from "../lib/api"

/**
 * HP-5 / HP-6 — the fiche de soins' money, and the eleven surfaces one `Enregistrer` moves.
 *
 * See `features/e2e-hot-paths/scenarios.md` § HP-5 and `coupling-matrix.md` § 2. Scenario ids are the test
 * names, so a red line in the log points straight at the row that describes it.
 *
 * ⚠️ **These are the invariants that fail SILENTLY.** Not one of the defects behind them raised an error: a
 * fiche saved 999 DT against a 40 DT act and displayed the money as collected; a carried act overtyped left the
 * patient owing the same work twice on two unlinked documents; a mixed séance under-billed the real act by
 * whatever share went to the act that cannot hold money. So every assertion here reads the **figure**, never a
 * toast.
 */

test.describe("HP-5 · fiche de soins — money @mutating @t0", () => {
  mutatingOnly()

  test("FICHE-08 · « Payé » above the acts total is REFUSED, and the save does not happen", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Argent")
    const consult = await api.singleSeanceAct()

    const r = await arrange.fiche(
      p.id,
      [arrange.act({ procedureTypeId: consult.id, name: consult.name, cost: 40 })],
      { amountPaid: 999 },
    )

    // Refused — and refused *pre-commit*, which is the whole point: the rule used to live post-commit in
    // `BillDentalRecordCommand.ResolvePayment` and was demoted to a warning inside an HTTP 200.
    expect(r.status, `expected a refusal, got ${r.status}: ${r.raw.slice(0, 300)}`).toBe(400)
    expect(r.raw).toContain("dépasse le total de la séance")

    // « Refusé » has to mean the save did not happen.
    const after = await api.records(p.id)
    expect(after.items ?? after, "a refused fiche must not be persisted").toHaveLength(0)
  })

  test("FICHE-10 · « Payé » = 0 saves the fiche, raises no invoice, and is not an error", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Zero")
    const consult = await api.singleSeanceAct()

    const r = await arrange.fiche(
      p.id,
      [arrange.act({ procedureTypeId: consult.id, name: consult.name, cost: 40 })],
      { amountPaid: 0 },
    )
    expect(r.status).toBe(200)

    const saved = r.body?.value ?? r.body
    expect(saved.billing?.outcome, "zero collected is NotCollected, a legitimate outcome").toBe(
      "NotCollected",
    )

    const invoices = await api.invoices(`?patientId=${p.id}&pageSize=50`)
    expect(invoices.items, "nothing collected ⇒ no note d'honoraires").toHaveLength(0)
  })

  test("FICHE-11 · a partial payment bills the FULL total and leaves the rest as a créance", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Partiel")
    const detartrage = await api.singleSeanceAct()

    const r = await arrange.fiche(
      p.id,
      [arrange.act({ procedureTypeId: detartrage.id, name: detartrage.name, cost: 100 })],
      { amountPaid: 60 },
    )
    expect(r.status).toBe(200)

    const invoices = await api.invoices(`?patientId=${p.id}&pageSize=50`)
    expect(invoices.items).toHaveLength(1)
    const note = invoices.items[0]

    // The note bills the work, not the payment — that is what makes the remainder a créance rather than
    // vanishing.
    expect(mil(note.totalTtc)).toBe(mil(100))
    expect(mil(note.amountCollected)).toBe(mil(60))
    expect(mil(note.outstanding)).toBe(mil(40))

    // …and the same 40 on the patient's own summary, which is a different read entirely.
    const summary = await api.billingSummary(p.id)
    expect(
      mil(summary.totalOutstanding ?? summary.outstanding ?? 0),
      `« Solde patient » must agree with the note: ${JSON.stringify(summary)}`,
    ).toBe(mil(40))
  })

  test("FICHE-12 · a fiche recorded two days late books its cash to THAT day, not today", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Retard")
    const consult = await api.singleSeanceAct()

    const twoDaysAgo = arrange.localDay(-2)
    const r = await arrange.fiche(
      p.id,
      [arrange.act({ procedureTypeId: consult.id, name: consult.name, cost: 40 })],
      { amountPaid: 40, interventionDate: twoDaysAgo },
    )
    expect(r.status).toBe(200)

    const day = twoDaysAgo.slice(0, 10)
    const surname = p.name.split(" ")[1]
    const onDay = await api.caisseLedger(day, day)
    const rows = onDay.movements ?? []
    const mine = rows.filter((m: any) => (m.patientName ?? "").includes(surname))

    expect(
      mine.length,
      `the movement must land on ${day}, the séance's own date — PaidOn is record.InterventionDate, ` +
        `not now. That day held ${rows.length} rows.`,
    ).toBeGreaterThan(0)
    expect(mil(mine[0].amount)).toBe(mil(40))
    expect(mine[0].direction).toBe("In")

    // And it must NOT also be on today, which is what a `DateTime.Now` would produce.
    const today = arrange.localDay(0).slice(0, 10)
    const onToday = await api.caisseLedger(today, today)
    const alsoToday = (onToday.movements ?? []).filter((m: any) =>
      (m.patientName ?? "").includes(surname),
    )
    expect(alsoToday, "the same payment must not appear in today's caisse as well").toHaveLength(0)
  })

  test("FICHE-13 · a cheque reaches « Chèques à encaisser », not « dont espèces »", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Cheque")
    const detartrage = await api.singleSeanceAct()

    const r = await arrange.fiche(
      p.id,
      [arrange.act({ procedureTypeId: detartrage.id, name: detartrage.name, cost: 60 })],
      {
        amountPaid: 60,
        paymentMethod: "Cheque",
        chequeNumber: "E2E-7788",
        chequeBankName: "BIAT",
        chequeDueDate: arrange.localDay(30),
      },
    )
    expect(r.status).toBe(200)

    const cheques = await api.cheques("?pageSize=200")
    const mine = (cheques.items ?? []).find((c: any) => c.chequeNumber === "E2E-7788")
    expect(
      mine,
      "a fiche settled by cheque used to produce a payment indistinguishable from notes in the drawer",
    ).toBeTruthy()
    expect(mil(mine.amount)).toBe(mil(60))
  })
})

test.describe("HP-5b · an act the TREATMENT prices @mutating @t0", () => {
  mutatingOnly()

  /**
   * Builds « a 3-séance crown followed for this patient, booked on step 1 ». Returns everything the assertions
   * need, because the whole cluster below shares this precondition and arranging it through the UI would
   * consume the scenario under test.
   */
  async function crownOnStepOne(api: any, arrange: any, patientId: string) {
    const crown = await api.protocolAct(2)
    const started = await api.startTreatment({
      patientId,
      procedureTypeId: crown.id,
      agreedTotal: 500,
      toothNumbers: [16],
      steps: null, // null = the catalogue protocol applies
    })
    expect(started.status, `startTreatment: ${started.raw?.slice(0, 300)}`).toBe(200)
    const plan = started.body?.value ?? started.body
    const item = plan.items[0]
    return { crown, plan, item, step1: item.steps[0] }
  }

  test("FICHE-21 · the server IMPOSES 0 on a devis-carried act, whatever the client sends", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Portee")
    const { crown, plan, item, step1 } = await crownOnStepOne(api, arrange, p.id)

    // The browser locks this field; the point of the scenario is that the *server* does too. 500 is what an
    // overtyped field would send.
    const r = await arrange.fiche(
      p.id,
      [arrange.act({ procedureTypeId: crown.id, name: crown.name, cost: 500, teeth: [16] })],
      {
        amountPaid: 0,
        treatmentPlanId: plan.id,
        treatmentPlanItemId: item.id,
        treatmentPlanItemStepId: step1.id,
      },
    )
    expect(r.status).toBe(200)
    const saved = r.body?.value ?? r.body

    const act = saved.acts[0]
    expect(mil(act.cost), "PlanCarriedActPricing imposes 0 — the act is priced once, on the treatment").toBe(0)
    expect(
      act.unitCost == null || mil(act.unitCost) === 0,
      "`UnitCost` goes to 0 beside `Cost`, or reopening the fiche restores the fee",
    ).toBeTruthy()

    // …and therefore no note d'honoraires exists for work the treatment already prices.
    const invoices = await api.invoices(`?patientId=${p.id}&pageSize=50`)
    expect(
      invoices.items,
      "a note here carries no TreatmentPlanId, so BilledPlanIds cannot de-duplicate it — the patient would owe the act twice",
    ).toHaveLength(0)
  })

  test("FICHE-30/31 · step 1 of 3 charts NOTHING and keeps the « à traiter » diagnosis", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Chart1")
    const { crown, plan, item, step1 } = await crownOnStepOne(api, arrange, p.id)

    const before = await api.toothCondition(p.id, 16)

    const r = await arrange.fiche(
      p.id,
      [
        arrange.act({
          procedureTypeId: crown.id,
          name: crown.name,
          cost: 500,
          teeth: [16],
          resultingCondition: crown.resultingCondition,
        }),
      ],
      {
        amountPaid: 0,
        treatmentPlanId: plan.id,
        treatmentPlanItemId: item.id,
        treatmentPlanItemStepId: step1.id,
      },
    )
    expect(r.status).toBe(200)

    const after = await api.toothCondition(p.id, 16)

    // A fresh fixture patient has nothing charted, so `before` is null and this asserts « still nothing ».
    // Stated as a comparison rather than `toBeNull` so the test keeps meaning if the fixture ever starts
    // seeding a chart.
    expect(
      after,
      "seven live rows charted a finished restoration from a step 1 of 2 — three claimed implants that did not exist",
    ).toBe(before)
    expect(before, "fixture sanity: a new patient's tooth 16 starts unc harted").toBeNull()
  })

  test("FICHE-32 · the LAST step charts the end state; the earlier ones did not", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Chart3")
    const { crown, plan, item } = await crownOnStepOne(api, arrange, p.id)

    const chartedAfter: Array<string | null> = []
    for (const [i, step] of item.steps.entries()) {
      const r = await arrange.fiche(
        p.id,
        [
          arrange.act({
            procedureTypeId: crown.id,
            name: crown.name,
            cost: 500,
            // Every séance names the teeth. That is what the fiche's own prefill produces in the browser
            // (`TreatedToothNumbers`) — and the *reason* it has to: the chart is written when the act
            // finishes, so a last fiche with no teeth charts nothing at all. That half is a client
            // behaviour and is asserted in `fiche-browser.spec.ts` · FICHE-33, not here.
            teeth: [16, 26],
            resultingCondition: crown.resultingCondition,
          }),
        ],
        {
          amountPaid: 0,
          interventionDate: arrange.localDay(-2 + i),
          treatmentPlanId: plan.id,
          treatmentPlanItemId: item.id,
          treatmentPlanItemStepId: step.id,
        },
      )
      expect(r.status, `séance ${i + 1}: ${r.raw?.slice(0, 300)}`).toBe(200)
      chartedAfter.push(await api.toothCondition(p.id, 16))
    }

    // Withheld on every séance but the last — the seven live rows written from a step 1 of 2.
    expect(
      chartedAfter.slice(0, -1),
      "an end state charted before the act is Done describes a mouth the patient does not have",
    ).toEqual(item.steps.slice(0, -1).map(() => null))

    // …and asserted once it is finished, on both teeth.
    expect(chartedAfter.at(-1)).toBe(crown.resultingCondition)
    expect(await api.toothCondition(p.id, 26)).toBe(crown.resultingCondition)

    // The act is Done; the PLAN stays a Draft, because a followed treatment is clinically live and
    // financially inert however many séances it records (`AdvanceAfterWorkRecorded`). Asserting
    // « Completed » here would be asserting the opposite of the rule.
    const reread = await api.plan(plan.id)
    expect(reread.items[0].status, "the act finishes when its last step lands").toBe("Done")
    expect(
      reread.status,
      "a followed treatment is an un-numbered Draft — clinically live, financially inert",
    ).toBe("Draft")
    expect(reread.number, "Accept is the only writer of Number").toBeNull()
  })

  test("FICHE-23 · a MIXED séance: the typed total never moves onto the carried act", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Mixte")
    const { crown, plan, item, step1 } = await crownOnStepOne(api, arrange, p.id)
    const detartrage = await api.singleSeanceAct()

    // The crown's step 1 (carried, must be 0) plus an independent détartrage done the same day. A client that
    // split a typed « Total » across both would under-bill the détartrage by the crown's share.
    const r = await arrange.fiche(
      p.id,
      [
        arrange.act({ procedureTypeId: crown.id, name: crown.name, cost: 0, teeth: [16] }),
        arrange.act({ procedureTypeId: detartrage.id, name: detartrage.name, cost: 150 }),
      ],
      {
        amountPaid: 0,
        treatmentPlanId: plan.id,
        treatmentPlanItemId: item.id,
        treatmentPlanItemStepId: step1.id,
      },
    )
    expect(r.status).toBe(200)
    const saved = r.body?.value ?? r.body

    const carried = saved.acts.find((a: any) => a.procedureTypeId === crown.id)
    const real = saved.acts.find((a: any) => a.procedureTypeId === detartrage.id)

    expect(mil(carried.cost), "the carried act is 0 by rule").toBe(0)
    expect(
      mil(real.cost),
      "the détartrage keeps the whole 150 — a share given to the crown is money the séance silently loses",
    ).toBe(mil(150))
    expect(mil(saved.cost ?? saved.totalCost ?? 0)).toBe(mil(150))
  })

  test("FICHE-39 · only ONE act is zeroed — a second independent crown the same day still bills", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("DeuxCour")
    const { crown, plan, item, step1 } = await crownOnStepOne(api, arrange, p.id)

    // Two rows of the SAME catalogue act: one the devis carries (tooth 16), one independent (tooth 46).
    const r = await arrange.fiche(
      p.id,
      [
        arrange.act({ procedureTypeId: crown.id, name: crown.name, cost: 500, teeth: [16] }),
        arrange.act({ procedureTypeId: crown.id, name: crown.name, cost: 500, teeth: [46] }),
      ],
      {
        amountPaid: 0,
        treatmentPlanId: plan.id,
        treatmentPlanItemId: item.id,
        treatmentPlanItemStepId: step1.id,
      },
    )
    expect(r.status).toBe(200)
    const saved = r.body?.value ?? r.body

    const zeroed = saved.acts.filter((a: any) => mil(a.cost) === 0)
    expect(
      zeroed,
      "zeroing every act sharing the catalogue procedure silently un-bills an independent crown — a money loss with no error",
    ).toHaveLength(1)
    expect(mil(saved.cost ?? 0)).toBe(mil(500))
  })
})
