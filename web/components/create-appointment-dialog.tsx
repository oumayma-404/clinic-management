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
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Calendar } from "@/components/ui/calendar"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Switch } from "@/components/ui/switch"
import { Textarea } from "@/components/ui/textarea"
import { Badge } from "@/components/ui/badge"
import { format } from "date-fns"
import { CalendarIcon, Clock, User, Stethoscope, FileText } from "lucide-react"
import { cn } from "@/lib/utils"
import { appointmentsApi } from "@/lib/api/appointments"
import { patientsApi } from "@/lib/api/patients"
import type { PatientDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"

interface CreateAppointmentDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  defaultDate?: Date
  defaultTime?: string
  onSuccess?: () => void
}

export function CreateAppointmentDialog({
  open,
  onOpenChange,
  defaultDate,
  defaultTime,
  onSuccess,
}: CreateAppointmentDialogProps) {
  // Patient state
  const [isNewPatient, setIsNewPatient] = useState(false)
  const [selectedPatientId, setSelectedPatientId] = useState("")
  const [newPatientFirstName, setNewPatientFirstName] = useState("")
  const [newPatientLastName, setNewPatientLastName] = useState("")
  const [patients, setPatients] = useState<PatientDto[]>([])
  const [loadingPatients, setLoadingPatients] = useState(false)

  // Appointment details
  const [date, setDate] = useState<Date | undefined>(defaultDate || new Date())
  const [doctorName, setDoctorName] = useState("")
  const [appointmentType, setAppointmentType] = useState("")

  // Time state
  const [startHour, setStartHour] = useState(defaultTime ? defaultTime.split(":")[0] : "09")
  const [startMinute, setStartMinute] = useState(defaultTime ? defaultTime.split(":")[1] || "00" : "00")
  const [useEndTime, setUseEndTime] = useState(false)
  const [endHour, setEndHour] = useState("10")
  const [endMinute, setEndMinute] = useState("00")
  const [duration, setDuration] = useState("30")

  const [notes, setNotes] = useState("")
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Load patients when dialog opens
  useEffect(() => {
    if (open) {
      loadPatients()
    }
  }, [open])

  // Reset form when dialog closes
  useEffect(() => {
    if (!open) {
      setIsNewPatient(false)
      setSelectedPatientId("")
      setNewPatientFirstName("")
      setNewPatientLastName("")
      setDoctorName("")
      setAppointmentType("")
      setStartHour(defaultTime ? defaultTime.split(":")[0] : "09")
      setStartMinute(defaultTime ? defaultTime.split(":")[1] || "00" : "00")
      setUseEndTime(false)
      setEndHour("10")
      setEndMinute("00")
      setDuration("30")
      setNotes("")
      setError(null)
    }
  }, [open, defaultTime])

  const loadPatients = async () => {
    try {
      setLoadingPatients(true)
      const data = await patientsApi.list({ limit: 100 })
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

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)
    setLoading(true)

    try {
      // Validate patient
      if (isNewPatient) {
        if (!newPatientFirstName.trim() || !newPatientLastName.trim()) {
          setError("Please enter both first name and last name for the new patient")
          setLoading(false)
          return
        }
      } else if (!selectedPatientId) {
        setError("Please select a patient")
        setLoading(false)
        return
      }

      // Validate date
      if (!date) {
        setError("Please select a date")
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

      let patientId = selectedPatientId

      // Create new patient if needed
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

      // Create appointment date time
      const appointmentDateTime = new Date(date)
      appointmentDateTime.setHours(Number.parseInt(startHour), Number.parseInt(startMinute), 0, 0)

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
        doctorName: doctorName.trim() || undefined,
        notes: appointmentNotes || undefined,
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

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle className="text-2xl">Create Appointment</DialogTitle>
          <DialogDescription>Schedule a new appointment for a patient</DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-6 mt-4">
          {error && <div className="rounded-lg bg-red-50 border border-red-200 p-3 text-sm text-red-800 dark:bg-red-950 dark:border-red-800 dark:text-red-200">{error}</div>}

          {/* Patient Section */}
          <div className="space-y-4 p-4 rounded-lg border bg-muted/30">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                <User className="h-5 w-5 text-muted-foreground" />
                <h3 className="font-semibold">Patient</h3>
              </div>
              <div className="flex items-center gap-2">
                <span className="text-sm text-muted-foreground">New patient</span>
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
                <Select 
                  value={selectedPatientId} 
                  onValueChange={setSelectedPatientId}
                  disabled={loadingPatients || loading}
                  required
                >
                  <SelectTrigger id="patient" className="h-10">
                    <SelectValue placeholder={loadingPatients ? "Loading patients..." : patients.length === 0 ? "No patients found" : "Choose a patient..."} />
                  </SelectTrigger>
                  <SelectContent className="max-h-[200px]">
                    {patients.length === 0 && !loadingPatients ? (
                      <div className="px-2 py-1.5 text-sm text-muted-foreground">No patients available</div>
                    ) : (
                      patients.map((patient) => (
                        <SelectItem key={patient.id} value={patient.id}>
                          {patient.firstName} {patient.lastName}
                        </SelectItem>
                      ))
                    )}
                  </SelectContent>
                </Select>
                {patients.length === 0 && !loadingPatients && (
                  <p className="text-xs text-muted-foreground">Create a new patient using the toggle above</p>
                )}
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
                <Popover>
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
                <Input
                  id="doctor"
                  placeholder="Dr. Smith"
                  value={doctorName}
                  onChange={(e) => setDoctorName(e.target.value)}
                  className="h-10"
                />
              </div>

              <div className="space-y-2">
                <Label htmlFor="type" className="text-sm">
                  Appointment Type
                </Label>
                <Select value={appointmentType} onValueChange={setAppointmentType}>
                  <SelectTrigger id="type" className="h-10">
                    <SelectValue placeholder="Select type..." />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="consultation">Consultation</SelectItem>
                    <SelectItem value="checkup">General Checkup</SelectItem>
                    <SelectItem value="followup">Follow-up</SelectItem>
                    <SelectItem value="procedure">Procedure</SelectItem>
                    <SelectItem value="lab">Lab Results</SelectItem>
                    <SelectItem value="emergency">Emergency</SelectItem>
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
            />
          </div>

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
  )
}
