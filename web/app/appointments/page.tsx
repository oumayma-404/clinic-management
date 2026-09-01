"use client"

import { useState, useCallback, useEffect, useRef } from "react"
import { AppShell } from "@/components/app-shell"
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { AppointmentCalendar } from "@/components/appointment-calendar"
import { CreateAppointmentDialog } from "@/components/create-appointment-dialog"
import { EditAppointmentDialog } from "@/components/edit-appointment-dialog"
import { ClinicGuard } from "@/components/clinic-guard"
import type { AppointmentDto } from "@/lib/api/types"
import { isSameDay, isToday, setHours, setMinutes } from "date-fns"
import { toLocalIso } from "@/lib/format"
import { useUrlFilters } from "@/lib/hooks/use-url-filters"
import { appointmentsApi } from "@/lib/api/appointments"
import { googleCalendarApi } from "@/lib/api/google-calendar"
import { ApiError } from "@/lib/api/client"
import { toast } from "sonner"
import { useConnectivity } from "@/lib/connectivity/connectivity"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useDoctors } from "@/lib/hooks/use-doctors"
import { useSession } from "@/lib/auth/session"
import { ActiveFilterChip } from "@/components/ui/list-toolbar"
import { useMediaQuery } from "@/lib/hooks/use-media-query"
import { cn } from "@/lib/utils"

/**
 * The next 5-minute boundary strictly after <paramref name="from"/> — the agenda's own booking granularity.
 *
 * <p>Strictly after, never "round to nearest": a form opened at 13:55:00 is submitted a few seconds later, so
 * returning the current boundary would still resolve to a past instant by the time the user presses Créer.</p>
 */
function nextFiveMinuteBoundary(from: Date): Date {
  const next = new Date(from)
  const minutes = next.getMinutes()
  next.setMinutes(minutes + (5 - (minutes % 5)), 0, 0)
  return next
}

/**
 * The agenda's own URL state, read ONCE at mount.
 *
 * ⚠️ {@link useUrlFilters} writes `?date=` / `?view=` so the day on screen is addressable and survives F5 — and
 * nothing read them back, so `/appointments?date=2026-08-31&view=week` still opened on today in Jour and the
 * shared link, the reload and the « regarde le lundi 24 » message were all silently wrong. A write with no read
 * is not persistence.
 *
 * Composed as `T00:00:00` and parsed as LOCAL midnight, never bare: a bare `AAAA-MM-JJ` parses as UTC, so for a
 * UTC+1 clinic it lands on the previous day. An unparseable value simply yields null and the defaults stand,
 * matching the graceful-deep-link rule the rest of this page follows.
 */
function seededDay(): Date | null {
  if (typeof window === "undefined") return null
  const raw = new URLSearchParams(window.location.search).get("date")
  if (!raw) return null
  const day = new Date(`${raw}T00:00:00`)
  return Number.isNaN(day.getTime()) ? null : day
}

/** @see seededDay — null when the URL names no view, so the caller keeps its own default. */
function seededView(): "day" | "week" | "month" | null {
  if (typeof window === "undefined") return null
  const raw = new URLSearchParams(window.location.search).get("view")
  return raw === "day" || raw === "week" || raw === "month" ? raw : null
}

export default function AppointmentsPage() {
  // Week is the default: it is the span staff actually plan against, and a single day of a specialist practice's
  // calendar is mostly empty. Month view stays one click away, and clicking a day cell there still drops into Day
  // view (handleSelectDay). ⚠️ Below `md:` the *initial* view becomes Jour instead — see `viewDecidedRef`.
  const [view, setView] = useState<"day" | "week" | "month">(() => seededView() ?? "week")
  /**
   * Has the view already been settled by something that outranks the narrow-screen default? (AC-28/AC-29)
   *
   * Three things set it: the user picking a tab, the dashboard drill-through forcing Mois, and the
   * narrow-screen default itself the first time it applies.
   *
   * ⚠️ It exists because « Jour below `md:` » is an **initial value, not a rule**. Without it the default
   * would re-assert on every `isNarrow` change — so rotating a phone would throw away the view the user had
   * just chosen, and a « RDV honorés — Ce mois » drill-through opened on a phone would land on a single day
   * instead of the month the card counted. `features/LEARNINGS.md`: a size heuristic must not be the sole
   * gate on an affordance.
   */
  const viewDecidedRef = useRef(seededView() !== null)

  /** The deep-link params are consumed once — see the effect that reads them. */
  const deepLinkHandledRef = useRef(false)
  // `md:` is 768px — the same boundary the nav rail, the card lists and the dialogs switch at.
  const isNarrow = useMediaQuery("(max-width: 767px)")
  const selectView = useCallback((next: "day" | "week" | "month") => {
    viewDecidedRef.current = true
    setView(next)
  }, [])

  /**
   * The cross-fade that replaces the remount.
   *
   * Collapsing three `<TabsContent>` calendars into one (below) removed the only thing that visually marked a
   * view switch — the rebuild. A prop change swaps three very different geometries in a single frame, which
   * reads as a glitch rather than as a transition, so 150 ms of opacity stands in for it.
   *
   * ⚠️ The fade class has to land in the **same commit that swaps the view**, which is why this is a
   * render-phase update (legal in React, and StrictMode-safe — unlike mutating a ref during render) rather than
   * an effect. From an effect the new grid paints fully opaque for a frame and *then* dips: a flicker, i.e. the
   * exact artefact the fade exists to remove. The rAF below clears it once that opacity-0 frame has actually
   * been painted, giving `transition-opacity` two values to animate between.
   */
  const [shownView, setShownView] = useState(view)
  const [viewFading, setViewFading] = useState(false)
  if (shownView !== view) {
    setShownView(view)
    setViewFading(true)
  }
  useEffect(() => {
    if (!viewFading) return
    const frame = window.requestAnimationFrame(() => setViewFading(false))
    return () => window.cancelAnimationFrame(frame)
  }, [viewFading])
  const [dialogOpen, setDialogOpen] = useState(false)
  const [editDialogOpen, setEditDialogOpen] = useState(false)
  const [selectedAppointment, setSelectedAppointment] = useState<AppointmentDto | null>(null)
  const [selectedDate, setSelectedDate] = useState(() => seededDay() ?? new Date())
  const [refreshKey, setRefreshKey] = useState(0)
  // Patient preselected when arriving from a patient's "Planifier un rendez-vous" (?patientId=…).
  const [bookingPatientId, setBookingPatientId] = useState<string | undefined>(undefined)
  const [isGoogleCalendarAuthorized, setIsGoogleCalendarAuthorized] = useState(false)
  const [disconnectOpen, setDisconnectOpen] = useState(false)
  const [isDisconnecting, setIsDisconnecting] = useState(false)
  const [showCancelled, setShowCancelled] = useState(false)
  const [showCompleted, setShowCompleted] = useState(false)
  // Google Calendar needs the server's internet egress; gate its controls in Local offline mode
  // (AC-6.2). Cloud always reports online (R-3).
  const { internetReachable } = useConnectivity()
  // Google Calendar connect/import/push endpoints are AdminOnly — only show the controls to admins so
  // a non-admin never gets a 403 → generic "Échec" toast (finding #9).
  const { user } = useSession()
  const isAdmin = user?.role === "admin"
  // Per-practitioner filter (AC-3.2): "all" = no filter. Passed down to the calendar's fetch.
  const { doctors } = useDoctors()
  // Seeded from the URL for the same reason as the day and the statuses above it — see `useUrlFilters` below.
  const [selectedDoctorId, setSelectedDoctorId] = useState<string>(
    () => (typeof window === "undefined" ? "all" : new URLSearchParams(window.location.search).get("doctorId") ?? "all"),
  )
  const doctorFilterId = selectedDoctorId === "all" ? undefined : selectedDoctorId
  /** Is there anything for the chip row to say? See the row itself for why it must not render otherwise. */
  const hasActiveFilterChips = showCancelled || showCompleted || Boolean(doctorFilterId)

  /**
   * The length a dragged span asked for, or `undefined` for every other way into the create dialog.
   *
   * ⚠️ It has to be **cleared** on a plain click, not merely left unset: this page keeps one long-lived dialog, so
   * a duration held over from the last drag would silently apply to the next appointment booked by clicking an
   * hour — and, being « touched », would also stop that booking's acts from setting their own length.
   */
  const [selectedDurationMinutes, setSelectedDurationMinutes] = useState<number | undefined>(undefined)

  const handleTimeSlotClick = useCallback((date: Date, time: string, durationMinutes?: number) => {
    const [hours, minutes] = time.split(':').map(Number)
    const dateWithTime = setMinutes(setHours(date, hours), minutes)
    setSelectedDate(dateWithTime)
    setSelectedDurationMinutes(durationMinutes)
    setDialogOpen(true)
  }, [])

  /** « Nouveau rendez-vous » from the desktop bar or the phone header — no span, so no duration override. */
  const openCreateDialog = useCallback(() => {
    setSelectedDurationMinutes(undefined)
    // `selectedDate` holds the instant the page mounted, so while today is on screen the dialog prefilled a start
    // time drifting further into the past the longer the agenda stayed open — and every ordinary booking then
    // tripped « Heure dans le passé », which trains the desk to click through the one prompt that should mean
    // something. Refreshed to the next 5-minute boundary; a slot clicked or dragged on the grid is untouched.
    setSelectedDate((current) => (isToday(current) ? nextFiveMinuteBoundary(new Date()) : current))
    setDialogOpen(true)
  }, [])

  const handleAppointmentClick = useCallback((appointment: AppointmentDto) => {
    setSelectedAppointment(appointment)
    setEditDialogOpen(true)
  }, [])

  // Month view: clicking a day cell's empty area / "+N more" focuses that date in Day view (AC-4).
  // Below `md:` the week strip lands here too — tapping a day in the density strip opens it.
  const handleSelectDay = useCallback((date: Date) => {
    setSelectedDate(date)
    selectView("day")
  }, [selectView])

  /**
   * A booking landed. Refetch — and **stay on the day it was booked for**.
   *
   * ⚠️ The agenda used to leave the user's day and jump back to the current week: `selectedDate` is refreshed to
   * « now » by `openCreateDialog` whenever today happens to be on screen, and the day picked inside the dialog
   * never came back out. So a receptionist booking next Tuesday created the RDV and then watched the screen
   * return to this week with it nowhere in sight — the appointment existed and was invisible to the person who
   * had just made it, which reads as « it did not save ». Reproduced 3× from fresh loads.
   *
   * Only moves when the booked day differs from the one on screen, so booking into the visible day does not
   * disturb a grid the user is already reading.
   */
  const handleAppointmentCreated = useCallback((appointmentDateTime: Date) => {
    setSelectedDate((current) => (isSameDay(current, appointmentDateTime) ? current : appointmentDateTime))
    setRefreshKey(prev => prev + 1)
  }, [])

  const handleAppointmentUpdated = useCallback(() => {
    setRefreshKey(prev => prev + 1)
  }, [])

  // Real-time: when another client of this clinic creates/edits/cancels an appointment, the server
  // broadcasts entityChanged("appointments") and we bump `refreshKey`, which reaches the calendar as its
  // `reloadToken` prop and refetches the current window IN PLACE.
  //
  // It used to arrive as `key={refreshKey}`, i.e. a remount: the calendar lost its scroll position, re-read
  // the clinic's working hours and flashed empty. Because this same handler is wired to the realtime hub, a
  // colleague booking a patient at reception blanked and re-scrolled the dentist's open agenda. Additive: if
  // the hub is down, manual refresh still works (AC-5).
  useClinicRealtime(RealtimeResource.Appointments, handleAppointmentUpdated)

  // Check Google Calendar status on mount and after authorization
  const checkGoogleCalendarStatus = useCallback(async () => {
    try {
      const status = await googleCalendarApi.getStatus()
      setIsGoogleCalendarAuthorized(status.isConfigured && status.tokenValid !== false)

      // Show message if token is invalid
      if (status.hasRefreshToken && !status.tokenValid) {
        console.warn("Google Calendar token is invalid. Please re-authorize.")
      }
    } catch (error) {
      /*
       * ⚠️ A 403 here is the server being RIGHT, not a fault to log.
       *
       * `/googlecalendar/status` is admin/doctor-only, and this page loads for a secretary on every shift — so
       * reception's console filled with an unhandled `ApiError` on every visit to the agenda, for a refusal that
       * is the access model working. The presentation half simply had no branch for it. Treated as « not mine to
       * see »: the control stays hidden, which is what the absent authorisation means, and nothing is logged.
       *
       * Every other failure is still reported — a real outage on this probe is worth knowing about.
       */
      if (error instanceof ApiError && (error.status === 403 || error.status === 404)) {
        setIsGoogleCalendarAuthorized(false)
        return
      }
      console.error("Failed to check Google Calendar status:", error)
    }
  }, [])

  useEffect(() => {
    checkGoogleCalendarStatus()

    // Check if we just came back from authorization
    const urlParams = new URLSearchParams(window.location.search)
    if (urlParams.get('googleCalendarAuthorized') === 'true') {
      toast.success("Autorisation Google Calendar réussie ! La synchronisation est activée.")
      // Remove the query parameter from URL
      window.history.replaceState({}, '', '/appointments')
      // Refresh status
      checkGoogleCalendarStatus()
    }
  }, [checkGoogleCalendarStatus])

  // Deep-link from a notification: open the referenced appointment — focus its day and open the edit
  // dialog. Graceful (spec Edge Cases): if the appointment no longer exists/is not visible, we simply
  // stay on the list.
  const openAppointmentById = useCallback((appointmentId: string) => {
    window.history.replaceState({}, "", "/appointments")
    appointmentsApi
      .get(appointmentId)
      .then((appt) => {
        setSelectedDate(new Date(appt.appointmentDateTime))
        setSelectedAppointment(appt)
        setEditDialogOpen(true)
      })
      .catch(() => {
        // Target gone or not visible — land on the list, no broken/blank state.
      })
  }, [])

  // On mount (cross-page navigation): read the query param. Uses window.location.search +
  // history.replaceState (no useSearchParams) so a refresh doesn't reopen it, matching the existing
  // Google-auth-return pattern.
  useEffect(() => {
    const appointmentId = new URLSearchParams(window.location.search).get("appointmentId")
    if (appointmentId) openAppointmentById(appointmentId)
  }, [openAppointmentById])

  // Deep-link from a patient's "Planifier un rendez-vous" (?patientId=…): open the create dialog with
  // that patient preselected. Same window.location + replaceState pattern (no useSearchParams) so a
  // refresh doesn't reopen it.
  useEffect(() => {
    const patientId = new URLSearchParams(window.location.search).get("patientId")
    if (patientId) {
      window.history.replaceState({}, "", "/appointments")
      setBookingPatientId(patientId)
      setDialogOpen(true)
    }
  }, [])

  /**
   * Dashboard drill-through (« Rendez-vous honorés » / « Taux d'absence »): `?from=&to=&status=`.
   *
   * <p>The calendar has no arbitrary-range view, so the window is honoured by focusing its FIRST day and switching to
   * the widest view — month — which is the closest honest rendering of "the period the card counted". The status list
   * is comma-separated because the absence rate's numerator is NoShow **and** Cancelled; landing on no-shows alone
   * would show a fraction of what the card counted.</p>
   *
   * <p>Only `Cancelled` and `Completed` need a toggle switched on: the calendar hides exactly those two by default and
   * already shows no-shows. Turning on `showCancelled` for a `NoShow`-only link would surface appointments the card
   * never counted, so the two are matched individually rather than as one "unusual statuses" group.</p>
   *
   * <p>Nothing here refuses a bad value: an unparseable date or an unknown status simply leaves the calendar as it
   * was, matching the graceful-deep-link rule the rest of this page follows.</p>
   *
   * <p>⚠️ <b><c>?date=</c> is handled in THIS effect rather than its own, and that is not tidiness.</b> The last
   * statement here wipes the query string, so a sibling effect declared after this one would read an empty
   * search and never fire. Keeping both in one place also makes the precedence readable: a single day is a more
   * specific request than a window, so <c>?date=</c> wins outright when both are present.</p>
   */
  useEffect(() => {
    /*
     * ⚠️ ONE SHOT, by a ref — the `replaceState` at the end of this effect used to be the guard, and it stopped
     * being one the moment `useUrlFilters` below started writing `?date=` back. A second run then read the
     * durable state as a fresh deep link: `?view=` is omitted when it is the default, so a reloaded Semaine
     * became `?date=…` with no view, which is exactly the shape that forces Jour. Re-consuming your own output
     * is not the same thing as consuming a link.
     */
    if (deepLinkHandledRef.current) return
    deepLinkHandledRef.current = true

    const params = new URLSearchParams(window.location.search)
    const from = params.get("from")
    const date = params.get("date")
    const statuses = (params.get("status") ?? "")
      .split(",")
      .map((s) => s.trim().toLowerCase())
      .filter(Boolean)

    if (!from && !date && statuses.length === 0) return

    /*
     * `?date=AAAA-MM-JJ` — land ON that day, in Jour.
     *
     * The dashboard's « Demain » line is what needs this: it is a *quick access* affordance, and honouring it
     * through `?from=` would drop the reader into the month grid, where finding tomorrow is the work the line
     * exists to remove.
     *
     * Composed as `T00:00:00` and parsed as LOCAL midnight, never bare: a bare `AAAA-MM-JJ` parses as UTC, so
     * for a UTC+1 clinic it lands on the previous day — the same trap `todayLocalIso()` exists to prevent.
     */
    const requestedDay = date ? new Date(`${date}T00:00:00`) : null
    if (requestedDay && !Number.isNaN(requestedDay.getTime())) {
      setSelectedDate(requestedDay)
      // ⚠️ Jour only when the URL named no view. `?date=` is BOTH the dashboard's « Demain » link and half of
      // this screen's own durable state, and forcing Jour unconditionally meant a reloaded Semaine came back as
      // Jour — the write half of the addressable-agenda fix undone by the read half beside it.
      if (!params.get("view")) {
        // `selectView`, not `setView`, for the reason spelled out in the month branch below.
        selectView("day")
      }
    } else if (from && !Number.isNaN(Date.parse(from))) {
      setSelectedDate(new Date(`${from}T00:00:00`))
      // `selectView`, not `setView`: this marks the view DECIDED, so the narrow-screen Jour default below
      // cannot overwrite it. Without that, « RDV honorés — Ce mois » opened on a phone would land on one day.
      selectView("month")
    }

    if (statuses.includes("cancelled")) setShowCancelled(true)
    if (statuses.includes("completed")) setShowCompleted(true)

    window.history.replaceState({}, "", "/appointments")
  }, [selectView])

  /*
   * ⚠️ The agenda's own state goes back INTO the URL — and the wipe above is still right.
   *
   * The two are not in conflict: the effect above consumes the **one-shot** deep-link params (`?appointmentId=`,
   * `?patientId=`, which must not reopen a dialog on F5) and clears them; this writes the **durable** view state
   * that survives a reload. Before it, nothing about the agenda was addressable at all — `?date=2026-08-24` plus
   * « Rendez-vous terminés » became the current week with no filter and 26 blocks, so a colleague could not be
   * sent « regarde le lundi 24 » and the desk could not refresh without losing its place.
   *
   * `toLocalIso`, never `toISOString().slice(0,10)`: the latter converts to UTC first and writes yesterday for the
   * first hour of every Tunisian day.
   */
  useUrlFilters({
    date: toLocalIso(selectedDate),
    // Only when it is not the default, so an ordinary visit keeps a clean URL.
    view: view === "week" ? undefined : view,
    status: [showCompleted ? "completed" : null, showCancelled ? "cancelled" : null].filter(Boolean).join(",") || undefined,
    doctorId: doctorFilterId,
  })

  /**
   * Below `md:`, open on Jour (AC-28).
   *
   * ⚠️ Deliberately an effect keyed on `isNarrow` rather than a lazy `useState` initialiser: `useMediaQuery`
   * is SSR-guarded and reports `false` on the server and on the first client render, so an initialiser would
   * always read "wide" and never fire. The effect runs again once `matchMedia` has answered.
   *
   * One-shot via `viewDecidedRef`, which is what makes it *initial* rather than enforced — the drill-through
   * above has already claimed the view by the time this can run, and once it applies, rotating the device
   * leaves the user's view alone.
   */
  useEffect(() => {
    if (viewDecidedRef.current || !isNarrow) return
    viewDecidedRef.current = true
    setView("day")
  }, [isNarrow])

  // Already on this page: a same-route push doesn't remount, so react to the header's deep-link event.
  useEffect(() => {
    const handler = (e: Event) => {
      const id = (e as CustomEvent<{ appointmentId?: string }>).detail?.appointmentId
      if (id) openAppointmentById(id)
    }
    window.addEventListener("clinic:deeplink", handler)
    return () => window.removeEventListener("clinic:deeplink", handler)
  }, [openAppointmentById])

  const handleAuthorizeGoogleCalendar = useCallback(async () => {
    try {
      // connect() navigates the browser to Google's consent page on success (per-clinic OAuth).
      await googleCalendarApi.connect()
    } catch (error) {
      if (error instanceof ApiError && error.status === 0) {
        toast.error("Connexion perdue. Veuillez réessayer.")
      } else {
        toast.error("Impossible de démarrer la connexion Google Calendar.")
      }
    }
  }, [])

  /**
   * AC-P2.33–2.35: disconnect the clinic's Google account. `Clinic.ClearGoogleCalendarConnection()` had existed
   * with no caller, so a clinic that authorised the wrong Google account could only overwrite it by re-running
   * the whole OAuth flow — never simply stop syncing.
   */
  const handleDisconnectGoogle = useCallback(async () => {
    setIsDisconnecting(true)
    try {
      await googleCalendarApi.disconnect()
      toast.success("Google Calendar déconnecté. Les rendez-vous ne sont plus envoyés à Google.")
      // Re-read rather than assume: the status endpoint is the authority on « connecté », and it also folds in
      // the server-side ClientId/ClientSecret configuration this action does not touch.
      await checkGoogleCalendarStatus()
      setDisconnectOpen(false)
    } catch (error) {
      if (error instanceof ApiError && error.status === 0) {
        toast.error("Connexion perdue. Veuillez réessayer.")
      } else {
        toast.error(error instanceof ApiError ? error.message : "Échec de la déconnexion de Google Calendar.")
      }
    } finally {
      setIsDisconnecting(false)
    }
  }, [checkGoogleCalendarStatus])

  return (
    <ClinicGuard>
      {/* The one genuine `mainClassName` user: the calendar scrolls its own time grid, so the page
          itself must not scroll — and the grid needs a bounded `h-full` flex column to size against. */}
      <AppShell width="wide" mainClassName="overflow-hidden" contentClassName="h-full flex flex-col">
        {/*
          ⚠️ A plain `<div>`, not `<Tabs>` — and that is a correctness fix, not tidying.

          Once the three `<TabsContent>` panels were collapsed into the single always-mounted calendar below,
          `<Tabs>` had no panels left to own. Radix still emits `aria-controls` on every `TabsTrigger`, pointing
          at panel ids that no longer render, so all three desktop triggers advertised a relationship to nothing
          — a screen reader follows the reference and finds no element. Keeping the component only for its
          styling meant keeping a broken ARIA contract for it.

          The replacement is the pattern the other two segmented controls in the app already use — the
          dashboard's `PeriodSelector` and, since this pass, the agenda's own phone header: `role="group"` plus
          `aria-pressed` on real buttons. That describes what this control actually is (three toggles that
          rescope one persistent view) rather than a tab set that lost its tabs.
        */}
        <div className="flex flex-1 flex-col min-h-0">
          {/*
            What is left at page level: the **active-filter chips**, and nothing else.

            The view switch, « Nouveau rendez-vous », the praticien filter and the Google controls used to live
            here in two rows above the calendar's own two — four rows of chrome before the grid, with the *date*
            two rows away from the *view switch* and an administrative Google row between them. They are now props
            on `<AppointmentCalendar>` (`onViewChange`, `onNewAppointment`, `doctorFilter`, `googleControls`) and
            render inside the one agenda bar the calendar owns, which is the component that also owns the window,
            the appointment index and the date arithmetic that bar is made of.

            The chips stay here deliberately. They are a statement about the page's own URL state — two of the
            fifteen entries in `lib/dashboard-links.ts` arrive with `?status=` and flip these on — and § 13
            requires an unrequested filter to be visible and removable *at every width*, so they must not be
            folded into the popover that holds the switches themselves.
          */}
          {/*
            AC-29 — a filter the user did not choose has to be visible and removable.

            Two of the fifteen entries in `lib/dashboard-links.ts` arrive here with `?status=`, which flips
            these toggles on. Without a chip the calendar simply shows more than usual with nothing on
            screen saying why, and « Taux d'absence » lands on a list the user cannot un-filter without
            hunting for a switch inside the calendar's own toolbar.

            ⚠️ **Rendered only when there is a chip to show, and that is a real fix rather than tidying.** This
            used to be an outer `<div className="mb-3 …">` wrapping a `hidden md:flex` row, so in the ordinary
            state — no filter, which is what the desk sees all day — the page still paid a zero-height flex
            container plus **12 px of margin**: a phantom band above the agenda bar that read as a rendering gap
            because that is exactly what it was. One element, one condition.

            It stays `hidden md:flex`: `AgendaPhoneHeader` renders its own copies below `md:`, inside the band
            that holds the phone's other controls.
          */}
          {hasActiveFilterChips && (
            <div className="mb-3 hidden flex-shrink-0 flex-wrap items-center gap-2 md:flex">
              {showCancelled && (
                <ActiveFilterChip label="Annulés affichés" onRemove={() => setShowCancelled(false)} />
              )}
              {showCompleted && (
                <ActiveFilterChip label="Terminés affichés" onRemove={() => setShowCompleted(false)} />
              )}
              {doctorFilterId && (
                <ActiveFilterChip
                  label={`Praticien : ${doctors.find((doc) => doc.id === doctorFilterId)?.name ?? "sélectionné"}`}
                  onRemove={() => setSelectedDoctorId("all")}
                />
              )}
            </div>
          )}

          {/*
            ⚠️ **ONE calendar, rendered outside `TabsContent`.**

            It used to be three — one per `<TabsContent>` — and Radix unmounts inactive content, so *every view
            switch destroyed and rebuilt the calendar*. That is not a re-render, it is a remount: `useAppointments`
            loses `hasLoadedRef` so the full skeleton flashes on every single tap, the clinic-status fetch fires
            again, `monthsAhead` resets, and the scroll position — the one thing a 24-hour grid must keep — is
            gone. Jour ⇄ Semaine ⇄ Mois is the most-used control on the screen, and each press paid for a cold
            start.

            The three prop sets were verified equivalent before collapsing them: only two props differed, and
            neither is read by the view that omitted it. `onSelectDay` was absent on Jour (nothing in Jour calls
            it) and `onTimeSlotClick` absent on Mois (Mois has no hour cells) — so passing both to one instance
            changes no behaviour. `<Tabs>` stays purely for the desktop `TabsList`.
          */}
          <div
            className={cn(
              "flex-1 min-h-0 transition-opacity duration-150 ease-snap",
              viewFading ? "opacity-0" : "opacity-100",
            )}
          >
            <AppointmentCalendar
              reloadToken={refreshKey}
              view={view}
              selectedDate={selectedDate}
              onDateChange={setSelectedDate}
              onTimeSlotClick={handleTimeSlotClick}
              onAppointmentClick={handleAppointmentClick}
              /*
               * Semaine and Mois both navigate to a day.
               *
               * ⚠️ This prop was once missing on Semaine, and the symptom was silent: `renderWeekStrip` — the
               * phone's whole Semaine view — is a list of seven day buttons calling `onSelectDay?.(day)`, so
               * with the prop absent every row was tappable, pressed, highlighted, and did **nothing**. An
               * optional callback that a whole view depends on has no way to complain about not being passed;
               * one instance is one fewer place to forget it.
               */
              onSelectDay={handleSelectDay}
              showCancelled={showCancelled}
              showCompleted={showCompleted}
              onShowCancelledChange={setShowCancelled}
              onShowCompletedChange={setShowCompleted}
              onChanged={handleAppointmentUpdated}
              onViewChange={selectView}
              doctorId={doctorFilterId}
              onNewAppointment={openCreateDialog}
              doctorFilter={{ doctors, value: selectedDoctorId, onChange: setSelectedDoctorId }}
              /*
               * Admins only, and that is the gate rather than a disabled state: every Google endpoint behind
               * these three actions is `AdminOnly`, so offering them to a secretary buys a 403 and a generic
               * « Échec » toast (finding #9). `undefined` leaves the calendar's « ⋯ » menu holding Exporter alone.
               */
              googleControls={
                isAdmin
                  ? {
                      authorized: isGoogleCalendarAuthorized,
                      onConnect: handleAuthorizeGoogleCalendar,
                      // AC-P2.34 — behind an AlertDialog, and deliberately NOT gated on internetReachable:
                      // clearing our own stored token is a local DB write, and it is exactly what an admin needs
                      // when the connected account is wrong or unreachable.
                      onDisconnect: () => setDisconnectOpen(true),
                    }
                  : undefined
              }
            />
          </div>
        </div>

      <CreateAppointmentDialog
        open={dialogOpen}
        onOpenChange={(o) => { setDialogOpen(o); if (!o) setBookingPatientId(undefined) }}
        defaultDate={selectedDate}
        defaultTime={selectedDate ? `${String(selectedDate.getHours()).padStart(2, '0')}:${String(selectedDate.getMinutes()).padStart(2, '0')}` : undefined}
        defaultDurationMinutes={selectedDurationMinutes}
        defaultPatientId={bookingPatientId}
        onSuccess={handleAppointmentCreated}
      />

      <EditAppointmentDialog
        open={editDialogOpen}
        onOpenChange={setEditDialogOpen}
        appointment={selectedAppointment}
        onSuccess={handleAppointmentUpdated}
      />

      {/* AC-P2.34/2.35 — disconnect confirmation. The copy is explicit that nothing is deleted in Google: an
          admin hesitating over this button is usually worried exactly about that. */}
      <AlertDialog open={disconnectOpen} onOpenChange={setDisconnectOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Déconnecter Google Calendar ?</AlertDialogTitle>
            <AlertDialogDescription>
              Les nouveaux rendez-vous ne seront plus envoyés à Google et l&apos;import manuel sera indisponible.
              Rien n&apos;est supprimé : les rendez-vous déjà synchronisés restent dans votre agenda Google, et
              vous pouvez reconnecter le compte à tout moment.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={isDisconnecting}>Annuler</AlertDialogCancel>
            <AlertDialogAction
              onClick={(e) => {
                e.preventDefault()
                void handleDisconnectGoogle()
              }}
              disabled={isDisconnecting}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              {isDisconnecting ? "Déconnexion…" : "Déconnecter"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
      </AppShell>
    </ClinicGuard>
  )
}
