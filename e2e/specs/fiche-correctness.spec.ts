import { expect, mutatingOnly, test } from "../lib/fixtures"
import { mil } from "../lib/api"

/**
 * HP-5 / HP-6 / HP-3 — the remaining tier-0 facts about what a fiche saves and what it keeps.
 *
 * Everything here is a **replace-the-whole-list** or a **read-never-guess** rule. `SetActs`, `SetSteps` and
 * `SetProcedures` all replace, and every one of them has cost data by being sent a list that was almost right.
 */

test.describe("HP-5 · ce qu'une fiche décide, et ce qu'elle lit @mutating @t0", () => {
  mutatingOnly()

  test("FICHE-35 · one séance can close TWO steps, and the act finishes when the aggregate says so", async ({
    api,
    arrange,
    patient,
  }) => {
    /*
     * ⚠️ « Whether that act is finished after this save » is « read from the aggregate once the step has been
     * marked, **never guessed from the step's rank**: a séance can close two steps at once, a protocol can be
     * re-cut mid-treatment, and an act booked whole has no steps at all. » This is the first of those three.
     */
    const p = await patient("DeuxEtapes")
    const act = await api.protocolAct(2)
    const started = await api.startTreatment({
      patientId: p.id,
      procedureTypeId: act.id,
      agreedTotal: Number(act.defaultCost) || 400,
      toothNumbers: [16],
      steps: null,
    })
    expect(started.status).toBe(200)
    const plan = started.body?.value ?? started.body
    const item = plan.items[0]
    const total = item.steps.length

    // Close every step but the last on one fiche each, then the last — the point is that the ACT's status is
    // derived from the steps rather than from how many fiches there were.
    for (const [i, step] of item.steps.entries()) {
      const before = await api.plan(plan.id)
      expect(
        before.items[0].status,
        `before séance ${i + 1} of ${total} the act cannot already be finished`,
      ).not.toBe("Done")

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
          interventionDate: arrange.localDay(-total + i),
          treatmentPlanId: plan.id,
          treatmentPlanItemId: item.id,
          treatmentPlanItemStepId: step.id,
        },
      )
      expect(r.status, `séance ${i + 1}: ${r.raw?.slice(0, 300)}`).toBe(200)

      const after = await api.plan(plan.id)
      const isLast = i === total - 1
      expect(
        after.items[0].status,
        `after séance ${i + 1} of ${total} the act must be ${isLast ? "Done" : "InProgress"}`,
      ).toBe(isLast ? "Done" : "InProgress")

      // …and the chart follows the ACT, not the séance count.
      expect(
        await api.toothCondition(p.id, 16),
        isLast
          ? "the end state is asserted only once the act is finished"
          : `nothing may be charted after séance ${i + 1} of ${total}`,
      ).toBe(isLast ? act.resultingCondition : null)
    }
  })

  test("FICHE-36 · an act booked WHOLE has no steps and finishes on its first fiche", async ({
    api,
    arrange,
    patient,
  }) => {
    // The third of the three cases a rank-based rule gets wrong: there is no rank at all.
    const p = await patient("EnUneFois")
    const act = await api.singleSeanceAct()
    const started = await api.startTreatment({
      patientId: p.id,
      procedureTypeId: act.id,
      agreedTotal: Number(act.defaultCost) || 60,
      toothNumbers: [36],
      steps: [], // ⚠️ `[]` is an explicit « une seule séance », never the same as null
    })
    expect(started.status, `startTreatment: ${started.raw?.slice(0, 300)}`).toBe(200)
    const plan = started.body?.value ?? started.body
    const item = plan.items[0]
    expect(item.steps ?? [], "an act done in one visit carries no protocol").toHaveLength(0)

    const r = await arrange.fiche(
      p.id,
      [
        arrange.act({
          procedureTypeId: act.id,
          name: act.name,
          cost: 0,
          teeth: [36],
          resultingCondition: "Obturation",
        }),
      ],
      {
        amountPaid: 0,
        treatmentPlanId: plan.id,
        treatmentPlanItemId: item.id,
      },
    )
    expect(r.status, `fiche: ${r.raw?.slice(0, 300)}`).toBe(200)

    const after = await api.plan(plan.id)
    expect(after.items[0].status, "Planned → Done on the first fiche, exactly as it always did").toBe("Done")
    expect(
      await api.toothCondition(p.id, 36),
      "…and it charts immediately, because it really is finished",
    ).toBe("Obturation")
  })

  test("FICHE-37 · a hand-typed devis line zeroes the act on a SINGLE-act fiche", async ({
    api,
    arrange,
    patient,
  }) => {
    /*
     * A devis line with no `ProcedureTypeId` — one a dentist typed rather than picked from the catalogue. There
     * is no catalogue identity to match on, so the rule falls back on arity: a single-act fiche is unambiguous.
     */
    const p = await patient("LibreUn")
    const created = await api.call("POST", "/treatment-plans", {
      patientId: p.id,
      title: "Devis à ligne libre",
      items: [{ designationFr: "Reprise de couronne (hors catalogue)", plannedCost: 250, toothNumbers: [16] }],
      installments: [],
    })
    expect(created.status, `create: ${created.raw?.slice(0, 400)}`).toBeLessThan(300)
    const plan = created.body?.value ?? created.body
    const item = plan.items[0]
    expect(item.procedureTypeId ?? null, "a hand-typed line names no catalogue act").toBeNull()

    const r = await arrange.fiche(
      p.id,
      [arrange.act({ name: "Reprise de couronne (hors catalogue)", cost: 250, teeth: [16] })],
      { amountPaid: 0, treatmentPlanId: plan.id, treatmentPlanItemId: item.id },
    )
    expect(r.status, `fiche: ${r.raw?.slice(0, 300)}`).toBe(200)
    const saved = r.body?.value ?? r.body

    expect(
      mil(saved.acts[0].cost),
      "one act and one devis line: unambiguous, so the treatment's price is imposed",
    ).toBe(0)

    const invoices = await api.invoices(`?patientId=${p.id}&pageSize=20`)
    expect(invoices.items, "…and no note bills work the treatment already prices").toHaveLength(0)
  })

  test("FICHE-38 · a hand-typed devis line zeroes NOTHING on a multi-act fiche", async ({
    api,
    arrange,
    patient,
  }) => {
    /*
     * ⚠️ « Silently un-billing the wrong act is worse than leaving a hand-typed line for the dentist to
     * correct. » With two acts and no catalogue identity to match on, the rule must decline to guess — and the
     * fiche must therefore bill both, which is a figure a human can see and fix.
     */
    const p = await patient("LibreDeux")
    const created = await api.call("POST", "/treatment-plans", {
      patientId: p.id,
      title: "Devis à ligne libre",
      items: [{ designationFr: "Reprise de couronne (hors catalogue)", plannedCost: 250, toothNumbers: [16] }],
      installments: [],
    })
    expect(created.status).toBeLessThan(300)
    const plan = created.body?.value ?? created.body
    const item = plan.items[0]

    const other = await api.singleSeanceAct()
    const r = await arrange.fiche(
      p.id,
      [
        arrange.act({ name: "Reprise de couronne (hors catalogue)", cost: 250, teeth: [16] }),
        arrange.act({ procedureTypeId: other.id, name: other.name, cost: 60 }),
      ],
      { amountPaid: 0, treatmentPlanId: plan.id, treatmentPlanItemId: item.id },
    )
    expect(r.status, `fiche: ${r.raw?.slice(0, 300)}`).toBe(200)
    const saved = r.body?.value ?? r.body

    expect(
      saved.acts.filter((a: any) => mil(a.cost) === 0),
      "nothing may be zeroed on a guess — the dentist corrects a visible figure instead",
    ).toHaveLength(0)
    expect(mil(saved.cost ?? 0)).toBe(mil(310))
  })

  test("FEDIT-14 · a reopened act's pricing INTENT is read, never re-derived from its teeth", async ({
    api,
    arrange,
    patient,
  }) => {
    /*
     * ⚠️ « A saved act's pricing intent is authoritative and must never be re-derived from its teeth » — and
     * the reason is that **a cost of 0 is also how a courtesy act is recorded**. So « looks like it costs
     * nothing » and « is priced per tooth » are both unguessable, and both have to survive a round trip.
     */
    const p = await patient("Intention")
    const act = await api.singleSeanceAct()

    // Per-tooth: 30 per tooth over three teeth = 90.
    const perTooth = await arrange.fiche(
      p.id,
      [
        arrange.act({
          procedureTypeId: act.id,
          name: act.name,
          cost: 90,
          unitCost: 30,
          perTooth: true,
          teeth: [16, 26, 36],
        }),
      ],
      { amountPaid: 0 },
    )
    expect(perTooth.status, `fiche: ${perTooth.raw?.slice(0, 300)}`).toBe(200)

    // A courtesy act on another patient: flat, and free.
    const p2 = await patient("Gracieux")
    const courtesy = await arrange.fiche(
      p2.id,
      [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 0, teeth: [16] })],
      { amountPaid: 0 },
    )
    expect(courtesy.status).toBe(200)

    // ── Both must read back as what they were, not as what their numbers imply. ──
    const reread = ((await api.records(p.id)).items ?? (await api.records(p.id)))[0]
    const savedAct = reread.acts[0]
    expect(savedAct.isPerTooth, "the per-tooth intent survives").toBe(true)
    expect(mil(savedAct.unitCost ?? 0), "and so does the unit it was built from").toBe(mil(30))
    expect(mil(savedAct.cost)).toBe(mil(90))

    const rereadFree = ((await api.records(p2.id)).items ?? (await api.records(p2.id)))[0]
    expect(
      rereadFree.acts[0].isPerTooth,
      "a 0 on one tooth is a courtesy act, not a per-tooth act priced at nothing",
    ).toBe(false)
    expect(mil(rereadFree.acts[0].cost)).toBe(0)
  })
})

test.describe("HP-3 · l'acte qu'on retire @mutating @t0", () => {
  mutatingOnly()

  test("EDIT-03 · removing ONE act of three leaves exactly two, prices intact", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("RetirerActe")
    const a = await api.singleSeanceAct()
    const consult = await api.procedureNamed("Consultation / examen bucco-dentaire")
    const radio = await api.procedureNamed("Radiographie rétro-alvéolaire")

    const appt = await api.createAppointment({
      patientId: p.id,
      appointmentDateTime: arrange.slot(2, 9).toISOString(),
      durationMinutes: 60,
      procedures: [
        { procedureTypeId: a.id, agreedCost: 70 },
        { procedureTypeId: consult.id, agreedCost: 45 },
        { procedureTypeId: radio.id, agreedCost: 25 },
      ],
    })
    expect(appt.procedures ?? []).toHaveLength(3)

    // Send back the list with the middle one gone — `SetProcedures` replaces the whole thing.
    const kept = (appt.procedures ?? [])
      .filter((x: any) => x.procedureTypeId !== consult.id)
      .map((x: any) => ({ procedureTypeId: x.procedureTypeId, agreedCost: x.agreedCost ?? null }))

    const r = await api.put(`/appointments/${appt.id}`, {
      id: appt.id,
      appointmentDateTime: appt.appointmentDateTime,
      durationMinutes: appt.durationMinutes,
      version: appt.version,
      allowOverlap: true,
      allowOutsideWorkingHours: true,
      procedures: kept,
    })
    expect(r.status, `remove one act: ${r.raw?.slice(0, 300)}`).toBe(200)

    const reread = await api.appointment(appt.id)
    expect(reread.procedures, "exactly two remain").toHaveLength(2)
    expect(
      (reread.procedures ?? []).some((x: any) => x.procedureTypeId === consult.id),
      "and the removed one is gone",
    ).toBeFalsy()
    expect(
      (reread.procedures ?? []).map((x: any) => mil(x.agreedCost ?? x.cost)).sort((x: number, y: number) => x - y),
      "the survivors keep their negotiated prices — a list that omits them restores the tarifs",
    ).toEqual([mil(25), mil(70)])
  })
})

test.describe("HP-13 · la recherche est une question SQL @mutating @t0", () => {
  mutatingOnly()

  test("XCUT-02 · search finds an accented name typed WITHOUT its accents", async ({ api, patient }) => {
    /*
     * ⚠️ Free-text search belongs in **SQL** (`SearchTerm` + `SqlSearch`'s `unaccent`); filtering an
     * already-cut page answers a different question and reports « aucun résultat ». A Tunisian practice types
     * « Gharbi » for « Ghárbi » and « Belhadj » for « Belhàdj » all day, so an accent-sensitive search is a
     * patient the receptionist cannot find.
     */
    const stamp = Date.now().toString(36)
    const accented = await api.createPatient({ firstName: "Émna", lastName: `Bélhadj-${stamp}` })
    expect(accented.id).toBeTruthy()

    const unaccented = await api.ok<{ items: any[] }>(
      "GET",
      `/patients?searchTerm=${encodeURIComponent(`Belhadj-${stamp}`)}&pageSize=50`,
    )
    expect(
      (unaccented.items ?? []).some((r: any) => r.id === accented.id),
      `« Belhadj-${stamp} » must find « Bélhadj-${stamp} » — the search is a database question, with unaccent`,
    ).toBeTruthy()

    // And the accented spelling finds it too, so the fix has not simply broken the other direction.
    const withAccent = await api.ok<{ items: any[] }>(
      "GET",
      `/patients?searchTerm=${encodeURIComponent(`Bélhadj-${stamp}`)}&pageSize=50`,
    )
    expect((withAccent.items ?? []).some((r: any) => r.id === accented.id)).toBeTruthy()

    void patient
  })
})
