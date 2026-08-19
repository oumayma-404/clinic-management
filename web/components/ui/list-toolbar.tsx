"use client"

import type { ReactNode } from "react"
import { Search, X } from "lucide-react"
import { Input } from "@/components/ui/input"
import { cn } from "@/lib/utils"

interface ListToolbarProps {
  /** Omit to render no search field (a list with nothing to search by). */
  search?: {
    value: string
    onChange: (value: string) => void
    placeholder: string
    /** Names the field for screen readers — the placeholder is not a label. */
    label: string
  }
  /** {@link FilterChip}s, or any other control that **narrows** the list. */
  children?: ReactNode
  className?: string
}

/**
 * The one toolbar above a list: search, then the controls that narrow it.
 *
 * <p><b>Only things that reduce the list belong here.</b> Every list page previously mixed its primary action
 * (« + Nouveau patient ») into this row, giving a create button the same weight as a filter and leaving the row with
 * no single meaning. The primary action now lives in {@link PageHeader}, where there is exactly one per page.</p>
 */
export function ListToolbar({ search, children, className }: ListToolbarProps) {
  return (
    <div className={cn("flex flex-wrap items-center gap-2", className)}>
      {/*
        ⚠️ `min-w-[190px]` is `sm:` and up only. Unprefixed, it was a floor the search box could not go
        below — and a flex child that cannot shrink is what pushes a page sideways. At 320 px the content
        box is ~288 px, so the box plus any sibling filter overflowed the row, which is the « champs qui
        débordent » the clinic reported. Below `sm:` the search takes a full row of its own and the filter
        chips wrap underneath it, which is also the right shape for a thumb.

        ⚠️ **`flex-1` is `sm:`-prefixed too, and that is what actually delivers « a full row of its own ».**
        `flex-1` is `flex: 1 1 0%`, and a flex-basis of `0%` beats the `w-full` beside it — so the box's
        *hypothetical* size was zero, it never triggered the wrap it is supposed to, and it shared the row with
        the filter chips instead. Measured on `/fournisseurs` at 390 px: the wrapper resolved to **4 px** wide,
        i.e. the search box was effectively invisible on a phone. With no `flex-1` below `sm:` the basis is
        `auto`, so `w-full` is the hypothetical size and the row wraps as intended. (Same trap as
        `subscription-banner.tsx` and `doctor-working-hours-card.tsx`.)
      */}
      {search && (
        <div className="relative w-full min-w-0 sm:w-auto sm:min-w-[190px] sm:max-w-sm sm:flex-1">
          <Search
            aria-hidden="true"
            className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
          />
          <Input
            type="search"
            aria-label={search.label}
            placeholder={search.placeholder}
            value={search.value}
            onChange={(event) => search.onChange(event.target.value)}
            className="pl-9"
          />
        </div>
      )}
      {children}
    </div>
  )
}

interface FilterChipProps {
  /** Stable label — it must **not** change with state. See the note below. */
  label: string
  active: boolean
  onToggle: () => void
  /** How many rows this filter would leave. Omit when the count is not known cheaply. */
  count?: number
  disabled?: boolean
}

/**
 * One filter, as a toggle chip.
 *
 * <p>Two things it fixes. (a) The label is <b>stable</b>: filters used to be `Button`s whose text flipped
 * (« Afficher les signalés » ↔ « Signalés affichés »), so the only way to know whether a filter was on was to read
 * a sentence and infer its tense — where a pressed chip is visibly pressed and carries `aria-pressed` for anyone
 * who cannot see it. (b) It shows its <b>count</b>, so you know what a filter will cost before spending a click on
 * it; « Signalés 17 » is a different decision from « Signalés 0 ».</p>
 */
export function FilterChip({ label, active, onToggle, count, disabled = false }: FilterChipProps) {
  return (
    <button
      type="button"
      aria-pressed={active}
      disabled={disabled}
      onClick={onToggle}
      className={cn(
        // ~30px painted; `touch-target` raises the tappable area to 44px on a coarse pointer (AC-10).
        "touch-target inline-flex items-center gap-1.5 rounded-full border px-3 py-1.5 text-sm transition-colors duration-150 ease-out",
        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1",
        "disabled:cursor-not-allowed disabled:opacity-60 motion-reduce:transition-none",
        active
          ? "border-primary bg-accent font-semibold text-accent-foreground"
          : "bg-card text-muted-foreground hover-hover:hover:text-foreground",
      )}
    >
      {label}
      {count !== undefined && (
        <span className="font-mono text-2xs tabular-nums opacity-75">{count}</span>
      )}
    </button>
  )
}

interface ActiveFilterChipProps {
  /** What the filter is doing, phrased as a fact: « Statut : Payée », « Signalés seulement ». */
  label: string
  /** Clears this one filter. */
  onRemove: () => void
}

/**
 * An **active filter, with a way off it** (AC-19).
 *
 * <p>Deliberately not a variant of `FilterChip`. That one is a <b>toggle</b> — `aria-pressed`, pressed-vs-not,
 * and the control is the filter itself. This one is a <b>statement plus a dismiss</b>: it says a filter is
 * applied and offers to remove it, which is a different affordance and a different thing to announce.</p>
 *
 * <p>It exists because of the card conversion. A table announces its own filtering — you can see the column and
 * the missing rows — but a card list has no header row, so a filtered list and a short list look identical, and
 * nine dashboard links land on a filtered list without the user having chosen the filter. « Aucun résultat » on a
 * screen with no visible filter is a bug report waiting to happen.</p>
 */
export function ActiveFilterChip({ label, onRemove }: ActiveFilterChipProps) {
  return (
    <span className="inline-flex items-center gap-1 rounded-full border border-primary bg-accent py-1 ps-3 pe-1 text-sm text-accent-foreground">
      {label}
      <button
        type="button"
        onClick={onRemove}
        aria-label={`Retirer le filtre : ${label}`}
        className="touch-target inline-flex size-5 items-center justify-center rounded-full text-muted-foreground transition-colors hover-hover:hover:bg-background hover-hover:hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      >
        <X className="size-3.5" aria-hidden="true" />
      </button>
    </span>
  )
}
