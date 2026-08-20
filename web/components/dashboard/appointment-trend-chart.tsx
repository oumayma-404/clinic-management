"use client"

import { useState } from "react"
import {
  Area,
  AreaChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts"
import { TrendingUp } from "lucide-react"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { EmptyState } from "@/components/ui/empty-state"
import { ModeSegmented } from "@/components/ui/mode-segmented"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { cn } from "@/lib/utils"
import type { MonthlyAppointmentPointDto } from "@/lib/api/types"
import { formatMonthLong, formatMonthShort } from "./dashboard-labels"

/**
 * The series colour — categorical slot 1, the same hue « Encaissé — 6 derniers mois » uses.
 *
 * <p>Deliberately <b>not</b> one of the `--appt-*` pastels. Those five encode a *status*, and this line is a count
 * of every séance whatever its outcome — painting it in « À venir »'s azur would say the two mean the same thing
 * while sitting a card apart. One series, so no legend: the card title names it.</p>
 */
const SERIES_COLOR = "var(--chart-1)"
const AXIS_COLOR = "var(--muted-foreground)"
const GRID_COLOR = "var(--border)"

/** Which count the line plots. Two views of one read — the response carries both. */
type Measure = "total" | "completed"

interface AppointmentTrendChartProps {
  points: MonthlyAppointmentPointDto[]
  loading?: boolean
}

/**
 * « Rendez-vous — 6 derniers mois »: is the practice getting busier?
 *
 * <p>Built as the twin of <c>collected-trend-chart.tsx</c> on purpose — same 2.4 px line, same ~26 % wash, same
 * hairline grid, same crosshair, same table view. Two trend charts a card apart that disagreed about what a trend
 * chart looks like would read as two different products.</p>
 *
 * <h3>The one thing it does that the money trend does not</h3>
 * <p>It says when the last month is <b>incomplete</b>. On the 3rd of a month that point holds three days of work
 * beside five whole months and draws a cliff that is not in the data — so it gets a hollow ring instead of a
 * filled marker, and the caption says how far through the month the figure goes. `isPartial` comes from the
 * server, which is the only side that knows the clinic's today.</p>
 *
 * <h3>Why the measure is a toggle</h3>
 * <p>« Combien de rendez-vous » has two honest answers — every séance in the book, or only the honoured ones — and
 * they diverge exactly when it matters (a month with many cancellations). Both come from one read, so the toggle
 * costs no request; « Tous » leads because the question is usually about volume. Same shape as
 * « Répartition des actes »' Durée / Nombre.</p>
 */
export function AppointmentTrendChart({ points, loading = false }: AppointmentTrendChartProps) {
  const [measure, setMeasure] = useState<Measure>("total")
  const [showTable, setShowTable] = useState(false)

  const total = points.reduce((sum, p) => sum + p[measure], 0)
  const latest = points.length > 0 ? points[points.length - 1] : null
  const hasAnything = points.some((p) => p.total > 0)

  return (
    // Matches the status chart beside it: `flex flex-col` here plus a flexing plot below means the shorter card's
    // chart grows into the row's height instead of leaving a slab of empty surface under it.
    <Card className="flex min-w-0 flex-col">
      <CardHeader className="gap-3">
        <div className="flex flex-wrap items-start justify-between gap-x-3 gap-y-2">
          <div className="min-w-0 space-y-1">
            <CardTitle className="flex items-center gap-2.5 text-base font-semibold">
              <span
                aria-hidden="true"
                className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary"
              >
                <TrendingUp className="size-4" strokeWidth={1.75} />
              </span>
              Rendez-vous — 6 derniers mois
            </CardTitle>
            <p className="text-xs text-muted-foreground">
              {measure === "total" ? "Séances au livre, par mois." : "Séances honorées, par mois."}{" "}
              {total.toLocaleString("fr-TN")} sur la période.
            </p>
          </div>

          {hasAnything && (
            <Button
              type="button"
              size="sm"
              variant="outline"
              aria-expanded={showTable}
              onClick={() => setShowTable((open) => !open)}
            >
              {showTable ? "Masquer le tableau" : "Afficher le tableau"}
            </Button>
          )}
        </div>

        <ModeSegmented<Measure>
          value={measure}
          onChange={setMeasure}
          ariaLabel="Mesure de la courbe"
          size="sm"
          options={[
            { value: "total", label: "Tous" },
            { value: "completed", label: "Honorés" },
          ]}
        />
      </CardHeader>

      <CardContent className="flex min-w-0 flex-1 flex-col gap-4">
        {loading ? (
          <div className="min-h-44 flex-1 animate-pulse rounded-lg bg-muted" aria-label="Chargement du graphique" />
        ) : !hasAnything ? (
          /* Never axes and a flat line under « 0 sur la période » — a new clinic's first month must not be
             indistinguishable from a broken chart. */
          <EmptyState
            icon={TrendingUp}
            size="compact"
            className="flex-1"
            title="Pas encore de rendez-vous"
            description="La courbe apparaîtra dès le premier rendez-vous enregistré."
          />
        ) : (
          <>
            {/* Out of flow for the ratchet reason in `appointment-status-chart.tsx` — same construct here. */}
            <div className="relative min-h-44 flex-1">
              <div className="absolute inset-0">
              <ResponsiveContainer width="100%" height="100%">
                <AreaChart data={points} margin={{ top: 8, right: 12, bottom: 4, left: 0 }}>
                  <defs>
                    {/* The wash: the series hue fading out. A saturated block would out-weigh the line itself. */}
                    <linearGradient id="apptTrendWash" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="0%" stopColor={SERIES_COLOR} stopOpacity={0.26} />
                      <stop offset="100%" stopColor={SERIES_COLOR} stopOpacity={0.02} />
                    </linearGradient>
                  </defs>

                  <CartesianGrid stroke={GRID_COLOR} strokeWidth={1} vertical={false} />

                  <XAxis
                    dataKey="month"
                    tickFormatter={formatMonthShort}
                    tick={{ fill: AXIS_COLOR, fontSize: 11, fontFamily: "var(--font-mono)" }}
                    stroke={GRID_COLOR}
                    tickLine={false}
                    axisLine={{ stroke: GRID_COLOR }}
                  />
                  <YAxis
                    // Whole appointments only — a fractional tick on a small domain is nonsense here.
                    allowDecimals={false}
                    tick={{ fill: AXIS_COLOR, fontSize: 11, fontFamily: "var(--font-mono)" }}
                    width={34}
                    tickLine={false}
                    axisLine={false}
                  />

                  <Tooltip
                    cursor={{ stroke: AXIS_COLOR, strokeWidth: 1 }}
                    content={<TrendTooltip measure={measure} />}
                  />

                  <Area
                    type="monotone"
                    dataKey={measure}
                    stroke={SERIES_COLOR}
                    strokeWidth={2.4}
                    strokeLinejoin="round"
                    strokeLinecap="round"
                    fill="url(#apptTrendWash)"
                    activeDot={{ r: 4, fill: SERIES_COLOR, stroke: "var(--card)", strokeWidth: 2 }}
                    /*
                     * Only the endpoint carries a permanent marker — a dot on all six makes the line read as a
                     * connect-the-dots exercise — and a HOLLOW one when that month is still running, so the
                     * shape itself says the last figure is not comparable with its neighbours.
                     */
                    dot={(props: { key?: string; cx?: number; cy?: number; index?: number }) => {
                      const isLast = props.index === points.length - 1
                      if (!isLast) return <g key={props.key ?? `appt-dot-${props.index}`} />
                      const partial = points[points.length - 1]?.isPartial
                      return (
                        <circle
                          key={props.key ?? "appt-endpoint"}
                          cx={props.cx}
                          cy={props.cy}
                          r={5}
                          fill={partial ? "var(--card)" : SERIES_COLOR}
                          stroke={partial ? SERIES_COLOR : "var(--card)"}
                          strokeWidth={2.5}
                        />
                      )
                    }}
                    isAnimationActive={false}
                  />
                </AreaChart>
              </ResponsiveContainer>
              </div>
            </div>

            {/* The one direct label: the endpoint, and whether it is a whole month. */}
            {latest && (
              <p className="text-xs text-muted-foreground">
                <span className="font-medium text-foreground">{formatMonthLong(latest.month)}</span> :{" "}
                {latest[measure].toLocaleString("fr-TN")}{" "}
                {latest[measure] === 1 ? "rendez-vous" : "rendez-vous"}
                {latest.isPartial && (
                  <span className="text-warning-ink"> — mois en cours, pas encore complet</span>
                )}
              </p>
            )}
          </>
        )}

        {/* Two columns, and the table IS this chart's accessible fallback — see the argued entry in
            `check-responsive.mjs`'s CARD_FALLBACK_EXEMPT, which its twin already carries. */}
        {showTable && !loading && hasAnything && (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Mois</TableHead>
                <TableHead className="text-right">{measure === "total" ? "Rendez-vous" : "Honorés"}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {points.map((point) => (
                <TableRow key={point.month}>
                  <TableCell className={cn(point.isPartial && "text-muted-foreground")}>
                    {formatMonthLong(point.month)}
                    {point.isPartial && " (en cours)"}
                  </TableCell>
                  <TableCell className="text-right tabular-nums">
                    {point[measure].toLocaleString("fr-TN")}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  )
}

/** The hover readout. The value leads and the month follows — the reader already knows the series. */
function TrendTooltip({
  active,
  payload,
  measure,
}: {
  active?: boolean
  payload?: Array<{ payload: MonthlyAppointmentPointDto }>
  measure: Measure
}) {
  if (!active || !payload?.length) return null
  const point = payload[0].payload

  return (
    <div className="rounded-lg border bg-card px-3 py-2 shadow-sm">
      <p className="text-sm font-semibold tabular-nums text-foreground">
        {point[measure].toLocaleString("fr-TN")} {point[measure] === 1 ? "rendez-vous" : "rendez-vous"}
      </p>
      <p className="mt-0.5 flex items-center gap-1.5 text-xs text-muted-foreground">
        <span
          aria-hidden="true"
          className="inline-block h-0.5 w-3 rounded-full"
          style={{ backgroundColor: SERIES_COLOR }}
        />
        {formatMonthLong(point.month)}
      </p>
      {/* Said in the tooltip too: a reader who hovers the last point is the one most likely to compare it. */}
      {point.isPartial && <p className="mt-1 text-2xs text-warning-ink">Mois en cours, pas encore complet</p>}
    </div>
  )
}
