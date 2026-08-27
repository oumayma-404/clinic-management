"use client"

import { useMemo, type ReactNode } from "react"
import { cn } from "@/lib/utils"
// The FDI quadrant layout is imported, not re-declared: `tooth-multiselect` is the single client-side
// authority for a tooth's dentition (it mirrors the backend `FdiTooth.IsAdult`), and this file used to carry
// its own copy of the same four arrays.
import { TEETH_BY_VIEW } from "@/components/tooth-multiselect"
import { ToothArchLayout, type ToothArch } from "@/components/tooth-arch-layout"
import type { DentitionView } from "@/lib/dentition"

/**
 * Which arch an FDI number belongs to. Quadrants 1/2 (permanent) and 5/6 (deciduous) are maxillary; 3/4 and 7/8
 * are mandibular. Kept here rather than in the layout because the layout deliberately knows nothing about teeth
 * beyond the four arrays it is handed.
 */
function isUpperFdi(toothNumber: number): boolean {
  const quadrant = Math.floor(toothNumber / 10)
  return quadrant === 1 || quadrant === 2 || quadrant === 5 || quadrant === 6
}

// How a tooth should paint on the chart (computed by the parent from the acts + the patient's odontogram).
export interface ToothPaint {
  /** Currently selected on the chart → strong highlight + selection ring. */
  selected: boolean
  /** Condition fill hex from this session's acts, or null (no resulting condition → neutral). */
  color: string | null
  /** Number of acts this tooth appears in this session (for the multi-indicator). */
  count: number
  /** Condition fill hex already on record for this tooth before this session (prior state), if any. */
  existingColor?: string | null
  /** True when the prior state is a charted diagnosis (« à traiter ») rather than a completed treatment. */
  existingIsDiagnosis?: boolean
}

// Neutral "selected" fill for a selected tooth with no chosen condition yet.
const SELECTED_NEUTRAL = "#cbd5e1"

type ToothKind = "incisor" | "canine" | "premolar" | "molar"

// Tooth glyph shapes reused from dental-chart.tsx (path data), normalized to a common height.
const TOOTH_SHAPES: Record<ToothKind, { w: number; h: number; vb: string; d: string }> = {
  incisor: {
    w: 18,
    h: 30,
    vb: "0 0 32 56",
    d: "M16 2C12 2 9 4 7 7C5 10 4 14 4 18C4 28 4 38 6 44C8 50 11 54 16 54C21 54 24 50 26 44C28 38 28 28 28 18C28 14 27 10 25 7C23 4 20 2 16 2Z",
  },
  canine: {
    w: 18,
    h: 30,
    vb: "0 0 36 58",
    d: "M18 2C15 2 12 3 10 5C8 7 6 10 5 14C4 18 4 24 4 28C4 36 5 44 7 48C9 52 13 56 18 56C23 56 27 52 29 48C31 44 32 36 32 28C32 24 32 18 31 14C30 10 28 7 26 5C24 3 21 2 18 2Z",
  },
  premolar: {
    w: 20,
    h: 30,
    vb: "0 0 42 60",
    d: "M21 4C17 4 13 6 11 9C9 12 7 16 6 20C5 24 4 30 4 34C4 40 5 46 7 50C9 54 13 60 21 60C29 60 33 54 35 50C37 46 38 40 38 34C38 30 37 24 36 20C35 16 33 12 31 9C29 6 25 4 21 4Z",
  },
  molar: {
    w: 22,
    h: 30,
    vb: "0 0 48 62",
    d: "M24 4C19 4 15 6 12 9C9 12 7 16 6 20C5 24 4 30 4 34C4 40 5 46 7 50C9 54 14 60 24 60C34 60 39 54 41 50C43 46 44 40 44 34C44 30 43 24 42 20C41 16 39 12 36 9C33 6 29 4 24 4Z",
  },
}

function toothKind(num: number): ToothKind {
  const last = num % 10
  if (last === 1 || last === 2) return "incisor"
  if (last === 3) return "canine"
  if (last === 4 || last === 5) return "premolar"
  return "molar"
}

function ToothGlyph({
  kind,
  fill,
  muted,
  selected,
  outline,
  dashedOutline,
}: {
  kind: ToothKind
  fill: string | null
  muted: boolean
  selected: boolean
  /** Stroke colour for the prior recorded state (drawn as the tooth outline), or null for the default. */
  outline?: string | null
  /** Dash the outline when the prior state is a charted diagnosis (not yet treated). */
  dashedOutline?: boolean
}) {
  const shape = TOOTH_SHAPES[kind]
  return (
    <svg
      width={shape.w}
      height={shape.h}
      viewBox={shape.vb}
      fill="none"
      className={cn("transition-all", muted && "opacity-60")}
    >
      {/*
        ⚠️ The condition outline is drawn WHETHER OR NOT the tooth is selected.

        It used to be `outline && !selected`, so the dashed « à traiter » ring — or the stroke recording what is
        already on the tooth — vanished at the exact moment the dentist tapped that tooth to work on it. The one
        instant the prior state matters most is the one where it disappeared, and what replaced it was a generic
        primary stroke that says nothing clinical.

        Selection is carried entirely by chrome that does not compete with the stroke: the wrapper's
        `ring-2 ring-primary ring-offset-1` (the offset is what keeps the ring legible against a retained
        coloured outline) plus a heavier stroke width here.
      */}
      <path
        d={shape.d}
        strokeWidth={selected ? (outline ? 4 : 3) : outline ? 3 : 2}
        strokeDasharray={outline && dashedOutline ? "5 4" : undefined}
        style={{
          ...(fill ? { fill } : {}),
          ...(outline ? { stroke: outline } : {}),
        }}
        className={cn(
          fill ? "" : "fill-white dark:fill-gray-100",
          // An inline `stroke` beats any class, so the class only has to answer the no-outline case.
          outline ? "" : selected ? "stroke-primary" : "stroke-gray-400",
        )}
      />
    </svg>
  )
}

interface RecordToothChartProps {
  /**
   * Which arch to draw — `adult`, `child` or `mixed`.
   *
   * ⚠️ Was `isAdult: boolean`, which is why mixed dentition could not be charted: a boolean has no third state, so
   * `TEETH_BY_VIEW` is now the one mapping and the caller decides the view (see `DentitionViewSwitch`).
   */
  view: DentitionView
  paint: Map<number, ToothPaint>
  onToggleTooth: (toothNumber: number) => void
  disabled?: boolean
  /**
   * Native hover text for a tooth, replacing the default « Dent 16 ». Newlines are honoured by browsers, so a
   * caller can list several lines.
   *
   * Deliberately the `title` attribute rather than the `ui/tooltip` primitive: this chart is rendered
   * `disabled` in read-only uses, and a disabled button fires no pointer events, so a Radix tooltip would
   * simply never open. `title` still shows.
   */
  toothTitle?: (toothNumber: number) => string
  /** Forwarded to `ToothArchLayout` — content that belongs to the card, below the arches (the fiche's legend). */
  footer?: ReactNode
}

export function RecordToothChart({
  view,
  paint,
  onToggleTooth,
  disabled,
  toothTitle,
  footer,
}: RecordToothChartProps) {
  const teeth = TEETH_BY_VIEW[view]

  const renderTooth = (num: number) => {
    const p = paint.get(num)
    const selected = p?.selected ?? false
    const worked = (p?.count ?? 0) > 0
    const fill = selected ? (p?.color ?? SELECTED_NEUTRAL) : (p?.color ?? null)
    // Worked-but-unselected teeth recede so the current selection reads first.
    const muted = worked && !selected
    return (
      <button
        key={num}
        type="button"
        disabled={disabled}
        onClick={() => onToggleTooth(num)}
        title={toothTitle?.(num) ?? `Dent ${num}`}
        /* A toggle, so it must say so and say which way it is set: selection was carried by fill colour and a
           ring alone, i.e. by nothing at all to a screen reader — on the control that decides what is charted
           and, on a per-tooth act, what is billed. */
        aria-pressed={selected}
        aria-label={toothTitle?.(num) ?? `Dent ${num}`}
        /*
         * ⚠️ `coarse:min-w-11` — the paint is WIDENED, not overlaid. This deliberately does **not** use
         * `touch-target`, which is the one place in the app where that primitive is the wrong tool.
         *
         * `touch-target` centres a 44px hit rectangle over a control without repainting it. That is exactly
         * right for a table's 32px row icons, which sit far enough apart. A painted tooth is 18–22px wide on a
         * ~24–26px pitch (`flex gap-0.5`), so a 44px overlay reaches 9–13px into each NEIGHBOUR — and both are
         * `position: relative` with `z-index: auto`, so the later sibling wins. The right third of every tooth
         * selected the tooth beside it, which charts and bills the wrong tooth. A clinical-safety defect, not a
         * comfort one, and no amount of care at the call site could have made an overlay safe at this pitch.
         *
         * Widening is affordable here precisely because the arch is a scroll box: `ToothArchLayout` wraps the
         * rows in `overflow-x-auto` over an `mx-auto w-max` block, so 16 cells at 44px simply extend the
         * scrollable region — they are never clipped (that is the `arch-clipping` invariant the check enforces).
         *
         * Gated on `coarse:` for the same reason `touch-target` is: the overlap only exists for a finger, and on
         * a mouse the arch fits a 780px dialog without scrolling today. Widening unconditionally would trade a
         * touch defect for a desktop one.
         */
        className="group flex flex-col items-center focus:outline-none disabled:cursor-not-allowed coarse:min-w-11"
      >
        {/* Movement hover gated behind `hover-hover:` (AC-11) — a tapped tooth kept the enlarged state.
            `ring-offset-1` + `ring-offset-card`: the selection ring now sits beside a RETAINED condition
            outline (see `ToothGlyph`), and without a gap the two colours touch and read as one smeared edge. */}
        <span
          className={cn(
            "relative rounded-md p-0.5 transition-all hover-hover:group-hover:scale-105",
            selected && "ring-2 ring-primary ring-offset-1 ring-offset-card",
          )}
        >
          <ToothGlyph
            kind={toothKind(num)}
            fill={fill}
            muted={muted}
            selected={selected}
            outline={p?.existingColor ?? null}
            dashedOutline={p?.existingIsDiagnosis}
          />
          {/* h-4, not h-3.5: the count rose from 8px to the 11px legibility floor (AC-2) and a 14px circle
              clips a two-digit count. `leading-none` keeps it optically centred at the larger size. */}
          {p && p.count > 1 && (
            <span className="absolute -right-1 -top-1 flex h-4 w-4 items-center justify-center rounded-full bg-primary text-2xs font-semibold leading-none text-primary-foreground">
              {p.count}
            </span>
          )}
        </span>
        <span className={cn("mt-0.5 text-2xs font-medium", selected ? "text-primary" : "text-muted-foreground")}>
          {num}
        </span>
      </button>
    )
  }

  /**
   * Which arch the phone should open on. Below `md:` the layout shows one at a time and used to always start on
   * MAXILLAIRE, so a fiche whose work is all on lower molars cost a tap before anything could be charted.
   *
   * The live selection wins over merely-painted teeth — it is what the dentist is doing right now — and within
   * each kind the lowest FDI number decides, because a `Map`'s iteration order is insertion order and would
   * otherwise make the answer depend on which effect happened to populate `paint` first.
   */
  const dataArch = useMemo<ToothArch | undefined>(() => {
    let selected: number | undefined
    let painted: number | undefined
    for (const [tooth, p] of paint) {
      if (p.selected) selected = selected === undefined ? tooth : Math.min(selected, tooth)
      else if (p.count > 0 || p.existingColor) painted = painted === undefined ? tooth : Math.min(painted, tooth)
    }
    const lead = selected ?? painted
    return lead === undefined ? undefined : isUpperFdi(lead) ? "upper" : "lower"
  }, [paint])

  // The geometry (scroll box, rows, midline, labels, the below-`md:` arch switch) lives in `ToothArchLayout`.
  // Everything above — paint, selection, `disabled`, the native `title` — stays here, which is exactly the
  // contract that lets the read-only summary reuse this chart. See the layout's own note.
  return <ToothArchLayout teeth={teeth} renderTooth={renderTooth} defaultArch={dataArch} footer={footer} />
}
