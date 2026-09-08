import { expect, mutatingOnly, test } from "../lib/fixtures"
import { mil } from "../lib/api"

/**
 * HP-10 / HP-6 — a numbered document is a legal object, and the ledgers behind it.
 *
 * ⚠️ **A note d'honoraires' number is the thing that cannot be got wrong.** It is per-clinic, per-year and
 * **gapless**: a number that is skipped cannot be explained to an inspector, and a number reused is two
 * documents claiming to be one. Everything else in this file follows from that — a note is corrected by an
 * avoir or by a *cancellation that keeps its number spent*, and never by an edit.
 */

test.describe("HP-10 · un document numéroté @mutating @t0", () => {
  mutatingOnly()

  /** Bills a fiche and returns the note it raised. */
  async function billedNote(api: any, arrange: any, patientId: string, amount: number) {
    const act = await api.singleSeanceAct()
    const r = await arrange.fiche(
      patientId,
      [arrange.act({ procedureTypeId: act.id, name: act.name, cost: amount })],
      { amountPaid: amount },
    )
    expect(r.status, `fiche: ${r.raw?.slice(0, 300)}`).toBe(200)
    const invoices = await api.invoices(`?patientId=${patientId}&pageSize=20`)
    expect(invoices.items, "a fiche with a payment raises its note on save").toHaveLength(1)
    return invoices.items[0]
  }

  test("MONEY-01 · three notes in a row take three CONSECUTIVE numbers", async ({
    api,
    arrange,
    patient,
  }) => {
    const numbers: string[] = []
    for (const label of ["Num1", "Num2", "Num3"]) {
      const p = await patient(label)
      const note = await billedNote(api, arrange, p.id, 40)
      expect(note.number, "an issued note always carries a number").toBeTruthy()
      numbers.push(note.number)
    }

    /*
     * ⚠️ Asserted as a **sequence**, not as « each has a number ». The format is `YYYY-NNNN` per clinic per
     * year, and the failure that matters is a *gap* — which is invisible unless consecutive numbers are
     * compared against each other.
     */
    const seq = numbers.map((n) => {
      const m = /^(\d{4})-(\d+)$/.exec(n)
      expect(m, `« ${n} » is not the documented YYYY-NNNN shape`).toBeTruthy()
      return { year: m![1], n: Number(m![2]) }
    })
    expect(new Set(seq.map((s) => s.year)).size, "all three in the same year").toBe(1)
    expect(
      [seq[1].n - seq[0].n, seq[2].n - seq[1].n],
      `numbers must be gapless: got ${numbers.join(" → ")}`,
    ).toEqual([1, 1])
  })

  test("MONEY-01b · a note CANCELLED for a correction keeps its number spent — the sequence never reuses it", async ({
    api,
    arrange,
    patient,
  }) => {
    /*
     * « The number is spent and marked cancelled, so the sequence stays gapless and the trail stays readable »
     * — `UpdateDentalRecordCommand`'s correction branch says exactly that. What must never happen is the
     * cancelled number being handed to the *replacement*: two documents would then share one identity.
     */
    const p = await patient("NumCorr")
    const act = await api.singleSeanceAct()
    const created = await arrange.fiche(
      p.id,
      [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 40 })],
      { amountPaid: 40 },
    )
    expect(created.status).toBe(200)
    const fiche = created.body?.value ?? created.body
    const first = (await api.invoices(`?patientId=${p.id}&pageSize=20`)).items[0]

    const corrected = await api.updateRecord(p.id, fiche.id, {
      interventionDate: fiche.interventionDate,
      amountPaid: 90,
      isAdultTeeth: true,
      notes: [],
      importantNotes: [],
      version: fiche.version,
      correctionReason: "E2E — le montant avait été mal saisi",
      acts: [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 90 })],
    })
    expect(corrected.status, `correction: ${corrected.raw?.slice(0, 400)}`).toBe(200)

    const after = (await api.invoices(`?patientId=${p.id}&pageSize=20`)).items
    expect(after, "the retired note and its replacement both survive as records").toHaveLength(2)

    const retired = after.find((i: any) => i.id === first.id)
    const replacement = after.find((i: any) => i.id !== first.id)

    expect(retired.status, "the wrong note should never have existed — cancelled, not credited").toBe("Cancelled")
    expect(retired.number, "…but its number stays spent, so the sequence has no gap").toBe(first.number)
    expect(
      replacement.number,
      "and the replacement takes a NEW number — reusing it would make two documents one identity",
    ).not.toBe(first.number)
    expect(mil(replacement.totalTtc)).toBe(mil(90))
  })

  test("MONEY-11 · la caisse holds all three kinds of movement, and their sum is its total", async ({
    api,
    arrange,
    patient,
  }) => {
    const day = arrange.localDay(0).slice(0, 10)
    const before = await api.caisse(day, day)

    // 1 · money in from a fiche → a note d'honoraires payment.
    const p1 = await patient("MvtFiche")
    await billedNote(api, arrange, p1.id, 40)

    // 2 · money in from a devis échéancier — a different ledger entirely.
    const p2 = await patient("MvtDevis")
    const act = await api.protocolAct(2)
    const started = await api.startTreatment({
      patientId: p2.id,
      procedureTypeId: act.id,
      agreedTotal: 300,
      toothNumbers: [16],
      steps: null,
    })
    const plan = started.body?.value ?? started.body
    expect((await api.acceptPlan(plan.id, {})).status).toBe(200)
    const withEcheance = await api.plan(plan.id)
    const paid = await api.call(
      "POST",
      `/treatment-plans/${plan.id}/installments/${withEcheance.installments[0].id}/payments`,
      { amount: 120, method: "Cash", paidOn: arrange.localDay(0) },
    )
    expect(paid.status, `installment: ${paid.raw?.slice(0, 300)}`).toBeLessThan(300)

    // 3 · money OUT — an expense. The till is not only receipts.
    const spent = await api.call("POST", "/expenses", {
      expenseDate: arrange.localDay(0),
      category: "Fournitures",
      amount: 25,
      method: "Cash",
      description: "E2E — consommables",
    })
    expect(spent.status, `expense: ${spent.raw?.slice(0, 300)}`).toBeLessThan(300)

    const after = await api.caisse(day, day)
    expect(mil(after.cashIn) - mil(before.cashIn), "40 from the note + 120 from the échéancier").toBe(mil(160))
    expect(mil(after.cashOut) - mil(before.cashOut), "and 25 out").toBe(mil(25))
    expect(
      mil(after.net) - mil(before.net),
      "« net » is in minus out, and the three movements are the only ones this test made",
    ).toBe(mil(135))

    // The ledger behind those totals must hold all three, and add up to them.
    const ledger = await api.caisseLedger(day, day)
    const kinds = new Set(
      (ledger.movements ?? [])
        .filter((m: any) => !m.isVoided)
        .map((m: any) => m.kind),
    )
    for (const kind of ["InvoicePayment", "InstallmentPayment"]) {
      expect(kinds, `« ${kind} » must be represented in the extrait`).toContain(kind)
    }
    expect(
      [...kinds].some((k) => /expense/i.test(String(k))),
      `an expense must appear as its own movement kind. Kinds present: ${[...kinds].join(", ")}`,
    ).toBeTruthy()
  })

  test("MONEY-29 · « Solde patient » is a note PLUS an independent devis, each counted once", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("DeuxDettes")

    // A note owing 40 of 100.
    const act = await api.singleSeanceAct()
    const fiche = await arrange.fiche(
      p.id,
      [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 100 })],
      { amountPaid: 60 },
    )
    expect(fiche.status).toBe(200)

    // …and a separate accepted devis for 300, bridged to nothing.
    const proto = await api.protocolAct(2)
    const started = await api.startTreatment({
      patientId: p.id,
      procedureTypeId: proto.id,
      agreedTotal: 300,
      toothNumbers: [26],
      steps: null,
    })
    const plan = started.body?.value ?? started.body
    expect((await api.acceptPlan(plan.id, {})).status).toBe(200)

    /*
     * ⚠️ The two live in different ledgers and are added by one authority. 340, never 40 (the plan forgotten)
     * and never 640 (the note's own total double-counted alongside its outstanding).
     */
    const summary = await api.billingSummary(p.id)
    expect(
      mil(summary.totalOutstanding ?? summary.outstanding ?? 0),
      "40 still owed on the note + 300 quoted on the devis",
    ).toBe(mil(340))

    const receivables = await api.receivables("?pageSize=300")
    const row = (receivables.items ?? []).find((r: any) => r.patientId === p.id)
    expect(mil(row?.totalOutstanding ?? 0), "« Créances » must agree with « Solde patient »").toBe(mil(340))

    /*
     * ── « Solde dû » says what it is MADE OF (`PatientDebtLines`) ──────────────────────────────────────
     *
     * ⚠️ **The decomposition must add up to the figure above it, or the band and the total disagree in front
     * of the patient.** That is the whole risk of showing a breakdown: two reads of the same money, and only a
     * cross-check keeps them one. So this asserts the sum, not merely that lines exist — a band that itemises
     * 340 as 40 + 250 is worse than no band at all.
     */
    expect(
      mil(summary.invoiceOutstanding ?? 0) + mil(summary.installmentOutstanding ?? 0),
      "the two ledgers' own subtotals must make the total",
    ).toBe(mil(340))

    const lines = summary.lines ?? []
    expect(lines, "one line per document that owes something").toHaveLength(2)
    expect(
      lines.reduce((t: number, l: any) => t + mil(l.outstanding), 0),
      "and the lines must sum to « Solde patient » exactly",
    ).toBe(mil(340))

    const note = lines.find((l: any) => l.kind === "Invoice")
    const devis = lines.find((l: any) => l.kind === "TreatmentPlan")
    expect(note, "the note d'honoraires is one line…").toBeTruthy()
    expect(devis, "…and the devis is the other").toBeTruthy()

    // Each line names the document it is about, so « Encaisser » lands on the right ledger.
    expect(note.number, "a line names its own document").toBeTruthy()
    expect(mil(note.outstanding)).toBe(mil(40))
    expect(mil(note.collected)).toBe(mil(60))
    expect(
      note.payableInstallmentId ?? null,
      "an invoice is paid on the invoice, so it names no échéance",
    ).toBeNull()

    expect(mil(devis.outstanding)).toBe(mil(300))
    expect(
      devis.payableInstallmentId,
      "a devis is paid on an ÉCHÉANCE — the line has to say which, or « Encaisser » cannot know",
    ).toBeTruthy()

    // `payableRoom` is what a payment may not exceed; on both lines it is what is still owed.
    expect(mil(note.payableRoom)).toBe(mil(40))
    expect(mil(devis.payableRoom)).toBe(mil(300))
  })
})

test.describe("HP-6 · ce qu'une note déjà émise interdit @mutating @t0", () => {
  mutatingOnly()

  test("FEDIT-10 · a séance whose cheque is BANKED cannot be redated", async ({
    api,
    arrange,
    patient,
  }) => {
    /*
     * ⚠️ L4. Redating a fiche moves its money with it — « every money read attributes a payment by `PaidOn` »,
     * so a visit corrected to the 31st would otherwise still count in the new month and « encaissé ce mois »
     * would report a figure nobody could explain. But once the bank has the cheque, the date it cleared is a
     * fact about the bank and not ours to move: `dental_record_payment_banked` is the refusal, and it is
     * deliberately NOT correctable — an avoir would state a refund that never happened.
     */
    const p = await patient("Encaisse")
    const act = await api.singleSeanceAct()
    const created = await arrange.fiche(
      p.id,
      [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 60 })],
      {
        amountPaid: 60,
        paymentMethod: "Cheque",
        chequeNumber: `E2E-${Date.now().toString(36).slice(-6)}`,
        chequeBankName: "BIAT",
        chequeDueDate: arrange.localDay(20),
      },
    )
    expect(created.status, `fiche: ${created.raw?.slice(0, 300)}`).toBe(200)
    const fiche = created.body?.value ?? created.body

    const note = (await api.invoices(`?patientId=${p.id}&pageSize=20`)).items[0]
    const payment = (await api.invoice(note.id)).payments?.[0]
    expect(payment, "the cheque is a payment on the note").toBeTruthy()

    // ⚠️ `{ banked: true }`, not `{}` — the flag is a two-way switch (« true to record it as banked, false to
    // clear the mark »), so an empty body asks for the opposite and the scenario skips itself as not exercised.
    const banked = await api.call("POST", `/invoices/${note.id}/payments/${payment.id}/banked`, {
      banked: true,
    })
    test.skip(
      banked.status >= 300,
      `could not mark the cheque banked: HTTP ${banked.status} ${banked.raw?.slice(0, 200)} — not exercised`,
    )

    // Now redate the séance.
    const r = await api.updateRecord(p.id, fiche.id, {
      interventionDate: arrange.localDay(-5),
      amountPaid: 60,
      isAdultTeeth: true,
      notes: [],
      importantNotes: [],
      version: fiche.version,
      acts: [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 60 })],
    })

    expect(r.status, `redating a banked cheque's séance: ${r.raw?.slice(0, 400)}`).toBe(400)
    expect(r.body?.code ?? r.body?.Code).toBe("dental_record_payment_banked")

    // « Refusé » has to mean the save did not happen.
    const reread = (await api.records(p.id)).items?.[0] ?? (await api.records(p.id))[0]
    expect(
      String(reread.interventionDate).slice(0, 10),
      "the séance keeps its date, because its money is already at the bank",
    ).toBe(arrange.localDay(0).slice(0, 10))
  })

  test("FEDIT-16 · deleting a fiche leaves its note d'honoraires FISCALLY intact and clinically detached", async ({
    api,
    arrange,
    patient,
  }) => {
    /*
     * ⚠️ **The rule is stated in `DeleteDentalRecordCommand` itself, and the first version of this test asserted
     * the opposite of it.** « Drop the provenance pointer on every invoice line raised from this fiche. The
     * invoice keeps its number, its lines and its amounts — **deleting a clinical record must never alter a
     * fiscal document.** » So the note survives, numbered and paid, and that is correct: the money really was
     * received, and un-numbering a document to tidy a clinical record would be the worse outcome by far.
     *
     * What must happen instead is a **detachment**, and it has to be at the level the guard actually reads:
     * `IInvoiceRepository.GetDentalRecordLinksAsync` projects over `invoice.Lines`, and
     * `Invoice.ClearDentalRecordLinks` clears exactly those. So after the delete the guard answers « nothing
     * bills this fiche », which is what lets the séance be re-recorded and re-billed.
     *
     * ⚠️ The invoice HEADER's own `dentalRecordId` survives, and that is not the same thing: nothing reads it
     * for the billing guard, so it is inert provenance rather than a live dangling link. Asserting on it —
     * which is what the first version did — reports a defect that is not there.
     */
    const p = await patient("SupprFact")
    const act = await api.singleSeanceAct()
    const created = await arrange.fiche(
      p.id,
      [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 70 })],
      { amountPaid: 70 },
    )
    expect(created.status).toBe(200)
    const fiche = created.body?.value ?? created.body
    const note = (await api.invoices(`?patientId=${p.id}&pageSize=20`)).items[0]
    expect(note.number, "the fiche's payment raised a numbered note").toBeTruthy()

    const r = await api.call("DELETE", `/patients/${p.id}/dental-records/${fiche.id}`)
    expect(r.status, `delete: ${r.raw?.slice(0, 300)}`).toBeLessThan(300)

    // ── 1 · the fiscal document is untouched ────────────────────────────────────────────────────────────
    const after = await api.invoice(note.id)
    expect(after.number, "the number is not surrendered to tidy a clinical record").toBe(note.number)
    expect(mil(after.totalTtc)).toBe(mil(70))
    expect(mil(after.amountCollected), "the money really was received").toBe(mil(70))
    expect(after.status).toBe(note.status)
    expect(after.lines, "its lines survive, with their amounts").toHaveLength(note.lines.length)
    expect(mil(after.lines[0].lineTotalHt)).toBe(mil(70))

    // ── 2 · but its clinical provenance is dropped, at the level the guard reads ────────────────────────
    expect(
      after.lines.every((l: any) => (l.dentalRecordId ?? null) === null),
      "every line's provenance pointer must be cleared — that is what the billing guard projects over",
    ).toBeTruthy()

    // ── 3 · so nothing thinks this fiche is billed any more, and the séance can be recorded again ───────
    const fresh = await arrange.fiche(
      p.id,
      [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 70 })],
      { amountPaid: 70 },
    )
    expect(
      fresh.status,
      `the séance must be re-recordable and re-billable: ${fresh.raw?.slice(0, 300)}`,
    ).toBe(200)

    const notesNow = (await api.invoices(`?patientId=${p.id}&pageSize=20`)).items
    expect(notesNow, "…on its own new note, beside the one that survived").toHaveLength(2)
    expect(
      new Set(notesNow.map((i: any) => i.number)).size,
      "two documents, two numbers — never one identity shared",
    ).toBe(2)

    // And the money is counted once per document, not doubled onto the patient.
    const summary = await api.billingSummary(p.id)
    expect(
      mil(summary.totalOutstanding ?? summary.outstanding ?? 0),
      "both notes are fully paid, so nothing is owed",
    ).toBe(0)
  })

  test("FEDIT-18 · a 409 on a fiche save offers « Recharger », and the next save succeeds", async ({
    api,
    arrange,
    patient,
  }) => {
    /*
     * The fiche is a version-round-tripping form like any other, so it goes through `useConflict` — « any form
     * that round-trips a version », in CLAUDE.md's words. Asserted on the wire here (the browser half of the
     * same rule is `EDIT-06`): a stale version must be refused, and the CURRENT version must then succeed.
     */
    const p = await patient("FicheConflit")
    const act = await api.singleSeanceAct()
    const created = await arrange.fiche(
      p.id,
      [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 50 })],
      { amountPaid: 0 },
    )
    expect(created.status).toBe(200)
    const fiche = created.body?.value ?? created.body

    const body = (version: number, note: string) => ({
      interventionDate: fiche.interventionDate,
      amountPaid: 0,
      isAdultTeeth: true,
      notes: [note],
      importantNotes: [],
      version,
      acts: [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 50 })],
    })

    // A colleague saves first, which ages the version we are holding.
    const peer = await api.updateRecord(p.id, fiche.id, body(fiche.version, "Note du collègue"))
    expect(peer.status, `peer save: ${peer.raw?.slice(0, 300)}`).toBe(200)

    // The stale save must be refused — never silently win.
    const stale = await api.updateRecord(p.id, fiche.id, body(fiche.version, "Ma note"))
    expect(stale.status, `a stale version must not win: ${stale.raw?.slice(0, 300)}`).toBe(409)

    // Re-read (« Recharger ») and apply again.
    const fresh = ((await api.records(p.id)).items ?? (await api.records(p.id)))[0]
    const retry = await api.updateRecord(p.id, fiche.id, body(fresh.version, "Ma note"))
    expect(
      retry.status,
      `re-reading and re-applying must succeed — a form whose version never moves repeats the refusal for ever`,
    ).toBe(200)

    const saved = retry.body?.value ?? retry.body
    expect(saved.notes, "and the note that landed is the one re-applied").toContain("Ma note")
  })
})
