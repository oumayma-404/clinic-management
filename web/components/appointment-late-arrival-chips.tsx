"use client"

import { useEffect, useState } from "react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { cn } from "@/lib/utils"

/** The usual retards, one tap each. */
export const LATE_ARRIVAL_PRESETS = [5, 10, 15, 20, 30] as const

/** Mirrors `Appointment.MaxLateArrivalMinutes` — the server refuses anything longer. */
export const MAX_LATE_ARRIVAL_MINUTES = 240

const isPreset = (minutes: number) => (LATE_ARRIVAL_PRESETS as readonly number[]).includes(minutes)

function parseMinutes(text: string): number | null {
  const value = Number.parseInt(text.trim(), 10)
  return Number.isInteger(value) && value >= 1 && value <= MAX_LATE_ARRIVAL_MINUTES ? value : null
}

/** « Retard » as one row of chips (5 · 10 · 15 · 20 · 30 · Autre, optional « Aucun »); each host decides when it saves. */
export function LateArrivalChips({
  value,
  onChange,
  withNone = false,
  disabled = false,
  label = "Retard",
}: {
  value: number | null
  onChange: (minutes: number | null) => void
  withNone?: boolean
  disabled?: boolean
  label?: string
}) {
  const custom = value !== null && !isPreset(value)
  const [otherOpen, setOtherOpen] = useState(false)
  const [otherText, setOtherText] = useState("")

  // A value set from outside (a reload, another tab) closes the field and shows itself as a chip.
  useEffect(() => {
    setOtherOpen(false)
    setOtherText(value !== null && !isPreset(value) ? String(value) : "")
  }, [value])

  const otherValue = parseMinutes(otherText)
  const commitOther = () => {
    if (otherValue === null) return
    setOtherOpen(false)
    if (otherValue !== value) onChange(otherValue)
  }

  // Grown, never `.touch-target`: these sit a few pixels apart, and an overlay would steal a neighbour's tap.
  const chip = (pressed: boolean) =>
    cn("h-8 min-w-11 px-2.5 coarse:h-11", !pressed && "bg-card")

  return (
    <div role="radiogroup" aria-label={label} className="flex flex-wrap items-center gap-1.5">
      {withNone && (
        <Button
          type="button"
          role="radio"
          aria-checked={value === null}
          variant={value === null ? "default" : "outline"}
          size="sm"
          className={chip(value === null)}
          disabled={disabled}
          onClick={() => value !== null && onChange(null)}
        >
          Aucun
        </Button>
      )}
      {LATE_ARRIVAL_PRESETS.map((minutes) => (
        <Button
          key={minutes}
          type="button"
          role="radio"
          aria-checked={value === minutes}
          variant={value === minutes ? "default" : "outline"}
          size="sm"
          className={chip(value === minutes)}
          disabled={disabled}
          onClick={() => value !== minutes && onChange(minutes)}
        >
          {minutes} min
        </Button>
      ))}
      {otherOpen ? (
        <span className="inline-flex items-center gap-1.5">
          <Input
            autoFocus
            inputMode="numeric"
            aria-label="Retard en minutes"
            aria-invalid={otherText.trim() !== "" && otherValue === null}
            value={otherText}
            disabled={disabled}
            onChange={(e) => setOtherText(e.target.value.replace(/\D/g, "").slice(0, 3))}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                e.preventDefault()
                commitOther()
              } else if (e.key === "Escape") {
                e.stopPropagation()
                setOtherOpen(false)
              }
            }}
            onBlur={commitOther}
            className="h-8 w-16 md:text-sm"
          />
          <span className="text-sm text-muted-foreground">min</span>
        </span>
      ) : (
        <Button
          type="button"
          role="radio"
          aria-checked={custom}
          variant={custom ? "default" : "outline"}
          size="sm"
          className={chip(custom)}
          disabled={disabled}
          onClick={() => setOtherOpen(true)}
        >
          {custom ? `${value} min` : "Autre"}
        </Button>
      )}
    </div>
  )
}
