"use client"

import { useState, useEffect, useMemo } from "react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Badge } from "@/components/ui/badge"
import { Textarea } from "@/components/ui/textarea"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from "@/components/ui/dialog"
import { Trash2, Plus, AlertTriangle, ChevronDown, ChevronUp, Stethoscope } from "lucide-react"
import { dentalRecordsApi } from "@/lib/api/dental-records"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import { odontogramApi } from "@/lib/api/odontogram"
import { showErrorToast } from "@/lib/errors"
import { toast } from "sonner"
import type { ProcedureTypeDto, DentalRecordDto, DentalActInput, PatientDto, ToothStateDto } from "@/lib/api/types"
import { formatDT, roundMillimes } from "@/lib/format"
import { CONDITION_ORDER, conditionStyle, serializeSurfaces } from "@/components/odontogram-conditions"
import { ADULT_FDI, CHILD_FDI, isAdultTooth } from "@/components/tooth-multiselect"
import { RecordToothChart, type ToothPaint } from "@/components/record-tooth-chart"
import { SessionActComposer } from "@/components/record/session-act-composer"
import { SessionActsList } from "@/components/record/session-acts-list"
import { hasInvalidPrice, resolveActCost, useSessionActs } from "@/components/record/use-session-acts"

// Sentinel for "not linked to a treatment-plan step".
const NO_PLAN_ITEM = "__none__"

/** An open treatment-plan step offered for linking a dental record (closes the plan→record loop). */
export interface PlanItemOption {
  itemId: string
  planId: string
  label: string
  /** Plan-step designation — prefilled into the composer on link (P0-1, carry-forward). */
  designationFr?: string
  /** Plan-step planned cost — prefilled into the composer on link. */
  plannedCost?: number
  /** Plan-step teeth — become the chart selection on link. */
  toothNumbers?: number[]
}

interface PatientRecordModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  patientName?: string
  patientId?: string
  record?: DentalRecordDto | null // Record to edit, null for a new record
  /** True when the record is already billed by a (non-cancelled) invoice — its payment is invoice-managed (AC-8). */
  isInvoiced?: boolean
  /** Patient — used to surface allergy / flag / medical-history alerts at the point of care. */
  patient?: PatientDto | null
  /** Open treatment-plan steps the record can complete (marks the step "réalisé" on save). */
  planItems?: PlanItemOption[]
  /** Optional appointment this record documents — completing it + dismissing its post-visit prompt on save. */
  appointmentId?: string | null
  onSuccess?: () => void
}

/**
 * Tooth-first dental-record entry: the chart on the left is a selection surface, the right pane records what
 * was done to the selected teeth and reads the session back grouped by tooth. Several procedures on one tooth
 * and one procedure across several teeth are both first-class; acts with no tooth are « actes généraux ».
 */
export function PatientRecordModal({
  open,
  onOpenChange,
  patientName: initialPatientName = "",
  patientId,
  record,
  isInvoiced = false,
  patient,
  planItems = [],
  appointmentId,
  onSuccess,
}: PatientRecordModalProps) {
  const [patientName, setPatientName] = useState(initialPatientName)
  const [interventionDate, setInterventionDate] = useState(new Date().toISOString().split("T")[0])
  // Which dentition the chart displays. Purely a view switch: acts already charted on the other dentition
  // are kept and stay listed, so a mixed-dentition session is recordable.
  const [isAdultView, setIsAdultView] = useState(true)
  const [amountPaid, setAmountPaid] = useState("")
  const [paidDirty, setPaidDirty] = useState(false)
  const [notes, setNotes] = useState<string[]>([])
  const [importantNotes, setImportantNotes] = useState<string[]>([])
  const [notesOpen, setNotesOpen] = useState(false)
  const [procedureTypes, setProcedureTypes] = useState<ProcedureTypeDto[]>([])
  const [priorStates, setPriorStates] = useState<ToothStateDto[]>([])
  const [linkedPlanItemId, setLinkedPlanItemId] = useState<string>(NO_PLAN_ITEM)
  const [loading, setLoading] = useState(false)

  const { acts, selection, draft, editingAct, editingKey, total, dispatch } = useSessionActs(record)

  // Active point-of-care alerts for this patient (allergies / flags / medical history).
  const activeFlags = (patient?.flags ?? []).filter((f) => f.isActive)
  const hasAlerts =
    Boolean(patient?.allergies?.trim()) || activeFlags.length > 0 || Boolean(patient?.medicalHistory?.trim())

  // Load the active procedure catalog (the composer's picker source) when the modal opens.
  useEffect(() => {
    if (!open) return
    procedureTypesApi
      .list(false)
      .then((data) => setProcedureTypes(data || []))
      .catch(() => setProcedureTypes([]))
  }, [open])

  // Load the patient's odontogram so the chart shows what is already on record (incl. « à traiter »
  // diagnoses) while the dentist charts today's work. Failure is silent — it is an overlay, not a gate.
  useEffect(() => {
    if (!open || !patientId) return
    let cancelled = false
    odontogramApi
      .get(patientId)
      .then((data) => {
        if (!cancelled) setPriorStates(data || [])
      })
      .catch(() => {
        if (!cancelled) setPriorStates([])
      })
    return () => {
      cancelled = true
    }
  }, [open, patientId])

  // Reset (create) or prefill (edit) the form when the modal opens — one explicit dispatch, so no effect
  // can later overwrite something the user typed.
  useEffect(() => {
    if (!open) return
    setPatientName(initialPatientName)
    setLinkedPlanItemId(NO_PLAN_ITEM)
    dispatch({ type: "reset", record })

    if (record) {
      setInterventionDate(new Date(record.interventionDate).toISOString().split("T")[0])
      setIsAdultView(record.isAdultTeeth)
      setAmountPaid(String(record.amountPaid))
      setPaidDirty(true) // a saved amount is the user's, never re-mirrored from the total
      setNotes([...record.notes])
      setImportantNotes([...record.importantNotes])
      setNotesOpen(record.notes.length > 0 || record.importantNotes.length > 0)
    } else {
      setInterventionDate(new Date().toISOString().split("T")[0])
      setIsAdultView(true)
      setAmountPaid("")
      setPaidDirty(false)
      setNotes([])
      setImportantNotes([])
      setNotesOpen(false)
    }
  }, [open, initialPatientName, record, dispatch])

  // « Montant payé » mirrors the running total until the user takes the field over. (The previous version
  // only filled it while empty, so it latched onto the first act's total and then went stale.)
  useEffect(() => {
    if (paidDirty || isInvoiced) return
    setAmountPaid(total > 0 ? String(total) : "")
  }, [total, paidDirty, isInvoiced])

  // Latest recorded state per tooth, EXCLUDING the record being edited — its own tooth states are this
  // session's output, and painting them as "prior state" would double-count them on the chart.
  const priorByTooth = useMemo(() => {
    const map = new Map<number, ToothStateDto>()
    for (const state of priorStates) {
      if (record && state.dentalRecordId === record.id) continue
      const current = map.get(state.toothNumber)
      if (!current || new Date(state.treatmentDate).getTime() > new Date(current.treatmentDate).getTime()) {
        map.set(state.toothNumber, state)
      }
    }
    return map
  }, [priorStates, record])

  const openDiagnosisTeeth = useMemo(
    () =>
      Array.from(priorByTooth.entries())
        .filter(([, s]) => s.source === "Diagnosis")
        .map(([tooth]) => tooth),
    [priorByTooth],
  )

  // Per-tooth paint: prior state as the outline, this session's acts as the fill, the live selection on top.
  const toothPaint = useMemo(() => {
    const map = new Map<number, ToothPaint>()

    for (const [tooth, state] of priorByTooth) {
      map.set(tooth, {
        selected: false,
        color: null,
        count: 0,
        existingColor: conditionStyle(state.condition).color,
        existingIsDiagnosis: state.source === "Diagnosis",
      })
    }

    for (const act of acts) {
      const color = act.resultingCondition ? conditionStyle(act.resultingCondition).color : null
      for (const tooth of act.toothNumbers) {
        const prev = map.get(tooth)
        map.set(tooth, {
          selected: prev?.selected ?? false,
          color: color ?? prev?.color ?? null,
          count: (prev?.count ?? 0) + 1,
          existingColor: prev?.existingColor ?? null,
          existingIsDiagnosis: prev?.existingIsDiagnosis,
        })
      }
    }

    for (const tooth of selection) {
      const prev = map.get(tooth)
      map.set(tooth, {
        selected: true,
        color: prev?.color ?? null,
        count: prev?.count ?? 0,
        existingColor: prev?.existingColor ?? null,
        existingIsDiagnosis: prev?.existingIsDiagnosis,
      })
    }

    return map
  }, [priorByTooth, acts, selection])

  // Acts charted on the dentition that is not currently displayed — surfaced so nothing hides behind the toggle.
  const hiddenDentitionActs = useMemo(
    () => acts.filter((a) => a.toothNumbers.some((t) => isAdultTooth(t) !== isAdultView)).length,
    [acts, isAdultView],
  )

  const viewTeeth = isAdultView ? ADULT_FDI : CHILD_FDI
  const upperQuadrants = isAdultView ? [1, 2] : [5, 6]
  const lowerQuadrants = isAdultView ? [3, 4] : [7, 8]
  const teethInQuadrants = (quadrants: number[]) => viewTeeth.filter((t) => quadrants.includes(Math.floor(t / 10)))

  const switchDentition = (adult: boolean) => {
    if (adult === isAdultView) return
    setIsAdultView(adult)
    // A selection on teeth that are no longer on screen would be a lie; the charted acts are untouched.
    dispatch({ type: "clearSelection" })
  }

  // Linking a plan step carries its designation / cost / teeth into the composer, so the dentist does not
  // retype what the plan already knows (P0-1). Only an untouched composer is prefilled.
  const handlePlanItemLink = (value: string) => {
    setLinkedPlanItemId(value)
    if (value === NO_PLAN_ITEM) return
    const item = planItems.find((p) => p.itemId === value)
    if (!item) return
    dispatch({
      type: "applyPlanItem",
      item: { designationFr: item.designationFr, plannedCost: item.plannedCost, toothNumbers: item.toothNumbers },
    })
  }

  const reste = Math.max(0, roundMillimes(total - (Number.parseFloat(amountPaid) || 0)))

  const handleSave = async () => {
    if (!patientId) {
      toast.error("Identifiant du patient requis")
      return
    }

    if (acts.length === 0) {
      toast.error("Ajoutez au moins un acte", { description: "Sélectionnez une dent puis décrivez l'acte réalisé." })
      return
    }

    const badPrice = acts.find((a) => hasInvalidPrice(a.unitCost))
    if (badPrice) {
      toast.error("Montant invalide", {
        description: `Vérifiez le tarif de l'acte « ${badPrice.procedureName} ».`,
      })
      return
    }

    const parsedActs: DentalActInput[] = acts
      .filter((a) => a.procedureName.trim() !== "")
      .map((a) => {
        const unit = Number.parseFloat(a.unitCost)
        return {
          procedureTypeId: a.procedureTypeId,
          procedureName: a.procedureName.trim(),
          cost: resolveActCost(a.unitCost, a.perTooth, a.toothNumbers.length),
          unitCost: Number.isFinite(unit) ? roundMillimes(unit) : null,
          isPerTooth: a.perTooth && a.toothNumbers.length > 0,
          toothNumbers: a.toothNumbers,
          resultingCondition: a.resultingCondition, // null when "Aucun"
          surfaces: serializeSurfaces(a.surfaces) || null,
          note: a.note.trim() || null,
        }
      })

    if (parsedActs.length === 0) {
      toast.error("Ajoutez au moins un acte", { description: "Chaque acte nécessite une désignation." })
      return
    }

    // The record's dentition flag is derived, not read from the toggle: a session may legitimately chart both
    // dentitions, so it is only a display hint — "enfant" when every charted tooth is deciduous.
    const chartedTeeth = parsedActs.flatMap((a) => a.toothNumbers)
    const isAdultTeeth = chartedTeeth.length === 0 ? isAdultView : chartedTeeth.some(isAdultTooth)

    setLoading(true)
    try {
      const linkedItem = planItems.find((p) => p.itemId === linkedPlanItemId)
      const recordData = {
        interventionDate,
        amountPaid: Number.parseFloat(amountPaid) || 0,
        isAdultTeeth,
        notes: notes.filter((n) => n.trim()).map((n) => n.trim()),
        importantNotes: importantNotes.filter((n) => n.trim()).map((n) => n.trim()),
        acts: parsedActs,
        treatmentPlanId: linkedItem?.planId ?? null,
        treatmentPlanItemId: linkedItem?.itemId ?? null,
        // Only carried on create — links the new record to the appointment it documents (closes the prompt).
        appointmentId: appointmentId ?? null,
      }

      if (record) {
        await dentalRecordsApi.update(patientId, record.id, recordData)
        toast.success("Fiche dentaire mise à jour")
      } else {
        await dentalRecordsApi.create(patientId, recordData)
        toast.success("Fiche dentaire enregistrée")
      }
      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      showErrorToast(err, "Erreur lors de l'enregistrement de la fiche.")
    } finally {
      setLoading(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[92vh] max-w-5xl overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{record ? "Modifier la fiche médicale" : "Ajouter une fiche médicale"}</DialogTitle>
          <DialogDescription>
            Cliquez une ou plusieurs dents, puis indiquez ce que vous avez fait. L'état résultant alimente
            l'odontogramme du patient.
          </DialogDescription>
        </DialogHeader>

        {/* Point-of-care medical alerts — surfaced before treatment (safety). */}
        {hasAlerts && (
          <div className="rounded-lg border border-amber-300 bg-amber-50 p-3 dark:border-amber-800 dark:bg-amber-950/40">
            <p className="flex items-center gap-1.5 text-sm font-semibold text-amber-800 dark:text-amber-200">
              <AlertTriangle className="h-4 w-4" /> Alertes médicales
            </p>
            <div className="mt-2 space-y-1.5 text-xs">
              {patient?.allergies?.trim() && (
                <p className="text-red-700 dark:text-red-300">
                  <span className="font-semibold">Allergies :</span> {patient.allergies}
                </p>
              )}
              {activeFlags.length > 0 && (
                <div className="flex flex-wrap items-center gap-1.5">
                  {activeFlags.map((f) => (
                    <Badge key={f.id} variant="destructive" className="text-[10px]">
                      {f.description || f.flagType}
                    </Badge>
                  ))}
                </div>
              )}
              {patient?.medicalHistory?.trim() && (
                <p className="text-amber-800 dark:text-amber-200">
                  <span className="font-semibold">Antécédents :</span> {patient.medicalHistory}
                </p>
              )}
            </div>
          </div>
        )}

        {/* Session header: patient, date, dentition view, optional plan-step link */}
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <div className="space-y-1.5">
            <Label htmlFor="patient-name">Patient</Label>
            <Input id="patient-name" value={patientName} readOnly className="h-9 font-medium" />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="date">Date</Label>
            <Input
              id="date"
              type="date"
              className="h-9"
              value={interventionDate}
              onChange={(e) => setInterventionDate(e.target.value)}
              disabled={loading}
            />
          </div>
          <div className="space-y-1.5">
            <Label>Schéma affiché</Label>
            <div className="flex items-center gap-1 rounded-lg bg-muted p-1">
              <Button
                type="button"
                variant={isAdultView ? "default" : "ghost"}
                size="sm"
                className="h-7 flex-1 px-3 text-xs"
                onClick={() => switchDentition(true)}
                disabled={loading}
              >
                Adulte
              </Button>
              <Button
                type="button"
                variant={!isAdultView ? "default" : "ghost"}
                size="sm"
                className="h-7 flex-1 px-3 text-xs"
                onClick={() => switchDentition(false)}
                disabled={loading}
              >
                Enfant
              </Button>
            </div>
            {hiddenDentitionActs > 0 && (
              <p className="text-[11px] text-amber-600 dark:text-amber-500">
                {hiddenDentitionActs} acte{hiddenDentitionActs > 1 ? "s" : ""} sur l'autre dentition (conservé
                {hiddenDentitionActs > 1 ? "s" : ""})
              </p>
            )}
          </div>
          {planItems.length > 0 && (
            <div className="space-y-1.5">
              <Label htmlFor="plan-item">
                Acte planifié <span className="font-normal text-muted-foreground">(optionnel)</span>
              </Label>
              <Select value={linkedPlanItemId} onValueChange={handlePlanItemLink} disabled={loading}>
                <SelectTrigger id="plan-item" className="h-9">
                  <SelectValue placeholder="Lier à un acte du plan" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={NO_PLAN_ITEM}>Aucun</SelectItem>
                  {planItems.map((p) => (
                    <SelectItem key={p.itemId} value={p.itemId}>
                      {p.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}
        </div>

        {/* Two panes: the chart is the subject picker, the session sheet is the record */}
        <div className="grid gap-4 lg:grid-cols-[minmax(320px,400px)_1fr]">
          <div className="space-y-2 lg:sticky lg:top-0 lg:self-start">
            <RecordToothChart
              isAdult={isAdultView}
              paint={toothPaint}
              onToggleTooth={(tooth) => dispatch({ type: "toggleTooth", tooth })}
              disabled={loading}
            />

            <div className="flex flex-wrap items-center gap-1.5">
              <span className="text-[11px] text-muted-foreground">Sélection rapide :</span>
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="h-6 px-2 text-[11px]"
                disabled={loading}
                onClick={() => dispatch({ type: "selectMany", teeth: teethInQuadrants(upperQuadrants), additive: true })}
              >
                Haut
              </Button>
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="h-6 px-2 text-[11px]"
                disabled={loading}
                onClick={() => dispatch({ type: "selectMany", teeth: teethInQuadrants(lowerQuadrants), additive: true })}
              >
                Bas
              </Button>
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="h-6 px-2 text-[11px]"
                disabled={loading}
                onClick={() => dispatch({ type: "selectMany", teeth: viewTeeth, additive: true })}
              >
                Toute la bouche
              </Button>
            </div>

            {openDiagnosisTeeth.length > 0 && (
              <button
                type="button"
                disabled={loading}
                onClick={() =>
                  dispatch({
                    type: "selectMany",
                    teeth: openDiagnosisTeeth.filter((t) => isAdultTooth(t) === isAdultView),
                    additive: true,
                  })
                }
                className="flex w-full items-center gap-1.5 rounded-md border border-orange-300 bg-orange-50 px-2 py-1.5 text-left text-[11px] text-orange-800 hover:bg-orange-100 disabled:cursor-not-allowed dark:border-orange-900 dark:bg-orange-950/40 dark:text-orange-200"
              >
                <Stethoscope className="h-3.5 w-3.5 shrink-0" />
                <span>
                  {openDiagnosisTeeth.length} dent{openDiagnosisTeeth.length > 1 ? "s" : ""} à traiter (
                  {openDiagnosisTeeth.join(", ")}) — cliquez pour sélectionner
                </span>
              </button>
            )}

            <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-[11px]">
              {CONDITION_ORDER.filter((c) => c !== "Sain").map((c) => (
                <span key={c} className="flex items-center gap-1">
                  <span className="h-2.5 w-2.5 rounded-full" style={{ backgroundColor: conditionStyle(c).color }} />
                  <span className="text-muted-foreground">{conditionStyle(c).label}</span>
                </span>
              ))}
              <span className="flex items-center gap-1">
                <span className="h-2.5 w-2.5 rounded-full border-2 border-dashed border-muted-foreground/70" />
                <span className="text-muted-foreground">contour = état déjà au dossier</span>
              </span>
            </div>
          </div>

          <div className="space-y-3">
            <div className="flex flex-wrap items-baseline justify-between gap-2">
              <Label>Séance</Label>
              <span className="text-xs text-muted-foreground">
                {acts.length} acte{acts.length > 1 ? "s" : ""} ·{" "}
                <span className="font-semibold text-foreground">{formatDT(total)}</span>
              </span>
            </div>

            <SessionActComposer
              draft={draft}
              selection={selection}
              procedureTypes={procedureTypes}
              editingAct={editingAct}
              dispatch={dispatch}
              disabled={loading}
            />

            <SessionActsList
              acts={acts}
              editingKey={editingKey}
              onEdit={(key) => dispatch({ type: "beginEditAct", key })}
              onRemove={(key) => dispatch({ type: "removeAct", key })}
              disabled={loading}
            />
          </div>
        </div>

        {/* Totals + payment */}
        <div className="grid gap-4 rounded-lg border bg-muted/30 p-3 sm:grid-cols-3">
          <div className="space-y-1.5">
            <Label htmlFor="paid">Montant payé (DT)</Label>
            <Input
              id="paid"
              type="number"
              min="0"
              step="0.001"
              value={amountPaid}
              onChange={(e) => {
                setPaidDirty(true)
                setAmountPaid(e.target.value)
              }}
              placeholder="0.000"
              disabled={loading || isInvoiced}
            />
          </div>
          <div className="flex items-end">
            {isInvoiced ? (
              <p className="text-xs text-muted-foreground">
                Facturé — le paiement est géré par la facture (voir l'onglet Factures).
              </p>
            ) : (
              <p className="text-xs text-muted-foreground">
                Reste à payer :{" "}
                <span className={reste > 0 ? "font-semibold text-amber-600" : "font-medium text-foreground"}>
                  {formatDT(reste)}
                </span>
              </p>
            )}
          </div>
          <div className="flex items-end justify-end text-sm">
            <span className="text-muted-foreground">Total :&nbsp;</span>
            <span className="font-semibold">{formatDT(total)}</span>
          </div>
        </div>

        {/* Notes — collapsed unless the record already carries some */}
        <div className="space-y-3">
          <Button
            type="button"
            variant="ghost"
            size="sm"
            className="h-8 w-full justify-between px-2 text-xs"
            onClick={() => setNotesOpen((v) => !v)}
          >
            <span>
              Notes de séance{" "}
              <span className="font-normal text-muted-foreground">
                {notes.length + importantNotes.length > 0 ? `(${notes.length + importantNotes.length})` : "(facultatif)"}
              </span>
            </span>
            {notesOpen ? <ChevronUp className="h-4 w-4" /> : <ChevronDown className="h-4 w-4" />}
          </Button>

          {notesOpen && (
            <div className="grid gap-4 sm:grid-cols-2">
              <div className="space-y-2">
                <Label className="text-xs">Notes</Label>
                {notes.map((note, index) => (
                  <div key={index} className="flex gap-2">
                    <Textarea
                      value={note}
                      onChange={(e) => {
                        const next = [...notes]
                        next[index] = e.target.value
                        setNotes(next)
                      }}
                      placeholder="Saisir une note…"
                      className="min-h-[70px] resize-y text-sm"
                      disabled={loading}
                    />
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      className="shrink-0"
                      onClick={() => setNotes(notes.filter((_, i) => i !== index))}
                      disabled={loading}
                      aria-label="Supprimer la note"
                    >
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                ))}
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => setNotes([...notes, ""])}
                  className="w-full"
                  disabled={loading}
                >
                  <Plus className="mr-1 h-4 w-4" /> Ajouter une note
                </Button>
              </div>

              <div className="space-y-2">
                <Label className="text-xs">
                  Notes importantes
                  <span className="ml-2 text-[11px] text-amber-600 dark:text-amber-500">⚠ Mises en évidence</span>
                </Label>
                {importantNotes.map((note, index) => (
                  <div key={index} className="flex gap-2">
                    <Textarea
                      value={note}
                      onChange={(e) => {
                        const next = [...importantNotes]
                        next[index] = e.target.value
                        setImportantNotes(next)
                      }}
                      placeholder="Saisir une note importante…"
                      className="min-h-[70px] resize-y border-amber-300 bg-amber-50/50 text-sm dark:border-amber-700 dark:bg-amber-950/20"
                      disabled={loading}
                    />
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      className="shrink-0"
                      onClick={() => setImportantNotes(importantNotes.filter((_, i) => i !== index))}
                      disabled={loading}
                      aria-label="Supprimer la note importante"
                    >
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                ))}
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => setImportantNotes([...importantNotes, ""])}
                  className="w-full border-amber-300 dark:border-amber-700"
                  disabled={loading}
                >
                  <Plus className="mr-1 h-4 w-4" /> Ajouter une note importante
                </Button>
              </div>
            </div>
          )}
        </div>

        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={loading}>
            Annuler
          </Button>
          <Button onClick={handleSave} disabled={loading} className="min-w-[140px]">
            {loading ? "Enregistrement…" : record ? "Enregistrer" : "Créer la fiche"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
