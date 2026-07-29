"use client"

import type React from "react"
import { useState, useEffect, useRef } from "react"
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
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
import { conditionStyle } from "@/components/odontogram-conditions"
import { cn } from "@/lib/utils"
import { planItemState } from "@/components/treatment-plans/plan-next-action"

interface LineRow {
  /**
   * The existing act this row stands for, when editing. Echoed back on save so the server keeps that act's
   * id — otherwise every draft edit re-issues the ids and silently orphans any appointment or dental-record
   * link pointing at those acts (neither has an FK to catch it).
   */
  id: string | null
  dentalActCodeId: string | null
  codeActe: string | null
  /**
   * The procedure this act will be performed as, kept when the row was filled from « Mes actes ». Persisted
   * so booking the act preselects it; previously the pick was snapshotted to a name and the id thrown away,
   * which left every plan-scheduled appointment without a procedure at all.
   */
  procedureTypeId: string | null
  designationFr: string
  /**
   * The charted diagnosis this row was seeded from, e.g. « Carie — dent 15 ». Display only — it is a reason to
   * treat, not an act, so it is never sent to the server. Empty for a hand-added row.
   */
  diagnosisLabel?: string
  /** The condition behind that label, so the hint can use its own colour from the odontogram palette. */
  diagnosisCondition?: string
  plannedCost: string
  toothNumbers: number[]
}

interface InstallmentRow {
  /**
   * The existing échéance this row revises. Dropped before (only `dueDate`/`amount` were kept), which is
   * harmless on a draft — the server replaces the whole schedule — but destructive on an **amendment**: an
   * échéance that has collected money must be echoed back by id or the server refuses the call outright
   * ("Une échéance déjà encaissée ne peut pas être supprimée de l'échéancier").
   */
  id: string | null
  dueDate: string
  amount: string
  /** Cash already collected on this échéance. 0 for a new row. Drives the "locked" affordance (AC-P2.6). */
  amountPaid: number
}

const emptyLine = (): LineRow => ({
  id: null,
  dentalActCodeId: null,
  codeActe: null,
  procedureTypeId: null,
  designationFr: "",
  plannedCost: "",
  toothNumbers: [],
})

/** A draft act line pre-filled from the odontogram ("Créer un plan depuis l'odontogramme"). */
export interface TreatmentPlanSeedLine {
  toothNumbers: number[]
  /** The act to perform. Blank when the charted condition names no single procedure — the dentist picks. */
  designationFr: string
  /** The charted diagnosis, shown as context under the field. Display only, never persisted. */
  diagnosisLabel?: string
  /** The condition behind that label, for the hint's colour. */
  diagnosisCondition?: string
  /** Prefilled planned cost from the matching procedure-type default (omitted when no catalog match). */
  plannedCost?: number
  /**
   * The procedure that treats the charted condition, when exactly one was matched. Carried so a devis built
   * from the odontogram can also book its acts with the procedure preselected — the seeded designation
   * (« Couronne — dent 16 ») is a condition label, so it would never match a procedure by name.
   */
  procedureTypeId?: string
}

interface TreatmentPlanFormModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  editingPlan?: TreatmentPlanDto | null
  /**
   * Amend an **accepted** devis instead of rewriting a draft (AC-P2.1). Requires `editingPlan`. The form is the
   * same, but the submit derives `addItems` / `removeItemIds` / `installments` and posts to
   * `POST /treatment-plans/{id}/amend`, which keeps the devis number and bumps `revisionNumber` — where the
   * draft path (`PUT /treatment-plans/{id}`) replaces the acts wholesale and is refused after acceptance.
   * Title, notes and patient are not amendable server-side, so they render read-only here.
   */
  amendMode?: boolean
  /** When opened from a patient page, the patient is preset and locked. */
  presetPatientId?: string
  presetPatientName?: string
  /** Pre-fill the act lines from charted diagnoses (new plans only). */
  seedLines?: TreatmentPlanSeedLine[]
  onSuccess?: () => void
}

/**
 * Upgrade the message when the same edit conflicts twice running. The first 409 means "someone saved before
 * you"; the second means "someone is editing this right now", and telling the user to reload again would be
 * repeating advice that has already failed.
 */
function conflictMessage(err: unknown, fallback: string, consecutive: React.MutableRefObject<number>): string {
  if (err instanceof ApiError && err.status === 409) {
    consecutive.current += 1
    if (consecutive.current > 1) {
      return "L'enregistrement a encore été modifié pendant votre saisie. Quelqu'un travaille probablement "
        + "dessus en même temps — coordonnez-vous avant de réessayer."
    }
    return err.message || fallback
  }
  consecutive.current = 0
  return err instanceof ApiError ? err.message : fallback
}

export function TreatmentPlanFormModal({
  open,
  onOpenChange,
  editingPlan,
  amendMode = false,
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
  const conflictStreak = useRef(0)

  const isEditing = !!editingPlan
  const isAmending = amendMode && !!editingPlan

  /**
   * Acts the server will refuse to remove, with the reason, so the form can say so *before* submit instead of
   * bouncing a French sentence back from the API. Keyed by act id; an act absent from the map is removable.
   *
   * Mirrors `TreatmentPlan.RemoveItem`: a réalisé act cannot be retired from a devis (its fiche and possibly
   * its invoice line point at it), and an act with a live appointment must have that appointment moved or
   * cancelled first.
   */
  const removalBlockers = new Map<string, string>()
  if (isAmending && editingPlan) {
    for (const item of editingPlan.items) {
      const state = planItemState(item)
      if (state === "done") {
        removalBlockers.set(
          item.id,
          "Acte déjà réalisé — détachez sa fiche de soins avant de le retirer du devis.",
        )
      } else if (state === "scheduled" || state === "to-record") {
        removalBlockers.set(
          item.id,
          "Un rendez-vous est prévu pour cet acte — annulez ou déplacez-le avant de le retirer.",
        )
      }
    }
  }

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
              id: it.id,
              dentalActCodeId: it.dentalActCodeId,
              codeActe: it.codeActe,
              procedureTypeId: it.procedureTypeId,
              designationFr: it.designationFr,
              plannedCost: String(it.plannedCost),
              toothNumbers: it.toothNumbers,
            }))
          : [emptyLine()],
      )
      setInstallments(
        editingPlan.installments.map((inst) => ({
          id: inst.id,
          dueDate: inst.dueDate.slice(0, 10),
          amount: String(inst.amount),
          amountPaid: inst.amountPaid,
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
              // Odontogram seeds are always new lines — there is no existing act to preserve.
              id: null,
              dentalActCodeId: null,
              codeActe: null,
              // Carried when the odontogram could tie the charted condition to exactly one procedure.
              procedureTypeId: s.procedureTypeId ?? null,
              designationFr: s.designationFr,
              diagnosisLabel: s.diagnosisLabel,
              diagnosisCondition: s.diagnosisCondition,
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
              // Clear any procedure kept from a previous pick on this row — its designation is being
              // replaced, so keeping the link would leave the act claiming a procedure it no longer names.
              procedureTypeId: null,
              designationFr: act.designationFr,
              // Prefill the fee from the catalog default only when the line has no cost yet.
              plannedCost: l.plannedCost.trim() === "" && act.defaultFee != null ? String(act.defaultFee) : l.plannedCost,
            }
          : l,
      ),
    )
    setPickerOpenIndex(null)
  }

  // A procedure type carries no CNAM code, so the line has no dentalActCodeId — but its `procedureTypeId` IS
  // kept. Snapshotting only the name (as this did) meant booking the act could not preselect the procedure,
  // so a plan-scheduled appointment got no procedure type, no colour and no default duration.
  const selectProcedureType = (index: number, pt: ProcedureTypeDto) => {
    setLines((prev) =>
      prev.map((l, i) =>
        i === index
          ? {
              ...l,
              dentalActCodeId: null,
              codeActe: null,
              procedureTypeId: pt.id,
              designationFr: pt.name,
              // Prefill the fee from the procedure default only when the line has no cost yet.
              plannedCost: l.plannedCost.trim() === "" && pt.defaultCost != null ? String(pt.defaultCost) : l.plannedCost,
            }
          : l,
      ),
    )
    setPickerOpenIndex(null)
  }

  // "Detach from the catalogue" makes the line pure free text, so it drops the procedure link too.
  const detachAct = (index: number) =>
    updateLine(index, { dentalActCodeId: null, codeActe: null, procedureTypeId: null })

  const updateInstallment = (index: number, patch: Partial<InstallmentRow>) => {
    setInstallments((prev) => prev.map((r, i) => (i === index ? { ...r, ...patch } : r)))
  }
  const addInstallment = () =>
    setInstallments((prev) => [
      ...prev,
      { id: null, dueDate: new Date().toISOString().slice(0, 10), amount: "", amountPaid: 0 },
    ])
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
        id: l.id,
        dentalActCodeId: l.dentalActCodeId,
        codeActe: l.codeActe,
        procedureTypeId: l.procedureTypeId,
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
        // Echoing the id back is what lets the server *revise* an existing échéance rather than replace the
        // schedule — mandatory for any row that has collected money.
        id: r.id,
        dueDate: `${r.dueDate}T00:00:00`,
        amount: i === amounts.length - 1 ? lastAmount : Number(r.amount),
      }))

      // AC-P2.6: refuse locally what the server refuses anyway, but name the row. A paid échéance may be
      // re-dated and raised, never lowered below what was collected and never dropped.
      for (let i = 0; i < installments.length; i++) {
        const row = installments[i]
        if (row.amountPaid > 0 && parsedInstallments[i].amount < row.amountPaid - 0.0005) {
          setError(
            `L'échéance du ${row.dueDate} a déjà encaissé ${formatDT(row.amountPaid)} — son montant ne peut pas être ramené en dessous.`,
          )
          return
        }
      }
    }

    if (isAmending && editingPlan) {
      const originalIds = new Set(editingPlan.items.map((i) => i.id))
      const keptIds = new Set(parsedLines.map((l) => l.id).filter((id): id is string => !!id))
      const removeItemIds = [...originalIds].filter((id) => !keptIds.has(id))

      const blocked = removeItemIds.filter((id) => removalBlockers.has(id))
      if (blocked.length > 0) {
        setError(removalBlockers.get(blocked[0])!)
        return
      }

      // Only rows with no id are additions; an existing act's designation/cost/teeth are NOT amendable
      // through this endpoint (the server takes addItems + removeItemIds only), so an edit-in-place would
      // silently do nothing. Remove-then-add is the honest expression of "change this act".
      const addItems = parsedLines.filter((l) => !l.id)

      if (addItems.length === 0 && removeItemIds.length === 0 && parsedInstallments.length === 0) {
        setError("Aucune modification demandée.")
        return
      }

      // An échéancier that was dropped entirely is sent as an empty list; the server answers
      // "L'échéancier ne peut pas être vide sur un devis accepté." rather than us guessing a spread.
      const droppedPaidRow = editingPlan.installments.some(
        (inst) => inst.amountPaid > 0 && !installments.some((r) => r.id === inst.id),
      )
      if (droppedPaidRow) {
        setError(
          "Une échéance déjà encaissée ne peut pas être supprimée de l'échéancier. Conservez-la et ajustez les autres.",
        )
        return
      }

      setLoading(true)
      try {
        await treatmentPlansApi.amend(editingPlan.id, {
          addItems,
          removeItemIds,
          installments: parsedInstallments,
        })
        toast.success("Devis modifié")
        onSuccess?.()
        onOpenChange(false)
      } catch (err) {
        setError(conflictMessage(err, "Échec de la modification du devis.", conflictStreak))
      } finally {
        setLoading(false)
      }
      return
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
        await treatmentPlansApi.update(editingPlan.id, { ...payload, version: editingPlan.version })
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
      setError(conflictMessage(err, "Échec de l'enregistrement du plan.", conflictStreak))
    } finally {
      setLoading(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-3xl max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>
            {isAmending
              ? "Modifier le devis"
              : isEditing
                ? "Modifier le plan de traitement"
                : "Nouveau plan de traitement"}
          </DialogTitle>
          <DialogDescription>
            {isAmending ? (
              <>
                Le devis garde son numéro{editingPlan?.number ? ` (${editingPlan.number})` : ""} et passe en
                révision {(editingPlan?.revisionNumber ?? 0) + 1}. Ajoutez ou retirez des actes, puis ajustez
                l&apos;échéancier au nouveau total. Le titre, les notes et le patient ne sont pas modifiables
                après acceptation.
              </>
            ) : (
              "Devis : actes planifiés, coûts et échéancier de paiement. Un brouillon peut être modifié librement."
            )}
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
          <FormErrorBanner message={error} />

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
                disabled={loading || isAmending}
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
              disabled={loading || isAmending}
            />
          </div>

          {/* Act lines */}
          <div className="space-y-2">
            <Label>Actes</Label>
            <div className="space-y-3">
              {lines.map((line, index) => {
                // Every field of an existing act is editable in amend mode — the endpoint now takes in-place
                // edits, and a réalisé or booked act is precisely the one whose price cannot be corrected any
                // other way, since it refuses removal. Only *removal* is still gated (`removalBlocked`).
                const removalBlocked = line.id ? removalBlockers.get(line.id) : undefined
                return (
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

                      {/* The diagnosis that motivated this line — context, not content. It used to BE the
                          designation, so the devis billed « Carie — dent 15 » as an act. Now the field opens
                          empty for a pathology and this says what to treat. */}
                      {line.diagnosisLabel && (
                        <div className="flex items-center gap-1.5 pt-0.5">
                          <span className="text-xs text-muted-foreground">Diagnostic :</span>
                          <span
                            className={cn(
                              "rounded border px-1.5 py-0.5 text-xs font-medium",
                              conditionStyle(line.diagnosisCondition ?? "").box,
                            )}
                          >
                            {line.diagnosisLabel}
                          </span>
                        </div>
                      )}
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
                      {removalBlocked && (
                        <p className="text-xs text-muted-foreground">{removalBlocked}</p>
                      )}
                    </div>
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      onClick={() => removeLine(index)}
                      disabled={loading || lines.length === 1 || !!removalBlocked}
                      aria-label="Supprimer l'acte"
                      title={removalBlocked ?? "Supprimer l'acte"}
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
                )
              })}
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
              {installments.map((row, index) => {
                // AC-P2.6: an échéance that has collected cash is locked against deletion and against being
                // lowered — the user sees which rows and why *before* submitting, not as an API refusal.
                const collected = row.amountPaid > 0
                return (
                <div key={index} className="space-y-1">
                  <div className="flex items-end gap-2">
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
                        min={collected ? row.amountPaid : 0}
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
                      disabled={loading || collected}
                      aria-label="Supprimer l'échéance"
                      title={
                        collected
                          ? "Échéance déjà encaissée — elle ne peut pas être supprimée."
                          : "Supprimer l'échéance"
                      }
                    >
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                  {collected && (
                    <p className="text-xs text-muted-foreground">
                      Déjà encaissé : {formatDT(row.amountPaid)} — cette échéance ne peut être ni supprimée ni
                      ramenée en dessous de ce montant.
                    </p>
                  )}
                </div>
                )
              })}
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
              {loading
                ? "Enregistrement..."
                : isAmending
                  ? "Enregistrer la révision"
                  : isEditing
                    ? "Enregistrer"
                    : "Créer le plan"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
