"use client"

import * as React from "react"

import { cn } from "@/lib/utils"

/**
 * A single time input, replacing the two-`Select` hour + minute pair the appointment dialogs used to carry.
 *
 * The pair was the worst friction in the product's most repeated action: the minute list was built from
 * `Array.from({ length: 60 })`, so entering « 09:30 » meant opening two dropdowns and scrolling a sixty-row
 * list. Clinics book on quarter-hours; nobody has ever needed to pick :37 from a menu.
 *
 * `<input type="time">` is used rather than a nicer-looking custom widget for three reasons that all matter
 * more than the styling: it is typeable (`0930` from the numpad, which is how a receptionist actually works),
 * it is already localized to a 24-hour clock by the browser under `lang="fr"`, and it holds **any** minute —
 * so an appointment imported from Google at 09:37 still renders its own time instead of falling back to a
 * value absent from a quarter-hour list. `step` only constrains the native stepper arrows, never typing.
 *
 * The `HH` / `mm` string pair is kept as the external contract because the dialogs' validation, duration
 * arithmetic and overlap detection are all written against it — swapping the control should not ripple into
 * their state shape.
 */
export interface TimeFieldProps {
  id?: string
  /** Two-digit hour, `"00"`–`"23"`. */
  hour: string
  /** Two-digit minute, `"00"`–`"59"`. */
  minute: string
  onChange: (next: { hour: string; minute: string }) => void
  disabled?: boolean
  required?: boolean
  className?: string
  "aria-describedby"?: string
}

const pad = (value: number) => String(value).padStart(2, "0")

export function TimeField({
  id,
  hour,
  minute,
  onChange,
  disabled,
  required,
  className,
  "aria-describedby": ariaDescribedBy,
}: TimeFieldProps) {
  const value = `${pad(Number.parseInt(hour, 10) || 0)}:${pad(Number.parseInt(minute, 10) || 0)}`

  return (
    <input
      id={id}
      type="time"
      // 5-minute stepper granularity: fine enough for the odd 09:05 recall, coarse enough that arrowing from
      // 09:00 to 09:30 is six presses rather than thirty.
      step={300}
      value={value}
      disabled={disabled}
      required={required}
      aria-describedby={ariaDescribedBy}
      onChange={(event) => {
        const next = event.target.value
        // Clearing the field (or a partially-typed value) yields "" — hold the previous time rather than
        // writing NaN into state, which would make the duration read as 0 and block the submit with a message
        // about the duration when the user is mid-keystroke on the time.
        if (!next) return
        const [nextHour, nextMinute] = next.split(":")
        if (!nextHour || !nextMinute) return
        onChange({ hour: pad(Number.parseInt(nextHour, 10)), minute: pad(Number.parseInt(nextMinute, 10)) })
      }}
      className={cn(
        // Matched to `ui/input.tsx` so it sits in the same forms without looking like a native control that
        // wandered in. `tabular-nums` keeps the digits from shifting width as the value changes.
        "flex h-10 w-full min-w-0 rounded-md border border-input bg-transparent px-3 py-2 text-sm tabular-nums shadow-xs outline-none transition-[color,box-shadow] focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 dark:bg-input/30",
        className,
      )}
    />
  )
}
