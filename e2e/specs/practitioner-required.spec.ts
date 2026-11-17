import { expect, mutatingOnly, test } from "../lib/fixtures"

/**
 * BOOK-51 / BOOK-52 — a patient RDV names its dentist (cabinet-de-groupe US-1).
 *
 * The QA clinic has several active dentists (the staff-chat spec creates them and never retires them), which is
 * exactly the case the rule is for: posted through `api.post` here, never through `api.createAppointment`, whose
 * default dentist would make the refusal unreachable.
 */
test.describe("BOOK-51/52 · le praticien d'un rendez-vous @mutating", () => {
  mutatingOnly()

  test("BOOK-51 · a patient RDV with no dentist is refused once the cabinet has several", async ({
    api,
    arrange,
    patient,
  }) => {
    const status = await api.ok<any>("GET", "/clinics/user-status")
    const active = (status?.doctors ?? []).filter((d: any) => d?.id && d.isActive !== false)
    test.skip(active.length < 2, "the QA clinic has a single active dentist; the refusal does not apply")

    const p = await patient("SansPraticien")
    const refused = await api.post<any>("/appointments", {
      patientId: p.id,
      appointmentDateTime: arrange.slot().toISOString(),
      durationMinutes: 30,
      allowOutsideWorkingHours: true,
      allowOverlap: true,
    })

    expect(refused.status, refused.raw).toBe(400)
    expect(refused.body?.code).toBe("practitioner_required")
  })

  test("BOOK-52 · a « créneau occupé » with no dentist still blocks the whole cabinet", async ({ api, arrange }) => {
    const created = await api.post<any>("/appointments", {
      appointmentDateTime: arrange.slot().toISOString(),
      durationMinutes: 30,
      allowOutsideWorkingHours: true,
      allowOverlap: true,
    })

    expect(created.status, created.raw).toBeLessThan(300)
    const appt = created.body?.value ?? created.body
    expect(appt.doctorId ?? null, "a blocked slot is everybody's").toBeNull()
  })
})
