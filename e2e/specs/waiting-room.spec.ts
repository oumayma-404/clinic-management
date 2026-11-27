import { expect, mutatingOnly, test } from "../lib/fixtures"

/**
 * HP-21 · La salle d'attente (`features/salle-attente`). Arrival, the queue, « Patient suivant » and every way back,
 * over the wire. The two rows whose half lives in `.tsx` — the desk's call banner and the toast's « Annuler » — are
 * held by the two-person browser pass (`features/salle-attente/qa/walk.mjs`, A10 and A13): the suite has one session,
 * and the banner is for a SECOND person.
 *
 * ⚠️ Every call names its visit (`appointmentId`) rather than « the first of the queue »: earlier tests and earlier
 * runs leave patients waiting on the same day, and « Patient suivant » would rightly call THEM.
 */
test.describe("HP-21 · salle d'attente @mutating @t0", () => {
  mutatingOnly()
  test.describe.configure({ mode: "serial" })

  const at = (minutesFromNow: number) => {
    const d = new Date(Date.now() + minutesFromNow * 60_000)
    d.setSeconds(0, 0)
    return d.toISOString()
  }
  const board = (api: any) => api.ok("GET", "/appointments/waiting-room")
  const move = (api: any, id: string, lane: string) => api.call("POST", `/appointments/${id}/waiting-room`, { lane })
  const lane = (b: any, id: string) =>
    (["upcoming", "waiting", "inChair", "done"] as const).find((k) => b[k].some((v: any) => v.appointmentId === id)) ?? null
  const visitOf = (b: any, id: string) =>
    [...b.upcoming, ...b.waiting, ...b.inChair, ...b.done].find((v: any) => v.appointmentId === id)

  /** The dentist this session calls for — its own chair, else the cabinet's only one. */
  async function chairOf(api: any) {
    const b = await board(api)
    return (b.callerDoctorId ?? b.dentists[0]?.doctorId) as string
  }

  async function booked(api: any, patient: any, tag: string, minutesFromNow: number) {
    const p = await patient(tag)
    const visit = await api.createAppointment({
      patientId: p.id,
      doctorId: await chairOf(api),
      appointmentDateTime: at(minutesFromNow),
      durationMinutes: 30,
    })
    return { p, visit }
  }

  test("WAIT-01 · « Arrivé » puts the visit in the waiting room with its time", async ({ api, patient }) => {
    const { visit } = await booked(api, patient, "Attente01", 30)

    const r = await move(api, visit.id, "Waiting")
    expect(r.status, r.raw?.slice(0, 300)).toBe(200)
    const b = await board(api)
    expect(lane(b, visit.id)).toBe("waiting")
    expect(visitOf(b, visit.id).arrivedAtUtc, "the wait counts from a real instant").toBeTruthy()
    expect((await api.appointment(visit.id)).waitingRoom?.lane, "the agenda's DTO carries the same lane").toBe("Waiting")
  })

  test("WAIT-02 · a walk-in is a real RDV of today, already waiting", async ({ api, patient }) => {
    const p = await patient("Attente02")
    const doctorId = await chairOf(api)

    const change = await api.ok("POST", "/appointments/waiting-room/walk-in", { patientId: p.id, doctorId })
    const appointment = await api.appointment(change.appointmentId)
    expect(appointment.waitingRoom.isWalkIn).toBe(true)
    expect(appointment.waitingRoom.lane).toBe("Waiting")
    expect(appointment.doctorId).toBe(doctorId)
    expect((await api.appointmentsOf(p.id)).map((a: any) => a.id), "on the agenda like any visit").toContain(appointment.id)

    const again = await api.call("POST", "/appointments/waiting-room/walk-in", { patientId: p.id, doctorId })
    expect(again.status, "a second « Sans RDV » for someone already here").toBe(409)
  })

  test("WAIT-03 · « Patient suivant » seats the called patient and ends the one in the chair", async ({ api, patient }) => {
    const b0 = await board(api)
    test.skip(!b0.canCallNext, "this session's account calls for no dentist — nothing to call with")
    const first = await booked(api, patient, "Attente03a", -5)
    const second = await booked(api, patient, "Attente03b", -5)
    await api.ok("POST", `/appointments/${first.visit.id}/waiting-room`, { lane: "Waiting" })
    await api.ok("POST", `/appointments/${second.visit.id}/waiting-room`, { lane: "Waiting" })

    await api.ok("POST", "/appointments/waiting-room/call-next", { appointmentId: first.visit.id })
    let b = await board(api)
    expect(lane(b, first.visit.id)).toBe("inChair")
    expect(visitOf(b, first.visit.id).calledInAtUtc).toBeTruthy()

    await api.ok("POST", "/appointments/waiting-room/call-next", { appointmentId: second.visit.id })
    b = await board(api)
    expect(lane(b, second.visit.id), "the next one is seated").toBe("inChair")
    expect(lane(b, first.visit.id), "and the chair's previous patient is done").toBe("done")
    expect((await api.appointment(first.visit.id)).status).toBe("Completed")

    const twice = await api.call("POST", "/appointments/waiting-room/call-next", { expectedNextId: first.visit.id })
    expect(twice.status, "a second press for a patient already called is refused, never the one after").toBe(409)
  })

  test("WAIT-04 · the queue follows who is ready first, not who arrived first", async ({ api, patient }) => {
    const later = await booked(api, patient, "Attente04a", 45)
    const due = await booked(api, patient, "Attente04b", -5)
    await api.ok("POST", `/appointments/${later.visit.id}/waiting-room`, { lane: "Waiting" })
    await api.ok("POST", `/appointments/${due.visit.id}/waiting-room`, { lane: "Waiting" })

    const order = (await board(api)).waiting.map((v: any) => v.appointmentId)
    expect(order.indexOf(due.visit.id), "an early arrival waits for his own slot").toBeLessThan(order.indexOf(later.visit.id))
  })

  test("WAIT-05 · « Annuler » restores exactly what the action changed", async ({ api, patient }) => {
    const { visit } = await booked(api, patient, "Attente05", 30)

    const change = await api.ok("POST", `/appointments/${visit.id}/waiting-room`, { lane: "Waiting" })
    await api.ok("POST", "/appointments/waiting-room/restore", { before: change.before, removeWalkInIds: change.removeWalkInIds })

    const back = await api.appointment(visit.id)
    expect(back.waitingRoom.lane).toBe("Upcoming")
    expect(back.waitingRoom.arrivedAtUtc ?? null).toBeNull()
  })

  test("WAIT-07 · « Parti sans être vu » cancels with its reason, and comes back", async ({ api, patient }) => {
    const { visit } = await booked(api, patient, "Attente07", -5)
    await api.ok("POST", `/appointments/${visit.id}/waiting-room`, { lane: "Waiting" })

    await api.ok("POST", `/appointments/${visit.id}/waiting-room`, { lane: "Left" })
    const left = await api.appointment(visit.id)
    expect(left.status).toBe("Cancelled")
    expect(left.waitingRoom.lane).toBe("Left")

    await api.ok("POST", `/appointments/${visit.id}/waiting-room`, { lane: "Waiting" })
    const back = await api.appointment(visit.id)
    expect(back.status, "every move is reversible").not.toBe("Cancelled")
    expect(back.waitingRoom.lane).toBe("Waiting")
  })

  test("WAIT-08 · the payment at the desk frees the chair", async ({ api, arrange, patient }) => {
    const b0 = await board(api)
    test.skip(!b0.canCallNext, "this session's account calls for no dentist — nothing to seat with")
    const { p, visit } = await booked(api, patient, "Attente08", -5)
    await api.ok("POST", `/appointments/${visit.id}/waiting-room`, { lane: "Waiting" })
    await api.ok("POST", "/appointments/waiting-room/call-next", { appointmentId: visit.id })

    const act = await api.singleSeanceAct()
    const cost = Number(act.defaultCost)
    const saved = await arrange.fiche(
      p.id,
      [{ procedureTypeId: act.id, procedureName: act.name, cost, unitCost: cost, isPerTooth: false, toothNumbers: [] }],
      { appointmentId: visit.id, amountPaid: 0 },
    )
    expect(saved.status, saved.raw?.slice(0, 300)).toBe(200)
    const fiche = saved.body?.value ?? saved.body
    expect(lane(await board(api), visit.id), "a fiche saved mid-séance leaves him in the chair").toBe("inChair")

    const billed = await api.billRecord(fiche.id, { amount: cost, method: "Cash" })
    expect(billed.status, billed.raw?.slice(0, 300)).toBeLessThan(300)
    expect(lane(await board(api), visit.id), "paid: the chair is free").toBe("done")
    expect((await api.appointment(visit.id)).status).toBe("Completed")
  })
})
