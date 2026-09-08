import { expect, mutatingOnly, test } from "../lib/fixtures"
import { gotoApp } from "../lib/goto"

/**
 * The half of the hot paths **only a browser can see**.
 *
 * Everything here is a defect that lives entirely in the client, so a wire test would pass while the screen was
 * unusable. The clearest example is `PLAN-01`: `primaryAction` is a `useMemo` that runs during render and calls
 * the `const` confirm openers, so declared above them **every `Completed` plan threw « Cannot access
 * 'confirmReopen' before initialization » and the workspace failed to render entirely** — invisible to `tsc`,
 * to `check:responsive` and to `next build`. Only a browser walk saw it.
 *
 * ⚠️ **Console errors are collected per test and asserted.** A React render crash inside a client component is
 * caught by an error boundary and shows as an empty region — no HTTP failure, no visible message. Watching the
 * console is what turns that into a red test.
 */

/** Every uncaught error and console error the page raised, so a blank region is reported as the crash it is. */
function watchConsole(page: any) {
  const errors: string[] = []
  page.on("console", (m: any) => {
    if (m.type() === "error") errors.push(m.text())
  })
  page.on("pageerror", (e: Error) => errors.push(`pageerror: ${e.message}`))
  return errors
}

test.describe("HP-7 · le workspace du devis, dans un navigateur @mutating @t0", () => {
  mutatingOnly()

  test("PLAN-01 · a COMPLETED plan's workspace renders, with no console crash", async ({
    api,
    page,
    patient,
    sharedContext,
  }) => {
    const errors = watchConsole(page)

    const p = await patient("PlanRender")
    const crown = await api.protocolAct(2)
    const started = await api.startTreatment({
      patientId: p.id,
      procedureTypeId: crown.id,
      agreedTotal: 500,
      toothNumbers: [16],
      steps: null,
    })
    const plan = started.body?.value ?? started.body
    expect((await api.acceptPlan(plan.id, {})).status).toBe(200)
    expect((await api.completePlan(plan.id, { leaveUnrealisedActs: true })).status).toBe(200)

    await gotoApp(page, `/treatment-plans/${plan.id}`, sharedContext)

    // Assert on what must BE there — the act's own designation — not on the absence of « Erreur ».
    await expect(
      page.getByText(crown.name).first(),
      "the workspace must render its acts; a TDZ throw during render leaves an empty region and no HTTP failure",
    ).toBeVisible()

    const tdz = errors.filter((e) => /before initialization|Cannot access/i.test(e))
    expect(tdz, `console: ${JSON.stringify(errors.slice(0, 5))}`).toHaveLength(0)
  })

  test("PLAN-02/03 · a followed Draft shows ONE primary action, and no « Annuler »", async ({
    api,
    page,
    patient,
    sharedContext,
  }) => {
    const p = await patient("PlanHeader")
    const crown = await api.protocolAct(2)
    const started = await api.startTreatment({
      patientId: p.id,
      procedureTypeId: crown.id,
      agreedTotal: 500,
      toothNumbers: [16],
      steps: null,
    })
    const plan = started.body?.value ?? started.body

    await gotoApp(page, `/treatment-plans/${plan.id}`, sharedContext)

    /*
     * « Annuler » was refused every time it was pressed: `isActive` is `isPlanLive`, which includes Draft, and
     * `TreatmentPlan.Cancel` throws « Un brouillon se supprime, il ne s'annule pas. » on exactly that status.
     * Browser-proven end to end — the motif filled in, the confirmation pressed, the server refusing.
     *
     * Scoped to the header: « Annuler » is also every dialog's dismiss button, so an unscoped query is a
     * strict-mode violation *and* a false finding.
     */
    const header = page.locator("header, [data-slot='card-header']").first()
    const cancel = header.getByRole("button", { name: /^Annuler$/ })
    expect(
      await cancel.count(),
      "a devis is cancelled because its NUMBER must be accounted for; a treatment that never had one is deleted",
    ).toBe(0)
  })
})

test.describe("HP-5 · la fiche de soins, dans un navigateur @mutating @t0", () => {
  mutatingOnly()

  /**
   * A patient with a multi-séance act followed, booked on step 1 — the fiche's own precondition.
   *
   * ⚠️ The act and its fee are taken from **whatever catalogue is present**, never named or hard-coded: a
   * freshly seeded database and a hand-edited developer one disagree about both, and a literal turns that
   * disagreement into a false defect report.
   */
  async function bookedOnStepOne(api: any, arrange: any, patientId: string) {
    const crown = await api.protocolAct(2)
    const started = await api.startTreatment({
      patientId,
      procedureTypeId: crown.id,
      agreedTotal: Number(crown.defaultCost) || 500,
      toothNumbers: [16],
      steps: null,
    })
    expect(started.status, `startTreatment: ${started.raw?.slice(0, 300)}`).toBe(200)
    const plan = started.body?.value ?? started.body
    const item = plan.items[0]

    /*
     * ⚠️ `treatmentPlanId` is **required beside** `treatmentPlanItemId` — `AppointmentPlanLink` refuses
     * « Le plan de traitement est requis pour lier l'acte. » without it. Worth knowing because that is the
     * *same sentence* the `plan.items[0]` defect produced, so an arrange that omits it looks exactly like the
     * product bug it is not.
     */
    const appt = await api.createAppointment({
      patientId,
      appointmentDateTime: arrange.slot(0, 10).toISOString(),
      durationMinutes: 60,
      treatmentPlanId: plan.id,
      treatmentPlanItemId: item.id,
      treatmentPlanItemStepId: item.steps[0].id,
      procedures: [
        {
          procedureTypeId: crown.id,
          agreedCost: null,
          treatmentPlanId: plan.id,
          treatmentPlanItemId: item.id,
          treatmentPlanItemStepId: item.steps[0].id,
        },
      ],
    })
    return { crown, plan, item, appt }
  }

  test("FICHE-20/22 · a wholly-carried séance locks the price and WITHHOLDS « Total » and « Payé »", async ({
    api,
    arrange,
    page,
    patient,
    sharedContext,
  }) => {
    const errors = watchConsole(page)
    const p = await patient("FicheLock")
    const { crown, item, appt } = await bookedOnStepOne(api, arrange, p.id)
    const steps = item.steps.length

    await gotoApp(page, `/patients/${p.id}?addRecord=1&appointmentId=${appt.id}`, sharedContext)

    // The booked act is prefilled and the séance knows its rank — out of however many the act actually has.
    await expect(page.getByText(crown.name).first()).toBeVisible()
    await expect(page.getByText(new RegExp(`SÉANCE\\s*1\\s*SUR\\s*${steps}`, "i"))).toBeVisible()

    /*
     * ⚠️ **The price field is WITHHELD, not rendered read-only** — `act-card.tsx` replaces it with
     * « Aucun honoraire sur cette séance. Cet acte est chiffré une fois, sur le traitement. » The card's own
     * comment says why, and it is the better design: « a collapsed card reading 0,000 DT beside the act's name
     * is the third of the séance's zeros and it says nothing — the act has no price *here*, which is different
     * from costing nothing. »
     *
     * ⚠️ CLAUDE.md still says « `act-card` renders its price `readOnly` », which is stale. This test asserts
     * the code.
     */
    await expect(
      page.getByText(/Aucun honoraire sur cette séance/),
      "an act the treatment prices states so in words, instead of showing a zero",
    ).toBeVisible()
    await expect(
      page.getByLabel(/Montant forfaitaire \(DT\)|Prix par dent \(DT\)/),
      "no price input at all on a wholly-carried séance",
    ).toHaveCount(0)

    /*
     * ⚠️ « Total » and « Payé » are withheld per SÉANCE, and only when EVERY named act is carried
     * (`seanceIsWhollyOnTreatment`) — a structural test, never `grandTotal === 0`, which is also true of a
     * détartrage nobody has priced yet.
     */
    await expect(page.locator("#session-total")).toHaveCount(0)
    await expect(page.locator("#paid")).toHaveCount(0)

    // …and the second money field, which is what this séance actually collects, IS offered.
    await expect(
      page.locator("#collected-on-plan"),
      "« Encaissé sur le traitement » is a second money field, never folded into « Payé »",
    ).toBeVisible()

    expect(errors.filter((e) => /before initialization|Cannot access/i.test(e))).toHaveLength(0)
  })

  test("FICHE-24 · a MIXED séance shows « Total » and « Payé », and locks only the carried act", async ({
    api,
    arrange,
    page,
    patient,
    sharedContext,
  }) => {
    const p = await patient("FicheMixte")
    const { crown, plan, item, appt } = await bookedOnStepOne(api, arrange, p.id)
    // A real, independently-billable act: no protocol, non-zero tarif — whatever the catalogue calls it.
    const detartrage = await api.singleSeanceAct()
    const tarif = Number(detartrage.defaultCost)

    // Add an independent act to the same booking, so not every act is carried.
    const upd = await api.put(`/appointments/${appt.id}`, {
      id: appt.id,
      appointmentDateTime: appt.appointmentDateTime,
      durationMinutes: appt.durationMinutes,
      version: appt.version,
      allowOverlap: true,
      allowOutsideWorkingHours: true,
      treatmentPlanId: plan.id,
      treatmentPlanItemId: item.id,
      treatmentPlanItemStepId: item.steps[0].id,
      procedures: [
        {
          procedureTypeId: crown.id,
          agreedCost: null,
          treatmentPlanId: plan.id,
          treatmentPlanItemId: item.id,
          treatmentPlanItemStepId: item.steps[0].id,
        },
        { procedureTypeId: detartrage.id, agreedCost: null },
      ],
    })
    expect(upd.status, `adding the second act: ${upd.raw?.slice(0, 300)}`).toBe(200)

    await gotoApp(page, `/patients/${p.id}?addRecord=1&appointmentId=${appt.id}`, sharedContext)

    // The display rule is PER ACT; the total rule is PER SÉANCE. Both must hold at once.
    await expect(
      page.locator("#session-total"),
      "« Total » is withheld only when EVERY named act is carried — this séance has a real détartrage",
    ).toBeVisible()
    await expect(page.locator("#paid")).toBeVisible()

    /*
     * The display rule is **per act**: the carried crown states « Aucun honoraire sur cette séance » and shows
     * no field; the détartrage keeps its own money.
     *
     * ⚠️ **Only the ARMED card renders a price input.** A collapsed card shows a figure, not a field — so
     * counting inputs across the modal measures « how many cards are open », which is a different question and
     * reads as 0. The first draft of this test failed on exactly that while the screen was entirely correct.
     */
    await expect(page.getByText(/Aucun honoraire sur cette séance/)).toHaveCount(1)
    await expect(page.getByText(/2\s*actes/)).toBeVisible()

    // The collapsed act carries its own money — the figure the carried act deliberately does NOT show.
    const detartrageCard = page.getByText(detartrage.name, { exact: true }).first()
    await expect(detartrageCard).toBeVisible()

    // Arm it, and only then is its price a field — and an editable one.
    await detartrageCard.click()
    const price = page.getByLabel(/Montant forfaitaire \(DT\)|Prix par dent \(DT\)/).first()
    await expect(
      price,
      "the détartrage must stay priceable — hiding on « this séance touches a devis » would make it unbillable",
    ).toBeVisible()
    await expect(price).not.toHaveAttribute("readonly", /.*/)
    // Its own tarif, to the millime, in the product's own French decimal form.
    expect(parseFloat((await price.inputValue()).replace(/\s/g, "").replace(",", "."))).toBeCloseTo(tarif, 3)
  })

  test("BOOK-25b · the fiche prefills the NEGOTIATED price, not the catalogue tarif", async ({
    api,
    arrange,
    page,
    patient,
    sharedContext,
  }) => {
    const p = await patient("FicheNego")
    const detartrage = await api.singleSeanceAct()
    const tarif = Number(detartrage.defaultCost)
    // Comfortably different from the tarif, so the two figures cannot be confused — and derived, not literal.
    const negotiated = Math.max(5, Math.round(tarif * 0.75))
    expect(negotiated, "the negotiated price must differ from the tarif, or this proves nothing").not.toBe(tarif)

    const appt = await api.createAppointment({
      patientId: p.id,
      appointmentDateTime: arrange.slot(0, 11).toISOString(),
      durationMinutes: 30,
      procedures: [{ procedureTypeId: detartrage.id, agreedCost: negotiated }],
    })

    await gotoApp(page, `/patients/${p.id}?addRecord=1&appointmentId=${appt.id}`, sharedContext)

    const price = page.getByLabel(/Montant forfaitaire \(DT\)|Prix par dent \(DT\)/).first()
    await expect(price).toBeVisible()
    const value = await price.inputValue()

    /*
     * ⚠️ Both prefill paths (`applyAppointment`, `addFromProcedure`) resolve `procedureTypeId` back to a
     * `ProcedureTypeDto` and read `defaultCost` — so an act's negotiated `AgreedCost` has to be threaded
     * through explicitly or it reverts to the tarif **in silence**. The two are one keystroke apart in the code
     * and a real sum apart on the patient's note.
     */
    expect(
      parseFloat(value.replace(/\s/g, "").replace(",", ".")),
      `the field shows « ${value} ». A price agreed on the telephone is the price billed; ${tarif} is the ` +
        `tarif the prefill must NOT fall back to`,
    ).toBeCloseTo(negotiated, 3)
  })

  test("FICHE-33 · séance 2's chart is prefilled with the teeth séance 1 treated", async ({
    api,
    arrange,
    page,
    patient,
    sharedContext,
  }) => {
    const p = await patient("FicheTeeth")
    const { crown, plan, item } = await bookedOnStepOne(api, arrange, p.id)

    // Séance 1, recorded on 16 and 26, through the wire.
    const first = await arrange.fiche(
      p.id,
      [arrange.act({ procedureTypeId: crown.id, name: crown.name, cost: 0, teeth: [16, 26] })],
      {
        amountPaid: 0,
        treatmentPlanId: plan.id,
        treatmentPlanItemId: item.id,
        treatmentPlanItemStepId: item.steps[0].id,
      },
    )
    expect(first.status, `séance 1: ${first.raw?.slice(0, 300)}`).toBe(200)

    // Séance 2, booked on step 2.
    const appt2 = await api.createAppointment({
      patientId: p.id,
      appointmentDateTime: arrange.slot(1, 10).toISOString(),
      durationMinutes: 60,
      treatmentPlanId: plan.id,
      treatmentPlanItemId: item.id,
      treatmentPlanItemStepId: item.steps[1].id,
      procedures: [
        {
          procedureTypeId: crown.id,
          agreedCost: null,
          treatmentPlanId: plan.id,
          treatmentPlanItemId: item.id,
          treatmentPlanItemStepId: item.steps[1].id,
        },
      ],
    })

    await gotoApp(page, `/patients/${p.id}?addRecord=1&appointmentId=${appt2.id}`, sharedContext)

    /*
     * ⚠️ **This protection is CLIENT-SIDE, and that is the point of testing it here.** The devis line for a
     * crown is often an « acte général » with no teeth, so séance 2 used to open on a blank chart. It stopped
     * being a convenience the moment the charting fix landed: the chart is written when the act **finishes**,
     * so teeth entered early and absent from the last fiche chart **nothing at all**. The server has no
     * fallback — a save that omits the teeth charts nothing — so the prefill is the whole guard.
     */
    for (const tooth of [16, 26]) {
      await expect(
        page.getByRole("button", { name: new RegExp(`Retirer la dent ${tooth}`) }),
        `tooth ${tooth} must be carried into séance 2's chart — the server charts what the LAST fiche sends, ` +
          `and it has no fallback`,
      ).toBeVisible()
    }
  })
})

test.describe("HP-2 · le dialogue de réservation, dans un navigateur @mutating @t0", () => {
  mutatingOnly()

  test("BOOK-33 · the séance editor renders in the EDIT dialog for a protocol act", async ({
    api,
    arrange,
    page,
    patient,
    sharedContext,
  }) => {
    const p = await patient("EditProto")
    const crown = await api.protocolAct(2)
    const protocolLabels: string[] = (crown.defaultSteps ?? []).map(
      (st: any) => st.label ?? st.Label,
    )

    const appt = await api.createAppointment({
      patientId: p.id,
      appointmentDateTime: arrange.slot(1, 14).toISOString(),
      durationMinutes: 60,
      procedures: [{ procedureTypeId: crown.id, agreedCost: null }],
    })

    await gotoApp(page, `/appointments?appointmentId=${appt.id}`, sharedContext)

    /*
     * The reported field defect: a client booked « Couronne / bridge », read « Cet acte se fait normalement en
     * 3 séances » and had **no control at all** underneath it. The edit dialog never passed `onStartProtocol`
     * — for everyone, always — while the picker rendered the *sentence* unconditionally and the *button* inside
     * `{onStartProtocol && …}`.
     *
     * So: if the sentence is on screen, the control must be too. Asserted as an implication, because a
     * one-séance act legitimately shows neither.
     */
    const dialog = page.locator("[data-slot='dialog-content']").first()
    await expect(dialog).toBeVisible()

    /*
     * ⚠️ **Match on the dialog's TEXT CONTENT, with `\s` for the space, not on a phrase.** The picker renders
     * « Traitement en {n}&nbsp;séances » as three nodes with a **non-breaking space** between the count and the
     * word, so `getByText(/Traitement en \d+ séances/)` — an ordinary U+0020 — finds nothing and reports the
     * control as missing. That is a false defect report on the exact screen this scenario exists to defend, and
     * the first draft of this test produced it.
     */
    const text = ((await dialog.textContent()) ?? "").replace(/\u00a0/g, " ")

    expect(
      /Traitement en \d+\s*séances?/.test(text),
      `the edit dialog must resolve the crown's catalogue protocol — split-by-default is now the rule. ` +
        `Dialog text began: ${text.slice(0, 200)}`,
    ).toBeTruthy()

    // It names what is being done TODAY, which is the one fact a dentist needs from the card…
    expect(/Ce rendez-vous est la 1re/.test(text), "the card must name this séance").toBeTruthy()
    // …and the séances that follow it.
    expect(/Ensuite/.test(text), "and the ones after it").toBeTruthy()

    /*
     * The reported field defect: a client booked « Couronne / bridge », read the sentence and had **no control
     * at all** underneath it, because the edit dialog never passed `onStartProtocol` — for everyone, always —
     * while the picker rendered the sentence unconditionally and the button inside `{onStartProtocol && …}`.
     *
     * So the séance list must be an editable frise, and the way out of it must be reachable. Both live below
     * the fold in a 1440×900 dialog, so the dialog's own body is scrolled rather than the page.
     */
    await dialog.evaluate((el: HTMLElement) => {
      const body = el.querySelector(".overflow-y-auto") as HTMLElement | null
      if (body) body.scrollTop = body.scrollHeight
    })

    // « Tout faire en une **seule** séance » — the word « seule » is in the label and a matcher without it
    // finds nothing. Kept loose so a rewording does not read as a missing control.
    const escape = dialog.getByRole("button", { name: /Tout faire en une\s+(seule\s+)?séance/ })
    await expect(
      escape,
      "split-by-default needs a way out, or the dentist cannot say « une seule séance »",
    ).toBeVisible()

    /*
     * Every séance of the protocol is on screen as its own row — a readable frise, never a disclosure mode.
     * The labels come from the CATALOGUE, not from literals: an act's protocol differs between databases, and
     * the first draft of this test named « Empreinte » and failed on a freshly seeded catalogue that has no
     * such step.
     */
    for (const label of protocolLabels) {
      expect(text.includes(label), `séance « ${label} » must be listed. Frise read: ${text.slice(0, 300)}`)
        .toBeTruthy()
    }
  })

  test("BOOK-44 · merely OPENING the edit dialog does not change the appointment's duration", async ({
    api,
    arrange,
    page,
    patient,
    sharedContext,
  }) => {
    const p = await patient("EditNoop")
    const crown = await api.protocolAct(2)

    const appt = await api.createAppointment({
      patientId: p.id,
      appointmentDateTime: arrange.slot(1, 16).toISOString(),
      durationMinutes: 45, // deliberately NOT the protocol's own first-step duration
      procedures: [{ procedureTypeId: crown.id, agreedCost: null }],
    })

    await gotoApp(page, `/appointments?appointmentId=${appt.id}`, sharedContext)
    await expect(page.locator("[data-slot='dialog-content']").first()).toBeVisible()

    // Close without saving.
    await page.keyboard.press("Escape")

    const reread = await api.appointment(appt.id)
    /*
     * ⚠️ The DTO field is `duration`, a **TimeSpan string** (« 00:45:00 »), not `durationMinutes` — that name
     * exists only on the *command*. Reading the wrong one gives `undefined`, which `toBe(45)` reports as a
     * product defect.
     */
    const minutes = (() => {
      const [h, m] = String(reread.duration ?? "").split(":").map(Number)
      return h * 60 + m
    })()
    expect(
      minutes,
      `the appointment now reads ${reread.duration}. The protocol is derived on RENDER, never seeded into ` +
        "state by an effect — the edit dialog's onChange also resets `durationTouched`, so an effect seeding " +
        "through it would silently re-length every appointment merely by opening it",
    ).toBe(45)
  })
})

test.describe("XCUT · transversal, dans un navigateur @t0", () => {
  /**
   * ⚠️ A scroll container must be `relative`, or it does not clip its own `absolute` children — and Tailwind's
   * `sr-only` **is** `position: absolute`. With `AppShell`'s `<main>` left static, every screen-reader-only
   * line below the fold resolved against `<body>`, escaped the page scroller and made the *document* taller
   * than `h-dvh`: a third scrollbar onto blank space, 1168 px on the dashboard at 1440×900 and 2611 px at
   * 390×844.
   */
  const routes = ["/", "/appointments", "/patients", "/treatment-plans", "/caisse", "/factures", "/creances"]

  for (const route of routes) {
    test(`XCUT-09 · ${route} does not grow a third scrollbar`, async ({ page, sharedContext }) => {
      const errors = watchConsole(page)
      await gotoApp(page, route, sharedContext)

      /*
       * ⚠️ **Prove we are in the APP first.** Every assertion below also passes on the login page — it is
       * short, it has no console crash, and it does not scroll. A session that died mid-run therefore turns
       * this whole check green while measuring nothing, which is the vacuous pass § 3 is about. Measured: an
       * earlier run reported seven of these green from `/login`.
       */
      expect(page.url(), `redirected to the login page — the session died before ${route} was measured`).not.toContain(
        "/login",
      )
      await expect(page.getByRole("navigation").or(page.getByText(/Tableau de bord/i)).first()).toBeVisible()

      const overflow = await page.evaluate(() => ({
        doc: document.documentElement.scrollHeight,
        view: window.innerHeight,
        bodyH: document.body.scrollHeight,
        wide: document.documentElement.scrollWidth > window.innerWidth + 1,
      }))

      expect(
        overflow.doc,
        `the document is ${overflow.doc}px against a ${overflow.view}px viewport — the page scroller is not ` +
          `clipping its own absolutes`,
      ).toBeLessThanOrEqual(overflow.view + 2)

      expect(overflow.wide, "the body must never scroll horizontally").toBeFalsy()

      const crashes = errors.filter((e) => /before initialization|Cannot access|is not a function/i.test(e))
      expect(crashes, `console on ${route}: ${JSON.stringify(errors.slice(0, 4))}`).toHaveLength(0)
    })
  }
})
