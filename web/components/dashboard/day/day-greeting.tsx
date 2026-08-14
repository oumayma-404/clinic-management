"use client"

import type { DayPhrase } from "@/lib/dashboard/day-phrases"
import { ZONES } from "@/lib/zones"
import { cn } from "@/lib/utils"

interface DayGreetingProps {
  phrase: DayPhrase | null
  /** « Jeudi 14 août », already capitalised by the caller. Empty until the date resolves after mount. */
  dayLabel: string
  loading?: boolean
}

/**
 * Zone 1 of the day board — what kind of day this is.
 *
 * <p>It sits on the page ground with no card of its own, because it <i>is</i> the page title: the same
 * mono-uppercase zone eyebrow, the same `text-title` size and the same 56ch subtitle measure `ui/page-header.tsx`
 * uses, so the dashboard's first line is on-system with every other screen. It is written here rather than
 * passed to `PageHeader` only because of the emoji, which that primitive has no slot for and should not grow one
 * for a single caller.</p>
 *
 * <p>⚠️ The headline comes from the phrase bank and the sub-line is <b>generated from the real count</b>
 * (`day-phrases.ts`). That split is what stops the greeting becoming decoration: the cheerful half can never
 * contradict the figures, because it never states one.</p>
 */
export function DayGreeting({ phrase, dayLabel, loading = false }: DayGreetingProps) {
  const zone = ZONES.daily

  return (
    <div className="flex items-start gap-3 sm:gap-4">
      {/* Decorative: the headline beside it says the same thing in words, so a screen reader gains nothing
          from « abeille » and would lose the sentence to it. */}
      <span aria-hidden="true" className="shrink-0 text-3xl leading-tight sm:text-4xl">
        {phrase?.emoji ?? "🦷"}
      </span>

      <div className="min-w-0">
        <p
          className={cn(
            "flex items-center gap-1.5 font-mono text-2xs font-medium uppercase tracking-[0.1em]",
            zone.text,
          )}
        >
          <span aria-hidden="true" className={cn("size-1.5 shrink-0 rounded-full", zone.bg)} />
          {/* Empty on the first paint by design — see the page's `todayLabel` note. The eyebrow keeps its
              height either way, so nothing shifts when it fills in. */}
          {dayLabel || zone.label}
        </p>

        {loading ? (
          <span
            className="mt-1 block h-8 w-64 max-w-full animate-pulse rounded bg-muted"
            aria-label="Chargement de la journée"
          />
        ) : (
          <h1 className="mt-1 text-title font-semibold leading-tight tracking-tight text-foreground">
            {phrase?.headline ?? "Bonjour"}
          </h1>
        )}

        {phrase?.subline && !loading && (
          <p className="mt-1.5 max-w-[56ch] text-sm text-muted-foreground">{phrase.subline}</p>
        )}
      </div>
    </div>
  )
}
