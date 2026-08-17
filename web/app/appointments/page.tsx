"use client"

import { useState, useCallback, useEffect, useRef } from "react"
import { AppShell } from "@/components/app-shell"
import { Button } from "@/components/ui/button"
import { Plus } from "lucide-react"
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
import { VisitClosureStrip } from "@/components/visits/visit-closure-strip"
import { CreateAppointmentDialog } from "@/components/create-appointment-dialog"
import { EditAppointmentDialog } from "@/components/edit-appointment-dialog"
import { ClinicGuard } from "@/components/clinic-guard"
import type { AppointmentDto } from "@/lib/api/types"
import { isToday, setHours, setMinutes } from "date-fns"
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

export default function AppointmentsPage() {
  // Week is the default: it is the span staff actually plan against, and a single day of a specialist practice's
  // calendar is mostly empty. Month view stays one click away, and clicking a day cell there still drops into Day
  // view (handleSelectDay). ⚠️ Below `md:` the *initial* view becomes Jour instead — see `viewDecidedRef`.
  const [view, setView] = useState<"day" | "week" | "month">("week")
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
  const viewDecidedRef = useRef(false)
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
  const [selectedDate, setSelectedDate] = useState(new Date())
  const [refreshKey, setRefreshKey] = useState(0)
  // Patient preselected when arriving from a patient's "Planifier un rendez-vous" (?patientId=…).
  const [bookingPatientId, setBookingPatientId] = useState<string | undefined>(undefined)
  const [isGoogleCalendarAuthorized, setIsGoogleCalendarAuthorized] = useState(false)
  const [isSyncing, setIsSyncing] = useState(false)
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
  const [selectedDoctorId, setSelectedDoctorId] = useState<string>("all")
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

  /** « Nouveau rendez-vous » from the bar or the floating action — no span, so no duration override. */
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

  const handleAppointmentCreated = useCallback(() => {
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
      // `selectView`, not `setView`, for the reason spelled out in the month branch below.
      selectView("day")
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

  const handleSyncFromGoogle = useCallback(async () => {
    setIsSyncing(true)
    try {
      await googleCalendarApi.syncFromGoogle()
      toast.success("Synchronisation depuis Google Calendar terminée.")
      setRefreshKey(prev => prev + 1) // Refresh appointments
    } catch (error) {
      // A mid-request connection drop surfaces as ApiError(status:0) — the shared offline signal (LEARNINGS).
      if (error instanceof ApiError && error.status === 0) {
        toast.error("Connexion perdue. Veuillez réessayer.")
      } else {
        toast.error("Échec de la synchronisation", {
          description: error instanceof Error ? error.message : "Erreur inconnue.",
        })
      }
    } finally {
      setIsSyncing(false)
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
            « À clôturer », above the agenda bar rather than inside it — and deliberately NOT a fifth row of
            chrome. It renders nothing at all when there is nothing to close, which is the ordinary state for a
            practice that closes each visit at the chair, so the four-rows-of-chrome problem the comment below
            describes cannot come back through this door. It is here because `/` redirects a secretary to this
            page, so the dashboard's own chip never reaches reception — who is exactly the person who knows
            whether the patient came and who took the money.
          */}
          <VisitClosureStrip />

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
            container plus **12 px of margin**: a phantom band between « À clôturer » and the agenda bar that
            read as a rendering gap because that is exactly what it was. One element, one condition.

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
                      syncing: isSyncing,
                      onConnect: handleAuthorizeGoogleCalendar,
                      onImport: handleSyncFromGoogle,
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

      {/*
        The agenda's create action below `md:`, where the toolbar row above is hidden. « Nouveau rendez-vous »
        keeps a stable home at every width (AC-31).

        ⚠️ **Labelled, not a bare `+`.** The toolbar button it replaces carries an explicit note that its label
        *shortens rather than disappearing*, because an icon-only primary action on the busiest screen in the app
        is the unlabelled-ghost-icon problem P3 spent a whole part removing. A plain `+` circle would have walked
        straight back into it, so this is an extended floating action instead.

        `--bottom-inset` is the global bottom nav plus the home indicator, so the action clears navigation.

        ⚠️ **Centred, and it stays centred now that the collision it fixed is gone.** This used to be `right-4`
        at exactly `bottom-[calc(1rem+var(--bottom-inset))]` — the *byte-identical* anchor the deleted AI
        assistant gave its minimised launcher, at `z-50` against this `z-40` — so that 56 px circle sat
        permanently on top of the right-hand end of this pill. The assistant is gone, so the right-hand corner is
        free again; the centre is kept because it reads correctly on its own terms, the page's own action sitting
        above the nav it belongs to. Moving it back would be churn for no gain, and this is now the *only*
        floating element in the app — which is also why `AppShell` no longer pays for `md:pb-20`.

        A bottom-centre toast can still pass over this for its four seconds (`app-toaster.tsx` anchors there on a
        coarse pointer). That is left alone deliberately: a transient toast over a floating action is ordinary
        mobile behaviour, whereas a button permanently under another button is a defect.

        The wrapper, rather than positioning classes on the `Button`: `ui/button.tsx` applies
        `active:scale-[0.97]`, so the press transform and a centring `-translate-x-1/2` would be arguing over the
        same property. `pointer-events-none` on the full-width strip keeps it from swallowing taps meant for the
        agenda underneath; the button re-enables them for itself.
      */}
      <div className="pointer-events-none fixed inset-x-0 bottom-[calc(1rem+var(--bottom-inset))] z-40 flex justify-center md:hidden">
        <Button
          onClick={openCreateDialog}
          className="pointer-events-auto gap-2 rounded-full shadow-lg"
        >
          <Plus className="h-4 w-4" />
          Nouveau RDV
        </Button>
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
