"use client"

import Link from "next/link"
import { ArrowDown, ArrowUp, Minus, type LucideIcon } from "lucide-react"
import { cn } from "@/lib/utils"
import type { PeriodComparison } from "@/lib/api/types"

/**
 * How much visual weight this figure gets.
 *
 * <p>`lead` is the answer its section is asked for — « Net » for l'argent, « Rendez-vous honorés » for l'activité.
 * `default` is a normal figure. `compact` drops the icon and shrinks the value, for the operational counts where the
 * *label* is what you scan and the number is usually a single digit.</p>
 *
 * <p>This exists because equal weight was the actual problem: nine figures in identical boxes at identical type
 * sizes forces the reader to do the ranking the design should have done for them.</p>
 *
 * <p>⚠️ `lead` replaced a `hero` that delegated to a **filled accent panel** (`hero-kpi.tsx`, now deleted). That
 * panel was right while these sections were the whole page and it sat above the fold; once the day board moved in
 * above it, the loudest surface in the product was one nobody scrolled to, and the design system allows exactly one
 * filled accent surface per page. A lead cell keeps the hierarchy and spends no accent to get it.</p>
 */
export type KpiEmphasis = "lead" | "default" | "compact"

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
}: KpiCardProps) {
  const isCompact = emphasis === "compact"
  const isLead = emphasis === "lead"

  return (
    <Link
      href={href}
      className={cn(
        // `bg-card` is load-bearing: the enclosing grid is `bg-border` showing through `gap-px`, so a cell that
        // does not paint its own background would render as a solid border block.
        "group relative bg-card transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring hover:bg-accent/40",
        // Padding steps down on a phone: at 320 px the grid gives each cell ~144 px, and `p-5` would spend
        // 40 of them on air before the number starts.
        isCompact ? "p-3 sm:p-4" : "p-4 sm:p-5",
        /*
         * The lead cell is **full width at every width**, because it sits in its own `KpiGrid columns={1}` above the
         * subordinate figures rather than spanning cells inside theirs. That is what keeps it safe at 320 px:
         * « 4 250,000 DT » at `text-3xl` measures ~210 px against ~144 px for half of a two-column grid, so the
         * figure itself would have been the thing that overflowed. It also puts the section's answer first in
         * reading order, which is the entire point of having a lead.
         */
        /*
         * ⚠️ `items-start` is not optional. A flex column stretches its children, so `DeltaBadge`'s `inline-flex`
         * pill was painted the full width of the cell — a 300 px « Pas de comparaison » lozenge under the figure.
         * The text block takes `w-full` back so long labels still wrap across the cell rather than against the
         * longest word.
         */
        isLead ? "flex flex-col items-start justify-center" : "block",
        // An urgent figure gets a left accent rather than a tinted box: at this density a filled background on one
        // cell of a shared surface reads as a rendering fault, while a 2px edge reads as emphasis.
        variant === "urgent" && "before:absolute before:inset-y-0 before:left-0 before:w-[2px] before:bg-destructive",
      )}
      aria-label={`${label} : ${value}. Voir le détail.`}
    >
      <div className={cn("min-w-0 space-y-1", isLead && "w-full")}>
        {/* A chip, not a bare glyph: an icon in the same grey as the label beside it is just more grey, and the
            wash gives the row an anchor the eye can land on (`/documents`' tile idiom). Out of `compact` on
            purpose — six chips down a dense block compete with the numbers they belong to. */}
        <p
          className={cn(
            "flex items-center gap-2 font-medium text-muted-foreground",
            isCompact ? "text-xs" : "text-sm",
          )}
        >
          {isCompact ? (
            <span
              aria-hidden="true"
              className={cn(
                "size-1.5 shrink-0 rounded-full",
                variant === "urgent" ? "bg-destructive" : "bg-primary/70",
              )}
            />
          ) : (
            <span
              aria-hidden="true"
              className={cn(
                "flex size-7 shrink-0 items-center justify-center rounded-lg transition-colors",
                variant === "urgent"
                  ? "bg-destructive-wash text-destructive"
                  : "bg-primary/10 text-primary group-hover:bg-primary/15",
              )}
            >
              <Icon className="size-4" strokeWidth={1.75} />
            </span>
          )}
          {/*
            Wraps rather than truncates. In two phone columns « Nouveaux patients » and « Taux d'absence »
            are wider than their cell, and a truncated KPI label is worse than a two-line one: « Nouveaux
            pat… » names nothing, and the label IS the figure's name (the accessible label reads it out).
          */}
          <span className="min-w-0 [overflow-wrap:anywhere]">{label}</span>
        </p>
        {loading ? (
          <span
            className={cn("block animate-pulse rounded bg-muted", isLead ? "h-9 w-40" : "h-8 w-20")}
            aria-label="Chargement"
          />
        ) : (
          <p
            className={cn(
              "font-semibold tabular-nums tracking-tight text-foreground",
              // One step down on a phone. « 1 840,000 DT » at `text-2xl` measures ~110 px against ~104 px of
              // content box in a 320 px two-column cell — it would be the figure itself that overflows.
              isCompact ? "text-lg sm:text-xl" : isLead ? "text-2xl sm:text-3xl" : "text-xl sm:text-2xl",
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
