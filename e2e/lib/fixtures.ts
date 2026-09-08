import { test as base, type BrowserContext, type Page } from "@playwright/test"
import { join } from "node:path"
import { ClinicApi } from "./api"
import { MAY_MUTATE } from "./env"

/**
 * The suite's own `test`, carrying an authenticated API client and the arrange helpers every hot path needs.
 *
 * ⚠️ **Fixture patients are named « E2E … » and are never deleted.** Two reasons. A patient carrying money
 * cannot be deleted at all (`PatientDeletionBlockers`), so a cleanup step would fail on exactly the scenarios
 * that mattered; and a run that tidies up destroys the evidence a red result needs. They are cheap, they are
 * obviously synthetic, and `reconcile-money` treats them like any other row — which is the point.
 */
export type Fixtures = {
  api: ClinicApi
  /**
   * ⚠️ **One browser context per worker, reused by every test — this overrides Playwright's per-test
   * isolation on purpose, and the suite does not work without it.**
   *
   * The BFF's refresh token **rotates on use** (`SessionFamily.Rotate`), which is correct
   * rotation-detection behaviour and fatal to the default fixture: Playwright builds each test a fresh
   * context from the same `storageState` file, so the first test to refresh invalidates the copy every later
   * context replays — and the run degrades into 401s and a redirect to `/login` **partway through**, with the
   * early tests green and the late ones reporting « element not found ». Measured exactly that way: PLAN-01
   * and BOOK-25b passed, BOOK-33 and BOOK-44 timed out looking for a dialog on the login page.
   *
   * Signing in again per file is not the alternative: a TOTP code is single-use and one 30-second window
   * apart, so a suite that re-authenticates cannot run faster than two files a minute.
   *
   * Sharing the context keeps the rotated cookie in one jar. `workers: 1` makes that one jar for the whole
   * run.
   */
  sharedContext: BrowserContext
  /** A fresh patient for this test alone, so no two tests share a ledger. */
  patient: (label: string) => Promise<{ id: string; name: string }>
  /** Arrange helpers, grouped by what they build. */
  arrange: Arrange
}

export class Arrange {
  constructor(private readonly api: ClinicApi) {}

  /** Tomorrow at 09:00 local — a slot that is neither in the past nor at a boundary. */
  slot(dayOffset = 1, hour = 9, minute = 0) {
    const d = new Date()
    d.setDate(d.getDate() + dayOffset)
    d.setHours(hour, minute, 0, 0)
    return d
  }

  /** `YYYY-MM-DDT00:00:00` in the clinic's own day, the shape the fiche's date field sends. */
  localDay(dayOffset = 0) {
    const d = new Date()
    d.setDate(d.getDate() + dayOffset)
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}T00:00:00`
  }

  /**
   * A booking. `allowOverlap` / `allowOutsideWorkingHours` default true in the client: an arrange step must not
   * fail because the QA clinic happens to be closed on the day the suite runs, which is a scheduling fact and
   * not the thing under test. The scenarios that *are* about those prompts pass them explicitly.
   */
  async booking(patientId: string, procedureTypeId: string, opts: Record<string, unknown> = {}) {
    return this.api.createAppointment({
      patientId,
      appointmentDateTime: this.slot().toISOString(),
      durationMinutes: 30,
      procedureTypeId,
      ...opts,
    })
  }

  /**
   * A saved fiche de soins. Returns the raw `{ status, body }` so a scenario can assert on a **refusal**: the
   * billing outcome and every refusal code ride on this response.
   */
  async fiche(
    patientId: string,
    acts: Array<Record<string, unknown>>,
    opts: Record<string, unknown> = {},
  ) {
    return this.api.createRecord(patientId, {
      interventionDate: this.localDay(),
      amountPaid: 0,
      isAdultTeeth: true,
      acts,
      notes: [],
      importantNotes: [],
      ...opts,
    })
  }

  /** One act row for a fiche, with the fields the parser actually reads. */
  act(o: {
    procedureTypeId?: string | null
    name: string
    cost: number
    teeth?: number[]
    pontics?: number[]
    perTooth?: boolean
    unitCost?: number | null
    resultingCondition?: string | null
  }) {
    return {
      procedureTypeId: o.procedureTypeId ?? null,
      procedureName: o.name,
      cost: o.cost,
      unitCost: o.unitCost ?? null,
      isPerTooth: o.perTooth ?? false,
      toothNumbers: o.teeth ?? [],
      ponticToothNumbers: o.pontics ?? [],
      resultingCondition: o.resultingCondition ?? null,
      surfaces: null,
      note: null,
    }
  }
}

export const test = base.extend<Fixtures, { _ctx: BrowserContext; _page: Page }>({
  _ctx: [
    async ({ browser }, use) => {
      const context = await browser.newContext({
        storageState: join(__dirname, "..", ".artifacts", "session.json"),
        locale: "fr-FR",
        timezoneId: "Africa/Tunis",
        viewport: { width: 1440, height: 900 },
      })
      await use(context)
      await context.close()
    },
    { scope: "worker" },
  ],

  /*
   * ⚠️ **ONE page for the whole worker, not one per test — and closing pages between tests is what broke the
   * suite.** The BFF rotates the refresh cookie on every `/bff/auth/token`, and
   * `SessionFamily.PreviousCredentialHash` exists to **detect** a replay, not to forgive one: presenting the
   * pre-rotation value ends the family outright («&#160;Un identifiant de session déjà remplacé a été
   * présenté.&#160»). A page closed mid-flight can still land its `/bff/auth/token` *after* the next page has
   * rotated — so the suite killed its own session about twenty seconds in, every run, and every test after
   * that point failed on the login page while reporting « element not found ».
   *
   * Measured: the family rotated exactly once, then ended 18 s later, with no second rotation.
   *
   * One long-lived page also matches how the product is actually used, and each test navigates from scratch,
   * so nothing on screen leaks between them.
   */
  _page: [
    async ({ _ctx }, use) => {
      const page = await _ctx.newPage()
      await use(page)
      // Not closed here either: the worker teardown closes the context, which closes it without leaving a
      // request in flight against a rotated cookie.
    },
    { scope: "worker" },
  ],

  sharedContext: async ({ _ctx }, use) => {
    await use(_ctx)
  },

  page: async ({ _page }, use) => {
    // Listeners are per-test (`watchConsole`), so drop the previous test's before handing the page over.
    _page.removeAllListeners("console")
    _page.removeAllListeners("pageerror")
    await use(_page)
  },

  api: async ({}, use) => {
    await use(new ClinicApi())
  },

  arrange: async ({ api }, use) => {
    await use(new Arrange(api))
  },

  patient: async ({ api }, use) => {
    let n = 0
    await use(async (label: string) => {
      if (!MAY_MUTATE) throw new Error("this run is read-only (CLINIC_E2E_READONLY=1) and cannot create a patient")
      // The suffix keeps two runs from colliding on the duplicate-name guard, which is itself a scenario
      // (BOOK-05) and must not fire by accident here.
      const stamp = `${Date.now().toString(36)}${n++}`
      const p = await api.createPatient({ firstName: "E2E", lastName: `${label}-${stamp}` })
      return { id: p.id, name: `E2E ${label}-${stamp}` }
    })
  },
})

export { expect } from "@playwright/test"

/** Skips a whole spec file when the run may not write. Used by every `@mutating` file's first line. */
export const mutatingOnly = () =>
  test.skip(!MAY_MUTATE, "read-only run (CLINIC_E2E_READONLY=1)")
