"use client"

import Link from "next/link"
import { ArrowDown, ArrowUp, Minus, type LucideIcon } from "lucide-react"
import { cn } from "@/lib/utils"
import type { PeriodComparison } from "@/lib/api/types"
import type { DeltaSense } from "@/components/dashboard/kpi-card"

interface HeroKpiProps {
  label: string
  description: string
  /** Pre-formatted, like every other figure — money through `formatDT`. */
  value: string
  icon: LucideIcon
  href: string
  comparison?: PeriodComparison
  sense?: DeltaSense
  previousPeriodLabel?: string
  loading?: boolean
  /**
   * Values for the inline sparkline, oldest first — the collected trend.
   *
   * <p>It lives here because the trend's *shape* answers the same question the hero's number does (« comment va le
   * cabinet ? »), and the reader should not have to travel 400 px down the page for it. The full six-month chart
   * stays below for reading actual values; this carries direction only, which is why it has no axis and no labels.</p>
   */
  sparkline?: number[]
}

/**
 * The dashboard's hero figure, and the page's **one** filled accent surface.
 *
 * <p>Why one, and why filled. The screen read as washed out because `--primary` had a respectable chroma and
 * filled nothing on screen — an accent that only ever tints a 28 px button is an accent on paper. A single
 * saturated panel gives the page its pop while every neutral around it stays quiet. The alternative — a hue per
 * section — spends the same colour budget on making nothing important.</p>
 *
 * <p>It renders <b>outside</b> `KpiGrid` rather than as a `col-span-2` cell inside it. A filled cell in a hairline
 * grid reads as a rendering fault, and the grid's shared border would cut across the panel's own edge.</p>
 *
 * <p>Its own file, not a branch inside `KpiCard`: the two share no markup at all beyond being a `Link`, and folding
 * a second full layout into that component's prop set is how a card ends up with an `isHero` ternary on every line
 * (which is exactly what it had).</p>
 */
export function HeroKpi({
  label,
  description,
  value,
  icon: Icon,
  href,
  comparison,
  sense = "up-is-good",
  previousPeriodLabel,
  loading = false,
  sparkline,
}: HeroKpiProps) {
  const delta = comparison?.deltaPercent
  const hasDelta = delta !== null && delta !== undefined
  const rising = (delta ?? 0) > 0
  const flat = delta === 0
  const Arrow = flat ? Minus : rising ? ArrowUp : ArrowDown
  const favourable = sense === "neutral" ? null : rising === (sense === "up-is-good")

  return (
    <Link
      href={href}
      className={cn(
        "group relative flex flex-col justify-between gap-4 overflow-hidden rounded-xl p-5",
        "text-hero-foreground shadow-lg shadow-primary/25",
        // Two layers: a radial glow from the top-right corner over a diagonal ramp. The glow is what stops a large
        // filled area from reading as a flat colour swatch.
        "bg-[radial-gradient(120%_90%_at_100%_0%,var(--hero-glow)_0%,transparent_62%),linear-gradient(158deg,var(--hero-from)_0%,var(--hero-to)_100%)]",
        "transition-shadow hover-hover:hover:shadow-xl hover-hover:hover:shadow-primary/30",
        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background",
      )}
      aria-label={`${label} : ${value}. Voir le détail.`}
    >
      {/* Watermark, at 10% and bleeding off two edges — an icon this size is texture, not signage. */}
      <Icon
        aria-hidden="true"
        strokeWidth={1.25}
        className="pointer-events-none absolute -bottom-6 -right-5 size-32 opacity-10"
      />

      <div className="relative">
        <div className="flex items-center justify-between gap-3">
          <p className="text-[13px] font-semibold tracking-wide opacity-85">{label}</p>

          {/* On a saturated ground the delta pill is translucent white rather than green/red: a semantic wash would
              fight the surface, and the arrow plus the signed number already carry the direction. The favourable /
              unfavourable reading is given to assistive tech below instead of being encoded in a colour nobody can
              see against teal. */}
          {comparison && !loading && (
            <span className="inline-flex shrink-0 items-center gap-1 rounded-full border border-white/25 bg-white/20 px-2 py-0.5 text-xs font-semibold tabular-nums">
              {hasDelta ? (
                <>
                  <Arrow className="size-3.5" aria-hidden="true" />
                  <span>
                    {delta.toLocaleString("fr-TN", {
                      signDisplay: "exceptZero",
                      minimumFractionDigits: 1,
                      maximumFractionDigits: 1,
                    })}{" "}
                    %
                  </span>
                </>
              ) : (
                <>
                  <Minus className="size-3.5" aria-hidden="true" />
                  <span>Pas de comparaison</span>
                </>
              )}
            </span>
          )}
        </div>

        {loading ? (
          <span className="mt-2 block h-11 w-44 animate-pulse rounded bg-white/20" aria-label="Chargement" />
        ) : (
          <p className="mt-1.5 text-4xl font-bold tabular-nums tracking-tighter">{value}</p>
        )}

        <p className="mt-2 text-xs opacity-75">
          {description}
          {comparison && previousPeriodLabel ? ` · vs. ${previousPeriodLabel}` : ""}
        </p>

        {hasDelta && favourable !== null && (
          <span className="sr-only">{favourable ? "Évolution favorable." : "Évolution défavorable."}</span>
        )}
      </div>

      {sparkline && sparkline.length > 1 && !loading && <Sparkline values={sparkline} />}
    </Link>
  )
}

/**
 * Direction only — no axis, no labels, no tooltip. « Tendance » below is where values are read; this says
 * « ça monte » in 46 px, which is the one thing the hero's own number cannot say about itself.
 *
 * <p><b>`aria-hidden`</b>: it restates a series the trend chart already exposes with a full accessible label *and* a
 * table view, so announcing it twice is noise.</p>
 */
function Sparkline({ values }: { values: number[] }) {
  const width = 180
  const height = 46
  const min = Math.min(...values)
  const max = Math.max(...values)
  // A flat series would divide by zero; drawing it through the middle is the honest rendering of "no variation".
  const span = max - min || 1
  const step = width / (values.length - 1)
  const points = values.map(
    (v, i) => `${(i * step).toFixed(1)},${(height - 6 - ((v - min) / span) * (height - 12)).toFixed(1)}`,
  )
  const [lastX, lastY] = points[points.length - 1].split(",")

  return (
    <svg
      viewBox={`0 0 ${width} ${height}`}
      preserveAspectRatio="none"
      className="relative block h-[46px] w-full"
      aria-hidden="true"
    >
      <defs>
        <linearGradient id="heroSparkWash" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="currentColor" stopOpacity={0.38} />
          <stop offset="100%" stopColor="currentColor" stopOpacity={0} />
        </linearGradient>
      </defs>
      <path d={`M${points.join(" ")} L${width},${height} L0,${height} Z`} fill="url(#heroSparkWash)" />
      <polyline
        points={points.join(" ")}
        fill="none"
        stroke="currentColor"
        strokeWidth={2}
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <circle cx={lastX} cy={lastY} r={3.2} fill="currentColor" />
    </svg>
  )
}
