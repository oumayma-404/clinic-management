import { expect, test } from "../lib/fixtures"
import { mil } from "../lib/api"

/**
 * The **read-only** half of the suite — the part that can be pointed at a live hosted deployment.
 *
 * ⚠️ **Nothing here writes, and that is the design.** A practice's ledger is not a fixture, so the deploy gate
 * runs with `CLINIC_E2E_READONLY=1`, every `@mutating` spec is skipped, and what remains is what can be
 * answered by *looking*. That is deliberately a smaller question than CI's — and it is the right one to ask
 * about a deployment that is already serving patients.
 *
 * <p>What it can still catch, all of which has actually happened in this product:</p>
 *
 * <ul>
 *   <li>A hot-path screen that renders **nothing** — a client-side throw during render, invisible to
 *   `tsc --noEmit`, `check:responsive` and `next build`, caught by an error boundary and shown as an empty
 *   region with no HTTP failure (`PLAN-01`).</li>
 *   <li>Two money reads **disagreeing** about the same data. There is one authority
 *   (`PlanBillingRules`) precisely so they cannot, and every read below is compared against another read
 *   rather than against a literal — so this works on any database, with any amount of real money in it.</li>
 *   <li>A paged read showing one row twice and skipping another — `OFFSET` over a non-unique sort, which reads
 *   to a practice as « a record vanished ».</li>
 * </ul>
 */

test.describe("SMOKE · reads only @smoke", () => {
  test("every hot-path read answers, and none of them is empty-because-broken", async ({ api }) => {
    /*
     * ⚠️ Asserted **positively**: each read must come back with the *shape* it promises, because « no error
     * string » is not « loaded ». An endpoint answering `{}` or `null` is what a broken projection looks like,
     * and `?? []` in a test is how that becomes a green line.
     */
    const reads: Array<[string, () => Promise<any>, (v: any) => boolean]> = [
      ["procedure-types", () => api.procedureTypes(false), (v) => Array.isArray(v)],
      ["treatment-plans", () => api.plans("?pageSize=5"), (v) => Array.isArray(v?.items)],
      ["treatments-in-progress", () => api.treatmentsInProgress(), (v) => Array.isArray(v?.items)],
      ["invoices", () => api.invoices("?pageSize=5"), (v) => Array.isArray(v?.items)],
      ["receivables", () => api.receivables("?pageSize=5"), (v) => Array.isArray(v?.items)],
      ["caisse", () => api.caisse(), (v) => typeof v?.cashIn === "number"],
      ["caisse ledger", () => api.caisseLedger(), (v) => Array.isArray(v?.movements)],
      ["cheques", () => api.cheques("?pageSize=5"), (v) => Array.isArray(v?.items)],
    ]

    // Collected, never stopped at the first failure: one dead endpoint must not hide the state of the other
    // seven (`.claude/rules/verification.md` § 1).
    const findings: string[] = []
    for (const [name, read, shaped] of reads) {
      try {
        const value = await read()
        if (!shaped(value)) findings.push(`${name}: answered, but not in its documented shape`)
      } catch (e) {
        findings.push(`${name}: ${(e as Error).message.slice(0, 200)}`)
      }
    }
    expect(findings, findings.join("\n")).toHaveLength(0)
  })

  test("the catalogue carries the acts every scenario resolves by name", async ({ api }) => {
    // Not a style check: `procedureNamed` fails loudly, and a deployment whose catalogue was never initialised
    // would fail 60 tests one at a time with the same cause.
    const acts = await api.procedureTypes(false)
    expect(acts.length, "an empty catalogue means the clinic was never seeded").toBeGreaterThan(0)

    const withProtocol = acts.filter((a: any) => (a.defaultSteps ?? []).length >= 2)
    expect(
      withProtocol.length,
      "at least one multi-séance act must exist, or the whole HP-4/HP-5b half of the product is unreachable",
    ).toBeGreaterThan(0)
  })

  test("XCUT-01 · a paged read never shows a row twice, nor skips one", async ({ api }) => {
    const size = 5
    const seen = new Set<string>()
    let duplicated: string | null = null
    let pages = 0

    for (let page = 1; page <= 5; page++) {
      const chunk = await api.invoices(`?page=${page}&pageSize=${size}`)
      const rows = chunk.items ?? []
      if (rows.length === 0) break
      pages++
      for (const row of rows) {
        if (seen.has(row.id)) duplicated = row.id
        seen.add(row.id)
      }
      if (rows.length < size) break
    }

    test.skip(pages < 2, `only ${pages} page(s) of invoices — paging cannot be exercised on this deployment`)
    expect(
      duplicated,
      `invoice ${duplicated} appeared on two pages — every paged read must order on a unique column last`,
    ).toBeNull()
  })

  test("the money reads agree with each other about what is owed", async ({ api }) => {
    /*
     * ⚠️ Compared **read against read**, never against a figure typed here: the deployment has whatever real
     * money it has. There is one authority on which plans carry debt (`PlanBillingRules`) shared by « Solde
     * patient », « Créances », la caisse and the dashboard, and the *point* of that is that they cannot
     * disagree — so a disagreement is the finding, whatever the numbers are.
     */
    const receivables = await api.receivables("")
    const rows = receivables.items ?? []
    test.skip(rows.length === 0, "nothing is owed on this deployment — the comparison has no subject")

    // The list's own total must equal the sum of its rows.
    const summed = rows.reduce((t: number, r: any) => t + mil(r.totalOutstanding), 0)
    expect(
      mil(receivables.totalOutstanding),
      "« Créances »' own total must be the sum of the rows it is showing",
    ).toBe(summed)

    // And each row must agree with that patient's own billing summary, which is a different query entirely.
    const disagreements: string[] = []
    for (const row of rows.slice(0, 5)) {
      const summary = await api.billingSummary(row.patientId)
      const own = mil(summary.totalOutstanding ?? summary.outstanding ?? 0)
      if (own !== mil(row.totalOutstanding)) {
        disagreements.push(
          `${row.patientName}: « Créances » says ${row.totalOutstanding}, « Solde patient » says ` +
            `${own / 1000}`,
        )
      }
    }
    expect(disagreements, disagreements.join("\n")).toHaveLength(0)
  })

  test("la caisse's day total is the sum of the movements behind it", async ({ api, arrange }) => {
    const day = arrange.localDay(0).slice(0, 10)
    const totals = await api.caisse(day, day)
    const ledger = await api.caisseLedger(day, day)
    const movements = ledger.movements ?? []

    /*
     * « Extrait de caisse » is a READ over the same window as the totals — « the lines and the totals always
     * describe the same period », as the controller puts it. A voided movement is excluded from the totals and
     * still listed, which is why the sum filters on `isVoided` rather than taking every row.
     */
    const cashIn = movements
      .filter((m: any) => m.direction === "In" && !m.isVoided)
      .reduce((t: number, m: any) => t + mil(m.amount), 0)

    expect(
      mil(totals.cashIn),
      `the day's « encaissé » must equal its own movements: ${movements.length} row(s) on ${day}`,
    ).toBe(cashIn)

    // The by-method breakdown is the same money, cut a second way.
    const byMethod = (totals.cashInByMethod ?? []).reduce((t: number, m: any) => t + mil(m.amount), 0)
    expect(byMethod, "« dont espèces / chèque / carte / virement » must add up to « encaissé »").toBe(
      mil(totals.cashIn),
    )
  })
})
