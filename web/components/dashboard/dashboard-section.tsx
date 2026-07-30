"use client"

import { AlertTriangle } from "lucide-react"
import { Button } from "@/components/ui/button"

interface DashboardSectionProps {
  title: string
  /** Optional one-line framing under the heading (e.g. what the comparison baseline is). */
  hint?: string
  /** Set while a period change is in flight — holds the previous render at reduced opacity. */
  refetching?: boolean
  error?: string | null
  onRetry?: () => void
  children: React.ReactNode
}

/**
 * A titled group of dashboard figures with its own loading / failed / real states.
 *
 * <p>Two behaviours are deliberate. A failed read renders « Indisponible » with a retry rather than « — » or « 0 »:
 * on a money screen a network error and a genuinely empty period must not look alike (the same distinction
 * `/factures`' `RevenueValue` makes). And a refetch <b>holds the previous render</b> at reduced opacity instead of
 * showing a skeleton — switching period must not blank the page or shift the layout.</p>
 */
export function DashboardSection({
  title,
  hint,
  refetching = false,
  error = null,
  onRetry,
  children,
}: DashboardSectionProps) {
  return (
    <section aria-label={title} className="space-y-3">
      {/* A hairline under the heading, not a box around the group. Part of the de-boxing: the section is now the
          object on the page, so it needs one edge to sit against — the figures inside it share a single surface. */}
      {/*
        The heading is a **monospace uppercase eyebrow**, not a 16px semibold title.

        Sections differentiate by type here rather than by colour: giving each block its own hue would spend the
        accent on making nothing important, and the accent is now reserved for the one filled surface. An eyebrow
        also stops the four section titles competing with the figures underneath them — they are labels for regions,
        not content. The hint joins them in the same register, since it says the same thing for every card below
        (« comparé à … »), which is precisely why it belongs here once instead of inside sixteen delta pills.
      */}
      <div className="flex flex-wrap items-baseline justify-between gap-2 border-b pb-2">
        <h2 className="font-mono text-[11px] font-medium uppercase tracking-[0.12em] text-muted-foreground">
          {title}
        </h2>
        {hint && <p className="font-mono text-[11px] text-muted-foreground">{hint}</p>}
      </div>

      {error ? (
        <div
          role="status"
          className="flex flex-wrap items-center gap-3 rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm"
        >
          <AlertTriangle className="h-4 w-4 shrink-0 text-destructive" aria-hidden="true" />
          <span className="min-w-0 flex-1">{error}</span>
          {onRetry && (
            <Button size="sm" variant="outline" onClick={onRetry}>
              Réessayer
            </Button>
          )}
        </div>
      ) : (
        <div
          className={refetching ? "opacity-60 transition-opacity" : "transition-opacity"}
          aria-busy={refetching || undefined}
        >
          {children}
        </div>
      )}
    </section>
  )
}
