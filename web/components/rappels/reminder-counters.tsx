"use client"

import { Ban, CircleX, Clock, Send, type LucideIcon } from "lucide-react"

import { KpiGrid } from "@/components/dashboard/kpi-grid"
import { STATUS_TONE_CLASS, STATUS_TONE_RING } from "@/components/ui/status-tone"
import { cn } from "@/lib/utils"
import type { ReminderDeliveryStatus, ReminderLogDto } from "@/lib/api/reminder-settings"
import { DELIVERY_LABEL_PLURAL, DELIVERY_TONE, type StatusFilter } from "./delivery-tone"

/**
 * The four delivery counters — **which are also the filter**.
 *
 * <p>There used to be two rows saying the same thing: four figures on white cells, and directly under them five
 * status chips carrying <i>the same four numbers</i> plus a « Tous ». One row stated the counts and the other one
 * acted on them, so the figures were decoration and the chips were an unlabelled repeat. Now a tap on « Bloqués »
 * <i>is</i> the filter, tapping it again clears it, and the row of chips is gone.</p>
 *
 * <p><b>Each tile wears its own tone's wash</b> (`ui/status-tone.ts`, via `delivery-tone.ts`), which is the whole
 * of the colour on this screen and none of it new — these are the same four `-wash` tokens the log's status pills
 * have always used, moved up to where they can be read across a room. A selected tile adds a 2 px inset ring in
 * its own hue; an idle one gets the ring at 1 px on hover, so a mouse discovers that the tile is a control
 * without a tablet showing a stuck state.</p>
 *
 * <p>⚠️ <b>Only « Bloqués » and « Échecs » go quiet at zero</b>, and the asymmetry is the point. Those two are the
 * alarms — an amber or a red « 0 » raises one about nothing, which is the surest way to teach a practice to stop
 * looking. « Envoyés » and « En attente » keep their wash at zero because a green or azure « 0 » alarms nobody,
 * and because a whole row draining to white at 08:00 would put us back where this started.</p>
 *
 * <p>⚠️ The counts are <b>clinic-wide</b>, straight off the server's counters — never derived from the rows on
 * screen, which would render « les échecs parmi ces 25 ».</p>
 */
export function ReminderCounters({
  data,
  status,
  onPick,
}: {
  /** `null` while the first read is in flight — the tiles keep their wash and skeleton the figure only. */
  data: ReminderLogDto | null
  status: StatusFilter
  /** Called with the tile's own status, or `"all"` when the active tile is tapped again. */
  onPick: (next: StatusFilter) => void
}) {
  return (
    /*
      Four columns from `lg:`, two before them. Four figures at 320 px would be four 80 px columns and
      « En attente » does not fit in one — this is the same grid the page has always used, on the shared
      `KpiGrid` surface so the hairlines and the elevation match la caisse and « Factures ».
    */
    <KpiGrid columns={4} className="sm:grid-cols-2 lg:grid-cols-4">
      {COUNTERS.map((counter) => {
        const value = data === null ? undefined : counter.value(data)
        const tone = DELIVERY_TONE[counter.status]
        const active = status === counter.status
        // See the ⚠️ above: an alarm colour at zero is an alarm about nothing.
        const quiet = counter.quietAtZero && value === 0
        const Icon = counter.icon

        return (
          <button
            key={counter.status}
            type="button"
            aria-pressed={active}
            onClick={() => onPick(active ? "all" : counter.status)}
            className={cn(
              // No `touch-target` needed: the tile is its own 84 px hit area, comfortably past the 44 px floor.
              "flex flex-col gap-0.5 p-4 text-start ring-inset transition-[box-shadow] duration-[160ms] ease-snap",
              quiet ? "bg-card text-muted-foreground" : STATUS_TONE_CLASS[tone],
              // Colour only — a ring colour with no width paints nothing, so it is safe unconditionally.
              STATUS_TONE_RING[tone],
              active ? "ring-2" : "hover-hover:hover:ring-1",
              // The focus ring overrides the tone's for as long as focus is there, which is the one place a
              // borrowed colour is right: `--ring` is what every other control in the app focuses with.
              "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
            )}
          >
            <span className="flex items-center gap-2 text-sm font-medium">
              <Icon aria-hidden="true" className="size-4 shrink-0" />
              {DELIVERY_LABEL_PLURAL[counter.status]}
            </span>
            {value === undefined ? (
              <span className="h-8 w-12 animate-pulse rounded bg-muted" aria-label="Chargement" />
            ) : (
              <span className="text-2xl font-semibold tabular-nums tracking-tight">
                {value.toLocaleString("fr-TN")}
              </span>
            )}
            {/*
              The window, as its own line rather than crammed into the label. « Échecs (7 j) » was a period
              hidden in a parenthesis on the one figure whose period is not today — and the other three had no
              way to say what theirs was at all.
            */}
            <span className="font-mono text-2xs opacity-75">{counter.meta}</span>
          </button>
        )
      })}
    </KpiGrid>
  )
}

/**
 * The four counters, in the order a day is read: what went out, what is queued behind it, what is stuck, what
 * broke.
 *
 * <p>« Bloqués » exists because a whole install's queue could stop sending with nothing on any screen to say so.
 * A blocked row is not waiting its turn — it needs a setting changed, and the reason is printed on the row.</p>
 */
const COUNTERS: {
  status: ReminderDeliveryStatus
  /** The period or scope this figure covers. Absent from the label on purpose — see the JSX. */
  meta: string
  icon: LucideIcon
  /** True for the two tones that are alarms. See the ⚠️ on the component. */
  quietAtZero: boolean
  value: (d: ReminderLogDto) => number
}[] = [
  { status: "sent", meta: "aujourd'hui", icon: Send, quietAtZero: false, value: (d) => d.sentToday },
  { status: "pending", meta: "dans la file", icon: Clock, quietAtZero: false, value: (d) => d.pending },
  { status: "blocked", meta: "un réglage à changer", icon: Ban, quietAtZero: true, value: (d) => d.blocked },
  // Several days, not today: a send that failed at 23:00 must still be counted the next morning.
  { status: "failed", meta: "7 derniers jours", icon: CircleX, quietAtZero: true, value: (d) => d.failedRecent },
]
