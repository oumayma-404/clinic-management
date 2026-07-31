"use client"

import { cn } from "@/lib/utils"
// The FDI quadrant layout is imported, not re-declared: `tooth-multiselect` is the single client-side
// authority for a tooth's dentition (it mirrors the backend `FdiTooth.IsAdult`), and this file used to carry
// its own copy of the same four arrays.
import { ADULT_TEETH, CHILD_TEETH } from "@/components/tooth-multiselect"

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
      <path
        d={shape.d}
        strokeWidth={outline ? 3 : 2}
        strokeDasharray={outline && dashedOutline ? "5 4" : undefined}
        style={{
          ...(fill ? { fill } : {}),
          ...(outline && !selected ? { stroke: outline } : {}),
        }}
        className={cn(
          fill ? "" : "fill-white dark:fill-gray-100",
          selected ? "stroke-primary" : outline ? "" : "stroke-gray-400",
        )}
      />
    </svg>
  )
}

interface RecordToothChartProps {
  isAdult: boolean
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
}

export function RecordToothChart({ isAdult, paint, onToggleTooth, disabled, toothTitle }: RecordToothChartProps) {
  const teeth = isAdult ? ADULT_TEETH : CHILD_TEETH

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
        className="group flex flex-col items-center focus:outline-none disabled:cursor-not-allowed"
      >
        {/* Movement hover gated behind `hover-hover:` (AC-11) — a tapped tooth kept the enlarged state. */}
        <span className={cn("relative rounded-md p-0.5 transition-all hover-hover:group-hover:scale-105", selected && "ring-2 ring-primary")}>
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

  return (
    <div className="overflow-x-auto rounded-lg border border-border bg-card p-3">
      {/*
        AC-32 — `w-max mx-auto`, never `justify-center`, inside a scroll container.

        ⚠️ This is not a phone-only nicety. `justify-content: center` distributes overflow to **both** sides,
        and the inline-start overflow is **not in the scrollable region** — so at 390px teeth 18–15 and 48–45
        were unreachable by any means: not by scrolling, not by dragging, not at all. `w-max` sizes this block
        to its content and `mx-auto` still centres it while there is room; once the content is wider than the
        container the auto margins collapse to zero (they cannot go negative), so the arch starts at the
        scroll origin and every tooth is reachable.

        One wrapper rather than one per row, so the « Maxillaire »/« Mandibule » labels and the midline rule
        share the arch's width instead of centring against the viewport and drifting off it when scrolled.
      */}
      <div className="mx-auto w-max">
        {/* Upper jaw */}
        <div className="space-y-1.5">
          <div className="text-center text-2xs font-medium text-muted-foreground">Maxillaire (haut)</div>
          <div className="flex gap-2">
            <div className="flex gap-0.5">{teeth.upperRight.map(renderTooth)}</div>
            <div className="w-px bg-border" />
            <div className="flex gap-0.5">{teeth.upperLeft.map(renderTooth)}</div>
          </div>
        </div>

        <div className="my-2 border-t border-border" />

        {/* Lower jaw */}
        <div className="space-y-1.5">
          <div className="flex gap-2">
            <div className="flex gap-0.5">{teeth.lowerRight.map(renderTooth)}</div>
            <div className="w-px bg-border" />
            <div className="flex gap-0.5">{teeth.lowerLeft.map(renderTooth)}</div>
          </div>
          <div className="text-center text-2xs font-medium text-muted-foreground">Mandibule (bas)</div>
        </div>
      </div>
    </div>
  )
}
