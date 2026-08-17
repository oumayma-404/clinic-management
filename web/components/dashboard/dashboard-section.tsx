"use client"

import Link from "next/link"
import { AlertTriangle } from "lucide-react"
import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"

interface DashboardSectionProps {
  title: string
  /** Optional one-line framing under the heading (e.g. what the comparison baseline is, or the window in words). */
  hint?: string
  /** A way through to the full screen this section summarises. Rendered only with {@link action}. */
  href?: string
  /** The link's wording, e.g. « Ouvrir l'agenda ». */
  action?: string
  /**
   * A control that scopes this section — the période track, a praticien filter.
   *
   * <p>It belongs in the header rather than above it, because that is what makes its scope legible: a selector
   * floating over three surfaces appears to govern all of them, and the one thing the dashboard must never do is
   * imply that « Ce mois » rescopes the day board.</p>
   */
  control?: React.ReactNode
  /** Set while a period change is in flight — holds the previous render at reduced opacity. */
  refetching?: boolean
  error?: string | null
  onRetry?: () => void
  /** For a section that also marks a change of subject — the bilan band takes `border-t pt-8`. */
  className?: string
  children?: React.ReactNode
}

/**
 * A titled region of the dashboard, with its own loading / failed / real states.
 *
 * <p>Two behaviours are deliberate. A failed read renders « Indisponible » with a retry rather than « — » or « 0 »:
 * on a money screen a network error and a genuinely empty period must not look alike (the same distinction
 * `/factures`' `RevenueValue` makes). And a refetch <b>holds the previous render</b> at reduced opacity instead of
 * showing a skeleton — switching period must not blank the page or shift the layout.</p>
 *
 * <p>⚠️ <b>This is the one section-heading primitive.</b> `app/page.tsx` carried a private `SectionBar` drawing the
 * same band for the day zones — the same eyebrow, the same hairline — because this component only knew about a
 * `hint` and it needed an « Ouvrir l'agenda » link. Two components painting one band is how they drift; the
 * `href`/`action` pair and the `control` slot are that merge, and `SectionBar` is gone.</p>
 *
 * <p>⚠️ `children` is optional: a header alone is a legitimate use (the période band owns a control and no figures
 * of its own).</p>
 */
export function DashboardSection({
  title,
  hint,
  href,
  action,
  control,
  refetching = false,
  error = null,
  onRetry,
  className,
  children,
}: DashboardSectionProps) {
  return (
    <section aria-label={title} className={cn("space-y-3", className)}>
      {/*
        The heading is a **monospace uppercase eyebrow**, not a 16px semibold title, with a hairline under it rather
        than a box around the group — the section is the object on the page, so it needs one edge to sit against.

        Sections differentiate by type here rather than by colour: giving each block its own hue would spend the
        accent on making nothing important. The accent appears once, as the dot, which is what ties this band to the
        day board's own zones.

        `items-baseline` + `flex-wrap` so a long title, its hint and a control fall onto their own lines at 320 px
        instead of compressing each other. The control is pushed to the end and takes the full row on a phone.
      */}
      <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-2 border-b pb-2">
        <h2 className="flex items-center gap-2 font-mono text-2xs font-medium uppercase tracking-[0.12em] text-muted-foreground">
          <span aria-hidden="true" className="size-1.5 shrink-0 rounded-full bg-primary" />
          {title}
        </h2>

        {hint && <p className="font-mono text-2xs text-muted-foreground">{hint}</p>}

        {href && action && (
          <Link
            href={href}
            className="text-xs font-medium text-primary underline-offset-4 hover-hover:hover:underline"
          >
            {action} →
          </Link>
        )}

        {control && <div className="w-full sm:ms-auto sm:w-auto">{control}</div>}
      </div>

      {error ? (
        <div
          role="status"
          className="flex flex-wrap items-center gap-3 rounded-lg border border-destructive/40 bg-destructive-wash p-3 text-sm"
        >
          <AlertTriangle className="size-4 shrink-0 text-destructive" aria-hidden="true" />
          <span className="min-w-0 flex-1">{error}</span>
          {onRetry && (
            <Button size="sm" variant="outline" onClick={onRetry}>
              Réessayer
            </Button>
          )}
        </div>
      ) : (
        children && (
          <div
            className={refetching ? "opacity-60 transition-opacity" : "transition-opacity"}
            aria-busy={refetching || undefined}
          >
            {children}
          </div>
        )
      )}
    </section>
  )
}
