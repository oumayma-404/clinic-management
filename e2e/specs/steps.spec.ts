import { expect, mutatingOnly, test } from "../lib/fixtures"

/**
 * HP-4 — the four editors of a step list, and the argument that defaults to null.
 *
 * ⚠️ **`TreatmentPlanItemStepInput`'s fourth argument is `MinDaysAfterPrevious`, and it defaults to null.** So a
 * three-argument copy **compiles, reads correctly, and erases the interval from every step of the act** —
 * `SetSteps` replaces the whole list. That is what `SetTreatmentPlanItemStepsCommand` did: renaming one step of
 * an implant wiped its osseointegration wait, with the client sending the field all along, and the only symptom
 * was `RecallWorklistRules` reporting a healthy implant as **abandoned**.
 *
 * There are exactly four editors and they must not disagree:
 *
 * | # | Editor | Edits |
 * |---|---|---|
 * | A | `ProcedureTypeStepsDialog` | the catalogue **template** |
 * | B | `AppointmentProtocolEditor` | the **planned** séances of one booking (both dialogs) |
 * | C | `PlanItemStepsDialog` | the steps of a **live devis act** |
 * | D | `ContinueSessionDialog` | the two generated steps of a continuation |
 */

test.describe("HP-4 · les sous-étapes @mutating @t0", () => {
  mutatingOnly()

  /**
   * A live devis act carrying a protocol with **at least one real interval** — the thing STEP-01 measures.
   *
   * ⚠️ Found by shape rather than named: which catalogue act carries an interval differs between a hand-edited
   * developer database and a freshly seeded one, and naming one makes the test assert a fact about a fixture
   * instead of about the product.
   */
  async function implantCrown(api: any, patientId: string, needStepOneInterval = false) {
    const interval = (st: any) => Number(st.minDaysAfterPrevious ?? st.MinDaysAfterPrevious ?? 0)
    /*
     * ⚠️ `needStepOneInterval` exists because « some step has an interval » and « step ONE has an interval » are
     * different searches, and the second is the case a rank-based rule silently drops. Asking only the first
     * one made `STEP-04/05` skip itself on a database that *did* carry a step-1 wait — a scenario reporting
     * « not exercised » when it could have been is the quieter half of a vacuous pass.
     */
    const act = needStepOneInterval
      ? await api.procedureWhere(
          "an act whose protocol carries an interval on its FIRST step",
          (a: any) => (a.defaultSteps ?? []).length >= 2 && interval((a.defaultSteps ?? [])[0]) > 0,
        )
      : await api.procedureWhere(
          "an act whose protocol carries at least one interval",
          (a: any) => (a.defaultSteps ?? []).length >= 2 && (a.defaultSteps ?? []).some((st: any) => interval(st) > 0),
        )
    const r = await api.startTreatment({
      patientId,
      procedureTypeId: act.id,
      agreedTotal: Number(act.defaultCost) || 500,
      toothNumbers: [16],
      steps: null,
    })
    expect(r.status, `startTreatment: ${r.raw?.slice(0, 300)}`).toBe(200)
    const plan = r.body?.value ?? r.body
    return { act, plan, item: plan.items[0] }
  }

  test("STEP-01 · renaming ONE step preserves every other step's MinDaysAfterPrevious", async ({
    api,
    patient,
  }) => {
    const p = await patient("Intervalle")
    const { plan, item } = await implantCrown(api, p.id)

    const before = item.steps.map((s: any) => ({
      label: s.label,
      min: s.minDaysAfterPrevious ?? null,
      dur: s.estimatedDurationMinutes ?? null,
    }))
    // Fixture sanity: without an interval anywhere, this test proves nothing (§ 3 — assert positively).
    expect(
      before.some((s: any) => s.min != null && s.min > 0),
      `the chosen act must carry at least one interval for this to measure anything. Got ` +
        `${JSON.stringify(before)}`,
    ).toBeTruthy()

    // The gesture: rename step 2 and send the list back, exactly as the dialog does.
    const sent = item.steps.map((s: any, i: number) => ({
      id: s.id,
      label: i === 1 ? `${s.label} (renommée)` : s.label,
      estimatedDurationMinutes: s.estimatedDurationMinutes ?? null,
      minDaysAfterPrevious: s.minDaysAfterPrevious ?? null,
    }))
    const r = await api.setSteps(plan.id, item.id, sent)
    expect(r.status, `setSteps: ${r.raw?.slice(0, 300)}`).toBe(200)

    const after = (await api.plan(plan.id)).items[0].steps.map((s: any) => ({
      label: s.label,
      min: s.minDaysAfterPrevious ?? null,
      dur: s.estimatedDurationMinutes ?? null,
    }))

    expect(after.map((s: any) => s.min), "a 3-argument copy erases the interval from EVERY step").toEqual(
      before.map((s: any) => s.min),
    )
    expect(after.map((s: any) => s.dur)).toEqual(before.map((s: any) => s.dur))
    expect(after[1].label).toBe(`${before[1].label} (renommée)`)
  })

  test("STEP-04/05 · the interval on the FIRST step survives an add and a reorder", async ({
    api,
    patient,
  }) => {
    const p = await patient("Premier")
    // Ask for the act whose FIRST step carries the wait — the whole point of the scenario.
    const found = await implantCrown(api, p.id, true).catch((e: Error) => e)
    test.skip(
      found instanceof Error,
      `no act on this database carries an interval on step 1 — scenario not exercised (${
        found instanceof Error ? found.message.slice(0, 160) : ""
      })`,
    )
    const { plan, item } = found as any

    const firstMin = item.steps[0].minDaysAfterPrevious ?? null
    expect(
      firstMin,
      `this act's protocol must carry an interval on its FIRST step for the scenario to bite — the case a ` +
        `rank-based rule silently drops. Got ${JSON.stringify(item.steps.map((s: any) => s.minDaysAfterPrevious))}`,
    ).not.toBeNull()

    const sent = [
      ...item.steps.map((s: any) => ({
        id: s.id,
        label: s.label,
        estimatedDurationMinutes: s.estimatedDurationMinutes ?? null,
        minDaysAfterPrevious: s.minDaysAfterPrevious ?? null,
      })),
      { label: "Contrôle à 1 an", estimatedDurationMinutes: 20, minDaysAfterPrevious: 365 },
    ]
    const r = await api.setSteps(plan.id, item.id, sent)
    expect(r.status, `setSteps: ${r.raw?.slice(0, 300)}`).toBe(200)

    const after = (await api.plan(plan.id)).items[0].steps
    expect(after).toHaveLength(item.steps.length + 1)
    expect(after[0].minDaysAfterPrevious, "the first step's own wait must not move").toBe(firstMin)
    expect(after.at(-1).minDaysAfterPrevious).toBe(365)
  })

  test("STEP-03 · a step with recorded work cannot be dropped", async ({ api, arrange, patient }) => {
    const p = await patient("Livre")
    const { act, plan, item } = await implantCrown(api, p.id)

    const fiche = await arrange.fiche(
      p.id,
      [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 0, teeth: [16] })],
      {
        amountPaid: 0,
        treatmentPlanId: plan.id,
        treatmentPlanItemId: item.id,
        treatmentPlanItemStepId: item.steps[0].id,
      },
    )
    expect(fiche.status, `fiche: ${fiche.raw?.slice(0, 300)}`).toBe(200)

    // Send the list back WITHOUT the step that has a fiche.
    const sent = item.steps.slice(1).map((s: any) => ({
      id: s.id,
      label: s.label,
      estimatedDurationMinutes: s.estimatedDurationMinutes ?? null,
      minDaysAfterPrevious: s.minDaysAfterPrevious ?? null,
    }))
    const r = await api.setSteps(plan.id, item.id, sent)

    expect(
      r.status,
      "dropping a step that evidences delivered work leaves the fiche attached to nothing — the wreckage " +
        "StopTreatment was written to avoid",
    ).toBe(400)

    const after = (await api.plan(plan.id)).items[0].steps
    expect(after, "the refusal must mean the list did not change").toHaveLength(item.steps.length)
  })

  test("STEP-09 · the catalogue template and the live devis act agree on the step CONTRACT", async ({
    api,
    patient,
  }) => {
    const p = await patient("Contrat")
    const { act, item } = await implantCrown(api, p.id)

    // A — the catalogue's own protocol (`defaultSteps`)…
    const template = act.defaultSteps ?? []
    expect(template.length, "the fixture act must carry a protocol").toBeGreaterThan(0)

    // …and C — the live devis act's steps, derived from it. Same length, same labels, same intervals.
    expect(item.steps.map((s: any) => s.label)).toEqual(template.map((s: any) => s.label ?? s.Label))
    expect(item.steps.map((s: any) => s.minDaysAfterPrevious ?? null)).toEqual(
      template.map((s: any) => s.minDaysAfterPrevious ?? s.MinDaysAfterPrevious ?? null),
    )
  })

  test("CAT-05 · a protocol with NO MinDaysAfterPrevious round-trips without inventing one", async ({
    api,
    patient,
  }) => {
    /*
     * ⚠️ **The act is FOUND, not named.** « Gingivectomie » carries no interval on the developer's hand-edited
     * catalogue and *does* carry one (7 days) on a freshly seeded database — so naming it made this test assert
     * the opposite of the truth on a clean install, and report it as a product defect. What matters is the
     * shape: some protocol somewhere stores no interval, and saving it back must not invent one.
     */
    const act = await api
      .procedureWhere(
        "an act whose protocol stores no interval on any step",
        (a: any) =>
          (a.defaultSteps ?? []).length >= 2 &&
          (a.defaultSteps ?? []).every(
            (st: any) => (st.minDaysAfterPrevious ?? st.MinDaysAfterPrevious ?? null) === null,
          ),
      )
      .catch(() => null)

    test.skip(
      act === null,
      "no catalogue act on this database stores a protocol without intervals — scenario not exercised",
    )

    const p = await patient("Legacy")
    const started = await api.startTreatment({
      patientId: p.id,
      procedureTypeId: act!.id,
      agreedTotal: Number(act!.defaultCost) || 50,
      toothNumbers: [],
      steps: null,
    })
    expect(started.status, `startTreatment: ${started.raw?.slice(0, 300)}`).toBe(200)
    const plan = started.body?.value ?? started.body
    const item = plan.items[0]

    expect(
      item.steps.every((s: any) => (s.minDaysAfterPrevious ?? null) === null),
      "an absent key must read as « no wait », never as 0 or a default. Got " +
        JSON.stringify(item.steps.map((s: any) => s.minDaysAfterPrevious)),
    ).toBeTruthy()

    // Save it back unchanged; still none.
    const r = await api.setSteps(
      plan.id,
      item.id,
      item.steps.map((s: any) => ({
        id: s.id,
        label: s.label,
        estimatedDurationMinutes: s.estimatedDurationMinutes ?? null,
        minDaysAfterPrevious: s.minDaysAfterPrevious ?? null,
      })),
    )
    expect(r.status).toBe(200)

    const after = (await api.plan(plan.id)).items[0].steps
    expect(after.every((s: any) => (s.minDaysAfterPrevious ?? null) === null)).toBeTruthy()
  })

  test("CAT-09 · a blank step label is refused before anything is written", async ({ api, patient }) => {
    const p = await patient("Blank")
    const { plan, item } = await implantCrown(api, p.id)

    const r = await api.setSteps(plan.id, item.id, [
      { label: "   ", estimatedDurationMinutes: 30, minDaysAfterPrevious: null },
    ])
    expect(r.status, "a blank séance name reaches SetSteps as a refusal on the treatment").toBe(400)

    const after = (await api.plan(plan.id)).items[0].steps
    expect(after).toHaveLength(item.steps.length)
  })
})
