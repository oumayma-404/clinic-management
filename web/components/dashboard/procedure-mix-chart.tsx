"use client"

import { useState } from "react"
import { Activity } from "lucide-react"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { EmptyState } from "@/components/ui/empty-state"
import { actTintStyle } from "@/lib/dashboard/act-colour"
import { formatDuration } from "@/lib/dashboard/day-summary"
import { cn } from "@/lib/utils"
import type { ProcedureMixPointDto } from "@/lib/api/types"

/** Kept in step with `DashboardProcedureMixReader.MaxPoints` — stated to the reader, never a silent cut. */
const SERVER_CAP = 8

type Measure = "minutes" | "count"

/**
 * « Répartition des actes » — what the period's work was actually made of.
 *
 * <p>The one figure on the dashboard counted over acts rather than visits or money, and the answer to a question
 * no other screen asks. A dentist can already see how many patients they saw and how much came in; nothing tells
 * them that two fifths of their chair time went to endodontics.</p>
 *
 * <p>⚠️ <b>The Durée / Nombre toggle is the point of the chart, not a convenience.</b> 62 détartrages weigh fewer
 * hours than 48 obturations, so the two measures rank the list differently — and « durée » is the half that says
 * where the day actually went. It defaults to duration for that reason.</p>
 *
 * <p>Bars are the acts' own catalogue colours, tinted through the shared helper so they match the day ribbon
 * exactly; a hand-typed devis line has no colour and takes the neutral.</p>
 */
export function ProcedureMixChart({
  points,
  loading = false,
}: {
  points: ProcedureMixPointDto[]
  loading?: boolean
}) {
  const [measure, setMeasure] = useState<Measure>("minutes")

  // Duration can legitimately be zero across the board — a clinic whose acts carry no per-act minutes — and a
  // chart of empty bars reads as broken. Fall back to counts and say nothing, since the toggle is right there.
  const totalMinutes = points.reduce((sum, p) => sum + p.minutes, 0)
  const effective: Measure = measure === "minutes" && totalMinutes === 0 ? "count" : measure

  const valueOf = (p: ProcedureMixPointDto) => (effective === "minutes" ? p.minutes : p.actCount)
  const max = points.reduce((m, p) => Math.max(m, valueOf(p)), 0)

  return (
    <Card>
      <CardHeader className="gap-3 sm:flex-row sm:items-center sm:justify-between">
        <CardTitle className="flex items-center gap-2.5">
          <span
            aria-hidden="true"
            className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary"
          >
            <Activity className="size-4" strokeWidth={1.75} />
          </span>
          Répartition des actes
        </CardTitle>

        <div
          role="group"
          aria-label="Mesure"
          className="inline-flex shrink-0 gap-0.5 rounded-full border bg-card p-0.5 shadow-sm"
        >
          {(["minutes", "count"] as const).map((m) => (
            <button
              key={m}
              type="button"
              aria-pressed={effective === m}
              onClick={() => setMeasure(m)}
              className={cn(
                "min-h-8 rounded-full px-3 text-xs font-medium transition-colors coarse:min-h-10 coarse:px-4",
                effective === m
                  ? "bg-primary font-semibold text-primary-foreground"
                  : "text-muted-foreground hover-hover:hover:text-foreground",
              )}
            >
              {m === "minutes" ? "Durée" : "Nombre"}
            </button>
          ))}
        </div>
      </CardHeader>

      <CardContent>
        {loading ? (
          <ul className="space-y-4" aria-label="Chargement de la répartition">
            {[0, 1, 2, 3, 4].map((i) => (
              <li key={i} className="space-y-2">
                <span className="block h-3.5 w-40 max-w-full animate-pulse rounded bg-muted" />
                <span className="block h-2 w-full animate-pulse rounded-full bg-muted" />
              </li>
            ))}
          </ul>
        ) : points.length === 0 ? (
          <EmptyState
            icon={Activity}
            size="compact"
            title="Aucun acte sur cette période"
            description="Les actes des rendez-vous honorés apparaîtront ici."
          />
        ) : (
          <>
            <ul className="space-y-3.5">
              {points.map((point) => {
                const value = valueOf(point)
                return (
                  <li key={point.procedureTypeId ?? `name:${point.name}`} className="space-y-1.5">
                    <p className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-0.5">
                      <span className="min-w-0 text-sm font-medium text-foreground [overflow-wrap:anywhere]">
                        {point.name}
                      </span>
                      {/* Both figures, always — the toggle changes the ranking, not what the reader is allowed
                          to know, and « 31 h » alone leaves « over how many acts? » unanswerable. */}
                      <span className="shrink-0 font-mono text-xs tabular-nums text-muted-foreground">
                        {point.minutes > 0 && `${formatDuration(point.minutes)} · `}
                        {point.actCount} {point.actCount === 1 ? "acte" : "actes"}
                      </span>
                    </p>
                    <span className="block h-2 overflow-hidden rounded-full bg-muted">
                      <span
                        className="block h-full rounded-full"
                        style={{
                          ...actTintStyle(point.colorHex),
                          width: `${max > 0 ? Math.max(2, (value / max) * 100) : 0}%`,
                        }}
                      />
                    </span>
                  </li>
                )
              })}
            </ul>

            {/* No silent caps: a clinic with a wider catalogue is told it is looking at the busiest acts. */}
            {points.length >= SERVER_CAP && (
              <p className="mt-4 text-xs text-muted-foreground">
                Les {SERVER_CAP} actes les plus fréquents de la période.
              </p>
            )}
          </>
        )}
      </CardContent>
    </Card>
  )
}
