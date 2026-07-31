"use client"

import { useState } from "react"
import { Button } from "@/components/ui/button"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"

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

/** Quadrant order is upper-right → upper-left → lower-right → lower-left, which is the order the flat lists
 *  already had; deriving them keeps that and makes a divergence impossible. */
const flatten = (q: ToothQuadrants): number[] => [...q.upperRight, ...q.upperLeft, ...q.lowerRight, ...q.lowerLeft]

export const ADULT_FDI = flatten(ADULT_TEETH)
export const CHILD_FDI = flatten(CHILD_TEETH)

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
      <PopoverContent className="w-80 space-y-3" align="start">
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
                  className="h-7 w-9 p-0 text-2xs"
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
                  className="h-7 w-9 p-0 text-2xs"
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
