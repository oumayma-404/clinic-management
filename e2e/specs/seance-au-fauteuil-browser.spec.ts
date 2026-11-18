import { expect, mutatingOnly, test } from "../lib/fixtures"
import { gotoApp } from "../lib/goto"
import { mil } from "../lib/api"

/**
 * HP-20 · La séance au fauteuil — the two rows that live in `.tsx` (`features/e2e-hot-paths/scenarios.md` § Layer):
 * « Payé » starts empty and only « Tout payé » or a typed figure fills it, and « Enregistrer » saves without closing
 * the fiche or the visit. A wire test posts a body its author wrote, so neither could be held there.
 */
test.describe("HP-20 · séance au fauteuil — browser @mutating @t0", () => {
  mutatingOnly()

  const minutesAgo = (m: number) => {
    const d = new Date(Date.now() - m * 60_000)
    d.setSeconds(0, 0)
    return d.toISOString()
  }

  async function runningVisit(api: any, patient: any, tag: string) {
    const p = await patient(tag)
    const act = await api.singleSeanceAct()
    const visit = await api.createAppointment({
      patientId: p.id,
      appointmentDateTime: minutesAgo(10),
      durationMinutes: 60,
      procedures: [{ procedureTypeId: act.id, agreedCost: null }],
    })
    return { p, act, visit }
  }

  const fiche = (page: any) => page.locator('[role="dialog"]').filter({ hasText: /Actes de la séance/ }).last()

  test("SEAT-11 · « Payé » starts empty; « Tout payé » fills the total", async ({ api, page, patient, sharedContext }) => {
    const { p, act, visit } = await runningVisit(api, patient, "FauteuilPaye")
    await gotoApp(page, `/patients/${p.id}?addRecord=1&appointmentId=${visit.id}`, sharedContext)

    const d = fiche(page)
    await expect(d.getByText(act.name).first()).toBeVisible()
    await expect(d.locator("#paid"), "nothing is recorded as taken until somebody says so").toHaveValue("")

    await d.getByRole("button", { name: /Tout payé/ }).click()
    const typed = (await d.locator("#paid").inputValue()).replace(/\s/g, "").replace(",", ".")
    expect(mil(Number(typed))).toBe(mil(Number(act.defaultCost)))
  })

  test("SEAT-12 · « Enregistrer » saves in place: the fiche stays open and the visit keeps running", async ({
    api,
    page,
    patient,
    sharedContext,
  }) => {
    const { p, act, visit } = await runningVisit(api, patient, "FauteuilStay")
    await gotoApp(page, `/patients/${p.id}?addRecord=1&appointmentId=${visit.id}`, sharedContext)

    const d = fiche(page)
    await expect(d.getByText(act.name).first()).toBeVisible()
    await expect(d.getByRole("button", { name: /^Terminer la séance/ })).toBeVisible()
    await d.getByRole("button", { name: /^Enregistrer$/ }).click()
    await expect(d.getByRole("status").filter({ hasText: /Enregistré à \d{2}:\d{2}/ })).toBeVisible()

    // A second press updates the same fiche — never a second one.
    await d.getByRole("button", { name: /^Enregistrer$/ }).click()
    await expect(d).toBeVisible()
    const records = await api.records(p.id)
    expect((records.items ?? records) as any[], "one fiche for one séance").toHaveLength(1)
    expect((await api.appointment(visit.id)).status, "the patient is still in the chair").not.toBe("Completed")
  })
})
