"use client"

import type { ReactNode } from "react"
import { Search } from "lucide-react"
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
      {search && (
        <div className="relative min-w-[190px] flex-1 sm:max-w-sm">
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
        "inline-flex items-center gap-1.5 rounded-full border px-3 py-1.5 text-sm transition-colors duration-150 ease-out",
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
