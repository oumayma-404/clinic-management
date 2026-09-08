import { expect, mutatingOnly, test } from "../lib/fixtures"
import { drainConfirmations, sawPrompt } from "../lib/dialogs"
import { dismissPostVisitPrompt, gotoApp } from "../lib/goto"

/**
 * HP-2 — the create-appointment dialog, driven for real.
 *
 * ⚠️ **Everything here is a defect that lives in the client and nowhere else.** The dialog's three advisory
 * refusals — slot taken, out of hours, past time — plus a fourth on the patient are **questions, not dead
 * ends**, and each of them **re-enters `performCreate` from the top**. So every side effect the save has
 * already produced runs again unless something remembers it did. Two such memories exist and both were paid
 * for in production:
 *
 * - `createdPatientIdRef` — « reuse before create ». Without it « créer quand même » on a taken slot made
 *   **two patients on /patients**, from one gesture.
 * - `createdPlansRef` — the same shape one object over. Without it one « créer quand même » left **two
 *   identical treatments**.
 *
 * ⚠️ **A wire test cannot see any of it**: the API is asked twice and answers correctly twice. Only a browser
 * pressing the confirmation reproduces it.
 *
 * ⚠️ **Confirmations are `role="alertdialog"`, not `role="dialog"`.** A selector list holding only the latter
 * reports « the confirmation has no text », which is this repo's recorded probe failure.
 */

/**
 * Opens the agenda-bar « Nouveau » and returns **that dialog**, identified by its own title.
 *
 * ⚠️ The trigger is the agenda-bar button specifically — a bare `:has-text('Nouveau')` is a strict-mode
 * violation, because a hidden « Nouveau RDV » twin exists.
 *
 * ⚠️ **And the returned locator is scoped by title, never `[data-slot='dialog-content'].first()`.** That generic
 * selector matches *whichever* dialog is open, so `expect(dialog).toBeHidden()` failed on a booking that had
 * actually succeeded: the create dialog had closed, and the locator had silently re-bound to the « Compte rendu
 * de visite » popup which fires the moment a past-dated visit is saved. The test reported « the save was
 * refused » while the appointment was visible on the agenda behind it.
 */
const openCreateDialog = async (page: any) => {
  await page.locator('button[data-size="sm"]:has-text("Nouveau")').first().click()
  const dialog = page
    .locator("[data-slot='dialog-content']")
    .filter({ has: page.locator("[data-slot='dialog-title']", { hasText: /Nouveau rendez-vous/ }) })
    .first()
  await expect(dialog).toBeVisible()
  return dialog
}

/** Picks a catalogue act by name through the cmdk popover the trigger opens. */
async function pickAct(page: any, dialog: any, name: string) {
  await dialog.locator("button", { hasText: /Sélectionner un type d/ }).first().click()
  const search = page.getByPlaceholder(/Rechercher un acte/)
  await expect(search).toBeVisible()
  await search.fill(name.slice(0, 18))
  const option = page.locator("[cmdk-item]", { hasText: name }).first()
  await expect(option).toBeVisible()
  await option.click()
  // The popover closes and the act's card renders on the form.
  await expect(dialog.getByText(name).first()).toBeVisible()
}

const submit = (dialog: any) =>
  dialog.locator("button", { hasText: /^Créer le rendez-vous$/ }).first().click()

test.describe("HP-2 · une confirmation ne duplique rien @mutating @t0", () => {
  mutatingOnly()

  /** A time earlier today — the past-time confirmation is the cheapest deterministic re-entry of the save. */
  function earlierToday() {
    const d = new Date()
    d.setHours(1, 15, 0, 0)
    return "01:15"
  }

  test("BOOK-05 · « Créer quand même » on a duplicate name creates ONE patient, not two", async ({
    api,
    page,
    patient,
    sharedContext,
  }) => {
    // An existing patient whose name the walk-in form is about to repeat.
    const existing = await patient("Dup")
    const [firstName, lastName] = [existing.name.split(" ")[0], existing.name.split(" ")[1]]

    await gotoApp(page, "/appointments", sharedContext)
    const dialog = await openCreateDialog(page)

    await dialog.locator("button", { hasText: /^Nouveau patient$/ }).first().click()
    await dialog.locator("#firstName").fill(firstName)
    await dialog.locator("#lastName").fill(lastName)
    await submit(dialog)

    // The server refuses with `PatientDuplicate`; the dialog turns it into a question. Any other prompt the
    // calendar happens to raise is confirmed too — see `drainConfirmations`.
    const seen = await drainConfirmations(page)
    sawPrompt(seen, /existe peut-être déjà/i, "Ce patient existe peut-être déjà")

    // The dialog closes on success.
    await expect(dialog).toBeHidden({ timeout: 20_000 })
    // A past-dated booking is « terminée » on arrival, so the review prompt fires immediately. Snoozed here so
    // it cannot sit over the next test's first click.
    await dismissPostVisitPrompt(page)

    /*
     * ⚠️ The assertion is a COUNT, not « did it work ». « Two people really can share a name », so creating a
     * second patient here is legitimate — creating a *third* from one gesture is the defect, and only a count
     * distinguishes them.
     */
    const rows = await api.patientsNamed(lastName)
    expect(
      rows,
      `one gesture must create exactly one patient beside the existing one. Found ${rows.length}: ` +
        rows.map((r: any) => r.id).join(", "),
    ).toHaveLength(2)
  })

  test("BOOK-06 · TWO confirmations in sequence still create ONE patient and ONE appointment", async ({
    api,
    page,
    patient,
    sharedContext,
  }) => {
    /*
     * ⚠️ The case `createdPatientIdRef` exists for, and the one a single confirmation does not reach: the save
     * is re-entered **twice**. Past time is asked first (client-side, before anything is written), then the
     * duplicate (server-side, after the patient create was attempted) — so the second re-entry runs
     * `patientsApi.create` a third time unless the ref short-circuits it.
     *
     * ⚠️ It also proves the grants MERGE rather than replace: a grant given to one prompt must survive the
     * next, or confirming the second re-raises the first and the dialog loops.
     */
    const existing = await patient("DupDeux")
    const [firstName, lastName] = [existing.name.split(" ")[0], existing.name.split(" ")[1]]

    await gotoApp(page, "/appointments", sharedContext)
    const dialog = await openCreateDialog(page)

    await dialog.locator("button", { hasText: /^Nouveau patient$/ }).first().click()
    await dialog.locator("#firstName").fill(firstName)
    await dialog.locator("#lastName").fill(lastName)
    await dialog.locator("#create-appt-start-time").fill(earlierToday())
    await submit(dialog)

    const seen = await drainConfirmations(page)
    sawPrompt(seen, /passé/i, "Heure dans le passé")
    sawPrompt(seen, /existe peut-être déjà/i, "Ce patient existe peut-être déjà")
    expect(
      seen.length,
      `at least two prompts must have been answered for this to exercise a double re-entry. Saw: ${seen.join(" · ")}`,
    ).toBeGreaterThanOrEqual(2)
    await expect(dialog).toBeHidden({ timeout: 20_000 })
    // A past-dated booking is « terminée » on arrival, so the review prompt fires immediately. Snoozed here so
    // it cannot sit over the next test's first click.
    await dismissPostVisitPrompt(page)

    const rows = await api.patientsNamed(lastName)
    expect(
      rows,
      `two confirmations, one gesture: still exactly one new patient. Found ${rows.length}`,
    ).toHaveLength(2)

    // …and exactly one appointment, on the patient that was just created.
    const created = rows.find((r: any) => r.id !== existing.id)
    expect(created, "the new patient must exist").toBeTruthy()
    expect(await api.appointmentsOf(created.id), "one gesture, one appointment").toHaveLength(1)
  })

  test("BOOK-35 · a confirmation on a SPLIT act creates ONE treatment, not two", async ({
    api,
    page,
    patient,
    sharedContext,
  }) => {
    /*
     * `createdPlansRef`'s own scar. A multi-séance act is split **by default** and its treatment is created
     * when the booking is **saved** — so a save that is re-entered creates the treatment again unless the ref
     * remembers. One « Continuer » used to leave two identical treatments on the patient.
     */
    const p = await patient("UnPlan")
    const act = await api.protocolAct(2)

    await gotoApp(page, `/patients/${p.id}`, sharedContext)
    await gotoApp(page, "/appointments", sharedContext)
    const dialog = await openCreateDialog(page)

    // An existing patient, so the only re-entry is the past-time one — this isolates the PLAN memory from the
    // patient memory that BOOK-05/06 cover.
    await dialog.locator("button", { hasText: /^Patient existant$/ }).first().click()
    await dialog.locator("button", { hasText: /Choisir un patient/ }).first().click()
    const search = page.getByPlaceholder(/Rechercher/).last()
    await search.fill(p.name.split(" ")[1])
    const option = page.locator("[cmdk-item]", { hasText: p.name.split(" ")[1] }).first()
    await expect(option).toBeVisible()
    await option.click()

    await pickAct(page, dialog, act.name)

    // Split by default — the séance frise must be on screen, or this scenario is not exercising a treatment.
    const text = ((await dialog.textContent()) ?? "").replace(/ /g, " ")
    expect(
      /Traitement en \d+\s*séances?/.test(text),
      `the act must be split by default for this to test anything. Dialog said: ${text.slice(0, 250)}`,
    ).toBeTruthy()

    await dialog.locator("#create-appt-start-time").fill(earlierToday())
    await submit(dialog)
    const seen = await drainConfirmations(page)
    sawPrompt(seen, /passé/i, "Heure dans le passé")
    await expect(dialog).toBeHidden({ timeout: 20_000 })
    // A past-dated booking is « terminée » on arrival, so the review prompt fires immediately. Snoozed here so
    // it cannot sit over the next test's first click.
    await dismissPostVisitPrompt(page)

    const plans = await api.plans(`?patientId=${p.id}&pageSize=50`)
    expect(
      plans.items,
      `one « Continuer » must leave exactly one treatment. Found ${plans.items.length}: ` +
        plans.items.map((x: any) => `${x.title}/${x.status}`).join(", "),
    ).toHaveLength(1)

    const plan = plans.items[0]
    expect(plan.status, "« Suivre ce traitement » creates an un-numbered Draft").toBe("Draft")
    expect(plan.number).toBeNull()
  })
})

test.describe("HP-2 · l'éditeur de séances est toujours là @mutating @t0", () => {
  mutatingOnly()

  /*
   * The reported field defect, in the two situations the *create* dialog reaches it: a client booked
   * « Couronne / bridge », read « Cet acte se fait normalement en 3 séances » and had **no control at all**
   * underneath it. The cause was one line — `onStartProtocol={selectedPatientId ? startProtocol : undefined}`
   * — against a picker that rendered the *sentence* unconditionally and the *button* inside
   * `{onStartProtocol && …}`. So the offer was absent whenever there was no patient id yet.
   *
   * The fix inverted the default rather than widening the gate, which is why these now assert a **frise and a
   * way out** rather than a « Suivre ce traitement » button.
   */

  const assertSeanceEditor = async (dialog: any, page: any) => {
    const text = ((await dialog.textContent()) ?? "").replace(/ /g, " ")
    expect(
      /Traitement en \d+\s*séances?/.test(text),
      `the protocol must be resolved and split by default. Dialog said: ${text.slice(0, 250)}`,
    ).toBeTruthy()

    // Both the frise and its exit live below the fold in this dialog, so scroll its own body, not the page.
    await dialog.evaluate((el: HTMLElement) => {
      const body = el.querySelector(".overflow-y-auto") as HTMLElement | null
      if (body) body.scrollTop = body.scrollHeight
    })
    await expect(
      dialog.locator("button", { hasText: /Tout faire en une\s+(seule\s+)?séance/ }).first(),
      "split-by-default needs a reachable way out, or the dentist cannot say « une seule séance »",
    ).toBeVisible()
    void page
  }

  test("BOOK-31 · the act picked BEFORE the patient still shows the séance editor", async ({
    api,
    page,
    sharedContext,
  }) => {
    const act = await api.protocolAct(2)

    await gotoApp(page, "/appointments", sharedContext)
    const dialog = await openCreateDialog(page)

    // Deliberately no patient chosen: `selectedPatientId` is "" here, which is what used to remove the control.
    await pickAct(page, dialog, act.name)
    await assertSeanceEditor(dialog, page)
  })

  test("BOOK-32 · a « Nouveau patient » walk-in still shows the séance editor", async ({
    api,
    page,
    sharedContext,
  }) => {
    const act = await api.protocolAct(2)

    await gotoApp(page, "/appointments", sharedContext)
    const dialog = await openCreateDialog(page)

    // That mode has NO patient id at all until the save, which is the second situation the defect covered.
    await dialog.locator("button", { hasText: /^Nouveau patient$/ }).first().click()
    await dialog.locator("#firstName").fill("E2E")
    await dialog.locator("#lastName").fill(`Walkin-${Date.now().toString(36)}`)

    await pickAct(page, dialog, act.name)
    await assertSeanceEditor(dialog, page)
  })

  test("BOOK-37 · « Tout faire en une seule séance » creates NO treatment", async ({
    api,
    page,
    patient,
    sharedContext,
  }) => {
    const p = await patient("UneSeule")
    const act = await api.protocolAct(2)

    await gotoApp(page, "/appointments", sharedContext)
    const dialog = await openCreateDialog(page)

    await dialog.locator("button", { hasText: /^Patient existant$/ }).first().click()
    await dialog.locator("button", { hasText: /Choisir un patient/ }).first().click()
    const search = page.getByPlaceholder(/Rechercher/).last()
    await search.fill(p.name.split(" ")[1])
    await page.locator("[cmdk-item]", { hasText: p.name.split(" ")[1] }).first().click()

    await pickAct(page, dialog, act.name)

    // The way out of split-by-default. `plannedProtocol = null` means « one séance », and `[]` is never stored.
    await dialog.evaluate((el: HTMLElement) => {
      const body = el.querySelector(".overflow-y-auto") as HTMLElement | null
      if (body) body.scrollTop = body.scrollHeight
    })
    await dialog.locator("button", { hasText: /Tout faire en une\s+(seule\s+)?séance/ }).first().click()

    await submit(dialog)
    // No prompt is *expected* here, but the dev calendar may still raise a slot collision, so drain rather
    // than assume.
    await drainConfirmations(page)
    await expect(dialog).toBeHidden({ timeout: 20_000 })
    // A past-dated booking is « terminée » on arrival, so the review prompt fires immediately. Snoozed here so
    // it cannot sit over the next test's first click.
    await dismissPostVisitPrompt(page)

    const plans = await api.plans(`?patientId=${p.id}&pageSize=50`)
    expect(
      plans.items,
      "saying « une seule séance » must create no treatment at all — just a plain appointment",
    ).toHaveLength(0)

    expect(await api.appointmentsOf(p.id), "…but the booking is still made").toHaveLength(1)
  })
})
