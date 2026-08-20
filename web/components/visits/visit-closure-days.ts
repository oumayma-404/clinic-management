import { toLocalIso } from "@/lib/format"
import type { VisitToCloseDto } from "@/lib/api/types"

/**
 * « À clôturer », cut into the journées the séances happened on.
 *
 * <p><b>Why the list needed this at all.</b> The worklist is ordered most-recent-first and every row carries a
 * full `JJ/MM/AAAA HH:MM`, so a week of visits reads as one column of near-identical dates with nothing to catch
 * the eye. « Aujourd'hui / Hier / mercredi 12 août » is the prise a reader actually navigates by — you remember a
 * *day*, not a timestamp — and it is what makes « celle-là traîne depuis quatre jours » visible without adding a
 * second badge to every row.</p>
 *
 * <p><b>`daysAgo` is what the sort stopped carrying.</b> While the list opened on the oldest séance, its age was
 * the order itself; opening on today puts the stalest work at the bottom, so the group states its own age instead.
 * It is deliberately `null` inside two days: the heading already says « Aujourd'hui » / « Hier », and an alarm
 * that is always on is one nobody reads.</p>
 *
 * <p>⚠️ <b>The day is the workstation's, deliberately, and this is the one place that is the right call.</b> The
 * standing rule in this product is that a clinic day is a fact about Tunis (`ClinicClock` server-side,
 * `todayLocalIso()` client-side) — but the times printed on these rows come from `formatDateTime`, which renders
 * in the browser's own zone. Grouping by any other rule would put « 00:30 » under a heading naming the previous
 * day, i.e. a header contradicting the rows beneath it. The heading and the times it covers must agree; on a
 * workstation set to Tunisia — every real one — the two rules coincide anyway.</p>
 */
export interface VisitClosureDayGroup {
  /** `YYYY-MM-DD` in the workstation's own zone. Stable within a render, used as the React key. */
  key: string
  /** « Aujourd'hui », « Hier », else « mercredi 12 août ». */
  label: string
  /** Whole days back, from 2 — `null` for today, yesterday and an unparseable instant. See the note above. */
  daysAgo: number | null
  visits: VisitToCloseDto[]
}

/** Groups consecutive visits by day, preserving the server's order within and between groups. */
export function visitClosureDayGroups(visits: VisitToCloseDto[]): VisitClosureDayGroup[] {
  const groups: VisitClosureDayGroup[] = []

  for (const visit of visits) {
    const date = new Date(visit.appointmentDateTime)
    // An unparseable instant would otherwise produce an « Invalid Date » heading and split the list on it.
    const key = Number.isNaN(date.getTime()) ? "" : toLocalIso(date)
    const last = groups[groups.length - 1]

    if (last && last.key === key) {
      last.visits.push(visit)
      continue
    }

    groups.push({ key, label: dayLabel(key, date), daysAgo: daysAgo(key), visits: [visit] })
  }

  return groups
}

/**
 * Whole days between `key` and today, from 2 up — `null` below that and for an unknown date.
 *
 * <p>Counted over the two **day keys**, never over the instants: a séance at 18:00 the day before yesterday is
 * « il y a 2 jours » whatever hour it is now, and subtracting timestamps would call it 1 all morning.</p>
 */
function daysAgo(key: string): number | null {
  if (key === "") return null

  const [y, m, d] = key.split("-").map(Number)
  const then = new Date(y, m - 1, d)
  const now = new Date()
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate())
  const days = Math.round((today.getTime() - then.getTime()) / 86_400_000)

  return days >= 2 ? days : null
}

function dayLabel(key: string, date: Date): string {
  if (key === "") return "Date inconnue"

  const today = new Date()
  const todayKey = toLocalIso(today)
  if (key === todayKey) return "Aujourd'hui"

  const yesterday = new Date(today)
  yesterday.setDate(yesterday.getDate() - 1)
  if (key === toLocalIso(yesterday)) return "Hier"

  // No year: every visit here is inside the chosen window, at most 90 days back, so the year is noise on
  // every row of every ordinary day.
  return date.toLocaleDateString("fr-TN", { weekday: "long", day: "numeric", month: "long" })
}
