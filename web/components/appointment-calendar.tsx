"use client"

import { Card } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { Switch } from "@/components/ui/switch"
import { Label } from "@/components/ui/label"
import { ChevronLeft, ChevronRight, Calendar, Filter, CloudOff, UploadCloud } from "lucide-react"
import { format, addDays, startOfWeek, addWeeks, subWeeks, subDays, startOfDay, endOfDay, isToday, startOfMonth, addMonths, subMonths, isSameMonth } from "date-fns"
import { useMemo, useRef, useEffect, useState, type CSSProperties } from "react"
import { toast } from "sonner"
import { useAppointments } from "@/lib/hooks/use-appointments"
import { googleCalendarApi } from "@/lib/api/google-calendar"
import { ApiError } from "@/lib/api/client"
import { useConnectivity } from "@/lib/connectivity/connectivity"
import type { AppointmentDto } from "@/lib/api/types"
import { cn, parseDurationToMinutes } from "@/lib/utils"

// Generate all 24 hours with hourly intervals
const generateHourlyTimeSlots = (): string[] => {
  const slots: string[] = []
  for (let hour = 0; hour < 24; hour++) {
    slots.push(`${String(hour).padStart(2, '0')}:00`)
  }
  return slots
}
const timeSlots = generateHourlyTimeSlots()

// Fixed pixel height of one hour row; appointment blocks are positioned/sized against it.
const HOUR_HEIGHT = 35
// Minimum rendered height so a very short appointment still shows the patient name legibly (AC-4).
const MIN_APPT_HEIGHT = 20
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
}

export function AppointmentCalendar({ view, selectedDate, onDateChange, onTimeSlotClick, onAppointmentClick, onSelectDay, showCancelled = false, showCompleted = false, onShowCancelledChange, onShowCompletedChange, onChanged, doctorId }: AppointmentCalendarProps) {
  const { internetReachable } = useConnectivity()
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
          className="border-0 bg-amber-100 text-amber-800 dark:bg-amber-500/20 dark:text-amber-400 h-4 gap-0.5 px-1 text-[9px] leading-none"
        >
          <CloudOff className="h-2.5 w-2.5" />
          non synchronisé
        </Badge>
        <button
          type="button"
          onClick={() => handlePushToGoogle(appointment)}
          disabled={!internetReachable || pushingId === appointment.id}
          title={internetReachable ? "Envoyer vers Google Agenda" : "Connexion internet requise"}
          className="inline-flex h-4 items-center gap-0.5 rounded bg-white/60 px-1 text-[9px] leading-none hover:bg-white disabled:cursor-not-allowed disabled:opacity-50 dark:bg-background/60"
        >
          <UploadCloud className="h-2.5 w-2.5" />
          {pushingId === appointment.id ? "..." : "Push"}
        </button>
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

  const { appointments: allAppointments, loading } = useAppointments(startDate, endDate, undefined, undefined, doctorId)

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
        const container = scrollContainerRef.current
        const slotTop = timeSlotElement.offsetTop
        const slotHeight = 35 // height per hour slot in pixels
        const minuteOffset = (minutes / 60) * slotHeight
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

      container.scrollTop = 8 * HOUR_HEIGHT // 8 AM at the top
      return true
    }

    if (!positionScroll()) {
      const timer = setTimeout(positionScroll, 150)
      return () => clearTimeout(timer)
    }
  }, [view, selectedDate, loading, isCurrentTimeVisible])

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
    const colorStyle = getStatusColor(appointment)

    return (
      <div
        key={appointment.id}
        className={cn(
          "absolute z-20 rounded transition-shadow hover:shadow-md overflow-hidden flex flex-col cursor-pointer pointer-events-auto",
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
              isVerySmall ? "text-[10px] leading-[1.1]" : isSmall ? "text-xs leading-[1.2]" : "text-sm leading-[1.3]",
            )}
          >
            {appointment.patientName}
          </span>
          {!isVerySmall && (
            <div className="flex items-center gap-1.5 flex-shrink-0">
              <Badge
                variant="secondary"
                className="border-0 bg-white/50 dark:bg-background/50 text-[10px] h-4 leading-none px-1.5"
              >
                {durationMinutes}m
              </Badge>
              {view === "day" && appointment.procedureTypeName && (
                <Badge
                  variant="secondary"
                  className="border-0 bg-white/50 dark:bg-background/50 text-[10px] h-4 leading-none px-1.5"
                >
                  {appointment.procedureTypeName}
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
        className: "bg-blue-100 text-blue-700 border-l-4 border-blue-500 dark:bg-blue-500/20 dark:text-blue-400",
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
      className: "bg-blue-100 text-blue-700 border-l-4 border-blue-500 dark:bg-blue-500/20 dark:text-blue-400",
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
          "flex w-full items-center gap-1 overflow-hidden rounded px-1 py-0.5 text-left text-[10px] leading-tight transition-shadow hover:shadow-sm",
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
  const renderMonthView = () => (
    <div className="flex h-full flex-col min-h-0">
      <div className="grid grid-cols-7 border-b bg-white dark:bg-background flex-shrink-0">
        {monthGridDays.slice(0, 7).map((day) => (
          <div
            key={`dow-${day.toISOString()}`}
            className="border-r py-2 text-center text-xs font-medium uppercase tracking-wider text-muted-foreground last:border-r-0"
          >
            {format(day, "EEE")}
          </div>
        ))}
      </div>

      {loading ? (
        <div className="flex flex-1 items-center justify-center">
          <p className="text-muted-foreground">Loading appointments...</p>
        </div>
      ) : (
        <div className="grid flex-1 grid-cols-7 grid-rows-6 min-h-0">
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
                  "flex min-w-0 cursor-pointer flex-col gap-0.5 overflow-hidden border-b border-r p-1 transition-colors last:border-r-0 hover:bg-blue-50/30 dark:hover:bg-muted/50",
                  !inMonth && "bg-gray-50/40 dark:bg-muted/20",
                )}
              >
                <div className="flex flex-shrink-0 justify-end">
                  <span
                    className={cn(
                      "inline-flex h-6 w-6 items-center justify-center rounded-full text-xs font-semibold",
                      isToday(day)
                        ? "bg-blue-600 text-white shadow-md"
                        : inMonth
                          ? "text-foreground"
                          : "text-muted-foreground/60",
                    )}
                  >
                    {format(day, "d")}
                  </span>
                </div>
                <div className="flex min-h-0 flex-col gap-0.5 overflow-hidden">
                  {visible.map((appointment) => renderMonthChip(appointment))}
                  {overflow > 0 && (
                    <span className="px-1 text-[10px] font-medium text-muted-foreground hover:text-foreground">
                      +{overflow} more
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
            <Button variant="outline" size="icon" className="h-9 w-9 bg-transparent" onClick={handlePrevious}>
              <ChevronLeft className="h-4 w-4" />
            </Button>
            <Button variant="outline" size="icon" className="h-9 w-9 bg-transparent" onClick={handleNext}>
              <ChevronRight className="h-4 w-4" />
            </Button>
          </div>
          <Button variant="outline" size="sm" onClick={handleToday} className="gap-2 bg-transparent">
            <Calendar className="h-4 w-4" />
            Today
          </Button>
          <div className="ml-2 text-xl font-semibold">
            {view === "week"
              ? `${format(weekDays[0], "MMM d")} - ${format(weekDays[6], "MMM d, yyyy")}`
              : view === "month"
                ? format(selectedDate, "MMMM yyyy")
                : format(selectedDate, "EEEE, MMMM d, yyyy")}
          </div>
        </div>

        <div className="flex items-center gap-4 flex-wrap">
          {/* Status Legend */}
          <div className="flex items-center gap-4 text-sm">
            <div className="flex items-center gap-2">
              <div className="h-3 w-3 rounded bg-blue-500" />
              <span className="text-muted-foreground">Scheduled</span>
            </div>
            <div className="flex items-center gap-2">
              <div className="h-3 w-3 rounded bg-green-500" />
              <span className="text-muted-foreground">Completed</span>
            </div>
            <div className="flex items-center gap-2">
              <div className="h-3 w-3 rounded bg-gray-400" />
              <span className="text-muted-foreground">Cancelled</span>
            </div>
          </div>

          {/* Filters */}
          <div className="flex items-center gap-4 pl-4 border-l">
            <div className="flex items-center gap-2">
              <Filter className="h-4 w-4 text-muted-foreground" />
              <span className="text-sm font-medium text-muted-foreground">Show:</span>
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
                  Completed
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
                  Cancelled
                </Label>
              </div>
            </div>
          </div>
        </div>
      </div>

      <Card className="flex-1 overflow-hidden shadow-sm min-h-0">
        {view === "month" ? (
          renderMonthView()
        ) : (
        <div className="flex h-full flex-col min-h-0">
          <div
            className={cn(
              "sticky top-0 z-10 grid border-b bg-white dark:bg-background flex-shrink-0",
              view === "week" ? "grid-cols-[60px_repeat(7,minmax(0,1fr))]" : "grid-cols-[60px_1fr]",
            )}
          >
            <div className="border-r bg-gray-50 dark:bg-muted" />
            {view === "week" ? (
              weekDays.map((day) => (
                <div key={day.toISOString()} className="border-r py-2 text-center last:border-r-0 min-w-0">
                  <div className="mb-1 text-xs font-medium uppercase tracking-wider text-muted-foreground">
                    {format(day, "EEE")}
                  </div>
                  <div
                    className={cn(
                      "mx-auto inline-flex h-8 w-8 items-center justify-center rounded-full text-xs font-semibold transition-colors",
                      isToday(day) ? "bg-blue-600 text-white shadow-md" : "text-foreground hover:bg-gray-100 dark:hover:bg-muted",
                    )}
                  >
                    {format(day, "d")}
                  </div>
                </div>
              ))
            ) : (
              <div className="py-2 text-center">
                <div className="mb-1 text-xs font-medium uppercase tracking-wider text-muted-foreground">
                  {format(selectedDate, "EEEE")}
                </div>
                <div
                  className={cn(
                    "mx-auto inline-flex h-8 w-8 items-center justify-center rounded-full text-xs font-semibold",
                    isToday(selectedDate) ? "bg-blue-600 text-white shadow-md" : "text-foreground",
                  )}
                >
                  {format(selectedDate, "d")}
                </div>
              </div>
            )}
          </div>

          {loading ? (
            <div className="flex h-96 items-center justify-center flex-shrink-0">
              <p className="text-muted-foreground">Loading appointments...</p>
            </div>
          ) : (
            <div ref={scrollContainerRef} className="flex-1 overflow-y-auto overflow-x-hidden min-h-0 relative">
              {/* Current time indicator line - overlay */}
              {isCurrentTimeVisible && currentTimePosition !== null && (
                <>
                  <div
                    className="absolute z-30 pointer-events-none"
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
                    className="absolute z-30 pointer-events-none"
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
              <div className={cn("grid min-w-full", view === "week" ? "grid-cols-[60px_repeat(7,minmax(0,1fr))]" : "grid-cols-[60px_1fr]")}>
                {timeSlots.map((time) => {
                  const hour = Number.parseInt(time.split(":")[0])
                  const isWorkingHours = hour >= 8 && hour < 18

                  return (
                    <div key={time} className="contents">
                      {/* Time label. `leading-none` is load-bearing: without it this div inherits
                          the global line-height (1.5), inflating each grid row to ~41px. The
                          appointment overlay positions blocks with the fixed HOUR_HEIGHT (35px), so
                          any row taller than 35px makes appointments drift upward (e.g. a 17:00 block
                          lands near 14:00). Keep rows exactly HOUR_HEIGHT tall. */}
                      <div
                        className={cn(
                          "border-b border-r bg-gray-50 dark:bg-muted px-2 py-2 text-right leading-none",
                          !isWorkingHours && "bg-gray-100/50 dark:bg-muted/50",
                        )}
                      >
                        <span className={cn("text-xs font-medium", isWorkingHours ? "text-gray-700 dark:text-foreground" : "text-gray-400 dark:text-muted-foreground")}>
                          {time}
                        </span>
                      </div>

                      {/* Day columns (empty — appointments render in the overlay below) */}
                      {view === "week"
                        ? weekDays.map((day) => (
                            <div
                              key={`${day.toISOString()}-${time}`}
                              className={cn(
                                "min-h-[35px] cursor-pointer border-b border-r transition-colors hover:bg-blue-50/30 dark:hover:bg-muted/50 last:border-r-0 min-w-0",
                                !isWorkingHours && "bg-gray-50/50 dark:bg-muted/30 opacity-70",
                              )}
                              onClick={() => onTimeSlotClick?.(day, time)}
                              data-time-slot={time}
                            />
                          ))
                        : (
                            <div
                              className={cn(
                                "min-h-[35px] cursor-pointer border-b border-r transition-colors hover:bg-blue-50/30 dark:hover:bg-muted/50",
                                !isWorkingHours && "bg-gray-50/50 dark:bg-muted/30 opacity-70",
                              )}
                              onClick={() => onTimeSlotClick?.(selectedDate, time)}
                              data-time-slot={time}
                            />
                          )}
                    </div>
                  )
                })}
              </div>

              {/* Appointment overlay (AC-4): each appointment rendered exactly once, positioned by its
                  start minute and sized proportionally to its duration. Absolute children of the scroll
                  container so they scroll with the grid; the empty cells above keep gridlines + clicks. */}
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
            </div>
          )}
        </div>
        )}
      </Card>
    </div>
  )
}
