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
import { InitialsAvatar } from "@/components/ui/initials-avatar"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Calendar } from "@/components/ui/calendar"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Textarea } from "@/components/ui/textarea"
import { Badge } from "@/components/ui/badge"
import { TimeField } from "@/components/ui/time-field"
import { format, parseISO } from "date-fns"
import { fr } from "date-fns/locale"
import { CalendarIcon, FileText, X, Save, Receipt, ChevronDown, MoreHorizontal, Trash2 } from "lucide-react"
import { cn, parseDurationToMinutes } from "@/lib/utils"
import {
  AppointmentRecap,
  AppointmentRecapSection,
  type AppointmentRecapModel,
} from "@/components/appointment-recap"
import { appointmentsApi } from "@/lib/api/appointments"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import {
  AppointmentActsPicker, hasInvalidAgreedCost, negotiatedTotalOf, toProcedurePayloads, totalActsDuration,
  type SelectedAct,
} from "@/components/appointment-acts-picker"
import { getErrorMessage, showErrorToast } from "@/lib/errors"
import { formatAmount, quoteFr } from "@/lib/format"
import { toast } from "sonner"
import type { AppointmentDto, ProcedureTypeDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { useDoctors } from "@/lib/hooks/use-doctors"
import { useAppointmentOverlap } from "@/lib/hooks/use-appointment-overlap"
import { ApiErrorCode } from "@/lib/api/client"
import { specialtyLabel } from "@/lib/specialties"
import Link from "next/link"
import { InvoiceFormModal } from "@/components/factures/invoice-form-modal"
import {
  MANUALLY_SETTABLE_STATUSES,
  appointmentStatusBadgeClass,
  appointmentStatusLabel,
} from "@/components/appointment-labels"

/**
 * Sentinel for "no practitioner" in the Radix Select, which cannot hold an empty-string value. Mapped to `""`
 * in state and sent to the API as an explicit `null`, which unassigns the practitioner.
 */
const UNASSIGNED_DOCTOR = "__unassigned__"

/** The offered visit lengths — see the create dialog, which asks the same « is this one of them? » question. */
const DURATION_PRESETS = [15, 30, 45, 60, 90, 120]

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
  /**
   * The statut this form was hydrated with — the baseline the save compares against.
   *
   * ⚠️ Not cosmetic. `status` is a controlled Select seeded from the appointment, so a user who only moves the date
   * still leaves it holding « Absent » — and the payload used to re-post that verbatim, which made the server
   * re-mark a rescheduled no-show absent (and, worse, skip the double-booking guard, so the freed slot could be
   * given away twice). The statut is now sent **only when it actually changed**.
   */
  const [hydratedStatus, setHydratedStatus] = useState<string>("scheduled")
  
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
  /**
   * Is « Notes et options » open? Opened on hydration when the visit **has** notes — folding away a note somebody
   * wrote would hide it from the person who opened the visit to read it, which is the opposite of the point.
   */
  const [showNotes, setShowNotes] = useState(false)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [showCancelDialog, setShowCancelDialog] = useState(false)
  /**
   * « Supprimer (créé par erreur) » — deliberately NOT a second door to « Annulé ». A séance nobody ever booked
   * on purpose is not an absence, and `Cancelled` counts in the « taux d'absence », so tidying the agenda that
   * way is what makes a cabinet's own figures describe a month it did not have.
   */
  const [showDeleteDialog, setShowDeleteDialog] = useState(false)
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

  /**
   * The appointment as the **server** has it, re-read each time this dialog opens.
   *
   * <p>⚠️ The `appointment` prop is a snapshot the page took when the row was clicked and **never refreshes**
   * (`app/appointments/page.tsx` sets `selectedAppointment` once), so the `Version` sent with the save could be
   * older than the stored row — at which point `SetExpectedVersion` refuses with a **409** and « Cet
   * enregistrement a été modifié par quelqu'un d'autre », on an appointment nobody else touched. The likeliest
   * writer is our own post-commit Google-Calendar push, which stamps `GoogleCalendarEventId` after the response
   * has already gone out, so the refetch that follows a save can land *before* it and cache a stale version.</p>
   *
   * <p>Falls back to the prop on a failed read: a snapshot is still better than a dialog that will not open, and
   * the save's own 409 remains the backstop.</p>
   */
  const [refreshed, setRefreshed] = useState<AppointmentDto | null>(null)
  const source = refreshed ?? appointment

  useEffect(() => {
    if (!open || !appointment?.id) return
    let cancelled = false
    appointmentsApi
      .get(appointment.id)
      .then((fresh) => { if (!cancelled) setRefreshed(fresh) })
      .catch(() => { /* keep the prop — see above */ })
    return () => { cancelled = true }
  }, [open, appointment?.id])


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
   * it is showing. Falls back to the manually-settable set only when the server did not send the field (an older
   * cached payload) — never to a client-side re-derivation of the rules.
   *
   * ⚠️ The fallback is `MANUALLY_SETTABLE_STATUSES`, not every status: « Séance passée » is written by the
   * progress job alone, so offering it here would let a user assert that a slot has ended when it has not.
   * The *current* status is still prepended below, so a visit already in it renders correctly.
   */
  const statusOptions = useMemo(() => {
    const current = source?.status
    const allowed = source?.allowedNextStatuses
    if (!allowed || allowed.length === 0) {
      return current && !MANUALLY_SETTABLE_STATUSES.includes(current as (typeof MANUALLY_SETTABLE_STATUSES)[number])
        ? [current, ...MANUALLY_SETTABLE_STATUSES]
        : [...MANUALLY_SETTABLE_STATUSES]
    }
    return current && !allowed.includes(current) ? [current, ...allowed] : allowed
  }, [source?.status, source?.allowedNextStatuses])

  // Advisory overlap warning (AC-3): excludes the appointment being edited; non-blocking.
  const { warning: overlapWarning, samePractitioner: overlapSamePractitioner } = useAppointmentOverlap({
    enabled: open,
    date,
    startHour,
    startMinute,
    durationMinutes: calculatedDuration,
    doctorId: selectedDoctorId || undefined,
    excludeAppointmentId: source?.id,
  })

  /** The acts' names, resolved exactly as `AppointmentActsPicker` resolves them — see the create dialog. */
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

  const leadColorHex = useMemo(() => {
    const lead = selectedActs.find((a) => a.procedureTypeId)
    if (!lead) return null
    return procedureTypes.find((p) => p.id === lead.procedureTypeId)?.colorHex ?? null
  }, [selectedActs, procedureTypes])

  const selectedDoctorName = useMemo(
    () => doctors.find((d) => d.id === selectedDoctorId)?.name ?? null,
    [doctors, selectedDoctorId],
  )

  /** Everything the récapitulatif states, all of it derived from this form. See `appointment-recap.tsx`. */
  const recapModel: AppointmentRecapModel = {
    // A saved appointment with no `patientId` genuinely IS a « créneau occupé » — unlike the create form, where
    // an empty patient only means nobody has picked one yet.
    kind: source?.patientId ? "patient" : "busy",
    patientName: source?.patientId ? source.patientName : null,
    colorHex: leadColorHex,
    date,
    startHour,
    startMinute,
    durationMinutes: calculatedDuration,
    actNames,
    negotiatedTotal: negotiatedTotalOf(selectedActs, procedureTypes),
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
  //
  // ⚠️ It DOES re-run once when the server's own copy lands (`refreshed?.version`), which is the point of that
  // re-read: hydrating from a snapshot whose version is behind the stored row is what turns the next save into
  // a 409. That is one re-hydration a few hundred milliseconds after opening, not a running sync — a peer's
  // later edit still leaves this form alone.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    const appointment = source
    if (appointment && open) {
      setPatientName(appointment.patientName)
      setStatus(appointment.status.toLowerCase())
      // Remembered so the save can tell « the user changed the statut » from « the form is echoing what it was
      // hydrated with ». See the `status` key in `performUpdate`.
      setHydratedStatus(appointment.status.toLowerCase())
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
                // A negotiated price hydrates as **typed**, so it is re-sent on save. Leaving it undefined would
                // show the catalogue tarif in the field and the next save — rescheduling, changing the note,
                // anything — would quietly restore every act to that tarif, since the list replaces the acts.
                agreedCost: p.agreedCost != null ? formatAmount(p.agreedCost) : undefined,
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
        setShowNotes(true)
      } else {
        setNotes("")
        setShowNotes(false)
      }
    }
  }, [appointment?.id, open, refreshed?.version])

  // Reset form when dialog closes
  useEffect(() => {
    if (!open) {
      setError(null)
      setUseEndTime(false)
      setShowCancelDialog(false)
      setShowPastTimeConfirm(false)
      // The server's copy belongs to the appointment that was open — keeping it would hydrate the next one
      // from the previous patient's row.
      setRefreshed(null)
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
      setError("Sélectionnez une date.")
      return false
    }

    // Same guard as the create dialog: an unparseable amount reads as null and would silently put the act back
    // to its catalogue tarif on save.
    if (selectedActs.some(hasInvalidAgreedCost)) {
      setError("Corrigez le prix d'un acte : saisissez un montant en dinars, par exemple 120,000.")
      return false
    }

    return true
  }

  // Performs the actual update. Called directly, or from either confirmation dialog once the user
  // confirms (past time, AC-2; out-of-hours, AC-P1.31).
  const performUpdate = async (allowOutsideWorkingHours = false, allowOverlap = false) => {
    grantedOverridesRef.current = { hours: allowOutsideWorkingHours, overlap: allowOverlap }
    const appointment = source
    if (!appointment) return
    setError(null)
    setLoading(true)

    try {
      const appointmentDateTime = buildAppointmentDateTime()
      if (!appointmentDateTime) {
        setError("Sélectionnez une date.")
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
        /*
         * ⚠️ Sent **only when the user changed it** — omitted otherwise, which the server reads as "leave the
         * statut alone".
         *
         * This is the root cause of L2a, and fixing it here fixes the class rather than the instance. Every other
         * field on this payload is already deliberate about the difference between "clear this" and "leave this
         * alone"; the statut was the one that always re-asserted whatever the form happened to be holding. So
         * moving a « Absent » appointment to next Tuesday posted `status: "noshow"` alongside the new date, and the
         * server dutifully re-marked the patient absent for a visit nobody had missed yet.
         */
        status: status !== hydratedStatus ? status : undefined,
        // Replaces the whole list. `[]` is a real instruction here (« ce rendez-vous n'a plus d'acte ») and the
        // server distinguishes it from an omitted key, which is why this dialog always sends it and the cancel
        // path — which posts { status } alone — never does.
        procedures: toProcedurePayloads(selectedActs),
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
    const originalStart = source ? parseISO(source.appointmentDateTime) : null
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
    const appointment = source
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

  /**
   * Take a mis-booked séance out of the agenda, the patient's history and the dashboard's figures, without
   * claiming the patient cancelled or failed to come. The server's own mark, the one « À clôturer » uses.
   *
   * On failure only the CONFIRMATION closes — the form behind it keeps every field the user typed (§ 13). The one
   * refusal this can meet (the séance carries a fiche or a live note d'honoraires) is a real answer with no
   * override, so it goes to the toast, which is the app's live region and reads the server's own sentence.
   */
  const handleDeleteAppointment = async () => {
    const appointment = source
    if (!appointment) return

    setLoading(true)
    try {
      await appointmentsApi.disregardVisit(appointment.id)

      setShowDeleteDialog(false)
      toast.success("Rendez-vous supprimé", {
        description:
          `Il ne compte pas comme une annulation. Vous pouvez le récupérer dans ${quoteFr("À clôturer")}.`,
      })
      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      setShowDeleteDialog(false)
      showErrorToast(err, "Échec de la suppression du rendez-vous")
    } finally {
      setLoading(false)
    }
  }

  /*
   * A typed booking is not discarded by a stray tap (J9). Below `md:` this is a full-screen sheet, so the strip
   * above it is a live dismiss target over a form that can hold a patient, several acts, a doctor and a time.
   *
   * Only the ROOT and « Annuler » route through the guard; every save path calls the raw prop, and so do the
   * AlertDialog escalations below — a confirmation the user just accepted must not then ask whether to discard.
   *
   * ⚠️ **Above the `!appointment` early return, and that placement is the whole of a crash fix.** This is the
   * only hook in the file that sat *below* it, and the page mounts this dialog permanently with
   * `appointment={null}` — so the first render recorded a hook list that stopped short, and the very first click
   * on an appointment rendered one hook longer. React answers that with « Rendered more hooks than during the
   * previous render », which is a throw during render: the whole agenda was replaced by `app/error.tsx`. Every
   * other hook here already precedes the return; a hook must never sit after a conditional return.
   */
  const guard = useDirtyGuard(open, onOpenChange)

  /*
   * What the deletion confirm names (§ 13 — « Êtes-vous sûr ? » cannot say which of a day's séances is going).
   * Only reachable for a séance with a patient: the menu item is absent on a « créneau occupé », whose removal
   * « séances retirées » cannot list and therefore cannot undo.
   */
  const deletionTarget = source
    ? `Le rendez-vous de ${patientName} du `
      + format(parseISO(source.appointmentDateTime), "d MMMM à HH:mm", { locale: fr })
    : ""

  if (!appointment) return null

  // AC-P1.41/1.44: was a string-mangler whose ([A-Z]) branch was dead (the value is lower-cased at
  // hydration), so « Inprogress » and « Noshow » reached the screen. One shared map now.

  return (
    <>
      <Dialog open={open} onOpenChange={guard.onOpenChange}>
        {/* Scrolling body, pinned header and footer — see the create dialog for why. This one matters even
            more: its footer holds three actions, one of them « Annuler le rendez-vous », and a destructive
            action that has to be hunted for by scrolling is a destructive action someone will mis-click. */}
        <DialogContent mobile="sheet" className="gap-0 overflow-hidden p-0 md:max-h-[90dvh] md:max-w-2xl lg:max-w-4xl">
          {/*
            ⚠️ The patient is an IDENTITY here, not a field — this replaced a whole bordered card whose entire
            contents were one **disabled** input holding the name and the sentence « Le nom du patient ne peut pas
            être modifié ». That is a fact about the record, not something anyone edits, and spending a card on it
            pushed everything that *is* editable further down a form that already scrolled. A dialog about a
            rendez-vous should say whose it is before it says anything else — and the link to their fiche is the
            thing staff actually reach for from here.
          */}
          <DialogHeader className="flex-shrink-0 gap-2 px-6 pb-4 pt-5">
            <DialogTitle className="text-lg md:text-2xl">Rendez-vous</DialogTitle>
            <DialogDescription className="sr-only">
              Mettez à jour les détails du rendez-vous ou changez son statut
            </DialogDescription>
            {source?.patientId ? (
              /*
                The patient's name IS the link to their fiche. It replaced a separate « Ouvrir la fiche » button
                that sat at the end of this row: with the name itself clickable the two were a second door to the
                same place, adjacent, which is what this dialog's own « Annuler le rendez-vous » note argues
                against — the name is where a reader looks for the person, so that is where the door belongs.

                ⚠️ `coarse:min-h-11` on the link, not `.touch-target`: it now carries the *only* path to the fiche
                and it sits directly above the date line, where an overlay would overhang and steal taps.

                ⚠️ Navigating away abandons any unsaved edit, exactly as the button it replaces did — the dirty
                guard covers closing the dialog, not following a link out of it. Unchanged behaviour, stated here
                because the affordance is now more inviting than a small link at the row's end.
              */
              <div className="flex items-center gap-3">
                <InitialsAvatar name={patientName} />
                <div className="min-w-0 flex-1">
                  {/* ⚠️ Underlined AT REST, not only on hover: this is the sole path to the fiche now, and a name
                      that reveals itself as a link only when a mouse is already on it is not discoverable at all
                      — least of all on a touch screen, which has no hover. The accessible name keeps the wording
                      the button carried, so what a screen reader announces did not get quieter either. */}
                  <Link
                    href={`/patients/${source.patientId}`}
                    aria-label={`Ouvrir la fiche de ${patientName}`}
                    title={`Ouvrir la fiche de ${patientName}`}
                    className="inline-flex max-w-full items-center rounded-sm text-sm font-semibold leading-tight underline decoration-muted-foreground/50 underline-offset-2 transition-colors hover:text-primary hover:decoration-primary coarse:min-h-11"
                  >
                    <span className="truncate">{patientName}</span>
                  </Link>
                  <p className="truncate text-xs text-muted-foreground">
                    {date ? format(date, "EEEE d MMMM yyyy", { locale: fr }) : "Date à définir"}
                  </p>
                </div>
              </div>
            ) : (
              <p className="text-sm text-muted-foreground">Créneau occupé — aucun patient</p>
            )}
          </DialogHeader>

          <form onSubmit={handleUpdate} className="flex min-h-0 flex-1 flex-col">
            <div className="flex min-h-0 flex-1 lg:flex-row">
            <div className="min-h-0 flex-1 space-y-4 overflow-y-auto px-6 pb-4">

            {/*
              Statut — FIRST, and one tap per option rather than a Select buried below the acts picker.

              Opening this dialog usually means « le patient est arrivé » / « c'est terminé » / « il n'est pas
              venu », so the status was the one field that had to be scrolled to. As buttons the whole set is
              also *visible*, which a Select is not: a collapsed control cannot show that « Terminé » is one tap
              away, nor that the options are exactly what this appointment may become.

              ⚠️ The options are still `statusOptions` — the server's own `allowedNextStatuses` — so this cannot
              offer a move the API then refuses. `h-11` gives the 44 px floor on every pointer, not only a coarse
              one: this is the dialog's primary action and it is used at the chair.
            */}
            <div className="space-y-2">
              {/*
                ⚠️ The Badge that used to sit here is gone: the *selected button* is the badge. Two live
                statements of one value in one box is how they end up disagreeing, and the second one bought
                nothing — a pastille reading « Terminé » directly above a highlighted « Terminé » button says
                the same thing twice. The statut it now appears in is the récapitulatif, which is a different
                claim (what the visit currently *is*, beside what the form would save).
              */}
              <Label className="text-sm">Statut</Label>
              <div role="radiogroup" aria-label="Statut du rendez-vous" className="grid grid-cols-2 gap-2 sm:grid-cols-3">
                {statusOptions.map((s) => {
                  const value = s.toLowerCase()
                  const active = value === status
                  return (
                    <Button
                      key={s}
                      type="button"
                      role="radio"
                      aria-checked={active}
                      variant={active ? "default" : "outline"}
                      onClick={() => setStatus(value)}
                      disabled={loading}
                      className={cn("h-11", !active && "bg-card")}
                    >
                      {appointmentStatusLabel(s)}
                    </Button>
                  )
                })}
              </div>
              {/* Nothing is saved until the footer's button is pressed, and a control that looks like a toggle
                  invites the opposite belief. */}
              {status !== hydratedStatus && (
                <p role="status" className="text-xs text-muted-foreground">
                  Statut modifié — enregistrez pour l&apos;appliquer.
                </p>
              )}
            </div>

            {/*
              The « Informations du patient » card is gone — see the identity line in the header for why. The
              patient's name is still held in state: the cancellation confirmation names them.
            */}

            {/* Quand. */}
            <div className="space-y-3">
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                {/* Date Picker */}
                <div className="space-y-2">
                  <Label htmlFor="edit-appt-date" className="text-sm">Date *</Label>
                  <Popover modal>
                    <PopoverTrigger asChild>
                      <Button
                        id="edit-appt-date"
                        variant="outline"
                        className={cn(
                          // `bg-card` — see the same control in the create dialog: `outline` is `bg-background`,
                          // which is the surface this dialog paints on.
                          "w-full h-10 justify-start bg-card text-left font-normal",
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

              {/* Durée — see the create dialog for why this is a label and its presets rather than a bordered
                  sub-row with a Switch at the far edge. */}
              <div className="space-y-2">
                <Label className="text-sm">Durée</Label>
                {useEndTime ? (
                  <div className="space-y-2 sm:max-w-[50%]">
                    <Label htmlFor="edit-appt-end-time" className="sr-only">Heure de fin *</Label>
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
                  /* Two rows of three below `sm:` — see the note on the same control in
                     `create-appointment-dialog.tsx`. `flex-1` loses to `buttonVariants`' `shrink-0`, so these
                     six presets could neither shrink nor wrap and clipped « 2h » off a 390px phone. */
                  <div className="grid grid-cols-3 gap-2 sm:flex sm:flex-wrap">
                    {/* The summed length as its own active chip when it is not a preset — see the create
                        dialog. Pressing it is « garder cette longueur », so it also stops the act-sum from
                        re-proposing over it. */}
                    {!DURATION_PRESETS.includes(calculatedDuration) && calculatedDuration > 0 && (
                      <Button
                        type="button"
                        variant="default"
                        size="sm"
                        onClick={() => { setDurationTouched(true); setDuration(String(calculatedDuration)) }}
                        className="sm:flex-1"
                        disabled={loading}
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
                        onClick={() => { setDurationTouched(true); setDuration(String(mins)) }}
                        className={cn("sm:flex-1", duration !== String(mins) && "bg-card")}
                        disabled={loading}
                      >
                        {mins < 60 ? `${mins}m` : `${mins / 60}h`}
                      </Button>
                    ))}
                  </div>
                )}
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
              {/* ⚠️ The overlap warning and its ~60 px reserved slot are gone from here — the clash now has a
                  permanent home in the récapitulatif. See `appointment-recap.tsx`. */}
            </div>

            {/* Les actes, puis le praticien — the acts first, for the reason the create dialog states. */}
            <div className="space-y-4">
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

              <div className="grid grid-cols-1 gap-4">
                <div className="space-y-2">
                  <Label htmlFor="doctor" className="text-sm">
                    Praticien
                  </Label>
                  <Select
                    value={selectedDoctorId || UNASSIGNED_DOCTOR}
                    onValueChange={(value) =>
                      setSelectedDoctorId(value === UNASSIGNED_DOCTOR ? "" : value)
                    }
                    disabled={loadingDoctors || loading}
                  >
                    <SelectTrigger className="h-10 w-full" id="doctor">
                      <SelectValue placeholder={loadingDoctors ? "Chargement des médecins…" : doctors.length === 0 ? "Aucun médecin enregistré" : "Choisir un médecin…"} />
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
              </div>
            </div>

            {/* Facturation (AC-P6.13) — a visit says which note d'honoraires bills it, and offers to raise one
                when it does not. The link was write-only before: the column existed, nothing populated it, and
                no screen read it, so « cette consultation a-t-elle été facturée ? » had no answer anywhere. */}
            {/*
              One line rather than a titled card. The *action* stays in this column deliberately: the
              récapitulatif states the billing status too, but a pane that disappears below `lg:` must never be
              the only place an action lives (`appointment-recap.tsx`).
            */}
            {source?.patientId && (
              <div className="flex flex-wrap items-center gap-x-2 gap-y-1 border-t pt-3 text-sm">
                <Receipt className="h-4 w-4 shrink-0 text-muted-foreground" />
                {source.invoiceId ? (
                  <>
                    <span className="font-medium">Facturé</span>
                    <span className="text-muted-foreground">
                      {source.invoiceNumber
                        ? `· n° ${source.invoiceNumber}`
                        : /* A draft consumes no number yet — say so rather than printing an empty « n° ». */
                          "· brouillon de note d'honoraires"}
                    </span>
                    <Button asChild type="button" variant="link" size="sm" className="h-auto p-0 text-xs">
                      <Link href="/factures">Ouvrir dans « Factures »</Link>
                    </Button>
                  </>
                ) : (
                  <>
                    <span className="text-muted-foreground">Non facturé</span>
                    <Button
                      type="button"
                      variant="link"
                      size="sm"
                      className="h-auto p-0 text-xs"
                      onClick={() => setBillingOpen(true)}
                      disabled={loading}
                    >
                      Facturer cette consultation
                    </Button>
                  </>
                )}
              </div>
            )}

            {/* Notes, folded away — but opened on hydration when this visit already carries one. */}
            <div className="border-t pt-3">
              <button
                type="button"
                onClick={() => setShowNotes((v) => !v)}
                aria-expanded={showNotes}
                aria-controls="edit-appt-notes"
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
                <div id="edit-appt-notes" className="pt-2">
                  <Textarea
                    placeholder="Ajouter des notes ou des instructions particulières…"
                    value={notes}
                    onChange={(e) => setNotes(e.target.value)}
                    className="min-h-[80px] resize-none"
                    disabled={loading}
                  />
                </div>
              )}
            </div>

            {/*
              ⚠️ The refusal belongs at the END of this scrolling body — it used to sit at the top, five
              sections up, which is the whole of the reported « the modal does not close and I don't
              understand the error »: every refusal here leaves the dialog open **by design** (that is what
              lets you correct the time and retry), and the sentence explaining why was off-screen.

              Moving it down was necessary but not sufficient: « Enregistrer » is in a PINNED footer, outside
              this scroller, so the banner is still below the fold unless you happen to be scrolled to the
              bottom. `FormErrorBanner` therefore scrolls itself into view — do not re-solve it by hand here.
            */}
            <FormErrorBanner message={error} />

            </div>

            {/* The pane, and the two read-only sections only this dialog has. Both are *statements*; the
                actions behind them (the statut buttons, « Facturer ») stay in the form column. */}
            <AppointmentRecap model={recapModel} variant="rail" className="hidden w-[272px] shrink-0 lg:flex">
              <AppointmentRecapSection title="Statut">
                <div className="flex flex-wrap items-center gap-2">
                  <Badge variant="secondary" className={appointmentStatusBadgeClass(status)}>
                    {appointmentStatusLabel(status)}
                  </Badge>
                  {status !== hydratedStatus && (
                    <span className="text-2xs text-muted-foreground">non enregistré</span>
                  )}
                </div>
              </AppointmentRecapSection>
              {source?.patientId && (
                <AppointmentRecapSection title="Facturation">
                  <p className="text-xs text-muted-foreground">
                    {source.invoiceId
                      ? source.invoiceNumber
                        ? `Facturé — n° ${source.invoiceNumber}`
                        : "Facturé — brouillon"
                      : "Non facturé"}
                  </p>
                </AppointmentRecapSection>
              )}
            </AppointmentRecap>
            </div>

            <AppointmentRecap model={recapModel} variant="bar" className="lg:hidden" />

            {/*
              ⚠️ `flex-col` is REMOVED, and that is the fix.

              `DialogFooter`'s base is `flex-col-reverse`, and `flex-col` belongs to the same tailwind-merge
              group — so passing it here silently cancelled the reverse and the three actions stacked in DOM
              order on every phone: « Annuler le rendez-vous » (destructive) first, « Fermer », then
              « Enregistrer les modifications » last. That put the irreversible action closest to the thumb and
              the primary one furthest from it.

              ⚠️ **And « Annuler le rendez-vous » is no longer one of them.** It was a *second door* to a state
              the statut buttons at the top of this form already offer — « Annulé » is one of them — so the
              dialog presented one outcome twice, once in red at the far edge of the footer where the thumb
              lands. On a phone the three stacked full-width buttons cost ~150 px of an 844 px screen with the
              destructive-looking one nearest the thumb and under the assistant's launcher. It lives in the
              « ⋯ » menu now: still one press away, no longer competing with Enregistrer, and the confirmation
              it opens is unchanged.
            */}
            <DialogFooter className="flex-shrink-0 gap-2 border-t bg-background px-6 py-4">
              {/*
                ⚠️ One flex item below `sm:`, dissolved by `sm:contents` above it. `DialogFooter` is
                `flex-col-reverse` on a phone, so three children are three full-width rows — which is how this
                footer got to ~150 px of an 844 px screen in the first place. Grouping « ⋯ » with « Fermer »
                makes it two rows: the primary action, then the secondary pair sharing one. `contents` at `sm:`
                removes the wrapper from the box tree entirely, so the trigger's own `sm:mr-auto` still pushes
                it to the far edge of the desktop row.
              */}
              <div className="flex gap-2 sm:contents">
                <DropdownMenu>
                  <DropdownMenuTrigger asChild>
                    <Button
                      type="button"
                      variant="outline"
                      size="icon"
                      // `bg-card` for the reason the date trigger states — the footer is `bg-background` too, so
                      // an `outline` trigger there is painted the surface it sits on and reads as loose dots.
                      /*
                       * ⚠️ `order-2` below `sm:` puts it on the RIGHT of the pair, and that is not cosmetic:
                       * the assistant's launcher is a fixed circle at the bottom-LEFT, and it sits exactly on
                       * this row. On the left the trigger is covered — and since « Annuler le rendez-vous »
                       * now lives only behind it, an unreachable trigger is an unreachable action.
                       */
                      className="touch-target order-2 shrink-0 bg-card sm:order-none sm:mr-auto"
                      aria-label="Autres actions"
                      disabled={loading}
                    >
                      <MoreHorizontal className="h-4 w-4" />
                    </Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="start">
                    <DropdownMenuItem
                      variant="destructive"
                      className="coarse:py-3"
                      disabled={loading || status === "cancelled"}
                      onSelect={() => setShowCancelDialog(true)}
                    >
                      <X className="h-4 w-4" />
                      Annuler le rendez-vous
                    </DropdownMenuItem>
                    {/*
                      ⚠️ Not the same outcome as the item above, and that is the whole reason it exists. « Annulé »
                      is a statement about a patient who was expected and did not come — the dashboard counts it in
                      the « taux d'absence » alongside « Absent ». A séance typed into the wrong day, or onto the
                      wrong patient, is neither: cancelling it to tidy the agenda is what makes a cabinet's own
                      figures describe a month it did not have. This one asserts nothing.

                      Wording: « Supprimer », because that is what the user came to do, and nothing they can reach
                      contradicts it — the séance leaves the agenda, the patient's history and the figures. The
                      description says where it can be recovered rather than calling the action something else.
                    */}
                    {/*
                      ⚠️ Not offered on a « créneau occupé », and the reason is the recovery promise rather than the
                      deletion. « À clôturer › séances retirées » is the only screen that lists the mark, and it
                      reads séances with a patient — a blocked slot has nothing to close, so a retired one would
                      leave the agenda and appear nowhere. Offering an irreversible removal under a dialog that says
                      it can be undone is the worse half of that trade, so the control is absent here; a blocked
                      slot is re-drawn in one drag.
                    */}
                    {source?.patientId && (
                      <DropdownMenuItem
                        variant="destructive"
                        className="coarse:py-3"
                        disabled={loading}
                        onSelect={() => setShowDeleteDialog(true)}
                      >
                        <Trash2 className="h-4 w-4" />
                        Supprimer (créé par erreur)
                      </DropdownMenuItem>
                    )}
                  </DropdownMenuContent>
                </DropdownMenu>
                <Button
                  type="button"
                  variant="outline"
                  className="order-1 flex-1 bg-card sm:order-none sm:flex-none"
                  onClick={() => guard.onOpenChange(false)}
                  disabled={loading}
                >
                  Fermer
                </Button>
              </div>
              {/* No longer disabled on an overlap: the collision is advisory and the server offers the override.
                  Blocking here made the warning a dead end and hid the fact that proceeding is allowed. */}
              <Button type="submit" disabled={loading}>
                <Save className="h-4 w-4 mr-2" />
                {loading ? "Enregistrement…" : "Enregistrer"}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>

      {/* The invoice draft is raised from an appointment context, so it carries `appointmentId` (AC-P6.12).
          Rendered as a sibling of the dialog rather than inside it — nesting a Dialog inside a Dialog fights
          over the focus trap. */}
      {source?.patientId && (
        <InvoiceFormModal
          open={billingOpen}
          onOpenChange={setBillingOpen}
          presetPatientId={source.patientId}
          presetPatientName={source.patientName}
          appointmentId={source.id}
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

      {/* « Supprimer (créé par erreur) » — names the patient and the slot, because « Êtes-vous sûr ? » cannot say
          which of a day's séances is about to leave the agenda (§ 13). */}
      <AlertDialog open={showDeleteDialog} onOpenChange={setShowDeleteDialog}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Supprimer ce rendez-vous ?</AlertDialogTitle>
            <AlertDialogDescription>
              {deletionTarget} quittera l&apos;agenda et le dossier du patient, et ne comptera pas comme une
              annulation dans le taux d&apos;absence. Vous pourrez le récupérer dans {quoteFr("À clôturer")} ›
              séances retirées.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={loading}>Non, conserver</AlertDialogCancel>
            <AlertDialogAction
              onClick={handleDeleteAppointment}
              disabled={loading}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              {loading ? "Suppression…" : "Oui, supprimer"}
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
      <DiscardChangesDialog guard={guard} />
    </>
  )
}


