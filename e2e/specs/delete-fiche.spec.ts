import { expect, mutatingOnly, test } from "../lib/fixtures"
import { mil } from "../lib/api"

/**
 * HP-14a — **supprimer une fiche de soins**: the widest blast radius in the product, run backwards.
 *
 * ⚠️ **Why an entire file for one gesture.** `coupling-matrix.md` § 2 lists **eleven** surfaces that one fiche
 * save moves. Until 2026-09-11 the suite had 119 tests and **not one** of them deleted a fiche — the catalogue
 * HP-1…HP-13 is entirely a record of things going forward. A surface that moved forward and does not move back
 * fails in exactly the same silent way as one that never moved, and it is worse, because the screen showing it
 * was right five minutes ago.
 *
 * ⚠️ **Two of these tests are OPEN QUESTIONS, not regressions.** `DEL-03` and `DEL-08` assert what
 * `scenarios.md` says must be true; a red line on either is a product decision to make, not a broken test.
 * They are written to *measure*, and each failure message says what was measured and what the alternative
 * would be. Read `features/e2e-hot-paths/scenarios.md` § HP-14a before changing either.
 *
 * ⚠️ **Deleting is `AdminOrDoctor`** — the class-level `[Authorize]` was once the only gate, which let a
 * secretary destroy clinical records (audit defect A-12).
 */

test.describe("HP-14a · supprimer une fiche de soins @mutating @t0", () => {
  mutatingOnly()

  const deleteFiche = (api: any, patientId: string, recordId: string) =>
    api.call("DELETE", `/patients/${patientId}/dental-records/${recordId}`)

  /** Every tooth row the chart holds for one tooth — treatments and diagnoses alike. */
  async function toothRows(api: any, patientId: string, toothNumber: number) {
    const chart = await api.odontogram(patientId)
    return (chart ?? []).filter((t: any) => t.toothNumber === toothNumber)
  }

  // ── DEL-01 / DEL-02 ──────────────────────────────────────────────────────────────────────────────────────

  test("DEL-01/02 · deleting an unbilled fiche takes the fiche AND the tooth state it asserted", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("DelSimple")
    // An act whose end state is charted, so there is something to withdraw. Found by shape, never named.
    const act = await api.procedureWhere(
      "an act with a resulting condition and no protocol",
      (a: any) => !!a.resultingCondition && (a.defaultSteps ?? []).length === 0,
    )

    /*
     * ⚠️ **The end state is charted from the ACT ROW, not from the catalogue.**
     * `DentalRecordActParser` reads `a.ResultingCondition` off the fiche's own act (`DentalRecordActParser.cs:50`)
     * — the catalogue's `resultingCondition` is what the *client* prefills into it. So a wire arrange that
     * omits it charts nothing at all, and the delete then has nothing to withdraw: the test reports « the act
     * charted nothing on 16 » about a product that is working exactly as designed.
     */
    const created = await arrange.fiche(p.id, [
      arrange.act({
        procedureTypeId: act.id,
        name: act.name,
        cost: 80,
        teeth: [16],
        resultingCondition: act.resultingCondition,
      }),
    ])
    expect(created.status, created.raw?.slice(0, 300)).toBe(200)
    const record = created.body?.value ?? created.body

    const charted = await toothRows(api, p.id, 16)
    expect(
      charted.length,
      "arrange failed: the act charted nothing on 16, so there is nothing for the delete to withdraw",
    ).toBeGreaterThan(0)

    const del = await deleteFiche(api, p.id, record.id)
    expect(del.status, `delete refused: ${del.raw?.slice(0, 400)}`).toBeLessThan(300)

    const records = await api.records(p.id)
    expect(
      (records.items ?? records ?? []).some((r: any) => r.id === record.id),
      "the fiche must be gone from the patient's history",
    ).toBe(false)

    /*
     * ⚠️ `ToothState` is child-of-record and the FK cascades, so this passes *by the database* — which is
     * precisely why the unit suite cannot see it and this file must. The chart must never outlive the evidence
     * it was asserted from.
     */
    const after = await toothRows(api, p.id, 16)
    expect(
      after.filter((t: any) => t.source === "Treatment" || t.dentalRecordId === record.id).length,
      `the chart still asserts a treatment from a fiche that no longer exists: ${JSON.stringify(after)}`,
    ).toBe(0)
  })

  // ── DEL-03 — the open question ───────────────────────────────────────────────────────────────────────────

  /*
   * ⚠️ **`fixme`, not `skip`, and not left red.** This is a MEASURED, reproducible product behaviour and a
   * decision nobody has made — not a broken test. Playwright reports it as a known-unmet scenario, which is
   * `.claude/rules/verification.md` § 1's third outcome (« not exercised, with the reason ») rather than a
   * failure people learn to scroll past.
   *
   * What it measures: `DentalRecordLinker.ClearDiagnosesForTreatedTeethAsync` DELETES the « à traiter » row
   * when the fiche is saved. Deleting the fiche cascades its treatment states away (`ToothState` is
   * child-of-record) but cannot rebuild a deleted row — so the tooth reads **healthy**: the product has
   * forgotten both that the work was done and that it was ever asked for.
   *
   * The three ways out, for whoever decides: soft-delete the diagnosis and restore it on the record's delete;
   * refuse the delete of a fiche that cleared one; or accept it and say so here. Remove the `fixme` with the
   * fix, or delete the test with a tombstone — do not quietly loosen the assertion.
   */
  test.fixme("DEL-03 · the « à traiter » the fiche cleared must not be lost with the fiche", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("DelDiagnostic")
    const act = await api.procedureWhere(
      "an act with a resulting condition and no protocol",
      (a: any) => !!a.resultingCondition && (a.defaultSteps ?? []).length === 0,
    )

    // 1 · The tooth is diagnosed: « this needs work ».
    const diagnosed = await api.call("POST", `/patients/${p.id}/odontogram/conditions`, {
      toothNumber: 16,
      condition: "Carie",
      surfaces: null,
      note: "E2E DEL-03",
    })
    expect(diagnosed.status, `diagnose: ${diagnosed.raw?.slice(0, 300)}`).toBe(200)

    const diagnosedRows = await toothRows(api, p.id, 16)
    expect(
      diagnosedRows.some((t: any) => t.source === "Diagnosis"),
      "arrange failed: no diagnosis was charted on 16",
    ).toBe(true)

    // 2 · The work is done and recorded — `ClearDiagnosesForTreatedTeethAsync` DELETES the diagnosis row.
    const created = await arrange.fiche(p.id, [
      arrange.act({
        procedureTypeId: act.id,
        name: act.name,
        cost: 80,
        teeth: [16],
        resultingCondition: act.resultingCondition,
      }),
    ])
    expect(created.status, created.raw?.slice(0, 300)).toBe(200)
    const record = created.body?.value ?? created.body

    // 3 · The fiche is deleted — « that séance never happened ».
    const del = await deleteFiche(api, p.id, record.id)
    expect(del.status, `delete refused: ${del.raw?.slice(0, 400)}`).toBeLessThan(300)

    /*
     * ⚠️ **This is the measurement.** The treatment assertion goes by FK cascade; the diagnosis was *deleted*
     * at save time (`DentalRecordLinker.ClearDiagnosesForTreatedTeethAsync`, `DentalRecordLinker.cs:80`) and
     * nothing rebuilds it. If that is what happens, tooth 16 now reads **healthy**: the product forgot both
     * that the work was done and that it had ever been asked for.
     *
     * The alternatives, if this is red: soft-delete the diagnosis and restore it on the record's delete, or
     * refuse the delete of a fiche that cleared one. Doing nothing is also a choice — but it should be a
     * choice, and until this test existed nobody had made it.
     */
    const after = await toothRows(api, p.id, 16)
    expect(
      after.some((t: any) => t.source === "Diagnosis"),
      `tooth 16 now reads HEALTHY: the treatment was withdrawn with the fiche and the « à traiter » that ` +
        `justified it was destroyed when the fiche was SAVED and never came back. Rows now: ` +
        `${JSON.stringify(after)}`,
    ).toBe(true)
  })

  // ── DEL-04 / DEL-05 ──────────────────────────────────────────────────────────────────────────────────────

  test("DEL-04/05 · deleting a fiche returns its devis STEP to « à planifier » and reopens a closed plan", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("DelEtape")
    const act = await api.protocolAct(2)

    /*
     * ⚠️ `POST /treatment-plans/start` takes **one act** — `procedureTypeId` + `agreedTotal` + `steps` — not an
     * `items` array. A wrong body is not refused field by field: `ProcedureTypeId` arrives as `Guid.Empty` and
     * the answer is « Acte introuvable. », which reads as a missing catalogue act rather than a wrong shape.
     *
     * The steps are taken from the catalogue's own protocol, never named (`protocolAct` found it by shape), so
     * this works on a freshly seeded database as well as the developer's hand-edited one.
     */
    const started = await api.startTreatment({
      patientId: p.id,
      procedureTypeId: act.id,
      agreedTotal: 500,
      toothNumbers: [16],
      steps: (act.defaultSteps ?? []).slice(0, 2).map((s: any, i: number) => ({
        label: s.label ?? s.name ?? `Séance ${i + 1}`,
        estimatedDurationMinutes: s.estimatedDurationMinutes ?? null,
        minDaysAfterPrevious: s.minDaysAfterPrevious ?? null,
      })),
    })
    expect(started.status, `start: ${started.raw?.slice(0, 400)}`).toBe(200)
    const planId = (started.body?.value ?? started.body).id
    expect((await api.acceptPlan(planId, {})).status).toBe(200)

    const plan = await api.plan(planId)
    const item = plan.items[0]
    const steps = item.steps ?? []
    expect(steps.length, "arrange failed: the act has no steps").toBe(2)

    /** One fiche closing one named step. */
    const ficheForStep = async (stepId: string) => {
      const r = await arrange.fiche(
        p.id,
        [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 0, teeth: [16] })],
        { treatmentPlanId: planId, treatmentPlanItemId: item.id, treatmentPlanItemStepId: stepId },
      )
      expect(r.status, `fiche for step ${stepId}: ${r.raw?.slice(0, 400)}`).toBe(200)
      return r.body?.value ?? r.body
    }

    const first = await ficheForStep(steps[0].id)
    const second = await ficheForStep(steps[1].id)

    const closed = await api.plan(planId)
    expect(
      closed.status,
      `arrange failed: both steps are recorded, so the plan should have auto-completed. Got ${closed.status}`,
    ).toBe("Completed")

    // Delete the fiche that carried the LAST step.
    const del = await deleteFiche(api, p.id, second.id)
    expect(del.status, `delete refused: ${del.raw?.slice(0, 400)}`).toBeLessThan(300)

    /*
     * ⚠️ `DetachPlanActsAsync` walks **steps first, and separately** — a stepped act only takes its own
     * `LinkedDentalRecordId` once its LAST step lands, so an act-level loop alone never sees a fiche that
     * carried step 1 of 2, and the step would go on claiming « réalisée » against a row that is gone.
     */
    const reopened = await api.plan(planId)
    expect(
      reopened.status,
      "a devis must never stay CLOSED against evidence that has been deleted",
    ).not.toBe("Completed")

    const reopenedItem = reopened.items[0]
    const done = (reopenedItem.steps ?? []).filter((s: any) => s.isDone || s.linkedDentalRecordId)
    expect(done.length, "exactly one of the two séances survives").toBe(1)
    expect(done[0].id, "and it is the FIRST one — the surviving fiche's").toBe(steps[0].id)

    // The first fiche is untouched: its own step is still evidenced by it.
    expect(done[0].linkedDentalRecordId ?? first.id, "the surviving step still names its fiche").toBe(first.id)
  })

  // ── DEL-08 / DEL-09 ────────────────────────────────────────────────────────────────

  /**
   * Deleting a BILLED fiche **succeeds**, and every money surface stays consistent afterwards.
   *
   * ⚠️ **This row's expectation was rewritten to match the code, and the code was right.** `scenarios.md`'s
   * `FEDIT-16` said « refused, or the note is dealt with first — never an orphaned note », and a first pass at
   * this feature took that literally and made the delete refuse while a live note bills the fiche. That is the
   * wrong way round, and `DeleteDentalRecordCommand` says so at the call site: « deleting a clinical record must
   * never alter a fiscal document ». Forcing an **avoir** — a fiscal document — in order to correct a
   * *clinical* mistake is the heavier, not the lighter, outcome; the money really was received; and the note
   * keeps its own line text, so nothing is left claiming money nobody owes.
   *
   * So what this asserts is the thing that actually matters on a hot path: after the delete, the four money
   * reads still agree with each other. `document-integrity.spec.ts`'s own `FEDIT-16` holds the fiscal half
   * (number, total, collected, lines, and the cleared provenance); this holds the **coupled** half.
   */
  test("DEL-08/09 · deleting a BILLED fiche leaves the money readable and CONSISTENT on every surface", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("DelFacturee")
    const act = await api.singleSeanceAct()

    // 120 billed, 60 collected — a partial payment, so there is a créance to follow as well as a movement.
    const created = await arrange.fiche(
      p.id,
      [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 120 })],
      { amountPaid: 60 },
    )
    expect(created.status, created.raw?.slice(0, 300)).toBe(200)
    const record = created.body?.value ?? created.body

    const billed = await api.invoices(`?patientId=${p.id}&pageSize=50`)
    expect((billed.items ?? []).length, "arrange failed: the fiche raised no note").toBe(1)
    const note = billed.items[0]

    const before = await api.billingSummary(p.id)
    expect(mil(before.totalOutstanding ?? 0), "60 of 120 is still owed").toBe(mil(60))

    const del = await deleteFiche(api, p.id, record.id)
    expect(del.status, `delete: ${del.raw?.slice(0, 400)}`).toBeLessThan(300)

    /*
     * ⚠️ **The coupling, which is what a hot-path suite is for.** The note is a fiscal document and survives;
     * the question is whether the reads over it still agree once the clinical row behind it is gone. A
     * disagreement here is the silent shape every money defect in this repo has had: one surface moves and the
     * other three do not, with no error anywhere.
     */
    const after = await api.billingSummary(p.id)
    expect(mil(after.totalOutstanding ?? 0), "the note still says 60 is owed").toBe(mil(60))

    const row = await api.receivableFor(p.id, p.name)
    expect(row, "and the patient is still in « Créances »").toBeTruthy()
    expect(mil(row.totalOutstanding ?? 0), "« Créances » must agree with « Solde patient »").toBe(
      mil(after.totalOutstanding ?? 0),
    )

    // The 60 that reached the till is still in it, on the day it was taken.
    const today = (() => {
      const d = new Date()
      return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`
    })()
    const ledger = await api.caisseLedger(today, today)
    const mine = (ledger.movements ?? []).filter((m: any) => m.patientId === p.id)
    expect(mine.length, "the payment stays in la caisse — the money really was received").toBe(1)

    // And the note itself is untouched, still naming what it billed.
    const reread = await api.invoice(note.id)
    expect(reread.number, "the number is not surrendered to tidy a clinical record").toBe(note.number)
    expect(mil(reread.amountCollected ?? 0)).toBe(mil(60))
    expect(
      String(reread.lines?.[0]?.designation ?? ""),
      "its line still says what was done, so the document is not left blank",
    ).not.toBe("")
  })

  // ── DEL-12 ───────────────────────────────────────────────────────────────────────────────────────────────

  test("DEL-12 · the visit returns to « à documenter » once its fiche is deleted", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("DelCloture")
    const act = await api.singleSeanceAct()

    const appt = await arrange.booking(p.id, act.id, {
      appointmentDateTime: arrange.slot(-1, 10).toISOString(),
    })

    /*
     * ⚠️ **« Payé » stays 0 on purpose.** Since `DEL-08` a live note d'honoraires refuses the delete, so a
     * paid fiche here would test that guard a second time instead of the visit-closure coupling this row is
     * about — and it would fail for a reason that has nothing to do with « à documenter ».
     */
    const created = await arrange.fiche(
      p.id,
      [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 60 })],
      { appointmentId: appt.id, amountPaid: 0 },
    )
    expect(created.status, created.raw?.slice(0, 300)).toBe(200)
    const record = created.body?.value ?? created.body

    const del = await deleteFiche(api, p.id, record.id)
    expect(del.status, `delete refused: ${del.raw?.slice(0, 400)}`).toBeLessThan(300)

    /*
     * The visit is no longer documented, so it owes a fiche again. Asserted through the patient's own record
     * list rather than the worklist read, because the worklist is clinic-wide and other tests move it.
     */
    const records = await api.records(p.id)
    const rows = records.items ?? records ?? []
    expect(
      rows.filter((r: any) => r.appointmentId === appt.id).length,
      "the visit must have no fiche left against it",
    ).toBe(0)
  })
})
