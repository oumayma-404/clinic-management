"use client"

import type React from "react"
import { useState, useEffect } from "react"
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Badge } from "@/components/ui/badge"
import { Textarea } from "@/components/ui/textarea"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from "@/components/ui/command"
import { Trash2, Plus, Search, X } from "lucide-react"
import { toast } from "sonner"
import {
  treatmentPlansApi,
  type TreatmentPlanItemInput,
  type TreatmentPlanInstallmentInput,
  type CreateTreatmentPlanRequest,
  type UpdateTreatmentPlanRequest,
} from "@/lib/api/treatment-plans"
import { dentalActsApi } from "@/lib/api/dental-acts"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import { patientsApi } from "@/lib/api/patients"
import { ApiError } from "@/lib/api/client"
import type { TreatmentPlanDto, PatientDto, DentalActDto, ProcedureTypeDto } from "@/lib/api/types"
import { formatDT } from "@/lib/format"
import { ToothMultiSelect } from "@/components/tooth-multiselect"

interface LineRow {
  dentalActCodeId: string | null
  codeActe: string | null
  designationFr: string
  plannedCost: string
  toothNumbers: number[]
}

interface InstallmentRow {
  dueDate: string
  amount: string
}

const emptyLine = (): LineRow => ({
  dentalActCodeId: null,
  codeActe: null,
  designationFr: "",
  plannedCost: "",
  toothNumbers: [],
})

/** A draft act line pre-filled from the odontogram ("Créer un plan depuis l'odontogramme"). */
export interface TreatmentPlanSeedLine {
  toothNumbers: number[]
  designationFr: string
  /** Prefilled planned cost from the matching procedure-type default (omitted when no catalog match). */
  plannedCost?: number
}

interface TreatmentPlanFormModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  editingPlan?: TreatmentPlanDto | null
  /** When opened from a patient page, the patient is preset and locked. */
  presetPatientId?: string
  presetPatientName?: string
  /** Pre-fill the act lines from charted diagnoses (new plans only). */
  seedLines?: TreatmentPlanSeedLine[]
  onSuccess?: () => void
}

export function TreatmentPlanFormModal({
  open,
  onOpenChange,
  editingPlan,
  presetPatientId,
  presetPatientName,
  seedLines,
  onSuccess,
}: TreatmentPlanFormModalProps) {
  const [patients, setPatients] = useState<PatientDto[]>([])
  const [acts, setActs] = useState<DentalActDto[]>([])
  const [procedureTypes, setProcedureTypes] = useState<ProcedureTypeDto[]>([])
  const [patientId, setPatientId] = useState("")
  const [title, setTitle] = useState("")
  const [notes, setNotes] = useState("")
  const [lines, setLines] = useState<LineRow[]>([emptyLine()])
  const [installments, setInstallments] = useState<InstallmentRow[]>([])
  const [pickerOpenIndex, setPickerOpenIndex] = useState<number | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const isEditing = !!editingPlan

  useEffect(() => {
    if (!open) return

    // Load the active dental act catalog for the line picker.
    dentalActsApi
      .list()
      .then(setActs)
      .catch(() => setActs([]))

    // Load the clinic's active procedure types — the primary "Mes actes" source for the line picker.
    procedureTypesApi
      .list(false)
      .then(setProcedureTypes)
      .catch(() => setProcedureTypes([]))

    // Only load a patient list when the caller doesn't preset one.
    if (!presetPatientId) {
      patientsApi
        .list({ limit: 500 })
        .then(setPatients)
        .catch(() => setPatients([]))
    }

    if (editingPlan) {
      setPatientId(editingPlan.patientId)
      setTitle(editingPlan.title)
      setNotes(editingPlan.notes ?? "")
      setLines(
        editingPlan.items.length > 0
          ? editingPlan.items.map((it) => ({
              dentalActCodeId: it.dentalActCodeId,
              codeActe: it.codeActe,
              designationFr: it.designationFr,
              plannedCost: String(it.plannedCost),
              toothNumbers: it.toothNumbers,
            }))
          : [emptyLine()],
      )
      setInstallments(
        editingPlan.installments.map((inst) => ({
          dueDate: inst.dueDate.slice(0, 10),
          amount: String(inst.amount),
        })),
      )
    } else {
      setPatientId(presetPatientId ?? "")
      const seeded = seedLines && seedLines.length > 0
      setTitle(seeded ? "Plan de traitement" : "")
      setNotes("")
      setLines(
        seeded
          ? seedLines!.map((s) => ({
              dentalActCodeId: null,
              codeActe: null,
              designationFr: s.designationFr,
              // Prefill the fee from the matching procedure-type default (odontogram match); blank otherwise.
              plannedCost: s.plannedCost != null && s.plannedCost > 0 ? String(s.plannedCost) : "",
              toothNumbers: s.toothNumbers,
            }))
          : [emptyLine()],
      )
      setInstallments([])
    }
    setError(null)
    // Seeds once when the dialog opens.
  }, [open, editingPlan, presetPatientId, seedLines])

  const updateLine = (index: number, patch: Partial<LineRow>) => {
    setLines((prev) => prev.map((l, i) => (i === index ? { ...l, ...patch } : l)))
  }

  const addLine = () => setLines((prev) => [...prev, emptyLine()])
  const removeLine = (index: number) =>
    setLines((prev) => (prev.length > 1 ? prev.filter((_, i) => i !== index) : prev))

  const selectAct = (index: number, act: DentalActDto) => {
    setLines((prev) =>
      prev.map((l, i) =>
        i === index
          ? {
              ...l,
              dentalActCodeId: act.id,
              codeActe: act.codeActe,
              designationFr: act.designationFr,
              // Prefill the fee from the catalog default only when the line has no cost yet.
              plannedCost: l.plannedCost.trim() === "" && act.defaultFee != null ? String(act.defaultFee) : l.plannedCost,
            }
          : l,
      ),
    )
    setPickerOpenIndex(null)
  }

  // A procedure type is snapshotted as a free-text line (no catalog id/code — backend treats it as free text).
  const selectProcedureType = (index: number, pt: ProcedureTypeDto) => {
    setLines((prev) =>
      prev.map((l, i) =>
        i === index
          ? {
              ...l,
              dentalActCodeId: null,
              codeActe: null,
              designationFr: pt.name,
              // Prefill the fee from the procedure default only when the line has no cost yet.
              plannedCost: l.plannedCost.trim() === "" && pt.defaultCost != null ? String(pt.defaultCost) : l.plannedCost,
            }
          : l,
      ),
    )
    setPickerOpenIndex(null)
  }

  const detachAct = (index: number) => updateLine(index, { dentalActCodeId: null, codeActe: null })

  const updateInstallment = (index: number, patch: Partial<InstallmentRow>) => {
    setInstallments((prev) => prev.map((r, i) => (i === index ? { ...r, ...patch } : r)))
  }
  const addInstallment = () =>
    setInstallments((prev) => [...prev, { dueDate: new Date().toISOString().slice(0, 10), amount: "" }])
  const removeInstallment = (index: number) => setInstallments((prev) => prev.filter((_, i) => i !== index))

  const total = lines.reduce((sum, l) => {
    const cost = Number(l.plannedCost)
    return Number.isFinite(cost) ? sum + cost : sum
  }, 0)

  const installmentsSum = installments.reduce((sum, r) => {
    const amt = Number(r.amount)
    return Number.isFinite(amt) ? sum + amt : sum
  }, 0)

  const installmentsMatch = installments.length === 0 || Math.abs(installmentsSum - total) < 0.0005

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)

    if (!patientId) {
      setError("Veuillez sélectionner un patient.")
      return
    }
    if (!title.trim()) {
      setError("Le titre est obligatoire.")
      return
    }

    const parsedLines: TreatmentPlanItemInput[] = lines
      .map((l) => ({
        dentalActCodeId: l.dentalActCodeId,
        codeActe: l.codeActe,
        designationFr: l.designationFr.trim(),
        plannedCost: Number(l.plannedCost),
        toothNumbers: l.toothNumbers,
      }))
      .filter((l) => l.designationFr !== "")

    if (parsedLines.length === 0) {
      setError("Ajoutez au moins un acte.")
      return
    }
    for (const l of parsedLines) {
      if (!Number.isFinite(l.plannedCost) || l.plannedCost < 0) {
        setError(`Coût invalide pour « ${l.designationFr} ».`)
        return
      }
    }

    // Build the installments; the last row absorbs the remainder so the schedule sums exactly to the total.
    let parsedInstallments: TreatmentPlanInstallmentInput[] = []
    if (installments.length > 0) {
      for (const r of installments) {
        if (!r.dueDate) {
          setError("Chaque échéance doit avoir une date.")
          return
        }
      }
      const amounts = installments.map((r) => Number(r.amount))
      for (let i = 0; i < amounts.length - 1; i++) {
        if (!Number.isFinite(amounts[i]) || amounts[i] < 0) {
          setError("Montant d'échéance invalide.")
          return
        }
      }
      const allButLast = amounts.slice(0, -1).reduce((s, a) => s + (Number.isFinite(a) ? a : 0), 0)
      const lastAmount = Math.round((total - allButLast) * 1000) / 1000
      if (lastAmount < 0) {
        setError("Le total des échéances dépasse le montant du plan.")
        return
      }
      parsedInstallments = installments.map((r, i) => ({
        dueDate: `${r.dueDate}T00:00:00`,
        amount: i === amounts.length - 1 ? lastAmount : Number(r.amount),
      }))
    }

    setLoading(true)
    try {
      if (isEditing && editingPlan) {
        const payload: UpdateTreatmentPlanRequest = {
          title: title.trim(),
          notes: notes.trim() || null,
          items: parsedLines,
          installments: parsedInstallments,
        }
        await treatmentPlansApi.update(editingPlan.id, payload)
        toast.success("Plan de traitement mis à jour")
      } else {
        const payload: CreateTreatmentPlanRequest = {
          patientId,
          title: title.trim(),
          notes: notes.trim() || null,
          items: parsedLines,
          installments: parsedInstallments,
        }
        await treatmentPlansApi.create(payload)
        toast.success("Plan de traitement créé")
      }
      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Échec de l'enregistrement du plan.")
    } finally {
      setLoading(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-3xl max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{isEditing ? "Modifier le plan de traitement" : "Nouveau plan de traitement"}</DialogTitle>
          <DialogDescription>
            Devis : actes planifiés, coûts et échéancier de paiement. Un brouillon peut être modifié librement.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
          {error && (
            <div className="rounded-lg bg-red-50 border border-red-200 p-3 text-sm text-red-800 dark:bg-red-950 dark:border-red-900 dark:text-red-200">
              {error}
            </div>
          )}

          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="patient">
                Patient <span className="text-destructive">*</span>
              </Label>
              {presetPatientId || isEditing ? (
                <Input id="patient" value={presetPatientName ?? editingPlan?.patientName ?? "Patient"} disabled />
              ) : (
                <Select value={patientId} onValueChange={setPatientId} disabled={loading}>
                  <SelectTrigger id="patient">
                    <SelectValue placeholder="Sélectionner un patient" />
                  </SelectTrigger>
                  <SelectContent>
                    {patients.map((p) => (
                      <SelectItem key={p.id} value={p.id}>
                        {p.firstName} {p.lastName}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="title">
                Titre <span className="text-destructive">*</span>
              </Label>
              <Input
                id="title"
                value={title}
                onChange={(e) => setTitle(e.target.value)}
                placeholder="Ex. Réhabilitation prothétique"
                disabled={loading}
              />
            </div>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="notes">Notes</Label>
            <Textarea
              id="notes"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              placeholder="Notes (optionnel)"
              rows={2}
              disabled={loading}
            />
          </div>

          {/* Act lines */}
          <div className="space-y-2">
            <Label>Actes</Label>
            <div className="space-y-3">
              {lines.map((line, index) => (
                <div key={index} className="rounded-lg border p-3 space-y-2">
                  <div className="flex items-start gap-2">
                    <div className="flex-1 space-y-1">
                      <div className="flex items-center gap-2">
                        <Input
                          value={line.designationFr}
                          onChange={(e) => updateLine(index, { designationFr: e.target.value })}
                          placeholder="Désignation de l'acte (ou choisir au catalogue)"
                          disabled={loading}
                        />
                        <Popover
                          open={pickerOpenIndex === index}
                          onOpenChange={(o) => setPickerOpenIndex(o ? index : null)}
                          modal
                        >
                          <PopoverTrigger asChild>
                            <Button
                              type="button"
                              variant="outline"
                              size="sm"
                              className="h-9 px-3 shrink-0"
                              disabled={loading}
                              title="Choisir un acte du catalogue"
                            >
                              <Search className="h-4 w-4" />
                              <span className="sr-only">Choisir un acte du catalogue</span>
                            </Button>
                          </PopoverTrigger>
                          <PopoverContent className="p-0 w-80" align="end">
                            <Command>
                              <CommandInput placeholder="Rechercher un acte…" />
                              <CommandList>
                                <CommandEmpty>Aucun acte trouvé.</CommandEmpty>
                                <CommandGroup heading="Mes actes">
                                  {procedureTypes.map((pt) => (
                                    <CommandItem
                                      key={pt.id}
                                      value={`pt ${pt.name}`}
                                      onSelect={() => selectProcedureType(index, pt)}
                                    >
                                      <div className="flex flex-col">
                                        <span className="text-sm font-medium">{pt.name}</span>
                                        {pt.defaultCost != null && pt.defaultCost > 0 && (
                                          <span className="text-xs text-muted-foreground">{formatDT(pt.defaultCost)}</span>
                                        )}
                                      </div>
                                    </CommandItem>
                                  ))}
                                </CommandGroup>
                                <CommandGroup heading="Nomenclature CNAM">
                                  {acts.map((act) => (
                                    <CommandItem
                                      key={act.id}
                                      value={`${act.codeActe} ${act.designationFr} ${act.lettreCle}`}
                                      onSelect={() => selectAct(index, act)}
                                    >
                                      <div className="flex flex-col">
                                        <span className="text-sm font-medium">{act.designationFr}</span>
                                        <span className="text-xs text-muted-foreground">
                                          {act.codeActe} · {act.category}
                                          {act.defaultFee != null ? ` · ${formatDT(act.defaultFee)}` : ""}
                                        </span>
                                      </div>
                                    </CommandItem>
                                  ))}
                                </CommandGroup>
                              </CommandList>
                            </Command>
                          </PopoverContent>
                        </Popover>
                      </div>
                      {line.codeActe && (
                        <Badge variant="secondary" className="gap-1 font-mono text-xs">
                          {line.codeActe}
                          <button
                            type="button"
                            onClick={() => detachAct(index)}
                            className="ml-1 rounded-full hover:text-destructive"
                            title="Détacher du catalogue (texte libre)"
                          >
                            <X className="h-3 w-3" />
                          </button>
                        </Badge>
                      )}
                    </div>
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      onClick={() => removeLine(index)}
                      disabled={loading || lines.length === 1}
                      aria-label="Supprimer l'acte"
                    >
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                  <div className="flex flex-wrap items-center gap-2">
                    <ToothMultiSelect
                      value={line.toothNumbers}
                      onChange={(teeth) => updateLine(index, { toothNumbers: teeth })}
                      disabled={loading}
                    />
                    <div className="flex items-center gap-1.5">
                      <span className="text-xs text-muted-foreground">Coût (DT)</span>
                      <Input
                        type="number"
                        min="0"
                        step="0.001"
                        value={line.plannedCost}
                        onChange={(e) => updateLine(index, { plannedCost: e.target.value })}
                        className="w-32"
                        disabled={loading}
                      />
                    </div>
                  </div>
                </div>
              ))}
            </div>
            <Button type="button" variant="outline" size="sm" onClick={addLine} disabled={loading} className="gap-2">
              <Plus className="h-4 w-4" /> Ajouter un acte
            </Button>
          </div>

          <div className="flex justify-end text-sm">
            <span className="text-muted-foreground">Total planifié :&nbsp;</span>
            <span className="font-semibold">{formatDT(total)}</span>
          </div>

          {/* Installments */}
          <div className="space-y-2">
            <Label>Échéancier</Label>
            <div className="space-y-2">
              {installments.length === 0 && (
                <p className="text-sm text-muted-foreground">Aucune échéance. Ajoutez un échéancier de paiement (optionnel).</p>
              )}
              {installments.map((row, index) => (
                <div key={index} className="flex items-end gap-2">
                  <div className="flex-1 space-y-1">
                    {index === 0 && <span className="text-xs text-muted-foreground">Échéance</span>}
                    <Input
                      type="date"
                      value={row.dueDate}
                      onChange={(e) => updateInstallment(index, { dueDate: e.target.value })}
                      disabled={loading}
                    />
                  </div>
                  <div className="w-36 space-y-1">
                    {index === 0 && <span className="text-xs text-muted-foreground">Montant (DT)</span>}
                    <Input
                      type="number"
                      min="0"
                      step="0.001"
                      value={row.amount}
                      onChange={(e) => updateInstallment(index, { amount: e.target.value })}
                      disabled={loading}
                    />
                  </div>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    onClick={() => removeInstallment(index)}
                    disabled={loading}
                    aria-label="Supprimer l'échéance"
                  >
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </div>
              ))}
            </div>
            <Button type="button" variant="outline" size="sm" onClick={addInstallment} disabled={loading} className="gap-2">
              <Plus className="h-4 w-4" /> Ajouter une échéance
            </Button>
            {installments.length > 0 && (
              <div className="flex justify-end text-xs">
                <span className={installmentsMatch ? "text-muted-foreground" : "text-amber-600 dark:text-amber-400"}>
                  Total des échéances : {formatDT(installmentsSum)} / {formatDT(total)}
                  {!installmentsMatch && " — la dernière échéance sera ajustée à l'enregistrement."}
                </span>
              </div>
            )}
          </div>

          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={loading}>
              Annuler
            </Button>
            <Button type="submit" disabled={loading}>
              {loading ? "Enregistrement..." : isEditing ? "Enregistrer" : "Créer le plan"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
