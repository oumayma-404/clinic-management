"use client"

import { useEffect, useRef, useState } from "react"
import Link from "next/link"
import { Card, CardContent } from "@/components/ui/card"
import { actSolidStyle, actTintStyle } from "@/lib/dashboard/act-colour"
import {
  formatClock,
  formatDuration,
  initialsOf,
  type DayGap,
  type DaySlot,
  type DaySummary,
} from "@/lib/dashboard/day-summary"
import { appointmentActsSummary } from "@/components/appointment-labels"
import { cn } from "@/lib/utils"

interface DayRibbonProps {
  summary: DaySummary
  nowMinutes: number
}

/** Below these painted widths a label cannot be read, so it is not drawn. See {@link useTrackWidth}. */
const INITIALS_MIN_PX = 40
const TIME_MIN_PX = 62
const GAP_FULL_MIN_PX = 86
const GAP_SHORT_MIN_PX = 52

/**
 * Zone 3 of the day board — the shape of the day.
 *
 * <p>One block per visit, placed at its hour and as wide as its duration, in a pastel tint of the act's own
 * catalogue colour. It is the only object in the product that answers « ai-je un trou cet après-midi ? » without
 * opening the agenda and counting, which is the whole reason it earns its space.</p>
 *
 * <p>⚠️ <b>Hour gridlines are not decoration.</b> Without a scale a block's width says nothing — 40 px could be
 * twenty minutes or two hours — so the ticks are what make the ribbon readable rather than merely pretty.</p>
 *
 * <p>⚠️ <b>A free stretch is drawn, not annotated.</b> Dashed, hatched, and labelled in the middle. It is the
 * question the agenda makes a dentist compute by hand.</p>
 *
 * <p>⚠️ <b>The window is a union</b> (`buildDaySummary`): the clinic's configured hours *and* every appointment
 * booked today, so a 07:00 emergency extends the ribbon instead of falling outside it — § 0, no capability
 * removed by a layout decision.</p>
 */
export function DayRibbon({ summary, nowMinutes }: DayRibbonProps) {
  const { trackRef, width } = useTrackWidth()
  const span = Math.max(1, summary.windowTo - summary.windowFrom)
  const pct = (minutes: number) => ((minutes - summary.windowFrom) / span) * 100
  const pxFor = (minutes: number) => (minutes / span) * width

  const nowInWindow = nowMinutes >= summary.windowFrom && nowMinutes <= summary.windowTo

  return (
    <Card>
      <CardContent className="space-y-0">
        <div
          ref={trackRef}
          className="relative h-12 overflow-hidden rounded-xl bg-muted sm:h-15"
          role="img"
          aria-label={ribbonLabel(summary)}
        >
          <HourTicks from={summary.windowFrom} to={summary.windowTo} pct={pct} />

          {summary.gaps.map((gap) => (
            <GapBlock
              key={`gap-${gap.startMinutes}`}
              gap={gap}
              leftPct={pct(gap.startMinutes)}
              widthPct={(gap.minutes / span) * 100}
              widthPx={pxFor(gap.minutes)}
            />
          ))}

          {summary.slots.map((slot) => (
            <SlotBlock
              key={slot.appointment.id}
              slot={slot}
              leftPct={pct(slot.startMinutes)}
              widthPct={((slot.endMinutes - slot.startMinutes) / span) * 100}
              widthPx={pxFor(slot.endMinutes - slot.startMinutes)}
            />
          ))}

          {nowInWindow && (
            <span
              aria-hidden="true"
              className="absolute -inset-y-1 z-20 w-0.5 rounded-full bg-foreground"
              style={{ left: `${pct(nowMinutes)}%` }}
            >
              <span className="absolute -left-[3px] top-0 size-2 rounded-full bg-foreground" />
            </span>
          )}
        </div>

        <Axis from={summary.windowFrom} to={summary.windowTo} />

        {summary.acts.length > 0 && <Legend summary={summary} />}

        <Facts summary={summary} />
      </CardContent>
    </Card>
  )
}

/* ── the ribbon's parts ─────────────────────────────────────────────────────────────────────────────────── */

function HourTicks({ from, to, pct }: { from: number; to: number; pct: (m: number) => number }) {
  const ticks: number[] = []
  for (let m = Math.ceil(from / 60) * 60; m < to; m += 60) ticks.push(m)

  return (
    <>
      {ticks.map((m) => (
        <span
          key={`tick-${m}`}
          aria-hidden="true"
          className={cn("absolute inset-y-0 w-px", (m / 60) % 3 === 0 ? "bg-foreground/12" : "bg-border")}
          style={{ left: `${pct(m)}%` }}
        />
      ))}
    </>
  )
}

function GapBlock({
  gap,
  leftPct,
  widthPct,
  widthPx,
}: {
  gap: DayGap
  leftPct: number
  widthPct: number
  widthPx: number
}) {
  // Measured, not guessed with a breakpoint — guessing is precisely how a word ends up clipped in half.
  const label =
    widthPx >= GAP_FULL_MIN_PX
      ? `${formatDuration(gap.minutes)} libre`
      : widthPx >= GAP_SHORT_MIN_PX
        ? formatDuration(gap.minutes)
        : null

  return (
    <span
      aria-hidden="true"
      className="absolute inset-y-1.5 grid place-content-center overflow-hidden rounded-lg border border-dashed border-foreground/20"
      style={{
        left: `${leftPct}%`,
        width: `${widthPct}%`,
        // `currentColor` picks up `text-muted-foreground` from the span itself, so the hatch follows the theme
        // without naming a token that could be renamed out from under it.
        color: "var(--muted-foreground)",
        backgroundImage:
          "repeating-linear-gradient(135deg, color-mix(in oklab, currentColor 22%, transparent) 0 1px, transparent 1px 7px)",
      }}
    >
      {label && <span className="px-1 font-mono text-2xs text-muted-foreground">{label}</span>}
    </span>
  )
}

function SlotBlock({
  slot,
  leftPct,
  widthPct,
  widthPx,
}: {
  slot: DaySlot
  leftPct: number
  widthPct: number
  widthPx: number
}) {
  const showInitials = widthPx >= INITIALS_MIN_PX
  const showTime = widthPx >= TIME_MIN_PX

  return (
    <Link
      href={`/appointments?appointmentId=${slot.appointment.id}`}
      className={cn(
        "absolute inset-y-1.5 z-10 block overflow-hidden rounded-md px-1.5 py-1 transition-[filter]",
        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring",
        "hover-hover:hover:brightness-[0.97]",
        slot.isPast && "opacity-45",
        slot.isCurrent && "ring-2 ring-foreground",
      )}
      style={{
        ...actTintStyle(slot.colorHex),
        left: `${leftPct}%`,
        width: `${widthPct}%`,
        minWidth: "4px",
      }}
      aria-label={`${formatClock(slot.startMinutes)} ${slot.appointment.patientName} · ${appointmentActsSummary(slot.appointment) ?? "Rendez-vous"}`}
    >
      {showInitials && (
        <span className="block truncate font-mono text-2xs font-semibold text-foreground">
          {initialsOf(slot.appointment.patientName)}
        </span>
      )}
      {showTime && (
        <span className="block truncate font-mono text-2xs text-muted-foreground">
          {formatClock(slot.startMinutes)}
        </span>
      )}
    </Link>
  )
}

/**
 * Seven evenly spaced marks, of which the phone shows four.
 *
 * <p>Hiding the even ones in CSS rather than rendering a different count keeps one markup for every width — the
 * device rule's « crossing a breakpoint must not remount » applied to the simplest possible case.</p>
 */
function Axis({ from, to }: { from: number; to: number }) {
  const marks = Array.from({ length: 7 }, (_, i) => Math.round(from + ((to - from) * i) / 6))

  return (
    <div className="mt-2 flex justify-between font-mono text-2xs tabular-nums text-muted-foreground">
      {marks.map((m, i) => (
        <span key={`axis-${m}-${i}`} className={cn(i % 2 === 1 && "hidden sm:inline")}>
          {formatClock(m)}
        </span>
      ))}
    </div>
  )
}

/**
 * The key to the colours directly above it.
 *
 * <p>⚠️ The swatch is <b>exactly the same paint</b> as a ribbon block, at 11 px. A more saturated dot would look
 * better and would stop being a key — the reader has to be able to match it to the block by eye.</p>
 *
 * <p>⚠️ <b>The act's name wraps; it must never be `whitespace-nowrap`.</b> An act name is clinic-authored and
 * routinely longer than a phone's card — « Extraction chirurgicale (sagesse / dent incluse) » is ~300 px at this
 * size against ~240 px of card content box at 320 px. A `nowrap` entry cannot shrink below its own min-content, so
 * `flex-wrap` on the list never gets the chance to help and the text is painted straight out through the card's
 * edge. `max-w-full` + `min-w-0` on the name is what caps the row at the card and lets the words fall to a second
 * line instead. The swatch and the count keep `shrink-0` so the key itself never collapses.</p>
 */
function Legend({ summary }: { summary: DaySummary }) {
  return (
    <ul className="mt-4 flex flex-wrap gap-x-4 gap-y-2 border-t pt-4">
      {summary.acts.map((act) => (
        <li key={act.key} className="flex min-w-0 max-w-full items-start gap-2 text-sm">
          <span
            aria-hidden="true"
            className="mt-[0.3rem] size-2.5 shrink-0 rounded-[4px]"
            style={actTintStyle(act.colorHex)}
          />
          <span className="shrink-0 font-semibold tabular-nums text-foreground">{act.count}</span>
          <span className="min-w-0 text-muted-foreground [overflow-wrap:anywhere]">{act.name}</span>
        </li>
      ))}
    </ul>
  )
}

/** The figures, as one plain sentence under the shape they describe rather than as competing big numbers. */
function Facts({ summary }: { summary: DaySummary }) {
  const items: Array<{ value: string; label: string }> = [
    { value: String(summary.count), label: "rendez-vous" },
    { value: String(summary.actCount), label: summary.actCount === 1 ? "acte" : "actes" },
  ]
  // Stated, never folded into « rendez-vous »: a blocked hour is why the chair time and the load can outrun the
  // visit count, and a reader who cannot see it reads those two figures as wrong.
  if (summary.blockedCount > 0) {
    items.push({
      value: String(summary.blockedCount),
      label: summary.blockedCount === 1 ? "créneau bloqué" : "créneaux bloqués",
    })
  }
  items.push({ value: formatDuration(summary.bookedMinutes), label: "au fauteuil" })
  if (summary.loadPercent !== null) {
    items.push({ value: `${summary.loadPercent} %`, label: "de la journée" })
  }

  return (
    <p className="mt-4 flex flex-wrap gap-x-4 gap-y-1 text-sm text-muted-foreground">
      {items.map((item) => (
        <span key={item.label}>
          <span className="font-semibold tabular-nums text-foreground">{item.value}</span> {item.label}
        </span>
      ))}
      {summary.endsAtMinutes !== null && (
        <span>
          fin prévue{" "}
          <span className="font-semibold tabular-nums text-foreground">{formatClock(summary.endsAtMinutes)}</span>
        </span>
      )}
    </p>
  )
}

/* ── measurement ────────────────────────────────────────────────────────────────────────────────────────── */

/**
 * The ribbon's painted width, so a label can be shown only when it fits.
 *
 * <p>A `ResizeObserver` rather than a breakpoint, because the same 20-minute visit is ~40 px on a desktop ribbon
 * and ~6 px at 320 px — and the rail collapsing, a tablet rotating or the browser zooming all change the width
 * without crossing a breakpoint. Guessing is what clips a word in half.</p>
 */
function useTrackWidth() {
  const trackRef = useRef<HTMLDivElement | null>(null)
  const [width, setWidth] = useState(0)

  useEffect(() => {
    const node = trackRef.current
    if (!node) return
    // Not every environment running this bundle has ResizeObserver (jsdom, an old WebView). Falling back to the
    // measured width once is better than throwing; the labels then simply never re-evaluate.
    if (typeof ResizeObserver === "undefined") {
      setWidth(node.getBoundingClientRect().width)
      return
    }
    const observer = new ResizeObserver(([entry]) => setWidth(entry.contentRect.width))
    observer.observe(node)
    return () => observer.disconnect()
  }, [])

  return { trackRef, width }
}

function ribbonLabel(summary: DaySummary): string {
  const window = `de ${formatClock(summary.windowFrom)} à ${formatClock(summary.windowTo)}`
  const gaps =
    summary.gaps.length === 0
      ? "aucune plage libre"
      : summary.gaps
          .map((g) => `${formatDuration(g.minutes)} libre à partir de ${formatClock(g.startMinutes)}`)
          .join(", ")
  // A screen reader is told about the blocked slots too — they are drawn on the ribbon it is describing.
  const blocked =
    summary.blockedCount > 0
      ? `, ${summary.blockedCount} ${summary.blockedCount === 1 ? "créneau bloqué" : "créneaux bloqués"}`
      : ""
  return `Journée ${window} : ${summary.count} rendez-vous${blocked}, ${gaps}.`
}
