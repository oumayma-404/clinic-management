"use client"

import { useId, type ReactNode } from "react"
import { cn } from "@/lib/utils"
import { conditionStyle } from "@/components/odontogram-conditions"
import { RECORDED_ACT_LABEL, RECORDED_ACT_LEGEND } from "@/components/odontogram-recorded-acts"
import {
  LOWER_ARCH_TRANSFORM,
  OCCLUSAL_GRID,
  TOOTH_ANATOMY,
  TOOTH_ASPECT,
  TOOTH_VIEWBOX,
  isUpperTooth,
  surfaceZone,
  surfaceZoneCode,
  toothKind,
  type ToothAnatomy,
} from "@/components/tooth-anatomy"

/**
 * The odontogram's **symbol** view — a clinical glyph per state, and two colours for the whole chart.
 *
 * ## The grammar, which is the entire point of this file
 *
 * | Channel | Answers | Vocabulary |
 * |---|---|---|
 * | **Shape** | what is it? | one symbol per `ToothCondition` — threads for an implant, an axial line for a canal, a cerne for a couronne, a cross for an extraction |
 * | **Colour** | done, or to do? | `--chart-mark-todo` (a `Diagnosis`) · `--chart-mark-done` (a `Treatment`). Nothing else. |
 * | **Fill** | in place, or only planned? | plein/hachuré = present · contour = planned |
 * | **Place** | where exactly? | the crown-and-root view carries structure; {@link OcclusalSurfaceBox} carries the faces |
 *
 * ⚠️ **This is not a re-skin of the box view and the two are not interchangeable.** The box paints one of
 * fifteen condition hues and can therefore show only the *most recent* state; a tooth that is both dévitalisée
 * and couronnée renders as a couronne and the canal is demoted to a 6 px dot. Here both are drawn, because a
 * symbol composes and a fill does not. That is why the two views are offered side by side rather than one
 * replacing the other — and why the fifteen hues survive untouched in the picker, the legend and the devis,
 * where a swatch beside a label is the right instrument.
 *
 * ## Adding a condition
 *
 * Give it an entry in {@link TOOTH_SYMBOLS}. `check:responsive`'s `tooth-symbol-covers-every-condition` compares
 * these keys with `odontogram-conditions.ts` in both directions and fails the build on drift — because the
 * failure mode otherwise is a tooth that draws *nothing at all*, which no error anywhere reports.
 */

/** One charted state, reduced to what a drawing needs. */
export interface ToothMark {
  condition: string
  /** `"Diagnosis"` — charted by hand, still to do. `"Treatment"` — written by a fiche de soins. */
  source: string
  surfaces?: string | null
  /**
   * Draw this mark in the neutral ink rather than in its status colour — « Actes réalisés »' case.
   *
   * ⚠️ There the colour channel already carries the ACT, so a state drawn in `--chart-mark-done` would
   * assert a status that chart does not sort by, and one drawn in the act's own hue would disappear into the
   * wash behind it. Shape says what the act left, colour says which act.
   */
  ink?: boolean
}

const TODO = "var(--chart-mark-todo)"
const DONE = "var(--chart-mark-done)"
/** See {@link ToothMark.ink} and the token's own note in `globals.css`. */
const INK = "var(--chart-mark-ink)"

/**
 * ⚠️ **The tooth is drawn with two gradients, and they are not decoration.**
 *
 * Flat fills read as a beige blob at 40 px. What makes the shape read as a *tooth* is that enamel is brightest
 * at the incisal edge and shades toward the collet, while dentine does the opposite and darkens toward the
 * apex — so the crown lifts off the root without a second outline between them. The first version of this
 * component flattened both and lost most of the drawing's legibility; the values live in `globals.css`.
 */
const OUTLINE = "var(--tooth-line)"

const markColor = (m: ToothMark) => (m.ink ? INK : m.source === "Diagnosis" ? TODO : DONE)
const isPlanned = (m: ToothMark) => m.source === "Diagnosis"

/** What a condition does to the tooth's own body, before any overlay is drawn. */
interface BodyEffect {
  /** Draw the body at this opacity — an absent tooth stays visible, which is what makes the cross legible. */
  fade?: number
  /** Dash the body outline: the tooth is there but not in its place. */
  dashed?: boolean
  hideCrown?: boolean
  /** The root is gone, or has been replaced by something this file draws instead. */
  hideRoots?: boolean
}

interface DrawContext {
  a: ToothAnatomy
  tooth: number
  mark: ToothMark
  color: string
  planned: boolean
  /** Unique per rendered tooth — every `clipPath` id must be, or the first one on the page wins for all 32. */
  clipId: string
}

interface ToothSymbol {
  /** Drawn over the body. Omitted when the condition is expressed by {@link ToothSymbol.body} alone. */
  draw?: (c: DrawContext) => ReactNode
  body?: BodyEffect
  /** How the legend describes the mark. Short: it sits under a 12 px swatch. */
  legend: string
}

const stroke = (width: number, extra?: Record<string, unknown>) => ({
  fill: "none",
  strokeWidth: width,
  strokeLinecap: "round" as const,
  strokeLinejoin: "round" as const,
  vectorEffect: "non-scaling-stroke" as const,
  ...extra,
})

/** The dash a *planned* mark wears, so contour-vs-plein reads even in one colour. */
const plannedDash = (planned: boolean) => (planned ? "5 4" : undefined)

export const TOOTH_SYMBOLS: Record<string, ToothSymbol> = {
  /* ── Pathologies ──────────────────────────────────────────────────────────────────────────────────────── */

  Carie: {
    legend: "Faces atteintes, en contour",
    // The lesion lives in the occlusal cell, where a face can actually be pointed at. On the crown it would be
    // a blob whose position means nothing: the facial view has no mesial and no distal.
    draw: ({ a, color }) => (
      <path d={cariousPatch(a)} {...stroke(2.4, { stroke: color, strokeDasharray: "4 3" })} />
    ),
  },

  RestaurationDefectueuse: {
    legend: "Obturation en place, cernée",
    // Two marks in one, and the order is the meaning: the restoration is THERE (done, solid) and it is
    // FAILING (to do, cerned). Drawing only the red ring would lose the fact that a filling exists.
    draw: ({ a, color }) => (
      <>
        <path d={cariousPatch(a, -5)} fill={DONE} fillOpacity={0.55} stroke={DONE} strokeWidth={1.4} vectorEffect="non-scaling-stroke" />
        <path d={cariousPatch(a, 4)} {...stroke(2.4, { stroke: color, strokeDasharray: "4 3" })} />
      </>
    ),
  },

  Fracture: {
    legend: "Trait brisé sur la couronne",
    draw: ({ color }) => (
      <path d="M34 92 L46 108 L38 118 L52 132 L46 146" {...stroke(2.8, { stroke: color })} />
    ),
  },

  LesionPeriapicale: {
    legend: "Halo à l'apex de la racine",
    draw: ({ a, color }) => (
      <>
        {a.apex.map(([x, y], i) => (
          <ellipse
            key={i}
            cx={x}
            cy={y - 1}
            rx={14}
            ry={12}
            fill={color}
            fillOpacity={0.16}
            stroke={color}
            strokeWidth={2.4}
            vectorEffect="non-scaling-stroke"
          />
        ))}
      </>
    ),
  },

  RacineResiduelle: {
    legend: "Racine seule, couronne arasée",
    body: { hideCrown: true },
    // The break line replaces the crown, so the tooth reads as arasée rather than merely undrawn.
    draw: ({ a, color }) => (
      <path
        d={`M${a.span[0] - 1} ${a.neck} L${a.span[0] + 7} ${a.neck - 6} L${a.span[0] + 15} ${a.neck + 1} L50 ${a.neck - 6} L${a.span[1] - 9} ${a.neck + 1} L${a.span[1] + 1} ${a.neck - 5}`}
        {...stroke(2.6, { stroke: color })}
      />
    ),
  },

  MaladieParodontale: {
    legend: "Niveau osseux abaissé",
    // The line says where the bone is now; the two arrows say which way it went. Without them a horizontal
    // rule across a root reads as a measurement, not a loss.
    draw: ({ a, color }) => {
      const [x1, x2] = a.span
      return (
        <>
          <path d={`M${x1 - 7} 62 L${x2 + 7} 62`} {...stroke(2.6, { stroke: color })} />
          {[x1 - 1, x2 + 1].map((x) => (
            <path key={x} d={`M${x} 44 L${x} 60 M${x - 3.5} 55 L${x} 60 L${x + 3.5} 55`} {...stroke(2, { stroke: color })} />
          ))}
        </>
      )
    },
  },

  ATraiter: {
    legend: "Astérisque au collet",
    // Deliberately a non-specific mark: « à traiter » is the one condition that names no pathology, so giving
    // it an anatomical symbol would claim more than the record says.
    draw: ({ color }) => (
      <>
        {[0, 60, 120].map((deg) => {
          const r = (deg * Math.PI) / 180
          const dx = Math.cos(r) * 10
          const dy = Math.sin(r) * 10
          return <path key={deg} d={`M${79 - dx} ${100 - dy} L${79 + dx} ${100 + dy}`} {...stroke(2.6, { stroke: color })} />
        })}
      </>
    ),
  },

  /* ── Constats ─────────────────────────────────────────────────────────────────────────────────────────── */

  DentIncluse: {
    legend: "Dent en pointillés sous la crête",
    body: { fade: 0.55, dashed: true },
  },

  ExtraitAbsent: {
    legend: "Dent barrée d'une croix",
    // ⚠️ The tooth is faded, never omitted. A blank cell is indistinguishable from a tooth nobody has charted,
    // and « absente » is a finding — it has to be *stated*, not left as a gap.
    body: { fade: 0.3 },
    draw: () => <path d="M18 16 L82 150 M82 16 L18 150" {...stroke(2.8, { stroke: "var(--muted-foreground)" })} />,
  },

  /* ── Déjà traité ──────────────────────────────────────────────────────────────────────────────────────── */

  Obturation: {
    legend: "Faces atteintes, en plein",
    draw: ({ a, color }) => (
      <path d={cariousPatch(a)} fill={color} stroke={color} strokeWidth={1.4} vectorEffect="non-scaling-stroke" />
    ),
  },

  TraitementDeCanal: {
    legend: "Trait axial dans chaque racine",
    draw: ({ a, color, planned }) => (
      <>
        {a.canals.map(([x1, y1, x2, y2], i) => (
          <path key={i} d={`M${x1} ${y1} L${x2} ${y2}`} {...stroke(3.6, { stroke: color, strokeDasharray: plannedDash(planned) })} />
        ))}
      </>
    ),
  },

  Couronne: {
    legend: "Couronne hachurée + limite cervicale",
    draw: (c) => <CrownCap {...c} />,
  },

  Bridge: {
    legend: "Couronnes reliées par une travée",
    /*
     * ⚠️ **The travée bar is drawn by {@link ToothSymbolGlyph}, not here**, and it needs a fact this function
     * cannot have: whether the *neighbouring* tooth is part of the same bridge. `ToothArchLayout` deliberately
     * takes no per-tooth state (its own doc-comment records why), so the continuity is computed once in
     * `odontogram.tsx` — which already holds every tooth — and threaded down as `bridgeSpan`. Without the bar a
     * three-unit bridge reads as three separate crowns, which is the one thing a bridge is not.
     */
    draw: (c) => <CrownCap {...c} />,
  },

  /*
   * ⚠️ **These two are the whole reason the chart may stop guessing.** A pilier is a prepared living tooth and
   * keeps its root; a pontique is suspended over a gap and has none — so the difference the drawing has to
   * carry is exactly the one the record now states, rather than one inferred from a neighbour. Anything the
   * practitioner charts as the older, unspecified `Bridge` is still drawn as a plain crowned unit.
   */
  BridgePilier: {
    legend: "Pilier — couronne sur une dent qui garde sa racine",
    draw: (c) => <CrownCap {...c} />,
  },

  BridgePontique: {
    legend: "Pontique — couronne suspendue, sans racine",
    body: { hideRoots: true },
    draw: (c) => <CrownCap {...c} />,
  },

  Implant: {
    legend: "Fût fileté à la place de la racine",
    body: { hideRoots: true },
    draw: (c) => <ImplantFixture {...c} />,
  },

  /*
   * ⚠️ **The one bridge unit that is not drawn like the others, because it is not built like them.** A pilier
   * is a prepared tooth that keeps its root; an implant pilier has a threaded fixture in bone and no root at
   * all. Charted as a plain `BridgePilier` it drew a rooted tooth over an implant — and « does this abutment
   * have a root? » is the question a dentist answers off this chart before touching it.
   *
   * So it is exactly what it is: the {@link ImplantFixture} in place of the root, plus the {@link CrownCap}
   * that makes it a bridge retainer, and the travée joins it like any other unit. `hideRoots` for the same
   * reason `Implant` sets it.
   */
  BridgePilierImplant: {
    legend: "Pilier sur implant — couronne sur un fût, sans racine",
    body: { hideRoots: true },
    draw: (c) => (
      <>
        <ImplantFixture {...c} />
        <CrownCap {...c} />
      </>
    ),
  },
}

/**
 * The threaded fixture an implant puts where the root was.
 *
 * ⚠️ **Extracted, not copied.** `Implant` and `BridgePilierImplant` are the same object under two different
 * prostheses, and this repo's dominant defect is a correct drawing wired to one of its call sites. Two copies
 * of seven thread strokes and a platform rect would drift the first time either was nudged, and the symptom
 * would be two teeth on one chart claiming different hardware.
 */
function ImplantFixture({ color, planned }: DrawContext) {
  return (
    <>
      <path
        d="M40 86 L60 86 L56 26 C55.4 18 44.6 18 44 26 Z"
        fill={color}
        fillOpacity={planned ? 0 : 0.16}
        stroke={color}
        strokeWidth={2}
        strokeLinejoin="round"
        vectorEffect="non-scaling-stroke"
      />
      {Array.from({ length: 7 }, (_, i) => {
        const y = 30 + i * 8
        const hw = 7.2 + ((y - 22) / 64) * 3.2
        return <path key={i} d={`M${50 - hw} ${y - 2.4} L${50 + hw} ${y + 2.4}`} {...stroke(1.8, { stroke: color })} />
      })}
      <rect
        x={42}
        y={86}
        width={16}
        height={7}
        rx={1.5}
        fill={planned ? "none" : color}
        stroke={color}
        strokeWidth={2}
        vectorEffect="non-scaling-stroke"
      />
    </>
  )
}

/**
 * A crown restoration: hachurée when it is in place, cernée when it is only planned — the two of Dentrix
 * Ascend's three fill styles that this chart needs (the third, plein, would hide the tooth under it).
 */
function CrownCap({ a, color, planned, clipId }: DrawContext) {
  return (
    <>
      {!planned && (
        <g clipPath={`url(#${clipId})`}>
          {Array.from({ length: 25 }, (_, i) => {
            const x = -60 + i * 9
            return <path key={i} d={`M${x} 160 L${x + 80} 80`} stroke={color} strokeWidth={1.5} opacity={0.55} vectorEffect="non-scaling-stroke" />
          })}
        </g>
      )}
      <path d={a.crown} {...stroke(2.8, { stroke: color, strokeDasharray: plannedDash(planned) })} />
      <path d={`M${a.span[0] - 2} ${a.neck + 2} L${a.span[1] + 2} ${a.neck + 2}`} {...stroke(2.6, { stroke: color })} />
    </>
  )
}

/**
 * The patch a carie or an obturation wears **on the facial view**.
 *
 * It is deliberately a plain rounded box in the middle of the crown and deliberately says nothing about which
 * face: the facial view has no mesial and no distal to point at. The faces are answered by
 * {@link OcclusalSurfaceBox}, which is the only place in this chart entitled to make that claim.
 */
function cariousPatch(a: ToothAnatomy, grow = 0): string {
  const x1 = 38 - grow
  const x2 = 62 + grow
  const y1 = a.neck + 12 - grow
  const y2 = a.neck + 36 + grow
  return `M${x1} ${y1} L${x2} ${y1} L${x2} ${y2} L${x1} ${y2} Z`
}

/** Conditions that carry no drawing at all. `Sain` is the absence of a state, never a stored row. */
export const UNDRAWN_CONDITIONS = ["Sain"] as const

/**
 * Whether this tooth's bridge continues into the cell drawn beside it, on each side **as the arch is laid out**
 * (not mesial/distal — the bar is a picture of adjacency on screen).
 */
export interface BridgeSpan {
  toPrevious: boolean
  toNext: boolean
  /** The whole run's status — one abutment still to place makes the travée a plan. */
  planned?: boolean
  /**
   * « Bridge 14 → 16 · 3 éléments » — the run this tooth belongs to, named, for the cell's hover text.
   *
   * ⚠️ The element count is the GROUP's tooth count, never the number of cells the bar crosses. A bridge
   * crosses un-charted pontic sites, and in the **mixte** arch it also crosses deciduous cells (`…16, 55, 15,
   * 54, 14…`), so a 3-unit bridge spans five cells there and « 5 éléments » would be wrong on the same
   * bridge in a different view.
   */
  runLabel?: string
}

interface ToothSymbolGlyphProps {
  toothNumber: number
  marks: ToothMark[]
  /** Rendered width in px; height follows {@link TOOTH_ASPECT}. */
  width?: number
  /** Set when the neighbouring cell carries the same bridge — see {@link BridgeSpan}. */
  bridgeSpan?: BridgeSpan
  /**
   * This tooth carries recorded work that charted no state — see `odontogram-recorded-acts.ts`.
   *
   * ⚠️ **A prop of its own, never an entry in {@link TOOTH_SYMBOLS}.** That map is compared with
   * `CONDITION_ORDER` in both directions by `check:responsive`, so a key that is not a `ToothCondition` fails
   * the build — correctly: this is a different vocabulary, and the mark says « something was done here » and
   * deliberately nothing about the tooth.
   */
  hasRecordedAct?: boolean
  /**
   * An act's catalogue colour, washed over the tooth — « Actes réalisés » in this drawing.
   *
   * ⚠️ **It mixes the four gradient stops; it does NOT replace them.** Flattening both stops to one flat hue
   * throws away the enamel/dentine falloff, which is the only thing making the shape read as a *tooth* rather
   * than a coloured silhouette — the token block in `globals.css` says so beside the literals, and it is what
   * the first pass of this got wrong. The strength is `--act-tint` (22 % light, 36 % dark), the same token the
   * agenda's blocks are painted with, so « how much of an act's colour survives into a wide surface » stays
   * one answer.
   */
  tint?: string | null
  /**
   * One full-strength band per act, at the collet, apical to the cervical line.
   *
   * ⚠️ **Full strength, and that is the documented companion of the wash above**: `--act-tint`'s own note
   * records that a small mark must keep the whole hue, because at 22 % a 4 px band is simply not there. Two
   * acts on one tooth stack rather than merging, so they stay countable.
   *
   * ⚠️ Drawn apical to the cervix rather than across it — a couronne's margin line sits at `neck + 2` and a
   * bridge's travée at `neck + 6`, so a band straddling the cervix would be crossed by both.
   */
  bands?: string[]
  className?: string
}

/**
 * One tooth, drawn from the side, with every one of its states on it.
 *
 * Purely presentational and deliberately carries **no selection chrome** — the ring, the « traitement en cours »
 * outline and the multi-select tick all live on the wrapping element, exactly as they do for the box view, so
 * the read-only callers can reuse this untouched. (Same contract `record-tooth-chart` keeps, and for the same
 * reason.)
 */
export function ToothSymbolGlyph({
  toothNumber,
  marks,
  width = 40,
  bridgeSpan,
  hasRecordedAct,
  tint,
  bands,
  className,
}: ToothSymbolGlyphProps) {
  const reactId = useId()
  const clipId = `crown-${reactId.replace(/[^a-zA-Z0-9-]/g, "")}`
  const a = TOOTH_ANATOMY[toothKind(toothNumber)]
  const upper = isUpperTooth(toothNumber)

  const symbols = marks
    .map((mark) => ({ mark, symbol: TOOTH_SYMBOLS[mark.condition] }))
    .filter((s): s is { mark: ToothMark; symbol: ToothSymbol } => Boolean(s.symbol))

  /*
   * ⚠️ **A tooth in the middle of a bridge is NOT necessarily the travée, and this component must not guess.**
   *
   * The first version derived it — « same bridge on both sides ⇒ pontique ⇒ draw no root » — and that is
   * medically false in the two configurations a prosthodontist meets most after the textbook one:
   *
   *   · a **pilier intermédiaire** (pier abutment) is « a natural tooth located between two edentulous spaces
   *     and terminal abutments »: 16 and 12 terminal, **14 an abutment**, 15 and 13 pontics. The derivation
   *     draws a living, prepared, rooted abutment as a crown floating in space.
   *   · a **cantilever** has « abutment or abutments at one end only, the other end of the pontic remaining
   *     unattached », so its pontic is not between anything at all.
   *
   * The root cause is that `ToothCondition.Bridge` is ONE flag: the charting convention has distinct marks for
   * the **retainer** and the **pontic** (BR / BP) and this model cannot hold the difference, so nothing here is
   * entitled to invent it. Each unit is now drawn as whatever it is actually charted as — an absent tooth keeps
   * its cross, an uncharted one stays healthy — and the bar is the only mark the bridge itself contributes.
   * Understating a bridge is recoverable; asserting the wrong tooth is a pontic is not.
   */

  // Body effects accumulate: a tooth can be both incluse and absent, and the stronger fade should win.
  const body = symbols.reduce<BodyEffect>((acc, { symbol }) => {
    const b = symbol.body
    if (!b) return acc
    return {
      fade: Math.min(acc.fade ?? 1, b.fade ?? 1),
      dashed: acc.dashed || b.dashed,
      hideCrown: acc.hideCrown || b.hideCrown,
      hideRoots: acc.hideRoots || b.hideRoots,
    }
  }, {})

  /**
   * One gradient stop, washed with the act's colour when there is one.
   *
   * ⚠️ **Mixed toward the stop, never in place of it** — see {@link ToothSymbolGlyphProps.tint}. `--act-tint`
   * is the strength, so this and the agenda's blocks cannot answer « how much of an act's colour » differently.
   */
  const washed = (token: string) =>
    tint ? `color-mix(in oklab, ${tint} var(--act-tint), var(${token}))` : `var(${token})`

  const bodyStroke = {
    stroke: OUTLINE,
    strokeWidth: 1.5,
    strokeDasharray: body.dashed ? "4 3" : undefined,
    vectorEffect: "non-scaling-stroke" as const,
    opacity: body.fade ?? 1,
  }

  return (
    <svg
      viewBox={TOOTH_VIEWBOX}
      width={width}
      height={Math.round(width * TOOTH_ASPECT)}
      aria-hidden="true"
      focusable="false"
      className={cn("overflow-visible", className)}
    >
      <defs>
        <clipPath id={clipId}>
          <path d={a.crown} />
        </clipPath>
        {/* Bottom-to-top on the crown, top-to-bottom on the root: the two run opposite ways on purpose. */}
        <linearGradient id={`${clipId}-en`} x1="0" y1="1" x2="0" y2="0">
          <stop offset="0%" stopColor={washed("--tooth-enamel-edge")} />
          <stop offset="100%" stopColor={washed("--tooth-enamel-neck")} />
        </linearGradient>
        <linearGradient id={`${clipId}-dn`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor={washed("--tooth-dentine-apex")} />
          <stop offset="100%" stopColor={washed("--tooth-dentine-neck")} />
        </linearGradient>
      </defs>
      {/* One flip for the lower arch — see the note in `tooth-anatomy.ts` on why nothing here may be text. */}
      <g transform={upper ? undefined : LOWER_ARCH_TRANSFORM}>
        {/*
          The travée, drawn FIRST so the crowns sit on top of it and the bar reads as passing behind them.
          It runs past the viewBox on purpose — the svg is `overflow-visible` — because the join has to cross
          the gap between two cells, which is 2 px on a mouse and ~12 px once `coarse:min-w-11` widens the
          buttons. Half a cell each way meets its neighbour's half whatever that gap turns out to be.
        */}
        {bridgeSpan && (bridgeSpan.toPrevious || bridgeSpan.toNext) && (
          <>
          {/*
            ⚠️ **The END STOPS, and they are what answers « where does this bridge begin and end? ».**
            Two bridges placed side by side used to be one continuous bar — the extent was inferred from arch
            adjacency, so 14·15·16 and 17·18 read as one five-unit span. `bridge-runs.ts` now separates them,
            but separation alone leaves two bars meeting at a cell boundary, which reads as a rendering seam
            rather than as a deliberate boundary. A short tick across the bar at each terminal says the run
            stops HERE, on purpose.

            Drawn per side and only where the run does not continue, so a 3-unit bridge gets exactly two.
          */}
          {!bridgeSpan.toPrevious && (
            <path
              d={`M50 ${a.neck + 1} L50 ${a.neck + 11}`}
              stroke={bridgeSpan.planned ? TODO : DONE}
              strokeDasharray={plannedDash(Boolean(bridgeSpan.planned))}
              strokeWidth={3}
              strokeLinecap="butt"
              vectorEffect="non-scaling-stroke"
            />
          )}
          {!bridgeSpan.toNext && (
            <path
              d={`M50 ${a.neck + 1} L50 ${a.neck + 11}`}
              stroke={bridgeSpan.planned ? TODO : DONE}
              strokeDasharray={plannedDash(Boolean(bridgeSpan.planned))}
              strokeWidth={3}
              strokeLinecap="butt"
              vectorEffect="non-scaling-stroke"
            />
          )}
          <path
            d={`M${bridgeSpan.toPrevious ? -55 : 50} ${a.neck + 6} L${bridgeSpan.toNext ? 155 : 50} ${a.neck + 6}`}
            /*
             * ⚠️ The bar takes the RUN's status, not this cell's. Hard-coding « réalisé » drew a solid blue
             * travée between two red, still-to-place abutments — a bridge asserted as present in a mouth that
             * does not have one yet, which is the single worst thing this chart could say. Caught by looking.
             */
            stroke={bridgeSpan.planned ? TODO : DONE}
            strokeDasharray={plannedDash(Boolean(bridgeSpan.planned))}
            strokeWidth={3}
            strokeLinecap="butt"
            vectorEffect="non-scaling-stroke"
          />
          </>
        )}
        {!body.hideRoots && a.roots.map((d, i) => <path key={i} d={d} fill={`url(#${clipId}-dn)`} {...bodyStroke} />)}
        {!body.hideCrown && <path d={a.crown} fill={`url(#${clipId}-en)`} {...bodyStroke} />}
        {/*
          The act bands — see {@link ToothSymbolGlyphProps.bands}. Apical to the cervix, so a couronne's margin
          (`neck + 2`) and a bridge's travée (`neck + 6`) never cross them, and drawn BEFORE the symbols so a
          glyph is never hidden under one.
        */}
        {(bands ?? []).map((colour, i) => (
          <rect
            key={`${colour}-${i}`}
            x={a.span[0] - 2}
            y={a.neck - 7 - i * 6}
            width={a.span[1] - a.span[0] + 4}
            height={4.5}
            rx={2.2}
            fill={colour}
          />
        ))}
        {symbols.map(({ mark, symbol }, i) =>
          symbol.draw ? (
            <g key={`${mark.condition}-${i}`}>
              {symbol.draw({ a, tooth: toothNumber, mark, color: markColor(mark), planned: isPlanned(mark), clipId })}
            </g>
          ) : null,
        )}
        {/*
          « Un acte a été réalisé ici » — a plain filled point at the collet, in the « réalisé » colour.

          ⚠️ **It claims no anatomy, and that is the whole design.** The act it stands for produced no
          `ResultingCondition`, so the record says work happened and says nothing about what the tooth now
          carries; a symbol borrowed from the clinical set would assert more than the fiche does. A point is
          the one mark in this vocabulary that means « recorded » and nothing else.

          ⚠️ Mirrored across from `ATraiter`'s asterisk (x 79 → 21) rather than sharing its position: the two
          are the vocabulary's only non-specific marks, they can appear on the same tooth, and colour alone
          (rouge / bleu) is not a difference that survives a greyscale print or a colour-blind reader.

          Drawn after the condition symbols so it is never hidden under a crown's hatching.

          ⚠️ **It carries a `vectorEffect="non-scaling-stroke"` stroke, and that is not decoration on a filled
          shape — it is the only reason the mark survives the legend.** Every other glyph here is made of
          strokes, which stay 2.6 CSS px at any scale, so they read at the 14 px the key draws them at; a pure
          `fill` scales with the viewBox, and at 14 px an `r={7}` disc is a **1 px** dot. Verified by looking:
          the first version was invisible in the legend while being perfectly legible on the 40 px chart, so the
          key taught a mark the reader could not recognise.
        */}
        {hasRecordedAct && (
          <circle
            cx={21}
            cy={100}
            r={9}
            fill={DONE}
            stroke={DONE}
            strokeWidth={3}
            vectorEffect="non-scaling-stroke"
          />
        )}
      </g>
    </svg>
  )
}

/** `OCCLUSAL_ZONES`' point list as a CSS `clip-path`, so the picker and the chart cannot draw different faces. */
function zoneClipPath(points: string): string {
  const pairs = points.split(" ").map((p) => {
    const [x, y] = p.split(",")
    return `${x}% ${y}%`
  })
  return `polygon(${pairs.join(", ")})`
}

interface OcclusalSurfacePickerProps {
  /** Which tooth, so mésial is drawn on the side it is really on. */
  toothNumber: number
  selected: Set<string>
  onToggle: (code: string) => void
  disabled?: boolean
  className?: string
}

/**
 * **Choosing the faces by pointing at them.**
 *
 * <p>The five faces were five buttons labelled `M O D V L`, with the French word only in a native `title` — so
 * on the one control whose subject is *where on the tooth*, the dentist read a letter and did the geometry in
 * their head, on every diagnosis. Worse on a phone, where a `title` needs a hover there is no pointer for.</p>
 *
 * ⚠️ **Real `<button>`s clipped to the zone shapes, never `<polygon>`s with a `role`.** Each face is an
 * absolutely-positioned button covering the whole square and `clip-path`ped to its own trapezoid; the clip
 * removes hit-testing outside it too, so the five never overlap. That keeps the focus ring, `aria-pressed` and a
 * real French `aria-label` — all of which a shape in an SVG would have had to reimplement badly.
 *
 * ⚠️ **The geometry comes from `OCCLUSAL_ZONES` through {@link zoneClipPath}**, the same constant the chart
 * paints, and the mésial/distal swap goes through the same `surfaceZone`. A picker that drew its own box would
 * be a second answer to « which side is mésial », and the two would disagree for exactly half the mouth.
 *
 * ⚠️ **It is offered for ONE tooth only.** The multi-tooth panel and the fiche's per-act faces keep the letter
 * buttons, because there the set applies to several teeth at once and a diagram would be asserting a geometry
 * that is only true of one of them.
 */
export function OcclusalSurfacePicker({
  toothNumber,
  selected,
  onToggle,
  disabled,
  className,
}: OcclusalSurfacePickerProps) {
  const codes = ["V", "M", "O", "D", "L"]
  return (
    <div
      role="group"
      aria-label={`Faces de la dent ${toothNumber}`}
      className={cn("relative size-[148px] shrink-0 select-none", className)}
    >
      {codes.map((code) => {
        const points = surfaceZone(toothNumber, code)
        if (!points) return null
        const on = selected.has(code)
        return (
          <button
            key={code}
            type="button"
            disabled={disabled}
            aria-pressed={on}
            aria-label={`${SURFACE_NAMES[code]} — dent ${toothNumber}`}
            onClick={() => onToggle(code)}
            style={{ clipPath: zoneClipPath(points) }}
            className={cn(
              "absolute inset-0 flex items-center justify-center text-2xs font-semibold transition-colors",
              "disabled:cursor-not-allowed disabled:opacity-60",
              on
                ? "bg-primary text-primary-foreground"
                : "bg-muted/50 text-muted-foreground hover-hover:hover:bg-muted",
            )}
          >
            {/* The letter still shows: it is the notation the note, the tooltip and the fiche all print. */}
            {/* ⚠️ Keyed on the ZONE the button actually paints, never on the stored letter — the two differ on the
                  patient's right, where M and D are mirrored. Keyed on the letter, « M » was positioned at the left
                  edge while its clip-path was the RIGHT trapezoid, so the letter fell outside its own clip and was
                  not drawn: half the mouth showed V, O and L over two unlabelled hit zones. */}
            <span className={cn("pointer-events-none", ZONE_LABEL_POSITION[surfaceZoneCode(toothNumber, code)])}>
              {code}
            </span>
          </button>
        )
      })}
      {/* Hairlines over the buttons, so the five faces stay countable when four are filled. */}
      <svg viewBox="0 0 100 100" className="pointer-events-none absolute inset-0 size-full" aria-hidden="true">
        {OCCLUSAL_GRID.map(([x1, y1, x2, y2], i) => (
          <line key={i} x1={x1} y1={y1} x2={x2} y2={y2} stroke="var(--card)" strokeWidth={1} vectorEffect="non-scaling-stroke" />
        ))}
      </svg>
    </div>
  )
}

/** Full French names — the picker's accessible labels, and the only place they are written for it. */
const SURFACE_NAMES: Record<string, string> = {
  M: "Mésiale",
  O: "Occlusale",
  D: "Distale",
  V: "Vestibulaire",
  L: "Linguale",
}

/**
 * Where the letter sits inside its trapezoid. A centred label lands on the clipped-away half for the four
 * outer faces, so it reads as belonging to the zone opposite the one it labels.
 */
const ZONE_LABEL_POSITION: Record<string, string> = {
  // `self-*` is align-self and `m*-auto` is the inline axis: the parent is a flex row, where `justify-self`
  // does nothing at all and would have left M and D centred on the clipped half.
  V: "self-start pt-1",
  L: "self-end pb-1",
  M: "me-auto ps-1.5",
  D: "ms-auto pe-1.5",
  O: "",
}

/**
 * The legend for the symbol view: the two status colours, the fill rule, then a glyph per condition **this
 * patient actually has**.
 *
 * ⚠️ Only the conditions on the chart, deliberately — the box view's legend lists all fifteen because a colour
 * swatch is one line, while fifteen drawn teeth would be taller than the chart they explain. The same rule the
 * existing legend already applies to « Traitement en cours »: a key for a mark the reader will never meet
 * teaches nothing.
 */
export function ToothSymbolLegend({
  conditions,
  hasRecordedActs,
  className,
}: {
  conditions: string[]
  /** This patient carries at least one « acte réalisé » mark — see `odontogram-recorded-acts.ts`. */
  hasRecordedActs?: boolean
  className?: string
}) {
  const shown = conditions.filter((c) => TOOTH_SYMBOLS[c])
  return (
    <div className={cn("flex flex-wrap items-center gap-x-4 gap-y-2 text-xs", className)}>
      {/* ⚠️ The two colours name their SOURCE, not just their status. « À faire » / « Réalisé » left a reader
          asking where the actes réalisés were — they are on this very chart, in blue, which is the one thing
          the key never said. This is the same chart for the diagnostics and for the work done. */}
      <span className="flex items-center gap-1.5">
        <span className="h-1 w-5 rounded-full" style={{ background: TODO }} />
        <span className="text-muted-foreground">À faire (diagnostic)</span>
      </span>
      <span className="flex items-center gap-1.5">
        <span className="h-1 w-5 rounded-full" style={{ background: DONE }} />
        <span className="text-muted-foreground">Réalisé (acte)</span>
      </span>
      <span className="text-muted-foreground">Contour = prévu · plein = en place</span>
      {/* Only when the patient has one — the rule this legend already applies to every condition glyph: a key
          for a mark the reader will never meet teaches nothing. */}
      {hasRecordedActs && (
        <span className="flex items-center gap-1.5" title={RECORDED_ACT_LEGEND}>
          <ToothSymbolGlyph toothNumber={16} marks={[]} hasRecordedAct width={14} />
          <span className="text-muted-foreground">{RECORDED_ACT_LABEL}</span>
        </span>
      )}
      {shown.map((c) => (
        <span key={c} className="flex items-center gap-1.5" title={TOOTH_SYMBOLS[c].legend}>
          {/* The real glyph, at legend size — never a hand-drawn stand-in, which is how a key drifts from the
              thing it explains. Tooth 16 gives every symbol a molar's two roots to sit in. */}
          <ToothSymbolGlyph toothNumber={16} marks={[{ condition: c, source: "Diagnosis" }]} width={14} />
          {/* The condition's own French label, from the one vocabulary both views share. */}
          <span className="text-muted-foreground">{conditionStyle(c).label}</span>
        </span>
      ))}
    </div>
  )
}

interface OcclusalSurfaceBoxProps {
  toothNumber: number
  marks: ToothMark[]
  size?: number
  className?: string
}

/**
 * The five faces as an occlusal cell — read-only for now.
 *
 * It exists in this slice even though the faces are not yet *editable* here, because without it « Carie MO »
 * and « Carie OD » draw the same tooth. Today that information renders as the letters `MO` crammed into a
 * 28 px box at 9 px, which is under the 11 px legibility floor the rest of the app is held to.
 */
export function OcclusalSurfaceBox({ toothNumber, marks, size = 22, className }: OcclusalSurfaceBoxProps) {
  const reactId = useId()
  const clipId = `occ-${reactId.replace(/[^a-zA-Z0-9-]/g, "")}`
  const withFaces = marks.filter((m) => m.surfaces)

  /*
   * ⚠️ An EMPTY cell recedes, and this is not decoration. Most teeth carry no surface entry, so at full
   * strength thirty-odd identical grids sat under the arch as the loudest thing in the card — the eye counted
   * boxes instead of reading teeth, and the two cells that did hold a face were the hardest to find. The cell
   * keeps its place (a row of unequal heights reads as a rendering fault) and drops its interior lines, which
   * only mean something to someone reading faces.
   */
  const empty = withFaces.length === 0

  return (
    <svg
      viewBox="0 0 100 100"
      width={size}
      height={size}
      aria-hidden="true"
      focusable="false"
      className={cn(empty && "opacity-55", className)}
    >
      <defs>
        <clipPath id={clipId}>
          <rect x={2} y={2} width={96} height={96} rx={14} />
        </clipPath>
      </defs>
      <rect x={2} y={2} width={96} height={96} rx={14} fill="var(--card)" stroke="var(--border)" strokeWidth={1.4} vectorEffect="non-scaling-stroke" />
      <g clipPath={`url(#${clipId})`}>
        {withFaces.flatMap((m, mi) =>
          [...(m.surfaces ?? "")].map((letter, li) => {
            const points = surfaceZone(toothNumber, letter)
            if (!points) return null
            const planned = isPlanned(m)
            const color = markColor(m)
            return (
              <polygon
                key={`${mi}-${li}`}
                points={points}
                fill={planned ? "none" : color}
                stroke={color}
                strokeWidth={planned ? 3 : 1}
                vectorEffect="non-scaling-stroke"
              />
            )
          }),
        )}
      </g>
      {/* Over the fill, so the five faces stay countable on a tooth where four of them are painted. */}
      {!empty && (
        <g clipPath={`url(#${clipId})`}>
          {OCCLUSAL_GRID.map(([x1, y1, x2, y2], i) => (
            <line key={i} x1={x1} y1={y1} x2={x2} y2={y2} stroke="var(--border)" strokeWidth={1} vectorEffect="non-scaling-stroke" />
          ))}
        </g>
      )}
    </svg>
  )
}
