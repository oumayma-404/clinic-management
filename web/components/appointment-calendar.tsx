"use client"

import { Card } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { Switch } from "@/components/ui/switch"
import { Label } from "@/components/ui/label"
import { ExportButton } from "@/components/ui/export-button"
import { ChevronLeft, ChevronRight, Calendar, Filter, CloudOff, UploadCloud, Plus, UserX } from "lucide-react"
import { format, addDays, startOfWeek, endOfWeek, addWeeks, subWeeks, subDays, startOfDay, endOfDay, isToday, startOfMonth, endOfMonth, addMonths, subMonths, isSameMonth, isSameDay } from "date-fns"
import { AgendaPhoneHeader } from "@/components/agenda-phone-header"
import { fr } from "date-fns/locale"
import { useMemo, useRef, useEffect, useState, type CSSProperties } from "react"
import { toast } from "sonner"
import { useAppointments } from "@/lib/hooks/use-appointments"
import { googleCalendarApi } from "@/lib/api/google-calendar"
import { ApiError } from "@/lib/api/client"
import { useConnectivity } from "@/lib/connectivity/connectivity"
import { useMediaQuery } from "@/lib/hooks/use-media-query"
import { useSession } from "@/lib/auth/session"
import type { AppointmentDto } from "@/lib/api/types"
import { cn, parseDurationToMinutes } from "@/lib/utils"
import { clinicsApi } from "@/lib/api/clinics"
import { WEEKDAYS, type WorkingDay } from "@/lib/working-hours"
import {
  APPOINTMENT_STATUS_TONE,
  appointmentActsCount,
  appointmentActsSummary,
  appointmentStatusLabel,
  normalizeStatus,
} from "@/components/appointment-labels"
import type { StatusTone } from "@/components/ui/status-tone"

// Generate all 24 hours with hourly intervals
const generateHourlyTimeSlots = (): string[] => {
  const slots: string[] = []
  for (let hour = 0; hour < 24; hour++) {
    slots.push(`${String(hour).padStart(2, '0')}:00`)
  }
  return slots
}
const timeSlots = generateHourlyTimeSlots()

/**
 * The open window for one calendar day, from the clinic's saved hours. Returns null when nothing is configured
 * or the day is closed — the caller shades accordingly.
 */
function openWindowFor(day: Date, hours: WorkingDay[] | null): { fromHour: number; toHour: number } | null {
  if (!hours || hours.length === 0) return null
  const name = WEEKDAYS[(day.getDay() + 6) % 7] // WEEKDAYS starts Monday; Date.getDay() starts Sunday
  const match = hours.find((h) => h.day?.trim().toLowerCase() === name.toLowerCase())
  if (!match || !match.enabled) return null
  const from = Number.parseInt(match.from?.slice(0, 2) ?? "", 10)
  const to = Number.parseInt(match.to?.slice(0, 2) ?? "", 10)
  if (!Number.isFinite(from) || !Number.isFinite(to) || from >= to) return null
  return { fromHour: from, toHour: to }
}

/**
 * Fixed pixel height of one hour row; appointment blocks are positioned/sized against it.
 *
 * 48, not 35. At 35 px/hour the *default* 30-minute appointment is 17.5 px tall — below `MIN_APPT_HEIGHT`,
 * so it was clamped taller than its own slot AND fell under the `isVerySmall` threshold below, which strips
 * the duration badge, the procedure name and the sync badge. The standard appointment rendered in the
 * degraded mode meant for a 5-minute one. Worse, two back-to-back 15-minute appointments sit 8.75 px apart
 * at 35 px/hour while both are clamped to 20 px, so they *visually overlapped* — and `computeOverlapLanes`
 * cannot lane them apart, because they do not overlap in time. At 48 a 30-minute block is 24 px (legible,
 * unclamped) and a 15-minute block is 12 px, so the clamp only ever bites on genuinely tiny durations.
 */
const HOUR_HEIGHT = 48
/**
 * The phone's hour row, taller than the desktop's — Google Calendar's own phone day view is ~64dp/hour, and the
 * reason is not taste. A phone shows ONE day, so the vertical axis is the only axis there is to spend: at 48 a
 * 30-minute visit is 24 px, which after padding fits one clipped line and nothing else, so the patient's name is
 * all a block can ever say. At 64 it is 32 px — name plus its start time — and a 15-minute block still clears
 * `MIN_APPT_HEIGHT` on its own rather than being clamped into its neighbour.
 */
const HOUR_HEIGHT_PHONE = 64
/**
 * Minimum rendered height so a very short appointment still shows the patient name legibly (AC-4).
 *
 * **Two values, because the two devices fail differently.** On a desk machine 18 px is the floor at which a
 * 15-minute block (12 px at `HOUR_HEIGHT`) stays readable. On a phone the appointment block is the most-tapped
 * control in the whole app and 18 px is barely a third of a fingertip, so its floor is 28.
 *
 * ⚠️ A floor is **not** a licence to overlap, and raising it is what makes that visible. Two consecutive
 * quarter-hour appointments sit `15/60 × 64 = 16 px` apart on a phone (12 px on a desktop), so clamping blindly
 * would paint the 09:00 block 12 px over the 09:15 one — an overlap `computeOverlapLanes` **cannot** lane apart,
 * because the two do not overlap *in time* and so are never in the same cluster. `renderAppointmentBlock`
 * therefore clamps to `min(floor, distance to the next appointment's start)`: a short visit with room around it
 * grows to the floor, a short visit with a neighbour keeps its true height. That is also why
 * `computeOverlapLanes` now reports `minutesToNextStart` — the geometry needs it, and the lane packer is the one
 * place that has already sorted the day.
 */
const MIN_APPT_HEIGHT = 18
const MIN_APPT_HEIGHT_PHONE = 28

/**
 * The week grid's columns — **one definition, used by the header, the hour grid and the loading skeleton**,
 * because three copies of a column template is three chances for the dates to sit over the wrong columns.
 *
 * ⚠️ The `120px` is not a taste choice, it is an **arithmetic contract with `weekBandWidthExpr`** (AC-30).
 * While the grid is wider than the viewport it scrolls, so the wrapper is `w-max`: its width is
 * `60 + 7 × 120 = 900px`, and the overlay's `(100% - 60px) / 7` therefore resolves to exactly `120px`. Change
 * one number without the other and every appointment block drifts sideways by a few pixels per column — the
 * kind of wrong that looks like a rendering glitch rather than a maths error. `check:responsive`'s
 * `agenda-scroll` reads both numbers out of this file for that reason.
 *
 * ⚠️ **The fluid override is `lg:`, not `md:`, and that one prefix is the entire 768–1023 px tablet band.**
 * At `md:` the columns became `1fr` while the rail is still 256 px wide, so at 820 px the seven day columns
 * shared ~514 px — about 65 px each, i.e. a ~61 px appointment block, of which the padding, the gap and the
 * duration badge leave roughly 11 px for the patient's name. A tablet is not a small desktop; below `lg:` it
 * gets the same honest scrolling grid a phone would, one size up.
 *
 * ⚠️ `minmax(120px,1fr)` rather than a bare `120px`, and that is the contract again rather than taste: the
 * wrapper carries `min-w-full`, so whenever the container happens to be wider than 900 px (a collapsed rail at
 * ~1000 px) a fixed track leaves dead space at the end while `100%` still means the *wrapper* — and every block
 * drifts. A flexible track absorbs exactly that surplus, so `(100% - 60px) / 7` stays true at every width.
 * Under `w-max` (indefinite available space) a `minmax(120px,1fr)` track resolves to its 120 px base, so the
 * 900 px intrinsic width is unchanged.
 */
const WEEK_COLS = "grid-cols-[60px_repeat(7,minmax(120px,1fr))] lg:grid-cols-[60px_repeat(7,minmax(0,1fr))]"
// Month view: max appointment chips shown per day cell before collapsing the rest into "+N more" (AC-2).
const MONTH_CELL_MAX_CHIPS = 3

/**
 * Mois on a phone is a **continuous scroll into the following months**, and these three numbers bound it.
 *
 * A month that stops at its own last row is a dead end on a phone: the desktop grid can afford ‹ › arrows in a
 * toolbar, but the phone gesture for "what about after that" is to keep scrolling — so the weeks simply carry on,
 * with a month heading where each new month starts, exactly like Google Calendar's month view.
 *
 * ⚠️ The span is **not** fixed at the maximum, because the rendered weeks decide the fetch window (see `endDate`):
 * asking for a year of appointments up front to draw dots nobody scrolled to would make Mois the most expensive
 * read in the app. It starts at three months and grows only as the user actually reaches the bottom.
 */
const PHONE_MONTH_AHEAD_INITIAL = 2
const PHONE_MONTH_AHEAD_STEP = 3
const PHONE_MONTH_AHEAD_MAX = 11
// Distance from the bottom at which the next batch of months is appended.
const PHONE_MONTH_GROW_THRESHOLD_PX = 240
// Dots per day cell in the phone month view before the rest go unshown (the cell is ~52px wide).
const PHONE_MONTH_MAX_DOTS = 3
/**
 * Weekday initials for the phone month header, Monday-first like every other grid here. Hardcoded rather than
 * `format(day, "EEEEE")`, because French gives L M M J V S D — two indistinguishable « M »s — so the header needs
 * a stable key per column anyway, and the cells below carry the full weekday name in their `sr-only` label.
 */
const PHONE_WEEKDAY_INITIALS = ["L", "M", "M", "J", "V", "S", "D"] as const

/**
 * Bottom padding every phone scroller needs so the « Nouveau RDV » floating action is not sitting on top of the
 * last ~52 px of content.
 *
 * The FAB is `fixed` at `bottom-[calc(1rem+var(--bottom-inset))]` and is roughly 40 px tall, so it permanently
 * covers the tail of whatever is scrolled underneath it — the last hour of the day grid, the last week of Mois,
 * Sunday's row in the week strip. Scroll padding is the only fix: a fixed element is out of flow, so nothing
 * below it can shrink around it. `md:pb-0` because the FAB itself is `md:hidden`.
 */
const PHONE_FAB_CLEARANCE = "pb-[calc(var(--bottom-inset)+3.5rem)] md:pb-0"

/**
 * A procedure colour, validated. Returns `#rrggbb` or `null`.
 *
 * ⚠️ The validation is not defensive tidiness, it is the reason this value may be **interpolated into a CSS
 * expression** at all (`color-mix(… ${hex} …)`). `procedureColorHex` is clinic-editable data arriving over the
 * wire; a strict shape test is what stops an unexpected string from becoming a CSS declaration of its own.
 * Anything that is not three or six hex digits falls back to the themed default rather than painting garbage.
 */
function parseProcedureHex(value: string | null | undefined): string | null {
  if (!value) return null
  const hex = value.trim().replace(/^#/, "")
  if (/^[0-9a-fA-F]{3}$/.test(hex)) {
    return `#${hex[0]}${hex[0]}${hex[1]}${hex[1]}${hex[2]}${hex[2]}`
  }
  return /^[0-9a-fA-F]{6}$/.test(hex) ? `#${hex}` : null
}

/** The status tone an appointment paints with — the same table the status badges read. */
function appointmentTone(appointment: { status: string }): StatusTone | undefined {
  return APPOINTMENT_STATUS_TONE[normalizeStatus(appointment.status)]
}

/**
 * How one appointment paints: **the procedure's hue is its identity, the status is a treatment on top of it.**
 * Composed, never one *instead of* the other — which is exactly the defect this replaces.
 *
 * ⚠️ The old function `return`ed from inside `if (appointment.procedureColorHex)`, so the status branch below it
 * was unreachable for any appointment carrying an act colour. Every seeded `ProcedureType` has one, so that was
 * not an edge case, it was the normal case: a **cancelled** visit rendered pixel-identical to a live one, and
 * the same value feeds `renderMonthChip`, the phone-month dots and the week strip — four surfaces, one blind
 * spot. The status branch was *also* missing `noshow` and `inprogress` entirely; both fell through to a default
 * identical to `scheduled`, so « Absent » — the status the desk most needs to spot — could not be shown at all.
 *
 * ⚠️ Two further things the old branch got wrong, both theme-blind. It painted `color: <the raw act hex>` on a
 * 15 %-alpha wash of that same hue over white: a light act colour lands near 2:1, and there was no `dark:`
 * counterpart at all, so on a dark ground the wash sat on nothing. Here the hue is spent where a hue reads best
 * and contrast does not apply — the 4 px left border — the surface is `color-mix(… var(--card))` so it tracks
 * the theme's own card, and the text comes from the theme (`text-card-foreground`). One expression, correct in
 * both themes.
 *
 * The status → treatment mapping is keyed on `APPOINTMENT_STATUS_TONE`, the table `appointmentStatusBadgeClass`
 * already reads, so the agenda and a status badge cannot describe the same appointment differently.
 */
function appointmentAppearance(appointment: AppointmentDto): { className: string; style: CSSProperties } {
  const tone = appointmentTone(appointment)
  const isBusy = !appointment.patientId || appointment.patientName === "Occupé"
  const hex = parseProcedureHex(appointment.procedureColorHex)

  const classes = ["border-l-4"]
  const style: CSSProperties = {}

  if (isBusy) {
    // A blocked slot is not a visit — amber, through the shared warning tokens rather than the `amber-100`
    // literals this carried, which no theme could follow.
    classes.push("bg-warning-wash font-semibold text-warning-ink")
    style.borderLeftColor = "var(--warning)"
  } else if (hex) {
    // « Terminé » keeps the act's hue — it is still that act — at half strength, so a finished column recedes
    // behind the live one instead of shouting the same volume.
    const washPercent = tone === "positive" ? 6 : 12
    classes.push("text-card-foreground shadow-sm")
    style.backgroundColor = `color-mix(in oklch, ${hex} ${washPercent}%, var(--card))`
    style.borderLeftColor = hex
  } else {
    classes.push("bg-accent text-accent-foreground")
    style.borderLeftColor = "var(--primary)"
  }

  switch (tone) {
    case "neutral":
      // Annulé. Struck through and faded, but still legible: « Annulés affichés » exists precisely so the desk
      // can read what was cancelled. The border drops to a neutral, since the act no longer identifies anything.
      classes.push("opacity-60 line-through")
      style.borderLeftColor = "var(--muted-foreground)"
      break
    case "negative":
      // Absent. `border-dashed` reads as "did not happen" and — like the line-through above and the icon the
      // block renders — states the status through **form as well as colour**, which is what makes it survive
      // both a colour-blind reader and the act hue already occupying the same border.
      classes.push("border-dashed")
      style.borderLeftColor = "var(--destructive)"
      break
    case "positive":
      classes.push("shadow-none")
      style.borderLeftColor = "var(--success)"
      break
    case "active":
      // En cours. A ring rather than a border, so it composes with the act's hue instead of overwriting it.
      // `--warning` and not `--accent`: `active` is the amber step of the shared tone scale (the tone that asks
      // for attention), and this theme's `--accent` is a near-white teal that would be invisible as a ring.
      classes.push("ring-2 ring-inset ring-warning")
      break
    default:
      // `pending` (Planifié) and `accepted` (Confirmé) get no treatment: nothing has happened to the visit yet,
      // so the act's own colour is the whole message.
      break
  }

  return { className: cn(...classes), style }
}

/**
 * The legend, as data — because it is rendered **twice** (the phone disclosure and the desktop row) and the two
 * copies had already drifted into hardcoding three of the six statuses between them. « En cours » and
 * « Absent » were missing from both, which is the read-side half of the same defect `appointmentAppearance`
 * fixes: a status the grid could not paint is a status the legend never had to explain.
 */
const LEGEND_ITEMS: { label: string; swatch: string }[] = [
  { label: "Planifié", swatch: "bg-primary" },
  { label: "En cours", swatch: "bg-warning-wash ring-2 ring-inset ring-warning" },
  { label: "Terminé", swatch: "bg-success" },
  { label: "Absent", swatch: "bg-destructive" },
  { label: "Annulé", swatch: "bg-muted-foreground" },
]

interface AppointmentCalendarProps {
  view: "day" | "week" | "month"
  selectedDate: Date
  onDateChange: (date: Date) => void
  onTimeSlotClick?: (date: Date, time: string) => void
  onAppointmentClick?: (appointment: AppointmentDto) => void
  /** Month view only: clicking a day cell's empty area / "+N more" focuses that date in Day view (AC-4). */
  onSelectDay?: (date: Date) => void
  showCancelled?: boolean
  showCompleted?: boolean
  /**
   * Lets the phone header switch view (< md:). Optional: when omitted the phone header is not rendered at all,
   * so an embedding that has no view switcher keeps its current behaviour. Pass the page's `selectView`, never a
   * bare `setView` — the page marks the view DECIDED so its narrow-screen Jour default stops re-asserting and
   * discarding the view the user just chose.
   */
  onViewChange?: (view: "day" | "week" | "month") => void
  onShowCancelledChange?: (show: boolean) => void
  onShowCompletedChange?: (show: boolean) => void
  /** Called after a per-card "Push to Google" succeeds so the parent can refetch (clears the badge). */
  onChanged?: () => void
  /** Optional per-practitioner filter (AC-3.2) — only appointments assigned to this doctor. */
  doctorId?: string
  /**
   * Bump to refetch the current window **in place**. Replaces the old `key={refreshKey}` remount, which threw
   * away scroll position and flashed the grid empty every time anyone in the clinic touched an appointment.
   */
  reloadToken?: unknown
}

export function AppointmentCalendar({ view, selectedDate, onDateChange, onTimeSlotClick, onAppointmentClick, onSelectDay, showCancelled = false, showCompleted = false, onShowCancelledChange, onShowCompletedChange, onChanged, doctorId, reloadToken, onViewChange }: AppointmentCalendarProps) {
  const { internetReachable } = useConnectivity()
  // `md:` — the same boundary the rest of this feature splits devices at. Declared here rather than beside the
  // scroll refs because the fetch window below reads it: Mois on a phone spans several months, not one grid.
  const isNarrow = useMediaQuery("(max-width: 767px)")

  /**
   * Has `matchMedia` answered yet?
   *
   * ⚠️ `useMediaQuery` returns `false` during SSR **and on the first client render** — its own docstring says so,
   * and says callers must read that `false` as "not yet known", never as "definitely a mouse". Four things here
   * key off `isNarrow`, and all four are *geometry*: `hourHeight`, `gutterPx`, `dayGridCols`, and the
   * `view === "week" && isNarrow` branch that swaps the whole time grid for the week strip. So the first frame a
   * phone painted was 48 px hour rows, a 60 px gutter and a 732 px-wide week grid inside a 358 px viewport — the
   * desktop agenda, for one frame, on the smallest screen.
   *
   * A CSS-variable fix (declaring the two heights as custom properties with a `@media` override) would cover the
   * first three and **cannot** cover the fourth: week-strip-versus-time-grid is a different component tree, not a
   * different value, and this component may not touch `globals.css`. So the gate is a mount flag and a neutral
   * skeleton — nothing is painted at a geometry that might be wrong, and the skeleton is one the app already
   * shows on every first load anyway.
   */
  const [mounted, setMounted] = useState(false)
  useEffect(() => {
    setMounted(true)
  }, [])

  /**
   * How many months past the selected one the phone's Mois view currently renders — and therefore fetches.
   * Reset whenever the selected month changes, so navigating months never inherits a previous scroll's span.
   */
  const [monthsAhead, setMonthsAhead] = useState(PHONE_MONTH_AHEAD_INITIAL)
  const selectedMonthKey = format(selectedDate, "yyyy-MM")
  useEffect(() => {
    setMonthsAhead(PHONE_MONTH_AHEAD_INITIAL)
  }, [selectedMonthKey])

  /**
   * The clinic's saved working hours, used to shade closed periods (AC-P1.33).
   *
   * The shading was `hour >= 8 && hour < 18` — hardcoded, and blind to the day of week, so Sunday shaded
   * identically to Monday and a clinic open 07:00–20:00 saw its real hours greyed out. Now read from the
   * clinic's own configuration; null/absent means nothing is configured, which per AC-P1.30 is *unrestricted*,
   * so nothing is shaded rather than everything.
   *
   * NOTE: the grid still renders 0..23 rows. Trimming it to the open hours only would require re-basing the
   * appointment overlay, which positions blocks from midnight against the fixed `HOUR_HEIGHT` (see the
   * load-bearing comment below) — deliberately left for a follow-up rather than risked here.
   */
  const [clinicHours, setClinicHours] = useState<WorkingDay[] | null>(null)
  useEffect(() => {
    let cancelled = false
    clinicsApi
      .getUserStatus()
      .then((status) => {
        if (!cancelled) setClinicHours(status.clinic?.workingHours ?? null)
      })
      // Best-effort: a failure means no shading, never a broken calendar.
      .catch(() => {
        if (!cancelled) setClinicHours(null)
      })
    return () => {
      cancelled = true
    }
  }, [])
  // The "Push to Google" endpoint is AdminOnly — only admins get the action (finding #9); everyone still
  // sees the "non synchronisé" status badge.
  const { user } = useSession()
  const isAdmin = user?.role === "admin"
  const [pushingId, setPushingId] = useState<string | null>(null)

  const handlePushToGoogle = async (appointment: AppointmentDto) => {
    if (!internetReachable || pushingId) return
    setPushingId(appointment.id)
    try {
      await googleCalendarApi.syncAppointment(appointment.id)
      toast.success("Rendez-vous synchronisé avec Google Agenda")
      onChanged?.()
    } catch (error) {
      // Mid-request connectivity loss ⇒ ApiError(status === 0) (google client now routes via client.ts).
      if (error instanceof ApiError && error.status === 0) {
        toast.error("Connexion perdue", {
          description: "La connexion a été interrompue. Réessayez une fois la connexion rétablie.",
        })
      } else {
        toast.error("Échec de la synchronisation", {
          description: error instanceof Error ? error.message : undefined,
        })
      }
    } finally {
      setPushingId(null)
    }
  }

  // "non synchronisé" badge + manual "Push to Google" for real appointments not yet in Google Calendar
  // (AC-6.6, FR-D4). Skips busy slots (no patient — never synced) and already-synced appointments.
  const renderSyncControls = (appointment: AppointmentDto) => {
    if (appointment.isSyncedToGoogle) return null
    if (!appointment.patientId || appointment.patientName === "Occupé") return null
    // Cancelled/completed appointments intentionally carry no Google event (the sync service deletes
    // it); don't advertise "push to Google" for them — the badge is for appointments not yet synced
    // (e.g. created offline), per AC-6.6.
    const status = appointment.status.toLowerCase()
    if (status === "cancelled" || status === "completed") return null

    return (
      <div className="mt-0.5 flex items-center gap-1 flex-shrink-0" onClick={(e) => e.stopPropagation()}>
        <Badge
          variant="secondary"
          className="border-0 bg-warning-wash text-warning-ink h-4 gap-0.5 px-1 text-2xs leading-none"
        >
          <CloudOff className="h-2.5 w-2.5" />
          non synchronisé
        </Badge>
        {isAdmin && (
          <button
            type="button"
            onClick={() => handlePushToGoogle(appointment)}
            disabled={!internetReachable || pushingId === appointment.id}
            title={internetReachable ? "Envoyer vers Google Agenda" : "Connexion internet requise"}
            aria-label={
              internetReachable
                ? `Envoyer le rendez-vous de ${appointment.patientName} vers Google Agenda`
                : "Connexion internet requise pour envoyer vers Google Agenda"
            }
            className="inline-flex h-4 items-center gap-0.5 rounded bg-white/60 px-1 text-2xs leading-none hover:bg-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50 dark:bg-background/60"
          >
            <UploadCloud className="h-2.5 w-2.5" />
            {/* AC-P3.49 — « Push » was the audit's own example. « Envoyer » at this size, with the full
                sentence in the title/aria-label. */}
            {pushingId === appointment.id ? "…" : "Envoyer"}
          </button>
        )}
      </div>
    )
  }
  // Memoized date range for API calls. Month view fetches the full visible grid (first visible day →
  // last visible day = start of the first week through the end of the fixed 6-row grid) so leading/
  // trailing days from adjacent months are populated too (AC-3).
  const startDate = useMemo(() => {
    if (view === "day") return startOfDay(selectedDate)
    if (view === "month") return startOfDay(startOfWeek(startOfMonth(selectedDate), { weekStartsOn: 1 }))
    return startOfDay(startOfWeek(selectedDate, { weekStartsOn: 1 }))
  }, [view, selectedDate])

  const endDate = useMemo(() => {
    if (view === "day") return endOfDay(selectedDate)
    if (view === "month") {
      // The phone's Mois scrolls on into the following months, so the window is whatever it currently renders —
      // the dots and the weeks have to come from one range or a scrolled-to month reads as an empty one.
      if (isNarrow) {
        return endOfDay(
          endOfWeek(endOfMonth(addMonths(startOfMonth(selectedDate), monthsAhead)), { weekStartsOn: 1 }),
        )
      }
      return endOfDay(addDays(startOfWeek(startOfMonth(selectedDate), { weekStartsOn: 1 }), 41)) // 6 rows × 7 - 1
    }
    return endOfDay(addDays(startOfWeek(selectedDate, { weekStartsOn: 1 }), 6)) // 7 days (0-6)
  }, [view, selectedDate, isNarrow, monthsAhead])

  /*
   * `error` used to be dropped on the floor here. A failed fetch therefore rendered a perfectly normal, empty
   * calendar — indistinguishable from a genuinely free day. In a clinic that is the one wrong answer that
   * matters: « rien cet après-midi » when in fact the server never answered. It is surfaced as a banner over
   * the grid (which keeps whatever rows were already loaded) with a Réessayer, following the same pattern
   * `dashboard-section.tsx` uses and the same lesson the procedure-type list in the create dialog documents.
   */
  const { appointments: allAppointments, loading, refetching, error, refetch } = useAppointments(
    startDate,
    endDate,
    undefined,
    undefined,
    doctorId,
    reloadToken,
  )

  // Filter appointments based on status filters
  const appointments = useMemo(() => {
    return allAppointments.filter(apt => {
      const status = apt.status.toLowerCase()
      if (status === 'cancelled') {
        return showCancelled
      }
      if (status === 'completed') {
        return showCompleted
      }
      // By default, show scheduled, confirmed, inprogress, noshow
      return true
    })
  }, [allAppointments, showCancelled, showCompleted])

  /**
   * The visible appointments indexed by their day, earliest first within a day.
   *
   * ⚠️ Not a convenience. The phone's Mois renders up to twelve months of day cells, and `getAppointmentsForDay`
   * is a full scan of the window per cell — 84 cells over a quarter's appointments is a scan per row of a table
   * nobody paged. One pass builds the index the cells then read in O(1).
   */
  const appointmentsByDay = useMemo(() => {
    const byDay = new Map<string, AppointmentDto[]>()
    for (const appt of appointments) {
      const key = format(new Date(appt.appointmentDateTime), "yyyy-MM-dd")
      const bucket = byDay.get(key)
      if (bucket) bucket.push(appt)
      else byDay.set(key, [appt])
    }
    for (const bucket of byDay.values()) {
      bucket.sort(
        (a, b) => new Date(a.appointmentDateTime).getTime() - new Date(b.appointmentDateTime).getTime(),
      )
    }
    return byDay
  }, [appointments])

  /**
   * The weeks the phone's Mois renders: from the week containing the 1st of the selected month, on through the
   * last week of the `monthsAhead`-th month after it. **Weeks, not month pages** — a boundary week belongs to two
   * months and rendering it once (with a heading where the new month starts) is what makes the scroll continuous
   * instead of a stack of grids that each repeat their neighbours' days.
   */
  const phoneMonthWeeks = useMemo(() => {
    const first = startOfWeek(startOfMonth(selectedDate), { weekStartsOn: 1 })
    const last = endOfWeek(endOfMonth(addMonths(startOfMonth(selectedDate), monthsAhead)), { weekStartsOn: 1 })
    const weeks: Date[][] = []
    for (let cursor = first; cursor <= last; cursor = addDays(cursor, 7)) {
      weeks.push(Array.from({ length: 7 }, (_, i) => addDays(cursor, i)))
    }
    return weeks
  }, [selectedDate, monthsAhead])

  // The 42 day cells (fixed 6 rows × 7 columns, weeks start Monday) covering the selected month plus
  // the leading/trailing days that fill the first/last weeks. Fixed length keeps the grid height stable
  // across months (Edge Cases: variable week count).
  const monthGridDays = useMemo(() => {
    const gridStart = startOfWeek(startOfMonth(selectedDate), { weekStartsOn: 1 })
    return Array.from({ length: 42 }, (_, i) => addDays(gridStart, i))
  }, [selectedDate])

  const scrollContainerRef = useRef<HTMLDivElement>(null)
  const [currentTime, setCurrentTime] = useState(new Date())
  const [currentTimePosition, setCurrentTimePosition] = useState<number | null>(null)

  /**
   * Everything the day grid's geometry is measured against, in one place, because the phone and the desktop no
   * longer agree on any of it — and three of these numbers used to be literals scattered across the JSX.
   *
   * ⚠️ `gutterPx` is an arithmetic contract with `dayBandLeftExpr`/`dayBandWidthExpr` AND with the current-time
   * overlay's `left`, which are computed in three different places. Changing one alone drifts the blocks or the
   * red line sideways — the same failure the `WEEK_COLS` note describes for the week grid.
   */
  const hourHeight = isNarrow ? HOUR_HEIGHT_PHONE : HOUR_HEIGHT
  const gutterPx = isNarrow ? 48 : 60
  const dayGridCols = isNarrow ? "grid-cols-[48px_1fr]" : "grid-cols-[60px_1fr]"

  // Update current time every minute
  useEffect(() => {
    const interval = setInterval(() => {
      setCurrentTime(new Date())
    }, 60000) // Update every minute

    return () => clearInterval(interval)
  }, [])

  // Calculate current time position based on actual DOM elements (day/week only — month has no time grid)
  useEffect(() => {
    if (!loading && view !== "month" && scrollContainerRef.current) {
      const now = currentTime
      const hours = now.getHours()
      const minutes = now.getMinutes()
      const currentHourSlot = `${String(hours).padStart(2, '0')}:00`
      
      // Find the actual DOM element for the current hour slot
      const timeSlotElement = scrollContainerRef.current.querySelector(`[data-time-slot="${currentHourSlot}"]`) as HTMLElement
      
      if (timeSlotElement) {
        const slotTop = timeSlotElement.offsetTop
        // Must be `hourHeight`, not a second copy of the number: this used to be a hardcoded `35`, so any
        // change to the row height silently drifted the current-time line away from the appointment blocks.
        const minuteOffset = (minutes / 60) * hourHeight
        const totalPosition = slotTop + minuteOffset
        setCurrentTimePosition(totalPosition)
      }
    }
    // `mounted`: the hour rows this measures do not exist until `matchMedia` has answered and the real grid
    // replaces the neutral skeleton, so without it the line would wait up to a minute for the next tick.
  }, [currentTime, loading, view, selectedDate, mounted, hourHeight])

  // Check if current time is in the visible date range
  const isCurrentTimeVisible = useMemo(() => {
    const now = currentTime
    if (view === "day") {
      return format(now, "yyyy-MM-dd") === format(selectedDate, "yyyy-MM-dd")
    } else {
      const weekStart = startOfWeek(selectedDate, { weekStartsOn: 1 })
      const weekEnd = addDays(weekStart, 6)
      const nowDateOnly = format(now, "yyyy-MM-dd")
      const weekStartDateOnly = format(weekStart, "yyyy-MM-dd")
      const weekEndDateOnly = format(weekEnd, "yyyy-MM-dd")
      return nowDateOnly >= weekStartDateOnly && nowDateOnly <= weekEndDateOnly
    }
  }, [view, selectedDate, currentTime])

  /**
   * The hour the grid opens on when "now" is not in view: **the earlier of the clinic's opening hour and the
   * first appointment actually booked** in the visible range.
   *
   * ⚠️ It was a hardcoded `08:00`, in a component that already has both halves of the real answer in scope
   * (`openWindowFor` and `clinicHours`, added for the closed-hours shading). A clinic opening at 07:00 therefore
   * opened its agenda with the first hour of the day already scrolled off the top, and a 06:30 emergency slot
   * was invisible until someone scrolled up looking for something they had no reason to believe was there —
   * which is the same class of defect as the empty-state overlay below: the screen answering « rien » when it
   * simply has not shown you.
   */
  const initialScrollHour = useMemo(() => {
    const days =
      view === "week"
        ? Array.from({ length: 7 }, (_, i) => addDays(startOfWeek(selectedDate, { weekStartsOn: 1 }), i))
        : [selectedDate]

    let hour = 8
    const openHours = days
      .map((day) => openWindowFor(day, clinicHours)?.fromHour)
      .filter((h): h is number => h !== undefined)
    if (openHours.length > 0) hour = Math.min(...openHours)

    for (const day of days) {
      for (const appointment of appointmentsByDay.get(format(day, "yyyy-MM-dd")) ?? []) {
        hour = Math.min(hour, new Date(appointment.appointmentDateTime).getHours())
      }
    }
    return Math.max(0, Math.min(23, hour))
  }, [view, selectedDate, clinicHours, appointmentsByDay])

  // Initial scroll position (AC-1): when today falls in the visible range, center the current-time
  // line in the viewport (Google-Calendar-like); otherwise open on `initialScrollHour`.
  useEffect(() => {
    if (loading || view === "month") return

    /*
     * ⚠️ `behavior: "smooth"` is an explicit option, and the CSS `scroll-behavior: auto !important` that
     * `globals.css` sets under `prefers-reduced-motion` does **not** override it — per spec that rule only
     * decides what `behavior: "auto"` means. So reduced motion is honoured here, in JS, or this would be the
     * one animation in the app that ignores the setting.
     */
    const reducedMotion =
      typeof window !== "undefined" && window.matchMedia?.("(prefers-reduced-motion: reduce)").matches
    const behavior: ScrollBehavior = reducedMotion ? "auto" : "smooth"

    const positionScroll = () => {
      const container = scrollContainerRef.current
      if (!container || !container.querySelector('[data-time-slot]')) return false

      if (isCurrentTimeVisible) {
        const now = new Date()
        const currentHourSlot = `${String(now.getHours()).padStart(2, '0')}:00`
        const slotElement = container.querySelector(`[data-time-slot="${currentHourSlot}"]`) as HTMLElement | null
        if (slotElement) {
          const nowPosition = slotElement.offsetTop + (now.getMinutes() / 60) * hourHeight
          container.scrollTo({ top: Math.max(0, nowPosition - container.clientHeight / 2), behavior })
          return true
        }
      }

      /*
       * The opening hour at the top — read from the DOM, not computed as `hour * HOUR_HEIGHT`.
       *
       * ⚠️ That arithmetic silently assumed the hour grid began at the scroll container's top. Since AC-30
       * moved the day header INSIDE this scroller (it has to be, or it would not scroll sideways with its
       * columns), the grid now starts one header below — so the old expression landed ~8 AM *minus the
       * header* and the morning was cut off. Asking the row where it actually is cannot drift again,
       * whatever else is stacked above it. `offsetTop` is measured from the `relative` wrapper, which is the
       * scroller's content origin, so it is already in `scrollTop` coordinates.
       */
      const slot = `${String(initialScrollHour).padStart(2, "0")}:00`
      const opening = container.querySelector(`[data-time-slot="${slot}"]`) as HTMLElement | null
      container.scrollTo({ top: opening ? opening.offsetTop : initialScrollHour * hourHeight, behavior })
      return true
    }

    if (!positionScroll()) {
      const timer = setTimeout(positionScroll, 150)
      return () => clearTimeout(timer)
    }
    // `isNarrow` is a dependency because the week time grid is not rendered at all below `md:` — crossing the
    // breakpoint mounts it for the first time, and without a re-run it would sit at midnight instead of the
    // opening hour. `mounted` is one for the same reason: nothing but a neutral skeleton exists until
    // `matchMedia` has answered, so this must run again once the real grid is in the DOM to measure.
  }, [view, selectedDate, loading, isCurrentTimeVisible, isNarrow, mounted, hourHeight, initialScrollHour])

  /**
   * All appointments starting on the given day (already status-filtered), **earliest first**. Each is rendered
   * exactly once by the overlay below (AC-4), replacing the old per-hour-slot duplication.
   *
   * ⚠️ The returned array is the index's own bucket, so callers must not `.sort()` it — that mutates the memo.
   * They no longer need to: the index sorts every bucket once, which is what the three callers each did by hand.
   */
  const getAppointmentsForDay = (date: Date): AppointmentDto[] =>
    appointmentsByDay.get(format(date, "yyyy-MM-dd")) ?? []

  // Horizontal band (as CSS calc expressions, without the wrapping `calc()`) for a day's appointment
  // column. Day view uses the full width right of the 60px time gutter; week view uses one of 7 equal
  // day columns. `laneStyle` splits this band into side-by-side lanes when appointments overlap.
  // Derived from `gutterPx`, never a second copy of it: a phone's gutter is 48px, and a hardcoded 64/70 there
  // left every block overhanging its own column by the difference.
  const dayBandLeftExpr = `${gutterPx + 4}px`
  const dayBandWidthExpr = `100% - ${gutterPx + 10}px`
  const weekBandLeftExpr = (dayIndex: number) => `60px + ((100% - 60px) / 7) * ${dayIndex} + 2px`
  const weekBandWidthExpr = `(100% - 60px) / 7 - 4px`

  // Split a day's column band into `colCount` equal side-by-side lanes; `colCount === 1` fills the band.
  const laneStyle = (leftExpr: string, widthExpr: string, colIndex: number, colCount: number): CSSProperties => {
    if (colCount <= 1) {
      return { left: `calc(${leftExpr})`, width: `calc(${widthExpr})` }
    }
    return {
      left: `calc((${leftExpr}) + (${widthExpr}) * ${colIndex} / ${colCount})`,
      width: `calc((${widthExpr}) / ${colCount} - 2px)`,
    }
  }

  // Assign overlapping appointments to side-by-side columns (Google-Calendar style) so simultaneous
  // appointments share the slot width instead of stacking on top of each other. Appointments are packed
  // into the first free column (a column frees once its last appointment ends); each cluster of
  // chain-overlapping appointments gets `colCount` = the max columns it ever needed, so widths are
  // consistent within the cluster. Works for any number of overlaps. Non-overlapping appointments get
  // `colCount === 1` (full width). Order of the returned lanes is irrelevant (absolute-positioned).
  /**
   * `minutesToNextStart` is how far below this block the next one begins — `Infinity` when nothing follows.
   *
   * It rides along here because this is the one place that has already sorted the day, and the geometry needs
   * it: `MIN_APPT_HEIGHT(_PHONE)` is a floor applied to blocks that are *shorter than their neighbours are
   * apart*, so without knowing that distance the clamp paints one appointment over the next. See the constant's
   * own note — the two appointments in question never overlap **in time**, so no amount of laning separates
   * them; only not growing them does.
   */
  type AppointmentLane = {
    appointment: AppointmentDto
    colIndex: number
    colCount: number
    minutesToNextStart: number
  }
  const computeOverlapLanes = (dayAppointments: AppointmentDto[]): AppointmentLane[] => {
    const items = dayAppointments
      .map((a) => {
        const start = new Date(a.appointmentDateTime)
        const startMin = start.getHours() * 60 + start.getMinutes()
        const endMin = startMin + Math.max(parseDurationToMinutes(a.duration), 1)
        return { appointment: a, startMin, endMin }
      })
      .sort((x, y) => x.startMin - y.startMin || x.endMin - y.endMin)

    /*
     * The next *strictly later* start, per item, in one backward pass over the sorted list. Strictly later
     * matters: two appointments booked at the same minute are laned side by side, so they are not below each
     * other and must not shorten each other. Deliberately measured against the whole day rather than against
     * the item's own lane — a conservative distance can only ever keep a block at its true height, which is
     * the behaviour before this change, whereas a too-generous one paints over a neighbour.
     */
    const nextStart = new Array<number>(items.length).fill(Number.POSITIVE_INFINITY)
    for (let i = items.length - 2; i >= 0; i--) {
      nextStart[i] =
        items[i + 1].startMin > items[i].startMin ? items[i + 1].startMin : nextStart[i + 1]
    }
    const gapOf = new Map<AppointmentDto, number>()
    items.forEach((it, i) => gapOf.set(it.appointment, nextStart[i] - it.startMin))

    const lanes: AppointmentLane[] = []
    let cluster: { appointment: AppointmentDto; startMin: number; endMin: number }[] = []
    let colEnds: number[] = []
    const colOf = new Map<AppointmentDto, number>()
    let clusterMaxEnd = -1

    const flush = () => {
      const colCount = colEnds.length
      for (const it of cluster) {
        lanes.push({
          appointment: it.appointment,
          colIndex: colOf.get(it.appointment) ?? 0,
          colCount,
          minutesToNextStart: gapOf.get(it.appointment) ?? Number.POSITIVE_INFINITY,
        })
      }
      cluster = []
      colEnds = []
      colOf.clear()
      clusterMaxEnd = -1
    }

    for (const it of items) {
      // A gap with no active appointment closes the current cluster.
      if (cluster.length > 0 && it.startMin >= clusterMaxEnd) flush()
      let col = colEnds.findIndex((end) => end <= it.startMin)
      if (col === -1) {
        col = colEnds.length
        colEnds.push(it.endMin)
      } else {
        colEnds[col] = it.endMin
      }
      colOf.set(it.appointment, col)
      cluster.push(it)
      clusterMaxEnd = Math.max(clusterMaxEnd, it.endMin)
    }
    flush()
    return lanes
  }

  // Render an appointment as a single continuous block, positioned by its start minute and sized
  // proportionally to its true duration, with a minimum height for legibility (AC-4). The block is an
  // absolute child of the scroll container (like the current-time line), so it scrolls with content.
  const renderAppointmentBlock = (
    appointment: AppointmentDto,
    positionStyle: CSSProperties,
    minutesToNextStart: number,
  ) => {
    const aptStart = new Date(appointment.appointmentDateTime)
    const durationMinutes = parseDurationToMinutes(appointment.duration)
    const startMinutesOfDay = aptStart.getHours() * 60 + aptStart.getMinutes()
    const top = (startMinutesOfDay / 60) * hourHeight

    /*
     * ── The height arithmetic, which is the whole of fixes (9) and (10) ──────────────────────────────────────
     *
     *   natural   = duration/60 × hourHeight   — the honest height, what the hour ruler says
     *   floor     = 28 px on a phone, 18 px on a desk machine (see MIN_APPT_HEIGHT_PHONE)
     *   roomBelow = distance to the next appointment's START, from `computeOverlapLanes`
     *
     *   height    = max(natural, min(floor, roomBelow))
     *
     * The `min` is the load-bearing half. A 15-minute visit is 16 px at `HOUR_HEIGHT_PHONE` (12 px on a desktop)
     * and consecutive quarter-hour visits sit exactly that far apart, so a bare `max(natural, floor)` clamps the
     * 09:00 block to 28 px and paints 12 px of it over the 09:15 one. `computeOverlapLanes` cannot rescue that:
     * the two do not overlap **in time**, so they are never in one cluster and never get side-by-side lanes —
     * the defect looks like a rendering bug and is arithmetic. Clamping by the real room below means a short
     * visit grows to the floor when it is alone (the common case, and the reason for the floor) and keeps its
     * true 16 px when it has a neighbour (where growing it would lie about the schedule anyway).
     */
    const naturalHeight = (durationMinutes / 60) * hourHeight
    const heightFloor = isNarrow ? MIN_APPT_HEIGHT_PHONE : MIN_APPT_HEIGHT
    const roomBelow = Number.isFinite(minutesToNextStart)
      ? (minutesToNextStart / 60) * hourHeight
      : Number.POSITIVE_INFINITY
    const height = Math.max(naturalHeight, Math.min(heightFloor, roomBelow))

    const isVerySmall = height < 30
    const isSmall = height < 48
    const actsSummary = appointmentActsSummary(appointment)
    const actsCount = appointmentActsCount(appointment)
    const colorStyle = appointmentAppearance(appointment)
    const tone = appointmentTone(appointment)
    const statusLabel = appointmentStatusLabel(appointment.status)
    // The name a screen reader (and the hover tooltip) gets. Status is in it because status is now *paint* on
    // the block — a struck-through, dashed or ringed block states something, and until this it stated it to
    // sighted users only.
    const blockLabel = `${appointment.patientName} · ${format(aptStart, "HH:mm")} · ${durationMinutes} min · ${statusLabel}${
      actsSummary ? ` · ${actsSummary}` : ""
    }`

    /*
     * The phone block is a different shape, not the desktop block scaled down.
     *
     * A 390px-wide block has room for one line, and everything the desktop puts on that line competes with the
     * patient's name for it: a duration badge (which the block's own HEIGHT already states), an acts badge, a
     * « non synchronisé » badge and an « Envoyer » button. That row is most of what made Jour feel crowded, and
     * the Google-Agenda controls in particular are an admin errand — not something a dentist reaches for while
     * looking at this afternoon. So on a phone a block says: who, and — when it is tall enough to have a second
     * line — from when, plus the acts. Both remain fully available from `md:` up, and tapping the block still
     * opens the edit dialog where the sync state is shown.
     *
     * ⚠️ A real `<button>`, unlike its desktop twin. It carries no nested control here, so it can be one — and
     * an appointment is a thing you should be able to reach with a keyboard. The desktop block hosts the
     * « Envoyer » button, so making *that* one a button would nest two buttons (invalid DOM), and `role="button"`
     * would be worse still: it makes its descendants presentational and would hide « Envoyer » from AT entirely.
     *
     * ⚠️ Deliberately **no `touch-target`**. The utility overlays a 44 px `::after`, and this element is
     * `overflow-hidden` (it has to be — that is what clips the second line), so the overlay would be clipped to
     * the block's own box and buy exactly nothing. With side-by-side lanes it would also straddle a neighbour.
     * The 28 px floor above is real painted pixels instead, which is a hit area that actually exists.
     */
    if (isNarrow) {
      // Below 40 px there are two lines to fit in ~32 px, so both tighten their leading. Left at 1.3 the pair
      // measures ~30 px inside a 32 px block and the second line is clipped — which is how a 30-minute visit
      // ended up showing only a name on a row height that was doubled to give it two.
      const tightLines = height < 40
      return (
        <button
          key={appointment.id}
          type="button"
          className={cn(
            "pointer-events-auto absolute z-20 flex cursor-pointer flex-col overflow-hidden rounded-md text-start transition-[box-shadow,transform] duration-[160ms] ease-snap active:scale-[0.99]",
            colorStyle.className,
            tightLines ? "px-1.5 py-0" : "px-1.5 py-0.5",
          )}
          style={{ top: `${top}px`, height: `${height}px`, ...positionStyle, ...colorStyle.style }}
          onClick={(e) => {
            e.stopPropagation()
            onAppointmentClick?.(appointment)
          }}
          title={blockLabel}
          aria-label={blockLabel}
        >
          <span className="flex min-w-0 items-center gap-1">
            {tone === "negative" && <UserX className="h-3 w-3 shrink-0" aria-hidden="true" />}
            <span
              className={cn(
                "min-w-0 truncate font-semibold",
                isVerySmall || tightLines ? "text-2xs leading-[1.15]" : "text-xs leading-[1.3]",
              )}
            >
              {appointment.patientName}
            </span>
          </span>
          {/* 30, not 40. A 30-minute visit — the default — is exactly 32 px at `HOUR_HEIGHT_PHONE`, so a 40 px
              gate excluded the single most common appointment in the app from ever showing its start time, and
              the taller phone row bought the standard case nothing at all. */}
          {height >= 30 && (
            <span
              className={cn(
                "truncate text-2xs opacity-75",
                tightLines ? "leading-[1.15]" : "leading-[1.3]",
              )}
            >
              {format(aptStart, "HH:mm")}
              {actsSummary ? ` · ${actsSummary}` : ""}
            </span>
          )}
        </button>
      )
    }

    return (
      <div
        key={appointment.id}
        className={cn(
          // Press feedback on the block itself: it is the primary click target of the whole screen and had
          // none. Kept at 0.99 because the block is absolutely positioned against its neighbours — anything
          // stronger reads as the appointment jumping out of its slot rather than being pressed.
          "pointer-events-auto absolute z-20 flex cursor-pointer flex-col overflow-hidden rounded transition-[box-shadow,transform] duration-[160ms] ease-snap hover:shadow-md active:scale-[0.99]",
          colorStyle.className,
          isVerySmall ? "px-1 py-0" : isSmall ? "p-1" : "p-1.5",
        )}
        style={{ top: `${top}px`, height: `${height}px`, ...positionStyle, ...colorStyle.style }}
        onClick={(e) => {
          e.stopPropagation()
          onAppointmentClick?.(appointment)
        }}
        title={blockLabel}
        aria-label={blockLabel}
      >
        <div className="flex items-center gap-2 min-w-0">
          {tone === "negative" && <UserX className="h-3 w-3 shrink-0" aria-hidden="true" />}
          <span
            className={cn(
              "font-semibold truncate flex-1 min-w-0",
              isVerySmall ? "text-2xs leading-[1.1]" : isSmall ? "text-xs leading-[1.2]" : "text-sm leading-[1.3]",
            )}
          >
            {appointment.patientName}
          </span>
          {!isVerySmall && (
            <div className="flex items-center gap-1.5 flex-shrink-0">
              <Badge
                variant="secondary"
                className="border-0 bg-white/50 dark:bg-background/50 text-2xs h-4 leading-none px-1.5"
              >
                {durationMinutes}m
              </Badge>
              {/* Day view names the acts (« Détartrage + Obturation »); the narrower views only say how many,
                  because a two-act name does not fit a week column and truncating it to « Détarta… » says less
                  than « 2 actes ». Either way a multi-act séance is never silently shown as a one-act visit. */}
              {view === "day" && actsSummary && (
                <Badge
                  variant="secondary"
                  className="border-0 bg-white/50 dark:bg-background/50 text-2xs h-4 leading-none px-1.5"
                >
                  {actsSummary}
                </Badge>
              )}
              {view !== "day" && actsCount > 1 && (
                <Badge
                  variant="secondary"
                  className="border-0 bg-white/50 dark:bg-background/50 text-2xs h-4 leading-none px-1.5"
                  title={actsSummary ?? undefined}
                >
                  {actsCount} actes
                </Badge>
              )}
            </div>
          )}
        </div>
        {appointment.notes && !isVerySmall && !isSmall && (
          <div className="mt-0.5 truncate text-xs opacity-75 leading-tight flex-shrink-0">{appointment.notes}</div>
        )}
        {!isVerySmall && renderSyncControls(appointment)}
      </div>
    )
  }

  const getWeekDays = () => {
    const start = startOfWeek(selectedDate, { weekStartsOn: 1 })
    return Array.from({ length: 7 }, (_, i) => addDays(start, i)) // All 7 days (Monday to Sunday)
  }

  const handlePrevious = () => {
    if (view === "day") {
      onDateChange(subDays(selectedDate, 1))
    } else if (view === "month") {
      onDateChange(subMonths(selectedDate, 1))
    } else {
      onDateChange(subWeeks(selectedDate, 1))
    }
  }

  /**
   * Horizontal swipe to change day — **Jour only**, deliberately.
   *
   * ⚠️ In Semaine the same container scrolls horizontally (AC-30 removed its `overflow-x-hidden` precisely so it
   * would). A swipe handler on that axis in that view would fight the scroll the user is performing, so the
   * gesture is scoped to the one view where the axis is free rather than dropped entirely.
   *
   * The direction test requires the horizontal travel to clearly dominate the vertical, because this container
   * also scrolls vertically through 24 hours — a diagonal thumb drag must scroll, not jump a day.
   */
  const swipeStartRef = useRef<{ x: number; y: number } | null>(null)
  const SWIPE_MIN_PX = 60
  const SWIPE_AXIS_RATIO = 1.5

  const handleTouchStart = (event: React.TouchEvent) => {
    if (view !== "day" || event.touches.length !== 1) {
      swipeStartRef.current = null
      return
    }
    swipeStartRef.current = { x: event.touches[0].clientX, y: event.touches[0].clientY }
  }

  const handleTouchEnd = (event: React.TouchEvent) => {
    const start = swipeStartRef.current
    swipeStartRef.current = null
    if (!start || view !== "day") return

    const touch = event.changedTouches[0]
    if (!touch) return

    const dx = touch.clientX - start.x
    const dy = touch.clientY - start.y
    if (Math.abs(dx) < SWIPE_MIN_PX || Math.abs(dx) < Math.abs(dy) * SWIPE_AXIS_RATIO) return

    // Swiping left reveals the NEXT day, matching every calendar app's direction convention.
    if (dx < 0) {
      handleNext()
    } else {
      handlePrevious()
    }
  }

  const handleNext = () => {
    if (view === "day") {
      onDateChange(addDays(selectedDate, 1))
    } else if (view === "month") {
      onDateChange(addMonths(selectedDate, 1))
    } else {
      onDateChange(addWeeks(selectedDate, 1))
    }
  }

  const handleToday = () => {
    onDateChange(new Date())
  }

  /**
   * Append the next batch of months once the phone's Mois scroll nears its end — the gesture that means "and
   * after that?" on a phone is to keep scrolling, not to find a chevron.
   *
   * Growing on scroll rather than rendering the cap up front is what keeps the read honest: the visible weeks are
   * also the fetch window (see `endDate`), so a fixed year-wide span would fetch a year of appointments to draw
   * dots on months nobody looked at.
   *
   * ⚠️ The latch is what makes that true in practice. `scroll` fires many times per flick, and every event inside
   * the threshold band satisfied the test — so one thumb flick could step the span 2 → 5 → 8 → 11 and fetch a
   * *year* of appointments in one gesture, which is exactly the read the incremental span exists to avoid. It is
   * cleared by an effect keyed on `monthsAhead`, i.e. once the new weeks have actually rendered and there is
   * genuinely more to reach. Staying latched at the cap is correct and deliberate: the span reset that follows a
   * month change clears it.
   */
  const monthGrowLatchRef = useRef(false)
  useEffect(() => {
    monthGrowLatchRef.current = false
  }, [monthsAhead])

  const handlePhoneMonthScroll = (event: React.UIEvent<HTMLDivElement>) => {
    if (monthGrowLatchRef.current) return
    const el = event.currentTarget
    if (el.scrollHeight - el.scrollTop - el.clientHeight > PHONE_MONTH_GROW_THRESHOLD_PX) return
    monthGrowLatchRef.current = true
    setMonthsAhead((current) => Math.min(current + PHONE_MONTH_AHEAD_STEP, PHONE_MONTH_AHEAD_MAX))
  }

  // A compact month-cell chip: start time + patient name, colored by the shared status/procedure rules
  // (AC-2). Clicking opens the edit dialog (AC-4); stopPropagation keeps the cell's day-navigation from
  // firing too.
  const renderMonthChip = (appointment: AppointmentDto) => {
    const colorStyle = appointmentAppearance(appointment)
    const tone = appointmentTone(appointment)
    const start = format(new Date(appointment.appointmentDateTime), "HH:mm")
    // The status belongs in the label for the same reason it belongs on the block: the chip now *paints* it.
    const chipLabel = `${start} · ${appointment.patientName} · ${appointmentStatusLabel(appointment.status)}`

    return (
      <button
        key={appointment.id}
        type="button"
        onClick={(e) => {
          e.stopPropagation()
          onAppointmentClick?.(appointment)
        }}
        className={cn(
          "flex w-full items-center gap-1 overflow-hidden rounded px-1 py-0.5 text-left text-2xs leading-tight transition-[box-shadow,transform] duration-[160ms] ease-snap hover:shadow-sm active:scale-[0.97]",
          colorStyle.className,
        )}
        style={colorStyle.style}
        title={chipLabel}
        aria-label={chipLabel}
      >
        {tone === "negative" && <UserX className="h-2.5 w-2.5 shrink-0" aria-hidden="true" />}
        <span className="flex-shrink-0 font-semibold">{start}</span>
        <span className="min-w-0 truncate">{appointment.patientName}</span>
      </button>
    )
  }

  // Month view (AC-1..AC-5): a fixed 6×7 day-cell grid (weeks start Monday). Adjacent-month days are
  // dimmed, today is highlighted, each cell lists its appointments as chips with a "+N more" overflow,
  // and clicking a cell's empty area / "+N more" navigates to Day view for that date. No time-of-day
  // layout, current-time line, or scroll-centering (day/week only).
  /**
   * Semaine below `md:` — seven tappable days with their density, instead of the time grid (AC-28).
   *
   * The scrolling week grid built for AC-30 is honest from `lg:` up: seven 120px columns, a sticky gutter and
   * a sticky header. On a 320–390px phone the same grid is a 900px canvas you read through a 320px window,
   * which is navigation, not reading. This answers the question a phone actually asks of a week — *which day
   * do I need?* — and hands off to Jour, where the hours are legible.
   *
   * ⚠️ **Seven rows, not seven columns.** « Strip » in the plan implies a horizontal band, and that was tried
   * on paper first: seven cells across 320px is ~45px each, which fits dots and nothing else — the same
   * unreadable sliver the month chips became — and it would leave the rest of the screen blank, since the
   * time grid is exactly what it replaces. Rows use the width the phone has, so each day can carry its count
   * and its first appointment time as well as the colour dots. Logged as DEV-8.
   *
   * Accessibility follows the month cells: **dots are `aria-hidden` decoration, the count is the fact.**
   */
  const renderWeekStrip = () => {
    /*
     * ⚠️ A skeleton, because this view had **no loading branch at all**. It read `appointments` directly and was
     * invoked without consulting `loading`, so during the very first fetch the phone's Semaine rendered seven
     * rows of « Aucun rendez-vous » — not a blank waiting state but a *positive statement* that the week is
     * free, which in a clinic is the one wrong answer that matters. The other three views all had this branch;
     * this one was written without it and nothing could see the difference.
     */
    if (loading) {
      return (
        <ul className="divide-y" role="status" aria-label="Chargement des rendez-vous">
          {Array.from({ length: 7 }).map((_, i) => (
            <li key={i} className="flex items-center gap-3 px-3 py-3">
              <span className="flex w-10 shrink-0 flex-col items-center gap-1">
                <span className="h-2 w-6 animate-pulse rounded bg-muted" />
                <span className="h-8 w-8 animate-pulse rounded-full bg-muted" />
              </span>
              <span className="flex min-w-0 flex-1 flex-col gap-1.5">
                <span className="h-2 w-10 animate-pulse rounded bg-muted" />
                <span className="h-3 w-32 animate-pulse rounded bg-muted" />
              </span>
            </li>
          ))}
        </ul>
      )
    }

    return (
    <ul
      className={cn(
        "divide-y overflow-y-auto transition-opacity duration-200 ease-snap",
        PHONE_FAB_CLEARANCE,
        refetching && "opacity-60",
      )}
      aria-label="Semaine — choisissez un jour"
    >
      {weekDays.map((day) => {
        const dayAppointments = getAppointmentsForDay(day)
        const visible = dayAppointments.slice(0, MONTH_CELL_MAX_CHIPS)
        const overflow = dayAppointments.length - visible.length
        const first = dayAppointments[0]

        return (
          <li key={day.toISOString()}>
            <button
              type="button"
              onClick={() => onSelectDay?.(day)}
              className="flex w-full items-center gap-3 px-3 py-3 text-start transition-colors hover-hover:hover:bg-accent/30 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring"
            >
              <span className="flex w-10 shrink-0 flex-col items-center gap-0.5">
                <span className="text-2xs font-medium uppercase tracking-wider text-muted-foreground">
                  {format(day, "EEE", { locale: fr })}
                </span>
                <span
                  className={cn(
                    "inline-flex h-8 w-8 items-center justify-center rounded-full text-sm font-semibold",
                    isToday(day) ? "bg-primary text-white shadow-md" : "text-foreground",
                  )}
                >
                  {format(day, "d")}
                </span>
              </span>

              <span className="min-w-0 flex-1">
                {dayAppointments.length === 0 ? (
                  <span className="text-sm text-muted-foreground">Aucun rendez-vous</span>
                ) : (
                  <>
                    <span className="flex flex-wrap items-center gap-1" aria-hidden="true">
                      {visible.map((appointment) => (
                        <span
                          key={appointment.id}
                          className="h-2 w-2 rounded-full"
                          style={{
                            backgroundColor:
                              parseProcedureHex(appointment.procedureColorHex) ?? "var(--muted-foreground)",
                          }}
                        />
                      ))}
                      {overflow > 0 && <span className="text-2xs text-muted-foreground">+{overflow}</span>}
                    </span>
                    <span className="mt-0.5 block truncate text-sm text-foreground">
                      {dayAppointments.length} rendez-vous
                      {first && ` · dès ${format(new Date(first.appointmentDateTime), "HH:mm")}`}
                    </span>
                  </>
                )}
              </span>

              <ChevronRight className="h-4 w-4 shrink-0 text-muted-foreground" aria-hidden="true" />
            </button>
          </li>
        )
      })}
    </ul>
    )
  }

  /**
   * Mois **below `md:`** — a sticky weekday header over a continuous vertical scroll of weeks, with a heading
   * wherever a new month starts. Google Agenda's month view, and a real branch rather than responsive classes on
   * the grid above, for the same reason `renderWeekStrip` is one.
   *
   * ⚠️ The 6×7 grid it replaces was **fixed height**: 42 cells forced into `flex-1`, which on a phone is about
   * 52 × 60 px per cell for a day that may hold four appointments, and — the part that made it feel like a
   * cramped desktop rather than an app — it *ended*. There was nothing after the last row, so « et en octobre ? »
   * meant finding a chevron. Here the weeks simply continue, and each cell is sized by content instead of by a
   * division of the viewport.
   *
   * Days from adjacent months are **not dimmed**, deliberately: in a continuous scroll the boundary week is not
   * "outside" anything, it is just the week it is, and the month headings are what carry the orientation. Dimming
   * half of it would only make the same days look less real above the heading than below it.
   */
  const renderPhoneMonthView = () => (
    <div className="flex h-full min-h-0 flex-col">
      <div className="grid flex-shrink-0 grid-cols-7 border-b bg-card py-1.5">
        {PHONE_WEEKDAY_INITIALS.map((initial, i) => (
          <div
            key={`${initial}-${i}`}
            className="text-center text-2xs font-medium uppercase tracking-wider text-muted-foreground"
          >
            {initial}
          </div>
        ))}
      </div>

      {loading ? (
        <div className="flex-1 space-y-px p-px" role="status" aria-label="Chargement des rendez-vous">
          {Array.from({ length: 7 }).map((_, row) => (
            <div key={row} className="grid grid-cols-7 gap-px">
              {Array.from({ length: 7 }).map((__, col) => (
                <div key={col} className="flex h-14 flex-col items-center justify-center gap-1.5">
                  <div className="h-7 w-7 animate-pulse rounded-full bg-muted" />
                  {(row + col) % 3 === 0 && <div className="h-1.5 w-1.5 animate-pulse rounded-full bg-muted" />}
                </div>
              ))}
            </div>
          ))}
        </div>
      ) : (
        <div
          onScroll={handlePhoneMonthScroll}
          className={cn(
            "min-h-0 flex-1 overflow-y-auto transition-opacity duration-200 ease-snap",
            PHONE_FAB_CLEARANCE,
            refetching && "opacity-60",
          )}
        >
          {phoneMonthWeeks.map((week) => {
            // The week that opens a month carries its heading. Every rendered span begins on the week holding a
            // 1st, so the first week always has one — the scroll never starts under an unlabelled month.
            const monthStart = week.find((day) => day.getDate() === 1)

            return (
              <div key={week[0].toISOString()}>
                {monthStart && (
                  <h3 className="px-3 pb-1 pt-3 text-sm font-semibold capitalize">
                    {format(monthStart, "MMMM yyyy", { locale: fr })}
                  </h3>
                )}
                <div className="grid grid-cols-7 border-b">
                  {week.map((day) => {
                    const dayAppointments = getAppointmentsForDay(day)
                    const dots = dayAppointments.slice(0, PHONE_MONTH_MAX_DOTS)
                    const dotOverflow = dayAppointments.length - dots.length
                    const selected = isSameDay(day, selectedDate)

                    return (
                      <button
                        key={day.toISOString()}
                        type="button"
                        onClick={() => onSelectDay?.(day)}
                        aria-current={selected ? "date" : undefined}
                        className="flex min-h-[56px] flex-col items-center gap-1 border-r py-1.5 transition-colors last:border-r-0 active:bg-accent/40"
                      >
                        <span
                          className={cn(
                            "grid h-7 w-7 place-items-center rounded-full text-sm tabular-nums transition-colors",
                            isToday(day)
                              ? "bg-primary font-semibold text-primary-foreground shadow-sm"
                              : selected
                                ? "border border-primary font-semibold text-primary"
                                : "text-foreground",
                          )}
                        >
                          {format(day, "d")}
                        </span>
                        {/* Dots are decoration; the count below is the accessible fact — the same split the
                            month chips and the week strip already use.

                            ⚠️ The « +N » is not decoration though. Capping at three dots and stopping made a day
                            with eight appointments look exactly like a day with three, and *density* is the only
                            thing this view is for — the week strip already said « +N » for the same reason. */}
                        <span className="flex h-3 items-center gap-[3px]" aria-hidden="true">
                          {dots.map((appointment) => (
                            <span
                              key={appointment.id}
                              className="h-1.5 w-1.5 rounded-full"
                              style={{
                                backgroundColor:
                                  parseProcedureHex(appointment.procedureColorHex) ?? "var(--muted-foreground)",
                              }}
                            />
                          ))}
                          {dotOverflow > 0 && (
                            <span className="text-2xs leading-none text-muted-foreground">+{dotOverflow}</span>
                          )}
                        </span>
                        <span className="sr-only">
                          {format(day, "EEEE d MMMM", { locale: fr })}
                          {dayAppointments.length > 0
                            ? ` — ${dayAppointments.length} rendez-vous`
                            : " — aucun rendez-vous"}
                        </span>
                      </button>
                    )
                  })}
                </div>
              </div>
            )
          })}
          {monthsAhead >= PHONE_MONTH_AHEAD_MAX && (
            // Says where the scroll stops rather than just stopping. Twelve months is the cap because the
            // rendered weeks decide the fetch window, and « l'année prochaine » is a job for the date picker.
            <p className="px-3 py-4 text-center text-2xs text-muted-foreground">
              Utilisez la flèche du mois pour aller plus loin.
            </p>
          )}
        </div>
      )}
    </div>
  )

  /** Mois from `md:` up — the fixed 6×7 grid with named chips. The phone gets `renderPhoneMonthView`. */
  const renderMonthView = () => (
    <div className="flex h-full flex-col min-h-0">
      <div className="grid grid-cols-7 border-b bg-white dark:bg-background flex-shrink-0">
        {monthGridDays.slice(0, 7).map((day) => (
          <div
            key={`dow-${day.toISOString()}`}
            className="border-r py-2 text-center text-xs font-medium uppercase tracking-wider text-muted-foreground last:border-r-0"
          >
            {format(day, "EEE", { locale: fr })}
          </div>
        ))}
      </div>

      {loading ? (
        // A skeleton shaped like the grid it stands in for, so the month does not jump when the chips arrive
        // (the same reasoning as `patients-table.tsx`). Only on the FIRST load — a month change now dims.
        <div className="grid flex-1 grid-cols-7 grid-rows-6 min-h-0" role="status" aria-label="Chargement des rendez-vous">
          {Array.from({ length: 42 }).map((_, i) => (
            <div key={i} className="min-w-0 border-b border-r p-1 last:border-r-0">
              <div className="flex justify-end">
                <div className="h-6 w-6 animate-pulse rounded-full bg-muted" />
              </div>
              {i % 3 === 0 && <div className="mt-1 h-3 animate-pulse rounded bg-muted" />}
            </div>
          ))}
        </div>
      ) : (
        <div
          className={cn(
            "grid flex-1 grid-cols-7 grid-rows-6 min-h-0 transition-opacity duration-200 ease-snap",
            refetching && "opacity-60",
          )}
        >
          {monthGridDays.map((day) => {
            const dayAppointments = getAppointmentsForDay(day)
            const inMonth = isSameMonth(day, selectedDate)
            const visible = dayAppointments.slice(0, MONTH_CELL_MAX_CHIPS)
            const overflow = dayAppointments.length - visible.length

            return (
              /*
               * ⚠️ The cell is a `<div>` with a **real `<button>` stretched across it**, not a `<button>` wrapping
               * its own content — and that is a hard constraint, not a preference. The cell renders appointment
               * chips, which are buttons: a `<button>` cell would nest buttons (invalid DOM, and React says so),
               * and `role="button"` on the div would be worse, because ARIA makes a button's descendants
               * presentational and would hide every chip from assistive tech. A stretched sibling gives the cell
               * one tab stop and a keyboard route to « ouvrir ce jour » while the chips stay separately
               * focusable. It was a bare `<div onClick>` before, i.e. reachable by mouse only, while its phone
               * twin — `renderPhoneMonthView` — was already a real button.
               */
              <div
                key={day.toISOString()}
                className={cn(
                  "relative flex min-w-0 flex-col gap-0.5 overflow-hidden border-b border-r p-1 transition-colors last:border-r-0 hover:bg-accent/30 dark:hover:bg-muted/50",
                  !inMonth && "bg-muted/40 dark:bg-muted/20",
                )}
              >
                <button
                  type="button"
                  onClick={() => onSelectDay?.(day)}
                  aria-label={`Voir le ${format(day, "EEEE d MMMM", { locale: fr })} en vue Jour${
                    dayAppointments.length > 0 ? ` — ${dayAppointments.length} rendez-vous` : " — aucun rendez-vous"
                  }`}
                  className="absolute inset-0 cursor-pointer focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring"
                />
                <div className="pointer-events-none relative flex flex-shrink-0 justify-end">
                  <span
                    className={cn(
                      "inline-flex h-6 w-6 items-center justify-center rounded-full text-xs font-semibold",
                      isToday(day)
                        ? "bg-primary text-primary-foreground shadow-md"
                        : inMonth
                          ? "text-foreground"
                          : "text-muted-foreground/60",
                    )}
                  >
                    {format(day, "d")}
                  </span>
                </div>
                <div className="relative flex min-h-0 flex-col gap-0.5 overflow-hidden">
                  {visible.map((appointment) => renderMonthChip(appointment))}
                  {overflow > 0 && (
                    // Not interactive: the stretched cell button behind it already opens the day, and a second
                    // control saying the same thing would only add a tab stop per cell.
                    <span className="pointer-events-none px-1 text-2xs font-medium text-muted-foreground">
                      +{overflow} autres
                    </span>
                  )}
                </div>
              </div>
            )
          })}
        </div>
      )}
    </div>
  )

  const weekDays = getWeekDays()

  /**
   * Nothing at all in the time grid's visible range — Jour's day, or all seven of Semaine's.
   *
   * Deliberately the *whole range* rather than per day: seven « aucun rendez-vous » cards down one week grid is
   * noise, and in Semaine the question the user is asking is about the week.
   */
  const emptyRange =
    view === "week"
      ? weekDays.every((day) => getAppointmentsForDay(day).length === 0)
      : getAppointmentsForDay(selectedDate).length === 0

  /**
   * The first paint, before `matchMedia` has answered — see the `mounted` note at the top.
   *
   * Deliberately **geometry-free**: no hour rows, no gutter width, no seven columns. Its whole job is to occupy
   * the card honestly for the one frame in which this component does not yet know whether it is drawing a phone
   * or a desk machine, so it must not commit to either. The real per-view skeletons take over immediately after.
   */
  const renderGeometrySkeleton = () => (
    <div className="flex h-full flex-col gap-2 p-3" role="status" aria-label="Chargement de l'agenda">
      {Array.from({ length: 10 }).map((_, i) => (
        <div key={i} className="flex items-center gap-3">
          <div className="h-3 w-9 flex-shrink-0 animate-pulse rounded bg-muted" />
          <div className="h-8 flex-1 animate-pulse rounded bg-muted/60" />
        </div>
      ))}
    </div>
  )

  return (
    <div className="flex h-full flex-col">
      {/*
        The phone header (< md:). Mounted here rather than in the page because it needs the appointment data this
        component owns — the density dots and the « Prochains RDV » summary both read it, and lifting the fetch to
        the page to feed a header would be a much larger change for no gain.
        Design + rationale: features/agenda-phone-ux/design.md.
      */}
      {onViewChange && (
        <AgendaPhoneHeader
          view={view}
          onViewChange={onViewChange}
          selectedDate={selectedDate}
          onDateChange={onDateChange}
          appointments={appointments}
          /*
           * The two status filters, on the phone at last. They lived only in the `md:flex` toolbar below and in
           * the `hidden md:flex` chip row on the page, so `lib/dashboard-links.ts`' « Taux d'absence » link —
           * `/appointments?status=NoShow,Cancelled` — landed a phone user on a *filtered* agenda with nothing on
           * screen saying why and no way to clear it. The chips and the « Filtres » disclosure now live in the
           * header, which is the phone's only toolbar.
           */
          showCancelled={showCancelled}
          showCompleted={showCompleted}
          onShowCancelledChange={onShowCancelledChange}
          onShowCompletedChange={onShowCompletedChange}
        />
      )}

      {/* The existing toolbar is desktop/tablet only: the phone header above replaces it below `md:`. */}
      <div className="mb-3 hidden items-center justify-between flex-wrap gap-3 md:flex">
        <div className="flex items-center gap-3">
          <div className="flex items-center gap-1">
            <Button variant="outline" size="icon" className="h-9 w-9 bg-transparent" onClick={handlePrevious} aria-label="Période précédente">
              <ChevronLeft className="h-4 w-4" />
            </Button>
            <Button variant="outline" size="icon" className="h-9 w-9 bg-transparent" onClick={handleNext} aria-label="Période suivante">
              <ChevronRight className="h-4 w-4" />
            </Button>
          </div>
          <Button variant="outline" size="sm" onClick={handleToday} className="gap-2 bg-transparent">
            <Calendar className="h-4 w-4" />
            Aujourd&apos;hui
          </Button>
          {/* `min-w-0` + `truncate`: « mercredi 12 novembre 2026 » is wider than a 390px phone, and an
              un-truncated title is what forced the whole toolbar onto a fifth row (AC-31). */}
          <div className="ml-2 min-w-0 truncate text-base font-semibold md:text-xl">
            {view === "week"
              ? `${format(weekDays[0], "d MMM", { locale: fr })} - ${format(weekDays[6], "d MMM yyyy", { locale: fr })}`
              : view === "month"
                ? format(selectedDate, "MMMM yyyy", { locale: fr })
                : format(selectedDate, "EEEE d MMMM yyyy", { locale: fr })}
          </div>
        </div>

        <div className="flex w-full flex-wrap items-center gap-x-4 gap-y-2 md:w-auto">
          {/* Status legend — desktop/tablet. The phone's copy is hoisted out of this `md:flex` row entirely
              (see below the error banner); both render `LEGEND_ITEMS`, so neither can go stale on its own. */}
          <div className="hidden items-center gap-4 text-sm md:flex">
            {LEGEND_ITEMS.map((item) => (
              <div key={item.label} className="flex items-center gap-2">
                <div className={cn("h-3 w-3 rounded", item.swatch)} />
                <span className="text-muted-foreground">{item.label}</span>
              </div>
            ))}
            {/* The grey shading had no legend entry at all, so a closed period looked like a rendering
                artefact. Only shown when hours are actually configured (AC-P1.33). */}
            {clinicHours && clinicHours.length > 0 && (
              <div className="flex items-center gap-2">
                <div className="h-3 w-3 rounded border bg-muted" />
                <span className="text-muted-foreground">Hors horaires d&apos;ouverture</span>
              </div>
            )}
          </div>

          {/* Filters. ⚠️ The dividing border is `md:` only — on a wrapped row a lone `border-l` reads as a
              rendering artefact rather than a separator between two groups. */}
          <div className="flex flex-wrap items-center gap-x-4 gap-y-2 md:border-l md:pl-4">
            <div className="flex items-center gap-2">
              <Filter className="h-4 w-4 text-muted-foreground" />
              <span className="text-sm font-medium text-muted-foreground">Afficher :</span>
            </div>
            <div className="flex items-center gap-4">
              <div className="flex items-center gap-2">
                <Switch
                  id="show-completed"
                  checked={showCompleted}
                  onCheckedChange={(checked) => {
                    onShowCompletedChange?.(checked)
                  }}
                />
                <Label htmlFor="show-completed" className="text-sm cursor-pointer">
                  Terminés
                </Label>
              </div>
              <div className="flex items-center gap-2">
                <Switch
                  id="show-cancelled"
                  checked={showCancelled}
                  onCheckedChange={(checked) => {
                    onShowCancelledChange?.(checked)
                  }}
                />
                <Label htmlFor="show-cancelled" className="text-sm cursor-pointer">
                  Annulés
                </Label>
              </div>
            </div>
          </div>

          {/*
            L5 — « Exporter » lives on the calendar, not on `app/appointments/page.tsx`, because the **window**
            does: `startDate`/`endDate` above are derived from `view` + `selectedDate` + (on a phone's Mois)
            `monthsAhead`, all of which are internal to this component. A copy of that derivation at page level
            would be a second authority on « which days are on screen » and would disagree the moment the phone
            lazily loaded another month. The two bounds go through the same `startOfDay`/`endOfDay`/`toISOString`
            transform `useAppointments` applies, so the file covers exactly the days the grid drew.

            ⚠️ It carries the window and the praticien, and **not** « Terminés » / « Annulés ». Those two toggles
            *reveal* rather than narrow — the grid hides completed and cancelled visits by default — so honouring
            them would mean the ordinary export of a past week omitted almost every appointment in it. `Statut` is
            a column in the CSV, so nothing is hidden: the file is the whole window and the spreadsheet can filter
            it. Stated here because it is the one place in L5 where the file is deliberately a superset.
          */}
          <ExportButton
            path="/appointments/export"
            label="rendez-vous"
            compact
            params={{
              startDate: startOfDay(startDate).toISOString(),
              endDate: endOfDay(endDate).toISOString(),
              doctorId,
            }}
          />
        </div>
      </div>

      {/*
        The fetch failed. This is deliberately a banner ABOVE the grid rather than a replacement for it: the
        rows already loaded are still the best information available, and an agenda that goes blank on a
        transient error is worse than a stale one that says so. Before this existed, `error` was destructured
        away and a failed request rendered as an empty day — « rien cet après-midi » when the server simply
        never answered.
      */}
      {error && (
        <div
          role="status"
          className="mb-3 flex flex-wrap items-center justify-between gap-3 rounded-lg border border-warning/30 bg-warning-wash px-3 py-2 text-sm text-warning-ink"
        >
          <span>
            Les rendez-vous n&apos;ont pas pu être chargés
            {appointments.length > 0 ? " — l'agenda ci-dessous peut être incomplet." : "."} {error}
          </span>
          <Button variant="outline" size="sm" onClick={() => void refetch()} disabled={refetching}>
            {refetching ? "Chargement…" : "Réessayer"}
          </Button>
        </div>
      )}

      {/*
        The legend on a phone, and it is a **sibling of the toolbar rather than a child of it** — which is the
        entire fix.

        ⚠️ This `<details className="md:hidden">` used to live inside the toolbar `<div className="… hidden … md:flex">`
        above. The ancestor is `display:none` below `md:` and the child hides itself from `md:` up, so the two
        conditions never overlapped and the phone legend **rendered at no width, in no view, ever**. It is the
        exact defect a `md:hidden` inside a `hidden md:flex` always is, and it is invisible in review because both
        halves read as correct on their own.

        The legend is REFERENCE, not a control, and five items of it is two rows on a phone — so below `md:` it
        folds into a disclosure rather than being deleted. Hiding it outright would have been simpler and wrong:
        the grey « hors horaires » shading has no other explanation anywhere.
      */}
      <details className="mb-2 px-3 md:hidden">
        <summary className="touch-target cursor-pointer text-sm text-muted-foreground">Légende</summary>
        <div className="mt-2 flex flex-wrap items-center gap-x-4 gap-y-1 text-sm">
          {LEGEND_ITEMS.map((item) => (
            <div key={item.label} className="flex items-center gap-2">
              <div className={cn("h-3 w-3 rounded", item.swatch)} />
              <span className="text-muted-foreground">{item.label}</span>
            </div>
          ))}
          {clinicHours && clinicHours.length > 0 && (
            <div className="flex items-center gap-2">
              <div className="h-3 w-3 rounded border bg-muted" />
              <span className="text-muted-foreground">Hors horaires d&apos;ouverture</span>
            </div>
          )}
        </div>
      </details>

      {/*
        Edge-to-edge on a phone. A card's border, radius, shadow and — the expensive one — the `py-6` it inherits
        from the primitive spend about 56 px of the one screen the agenda has, to draw a frame around content that
        already fills the viewport. No phone calendar app frames its own grid.
      */}
      <Card className="min-h-0 flex-1 overflow-hidden rounded-none border-0 py-0 shadow-none md:rounded-xl md:border md:py-6 md:shadow-sm">
        {!mounted ? (
          renderGeometrySkeleton()
        ) : view === "month" ? (
          isNarrow ? renderPhoneMonthView() : renderMonthView()
        ) : view === "week" && isNarrow ? (
          /* ⚠️ A real branch, not `md:hidden` on the grid. A hidden (`display:none`) scroll container reports
             `offsetTop: 0` for every row, so the opening-hour positioning and the current-time line would both
             compute against a zero-height layout and be wrong the moment the viewport crossed back to `md:`. Not
             rendering it means there is nothing to mis-measure, and the scroll effect re-runs on `isNarrow`. */
          renderWeekStrip()
        ) : (
        <div className="relative flex h-full flex-col min-h-0">
          {/*
            AC-30 — **one element scrolls both axes**, and everything else follows from that.

            The week grid is the only wide content in the app that used to be clipped rather than scrollable
            (`overflow-x-hidden`), which is what AC-P3.14 forbids. Making it scroll is not a class change,
            for two reasons:

            1. **The day header must be INSIDE this scroller.** It used to be a sibling above it, so scrolling
               sideways would have left the dates sitting over the wrong columns. It is `sticky top-0` here
               instead, which is the same result vertically and stays honest horizontally.
            2. **A `sticky left-0` gutter only sticks to a scrollport that scrolls horizontally.** So the
               obvious alternative — a horizontal scroller outside, a vertical one inside — cannot give a
               sticky time column at all. Hence `overflow-auto` on this single element.

            ⚠️ The inner wrapper below is load-bearing, not tidiness: the appointment overlay is positioned
            with percentages (`(100% - 60px) / 7`), and a percentage resolves against the containing block's
            **padding box** — the visible width, not the scrollable content width. With the overlay as a direct
            child of this scroller, the moment the grid got wider than the viewport every block would land in
            the wrong column, silently. The wrapper is `w-max`, so `100%` means *the grid*, and the `calc()`
            strings — and with them the `HOUR_HEIGHT` invariant — need no change at all.
          */}
          <div
            ref={scrollContainerRef}
            onTouchStart={handleTouchStart}
            onTouchEnd={handleTouchEnd}
            className={cn(
              "relative min-h-0 flex-1 overflow-auto transition-opacity duration-200 ease-snap",
              refetching && "opacity-60",
            )}
          >
          {/* `lg:w-full`, matching `WEEK_COLS` — the two are one contract and moving only one of them is what
              puts every block in the wrong column. `PHONE_FAB_CLEARANCE` is padding-bottom only, so it changes
              neither the padding box's width (the `100%` the bands resolve against) nor the `top: 0` origin the
              absolutely-positioned blocks measure from. */}
          <div
            className={cn(
              "relative",
              PHONE_FAB_CLEARANCE,
              view === "week" ? "w-max min-w-full lg:w-full" : "w-full",
            )}
          >
          {/*
            Not rendered on a phone: below `md:` this branch is only ever Jour, and the phone header directly
            above already names the day — « mer. 12 novembre » in its title and the same date circled in its week
            strip. A third statement of it costs ~46 px of the only screen the grid has to itself. Google Agenda's
            phone day view has no in-grid date header for exactly this reason.
          */}
          {!isNarrow && (
          <div
            className={cn(
              // z-50: above the sticky gutter (z-30) and the current-time overlay (z-40), both of which
              // scroll under it.
              "sticky top-0 z-50 grid border-b bg-white dark:bg-background",
              view === "week" ? WEEK_COLS : dayGridCols,
            )}
          >
            {/* The corner cell is sticky on BOTH axes, so it stays over the time gutter as it scrolls. */}
            <div className="sticky left-0 z-10 border-r bg-muted/60" />
            {view === "week" ? (
              /*
               * A **button**, so Semaine's day header opens that day the way a Mois cell does.
               *
               * ⚠️ The circle below already carried `hover:bg-gray-100` while the cell was a plain `<div>` — an
               * affordance with nothing behind it, which is worse than no affordance: it invites the click Mois
               * has trained the user to expect and then swallows it. Only the header navigates; the hour cells
               * below still open the create dialog, which is why this is not a click handler on the column.
               */
              weekDays.map((day) => (
                <button
                  key={day.toISOString()}
                  type="button"
                  onClick={() => onSelectDay?.(day)}
                  aria-label={`Voir le ${format(day, "EEEE d MMMM", { locale: fr })} en vue Jour`}
                  className="min-w-0 border-r py-2 text-center transition-colors last:border-r-0 hover:bg-accent/30 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring dark:hover:bg-muted/50"
                >
                  <div className="mb-1 text-xs font-medium uppercase tracking-wider text-muted-foreground">
                    {format(day, "EEE", { locale: fr })}
                  </div>
                  <div
                    className={cn(
                      "mx-auto inline-flex h-8 w-8 items-center justify-center rounded-full text-xs font-semibold transition-colors",
                      isToday(day) ? "bg-primary text-white shadow-md" : "text-foreground",
                    )}
                  >
                    {format(day, "d")}
                  </div>
                </button>
              ))
            ) : (
              <div className="py-2 text-center">
                <div className="mb-1 text-xs font-medium uppercase tracking-wider text-muted-foreground">
                  {format(selectedDate, "EEEE", { locale: fr })}
                </div>
                <div
                  className={cn(
                    "mx-auto inline-flex h-8 w-8 items-center justify-center rounded-full text-xs font-semibold",
                    isToday(selectedDate) ? "bg-primary text-white shadow-md" : "text-foreground",
                  )}
                >
                  {format(selectedDate, "d")}
                </div>
              </div>
            )}
          </div>
          )}

          {loading ? (
            // Skeleton rows at the real hour height, so the grid does not resize when the appointments land.
            <div role="status" aria-label="Chargement des rendez-vous">
              {Array.from({ length: 12 }).map((_, i) => (
                <div
                  key={i}
                  className={cn("grid border-b", view === "week" ? WEEK_COLS : dayGridCols)}
                  style={{ height: hourHeight }}
                >
                  <div
                    className={cn(
                      "flex items-center justify-end px-2",
                      !isNarrow && "border-r bg-muted/60",
                    )}
                  >
                    <div className="h-3 w-8 animate-pulse rounded bg-muted-foreground/20" />
                  </div>
                  {Array.from({ length: view === "week" ? 7 : 1 }).map((__, c) => (
                    <div key={c} className="border-r p-1 last:border-r-0">
                      {(i + c) % 4 === 0 && <div className="h-full animate-pulse rounded bg-muted" />}
                    </div>
                  ))}
                </div>
              ))}
            </div>
          ) : (
            <>
              {/* Current time indicator line - overlay */}
              {isCurrentTimeVisible && currentTimePosition !== null && (
                <>
                  <div
                    className="absolute z-40 pointer-events-none"
                    style={{
                      left: `${gutterPx}px`,
                      right: '0',
                      top: `${currentTimePosition}px`,
                      height: '2px',
                      marginTop: '-1px',
                    }}
                  >
                    {/* Saturated on a phone: it is the one mark that says « maintenant » on a screen showing a
                        single day, and the desktop's pale 300 disappears under a coloured block behind it. */}
                    <div className={cn("h-full shadow-sm", isNarrow ? "bg-destructive" : "bg-destructive/60")} />
                  </div>
                  {/* Current time dot on time column */}
                  <div
                    className="absolute z-40 pointer-events-none"
                    style={{
                      left: `${gutterPx - 14}px`,
                      top: `${currentTimePosition - 4}px`,
                    }}
                  >
                    <div className="w-2 h-2 rounded-full bg-destructive shadow-sm" />
                  </div>
                  {/*
                    The clock reading, in the gutter. Below `md:` only: on a phone the hour labels scroll far
                    enough out of view that the line alone says "now" without saying *when* now is. On desktop the
                    surrounding hour labels are always visible, so the same label would only add clutter.
                    Opaque background because it sits on top of the gutter's own hour text.
                  */}
                  <div
                    className="absolute z-40 pointer-events-none md:hidden"
                    style={{ left: '2px', top: `${currentTimePosition - 7}px` }}
                  >
                    <span className="rounded bg-card px-0.5 text-2xs font-bold tabular-nums text-destructive">
                      {format(currentTime, "HH:mm")}
                    </span>
                  </div>
                </>
              )}
              {/* Hour grid: time labels + empty clickable cells (gridlines + click-to-create). */}
              <div className={cn("grid", view === "week" ? WEEK_COLS : dayGridCols)}>
                {timeSlots.map((time) => {
                  const hour = Number.parseInt(time.split(":")[0])
                  // The label column has no single day in week view, so it reflects the focused date; each day
                  // cell below shades against its OWN window (Sunday no longer shades like Monday).
                  const labelWindow = openWindowFor(selectedDate, clinicHours)
                  const isWorkingHours =
                    labelWindow === null || (hour >= labelWindow.fromHour && hour < labelWindow.toHour)

                  return (
                    <div key={time} className="contents">
                      {/* Time label. `leading-none` is load-bearing: without it this div inherits the global
                          line-height (1.5) and can outgrow the row. The appointment overlay positions blocks
                          against the fixed `hourHeight`, so any row taller than it makes appointments drift
                          upward (e.g. a 17:00 block landing near 14:00). Keep rows exactly `hourHeight` tall —
                          which is why the day cells below set `minHeight` from the value itself rather than
                          repeating the number in a Tailwind class, as they used to.

                          ⚠️ On a phone the gutter is a **bare column of times**, not a shaded panel: no fill, no
                          right border, no closed-hours tint. A filled 48px band down the edge of a 390px screen
                          reads as a second column of content competing with the appointments — it is the same
                          chrome-instead-of-data trade as the card frame. The hour lines and the day cells still
                          carry the grid; the shading of closed hours stays on the cells, where it means
                          something. */}
                      <div
                        className={cn(
                          // `sticky left-0` keeps the hour visible while the week scrolls sideways (AC-30) —
                          // a scrolled column with no time beside it is unreadable. z-30 puts it above the
                          // appointment blocks (z-20) and below the current-time dot (z-40), which is drawn
                          // just inside this very column.
                          "sticky left-0 z-30 px-2 py-2 text-right leading-none",
                          isNarrow
                            ? "bg-card"
                            : cn(
                                "border-b border-r bg-muted/60",
                                !isWorkingHours && "bg-muted/50",
                              ),
                        )}
                      >
                        <span
                          className={cn(
                            "font-medium",
                            isNarrow
                              ? "text-2xs text-muted-foreground"
                              : cn(
                                  "text-xs",
                                  isWorkingHours
                                    ? "text-foreground"
                                    : "text-muted-foreground",
                                ),
                          )}
                        >
                          {time}
                        </span>
                      </div>

                      {/* Day columns (empty — appointments render in the overlay below).
                          ⚠️ These stay plain `<div onClick>`, deliberately, and it is the one place in this file
                          where "make it a button" is the wrong answer: there are 24 of them in Jour and **168**
                          in Semaine, so giving each a `tabIndex` would put 168 tab stops between the toolbar and
                          the appointments — a keyboard user would have to traverse the empty grid to reach the
                          only things on it. The keyboard route to creating an appointment is « Nouveau
                          rendez-vous » (the toolbar button, and the phone's floating action), which opens the
                          same dialog with the same date; the cell click is a pointer shortcut on top of it. */}
                      {view === "week"
                        ? weekDays.map((day) => {
                            // Each day shades against its OWN window — Sunday no longer shades like Monday.
                            const dayWindow = openWindowFor(day, clinicHours)
                            const dayOpen =
                              dayWindow === null || (hour >= dayWindow.fromHour && hour < dayWindow.toHour)
                            return (
                              <div
                                key={`${day.toISOString()}-${time}`}
                                className={cn(
                                  "min-w-0 cursor-pointer border-b border-r transition-colors last:border-r-0 hover:bg-accent/30 dark:hover:bg-muted/50",
                                  !dayOpen && "bg-muted/40 opacity-70",
                                )}
                                style={{ minHeight: hourHeight }}
                                onClick={() => onTimeSlotClick?.(day, time)}
                                data-time-slot={time}
                              />
                            )
                          })
                        : (
                            <div
                              className={cn(
                                "cursor-pointer border-b transition-colors hover:bg-accent/30 dark:hover:bg-muted/50",
                                !isNarrow && "border-r",
                                !isWorkingHours && "bg-muted/40 opacity-70",
                              )}
                              style={{ minHeight: hourHeight }}
                              onClick={() => onTimeSlotClick?.(selectedDate, time)}
                              data-time-slot={time}
                            />
                          )}
                    </div>
                  )
                })}
              </div>

              {/* Appointment overlay (AC-4): each appointment rendered exactly once, positioned by its
                  start minute and sized proportionally to its duration. Absolute children of the `w-max`
                  wrapper — NOT of the scroll container — so their percentage widths mean the grid rather
                  than the viewport (see the AC-30 note above); the empty cells keep gridlines + clicks. */}
              {view === "week"
                ? weekDays.map((day, dayIndex) =>
                    computeOverlapLanes(getAppointmentsForDay(day)).map(
                      ({ appointment, colIndex, colCount, minutesToNextStart }) =>
                        renderAppointmentBlock(
                          appointment,
                          laneStyle(weekBandLeftExpr(dayIndex), weekBandWidthExpr, colIndex, colCount),
                          minutesToNextStart,
                        ),
                    ),
                  )
                : computeOverlapLanes(getAppointmentsForDay(selectedDate)).map(
                    ({ appointment, colIndex, colCount, minutesToNextStart }) =>
                      renderAppointmentBlock(
                        appointment,
                        laneStyle(dayBandLeftExpr, dayBandWidthExpr, colIndex, colCount),
                        minutesToNextStart,
                      ),
                  )}
            </>
          )}
          </div>
          </div>

          {/*
            The empty state for the time grid. A free day was ~1536 px of blank ruled paper: nothing on screen
            distinguished « personne aujourd'hui » from « the grid has not filled in », and nothing offered the
            obvious next move.

            ⚠️ **Overlaid, not substituted.** Replacing the grid would take away the click-an-hour-to-book
            gesture, which is the very thing the card is telling you about — hence `pointer-events-none` on the
            sheet and `auto` on the button alone, so every hour cell underneath stays tappable.

            ⚠️ Suppressed while `error` is set. The banner above already says the fetch failed; « aucun rendez-vous »
            printed next to it is exactly the false statement that banner exists to prevent, and it would be
            *more* emphatic than the truth.
          */}
          {!loading && !error && emptyRange && (
            <div className="pointer-events-none absolute inset-0 z-40 flex items-center justify-center p-6">
              <div className="max-w-xs rounded-xl border bg-card/95 px-4 py-5 text-center shadow-md backdrop-blur-[1px]">
                <p className="text-sm font-medium">
                  {view === "week" ? "Aucun rendez-vous cette semaine" : "Aucun rendez-vous ce jour-là"}
                </p>
                <p className="mt-1 text-2xs text-muted-foreground">
                  {/* Two spellings of the same sentence rather than one that is wrong on half the devices: this
                      app is used on a phone in a corridor and on a desk machine at reception. */}
                  <span className="md:hidden">Touchez une heure pour en ajouter un</span>
                  <span className="hidden md:inline">Cliquez sur une heure pour en ajouter un</span>
                </p>
                <Button
                  size="sm"
                  className="pointer-events-auto mt-3 gap-2"
                  onClick={() =>
                    onTimeSlotClick?.(selectedDate, `${String(initialScrollHour).padStart(2, "0")}:00`)
                  }
                >
                  <Plus className="h-4 w-4" />
                  Nouveau rendez-vous
                </Button>
              </div>
            </div>
          )}
        </div>
        )}
      </Card>
    </div>
  )
}
