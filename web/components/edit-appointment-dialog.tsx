"use client"

import type React from "react"
import { useState, useEffect, useMemo } from "react"
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
import { format, parseISO } from "date-fns"
import { CalendarIcon, Clock, User, Stethoscope, FileText, X, Save } from "lucide-react"
import { cn, parseDurationToMinutes } from "@/lib/utils"
import { appointmentsApi } from "@/lib/api/appointments"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import type { AppointmentDto, ProcedureTypeDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { useDoctors } from "@/lib/hooks/use-doctors"
import { useAppointmentOverlap } from "@/lib/hooks/use-appointment-overlap"

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
  const [date, setDate] = useState<Date | undefined>(new Date())
  const [selectedDoctorId, setSelectedDoctorId] = useState<string>("")
  const [appointmentType, setAppointmentType] = useState("")
  const [status, setStatus] = useState<string>("scheduled")
  
  // Load doctors list
  const { doctors, currentUserDoctor, isLoading: loadingDoctors } = useDoctors()

  // Procedure type state
  const [procedureTypes, setProcedureTypes] = useState<ProcedureTypeDto[]>([])
  const [selectedProcedureTypeId, setSelectedProcedureTypeId] = useState<string | undefined>(undefined)
  const [loadingProcedureTypes, setLoadingProcedureTypes] = useState(false)

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

  // Advisory overlap warning (AC-3): excludes the appointment being edited; non-blocking.
  const { warning: overlapWarning, blocking: overlapBlocking } = useAppointmentOverlap({
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
      const data = await procedureTypesApi.list(false) // Only active procedure types
      setProcedureTypes(data || [])
    } catch (err) {
      console.error("Failed to load procedure types:", err)
      setProcedureTypes([]) // Ensure it's always an array
    } finally {
      setLoadingProcedureTypes(false)
    }
  }

  // Handle procedure type selection - update duration when procedure is selected
  useEffect(() => {
    if (selectedProcedureTypeId && procedureTypes.length > 0) {
      const selectedProcedure = procedureTypes.find(p => p.id === selectedProcedureTypeId)
      if (selectedProcedure && selectedProcedure.defaultDurationMinutes) {
        setDuration(String(selectedProcedure.defaultDurationMinutes))
      }
    }
  }, [selectedProcedureTypeId, procedureTypes])

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
      setSelectedProcedureTypeId(appointment.procedureTypeId || undefined)

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

      // Parse notes to extract appointment type if present
      if (appointment.notes) {
        const notesLines = appointment.notes.split('\n')
        const typeLine = notesLines.find(line => line.startsWith('Type: '))
        if (typeLine) {
          setAppointmentType(typeLine.replace('Type: ', '').trim())
          const remainingNotes = notesLines.filter(line => !line.startsWith('Type: ')).join('\n').trim()
          setNotes(remainingNotes)
        } else {
          setNotes(appointment.notes)
        }
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

  // Performs the actual update. Called directly, or from the past-time confirmation dialog once the
  // user confirms (AC-2).
  const performUpdate = async () => {
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

      // Combine appointment type and notes
      let appointmentNotes = notes.trim()
      if (appointmentType && notes.trim()) {
        appointmentNotes = `Type: ${appointmentType}\n${notes.trim()}`
      } else if (appointmentType) {
        appointmentNotes = `Type: ${appointmentType}`
      }

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
        procedureTypeId: selectedProcedureTypeId || null,
        // The version this form was hydrated from.
        version: appointment.version,
      })

      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.message)
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

  const statusColors: Record<string, string> = {
    scheduled: "bg-blue-100 text-blue-700 border-blue-300 dark:bg-blue-500/20 dark:text-blue-400",
    confirmed: "bg-blue-100 text-blue-700 border-blue-300 dark:bg-blue-500/20 dark:text-blue-400",
    completed: "bg-green-100 text-green-700 border-green-300 dark:bg-green-500/20 dark:text-green-400",
    cancelled: "bg-gray-100 text-gray-700 border-gray-300 dark:bg-gray-800 dark:text-gray-400",
    inprogress: "bg-yellow-100 text-yellow-700 border-yellow-300 dark:bg-yellow-500/20 dark:text-yellow-400",
    noshow: "bg-orange-100 text-orange-700 border-orange-300 dark:bg-orange-500/20 dark:text-orange-400",
  }

  const statusDisplay = status.charAt(0).toUpperCase() + status.slice(1).replace(/([A-Z])/g, ' $1').trim()

  return (
    <>
      <Dialog open={open} onOpenChange={onOpenChange}>
        <DialogContent className="max-w-2xl max-h-[90vh] overflow-y-auto">
          <DialogHeader>
            <div className="flex items-start justify-between">
              <div>
                <DialogTitle className="text-2xl">Modifier le rendez-vous</DialogTitle>
                <DialogDescription>Mettez à jour les détails du rendez-vous ou changez son statut</DialogDescription>
              </div>
              <Badge className={cn("border", statusColors[status] || statusColors.scheduled)}>
                {statusDisplay}
              </Badge>
            </div>
          </DialogHeader>

          <form onSubmit={handleUpdate} className="space-y-6 mt-4">
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
                <h3 className="font-semibold">Date & Time</h3>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                {/* Date Picker */}
                <div className="space-y-2">
                  <Label className="text-sm">Date *</Label>
                  <Popover modal>
                    <PopoverTrigger asChild>
                      <Button
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

                {/* Start Time */}
                <div className="space-y-2">
                  <Label className="text-sm">Heure de début *</Label>
                  <div className="flex gap-2">
                    <Select value={startHour} onValueChange={setStartHour} disabled={loading}>
                      <SelectTrigger className="h-10">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent className="max-h-[200px]">
                        {Array.from({ length: 24 }, (_, i) => {
                          const hour = String(i).padStart(2, "0")
                          return (
                            <SelectItem key={i} value={hour}>
                              {hour}
                            </SelectItem>
                          )
                        })}
                      </SelectContent>
                    </Select>
                    <span className="flex items-center text-lg font-semibold">:</span>
                    <Select value={startMinute} onValueChange={setStartMinute} disabled={loading}>
                      <SelectTrigger className="h-10">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent className="max-h-[200px]">
                        {Array.from({ length: 60 }, (_, i) => {
                          const min = String(i).padStart(2, "0")
                          return (
                            <SelectItem key={i} value={min}>
                              {min}
                            </SelectItem>
                          )
                        })}
                      </SelectContent>
                    </Select>
                  </div>
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
                    <Label className="text-sm">Heure de fin *</Label>
                    <div className="flex gap-2">
                      <Select value={endHour} onValueChange={setEndHour} disabled={loading}>
                        <SelectTrigger className="h-10">
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent className="max-h-[200px]">
                          {Array.from({ length: 24 }, (_, i) => {
                            const hour = String(i).padStart(2, "0")
                            return (
                              <SelectItem key={i} value={hour}>
                                {hour}
                              </SelectItem>
                            )
                          })}
                        </SelectContent>
                      </Select>
                      <span className="flex items-center text-lg font-semibold">:</span>
                      <Select value={endMinute} onValueChange={setEndMinute} disabled={loading}>
                        <SelectTrigger className="h-10">
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent className="max-h-[200px]">
                          {Array.from({ length: 60 }, (_, i) => {
                            const min = String(i).padStart(2, "0")
                            return (
                              <SelectItem key={i} value={min}>
                                {min}
                              </SelectItem>
                            )
                          })}
                        </SelectContent>
                      </Select>
                    </div>
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

              {/* Overlap warning: red + blocking for a same-practitioner clash, amber advisory otherwise. */}
              {overlapWarning && (
                <p
                  className={
                    overlapBlocking
                      ? "text-sm text-red-600 dark:text-red-400"
                      : "text-sm text-amber-600 dark:text-amber-400"
                  }
                >
                  ⚠ {overlapWarning}
                </p>
              )}
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
                            {doctor.name} {doctor.specialty ? `- ${doctor.specialty}` : ""}
                          </SelectItem>
                        ))
                      )}
                    </SelectContent>
                  </Select>
                </div>

                <div className="space-y-2">
                  <Label htmlFor="procedureType" className="text-sm">
                    Type d'acte
                  </Label>
                  <Select 
                    value={selectedProcedureTypeId} 
                    onValueChange={setSelectedProcedureTypeId}
                    disabled={loadingProcedureTypes || loading}
                  >
                    <SelectTrigger id="procedureType" className="h-10 w-full">
                      <SelectValue placeholder={loadingProcedureTypes ? "Chargement…" : "Sélectionner un type d'acte"} />
                    </SelectTrigger>
                    <SelectContent className="max-h-[200px]">
                      {procedureTypes.length === 0 && !loadingProcedureTypes ? (
                        <div className="px-2 py-1.5 text-sm text-muted-foreground">Aucun type d'acte disponible</div>
                      ) : (
                        procedureTypes.map((procedureType) => (
                          <SelectItem key={procedureType.id} value={procedureType.id}>
                            <div className="flex items-center gap-2">
                              <div 
                                className="w-3 h-3 rounded-full" 
                                style={{ backgroundColor: procedureType.colorHex }}
                              />
                              {procedureType.name} ({procedureType.defaultDurationMinutes} min)
                            </div>
                          </SelectItem>
                        ))
                      )}
                    </SelectContent>
                  </Select>
                  {selectedProcedureTypeId && (
                    <div className="flex items-center gap-2 mt-1">
                      <p className="text-xs text-muted-foreground">
                        Durée fixée à {procedureTypes.find(p => p.id === selectedProcedureTypeId)?.defaultDurationMinutes} minutes (vous pouvez la modifier)
                      </p>
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        className="h-5 px-2 text-xs"
                        onClick={() => setSelectedProcedureTypeId(undefined)}
                        disabled={loading}
                      >
                        Effacer
                      </Button>
                    </div>
                  )}
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
                      <SelectItem value="scheduled">Planifié</SelectItem>
                      <SelectItem value="confirmed">Confirmé</SelectItem>
                      <SelectItem value="inprogress">En cours</SelectItem>
                      <SelectItem value="completed">Terminé</SelectItem>
                      <SelectItem value="cancelled">Annulé</SelectItem>
                      <SelectItem value="noshow">Absence</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
              </div>
            </div>

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

            <DialogFooter className="gap-2 pt-2 flex-col sm:flex-row">
              <Button
                type="button"
                variant="destructive"
                onClick={() => setShowCancelDialog(true)}
                disabled={loading || status === "cancelled"}
                className="sm:mr-auto"
              >
                <X className="h-4 w-4 mr-2" />
                Cancel Appointment
              </Button>
              <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={loading}>
                Close
              </Button>
              <Button type="submit" disabled={loading || overlapBlocking}>
                <Save className="h-4 w-4 mr-2" />
                {loading ? "Enregistrement…" : "Enregistrer les modifications"}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>

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
    </>
  )
}


