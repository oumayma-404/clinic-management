"use client"

import { Card } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { Switch } from "@/components/ui/switch"
import { Label } from "@/components/ui/label"
import { ChevronLeft, ChevronRight, Calendar, Filter } from "lucide-react"
import { format, addDays, startOfWeek, addWeeks, subWeeks, subDays, startOfDay, endOfDay, setHours, setMinutes, isToday, isSameDay } from "date-fns"
import { useMemo, useRef, useEffect, useState } from "react"
import { useAppointments } from "@/lib/hooks/use-appointments"
import type { AppointmentDto } from "@/lib/api/types"
import { cn } from "@/lib/utils"

// Generate all 24 hours with hourly intervals
const generateHourlyTimeSlots = (): string[] => {
  const slots: string[] = []
  for (let hour = 0; hour < 24; hour++) {
    slots.push(`${String(hour).padStart(2, '0')}:00`)
  }
  return slots
}
const timeSlots = generateHourlyTimeSlots()

// Helper function to parse TimeSpan string to minutes
function parseDurationToMinutes(duration: string): number {
  const parts = duration.split(':')
  if (parts.length === 3) {
    const hours = parseInt(parts[0], 10)
    const minutes = parseInt(parts[1], 10)
    return hours * 60 + minutes
  }
  return 60 // Default to 1 hour
}

interface AppointmentCalendarProps {
  view: "day" | "week"
  selectedDate: Date
  onDateChange: (date: Date) => void
  onTimeSlotClick?: (date: Date, time: string) => void
  onAppointmentClick?: (appointment: AppointmentDto) => void
  showCancelled?: boolean
  showCompleted?: boolean
  onShowCancelledChange?: (show: boolean) => void
  onShowCompletedChange?: (show: boolean) => void
}

export function AppointmentCalendar({ view, selectedDate, onDateChange, onTimeSlotClick, onAppointmentClick, showCancelled = false, showCompleted = false, onShowCancelledChange, onShowCompletedChange }: AppointmentCalendarProps) {
  // Memoized date range for API calls
  const startDate = useMemo(() => {
    return view === "day"
      ? startOfDay(selectedDate)
      : startOfDay(startOfWeek(selectedDate, { weekStartsOn: 1 }))
  }, [view, selectedDate])

  const endDate = useMemo(() => {
    return view === "day"
      ? endOfDay(selectedDate)
      : endOfDay(addDays(startOfWeek(selectedDate, { weekStartsOn: 1 }), 6)) // 7 days (0-6)
  }, [view, selectedDate])

  const { appointments: allAppointments, loading } = useAppointments(startDate, endDate)

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

  // Calculate current time position based on actual DOM elements
  useEffect(() => {
    if (!loading && scrollContainerRef.current) {
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

  // Auto-scroll to show 8 AM as the first visible slot on initial load
  useEffect(() => {
    if (!loading) {
      const scrollTo8AM = () => {
        if (scrollContainerRef.current) {
          const container = scrollContainerRef.current
          const hourHeight = 35 // height per hour slot in pixels
          const eightAMPosition = 8 * hourHeight // Position of 8 AM slot
          
          // Check if content is rendered by checking if we have time slots
          if (container.querySelector('[data-time-slot]')) {
            container.scrollTop = eightAMPosition
            return true
          }
          return false
        }
        return false
      }
      
      // Try immediately
      if (!scrollTo8AM()) {
        // If not ready, try after a short delay
        const timer = setTimeout(() => {
          scrollTo8AM()
        }, 150)
        
        return () => clearTimeout(timer)
      }
    }
  }, [view, selectedDate, loading])

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

  // Helper to calculate appointment style (top, height) for proportional display
  const getAppointmentStyle = (appointment: AppointmentDto, slotTime: string) => {
    const aptStart = new Date(appointment.appointmentDateTime)
    const aptDurationMinutes = parseDurationToMinutes(appointment.duration)
    const [slotHour] = slotTime.split(':').map(Number)

    const startMinutesInHour = aptStart.getMinutes()
    const topPercentage = (startMinutesInHour / 60) * 100
    const heightPercentage = (aptDurationMinutes / 60) * 100

    return {
      top: `${topPercentage}%`,
      height: `${heightPercentage}%`,
    }
  }

  // Helper function to get appointments that overlap with an hourly time slot
  const getAppointmentsForHourSlot = (date: Date, hourSlot: string): AppointmentDto[] => {
    const [slotHour] = hourSlot.split(':').map(Number)
    const slotStart = setMinutes(setHours(date, slotHour), 0)
    const slotEnd = setMinutes(setHours(date, slotHour), 59)

    return appointments.filter((apt) => {
      const aptStart = new Date(apt.appointmentDateTime)
      const aptDuration = parseDurationToMinutes(apt.duration)
      const aptEnd = new Date(aptStart.getTime() + aptDuration * 60000)

      const aptDateOnly = format(aptStart, "yyyy-MM-dd")
      const slotDateOnly = format(slotStart, "yyyy-MM-dd")

      return aptDateOnly === slotDateOnly &&
             ((aptStart < slotEnd && aptEnd > slotStart) || // Overlaps
              (format(aptStart, "HH:mm") === hourSlot && aptEnd > slotStart)) // Starts exactly at the hour
    })
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
    } else {
      onDateChange(subWeeks(selectedDate, 1))
    }
  }

  const handleNext = () => {
    if (view === "day") {
      onDateChange(addDays(selectedDate, 1))
    } else {
      onDateChange(addWeeks(selectedDate, 1))
    }
  }

  const handleToday = () => {
    onDateChange(new Date())
  }

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
              <div className={cn("grid min-w-full", view === "week" ? "grid-cols-[60px_repeat(7,minmax(0,1fr))]" : "grid-cols-[60px_1fr]")}>
                {timeSlots.map((time) => {
                  const hour = Number.parseInt(time.split(":")[0])
                  const isWorkingHours = hour >= 8 && hour < 18

                  return (
                    <div key={time} className="contents">
                      {/* Time label */}
                      <div
                        className={cn(
                          "border-b border-r bg-gray-50 dark:bg-muted px-2 py-2 text-right",
                          !isWorkingHours && "bg-gray-100/50 dark:bg-muted/50",
                        )}
                      >
                        <span className={cn("text-xs font-medium", isWorkingHours ? "text-gray-700 dark:text-foreground" : "text-gray-400 dark:text-muted-foreground")}>
                          {time}
                        </span>
                      </div>

                      {/* Day columns */}
                      {view === "week"
                        ? weekDays.map((day) => {
                            const slotAppointments = getAppointmentsForHourSlot(day, time)

                            return (
                              <div
                                key={`${day.toISOString()}-${time}`}
                                className={cn(
                                  "min-h-[35px] cursor-pointer border-b border-r p-0.5 transition-colors hover:bg-blue-50/30 dark:hover:bg-muted/50 last:border-r-0 min-w-0 relative",
                                  !isWorkingHours && "bg-gray-50/50 dark:bg-muted/30 opacity-70",
                                )}
                                onClick={() => !slotAppointments.length && onTimeSlotClick && onTimeSlotClick(day, time)}
                                data-time-slot={time}
                              >
                                {slotAppointments.length > 0 ? (
                                  slotAppointments.map((appointment) => {
                                    const style = getAppointmentStyle(appointment, time)
                                    const durationMinutes = parseDurationToMinutes(appointment.duration)
                                    const heightPercent = parseFloat(style.height?.replace('%', '') || '100')
                                    const isVerySmall = heightPercent < 40
                                    const isSmall = heightPercent < 60
                                    const colorStyle = getStatusColor(appointment)
                                    return (
                                      <div
                                        key={appointment.id}
                                        className={cn(
                                          "absolute left-0.5 right-0.5 rounded transition-shadow hover:shadow-md overflow-hidden flex flex-col cursor-pointer",
                                          colorStyle.className,
                                          isVerySmall ? "p-0.5" : isSmall ? "p-1" : "p-1.5",
                                        )}
                                        style={{ ...style, ...colorStyle.style }}
                                        onClick={(e) => {
                                          e.stopPropagation()
                                          onAppointmentClick?.(appointment)
                                        }}
                                      >
                                        <div className={cn(
                                          "truncate font-semibold flex-shrink-0",
                                          isVerySmall ? "text-[9px] leading-[1.1]" : isSmall ? "text-[10px] leading-[1.2]" : "text-xs leading-[1.3]"
                                        )}>
                                          {appointment.patientName}
                                        </div>
                                        {appointment.notes && !isVerySmall && !isSmall && (
                                          <div className="mt-0.5 truncate text-[10px] opacity-75 leading-tight flex-shrink-0">{appointment.notes}</div>
                                        )}
                                        {!isVerySmall && !isSmall && (
                                          <Badge
                                            variant="secondary"
                                            className="mt-0.5 h-3 border-0 bg-white/50 dark:bg-background/50 px-1 text-[9px] font-medium leading-none flex-shrink-0"
                                          >
                                            {durationMinutes}m
                                          </Badge>
                                        )}
                                      </div>
                                    )
                                  })
                                ) : null}
                              </div>
                            )
                          })
                        : (() => {
                            const slotAppointments = getAppointmentsForHourSlot(selectedDate, time)
                            return (
                              <div
                                className={cn(
                                  "min-h-[35px] cursor-pointer border-b border-r p-1 transition-colors hover:bg-blue-50/30 dark:hover:bg-muted/50 relative",
                                  !isWorkingHours && "bg-gray-50/50 dark:bg-muted/30 opacity-70",
                                )}
                                onClick={() => !slotAppointments.length && onTimeSlotClick && onTimeSlotClick(selectedDate, time)}
                                data-time-slot={time}
                              >
                                {slotAppointments.length > 0 ? (
                                  slotAppointments.map((appointment) => {
                                    const style = getAppointmentStyle(appointment, time)
                                    const durationMinutes = parseDurationToMinutes(appointment.duration)
                                    const heightPercent = parseFloat(style.height?.replace('%', '') || '100')
                                    const isVerySmall = heightPercent < 40
                                    const isSmall = heightPercent < 60
                                    const colorStyle = getStatusColor(appointment)
                                    return (
                                      <div
                                        key={appointment.id}
                                        className={cn(
                                          "absolute left-1 right-1 rounded transition-shadow hover:shadow-md overflow-hidden flex flex-col cursor-pointer",
                                          colorStyle.className,
                                          isVerySmall ? "p-0.5" : isSmall ? "p-1" : "p-2",
                                        )}
                                        style={{ ...style, ...colorStyle.style }}
                                        onClick={(e) => {
                                          e.stopPropagation()
                                          onAppointmentClick?.(appointment)
                                        }}
                                      >
                                        {/* Patient name with labels on the right (day view only) */}
                                        <div className={cn(
                                          "flex items-center gap-2 flex-shrink-0",
                                          isVerySmall ? "text-[10px] leading-[1.1]" : isSmall ? "text-xs leading-[1.2]" : "text-sm leading-[1.3]"
                                        )}>
                                          <span className={cn(
                                            "font-semibold truncate flex-1 min-w-0",
                                          )}>
                                            {appointment.patientName}
                                          </span>
                                          {view === "day" && !isVerySmall && (
                                            <div className="flex items-center gap-1.5 flex-shrink-0">
                                              <Badge variant="secondary" className="border-0 bg-white/50 dark:bg-background/50 text-[10px] h-4 leading-none px-1.5">
                                                {durationMinutes}m
                                              </Badge>
                                              {appointment.procedureTypeName && (
                                                <Badge variant="secondary" className="border-0 bg-white/50 dark:bg-background/50 text-[10px] h-4 leading-none px-1.5">
                                                  {appointment.procedureTypeName}
                                                </Badge>
                                              )}
                                            </div>
                                          )}
                                        </div>
                                        {appointment.notes && !isVerySmall && !isSmall && (
                                          <div className="mt-0.5 truncate text-xs opacity-75 leading-tight flex-shrink-0">{appointment.notes}</div>
                                        )}
                                      </div>
                                    )
                                  })
                                ) : null}
                              </div>
                            )
                          })()}
                    </div>
                  )
                })}
              </div>
            </div>
          )}
        </div>
      </Card>
    </div>
  )
}
