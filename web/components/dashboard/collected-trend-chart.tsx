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
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { ZONES, zoneChipClass } from "@/lib/zones"
import { formatDT } from "@/lib/format"
import type { MonthlyCollectedPointDto } from "@/lib/api/types"
import { formatMonthLong, formatMonthShort } from "./dashboard-labels"

/**
 * The series colour. A single series, so it takes categorical slot 1 (`--chart-1`) — validated at 3:1+ against both
 * the light (`#ffffff`) and dark (`#0d161c`) card surfaces, inside the lightness band and above the chroma floor in
 * both modes. One hue for magnitude over time; no ramp, because a value-ramp on a time axis would double-encode the
 * height as colour and burn the only free channel on information the line already shows.
 *
 * <p>Read through the CSS custom property rather than hardcoded, so the chart follows the app's theme tokens — the
 * light and dark steps swap at the token, not in this file.</p>
 */
const SERIES_COLOR = "var(--chart-1)"

/** Axis / grid ink. Recessive, from text tokens — never the series colour (a data hue is illegible as text). */
const AXIS_COLOR = "var(--muted-foreground)"
const GRID_COLOR = "var(--border)"

interface CollectedTrendChartProps {
  points: MonthlyCollectedPointDto[]
  loading?: boolean
}

/**
 * « Tendance » — six months of collected cash.
 *
 * <p>Built to the project's first chart conventions: one axis (never a second y-scale), a 2px line with a ~10% wash,
 * hairline solid gridlines one step off the surface, an 8px end-marker with a 2px surface ring, the value direct-
 * labelled only at the endpoint (a number on every point goes unread), and a crosshair tooltip whose value leads and
 * label follows. A table view is always reachable, so no value is gated behind hovering.</p>
 *
 * <p>No legend: there is one series and the card title names it. A one-swatch legend restates the title.</p>
 */
export function CollectedTrendChart({ points, loading = false }: CollectedTrendChartProps) {
  const [showTable, setShowTable] = useState(false)

  const total = points.reduce((sum, p) => sum + p.collected, 0)
  const latest = points.length > 0 ? points[points.length - 1] : null

  return (
    <Card>
      <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3 pb-2">
        <div className="space-y-1">
          <CardTitle className="text-base font-semibold">Encaissé — 6 derniers mois</CardTitle>
          <p className="text-xs text-muted-foreground">
            Paiements de notes d’honoraires, par mois. Total sur la période : {formatDT(total)}.
          </p>
        </div>
        <Button
          type="button"
          size="sm"
          variant="outline"
          aria-expanded={showTable}
          onClick={() => setShowTable((open) => !open)}
        >
          {showTable ? "Masquer le tableau" : "Afficher le tableau"}
        </Button>
      </CardHeader>

      <CardContent className="space-y-4">
        {loading ? (
          <div className="h-52 animate-pulse rounded-lg bg-muted" aria-label="Chargement du graphique" />
        ) : points.length === 0 ? (
          /*
           * An empty branch, which this chart had none of.
           *
           * With zero points it rendered `<AreaChart data={[]}>` under a header reading « Total sur la période :
           * 0,000 DT » — axes, gridlines and no line. A new clinic's first month is indistinguishable from a
           * broken chart, and that is the one moment a user has no basis to tell the difference.
           */
          <EmptyState
            icon={TrendingUp}
            size="compact"
            className="h-52"
            title="Pas encore d'encaissements"
            description="La courbe apparaîtra dès le premier paiement enregistré sur une note d'honoraires."
            chipClassName={zoneChipClass(ZONES.money)}
          />
        ) : (
          <>
            {/* Height covers the plot AND the x-axis band, so the card never grows a nested scrollbar. */}
            <div className="h-52 w-full">
              <ResponsiveContainer width="100%" height="100%">
                <AreaChart data={points} margin={{ top: 8, right: 16, bottom: 4, left: 4 }}>
                  <defs>
                    {/* The wash: the series hue fading to nothing. A saturated block would out-weigh the line.
                        Raised from 0.16 to 0.26 when the ground became tinted — at 0.16 against a tinted ground the
                        fill was very nearly invisible, which left a bare stroke floating in an empty plot. */}
                    <linearGradient id="collectedWash" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="0%" stopColor={SERIES_COLOR} stopOpacity={0.26} />
                      <stop offset="100%" stopColor={SERIES_COLOR} stopOpacity={0.02} />
                    </linearGradient>
                  </defs>

                  {/* Horizontal hairlines only — solid, never dashed (dashing reads as a threshold). */}
                  <CartesianGrid stroke={GRID_COLOR} strokeWidth={1} vertical={false} />

                  <XAxis
                    dataKey="month"
                    tickFormatter={formatMonthShort}
                    // Monospace, matching the section eyebrows: the axis is data, and a tabular face keeps six
                    // month labels optically evenly spaced rather than drifting with their letter widths.
                    tick={{ fill: AXIS_COLOR, fontSize: 11, fontFamily: "var(--font-mono)" }}
                    stroke={GRID_COLOR}
                    tickLine={false}
                    axisLine={{ stroke: GRID_COLOR }}
                  />
                  <YAxis
                    // Clean rounded ticks — they carry the values that are not direct-labelled.
                    tickFormatter={(value: number) => value.toLocaleString("fr-TN", { notation: "compact" })}
                    tick={{ fill: AXIS_COLOR, fontSize: 11, fontFamily: "var(--font-mono)" }}
                    width={52}
                    tickLine={false}
                    axisLine={false}
                  />

                  <Tooltip
                    // The crosshair finds the X: readers aim at a month, never at a 2px line.
                    cursor={{ stroke: AXIS_COLOR, strokeWidth: 1 }}
                    content={<TrendTooltip />}
                  />

                  <Area
                    type="monotone"
                    dataKey="collected"
                    stroke={SERIES_COLOR}
                    strokeWidth={2.4}
                    strokeLinejoin="round"
                    strokeLinecap="round"
                    fill="url(#collectedWash)"
                    // 8px marker (r=4) with a 2px surface ring, so it stays legible where it crosses the line.
                    activeDot={{ r: 4, fill: SERIES_COLOR, stroke: "var(--card)", strokeWidth: 2 }}
                    /*
                     * The **endpoint** carries a permanent marker; every other point stays bare.
                     *
                     * A dot on all six is the chart-junk default and makes the line read as a connect-the-dots
                     * exercise. But the last month is the one being asked about — it is the figure the hero above
                     * reports — so it earns a fixed anchor the eye can land on without hovering. Returning `null`
                     * for the others rather than `false` keeps recharts' own typing happy.
                     */
                    dot={(props: { key?: string; cx?: number; cy?: number; index?: number }) =>
                      props.index === points.length - 1 ? (
                        <circle
                          key={props.key ?? "trend-endpoint"}
                          cx={props.cx}
                          cy={props.cy}
                          r={5}
                          fill={SERIES_COLOR}
                          stroke="var(--card)"
                          strokeWidth={2.5}
                        />
                      ) : (
                        <g key={props.key ?? `trend-dot-${props.index}`} />
                      )
                    }
                    isAnimationActive={false}
                  />
                </AreaChart>
              </ResponsiveContainer>
            </div>

            {/* The one direct label: the endpoint. Labelling every point is chaos and goes unread. */}
            {latest && (
              <p className="text-xs text-muted-foreground">
                <span className="font-medium text-foreground">{formatMonthLong(latest.month)}</span> :{" "}
                {formatDT(latest.collected)}
              </p>
            )}
          </>
        )}

        {/* The table view: every value reachable without hovering, and the WCAG-clean equivalent of the plot. */}
        {showTable && !loading && (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Mois</TableHead>
                <TableHead className="text-right">Encaissé</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {points.map((point) => (
                <TableRow key={point.month}>
                  <TableCell>{formatMonthLong(point.month)}</TableCell>
                  <TableCell className="text-right tabular-nums">{formatDT(point.collected)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  )
}

/**
 * The hover readout. The value leads and the month follows — the legend's hierarchy inverted, because here the reader
 * already knows the series and wants the number. The series is keyed by a short stroke, not a filled box.
 */
function TrendTooltip({
  active,
  payload,
}: {
  active?: boolean
  payload?: Array<{ payload: MonthlyCollectedPointDto }>
}) {
  if (!active || !payload?.length) return null

  const point = payload[0].payload

  return (
    <div className="rounded-lg border bg-card px-3 py-2 shadow-sm">
      <p className="text-sm font-semibold text-foreground tabular-nums">{formatDT(point.collected)}</p>
      <p className="mt-0.5 flex items-center gap-1.5 text-xs text-muted-foreground">
        <span
          aria-hidden="true"
          className="inline-block h-0.5 w-3 rounded-full"
          style={{ backgroundColor: SERIES_COLOR }}
        />
        {formatMonthLong(point.month)}
      </p>
    </div>
  )
}
