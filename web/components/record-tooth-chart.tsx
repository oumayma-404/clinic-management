"use client"

import { cn } from "@/lib/utils"

// FDI quadrant layout (mirrors dental-chart.tsx / odontogram.tsx).
const ADULT_TEETH = {
  upperRight: [18, 17, 16, 15, 14, 13, 12, 11],
  upperLeft: [21, 22, 23, 24, 25, 26, 27, 28],
  lowerRight: [48, 47, 46, 45, 44, 43, 42, 41],
  lowerLeft: [31, 32, 33, 34, 35, 36, 37, 38],
}
const CHILD_TEETH = {
  upperRight: [55, 54, 53, 52, 51],
  upperLeft: [61, 62, 63, 64, 65],
  lowerRight: [85, 84, 83, 82, 81],
  lowerLeft: [71, 72, 73, 74, 75],
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
}

export function RecordToothChart({ isAdult, paint, onToggleTooth, disabled }: RecordToothChartProps) {
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
        title={`Dent ${num}`}
        className="group flex flex-col items-center focus:outline-none disabled:cursor-not-allowed"
      >
        <span className={cn("relative rounded-md p-0.5 transition-all group-hover:scale-105", selected && "ring-2 ring-primary")}>
          <ToothGlyph
            kind={toothKind(num)}
            fill={fill}
            muted={muted}
            selected={selected}
            outline={p?.existingColor ?? null}
            dashedOutline={p?.existingIsDiagnosis}
          />
          {p && p.count > 1 && (
            <span className="absolute -right-1 -top-1 flex h-3.5 w-3.5 items-center justify-center rounded-full bg-primary text-[8px] font-semibold text-primary-foreground">
              {p.count}
            </span>
          )}
        </span>
        <span className={cn("mt-0.5 text-[9px] font-medium", selected ? "text-primary" : "text-muted-foreground")}>
          {num}
        </span>
      </button>
    )
  }

  return (
    <div className="overflow-x-auto rounded-lg border border-border bg-card p-3">
      {/* Upper jaw */}
      <div className="space-y-1.5">
        <div className="text-center text-[10px] font-medium text-muted-foreground">Maxillaire (haut)</div>
        <div className="flex justify-center gap-2">
          <div className="flex gap-0.5">{teeth.upperRight.map(renderTooth)}</div>
          <div className="w-px bg-border" />
          <div className="flex gap-0.5">{teeth.upperLeft.map(renderTooth)}</div>
        </div>
      </div>

      <div className="my-2 border-t border-border" />

      {/* Lower jaw */}
      <div className="space-y-1.5">
        <div className="flex justify-center gap-2">
          <div className="flex gap-0.5">{teeth.lowerRight.map(renderTooth)}</div>
          <div className="w-px bg-border" />
          <div className="flex gap-0.5">{teeth.lowerLeft.map(renderTooth)}</div>
        </div>
        <div className="text-center text-[10px] font-medium text-muted-foreground">Mandibule (bas)</div>
      </div>
    </div>
  )
}
