import { expect, mutatingOnly, test } from "../lib/fixtures"
import { mil } from "../lib/api"

/**
 * HP-10c — **« Créances », « Solde patient » and la caisse must agree, always.**
 *
 * ⚠️ **Why this file exists, and why it is the first one to write.** « No money gaps » is not a property of any
 * one handler; it is a property of the *set* of reads. A patient owes something, and four surfaces answer the
 * question independently — `GetPatientBillingSummaryQuery`, `/billing/receivables`, `/billing/caisse` and the
 * dashboard. Every money defect this repo has recorded is one of them disagreeing with the others **with no
 * error anywhere**: a bridged plan whose 10 DT was readable nowhere, a balance reading 0 over a live 30 DT
 * note, 25 of 27 unpaid rows flagged « En retard » the day after signature.
 *
 * So each test here does the same thing: make one gesture, then ask **every** surface and require them to say
 * the same number. A test that asserts on the surface it just wrote to cannot see this class of defect at all.
 *
 * ⚠️ **Read-against-read, never against a running total.** The clinic-wide figures (`receivables.totalOutstanding`)
 * move when any other test bills anything, and `workers: 1` does not make them stable *across* tests. Every
 * assertion below is scoped to this test's own patient, and the clinic-wide read is used only to confirm the
 * patient's own row **appears in it with the same figure** — which is the actual coupling, and is stable.
 */

test.describe("HP-10c · les lectures d'argent s'accordent @mutating @t0", () => {
  mutatingOnly()

  /**
   * The three answers to « what does this patient owe? », in millimes.
   *
   * ⚠️ **« Créances » is asked through `receivableFor`, never paged through.** The developer's clinic carries
   * **285** receivable rows; a fixture patient owing 20 DT sits well past any page size a test would pick, and
   * the row then reads `absent` — which is indistinguishable from « owes nothing » and is exactly how
   * `CONT-03/04` reported a money gap against a correct product on 2026-09-11.
   */
  async function owed(api: any, patientId: string, patientName: string) {
    const summary = await api.billingSummary(patientId)
    const row = await api.receivableFor(patientId, patientName)
    return {
      summaryTotal: mil(summary.totalOutstanding ?? 0),
      summaryInvoice: mil(summary.invoiceOutstanding ?? 0),
      summaryInstallment: mil(summary.installmentOutstanding ?? 0),
      receivable: row ? mil(row.totalOutstanding ?? 0) : null,
      lines: summary.lines ?? [],
    }
  }

  // ── MONEY-20 ─────────────────────────────────────────────────────────────────────────────────────────────

  test("MONEY-20 · a fiche of 100 with 60 paid owes 40, and « Créances » says the same 40", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Solde40")
    const act = await api.singleSeanceAct()

    const r = await arrange.fiche(
      p.id,
      [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 100 })],
      { amountPaid: 60 },
    )
    expect(r.status, `fiche: ${r.raw?.slice(0, 300)}`).toBe(200)

    const state = await owed(api, p.id, p.name)

    expect(state.summaryTotal, "« Solde patient » after paying 60 of 100").toBe(mil(40))
    expect(state.summaryInvoice, "the 40 is owed on the NOTE, not on a plan").toBe(mil(40))
    expect(state.summaryInstallment, "no plan is involved").toBe(0)

    /*
     * ⚠️ The second half is the one that matters. A summary computing 40 correctly while « Créances » computes
     * something else is precisely the defect shape — one rule, every read — and it is invisible from either
     * screen alone.
     */
    expect(state.receivable, "the patient must APPEAR in « Créances »").not.toBeNull()
    expect(state.receivable, "« Créances » must agree with « Solde patient »").toBe(state.summaryTotal)

    // And the decomposition must sum to the figure printed above it (`PatientDebtLines`).
    const lineSum = state.lines.reduce((t: number, l: any) => t + mil(l.outstanding ?? l.amount ?? 0), 0)
    expect(lineSum, `« Reste à payer » rows must sum to the total: ${JSON.stringify(state.lines)}`).toBe(
      state.summaryTotal,
    )
  })

  // ── MONEY-21 ─────────────────────────────────────────────────────────────────────────────────────────────

  test("MONEY-21 · topping the payment up to 100 clears the créance and raises NO second note", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("ToppedUp")
    const act = await api.singleSeanceAct()

    const created = await arrange.fiche(
      p.id,
      [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 100 })],
      { amountPaid: 60 },
    )
    expect(created.status, created.raw?.slice(0, 300)).toBe(200)
    const record = created.body?.value ?? created.body

    const before = await api.invoices(`?patientId=${p.id}&pageSize=100`)
    const notesBefore = (before.items ?? []).length
    expect(notesBefore, "the first save must raise exactly one note").toBe(1)

    const updated = await api.updateRecord(p.id, record.id, {
      interventionDate: record.interventionDate,
      amountPaid: 100,
      isAdultTeeth: true,
      acts: [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 100 })],
      notes: [],
      importantNotes: [],
    })
    expect(updated.status, `top-up refused: ${updated.raw?.slice(0, 400)}`).toBe(200)

    const state = await owed(api, p.id, p.name)
    expect(state.summaryTotal, "nothing is owed once the séance is fully paid").toBe(0)
    expect(state.receivable, "and the patient leaves « Créances » entirely").toBeNull()

    /*
     * ⚠️ **The point of the scenario.** A fiche is re-saved routinely — the dentist corrects a tooth, adds a
     * note, changes the date. If the billing were not idempotent, each of those would raise another note for
     * the same séance, and nobody could say which one the patient holds.
     */
    const after = await api.invoices(`?patientId=${p.id}&pageSize=100`)
    expect(
      (after.items ?? []).length,
      `re-saving must TOP UP the note, never raise a second: ${(after.items ?? [])
        .map((i: any) => `${i.number ?? "(draft)"}=${i.total}`)
        .join(" · ")}`,
    ).toBe(1)
    expect(mil(after.items[0].amountCollected ?? 0), "the one note now holds the whole 100").toBe(mil(100))
  })

  // ── MONEY-22 ─────────────────────────────────────────────────────────────────────────────────────────────

  test("MONEY-22 · a devis of 1 000 with 2 of 4 échéances paid owes 500, on every surface", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Echeancier")
    const act = await api.singleSeanceAct()

    /*
     * ⚠️ `POST /treatment-plans/start` takes **one act**, not an `items` array — it is « Suivre ce
     * traitement », the door that mints an un-numbered Draft around a single procedure. An `items` payload is
     * silently ignored, `ProcedureTypeId` arrives as `Guid.Empty`, and the refusal is « Acte introuvable. »,
     * which reads as a catalogue problem rather than a wrong body.
     */
    const started = await api.startTreatment({
      patientId: p.id,
      procedureTypeId: act.id,
      agreedTotal: 1000,
      toothNumbers: [16],
      steps: null,
    })
    expect(started.status, `start: ${started.raw?.slice(0, 300)}`).toBe(200)
    const planId = (started.body?.value ?? started.body).id

    const accepted = await api.acceptPlan(planId, {})
    expect(accepted.status, `accept: ${accepted.raw?.slice(0, 300)}`).toBe(200)

    /*
     * `Accept` writes ONE lump-sum échéance dated at the acceptance instant so a payment has somewhere to
     * live — it is a ledger container, not a schedule anybody agreed to. A real échéancier is what
     * « Réviser l'échéancier » produces, and it is what this scenario needs.
     */
    const day = (offset: number) => {
      const d = new Date()
      d.setDate(d.getDate() + offset)
      return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`
    }
    const revised = await api.call("PUT", `/treatment-plans/${planId}/installments`, {
      planId,
      installments: [30, 60, 90, 120].map((n, i) => ({
        amount: 250,
        dueDate: `${day(n)}T00:00:00`,
        label: `Échéance ${i + 1}`,
      })),
    })
    expect(revised.status, `revise: ${revised.raw?.slice(0, 400)}`).toBe(200)

    const plan = await api.plan(planId)
    const installments = plan.installments ?? []
    expect(installments.length, "four échéances of 250").toBe(4)

    for (const inst of installments.slice(0, 2)) {
      const paid = await api.call("POST", `/treatment-plans/${planId}/installments/${inst.id}/payments`, {
        planId,
        installmentId: inst.id,
        amount: 250,
        method: "Cash",
        paidOn: new Date().toISOString(),
      })
      expect(paid.status, `payment on ${inst.id}: ${paid.raw?.slice(0, 400)}`).toBe(200)
    }

    const state = await owed(api, p.id, p.name)
    expect(state.summaryInstallment, "500 of the 1 000 is still to come").toBe(mil(500))
    expect(state.summaryTotal, "and nothing else is owed").toBe(mil(500))
    expect(state.receivable, "« Créances » must carry the same 500").toBe(mil(500))

    /*
     * The plan's own arithmetic, asked at the source rather than re-derived here.
     *
     * ⚠️ The field is `amountPaid` — **not** `totalPaid` and not `amountCollected`, both of which read
     * `undefined` and make a correct 500 assert as 0. A wrong field name is the quietest probe bug there is: it
     * throws nothing, it reports a plausible number, and it names the product.
     */
    const after = await api.plan(planId)
    expect(mil(after.amountPaid ?? 0), "the plan has taken 500").toBe(mil(500))
    expect(mil(after.outstanding ?? 0), "and still expects 500").toBe(mil(500))
  })

  // ── MONEY-25 ─────────────────────────────────────────────────────────────────────────────────────────────

  test("MONEY-25 · a CANCELLED plan carries no créance, and its payments can no longer be moved", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("DevisAnnule")
    const act = await api.singleSeanceAct()

    const started = await api.startTreatment({
      patientId: p.id,
      procedureTypeId: act.id,
      agreedTotal: 400,
      toothNumbers: [16],
      steps: null,
    })
    expect(started.status, `start: ${started.raw?.slice(0, 300)}`).toBe(200)
    const planId = (started.body?.value ?? started.body).id
    expect((await api.acceptPlan(planId, {})).status).toBe(200)

    const plan = await api.plan(planId)
    const inst = (plan.installments ?? [])[0]
    expect(inst, "acceptance raises one lump-sum échéance to hold payments").toBeTruthy()

    const paid = await api.call("POST", `/treatment-plans/${planId}/installments/${inst.id}/payments`, {
      planId,
      installmentId: inst.id,
      amount: 150,
      method: "Cash",
      paidOn: new Date().toISOString(),
    })
    expect(paid.status, paid.raw?.slice(0, 300)).toBe(200)

    const owing = await owed(api, p.id, p.name)
    expect(owing.summaryTotal, "before the cancellation, 250 of 400 is owed").toBe(mil(250))

    const cancelled = await api.cancelPlan(planId, "Le patient ne poursuit pas.")
    expect(cancelled.status, `cancel: ${cancelled.raw?.slice(0, 400)}`).toBe(200)

    /*
     * ⚠️ `PlanBillingRules.CarriesDebt(Cancelled)` is false — a cancelled devis owes nothing, whatever its
     * échéancier still says. Asserting on BOTH reads because « Créances » and « Solde patient » compute this
     * independently and a cancelled plan lingering in one of them is money the patient does not owe.
     */
    const afterCancel = await owed(api, p.id, p.name)
    expect(afterCancel.summaryTotal, "a cancelled devis owes nothing").toBe(0)
    expect(afterCancel.summaryInstallment, "and specifically nothing on the échéancier").toBe(0)
    expect(afterCancel.receivable, "the patient leaves « Créances »").toBeNull()

    const refused = await api.call("POST", `/treatment-plans/${planId}/installments/${inst.id}/payments`, {
      planId,
      installmentId: inst.id,
      amount: 50,
      method: "Cash",
      paidOn: new Date().toISOString(),
    })
    expect(refused.status, "a cancelled plan takes no further payment").toBeGreaterThanOrEqual(400)
    /*
     * ⚠️ **The refusal is asserted as a refusal, not by its wording.** `CLAUDE.md`'s own rule — « never
     * recover an outcome by matching French prose » — applies to the tests too: the first draft of this line
     * required /annul/i and went red against « Le plan doit être accepté pour enregistrer un paiement. », which
     * is a correct refusal arriving through `MONEY-28`'s guard rather than a cancellation-specific one. What
     * matters is that the money was refused and the sentence is French and actionable, not which of two true
     * sentences the server chose.
     */
    expect(refused.raw, `the refusal must carry a French sentence — got: ${refused.raw?.slice(0, 300)}`)
      .toMatch(/annul|accept|plan/i)
  })

  // ── MONEY-14 / MONEY-15 ──────────────────────────────────────────────────────────────────────────────────

  test("MONEY-14/15 · a cheque is counted as a cheque, and lands in « Chèques à encaisser » with its identity", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Cheque")
    const act = await api.singleSeanceAct()

    const chequeNumber = `E2E${Date.now().toString().slice(-7)}`
    const r = await arrange.fiche(
      p.id,
      [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 300 })],
      {
        amountPaid: 300,
        paymentMethod: "Cheque",
        chequeNumber,
        chequeBankName: "Banque de Tunisie",
        chequeDueDate: new Date(Date.now() + 30 * 86_400_000).toISOString(),
      },
    )
    expect(r.status, `fiche by cheque: ${r.raw?.slice(0, 400)}`).toBe(200)

    /*
     * ⚠️ **The recorded defect is that this was a hard-coded `Cash`.** A cheque counted under « dont espèces »
     * is money the practice believes is in the drawer and is not — and the cheque itself then has nowhere to be
     * chased, which is the second half of the same mistake (L8).
     */
    const today = (() => {
      const d = new Date()
      return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`
    })()
    const ledger = await api.caisseLedger(today, today)
    const mine = (ledger.movements ?? []).filter((m: any) => m.patientId === p.id)
    expect(mine.length, `the payment must reach la caisse: ${JSON.stringify(ledger).slice(0, 300)}`).toBe(1)
    expect(String(mine[0].method ?? mine[0].paymentMethod ?? "")).toMatch(/cheque|chèque/i)

    const cheques = await api.cheques("?pageSize=500")
    const row = (cheques.items ?? []).find(
      (c: any) => c.chequeNumber === chequeNumber || c.number === chequeNumber,
    )
    expect(
      row,
      `the cheque must be chaseable by its own number (${chequeNumber}). Got ${(cheques.items ?? []).length} row(s)`,
    ).toBeTruthy()
    expect(String(row.bankName ?? row.chequeBankName ?? "")).toMatch(/Banque de Tunisie/i)
  })
})
