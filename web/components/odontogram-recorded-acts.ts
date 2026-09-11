import type { DentalRecordDto, ToothStateDto } from "@/lib/api/types"

/**
 * **Work recorded on a tooth that the odontogram's own vocabulary cannot hold.**
 *
 * <p>The chart reads `ToothStateDto[]`, and a tooth state exists only when the act that produced it has a
 * `ResultingCondition`. Two very ordinary things therefore left a treated tooth looking untouched, on BOTH
 * drawings, with no error anywhere:</p>
 *
 * <ol>
 *   <li><b>An act that charts nothing by design.</b> « Coiffage pulpaire » is seeded `ToothCondition.Sain`
 *   — the barème's own line is « à l'exclusion de l'obturation définitive » — and so are « Inlay-core »,
 *   « Couronne provisoire », « Incision d'abcès », « Greffe osseuse ». `DentalRecordActParser.BuildToothStates`
 *   skips them outright. Measured on the dev database: <b>258 of 566 recorded acts</b> name teeth and write no
 *   state at all, i.e. nearly half of all recorded work was invisible on the chart the consultation is read off.</li>
 *   <li><b>A multi-séance act still under way.</b> `ToothChartingRules` deliberately withholds the end state
 *   until the act is `Done` — rightly, since charting « Implant » from séance 1 describes a mouth the patient
 *   does not have — but the tooth was then left saying nothing at all for the length of the treatment.</li>
 * </ol>
 *
 * <p>⚠️ <b>This is NOT a condition and must never enter `CONDITION_ORDER`.</b> That list feeds the diagnosis
 * picker (a dentist would be offered « acte réalisé » as a diagnosis), the legend, and the server mirror test
 * `OdontogramConditionMirrorTests`. It is a second, much smaller vocabulary — « something was done here, and it
 * says nothing about the tooth's state » — which is exactly the claim the record supports and no more.</p>
 *
 * <p>⚠️ <b>One owner, several readers</b>, per this repo's dominant defect shape: both drawings, the tooltip,
 * both legends and the four derivations that decide which teeth are visible read from here.
 * `check:responsive`'s `recorded-act-reaches-both-drawings` fails the build if one drawing is wired and the
 * other is not — the failure mode otherwise is a tooth that carries a mark in « Symboles » and nothing in
 * « Cases », which no error reports.</p>
 */
export interface RecordedAct {
  /** The act's own name, as the fiche recorded it — « Coiffage pulpaire ». */
  name: string
  /** The fiche's `interventionDate`, ISO. */
  date: string
  /** The fiche it came from, so a reader can tell two séances of the same act apart. */
  recordId: string
}

/** How the mark is named wherever it is written — legend, tooltip, aria. */
export const RECORDED_ACT_LABEL = "Acte réalisé"

/**
 * What the legend says under the glyph. It has to be read next to « Réalisé », which means « the state this act
 * left the tooth in » — so this one names the difference rather than repeating the word.
 */
export const RECORDED_ACT_LEGEND = "Acte réalisé, sans changement d'état de la dent"

/**
 * The « Cases » fill for a tooth whose only record is an act of this kind.
 *
 * <p>⚠️ <b>Green, and deliberately not `--chart-mark-done`.</b> The symbol view's « réalisé » blue
 * (oklch 0.480 0.155 262) sits almost on top of `Obturation`'s `blue-500` in the box palette, and that
 * palette's whole job is that its hues stay tellable apart at a 12 px swatch. The green family was the one
 * left free — `RacineResiduelle` took `lime-700`, which is the olive end — and « done, nothing to treat » is
 * what green already says everywhere else in this product.</p>
 *
 * <p>⚠️ <b>`green-600` and NOT `emerald-600`, which was the first choice and measures wrong.</b> Against the
 * eighteen condition hexes, emerald-600 (#059669) lands at <b>ΔE 20.6 from `Bridge`</b> (teal-500) — inside the
 * ~20 band the « À traiter » note records as « the same colour at the size this legend draws », and it would
 * have been the second-closest pair in the whole palette. `green-600` takes the nearest neighbour to
 * <b>ΔE 27.4</b> (`RacineResiduelle`, lime-700) and sits 40.8 from `Bridge`. Measure before adding a hue here;
 * the family being « free » is not the test.</p>
 */
export const RECORDED_ACT_BOX = "bg-green-600 text-white border-green-700"
export const RECORDED_ACT_SWATCH = "bg-green-600"
/** The same green as a hex, for the dots — which paint with an inline style, never with `swatch`. */
export const RECORDED_ACT_COLOR = "#16a34a"

/** Shared empty list, so a tooth with nothing recorded does not hand its cell a new array every render. */
export const NO_RECORDED_ACTS: RecordedAct[] = []

/**
 * Which of a patient's recorded acts left no mark on the chart, per tooth, newest fiche first.
 *
 * <p>The two branches below are not one test written twice — they answer the same question with different
 * certainty, and only the second needs the tooth states at all.</p>
 *
 * <p>⚠️ <b>An act with no `resultingCondition` charts nothing, full stop</b> — `BuildToothStates` skips
 * `null`/`Sain` before doing anything else, so no lookup can add information here.</p>
 *
 * <p>⚠️ <b>An act that DOES carry one may still have charted nothing</b>, because `ToothChartingRules` clears
 * it for the odontogram while leaving it on the stored act (« The fiche keeps the condition the dentist
 * chose »). So `resultingCondition` cannot answer that half, and the honest test is the outcome: did this
 * fiche write a `Treatment` state on this tooth? Every state a fiche produces carries its `dentalRecordId`
 * (`BuildToothStates`), including every legacy row, so the join is sound.</p>
 *
 * <p>⚠️ The second branch is per (tooth, fiche) rather than per act, which under-reports in one shape: a
 * single fiche carrying two acts on one tooth, one of which charted. That tooth already wears a mark from that
 * visit, so the cost is a missing tooltip line and never a silent tooth — the failure this exists to prevent.</p>
 */
export function buildRecordedActs(
  records: DentalRecordDto[],
  byTooth: Map<number, ToothStateDto[]>,
): Map<number, RecordedAct[]> {
  const map = new Map<number, RecordedAct[]>()

  /** Did this fiche write anything onto this tooth? */
  const charted = (recordId: string, tooth: number) =>
    (byTooth.get(tooth) ?? []).some((s) => s.source === "Treatment" && s.dentalRecordId === recordId)

  for (const record of records) {
    for (const act of record.acts ?? []) {
      // `Sain` is stored as null by `DentalRecordAct`'s constructor, but a DTO from an older shape may still
      // carry the word — both mean « charts nothing », so both are tested.
      const chartsNothing = !act.resultingCondition || act.resultingCondition === "Sain"
      for (const tooth of act.toothNumbers ?? []) {
        if (!chartsNothing && charted(record.id, tooth)) continue
        const list = map.get(tooth) ?? []
        list.push({ name: act.procedureName, date: record.interventionDate, recordId: record.id })
        map.set(tooth, list)
      }
    }
  }

  for (const list of map.values()) {
    list.sort((a, b) => (a.date < b.date ? 1 : a.date > b.date ? -1 : 0))
  }
  return map
}
