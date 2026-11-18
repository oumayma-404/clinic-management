import { expect, mutatingOnly, test } from "../lib/fixtures"
import { mil } from "../lib/api"

/**
 * HP-20 · La séance au fauteuil (`features/seance-au-fauteuil`). The dentist writes the fiche DURING the séance and
 * leaves « Payé » empty; the desk takes the money from « À encaisser ». These are the rules the server owns; the two
 * rows that live in `.tsx` are in `seance-au-fauteuil-browser.spec.ts`.
 *
 * ⚠️ Every visit here is booked to be RUNNING now (started ten minutes ago, an hour long), because the rule under
 * test is « a save in the middle of a running séance leaves it alone » — a visit in the past would complete on the
 * first save by design.
 */
test.describe("HP-20 · séance au fauteuil @mutating @t0", () => {
  mutatingOnly()

  const minutesAgo = (m: number) => {
    const d = new Date(Date.now() - m * 60_000)
    d.setSeconds(0, 0)
    return d.toISOString()
  }
  const actInput = (act: any, cost: number) => ({
    procedureTypeId: act.id,
    procedureName: act.name,
    cost,
    unitCost: cost,
    isPerTooth: false,
    toothNumbers: [],
  })
  const body = (r: any) => r.body?.value ?? r.body
  const recordsOf = async (api: any, patientId: string) => {
    const r = await api.records(patientId)
    return (r.items ?? r) as any[]
  }

  /** A patient, a running visit with one act, and that visit's fiche saved with « Payé » empty. */
  async function seance(api: any, arrange: any, patient: any, tag: string, startedMinutesAgo = 10) {
    const p = await patient(tag)
    const act = await api.singleSeanceAct()
    const cost = Number(act.defaultCost)
    const visit = await api.createAppointment({
      patientId: p.id,
      appointmentDateTime: minutesAgo(startedMinutesAgo),
      durationMinutes: 60,
      procedures: [{ procedureTypeId: act.id, agreedCost: null }],
    })
    const saved = await arrange.fiche(p.id, [actInput(act, cost)], { appointmentId: visit.id, amountPaid: 0 })
    expect(saved.status, `fiche: ${saved.raw?.slice(0, 300)}`).toBe(200)
    return { p, act, cost, visit, fiche: body(saved) }
  }

  test("SEAT-01 · a fiche saved mid-séance leaves the visit running and raises no note", async ({ api, arrange, patient }) => {
    const { p, visit, fiche } = await seance(api, arrange, patient, "Fauteuil01")

    expect(fiche.billing?.outcome, "« Payé » empty is « nothing taken at the chair »").toBe("NotCollected")
    expect((await api.appointment(visit.id)).status, "the patient is still in the chair").not.toBe("Completed")
    expect((await api.invoices(`?patientId=${p.id}&pageSize=50`)).items).toHaveLength(0)
  })

  test("SEAT-02 · « Terminer la séance » ends the visit", async ({ api, arrange, patient }) => {
    const { p, act, cost, visit, fiche } = await seance(api, arrange, patient, "Fauteuil02")

    const finished = await api.updateRecord(p.id, fiche.id, {
      version: fiche.version,
      interventionDate: arrange.localDay(),
      amountPaid: 0,
      isAdultTeeth: true,
      acts: [actInput(act, cost)],
      finishSeance: true,
    })
    expect(finished.status, finished.raw?.slice(0, 300)).toBe(200)
    expect((await api.appointment(visit.id)).status).toBe("Completed")
  })

  test("SEAT-03 · the saved séance is « à encaisser » at its total", async ({ api, arrange, patient }) => {
    const { visit, cost, fiche } = await seance(api, arrange, patient, "Fauteuil03")

    const rows = (await api.ok("GET", "/appointments/to-collect")) as any[]
    const row = rows.find((r) => r.appointmentId === visit.id)
    expect(row, "the séance waits for the desk").toBeTruthy()
    expect(row.kind).toBe("Unbilled")
    expect(row.dentalRecordId).toBe(fiche.id)
    expect(mil(row.amountDue)).toBe(mil(cost))
  })

  test("SEAT-04 · the desk bills it by cheque: the cheque keeps its identity and the séance leaves the list", async ({
    api,
    arrange,
    patient,
  }) => {
    const { p, visit, cost, fiche } = await seance(api, arrange, patient, "Fauteuil04")

    const number = `SEAT04-${Date.now() % 1_000_000}`
    const billed = await api.billRecord(fiche.id, { amount: cost, method: "Cheque", chequeNumber: number, chequeBankName: "BIAT" })
    expect(billed.status, billed.raw?.slice(0, 300)).toBeLessThan(300)

    const note = (await api.invoices(`?patientId=${p.id}&pageSize=50`)).items[0]
    expect(note.status, "issued and paid in full").toBe("Paid")
    // The cheque's identity, where the desk reads it: « Chèques à encaisser ».
    const cheque = ((await api.cheques("?pageSize=500")).items ?? []).filter((c: any) => c.chequeNumber === number)
    expect(cheque, "the cheque is listed once, by its number").toHaveLength(1)
    expect(cheque[0].bankName).toBe("BIAT")
    expect(mil(cheque[0].amount)).toBe(mil(cost))
    const rows = (await api.ok("GET", "/appointments/to-collect")) as any[]
    expect(rows.some((r) => r.appointmentId === visit.id), "paid in full: nothing left for the desk").toBe(false)
  })

  test("SEAT-05 · a reopened fiche reads the desk's money, and a re-save takes nothing twice", async ({
    api,
    arrange,
    patient,
  }) => {
    const { p, act, cost, fiche } = await seance(api, arrange, patient, "Fauteuil05")
    expect((await api.billRecord(fiche.id, { amount: cost, method: "Cash" })).status).toBeLessThan(300)

    const reopened = (await recordsOf(api, p.id)).find((r) => r.id === fiche.id)
    expect(mil(reopened.collectedOnNote), "« Payé » reopens on the note's figure, desk money included").toBe(mil(cost))

    const resaved = await api.updateRecord(p.id, fiche.id, {
      version: reopened.version,
      interventionDate: arrange.localDay(),
      amountPaid: cost,
      isAdultTeeth: true,
      acts: [actInput(act, cost)],
    })
    expect(resaved.status, resaved.raw?.slice(0, 300)).toBe(200)
    expect(body(resaved).billing?.outcome).toBe("AlreadyBilled")
    const note = (await api.invoices(`?patientId=${p.id}&pageSize=50`)).items[0]
    expect((await api.invoice(note.id)).payments.filter((x: any) => !x.isVoided)).toHaveLength(1)
  })

  test("SEAT-06 · a séance already holding a note from its rendez-vous is not billed a second time", async ({
    api,
    arrange,
    patient,
  }) => {
    const { p, act, cost, visit, fiche } = await seance(api, arrange, patient, "Fauteuil06")
    // « Facturer cette consultation » in the RDV window: a draft naming the VISIT and no fiche.
    const draft = await api.call("POST", "/invoices", {
      patientId: p.id,
      appointmentId: visit.id,
      lines: [{ designation: act.name, quantity: 1, unitPriceHt: cost }],
    })
    expect(draft.status, draft.raw?.slice(0, 300)).toBeLessThan(300)

    const refused = await api.billRecord(fiche.id, { amount: cost, method: "Cash" })
    expect(refused.status).toBeGreaterThanOrEqual(400)
    expect(refused.raw).toContain("seance_has_note")
    expect((await api.invoices(`?patientId=${p.id}&pageSize=50`)).items, "no second note").toHaveLength(1)
  })

  test("SEAT-07 · « Corriger la note » carries the desk's cheque onto the replacement", async ({
    api,
    arrange,
    patient,
  }) => {
    const { p, act, cost, fiche } = await seance(api, arrange, patient, "Fauteuil07")
    const number = `SEAT07-${Date.now() % 1_000_000}`
    expect(
      (await api.billRecord(fiche.id, { amount: cost, method: "Cheque", chequeNumber: number, chequeBankName: "BIAT" })).status,
    ).toBeLessThan(300)
    const before = (await recordsOf(api, p.id)).find((r) => r.id === fiche.id)

    const corrected = await api.updateRecord(p.id, fiche.id, {
      version: before.version,
      interventionDate: arrange.localDay(),
      amountPaid: cost,
      isAdultTeeth: true,
      acts: [actInput(act, cost + 10)],
      correctionReason: "QA — prix corrigé",
    })
    expect(corrected.status, corrected.raw?.slice(0, 300)).toBe(200)

    const notes = (await api.invoices(`?patientId=${p.id}&pageSize=50`)).items
    const live = notes.filter((n: any) => n.status !== "Cancelled")
    expect(notes.filter((n: any) => n.status === "Cancelled"), "the wrong note is cancelled").toHaveLength(1)
    expect(live, "and replaced by one note").toHaveLength(1)
    const replacement = await api.invoice(live[0].id)
    const carried = replacement.payments.filter((x: any) => !x.isVoided)
    expect(carried, "the desk's payment, carried").toHaveLength(1)
    expect(carried[0].method, "a cheque — not re-typed as cash").toBe("Cheque")
    expect(mil(carried[0].amount)).toBe(mil(cost))
    const cheque = ((await api.cheques("?pageSize=500")).items ?? []).filter((c: any) => c.chequeNumber === number)
    expect(cheque, "« Chèques » still holds that cheque exactly once — the voided original is gone").toHaveLength(1)
    expect(cheque[0].bankName).toBe("BIAT")
    expect(mil(replacement.outstanding), "the corrected price's difference is still owed").toBe(mil(10))
  })

  test("SEAT-08 · a deposited cheque the correction would drop is refused, and nothing is written", async ({
    api,
    arrange,
    patient,
  }) => {
    const { p, act, cost, fiche } = await seance(api, arrange, patient, "Fauteuil08")
    expect(
      (await api.billRecord(fiche.id, { amount: cost, method: "Cheque", chequeNumber: "9001", chequeBankName: "STB" })).status,
    ).toBeLessThan(300)
    const note = (await api.invoices(`?patientId=${p.id}&pageSize=50`)).items[0]
    const payment = (await api.invoice(note.id)).payments[0]
    expect((await api.post(`/invoices/${note.id}/payments/${payment.id}/banked`, { banked: true })).status).toBe(200)
    const before = (await recordsOf(api, p.id)).find((r) => r.id === fiche.id)

    const refused = await api.updateRecord(p.id, fiche.id, {
      version: before.version,
      interventionDate: arrange.localDay(),
      amountPaid: 0,
      isAdultTeeth: true,
      // The act changes, so the guard refuses and the correction is entered; « Payé » 0 then carries nothing.
      acts: [actInput(act, cost + 10)],
      correctionReason: "QA — le patient n'a pas payé",
    })
    expect(refused.status).toBeGreaterThanOrEqual(400)
    expect(refused.raw).toContain("dental_record_payment_banked")
    expect((await api.invoice(note.id)).status, "the note is untouched").not.toBe("Cancelled")
  })

  test("SEAT-09 · a devis séance collected at the desk is that fiche's money", async ({ api, arrange, patient }) => {
    const p = await patient("Fauteuil09")
    const act = await api.singleSeanceAct()
    const created = await api.call("POST", "/treatment-plans", {
      patientId: p.id,
      title: "Devis QA fauteuil",
      items: [{ procedureTypeId: act.id, designationFr: act.name, plannedCost: 300, toothNumbers: [16] }],
      installments: [
        { dueDate: arrange.localDay(-3), amount: 100 },
        { dueDate: arrange.localDay(30), amount: 200 },
      ],
    })
    expect(created.status, created.raw?.slice(0, 300)).toBeLessThan(300)
    const plan = body(created)
    const item = plan.items[0]
    const visit = await api.createAppointment({
      patientId: p.id,
      appointmentDateTime: minutesAgo(10),
      durationMinutes: 60,
      treatmentPlanId: plan.id,
      procedures: [
        { procedureTypeId: act.id, agreedCost: null, treatmentPlanId: plan.id, treatmentPlanItemId: item.id },
      ],
    })
    const saved = await arrange.fiche(p.id, [actInput(act, 0)], {
      appointmentId: visit.id,
      treatmentPlanId: plan.id,
      treatmentPlanItemId: item.id,
    })
    expect(saved.status, saved.raw?.slice(0, 300)).toBe(200)
    const fiche = body(saved)

    const row = ((await api.ok("GET", "/appointments/to-collect")) as any[]).find((r) => r.appointmentId === visit.id)
    expect(row?.kind, "a devis séance with a late échéance waits for the desk").toBe("Treatment")
    expect(mil(row.amountDue)).toBe(mil(100))
    expect(row.treatmentPlanId).toBe(plan.id)

    const fresh = await api.plan(plan.id)
    const late = fresh.installments.find((i: any) => !i.isPaid && mil(i.outstanding) > 0)
    const paid = await api.call("POST", `/treatment-plans/${plan.id}/installments/${late.id}/payments`, {
      amount: 100,
      method: "Cash",
      paidOn: new Date().toISOString(),
      dentalRecordId: fiche.id,
    })
    expect(paid.status, paid.raw?.slice(0, 300)).toBe(200)
    const reread = (await recordsOf(api, p.id)).find((r) => r.id === fiche.id)
    expect(mil(reread.collectedOnTreatment), "the desk's payment is that séance's money").toBe(mil(100))
  })
})
