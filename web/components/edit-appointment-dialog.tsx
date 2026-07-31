"use client"

import type React from "react"
import { useState, useEffect, useMemo, useRef } from "react"
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
  DialogDescription,
} from "@/components/ui/dialog"
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
import { Button } from "@/components/ui/button"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Calendar } from "@/components/ui/calendar"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Switch } from "@/components/ui/switch"
import { Textarea } from "@/components/ui/textarea"
import { Badge } from "@/components/ui/badge"
import { TimeField } from "@/components/ui/time-field"
import { format, parseISO } from "date-fns"
import { CalendarIcon, Clock, User, Stethoscope, FileText, X, Save, Receipt } from "lucide-react"
import { cn, parseDurationToMinutes } from "@/lib/utils"
import { appointmentsApi } from "@/lib/api/appointments"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import { AppointmentActsPicker, totalActsDuration, type SelectedAct } from "@/components/appointment-acts-picker"
import { getErrorMessage } from "@/lib/errors"
import type { AppointmentDto, ProcedureTypeDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { useDoctors } from "@/lib/hooks/use-doctors"
import { useAppointmentOverlap } from "@/lib/hooks/use-appointment-overlap"
import { ApiErrorCode } from "@/lib/api/client"
import { specialtyLabel } from "@/lib/specialties"
import Link from "next/link"
import { InvoiceFormModal } from "@/components/factures/invoice-form-modal"
import {
  APPOINTMENT_STATUSES,
  appointmentStatusBadgeClass,
  appointmentStatusLabel,
} from "@/components/appointment-labels"

/**
 * Sentinel for "no practitioner" in the Radix Select, which cannot hold an empty-string value. Mapped to `""`
 * in state and sent to the API as an explicit `null`, which unassigns the practitioner.
 */
const UNASSIGNED_DOCTOR = "__unassigned__"

interface EditAppointmentDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  appointment: AppointmentDto | null
  onSuccess?: () => void
}

export function EditAppointmentDialog({ open, onOpenChange, appointment, onSuccess }: EditAppointmentDialogProps) {
  // State for all appointment fields
  const [patientName, setPatientName] = useState("")
  // « Facturer cette consultation » — the draft note d'honoraires raised from this visit (AC-P6.12).
  const [billingOpen, setBillingOpen] = useState(false)
  const [date, setDate] = useState<Date | undefined>(new Date())
  const [selectedDoctorId, setSelectedDoctorId] = useState<string>("")
  // `appointmentType` removed with the `Type: ` prefix (AC-P1.51) — the act is `procedureTypeId` alone.
  const [status, setStatus] = useState<string>("scheduled")
  
  // Load doctors list
  const { doctors, currentUserDoctor, isLoading: loadingDoctors } = useDoctors()

  // Procedure type state
  const [procedureTypes, setProcedureTypes] = useState<ProcedureTypeDto[]>([])
  /** The acts of this séance — several are the normal case, not the exception. */
  const [selectedActs, setSelectedActs] = useState<SelectedAct[]>([])
  /**
   * Has the user set the duration by hand *during this editing session*? Until they do, it follows the sum of the
   * acts; afterwards it is left alone. Seeded true when the form hydrates, because a booked visit's stored
   * duration is already a decision someone made — re-deriving it from the acts on open would silently rewrite the
   * length of every appointment merely opened for a look.
   */
  const [durationTouched, setDurationTouched] = useState(true)
  const [loadingProcedureTypes, setLoadingProcedureTypes] = useState(false)
  // AC-P3.31 — why the acte list is empty (C-4: this dialog swallowed the failure without even a comment).
  const [procedureTypesError, setProcedureTypesError] = useState<string | null>(null)

  // Time state
  const [startHour, setStartHour] = useState("09")
  const [startMinute, setStartMinute] = useState("00")
  const [useEndTime, setUseEndTime] = useState(false)
  const [endHour, setEndHour] = useState("10")
  const [endMinute, setEndMinute] = useState("00")
  const [duration, setDuration] = useState("30")

  const [notes, setNotes] = useState("")
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [showCancelDialog, setShowCancelDialog] = useState(false)
  const [showPastTimeConfirm, setShowPastTimeConfirm] = useState(false)
  // The server's out-of-hours reason while its confirm is open, or null. See create-appointment-dialog.
  const [outsideHoursPrompt, setOutsideHoursPrompt] = useState<string | null>(null)
  // Double-booking prompt — the collision is advisory, exactly like the out-of-hours one below.
  const [slotTakenPrompt, setSlotTakenPrompt] = useState<string | null>(null)
  /**
   * Overrides the user has already granted in this submit sequence.
   *
   * <p>Needed because the server checks the collision BEFORE the working hours, so a booking that is both
   * double-booked and out-of-hours prompts twice. Without carrying the first grant forward, confirming the overlap
   * and then confirming the hours would retry with `allowOverlap` lost and prompt for the overlap again — a loop.</p>
   */
  const grantedOverridesRef = useRef({ hours: false, overlap: false })


  // Calculate duration from end time
  const calculatedDuration = useMemo(() => {
    if (useEndTime) {
      const startTotalMinutes = Number.parseInt(startHour) * 60 + Number.parseInt(startMinute)
      const endTotalMinutes = Number.parseInt(endHour) * 60 + Number.parseInt(endMinute)
      const diff = endTotalMinutes - startTotalMinutes
      return diff > 0 ? diff : 0
    }
    return Number.parseInt(duration)
  }, [useEndTime, startHour, startMinute, endHour, endMinute, duration])

  // Format duration display
  const durationDisplay = useMemo(() => {
    const hours = Math.floor(calculatedDuration / 60)
    const mins = calculatedDuration % 60
    if (hours > 0 && mins > 0) return `${hours}h ${mins}m`
    if (hours > 0) return `${hours}h`
    return `${mins}m`
  }, [calculatedDuration])

  /**
   * The statuses this appointment may move to, plus its current one so the Select always has a value for what
   * it is showing. Falls back to the full set only when the server did not send the field (an older cached
   * payload) — never to a client-side re-derivation of the rules.
   */
  const statusOptions = useMemo(() => {
    const current = appointment?.status
    const allowed = appointment?.allowedNextStatuses
    if (!allowed || allowed.length === 0) {
      return [...APPOINTMENT_STATUSES]
    }
    return current && !allowed.includes(current) ? [current, ...allowed] : allowed
  }, [appointment?.status, appointment?.allowedNextStatuses])

  // Advisory overlap warning (AC-3): excludes the appointment being edited; non-blocking.
  const { warning: overlapWarning, samePractitioner: overlapSamePractitioner } = useAppointmentOverlap({
    enabled: open,
    date,
    startHour,
    startMinute,
    durationMinutes: calculatedDuration,
    doctorId: selectedDoctorId || undefined,
    excludeAppointmentId: appointment?.id,
  })

  // Build the appointment start Date from the current date + start time, or null if no date.
  const buildAppointmentDateTime = (): Date | null => {
    if (!date) return null
    const dt = new Date(date)
    dt.setHours(Number.parseInt(startHour), Number.parseInt(startMinute), 0, 0)
    return dt
  }

  // Load procedure types when dialog opens
  useEffect(() => {
    if (open) {
      loadProcedureTypes()
    }
  }, [open])

  const loadProcedureTypes = async () => {
    try {
      setLoadingProcedureTypes(true)
      setProcedureTypesError(null)
      const data = await procedureTypesApi.list(false) // Only active procedure types
      setProcedureTypes(data || [])
    } catch (err) {
      // AC-P3.31 / C-4 — the audit's missing sixth swallow. This one had not even a comment: the list
      // silently rendered « Aucun type d'acte disponible » on a failed call, which is a different fact.
      setProcedureTypesError(getErrorMessage(err, "La liste des actes n'a pas pu être chargée."))
      setProcedureTypes([]) // Ensure it's always an array
    } finally {
      setLoadingProcedureTypes(false)
    }
  }

  /**
   * Editing the act list re-proposes the summed duration — but only once the user has actually touched the acts
   * (`durationTouched` starts true on hydration). Adding a second act to a 30-min visit should offer the room for
   * it; merely opening that visit should not silently relengthen it.
   */
  useEffect(() => {
    if (!open || durationTouched || useEndTime) return
    const total = totalActsDuration(selectedActs, procedureTypes)
    if (total > 0) setDuration(String(total))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, selectedActs, procedureTypes, durationTouched, useEndTime])

  // Populate the form once per opening — keyed on the appointment's ID, not the object. The calendar
  // refetches on every realtime `appointments` event and hands down a fresh object each time; depending
  // on it meant a peer booking an unrelated slot reset this form under the user's hands.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    if (appointment && open) {
      setPatientName(appointment.patientName)
      setStatus(appointment.status.toLowerCase())
      // Try to find doctor by ID first, then by name as fallback
      if ((appointment as any).doctorId) {
        setSelectedDoctorId((appointment as any).doctorId)
      } else if (appointment.doctorName && doctors.length > 0) {
        // Try to find doctor by name
        const doctor = doctors.find(d => d.name === appointment.doctorName)
        if (doctor) {
          setSelectedDoctorId(doctor.id || "")
        } else {
          setSelectedDoctorId("")
        }
      } else {
        setSelectedDoctorId("")
      }
      // Hydrate the whole séance. Falls back to the lead-act scalar for a response that predates `procedures`
      // (or a server that has not been updated), so an older appointment still shows the act it was booked with
      // instead of an empty list that the next save would then persist.
      const storedActs: SelectedAct[] =
        appointment.procedures && appointment.procedures.length > 0
          ? appointment.procedures
              .slice()
              .sort((a, b) => a.sequenceNumber - b.sequenceNumber)
              .map((p) => ({
                procedureTypeId: p.procedureTypeId ?? null,
                treatmentPlanItemId: p.treatmentPlanItemId ?? null,
                planLabel: p.treatmentPlanItemId ? "devis" : undefined,
                fallbackName: p.name ?? undefined,
              }))
          : appointment.procedureTypeId
            ? [
                {
                  procedureTypeId: appointment.procedureTypeId,
                  treatmentPlanItemId: appointment.treatmentPlanItemId ?? null,
                  planLabel: appointment.treatmentPlanItemId ? "devis" : undefined,
                  fallbackName: appointment.procedureTypeName ?? undefined,
                },
              ]
            : []
      setSelectedActs(storedActs)
      setDurationTouched(true)

      // Parse appointment date/time
      const appointmentDate = parseISO(appointment.appointmentDateTime)
      setDate(appointmentDate)

      const hours = String(appointmentDate.getHours()).padStart(2, "0")
      const minutes = String(appointmentDate.getMinutes()).padStart(2, "0")
      setStartHour(hours)
      setStartMinute(minutes)

      // Parse duration
      const durationMinutes = parseDurationToMinutes(appointment.duration)
      setDuration(String(durationMinutes))

      // Calculate end time
      const startTotalMinutes = appointmentDate.getHours() * 60 + appointmentDate.getMinutes()
      const endTotalMinutes = startTotalMinutes + durationMinutes
      const newEndHour = Math.floor(endTotalMinutes / 60) % 24
      const newEndMinute = endTotalMinutes % 60
      setEndHour(String(newEndHour).padStart(2, "0"))
      setEndMinute(String(newEndMinute).padStart(2, "0"))

      // AC-P1.51: the `Type: ` prefix READER is gone, in the same change as the writer below. The act now
      // lives only in `procedureTypeId` (the `MigrateAppointmentTypePrefix` migration moved it there), so the
      // notes are just the notes. The old parser also had two latent bugs worth not carrying forward:
      // `.replace('Type: ', '')` was a first-occurrence-anywhere replace rather than an anchored strip, and the
      // filter dropped EVERY line beginning "Type: ", so a user who legitimately typed a second such line lost
      // it on the next edit round-trip.
      if (appointment.notes) {
        setNotes(appointment.notes)
      } else {
        setNotes("")
      }
    }
  }, [appointment?.id, open])

  // Reset form when dialog closes
  useEffect(() => {
    if (!open) {
      setError(null)
      setUseEndTime(false)
      setShowCancelDialog(false)
      setShowPastTimeConfirm(false)
    }
  }, [open])

  // Synchronous validation; sets an error message and returns false on the first failure.
  const validateForm = (): boolean => {
    if (!appointment) {
      setError("Aucun rendez-vous sélectionné")
      return false
    }

    if (useEndTime) {
      const startTotalMinutes = Number.parseInt(startHour) * 60 + Number.parseInt(startMinute)
      const endTotalMinutes = Number.parseInt(endHour) * 60 + Number.parseInt(endMinute)
      if (endTotalMinutes <= startTotalMinutes) {
        setError("L'heure de fin doit être postérieure à l'heure de début")
        return false
      }
    }

    if (calculatedDuration <= 0) {
      setError("La durée doit être supérieure à 0")
      return false
    }

    if (!date) {
      setError("Veuillez sélectionner une date")
      return false
    }

    return true
  }

  // Performs the actual update. Called directly, or from either confirmation dialog once the user
  // confirms (past time, AC-2; out-of-hours, AC-P1.31).
  const performUpdate = async (allowOutsideWorkingHours = false, allowOverlap = false) => {
    grantedOverridesRef.current = { hours: allowOutsideWorkingHours, overlap: allowOverlap }
    if (!appointment) return
    setError(null)
    setLoading(true)

    try {
      const appointmentDateTime = buildAppointmentDateTime()
      if (!appointmentDateTime) {
        setError("Veuillez sélectionner une date")
        setLoading(false)
        return
      }

      // AC-P1.51: the `Type: ` prefix WRITER is gone. The act is carried by `procedureTypeId` alone — writing
      // the name into the notes as well is what let the two disagree, and the divergence was persisted forward
      // on every save.
      const appointmentNotes = notes.trim()

      // Update appointment via API.
      // Every nullable field here is tri-state on the server: omitting a key leaves the field untouched, an
      // explicit null clears it. So these must send `null`, not `undefined` — `JSON.stringify` drops undefined
      // keys entirely, which is why emptying the notes box or unassigning the practitioner used to be a
      // silent no-op.
      await appointmentsApi.update(appointment.id, {
        appointmentDateTime: appointmentDateTime.toISOString(),
        durationMinutes: calculatedDuration,
        doctorId: selectedDoctorId || null,
        notes: appointmentNotes || null,
        status: status,
        // Replaces the whole list. `[]` is a real instruction here (« ce rendez-vous n'a plus d'acte ») and the
        // server distinguishes it from an omitted key, which is why this dialog always sends it and the cancel
        // path — which posts { status } alone — never does.
        procedures: selectedActs.map((a) => ({
          procedureTypeId: a.procedureTypeId,
          treatmentPlanItemId: a.treatmentPlanItemId ?? null,
        })),
        allowOutsideWorkingHours: allowOutsideWorkingHours || undefined,
        allowOverlap: allowOverlap || undefined,
        // The version this form was hydrated from.
        version: appointment.version,
      })

      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      if (err instanceof ApiError) {
        // Moving an appointment out of hours warns rather than refuses — same reasoning as on create. Guarded on
        // `!allowOutsideWorkingHours` so a re-submit that still refuses shows a real error instead of looping.
        if (err.code === ApiErrorCode.SlotTaken && !allowOverlap) {
          setSlotTakenPrompt(err.message)
        } else if (err.code === ApiErrorCode.OutsideWorkingHours && !allowOutsideWorkingHours) {
          setOutsideHoursPrompt(err.message)
        } else {
          setError(err.message)
        }
      } else {
        setError("Échec de la mise à jour du rendez-vous")
      }
    } finally {
      setLoading(false)
    }
  }

  const handleUpdate = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)

    if (!validateForm()) return

    // Past-time guard (AC-2): only nag when the start is *moved* to a past time. Editing an
    // already-past appointment without changing its start does not trigger the confirmation.
    // Compare on minute granularity: buildAppointmentDateTime zeroes seconds, so normalize
    // originalStart (which keeps the stored seconds, e.g. from a Google-synced appointment) and
    // floor "now" to the current minute — otherwise a no-op save on a sub-minute start would
    // spuriously nag, and re-selecting the current minute would be treated as past.
    const appointmentDateTime = buildAppointmentDateTime()
    const originalStart = appointment ? parseISO(appointment.appointmentDateTime) : null
    originalStart?.setSeconds(0, 0)
    const nowFloored = new Date()
    nowFloored.setSeconds(0, 0)
    const movedToPast =
      appointmentDateTime !== null &&
      appointmentDateTime.getTime() < nowFloored.getTime() &&
      (originalStart === null || appointmentDateTime.getTime() !== originalStart.getTime())
    if (movedToPast) {
      setShowPastTimeConfirm(true)
      return
    }

    await performUpdate()
  }

  const handleCancelAppointment = async () => {
    if (!appointment) return

    setLoading(true)
    try {
      await appointmentsApi.update(appointment.id, {
        status: "cancelled",
        version: appointment.version,
      })

      setShowCancelDialog(false)
      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.message)
      } else {
        setError("Échec de l'annulation du rendez-vous")
      }
    } finally {
      setLoading(false)
    }
  }

  if (!appointment) return null


  // AC-P1.41/1.44: was a string-mangler whose ([A-Z]) branch was dead (the value is lower-cased at
  // hydration), so « Inprogress » and « Noshow » reached the screen. One shared map now.

  return (
    <>
      <Dialog open={open} onOpenChange={onOpenChange}>
        {/* Scrolling body, pinned header and footer — see the create dialog for why. This one matters even
            more: its footer holds three actions, one of them « Annuler le rendez-vous », and a destructive
            action that has to be hunted for by scrolling is a destructive action someone will mis-click. */}
        <DialogContent mobile="sheet" className="gap-0 overflow-hidden p-0 md:max-h-[90dvh] md:max-w-2xl">
          <DialogHeader className="flex-shrink-0 px-6 pb-4 pt-6">
            <div className="flex items-start justify-between gap-4 pr-6">
              <div>
                <DialogTitle className="text-2xl">Modifier le rendez-vous</DialogTitle>
                <DialogDescription>Mettez à jour les détails du rendez-vous ou changez son statut</DialogDescription>
              </div>
              <Badge variant="secondary" className={appointmentStatusBadgeClass(status)}>
                {appointmentStatusLabel(status)}
              </Badge>
            </div>
          </DialogHeader>

          <form onSubmit={handleUpdate} className="flex min-h-0 flex-1 flex-col">
            <div className="min-h-0 flex-1 space-y-6 overflow-y-auto px-6 pb-4">
            <FormErrorBanner message={error} />

            {/* Patient Section */}
            <div className="space-y-4 p-4 rounded-lg border bg-muted/30">
              <div className="flex items-center gap-2">
                <User className="h-5 w-5 text-muted-foreground" />
                <h3 className="font-semibold">Informations du patient</h3>
              </div>

              <div className="space-y-2">
                <Label htmlFor="patientName" className="text-sm">
                  Nom du patient
                </Label>
                <Input
                  id="patientName"
                  value={patientName}
                  onChange={(e) => setPatientName(e.target.value)}
                  className="h-10"
                  disabled
                />
                <p className="text-xs text-muted-foreground">Le nom du patient ne peut pas être modifié</p>
              </div>
            </div>

            {/* Date & Time Section */}
            <div className="space-y-4 p-4 rounded-lg border bg-muted/30">
              <div className="flex items-center gap-2">
                <CalendarIcon className="h-5 w-5 text-muted-foreground" />
                <h3 className="font-semibold">Date et heure</h3>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                {/* Date Picker */}
                <div className="space-y-2">
                  <Label htmlFor="edit-appt-date" className="text-sm">Date *</Label>
                  <Popover modal>
                    <PopoverTrigger asChild>
                      <Button
                        id="edit-appt-date"
                        variant="outline"
                        className={cn(
                          "w-full h-10 justify-start text-left font-normal",
                          !date && "text-muted-foreground",
                        )}
                        disabled={loading}
                      >
                        <CalendarIcon className="mr-2 h-4 w-4" />
                        {date ? format(date, "dd/MM/yyyy") : "Choisir une date"}
                      </Button>
                    </PopoverTrigger>
                    <PopoverContent className="w-auto p-0" align="start">
                      <Calendar mode="single" selected={date} onSelect={setDate} initialFocus />
                    </PopoverContent>
                  </Popover>
                </div>

                {/* Start Time — one typeable field, as in the create dialog. */}
                <div className="space-y-2">
                  <Label htmlFor="edit-appt-start-time" className="text-sm">Heure de début *</Label>
                  <TimeField
                    id="edit-appt-start-time"
                    hour={startHour}
                    minute={startMinute}
                    onChange={({ hour, minute }) => {
                      setStartHour(hour)
                      setStartMinute(minute)
                    }}
                    disabled={loading}
                    required
                  />
                </div>
              </div>

              {/* Duration Toggle */}
              <div className="flex items-center justify-between pt-2 border-t">
                <div className="flex items-center gap-2">
                  <Clock className="h-4 w-4 text-muted-foreground" />
                  <span className="text-sm font-medium">Durée</span>
                  {calculatedDuration > 0 && (
                    <Badge variant="secondary" className="ml-2">
                      {durationDisplay}
                    </Badge>
                  )}
                </div>
                <div className="flex items-center gap-2">
                  <span className="text-sm text-muted-foreground">Définir l'heure de fin</span>
                  <Switch
                    checked={useEndTime}
                    onCheckedChange={(checked) => {
                      setUseEndTime(checked)
                      if (checked) {
                        const startTotalMinutes = Number.parseInt(startHour) * 60 + Number.parseInt(startMinute)
                        const endTotalMinutes = startTotalMinutes + Number.parseInt(duration)
                        const newEndHour = Math.floor(endTotalMinutes / 60) % 24
                        const newEndMinute = endTotalMinutes % 60
                        setEndHour(String(newEndHour).padStart(2, "0"))
                        setEndMinute(String(newEndMinute).padStart(2, "0"))
                      }
                    }}
                    disabled={loading}
                  />
                </div>
              </div>

              {/* Duration or End Time Input */}
              <div className="space-y-2">
                {useEndTime ? (
                  <div className="space-y-2">
                    <Label htmlFor="edit-appt-end-time" className="text-sm">Heure de fin *</Label>
                    <TimeField
                      id="edit-appt-end-time"
                      hour={endHour}
                      minute={endMinute}
                      onChange={({ hour, minute }) => {
                        setEndHour(hour)
                        setEndMinute(minute)
                      }}
                      disabled={loading}
                      required
                    />
                  </div>
                ) : (
                  <div className="flex gap-2">
                    {[15, 30, 45, 60, 90, 120].map((mins) => (
                      <Button
                        key={mins}
                        type="button"
                        variant={duration === String(mins) ? "default" : "outline"}
                        size="sm"
                        onClick={() => setDuration(String(mins))}
                        className="flex-1"
                        disabled={loading}
                      >
                        {mins < 60 ? `${mins}m` : `${mins / 60}h`}
                      </Button>
                    ))}
                  </div>
                )}
              </div>

              {/* Overlap warning — reserved height so the message appearing/disappearing while the user
                  nudges the time does not shove the form under their cursor, and a blocking clash explains
                  the disabled submit right here rather than leaving it silently dead. See the create dialog. */}
              <div className="min-h-[2.5rem] pt-1" aria-live="polite">
                {overlapWarning && (
                  <div
                    className={cn(
                      "space-y-0.5 text-sm transition-opacity duration-200 ease-snap",
                      overlapSamePractitioner ? "text-red-600 dark:text-red-400" : "text-amber-600 dark:text-amber-400",
                    )}
                  >
                    <p>⚠ {overlapWarning}</p>
                    {overlapSamePractitioner && (
                      <p className="text-xs">
                        Vous pouvez continuer : une confirmation vous sera demandée.
                      </p>
                    )}
                  </div>
                )}
              </div>
            </div>

            {/* Additional Details Section */}
            <div className="space-y-4 p-4 rounded-lg border bg-muted/30">
              <div className="flex items-center gap-2">
                <Stethoscope className="h-5 w-5 text-muted-foreground" />
                <h3 className="font-semibold">Détails</h3>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div className="space-y-2">
                  <Label htmlFor="doctor" className="text-sm">
                    Médecin
                  </Label>
                  <Select
                    value={selectedDoctorId || UNASSIGNED_DOCTOR}
                    onValueChange={(value) =>
                      setSelectedDoctorId(value === UNASSIGNED_DOCTOR ? "" : value)
                    }
                    disabled={loadingDoctors || loading}
                  >
                    <SelectTrigger className="h-10 w-full" id="doctor">
                      <SelectValue placeholder={loadingDoctors ? "Chargement des médecins…" : doctors.length === 0 ? "Aucun médecin trouvé" : "Choisir un médecin…"} />
                    </SelectTrigger>
                    <SelectContent className="max-h-[200px]">
                      {/* Unassigning is now a real operation server-side (an explicit null clears DoctorId),
                          so it needs an option. Radix Select cannot hold value="" — hence the sentinel. */}
                      <SelectItem value={UNASSIGNED_DOCTOR}>Aucun praticien</SelectItem>
                      {doctors.length === 0 && !loadingDoctors ? (
                        <div className="px-2 py-1.5 text-sm text-muted-foreground">Aucun médecin disponible</div>
                      ) : (
                        doctors.map((doctor) => (
                          <SelectItem key={doctor.id || doctor.name} value={doctor.id || ""}>
                            {doctor.name} {doctor.specialty ? `- ${specialtyLabel(doctor.specialty)}` : ""}
                          </SelectItem>
                        ))
                      )}
                    </SelectContent>
                  </Select>
                </div>

                {/* The séance's acts, spanning the row: it is a list that grows, and a half-width column
                    truncates every act name. Editing it re-proposes the summed duration. */}
                <div className="md:col-span-2">
                  <AppointmentActsPicker
                    procedureTypes={procedureTypes}
                    loading={loadingProcedureTypes}
                    error={procedureTypesError}
                    onRetry={() => void loadProcedureTypes()}
                    value={selectedActs}
                    onChange={(acts) => { setDurationTouched(false); setSelectedActs(acts) }}
                    disabled={loading}
                    onProcedureCreated={(created) => setProcedureTypes((prev) => [...prev, created])}
                    fallbackDurationMinutes={calculatedDuration}
                    idPrefix="edit-appt"
                  />
                </div>

                <div className="space-y-2 md:col-span-2">
                  <Label htmlFor="status" className="text-sm">
                    Statut
                  </Label>
                  <Select
                    value={status}
                    onValueChange={(value) => setStatus(value)}
                    disabled={loading}
                  >
                    <SelectTrigger id="status" className="h-10">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {/* AC-P1.6/1.43: the options come from the server's declared transition table, so the
                          control can no longer offer a move the API refuses. The six hardcoded options were
                          also the only French status copy in the app — that now lives in appointment-labels.ts.
                          Values stay lower-cased because this dialog posts the selection back verbatim. */}
                      {statusOptions.map((s) => (
                        <SelectItem key={s} value={s.toLowerCase()}>
                          {appointmentStatusLabel(s)}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
              </div>
            </div>

            {/* Facturation (AC-P6.13) — a visit says which note d'honoraires bills it, and offers to raise one
                when it does not. The link was write-only before: the column existed, nothing populated it, and
                no screen read it, so « cette consultation a-t-elle été facturée ? » had no answer anywhere. */}
            {appointment?.patientId && (
              <div className="space-y-3 p-4 rounded-lg border bg-muted/30">
                <div className="flex items-center gap-2">
                  <Receipt className="h-5 w-5 text-muted-foreground" />
                  <h3 className="font-semibold">Facturation</h3>
                </div>
                {appointment.invoiceId ? (
                  <div className="flex flex-wrap items-center gap-2">
                    <Badge variant="outline" className="gap-1">
                      <Receipt className="h-3 w-3" />
                      Facturé
                    </Badge>
                    <span className="text-sm text-muted-foreground">
                      {appointment.invoiceNumber
                        ? `Note n° ${appointment.invoiceNumber}`
                        : /* A draft consumes no number yet — say so rather than printing an empty « n° ». */
                          "Brouillon de note d'honoraires"}
                    </span>
                    <Button asChild type="button" variant="link" size="sm" className="h-auto p-0">
                      <Link href="/factures">Ouvrir dans « Factures »</Link>
                    </Button>
                  </div>
                ) : (
                  <div className="space-y-2">
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={() => setBillingOpen(true)}
                      disabled={loading}
                    >
                      <Receipt className="h-4 w-4 mr-2" />
                      Facturer cette consultation
                    </Button>
                    <p className="text-xs text-muted-foreground">
                      Crée un brouillon de note d&apos;honoraires rattaché à ce rendez-vous.
                    </p>
                  </div>
                )}
              </div>
            )}

            {/* Notes Section */}
            <div className="space-y-4 p-4 rounded-lg border bg-muted/30">
              <div className="flex items-center gap-2">
                <FileText className="h-5 w-5 text-muted-foreground" />
                <h3 className="font-semibold">Notes</h3>
              </div>
              <Textarea
                placeholder="Ajouter des notes ou des instructions particulières…"
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
                className="min-h-[80px] resize-none"
                disabled={loading}
              />
            </div>

            </div>

            <DialogFooter className="flex-shrink-0 flex-col gap-2 border-t bg-background px-6 py-4 sm:flex-row">
              <Button
                type="button"
                variant="destructive"
                onClick={() => setShowCancelDialog(true)}
                disabled={loading || status === "cancelled"}
                className="sm:mr-auto"
              >
                <X className="h-4 w-4 mr-2" />
                Annuler le rendez-vous
              </Button>
              <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={loading}>
                Fermer
              </Button>
              {/* No longer disabled on an overlap: the collision is advisory and the server offers the override.
                  Blocking here made the warning a dead end and hid the fact that proceeding is allowed. */}
              <Button type="submit" disabled={loading}>
                <Save className="h-4 w-4 mr-2" />
                {loading ? "Enregistrement…" : "Enregistrer les modifications"}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>

      {/* The invoice draft is raised from an appointment context, so it carries `appointmentId` (AC-P6.12).
          Rendered as a sibling of the dialog rather than inside it — nesting a Dialog inside a Dialog fights
          over the focus trap. */}
      {appointment?.patientId && (
        <InvoiceFormModal
          open={billingOpen}
          onOpenChange={setBillingOpen}
          presetPatientId={appointment.patientId}
          presetPatientName={appointment.patientName}
          appointmentId={appointment.id}
          onSuccess={() => {
            setBillingOpen(false)
            // Refetch so the « Facturé » badge above replaces the button without a manual reload.
            onSuccess?.()
          }}
        />
      )}

      {/* Cancel Appointment Confirmation Dialog */}
      <AlertDialog open={showCancelDialog} onOpenChange={setShowCancelDialog}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Annuler le rendez-vous ?</AlertDialogTitle>
            <AlertDialogDescription>
              Voulez-vous vraiment annuler ce rendez-vous avec {patientName} ? Cette action peut être annulée en
              remettant le statut sur « Planifié ».
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={loading}>Non, conserver</AlertDialogCancel>
            <AlertDialogAction
              onClick={handleCancelAppointment}
              disabled={loading}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              {loading ? "Annulation…" : "Oui, annuler le rendez-vous"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {/* Past-time confirmation (AC-2): only shown when the start is moved to a past time. */}
      <AlertDialog open={showPastTimeConfirm} onOpenChange={setShowPastTimeConfirm}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Heure dans le passé</AlertDialogTitle>
            <AlertDialogDescription>
              L&apos;heure sélectionnée est déjà passée. Voulez-vous quand même enregistrer ce rendez-vous ?
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={loading}>Annuler</AlertDialogCancel>
            <AlertDialogAction
              onClick={() => {
                setShowPastTimeConfirm(false)
                performUpdate()
              }}
              disabled={loading}
            >
              Continuer
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {/*
        Double-booking confirmation — same shape as the out-of-hours confirm below, and for the same reason: an
        overlap with the same practitioner is a legitimate thing a clinic does (a second chair, an assistant
        preparing one patient while the dentist starts another, an emergency squeezed in), so it warns and lets the
        user proceed instead of refusing.

        Driven by the server's `slot_taken` code rather than the local overlap hook, because the server is the
        authority — and because confirming here sets `allowOverlap`, which records the acknowledgement and exempts
        the row from the database's exclusion constraint. Without that flag the write is impossible, so a purely
        client-side "ignore the warning" would just fail at the database.
      */}
      <AlertDialog
        open={slotTakenPrompt !== null}
        onOpenChange={(o) => { if (!o) setSlotTakenPrompt(null) }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Créneau déjà occupé</AlertDialogTitle>
            <AlertDialogDescription>
              {slotTakenPrompt} Voulez-vous quand même enregistrer ce rendez-vous ? Le double rendez-vous sera
              enregistré comme volontaire.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={loading}>Annuler</AlertDialogCancel>
            <AlertDialogAction
              onClick={() => {
                setSlotTakenPrompt(null)
                // `true` makes the server record it via Appointment.MarkBookedWithOverlap() rather than letting it
                // through unmarked — the flag is both the audit trail and the constraint exemption.
                void performUpdate(grantedOverridesRef.current.hours, true)
              }}
              disabled={loading}
            >
              Continuer
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {/*
        Out-of-hours confirmation — same shape as the past-time confirm above, and deliberately so: moving a visit
        outside the posted hours is something clinics legitimately do, so it warns and lets the user proceed.
        Driven by the server's `outside_working_hours` code rather than a client-side hours check, so the rule
        (doctor override → clinic → unrestricted, in clinic-local time) stays in one place.
      */}
      <AlertDialog
        open={outsideHoursPrompt !== null}
        onOpenChange={(o) => { if (!o) setOutsideHoursPrompt(null) }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>En dehors des horaires d&apos;ouverture</AlertDialogTitle>
            <AlertDialogDescription>
              {outsideHoursPrompt} Voulez-vous quand même enregistrer ce rendez-vous ?
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={loading}>Annuler</AlertDialogCancel>
            <AlertDialogAction
              onClick={() => {
                setOutsideHoursPrompt(null)
                // `true` records the move as a deliberate exception (MarkBookedOutsideWorkingHours).
                void performUpdate(true, grantedOverridesRef.current.overlap)
              }}
              disabled={loading}
            >
              Continuer
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}


