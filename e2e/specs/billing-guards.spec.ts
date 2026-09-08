import { expect, mutatingOnly, test } from "../lib/fixtures"
import { mil } from "../lib/api"

/**
 * HP-6 / HP-10 — the guards that must refuse, and the remedy each one names.
 *
 * ⚠️ **A refusal whose remedy is unreachable is worse than no refusal**, and each of these was that once. The
 * one that cost most: « Détacher la fiche » refused with « Annulez la facture (ou émettez un avoir) », the
 * dentist issued the avoir, retried — and got the identical refusal, because the guard asked
 * `RepresentsItsPlan(Status)` alone and a credit note is a **separate aggregate** that leaves the invoice
 * `Paid`. And « Annulez » was not available either: `Invoice.CanCancel` refuses a note carrying a live payment.
 * Both remedies the message named were unreachable, so a fiche attached to the wrong step was permanent.
 *
 * So these tests assert **the code**, never the sentence (« never recover an outcome by matching French
 * prose »), and then assert that following the named remedy actually **clears** the refusal.
 */

test.describe("HP-6 · les refus de la fiche facturée @mutating @t0", () => {
  mutatingOnly()

  /** A fiche billed on a live, issued note: 100 quoted, 60 collected. */
  async function billedFiche(api: any, arrange: any, patientId: string, cost = 100, paid = 60) {
    const act = await api.singleSeanceAct()
    const r = await arrange.fiche(
      patientId,
      [arrange.act({ procedureTypeId: act.id, name: act.name, cost })],
      { amountPaid: paid },
    )
    expect(r.status, `fiche: ${r.raw?.slice(0, 300)}`).toBe(200)
    const fiche = r.body?.value ?? r.body

    const invoices = await api.invoices(`?patientId=${patientId}&pageSize=20`)
    expect(invoices.items, "a fiche with a payment raises its note on save").toHaveLength(1)
    return { act, fiche, note: invoices.items[0] }
  }

  /** Re-sends a saved fiche with one thing changed, exactly as the modal's Enregistrer does. */
  const resave = (api: any, fiche: any, patch: Record<string, unknown>) =>
    api.updateRecord(fiche.patientId, fiche.id, {
      interventionDate: fiche.interventionDate,
      amountPaid: fiche.amountPaid,
      isAdultTeeth: fiche.isAdultTeeth ?? true,
      notes: fiche.notes ?? [],
      importantNotes: fiche.importantNotes ?? [],
      acts: (fiche.acts ?? []).map((a: any) => ({
        procedureTypeId: a.procedureTypeId,
        procedureName: a.procedureName,
        cost: a.cost,
        unitCost: a.unitCost,
        isPerTooth: a.isPerTooth,
        toothNumbers: a.toothNumbers ?? [],
        ponticToothNumbers: a.ponticToothNumbers ?? [],
        resultingCondition: a.resultingCondition,
        surfaces: a.surfaces,
        note: a.note,
      })),
      version: fiche.version,
      ...patch,
    })

  test("FEDIT-02 · raising « Payé » on a billed fiche TOPS UP the note — it never raises a second", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("TopUp")
    const { fiche, note } = await billedFiche(api, arrange, p.id, 100, 60)

    const r = await resave(api, fiche, { amountPaid: 100 })
    expect(r.status, `re-save: ${r.raw?.slice(0, 300)}`).toBe(200)

    const invoices = await api.invoices(`?patientId=${p.id}&pageSize=20`)
    expect(
      invoices.items,
      "a fiche is re-saved routinely — a re-save raising a second note is the duplicate the guard exists for",
    ).toHaveLength(1)
    expect(invoices.items[0].id).toBe(note.id)
    expect(mil(invoices.items[0].amountCollected)).toBe(mil(100))
    expect(mil(invoices.items[0].outstanding)).toBe(0)
  })

  test("FEDIT-03 · changing the acts of a billed fiche is refused, and the save does NOT happen", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("ActsMoved")
    const { act, fiche } = await billedFiche(api, arrange, p.id, 100, 60)

    const r = await resave(api, fiche, {
      acts: [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 250 })],
    })

    expect(r.status).toBe(400)
    expect(
      r.body?.code ?? r.body?.Code,
      `branch on the code, never the sentence. Body: ${r.raw.slice(0, 300)}`,
    ).toBe("dental_record_acts_changed_after_billing")

    // Pre-commit: the note's lines are frozen, so the fiche must still describe what was billed.
    const reread = await api.records(p.id)
    const saved = (reread.items ?? reread)[0]
    expect(mil(saved.cost ?? saved.acts[0].cost), "« refusé » has to mean the save did not happen").toBe(
      mil(100),
    )
  })

  test("FEDIT-05 · lowering « Payé » below what the note collected is refused, naming the amount", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Lowered")
    const { fiche } = await billedFiche(api, arrange, p.id, 100, 60)

    const r = await resave(api, fiche, { amountPaid: 20 })

    expect(r.status).toBe(400)
    expect(r.body?.code ?? r.body?.Code).toBe("dental_record_payment_lowered")
    // The refusal must carry the figure, or the user goes hunting through /factures.
    expect(r.raw, "the refusal names the note and what it has already collected").toMatch(/60,000/)
  })

  test("FEDIT-07/09 · a CANCELLED note refuses a re-save and does not silently raise a second (A-1)", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Cancelled")
    // 0 collected so the note carries no live payment and `CanCancel` allows it.
    const act = await api.singleSeanceAct()
    const created = await arrange.fiche(
      p.id,
      [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 100 })],
      { amountPaid: 100 },
    )
    expect(created.status).toBe(200)
    const fiche = created.body?.value ?? created.body

    const invoices = await api.invoices(`?patientId=${p.id}&pageSize=20`)
    const note = invoices.items[0]

    // A note holding a live payment cannot be cancelled — that is MONEY-02, and it is why the avoir is the only
    // remedy. Void the payment first so this scenario can reach the *cancelled* precondition at all.
    const payments = (await api.invoice(note.id)).payments ?? []
    for (const pay of payments) {
      const v = await api.call("POST", `/invoices/${note.id}/payments/${pay.id}/void`, {
        reason: "E2E — arranging the cancelled-note precondition",
      })
      expect(v.status, `void: ${v.raw?.slice(0, 200)}`).toBeLessThan(300)
    }
    const cancelled = await api.cancelInvoice(note.id, { reason: "E2E — A-1 precondition" })
    test.skip(
      cancelled.status >= 300,
      `could not cancel the note: HTTP ${cancelled.status} ${cancelled.raw?.slice(0, 200)}`,
    )

    const r = await resave(api, fiche, { amountPaid: 100 })
    expect(r.status, `re-save over a cancelled note: ${r.raw?.slice(0, 300)}`).toBe(400)
    expect(r.body?.code ?? r.body?.Code).toBe("dental_record_invoice_not_live")

    const after = await api.invoices(`?patientId=${p.id}&pageSize=20`)
    expect(
      after.items,
      "two notes for one séance is the duplicate the whole guard exists to prevent — nobody could then say " +
        "which one the patient is holding",
    ).toHaveLength(1)
  })

  test("MONEY-02 · a note carrying a live payment CANNOT be cancelled", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("NoCancel")
    const { note } = await billedFiche(api, arrange, p.id, 100, 60)

    const r = await api.cancelInvoice(note.id, { reason: "E2E" })
    expect(
      r.status,
      "this is why the avoir is the only correction — and why a refusal naming « annulez » was unreachable",
    ).toBe(400)
  })

  test("MONEY-03/04 · a PARTIAL avoir leaves the note billing; a FULL one changes which rule refuses", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Avoir")
    const { act, fiche, note } = await billedFiche(api, arrange, p.id, 100, 60)
    const changedActs = [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 250 })]

    // A partial avoir — 20 of the 60 collected. `IsSpent` needs CreditedTotal >= AmountCollected.
    const partial = await api.avoir(note.id, { amount: 20, reason: "E2E — partial" })
    expect(partial.status, `avoir: ${partial.raw?.slice(0, 300)}`).toBeLessThan(300)

    let r = await resave(api, fiche, { acts: changedActs })
    expect(r.status).toBe(400)
    expect(
      r.body?.code ?? r.body?.Code,
      "a partial avoir leaves the note still billing the work, and the message says a partial one does not suffice",
    ).toBe("dental_record_acts_changed_after_billing")

    // The rest of it, so the note is fully credited.
    const rest = await api.avoir(note.id, { amount: 40, reason: "E2E — the remainder" })
    expect(rest.status, `second avoir: ${rest.raw?.slice(0, 300)}`).toBeLessThan(300)

    /*
     * ⚠️ The refusal now CHANGES rather than clearing, and that ordering is deliberate: `Check` asks `IsSpent`
     * **first**, « because an invoice that can take no money at all is reported before what changed on the
     * fiche — the second is not actionable until the first is resolved ». So a fully-credited note answers
     * `dental_record_invoice_not_live`, whose own remedy is to re-bill from « Facturer cette intervention ».
     *
     * The first draft of this test expected the edit to succeed. It does not, and asserting the *shift* is the
     * honest measurement: it proves the avoir was seen.
     */
    r = await resave(api, fiche, { acts: changedActs })
    expect(r.status).toBe(400)
    expect(r.body?.code ?? r.body?.Code).toBe("dental_record_invoice_not_live")
  })

  test("FEDIT-04 · « Corriger » is the escape hatch: a `correctionReason` retires the note and re-bills", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Corriger")
    const { act, fiche, note } = await billedFiche(api, arrange, p.id, 100, 60)

    /*
     * Correcting is NOT an avoir, and that distinction is the whole branch: an avoir records money going back
     * to the patient, and a mis-keyed amount gave nothing back. Supplying `correctionReason` retires the wrong
     * note (payments marked never-received, note cancelled, number spent-and-cancelled so the sequence stays
     * gapless) and the corrected séance raises a fresh one.
     */
    const r = await resave(api, fiche, {
      acts: [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 250 })],
      amountPaid: 250,
      correctionReason: "E2E — l'acte avait été mal saisi",
    })
    expect(r.status, `correction: ${r.raw?.slice(0, 400)}`).toBe(200)

    const invoices = await api.invoices(`?patientId=${p.id}&pageSize=20`)
    const retired = invoices.items.find((i: any) => i.id === note.id)
    const replacement = invoices.items.find((i: any) => i.id !== note.id)

    expect(retired?.status, "the wrong note should never have existed — it is cancelled, not credited").toBe(
      "Cancelled",
    )
    expect(replacement, "the corrected séance raises a fresh note").toBeTruthy()
    expect(mil(replacement.totalTtc)).toBe(mil(250))
    expect(
      replacement.number,
      "the number is spent and marked cancelled, so the sequence stays gapless and the trail readable",
    ).toBeTruthy()
  })
})

test.describe("HP-5b · encaisser sur un traitement @mutating @t0", () => {
  mutatingOnly()

  test("FICHE-26/27 · collecting on a DRAFT treatment mints its number and gives it a real échéancier", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Collect")
    const crown = await api.protocolAct(2)

    const started = await api.startTreatment({
      patientId: p.id,
      procedureTypeId: crown.id,
      agreedTotal: 500,
      toothNumbers: [16],
      steps: null,
    })
    expect(started.status).toBe(200)
    const plan = started.body?.value ?? started.body
    expect(plan.status, "« Suivre ce traitement » creates an un-numbered Draft").toBe("Draft")
    expect(plan.number).toBeNull()

    const item = plan.items[0]
    const r = await arrange.fiche(
      p.id,
      [arrange.act({ procedureTypeId: crown.id, name: crown.name, cost: 0, teeth: [16] })],
      {
        amountPaid: 0,
        amountCollectedOnPlan: 200,
        treatmentPlanId: plan.id,
        treatmentPlanItemId: item.id,
        treatmentPlanItemStepId: item.steps[0].id,
      },
    )
    expect(r.status, `fiche: ${r.raw?.slice(0, 300)}`).toBe(200)

    const reread = await api.plan(plan.id)
    expect(
      reread.number,
      "a Draft has no échéancier and la caisse cannot see one, so collecting MINTS the number",
    ).toBeTruthy()
    expect(mil(reread.amountPaid)).toBe(mil(200))

    // ⚠️ The corollary: an un-numbered plan must never wear a debt-bearing status. It has a number now, so the
    // debt it carries is for the total that was actually quoted.
    expect(mil(reread.totalPlanned), "the créance is for the quoted total, not one nobody quoted").toBe(mil(500))
    expect(mil(reread.outstanding)).toBe(mil(300))

    const summary = await api.billingSummary(p.id)
    expect(mil(summary.totalOutstanding ?? summary.outstanding ?? 0)).toBe(mil(300))
  })

  test("FICHE-25 · « Encaissé sur le traitement » is never folded into « Payé »", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("TwoFields")
    const crown = await api.protocolAct(2)
    const started = await api.startTreatment({
      patientId: p.id,
      procedureTypeId: crown.id,
      agreedTotal: 500,
      toothNumbers: [16],
      steps: null,
    })
    const plan = started.body?.value ?? started.body
    const item = plan.items[0]

    const r = await arrange.fiche(
      p.id,
      [arrange.act({ procedureTypeId: crown.id, name: crown.name, cost: 0, teeth: [16] })],
      {
        amountPaid: 0,
        amountCollectedOnPlan: 200,
        treatmentPlanId: plan.id,
        treatmentPlanItemId: item.id,
        treatmentPlanItemStepId: item.steps[0].id,
      },
    )
    expect(r.status).toBe(200)
    const fiche = r.body?.value ?? r.body

    expect(mil(fiche.amountPaid), "the two money fields are not interchangeable").toBe(0)

    // No note d'honoraires: the money went to the devis échéancier, which is a different ledger.
    const invoices = await api.invoices(`?patientId=${p.id}&pageSize=20`)
    expect(invoices.items).toHaveLength(0)

    // …and exactly ONE caisse movement for the 200, not two.
    const day = arrange.localDay(0).slice(0, 10)
    const ledger = await api.caisseLedger(day, day)
    const mine = (ledger.movements ?? []).filter((m: any) => m.patientId === p.id)
    expect(mine, "one payment, one movement").toHaveLength(1)
    expect(mil(mine[0].amount)).toBe(mil(200))
  })
})
