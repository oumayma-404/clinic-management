"use client"

import type React from "react"

import { useCallback, useEffect, useState } from "react"
import { format } from "date-fns"
import { fr } from "date-fns/locale"
import { toast } from "sonner"
import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { ClinicGuard } from "@/components/clinic-guard"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
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
import { Plus, Repeat, Loader2 } from "lucide-react"
import { appointmentsApi, type CreateRecurringSeriesPayload } from "@/lib/api/appointments"
import { patientsApi } from "@/lib/api/patients"
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
  const [saving, setSaving] = useState(false)

  // Reset the form whenever the dialog is (re)opened.
  useEffect(() => {
    if (!open) return
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

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!validate()) return

    const payload: CreateRecurringSeriesPayload = {
      patientId,
      startDateTime: new Date(startDateTime).toISOString(),
      durationMinutes: Number(durationMinutes),
      frequency,
      interval: Number(interval),
      endDate: endMode === "date" && endDate ? new Date(endDate).toISOString() : null,
      occurrenceCount: endMode === "count" && occurrenceCount !== "" ? Number(occurrenceCount) : null,
      procedureTypeId: procedureTypeId !== NO_PROCEDURE ? procedureTypeId : null,
      notes: notes.trim() || null,
    }

    try {
      setSaving(true)
      const result = await appointmentsApi.createRecurring(payload)
      toast.success(`${result.createdCount} rendez-vous créés`)
      if (result.skippedPastCount > 0 || result.conflicts.length > 0) {
        toast.warning(`${result.skippedPastCount} ignorés (passé), ${result.conflicts.length} conflits`)
      }
      onOpenChange(false)
      onCreated()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la création de la série")
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[90vh] max-w-lg overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Nouvelle série récurrente</DialogTitle>
          <DialogDescription>Planifiez une série de rendez-vous répétés pour un patient.</DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="patient">
              Patient <span className="text-destructive">*</span>
            </Label>
            <Select value={patientId} onValueChange={setPatientId}>
              <SelectTrigger id="patient">
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

          <div className="grid grid-cols-2 gap-4">
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

          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label htmlFor="frequency">
                Fréquence <span className="text-destructive">*</span>
              </Label>
              <Select value={frequency} onValueChange={setFrequency}>
                <SelectTrigger id="frequency">
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
              <SelectTrigger id="endMode">
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
              <SelectTrigger id="procedureType">
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
      </DialogContent>
    </Dialog>
  )
}

// ---- Page ------------------------------------------------------------------------------------------

export default function RecurringSeriesPage() {
  const [series, setSeries] = useState<RecurringAppointmentDto[]>([])
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
      const data = await appointmentsApi.listRecurring(true)
      setSeries(data)
    } catch (err) {
      const message = err instanceof ApiError ? err.message : "Échec du chargement des séries"
      setError(message)
      toast.error(message)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    loadSeries()
  }, [loadSeries])

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
      <div className="flex h-screen bg-background">
        <DashboardSidebar />

        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />

          <main className="flex-1 overflow-y-auto p-6">
            <div className="mx-auto max-w-7xl space-y-6">
              {/* Page Header */}
              <div className="flex items-center justify-between">
                <div>
                  <h1 className="text-3xl font-semibold text-foreground">Rendez-vous récurrents (séries)</h1>
                  <p className="mt-1 text-sm text-muted-foreground">
                    Planifiez et gérez des séries de rendez-vous répétés.
                  </p>
                </div>

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
                      {series.length}
                    </Badge>
                  </CardTitle>
                </CardHeader>
                <CardContent>
                  {loading ? (
                    <div className="flex items-center justify-center py-12 text-muted-foreground">
                      <Loader2 className="h-5 w-5 animate-spin" />
                    </div>
                  ) : error ? (
                    <p className="py-12 text-center text-sm text-destructive">{error}</p>
                  ) : series.length === 0 ? (
                    <p className="py-12 text-center text-muted-foreground">Aucune série récurrente</p>
                  ) : (
                    <div className="overflow-x-auto">
                      <Table>
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
                          {series.map((s) => (
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
                          ))}
                        </TableBody>
                      </Table>
                    </div>
                  )}
                </CardContent>
              </Card>
            </div>
          </main>
        </div>

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
      </div>
    </ClinicGuard>
  )
}
