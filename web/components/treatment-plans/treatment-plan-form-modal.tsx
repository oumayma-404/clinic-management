"use client"

import type React from "react"
import { useState, useEffect, useCallback, useRef } from "react"
import { Dialog, DialogBody, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from "@/components/ui/dialog"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import { Button } from "@/components/ui/button"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { LoadFailureNotice } from "@/components/ui/load-failure"
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
import { useFreshVersion } from "@/lib/hooks/use-fresh-version"
import type { TreatmentPlanDto, PatientDto, DentalActDto, ProcedureTypeDto } from "@/lib/api/types"
import { formatAmount, formatDT, parseAmountInput, quoteFr, todayLocalIso } from "@/lib/format"
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
  /**
   * Has the dentist typed this fee themselves? Until they do, it **follows** whichever act the row is set to,
   * so picking a different act reprices the line.
   *
   * The guard this replaces was `plannedCost === ""` — prefill only an empty field — which could not tell a fee
   * the dentist typed from one a *previous pick* had prefilled, and so protected both. Any row arriving with a
   * cost (every odontogram seed does) therefore kept its original fee through every subsequent change of act:
   * « Détartrage » priced at the couronne's fee.
   */
  costTouched: boolean
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
  costTouched: false,
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
   *
   * Since plans are accepted at creation this is the **only** editor a devis ever gets, so everything the
   * endpoint accepts is editable here: acts in place (`updateItems`, id preserved), additions, removals, the
   * échéancier, the title and the notes. Only the patient is fixed — moving a numbered devis to someone else is
   * not an amendment.
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
  /** At least one of the three picker reads failed — never conflated with "the catalogue is empty". */
  const [pickersFailed, setPickersFailed] = useState(false)
  const [patientId, setPatientId] = useState("")
  const [title, setTitle] = useState("")
  const [notes, setNotes] = useState("")
  const [lines, setLines] = useState<LineRow[]>([emptyLine()])
  const [installments, setInstallments] = useState<InstallmentRow[]>([])
  const [pickerOpenIndex, setPickerOpenIndex] = useState<number | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const guard = useDirtyGuard(open, onOpenChange)
  const conflictStreak = useRef(0)
  // The version this devis saves with, kept equal to the row's current one. ⚠️ The VERSION only — the read
  // lands after hydration, so its items and échéances would replace what the user has edited.
  const { source: freshPlan, resync } = useFreshVersion(
    open,
    editingPlan?.id,
    editingPlan,
    () => treatmentPlansApi.get(editingPlan!.id),
  )

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

  /*
   * The three lists this editor picks from — and, crucially, **whether each read failed**.
   *
   * ⚠️ All three used to end in `.catch(() => setX([]))`. On a devis editor that is not a soft degradation: an
   * empty « Mes actes » / CNAM catalogue leaves the practitioner typing a désignation and a fee by hand, so the
   * plan is created with no `dentalActCodeId` and no `procedureTypeId` — no CNAM code on the devis PDF, and no
   * procédure when the act is later booked. An empty patient list on the create path is worse: the form's only
   * required field has no selectable value, and « aucun patient » in a clinic with three hundred reads as the
   * software having lost them.
   */
  const loadPickers = useCallback(async () => {
    const [actsResult, proceduresResult, patientsResult] = await Promise.allSettled([
      dentalActsApi.list(),
      procedureTypesApi.list(false),
      presetPatientId ? Promise.resolve(null) : patientsApi.list({ limit: 500 }),
    ])

    if (actsResult.status === "fulfilled") setActs(actsResult.value)
    if (proceduresResult.status === "fulfilled") setProcedureTypes(proceduresResult.value)
    if (patientsResult.status === "fulfilled" && patientsResult.value) setPatients(patientsResult.value)

    setPickersFailed(
      actsResult.status === "rejected" ||
        proceduresResult.status === "rejected" ||
        patientsResult.status === "rejected",
    )
  }, [presetPatientId])

  useEffect(() => {
    if (!open) return

    void loadPickers()

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
              plannedCost: formatAmount(it.plannedCost),
              // A stored fee is the number that was agreed with the patient, whatever the catalogue says today.
              // It is never re-derived from a default, so re-picking an act to fix its designation cannot
              // reprice work already quoted.
              costTouched: true,
              toothNumbers: it.toothNumbers,
            }))
          : [emptyLine()],
      )
      setInstallments(
        editingPlan.installments.map((inst) => ({
          id: inst.id,
          dueDate: inst.dueDate.slice(0, 10),
          amount: formatAmount(inst.amount),
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
              plannedCost: s.plannedCost != null && s.plannedCost > 0 ? formatAmount(s.plannedCost) : "",
              // Untouched on purpose: this fee came from a catalogue default the app chose, not from the
              // dentist. It is exactly the case the old empty-field guard got wrong — the seed arrives with a
              // cost, so every later change of act kept the *seeded* act's price.
              costTouched: false,
              toothNumbers: s.toothNumbers,
            }))
          : [emptyLine()],
      )
      setInstallments([])
    }
    setError(null)
    // Seeds once when the dialog opens.
  }, [open, editingPlan, presetPatientId, seedLines, loadPickers])

  const updateLine = (index: number, patch: Partial<LineRow>) => {
    setLines((prev) => prev.map((l, i) => (i === index ? { ...l, ...patch } : l)))
  }

  const addLine = () => setLines((prev) => [...prev, emptyLine()])
  const removeLine = (index: number) =>
    setLines((prev) => (prev.length > 1 ? prev.filter((_, i) => i !== index) : prev))

  /**
   * The fee a row should show after being re-pointed at a new act.
   *
   * A fee the dentist typed is theirs and survives every later pick. Otherwise the fee follows the act — which
   * is the whole fix: the previous act's price is simply wrong for the act now named, and leaving it there is
   * how « Détartrage » ended up at the couronne's fee.
   *
   * A new act with no default clears the field rather than keeping the old number: an empty box asks for the
   * price, whereas a stale one silently asserts a wrong one. (Harmless server-side — for a CNAM-linked line
   * `TreatmentPlanItemPricing` fills a blank cost from the act's own default.)
   */
  const repricedFor = (line: LineRow, defaultFee: number | null | undefined): string => {
    if (line.costTouched) return line.plannedCost
    return defaultFee != null && defaultFee > 0 ? formatAmount(defaultFee) : ""
  }

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
              plannedCost: repricedFor(l, act.defaultFee),
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
              plannedCost: repricedFor(l, pt.defaultCost),
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
      { id: null, dueDate: todayLocalIso(), amount: "", amountPaid: 0 },
    ])
  const removeInstallment = (index: number) => setInstallments((prev) => prev.filter((_, i) => i !== index))

  const total = lines.reduce((sum, l) => {
    const cost = parseAmountInput(l.plannedCost)
    return Number.isFinite(cost) ? sum + cost : sum
  }, 0)

  const installmentsSum = installments.reduce((sum, r) => {
    const amt = parseAmountInput(r.amount)
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
        plannedCost: parseAmountInput(l.plannedCost),
        toothNumbers: l.toothNumbers,
      }))
      .filter((l) => l.designationFr !== "")

    if (parsedLines.length === 0) {
      setError("Ajoutez au moins un acte.")
      return
    }
    for (const l of parsedLines) {
      if (!Number.isFinite(l.plannedCost) || l.plannedCost < 0) {
        setError(`Coût invalide pour ${quoteFr(l.designationFr)}.`)
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
      const amounts = installments.map((r) => parseAmountInput(r.amount))
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
        amount: i === amounts.length - 1 ? lastAmount : parseAmountInput(r.amount),
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

      // Rows with no id are additions; rows with one are **corrections in place**, sent as `updateItems`.
      //
      // They used to be dropped here on the belief that the endpoint took additions and removals only. It takes
      // `updateItems` too, and the inputs above were already editable — so a dentist could retype a fee, press
      // « Enregistrer la révision », get a success toast and lose the edit. That silent discard is the whole
      // reason a plan needed a draft stage to be correctable, and it has to go for creation-time acceptance to
      // be safe. Sending an id also *preserves* it, so every appointment and fiche link survives the change —
      // which remove-then-add cannot do, and which is refused outright for a réalisé or booked act.
      const addItems = parsedLines.filter((l) => !l.id)
      const updateItems = parsedLines.filter((l) => {
        if (!l.id) return false
        const before = editingPlan.items.find((i) => i.id === l.id)
        if (!before) return false
        // Only genuinely changed lines: re-sending every act unchanged would bump the révision counter on a
        // no-op save, and that counter is how a patient's earlier printout is identified.
        return (
          l.designationFr.trim() !== before.designationFr.trim() ||
          Math.abs(l.plannedCost - before.plannedCost) > 0.0005 ||
          (l.codeActe ?? null) !== (before.codeActe ?? null) ||
          (l.dentalActCodeId ?? null) !== (before.dentalActCodeId ?? null) ||
          (l.procedureTypeId ?? null) !== (before.procedureTypeId ?? null) ||
          l.toothNumbers.join(",") !== before.toothNumbers.join(",")
        )
      })

      const retitling = title.trim() !== "" && title.trim() !== editingPlan.title
      const renoting = (notes.trim() || null) !== (editingPlan.notes ?? null)

      if (
        addItems.length === 0 &&
        updateItems.length === 0 &&
        removeItemIds.length === 0 &&
        parsedInstallments.length === 0 &&
        !retitling &&
        !renoting
      ) {
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
          updateItems,
          removeItemIds,
          installments: parsedInstallments,
          title: title.trim(),
          // Tri-state server-side and always sent: it compares against the stored value, so an unchanged note
          // is not counted as an amendment and does not bump the révision.
          notes: notes.trim() || null,
          // The row's version as last read, so a peer's edit 409s instead of overwriting their fees — and
          // our own earlier write is not mistaken for one.
          version: freshPlan?.version ?? editingPlan.version,
        })
        toast.success("Devis modifié")
        onSuccess?.()
        onOpenChange(false)
      } catch (err) {
        setError(conflictMessage(err, "Échec de la modification du devis.", conflictStreak))
        if (!(err instanceof ApiError && err.status === 409)) await resync()
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
        await treatmentPlansApi.update(editingPlan.id, {
          ...payload,
          version: freshPlan?.version ?? editingPlan.version,
        })
        toast.success("Plan de traitement mis à jour")
      } else {
        const payload: CreateTreatmentPlanRequest = {
          patientId,
          title: title.trim(),
          notes: notes.trim() || null,
          items: parsedLines,
          installments: parsedInstallments,
        }
        const created = await treatmentPlansApi.create(payload)
        // Names the number, because that is the evidence the plan is already live — a bare « créé » left the
        // dentist looking for the « Accepter » button that no longer exists.
        toast.success(
          created.number ? `Devis ${created.number} créé et validé` : "Devis créé et validé",
        )
      }
      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      setError(conflictMessage(err, "Échec de l'enregistrement du plan.", conflictStreak))
      // A non-conflict failure may still have moved the row; a real 409 is left alone, or the retry would
      // silently overwrite the colleague who caused it.
      if (!(err instanceof ApiError && err.status === 409)) await resync()
    } finally {
      setLoading(false)
    }
  }

  return (
    <>
    {/* Only the ROOT and « Annuler » route through the guard — the save path calls the raw prop (AC-23). */}
    <Dialog open={open} onOpenChange={guard.onOpenChange}>
      <DialogContent mobile="sheet" className="md:max-h-[90dvh] md:max-w-3xl">
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
                révision {(editingPlan?.revisionNumber ?? 0) + 1}. Corrigez les actes et leurs montants, ajoutez
                ou retirez-en, puis ajustez l&apos;échéancier au nouveau total. Seul le patient n&apos;est pas
                modifiable.
              </>
            ) : (
              "Devis : actes planifiés, coûts et échéancier. Il est validé et numéroté dès sa création — les montants restent corrigeables ensuite."
            )}
          </DialogDescription>
        </DialogHeader>

        {/* The form owns the remaining height so `DialogBody` scrolls and the footer stays on screen (AC-21). */}
        <form onSubmit={handleSubmit} className="flex min-h-0 flex-1 flex-col gap-4">
          <DialogBody className="space-y-4">
          <FormErrorBanner message={error} />

          {/* One notice for the three picker reads: they load together, they fail together in practice, and three
              separate banners over one form would say the same thing three times. */}
          {pickersFailed && (
            <LoadFailureNotice
              message="Les listes de sélection n'ont pas pu être chargées."
              detail="Patients, actes CNAM et « Mes actes » sont peut-être incomplets — un acte saisi à la main n'aura ni code CNAM ni procédure."
              onRetry={() => void loadPickers()}
            />
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
                      {/* `text` + `inputMode="decimal"`, never `type="number"` (J8): a number input refuses the
                          comma this product prints with, and a rejected keystroke returns an EMPTY value — so an
                          act looked priced and the devis planned 0 for it. The keypad still appears. */}
                      <Input
                        type="text"
                        inputMode="decimal"
                        value={line.plannedCost}
                        // Typing a fee claims it: from here on it is the dentist's number and no later
                        // catalogue pick overwrites it.
                        onChange={(e) => updateLine(index, { plannedCost: e.target.value, costTouched: true })}
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
                      {/* Same conversion as « Coût » above (J8). The `min` it drops was never the real guard:
                          the server refuses an échéance below what it has already collected, and the locked-row
                          note above says so before submit. */}
                      <Input
                        type="text"
                        inputMode="decimal"
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
                {/* Token, not `amber-600` + a `dark:` twin — same reasoning as `revise-installments-modal`:
                    `--warning-ink` is the amber step that stays legible at this size and follows the palette. */}
                <span className={installmentsMatch ? "text-muted-foreground" : "text-warning-ink"}>
                  Total des échéances : {formatDT(installmentsSum)} / {formatDT(total)}
                  {!installmentsMatch && " — la dernière échéance sera ajustée à l'enregistrement."}
                </span>
              </div>
            )}
          </div>
          </DialogBody>

          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" onClick={() => guard.onOpenChange(false)} disabled={loading}>
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
    <DiscardChangesDialog guard={guard} />
    </>
  )
}
