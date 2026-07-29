"use client"

import Link from "next/link"
import { ArrowDown, ArrowUp, Minus, type LucideIcon } from "lucide-react"
import { Card, CardContent } from "@/components/ui/card"
import { cn } from "@/lib/utils"
import type { PeriodComparison } from "@/lib/api/types"

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
}

/**
 * One dashboard figure: a link to the records it counted, with its comparison against the previous period.
 *
 * <p>Always a `Link`. That is the feature — the retired `StatsCard` made `href` optional and four of seven cards
 * pointed at an unfiltered list, so the number and its destination disagreed.</p>
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
}: KpiCardProps) {
  return (
    <Link
      href={href}
      className="block rounded-xl focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      aria-label={`${label} : ${value}. Voir le détail.`}
    >
      <Card
        className={cn(
          "h-full transition-colors hover:border-accent hover:bg-accent/40",
          variant === "urgent" && "border-destructive/50 bg-destructive/5",
        )}
      >
        <CardContent className="p-5">
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0 space-y-1">
              <p className="text-sm font-medium text-muted-foreground">{label}</p>
              {loading ? (
                <span className="block h-8 w-20 animate-pulse rounded bg-muted" aria-label="Chargement" />
              ) : (
                <p className="text-2xl font-semibold text-foreground">{value}</p>
              )}
              <p className="text-xs text-muted-foreground">{description}</p>
            </div>
            <div
              className={cn(
                "flex h-10 w-10 shrink-0 items-center justify-center rounded-lg",
                variant === "urgent" ? "bg-destructive/10" : "bg-accent",
              )}
            >
              <Icon
                className={cn("h-5 w-5", variant === "urgent" ? "text-destructive" : "text-accent-foreground")}
                aria-hidden="true"
              />
            </div>
          </div>

          {comparison && !loading && (
            <DeltaBadge comparison={comparison} sense={sense} previousPeriodLabel={previousPeriodLabel} />
          )}
        </CardContent>
      </Card>
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
      <p className="mt-3 flex items-center gap-1.5 text-xs text-muted-foreground">
        <Minus className="h-3.5 w-3.5" aria-hidden="true" />
        <span>Pas de comparaison — {baseline}</span>
      </p>
    )
  }

  const rising = deltaPercent > 0
  const flat = deltaPercent === 0
  const good = sense === "neutral" ? null : rising === (sense === "up-is-good")

  const tone =
    flat || good === null
      ? "text-muted-foreground"
      : good
        ? "text-green-700 dark:text-green-400"
        : "text-destructive"

  const Arrow = flat ? Minus : rising ? ArrowUp : ArrowDown
  // Intl gives the French decimal comma and an explicit sign, so « +18,3 % » reads natively.
  const formatted = `${deltaPercent.toLocaleString("fr-TN", {
    signDisplay: "exceptZero",
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
  })} %`

  return (
    <p className={cn("mt-3 flex items-center gap-1.5 text-xs font-medium", tone)}>
      <Arrow className="h-3.5 w-3.5" aria-hidden="true" />
      <span>
        {formatted} <span className="font-normal text-muted-foreground">{baseline}</span>
      </span>
    </p>
  )
}
