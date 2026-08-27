"use client"

import { Ban, CircleX, Clock, Send, type LucideIcon } from "lucide-react"

import { Stat, StatStrip } from "@/components/ui/stat-strip"
import { DELIVERY_LABEL_PLURAL, DELIVERY_TONE, type StatusFilter } from "./delivery-tone"
import type { ReminderDeliveryStatus, ReminderLogDto } from "@/lib/api/reminder-settings"

/**
 * The four delivery counters — **which are also the filter**.
 *
 * <p>There used to be two rows saying the same thing: four figures on white cells, and directly under them five
 * status chips carrying <i>the same four numbers</i> plus a « Tous ». One row stated the counts and the other one
 * acted on them, so the figures were decoration and the chips were an unlabelled repeat. Now a tap on « Bloqués »
 * <i>is</i> the filter, tapping it again clears it, and the row of chips is gone.</p>
 *
 * <p><b>The tone is on the figure, not on the tile.</b> Each counter used to wear its whole `-wash` edge to edge,
 * and four filled tiles side by side — a green, an azure, an amber and a red — read as a traffic light rather
 * than as a clinic's software: the loudest surface in the product sat on the screen where the least is at stake.
 * The information those fills carried is real and it is all still here, in the figure's own ink and its glyph,
 * on the one neutral strip every other summary row in the app now uses (`ui/stat-strip.tsx`).</p>
 *
 * <p>⚠️ <b>Only « Bloqués » and « Échecs » go quiet at zero</b>, and the asymmetry is the point. Those two are the
 * alarms — a red « 0 » raises one about nothing, which is the surest way to teach a practice to stop looking.
 * « Envoyés » and « En attente » keep their tone at zero because a green or azure « 0 » alarms nobody.</p>
 *
 * <p>⚠️ The counts are <b>clinic-wide</b>, straight off the server's counters — never derived from the rows on
 * screen, which would render « les échecs parmi ces 25 ».</p>
 */
export function ReminderCounters({
  data,
  status,
  onPick,
}: {
  /** `null` while the first read is in flight — the tiles keep their labels and skeleton the figure only. */
  data: ReminderLogDto | null
  status: StatusFilter
  /** Called with the tile's own status, or `"all"` when the active tile is tapped again. */
  onPick: (next: StatusFilter) => void
}) {
  return (
    <StatStrip>
      {COUNTERS.map((counter) => {
        const value = data === null ? undefined : counter.value(data)
        const active = status === counter.status
        // See the ⚠️ on the component: an alarm colour at zero is an alarm about nothing.
        const quiet = counter.quietAtZero && value === 0

        return (
          <Stat
            key={counter.status}
            label={DELIVERY_LABEL_PLURAL[counter.status]}
            icon={counter.icon}
            tone={quiet ? "neutral" : DELIVERY_TONE[counter.status]}
            loading={value === undefined}
            value={value?.toLocaleString("fr-TN") ?? ""}
            /*
              The window, as its own line rather than crammed into the label. « Échecs (7 j) » was a period
              hidden in a parenthesis on the one figure whose period is not today — and the other three had no
              way to say what theirs was at all.
            */
            /*
              ⚠️ The hint states the tile's OWN scope and, when the two differ, the filter's too.
              « ENVOYÉS · 0 · aujourd'hui » applied a filter spanning the whole *date range* and returned 22 rows,
              so one control said « voici combien » and « et c'est ce que vous regardez » about two different sets
              — self-contradicting at a glance on the tile most likely to read zero.
            */
            hint={
              counter.filterMeta && value !== undefined
                ? `${counter.meta} · filtre : ${counter.filterMeta}`
                : counter.meta
            }
            selected={active}
            onSelect={() => onPick(active ? "all" : counter.status)}
          />
        )
      })}
    </StatStrip>
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
  /**
   * What the FILTER shows, when it is not the same set the figure counts.
   *
   * ⚠️ Only « Envoyés » and « Échecs » need one, and only because their figures are windowed while the filter is
   * not: a tile reading « 0 · aujourd'hui » filtered the whole date range and returned 22 rows. The counters are
   * deliberately clinic-wide and filter-independent — that is documented and correct — but merging the count and
   * the filter into one control makes the pair look self-contradicting unless the tile says so.
   */
  filterMeta?: string
  value: (d: ReminderLogDto) => number
}[] = [
  {
    status: "sent",
    meta: "aujourd'hui",
    filterMeta: "toute la période",
    icon: Send,
    quietAtZero: false,
    value: (d) => d.sentToday,
  },
  { status: "pending", meta: "dans la file", icon: Clock, quietAtZero: false, value: (d) => d.pending },
  { status: "blocked", meta: "un réglage à changer", icon: Ban, quietAtZero: true, value: (d) => d.blocked },
  // Several days, not today: a send that failed at 23:00 must still be counted the next morning.
  {
    status: "failed",
    meta: "7 derniers jours",
    filterMeta: "toute la période",
    icon: CircleX,
    quietAtZero: true,
    value: (d) => d.failedRecent,
  },
]
