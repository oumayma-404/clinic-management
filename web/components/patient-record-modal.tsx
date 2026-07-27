"use client"

import { useState, useEffect, useMemo } from "react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Badge } from "@/components/ui/badge"
import { Textarea } from "@/components/ui/textarea"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from "@/components/ui/dialog"
import { Trash2, Plus, AlertTriangle, Stethoscope } from "lucide-react"
import { dentalRecordsApi } from "@/lib/api/dental-records"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import { odontogramApi } from "@/lib/api/odontogram"
import { showErrorToast } from "@/lib/errors"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { useConflict } from "@/lib/hooks/use-conflict"
import { toast } from "sonner"
import type {
  ProcedureTypeDto,
  DentalRecordDto,
  DentalActInput,
  PatientDto,
  ToothStateDto,
  AppointmentDto,
} from "@/lib/api/types"
import { formatDT, roundMillimes } from "@/lib/format"
import { CONDITION_ORDER, conditionStyle, serializeSurfaces } from "@/components/odontogram-conditions"
import { ADULT_FDI, CHILD_FDI, isAdultTooth } from "@/components/tooth-multiselect"
import { RecordToothChart, type ToothPaint } from "@/components/record-tooth-chart"
import { ActSlot } from "@/components/record/act-slot"
import { ActDetailFields } from "@/components/record/act-detail-fields"
import { RecordSection } from "@/components/record/record-section"
import { SessionActsList } from "@/components/record/session-acts-list"
import { hasInvalidPrice, resolveActCost, useSessionActs, type SessionAct } from "@/components/record/use-session-acts"

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
  /** True when the record is already billed by a (non-cancelled) invoice — its payment is invoice-managed. */
  isInvoiced?: boolean
  /** Patient — used to surface allergy / flag / medical-history alerts at the point of care. */
  patient?: PatientDto | null
  /** Open treatment-plan steps the record can complete (marks the step "réalisé" on save). */
  planItems?: PlanItemOption[]
  /** Optional appointment this record documents — completing it + dismissing its post-visit prompt on save. */
  appointmentId?: string | null
  /**
   * The appointment being documented, when known. Its booked `procedureTypeId` PROPOSES the act, so the
   * common visit is "tap the teeth, confirm". Nothing is committed from it — see `ActSlot`.
   */
  appointment?: AppointmentDto | null
  onSuccess?: () => void
}

/**
 * Confirm-first dental-record entry. The act comes first — proposed from the appointment when there is one,
 * otherwise picked from the catalogue — then the chart says which teeth, then « Confirmer » saves. Everything
 * the two-pane form carried (tarif and per-tooth pricing, état résultant, faces, notes, montant payé, several
 * acts per session, mixed dentition) is still here, folded into sections whose headers state their own
 * contents so nothing is hidden by being collapsed.
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
  appointment,
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
  const [procedureTypes, setProcedureTypes] = useState<ProcedureTypeDto[]>([])
  const [priorStates, setPriorStates] = useState<ToothStateDto[]>([])
  const [linkedPlanItemId, setLinkedPlanItemId] = useState<string>(NO_PLAN_ITEM)
  const [openSections, setOpenSections] = useState({ details: false, acts: false, notes: false })
  const [loading, setLoading] = useState(false)
  // A save conflict stays in the form; everything else keeps the existing toast.
  const conflict = useConflict()

  const { acts, selection, draft, hasDraft, draftTotal, grandTotal, editingAct, editingKey, dispatch } =
    useSessionActs(record)

  const toggleSection = (key: keyof typeof openSections) =>
    setOpenSections((prev) => ({ ...prev, [key]: !prev[key] }))

  // Active point-of-care alerts for this patient (allergies / flags / medical history).
  const activeFlags = (patient?.flags ?? []).filter((f) => f.isActive)
  const hasAlerts =
    Boolean(patient?.allergies?.trim()) || activeFlags.length > 0 || Boolean(patient?.medicalHistory?.trim())

  // Load the active procedure catalog (the picker's source) when the modal opens.
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
      // A section holding a value opens itself, so editing can never look like it lost data.
      setOpenSections({
        details: false,
        acts: record.acts.length > 0,
        notes: record.notes.length > 0 || record.importantNotes.length > 0,
      })
    } else {
      setInterventionDate(new Date().toISOString().split("T")[0])
      setIsAdultView(true)
      setAmountPaid("")
      setPaidDirty(false)
      setNotes([])
      setImportantNotes([])
      setOpenSections({ details: false, acts: false, notes: false })
    }
  }, [open, initialPatientName, record, dispatch])

  // AC-9: an appointment booked FROM a plan step already knows which act this visit is for, so opening the
  // record from it pre-selects that step — the dentist no longer has to find it in the dropdown to close the
  // loop. Runs before the procedure proposal below so the plan act wins when both are available: the plan
  // step is the more specific truth (it carries the agreed désignation, cost and teeth), and both dispatches
  // no-op on a non-empty draft, so whichever lands first keeps it.
  //
  // Only pre-selects a step that is actually in `planItems` — that list holds the plan's OPEN steps, so an
  // act already marked réalisé (or on a cancelled plan) correctly falls through to the normal flow.
  useEffect(() => {
    if (!open || record || !appointment?.treatmentPlanItemId) return
    const linked = planItems.find((p) => p.itemId === appointment.treatmentPlanItemId)
    if (!linked) return

    setLinkedPlanItemId(linked.itemId)
    dispatch({
      type: "applyPlanItem",
      item: {
        designationFr: linked.designationFr,
        plannedCost: linked.plannedCost,
        toothNumbers: linked.toothNumbers,
      },
    })
  }, [open, record, appointment, planItems, dispatch])

  // Option C: propose the appointment's booked procedure. Runs after the reset above and is itself guarded —
  // `applyAppointment` is a no-op unless the session is untouched, so it can never clobber a saved record or
  // work in progress. A record being edited is never re-proposed.
  useEffect(() => {
    if (!open || record || !appointment?.procedureTypeId || procedureTypes.length === 0) return
    const booked = procedureTypes.find((p) => p.id === appointment.procedureTypeId)
    if (booked) dispatch({ type: "applyAppointment", procedure: booked })
  }, [open, record, appointment, procedureTypes, dispatch])

  // « Montant payé » mirrors the running total until the user takes the field over.
  useEffect(() => {
    if (paidDirty || isInvoiced) return
    setAmountPaid(grandTotal > 0 ? String(grandTotal) : "")
  }, [grandTotal, paidDirty, isInvoiced])

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

  // Per-tooth paint: prior state as the outline, confirmed acts as the fill, the live selection on top.
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

  // Linking a plan step carries its designation / cost / teeth into the draft, so the dentist does not
  // retype what the plan already knows. Only an untouched draft is prefilled.
  const handlePlanItemLink = (value: string) => {
    setLinkedPlanItemId(value)
    if (value === NO_PLAN_ITEM) return
    const item = planItems.find((p) => p.itemId === value)
    if (!item) return
    dispatch({
      type: "applyPlanItem",
      item: { designationFr: item.designationFr, plannedCost: item.plannedCost, toothNumbers: item.toothNumbers },
    })
    setOpenSections((prev) => ({ ...prev, details: true }))
  }

  const reste = Math.max(0, roundMillimes(grandTotal - (Number.parseFloat(amountPaid) || 0)))

  /**
   * Everything that will be persisted: the confirmed acts, with the in-progress draft folded in. Pressing
   * « Confirmer » on a proposed act must save it without a separate "add" step — that IS the confirm-first
   * flow — so the draft is materialised here rather than requiring a commit dispatch first.
   */
  const actsToPersist = useMemo<SessionAct[]>(() => {
    if (!hasDraft) return acts
    const materialised: SessionAct = { ...draft, key: editingKey ?? "draft", toothNumbers: [...selection] }
    return editingKey ? acts.map((a) => (a.key === editingKey ? materialised : a)) : [...acts, materialised]
  }, [acts, draft, hasDraft, editingKey, selection])

  const handleSave = async () => {
    if (!patientId) {
      toast.error("Identifiant du patient requis")
      return
    }

    if (actsToPersist.length === 0) {
      toast.error("Ajoutez au moins un acte", { description: "Choisissez l'acte réalisé, puis les dents." })
      return
    }

    const badPrice = actsToPersist.find((a) => hasInvalidPrice(a.unitCost))
    if (badPrice) {
      setOpenSections((prev) => ({ ...prev, details: true }))
      toast.error("Montant invalide", {
        description: `Vérifiez le tarif de l'acte « ${badPrice.procedureName} ».`,
      })
      return
    }

    const parsedActs: DentalActInput[] = actsToPersist
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
        await dentalRecordsApi.update(patientId, record.id, { ...recordData, version: record.version })
        toast.success("Fiche dentaire mise à jour")
      } else {
        await dentalRecordsApi.create(patientId, recordData)
        toast.success("Fiche dentaire enregistrée")
      }
      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      // A conflict is not a transient blip — a colleague saved this fiche while it was open — so it stays
      // in the form rather than flashing past in a toast.
      if (!conflict.capture(err, "Erreur lors de l'enregistrement de la fiche.")) {
        showErrorToast(err, "Erreur lors de l'enregistrement de la fiche.")
      }
    } finally {
      setLoading(false)
    }
  }

  // ── collapsed-section summaries: the section's contents, so folding hides nothing ──
  const detailsSummary = useMemo(() => {
    if (!hasDraft) return "aucun acte en cours"
    const faces = Array.from(draft.surfaces)
    const bits = [
      draft.resultingCondition ? conditionStyle(draft.resultingCondition).label : "aucun état",
      `${formatDT(Number.parseFloat(draft.unitCost) || 0)}${draft.perTooth ? " / dent" : " forfait"}`,
      faces.length > 0 ? `faces ${faces.join(", ")}` : "aucune face",
    ]
    if (draft.note.trim()) bits.push("note")
    return bits.join(" · ")
  }, [hasDraft, draft])

  const actsSummary =
    acts.length === 0
      ? "aucun acte confirmé"
      : `${acts.length} acte${acts.length > 1 ? "s" : ""} · ${formatDT(
          roundMillimes(acts.reduce((s, a) => s + resolveActCost(a.unitCost, a.perTooth, a.toothNumbers.length), 0)),
        )}`

  const noteCount = notes.filter((n) => n.trim()).length
  const importantCount = importantNotes.filter((n) => n.trim()).length
  const notesSummary =
    noteCount + importantCount === 0
      ? "aucune note"
      : [noteCount > 0 ? `${noteCount} note${noteCount > 1 ? "s" : ""}` : null,
         importantCount > 0 ? `${importantCount} importante${importantCount > 1 ? "s" : ""}` : null]
          .filter(Boolean)
          .join(" · ")

  const proposedFromAppointment =
    !record && !editingAct && acts.length === 0 && draft.procedureTypeId != null &&
    draft.procedureTypeId === appointment?.procedureTypeId

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      {/* NOTE: the width override MUST be `sm:max-w-*`. DialogContent's base class ends with `sm:max-w-lg`,
          and tailwind-merge treats an unprefixed `max-w-*` as a different group — so a plain `max-w-3xl`
          silently loses to it at every viewport ≥640px and the dialog stays 512px wide. */}
      <DialogContent className="max-h-[92vh] w-[min(96vw,780px)] gap-3 overflow-y-auto sm:max-w-[min(96vw,780px)]">
        <DialogHeader>
          <DialogTitle>{record ? "Modifier la fiche médicale" : "Ajouter une fiche médicale"}</DialogTitle>
          <DialogDescription>
            {record
              ? "Les sections qui portent une valeur sont déjà dépliées."
              : appointment?.procedureTypeName
                ? "Confirmez ce qui a été réalisé, puis les dents concernées."
                : "Indiquez l'acte réalisé, puis les dents concernées."}
          </DialogDescription>
        </DialogHeader>

        <FormErrorBanner message={conflict.error} />

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
                {hiddenDentitionActs} acte{hiddenDentitionActs > 1 ? "s" : ""} sur l&apos;autre dentition
                (conservé{hiddenDentitionActs > 1 ? "s" : ""})
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

        {/* THE ACT — proposed from the appointment, or picked from the catalogue. One slot, two states. */}
        <ActSlot
          draft={draft}
          hasDraft={hasDraft}
          draftTotal={draftTotal}
          procedureTypes={procedureTypes}
          proposedFromAppointment={proposedFromAppointment}
          editingAct={editingAct}
          dispatch={dispatch}
          disabled={loading}
        />

        {/* THE TEETH — full width, no longer competing with a form column for space. */}
        <div className="space-y-2">
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <Label>{hasDraft ? "Sur quelle(s) dent(s) ?" : "Dents concernées"}</Label>
            <div className="flex flex-wrap items-center gap-1.5">
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
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="h-6 px-2 text-[11px]"
                disabled={loading || selection.length === 0}
                onClick={() => dispatch({ type: "clearSelection" })}
              >
                Vider
              </Button>
            </div>
          </div>

          <RecordToothChart
            isAdult={isAdultView}
            paint={toothPaint}
            onToggleTooth={(tooth) => dispatch({ type: "toggleTooth", tooth })}
            disabled={loading}
          />

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

          {/* Selection + the live per-tooth arithmetic, so the billed number is never a surprise. */}
          <div className="flex min-h-[24px] flex-wrap items-center gap-1.5 text-xs">
            {selection.length > 0 ? (
              <>
                <span className="text-muted-foreground">Sélection :</span>
                {selection.map((t) => (
                  <Badge key={t} variant="secondary" className="text-xs tabular-nums">
                    {t}
                  </Badge>
                ))}
              </>
            ) : (
              <span className="italic text-muted-foreground">
                Aucune dent — l&apos;acte sera enregistré comme acte général (détartrage, panoramique…)
              </span>
            )}
            {hasDraft && (
              <span className="ml-auto tabular-nums text-muted-foreground">
                {draft.perTooth && selection.length > 0 && (
                  <>
                    {formatDT(Number.parseFloat(draft.unitCost) || 0)} × {selection.length} dent
                    {selection.length > 1 ? "s" : ""} ={" "}
                  </>
                )}
                <span className="font-semibold text-foreground">{formatDT(draftTotal)}</span>
              </span>
            )}
          </div>

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

        {/* THE DETAIL — folded, never removed, always summarised. */}
        <RecordSection
          title="Détails de l'acte"
          summary={detailsSummary}
          open={openSections.details}
          onToggle={() => toggleSection("details")}
        >
          {hasDraft ? (
            <ActDetailFields draft={draft} toothCount={selection.length} dispatch={dispatch} disabled={loading} />
          ) : (
            <p className="text-xs italic text-muted-foreground">
              Choisissez d&apos;abord un acte : son tarif et son état résultant seront préremplis depuis le
              catalogue.
            </p>
          )}
        </RecordSection>

        <RecordSection
          title="Actes de la séance"
          summary={actsSummary}
          open={openSections.acts}
          onToggle={() => toggleSection("acts")}
        >
          <SessionActsList
            acts={acts}
            editingKey={editingKey}
            onEdit={(key) => {
              dispatch({ type: "beginEditAct", key })
              setOpenSections((prev) => ({ ...prev, details: true }))
            }}
            onRemove={(key) => dispatch({ type: "removeAct", key })}
            disabled={loading}
          />
          {editingAct && (
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="h-8 justify-self-start text-xs"
              onClick={() => dispatch({ type: "cancelEdit" })}
              disabled={loading}
            >
              Annuler la modification
            </Button>
          )}
        </RecordSection>

        <RecordSection
          title="Notes de séance"
          summary={notesSummary}
          open={openSections.notes}
          onToggle={() => toggleSection("notes")}
          highlight={importantCount > 0}
        >
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
        </RecordSection>

        {/* Totals + payment — the number that will be saved, always on screen. */}
        <div className="grid gap-3 rounded-lg border bg-muted/30 p-3 sm:grid-cols-3">
          <div className="flex items-center gap-2">
            <Label htmlFor="paid" className="shrink-0 text-xs text-muted-foreground">
              Payé
            </Label>
            <Input
              id="paid"
              type="number"
              min="0"
              step="0.001"
              className="h-8 text-right tabular-nums"
              value={amountPaid}
              onChange={(e) => {
                setPaidDirty(true)
                setAmountPaid(e.target.value)
              }}
              placeholder="0.000"
              disabled={loading || isInvoiced}
            />
          </div>
          <div className="flex items-center text-xs">
            {isInvoiced ? (
              <p className="text-muted-foreground">Facturé — le paiement est géré par la facture.</p>
            ) : (
              <p className="text-muted-foreground">
                Reste à payer :{" "}
                <span className={reste > 0 ? "font-semibold text-amber-600" : "font-medium text-foreground"}>
                  {formatDT(reste)}
                </span>
              </p>
            )}
          </div>
          <div className="flex items-center justify-end gap-2 text-sm">
            <span className="text-muted-foreground">Total</span>
            <span className="text-base font-semibold tabular-nums">{formatDT(grandTotal)}</span>
          </div>
        </div>

        <DialogFooter className="gap-2 sm:justify-between">
          {/* Confirms the act in hand and clears the draft, keeping the selection so a second procedure on the
              same tooth is one pick away. Saving does NOT require this — the draft is persisted either way. */}
          <Button
            type="button"
            variant="outline"
            onClick={() => {
              dispatch({ type: "commitDraft" })
              setOpenSections((prev) => ({ ...prev, acts: true }))
            }}
            disabled={loading || !hasDraft}
            className="sm:mr-auto"
          >
            <Plus className="mr-1 h-4 w-4" /> Ajouter un autre acte
          </Button>
          <div className="flex gap-2">
            <Button variant="outline" onClick={() => onOpenChange(false)} disabled={loading}>
              Annuler
            </Button>
            <Button onClick={handleSave} disabled={loading} className="min-w-[150px]">
              {loading
                ? "Enregistrement…"
                : record
                  ? "Enregistrer"
                  : appointmentId
                    ? "Confirmer la séance"
                    : "Créer la fiche"}
            </Button>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
