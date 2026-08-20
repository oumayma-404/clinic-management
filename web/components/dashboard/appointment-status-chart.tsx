"use client"

import { useMemo, useState } from "react"
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts"
import { AlertCircle, CalendarRange, TrendingDown, TrendingUp } from "lucide-react"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { EmptyState } from "@/components/ui/empty-state"
import { ModeSegmented } from "@/components/ui/mode-segmented"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { CardList, CARDS_ONLY, TABLE_ONLY } from "@/components/ui/card-list"
import { todayLocalIso } from "@/lib/format"
import { cn } from "@/lib/utils"
import type { AppointmentStatusBucketDto, AppointmentStatusMixDto } from "@/lib/api/types"
import {
  APPOINTMENT_STATUS_CLASSES,
  APPOINTMENT_STATUS_CLASS_HINTS,
  GRANULARITY_LABELS,
  bucketLabel,
  comparedToDaysLabel,
  dayCountBetween,
  windowLabel,
  type AppointmentStatusClassKey,
} from "./dashboard-labels"

/** Which window the card is showing. `custom` is the only one that needs the two date fields. */
export type StatusWindowMode = "week" | "month" | "custom"

/**
 * The narrowest a column may be drawn before it stops reading as a column.
 *
 * <p>Below ~10 px a stacked column's smaller segments are sub-pixel and the whole thing reads as noise. So the
 * plot claims `bucketCount × (COLUMN_MIN + GAP)` and <b>scrolls inside its own container</b> when that exceeds the
 * card — § 11 of the device rules. The alternative would be to coarsen the buckets on a narrow screen, which is a
 * layout decision removing a capability, and § 0 forbids exactly that.</p>
 */
const COLUMN_MIN_PX = 12
const COLUMN_GAP_PX = 4

/** The widest a column is drawn. 7 buckets across a desktop card is otherwise a 55 px saturated slab. */
const COLUMN_MAX_PX = 34

const AXIS_COLOR = "var(--muted-foreground)"
const GRID_COLOR = "var(--border)"

interface AppointmentStatusChartProps {
  data: AppointmentStatusMixDto | null
  loading?: boolean
  refetching?: boolean
  error?: string | null
  mode: StatusWindowMode
  onModeChange: (mode: StatusWindowMode) => void
  /** Applies a custom range. Both are `yyyy-MM-dd` clinic-local day keys. */
  onCustomRange: (from: string, to: string) => void
  customFrom?: string
  customTo?: string
  onRetry?: () => void
}

/**
 * « Rendez-vous par statut » — how many séances the window held, and what became of them.
 *
 * <h3>Why stacked columns</h3>
 * <p>The reader has two questions at once — how busy was each day, and how did those visits turn out — and a
 * stack is the one form that answers both on one axis. A donut loses the time entirely (and would make the period
 * control pointless), grouped bars bury the day's total under five smaller marks, and a 100 % stack hides volume,
 * which is half the question: a week of 12 and a week of 76 would draw the same column.</p>
 *
 * <h3>Five classes, not seven</h3>
 * <p>The server folds the seven appointment statuses into five (see <c>AppointmentStatusClass</c>) because seven
 * fills side by side cannot be told apart — measured, not assumed. The Planifié/Confirmé distinction the fold
 * gives up comes back as the legend's « dont N confirmés », which is where a footnote belongs.</p>
 *
 * <h3>The pastel fills, and what licenses them</h3>
 * <p>Every fill sits below 3:1 against the card — that is what pastel means, and it is admitted rather than
 * accidental. It is legal because the relief the rule demands ships with it: a same-hue `-edge` hairline gives
 * each segment a boundary a pale fill would otherwise lack, the legend <b>names and counts</b> all five, and the
 * table view carries every value. Remove any of those three and the palette is a defect.</p>
 */
export function AppointmentStatusChart({
  data,
  loading = false,
  refetching = false,
  error = null,
  mode,
  onModeChange,
  onCustomRange,
  customFrom,
  customTo,
  onRetry,
}: AppointmentStatusChartProps) {
  const [showTable, setShowTable] = useState(false)

  /*
   * The custom range is a DRAFT until « Appliquer ».
   *
   * Binding the inputs straight to the query would fire a request per keystroke — and worse, a half-typed year
   * ("202") is a valid-looking day key the server would answer for. So the fields are local and only a deliberate
   * apply moves the window.
   */
  const [draftFrom, setDraftFrom] = useState(customFrom ?? todayLocalIso())
  const [draftTo, setDraftTo] = useState(customTo ?? todayLocalIso())
  const draftInverted = Boolean(draftFrom && draftTo && draftTo < draftFrom)

  const buckets = data?.buckets ?? []
  const granularity = data?.granularity ?? "Day"

  /*
   * Which class sits at the top of each column, so only that segment gets the rounded data-end.
   *
   * Computed rather than assumed to be the last declared class: a column with no absences has « Annulé » on top,
   * and rounding a class that is not there would round a segment of height zero while the real top stayed square.
   */
  const rows = useMemo(
    () =>
      buckets.map((b) => ({
        ...b,
        label: bucketLabel(b.start, b.endInclusive, granularity),
        topClass: [...APPOINTMENT_STATUS_CLASSES].reverse().find((c) => b[c.key] > 0)?.key ?? null,
      })),
    [buckets, granularity],
  )

  const hasAnything = data !== null && data.total > 0
  /** The plot's own width demand. Scrolls in its own container when it exceeds the card. */
  const trackWidth = rows.length * (COLUMN_MIN_PX + COLUMN_GAP_PX)

  const delta = useMemo(() => {
    if (!data || data.previousTotal === 0) return null
    return Math.round(((data.total - data.previousTotal) / data.previousTotal) * 100)
  }, [data])

  const dayCount = data ? dayCountBetween(data.from, data.toInclusive) : 0

  return (
    /*
      `flex flex-col` + a flexing plot is what keeps this card and the trend beside it the same height: the taller
      one sets the row and the other's plot grows into it, instead of one card trailing a block of empty surface.

      ⚠️ `min-w-0` is not tidying — without it this card overflowed a 390 px viewport and the PAGE scrolled
      sideways. A grid item's default `min-width: auto` refuses to shrink below its content's min-content width,
      and the scroll track below declares 31 × 16 px of it, so the intrinsic width propagated all the way up
      through the card and out of the layout. Every ancestor between here and the `overflow-x-auto` box needs the
      same release, which is why it appears three times in this file.
    */
    <Card className="flex min-w-0 flex-col">
      <CardHeader className="gap-3">
        <div className="flex flex-wrap items-start justify-between gap-x-3 gap-y-2">
          <div className="min-w-0 space-y-1">
            <CardTitle className="flex items-center gap-2.5 text-base font-semibold">
              <span
                aria-hidden="true"
                className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary"
              >
                <CalendarRange className="size-4" strokeWidth={1.75} />
              </span>
              Rendez-vous par statut
            </CardTitle>
            {/* The card states its OWN window in words. It is the one block whose period is not the page's, so
                « Cette semaine » on its control is a button, not a claim about which days are counted. */}
            <p className="font-mono text-xs text-muted-foreground">
              {data ? `${GRANULARITY_LABELS[granularity]} · ${windowLabel(data.from, data.toInclusive)}` : " "}
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

        {/* Full width below `sm:`, so three French labels never overflow a 320 px card. */}
        <ModeSegmented<StatusWindowMode>
          value={mode}
          onChange={onModeChange}
          ariaLabel="Période des rendez-vous par statut"
          size="sm"
          options={[
            { value: "week", label: "Cette semaine" },
            { value: "month", label: "Ce mois" },
            { value: "custom", label: "Personnalisé" },
          ]}
        />

        {mode === "custom" && (
          <div className="grid gap-3 rounded-lg border bg-muted/40 p-3">
            <div className="grid gap-3 sm:grid-cols-2">
              <div className="space-y-1.5">
                <Label htmlFor="appt-status-from" className="text-xs text-muted-foreground">
                  Du
                </Label>
                {/* A real date input, so it inherits the 44 px coarse-pointer floor from `globals.css` and the
                    `text-base md:text-sm` guard that stops iOS zooming into it. */}
                <Input
                  id="appt-status-from"
                  type="date"
                  value={draftFrom}
                  max={draftTo || undefined}
                  onChange={(e) => setDraftFrom(e.target.value)}
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="appt-status-to" className="text-xs text-muted-foreground">
                  Au
                </Label>
                <Input
                  id="appt-status-to"
                  type="date"
                  value={draftTo}
                  min={draftFrom || undefined}
                  onChange={(e) => setDraftTo(e.target.value)}
                />
              </div>
            </div>
            {/* Caught here as well as on the server: the browser can say so before a request, and the server must
                still say so because `min`/`max` are a hint a typed value can walk straight past. */}
            {draftInverted && (
              <p role="status" className="text-xs text-destructive">
                La date de fin doit être postérieure à la date de début.
              </p>
            )}
            <Button
              type="button"
              size="sm"
              className="w-full sm:w-auto sm:justify-self-start"
              disabled={!draftFrom || !draftTo || draftInverted}
              onClick={() => onCustomRange(draftFrom, draftTo)}
            >
              Appliquer
            </Button>
          </div>
        )}
      </CardHeader>

      <CardContent className="flex min-w-0 flex-1 flex-col gap-4">
        {/*
          A failed read is NOT an empty period. « Aucun rendez-vous » is a confident statement about the practice,
          and rendering it because a request failed is the defect § 13 names. The banner carries the retry and the
          server's own sentence — which is the useful one here, since the refusals are about the range itself.
        */}
        {error ? (
          <div
            role="status"
            className="flex flex-wrap items-center gap-3 rounded-lg bg-destructive-wash p-3 text-xs text-destructive"
          >
            <AlertCircle className="size-4 shrink-0" aria-hidden="true" />
            <span className="min-w-0 flex-1">{error}</span>
            {onRetry && (
              <Button type="button" size="sm" variant="outline" onClick={onRetry}>
                Réessayer
              </Button>
            )}
          </div>
        ) : loading ? (
          <div className="flex flex-1 flex-col gap-3" aria-label="Chargement des rendez-vous par statut">
            <span className="block h-8 w-24 animate-pulse rounded bg-muted" />
            <span className="block min-h-44 flex-1 animate-pulse rounded-lg bg-muted" />
          </div>
        ) : !hasAnything ? (
          <EmptyState
            icon={CalendarRange}
            size="compact"
            className="flex-1"
            title="Aucun rendez-vous sur cette période"
            description="Les colonnes apparaîtront dès le premier rendez-vous pris sur la fenêtre choisie."
          />
        ) : (
          <>
            <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
              <span className="text-2xl font-bold leading-none tracking-tight">
                {data!.total.toLocaleString("fr-TN")}
              </span>
              <span className="text-xs text-muted-foreground">
                {data!.total === 1 ? "rendez-vous" : "rendez-vous"}
              </span>
              {delta !== null && (
                <span
                  className={cn(
                    "inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-semibold tabular-nums",
                    delta >= 0 ? "bg-success-wash text-success" : "bg-destructive-wash text-destructive",
                  )}
                  // The comparison names DAYS, never « le mois dernier »: the server compares the same number of
                  // days immediately before, which for a calendar month is not the previous calendar month.
                  title={comparedToDaysLabel(dayCount)}
                >
                  {delta >= 0 ? (
                    <TrendingUp className="size-3" aria-hidden="true" />
                  ) : (
                    <TrendingDown className="size-3" aria-hidden="true" />
                  )}
                  {delta > 0 ? "+" : ""}
                  {delta.toLocaleString("fr-TN")}&nbsp;%
                  <span className="sr-only"> {comparedToDaysLabel(dayCount)}</span>
                </span>
              )}
            </div>

            {/*
              Held at reduced opacity while a new window loads, never swapped for a skeleton: the columns would
              otherwise flash empty and the card's height would jump on every period change.
            */}
            <div className={cn("flex min-w-0 flex-1 flex-col gap-2", refetching && "opacity-60 transition-opacity")}>
              {/* § 11 — wide content scrolls in ITS OWN container; the page body never scrolls sideways. */}
              <div className="min-h-44 min-w-0 flex-1 overflow-x-auto">
                <div className="h-full min-h-44" style={{ minWidth: `${trackWidth}px` }}>
                  <ResponsiveContainer width="100%" height="100%">
                    <BarChart data={rows} margin={{ top: 8, right: 8, bottom: 4, left: 0 }} barCategoryGap={COLUMN_GAP_PX}>
                      {/* Horizontal hairlines only, solid — a dashed grid reads as a threshold. */}
                      <CartesianGrid stroke={GRID_COLOR} strokeWidth={1} vertical={false} />
                      <XAxis
                        dataKey="label"
                        tick={{ fill: AXIS_COLOR, fontSize: 11, fontFamily: "var(--font-mono)" }}
                        stroke={GRID_COLOR}
                        tickLine={false}
                        axisLine={{ stroke: GRID_COLOR }}
                        // Recharts drops labels that would collide rather than overlapping them, which is what
                        // keeps a 31-day month readable at the scroll container's width.
                        interval="preserveStartEnd"
                        minTickGap={8}
                      />
                      <YAxis
                        // Whole appointments only — « 2,5 rendez-vous » is not a thing, and recharts will happily
                        // emit fractional ticks on a small domain.
                        allowDecimals={false}
                        tick={{ fill: AXIS_COLOR, fontSize: 11, fontFamily: "var(--font-mono)" }}
                        width={30}
                        tickLine={false}
                        axisLine={false}
                      />
                      <Tooltip
                        cursor={{ fill: "var(--muted)", opacity: 0.55 }}
                        content={<StatusTooltip />}
                      />
                      {APPOINTMENT_STATUS_CLASSES.map((cls) => (
                        <Bar
                          key={cls.key}
                          dataKey={cls.key}
                          stackId="status"
                          // Declaration order IS stack order, bottom first — see the note on
                          // APPOINTMENT_STATUS_CLASSES for why this sequence and not another.
                          fill={`var(--${cls.token})`}
                          maxBarSize={COLUMN_MAX_PX}
                          shape={<StatusSegment classKey={cls.key} />}
                          isAnimationActive={false}
                        />
                      ))}
                    </BarChart>
                  </ResponsiveContainer>
                </div>
              </div>
              {rows.length * (COLUMN_MAX_PX + COLUMN_GAP_PX) > 0 && (
                <p className="sr-only">
                  {rows.length} {rows.length === 1 ? "colonne" : "colonnes"}, {GRANULARITY_LABELS[granularity]}.
                  Le tableau ci-dessous donne chaque valeur.
                </p>
              )}
            </div>

            {/*
              The legend is not decoration here — it is the relief that licenses pastel fills below 3:1. It names
              AND counts every class, so identity is never carried by colour alone.
            */}
            {/*
              ⚠️ `last:sm:col-span-2` is load-bearing, not tidying. This is a hairline grid — `bg-border` showing
              through a 1px gap — and there are FIVE classes, so in two columns the sixth cell is empty and the
              border paints straight through it as a grey slab. That is the same defect `app/page.tsx` documents for
              its KPI grids, and it is visible the moment the card renders. Letting the last row span both columns
              closes it without dropping to one tall column.
            */}
            <ul className="grid gap-px overflow-hidden rounded-lg border bg-border sm:grid-cols-2 [&>li:last-child]:sm:col-span-2">
              {APPOINTMENT_STATUS_CLASSES.map((cls) => {
                const count = buckets.reduce((sum, b) => sum + b[cls.key], 0)
                const share = data!.total > 0 ? Math.round((count / data!.total) * 100) : 0
                return (
                  <li
                    key={cls.key}
                    className="flex items-center gap-2.5 bg-card px-3 py-2"
                    title={APPOINTMENT_STATUS_CLASS_HINTS[cls.key]}
                  >
                    <span
                      aria-hidden="true"
                      className="size-2.5 shrink-0 rounded-sm"
                      style={{
                        backgroundColor: `var(--${cls.token})`,
                        boxShadow: `inset 0 0 0 1px var(--${cls.token}-edge)`,
                      }}
                    />
                    <span className="min-w-0 flex-1 text-xs">
                      {cls.label}
                      {/* The one fact the five-class fold gives up, handed back where a footnote belongs. */}
                      {cls.key === "upcoming" && data!.confirmedUpcoming > 0 && (
                        <span className="text-muted-foreground">
                          {" · "}dont {data!.confirmedUpcoming.toLocaleString("fr-TN")} confirmé
                          {data!.confirmedUpcoming === 1 ? "" : "s"}
                        </span>
                      )}
                    </span>
                    <span className="font-mono text-xs font-semibold tabular-nums">
                      {count.toLocaleString("fr-TN")}
                    </span>
                    <span className="w-9 text-end font-mono text-2xs tabular-nums text-muted-foreground">
                      {share}&nbsp;%
                    </span>
                  </li>
                )
              })}
            </ul>
          </>
        )}

        {/* Every value reachable without hovering — the WCAG-clean equivalent of the plot, and § 6's two trees:
            a real table above `md:`, a semantic card list below it. Seven columns on a 320 px phone is the exact
            defect the card-list rule exists to remove. */}
        {showTable && hasAnything && (
          <>
            <div className={cn(TABLE_ONLY, "min-w-0 overflow-x-auto")}>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{granularity === "Day" ? "Jour" : "Période"}</TableHead>
                    {APPOINTMENT_STATUS_CLASSES.map((cls) => (
                      <TableHead key={cls.key} className="text-right" title={APPOINTMENT_STATUS_CLASS_HINTS[cls.key]}>
                        {cls.label}
                      </TableHead>
                    ))}
                    <TableHead className="text-right">Total</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {rows.map((row) => (
                    <TableRow key={row.start}>
                      <TableCell>{row.label}</TableCell>
                      {APPOINTMENT_STATUS_CLASSES.map((cls) => (
                        <TableCell key={cls.key} className="text-right tabular-nums">
                          {/* A zero renders as « — »: in a grid of counts a dash reads as "none" faster than a 0
                              does, and it keeps the eye on the columns that carry something. */}
                          {row[cls.key] === 0 ? "—" : row[cls.key].toLocaleString("fr-TN")}
                        </TableCell>
                      ))}
                      <TableCell className="text-right font-semibold tabular-nums">
                        {row.total.toLocaleString("fr-TN")}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
            <div className={CARDS_ONLY}>
              <CardList
                ariaLabel="Rendez-vous par statut, par période"
                items={rows}
                getKey={(row) => row.start}
                title={(row) => row.label}
                status={(row) => (
                  <span className="font-mono text-xs tabular-nums text-muted-foreground">
                    {row.total.toLocaleString("fr-TN")} au total
                  </span>
                )}
                // A field with no value is omitted rather than shown as « — » (§ 6): on a phone card, five rows of
                // dashes is five lines saying nothing.
                fields={(row) =>
                  APPOINTMENT_STATUS_CLASSES.map((cls) =>
                    row[cls.key] > 0
                      ? { label: cls.label, value: row[cls.key].toLocaleString("fr-TN") }
                      : null,
                  )
                }
              />
            </div>
          </>
        )}
      </CardContent>
    </Card>
  )
}

/**
 * One stacked segment.
 *
 * <p>A custom shape rather than recharts' default rect, because a pastel fill needs <b>both</b> separations and a
 * plain <c>&lt;Bar&gt;</c> can only carry one: a 2 px surface gap between neighbours (drawn here by insetting the
 * rect, not by stroking the card colour) <i>and</i> a 1 px same-hue edge, which is what gives a fill sitting at
 * 1.5:1 against the card a boundary at all. The edge is inset by half its width so it paints inside the segment
 * and cannot bleed over the neighbour.</p>
 *
 * <p>Only the topmost segment of each column is rounded — the data end. Rounding every segment would draw five
 * pills stacked in a tube, and rounding the baseline would lift the column off its own axis.</p>
 */
function StatusSegment({
  classKey,
  ...props
}: {
  classKey: AppointmentStatusClassKey
  x?: number
  y?: number
  width?: number
  height?: number
  fill?: string
  payload?: { topClass: AppointmentStatusClassKey | null }
}) {
  const { x = 0, y = 0, width = 0, height = 0, fill, payload } = props

  // Recharts renders a zero-value segment as a zero-height rect; drawing its 1px edge would leave a hairline
  // floating where there is no data.
  if (height <= 0 || width <= 0) return null

  const inset = 1
  const drawnHeight = Math.max(1, height - inset * 2)
  const isTop = payload?.topClass === classKey
  const radius = isTop ? Math.min(4, width / 2, drawnHeight) : 0
  const token = APPOINTMENT_STATUS_CLASSES.find((c) => c.key === classKey)?.token

  return (
    <rect
      x={x + 0.5}
      y={y + inset}
      width={Math.max(1, width - 1)}
      height={drawnHeight}
      rx={radius}
      ry={radius}
      fill={fill}
      stroke={token ? `var(--${token}-edge)` : undefined}
      strokeWidth={1}
    />
  )
}

/**
 * The hover readout: the bucket's total leads, then only the classes that are actually present.
 *
 * <p>A line reading « Absent 0 » teaches the reader nothing and pushes the lines that matter down, so zero classes
 * are dropped. The stack is listed top-down, matching what the eye just pointed at.</p>
 */
function StatusTooltip({
  active,
  payload,
}: {
  active?: boolean
  payload?: Array<{ payload: AppointmentStatusBucketDto & { label: string } }>
}) {
  if (!active || !payload?.length) return null
  const bucket = payload[0].payload

  return (
    <div className="rounded-lg border bg-card px-3 py-2 shadow-sm">
      <p className="font-mono text-2xs uppercase tracking-wider text-muted-foreground">{bucket.label}</p>
      <p className="mt-0.5 text-sm font-semibold tabular-nums text-foreground">
        {bucket.total.toLocaleString("fr-TN")} rendez-vous
      </p>
      <ul className="mt-1.5 space-y-1">
        {[...APPOINTMENT_STATUS_CLASSES]
          .reverse()
          .filter((cls) => bucket[cls.key] > 0)
          .map((cls) => (
            <li key={cls.key} className="flex items-center gap-2 text-xs tabular-nums">
              <span
                aria-hidden="true"
                className="size-2 shrink-0 rounded-[2px]"
                style={{
                  backgroundColor: `var(--${cls.token})`,
                  boxShadow: `inset 0 0 0 1px var(--${cls.token}-edge)`,
                }}
              />
              <span className="flex-1 pe-3 text-muted-foreground">{cls.label}</span>
              <span className="font-semibold">{bucket[cls.key].toLocaleString("fr-TN")}</span>
            </li>
          ))}
      </ul>
    </div>
  )
}
