import { expect, mutatingOnly, test } from "../lib/fixtures"
import { mil } from "../lib/api"

/**
 * HP-7 / HP-12 — an échéance nobody agreed to, and a séance that says what it WAS.
 *
 * ⚠️ **« En retard » had stopped meaning anything.** Measured on the dev database: **25 of 27** unpaid
 * échéances flagged late, across every status — including devis a note d'honoraires had already billed (whose
 * échéancier can no longer take money at all) and devis that had been **cancelled**. A badge that fires on
 * almost every row is a badge a practice stops reading, which is exactly what had happened.
 *
 * The cause was never the badge. `TreatmentPlan.Accept` raises **one lump-sum échéance for the whole total,
 * dated at the acceptance instant**, when the dentist supplied no schedule — and that row is a **ledger
 * container, not a promise**: a payment needs an échéance to attach to. Nobody agreed its date.
 *
 * ⚠️ **The distinction had to be STORED, not guessed.** « One row, dated at acceptance » also matches a schedule
 * a dentist deliberately typed for the signature day, and silencing that destroys the only thing an échéancier
 * is for. Hence `Installment.IsAutoRaised`, false by default so every hand-entered row is an agreed date without
 * any caller saying so, and set by the **two** system writers only (`Accept`, `RespreadSchedule`).
 */

test.describe("HP-7 · une échéance que personne n'a promise @mutating @t0", () => {
  mutatingOnly()

  test("PLAN-08/09 · same overdue DATE, opposite lateness — because one was agreed and one was not", async ({
    api,
    arrange,
    patient,
  }) => {
    /*
     * ⚠️ **This is the whole rule in one test, and it is the only shape that can prove it.** Two devis, each
     * with a single échéance dated *yesterday*: one raised by `Accept` (nobody typed that date), one typed by a
     * dentist. A rule that keys on the date alone must call both late; a rule that keys on the date and the
     * amount and the row count must still call both late. Only `IsAutoRaised` separates them — which is why it
     * is stored rather than derived.
     *
     * Dated yesterday rather than today, because « late » is a fact about a date that has passed and this test
     * must not have to wait until tomorrow to be true.
     */
    const act = await api.protocolAct(2)

    // ── A · the auto-raised one: accept with no schedule at all. ──
    const pAuto = await patient("AutoEch")
    const startedAuto = await api.startTreatment({
      patientId: pAuto.id,
      procedureTypeId: act.id,
      agreedTotal: 300,
      toothNumbers: [16],
      steps: null,
    })
    const planAuto = startedAuto.body?.value ?? startedAuto.body
    expect((await api.acceptPlan(planAuto.id, {})).status).toBe(200)

    const autoRow = (await api.plan(planAuto.id)).installments[0]
    expect(autoRow.isAutoRaised, "Accept is one of only two writers of this flag").toBe(true)
    expect(
      autoRow.isOverdue,
      "an échéance the system raised is not a date anybody promised, so it can never be late",
    ).toBe(false)

    // ── B · the agreed one: a devis created WITH a schedule the dentist typed, dated yesterday. ──
    const pTyped = await patient("TypedEch")
    const createdTyped = await api.call("POST", "/treatment-plans", {
      patientId: pTyped.id,
      title: "Devis à échéancier convenu",
      items: [
        {
          procedureTypeId: act.id,
          designationFr: act.name,
          plannedCost: 300,
          toothNumbers: [16],
        },
      ],
      installments: [{ dueDate: arrange.localDay(-1), amount: 300 }],
    })
    expect(createdTyped.status, `create with a schedule: ${createdTyped.raw?.slice(0, 400)}`).toBeLessThan(300)
    const planTyped = createdTyped.body?.value ?? createdTyped.body

    const typedPlan = await api.plan(planTyped.id)
    const typedRow = typedPlan.installments[0]

    expect(
      typedRow.isAutoRaised,
      "false by DEFAULT, so a row a human entered is an agreed date without any caller having to say so",
    ).toBe(false)
    expect(
      typedRow.isOverdue,
      `a date a dentist typed and the patient has passed IS late — silencing it would destroy the only thing ` +
        `an échéancier is for. Due ${String(typedRow.dueDate).slice(0, 10)}, today ${arrange.localDay(0).slice(0, 10)}`,
    ).toBe(true)

    // The two rows are otherwise the same shape: one row, the whole total. Only the flag differs.
    expect(mil(typedRow.amount)).toBe(mil(autoRow.amount))
    expect(typedPlan.installments).toHaveLength(1)
  })

  test("PLAN-10 · « Réviser l'échéancier » CLEARS the auto-raised flag — revising is agreeing", async ({
    api,
    arrange,
    patient,
  }) => {
    /*
     * ⚠️ `Revise` clears it because revising *is* a dentist looking at the dates and settling on them. Getting
     * this backwards turns the rule into « nothing is ever late », which is the failure mode on the other side
     * of the one it was written for.
     */
    const p = await patient("Reviser")
    const act = await api.protocolAct(2)
    const started = await api.startTreatment({
      patientId: p.id,
      procedureTypeId: act.id,
      agreedTotal: 300,
      toothNumbers: [16],
      steps: null,
    })
    const plan = started.body?.value ?? started.body
    expect((await api.acceptPlan(plan.id, {})).status).toBe(200)
    expect((await api.plan(plan.id)).installments[0].isAutoRaised).toBe(true)

    // The dentist revises: two dates they have chosen, the earlier one already past.
    const revised = await api.put(`/treatment-plans/${plan.id}/installments`, {
      installments: [
        { dueDate: arrange.localDay(-1), amount: 100 },
        { dueDate: arrange.localDay(30), amount: 200 },
      ],
    })
    expect(revised.status, `revise: ${revised.raw?.slice(0, 400)}`).toBe(200)

    const after = await api.plan(plan.id)
    expect(after.installments, "the schedule is what the dentist typed").toHaveLength(2)
    expect(
      after.installments.every((i: any) => i.isAutoRaised === false),
      "revising is agreeing, so no row is auto-raised any more",
    ).toBeTruthy()

    const past = after.installments.find((i: any) => String(i.dueDate).slice(0, 10) < arrange.localDay(0).slice(0, 10))
    const future = after.installments.find((i: any) => String(i.dueDate).slice(0, 10) > arrange.localDay(0).slice(0, 10))
    expect(past?.isOverdue, "an agreed date that has passed is late").toBe(true)
    expect(future?.isOverdue, "…and one that has not, is not").toBe(false)
  })

  test("PLAN-11/12 · a revision must total the plan, and may not be empty", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("ReviserRefus")
    const act = await api.protocolAct(2)
    const started = await api.startTreatment({
      patientId: p.id,
      procedureTypeId: act.id,
      agreedTotal: 300,
      toothNumbers: [16],
      steps: null,
    })
    const plan = started.body?.value ?? started.body
    expect((await api.acceptPlan(plan.id, {})).status).toBe(200)

    const wrongTotal = await api.put(`/treatment-plans/${plan.id}/installments`, {
      installments: [{ dueDate: arrange.localDay(30), amount: 250 }],
    })
    expect(wrongTotal.status, "an échéancier that does not total the devis is not a schedule for it").toBe(400)

    const empty = await api.put(`/treatment-plans/${plan.id}/installments`, { installments: [] })
    expect(empty.status, "« L'échéancier ne peut pas être vide sur un devis accepté. »").toBe(400)

    // Neither refusal may have changed anything.
    const after = await api.plan(plan.id)
    expect(after.installments).toHaveLength(1)
    expect(mil(after.installments[0].amount)).toBe(mil(300))
  })
})

test.describe("HP-12 · une séance dit ce qu'elle ÉTAIT @mutating @t0", () => {
  mutatingOnly()

  test("HIST-01/02 · three séances of one act read as three STEPS, not three acts", async ({
    api,
    arrange,
    patient,
  }) => {
    /*
     * ⚠️ Three fiches of one implant read « Implant dentaire · Implant dentaire · Implant dentaire » in the
     * patient's history — which says the patient had **three implants** — with nothing saying the three were
     * one treatment. The step label and its rank were on record all along and never read back.
     *
     * ⚠️ And the money cells key on the **LINK**, not on money having moved: branching on
     * `collectedOnTreatment > 0` left a séance that collected nothing reading « 0,000 DT · 0,000 DT » —
     * indistinguishable from an ordinary free visit while the treatment showed its total outstanding — and on a
     * six-visit implant **most séances collect nothing**.
     */
    const p = await patient("Histo")
    const act = await api.protocolAct(3).catch(() => null)
    test.skip(
      act === null,
      "no catalogue act on this database has three or more séances — scenario not exercised",
    )

    const started = await api.startTreatment({
      patientId: p.id,
      procedureTypeId: act!.id,
      agreedTotal: 900,
      toothNumbers: [16],
      steps: null,
    })
    expect(started.status).toBe(200)
    const plan = started.body?.value ?? started.body
    const item = plan.items[0]
    expect((await api.acceptPlan(plan.id, {})).status).toBe(200)

    // One fiche per step. Only the FIRST collects anything — which is the case that used to read as free.
    for (const [i, step] of item.steps.entries()) {
      const r = await arrange.fiche(
        p.id,
        [arrange.act({ procedureTypeId: act!.id, name: act!.name, cost: 0, teeth: [16] })],
        {
          amountPaid: 0,
          amountCollectedOnPlan: i === 0 ? 200 : 0,
          interventionDate: arrange.localDay(-item.steps.length + i),
          treatmentPlanId: plan.id,
          treatmentPlanItemId: item.id,
          treatmentPlanItemStepId: step.id,
        },
      )
      expect(r.status, `séance ${i + 1}: ${r.raw?.slice(0, 300)}`).toBe(200)
    }

    const records = await api.records(p.id)
    const rows = (records.items ?? records) as any[]
    expect(rows, "one fiche per séance").toHaveLength(item.steps.length)

    /*
     * ── Every row names the treatment, the act, AND which step of it this séance was ──
     *
     * ⚠️ **The row is a composition of four fields, and asserting only the first is the mistake this test made
     * first.** `treatmentActDesignation` is the ACT, so it is *correctly* the same on every séance — « Facette
     * | Facette | Facette | Facette » is right, not the defect. What distinguishes them is
     * `treatmentStepLabel` with `treatmentStepNumber` / `treatmentStepTotal`, and together the four print
     * « Facette / Validation du mock-up · étape 2 / 4 ». The recorded defect was that the last three were on
     * record and never read back, so the history said the patient had had four facettes.
     */
    for (const row of rows) {
      expect(row.treatmentPlanId, "each séance names the treatment that binds them").toBe(plan.id)
      expect(row.treatmentActDesignation, "…and the act it carries out").toBeTruthy()
      expect(
        row.treatmentStepLabel,
        `each séance must say WHICH step it was. Got « ${row.treatmentStepLabel} »`,
      ).toBeTruthy()
      expect(row.treatmentStepTotal, "out of how many").toBe(item.steps.length)
    }

    // The ACT is the same on all of them — that is correct, and is exactly why the step is needed.
    expect(
      new Set(rows.map((r) => r.treatmentActDesignation)).size,
      "one act, so one designation",
    ).toBe(1)

    // The STEPS are what make the séances distinguishable, by label and by rank.
    const labels = rows.map((r) => r.treatmentStepLabel)
    expect(
      new Set(labels).size,
      `the séances must be distinguishable from each other. Got: ${labels.join(" | ")}`,
    ).toBe(item.steps.length)
    expect(
      rows.map((r) => r.treatmentStepNumber).sort((a: number, b: number) => a - b),
      "and the ranks run 1..N, so « étape 3 / 6 » can be printed",
    ).toEqual(item.steps.map((_: any, i: number) => i + 1))

    // ── The money cells key on the LINK, so a séance that collected nothing is still marked as the treatment's.
    const collected = rows.filter((r) => mil(r.collectedOnTreatment ?? 0) > 0)
    const free = rows.filter((r) => mil(r.collectedOnTreatment ?? 0) === 0)
    expect(collected, "one séance took 200").toHaveLength(1)
    expect(mil(collected[0].collectedOnTreatment)).toBe(mil(200))
    expect(free.length, "and the others took nothing — the ordinary case on a multi-séance act").toBeGreaterThan(0)
    for (const row of free) {
      expect(
        row.treatmentPlanId,
        "a séance that collected nothing must still carry its treatment badge, or it reads as a free visit",
      ).toBe(plan.id)
    }
  })

  test("PLAN-23 · « Traitements en cours » answers ONE question about a booked séance", async ({
    api,
    arrange,
    patient,
  }) => {
    /*
     * ⚠️ The « is this séance booked? » subquery must answer **exactly** what `TreatmentsInProgressReader`
     * answers — per **step**, and with **no « from today » floor**, because an `AwaitingClosure` visit is still
     * a standing booking. Otherwise the screen groups by one rule and sorts by another, and its three groups
     * interleave instead of being contiguous.
     */
    const p = await patient("Groupes")
    const act = await api.protocolAct(2)
    const started = await api.startTreatment({
      patientId: p.id,
      procedureTypeId: act.id,
      agreedTotal: 400,
      toothNumbers: [16],
      steps: null,
    })
    const plan = started.body?.value ?? started.body
    const item = plan.items[0]

    // Book the NEXT step in the PAST, so it is a standing booking the job has already advanced.
    const appt = await api.createAppointment({
      patientId: p.id,
      appointmentDateTime: arrange.slot(-1, 11).toISOString(),
      durationMinutes: 30,
      treatmentPlanId: plan.id,
      treatmentPlanItemId: item.id,
      treatmentPlanItemStepId: item.steps[0].id,
      procedures: [
        {
          procedureTypeId: act.id,
          agreedCost: null,
          treatmentPlanId: plan.id,
          treatmentPlanItemId: item.id,
          treatmentPlanItemStepId: item.steps[0].id,
        },
      ],
    })

    const live = await api.treatmentsInProgress()
    const row = (live.items ?? []).find((r: any) => r.planId === plan.id)
    expect(row, "the treatment must be listed").toBeTruthy()

    expect(
      row.nextStepAppointmentId,
      `a booking in the PAST is still a booking — a « from today » floor would report this séance as ` +
        `unbooked while the appointment sits on the agenda. Appointment ${appt.id} on ` +
        `${String(appt.appointmentDateTime).slice(0, 10)}`,
    ).toBe(appt.id)

    // The step it names as next is the one actually booked — per step, not per act.
    expect(row.nextStepId, "the answer is per STEP").toBe(item.steps[0].id)
    expect(row.nextStepNumber).toBe(1)
    expect(row.stepsTotal).toBe(item.steps.length)
  })
})
