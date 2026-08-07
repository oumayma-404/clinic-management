"use client"

import { useState } from "react"
import { Button } from "@/components/ui/button"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import type { DentitionView } from "@/lib/dentition"

/**
 * FDI tooth numbers **by quadrant** — the arch layout every chart draws. Adult = quadrants 1–4, child = 5–8.
 *
 * This is the one source, and the flat `ADULT_FDI`/`CHILD_FDI` below are **derived from it** rather than
 * written out a second time. Before this, the quadrant shape was copied in `odontogram.tsx` and
 * `record-tooth-chart.tsx` while the flat shape lived here — three literals that happened to agree, with
 * nothing making them agree. (`odontogram-acts-chart.tsx` was never a copy: it takes `teeth` as a prop.)
 */
export interface ToothQuadrants {
  upperRight: number[]
  upperLeft: number[]
  lowerRight: number[]
  lowerLeft: number[]
}

export const ADULT_TEETH: ToothQuadrants = {
  upperRight: [18, 17, 16, 15, 14, 13, 12, 11],
  upperLeft: [21, 22, 23, 24, 25, 26, 27, 28],
  lowerRight: [48, 47, 46, 45, 44, 43, 42, 41],
  lowerLeft: [31, 32, 33, 34, 35, 36, 37, 38],
}

export const CHILD_TEETH: ToothQuadrants = {
  upperRight: [55, 54, 53, 52, 51],
  upperLeft: [61, 62, 63, 64, 65],
  lowerRight: [85, 84, 83, 82, 81],
  lowerLeft: [71, 72, 73, 74, 75],
}

/**
 * **Mixed dentition** — both sets in one arch, each deciduous tooth immediately distal to the permanent tooth
 * that succeeds it (55 beside 15, 54 beside 14, 53 beside 13, 52 beside 12, 51 beside 11).
 *
 * ⚠️ Interleaved by position, not concatenated. Appending 55–51 after 11 would put the baby teeth *across* the
 * midline, so the chart would state an anatomy that does not exist — and on the one chart whose entire purpose is
 * to say which tooth was treated, a position that lies is worse than a position that is missing.
 *
 * A deciduous tooth and its successor never coexist in a real mouth, so a quadrant of 13 cells always has empty
 * positions; that is the point. The dentist charts whichever is actually there instead of choosing an arch that
 * excludes half the mouth. The two sets are disjoint, so **no FDI number appears twice** in any view.
 */
export const MIXED_TEETH: ToothQuadrants = {
  upperRight: [18, 17, 16, 55, 15, 54, 14, 53, 13, 52, 12, 51, 11],
  upperLeft: [21, 61, 22, 62, 23, 63, 24, 64, 25, 65, 26, 27, 28],
  lowerRight: [48, 47, 46, 85, 45, 84, 44, 83, 43, 82, 42, 81, 41],
  lowerLeft: [31, 71, 32, 72, 33, 73, 34, 74, 35, 75, 36, 37, 38],
}

/** Quadrant order is upper-right → upper-left → lower-right → lower-left, which is the order the flat lists
 *  already had; deriving them keeps that and makes a divergence impossible. */
const flatten = (q: ToothQuadrants): number[] => [...q.upperRight, ...q.upperLeft, ...q.lowerRight, ...q.lowerLeft]

export const ADULT_FDI = flatten(ADULT_TEETH)
export const CHILD_FDI = flatten(CHILD_TEETH)
export const MIXED_FDI = flatten(MIXED_TEETH)

/**
 * The quadrants a `DentitionView` draws — the one mapping, so a chart can never pair a view with the wrong arch.
 * Exhaustive `Record`, deliberately: a fourth view added without an arch is a `tsc` error, not a blank chart.
 */
export const TEETH_BY_VIEW: Record<DentitionView, ToothQuadrants> = {
  adult: ADULT_TEETH,
  child: CHILD_TEETH,
  mixed: MIXED_TEETH,
}

/** Flat FDI list for a view, in the same quadrant order. Used by « Toute la bouche » and the hidden-act count. */
export const FDI_BY_VIEW: Record<DentitionView, number[]> = {
  adult: ADULT_FDI,
  child: CHILD_FDI,
  mixed: MIXED_FDI,
}

/** The FDI quadrant *numbers* a view's upper and lower arches cover — what « Haut » / « Bas » select. */
export const ARCH_QUADRANTS_BY_VIEW: Record<DentitionView, { upper: number[]; lower: number[] }> = {
  adult: { upper: [1, 2], lower: [3, 4] },
  child: { upper: [5, 6], lower: [7, 8] },
  mixed: { upper: [1, 2, 5, 6], lower: [3, 4, 7, 8] },
}

/**
 * True for a permanent (adult) FDI tooth, false for a deciduous one — mirrors the backend `FdiTooth.IsAdult`.
 * The single client-side authority for a tooth's dentition, so a record holding both (mixed dentition) is
 * always split by the tooth itself, never by the record's `isAdultTeeth` display flag.
 */
export function isAdultTooth(toothNumber: number): boolean {
  return ADULT_FDI.includes(toothNumber)
}

interface ToothMultiSelectProps {
  value: number[]
  onChange: (teeth: number[]) => void
  disabled?: boolean
  /** Restrict the picker to one dentition (true = adult, false = child). Undefined shows both groups. */
  isAdult?: boolean
}

// A compact FDI tooth multiselect: a popover with adult and/or child toggle chips.
export function ToothMultiSelect({ value, onChange, disabled, isAdult }: ToothMultiSelectProps) {
  const [open, setOpen] = useState(false)
  const toggle = (n: number) => {
    onChange(value.includes(n) ? value.filter((t) => t !== n) : [...value, n].sort((a, b) => a - b))
  }
  const showAdult = isAdult === undefined || isAdult === true
  const showChild = isAdult === undefined || isAdult === false

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button type="button" variant="outline" size="sm" className="h-9 justify-start font-normal" disabled={disabled}>
          {value.length > 0 ? `Dents : ${value.join(", ")}` : "Dents…"}
        </Button>
      </PopoverTrigger>
      {/* Wider on a finger, because the cells inside grew — `ui/popover.tsx` already caps every popover at
          `max-w-[calc(100vw-2rem)]`, so this cannot overflow a 390px phone. `max-h`/scroll because 32 adult
          teeth at 40px plus the child arch is taller than a short viewport. */}
      <PopoverContent className="w-80 max-h-[70dvh] space-y-3 overflow-y-auto coarse:w-[22rem]" align="start">
        {showAdult && (
          <div className="space-y-1.5">
            <p className="text-xs font-medium text-muted-foreground">Adulte</p>
            <div className="flex flex-wrap gap-1">
              {ADULT_FDI.map((n) => (
                <Button
                  key={n}
                  type="button"
                  size="sm"
                  variant={value.includes(n) ? "default" : "outline"}
                  /*
                   * `coarse:size-10` grows the PAINT; the 44px overlay it already had was stealing neighbours' taps.
                   *
                   * These are 28×36px `Button`s in a `flex flex-wrap gap-1` grid of 32, so `buttonVariants`'
                   * `touch-target` overlay (44px minimum) overhangs each cell by ~8px horizontally and ~8px
                   * vertically — and the row pitch is only 32px. Neighbours overlapped by ~12px vertically, and
                   * since the later sibling paints last it won the hit test. In a wrapped grid you cannot even
                   * see which row you are hitting, so picking tooth 26 could silently record tooth 16.
                   *
                   * Growing the painted cell to 40px on a coarse pointer closes the gap between paint and hit
                   * area, which is the only version of this that is honest — the user taps what they see.
                   */
                  className="h-7 w-9 p-0 text-2xs coarse:size-10 coarse:text-xs"
                  onClick={() => toggle(n)}
                  // The number is the button's accessible name, but nothing said whether it was selected;
                  // aria-pressed is what makes this grid usable without seeing the fill colour.
                  aria-pressed={value.includes(n)}
                  aria-label={`Dent ${n}`}
                >
                  {n}
                </Button>
              ))}
            </div>
          </div>
        )}
        {showChild && (
          <div className="space-y-1.5">
            <p className="text-xs font-medium text-muted-foreground">Enfant</p>
            <div className="flex flex-wrap gap-1">
              {CHILD_FDI.map((n) => (
                <Button
                  key={n}
                  type="button"
                  size="sm"
                  variant={value.includes(n) ? "default" : "outline"}
                  /*
                   * `coarse:size-10` grows the PAINT; the 44px overlay it already had was stealing neighbours' taps.
                   *
                   * These are 28×36px `Button`s in a `flex flex-wrap gap-1` grid of 32, so `buttonVariants`'
                   * `touch-target` overlay (44px minimum) overhangs each cell by ~8px horizontally and ~8px
                   * vertically — and the row pitch is only 32px. Neighbours overlapped by ~12px vertically, and
                   * since the later sibling paints last it won the hit test. In a wrapped grid you cannot even
                   * see which row you are hitting, so picking tooth 26 could silently record tooth 16.
                   *
                   * Growing the painted cell to 40px on a coarse pointer closes the gap between paint and hit
                   * area, which is the only version of this that is honest — the user taps what they see.
                   */
                  className="h-7 w-9 p-0 text-2xs coarse:size-10 coarse:text-xs"
                  onClick={() => toggle(n)}
                  // The number is the button's accessible name, but nothing said whether it was selected;
                  // aria-pressed is what makes this grid usable without seeing the fill colour.
                  aria-pressed={value.includes(n)}
                  aria-label={`Dent ${n}`}
                >
                  {n}
                </Button>
              ))}
            </div>
          </div>
        )}
        {value.length > 0 && (
          <Button type="button" variant="ghost" size="sm" className="w-full" onClick={() => onChange([])}>
            Tout effacer
          </Button>
        )}
      </PopoverContent>
    </Popover>
  )
}
