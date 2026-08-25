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
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from "@/components/ui/command"
import { Calendar } from "@/components/ui/calendar"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Textarea } from "@/components/ui/textarea"
import { Badge } from "@/components/ui/badge"
import { TimeField } from "@/components/ui/time-field"
import { format } from "date-fns"
import { CalendarIcon, Stethoscope, FileText, Check, ChevronsUpDown, ChevronDown } from "lucide-react"
import { cn } from "@/lib/utils"
import { AppointmentRecap, type AppointmentRecapModel } from "@/components/appointment-recap"
import { ModeSegmented } from "@/components/ui/mode-segmented"
import { appointmentsApi } from "@/lib/api/appointments"
import { patientsApi } from "@/lib/api/patients"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import { AppointmentActsPicker, totalActsDuration, type SelectedAct } from "@/components/appointment-acts-picker"
import { getErrorMessage } from "@/lib/errors"
import type { PatientDto, ProcedureTypeDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { useDoctors } from "@/lib/hooks/use-doctors"
import { useAppointmentOverlap } from "@/lib/hooks/use-appointment-overlap"
import { ApiErrorCode } from "@/lib/api/client"
import { isDeliverablePhone, PHONE_ERROR_FR } from "@/lib/phone"
import { specialtyLabel } from "@/lib/specialties"

/**
 * A treatment-plan act offered for booking, as the plan workspace hands it over.
 *
 * <p>An array rather than the old single `presetPlanItemId`, because grouping is the point: « ces deux actes dans
 * la même séance » is one dialog opening with two entries, and « séparément » is two openings with one each. The
 * caller decides which — this dialog just books what it is given.</p>
 */
export interface PresetPlanAct {
  planItemId: string
  /** The catalog act it stands for, when the workspace could resolve one. */
  procedureTypeId?: string
  /** Désignation, for the « devis » chip and the header summary. */
  label: string
}

/**
 * The three refusals this dialog can talk the user through, and whether they have already said yes to each.
 *
 * <p>One object rather than three positional booleans on `performCreate`, because they compose: a booking can be
 * double-booked *and* out of hours *and* for a patient who looks like somebody already on file, and each
 * confirmation re-submits. Merging a partial grant into what was already given is what keeps the sequence from
 * looping back to a question the user has answered.</p>
 */
type CreateOverrides = {
  /** `allowOutsideWorkingHours` — the practitioner is closed then, and the user accepts the exception. */
  hours: boolean
  /** `allowOverlap` — the slot already holds a booking (a second chair, an emergency squeezed in). */
  overlap: boolean
  /** `allowDuplicate` on the patient create — a genuine namesake, not the patient already on file. */
  duplicatePatient: boolean
}

const NO_OVERRIDES: CreateOverrides = { hours: false, overlap: false, duplicatePatient: false }

/**
 * The offered visit lengths. Named because the row now also has to answer « is the current duration one of
 * these? » — a summed act list routinely is not, and before this that case highlighted nothing at all.
 */
const DURATION_PRESETS = [15, 30, 45, 60, 90, 120]

interface CreateAppointmentDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  defaultDate?: Date
  defaultTime?: string
  /**
   * The visit's length, when the caller already knows it — today only the agenda's drag-across-hours does.
   *
   * ⚠️ **Supplying it also stops the acts' sum from overwriting the field.** A dragged span is an explicit
   * statement about how long the visit is, exactly like typing the number by hand, so it arrives with
   * `durationTouched` already true; without that the first act picked would silently replace the two hours the
   * user just painted with the catalogue's 30 minutes. It is still an ordinary editable field.
   */
  defaultDurationMinutes?: number
  onSuccess?: () => void
  /** Fires with the new appointment's id after a successful create (e.g. waiting-list promote-and-book). */
  onCreated?: (appointmentId: string) => void
  /** Preselect an existing patient when booking from a patient's page ("Planifier un rendez-vous"). */
  defaultPatientId?: string
  /** When scheduling a treatment-plan step ("Planifier"): fixes the patient and links the appointment. */
  presetPatientId?: string
  presetPatientName?: string
  presetPlanId?: string
  /**
   * The plan acts this séance carries out — one entry books a single step, several book them **together in one
   * visit**, each keeping its own devis link so the plan reports all of them as planned.
   */
  presetPlanActs?: PresetPlanAct[]
}

export function CreateAppointmentDialog({
  open,
  onOpenChange,
  defaultDate,
  defaultTime,
  defaultDurationMinutes,
  onSuccess,
  onCreated,
  defaultPatientId,
  presetPatientId,
  presetPatientName,
  presetPlanId,
  presetPlanActs,
}: CreateAppointmentDialogProps) {
  // True when this dialog was opened to schedule treatment-plan steps.
  const planActs = presetPlanActs ?? []
  const isPlanScheduling = planActs.length > 0
  // Patient state
  const [isBusySlot, setIsBusySlot] = useState(false)
  const [isNewPatient, setIsNewPatient] = useState(false)
  const [selectedPatientId, setSelectedPatientId] = useState("")
  const [newPatientFirstName, setNewPatientFirstName] = useState("")
  const [newPatientLastName, setNewPatientLastName] = useState("")
  const [newPatientPhone, setNewPatientPhone] = useState("")
  const [patients, setPatients] = useState<PatientDto[]>([])
  const [loadingPatients, setLoadingPatients] = useState(false)

  // Procedure type state
  const [procedureTypes, setProcedureTypes] = useState<ProcedureTypeDto[]>([])
  /** The acts of this séance — several are the normal case, not the exception. */
  const [selectedActs, setSelectedActs] = useState<SelectedAct[]>([])
  const [loadingProcedureTypes, setLoadingProcedureTypes] = useState(false)
  /** Has the catalog fetch SETTLED — loaded or failed? See the plan-act seeding effect. */
  const [procedureTypesLoaded, setProcedureTypesLoaded] = useState(false)
  // AC-P3.31 — why the acte list is empty, so an unreachable server is not mistaken for an empty catalogue.
  const [procedureTypesError, setProcedureTypesError] = useState<string | null>(null)
  /**
   * Has the user set the duration themselves? Until they do, it follows the **sum** of the chosen acts. After they
   * do it is left alone: auto-summing over a hand-typed 45 min would silently undo an explicit decision, and the
   * summed default is a convenience, not a rule.
   *
   * ⚠️ A dragged span starts it **true**. Painting 09:00 → 11:00 on the agenda is the same kind of decision as
   * typing 120, so it must survive picking the acts — otherwise the first act chosen replaces the two hours the
   * user just drew with the catalogue's 30 minutes, which is the one thing that would make the gesture pointless.
   */
  const [durationTouched, setDurationTouched] = useState(defaultDurationMinutes !== undefined)

  // Appointment details
  const [date, setDate] = useState<Date | undefined>(defaultDate || new Date())
  const [selectedDoctorId, setSelectedDoctorId] = useState<string>("")
  // `appointmentType` removed with the `Type: ` prefix (AC-P1.51). It had no input control of its own — it was
  // only ever set from `presetProcedureName` when scheduling a plan act, purely to build that prefix. The act
  // reaches the appointment through `procedureTypeId`, which `presetProcedureTypeId` already preselects.
  
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
  const [duration, setDuration] = useState(defaultDurationMinutes ? String(defaultDurationMinutes) : "30")

  const [notes, setNotes] = useState("")
  /**
   * Is « Notes et options » open? Closed by default: a titled card with an icon for one optional textarea cost as
   * much height as the patient, on the form whose whole problem was that it did not fit.
   */
  const [showNotes, setShowNotes] = useState(false)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [patientPickerOpen, setPatientPickerOpen] = useState(false)
  const [showPastTimeConfirm, setShowPastTimeConfirm] = useState(false)
  // The server's out-of-hours reason (« Dr X : Le cabinet est fermé le samedi. ») while the confirm is open,
  // or null. Holds the message rather than a boolean so the prompt can name the closed period the server
  // actually objected to, instead of a vague « en dehors des horaires ».
  const [outsideHoursPrompt, setOutsideHoursPrompt] = useState<string | null>(null)
  // Double-booking prompt — the collision is advisory, exactly like the out-of-hours one below.
  const [slotTakenPrompt, setSlotTakenPrompt] = useState<string | null>(null)
  /**
   * « Ce patient existe déjà : … » — the server's own refusal while the confirm is open, or null. Same advisory
   * shape as the two above: the message names who was matched and why, so the prompt shows it verbatim instead of
   * inventing a vaguer sentence.
   */
  const [duplicatePatientPrompt, setDuplicatePatientPrompt] = useState<string | null>(null)
  /**
   * Overrides the user has already granted in this submit sequence.
   *
   * <p>Needed because the server checks the collision BEFORE the working hours, so a booking that is both
   * double-booked and out-of-hours prompts twice. Without carrying the first grant forward, confirming the overlap
   * and then confirming the hours would retry with `allowOverlap` lost and prompt for the overlap again — a loop.
   * `performCreate` merges into this rather than replacing it, so each grant survives the next prompt; a fresh
   * `handleSubmit` resets it, because a re-submit after editing the time deserves to be asked again.</p>
   */
  const grantedOverridesRef = useRef<CreateOverrides>({ ...NO_OVERRIDES })

  /**
   * The patient this submit sequence has **already created**, if any.
   *
   * ⚠️ This is the fix for the reported defect: a new patient booked into a taken slot ended up on `/patients`
   * twice. `performCreate` is re-entered by design — the slot-taken, out-of-hours and past-time confirmations all
   * call it again — and it re-ran `patientsApi.create` every time, so confirming « créer quand même » created a
   * second patient. The first one is already committed and cannot be taken back, so the retry has to *reuse* it.
   *
   * <p>A ref rather than state, because the retry reads it in the same tick the confirmation grants the override;
   * a state update would not have landed yet. Cleared only when the dialog closes — it is a fact about the
   * database, not about this submit — which is also why the fields below go read-only once it is set: the patient
   * exists under the name that was typed, and letting the name drift afterwards would silently produce the very
   * second record this ref prevents.</p>
   */
  const createdPatientIdRef = useRef<string | null>(null)
  /**
   * The created patient's name — and the **render signal** for the ref above, which a ref cannot be: a ref change
   * does not re-render, so the read-only fields and their explanation would not appear until something else did.
   * Set and cleared together with the ref, always.
   */
  const [createdPatientName, setCreatedPatientName] = useState<string | null>(null)
  const patientAlreadyCreated = createdPatientName !== null


  // Load patients and procedure types when dialog opens
  useEffect(() => {
    if (open) {
      loadPatients()
      loadProcedureTypes()
    }
  }, [open])

  // When opened to schedule a plan step, fix the patient. The act name no longer goes into the notes
  // (AC-P1.51) — the effect below preselects `procedureTypeId`, which is where the act belongs.
  useEffect(() => {
    if (open && isPlanScheduling) {
      setIsBusySlot(false)
      setIsNewPatient(false)
      if (presetPatientId) setSelectedPatientId(presetPatientId)
    }
  }, [open, isPlanScheduling, presetPatientId])

  /**
   * Seed the act list from the plan acts once the catalog has loaded (it arrives async, so this cannot live in the
   * effect above).
   *
   * <p>An act whose procedure is **not** in the loaded catalog — a stale id, or a devis line the clinic never
   * turned into a catalog act — is seeded as a **link-only** row rather than dropped. It carries no duration or
   * colour, but it does carry its devis link, which is the whole reason the visit is being booked.</p>
   *
   * <p>Only fills an untouched list, so reopening the dialog never overwrites acts the user just added.</p>
   *
   * <p>⚠️ <b>It must wait for the catalog to have SETTLED.</b> The `selectedActs.length > 0` guard makes this a
   * seed-once effect, so the first pass — which runs on the same tick the fetch is *started*, against an empty
   * `procedureTypes` — used to win: every act was seeded link-only, the later pass bailed out, and the séance
   * kept the default 30 minutes however many acts it held. « Planifier ensemble » on three acts therefore
   * booked a 30-minute slot and double-booked whatever followed. `procedureTypesLoaded` also flips on
   * *failure*, so an unreachable catalog still seeds link-only rows and the devis link survives.</p>
   */
  useEffect(() => {
    if (!open || !isPlanScheduling || !procedureTypesLoaded || selectedActs.length > 0) return
    setSelectedActs(
      planActs.map<SelectedAct>((a) => ({
        procedureTypeId:
          a.procedureTypeId && procedureTypes.some((p) => p.id === a.procedureTypeId)
            ? a.procedureTypeId
            : null,
        treatmentPlanItemId: a.planItemId,
        planLabel: "devis",
        fallbackName: a.label,
      })),
    )
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, isPlanScheduling, procedureTypes, procedureTypesLoaded])

  /**
   * The visit's length follows the sum of its acts until the user says otherwise. This is what makes grouping
   * usable: three acts booked into one séance need the room for three, and a séance that inherited only the first
   * act's 30 minutes would collide with whatever is booked after it on every calendar it appears on.
   */
  useEffect(() => {
    if (!open || durationTouched || useEndTime) return
    const total = totalActsDuration(selectedActs, procedureTypes)
    if (total > 0) setDuration(String(total))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, selectedActs, procedureTypes, durationTouched, useEndTime])

  // Booking from a patient's page ("Planifier un rendez-vous"): preselect that patient (existing patient,
  // not a busy slot / not the inline-new-patient form). Plan scheduling takes precedence when both apply.
  useEffect(() => {
    if (open && !isPlanScheduling && defaultPatientId) {
      setIsBusySlot(false)
      setIsNewPatient(false)
      setSelectedPatientId(defaultPatientId)
    }
  }, [open, isPlanScheduling, defaultPatientId])

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

  /*
   * A dragged span, applied on open. Separate from the date/time effect above because it must also fire for a
   * caller that supplies a duration and no date — and because it is the one prop that arrives already *touched*.
   */
  useEffect(() => {
    if (!open || defaultDurationMinutes === undefined) return
    setDuration(String(defaultDurationMinutes))
    setUseEndTime(false)
    setDurationTouched(true)
  }, [open, defaultDurationMinutes])

  // Reset form when dialog closes
  useEffect(() => {
    if (!open) {
      // ⚠️ `isBusySlot` belongs in this list and was the one field missing from it: the switch stayed on across
      // openings, so the booking right after a « créneau occupé » silently offered no patient at all.
      setIsBusySlot(false)
      setIsNewPatient(false)
      setSelectedPatientId("")
      setNewPatientFirstName("")
      setNewPatientLastName("")
      setNewPatientPhone("")
      setSelectedDoctorId("")
      setSelectedActs([])
      setProcedureTypesLoaded(false)
      // Back to the caller's own defaults, not to the hardcoded pair: this dialog is a long-lived instance the
      // agenda re-opens with a different span each time, so resetting to « 30 · untouched » would discard the
      // duration the very next drag is about to supply.
      setDurationTouched(defaultDurationMinutes !== undefined)
      const initialTime = getInitialTime()
      setStartHour(initialTime.hour)
      setStartMinute(initialTime.minute)
      setUseEndTime(false)
      setEndHour("10")
      setEndMinute("00")
      setDuration(defaultDurationMinutes ? String(defaultDurationMinutes) : "30")
      setNotes("")
      setShowNotes(false)
      setError(null)
      setPatientPickerOpen(false)
      setShowPastTimeConfirm(false)
      setSlotTakenPrompt(null)
      setOutsideHoursPrompt(null)
      setDuplicatePatientPrompt(null)
      // The created-patient memory ends with the dialog: a new booking starts from a blank new-patient form, and
      // reusing an id across two openings would attach an unrelated appointment to whoever was created last.
      createdPatientIdRef.current = null
      setCreatedPatientName(null)
      grantedOverridesRef.current = { ...NO_OVERRIDES }
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
        setError(`Échec du chargement des patients : ${err.message}`)
      } else {
        setError("Échec du chargement des patients. Veuillez réessayer.")
      }
    } finally {
      setLoadingPatients(false)
    }
  }

  const loadProcedureTypes = async () => {
    try {
      setLoadingProcedureTypes(true)
      setProcedureTypesError(null)
      const data = await procedureTypesApi.list(false) // Only active procedure types
      setProcedureTypes(data || [])
    } catch (err) {
      // AC-P3.31 — the old `// Don't show error to user` was wrong in the one way that matters: an empty
      // list looks identical to a clinic that has no procedures configured, so the user retypes the act as
      // a custom procedure instead of retrying. It is not blocking (the dialog still saves), so this is an
      // inline explanation next to the empty list, not a form-level error.
      setProcedureTypesError(getErrorMessage(err, "La liste des actes n'a pas pu être chargée."))
      setProcedureTypes([]) // Ensure it's always an array
    } finally {
      setLoadingProcedureTypes(false)
      setProcedureTypesLoaded(true)
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

  // Display name of the currently-selected patient (for the searchable picker trigger).
  const selectedPatientName = useMemo(() => {
    const patient = patients.find((p) => p.id === selectedPatientId)
    return patient ? `${patient.firstName} ${patient.lastName}` : ""
  }, [patients, selectedPatientId])

  // Overlap detection: a same-practitioner clash blocks Save (mirrors the server guard); an
  // other-practitioner overlap stays an advisory amber hint.
  const { warning: overlapWarning, samePractitioner: overlapSamePractitioner } = useAppointmentOverlap({
    enabled: open,
    date,
    startHour,
    startMinute,
    durationMinutes: calculatedDuration,
    doctorId: selectedDoctorId || undefined,
  })

  /**
   * The acts' names, resolved against the catalog — the same three cases `AppointmentActsPicker` resolves, so the
   * recapitulation cannot name an act differently from the list the user is reading beside it.
   */
  const actNames = useMemo(
    () =>
      selectedActs.map((act) => {
        if (!act.procedureTypeId) return act.fallbackName ?? "Acte du devis"
        return (
          procedureTypes.find((p) => p.id === act.procedureTypeId)?.name ??
          act.fallbackName ??
          "Acte indisponible"
        )
      }),
    [selectedActs, procedureTypes],
  )

  /** The lead act's colour — what the agenda will actually paint this block with. */
  const leadColorHex = useMemo(() => {
    const lead = selectedActs.find((a) => a.procedureTypeId)
    if (!lead) return null
    return procedureTypes.find((p) => p.id === lead.procedureTypeId)?.colorHex ?? null
  }, [selectedActs, procedureTypes])

  const selectedDoctorName = useMemo(
    () => doctors.find((d) => d.id === selectedDoctorId)?.name ?? null,
    [doctors, selectedDoctorId],
  )

  /**
   * What the récapitulatif states — every field derived from the form beside it, nothing fetched and nothing
   * invented. See `appointment-recap.tsx` for why that constraint is load-bearing rather than incidental.
   */
  const recapModel: AppointmentRecapModel = {
    kind: isBusySlot ? "busy" : "patient",
    patientName: isBusySlot
      ? null
      : isPlanScheduling
        ? presetPatientName ?? null
        : isNewPatient
          ? [newPatientFirstName.trim(), newPatientLastName.trim()].filter(Boolean).join(" ") || null
          : selectedPatientName || null,
    colorHex: leadColorHex,
    date,
    startHour,
    startMinute,
    durationMinutes: calculatedDuration,
    actNames,
    doctorName: selectedDoctorName,
    warning: overlapWarning
      ? { message: overlapWarning, samePractitioner: overlapSamePractitioner }
      : null,
  }

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
          setError("Veuillez saisir le prénom et le nom du nouveau patient")
          return false
        }
        // Optional, reconciled with the patient form: the column is nullable and this door is where a walk-in is
        // booked, often with a name alone. A number that IS given must still be deliverable.
        if (newPatientPhone.trim() && !isDeliverablePhone(newPatientPhone)) {
          setError(PHONE_ERROR_FR)
          return false
        }
      } else if (!selectedPatientId) {
        setError("Veuillez sélectionner un patient")
        return false
      }
    }

    if (!date) {
      setError("Veuillez sélectionner une date")
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

    return true
  }

  // Performs the actual create (patient creation + appointment). Called directly, or from either
  // confirmation dialog once the user confirms (past time, AC-2; out-of-hours, AC-P1.31).
  const performCreate = async (overrides: Partial<CreateOverrides> = {}) => {
    // Merge, never replace: a grant given to an earlier prompt in this same sequence must survive the next one.
    const granted: CreateOverrides = { ...grantedOverridesRef.current, ...overrides }
    grantedOverridesRef.current = granted
    const { hours: allowOutsideWorkingHours, overlap: allowOverlap } = granted
    setError(null)
    setLoading(true)

    try {
      let patientId: string | null = null

      // Create new patient if needed (only if not a busy slot)
      if (!isBusySlot) {
        patientId = selectedPatientId
        if (isNewPatient) {
          /*
           * ⚠️ Reuse before create. This block runs again on every confirmation the server asks for — slot taken,
           * out of hours, past time — and it used to call `patientsApi.create` each time, which is exactly how the
           * reported duplicate happened: one « créer quand même » on a taken slot, two patients on /patients.
           *
           * The already-created patient is committed and cannot be withdrawn, so the retry books the appointment
           * onto it. Nothing else in this function needs to know a retry is in progress.
           */
          if (createdPatientIdRef.current) {
            patientId = createdPatientIdRef.current
          } else {
            try {
              const newPatient = await patientsApi.create({
                firstName: newPatientFirstName.trim(),
                lastName: newPatientLastName.trim(),
                phoneNumber: newPatientPhone.trim() || null,
                // Absent on the first attempt: the server checks whether this person is already on file and
                // refuses with `PatientDuplicate` if so. Only a confirmed prompt sets it.
                allowDuplicate: granted.duplicatePatient || undefined,
              })
              patientId = newPatient.id
              createdPatientIdRef.current = newPatient.id
              setCreatedPatientName(`${newPatient.firstName} ${newPatient.lastName}`.trim())
            } catch (err) {
              // « Ce patient existe déjà » is a question, not a dead end — two people really can share a name, and
              // this form is also where a walk-in with nothing but a name is registered. Guarded on the grant so a
              // refusal that somehow persists surfaces as a real error instead of reopening the same prompt.
              if (
                err instanceof ApiError &&
                err.code === ApiErrorCode.PatientDuplicate &&
                !granted.duplicatePatient
              ) {
                setDuplicatePatientPrompt(err.message)
                setLoading(false)
                return
              }
              if (err instanceof ApiError) {
                setError(`Échec de la création du patient : ${err.message}`)
              } else {
                setError("Échec de la création du patient")
              }
              setLoading(false)
              return
            }
          }
        }
      }

      const appointmentDateTime = buildAppointmentDateTime()
      if (!appointmentDateTime) {
        setError("Veuillez sélectionner une date")
        setLoading(false)
        return
      }

      // AC-P1.51: the `Type: ` prefix writer is gone — the act is carried by `procedureTypeId` alone.
      //
      // This dialog was the source of the divergence: when scheduling a plan step it wrote
      // `Type: <presetProcedureName>` unconditionally, while `procedureTypeId` was filled by a *separate*
      // guarded effect that could no-op (a stale or cross-clinic id) or bail (the user had already picked
      // something). The note then said one act and the column another.
      const appointmentNotes = notes.trim()

      // The séance's acts. A « créneau occupé » carries none by definition — no patient, so no clinical act.
      // A row with a null procedure is a devis link with no catalog act behind it; the server accepts those and
      // names them from the plan step's désignation.
      const procedures = isBusySlot
        ? []
        : selectedActs.map((a) => ({
            procedureTypeId: a.procedureTypeId,
            treatmentPlanItemId: a.treatmentPlanItemId ?? null,
          }))

      // Create appointment
      const created = await appointmentsApi.create({
        patientId,
        appointmentDateTime: appointmentDateTime.toISOString(),
        durationMinutes: calculatedDuration,
        doctorId: selectedDoctorId || undefined,
        notes: appointmentNotes || undefined,
        // The devis links ride on the act rows, so the single-act `treatmentPlanItemId` is deliberately not sent:
        // the server derives that scalar from the list, and sending both risks naming a different lead act.
        procedures,
        treatmentPlanId: isPlanScheduling ? presetPlanId : undefined,
        allowOutsideWorkingHours: allowOutsideWorkingHours || undefined,
        allowOverlap: allowOverlap || undefined,
      })

      onCreated?.(created.id)
      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      if (err instanceof ApiError) {
        // Out-of-hours is a warning, not a refusal: offer to proceed instead of leaving the user stuck. Guarded
        // on `!allowOutsideWorkingHours` so a re-submit that somehow still refuses surfaces as a real error
        // rather than reopening the same prompt forever.
        if (err.code === ApiErrorCode.SlotTaken && !allowOverlap) {
          setSlotTakenPrompt(err.message)
        } else if (err.code === ApiErrorCode.OutsideWorkingHours && !allowOutsideWorkingHours) {
          setOutsideHoursPrompt(err.message)
        } else {
          setError(err.message)
        }
      } else {
        setError("Échec de la création du rendez-vous")
      }
    } finally {
      setLoading(false)
    }
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)
    // A fresh submit re-asks every question: the user may have changed the time, the practitioner or the patient
    // since the last one, so a grant given about the previous attempt says nothing about this one. (The created
    // patient is deliberately NOT reset — see `createdPatientIdRef`.)
    grantedOverridesRef.current = { ...NO_OVERRIDES }

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


  /*
   * A typed booking is not discarded by a stray tap (J9). Below `md:` this is a full-screen sheet, so the strip
   * above it is a live dismiss target over a form that can hold a patient, several acts, a doctor and a time.
   *
   * Only the ROOT and « Annuler » route through the guard; every save path calls the raw prop, and so do the
   * AlertDialog escalations below — a confirmation the user just accepted must not then ask whether to discard.
   */
  const guard = useDirtyGuard(open, onOpenChange)

  return (
    <>
    <Dialog open={open} onOpenChange={guard.onOpenChange}>
      {/*
        The form scrolls; the header and the footer do not.

        Before, `overflow-y-auto` was on the DialogContent itself, so « Créer le rendez-vous » sat at the
        bottom of a four-section form and had to be scrolled to — on the app's most repeated action. (The
        error banner had already been moved down beside it to compensate, which was evidence of the layout
        problem rather than a fix for it; with a pinned footer it can stay there and always be visible.)
        `sm:max-w-2xl` rather than a bare `max-w-2xl`, so the width cap no longer overrides the primitive's
        own `max-w-[calc(100%-2rem)]` mobile guard.
      */}
      {/*
        `lg:max-w-4xl` is what makes room for the récapitulatif pane, and the hinge is `lg:` rather than `md:` on
        purpose: an iPad portrait is 820 px, so at `md:` the two panes would share ~645 px and neither would be
        readable. Below `lg:` the pane becomes the strip above the footer instead. Both caps are prefixed — an
        unprefixed one would cancel the primitive's own mobile gutter (frontend rules § 4).
      */}
      <DialogContent mobile="sheet" className="gap-0 overflow-hidden p-0 md:max-h-[90dvh] md:max-w-2xl lg:max-w-4xl">
        {/*
          The subtitle is `sr-only` at every width rather than deleted: Radix wants a description, and « Planifier
          un nouveau rendez-vous pour un patient » restates the title under a segmented that already says what is
          being booked. Measured: keeping it visible left the form 36 px past its own scroll height at 1440×900 —
          i.e. this one redundant line was the difference between a form that fits and a form that scrolls.
        */}
        <DialogHeader className="flex-shrink-0 gap-2 px-6 pb-3 pt-5">
          <DialogTitle className="text-lg md:text-2xl">Nouveau rendez-vous</DialogTitle>
          <DialogDescription className="sr-only">
            Planifier un nouveau rendez-vous pour un patient
          </DialogDescription>
          {/*
            The « Créneau occupé » switch, promoted out of the Patient card and turned into half of a two-way
            choice. It is the first decision — *what* is being booked — so it belongs above the form rather than
            floating at the right edge of the first field. Absent when scheduling a devis act: that opening books
            a patient's plan step, and a block would have nothing to link to.
          */}
          {!isPlanScheduling && (
            <ModeSegmented
              ariaLabel="Nature du créneau"
              className="mt-1 max-w-sm"
              disabled={loading}
              value={isBusySlot ? "busy" : "patient"}
              onChange={(mode) => {
                const busy = mode === "busy"
                setIsBusySlot(busy)
                if (busy) {
                  setIsNewPatient(false)
                  setSelectedPatientId("")
                  setNewPatientFirstName("")
                  setNewPatientLastName("")
                  setNewPatientPhone("")
                  // A busy slot has no patient, so it has no clinical acts either.
                  setSelectedActs([])
                }
              }}
              options={[
                { value: "patient", label: "Patient" },
                { value: "busy", label: "Créneau occupé" },
              ]}
            />
          )}
        </DialogHeader>

        <form onSubmit={handleSubmit} className="flex min-h-0 flex-1 flex-col">
          {/* The split: form on the left, récapitulatif on the right from `lg:`. One flex row, so the pane costs
              no height arithmetic and the form column keeps its own scroll. */}
          <div className="flex min-h-0 flex-1 lg:flex-row">
          <div className="min-h-0 flex-1 space-y-4 overflow-y-auto px-6 pb-4">
          {/*
            1 — Patient. First, because it is the first thing decided and the field every other one depends on.
            The card chrome is gone: four bordered boxes of identical weight said nothing about which decision
            mattered, and cost ~120 px of the height that made this form scroll.
          */}
          <div className="space-y-3">

            {isPlanScheduling ? (
              <div className="space-y-2">
                <div className="rounded-md border bg-background p-3">
                  <p className="text-sm font-medium">{presetPatientName ?? "Patient"}</p>
                  {/* One badge per act, so a grouped séance says up front which steps of the devis it covers —
                      « Acte du plan : X » in the singular would have hidden the other two. */}
                  <div className="mt-1 flex flex-wrap gap-1">
                    {planActs.map((act) => (
                      <Badge key={act.planItemId} variant="secondary" className="gap-1 text-xs">
                        <Stethoscope className="h-3 w-3" />
                        {act.label}
                      </Badge>
                    ))}
                  </div>
                  {planActs.length > 1 && (
                    <p className="mt-2 text-xs text-muted-foreground">
                      Ces {planActs.length} actes seront réalisés dans la même séance.
                    </p>
                  )}
                </div>
              </div>
            ) : !isBusySlot ? (
              <>
                <Label className="text-sm">Patient *</Label>
                {/*
                  The second of the two switches, and the same reasoning: « existant » and « nouveau » are one
                  question with two answers, so they are one control. Locked once the patient has actually been
                  created — switching back would clear the fields and book onto whoever is picked instead,
                  silently orphaning a real patient record.
                */}
                <ModeSegmented
                  ariaLabel="Type de patient"
                  size="sm"
                  disabled={patientAlreadyCreated || loading}
                  value={isNewPatient ? "new" : "existing"}
                  onChange={(mode) => {
                    const isNew = mode === "new"
                    setIsNewPatient(isNew)
                    if (isNew) {
                      setSelectedPatientId("")
                    } else {
                      setNewPatientFirstName("")
                      setNewPatientLastName("")
                      setNewPatientPhone("")
                    }
                  }}
                  options={[
                    { value: "existing", label: "Patient existant" },
                    { value: "new", label: "Nouveau patient" },
                  ]}
                />

                {isNewPatient ? (
                  <div className="space-y-3">
                    {/*
                      The patient is created but the appointment is not — the state you are left in when a booking is
                      refused after the patient went in (a taken slot, closed hours). Saying so is what makes the
                      read-only fields legible instead of looking broken, and it is the honest answer to « why can I
                      not correct the name now? »: the record exists, and editing this form cannot reach it.
                    */}
                    {patientAlreadyCreated && (
                      <p
                        role="status"
                        className="rounded-md border border-warning/40 bg-warning-wash px-3 py-2 text-xs text-warning-ink"
                      >
                        {createdPatientName ?? "Ce patient"} a été créé. Il reste à enregistrer le rendez-vous —
                        corrigez l&apos;heure si besoin puis réessayez. Pour modifier son nom, ouvrez sa fiche depuis
                        « Patients ».
                      </p>
                    )}
                    <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                      <div className="space-y-2">
                        <Label htmlFor="firstName" className="text-sm">
                          Prénom *
                        </Label>
                        <Input
                          id="firstName"
                          placeholder="Mohamed"
                          value={newPatientFirstName}
                          onChange={(e) => setNewPatientFirstName(e.target.value)}
                          className="h-10"
                          disabled={patientAlreadyCreated}
                          required
                        />
                      </div>
                      <div className="space-y-2">
                        <Label htmlFor="lastName" className="text-sm">
                          Nom *
                        </Label>
                        <Input
                          id="lastName"
                          placeholder="Ben Salah"
                          value={newPatientLastName}
                          onChange={(e) => setNewPatientLastName(e.target.value)}
                          className="h-10"
                          disabled={patientAlreadyCreated}
                          required
                        />
                      </div>
                    </div>
                    <div className="space-y-2">
                      <Label htmlFor="newPatientPhone" className="text-sm">
                        Téléphone <span className="text-xs text-muted-foreground">(recommandé)</span>
                      </Label>
                      <Input
                        id="newPatientPhone"
                        type="tel"
                        placeholder="Ex. 20 123 456"
                        value={newPatientPhone}
                        onChange={(e) => setNewPatientPhone(e.target.value)}
                        className="h-10"
                        disabled={patientAlreadyCreated}
                      />
                      <p className="text-xs text-muted-foreground">
                        Numéro tunisien à 8 chiffres, ou +216… Sans lui, ce patient ne recevrait ni rappel ni
                        relance.
                      </p>
                    </div>
                  </div>
                ) : (
                  <div className="space-y-2">
                    {/* Visually hidden: the « Patient * » heading two rows up already names this field, and a
                        second visible label under a segmented that says « Patient existant » is noise. The
                        association still has to exist for the combobox, hence a real Label rather than none. */}
                    <Label htmlFor="patient" className="sr-only">
                      Sélectionner un patient
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
                          /* Radix focuses the first tabbable element in the dialog, which was the « Créneau
                             occupé » switch — so booking always started with a reach for the mouse. The
                             patient is the first decision in this form, so it gets the focus. */
                          autoFocus
                          className="w-full h-10 justify-between bg-card font-normal"
                        >
                          <span className={cn("truncate", !selectedPatientId && "text-muted-foreground")}>
                            {selectedPatientName ||
                              (loadingPatients
                                ? "Chargement des patients…"
                                : patients.length === 0
                                  ? "Aucun patient trouvé"
                                  : "Choisir un patient…")}
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
                      <p className="text-xs text-muted-foreground">Créez un nouveau patient à l'aide du bouton ci-dessus</p>
                    )}
                  </div>
                )}
              </>
            ) : (
              /*
                ⚠️ The copy states what the server actually does now, and it used to state the opposite of it.
                « Aucun patient ne pourra être assigné à cette période » was a promise the product could not keep:
                a block carries no patient and, until L2b, `FindCollisionAsync` returned "no collision" for any
                candidate with no `DoctorId` — so a « créneau occupé » with no practitioner prevented **nothing**,
                and the database could not help either (its exclusion constraint is predicated on
                `DoctorId IS NOT NULL`). L2b made an unassigned booking compete with everything in the clinic,
                which is what finally makes a lunch break protectable.
                Two wordings, because the scope genuinely differs — and neither says « aucun … ne pourra », since
                the guard is advisory: a colleague can still book over it by confirming « Continuer quand même »,
                and the row is then recorded as a deliberate overlap.
              */
              <div className="p-3 rounded-lg bg-warning-wash border border-warning/30">
                <p className="text-sm text-warning-ink">
                  {selectedDoctorId
                    ? "Ce créneau sera marqué comme occupé pour ce praticien : toute tentative de lui assigner un rendez-vous à cette période demandera une confirmation."
                    : "Ce créneau sera marqué comme occupé pour tout le cabinet : toute tentative d'y créer un rendez-vous demandera une confirmation."}
                </p>
              </div>
            )}
          </div>

          {/* 2 — Quand. */}
          <div className="space-y-3">
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              {/* Date Picker */}
              <div className="space-y-2">
                <Label htmlFor="create-appt-date" className="text-sm">Date *</Label>
                <Popover modal>
                  <PopoverTrigger asChild>
                    <Button
                      id="create-appt-date"
                      variant="outline"
                      className={cn(
                        // ⚠️ `bg-card`, not the variant's own `bg-background`: this dialog paints on
                        // `--background`, so an outline control here is the same value as the surface behind it
                        // and reads as transparent. Its own neighbour — the `TimeField` — is already `bg-card`,
                        // so without this the two halves of one row are different whites.
                        "w-full h-10 justify-start bg-card text-left font-normal",
                        !date && "text-muted-foreground",
                      )}
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

              {/* Start Time — one typeable field. Was two Selects, the second listing all sixty minutes. */}
              <div className="space-y-2">
                <Label htmlFor="create-appt-start-time" className="text-sm">Heure de début *</Label>
                <TimeField
                  id="create-appt-start-time"
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

            {/* Durée — a label and its presets, no longer a bordered sub-row with a switch at the far edge. */}
            <div className="space-y-2">
              <Label className="text-sm">Durée</Label>
              {useEndTime ? (
                <div className="space-y-2 sm:max-w-[50%]">
                  <Label htmlFor="create-appt-end-time" className="sr-only">Heure de fin *</Label>
                  <TimeField
                    id="create-appt-end-time"
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
                /*
                 * A GRID below `sm:`, because these six buttons could not shrink and could not wrap.
                 *
                 * `buttonVariants` carries both `shrink-0` and `whitespace-nowrap`, and `flex-1` /
                 * `shrink-0` are different tailwind-merge groups — so both survived and `flex-shrink: 0`
                 * won. Six `px-3` presets need ~324 px of min-content against ~310 px of form body at 390 px
                 * (~294 px at 360 px), and the body is `overflow-y-auto`, which computes `overflow-x` to
                 * `auto`: « 2h » was clipped and the form gained a horizontal scrollbar, breaking the
                 * "the body never scrolls sideways" invariant the shell is built on.
                 *
                 * Two rows of three fits every phone with room to spare, and `sm:flex` restores the single
                 * row where it always fitted. `flex-1` is dropped: a grid cell is already equal-width.
                 */
                <div className="grid grid-cols-3 gap-2 sm:flex sm:flex-wrap">
                  {/*
                    The summed length as a chip of its own, active, when it is not one of the presets.
                    Before this the acts could sum to 70 min and *no* preset was highlighted, so the row read as
                    « aucune durée choisie » while a badge elsewhere said otherwise. Pressing it is a real
                    action — « garder cette longueur » — which is why it also sets `durationTouched`.
                  */}
                  {!DURATION_PRESETS.includes(calculatedDuration) && calculatedDuration > 0 && (
                    <Button
                      type="button"
                      variant="default"
                      size="sm"
                      onClick={() => { setDurationTouched(true); setDuration(String(calculatedDuration)) }}
                      className="sm:flex-1"
                    >
                      {durationDisplay}
                    </Button>
                  )}
                  {DURATION_PRESETS.map((mins) => (
                    <Button
                      key={mins}
                      type="button"
                      variant={duration === String(mins) ? "default" : "outline"}
                      size="sm"
                      // Picking a duration by hand stops the act-sum from overwriting it — see `durationTouched`.
                      onClick={() => { setDurationTouched(true); setDuration(String(mins)) }}
                      // `bg-card` on the unselected ones for the reason the date trigger states.
                      className={cn("sm:flex-1", duration !== String(mins) && "bg-card")}
                    >
                      {mins < 60 ? `${mins}m` : `${mins / 60}h`}
                    </Button>
                  ))}
                </div>
              )}
              {/*
                The « Définir l'heure de fin » switch, demoted to the text control it always was. A `Switch` at
                the far edge of its own bordered row spent a full row of height announcing a mode almost nobody
                uses; as a link under the presets it costs one line and reads as the alternative it is.

                It grows its own box rather than taking `.touch-target`: the overlay would reach up into the
                8 px gap above and overhang the preset row, and the later sibling paints last (frontend
                rules § 2).
              */}
              <button
                type="button"
                disabled={loading}
                onClick={() => {
                  const next = !useEndTime
                  setUseEndTime(next)
                  if (next) {
                    const startTotalMinutes = Number.parseInt(startHour) * 60 + Number.parseInt(startMinute)
                    const endTotalMinutes = startTotalMinutes + Number.parseInt(duration)
                    const newEndHour = Math.floor(endTotalMinutes / 60) % 24
                    const newEndMinute = endTotalMinutes % 60
                    setEndHour(String(newEndHour).padStart(2, "0"))
                    setEndMinute(String(newEndMinute).padStart(2, "0"))
                  }
                }}
                className="inline-flex min-h-9 items-center text-xs text-muted-foreground underline-offset-2 hover:text-foreground hover:underline coarse:min-h-11"
              >
                {useEndTime ? "Utiliser une durée" : "Définir l'heure de fin"}
              </button>
            </div>
            {/*
              ⚠️ The overlap warning is NOT here any more, and the ~60 px `min-h-[2.5rem]` reservation that used
              to hold its place is gone with it. That reservation existed so an appearing message would not shove
              the form under the cursor — a real problem, solved by giving the clash a permanent home in the
              récapitulatif instead. Empty most of the time, it read as a rendering fault at every width.
            */}
          </div>

          {/* 3 & 4 — les actes, puis le praticien. */}
          <div className="space-y-4">
            {/*
              The acts come FIRST, above the practitioner, and are no longer filed under a heading called
              « Détails ». They decide the duration, the colour the agenda paints the block with and the fiche de
              soins proposal; the practitioner decides none of those and, in a single-chair cabinet, is already
              filled in. A « créneau occupé » has no acts by definition.
            */}
            {!isBusySlot && (
              <AppointmentActsPicker
                procedureTypes={procedureTypes}
                loading={loadingProcedureTypes}
                error={procedureTypesError}
                onRetry={() => void loadProcedureTypes()}
                value={selectedActs}
                onChange={setSelectedActs}
                disabled={loading}
                onProcedureCreated={(created) => setProcedureTypes((prev) => [...prev, created])}
                fallbackDurationMinutes={calculatedDuration}
                idPrefix="create-appt"
              />
            )}

            <div className="grid grid-cols-1 gap-4">
              <div className="space-y-2">
                <Label htmlFor="doctor" className="text-sm">
                  Praticien
                </Label>
                <Select
                  value={selectedDoctorId}
                  onValueChange={setSelectedDoctorId}
                  disabled={loadingDoctors || loading}
                >
                  <SelectTrigger className="h-10 w-full" id="doctor">
                    <SelectValue placeholder={loadingDoctors ? "Chargement des médecins…" : doctors.length === 0 ? "Aucun médecin trouvé" : "Choisir un médecin…"} />
                  </SelectTrigger>
                  <SelectContent className="max-h-[200px]">
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

            </div>
          </div>

          {/* 5 — Notes, folded away. */}
          <div className="border-t pt-3">
            <button
              type="button"
              onClick={() => setShowNotes((v) => !v)}
              aria-expanded={showNotes}
              aria-controls="create-appt-notes"
              className="flex min-h-9 w-full items-center gap-2 text-sm font-medium text-muted-foreground hover:text-foreground coarse:min-h-11"
            >
              <FileText className="h-4 w-4" />
              Notes et options
              {notes.trim().length > 0 && (
                <Badge variant="secondary" className="ms-1">1</Badge>
              )}
              <ChevronDown className={cn("ms-auto h-4 w-4 transition-transform duration-200", showNotes && "rotate-180")} />
            </button>
            {showNotes && (
              <div id="create-appt-notes" className="pt-2">
                <Textarea
                  placeholder="Ajouter des notes ou des instructions particulières…"
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                  className="min-h-[80px] resize-none"
                />
              </div>
            )}
          </div>

            {/* At the end of the scrolling body, not the top — but « Créer le rendez-vous » is in a PINNED
                footer outside this scroller, so `FormErrorBanner` scrolls itself into view to be seen. */}
            {/* The shared primitive, not a hand-rolled red box: this is the busiest form in the app and it was
                reporting failures in a slightly different red from the eighteen dialogs that route through
                `FormErrorBanner` — which now renders on `--destructive` tokens and needs no `dark:` twin. */}
            <FormErrorBanner message={error} />
          </div>

          {/* The pane. `flex` rather than `block` so its own `overflow-y-auto` gets a height to scroll within. */}
          <AppointmentRecap model={recapModel} variant="rail" className="hidden w-[272px] shrink-0 lg:flex" />
          </div>

          {/* …and the same model as a strip, below `lg:`. Never both: one is `hidden lg:flex`, the other
              `lg:hidden`. It sits between the scrolling form and the footer, so it stays visible while the
              patient, the time and the acts are being filled in above it. */}
          <AppointmentRecap model={recapModel} variant="bar" className="lg:hidden" />

          <DialogFooter className="flex-shrink-0 gap-2 border-t bg-background px-6 py-4">
            <Button type="button" variant="outline" onClick={() => guard.onOpenChange(false)} disabled={loading}>
              Annuler
            </Button>
            {/* No longer disabled on an overlap: the collision is advisory and the server offers the override.
                Blocking here made the warning a dead end and hid the fact that proceeding is allowed. */}
            <Button type="submit" disabled={loading}>
              {loading ? "Création…" : "Créer le rendez-vous"}
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


    {/*
      Double-booking confirmation. Same shape as the out-of-hours confirm below, and for the same reason: an overlap
      with the same practitioner is a legitimate thing a clinic does (a second chair, an assistant preparing one
      patient while the dentist starts another, an emergency squeezed in), so it warns and lets the user proceed.

      Driven by the server's `slot_taken` code rather than the local overlap hook, because the server is the
      authority — and because confirming here sets `allowOverlap`, which is what records the acknowledgement and
      exempts the row from the database's exclusion constraint. Without that flag the write is impossible, so a
      purely client-side "ignore the warning" would fail at the database.
    */}
    <AlertDialog
      open={slotTakenPrompt !== null}
      onOpenChange={(o) => { if (!o) setSlotTakenPrompt(null) }}
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Créneau déjà occupé</AlertDialogTitle>
          <AlertDialogDescription>
            {slotTakenPrompt} Voulez-vous quand même créer ce rendez-vous ? Le double rendez-vous sera enregistré comme
            volontaire.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={loading}>Annuler</AlertDialogCancel>
          <AlertDialogAction
            onClick={() => {
              setSlotTakenPrompt(null)
              // `true` makes the server record it via Appointment.MarkBookedWithOverlap() rather than letting it
              // through unmarked — the flag is both the audit trail and the constraint exemption.
              void performCreate({ overlap: true })
            }}
            disabled={loading}
          >
            Continuer
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    {/*
      Out-of-hours confirmation. Same shape as the past-time confirm above, and deliberately so: booking outside
      the posted hours is a legitimate thing a clinic does (an emergency, a favour, a Saturday not yet in the
      settings), so it warns and lets the user proceed rather than refusing.

      Driven by the server's `outside_working_hours` code, not a client-side hours check — the rule resolves a
      doctor override against the clinic's hours in clinic-local time, and duplicating that in the browser is how
      the two would drift.
    */}
    <AlertDialog
      open={outsideHoursPrompt !== null}
      onOpenChange={(o) => { if (!o) setOutsideHoursPrompt(null) }}
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>En dehors des horaires d&apos;ouverture</AlertDialogTitle>
          <AlertDialogDescription>
            {outsideHoursPrompt} Voulez-vous quand même créer ce rendez-vous ?
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={loading}>Annuler</AlertDialogCancel>
          <AlertDialogAction
            onClick={() => {
              setOutsideHoursPrompt(null)
              // `true` makes the server record the booking as a deliberate out-of-hours exception
              // (Appointment.MarkBookedOutsideWorkingHours) rather than just letting it through unmarked.
              void performCreate({ hours: true })
            }}
            disabled={loading}
          >
            Continuer
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    {/*
      « Ce patient existe déjà » — the third advisory refusal, and the one this dialog needed most: its quick-add form
      collects a name and a phone, so the patient it creates is exactly the kind that is hard to tell apart from
      somebody already on file, and nothing here used to check.

      ⚠️ The destructive-looking option is « Créer quand même », not « Annuler ». A duplicate cannot be merged or
      deleted afterwards, so it is the irreversible choice and is styled as one — the ordinary answer is to close this,
      turn « Nouveau patient » off and pick the existing patient from the list.
    */}
    <AlertDialog
      open={duplicatePatientPrompt !== null}
      onOpenChange={(o) => { if (!o) setDuplicatePatientPrompt(null) }}
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Ce patient existe peut-être déjà</AlertDialogTitle>
          <AlertDialogDescription>
            {duplicatePatientPrompt}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          {/* Named after what it does, and it does it: the safe answer is not « annuler the booking » but « this is
              the patient you already have », so this switches the form back to the picker and clears what was typed
              — the same thing the « Nouveau patient » switch does when turned off. Nothing was created, so there is
              nothing to keep. */}
          <AlertDialogCancel
            disabled={loading}
            onClick={() => {
              setIsNewPatient(false)
              setNewPatientFirstName("")
              setNewPatientLastName("")
              setNewPatientPhone("")
            }}
          >
            Choisir un patient existant
          </AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            onClick={() => {
              setDuplicatePatientPrompt(null)
              void performCreate({ duplicatePatient: true })
            }}
            disabled={loading}
          >
            Créer quand même
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
    <DiscardChangesDialog guard={guard} />
    </>
  )
}
