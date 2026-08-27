"use client"

import Link from "next/link"
import { ChevronRight } from "lucide-react"
import { cn } from "@/lib/utils"

/** How loudly one count asks to be dealt with. Never colour alone — the number and its label carry the fact. */
export type AlertTone = "live" | "hot" | "warm" | "calm"

export interface DayAlert {
  key: string
  count: number
  /** Always plural-agnostic French — « fiches à saisir », « en salle d'attente ». */
  label: string
  href: string
  tone: AlertTone
  /** Rendered faded and unemphasised. A zero is the absence of work, not a figure to read. */
  isZero: boolean
}

const CAP_TONE: Record<AlertTone, string> = {
  live: "bg-accent text-accent-foreground",
  hot: "bg-destructive-wash text-destructive",
  warm: "bg-warning-wash text-warning-ink",
  calm: "bg-muted text-muted-foreground",
}

/**
 * Zone 4 of the day board — what is waiting, outside the chair.
 *
 * <p>⚠️ <b>Full-width rows on a phone, wrapping pills above `sm:`.</b> Labels like « en salle d'attente » in a
 * wrapping pill are exactly what clipped words at 320 px; below the hinge each becomes its own 44 px row with a
 * chevron, which is also the larger tap target.</p>
 *
 * <p>⚠️ <b>Severity lives in the numeral's chip, never in the whole surface.</b> Six tinted rectangles in a row
 * is a carnival, and it makes the one genuinely urgent count no louder than the rest.</p>
 */
export function DayAlerts({ alerts }: { alerts: DayAlert[] }) {
  if (alerts.length === 0) return null

  return (
    <ul className="grid gap-2 sm:flex sm:flex-wrap sm:gap-2.5">
      {alerts.map((alert) => (
        <li key={alert.key} className="min-w-0">
          <Link
            href={alert.href}
            className={cn(
              "flex min-h-11 w-full items-center gap-2.5 rounded-xl border px-2.5 py-2 text-sm font-medium transition-colors",
              "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background",
              alert.isZero
                ? "border-border/60 bg-transparent text-muted-foreground"
                : "border-border bg-card text-foreground shadow-sm hover-hover:hover:border-primary/40",
            )}
          >
            <span
              aria-hidden="true"
              className={cn(
                "grid size-8 shrink-0 place-content-center rounded-lg text-sm font-bold tabular-nums",
                alert.isZero ? "bg-transparent font-medium text-muted-foreground" : CAP_TONE[alert.tone],
              )}
            >
              {alert.count}
            </span>
            {/* The count is read out here rather than left to the chip, which is aria-hidden — a screen reader
                must hear « 3 fiches à saisir », not « fiches à saisir ». */}
            <span className="min-w-0 pe-1 leading-snug">
              <span className="sr-only">{alert.count} </span>
              {alert.label}
            </span>
            <ChevronRight
              aria-hidden="true"
              className="ms-auto size-4 shrink-0 text-muted-foreground sm:hidden"
            />
          </Link>
        </li>
      ))}
    </ul>
  )
}
