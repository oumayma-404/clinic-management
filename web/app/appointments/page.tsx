"use client"

import { useState, useCallback, useEffect } from "react"
import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { Button } from "@/components/ui/button"
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs"
import { Plus, RefreshCw, Calendar } from "lucide-react"
import { AppointmentCalendar } from "@/components/appointment-calendar"
import { CreateAppointmentDialog } from "@/components/create-appointment-dialog"
import { EditAppointmentDialog } from "@/components/edit-appointment-dialog"
import { ClinicGuard } from "@/components/clinic-guard"
import type { AppointmentDto } from "@/lib/api/types"
import { setHours, setMinutes } from "date-fns"
import { appointmentsApi } from "@/lib/api/appointments"
import { googleCalendarApi } from "@/lib/api/google-calendar"
import { ApiError } from "@/lib/api/client"
import { toast } from "sonner"
import { useConnectivity } from "@/lib/connectivity/connectivity"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useDoctors } from "@/lib/hooks/use-doctors"
import { useSession } from "@/lib/auth/session"
import { Select, SelectTrigger, SelectValue, SelectContent, SelectItem } from "@/components/ui/select"

export default function AppointmentsPage() {
  const [view, setView] = useState<"day" | "week" | "month">("day")
  const [dialogOpen, setDialogOpen] = useState(false)
  const [editDialogOpen, setEditDialogOpen] = useState(false)
  const [selectedAppointment, setSelectedAppointment] = useState<AppointmentDto | null>(null)
  const [selectedDate, setSelectedDate] = useState(new Date())
  const [refreshKey, setRefreshKey] = useState(0)
  // Patient preselected when arriving from a patient's "Planifier un rendez-vous" (?patientId=…).
  const [bookingPatientId, setBookingPatientId] = useState<string | undefined>(undefined)
  const [isGoogleCalendarAuthorized, setIsGoogleCalendarAuthorized] = useState(false)
  const [isSyncing, setIsSyncing] = useState(false)
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

  const handleTimeSlotClick = useCallback((date: Date, time: string) => {
    const [hours, minutes] = time.split(':').map(Number)
    const dateWithTime = setMinutes(setHours(date, hours), minutes)
    setSelectedDate(dateWithTime)
    setDialogOpen(true)
  }, [])

  const handleAppointmentClick = useCallback((appointment: AppointmentDto) => {
    setSelectedAppointment(appointment)
    setEditDialogOpen(true)
  }, [])

  // Month view: clicking a day cell's empty area / "+N more" focuses that date in Day view (AC-4).
  const handleSelectDay = useCallback((date: Date) => {
    setSelectedDate(date)
    setView("day")
  }, [])

  const handleAppointmentCreated = useCallback(() => {
    setRefreshKey(prev => prev + 1)
  }, [])

  const handleAppointmentUpdated = useCallback(() => {
    setRefreshKey(prev => prev + 1)
  }, [])

  // Real-time: when another client of this clinic creates/edits/cancels an appointment, the server
  // broadcasts entityChanged("appointments") and we refetch by bumping refreshKey (the calendar
  // remounts and reloads) — same refresh path as a local create/edit. Additive: if the hub is down,
  // manual refresh still works (AC-5).
  useClinicRealtime(RealtimeResource.Appointments, handleAppointmentUpdated)

  // Check Google Calendar status on mount and after authorization
  useEffect(() => {
    const checkGoogleCalendarStatus = async () => {
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
    }
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
  }, [])

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

  return (
    <ClinicGuard>
      <div className="flex h-screen bg-background">
      <DashboardSidebar />

      <div className="flex flex-1 flex-col overflow-hidden">
        <DashboardHeader />

        <main className="flex-1 overflow-hidden p-4">
          <div className="mx-auto h-full max-w-[1400px] flex flex-col">
            {/* View Tabs */}
            <Tabs value={view} onValueChange={(v) => setView(v as "day" | "week" | "month")} className="flex-1 flex flex-col min-h-0">
              <div className="flex items-center justify-between mb-3 flex-shrink-0">
                <TabsList>
                  <TabsTrigger value="day">Jour</TabsTrigger>
                  <TabsTrigger value="week">Semaine</TabsTrigger>
                  <TabsTrigger value="month">Mois</TabsTrigger>
                </TabsList>
                <div className="flex items-center gap-2">
                  {isAdmin && (!isGoogleCalendarAuthorized ? (
                    <Button
                      onClick={handleAuthorizeGoogleCalendar}
                      variant="outline"
                      className="gap-2"
                      size="sm"
                      disabled={!internetReachable}
                      title={!internetReachable ? "Connexion internet requise" : undefined}
                    >
                      <Calendar className="h-4 w-4" />
                      Synchroniser avec Google Calendar
                    </Button>
                  ) : (
                    <Button
                      onClick={handleSyncFromGoogle}
                      variant="outline"
                      className="gap-2"
                      size="sm"
                      disabled={isSyncing || !internetReachable}
                      title={!internetReachable ? "Connexion internet requise" : undefined}
                    >
                      <RefreshCw className={`h-4 w-4 ${isSyncing ? "animate-spin" : ""}`} />
                      {isSyncing ? "Synchronisation…" : "Importer depuis Google"}
                    </Button>
                  ))}
                  {isAdmin && !internetReachable && (
                    <span className="text-xs text-amber-600 dark:text-amber-400">Connexion requise</span>
                  )}
                  <Select value={selectedDoctorId} onValueChange={setSelectedDoctorId}>
                    <SelectTrigger className="h-9 w-[180px]">
                      <SelectValue placeholder="Praticien" />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="all">Tous les praticiens</SelectItem>
                      {doctors.filter((doc) => doc.id).map((doc) => (
                        <SelectItem key={doc.id} value={doc.id!}>
                          {doc.name}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                  <Button onClick={() => setDialogOpen(true)} className="gap-2" size="sm">
                    <Plus className="h-4 w-4" />
                    Nouveau rendez-vous
                  </Button>
                </div>
              </div>

              <TabsContent value="day" className="flex-1 min-h-0 mt-0">
                <AppointmentCalendar
                  key={refreshKey}
                  view="day"
                  selectedDate={selectedDate}
                  onDateChange={setSelectedDate}
                  onTimeSlotClick={handleTimeSlotClick}
                  onAppointmentClick={handleAppointmentClick}
                  showCancelled={showCancelled}
                  showCompleted={showCompleted}
                  onShowCancelledChange={setShowCancelled}
                  onShowCompletedChange={setShowCompleted}
                  onChanged={handleAppointmentUpdated}
                  doctorId={doctorFilterId}
                />
              </TabsContent>

              <TabsContent value="week" className="flex-1 min-h-0 mt-0">
                <AppointmentCalendar
                  key={refreshKey}
                  view="week"
                  selectedDate={selectedDate}
                  onDateChange={setSelectedDate}
                  onTimeSlotClick={handleTimeSlotClick}
                  onAppointmentClick={handleAppointmentClick}
                  showCancelled={showCancelled}
                  showCompleted={showCompleted}
                  onShowCancelledChange={setShowCancelled}
                  onShowCompletedChange={setShowCompleted}
                  onChanged={handleAppointmentUpdated}
                  doctorId={doctorFilterId}
                />
              </TabsContent>

              <TabsContent value="month" className="flex-1 min-h-0 mt-0">
                <AppointmentCalendar
                  key={refreshKey}
                  view="month"
                  selectedDate={selectedDate}
                  onDateChange={setSelectedDate}
                  onAppointmentClick={handleAppointmentClick}
                  onSelectDay={handleSelectDay}
                  showCancelled={showCancelled}
                  showCompleted={showCompleted}
                  onShowCancelledChange={setShowCancelled}
                  onShowCompletedChange={setShowCompleted}
                  onChanged={handleAppointmentUpdated}
                  doctorId={doctorFilterId}
                />
              </TabsContent>
            </Tabs>
          </div>
        </main>
      </div>

      <CreateAppointmentDialog
        open={dialogOpen}
        onOpenChange={(o) => { setDialogOpen(o); if (!o) setBookingPatientId(undefined) }}
        defaultDate={selectedDate}
        defaultTime={selectedDate ? `${String(selectedDate.getHours()).padStart(2, '0')}:${String(selectedDate.getMinutes()).padStart(2, '0')}` : undefined}
        defaultPatientId={bookingPatientId}
        onSuccess={handleAppointmentCreated}
      />

      <EditAppointmentDialog
        open={editDialogOpen}
        onOpenChange={setEditDialogOpen}
        appointment={selectedAppointment}
        onSuccess={handleAppointmentUpdated}
      />
      </div>
    </ClinicGuard>
  )
}
