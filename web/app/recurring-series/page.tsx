"use client"

import type React from "react"

import { useCallback, useEffect, useState } from "react"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { DEFAULT_PAGE_SIZE, emptyPage, type PagedResponse } from "@/lib/api/paging"

import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useRouter } from "next/navigation"
import { format } from "date-fns"
import { fr } from "date-fns/locale"
import { toast } from "sonner"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { PageHeader } from "@/components/ui/page-header"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Textarea } from "@/components/ui/textarea"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { CardList, CARDS_ONLY, TABLE_ONLY } from "@/components/ui/card-list"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
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
import { AlertTriangle, Plus, Repeat } from "lucide-react"
import { EmptyState } from "@/components/ui/empty-state"
import { appointmentsApi, type CreateRecurringSeriesPayload } from "@/lib/api/appointments"
import { patientsApi } from "@/lib/api/patients"
import { useDoctors } from "@/lib/hooks/use-doctors"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import { ApiError } from "@/lib/api/client"
import type { PatientDto, ProcedureTypeDto, RecurringAppointmentDto } from "@/lib/api/types"

// ---- Helpers ---------------------------------------------------------------------------------------

const FREQUENCY_OPTIONS: { value: string; label: string }[] = [
  { value: "Daily", label: "Quotidienne" },
  { value: "Weekly", label: "Hebdomadaire" },
  { value: "Monthly", label: "Mensuelle" },
]

// Sentinel for "no procedure type" — shadcn Select cannot use an empty-string item value.
const NO_PROCEDURE = "none"

const frequencyLabel = (pattern: string): string =>
  FREQUENCY_OPTIONS.find((o) => o.value === pattern)?.label ?? pattern

const patientLabel = (p: PatientDto): string => `${p.firstName} ${p.lastName}`.trim()

const formatDateTime = (iso: string | null | undefined): string => {
  if (!iso) return "—"
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? "—" : format(d, "dd/MM/yyyy HH:mm", { locale: fr })
}

const formatDate = (iso: string): string => {
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? "—" : format(d, "dd/MM/yyyy", { locale: fr })
}

// The series' end is either an explicit date, a fixed number of occurrences, or open-ended.
const formatEnd = (s: RecurringAppointmentDto): string => {
  if (s.endDate) return `Jusqu'au ${formatDate(s.endDate)}`
  if (s.occurrenceCount != null) return `${s.occurrenceCount} occurrences`
  return "—"
}

/** Column widths the loading skeleton mirrors, in the table's own order (8 columns). */
const SERIES_COLUMN_WIDTHS = [
  "w-[20%]", "w-[14%]", "w-[9%]", "w-[15%]", "w-[15%]", "w-[10%]", "w-[8%]", "w-[9%]",
] as const

const intervalLabel = (pattern: string, interval: number): string => {
  if (interval <= 1) return frequencyLabel(pattern)
  const unit =
    pattern === "Daily" ? "jours" : pattern === "Weekly" ? "semaines" : pattern === "Monthly" ? "mois" : "fois"
  return `toutes les ${interval} ${unit}`
}

// ---- New-series dialog (same-file helper) ----------------------------------------------------------

type EndMode = "count" | "date"

interface NewSeriesDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  patients: PatientDto[]
  procedureTypes: ProcedureTypeDto[]
  onCreated: () => void
}

function NewSeriesDialog({ open, onOpenChange, patients, procedureTypes, onCreated }: NewSeriesDialogProps) {
  /*
   * The practitioner, and it is **required** — exactly as it is in the single-appointment dialog.
   *
   * ⚠️ This field did not exist, and its absence was a blocker rather than an omission. `doctorId` was therefore
   * always null in the payload, and both of the server's collision branches were gated on `DoctorId.HasValue`, so a
   * twelve-week series booked straight over twelve existing patients and reported a clean run. The database could
   * not catch it either: the exclusion constraint is predicated on `DoctorId IS NOT NULL`. L2b fixed the server so a
   * series with no practitioner is checked clinic-wide, but a *recurring* booking with no named dentist is not a
   * thing a clinic means — it is what a « créneau occupé » block is for — so this asks.
   */
  const { doctors } = useDoctors()
  const [doctorId, setDoctorId] = useState("")
  const [patientId, setPatientId] = useState("")
  const [startDateTime, setStartDateTime] = useState("")
  const [durationMinutes, setDurationMinutes] = useState("30")
  const [frequency, setFrequency] = useState("Weekly")
  const [interval, setInterval] = useState("1")
  const [endMode, setEndMode] = useState<EndMode>("count")
  const [occurrenceCount, setOccurrenceCount] = useState("")
  const [endDate, setEndDate] = useState("")
  const [procedureTypeId, setProcedureTypeId] = useState(NO_PROCEDURE)
  const [notes, setNotes] = useState("")
  const [errors, setErrors] = useState<Record<string, string>>({})
  const router = useRouter()
  const [saving, setSaving] = useState(false)
  /**
   * What the last create actually did, when some occurrences were skipped (AC-P1.36). Held instead of closing
   * so the dates survive; null = the form is showing.
   */
  const [outcome, setOutcome] = useState<SeriesOutcome | null>(null)

  /**
   * « Replanifier » for one skipped occurrence (AC-P1.37): close this dialog and open the ordinary create
   * dialog pre-filled with that date, so the dentist picks a new slot with the patient and act already set.
   * Deep-linked through the appointments page rather than mounting a second create dialog here — that page
   * already owns `CreateAppointmentDialog` and its `?patientId=` entry point.
   */
  const onReschedule = (iso: string) => {
    const params = new URLSearchParams({ patientId, at: iso })
    setOutcome(null)
    onOpenChange(false)
    router.push(`/appointments?${params.toString()}`)
  }

  // Reset the form whenever the dialog is (re)opened.
  useEffect(() => {
    if (!open) return
    setDoctorId("")
    setPatientId("")
    setStartDateTime("")
    setDurationMinutes("30")
    setFrequency("Weekly")
    setInterval("1")
    setEndMode("count")
    setOccurrenceCount("")
    setEndDate("")
    setProcedureTypeId(NO_PROCEDURE)
    setNotes("")
    setErrors({})
  }, [open])

  const validate = (): boolean => {
    const next: Record<string, string> = {}
    if (!patientId) next.patientId = "Sélectionnez un patient"
    // Required, for the reason stated on `doctorId` above: a series is the writer that books the most slots, so it
    // is the last one that should be exempt from the double-booking check.
    if (!doctorId) next.doctorId = "Sélectionnez un praticien"
    if (!startDateTime) next.startDateTime = "La date de début est requise"
    if (durationMinutes === "" || Number(durationMinutes) <= 0) next.durationMinutes = "Durée invalide"
    if (interval === "" || Number(interval) < 1) next.interval = "L'intervalle doit être au moins 1"
    if (endMode === "count" && (occurrenceCount === "" || Number(occurrenceCount) < 1)) {
      next.occurrenceCount = "Indiquez un nombre d'occurrences (au moins 1)"
    }
    if (endMode === "date" && !endDate) next.endDate = "Indiquez une date de fin"
    setErrors(next)
    return Object.keys(next).length === 0
  }

  /**
   * Create the series. `allowOverlap` is the confirmed override for the colliding occurrences the outcome panel
   * has just listed — a second run that books them anyway rather than skipping them.
   *
   * ⚠️ `allowOverlap` was declared on `CreateRecurringSeriesPayload` with **no counterpart on the command**, so it
   * had always been dropped on the floor; L2b wired the server side, and this is its caller. A flag with no caller
   * is the failure `Clinic.SetStockExpiryLeadDays` is remembered for, and shipping the wiring without the button
   * would have repeated it.
   */
  const submit = async (allowOverlap: boolean) => {
    const payload: CreateRecurringSeriesPayload = {
      patientId,
      doctorId,
      startDateTime: new Date(startDateTime).toISOString(),
      durationMinutes: Number(durationMinutes),
      frequency,
      interval: Number(interval),
      endDate: endMode === "date" && endDate ? new Date(endDate).toISOString() : null,
      occurrenceCount: endMode === "count" && occurrenceCount !== "" ? Number(occurrenceCount) : null,
      procedureTypeId: procedureTypeId !== NO_PROCEDURE ? procedureTypeId : null,
      notes: notes.trim() || null,
      allowOverlap: allowOverlap || undefined,
    }

    try {
      setSaving(true)
      const result = await appointmentsApi.createRecurring(payload)
      toast.success(`${result.createdCount} rendez-vous créés`)
      onCreated()

      // On a forced re-run the conflicts were created rather than skipped, so listing them again would offer
      // « Replanifier » for visits that now exist. Close instead — the toast already says how many were booked.
      if (allowOverlap) {
        setOutcome(null)
        onOpenChange(false)
        return
      }

      // AC-P1.36/1.37: the skipped dates used to be reduced to a COUNT in a toast, and the dialog closed
      // immediately — so the one piece of information the dentist needs (which dates did not get booked) was
      // discarded. Keep the dialog open on the outcome panel instead, listing each date with a « Replanifier »
      // action; close only when nothing was skipped.
      const skipped = [...(result.conflicts ?? []), ...(result.outsideWorkingHours ?? [])]
      if (skipped.length === 0) {
        onOpenChange(false)
        return
      }
      setOutcome({
        createdCount: result.createdCount,
        skippedPastCount: result.skippedPastCount,
        conflicts: result.conflicts ?? [],
        outsideWorkingHours: result.outsideWorkingHours ?? [],
      })
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la création de la série")
    } finally {
      setSaving(false)
    }
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!validate()) return
    await submit(false)
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[90dvh] overflow-y-auto md:max-w-lg">
        <DialogHeader>
          <DialogTitle>Nouvelle série récurrente</DialogTitle>
          <DialogDescription>Planifiez une série de rendez-vous répétés pour un patient.</DialogDescription>
        </DialogHeader>

        {/*
          AC-P1.36/1.37 — the outcome panel. `conflicts` came back from the API and the UI reduced it to a
          count in a toast, then closed the dialog: the dates were thrown away, so a dentist could not act on
          them. Conflicts and out-of-hours dates are listed separately because the remedies differ — a clash
          needs another slot, a closed day needs different hours or a confirmed override.
        */}
        {outcome ? (
          <div className="space-y-4" role="status" aria-live="polite">
            <p className="text-sm">
              <span className="font-semibold">{outcome.createdCount}</span> rendez-vous créés.
              {outcome.skippedPastCount > 0 && (
                <> {outcome.skippedPastCount} ignoré(s) car déjà passé(s).</>
              )}
            </p>

            {outcome.conflicts.length > 0 && (
              <div className="space-y-2">
                <p className="text-sm font-medium">
                  {outcome.conflicts.length} créneau(x) déjà réservé(s) :
                </p>
                <ul className="space-y-1">
                  {outcome.conflicts.map((iso) => (
                    <li key={iso} className="flex items-center justify-between gap-2 rounded-md border px-3 py-1.5">
                      <span className="text-sm">{formatDateTime(iso)}</span>
                      <Button type="button" variant="outline" size="sm" onClick={() => onReschedule(iso)}>
                        Replanifier
                      </Button>
                    </li>
                  ))}
                </ul>
              </div>
            )}

            {outcome.outsideWorkingHours.length > 0 && (
              <div className="space-y-2">
                <p className="text-sm font-medium">
                  {outcome.outsideWorkingHours.length} date(s) en dehors des horaires d&apos;ouverture :
                </p>
                <ul className="space-y-1">
                  {outcome.outsideWorkingHours.map((iso) => (
                    <li key={iso} className="flex items-center justify-between gap-2 rounded-md border px-3 py-1.5">
                      <span className="text-sm">{formatDateTime(iso)}</span>
                      <Button type="button" variant="outline" size="sm" onClick={() => onReschedule(iso)}>
                        Replanifier
                      </Button>
                    </li>
                  ))}
                </ul>
              </div>
            )}

            <DialogFooter className="gap-2 sm:gap-2">
              {/* The confirmed override, offered only where there is something to override. « Replanifier » above
                  remains the recommended remedy — this is the second chair / the emergency squeezed in, and the
                  server records each such row as a deliberate overlap rather than an accident. */}
              {outcome.conflicts.length > 0 && (
                <Button type="button" variant="outline" disabled={saving} onClick={() => void submit(true)}>
                  Créer malgré {outcome.conflicts.length} conflit{outcome.conflicts.length > 1 ? "s" : ""}
                </Button>
              )}
              <Button type="button" onClick={() => { setOutcome(null); onOpenChange(false) }}>
                Terminé
              </Button>
            </DialogFooter>
          </div>
        ) : (
        <form onSubmit={handleSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="patient">
              Patient <span className="text-destructive">*</span>
            </Label>
            <Select value={patientId} onValueChange={setPatientId}>
              <SelectTrigger id="patient" className="w-full">
                <SelectValue placeholder="Sélectionner un patient" />
              </SelectTrigger>
              <SelectContent>
                {patients.map((p) => (
                  <SelectItem key={p.id} value={p.id}>
                    {patientLabel(p)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            {errors.patientId && <p className="text-xs text-destructive">{errors.patientId}</p>}
          </div>

          <div className="space-y-2">
            <Label htmlFor="doctor">
              Praticien <span className="text-destructive">*</span>
            </Label>
            <Select value={doctorId} onValueChange={setDoctorId}>
              <SelectTrigger id="doctor" className="w-full">
                <SelectValue placeholder="Sélectionner un praticien" />
              </SelectTrigger>
              <SelectContent>
                {doctors
                  .filter((d) => d.id)
                  .map((d) => (
                    <SelectItem key={d.id} value={d.id!}>
                      {d.name}
                    </SelectItem>
                  ))}
              </SelectContent>
            </Select>
            {/* Says why it is required, because « pourquoi dois-je choisir ? » is a fair question on a field that
                did not exist yesterday — and the answer is the whole point of L2b. */}
            <p className="text-xs text-muted-foreground">
              Requis : c&apos;est ce qui permet de détecter les créneaux déjà réservés.
            </p>
            {errors.doctorId && <p className="text-xs text-destructive">{errors.doctorId}</p>}
          </div>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="startDateTime">
                Date et heure de début <span className="text-destructive">*</span>
              </Label>
              <Input
                id="startDateTime"
                type="datetime-local"
                value={startDateTime}
                onChange={(e) => setStartDateTime(e.target.value)}
              />
              {errors.startDateTime && <p className="text-xs text-destructive">{errors.startDateTime}</p>}
            </div>

            <div className="space-y-2">
              <Label htmlFor="duration">
                Durée (minutes) <span className="text-destructive">*</span>
              </Label>
              <Input
                id="duration"
                type="number"
                min="1"
                placeholder="30"
                value={durationMinutes}
                onChange={(e) => setDurationMinutes(e.target.value)}
              />
              {errors.durationMinutes && <p className="text-xs text-destructive">{errors.durationMinutes}</p>}
            </div>
          </div>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="frequency">
                Fréquence <span className="text-destructive">*</span>
              </Label>
              <Select value={frequency} onValueChange={setFrequency}>
                <SelectTrigger id="frequency" className="w-full">
                  <SelectValue placeholder="Fréquence" />
                </SelectTrigger>
                <SelectContent>
                  {FREQUENCY_OPTIONS.map((o) => (
                    <SelectItem key={o.value} value={o.value}>
                      {o.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="space-y-2">
              <Label htmlFor="interval">
                Intervalle <span className="text-destructive">*</span>
              </Label>
              <Input
                id="interval"
                type="number"
                min="1"
                placeholder="1"
                value={interval}
                onChange={(e) => setInterval(e.target.value)}
              />
              <p className="text-xs text-muted-foreground">{intervalLabel(frequency, Number(interval) || 1)}</p>
              {errors.interval && <p className="text-xs text-destructive">{errors.interval}</p>}
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="endMode">Fin de la série</Label>
            <Select value={endMode} onValueChange={(v) => setEndMode(v as EndMode)}>
              <SelectTrigger id="endMode" className="w-full">
                <SelectValue placeholder="Type de fin" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="count">Nombre d'occurrences</SelectItem>
                <SelectItem value="date">Jusqu'au</SelectItem>
              </SelectContent>
            </Select>

            {endMode === "count" ? (
              <div className="space-y-2 pt-1">
                <Input
                  id="occurrenceCount"
                  type="number"
                  min="1"
                  placeholder="Nombre d'occurrences"
                  value={occurrenceCount}
                  onChange={(e) => setOccurrenceCount(e.target.value)}
                />
                {errors.occurrenceCount && <p className="text-xs text-destructive">{errors.occurrenceCount}</p>}
              </div>
            ) : (
              <div className="space-y-2 pt-1">
                <Input id="endDate" type="date" value={endDate} onChange={(e) => setEndDate(e.target.value)} />
                {errors.endDate && <p className="text-xs text-destructive">{errors.endDate}</p>}
              </div>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="procedureType">Type d'acte</Label>
            <Select value={procedureTypeId} onValueChange={setProcedureTypeId}>
              <SelectTrigger id="procedureType" className="w-full">
                <SelectValue placeholder="Optionnel" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={NO_PROCEDURE}>Aucun</SelectItem>
                {procedureTypes.map((pt) => (
                  <SelectItem key={pt.id} value={pt.id}>
                    {pt.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-2">
            <Label htmlFor="notes">Notes</Label>
            <Textarea
              id="notes"
              placeholder="Optionnel"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              rows={2}
            />
          </div>

          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={saving}>
              Annuler
            </Button>
            <Button type="submit" disabled={saving}>
              {saving ? "Création..." : "Créer la série"}
            </Button>
          </DialogFooter>
        </form>
        )}
      </DialogContent>
    </Dialog>
  )
}

// ---- Page ------------------------------------------------------------------------------------------

/** The per-occurrence result of a series create, kept so skipped dates can be listed and re-booked. */
interface SeriesOutcome {
  createdCount: number
  skippedPastCount: number
  /** ISO dates that clashed with an existing booking — need a different slot. */
  conflicts: string[]
  /** ISO dates outside the practitioner's working hours — need different hours, or a confirmed override. */
  outsideWorkingHours: string[]
}

export default function RecurringSeriesPage() {
  const [seriesPage, setSeriesPage] = useState<PagedResponse<RecurringAppointmentDto>>(
    () => emptyPage<RecurringAppointmentDto>(),
  )
  const series = seriesPage.items
  const [search, setSearch] = useState("")
  const [debouncedSearch, setDebouncedSearch] = useState("")
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)

  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [patients, setPatients] = useState<PatientDto[]>([])
  const [procedureTypes, setProcedureTypes] = useState<ProcedureTypeDto[]>([])
  const [dialogOpen, setDialogOpen] = useState(false)
  const [cancelTarget, setCancelTarget] = useState<RecurringAppointmentDto | null>(null)
  const [cancelOpen, setCancelOpen] = useState(false)
  const [cancelling, setCancelling] = useState(false)

  const loadSeries = useCallback(async () => {
    try {
      setLoading(true)
      setError(null)
      const data = await appointmentsApi.listRecurringPaged({
        page,
        pageSize,
        search: debouncedSearch || undefined,
        activeOnly: true,
      })
      setSeriesPage(data)
    } catch (err) {
      const message = err instanceof ApiError ? err.message : "Échec du chargement des séries"
      setError(message)
      toast.error(message)
    } finally {
      setLoading(false)
    }
  }, [page, pageSize, debouncedSearch])

  // Debounced so a search does not fire a request per keystroke.
  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search.trim()), 300)
    return () => clearTimeout(timer)
  }, [search])

  // A new term (or filter) must not leave the table on a page the narrowed result set no longer has.
  useEffect(() => {
    setPage(1)
  }, [debouncedSearch])

  useEffect(() => {
    loadSeries()
  }, [loadSeries])

  // AC-P4.21/4.26 — a series and its expanded rendez-vous are the same data seen two ways, and both live under
  // Features/Appointments/Commands, so creating or cancelling a series anywhere emits `appointments`.
  useClinicRealtime(RealtimeResource.Appointments, loadSeries)

  // Reference data for the create form — fetched once. Failures are non-blocking (the list still shows).
  useEffect(() => {
    let active = true
    ;(async () => {
      try {
        const [patientsData, procedureTypesData] = await Promise.all([patientsApi.list(), procedureTypesApi.list()])
        if (!active) return
        setPatients(patientsData)
        setProcedureTypes(procedureTypesData)
      } catch (err) {
        if (!active) return
        toast.error(err instanceof ApiError ? err.message : "Échec du chargement des données du formulaire")
      }
    })()
    return () => {
      active = false
    }
  }, [])

  const handleCancel = (s: RecurringAppointmentDto) => {
    setCancelTarget(s)
    setCancelOpen(true)
  }

  /**
   * The two empty facts kept apart. The filtered branch stays a short line plus a way back — offering
   * « Nouvelle série » there would invite a duplicate of a series the search simply failed to match.
   */
  const renderEmpty = (size: "default" | "compact") =>
    debouncedSearch !== "" ? (
      <div className="flex flex-col items-center gap-2 py-2">
        <p className="text-sm text-muted-foreground">Aucune série ne correspond à votre recherche</p>
        <Button variant="outline" size="sm" onClick={() => setSearch("")}>
          Effacer la recherche
        </Button>
      </div>
    ) : (
      <EmptyState
        icon={Repeat}
        size={size}
        title="Aucune série récurrente"
        description="Pour un suivi qui revient à intervalle fixe — contrôle orthodontique, détartrage semestriel — créez la série une fois et l'application place tous les rendez-vous."
        action={
          <Button onClick={() => setDialogOpen(true)} className="gap-2">
            <Plus className="h-4 w-4" />
            Nouvelle série
          </Button>
        }
      />
    )

  const confirmCancel = async () => {
    if (!cancelTarget) return
    try {
      setCancelling(true)
      const result = await appointmentsApi.cancelRecurring(cancelTarget.id, "WholeSeries")
      toast.success(`${result.cancelled} rendez-vous annulés`)
      setCancelOpen(false)
      setCancelTarget(null)
      await loadSeries()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de l'annulation de la série")
    } finally {
      setCancelling(false)
    }
  }

  return (
    <ClinicGuard>
      <AppShell contentClassName="space-y-6">
        {/*
          The row could not wrap against a ~180px button on a 390px screen. No `zone` either: `PageHeader`
          derives it from the route (`/recurring-series` is « Quotidien »), and « Clinique » here contradicted
          the rail.
        */}
        <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <PageHeader
            title="Rendez-vous récurrents"
            subtitle="Séries de rendez-vous répétés — planification et annulation."
          />

          <Button onClick={() => setDialogOpen(true)} className="gap-2">
            <Plus className="h-4 w-4" />
            Nouvelle série
          </Button>
        </div>

        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Repeat className="h-5 w-5" />
              Séries actives
              <Badge variant="secondary" className="ml-2">
                {seriesPage.totalCount}
              </Badge>
            </CardTitle>
          </CardHeader>
          <CardContent>
            {/* Retry banner (finding #2) + `loading` routed into the list rather than a lone spinner the full
                table then replaces (finding #3). Shape from `dashboard/dashboard-section.tsx`. */}
            {error ? (
              <div
                role="status"
                className="flex flex-wrap items-center gap-3 rounded-lg border border-destructive/40 bg-destructive-wash p-3 text-sm"
              >
                <AlertTriangle className="h-4 w-4 shrink-0 text-destructive" aria-hidden="true" />
                <span className="min-w-0 flex-1">{error}</span>
                <Button size="sm" variant="outline" onClick={loadSeries}>
                  Réessayer
                </Button>
              </div>
            ) : (
              <div>
                <div className="mb-4">
                  <Label htmlFor="series-search" className="sr-only">
                    Rechercher une série
                  </Label>
                  <Input
                    id="series-search"
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    placeholder="Rechercher une série (patient, praticien, notes)…"
                  />
                </div>
                <CardList
                  className={CARDS_ONLY}
                  ariaLabel="Séries de rendez-vous récurrents"
                  items={series}
                  loading={loading}
                  empty={renderEmpty("compact")}
                  getKey={(s) => s.id}
                  title={(s) => s.patientName ?? "Patient inconnu"}
                  subtitle={(s) => frequencyLabel(s.recurrencePattern)}
                  muted={(s) => !s.isActive}
                  status={(s) => (
                    <Badge variant={s.isActive ? "default" : "secondary"}>{s.isActive ? "Actif" : "Terminé"}</Badge>
                  )}
                  fields={(s) => [
                    { label: "Début", value: formatDateTime(s.startDate) },
                    { label: "Fin", value: formatEnd(s) },
                    { label: "Intervalle", value: s.interval },
                    { label: "Rendez-vous", value: s.appointmentCount },
                  ]}
                  actions={(s) =>
                    s.isActive ? (
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => handleCancel(s)}
                        className="gap-1 text-destructive hover:text-destructive"
                      >
                        Annuler
                      </Button>
                    ) : null
                  }
                />
                <Table containerClassName={TABLE_ONLY}>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Patient</TableHead>
                      <TableHead>Fréquence</TableHead>
                      <TableHead>Intervalle</TableHead>
                      <TableHead>Début</TableHead>
                      <TableHead>Fin</TableHead>
                      <TableHead>Rendez-vous</TableHead>
                      <TableHead>Actif</TableHead>
                      <TableHead className="text-right">Actions</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {loading ? (
                      Array.from({ length: 5 }).map((_, row) => (
                        <TableRow key={`skeleton-${row}`}>
                          {SERIES_COLUMN_WIDTHS.map((width, col) => (
                            <TableCell key={col}>
                              <div
                                className={`h-5 animate-pulse rounded bg-muted ${width}`}
                                role={row === 0 && col === 0 ? "status" : undefined}
                                aria-label={row === 0 && col === 0 ? "Chargement des séries" : undefined}
                              />
                            </TableCell>
                          ))}
                        </TableRow>
                      ))
                    ) : series.length === 0 ? (
                      <TableRow>
                        <TableCell colSpan={8}>{renderEmpty("default")}</TableCell>
                      </TableRow>
                    ) : (
                    series.map((s) => (
                      <TableRow key={s.id}>
                        <TableCell className="font-medium text-foreground">
                          {s.patientName ?? "—"}
                        </TableCell>
                        <TableCell>{frequencyLabel(s.recurrencePattern)}</TableCell>
                        <TableCell className="text-muted-foreground">{s.interval}</TableCell>
                        <TableCell className="text-muted-foreground">{formatDateTime(s.startDate)}</TableCell>
                        <TableCell className="text-muted-foreground">{formatEnd(s)}</TableCell>
                        <TableCell className="text-muted-foreground">{s.appointmentCount}</TableCell>
                        <TableCell>
                          <Badge variant={s.isActive ? "default" : "secondary"}>
                            {s.isActive ? "Oui" : "Non"}
                          </Badge>
                        </TableCell>
                        <TableCell className="text-right">
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => handleCancel(s)}
                            disabled={!s.isActive}
                            className="h-8 gap-1 text-destructive hover:text-destructive"
                          >
                            Annuler la série
                          </Button>
                        </TableCell>
                      </TableRow>
                    ))
                    )}
                  </TableBody>
                </Table>
                {/* Hidden while the skeletons are up: the pager reads its counts from an empty page, so it
                    would print « Aucun … » under rows that are still loading. */}
                {!loading && (
                  <DataTablePagination
                    page={seriesPage}
                    onPageChange={setPage}
                    onPageSizeChange={setPageSize}
                    loading={loading}
                    label={["série", "séries"]}
                  />
                )}
              </div>
            )}
          </CardContent>
        </Card>

        <NewSeriesDialog
          open={dialogOpen}
          onOpenChange={setDialogOpen}
          patients={patients}
          procedureTypes={procedureTypes}
          onCreated={loadSeries}
        />
        <AlertDialog open={cancelOpen} onOpenChange={setCancelOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Annuler toute la série ?</AlertDialogTitle>
            <AlertDialogDescription>
              Tous les rendez-vous à venir de la série
              {cancelTarget?.patientName ? ` de ${cancelTarget.patientName}` : ""} seront annulés. Cette action
              est irréversible.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={cancelling}>Retour</AlertDialogCancel>
            <AlertDialogAction
              onClick={(e) => {
                e.preventDefault()
                confirmCancel()
              }}
              disabled={cancelling}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              {cancelling ? "Annulation..." : "Annuler la série"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
        </AlertDialog>
      </AppShell>
    </ClinicGuard>
  )
}
