import { expect, mutatingOnly, test } from "../lib/fixtures"
import { mil } from "../lib/api"

/**
 * HP-2 / HP-3 — a booking's acts and the tri-state update DTO.
 *
 * ⚠️ **An update DTO is tri-state**: an absent key means « unchanged », `[]` or an explicit null means
 * « clear ». Conflating them deletes data — `{ status }` alone on an appointment would drop **every act of the
 * séance**. And `SetProcedures` replaces the whole list, so a saved `procedures` list that omits the prices
 * restores every act to its **tarif**, silently discarding the price the patient was quoted on the telephone.
 */

test.describe("HP-2/3 · les actes d'un rendez-vous @mutating @t0", () => {
  mutatingOnly()

  /**
   * Three acts on one booking, the middle one at a **negotiated** price.
   *
   * ⚠️ The negotiated figure is **derived from the act's own tarif**, never a literal: the catalogue differs
   * between a hand-edited developer database and a freshly seeded one, and a hard-coded 45-against-60 fails on
   * the second while looking exactly like a product defect.
   */
  async function threeActBooking(api: any, arrange: any, patientId: string) {
    const consult = await api.procedureNamed("Consultation / examen bucco-dentaire")
    const detartrage = await api.singleSeanceAct()
    const radio = await api.procedureNamed("Radiographie rétro-alvéolaire")

    const tarif = Number(detartrage.defaultCost)
    // Comfortably different from the tarif, and never 0 (which would be indistinguishable from « unpriced »).
    const negotiated = Math.max(5, Math.round(tarif * 0.75))

    const appt = await api.createAppointment({
      patientId,
      appointmentDateTime: arrange.slot().toISOString(),
      durationMinutes: 60,
      procedures: [
        { procedureTypeId: consult.id, agreedCost: null },
        { procedureTypeId: detartrage.id, agreedCost: negotiated },
        { procedureTypeId: radio.id, agreedCost: null },
      ],
    })
    return { consult, detartrage, radio, appt, tarif, negotiated }
  }

  test("EDIT-02 · changing ONLY the status keeps all three acts", async ({ api, arrange, patient }) => {
    const p = await patient("TriState")
    const { appt } = await threeActBooking(api, arrange, p.id)
    expect(appt.procedures ?? [], "fixture sanity: three acts on the booking").toHaveLength(3)

    // The gesture: a PUT carrying the status and nothing about the acts. Absent ⇒ unchanged.
    const r = await api.put(`/appointments/${appt.id}`, {
      id: appt.id,
      appointmentDateTime: appt.appointmentDateTime,
      durationMinutes: appt.durationMinutes,
      // ⚠️ NOT « Confirmed »: that member is legacy-only and no transition leads to it any more, so
      // Scheduled → Confirmed is refused by design (« withdrawing a confirmation » has no clinical
      // meaning — the slot is booked either way). `InProgress` is the real next stage.
      status: "InProgress",
      version: appt.version,
      allowOverlap: true,
      allowOutsideWorkingHours: true,
    })
    expect(r.status, `status-only PUT: ${r.raw?.slice(0, 300)}`).toBe(200)

    const reread = await api.appointment(appt.id)
    expect(
      reread.procedures ?? [],
      "{ status } alone would drop every act of the séance — absent means unchanged, not clear",
    ).toHaveLength(3)
  })

  test("EDIT-04 · a re-save that resends the acts keeps the NEGOTIATED price", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Negocie")
    const { detartrage, appt, tarif, negotiated } = await threeActBooking(api, arrange, p.id)

    const row = (appt.procedures ?? []).find((x: any) => x.procedureTypeId === detartrage.id)
    expect(
      mil(row.agreedCost ?? row.cost),
      `fixture sanity: ${negotiated} was agreed, not the ${tarif} tarif`,
    ).toBe(mil(negotiated))

    // The gesture: reopen and save with no change — the client resends the list it holds.
    const r = await api.put(`/appointments/${appt.id}`, {
      id: appt.id,
      appointmentDateTime: appt.appointmentDateTime,
      durationMinutes: appt.durationMinutes,
      version: appt.version,
      allowOverlap: true,
      allowOutsideWorkingHours: true,
      procedures: (appt.procedures ?? []).map((x: any) => ({
        procedureTypeId: x.procedureTypeId,
        agreedCost: x.agreedCost ?? null,
      })),
    })
    expect(r.status, `re-save: ${r.raw?.slice(0, 300)}`).toBe(200)

    const reread = await api.appointment(appt.id)
    const after = (reread.procedures ?? []).find((x: any) => x.procedureTypeId === detartrage.id)
    expect(
      mil(after.agreedCost ?? after.cost),
      "a price agreed on the telephone is the price billed — a list that omits it restores the tarif in silence",
    ).toBe(mil(negotiated))
  })

  test("BOOK-25 · the negotiated price reaches the FICHE, which otherwise re-prices from the catalogue", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("FichePrix")
    const { detartrage, appt, tarif, negotiated } = await threeActBooking(api, arrange, p.id)

    /*
     * ⚠️ The fiche prices a booked act from the CATALOGUE, not from the appointment's row: both prefill paths
     * (`applyAppointment`, `addFromProcedure`) resolve `procedureTypeId` back to a `ProcedureTypeDto` and read
     * `defaultCost`. So the negotiated 45 has to be threaded through explicitly or it reverts to the 60 tarif.
     *
     * This test asserts the fact the *client* must read — that the appointment still carries the 45 and names
     * which act it belongs to. The prefill itself is a browser behaviour and is asserted in
     * `booking-browser.spec.ts` · BOOK-25b.
     */
    const reread = await api.appointment(appt.id)
    const row = (reread.procedures ?? []).find((x: any) => x.procedureTypeId === detartrage.id)
    expect(row, "the fiche's prefill needs the act row to name its own price").toBeTruthy()
    expect(mil(row.agreedCost ?? row.cost)).toBe(mil(negotiated))
    expect(
      mil(detartrage.defaultCost),
      "…and the catalogue tarif it must NOT fall back to, which must actually differ for this to measure anything",
    ).toBe(mil(tarif))
    expect(negotiated, "the two figures must differ, or the scenario proves nothing").not.toBe(tarif)
  })

  test("BOOK-34/39 · one booking follows ONE treatment, and the second protocol act is left alone", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("UnSeul")
    // Two DIFFERENT acts that both carry a protocol — by shape, not by name.
    const crown = await api.protocolAct(2)
    const implant = await api.procedureWhere(
      "a second, different act with a catalogue protocol",
      (a: any) => (a.defaultSteps ?? []).length >= 2 && a.id !== crown.id,
    )

    // Two protocol acts on one booking. An appointment carries a single `TreatmentPlanId`.
    const first = await api.startTreatment({
      patientId: p.id,
      procedureTypeId: crown.id,
      agreedTotal: 500,
      toothNumbers: [16],
      steps: null,
    })
    expect(first.status).toBe(200)
    const crownPlan = first.body?.value ?? first.body

    const appt = await api.createAppointment({
      patientId: p.id,
      appointmentDateTime: arrange.slot().toISOString(),
      durationMinutes: 60,
      treatmentPlanId: crownPlan.id,
      treatmentPlanItemId: crownPlan.items[0].id,
      procedures: [
        {
          procedureTypeId: crown.id,
          agreedCost: null,
          treatmentPlanId: crownPlan.id,
          treatmentPlanItemId: crownPlan.items[0].id,
        },
        { procedureTypeId: implant.id, agreedCost: null },
      ],
    })

    /*
     * ⚠️ **There is no `TreatmentPlanId` on an appointment** — not on the entity, not on the DTO, not as a
     * column. `Appointments` and `AppointmentProcedures` each carry `TreatmentPlanItemId` (+ a step id), and
     * « one treatment per booking » is a fact *derived* from those. CLAUDE.md's phrasing is conceptual; a test
     * asserting the field reads `undefined` and fails for the wrong reason.
     */
    expect(appt.treatmentPlanItemId, "the booking takes the crown's own plan ACT").toBe(crownPlan.items[0].id)

    // The implant row is an ordinary act with no plan link — its card is what says which act holds the slot.
    const implantRow = (appt.procedures ?? []).find((x: any) => x.procedureTypeId === implant.id)
    expect(implantRow, "the second protocol act is still booked, just not followed").toBeTruthy()
    expect(implantRow.treatmentPlanItemId ?? null).toBeNull()

    // Exactly one treatment exists for this patient.
    const plans = await api.plans(`?patientId=${p.id}&pageSize=50`)
    expect(
      plans.items,
      "resolveAttachedPlanId refuses two — only the first undecided protocol act is followed",
    ).toHaveLength(1)
  })

  test("XCUT-01 · a paged list orders on a unique column last, so no row is shown twice", async ({ api }) => {
    /*
     * `OFFSET` over a non-unique sort shows one row twice and skips another, which reads as « a record
     * vanished ». Asserted over the real invoice list, whose sort key (issue date) is emphatically not unique.
     */
    const size = 5
    const seen = new Set<string>()
    let duplicated: string | null = null

    for (let page = 1; page <= 4; page++) {
      const chunk = await api.invoices(`?page=${page}&pageSize=${size}`)
      for (const row of chunk.items ?? []) {
        if (seen.has(row.id)) duplicated = row.id
        seen.add(row.id)
      }
      if ((chunk.items ?? []).length < size) break
    }

    expect(duplicated, `row ${duplicated} appeared on two pages — a paged read must .ThenBy(x => x.Id)`).toBeNull()
  })

  test("XCUT-03 · `paging: null` reads everything — a money total is not a very large page", async ({
    api,
  }) => {
    const all = await api.receivables("")
    // A freshly bootstrapped database legitimately has few or no receivables, and « fewer rows than the page
    // size » is not a defect — so the scenario is reported as *not exercised* rather than passing vacuously.
    test.skip(
      (all.items ?? []).length < 2,
      `only ${(all.items ?? []).length} receivable(s) on this database — paging cannot be exercised`,
    )

    const paged = await api.receivables("?page=1&pageSize=2")
    expect(paged.items).toHaveLength(2)
    expect(
      mil(paged.totalOutstanding),
      "the TOTAL must be over the whole window, never over the cut page",
    ).toBe(mil(all.totalOutstanding))
  })
})
