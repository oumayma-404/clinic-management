import { expect, mutatingOnly, test } from "../lib/fixtures"
import { drainConfirmations } from "../lib/dialogs"
import { gotoApp } from "../lib/goto"

/**
 * HP-3 — optimistic concurrency, and the recovery a refusal must offer.
 *
 * ⚠️ **`xmin` is per-ROW, so any write ages every open form's version — whoever made it.** That is what makes
 * this a hot path rather than an edge: `AppointmentProgressJob` writes an appointment **every minute** (« En
 * cours » at its slot's start, « Séance passée » at its end), so a dialog left open across a minute boundary
 * gets a 409 that names a person when nobody touched anything. Production, 2026-09-05: the dialog read the row
 * at 15:50:04, the job wrote it at 15:50:05, the user pressed Enregistrer at 15:50:32 and was told « cet
 * enregistrement a été modifié par quelqu'un d'autre pendant votre saisie » — one clinic, one user, one session.
 *
 * ⚠️ **And the frontend half cost far more than the one refused save.** A dialog that catches a 409 into a plain
 * `setError` is **poisoned for good**: the version it holds never moves, so every later click repeats the
 * refusal. Six of them over 81 minutes, until the user reloaded the page. So the assertion that matters is not
 * « does it refuse » but **« can the user get out of it without reloading the browser »**.
 *
 * ⚠️ « Recharger » is deliberately a BUTTON and never automatic: refreshing the version behind the user's back
 * turns their next click into exactly the lost update the token exists to stop.
 */

const editDialog = (page: any) =>
  page
    .locator("[data-slot='dialog-content']")
    .filter({ has: page.locator("[data-slot='dialog-title']", { hasText: /Rendez-vous/ }) })
    .first()

/**
 * Presses Enregistrer and answers whatever the CALENDAR objects to, so what remains is the conflict.
 *
 * ⚠️ Without the drain this file tested nothing: the fixture's slot collided with a real appointment, the save
 * raised « Créneau déjà occupé » first, and the 409 was never reached. A concurrency test that never provokes a
 * conflict fails for reasons that have nothing to do with concurrency — and would have *passed* just as
 * meaninglessly had the assertion been weaker.
 */
async function saveThroughPrompts(page: any, dialog: any) {
  await dialog.locator("button", { hasText: /^Enregistrer$/ }).first().click()
  await drainConfirmations(page)
}

test.describe("HP-3 · un 409 a une sortie @mutating @t0", () => {
  mutatingOnly()

  /** An appointment far enough ahead that `AppointmentProgressJob` will not write it mid-test. */
  async function quietAppointment(api: any, arrange: any, patientId: string) {
    const act = await api.singleSeanceAct()
    return api.createAppointment({
      patientId,
      appointmentDateTime: arrange.slot(3, 15).toISOString(),
      durationMinutes: 30,
      procedures: [{ procedureTypeId: act.id, agreedCost: null }],
    })
  }

  test("EDIT-06 · a real conflict refuses, offers « Recharger », and the NEXT save succeeds", async ({
    api,
    arrange,
    page,
    patient,
    sharedContext,
  }) => {
    const p = await patient("Conflit")
    const appt = await quietAppointment(api, arrange, p.id)

    await gotoApp(page, `/appointments?appointmentId=${appt.id}`, sharedContext)
    const dialog = editDialog(page)
    await expect(dialog).toBeVisible()

    // ── A colleague saves the same row from elsewhere. This is what ages the version the dialog is holding. ──
    const peer = await api.put(`/appointments/${appt.id}`, {
      id: appt.id,
      appointmentDateTime: appt.appointmentDateTime,
      durationMinutes: 45,
      version: appt.version,
      allowOverlap: true,
      allowOutsideWorkingHours: true,
      notes: "Modifié par un collègue",
    })
    expect(peer.status, `the peer save must succeed: ${peer.raw?.slice(0, 300)}`).toBe(200)

    // Change something in the stale form, then save — the version it holds is now behind.
    await dialog.locator("button", { hasText: /^1h$/ }).first().click()
    await saveThroughPrompts(page, dialog)

    /*
     * ⚠️ The refusal must be VISIBLE and must carry its own way out. Asserted on the « Recharger » affordance
     * rather than on the sentence: « never recover an outcome by matching French prose », and the sentence is
     * the server's while the button is the client's contract.
     */
    const reload = page.locator("button", { hasText: /^Recharger$/ }).first()
    await expect(
      reload,
      "a 409 must offer « Recharger » — the recovery the server's own sentence tells the user to perform",
    ).toBeVisible({ timeout: 15_000 })

    // ── The assertion the defect failed: pressing it must UNPOISON the form. ──
    await reload.click()

    // ⚠️ « Recharger » must NOT close the dialog. Calling the parent's *saved* callback here bumped
    // `refreshKey` and took the form down with it — so a refusal looked exactly like a save that had worked.
    await expect(
      dialog,
      "« Recharger » re-reads the row; it is not a save, and it must not dismiss the form",
    ).toBeVisible()

    // Re-apply the change deliberately, as the server's sentence asks, and save again.
    await dialog.locator("button", { hasText: /^1h$/ }).first().click()
    await saveThroughPrompts(page, dialog)

    await expect(
      dialog,
      "the second save must SUCCEED. A dialog whose version never moves repeats the refusal for ever — six " +
        "times over 81 minutes, in the recorded case",
    ).toBeHidden({ timeout: 20_000 })

    const reread = await api.appointment(appt.id)
    const minutes = (() => {
      const [h, m] = String(reread.duration ?? "").split(":").map(Number)
      return h * 60 + m
    })()
    expect(minutes, "and the user's change is the one that landed").toBe(60)
    expect(reread.notes, "…without silently discarding the colleague's").toContain("collègue")
  })

  test("EDIT-07 · without reloading, a second save is refused again — and still says so", async ({
    api,
    arrange,
    page,
    patient,
    sharedContext,
  }) => {
    /*
     * The other half of the same rule, and the one that keeps « Recharger » honest: retrying *without* it must
     * keep failing, because the version really has not moved. What must never happen is the refusal going
     * silent — a second click that appears to do nothing is indistinguishable from a save that worked.
     */
    const p = await patient("ConflitBis")
    const appt = await quietAppointment(api, arrange, p.id)

    await gotoApp(page, `/appointments?appointmentId=${appt.id}`, sharedContext)
    const dialog = editDialog(page)
    await expect(dialog).toBeVisible()

    const peer = await api.put(`/appointments/${appt.id}`, {
      id: appt.id,
      appointmentDateTime: appt.appointmentDateTime,
      durationMinutes: 45,
      version: appt.version,
      allowOverlap: true,
      allowOutsideWorkingHours: true,
    })
    expect(peer.status).toBe(200)

    await dialog.locator("button", { hasText: /^1h$/ }).first().click()
    await saveThroughPrompts(page, dialog)
    await expect(page.locator("button", { hasText: /^Recharger$/ }).first()).toBeVisible({ timeout: 15_000 })

    // Press save again, without reloading.
    await saveThroughPrompts(page, dialog)

    // Still refused, still open, and the way out is still on offer.
    await expect(dialog, "the stale version cannot win by persistence").toBeVisible()
    await expect(
      page.locator("button", { hasText: /^Recharger$/ }).first(),
      "the second refusal must still name its remedy — a refusal that goes quiet reads as success",
    ).toBeVisible()

    // The row is untouched by the two refused attempts.
    const reread = await api.appointment(appt.id)
    const minutes = (() => {
      const [h, m] = String(reread.duration ?? "").split(":").map(Number)
      return h * 60 + m
    })()
    expect(minutes, "« refusé » has to mean the save did not happen — twice").toBe(45)
  })

  test("EDIT-05 · a save the JOB's write preceded is not refused", async ({
    api,
    arrange,
    page,
    patient,
    sharedContext,
  }) => {
    /*
     * ⚠️ **A write NOBODY MADE still spends the concurrency token somebody is holding.** `AppointmentProgressJob`
     * advances an appointment at its slot's start and end minute, and `xmin` is per-row, so that write ages the
     * version every open form holds. `Appointment.VersionBeforeAutoAdvance` + `AutomaticWriteInterceptor` are
     * what make the job's own comment true — « a user's concurrent edit legitimately wins » — and the
     * interceptor decides from the **audit actor**, so a new automatic writer cannot forget to participate.
     *
     * ⚠️ **Not exercised by waiting for the job.** It fires on the minute for an appointment at its own slot
     * boundary, which is neither promptly nor deterministically reachable from a test; and forcing it would be
     * asserting the scheduler, not the rule. What IS asserted here is the observable half: an appointment the
     * job has already advanced still saves from a form opened afterwards. The rule's own unit coverage is where
     * the interceptor is pinned.
     */
    const p = await patient("Auto")
    const act = await api.singleSeanceAct()

    // An appointment whose slot has already passed, so the job has advanced it to « Séance passée ».
    const appt = await api.createAppointment({
      patientId: p.id,
      appointmentDateTime: arrange.slot(-1, 10).toISOString(),
      durationMinutes: 30,
      procedures: [{ procedureTypeId: act.id, agreedCost: null }],
    })

    const advanced = await api.appointment(appt.id)
    test.skip(
      advanced.status === "Scheduled",
      `the job has not advanced this appointment yet (status ${advanced.status}) — scenario not exercised`,
    )

    await gotoApp(page, `/appointments?appointmentId=${appt.id}`, sharedContext)
    const dialog = editDialog(page)
    await expect(dialog).toBeVisible()

    await dialog.locator("button", { hasText: /^1h$/ }).first().click()
    await saveThroughPrompts(page, dialog)

    await expect(
      dialog,
      "an automatic advance must not refuse the next human save — that refusal named a person when nobody " +
        "had touched anything",
    ).toBeHidden({ timeout: 20_000 })
  })
})
