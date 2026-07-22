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
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from "@/components/ui/command"
import { Calendar } from "@/components/ui/calendar"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Switch } from "@/components/ui/switch"
import { Textarea } from "@/components/ui/textarea"
import { Badge } from "@/components/ui/badge"
import { format } from "date-fns"
import { CalendarIcon, Clock, User, Stethoscope, FileText, Check, ChevronsUpDown, Plus } from "lucide-react"
import { cn } from "@/lib/utils"
import { appointmentsApi } from "@/lib/api/appointments"
import { patientsApi } from "@/lib/api/patients"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import type { PatientDto, ProcedureTypeDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { useDoctors } from "@/lib/hooks/use-doctors"
import { useAppointmentOverlap } from "@/lib/hooks/use-appointment-overlap"

// Sentinel value for the "custom procedure" option inside the procedure-type Select.
const CUSTOM_PROCEDURE_VALUE = "__custom__"
// Default colors for on-the-fly custom procedures — MUST be values the backend ColorHex value object
// accepts (same curated palette as the procedure-type form). Rotated by catalog size for variety.
const CUSTOM_PROCEDURE_COLORS = ["#4F83CC", "#2A9D8F", "#6BAA75", "#9B8EDC", "#E9A23B", "#E76F51"]

interface CreateAppointmentDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  defaultDate?: Date
  defaultTime?: string
  onSuccess?: () => void
  /** When scheduling a treatment-plan step ("Planifier"): fixes the patient and links the appointment. */
  presetPatientId?: string
  presetPatientName?: string
  presetPlanId?: string
  presetPlanItemId?: string
  presetProcedureName?: string
}

export function CreateAppointmentDialog({
  open,
  onOpenChange,
  defaultDate,
  defaultTime,
  onSuccess,
  presetPatientId,
  presetPatientName,
  presetPlanId,
  presetPlanItemId,
  presetProcedureName,
}: CreateAppointmentDialogProps) {
  // True when this dialog was opened to schedule a specific treatment-plan step.
  const isPlanScheduling = Boolean(presetPlanItemId)
  // Patient state
  const [isBusySlot, setIsBusySlot] = useState(false)
  const [isNewPatient, setIsNewPatient] = useState(false)
  const [selectedPatientId, setSelectedPatientId] = useState("")
  const [newPatientFirstName, setNewPatientFirstName] = useState("")
  const [newPatientLastName, setNewPatientLastName] = useState("")
  const [patients, setPatients] = useState<PatientDto[]>([])
  const [loadingPatients, setLoadingPatients] = useState(false)

  // Procedure type state
  const [procedureTypes, setProcedureTypes] = useState<ProcedureTypeDto[]>([])
  const [selectedProcedureTypeId, setSelectedProcedureTypeId] = useState<string | undefined>(undefined)
  const [loadingProcedureTypes, setLoadingProcedureTypes] = useState(false)

  // Inline "custom procedure" creation: pick the custom option in the dropdown, enter a name (+ optional
  // typical duration/amount); it's saved to the procedure catalog and selected. If no typical duration is
  // given, the appointment's current duration is used; if given, the appointment duration follows it.
  const [customProcedureMode, setCustomProcedureMode] = useState(false)
  const [customProcedureName, setCustomProcedureName] = useState("")
  const [customProcedureDuration, setCustomProcedureDuration] = useState("")
  const [customProcedureCost, setCustomProcedureCost] = useState("")
  const [creatingCustomProcedure, setCreatingCustomProcedure] = useState(false)
  const [customProcedureError, setCustomProcedureError] = useState<string | null>(null)

  // Appointment details
  const [date, setDate] = useState<Date | undefined>(defaultDate || new Date())
  const [selectedDoctorId, setSelectedDoctorId] = useState<string>("")
  const [appointmentType, setAppointmentType] = useState("")
  
  // Load doctors list
  const { doctors, currentUserDoctor, isLoading: loadingDoctors } = useDoctors()
  
  // Auto-select current user's doctor if they are a doctor
  useEffect(() => {
    if (open && currentUserDoctor && !selectedDoctorId) {
      setSelectedDoctorId(currentUserDoctor.id || "")
    }
  }, [open, currentUserDoctor, selectedDoctorId])

  // Time state - extract from defaultDate if available, otherwise use defaultTime
  const getInitialTime = () => {
    if (defaultDate) {
      return {
        hour: String(defaultDate.getHours()).padStart(2, "0"),
        minute: String(defaultDate.getMinutes()).padStart(2, "0")
      }
    }
    if (defaultTime) {
      const [hour, minute] = defaultTime.split(":")
      return {
        hour: hour || "09",
        minute: minute || "00"
      }
    }
    return { hour: "09", minute: "00" }
  }

  const initialTime = getInitialTime()
  const [startHour, setStartHour] = useState(initialTime.hour)
  const [startMinute, setStartMinute] = useState(initialTime.minute)
  const [useEndTime, setUseEndTime] = useState(false)
  const [endHour, setEndHour] = useState("10")
  const [endMinute, setEndMinute] = useState("00")
  const [duration, setDuration] = useState("30")

  const [notes, setNotes] = useState("")
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [patientPickerOpen, setPatientPickerOpen] = useState(false)
  const [showPastTimeConfirm, setShowPastTimeConfirm] = useState(false)

  // Load patients and procedure types when dialog opens
  useEffect(() => {
    if (open) {
      loadPatients()
      loadProcedureTypes()
    }
  }, [open])

  // When opened to schedule a plan step, fix the patient and record the act name in the notes.
  useEffect(() => {
    if (open && isPlanScheduling) {
      setIsBusySlot(false)
      setIsNewPatient(false)
      if (presetPatientId) setSelectedPatientId(presetPatientId)
      if (presetProcedureName) setAppointmentType(presetProcedureName)
    }
  }, [open, isPlanScheduling, presetPatientId, presetProcedureName])

  // Update date and time when defaultDate or defaultTime changes (when dialog is open)
  useEffect(() => {
    if (open && (defaultDate || defaultTime)) {
      if (defaultDate) {
        // Set date (without time for the date picker)
        const dateOnly = new Date(defaultDate)
        dateOnly.setHours(0, 0, 0, 0)
        setDate(dateOnly)
        // Set time from defaultDate
        setStartHour(String(defaultDate.getHours()).padStart(2, "0"))
        setStartMinute(String(defaultDate.getMinutes()).padStart(2, "0"))
      } else if (defaultTime) {
        const [hour, minute] = defaultTime.split(":")
        setStartHour(hour || "09")
        setStartMinute(minute || "00")
      }
    }
  }, [open, defaultDate, defaultTime])

  // Reset form when dialog closes
  useEffect(() => {
    if (!open) {
      setIsNewPatient(false)
      setSelectedPatientId("")
      setNewPatientFirstName("")
      setNewPatientLastName("")
      setSelectedDoctorId("")
      setAppointmentType("")
      setSelectedProcedureTypeId(undefined)
      setCustomProcedureMode(false)
      setCustomProcedureName("")
      setCustomProcedureDuration("")
      setCustomProcedureCost("")
      setCustomProcedureError(null)
      const initialTime = getInitialTime()
      setStartHour(initialTime.hour)
      setStartMinute(initialTime.minute)
      setUseEndTime(false)
      setEndHour("10")
      setEndMinute("00")
      setDuration("30")
      setNotes("")
      setError(null)
      setPatientPickerOpen(false)
      setShowPastTimeConfirm(false)
      // Reset date to defaultDate or new Date
      setDate(defaultDate || new Date())
    }
  }, [open])

  const loadPatients = async () => {
    try {
      setLoadingPatients(true)
      const data = await patientsApi.list()
      setPatients(data)
    } catch (err) {
      console.error("Failed to load patients:", err)
      if (err instanceof ApiError) {
        setError(`Failed to load patients: ${err.message}`)
      } else {
        setError("Failed to load patients. Please try again.")
      }
    } finally {
      setLoadingPatients(false)
    }
  }

  const loadProcedureTypes = async () => {
    try {
      setLoadingProcedureTypes(true)
      const data = await procedureTypesApi.list(false) // Only active procedure types
      setProcedureTypes(data || [])
    } catch (err) {
      console.error("Failed to load procedure types:", err)
      // Don't show error to user, just log it - procedure types are optional
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

  // Display name of the currently-selected patient (for the searchable picker trigger).
  const selectedPatientName = useMemo(() => {
    const patient = patients.find((p) => p.id === selectedPatientId)
    return patient ? `${patient.firstName} ${patient.lastName}` : ""
  }, [patients, selectedPatientId])

  // Advisory overlap warning (AC-3): non-blocking, always visible while the dialog is open.
  const overlapWarning = useAppointmentOverlap({
    enabled: open,
    date,
    startHour,
    startMinute,
    durationMinutes: calculatedDuration,
  })

  // Build the appointment start Date from the current date + start time, or null if no date.
  const buildAppointmentDateTime = (): Date | null => {
    if (!date) return null
    const dt = new Date(date)
    dt.setHours(Number.parseInt(startHour), Number.parseInt(startMinute), 0, 0)
    return dt
  }

  // Synchronous validation; sets an error message and returns false on the first failure.
  const validateForm = (): boolean => {
    if (!isBusySlot) {
      if (isNewPatient) {
        if (!newPatientFirstName.trim() || !newPatientLastName.trim()) {
          setError("Please enter both first name and last name for the new patient")
          return false
        }
      } else if (!selectedPatientId) {
        setError("Please select a patient")
        return false
      }
    }

    if (!date) {
      setError("Please select a date")
      return false
    }

    if (useEndTime) {
      const startTotalMinutes = Number.parseInt(startHour) * 60 + Number.parseInt(startMinute)
      const endTotalMinutes = Number.parseInt(endHour) * 60 + Number.parseInt(endMinute)
      if (endTotalMinutes <= startTotalMinutes) {
        setError("End time must be after start time")
        return false
      }
    }

    if (calculatedDuration <= 0) {
      setError("Duration must be greater than 0")
      return false
    }

    return true
  }

  // Performs the actual create (patient creation + appointment). Called directly, or from the
  // past-time confirmation dialog once the user confirms (AC-2).
  const performCreate = async () => {
    setError(null)
    setLoading(true)

    try {
      let patientId: string | null = null

      // Create new patient if needed (only if not a busy slot)
      if (!isBusySlot) {
        patientId = selectedPatientId
        if (isNewPatient) {
          try {
            const newPatient = await patientsApi.create({
              firstName: newPatientFirstName.trim(),
              lastName: newPatientLastName.trim(),
            })
            patientId = newPatient.id
          } catch (err) {
            if (err instanceof ApiError) {
              setError(`Failed to create patient: ${err.message}`)
            } else {
              setError("Failed to create patient")
            }
            setLoading(false)
            return
          }
        }
      }

      const appointmentDateTime = buildAppointmentDateTime()
      if (!appointmentDateTime) {
        setError("Please select a date")
        setLoading(false)
        return
      }

      // Combine appointment type and notes if both exist
      let appointmentNotes = notes.trim()
      if (appointmentType && notes.trim()) {
        appointmentNotes = `Type: ${appointmentType}\n${notes.trim()}`
      } else if (appointmentType) {
        appointmentNotes = `Type: ${appointmentType}`
      }

      // Create appointment
      await appointmentsApi.create({
        patientId,
        appointmentDateTime: appointmentDateTime.toISOString(),
        durationMinutes: calculatedDuration,
        doctorId: selectedDoctorId || undefined,
        notes: appointmentNotes || undefined,
        procedureTypeId: isBusySlot ? undefined : selectedProcedureTypeId,
        treatmentPlanId: isPlanScheduling ? presetPlanId : undefined,
        treatmentPlanItemId: isPlanScheduling ? presetPlanItemId : undefined,
      })

      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.message)
      } else {
        setError("Failed to create appointment")
      }
    } finally {
      setLoading(false)
    }
  }

  // Create a procedure on the fly from the custom-procedure panel, persist it to the catalog, and select
  // it. Typical duration is optional — when omitted, the appointment's current duration is used as the
  // procedure's default; when provided, selecting the procedure updates the appointment duration (via the
  // procedure-selection effect, so we drop end-time mode to let the preset duration take effect).
  const handleCreateCustomProcedure = async () => {
    setCustomProcedureError(null)
    const name = customProcedureName.trim()
    if (!name) {
      setCustomProcedureError("Le nom de la procédure est requis")
      return
    }
    // Duplicate names are rejected by the backend (unique per clinic). Catch the common case up front
    // with a clear message instead of a round-trip 400.
    const existing = procedureTypes.find((pt) => pt.name.trim().toLowerCase() === name.toLowerCase())
    if (existing) {
      setCustomProcedureError(`Une procédure nommée « ${existing.name} » existe déjà. Choisissez-la dans la liste ou utilisez un autre nom.`)
      return
    }
    const typedDuration = customProcedureDuration ? Number(customProcedureDuration) : NaN
    const inferred = Number.isFinite(typedDuration) && typedDuration > 0 ? Math.floor(typedDuration) : calculatedDuration
    const durationMinutes = Math.min(479, Math.max(1, inferred || 30))
    const cost = customProcedureCost ? Number.parseFloat(customProcedureCost) : null
    if (cost !== null && (Number.isNaN(cost) || cost < 0)) {
      setCustomProcedureError("Le montant est invalide")
      return
    }

    setCreatingCustomProcedure(true)
    try {
      const colorHex = CUSTOM_PROCEDURE_COLORS[procedureTypes.length % CUSTOM_PROCEDURE_COLORS.length]
      const created = await procedureTypesApi.create({ name, defaultDurationMinutes: durationMinutes, defaultCost: cost, colorHex })
      setProcedureTypes((prev) => [...prev, created])
      setUseEndTime(false)
      setSelectedProcedureTypeId(created.id)
      setCustomProcedureMode(false)
      setCustomProcedureName("")
      setCustomProcedureDuration("")
      setCustomProcedureCost("")
    } catch (err) {
      const message = err instanceof ApiError ? err.message : ""
      // Backend also enforces uniqueness (e.g. an inactive procedure with the same name) — surface it in French.
      if (/already exists|existe déjà/i.test(message)) {
        setCustomProcedureError(`Une procédure nommée « ${name} » existe déjà. Choisissez-la dans la liste ou utilisez un autre nom.`)
      } else {
        setCustomProcedureError(message || "Échec de la création de la procédure")
      }
    } finally {
      setCreatingCustomProcedure(false)
    }
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)

    if (!validateForm()) return

    // Past-time guard (AC-2): warn before creating an appointment in the past; the form stays intact
    // so the user can cancel and adjust. Confirming proceeds via performCreate(). Floor "now" to the
    // current minute so booking the current slot (buildAppointmentDateTime zeroes seconds) isn't
    // treated as past.
    const appointmentDateTime = buildAppointmentDateTime()
    const nowFloored = new Date()
    nowFloored.setSeconds(0, 0)
    if (appointmentDateTime && appointmentDateTime.getTime() < nowFloored.getTime()) {
      setShowPastTimeConfirm(true)
      return
    }

    await performCreate()
  }

  return (
    <>
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle className="text-2xl">Create Appointment</DialogTitle>
          <DialogDescription>Schedule a new appointment for a patient</DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-6 mt-4">
          {/* Patient Section */}
          <div className="space-y-4 p-4 rounded-lg border bg-muted/30">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                <User className="h-5 w-5 text-muted-foreground" />
                <h3 className="font-semibold">Patient</h3>
              </div>
              {!isPlanScheduling && (
                <div className="flex items-center gap-2">
                  <span className="text-sm text-muted-foreground">Créneau occupé</span>
                  <Switch
                    checked={isBusySlot}
                    onCheckedChange={(checked) => {
                      setIsBusySlot(checked)
                      if (checked) {
                        setIsNewPatient(false)
                        setSelectedPatientId("")
                        setNewPatientFirstName("")
                        setNewPatientLastName("")
                        setSelectedProcedureTypeId(undefined)
                        setCustomProcedureMode(false)
                      }
                    }}
                  />
                </div>
              )}
            </div>

            {isPlanScheduling ? (
              <div className="space-y-2">
                <div className="rounded-md border bg-background p-3">
                  <p className="text-sm font-medium">{presetPatientName ?? "Patient"}</p>
                  <Badge variant="secondary" className="mt-1 gap-1 text-xs">
                    <Stethoscope className="h-3 w-3" /> Acte du plan{presetProcedureName ? ` : ${presetProcedureName}` : ""}
                  </Badge>
                </div>
              </div>
            ) : !isBusySlot ? (
              <>
                <div className="flex items-center justify-end gap-2">
                  <span className="text-sm text-muted-foreground">Nouveau patient</span>
                  <Switch
                    checked={isNewPatient}
                    onCheckedChange={(checked) => {
                      setIsNewPatient(checked)
                      if (checked) {
                        setSelectedPatientId("")
                      } else {
                        setNewPatientFirstName("")
                        setNewPatientLastName("")
                      }
                    }}
                  />
                </div>

                {isNewPatient ? (
                  <div className="grid grid-cols-2 gap-3">
                    <div className="space-y-2">
                      <Label htmlFor="firstName" className="text-sm">
                        First Name *
                      </Label>
                      <Input
                        id="firstName"
                        placeholder="John"
                        value={newPatientFirstName}
                        onChange={(e) => setNewPatientFirstName(e.target.value)}
                        className="h-10"
                        required
                      />
                    </div>
                    <div className="space-y-2">
                      <Label htmlFor="lastName" className="text-sm">
                        Last Name *
                      </Label>
                      <Input
                        id="lastName"
                        placeholder="Doe"
                        value={newPatientLastName}
                        onChange={(e) => setNewPatientLastName(e.target.value)}
                        className="h-10"
                        required
                      />
                    </div>
                  </div>
                ) : (
                  <div className="space-y-2">
                    <Label htmlFor="patient" className="text-sm">
                      Select Patient *
                    </Label>
                    {/* Searchable patient picker (AC-5): type to filter patients by name. */}
                    {/* modal: the parent Dialog is modal and disables pointer events outside its
                        content; a non-modal Popover portals its list to <body> and inherits
                        pointer-events:none, so suggestions can't be clicked (only keyboard-selected).
                        modal makes the Popover manage its own pointer layer so clicks work. */}
                    <Popover open={patientPickerOpen} onOpenChange={setPatientPickerOpen} modal>
                      <PopoverTrigger asChild>
                        <Button
                          id="patient"
                          type="button"
                          variant="outline"
                          role="combobox"
                          aria-expanded={patientPickerOpen}
                          disabled={loadingPatients || loading}
                          className="w-full h-10 justify-between font-normal"
                        >
                          <span className={cn("truncate", !selectedPatientId && "text-muted-foreground")}>
                            {selectedPatientName ||
                              (loadingPatients
                                ? "Loading patients..."
                                : patients.length === 0
                                  ? "No patients found"
                                  : "Choose a patient...")}
                          </span>
                          <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
                        </Button>
                      </PopoverTrigger>
                      <PopoverContent className="p-0" align="start" style={{ width: "var(--radix-popover-trigger-width)" }}>
                        <Command>
                          <CommandInput placeholder="Rechercher un patient..." />
                          <CommandList>
                            <CommandEmpty>Aucun patient trouvé.</CommandEmpty>
                            <CommandGroup>
                              {patients.map((patient) => {
                                const fullName = `${patient.firstName} ${patient.lastName}`
                                return (
                                  <CommandItem
                                    key={patient.id}
                                    value={fullName}
                                    onSelect={() => {
                                      setSelectedPatientId(patient.id)
                                      setPatientPickerOpen(false)
                                    }}
                                  >
                                    <Check
                                      className={cn(
                                        "mr-2 h-4 w-4",
                                        selectedPatientId === patient.id ? "opacity-100" : "opacity-0",
                                      )}
                                    />
                                    {fullName}
                                  </CommandItem>
                                )
                              })}
                            </CommandGroup>
                          </CommandList>
                        </Command>
                      </PopoverContent>
                    </Popover>
                    {patients.length === 0 && !loadingPatients && (
                      <p className="text-xs text-muted-foreground">Create a new patient using the toggle above</p>
                    )}
                  </div>
                )}
              </>
            ) : (
              <div className="p-3 rounded-lg bg-amber-50 dark:bg-amber-950 border border-amber-200 dark:border-amber-800">
                <p className="text-sm text-amber-800 dark:text-amber-200">
                  Ce créneau sera marqué comme occupé. Aucun patient ne pourra être assigné à cette période.
                </p>
              </div>
            )}
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
                    >
                      <CalendarIcon className="mr-2 h-4 w-4" />
                      {date ? format(date, "PPP") : "Pick a date"}
                    </Button>
                  </PopoverTrigger>
                  <PopoverContent className="w-auto p-0" align="start">
                    <Calendar mode="single" selected={date} onSelect={setDate} initialFocus />
                  </PopoverContent>
                </Popover>
              </div>

              {/* Start Time */}
              <div className="space-y-2">
                <Label className="text-sm">Start Time *</Label>
                <div className="flex gap-2">
                  <Select value={startHour} onValueChange={setStartHour} required>
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
                  <Select value={startMinute} onValueChange={setStartMinute} required>
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
                <span className="text-sm font-medium">Duration</span>
                {calculatedDuration > 0 && (
                  <Badge variant="secondary" className="ml-2">
                    {durationDisplay}
                  </Badge>
                )}
              </div>
              <div className="flex items-center gap-2">
                <span className="text-sm text-muted-foreground">Set end time</span>
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
                />
              </div>
            </div>

            {/* Duration or End Time Input */}
            <div className="space-y-2">
              {useEndTime ? (
                <div className="space-y-2">
                  <Label className="text-sm">End Time *</Label>
                  <div className="flex gap-2">
                    <Select value={endHour} onValueChange={setEndHour} required>
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
                    <Select value={endMinute} onValueChange={setEndMinute} required>
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
                    >
                      {mins < 60 ? `${mins}m` : `${mins / 60}h`}
                    </Button>
                  ))}
                </div>
              )}
            </div>

            {/* Overlap warning (AC-3): non-blocking amber text naming the conflicting appointment. */}
            {overlapWarning && (
              <p className="text-sm text-amber-600 dark:text-amber-400">⚠ {overlapWarning}</p>
            )}
          </div>

          {/* Additional Details Section */}
          <div className="space-y-4 p-4 rounded-lg border bg-muted/30">
            <div className="flex items-center gap-2">
              <Stethoscope className="h-5 w-5 text-muted-foreground" />
              <h3 className="font-semibold">Details</h3>
            </div>

            <div className={`grid gap-4 ${!isBusySlot ? 'grid-cols-1 md:grid-cols-2' : 'grid-cols-1'}`}>
              <div className="space-y-2">
                <Label htmlFor="doctor" className="text-sm">
                  Doctor
                </Label>
                <Select
                  value={selectedDoctorId}
                  onValueChange={setSelectedDoctorId}
                  disabled={loadingDoctors || loading}
                >
                  <SelectTrigger className="h-10 w-full" id="doctor">
                    <SelectValue placeholder={loadingDoctors ? "Loading doctors..." : doctors.length === 0 ? "No doctors found" : "Choose a doctor..."} />
                  </SelectTrigger>
                  <SelectContent className="max-h-[200px]">
                    {doctors.length === 0 && !loadingDoctors ? (
                      <div className="px-2 py-1.5 text-sm text-muted-foreground">No doctors available</div>
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

              {!isBusySlot && (
                <div className="space-y-2">
                  <Label htmlFor="procedureType" className="text-sm">
                    Procedure Type
                  </Label>
                  <Select
                    value={selectedProcedureTypeId}
                    onValueChange={(v) => {
                      if (v === CUSTOM_PROCEDURE_VALUE) {
                        // Open the inline creation panel instead of selecting a real procedure.
                        setCustomProcedureMode(true)
                        setCustomProcedureError(null)
                      } else if (v) {
                        // Ignore the spurious empty-string reset Radix Select fires right after a value is
                        // set to a just-added item (before its internal collection registers it) — writing
                        // that "" back would clear the freshly created custom procedure. Real ids are never "".
                        setSelectedProcedureTypeId(v)
                      }
                    }}
                    disabled={loadingProcedureTypes}
                  >
                    <SelectTrigger id="procedureType" className="h-10 w-full">
                      <SelectValue placeholder={loadingProcedureTypes ? "Loading..." : "Select procedure type"} />
                    </SelectTrigger>
                    <SelectContent className="max-h-[200px]">
                      {procedureTypes.map((procedureType) => (
                        <SelectItem key={procedureType.id} value={procedureType.id}>
                          <div className="flex items-center gap-2">
                            <div
                              className="w-3 h-3 rounded-full"
                              style={{ backgroundColor: procedureType.colorHex }}
                            />
                            {procedureType.name} ({procedureType.defaultDurationMinutes} min)
                          </div>
                        </SelectItem>
                      ))}
                      <SelectItem value={CUSTOM_PROCEDURE_VALUE}>
                        <div className="flex items-center gap-2 font-medium">
                          <Plus className="h-3.5 w-3.5" />
                          Procédure personnalisée…
                        </div>
                      </SelectItem>
                    </SelectContent>
                  </Select>

                  {selectedProcedureTypeId && !customProcedureMode && (
                    <div className="flex items-center gap-2 mt-1">
                      <p className="text-xs text-muted-foreground">
                        Duration set to {procedureTypes.find(p => p.id === selectedProcedureTypeId)?.defaultDurationMinutes} minutes (you can change it)
                      </p>
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        className="h-5 px-2 text-xs"
                        onClick={() => setSelectedProcedureTypeId(undefined)}
                      >
                        Clear
                      </Button>
                    </div>
                  )}
                </div>
              )}
            </div>

            {/* Custom-procedure creation — full width below the row so it isn't cramped in one column. */}
            {!isBusySlot && customProcedureMode && (
              <div className="rounded-md border bg-background p-3 space-y-3">
                <p className="text-sm font-medium">Nouvelle procédure personnalisée</p>
                {customProcedureError && (
                  <p className="text-xs text-red-600 dark:text-red-400">{customProcedureError}</p>
                )}
                <div className="grid gap-3 sm:grid-cols-[1fr_120px_140px]">
                  <div className="space-y-1">
                    <Label className="text-xs text-muted-foreground">Nom *</Label>
                    <Input
                      value={customProcedureName}
                      onChange={(e) => setCustomProcedureName(e.target.value)}
                      onKeyDown={(e) => { if (e.key === "Enter") { e.preventDefault(); handleCreateCustomProcedure() } }}
                      placeholder="Nom de la procédure"
                      className="h-9"
                      disabled={creatingCustomProcedure}
                      autoFocus
                    />
                  </div>
                  <div className="space-y-1">
                    <Label className="text-xs text-muted-foreground">Durée (min)</Label>
                    <Input
                      type="number"
                      min="1"
                      max="479"
                      value={customProcedureDuration}
                      onChange={(e) => setCustomProcedureDuration(e.target.value)}
                      onKeyDown={(e) => { if (e.key === "Enter") { e.preventDefault(); handleCreateCustomProcedure() } }}
                      placeholder="auto"
                      className="h-9"
                      disabled={creatingCustomProcedure}
                    />
                  </div>
                  <div className="space-y-1">
                    <Label className="text-xs text-muted-foreground">Montant</Label>
                    <div className="relative">
                      <span className="absolute left-2 top-1/2 -translate-y-1/2 text-xs text-muted-foreground">DT</span>
                      <Input
                        type="number"
                        min="0"
                        step="0.01"
                        value={customProcedureCost}
                        onChange={(e) => setCustomProcedureCost(e.target.value)}
                        onKeyDown={(e) => { if (e.key === "Enter") { e.preventDefault(); handleCreateCustomProcedure() } }}
                        placeholder="0.00"
                        className="h-9 pl-8"
                        disabled={creatingCustomProcedure}
                      />
                    </div>
                  </div>
                </div>
                <div className="flex items-center justify-between gap-3">
                  <p className="text-[11px] text-muted-foreground">
                    Durée et montant optionnels. Sans durée, celle du rendez-vous ({calculatedDuration} min) est utilisée.
                  </p>
                  <div className="flex flex-shrink-0 gap-2">
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      className="h-8"
                      onClick={() => { setCustomProcedureMode(false); setCustomProcedureError(null) }}
                      disabled={creatingCustomProcedure}
                    >
                      Annuler
                    </Button>
                    <Button
                      type="button"
                      size="sm"
                      className="h-8"
                      onClick={handleCreateCustomProcedure}
                      disabled={creatingCustomProcedure}
                    >
                      {creatingCustomProcedure ? "Ajout…" : "Ajouter"}
                    </Button>
                  </div>
                </div>
              </div>
            )}
          </div>

          {/* Notes Section */}
          <div className="space-y-4 p-4 rounded-lg border bg-muted/30">
            <div className="flex items-center gap-2">
              <FileText className="h-5 w-5 text-muted-foreground" />
              <h3 className="font-semibold">Notes</h3>
            </div>
            <Textarea
              placeholder="Add any additional notes or special instructions..."
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              className="min-h-[80px] resize-none"
            />
          </div>

          {/* Validation error shown next to the submit button — the dialog is tall and scrollable, so an
              error at the top would be off-screen when the user submits from the bottom. */}
          {error && (
            <div className="rounded-lg bg-red-50 border border-red-200 p-3 text-sm text-red-800 dark:bg-red-950 dark:border-red-800 dark:text-red-200">
              {error}
            </div>
          )}

          <DialogFooter className="gap-2 pt-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={loading}>
              Cancel
            </Button>
            <Button type="submit" disabled={loading}>
              {loading ? "Creating..." : "Create Appointment"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>

    {/* Past-time confirmation (AC-2): blocking; confirming proceeds, cancelling leaves the form intact. */}
    <AlertDialog open={showPastTimeConfirm} onOpenChange={setShowPastTimeConfirm}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Heure dans le passé</AlertDialogTitle>
          <AlertDialogDescription>
            L&apos;heure sélectionnée est déjà passée. Voulez-vous quand même créer ce rendez-vous ?
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={loading}>Annuler</AlertDialogCancel>
          <AlertDialogAction
            onClick={() => {
              setShowPastTimeConfirm(false)
              performCreate()
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
