import { expect, mutatingOnly, test } from "../lib/fixtures"

/**
 * HP-5c — **le bridge : le seul acte du produit qui charte deux états.**
 *
 * Everywhere else one act charts one condition across all its teeth. A fixed bridge cannot: its **piliers** are
 * crowned teeth that keep their own roots, and its **pontiques** are suspended replacements with no root under
 * them at all. Entered as one act with one condition it charted **three abutments and no pontic** — anatomically
 * impossible — and the odontogramme then drew a travée across the run, which made it look deliberate. Entering
 * the same procedure twice was the only way to say it, and nothing suggested that.
 *
 * ⚠️ **The roles are never inferred from position, and `BridgeCharting` exists because the product tried.**
 * « The ends are piliers and the middles are pontiques » is false for the two arrangements a prosthodontist
 * reaches for most often after the simple three-unit case: a **pier** abutment is crowned in the *middle* of the
 * span, and a **cantilever** hangs a pontique past the last abutment. Reading the geometry charts both of those
 * backwards, silently, on a record that outlives the visit. And FDI numbers do not sort into anatomical order —
 * 12 · 11 · 21 sorts to 11 · 12 · 21 — so « the middle one » is not even the middle tooth.
 *
 * ⚠️ **`ponticToothNumbers` is inert unless the act's own condition is a bridge unit** (`Bridge`,
 * `BridgePilier`, `BridgePontique` — `BridgeCharting.Units`). That is what keeps every row written before this
 * existed, and every bridge a dentist does not detail, charting the way it always did. The split is opt-in per
 * act and marking one tooth is the opt-in.
 *
 * ⚠️ **No catalogue act defaults to a bridge condition** — « Couronne / bridge (par élément) » charts
 * `Couronne`, which is right for a pilier (a bridge element *is* a crown on an abutment). The bridge vocabulary
 * is reached by the dentist setting the état on the fiche, so that is what these tests send.
 */

const PILIER = "BridgePilier"
const PONTIQUE = "BridgePontique"
const BRIDGE = "Bridge"

test.describe("HP-5c · le bridge @mutating @t0", () => {
  mutatingOnly()

  /** Records one finished bridge act and returns the chart it produced, tooth by tooth. */
  async function chartBridge(
    api: any,
    arrange: any,
    patientId: string,
    teeth: number[],
    pontics: number[],
    condition: string = BRIDGE,
  ) {
    // A single-séance act, so the act is `Done` on its first fiche and `ToothChartingRules` lets the end state
    // through — the bridge fold is about WHICH state, never about when.
    const act = await api.singleSeanceAct()
    const r = await arrange.fiche(
      patientId,
      [
        arrange.act({
          procedureTypeId: act.id,
          name: act.name,
          cost: 500,
          teeth,
          pontics,
          resultingCondition: condition,
        }),
      ],
      { amountPaid: 0 },
    )
    expect(r.status, `fiche: ${r.raw?.slice(0, 400)}`).toBe(200)

    const chart: Record<number, string | null> = {}
    for (const tooth of teeth) chart[tooth] = await api.toothCondition(patientId, tooth)
    return { act, saved: r.body?.value ?? r.body, chart }
  }

  test("FICHE-40 · a three-unit bridge charts two piliers and one pontique", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("Pont3")
    // 14 · 15 · 16, with 15 the suspended replacement. One act, one devis line, the right total.
    const { chart } = await chartBridge(api, arrange, p.id, [14, 15, 16], [15])

    expect(chart[14], "an abutment keeps its own root and is crowned").toBe(PILIER)
    expect(chart[16]).toBe(PILIER)
    expect(
      chart[15],
      "three abutments and no pontic is anatomically impossible, and is what one condition per act produced",
    ).toBe(PONTIQUE)
  })

  test("FICHE-42 · the SAME act with NO pontique marked charts exactly as it always did", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("PontAucun")
    const { chart } = await chartBridge(api, arrange, p.id, [14, 15, 16], [])

    /*
     * ⚠️ This is what makes the whole feature safe against every existing row: the fold is skipped entirely
     * when nothing is marked, so a bridge a dentist does not detail — and every record written before the split
     * existed — charts its act's own condition on every tooth, unchanged.
     */
    for (const tooth of [14, 15, 16]) {
      expect(chart[tooth], `tooth ${tooth} must keep the act's own condition`).toBe(BRIDGE)
    }
  })

  test("FICHE-43a · a CANTILEVER: the pontique hangs past the last abutment", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("PontCantilever")
    // 44 and 45 are crowned; 46 hangs off the end. « The middles are pontiques » charts this backwards.
    const { chart } = await chartBridge(api, arrange, p.id, [44, 45, 46], [46])

    expect(chart[44]).toBe(PILIER)
    expect(chart[45], "a cantilever's middle tooth is an ABUTMENT, not a pontic").toBe(PILIER)
    expect(chart[46], "…and the tooth past the end is the pontic").toBe(PONTIQUE)
  })

  test("FICHE-43b · a PIER abutment: the crowned tooth is in the MIDDLE of the span", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("PontPilier")
    // 14 · 15 · 16 · 17 · 18 — 16 is an intermediate abutment, so 15 and 17 are the pontiques.
    const { chart } = await chartBridge(api, arrange, p.id, [14, 15, 16, 17, 18], [15, 17])

    expect(chart[14]).toBe(PILIER)
    expect(chart[16], "a pier abutment is crowned in the middle of the span").toBe(PILIER)
    expect(chart[18]).toBe(PILIER)
    expect(chart[15]).toBe(PONTIQUE)
    expect(chart[17]).toBe(PONTIQUE)
  })

  test("FICHE-43c · FDI order is not anatomical order, so « the middle one » is the wrong tooth", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("PontFDI")
    /*
     * ⚠️ 12 · 11 · 21 crosses the midline and **sorts to 11 · 12 · 21**. Anatomically 11 is next to 21, so a
     * bridge replacing 11 has 12 and 21 as its abutments — and any rule that took « the middle of the sorted
     * list » would chart 12 as the pontic and 11 as an abutment. Both backwards, on the one arrangement where
     * the numbering actively misleads.
     */
    const { chart } = await chartBridge(api, arrange, p.id, [12, 11, 21], [11])

    expect(chart[12], "12 is an abutment though it sorts to the middle").toBe(PILIER)
    expect(chart[21]).toBe(PILIER)
    expect(chart[11], "11 is the replaced tooth though it sorts first").toBe(PONTIQUE)
  })

  test("FICHE-42b · pontiques marked on a NON-bridge act are inert", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("PontInerte")
    /*
     * ⚠️ `BridgeCharting.IsUnit` is what protects every row in the database. « Couronne » is not a bridge unit,
     * so a stale or mistaken pontique list must change nothing at all — a fold that fired on any condition
     * would rewrite ordinary crowns as bridge elements, on records nobody touched.
     */
    const { chart } = await chartBridge(api, arrange, p.id, [24, 25], [25], "Couronne")

    expect(chart[24]).toBe("Couronne")
    expect(chart[25], "a pontique mark on a non-bridge act must be ignored, not applied").toBe("Couronne")
  })

  test("FICHE-44 · reopening the fiche restores the pontique marks", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("PontRelire")
    const { saved } = await chartBridge(api, arrange, p.id, [14, 15, 16], [15])

    // Read back through the ordinary list read, which is what the modal reopens from.
    const records = await api.records(p.id)
    const reread = (records.items ?? records)[0]
    const act = reread.acts[0]

    expect(
      [...(act.ponticToothNumbers ?? [])].sort((a: number, b: number) => a - b),
      "the marks must round-trip, or the next save silently un-marks the pontic",
    ).toEqual([15])
    expect([...act.toothNumbers].sort((a: number, b: number) => a - b)).toEqual([14, 15, 16])
    expect(act.resultingCondition).toBe(BRIDGE)
    expect(saved.acts[0].id).toBeTruthy()
  })

  test("FICHE-44b · a STALE pontique naming a tooth no longer on the act is intersected, not refused", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("PontPerime")
    /*
     * ⚠️ The DTO says so in as many words: pontiques are « intersected with `ToothNumbers` by the aggregate
     * rather than refused — a client legitimately holds a stale list the moment the dentist changes the act's
     * état or removes a tooth ». Refusing would make removing a tooth from a bridge an unexplainable error; and
     * *keeping* the orphan would chart a pontic on a tooth the act does not treat.
     */
    const act = await api.singleSeanceAct()
    const r = await arrange.fiche(
      p.id,
      [
        arrange.act({
          procedureTypeId: act.id,
          name: act.name,
          cost: 500,
          teeth: [14, 15],
          pontics: [15, 47], // 47 was dropped from the act; the client still holds it
          resultingCondition: BRIDGE,
        }),
      ],
      { amountPaid: 0 },
    )

    expect(r.status, `a stale pontic must not be refused: ${r.raw?.slice(0, 300)}`).toBe(200)
    const saved = r.body?.value ?? r.body
    expect(
      [...(saved.acts[0].ponticToothNumbers ?? [])].sort((a: number, b: number) => a - b),
      "the orphan is dropped, the real one kept",
    ).toEqual([15])

    // …and nothing is charted on the tooth the act does not treat.
    expect(await api.toothCondition(p.id, 47)).toBeNull()
    expect(await api.toothCondition(p.id, 15)).toBe(PONTIQUE)
    expect(await api.toothCondition(p.id, 14)).toBe(PILIER)
  })

  test("FICHE-40b · a bridge is ONE act on the devis, priced once, whatever its span", async ({
    api,
    arrange,
    patient,
  }) => {
    const p = await patient("PontUnActe")
    /*
     * The reason the split had to happen inside one act rather than by entering the procedure twice: a bridge is
     * priced per element but quoted as one line, and two rows would double the devis and the fiche total.
     */
    const { saved } = await chartBridge(api, arrange, p.id, [14, 15, 16], [15])

    expect(saved.acts, "one act, not one per element").toHaveLength(1)
    expect(Number(saved.cost)).toBeCloseTo(500, 3)
  })
})
