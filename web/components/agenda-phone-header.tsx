"use client"

import { useMemo, useState } from "react"
import { addDays, addMonths, isSameDay, isSameMonth, startOfMonth, startOfWeek } from "date-fns"
import { fr } from "date-fns/locale"
import { format } from "date-fns"
import { ChevronDown, ChevronRight, ChevronLeft, Filter } from "lucide-react"

import { cn } from "@/lib/utils"
import { Switch } from "@/components/ui/switch"
import { Label } from "@/components/ui/label"
import { ActiveFilterChip } from "@/components/ui/list-toolbar"
import type { AppointmentDto } from "@/lib/api/types"

type CalendarView = "day" | "week" | "month"

interface AgendaPhoneHeaderProps {
  view: CalendarView
  onViewChange: (view: CalendarView) => void
  selectedDate: Date
  onDateChange: (date: Date) => void
  /** The appointments already loaded by the calendar for the visible window — never re-fetched here. */
  appointments: AppointmentDto[]
  /**
   * The two status filters. They exist on the desktop toolbar and in the page's `hidden md:flex` chip row, and
   * **nowhere below `md:`** — which matters because two of the fifteen entries in `lib/dashboard-links.ts` land
   * here with `?status=…` and flip them on. A phone user arriving from « Taux d'absence » saw a filtered agenda
   * with nothing saying why and no way back to the normal one.
   */
  showCancelled?: boolean
  showCompleted?: boolean
  onShowCancelledChange?: (show: boolean) => void
  onShowCompletedChange?: (show: boolean) => void
}

const VIEWS: { value: CalendarView; label: string }[] = [
  { value: "day", label: "Jour" },
  { value: "week", label: "Semaine" },
  { value: "month", label: "Mois" },
]

/** Density dots are capped so a heavy day does not overflow a 55 px-wide column. */
const MAX_DOTS = 3

/**
 * The agenda's header **below `md:`** — the phone surface, mounted by `appointment-calendar.tsx` (which owns the
 * appointment data this needs). Design: `features/agenda-phone-ux/design.md`.
 *
 * Three navigation affordances at three different scales, which is why they coexist rather than duplicate:
 * a 7-day strip (this week), a collapsible mini-month (this month), and the calendar's own Jour-only swipe
 * (adjacent day).
 *
 * ⚠️ View switching is a **segmented control in this header, not a bottom bar** (design D11): `bottom-nav.tsx`
 * is already the app's global bottom navigation, rendered from `app-shell.tsx`, and a second bottom bar on one
 * screen would regress Phase 02.
 *
 * ⚠️ **In Mois this header is title + ‹ › + the view switch, and nothing else.** The mini-month and the 7-day
 * strip are both hidden there, because the screen below them *is* a month of days — three month/week pickers
 * stacked above one month grid was most of what made Mois feel crowded, and the two extra ones each said less
 * than the grid did. What they leave behind is the ability to go *back*: the phone's Mois scrolls forward into
 * the following months, so the ‹ › pair is the only way to reach a past month, which is why it replaces the
 * disclosure chevron rather than sitting beside it.
 *
 * ⚠️ There was also a « Prochains RDV » card here — the day's first three appointments, listed above a grid that
 * was already showing them. It is gone: a summary of what is visibly on screen is not a summary, and on a 390px
 * phone it cost ~90px of the day grid to repeat it.
 */
export function AgendaPhoneHeader({
  view,
  onViewChange,
  selectedDate,
  onDateChange,
  appointments,
  showCancelled = false,
  showCompleted = false,
  onShowCancelledChange,
  onShowCompletedChange,
}: AgendaPhoneHeaderProps) {
  const [monthOpen, setMonthOpen] = useState(false)
  const [filtersOpen, setFiltersOpen] = useState(false)

  // How many appointments each visible day holds — drives the density dots in both the strip and the mini-month.
  const countsByDay = useMemo(() => {
    const counts = new Map<string, number>()
    for (const appt of appointments) {
      const key = format(new Date(appt.appointmentDateTime), "yyyy-MM-dd")
      counts.set(key, (counts.get(key) ?? 0) + 1)
    }
    return counts
  }, [appointments])

  const countFor = (date: Date) => countsByDay.get(format(date, "yyyy-MM-dd")) ?? 0

  // Monday-based, matching the rest of the agenda (`weekStartsOn: 1`).
  const weekDays = useMemo(() => {
    const start = startOfWeek(selectedDate, { weekStartsOn: 1 })
    return Array.from({ length: 7 }, (_, i) => addDays(start, i))
  }, [selectedDate])

  // Six rows of seven, so the grid height never changes between months.
  const monthDays = useMemo(() => {
    const start = startOfWeek(startOfMonth(selectedDate), { weekStartsOn: 1 })
    return Array.from({ length: 42 }, (_, i) => addDays(start, i))
  }, [selectedDate])

  const today = new Date()
  const isMonthView = view === "month"
  // The disclosure can only be open in a view that offers it, so switching to Mois cannot leave a mini-month
  // stranded under the month grid — derived rather than reset in an effect.
  const monthPickerOpen = monthOpen && !isMonthView
  // In Mois the pill's job is to return to *this month*, not to this day: it must not linger all month because
  // the user is reading a date other than today.
  const showTodayPill = isMonthView ? !isSameMonth(selectedDate, today) : !isSameDay(selectedDate, today)

  return (
    // `pb-2` on the container, not on each branch: with « Prochains RDV » gone the last child differs per view
    // (the strip in Jour/Semaine, the mini-month when open, the view switch in Mois), and three separate bottom
    // paddings is three chances for one of them to sit flush against the border.
    <div className="md:hidden border-b bg-card pb-2">
      {/* Title + Aujourd'hui. The title names the MONTH in Semaine/Mois and the DAY in Jour, because when you
          are reading one day's grid the day is what you need to confirm. */}
      <div className="flex items-center gap-1 px-3 pt-2">
        {isMonthView ? (
          <>
            <span className="px-1.5 text-base font-semibold capitalize">
              {format(selectedDate, "MMMM yyyy", { locale: fr })}
            </span>
            {/* `touch-target`: 44 px tall already, but only 36 px wide — and in Mois these two are the *only*
                way to reach a past month (the grid below scrolls forward), so a mis-tap on the one control that
                goes back is the worst one to leave under-sized. The overlay squares them off without widening
                the painted button, which would push the title. */}
            <button
              type="button"
              aria-label="Mois précédent"
              onClick={() => onDateChange(addMonths(selectedDate, -1))}
              className="touch-target grid h-11 w-9 place-items-center rounded-lg text-muted-foreground"
            >
              <ChevronLeft className="h-4 w-4" />
            </button>
            <button
              type="button"
              aria-label="Mois suivant"
              onClick={() => onDateChange(addMonths(selectedDate, 1))}
              className="touch-target grid h-11 w-9 place-items-center rounded-lg text-muted-foreground"
            >
              <ChevronRight className="h-4 w-4" />
            </button>
          </>
        ) : (
          <button
            type="button"
            onClick={() => setMonthOpen((open) => !open)}
            aria-expanded={monthPickerOpen}
            className="flex min-h-11 items-center gap-1.5 rounded-lg px-1.5 text-base font-semibold"
          >
            {view === "day"
              ? format(selectedDate, "EEE d MMMM", { locale: fr })
              : format(selectedDate, "MMMM yyyy", { locale: fr })}
            <ChevronDown className={cn("h-3.5 w-3.5 text-muted-foreground transition-transform", monthPickerOpen && "rotate-180")} />
          </button>
        )}
        <div className="flex-1" />
        {showTodayPill && (
          <button
            type="button"
            onClick={() => onDateChange(today)}
            /* `touch-target` rather than `h-11`: the utility raises the *tappable* area to 44 px on a coarse
               pointer without repainting a single pixel, which is the whole reason `globals.css` declares it —
               a 44 px-tall lozenge next to a 32 px title would be a density regression to fix an ergonomics
               one. Painted 32 px, hit 44 px. */
            className="touch-target h-8 rounded-full border border-primary px-3 text-xs font-semibold text-primary"
          >
            Aujourd&apos;hui
          </button>
        )}
      </div>

      {/*
        View switch — a segmented control, NOT a bottom bar (D11).

        ⚠️ `role="group"` + `aria-pressed`, deliberately downgraded from `role="tablist"`/`role="tab"`. The tab
        pattern is a **contract**: each tab needs `aria-controls` pointing at a real `role="tabpanel"`, and the
        group needs arrow-key roving tabindex with only the selected tab in the tab order. None of that was here
        — there is no tabpanel element to point at (the calendar below is a plain grid), and all three buttons
        were plain tab stops. An announced-but-unimplemented tab pattern is worse than no pattern: it tells a
        screen-reader user to press arrow keys, and nothing happens. Three toggle buttons that say which one is
        pressed is what this control actually is.

        44 px painted here rather than a `touch-target` overlay: this is the agenda's primary control, it has a
        full row to itself, and three adjacent 36 px targets is exactly where a mis-tap costs a view switch.
      */}
      <div
        role="group"
        aria-label="Vue de l'agenda"
        className="mx-3 mt-2 grid grid-cols-3 gap-1 rounded-lg border border-border bg-muted p-1"
      >
        {VIEWS.map((v) => (
          <button
            key={v.value}
            type="button"
            aria-pressed={view === v.value}
            onClick={() => onViewChange(v.value)}
            className={cn(
              // Matched to the desktop bar's switch: a bordered track so the control reads as one at rest, and
              // a primary ring on the pressed pill so the state survives a bright screen at arm's length. The
              // desktop twin additionally had to be *un-inverted* (it painted `bg-background`, the page ground,
              // over a white card); this one was already on `bg-card` and only needed the ring and the border.
              "min-h-11 rounded-md text-xs font-semibold transition-colors",
              view === v.value
                ? "bg-card text-primary shadow-sm ring-1 ring-primary/30 dark:bg-input/70"
                : "text-muted-foreground",
            )}
          >
            {v.label}
          </button>
        ))}
      </div>

      {/*
        Status filters — « Filtres » plus a chip per active filter.

        The chips are the load-bearing half: a filter the user did not choose has to be visible and removable
        (AC-29), and the desktop already says so in `app/appointments/page.tsx`'s `hidden md:flex` chip row. The
        disclosure is what makes them reachable in the first place — a phone had no surface for these switches at
        all. Collapsed it costs one ~28 px row (with a 44 px hit area via `touch-target`), which is why it is a
        disclosure rather than two permanently-mounted switches on the smallest screen in the app.
      */}
      {(onShowCancelledChange || onShowCompletedChange) && (
        <div className="mt-1.5 px-3">
          <div className="flex flex-wrap items-center gap-2">
            <button
              type="button"
              onClick={() => setFiltersOpen((open) => !open)}
              aria-expanded={filtersOpen}
              className="touch-target inline-flex items-center gap-1.5 rounded-md text-xs font-medium text-muted-foreground"
            >
              <Filter className="h-3.5 w-3.5" aria-hidden="true" />
              Filtres
              <ChevronDown
                className={cn("h-3 w-3 transition-transform", filtersOpen && "rotate-180")}
                aria-hidden="true"
              />
            </button>
            {showCancelled && (
              <ActiveFilterChip label="Annulés affichés" onRemove={() => onShowCancelledChange?.(false)} />
            )}
            {showCompleted && (
              <ActiveFilterChip label="Terminés affichés" onRemove={() => onShowCompletedChange?.(false)} />
            )}
          </div>

          {filtersOpen && (
            <div className="mt-1 flex flex-col gap-2 pb-1">
              <div className="flex items-center gap-2">
                <Switch
                  id="phone-show-completed"
                  checked={showCompleted}
                  onCheckedChange={(checked) => onShowCompletedChange?.(checked)}
                />
                <Label htmlFor="phone-show-completed" className="cursor-pointer text-sm">
                  Terminés affichés
                </Label>
              </div>
              <div className="flex items-center gap-2">
                <Switch
                  id="phone-show-cancelled"
                  checked={showCancelled}
                  onCheckedChange={(checked) => onShowCancelledChange?.(checked)}
                />
                <Label htmlFor="phone-show-cancelled" className="cursor-pointer text-sm">
                  Annulés affichés
                </Label>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Collapsible mini-month. Density dots, never chips — a 390/7 ≈ 55 px column cannot hold a chip. */}
      {monthPickerOpen && (
        <div className="mt-2 border-t px-3 pb-3 pt-2">
          <div className="flex items-center gap-1">
            <span className="flex-1 text-sm font-medium capitalize">
              {format(selectedDate, "MMMM yyyy", { locale: fr })}
            </span>
            <button
              type="button"
              aria-label="Mois précédent"
              onClick={() => onDateChange(addMonths(selectedDate, -1))}
              className="grid h-11 w-11 place-items-center rounded-full text-muted-foreground"
            >
              <ChevronLeft className="h-4 w-4" />
            </button>
            <button
              type="button"
              aria-label="Mois suivant"
              onClick={() => onDateChange(addMonths(selectedDate, 1))}
              className="grid h-11 w-11 place-items-center rounded-full text-muted-foreground"
            >
              <ChevronRight className="h-4 w-4" />
            </button>
          </div>
          <div className="mt-1 grid grid-cols-7 gap-px">
            {["L", "M", "M", "J", "V", "S", "D"].map((d, i) => (
              <div key={`${d}-${i}`} className="pb-1 text-center text-2xs text-muted-foreground">
                {d}
              </div>
            ))}
            {monthDays.map((day) => {
              const selected = isSameDay(day, selectedDate)
              const outside = !isSameMonth(day, selectedDate)
              return (
                <button
                  key={day.toISOString()}
                  type="button"
                  onClick={() => {
                    onDateChange(day)
                    setMonthOpen(false)
                  }}
                  aria-current={selected ? "date" : undefined}
                  className={cn(
                    "flex aspect-square flex-col items-center justify-center gap-0.5 rounded-lg text-xs tabular-nums",
                    outside && "text-muted-foreground/40",
                    selected && "bg-primary font-semibold text-primary-foreground",
                  )}
                >
                  {format(day, "d")}
                  {countFor(day) > 0 && (
                    <span className={cn("h-[3px] w-[3px] rounded-full", selected ? "bg-primary-foreground" : "bg-primary")} />
                  )}
                </button>
              )
            })}
          </div>
        </div>
      )}

      {/* 7-day strip — tap a day to go to it. Hidden while the mini-month is open (it would say the same thing),
          and in Mois, where the grid below already shows this week among all the others. */}
      {!monthPickerOpen && !isMonthView && (
        <div className="mt-2 grid grid-cols-7 gap-0.5 px-2">
          {weekDays.map((day) => {
            const selected = isSameDay(day, selectedDate)
            const count = countFor(day)
            return (
              <button
                key={day.toISOString()}
                type="button"
                onClick={() => onDateChange(day)}
                aria-current={selected ? "date" : undefined}
                aria-label={`${format(day, "EEEE d MMMM", { locale: fr })}${count > 0 ? ` — ${count} rendez-vous` : ""}`}
                className="flex min-h-[52px] flex-col items-center gap-0.5 rounded-lg pb-1.5 pt-1"
              >
                <span className="text-2xs uppercase tracking-wide text-muted-foreground">
                  {format(day, "EEE", { locale: fr })}
                </span>
                <span
                  className={cn(
                    "grid h-[30px] w-[30px] place-items-center rounded-full text-base font-semibold tabular-nums",
                    selected && "bg-primary text-primary-foreground",
                  )}
                >
                  {format(day, "d")}
                </span>
                <span className="flex h-1 gap-0.5" aria-hidden="true">
                  {Array.from({ length: Math.min(count, MAX_DOTS) }).map((_, i) => (
                    <span key={i} className="h-1 w-1 rounded-full bg-primary opacity-75" />
                  ))}
                </span>
              </button>
            )
          })}
        </div>
      )}
    </div>
  )
}
