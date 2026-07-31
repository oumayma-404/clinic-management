"use client"

import Link from "next/link"
import { ArrowDown, ArrowUp, Minus, type LucideIcon } from "lucide-react"
import { cn } from "@/lib/utils"
import { HeroKpi } from "@/components/dashboard/hero-kpi"
import type { PeriodComparison } from "@/lib/api/types"

/**
 * How much visual weight this figure gets.
 *
 * <p>`hero` is for the one number a user opens the page for — « Net » for a practitioner-owner. `default` is a
 * normal figure. `compact` drops the icon and shrinks the value, for the operational counts where the *label* is
 * what you scan and the number is usually a single digit.</p>
 *
 * <p>This exists because equal weight was the actual problem: sixteen figures in identical boxes at identical type
 * sizes forces the reader to do the ranking the design should have done for them.</p>
 */
export type KpiEmphasis = "hero" | "default" | "compact"

/**
 * Whether a rise in this figure is good news. Drives the delta's colour, which is otherwise a lie: « Dépenses +18 % »
 * and « Encaissé +18 % » are the same arrow and opposite news, so the direction alone cannot pick the colour.
 * « Taux d'absence » and « Dépenses » are the inverted ones.
 */
export type DeltaSense = "up-is-good" | "up-is-bad" | "neutral"

interface KpiCardProps {
  label: string
  description: string
  /** Pre-formatted value — money through `formatDT`, counts through `toLocaleString`, rates as « 8,3 % ». */
  value: string
  icon: LucideIcon
  href: string
  /** Absent for point-in-time figures (créances, the à-traiter counts), which have no previous value. */
  comparison?: PeriodComparison
  sense?: DeltaSense
  /** Names the baseline in the delta's accessible text, e.g. « le mois dernier ». */
  previousPeriodLabel?: string
  loading?: boolean
  variant?: "default" | "urgent"
  emphasis?: KpiEmphasis
  /** Spans two columns of the enclosing `KpiGrid`. Ignored by `hero`, which stands outside the grid. */
  wide?: boolean
  /**
   * Values for the hero's inline sparkline, oldest first — the collected trend.
   *
   * <p>It lives on the hero because the trend's *shape* answers the same question the hero's number does
   * (« comment va le cabinet ? »), and the reader should not have to travel 400 px down the page for it. The full
   * six-month chart stays below for reading actual values; this is the direction only, which is why it carries no
   * axis and no labels.</p>
   */
  sparkline?: number[]
}

/**
 * One dashboard figure: a link to the records it counted, with its comparison against the previous period.
 *
 * <p>Always a `Link`. That is the feature — the retired `StatsCard` made `href` optional and four of seven cards
 * pointed at an unfiltered list, so the number and its destination disagreed.</p>
 *
 * <p><b>No longer its own `Card`.</b> It paints a plain `bg-card` cell and expects to sit inside a
 * {@link KpiGrid}, which supplies the single border and the hairlines. Sixteen individually-bordered cards was
 * the "too boxy" complaint, and it was also a hierarchy failure — see {@link KpiEmphasis}.</p>
 *
 * <p><b>`hero` is the exception and renders its own filled accent surface</b> — see {@link HeroKpi}. It is the
 * single saturated surface on the page, and that is the whole colour strategy: the screen read as washed out
 * because the accent filled *nothing*, and the answer is one bold surface with everything around it quiet, not
 * six accents competing.</p>
 */
export function KpiCard({
  label,
  description,
  value,
  icon: Icon,
  href,
  comparison,
  sense = "up-is-good",
  previousPeriodLabel,
  loading = false,
  variant = "default",
  emphasis = "default",
  wide = false,
  sparkline,
}: KpiCardProps) {
  const isCompact = emphasis === "compact"

  if (emphasis === "hero") {
    // Delegated wholesale: the hero shares no markup with a grid cell beyond being a `Link`, and folding a second
    // full layout in here is what produced an `isHero` ternary on nearly every line of this component.
    return (
      <HeroKpi
        label={label}
        description={description}
        value={value}
        icon={Icon}
        href={href}
        comparison={comparison}
        sense={sense}
        previousPeriodLabel={previousPeriodLabel}
        loading={loading}
        sparkline={sparkline}
      />
    )
  }

  return (
    <Link
      href={href}
      className={cn(
        // `bg-card` is load-bearing: the enclosing grid is `bg-border` showing through `gap-px`, so a cell that
        // does not paint its own background would render as a solid border block.
        "group relative block bg-card transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring hover:bg-accent/40",
        // Padding steps down on a phone: at 320 px the grid gives each cell ~144 px, and `p-5` would spend
        // 40 of them on air before the number starts.
        isCompact ? "p-3 sm:p-4" : "p-4 sm:p-5",
        // `col-span-2` at every width now that the base grid is two columns — it used to be `sm:` only
        // because the base grid was one column, where spanning two was a no-op.
        wide && "col-span-2",
        // An urgent figure gets a left accent rather than a tinted box: at this density a filled background on one
        // cell of a shared surface reads as a rendering fault, while a 2px edge reads as emphasis.
        variant === "urgent" && "before:absolute before:inset-y-0 before:left-0 before:w-[2px] before:bg-destructive",
      )}
      aria-label={`${label} : ${value}. Voir le détail.`}
    >
      <div className="min-w-0 space-y-1">
        {/* A 6px accent dot restores a little identity to the label row without the 40px filled tiles that were
            most of what made the old grid feel heavy. Decoration, hence aria-hidden — the label is the name. */}
        <p
          className={cn(
            "flex items-center gap-2 font-medium text-muted-foreground",
            isCompact ? "text-xs" : "text-sm",
          )}
        >
          <span
            aria-hidden="true"
            className={cn(
              "size-1.5 shrink-0 rounded-full",
              variant === "urgent" ? "bg-destructive" : "bg-primary/70",
            )}
          />
          {/*
            Wraps rather than truncates. In two phone columns « Nouveaux patients » and « Taux d'absence »
            are wider than their cell, and a truncated KPI label is worse than a two-line one: « Nouveaux
            pat… » names nothing, and the label IS the figure's name (the accessible label reads it out).
          */}
          <span className="min-w-0 [overflow-wrap:anywhere]">{label}</span>
        </p>
        {loading ? (
          <span className="block h-8 w-20 animate-pulse rounded bg-muted" aria-label="Chargement" />
        ) : (
          <p
            className={cn(
              "font-semibold tabular-nums tracking-tight text-foreground",
              // One step down on a phone. « 1 840,000 DT » at `text-2xl` measures ~110 px against ~104 px of
              // content box in a 320 px two-column cell — it would be the figure itself that overflows.
              isCompact ? "text-lg sm:text-xl" : "text-xl sm:text-2xl",
              variant === "urgent" && "text-destructive",
            )}
          >
            {value}
          </p>
        )}
        {/* The description is dropped at compact density. On « Stock bas » the label already says it and the
            second line of grey text was pure noise repeated six times down the à-traiter block. */}
        {!isCompact && <p className="text-xs text-muted-foreground">{description}</p>}
      </div>

      {comparison && !loading && (
        <DeltaBadge comparison={comparison} sense={sense} previousPeriodLabel={previousPeriodLabel} />
      )}
    </Link>
  )
}

/**
 * The comparison, rendered as an arrow + signed percentage.
 *
 * <p>Three distinct states, kept apart on purpose: a real delta, « — » when there is no comparable baseline (a new
 * clinic's first month, or a zero baseline where a percentage is undefined), and nothing at all when the figure is
 * point-in-time. Rendering « 0 % » for the middle case would assert "unchanged", which is a claim the data does not
 * support.</p>
 *
 * <p>The icon is not the only channel — the sign is in the text, and the full comparison is in the accessible label —
 * so the meaning never rests on colour alone.</p>
 */
function DeltaBadge({
  comparison,
  sense,
  previousPeriodLabel,
}: {
  comparison: PeriodComparison
  sense: DeltaSense
  previousPeriodLabel?: string
}) {
  const { deltaPercent } = comparison
  const baseline = previousPeriodLabel ? `vs. ${previousPeriodLabel}` : "vs. période précédente"

  if (deltaPercent === null || deltaPercent === undefined) {
    return (
      <p className="mt-3 inline-flex items-center gap-1.5 rounded-full bg-muted px-2 py-0.5 text-xs text-muted-foreground">
        <Minus className="size-3.5" aria-hidden="true" />
        <span>Pas de comparaison</span>
        <span className="sr-only">— {baseline}</span>
      </p>
    )
  }

  const rising = deltaPercent > 0
  const flat = deltaPercent === 0
  const good = sense === "neutral" ? null : rising === (sense === "up-is-good")

  /*
   * A **tinted pill**, not bare coloured text — the cheapest and most legible "this is a comparison" signal there
   * is, and the previous treatment (a coloured word floating under the number) was the single most dated thing on
   * the page. The wash pairs come from the theme (`--success-wash` / `--destructive-wash`) rather than hardcoded
   * `green-700`, so both modes and any future accent follow automatically.
   */
  const tone =
    flat || good === null
      ? "bg-muted text-muted-foreground"
      : good
        ? "bg-success-wash text-success"
        : "bg-destructive-wash text-destructive"

  const Arrow = flat ? Minus : rising ? ArrowUp : ArrowDown
  // Intl gives the French decimal comma and an explicit sign, so « +18,3 % » reads natively.
  const formatted = `${deltaPercent.toLocaleString("fr-TN", {
    signDisplay: "exceptZero",
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
  })} %`

  return (
    <p className={cn("mt-3 inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-semibold tabular-nums", tone)}>
      <Arrow className="size-3.5" aria-hidden="true" />
      <span>{formatted}</span>
      {/* The baseline moves out of the pill: repeated sixteen times down the page it was more ink than the figures
          it qualified, and it is identical for every card in a section — which is why the section header states it
          once. Kept for assistive tech, where there is no "elsewhere on the page" to read it from. */}
      <span className="sr-only">{baseline}</span>
    </p>
  )
}
