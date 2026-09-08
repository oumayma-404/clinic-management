import type { ToothStateDto } from "@/lib/api/types"
import type { ToothQuadrants } from "@/components/tooth-multiselect"
import { BRIDGE_UNIT_CONDITIONS } from "@/components/odontogram-conditions"
import type { BridgeSpan } from "@/components/tooth-symbols"

/**
 * **Which teeth are one bridge, and where that bridge begins and ends** — the one owner of that question on
 * the client.
 *
 * ## Why this is a module and not four lines in `odontogram.tsx`
 *
 * It used to be, and it was wrong in a way nothing reported. The travée was joined across bridge-marked teeth
 * by **arch adjacency**, capped at three intervening sites, so:
 *
 * - a bridge on 14·15·16 beside a bridge on 17·18 drew **one five-unit bar** — the dentist's own report;
 * - and because the run's `planned` flag was OR-ed across the merge, a **finished** crown on 16 beside a
 *   *planned* bridge on 17·18 was drawn dashed-red — asserted as **not yet placed**. A false clinical
 *   statement on the one diagram that is read at a glance, and the worse half of the defect;
 * - and it failed the other way too: a bridge charted on abutments four sites apart drew **no travée at all**.
 *
 * `BridgeCharting`'s own summary on the server says a bridge's *shape* cannot be read off position. Its
 * **extent** cannot either, and inferring it here was the same mistake one level up. So the record states it:
 * `ToothState.BridgeGroupId` is minted per bridge act (and per charting gesture for a planned one), and this
 * groups on it.
 *
 * ## Two passes, and the order is load-bearing
 *
 * 1. **Grouped.** Rows carrying a `bridgeGroupId` are grouped by it and each group's run is drawn from its
 *    first to its last tooth **in arch order**, crossing whatever lies between. That is what makes a bridge
 *    charted on abutments only (« `Bridge` on 14 and 16, nothing on 15 » — measured on this product's own
 *    data) correct with no cap at all, and a 6-unit bridge correct too.
 * 2. **Ungrouped fallback.** Every row *without* a group — which is every row written before the column
 *    existed, and every bridge recorded one tooth at a time — goes through the original adjacency scan,
 *    unchanged, so those keep rendering exactly as they always did.
 *
 * ⚠️ **Grouped teeth are EXCLUDED from the fallback's candidate list**, not merely ranked below it. Otherwise a
 * legacy `Bridge` on 17 reaches into a grouped bridge on 14·15·16 and the merge is back.
 */

/**
 * How many un-bridged sites a travée may cross before two bridges are read as one — **for ungrouped rows only**.
 *
 * ⚠️ A bridge is charted on its ABUTMENTS, and the pontic site often carries nothing at all: `Bridge` on 14 and
 * on 16 with 15 blank is real, measured data. So « join adjacent bridge teeth » finds no run and draws two
 * unrelated crowns, which is precisely what a bridge is not. Three is a 5-unit bridge, beyond anything a
 * practice places in one span.
 *
 * ⚠️ **Do not delete this with the adjacency pass it serves.** It is the only thing that keeps every bridge
 * recorded before `BridgeGroupId` existed drawing its travée — and it is also, unavoidably, why two *legacy*
 * bridges side by side still merge. That is not fixable retroactively: the information was never recorded.
 */
const MAX_PONTIC_SITES = 3

/** The bridge entry that speaks for a tooth, or undefined. */
type BridgeMark = { condition: string; source: string; groupId: string | null; date: number }

const isBridgeCondition = (condition: string) => BRIDGE_UNIT_CONDITIONS.includes(condition)

/**
 * Build the per-tooth travée map the glyph reads.
 *
 * @param teeth the arch as laid out, so adjacency is what the eye sees. ⚠️ Read off the **layout**, never off
 *   the FDI number: the upper arch runs 18…11 then 21…28, so 11 and 21 are neighbours on screen while their
 *   numbers are ten apart — and a bridge across the midline is an ordinary anterior bridge, not an edge case.
 * @param byTooth every recorded state per tooth, newest first (the odontogramme's own read).
 */
export function buildBridgeRuns(
  teeth: ToothQuadrants,
  byTooth: Map<number, ToothStateDto[]>,
): Map<number, BridgeSpan> {
  /*
   * ⚠️ **One bridge mark per tooth, and it is the NEWEST one.** A tooth accumulates states over time, so 16 can
   * carry a `Bridge` from 2024 and a `BridgePilier` from 2026. Grouping the *entries* would put that tooth in
   * two groups, both runs would claim it, and the merge below would join them — the original defect, back on
   * any bridge that was ever redone. Resolving to one mark first makes a tooth belong to exactly one group.
   */
  const markOf = new Map<number, BridgeMark>()
  for (const [tooth, entries] of byTooth) {
    const entry = entries.find((e) => isBridgeCondition(e.condition))
    if (!entry) continue
    markOf.set(tooth, {
      condition: entry.condition,
      source: entry.source,
      groupId: entry.bridgeGroupId ?? null,
      date: new Date(entry.treatmentDate).getTime(),
    })
  }

  const spans = new Map<number, BridgeSpan>()
  if (markOf.size === 0) return spans

  const arches = [
    [...teeth.upperRight, ...teeth.upperLeft],
    [...teeth.lowerRight, ...teeth.lowerLeft],
  ]

  /*
   * A cell can be *contested* by two runs — normally only a blank pontic site one run crosses, but two
   * interleaved groups are possible on contradictory data — and then the newest run owns the cell whole.
   *
   * ⚠️ **Never `planned: a.planned || b.planned`.** That OR is precisely what drew a FINISHED crown as
   * still-to-place: it let one bridge's « à poser » status leak along the bar into the cells of the bridge
   * beside it. A cell takes one run's status, entire. Each run below is emitted over its cells exactly once,
   * so there is no within-run merging left for this to get wrong.
   */
  const claimedAt = new Map<number, number>()
  const claim = (tooth: number, span: BridgeSpan, at: number) => {
    const held = claimedAt.get(tooth)
    if (held !== undefined && held > at) return
    claimedAt.set(tooth, at)
    spans.set(tooth, span)
  }

  for (const arch of arches) {
    const index = new Map(arch.map((t, i) => [t, i]))
    const positionOf = (tooth: number) => index.get(tooth)!

    /** Emit one run over the cells from `from` to `to` inclusive. */
    const emit = (run: number[], planned: boolean, at: number) => {
      const ordered = [...run].sort((a, b) => positionOf(a) - positionOf(b))
      const from = positionOf(ordered[0])
      const to = positionOf(ordered[ordered.length - 1])
      const runLabel =
        `Bridge ${ordered[0]} → ${ordered[ordered.length - 1]} · ${run.length} élément` +
        (run.length > 1 ? 's' : '')
      for (let i = from; i <= to; i++) {
        claim(arch[i], { toPrevious: i > from, toNext: i < to, planned, runLabel }, at)
      }
    }

    // ── pass 1: the groups, each stating its own extent ───────────────────────────────
    const groups = new Map<string, number[]>()
    for (const [tooth, mark] of markOf) {
      if (!mark.groupId || !index.has(tooth)) continue
      groups.set(mark.groupId, [...(groups.get(mark.groupId) ?? []), tooth])
    }

    const grouped = new Set<number>()
    for (const groupTeeth of groups.values()) {
      /*
       * ⚠️ Clamped to THIS arch — that is what the `index.has` filter above does. A group whose teeth span
       * both arches is a mis-tap (16 and 46 in one act) and then draws two independent runs rather than one bar
       * across the mouth. A bridge cannot cross the arches and the picture must not pretend it can.
       */
      for (const t of groupTeeth) grouped.add(t)

      /*
       * ⚠️ A group of ONE draws no bar and does not fall back — it stated its extent, and its extent is one
       * tooth. That happens when a planned bridge's other units have been treated away
       * (`ClearDiagnosesForTreatedTeethAsync`), and it is the honest picture: half a bridge placed, half still
       * to come. The server never mints a group for a one-tooth ACT, for a different and more important reason
       * — see `BuildToothStates`.
       */
      if (groupTeeth.length < 2) continue

      const marks = groupTeeth.map((t) => markOf.get(t)!)
      const planned = marks.some((m) => m.source === 'Diagnosis')
      emit(groupTeeth, planned, Math.max(...marks.map((m) => m.date)))
    }

    // ── pass 2: the ungrouped remainder, by adjacency, exactly as before ───────────────────
    /*
     * ⚠️ Built as maximal **chains**, not as consecutive pairs. The original wrote each pair over the arch
     * and OR-ed `toPrevious`/`toNext` onto whatever was already there, which for a run of piers all within
     * `MAX_PONTIC_SITES` yields one continuous bar — chains are exactly equivalent and produce the same
     * rendering, but they make each legacy run ONE object with ONE status, so the contest rule in `claim` has
     * something coherent to compare. Keeping the pairs would have made a chain's own two halves contest each
     * other and the newer half would erase the older one's continuity.
     */
    const piers = arch
      .map((t, i) => ({ i, tooth: t, mark: markOf.get(t) }))
      .filter((x) => x.mark && !grouped.has(x.tooth)) as { i: number; tooth: number; mark: BridgeMark }[]

    let chain: typeof piers = []
    const flush = () => {
      if (chain.length >= 2) {
        emit(
          chain.map((c) => c.tooth),
          chain.some((c) => c.mark.source === 'Diagnosis'),
          Math.max(...chain.map((c) => c.mark.date)),
        )
      }
      chain = []
    }
    for (const pier of piers) {
      const previous = chain[chain.length - 1]
      // More than three intervening sites is not a bridge anybody places — it is the next bridge along.
      if (previous && pier.i - previous.i - 1 > MAX_PONTIC_SITES) flush()
      chain.push(pier)
    }
    flush()
  }

  return spans
}
