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

/**
 * Chooses an existing patient through the searchable trigger.
 *
 * ⚠️ `getByPlaceholder(/Rechercher/).last()` — the header carries a global patient search with the same
 * placeholder, and matching it instead is one of this repo's recorded probe failures.
 */
async function pickExistingPatient(page: any, dialog: any, fullName: string) {
  await dialog.locator("button", { hasText: /^Patient existant$/ }).first().click()
  await dialog.locator("button", { hasText: /Choisir un patient/ }).first().click()
  const surname = fullName.split(" ")[1]
  const search = page.getByPlaceholder(/Rechercher/).last()
  await search.fill(surname)
  const option = page.locator("[cmdk-item]", { hasText: surname }).first()
  await expect(option).toBeVisible()
  await option.click()
}

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


test.describe("HP-2 · la suite d'une séance ne s'écrit qu'à l'enregistrement @mutating @t0", () => {
  mutatingOnly()

  /*
   * ⚠️ Twice the file's budget, because these two arrange more than any of its siblings before they drive:
   * a patient, a fiche de soins carrying a priced act, and the note d'honoraires that fiche raises — then a
   * full agenda load, two dialogs and three reads back. Measured at ~65 s against the config's 60, so the
   * failure was the clock and not the product: the screenshot showed the booking dialog not yet open.
   */
  test.describe.configure({ timeout: 150_000 })

  /*
   * ⚠️ **A wire test cannot see any of this.** `ContinueRecordedActCommand` is correct and stays correct; the
   * defect was WHEN the browser called it. `ContinueSessionDialog` minted the devis on its own button press —
   * numbered and `Accepted`, so with the lump-sum échéance `Accept` raises — and « Annuler » on the booking
   * behind it closed a form that had nothing left to undo. Reported from use: a live créance for a séance
   * nobody booked, and the act it continued gone from « Suite d'une séance précédente » for good
   * (`ContinuationTracking` counts an act on any non-cancelled plan as taken), recoverable only by cancelling
   * the devis with a motif.
   *
   * Only a browser pressing « Annuler » reproduces it, which is why the pair lives here.
   */

  /** The reported shape: 200 DT recorded, 150 collected — so 50 still owed on the note the fiche raised. */
  async function recordedAndPartlyPaid(api: any, arrange: any, patientId: string) {
    const act = await api.singleSeanceAct()
    const r = await arrange.fiche(
      patientId,
      [arrange.act({ procedureTypeId: act.id, name: act.name, cost: 200, teeth: [11] })],
      { amountPaid: 150 },
    )
    expect(r.status, `fiche: ${r.raw?.slice(0, 300)}`).toBe(200)
    const fiche = r.body?.value ?? r.body
    return { act, fiche, ficheActId: fiche.acts[0].id }
  }

  /** Opens the third door and fills it in, leaving the booking dialog carrying a PENDING continuation. */
  async function chooseContinuation(page: any, dialog: any, actName: string, remaining: string) {
    await dialog.locator("button", { hasText: /suite d'une séance précédente/i }).first().click()
    const cont = page
      .locator("[data-slot='dialog-content']")
      .filter({
        has: page.locator("[data-slot='dialog-title']", { hasText: /Suite d'une séance précédente/ }),
      })
      .first()
    await expect(cont).toBeVisible()

    const row = cont.locator("[role='radio']", { hasText: actName }).first()
    await expect(row, "the recorded séance must be offered as continuable").toBeVisible()
    await row.click()

    await cont.locator("#next-step-label").fill("Finition")
    await cont.locator("#remaining-cost").fill(remaining)

    // ⚠️ The label is itself an assertion: « Créer le traitement » would mean the press still writes.
    await cont.locator("button", { hasText: /^Ajouter au rendez-vous$/ }).first().click()
    await expect(cont).toBeHidden({ timeout: 15_000 })
  }

  test("BOOK-49 · « Annuler » after choosing a continuation leaves NO devis behind", async ({
    api,
    arrange,
    page,
    patient,
    sharedContext,
  }) => {
    const p = await patient("SuiteAnnul")
    const { act, ficheActId } = await recordedAndPartlyPaid(api, arrange, p.id)

    await gotoApp(page, "/appointments", sharedContext)
    const dialog = await openCreateDialog(page)
    await pickExistingPatient(page, dialog, p.name)
    await chooseContinuation(page, dialog, act.name, "20")

    /*
     * The card states what it is before anything exists — and both halves of that sentence were reported
     * wrong: it named the act as though this visit were the whole of it, and quoted the devis' own balance
     * while the note beside it still held 50 for the same work.
     */
    const text = ((await dialog.textContent()) ?? "").replace(/\u00a0/g, " ")
    expect(
      /séance 2 sur 2/.test(text),
      `the row must say which séance this is, and of what. Dialog said: ${text.slice(0, 400)}`,
    ).toBeTruthy()
    expect(
      /Total des deux séances/.test(text),
      `the row must state BOTH documents. Dialog said: ${text.slice(0, 400)}`,
    ).toBeTruthy()

    // ⚠️ Asserted BEFORE the cancel as well: « nothing afterwards » would also be true of a devis created and
    // then somehow removed, and this is specifically about nothing being written in the first place.
    expect(
      (await api.plans(`?patientId=${p.id}&pageSize=50`)).items,
      "choosing the séance must write nothing — the devis is minted by the SAVE",
    ).toHaveLength(0)

    /*
     * ⚠️ **« Annuler » on a dirty form raises « Abandonner les modifications ? », and it is an `alertdialog`.**
     * `drainConfirmations` cannot answer this one — its affirmative list is the four SAVE prompts, and it
     * throws on anything else — so the abandon is confirmed here. Not answering it left the booking dialog
     * open and the test reported « the cancel did not close the dialog », which was false.
     */
    await dialog.locator("button", { hasText: /^Annuler$/ }).first().click()
    const abandon = page.locator("[role='alertdialog']").first()
    if (await abandon.isVisible().catch(() => false)) {
      await abandon.locator("button", { hasText: /^Abandonner$/ }).first().click()
    }
    await expect(dialog).toBeHidden({ timeout: 15_000 })

    const plans = await api.plans(`?patientId=${p.id}&pageSize=50`)
    expect(
      plans.items,
      "abandoning the booking must leave nothing. Found " +
        plans.items.map((x: any) => `${x.number ?? "sans n°"}/${x.status}`).join(", "),
    ).toHaveLength(0)
    expect(await api.appointmentsOf(p.id), "and no appointment either").toHaveLength(0)

    /*
     * ⚠️ The half that made the orphan permanent. An act on any non-cancelled plan counts as already
     * continued, so the eager devis took the séance out of this list — the dentist's own recovery path was
     * closed by the very mistake they were trying to undo.
     */
    const offered = await api.continuableActs(p.id)
    expect(
      offered.some((a: any) => a.actId === ficheActId),
      "the séance must still be offered — changing one's mind cannot cost the act for ever",
    ).toBeTruthy()
  })

  test("BOOK-50 · saving the booking DOES create the devis, numbered and linked", async ({
    api,
    arrange,
    page,
    patient,
    sharedContext,
  }) => {
    /*
     * The other half, and the one a deferral can break silently: a booking whose card promised a treatment and
     * saved an ordinary one-off instead. N26 holds the call site; this holds the outcome.
     */
    const p = await patient("SuiteGardee")
    const { act } = await recordedAndPartlyPaid(api, arrange, p.id)

    await gotoApp(page, "/appointments", sharedContext)
    const dialog = await openCreateDialog(page)
    await pickExistingPatient(page, dialog, p.name)
    await chooseContinuation(page, dialog, act.name, "20")

    await submit(dialog)
    await drainConfirmations(page)
    await expect(dialog).toBeHidden({ timeout: 20_000 })
    await dismissPostVisitPrompt(page)

    const plans = await api.plans(`?patientId=${p.id}&pageSize=50`)
    expect(
      plans.items,
      "one save, one devis. Found " +
        plans.items.map((x: any) => `${x.number ?? "sans n°"}/${x.status}`).join(", "),
    ).toHaveLength(1)

    const plan = plans.items[0]
    /*
     * ⚠️ **The NUMBER is the assertion, not the status.** `Accept` is the only writer of `Number`, so a numbered
     * plan was accepted — and the status has already moved on by the time this read happens: the continuation
     * marks its « 1re séance » done against the fiche in the same transaction, and `AdvanceAfterWorkRecorded`
     * takes an accepted plan with recorded work to `InProgress`. Asserting the literal « Accepted » measured
     * the first instant of a plan that is never observed in it.
     */
    expect(plan.number, "a continuation's devis is numbered and accepted in the same save").toBeTruthy()
    expect(
      ["Accepted", "InProgress"],
      `a continuation's devis is live and debt-bearing; it was ${plan.status}`,
    ).toContain(plan.status)

    /*
     * ⚠️ The appointment must carry the plan. `resolveAttachedPlanId` reads `planIdByItem`, and a devis minted
     * seconds ago is in neither the hook's read nor the state write of this same tick — so without the
     * materialiser merging its own ids the server refuses with « Le plan de traitement est requis pour lier
     * l'acte. », the feature turned down by its own client.
     *
     * ⚠️ **Asserted on `treatmentPlanItemId`, because `AppointmentDto` carries no `treatmentPlanId`.** The
     * server stores that scalar (and refuses the save without it), but the read exposes only the act-level
     * link — so a check on the plan id reads `undefined` and fails on a booking that worked perfectly.
     */
    const appts = await api.appointmentsOf(p.id)
    expect(appts, "the booking is made").toHaveLength(1)
    const itemIds = plan.items.map((i: any) => i.id)
    expect(
      itemIds,
      `the séance must be linked to an act of the devis just created; it carried ${appts[0].treatmentPlanItemId}`,
    ).toContain(appts[0].treatmentPlanItemId)

    /*
     * ⚠️ And it must be the act still to do, never the one the note already billed. A priced « travail
     * restant » makes the continuation's FIRST act `Done` on creation, so booking `items[0]` puts the séance
     * on work that is finished — which is what `schedulablePlanItems` is the gate against.
     */
    const booked = plan.items.find((i: any) => i.id === appts[0].treatmentPlanItemId)
    expect(booked?.status, "the séance belongs to the remaining work, not to the billed act").not.toBe("Done")
  })
})
