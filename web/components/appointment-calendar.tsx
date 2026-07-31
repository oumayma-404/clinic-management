"use client"

import { Card } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { Switch } from "@/components/ui/switch"
import { Label } from "@/components/ui/label"
import { ChevronLeft, ChevronRight, Calendar, Filter, CloudOff, UploadCloud } from "lucide-react"
import { format, addDays, startOfWeek, addWeeks, subWeeks, subDays, startOfDay, endOfDay, isToday, startOfMonth, addMonths, subMonths, isSameMonth } from "date-fns"
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
import { appointmentActsCount, appointmentActsSummary } from "@/components/appointment-labels"

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
// Minimum rendered height so a very short appointment still shows the patient name legibly (AC-4).
const MIN_APPT_HEIGHT = 18

/**
 * The week grid's columns — **one definition, used by the header, the hour grid and the loading skeleton**,
 * because three copies of a column template is three chances for the dates to sit over the wrong columns.
 *
 * ⚠️ The `96px` is not a taste choice, it is an **arithmetic contract with `weekBandWidthExpr`** (AC-30).
 * Below `md:` the grid is wider than the viewport and scrolls, so the wrapper is `w-max`: its width is
 * `60 + 7 × 96 = 732px`, and the overlay's `(100% - 60px) / 7` therefore resolves to exactly `96px`. Change
 * one number without the other and every appointment block drifts sideways by a few pixels per column — the
 * kind of wrong that looks like a rendering glitch rather than a maths error. At `md:` and up the wrapper is
 * `w-full` and the columns are `1fr`, so both sides track the container and the contract holds for free.
 */
const WEEK_COLS = "grid-cols-[60px_repeat(7,96px)] md:grid-cols-[60px_repeat(7,minmax(0,1fr))]"
// Month view: max appointment chips shown per day cell before collapsing the rest into "+N more" (AC-2).
const MONTH_CELL_MAX_CHIPS = 3

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

export function AppointmentCalendar({ view, selectedDate, onDateChange, onTimeSlotClick, onAppointmentClick, onSelectDay, showCancelled = false, showCompleted = false, onShowCancelledChange, onShowCompletedChange, onChanged, doctorId, reloadToken }: AppointmentCalendarProps) {
  const { internetReachable } = useConnectivity()

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
          className="border-0 bg-amber-100 text-amber-800 dark:bg-amber-500/20 dark:text-amber-400 h-4 gap-0.5 px-1 text-2xs leading-none"
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
    if (view === "month") return endOfDay(addDays(startOfWeek(startOfMonth(selectedDate), { weekStartsOn: 1 }), 41)) // 6 rows × 7 - 1
    return endOfDay(addDays(startOfWeek(selectedDate, { weekStartsOn: 1 }), 6)) // 7 days (0-6)
  }, [view, selectedDate])

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
  // `md:` — the same boundary the rest of this feature splits devices at. Semaine swaps to the density strip
  // below it (AC-28); Jour and Mois are unaffected.
  const isNarrow = useMediaQuery("(max-width: 767px)")

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
        // Must be HOUR_HEIGHT, not a second copy of the number: this used to be a hardcoded `35`, so any
        // change to the row height silently drifted the current-time line away from the appointment blocks.
        const minuteOffset = (minutes / 60) * HOUR_HEIGHT
        const totalPosition = slotTop + minuteOffset
        setCurrentTimePosition(totalPosition)
      }
    }
  }, [currentTime, loading, view, selectedDate])

  const getCurrentTimePosition = useMemo(() => {
    const now = currentTime
    const hours = now.getHours()
    const minutes = now.getMinutes()
    return { currentHour: hours, currentMinute: minutes }
  }, [currentTime])

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

  // Initial scroll position (AC-1): when today falls in the visible range, center the current-time
  // line in the viewport (Google-Calendar-like); otherwise scroll so 8 AM is at the top, as before.
  useEffect(() => {
    if (loading || view === "month") return

    const positionScroll = () => {
      const container = scrollContainerRef.current
      if (!container || !container.querySelector('[data-time-slot]')) return false

      if (isCurrentTimeVisible) {
        const now = new Date()
        const currentHourSlot = `${String(now.getHours()).padStart(2, '0')}:00`
        const slotElement = container.querySelector(`[data-time-slot="${currentHourSlot}"]`) as HTMLElement | null
        if (slotElement) {
          const nowPosition = slotElement.offsetTop + (now.getMinutes() / 60) * HOUR_HEIGHT
          container.scrollTop = Math.max(0, nowPosition - container.clientHeight / 2)
          return true
        }
      }

      /*
       * 8 AM at the top — read from the DOM, not computed as `8 * HOUR_HEIGHT`.
       *
       * ⚠️ That arithmetic silently assumed the hour grid began at the scroll container's top. Since AC-30
       * moved the day header INSIDE this scroller (it has to be, or it would not scroll sideways with its
       * columns), the grid now starts one header below — so the old expression landed ~8 AM *minus the
       * header* and the morning was cut off. Asking the 08:00 row where it actually is cannot drift again,
       * whatever else is stacked above it. `offsetTop` is measured from the `relative` wrapper, which is the
       * scroller's content origin, so it is already in `scrollTop` coordinates.
       */
      const morning = container.querySelector('[data-time-slot="08:00"]') as HTMLElement | null
      container.scrollTop = morning ? morning.offsetTop : 8 * HOUR_HEIGHT
      return true
    }

    if (!positionScroll()) {
      const timer = setTimeout(positionScroll, 150)
      return () => clearTimeout(timer)
    }
    // `isNarrow` is a dependency because the week time grid is not rendered at all below `md:` — crossing the
    // breakpoint mounts it for the first time, and without a re-run it would sit at midnight instead of 8 AM.
  }, [view, selectedDate, loading, isCurrentTimeVisible, isNarrow])

  // All appointments starting on the given day (already status-filtered). Each is rendered exactly
  // once by the overlay below (AC-4), replacing the old per-hour-slot duplication.
  const getAppointmentsForDay = (date: Date): AppointmentDto[] => {
    const dayStr = format(date, "yyyy-MM-dd")
    return appointments.filter((apt) => format(new Date(apt.appointmentDateTime), "yyyy-MM-dd") === dayStr)
  }

  // Horizontal band (as CSS calc expressions, without the wrapping `calc()`) for a day's appointment
  // column. Day view uses the full width right of the 60px time gutter; week view uses one of 7 equal
  // day columns. `laneStyle` splits this band into side-by-side lanes when appointments overlap.
  const dayBandLeftExpr = "64px"
  const dayBandWidthExpr = "100% - 70px"
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
  type AppointmentLane = { appointment: AppointmentDto; colIndex: number; colCount: number }
  const computeOverlapLanes = (dayAppointments: AppointmentDto[]): AppointmentLane[] => {
    const items = dayAppointments
      .map((a) => {
        const start = new Date(a.appointmentDateTime)
        const startMin = start.getHours() * 60 + start.getMinutes()
        const endMin = startMin + Math.max(parseDurationToMinutes(a.duration), 1)
        return { appointment: a, startMin, endMin }
      })
      .sort((x, y) => x.startMin - y.startMin || x.endMin - y.endMin)

    const lanes: AppointmentLane[] = []
    let cluster: { appointment: AppointmentDto; startMin: number; endMin: number }[] = []
    let colEnds: number[] = []
    const colOf = new Map<AppointmentDto, number>()
    let clusterMaxEnd = -1

    const flush = () => {
      const colCount = colEnds.length
      for (const it of cluster) {
        lanes.push({ appointment: it.appointment, colIndex: colOf.get(it.appointment) ?? 0, colCount })
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
  const renderAppointmentBlock = (appointment: AppointmentDto, positionStyle: CSSProperties) => {
    const aptStart = new Date(appointment.appointmentDateTime)
    const durationMinutes = parseDurationToMinutes(appointment.duration)
    const startMinutesOfDay = aptStart.getHours() * 60 + aptStart.getMinutes()
    const top = (startMinutesOfDay / 60) * HOUR_HEIGHT
    const height = Math.max((durationMinutes / 60) * HOUR_HEIGHT, MIN_APPT_HEIGHT)
    const isVerySmall = height < 30
    const isSmall = height < 48
    const actsSummary = appointmentActsSummary(appointment)
    const actsCount = appointmentActsCount(appointment)
    const colorStyle = getStatusColor(appointment)

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
        title={`${appointment.patientName} · ${durationMinutes}m`}
      >
        <div className="flex items-center gap-2 min-w-0">
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

  const formatTimeDisplay = (time: string): string => {
    return time
  }

  const getStatusColor = (appointment: AppointmentDto) => {
    // Busy slots (no patient) - use distinct styling
    if (!appointment.patientId || appointment.patientName === "Occupé") {
      return {
        className: "bg-amber-100 text-amber-800 border-l-4 border-amber-500 dark:bg-amber-500/20 dark:text-amber-400 font-semibold",
        style: {}
      }
    }
    
    // If appointment has a procedure color, use it
    if (appointment.procedureColorHex) {
      try {
        // Create a lighter version of the procedure color for background
        const hex = appointment.procedureColorHex.replace('#', '')
        if (hex.length === 6) {
          const r = parseInt(hex.substring(0, 2), 16)
          const g = parseInt(hex.substring(2, 4), 16)
          const b = parseInt(hex.substring(4, 6), 16)
          const bgColor = `rgba(${r}, ${g}, ${b}, 0.15)`
          const textColor = appointment.procedureColorHex
          const borderColor = appointment.procedureColorHex
          
          return {
            className: "border-l-4 shadow-sm",
            style: {
              backgroundColor: bgColor,
              color: textColor,
              borderLeftColor: borderColor,
            }
          }
        }
      } catch (e) {
        // If color parsing fails, fall through to status-based colors
        console.warn('Failed to parse procedure color:', appointment.procedureColorHex, e)
      }
    }
    
    // Otherwise, use status-based colors
    const statusLower = appointment.status.toLowerCase()
    if (statusLower === 'scheduled' || statusLower === 'confirmed') {
      return {
        className: "bg-accent text-primary border-l-4 border-primary/20",
        style: {}
      }
    }
    if (statusLower === 'completed') {
      return {
        className: "bg-green-100 text-green-700 border-l-4 border-green-500 dark:bg-green-500/20 dark:text-green-400",
        style: {}
      }
    }
    if (statusLower === 'cancelled') {
      return {
        className: "bg-gray-100 text-gray-500 border-l-4 border-gray-400 opacity-60 dark:bg-gray-800 dark:text-gray-400",
        style: {}
      }
    }
    return {
      className: "bg-accent text-primary border-l-4 border-primary/20",
      style: {}
    }
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

  // A compact month-cell chip: start time + patient name, colored by the shared status/procedure rules
  // (AC-2). Clicking opens the edit dialog (AC-4); stopPropagation keeps the cell's day-navigation from
  // firing too.
  const renderMonthChip = (appointment: AppointmentDto) => {
    const colorStyle = getStatusColor(appointment)
    const start = format(new Date(appointment.appointmentDateTime), "HH:mm")

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
        title={`${start} · ${appointment.patientName}`}
      >
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
   * The scrolling week grid built for AC-30 is honest from `md:` up: seven 96px columns, a sticky gutter and
   * a sticky header. On a 320–390px phone the same grid is a 732px canvas you read through a 320px window,
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
  const renderWeekStrip = () => (
    <ul className="divide-y overflow-y-auto" aria-label="Semaine — choisissez un jour">
      {weekDays.map((day) => {
        const dayAppointments = getAppointmentsForDay(day).sort(
          (a, b) => new Date(a.appointmentDateTime).getTime() - new Date(b.appointmentDateTime).getTime(),
        )
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
                          style={{ backgroundColor: appointment.procedureColorHex || "#6C757D" }}
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
            const dayAppointments = getAppointmentsForDay(day).sort(
              (a, b) => new Date(a.appointmentDateTime).getTime() - new Date(b.appointmentDateTime).getTime(),
            )
            const inMonth = isSameMonth(day, selectedDate)
            const visible = dayAppointments.slice(0, MONTH_CELL_MAX_CHIPS)
            const overflow = dayAppointments.length - visible.length

            return (
              <div
                key={day.toISOString()}
                onClick={() => onSelectDay?.(day)}
                className={cn(
                  "flex min-w-0 cursor-pointer flex-col gap-0.5 overflow-hidden border-b border-r p-1 transition-colors last:border-r-0 hover:bg-accent/30 dark:hover:bg-muted/50",
                  !inMonth && "bg-gray-50/40 dark:bg-muted/20",
                )}
              >
                <div className="flex flex-shrink-0 justify-end">
                  <span
                    className={cn(
                      "inline-flex h-6 w-6 items-center justify-center rounded-full text-xs font-semibold",
                      isToday(day)
                        ? "bg-primary text-white shadow-md"
                        : inMonth
                          ? "text-foreground"
                          : "text-muted-foreground/60",
                    )}
                  >
                    {format(day, "d")}
                  </span>
                </div>
                {/*
                  Below `md:` a month cell is about 45 px wide — a chip there is a coloured sliver with one or
                  two characters of a patient's name, which is worse than no name at all because it reads as
                  data. Dots say the one thing the month view is actually for at that size: *which days are
                  busy*. The cell stays tappable and drops into Jour, where the names are legible.

                  Capped at MONTH_CELL_MAX_CHIPS dots + a count, mirroring the chip branch, so a heavy day does
                  not become an unreadable smear — the same reasoning as `plan-act-pips`' twelve-pip fallback.
                */}
                <div className="flex flex-wrap items-center gap-0.5 px-0.5 md:hidden" aria-hidden="true">
                  {visible.map((appointment) => (
                    <span
                      key={appointment.id}
                      className="h-1.5 w-1.5 rounded-full"
                      style={{ backgroundColor: appointment.procedureColorHex || "#6C757D" }}
                    />
                  ))}
                  {overflow > 0 && <span className="text-2xs text-muted-foreground">+{overflow}</span>}
                </div>
                {/* The dots are `aria-hidden`; this carries the accessible fact for the whole cell. */}
                {dayAppointments.length > 0 && (
                  <span className="sr-only md:hidden">
                    {dayAppointments.length} rendez-vous
                  </span>
                )}

                <div className="hidden min-h-0 flex-col gap-0.5 overflow-hidden md:flex">
                  {visible.map((appointment) => renderMonthChip(appointment))}
                  {overflow > 0 && (
                    <span className="px-1 text-2xs font-medium text-muted-foreground hover:text-foreground">
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

  return (
    <div className="flex h-full flex-col">
      <div className="mb-3 flex items-center justify-between flex-wrap gap-3">
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
          {/*
            The legend is REFERENCE, not a control, and four items of it is two rows on a phone — so below
            `md:` it folds into a disclosure rather than being deleted. Hiding it outright would have been
            simpler and wrong: the grey « hors horaires » shading has no other explanation anywhere, which is
            the exact defect its legend entry was added to fix.
          */}
          <details className="w-full md:hidden">
            <summary className="cursor-pointer touch-target text-sm text-muted-foreground">Légende</summary>
            <div className="mt-2 flex flex-wrap items-center gap-x-4 gap-y-1 text-sm">
              <div className="flex items-center gap-2">
                <div className="h-3 w-3 rounded bg-primary" />
                <span className="text-muted-foreground">Planifié</span>
              </div>
              <div className="flex items-center gap-2">
                <div className="h-3 w-3 rounded bg-green-500" />
                <span className="text-muted-foreground">Terminé</span>
              </div>
              <div className="flex items-center gap-2">
                <div className="h-3 w-3 rounded bg-gray-400" />
                <span className="text-muted-foreground">Annulé</span>
              </div>
              {clinicHours && clinicHours.length > 0 && (
                <div className="flex items-center gap-2">
                  <div className="h-3 w-3 rounded border bg-gray-100 dark:bg-muted/50" />
                  <span className="text-muted-foreground">Hors horaires d&apos;ouverture</span>
                </div>
              )}
            </div>
          </details>

          {/* Status Legend */}
          <div className="hidden items-center gap-4 text-sm md:flex">
            <div className="flex items-center gap-2">
              <div className="h-3 w-3 rounded bg-primary" />
              <span className="text-muted-foreground">Planifié</span>
            </div>
            <div className="flex items-center gap-2">
              <div className="h-3 w-3 rounded bg-green-500" />
              <span className="text-muted-foreground">Terminé</span>
            </div>
            <div className="flex items-center gap-2">
              <div className="h-3 w-3 rounded bg-gray-400" />
              <span className="text-muted-foreground">Annulé</span>
            </div>
            {/* The grey shading had no legend entry at all, so a closed period looked like a rendering
                artefact. Only shown when hours are actually configured (AC-P1.33). */}
            {clinicHours && clinicHours.length > 0 && (
              <div className="flex items-center gap-2">
                <div className="h-3 w-3 rounded border bg-gray-100 dark:bg-muted/50" />
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
          className="mb-3 flex flex-wrap items-center justify-between gap-3 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-200"
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

      <Card className="flex-1 overflow-hidden shadow-sm min-h-0">
        {view === "month" ? (
          renderMonthView()
        ) : view === "week" && isNarrow ? (
          /* ⚠️ A real branch, not `md:hidden` on the grid. A hidden (`display:none`) scroll container reports
             `offsetTop: 0` for every row, so the 8 AM positioning and the current-time line would both compute
             against a zero-height layout and be wrong the moment the viewport crossed back to `md:`. Not
             rendering it means there is nothing to mis-measure, and the scroll effect re-runs on `isNarrow`. */
          renderWeekStrip()
        ) : (
        <div className="flex h-full flex-col min-h-0">
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
            className={cn(
              "relative min-h-0 flex-1 overflow-auto transition-opacity duration-200 ease-snap",
              refetching && "opacity-60",
            )}
          >
          <div className={cn("relative", view === "week" ? "w-max min-w-full md:w-full" : "w-full")}>
          <div
            className={cn(
              // z-50: above the sticky gutter (z-30) and the current-time overlay (z-40), both of which
              // scroll under it.
              "sticky top-0 z-50 grid border-b bg-white dark:bg-background",
              view === "week" ? WEEK_COLS : "grid-cols-[60px_1fr]",
            )}
          >
            {/* The corner cell is sticky on BOTH axes, so it stays over the time gutter as it scrolls. */}
            <div className="sticky left-0 z-10 border-r bg-gray-50 dark:bg-muted" />
            {view === "week" ? (
              weekDays.map((day) => (
                <div key={day.toISOString()} className="border-r py-2 text-center last:border-r-0 min-w-0">
                  <div className="mb-1 text-xs font-medium uppercase tracking-wider text-muted-foreground">
                    {format(day, "EEE", { locale: fr })}
                  </div>
                  <div
                    className={cn(
                      "mx-auto inline-flex h-8 w-8 items-center justify-center rounded-full text-xs font-semibold transition-colors",
                      isToday(day) ? "bg-primary text-white shadow-md" : "text-foreground hover:bg-gray-100 dark:hover:bg-muted",
                    )}
                  >
                    {format(day, "d")}
                  </div>
                </div>
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

          {loading ? (
            // Skeleton rows at the real HOUR_HEIGHT, so the grid does not resize when the appointments land.
            <div role="status" aria-label="Chargement des rendez-vous">
              {Array.from({ length: 12 }).map((_, i) => (
                <div
                  key={i}
                  className={cn("grid border-b", view === "week" ? WEEK_COLS : "grid-cols-[60px_1fr]")}
                  style={{ height: HOUR_HEIGHT }}
                >
                  <div className="flex items-center justify-end border-r bg-gray-50 px-2 dark:bg-muted">
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
                      left: '60px',
                      right: '0',
                      top: `${currentTimePosition}px`,
                      height: '2px',
                      marginTop: '-1px',
                    }}
                  >
                    <div className="h-full bg-red-300 dark:bg-red-400/70 shadow-sm" />
                  </div>
                  {/* Current time dot on time column */}
                  <div
                    className="absolute z-40 pointer-events-none"
                    style={{
                      left: '46px',
                      top: `${currentTimePosition - 4}px`,
                    }}
                  >
                    <div className="w-2 h-2 rounded-full bg-red-400 dark:bg-red-500 shadow-sm" />
                  </div>
                </>
              )}
              {/* Hour grid: time labels + empty clickable cells (gridlines + click-to-create). */}
              <div className={cn("grid", view === "week" ? WEEK_COLS : "grid-cols-[60px_1fr]")}>
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
                          against the fixed `HOUR_HEIGHT`, so any row taller than it makes appointments drift
                          upward (e.g. a 17:00 block landing near 14:00). Keep rows exactly HOUR_HEIGHT tall —
                          which is why the day cells below set `minHeight` from the constant itself rather than
                          repeating the number in a Tailwind class, as they used to. */}
                      <div
                        className={cn(
                          // `sticky left-0` keeps the hour visible while the week scrolls sideways (AC-30) —
                          // a scrolled column with no time beside it is unreadable. z-30 puts it above the
                          // appointment blocks (z-20) and below the current-time dot (z-40), which is drawn
                          // at left:46px, i.e. inside this very column.
                          "sticky left-0 z-30 border-b border-r bg-gray-50 dark:bg-muted px-2 py-2 text-right leading-none",
                          !isWorkingHours && "bg-gray-100/50 dark:bg-muted/50",
                        )}
                      >
                        <span className={cn("text-xs font-medium", isWorkingHours ? "text-gray-700 dark:text-foreground" : "text-gray-400 dark:text-muted-foreground")}>
                          {time}
                        </span>
                      </div>

                      {/* Day columns (empty — appointments render in the overlay below) */}
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
                                  !dayOpen && "bg-gray-50/50 dark:bg-muted/30 opacity-70",
                                )}
                                style={{ minHeight: HOUR_HEIGHT }}
                                onClick={() => onTimeSlotClick?.(day, time)}
                                data-time-slot={time}
                              />
                            )
                          })
                        : (
                            <div
                              className={cn(
                                "cursor-pointer border-b border-r transition-colors hover:bg-accent/30 dark:hover:bg-muted/50",
                                !isWorkingHours && "bg-gray-50/50 dark:bg-muted/30 opacity-70",
                              )}
                              style={{ minHeight: HOUR_HEIGHT }}
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
                    computeOverlapLanes(getAppointmentsForDay(day)).map(({ appointment, colIndex, colCount }) =>
                      renderAppointmentBlock(
                        appointment,
                        laneStyle(weekBandLeftExpr(dayIndex), weekBandWidthExpr, colIndex, colCount),
                      ),
                    ),
                  )
                : computeOverlapLanes(getAppointmentsForDay(selectedDate)).map(({ appointment, colIndex, colCount }) =>
                    renderAppointmentBlock(
                      appointment,
                      laneStyle(dayBandLeftExpr, dayBandWidthExpr, colIndex, colCount),
                    ),
                  )}
            </>
          )}
          </div>
          </div>
        </div>
        )}
      </Card>
    </div>
  )
}
