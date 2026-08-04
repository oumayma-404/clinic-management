"use client"

import type { ReactNode } from "react"
import { usePathname } from "next/navigation"
import { cn } from "@/lib/utils"
import { navIconForPath, zoneForPath } from "@/lib/zones"

interface PageHeaderProps {
  /**
   * **Deprecated as an input.** The zone is now derived from the route (`lib/zones.ts`), because the two had
   * already drifted: the rail grouped « Patients » under « Quotidien » while this header called the same page
   * « Dossiers », and it grouped « Factures » under « Finances » while the header said « Argent ». A user who
   * navigates by one vocabulary and lands in another has to reconcile them, and there is nothing to reconcile —
   * they were the same place under two names nobody chose together.
   *
   * <p>Kept only as an **override** for a surface with no route of its own. Passing it is almost always wrong.</p>
   */
  zone?: string
  title: string
  /**
   * One line under the title, carrying **a fact** — « 1 284 dossiers · 23 ce mois ».
   *
   * <p>Not a paraphrase of the page. « Consultez et gérez tous les dossiers patients » describes the screen to
   * someone already looking at it, which is the one reader who does not need it.</p>
   */
  subtitle?: ReactNode
  /** Right-aligned controls. **One** primary action per page; everything else is `variant="outline"` or a link. */
  actions?: ReactNode
  /** Suppresses the icon chip for a page whose route has no sensible glyph. */
  hideIcon?: boolean
  className?: string
}

/**
 * The one page header.
 *
 * <p>It replaces four hand-rolled treatments — `text-3xl font-semibold` on ten pages, `text-2xl font-bold` on two,
 * `text-xl font-semibold` on one, and a blue gradient clipped to text on `/documents`. None of them was wrong
 * alone; together they were the main reason the app read as several products, because the page title is the first
 * thing on every screen.</p>
 *
 * <p><b>26 px / 650, one size, no colour on the title.</b> A page title does not need to compete — the figures and
 * tables below it carry the content, and a coloured or gradient title spends the accent where it buys nothing.</p>
 *
 * <p><b>What is new is beside the title, not in it.</b> A zone-tinted icon chip and a zone-coloured eyebrow, both
 * derived from the route and both matching the rail exactly. That is where colour belongs on this surface: the
 * chip and the eyebrow say <i>where you are</i>, which is a job, while a coloured heading would only say
 * <i>this is a heading</i>, which the reader could already see. For a user who is not fluent with software, the
 * glyph is also the faster half — a shape is recognised before a French phrase is finished.</p>
 */
export function PageHeader({ zone, title, subtitle, actions, hideIcon = false, className }: PageHeaderProps) {
  const pathname = usePathname()
  const resolved = zoneForPath(pathname)
  const Icon = hideIcon ? undefined : navIconForPath(pathname)
  const eyebrow = zone ?? resolved.label

  return (
    <div className={cn("flex flex-wrap items-end justify-between gap-4", className)}>
      <div className="flex min-w-0 items-start gap-3">
        {Icon && (
          /*
           * The chip is `hidden sm:flex`. On a 390 px phone the title block already competes with the actions
           * row for a single line's width, and 44 px of decoration is 44 px the patient's name or the period
           * label does not get. The eyebrow below still carries the zone's colour there, so nothing is lost but
           * the ornament.
           */
          <span
            aria-hidden="true"
            className={cn(
              "mt-0.5 hidden size-11 shrink-0 items-center justify-center rounded-xl sm:flex",
              resolved.wash,
              resolved.text,
            )}
          >
            <Icon className="size-5" strokeWidth={1.75} />
          </span>
        )}

        <div className="min-w-0">
          <p
            className={cn(
              "flex items-center gap-1.5 font-mono text-2xs font-medium uppercase tracking-[0.1em]",
              resolved.text,
            )}
          >
            {/* The dot repeats the rail's 3 px active bar at the top of the page it opens — the same colour in
                the same relationship, so the two surfaces read as one place rather than two. */}
            <span aria-hidden="true" className={cn("size-1.5 shrink-0 rounded-full", resolved.bg)} />
            {eyebrow}
          </p>
          <h1 className="mt-1 text-title font-semibold leading-tight tracking-tight text-foreground">{title}</h1>
          {subtitle && <p className="mt-1 max-w-[56ch] text-sm text-muted-foreground">{subtitle}</p>}
        </div>
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </div>
  )
}
