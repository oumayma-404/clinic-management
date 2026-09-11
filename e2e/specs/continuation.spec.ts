import { expect, mutatingOnly, test } from "../lib/fixtures"
import { mil } from "../lib/api"

/**
 * HP-8 — « cette séance est la suite de celle du 12 août » (`ContinueRecordedActCommand`).
 *
 * ⚠️ **The all-or-nothing devis→note bridge is the single most dangerous edge in the product**, and this file is
 * the guard on it. The moment a real (non-Draft, non-Cancelled) note names a plan,
 * `PlanBillingRules.BilledPlanIds` drops that plan **whole** from « Solde patient », « Créances », la caisse and
 * the dashboard. So money added to a bridged plan afterwards is owed by the patient and readable **nowhere** —
 * measured on the reported case: a 30 DT coiffage billed on a note, continued at 10 DT, left the balance reading
 * **0**.
 *
 * Which is why every test here asserts the **patient's balance**, not the plan's own figure. The plan's figure
 * was right in the defect; the balance was the surface that had stopped looking.
 */

test.describe("HP-8 · la suite d'un acte @mutating @t0", () => {
  mutatingOnly()

  /** A recorded 30 DT coiffage — the reported case's act, and the smallest one that shows the defect. */
  async function recordedCoiffage(api: any, arrange: any, patientId: string, amountPaid: number) {
    const act = await api.singleSeanceAct()
    const r = await arrange.fiche(
      patientId,
      [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 30, teeth: [24] })],
      { amountPaid },
    )
    expect(r.status, `fiche: ${r.raw?.slice(0, 300)}`).toBe(200)
    const fiche = r.body?.value ?? r.body
    return { act, fiche, ficheActId: fiche.acts[0].id }
  }

  /**
   * A **Draft** note d'honoraires over a fiche — the state `invoice-form-modal` produces, since `POST /invoices`
   * always creates a Draft and the modal sends `dentalRecordId`.
   *
   * ⚠️ Not `billRecord`: that raises an *issued* note and refuses « Le montant encaissé doit être supérieur
   * à 0. », so it cannot reach the Draft precondition at all.
   */
  async function draftNoteOver(api: any, patientId: string, ficheId: string, amount: number) {
    const r = await api.call("POST", "/invoices", {
      patientId,
      dentalRecordId: ficheId,
      lines: [{ designation: "Coiffage pulpaire", quantity: 1, unitPriceHt: amount, dentalRecordId: ficheId }],
    })
    expect(r.status, `draft note: ${r.raw?.slice(0, 300)}`).toBe(201)
    const note = r.body?.value ?? r.body
    expect(note.status, "POST /invoices creates a Draft").toBe("Draft")
    return note
  }

  const balanceOf = async (api: any, patientId: string) => {
    const s = await api.billingSummary(patientId)
    return mil(s.totalOutstanding ?? s.outstanding ?? 0)
  }

  test("CONT-01 · no note: the plan owns the act's fee, and step 1 is done against its fiche", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Suite")
    const { fiche, ficheActId } = await recordedCoiffage(api, arrange, p.id, 0)

    const r = await api.continueRecordedAct({
      dentalRecordId: fiche.id,
      actId: ficheActId,
      nextStepLabel: "Reprise du coiffage",
    })
    expect(r.status, r.raw?.slice(0, 400)).toBe(200)
    const plan = r.body?.value ?? r.body

    // Two steps, generic labels: nothing knows how the finished work was actually divided, so inventing
    // « Préparation / Empreinte » would be a claim about a patient's mouth.
    const item = plan.items[0]
    expect(item.steps.map((s: any) => s.label)).toEqual(["1re séance", "Reprise du coiffage"])
    expect(item.steps[0].doneDate, "step 1 is marked done against the fiche that evidences it").toBeTruthy()
    expect(item.steps[1].doneDate).toBeFalsy()

    // With no note, the plan owns the fee and bills it once when the work is done.
    expect(mil(plan.totalPlanned)).toBe(mil(30))
  })

  test("CONT-02 · a LIVE note and no new money: the note is ATTACHED, so nothing is claimed twice", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Attache")
    const { fiche, ficheActId } = await recordedCoiffage(api, arrange, p.id, 30)

    const before = await balanceOf(api, p.id)
    expect(before, "a fully paid 30 DT note owes nothing").toBe(0)

    const r = await api.continueRecordedAct({ dentalRecordId: fiche.id, actId: ficheActId })
    expect(r.status, r.raw?.slice(0, 400)).toBe(200)
    const plan = r.body?.value ?? r.body

    expect(
      plan.linkedInvoiceId,
      "without the link, Accept's lump-sum échéance would claim the whole total a second time",
    ).toBeTruthy()

    expect(
      await balanceOf(api, p.id),
      "attaching is what stops the double count — the balance must not move",
    ).toBe(before)
  })

  test("CONT-03/04 · a LIVE note AND new money: the note is NOT attached, and BOTH figures stay readable", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Disjoint")
    /*
     * The reported case's shape: a 30 DT coiffage on a LIVE note, continued at 10 DT.
     *
     * ⚠️ 20 collected rather than 0, because a note d'honoraires cannot be raised for nothing —
     * `BillDentalRecordCommand` refuses « Le montant encaissé doit être supérieur à 0. » So the note bills 30,
     * holds 20, and owes 10; the invariant under test is unchanged and the arithmetic is simply 10 + 10.
     */
    const { fiche, ficheActId } = await recordedCoiffage(api, arrange, p.id, 20)

    const noteBefore = await balanceOf(api, p.id)
    expect(noteBefore, "the note bills 30 and holds 20, so it owes 10").toBe(mil(10))

    const r = await api.continueRecordedAct({
      dentalRecordId: fiche.id,
      actId: ficheActId,
      nextStepLabel: "Suite",
      remainingWorkCost: 10,
      remainingWorkLabel: "Travail restant",
    })
    expect(r.status, r.raw?.slice(0, 400)).toBe(200)
    const plan = r.body?.value ?? r.body

    // The corollary the first version got backwards: where there is new money the note is NOT attached, so the
    // two documents stay disjoint.
    expect(
      plan.linkedInvoiceId,
      "a bridged plan may hold NOTHING the note does not bill — attaching here hides the 10 from every read",
    ).toBeFalsy()

    // The already-billed act sits on the devis at 0; the devis owes the new work alone.
    const billedAct = plan.items.find((i: any) => mil(i.totalCost ?? i.cost ?? 0) === 0)
    expect(billedAct, `expected one act priced 0; got ${JSON.stringify(plan.items.map((i: any) => [i.designationFr, i.totalCost ?? i.cost]))}`).toBeTruthy()
    expect(mil(plan.totalPlanned), "the devis owes the new work alone").toBe(mil(10))

    // ── The assertion the defect would have failed: the patient's own balance. ──
    // 30 on the note + 10 on the devis. The measured defect read 0.
    expect(
      await balanceOf(api, p.id),
      "a coiffage billed on a note and continued at 10 DT left the balance reading 0 — this is that assertion. " +
        "10 still owed on the note + 10 newly owed on the devis",
    ).toBe(mil(20))

    // And « Créances » is the second read that had stopped looking.
    const mine = await api.receivableFor(p.id, p.name)
    expect(mine, "the patient must appear in « Créances »").toBeTruthy()
    expect(mil(mine.totalOutstanding)).toBe(mil(20))
  })

  test("CONT-05/06 · a DRAFT note is treated exactly like a live one: disjoint from the start", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Brouillon")
    const { fiche, ficheActId } = await recordedCoiffage(api, arrange, p.id, 0)

    const draft = await draftNoteOver(api, p.id, fiche.id, 30)

    const r = await api.continueRecordedAct({
      dentalRecordId: fiche.id,
      actId: ficheActId,
      remainingWorkCost: 10,
    })
    expect(r.status, r.raw?.slice(0, 400)).toBe(200)
    const plan = r.body?.value ?? r.body

    /*
     * ⚠️ **`noteKeepsTheFirstAct` is gated on « is there a note », never on the note's STATUS** — and it read
     * the status until 2026-09-08, which was the defect `CONT-05b` below exists for. « Does this note bill
     * everything the plan holds » has one answer at every moment of the note's life (`remainingCost === 0`);
     * « does it represent the plan today » does not.
     */
    expect(
      plan.linkedInvoiceId,
      "with new money the two documents are disjoint from the start, Draft or not",
    ).toBeFalsy()
    expect(mil(plan.totalPlanned), "the devis owes the new work alone").toBe(mil(10))

    /*
     * ⚠️ 10, not 40 — and that is correct rather than a loss. A Draft note claims nothing *yet*, which is what
     * a Draft is: the same fiche under the same Draft note reads 0 with no continuation at all. The 30 is on
     * the note and arrives in the balance when the note is issued — see `CONT-05b`.
     */
    expect(
      await balanceOf(api, p.id),
      "a Draft note contributes nothing; the devis owes its own 10 and the note will owe its 30 when issued",
    ).toBe(mil(10))

    // And the devis names the Draft as a Draft rather than printing a blank where its number would be.
    expect(draft.number, "a Draft has no number").toBeNull()
    expect(plan.notes ?? "", `plan notes: ${plan.notes}`).toContain("brouillon de note d'honoraires")
  })

  test("CONT-05b · ISSUING that draft does NOT make the remaining work invisible", async ({
    api,
    arrange,
    patient,
  }) => {
    /*
     * ── The defect this test was written for, and the guard that it stays fixed ──
     *
     * `noteKeepsTheFirstAct` used to read the note's status **at continuation time**, and a Draft's status is
     * not final. `RepresentsItsPlan(Draft)` being false sent the Draft down the *other* branch, so it **was**
     * attached; issuing it later flipped the same predicate to true, `BilledPlanIds` dropped the plan whole,
     * and the plan was holding 10 DT the note does not bill. Measured before the fix:
     *
     *   fiche 30, Draft note over it        → balance  0
     *   continue with 10 of new work        → balance 40
     *   ISSUE the note (30, gets a number)  → balance 30   ← the 10 was readable NOWHERE
     *   the plan itself still reported        outstanding 40
     *
     * Exactly the state `AmendTreatmentPlanCommand` refuses in words (« acts added afterwards would be
     * invisible in every balance »), reached by a second route. Fully reachable from the UI:
     * `invoice-form-modal.tsx` sends `dentalRecordId` on create and `POST /invoices` makes a Draft.
     *
     * The unit-level twin is `ContinueRecordedActMoneyTests.Issuing_A_Draft_Note_Afterwards_Cannot_Hide_The_Remaining_Work`.
     */
    const p = await patient("DraftIssue")
    const { fiche, ficheActId } = await recordedCoiffage(api, arrange, p.id, 0)
    const draft = await draftNoteOver(api, p.id, fiche.id, 30)

    const r = await api.continueRecordedAct({
      dentalRecordId: fiche.id,
      actId: ficheActId,
      remainingWorkCost: 10,
    })
    expect(r.status).toBe(200)
    const plan = r.body?.value ?? r.body
    expect(await balanceOf(api, p.id), "the devis owes its 10 while the note is a Draft").toBe(mil(10))

    const issued = await api.call("POST", `/invoices/${draft.id}/issue`, {})
    expect(issued.status, `issue: ${issued.raw?.slice(0, 300)}`).toBe(200)

    // ── The assertion. 30 on the note + 10 on the devis; nothing has been paid. ──
    const after = await balanceOf(api, p.id)
    const reread = await api.plan(plan.id)
    expect(
      after,
      `issuing the note must not drop the plan: « Solde patient » reads ${after / 1000} DT and the plan itself ` +
        `reports ${reread.outstanding} DT. Before the fix these were 30 and 40.`,
    ).toBe(mil(40))

    // …and the two documents still agree about which holds what.
    expect(mil(reread.totalPlanned), "the devis owes the new work alone").toBe(mil(10))
    const note = await api.invoice(draft.id)
    expect(mil(note.totalTtc), "the note bills the work it always billed").toBe(mil(30))
    expect(note.treatmentPlanId ?? null, "and it was never attached").toBeNull()

    // « Créances » is the second read that had stopped looking.
    const mine = await api.receivableFor(p.id, p.name)
    expect(mine, "the patient must appear in « Créances »").toBeTruthy()
    expect(mil(mine.totalOutstanding)).toBe(mil(40))
  })

  test("CONT-05c · with NO new money a Draft note IS attached — the de-duplication is the point", async ({
    api,
    arrange,
    patient,
  }) => {
    /*
     * The other half of the same rule, and why the gate is `remainingCost === 0` rather than « never attach »:
     * with no new money the note bills **everything** the plan holds, so attaching is a true claim and the
     * de-duplication it buys is what stops a 30 DT act being owed on two documents.
     */
    const p = await patient("BrouillonSeul")
    const { fiche, ficheActId } = await recordedCoiffage(api, arrange, p.id, 0)
    const draft = await draftNoteOver(api, p.id, fiche.id, 30)

    const r = await api.continueRecordedAct({ dentalRecordId: fiche.id, actId: ficheActId })
    expect(r.status, r.raw?.slice(0, 400)).toBe(200)
    const plan = r.body?.value ?? r.body

    expect(plan.linkedInvoiceId, "the note bills the whole plan, so it speaks for it").toBe(draft.id)
    expect(mil(plan.totalPlanned)).toBe(mil(30))

    // A Draft excludes nothing, so the plan carries the 30 for now…
    expect(await balanceOf(api, p.id)).toBe(mil(30))

    // …and once issued the note carries it instead. Counted once either way — never 60.
    expect((await api.call("POST", `/invoices/${draft.id}/issue`, {})).status).toBe(200)
    expect(
      await balanceOf(api, p.id),
      "the same 30, through whichever document owns it — the double count is what attaching prevents",
    ).toBe(mil(30))
  })

  test("CONT-07 · the money already collected is NEVER replayed onto the plan", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Encaisse")
    // 30 quoted, 20 handed over — the shape of « 800 of 1 000 » at fixture scale.
    const { fiche, ficheActId } = await recordedCoiffage(api, arrange, p.id, 20)

    const day = arrange.localDay(0).slice(0, 10)
    const before = await api.caisse(day, day)

    const r = await api.continueRecordedAct({ dentalRecordId: fiche.id, actId: ficheActId })
    expect(r.status, r.raw?.slice(0, 400)).toBe(200)

    const after = await api.caisse(day, day)
    expect(
      mil(after.cashIn),
      "a plan installment posts its own caisse movement — replaying the 20 would show 40 in the till for 20 received",
    ).toBe(mil(before.cashIn))

    // The 10 still owed stays on its note, where every money read already carries it.
    expect(await balanceOf(api, p.id)).toBe(mil(10))
  })

  test("CONT-12 · an act already on a devis raises no SECOND continuation", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Deuxfois")
    const { fiche, ficheActId } = await recordedCoiffage(api, arrange, p.id, 0)

    const first = await api.continueRecordedAct({ dentalRecordId: fiche.id, actId: ficheActId })
    expect(first.status).toBe(200)

    const second = await api.continueRecordedAct({ dentalRecordId: fiche.id, actId: ficheActId })
    expect(
      second.status,
      "a second devis over the same work is what the alreadyTracked guard exists to prevent",
    ).toBe(400)

    // …and the act has left the offered list, so the dialog cannot propose it either.
    const offered = await api.continuableActs(p.id)
    const rows = offered.items ?? offered
    expect(
      (rows as any[]).filter((a: any) => a.actId === ficheActId || a.dentalRecordActId === ficheActId),
    ).toHaveLength(0)
  })
})
