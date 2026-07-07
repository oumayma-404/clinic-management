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
import { Calendar } from "@/components/ui/calendar"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Switch } from "@/components/ui/switch"
import { Textarea } from "@/components/ui/textarea"
import { Badge } from "@/components/ui/badge"
import { format, parseISO } from "date-fns"
import { CalendarIcon, Clock, User, Stethoscope, FileText, X, Save } from "lucide-react"
import { cn } from "@/lib/utils"
import { appointmentsApi } from "@/lib/api/appointments"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import type { AppointmentDto, ProcedureTypeDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { useDoctors } from "@/lib/hooks/use-doctors"

interface EditAppointmentDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  appointment: AppointmentDto | null
  onSuccess?: () => void
}

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

  // Populate form when appointment changes
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
  }, [appointment, open])

  // Reset form when dialog closes
  useEffect(() => {
    if (!open) {
      setError(null)
      setUseEndTime(false)
      setShowCancelDialog(false)
    }
  }, [open])

  const handleUpdate = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)
    setLoading(true)

    try {
      if (!appointment) {
        setError("No appointment selected")
        setLoading(false)
        return
      }

      // Validate time
      if (useEndTime) {
        const startTotalMinutes = Number.parseInt(startHour) * 60 + Number.parseInt(startMinute)
        const endTotalMinutes = Number.parseInt(endHour) * 60 + Number.parseInt(endMinute)
        if (endTotalMinutes <= startTotalMinutes) {
          setError("End time must be after start time")
          setLoading(false)
          return
        }
      }

      if (calculatedDuration <= 0) {
        setError("Duration must be greater than 0")
        setLoading(false)
        return
      }

      // Validate date
      if (!date) {
        setError("Please select a date")
        setLoading(false)
        return
      }

      // Build appointment date/time
      const appointmentDateTime = new Date(date)
      appointmentDateTime.setHours(Number.parseInt(startHour), Number.parseInt(startMinute), 0, 0)

      // Combine appointment type and notes
      let appointmentNotes = notes.trim()
      if (appointmentType && notes.trim()) {
        appointmentNotes = `Type: ${appointmentType}\n${notes.trim()}`
      } else if (appointmentType) {
        appointmentNotes = `Type: ${appointmentType}`
      }

      // Update appointment via API
      await appointmentsApi.update(appointment.id, {
        appointmentDateTime: appointmentDateTime.toISOString(),
        durationMinutes: calculatedDuration,
        doctorId: selectedDoctorId || undefined,
        notes: appointmentNotes || undefined,
        status: status,
        procedureTypeId: selectedProcedureTypeId || null,
      })

      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.message)
      } else {
        setError("Failed to update appointment")
      }
    } finally {
      setLoading(false)
    }
  }

  const handleCancelAppointment = async () => {
    if (!appointment) return

    setLoading(true)
    try {
      await appointmentsApi.update(appointment.id, {
        status: "cancelled",
      })

      setShowCancelDialog(false)
      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.message)
      } else {
        setError("Failed to cancel appointment")
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
                <DialogTitle className="text-2xl">Edit Appointment</DialogTitle>
                <DialogDescription>Update appointment details or change status</DialogDescription>
              </div>
              <Badge className={cn("border", statusColors[status] || statusColors.scheduled)}>
                {statusDisplay}
              </Badge>
            </div>
          </DialogHeader>

          <form onSubmit={handleUpdate} className="space-y-6 mt-4">
            {error && (
              <div className="rounded-lg bg-red-50 border border-red-200 p-3 text-sm text-red-800 dark:bg-red-950 dark:border-red-800 dark:text-red-200">
                {error}
              </div>
            )}

            {/* Patient Section */}
            <div className="space-y-4 p-4 rounded-lg border bg-muted/30">
              <div className="flex items-center gap-2">
                <User className="h-5 w-5 text-muted-foreground" />
                <h3 className="font-semibold">Patient Information</h3>
              </div>

              <div className="space-y-2">
                <Label htmlFor="patientName" className="text-sm">
                  Patient Name
                </Label>
                <Input
                  id="patientName"
                  value={patientName}
                  onChange={(e) => setPatientName(e.target.value)}
                  className="h-10"
                  disabled
                />
                <p className="text-xs text-muted-foreground">Patient name cannot be changed</p>
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
                  <Popover>
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
                    disabled={loading}
                  />
                </div>
              </div>

              {/* Duration or End Time Input */}
              <div className="space-y-2">
                {useEndTime ? (
                  <div className="space-y-2">
                    <Label className="text-sm">End Time *</Label>
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
            </div>

            {/* Additional Details Section */}
            <div className="space-y-4 p-4 rounded-lg border bg-muted/30">
              <div className="flex items-center gap-2">
                <Stethoscope className="h-5 w-5 text-muted-foreground" />
                <h3 className="font-semibold">Details</h3>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div className="space-y-2">
                  <Label htmlFor="doctor" className="text-sm">
                    Doctor
                  </Label>
                  <Select
                    value={selectedDoctorId}
                    onValueChange={setSelectedDoctorId}
                    disabled={loadingDoctors || loading}
                  >
                    <SelectTrigger className="h-10" id="doctor">
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

                <div className="space-y-2">
                  <Label htmlFor="procedureType" className="text-sm">
                    Procedure Type
                  </Label>
                  <Select 
                    value={selectedProcedureTypeId} 
                    onValueChange={setSelectedProcedureTypeId}
                    disabled={loadingProcedureTypes || loading}
                  >
                    <SelectTrigger id="procedureType" className="h-10">
                      <SelectValue placeholder={loadingProcedureTypes ? "Loading..." : "Select procedure type"} />
                    </SelectTrigger>
                    <SelectContent className="max-h-[200px]">
                      {procedureTypes.length === 0 && !loadingProcedureTypes ? (
                        <div className="px-2 py-1.5 text-sm text-muted-foreground">No procedure types available</div>
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
                        Duration set to {procedureTypes.find(p => p.id === selectedProcedureTypeId)?.defaultDurationMinutes} minutes (you can change it)
                      </p>
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        className="h-5 px-2 text-xs"
                        onClick={() => setSelectedProcedureTypeId(undefined)}
                        disabled={loading}
                      >
                        Clear
                      </Button>
                    </div>
                  )}
                </div>

                <div className="space-y-2 md:col-span-2">
                  <Label htmlFor="status" className="text-sm">
                    Status
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
                      <SelectItem value="scheduled">Scheduled</SelectItem>
                      <SelectItem value="confirmed">Confirmed</SelectItem>
                      <SelectItem value="inprogress">In Progress</SelectItem>
                      <SelectItem value="completed">Completed</SelectItem>
                      <SelectItem value="cancelled">Cancelled</SelectItem>
                      <SelectItem value="noshow">No Show</SelectItem>
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
                placeholder="Add any additional notes or special instructions..."
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
              <Button type="submit" disabled={loading}>
                <Save className="h-4 w-4 mr-2" />
                {loading ? "Saving..." : "Save Changes"}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>

      {/* Cancel Appointment Confirmation Dialog */}
      <AlertDialog open={showCancelDialog} onOpenChange={setShowCancelDialog}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Cancel Appointment?</AlertDialogTitle>
            <AlertDialogDescription>
              Are you sure you want to cancel this appointment with {patientName}? This action can be undone by changing
              the status back to scheduled.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={loading}>No, keep it</AlertDialogCancel>
            <AlertDialogAction
              onClick={handleCancelAppointment}
              disabled={loading}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              {loading ? "Cancelling..." : "Yes, cancel appointment"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}


