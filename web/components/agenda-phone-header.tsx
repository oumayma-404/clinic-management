"use client"

import { useMemo, useState } from "react"
import { addDays, addMonths, isSameDay, isSameMonth, startOfMonth, startOfWeek } from "date-fns"
import { fr } from "date-fns/locale"
import { format } from "date-fns"
import { ChevronDown, ChevronRight, ChevronLeft } from "lucide-react"

import { cn } from "@/lib/utils"
import type { AppointmentDto } from "@/lib/api/types"

type CalendarView = "day" | "week" | "month"

interface AgendaPhoneHeaderProps {
  view: CalendarView
  onViewChange: (view: CalendarView) => void
  selectedDate: Date
  onDateChange: (date: Date) => void
  /**
   * One period back / forward, in whatever unit the current view counts in — a day, a week, a month.
   *
   * ⚠️ Passed in rather than computed here, and that is the point: the calendar already owns
   * `handlePrevious`/`handleNext`, the swipe gesture calls the same two, and the desktop bar's ‹ › are the same
   * two again. A fourth copy of « what does « suivant » mean in this view » is a fourth chance for one of them
   * to disagree — which is exactly how Semaine ended up with no way forward at all while Mois had one.
   */
  onPrevious: () => void
  onNext: () => void
  /** The appointments already loaded by the calendar for the visible window — never re-fetched here. */
  appointments: AppointmentDto[]
  /**
   * Predicate: is the cabinet open that day, per the hours in force? Supplied by the calendar, which already
   * resolves practitioner-then-clinic hours for the grid's shading.
   *
   * ⚠️ The strip counted appointments only, so a closed Saturday read « samedi 29 août » with nothing else —
   * the same rendering as an open day with no bookings, while the desktop grid one breakpoint up said
   * « cabinet fermé ». The phone is the surface where the desk actually stands.
   */
  isDayOpen?: (day: Date) => boolean
  /**
   * Rendered at the bottom of the disclosure panel, under the view switch and the mini-month — the legend and
   * the « Afficher les 24 heures » escape hatch, both owned by the calendar (they read `gridWindow`).
   *
   * ⚠️ They are passed in rather than rebuilt here for the usual reason, and one specific one: the 24-hour
   * toggle is the *only* route to an out-of-hours slot, so a second copy of it that drifted from `gridWindow`
   * would silently offer hours the grid does not draw.
   */
  panelExtra?: React.ReactNode
  /** Google's « Nothing planned » line: one quiet row under the strip, shown only when the range is empty. */
  note?: React.ReactNode
  /**
   * Told to the calendar so its floating « + » can stand down while the panel is open.
   *
   * ⚠️ The button is `absolute` against the calendar's whole box — header included — so an open panel puts it
   * squarely over the legend's last row. It is the panel's own state that has to travel, not the button: moving
   * the « + » out of the corner would give up the placement the owner asked for to fix a transient overlap.
   */
  onPanelOpenChange?: (open: boolean) => void
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
 * ⚠️ **‹ › are on this row in EVERY view now, and their absence was the defect.** They used to render in Mois
 * alone, on the argument that Jour has a swipe and Semaine's day strip is its own navigation — but a strip only
 * ever moves *within* the week it is showing, so Semaine on a phone had no way to reach next week at all, and
 * Jour's only way to tomorrow was a gesture nothing on screen advertised. A button that says which direction it
 * goes is what a calendar owes its reader; the swipe (still there, and now in all three views) is the shortcut on
 * top of it, never the only route.
 *
 * ⚠️ **There are no status filters here any anywhere else.** « Terminés » and « Annulés » both defaulted to
 * *shown*, so the disclosure and its two permanent chips were 72 px of a 390 px screen stating that nothing was
 * filtered. An agenda shows the practice's day — all of it — and the two toggles are gone from the phone, the
 * desktop popover and the URL alike.
 *
 * ⚠️ **The 7-day strip renders in Jour only.** In Semaine the grid below now draws its own sticky day header —
 * seven dates over the columns they belong to — so a second row of seven dates above it said the same thing
 * twice and cost the grid 58 px. In Mois the screen below *is* a month of days.
 *
 * ⚠️ View switching is a **segmented control inside the disclosure panel, not a permanent row and not a bottom
 * bar** (design D11): `bottom-nav.tsx` is already the app's global bottom navigation, rendered from
 * `app-shell.tsx`, and a second bottom bar on one screen would regress Phase 02. Why it left the header's own
 * row — and what the two-tap switch bought — is on the panel itself, below.
 *
 * ⚠️ **« Nouveau » is not on this row.** It is the floating `+` the calendar paints over the bottom-end corner of
 * the grid — Google Agenda's own placement, and the user's — which is what freed this row for the ‹ › pair.
 */
export function AgendaPhoneHeader({
  view,
  onViewChange,
  selectedDate,
  onDateChange,
  onPrevious,
  onNext,
  appointments,
  isDayOpen,
  panelExtra,
  note,
  onPanelOpenChange,
}: AgendaPhoneHeaderProps) {
  const [monthOpen, setMonthOpen] = useState(false)
  /**
   * ⚠️ The parent is told **outside** the updater, not inside it. A `setState` updater is expected to be pure and
   * React may run it during a render, so calling `onPanelOpenChange` from within one updated `AppointmentCalendar`
   * while `AgendaPhoneHeader` was rendering — « Cannot update a component while rendering a different component »
   * in the console, and a warning is the *lenient* outcome. Both callers are click handlers, so reading `monthOpen`
   * directly is safe and the updater form buys nothing.
   */
  const setPanel = (open: boolean) => {
    setMonthOpen(open)
    onPanelOpenChange?.(open)
  }
  const togglePanel = () => setPanel(!monthOpen)
  const closePanel = () => setPanel(false)

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
  const isDayView = view === "day"
  /**
   * The disclosure is open in **every** view now, and that is the whole shape of this header.
   *
   * ⚠️ It used to be gated on `!isMonthView`, because all it held was a mini-month and the screen below Mois is
   * one. It now holds the *view switch* as well, so gating it there would leave Mois with no way out of Mois.
   * The mini-month is what stays month-view-only (see `showMiniMonth`), not the panel.
   */
  const panelOpen = monthOpen
  const showMiniMonth = !isMonthView
  // In Mois the pill's job is to return to *this month*, not to this day: it must not linger all month because
  // the user is reading a date other than today.
  const showTodayPill = isMonthView ? !isSameMonth(selectedDate, today) : !isSameDay(selectedDate, today)
  /** What ‹ › move by, in words — the label is the one thing that tells a screen reader which unit this is. */
  const stepLabel = isDayView ? "Jour" : isMonthView ? "Mois" : "Semaine"

  return (
    // The bottom padding is on the container, not on each branch: the last child differs per view (the strip in
    // Jour, the mini-month when open, the view switch in Semaine/Mois), and three separate bottom paddings is
    // three chances for one of them to sit flush against the border.
    /* ⚠️ **Full-bleed (`-mx-4`), because the grid below it is.** `AppShell` gutters `<main>` with `p-4`, so the
       agenda used to be a 358 px-wide slab inset in a 390 px screen — the « ça ne va pas jusqu'au bord » the
       owner reported from an iPhone 13. Header and card bleed back through that gutter *together*: a header that
       stopped 16 px short of a full-bleed grid would draw the day strip out of line with its own columns. */
    <div className="-mx-4 md:hidden border-b bg-card pb-1.5">
      {/* Title + ‹ › + Aujourd'hui. The title names the MONTH in Semaine/Mois and the DAY in Jour, because when
          you are reading one day's grid the day is what you need to confirm. */}
      <div className="flex items-center gap-0.5 px-2 pt-1.5">
        <button
            type="button"
            onClick={togglePanel}
            aria-expanded={panelOpen}
            /* `min-w-0` + `truncate`: with ‹ › and « Aujourd'hui » to its right, the date is the one item on
               this row that may give up width — at 320 px the four together are wider than the row, and a title
               that pushes instead of shortening would drive the arrows off screen.

               ⚠️ Painted **36 px** with a `touch-target` overlay, not `min-h-11`: it was the tallest thing in
               this header and so it set the row's height, and this header's height is the day grid's. The
               overlay is safe *here* — its 4 px overhang meets the arrows' own 44 px areas, and a title
               overhanging a « previous » arrow costs a re-tap, never a wrong action (§ 2). */
            className="touch-target flex min-h-9 min-w-0 flex-1 items-center gap-1.5 rounded-lg px-1 text-base font-semibold"
          >
            <span className="truncate">
              {isDayView
                ? format(selectedDate, "EEE d MMMM", { locale: fr })
                : format(selectedDate, "MMMM yyyy", { locale: fr })}
            </span>
            <ChevronDown className={cn("h-3.5 w-3.5 shrink-0 text-muted-foreground transition-transform", panelOpen && "rotate-180")} />
          </button>
        {showTodayPill && (
          <button
            type="button"
            onClick={() => onDateChange(today)}
            /* `touch-target` rather than `h-11`: the utility raises the *tappable* area to 44 px on a coarse
               pointer without repainting a single pixel, which is the whole reason `globals.css` declares it —
               a 44 px-tall lozenge next to a 32 px title would be a density regression to fix an ergonomics
               one. Painted 32 px, hit 44 px. */
            className="touch-target h-8 shrink-0 rounded-full border border-primary px-2.5 text-xs font-semibold text-primary"
          >
            Aujourd&apos;hui
          </button>
        )}
        {/* ⚠️ The two arrows **grow their own 44 px box** (`h-11 w-10`) rather than carrying `.touch-target`:
            they are adjacent, and two overlapping overlays hand every shared pixel to whichever paints last —
            which would make « précédent » sometimes mean « suivant » (§ 2). Ten pixels wide is enough for a
            16 px glyph and keeps both inside a 320 px row beside a truncating title. */}
        <button
          type="button"
          aria-label={`${stepLabel} précédent`}
          onClick={onPrevious}
          className="grid h-11 w-10 shrink-0 place-items-center rounded-lg text-muted-foreground active:bg-accent/50"
        >
          <ChevronLeft className="h-5 w-5" />
        </button>
        <button
          type="button"
          aria-label={`${stepLabel} suivant`}
          onClick={onNext}
          className="grid h-11 w-10 shrink-0 place-items-center rounded-lg text-muted-foreground active:bg-accent/50"
        >
          <ChevronRight className="h-5 w-5" />
        </button>
      </div>

      {/*
        View switch — a segmented control, NOT a bottom bar (D11).

        ⚠️ **It stays a PERMANENT row, on the owner's instruction, and it was briefly folded into the disclosure
        panel below.** The argument for folding was arithmetic and it was real — 52 px of a 390 px screen, on a
        page whose chrome measured 278 px against Google Agenda's ~132 — and Google does put its own
        Day/Week/Month behind a menu. It is still the wrong trade *here*: Jour ⇄ Semaine is the most-used control
        on this screen and a desk switches between them dozens of times a day, so two taps each is a worse tax
        than the row it saves. The other three savings stand (the legend and the 24-hour hatch went into the
        panel, the footer strip is desktop-only, the card is full-bleed), which is what pays for keeping it.

        ⚠️ `role="group"` + `aria-pressed`, deliberately downgraded from `role="tablist"`/`role="tab"`. The tab
        pattern is a **contract**: each tab needs `aria-controls` pointing at a real `role="tabpanel"`, and the
        group needs arrow-key roving tabindex with only the selected tab in the tab order. None of that was here
        — there is no tabpanel element to point at (the calendar below is a plain grid), and all three buttons
        were plain tab stops. An announced-but-unimplemented tab pattern is worse than no pattern: it tells a
        screen-reader user to press arrow keys, and nothing happens. Three toggle buttons that say which one is
        pressed is what this control actually is.

        The tap area is **grown, never overlaid**: this is the agenda's primary control and its three buttons are
        adjacent, so a `.touch-target` on each would overhang its neighbours and hand the shared pixels to
        whichever paints last — a mis-tap that costs a view switch. Hence `coarse:min-h-11` on a finger, and 36 px
        painted where there is a mouse, since the row is the day grid's space either way.
      */}
      <div
        role="group"
        aria-label="Vue de l'agenda"
        /* Full width, and thinner than it was — the frame gave up `p-1` → `p-0.5` and the buttons are painted
           36 px on a mouse. ⚠️ **`coarse:min-h-11` is a floor, not a preference**: three adjacent targets cannot
           use a `.touch-target` overlay (each would overhang its neighbour, and the later sibling wins the tap),
           so on a finger the painted height *is* the tap height and 44 px is where § 2 stops it. */
        className="mx-3 mt-1.5 grid grid-cols-3 gap-0.5 rounded-lg border border-border bg-muted p-0.5"
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
              "min-h-9 rounded-md text-xs font-semibold transition-colors coarse:min-h-11",
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
        ══ The disclosure panel — the mini-month, plus what the calendar handed over ══

        ⚠️ It opens in **every** view, including Mois, and that is what changed when the legend and the 24-hour
        hatch moved in. It used to be a mini-month alone, so gating it on `!isMonthView` cost nothing; now Mois
        would be the one view with no route to either. The *mini-month* is what stays month-view-only
        (`showMiniMonth`) — the screen below it is already a month of days.
      */}
      {panelOpen && (
        <div className="mt-2 border-t px-3 pb-3 pt-2">

      {/* Mini-month. Density dots, never chips — a 390/7 ≈ 55 px column cannot hold a chip. */}
      {showMiniMonth && (
        <div>
          <div className="flex items-center gap-1">
            <span className="flex-1 text-sm font-medium capitalize">
              {format(selectedDate, "MMMM yyyy", { locale: fr })}
            </span>
            {/* These page the PICKER by a month whatever the view is, which is why they do not reuse
                `onPrevious`/`onNext`: in Jour those move by a day, and a month grid that advances one square is
                not a month picker. */}
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
                    closePanel()
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

          {/* The legend and « Afficher les 24 heures », handed over by the calendar — see `panelExtra`. The
              separator is conditional: in Mois there is no mini-month above it, so an unconditional `border-t`
              would sit one padding step under the panel's own and read as a double rule. */}
          {panelExtra && <div className={cn(showMiniMonth && "mt-3 border-t pt-3")}>{panelExtra}</div>}
        </div>
      )}

      {/* 7-day strip — tap a day to go to it. Jour only: Semaine's grid draws its own dates over its own
          columns, and Mois is a month of days. Hidden while the panel is open (its mini-month says the same
          thing). */}
      {!panelOpen && isDayView && (
        /* At 320 px the seven cells measured ~37 px wide — under the § 2 floor, on the control this header
            exists for. The height floor above was reasoned about; the width never was.

            ⚠️ The strip no longer carries its own `-mx-4`: the **header** bleeds now, so a second bleed here
            would push it 16 px past the screen on each side. It gets the full 390 px either way — seven cells
            of ~55 px, comfortably past the § 2 floor — and the padding only shrinks below 360 px. */
        <div className="mt-1.5 grid grid-cols-7 gap-0 px-1 min-[360px]:gap-0.5 min-[360px]:px-2">
          {weekDays.map((day) => {
            const selected = isSameDay(day, selectedDate)
            const count = countFor(day)
            const open = isDayOpen ? isDayOpen(day) : true
            return (
              <button
                key={day.toISOString()}
                type="button"
                onClick={() => onDateChange(day)}
                aria-current={selected ? "date" : undefined}
                aria-label={`${format(day, "EEEE d MMMM", { locale: fr })}${
                  open ? (count > 0 ? ` — ${count} rendez-vous` : "") : " — cabinet fermé"
                }`}
                /* 58 px, not the 64 it painted: this row is the second-tallest thing in the header and
                   every pixel of it belongs to the day grid. The tap target stays 14 px past the § 2 floor. */
                className="flex min-h-[52px] flex-col items-center rounded-lg pb-1 pt-0.5"
              >
                <span className="text-2xs uppercase tracking-wide text-muted-foreground">
                  {format(day, "EEE", { locale: fr })}
                </span>
                <span
                  className={cn(
                    "grid h-[28px] w-[28px] place-items-center rounded-full text-base font-semibold tabular-nums",
                    selected && "bg-primary text-primary-foreground",
                  )}
                >
                  {format(day, "d")}
                </span>
                {/* Visible, not only in the `aria-label`: « fermé » under the date is what the desk reads at a
                    glance, and it replaces the density dots because a closed day has nothing to count. */}
                {!open && (
                  <span className="mt-0.5 text-[0.5625rem] uppercase leading-none tracking-wide text-muted-foreground">
                    fermé
                  </span>
                )}
                {/* `mt-0.5` here rather than a `gap` on the column: the density dots need clearing from the date
                    circle (flush, they read as part of it), while the weekday label above does not. */}
                <span className={cn("mt-0.5 flex h-1 gap-0.5", !open && "hidden")} aria-hidden="true">
                  {Array.from({ length: Math.min(count, MAX_DOTS) }).map((_, i) => (
                    <span key={i} className="h-1 w-1 rounded-full bg-primary opacity-75" />
                  ))}
                </span>
              </button>
            )
          })}
        </div>
      )}

      {/* Google's « Nothing planned », in the band above the ruler rather than in a strip below it — which is
          where the sentence used to live, costing the grid a 40 px footer to say that it was empty. */}
      {!panelOpen && note && (
        <p className="px-3 pt-1 text-xs leading-snug text-muted-foreground">{note}</p>
      )}
    </div>
  )
}
