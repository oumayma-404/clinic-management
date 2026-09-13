"use client"

import * as React from "react"

import { cn } from "@/lib/utils"

/**
 * The product's one time control — a **24-hour** field, unconditionally.
 *
 * <p>⚠️ It is a masked text input rather than `<input type="time">`, and that is the whole point. A native time
 * input renders in the **browser's UI locale**, which `lang="fr"` does not touch: measured in Chrome 2026-09-13,
 * an input carrying `lang="en-US"` and one carrying `lang="fr"` rendered identically, both following the
 * browser. So on any workstation whose Chrome is English — the ordinary case on a Windows PC — every time in
 * this app was picked and read as « 02:30 PM », in a product whose own prose, PDFs, SMS reminders and agenda all
 * say « 14:30 ». Tunisian practices work on a 24-hour clock; the file this comment replaces claimed the browser
 * already handled it under `lang="fr"`, and that claim was simply false.</p>
 *
 * <p>What the native control gave up in exchange is the clock popup. What it keeps is the half that a
 * receptionist actually uses: `0930` straight off the numpad, `inputMode="numeric"` for the phone keypad, and
 * ↑/↓ stepping by five minutes. Any minute is still holdable, so an appointment imported from Google at 09:37
 * renders its own time.</p>
 *
 * <p>Emits on the fourth digit and again on blur, so a value is never left behind an entry the user has
 * finished — and blur fires on a button's `mousedown`, before its `click`, so « Enregistrer » sees the time that
 * is on screen.</p>
 */
export interface TimeInputProps {
  id?: string
  /** `"HH:mm"` — or `""` when nothing is set, which only an `allowEmpty` field can be. */
  value: string
  onChange: (next: string) => void
  /** Minutes ↑/↓ move by. */
  step?: number
  /** Whether an emptied field means « not set »; otherwise it snaps back to its previous time on blur. */
  allowEmpty?: boolean
  disabled?: boolean
  required?: boolean
  className?: string
  "aria-label"?: string
  "aria-describedby"?: string
}

const pad2 = (value: number) => String(value).padStart(2, "0")

/** Digits only, at most four — the entire model behind the mask. */
const digitsOf = (raw: string) => raw.replace(/\D/g, "").slice(0, 4)

/** `"0930"` → `"09:30"`; three digits or fewer stay bare so Backspace deletes one character at a time. */
const maskDigits = (digits: string) => (digits.length > 2 ? `${digits.slice(0, 2)}:${digits.slice(2)}` : digits)

/** `"9"` → `"09:00"` · `"930"` → `"09:30"` · `"2570"` → `"23:59"` · `""` → `""`. Never returns a bad time. */
function normalizeDigits(digits: string): string {
  if (!digits) return ""
  const rawHour = digits.length <= 2 ? digits : digits.slice(0, -2)
  const rawMinute = digits.length <= 2 ? "0" : digits.slice(-2)
  return `${pad2(Math.min(23, Number.parseInt(rawHour, 10) || 0))}:${pad2(
    Math.min(59, Number.parseInt(rawMinute, 10) || 0),
  )}`
}

export function TimeInput({
  id,
  value,
  onChange,
  step = 5,
  allowEmpty = false,
  disabled,
  required,
  className,
  "aria-label": ariaLabel,
  "aria-describedby": ariaDescribedBy,
}: TimeInputProps) {
  // Holds the half-typed text; `null` means « show the committed value ».
  const [draft, setDraft] = React.useState<string | null>(null)
  const shown = draft ?? value

  const commit = (digits: string) => {
    const next = normalizeDigits(digits)
    setDraft(null)
    // An emptied field that may not be empty keeps its previous time rather than writing "" into a form that
    // would then refuse the save with a message about a field the user can see is filled.
    if (!next && !allowEmpty) return
    if (next !== value) onChange(next)
  }

  const nudge = (deltaMinutes: number) => {
    const [hour, minute] = (normalizeDigits(digitsOf(shown)) || "00:00").split(":").map(Number)
    const total = (hour * 60 + minute + deltaMinutes + 1440) % 1440
    setDraft(null)
    onChange(`${pad2(Math.floor(total / 60))}:${pad2(total % 60)}`)
  }

  return (
    <input
      id={id}
      type="text"
      inputMode="numeric"
      autoComplete="off"
      placeholder="--:--"
      maxLength={5}
      // Guards a paste of something that is not a time; typing can never reach a value this refuses.
      pattern="([01][0-9]|2[0-3]):[0-5][0-9]"
      value={shown}
      disabled={disabled}
      required={required}
      aria-label={ariaLabel}
      aria-describedby={ariaDescribedBy}
      onChange={(event) => {
        const digits = digitsOf(event.target.value)
        setDraft(maskDigits(digits))
        if (digits.length === 4) commit(digits)
      }}
      onFocus={(event) => event.target.select()}
      onBlur={() => commit(digitsOf(shown))}
      onKeyDown={(event) => {
        if (event.key === "ArrowUp") {
          event.preventDefault()
          nudge(step)
        } else if (event.key === "ArrowDown") {
          event.preventDefault()
          nudge(-step)
        }
      }}
      className={cn(
        // Matched to `ui/input.tsx` so it sits in the same forms without looking like a control that wandered
        // in. `tabular-nums` keeps the digits from shifting width as the value changes, and `text-base` below
        // `md:` is the same iOS focus-zoom guard `Input` carries — this is one of the most-tapped fields here.
        //
        // ⚠️ `min-w-[4.5rem]` REPLACES a floor the native control gave for free, and its absence was measured:
        // `<input type="time">` had a ~105 px intrinsic width no flex context could shrink past, so a row too
        // narrow for two of them **wrapped**. A text input has no such floor, and on the first pass the two
        // « Pause » fields of `/settings` collapsed to **26 px** at 320 px — narrower than their own `--:--`
        // placeholder. A caller must therefore NOT pass `min-w-0` (same tailwind-merge group, so it wins and
        // the floor is gone); put `min-w-0` on the wrapper and let the row `flex-wrap` instead.
        "flex h-10 w-full min-w-[4.5rem] rounded-md border border-input bg-card px-3 py-2 text-base md:text-sm tabular-nums shadow-xs outline-none transition-[color,box-shadow] placeholder:text-muted-foreground focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 dark:bg-input/30",
        className,
      )}
    />
  )
}

/**
 * The same field against an `HH` / `mm` string pair — the shape the appointment dialogs' validation, duration
 * arithmetic and overlap detection are all written against, kept so the control below it can change without
 * rippling into their state.
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

export function TimeField({ hour, minute, onChange, ...rest }: TimeFieldProps) {
  return (
    <TimeInput
      {...rest}
      value={`${pad2(Number.parseInt(hour, 10) || 0)}:${pad2(Number.parseInt(minute, 10) || 0)}`}
      onChange={(next) => {
        const [nextHour, nextMinute] = next.split(":")
        onChange({ hour: nextHour, minute: nextMinute })
      }}
    />
  )
}
