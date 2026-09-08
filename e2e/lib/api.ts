import { readFileSync } from "node:fs"
import { join } from "node:path"
import { API } from "./env"

/**
 * A thin client over the clinic API, for **arranging** a scenario's precondition and for **asserting** on the
 * coupled surfaces a gesture is supposed to move.
 *
 * <p><b>Why the suite talks to the wire at all, in a suite whose point is the browser.</b> Two reasons, both
 * from `.claude/rules/verification.md`:</p>
 *
 * <ul>
 *   <li>§ 5 — reach for the cheapest ground truth first. Arranging « a plan bridged to a live note that has
 *   collected 800 of 1 000 » through the UI is a dozen dialogs; through the wire it is four calls, and the
 *   scenario under test has not been consumed by its own setup.</li>
 *   <li>§ 3 — assert positively. « Créances shows 40 » is one number here and a text-scrape there; the whole
 *   class of defect this suite exists for is a figure that is right on the screen you touched and wrong on the
 *   four you did not.</li>
 * </ul>
 *
 * <p>⚠️ <b>The gesture under test is always performed in the browser.</b> This client arranges and asserts; it
 * never stands in for the click. A defect this repo has recorded more than once lives entirely in the client —
 * a locked field the server also imposes, a `plan.items[0]`, a `setError` that poisons a form — and a
 * wire-only test cannot see any of them.</p>
 */
export class ClinicApi {
  private readonly token: string

  constructor(token?: string) {
    this.token =
      token ?? readFileSync(join(__dirname, "..", ".artifacts", "token.txt"), "utf8").trim()
  }

  /**
   * One request.
   *
   * ⚠️ Returns `{ status, body }` rather than throwing on a non-2xx, because **a refusal is a result this suite
   * asserts on**. Half the tier-0 scenarios expect an HTTP 400 with a specific `code`; a client that threw
   * would turn each of those into a test that has to catch, and the first one written without a catch would
   * pass for the wrong reason.
   */
  async call<T = unknown>(
    method: string,
    path: string,
    body?: unknown,
  ): Promise<{ status: number; body: T; raw: string }> {
    let res = await this.send(method, path, body)

    /*
     * ⚠️ **A 429 is backed off and retried, never reported.** The API rate-limits by account, and a suite that
     * arranges its own preconditions is by far the burstiest client it will ever see: 65 tests × several writes
     * inside 90 seconds. Measured on a full run — four tests failed with « Trop de tentatives. Veuillez
     * réessayer dans 5 minutes. » on `POST /patients`, which reads exactly like a broken endpoint and is
     * actually the limiter doing its job.
     *
     * Honouring `Retry-After` rather than fixing a delay: the limiter is the authority on how long it wants,
     * and guessing shorter just burns the budget again. `/bff/auth/token`'s own handler makes the same choice
     * for the same reason — « the client's job is to back off, not to sign the user out ».
     */
    for (let attempt = 0; res.status === 429 && attempt < 3; attempt++) {
      const retryAfter = Number(res.headers.get("retry-after"))
      const waitMs = Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter * 1000 : 20_000 * (attempt + 1)
      await new Promise((r) => setTimeout(r, waitMs))
      res = await this.send(method, path, body)
    }

    const raw = await res.text()
    let parsed: unknown = null
    try {
      parsed = raw ? JSON.parse(raw) : null
    } catch {
      // An HTML body here is the dev server holding a build error, not a payload. Left as `raw` so the failure
      // message can say so instead of « unexpected token < ».
    }
    return { status: res.status, body: parsed as T, raw }
  }

  /** One HTTP round trip, with no retry policy of its own. */
  private send(method: string, path: string, body?: unknown) {
    return fetch(`${API}${path}`, {
      method,
      headers: {
        Authorization: `Bearer ${this.token}`,
        ...(body === undefined ? {} : { "Content-Type": "application/json" }),
      },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    })
  }

  get = <T = any>(p: string) => this.call<T>("GET", p)
  post = <T = any>(p: string, b?: unknown) => this.call<T>("POST", p, b)
  put = <T = any>(p: string, b?: unknown) => this.call<T>("PUT", p, b)
  del = <T = any>(p: string) => this.call<T>("DELETE", p)

  /**
   * Unwraps the `Result<T>` envelope the API returns, failing loudly with the server's own French sentence.
   * Use it for **arrange** steps, where a silent failure would make the test assert against a precondition that
   * does not exist — the shape § 3 calls « reported a loading spinner as a success ».
   */
  async ok<T = any>(method: string, path: string, body?: unknown): Promise<T> {
    const r = await this.call<any>(method, path, body)
    if (r.status < 200 || r.status >= 300) {
      throw new Error(`arrange failed: ${method} ${path} → HTTP ${r.status} ${r.raw.slice(0, 400)}`)
    }
    return (r.body?.value ?? r.body) as T
  }

  // ── Catalogue ────────────────────────────────────────────────────────────────────────────────────────────
  /** ⚠️ `/procedure-types` is a **paged** read (`{ items, totalCount, … }`), not a bare array. */
  procedureTypes = async (activeOnly = true) =>
    (await this.ok<{ items: any[] }>("GET", `/procedure-types?includeInactive=${!activeOnly}&pageSize=200`))
      .items

  /** The catalogue act named exactly, or a loud failure — a `find` returning undefined is a confusing later crash. */
  async procedureNamed(name: string) {
    const all = await this.procedureTypes(false)
    const hit = all.find((p) => p.name === name)
    if (!hit) {
      throw new Error(
        `no catalogue act named « ${name} ». Present: ${all.map((p) => p.name).slice(0, 40).join(" · ")}`,
      )
    }
    return hit
  }

  /**
   * The first catalogue act matching a predicate over its protocol, or a loud failure naming what was searched.
   *
   * ⚠️ **This exists because hard-coding an act's tarif or its step labels over-fits the suite to one
   * database.** Measured: seven tests that were green against the developer's hand-edited catalogue failed on a
   * freshly seeded one — « Détartrage » was not 60, « Couronne / bridge » had no step called « Empreinte », and
   * « Gingivectomie » *did* carry an interval. None of that is a product defect and all of it reads as one, so
   * the expectations are derived from whatever catalogue is actually present.
   */
  async procedureWhere(what: string, predicate: (act: any) => boolean) {
    const all = await this.procedureTypes(false)
    const hit = all.find(predicate)
    if (!hit) {
      throw new Error(
        `no catalogue act satisfies « ${what} ». Present: ` +
          all.map((p) => `${p.name}[${(p.defaultSteps ?? []).length} steps]`).join(" · "),
      )
    }
    return hit
  }

  /** An act with a real multi-séance protocol — at least `minSteps` of them. */
  protocolAct = (minSteps = 2) =>
    this.procedureWhere(
      `an act with at least ${minSteps} catalogue steps`,
      (a) => (a.defaultSteps ?? []).length >= minSteps,
    )

  /** An act that is billed in one visit — no protocol at all. */
  singleSeanceAct = () =>
    this.procedureWhere(
      "an act with no catalogue protocol and a non-zero tarif",
      (a) => (a.defaultSteps ?? []).length === 0 && Number(a.defaultCost) > 0,
    )

  // ── Patients ─────────────────────────────────────────────────────────────────────────────────────────────
  createPatient = (p: { firstName: string; lastName: string; phone?: string | null }) =>
    this.ok<any>("POST", "/patients", {
      firstName: p.firstName,
      lastName: p.lastName,
      phone: p.phone ?? null,
    })

  patient = (id: string) => this.ok<any>("GET", `/patients/${id}`)

  /**
   * Patients whose surname matches, as a **loud** read.
   *
   * ⚠️ **`this.get(…)` plus `?? []` is how a rate-limited read becomes « the patient was never created ».**
   * Measured: `BOOK-05` and `BOOK-06` reported « Found 0 » — a defect in `createdPatientIdRef`, on the face of
   * it — while the patients were in the database all along. A 429 that outlasts the backoff returns a body with
   * no `items`, `?? []` turns that into an empty list, and the assertion then describes the limiter rather than
   * the product. `ok()` throws instead, so the failure names itself.
   *
   * ⚠️ Free-text search is a **database** question here (`SearchTerm` + `SqlSearch`'s `unaccent`), so the
   * filter below narrows an already-correct result rather than doing the searching.
   */
  async patientsNamed(lastName: string) {
    /*
     * ⚠️ **The parameter is `searchTerm`, not `search`** — and this is the THIRD endpoint on which that class of
     * mistake has cost a test, because **an unknown query parameter is ignored, never refused**:
     * `fromDate`→`fromDay` on the caisse, `fromDate`→`startDate` on appointments, `search`→`searchTerm` here.
     * With the wrong name the endpoint cheerfully returned all 266 patients in alphabetical order, the surname
     * under test sat past the first page, and `BOOK-06` reported « Found 0 » — « the patient was never
     * created » — about a patient that had been created seconds earlier.
     */
    const page = await this.ok<{ items: any[]; totalCount?: number }>(
      "GET",
      `/patients?searchTerm=${encodeURIComponent(lastName)}&pageSize=100`,
    )
    const items = page.items ?? []

    /*
     * ⚠️ **And a guard against the same mistake being made again.** A filter that was ignored is invisible in
     * the response — the rows just aren't the ones you asked for — so the only way to notice is to check that
     * what came back is consistent with the question. Every row must plausibly match the term; if a majority do
     * not, the parameter was dropped and this read is not answering what it looks like it is answering.
     */
    const plausible = items.filter((r: any) =>
      `${r.firstName ?? ""} ${r.lastName ?? ""}`.toLowerCase().includes(lastName.toLowerCase()),
    )
    if (items.length > 0 && plausible.length === 0) {
      throw new Error(
        `the patient search appears to have IGNORED its filter: asked for « ${lastName} », got ` +
          `${items.length} row(s) starting « ${items[0]?.lastName} ». Check the parameter name.`,
      )
    }

    return items.filter((r: any) => r.lastName === lastName)
  }

  /**
   * A patient's appointments. ⚠️ `GET /appointments` answers a **bare array**, not `{ items }` — and its date
   * parameters are `startDate`/`endDate`, not `fromDate`/`toDate`.
   */
  async appointmentsOf(patientId: string) {
    const rows = await this.ok<any>("GET", `/appointments?patientId=${patientId}`)
    return Array.isArray(rows) ? rows : (rows.items ?? [])
  }
  deletePatient = (id: string) => this.call("DELETE", `/patients/${id}`)

  // ── Appointments ─────────────────────────────────────────────────────────────────────────────────────────
  createAppointment = (a: Record<string, unknown>) =>
    this.ok<any>("POST", "/appointments", { allowOutsideWorkingHours: true, allowOverlap: true, ...a })

  appointment = (id: string) => this.ok<any>("GET", `/appointments/${id}`)

  // ── Fiches de soins ──────────────────────────────────────────────────────────────────────────────────────
  createRecord = (patientId: string, r: Record<string, unknown>) =>
    this.call<any>("POST", `/patients/${patientId}/dental-records`, { patientId, ...r })

  updateRecord = (patientId: string, id: string, r: Record<string, unknown>) =>
    this.call<any>("PUT", `/patients/${patientId}/dental-records/${id}`, { id, patientId, ...r })

  records = (patientId: string) => this.ok<any>("GET", `/patients/${patientId}/dental-records`)

  // ── Odontogramme ─────────────────────────────────────────────────────────────────────────────────────────
  /**
   * ⚠️ **A bare array of tooth rows** — *not* `{ items }` and *not* `{ teeth }`, unlike almost every other read
   * in this API. Getting that wrong does not throw: `chart.teeth ?? chart.items ?? []` yields `[]`, every
   * lookup answers `null`, and a « nothing is charted » assertion **passes vacuously** — which is exactly how
   * the first draft of `FICHE-30` reported a green result while measuring nothing at all.
   *
   * <p>A tooth with no entry is simply absent (there is no « Sain » row), so absence *is* the healthy state.</p>
   */
  odontogram = (patientId: string) => this.ok<any[]>("GET", `/patients/${patientId}/odontogram`)

  /** The charted condition of one tooth, or `null` when nothing is charted on it. */
  async toothCondition(patientId: string, toothNumber: number): Promise<string | null> {
    const chart = await this.odontogram(patientId)
    if (!Array.isArray(chart)) {
      throw new Error(`odontogram is expected to be an array; got ${typeof chart}: ${JSON.stringify(chart).slice(0, 200)}`)
    }
    return chart.find((t) => t.toothNumber === toothNumber)?.condition ?? null
  }

  // ── Traitements ──────────────────────────────────────────────────────────────────────────────────────────
  plan = (id: string) => this.ok<any>("GET", `/treatment-plans/${id}`)
  plans = (q = "") => this.ok<any>("GET", `/treatment-plans${q}`)
  startTreatment = (b: Record<string, unknown>) => this.call<any>("POST", "/treatment-plans/start", b)
  acceptPlan = (id: string, b: Record<string, unknown> = {}) =>
    this.call<any>("POST", `/treatment-plans/${id}/accept`, b)
  completePlan = (id: string, b: Record<string, unknown> = {}) =>
    this.call<any>("POST", `/treatment-plans/${id}/complete`, b)
  stopPlan = (id: string, b: Record<string, unknown> = {}) =>
    this.call<any>("POST", `/treatment-plans/${id}/stop`, b)
  reopenPlan = (id: string) => this.call<any>("POST", `/treatment-plans/${id}/reopen`, {})
  cancelPlan = (id: string, reason: string) =>
    this.call<any>("POST", `/treatment-plans/${id}/cancel`, { reason })
  deletePlan = (id: string) => this.call<any>("DELETE", `/treatment-plans/${id}`)
  setSteps = (planId: string, itemId: string, steps: unknown[]) =>
    this.call<any>("PUT", `/treatment-plans/${planId}/items/${itemId}/steps`, { steps })
  /** ⚠️ `patientId` is **required** — without it the endpoint answers « Patient introuvable. », not an empty list. */
  continuableActs = (patientId: string) =>
    this.ok<any>("GET", `/treatment-plans/continuable-acts?patientId=${patientId}`)
  continueRecordedAct = (b: Record<string, unknown>) =>
    this.call<any>("POST", "/treatment-plans/continue-recorded-act", b)
  treatmentsInProgress = () => this.ok<any>("GET", "/treatment-plans/treatments-in-progress")

  // ── Argent ───────────────────────────────────────────────────────────────────────────────────────────────
  invoices = (q = "") => this.ok<any>("GET", `/invoices${q}`)
  invoice = (id: string) => this.ok<any>("GET", `/invoices/${id}`)
  billRecord = (recordId: string, b: Record<string, unknown> = {}) =>
    this.call<any>("POST", `/invoices/from-dental-record/${recordId}`, b)
  cancelInvoice = (id: string, b: Record<string, unknown> = {}) =>
    this.call<any>("POST", `/invoices/${id}/cancel`, b)
  avoir = (id: string, b: Record<string, unknown>) => this.call<any>("POST", `/invoices/${id}/avoir`, b)
  billingSummary = (patientId: string) => this.ok<any>("GET", `/patients/${patientId}/billing-summary`)
  receivables = (q = "") => this.ok<any>("GET", `/billing/receivables${q}`)
  /**
   * ⚠️ **The window parameters are `fromDay` / `toDay` (clinic-local `YYYY-MM-DD`), not `fromDate` / `toDate`.**
   * An unrecognised query parameter is **ignored, never refused**, so the wrong name silently returns *today's*
   * window — which reads as « the payment never reached la caisse » and is the false finding this helper exists
   * to prevent. `from` / `to` also exist and take instants.
   */
  caisse = (fromDay?: string, toDay?: string) =>
    this.ok<any>("GET", `/billing/caisse${this.dayWindow(fromDay, toDay)}`)

  caisseLedger = (fromDay?: string, toDay?: string, extra = "") =>
    this.ok<any>(
      "GET",
      `/billing/caisse/ledger${this.dayWindow(fromDay, toDay)}${this.dayWindow(fromDay, toDay) ? "&" : "?"}pageSize=500${extra}`,
    )

  private dayWindow(fromDay?: string, toDay?: string) {
    const q = [
      fromDay ? `fromDay=${fromDay}` : null,
      toDay ? `toDay=${toDay}` : null,
    ].filter(Boolean)
    return q.length ? `?${q.join("&")}` : ""
  }
  cheques = (q = "") => this.ok<any>("GET", `/billing/cheques${q}`)
}

/**
 * Millimes, as an integer. Money comparisons go through this rather than `toBeCloseTo`: the product's own
 * arithmetic is integer millimes (`distributeSessionTotal`), so a float tolerance would accept a rounding
 * defect the product deliberately does not have.
 */
export const mil = (n: number) => Math.round(n * 1000)
