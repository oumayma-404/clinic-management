"use client"

import { useState, useEffect, useCallback, useMemo, useRef } from "react"
import { Button } from "@/components/ui/button"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Badge } from "@/components/ui/badge"
import { Textarea } from "@/components/ui/textarea"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Dialog, DialogBody, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from "@/components/ui/dialog"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import { Trash2, Plus, Stethoscope } from "lucide-react"
import { PatientAlertPanel } from "@/components/patient/patient-alert-panel"
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
import { formatAmount, formatDT, parseAmountInput, roundMillimes, todayLocalIso, toLocalIso } from "@/lib/format"
import {
  CONDITION_ORDER, conditionStyle, needsTreatment, serializeSurfaces,
} from "@/components/odontogram-conditions"
import { ARCH_QUADRANTS_BY_VIEW, FDI_BY_VIEW, isAdultTooth } from "@/components/tooth-multiselect"
import { dentitionViewFor, dentitionViewForTeeth, type DentitionView } from "@/lib/dentition"
import { DentitionViewSwitch } from "@/components/dentition-view-switch"
import { RecordToothChart, type ToothPaint } from "@/components/record-tooth-chart"
import { ActSlot } from "@/components/record/act-slot"
import { ActDetailFields } from "@/components/record/act-detail-fields"
import { RecordSection } from "@/components/record/record-section"
import { SessionActsList } from "@/components/record/session-acts-list"
import { hasInvalidPrice, resolveActCost, useSessionActs, type SessionAct } from "@/components/record/use-session-acts"
import {
  CHEQUE_METHOD,
  ChequeFields,
  EMPTY_CHEQUE_FIELDS,
  chequePaymentFields,
  type ChequeFieldsValue,
} from "@/components/factures/cheque-fields"

/**
 * The methods a séance can be settled with, and their French labels — the same four `PaymentMethod` storage keys
 * the till and the échéancier offer, because a payment recorded at the chair is the same kind of row as one
 * recorded at the desk and lands in the same ledger.
 *
 * <p>`Cash` is the default rather than an empty « choisir… »: it is overwhelmingly the common case in a Tunisian
 * cabinet, and a required extra tap on every fiche is how a field gets ignored.</p>
 */
const CASH_METHOD = "Cash"
const FICHE_PAYMENT_METHODS: { value: string; label: string }[] = [
  { value: CASH_METHOD, label: "Espèces" },
  { value: CHEQUE_METHOD, label: "Chèque" },
  { value: "Card", label: "Carte" },
  { value: "Transfer", label: "Virement" },
]

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
  /*
   * ⚠️ `todayLocalIso()`, never `new Date().toISOString().split("T")[0]` (AC-P6.5).
   *
   * `toISOString` converts to UTC first, so between 00:00 and 01:00 in Tunis (UTC+1) it pre-filled *yesterday* —
   * and on the 1st, the previous month. This particular date is not cosmetic: it is inherited by
   * `POST /invoices/from-dental-record` as the note d'honoraires' date and by every `ToothState.treatmentDate`
   * the fiche writes, so it is a money date AND a clinical one, landing in the form as a plausible value nobody
   * re-reads.
   */
  const [interventionDate, setInterventionDate] = useState(todayLocalIso())
  /**
   * Which dentition the chart displays — **seeded, then the user's**.
   *
   * <p>Three states, and the distinction between them is the whole fix. It began as a local Adulte/Enfant switch
   * defaulting to Adulte (so a child's visit started by noticing the chart was wrong), which was then replaced by a
   * pure derivation from the patient — and *that* is what made the mixed stage unchartable: an eight-year-old
   * charted `Child` had no way to record a permanent 36, and one charted `Adult` had no way to record a remaining
   * 75. The server always allowed both (`DentalRecordActParser`), so the UI was the narrower half.</p>
   *
   * <p>`chosenView === null` means "the user has not chosen", **not** "adult" — the same distinction
   * `ToothArchLayout` draws for `defaultArch`, and for the same reason: `patient` arrives from an async read, so
   * seeding `useState(...)` would freeze the answer at the frame before the data existed. Resolving at render lets
   * a late seed land, and any deliberate tap wins from then on.</p>
   *
   * <p>The seed keeps what the old derivation protected: a fiche saved on baby teeth reopens on baby teeth, or its
   * acts would all read as "on the other dentition" and the chart would open empty. It reads the record's **own
   * acts** rather than its `isAdultTeeth` flag, so a fiche that genuinely spans both dentitions reopens on Mixte —
   * which the flag, being one boolean over a whole session, cannot say.</p>
   */
  const [chosenView, setChosenView] = useState<DentitionView | null>(null)
  const seededView = useMemo<DentitionView>(
    () =>
      record
        ? (dentitionViewForTeeth(record.toothNumbers, isAdultTooth) ?? (record.isAdultTeeth ? "adult" : "child"))
        : dentitionViewFor(patient?.dentition),
    [record, patient?.dentition],
  )
  const dentitionView = chosenView ?? seededView
  const [amountPaid, setAmountPaid] = useState("")
  const [paidDirty, setPaidDirty] = useState(false)
  /**
   * How the séance was settled, and — for a cheque — which cheque.
   *
   * <p>The payment this save produces used to be booked as cash unconditionally, so a patient handing over a
   * post-dated cheque at the end of a session produced a payment indistinguishable from notes in the drawer:
   * absent from « Chèques à encaisser », counted under « dont espèces ». A cheque at the chair is exactly as
   * common here as at the till.</p>
   */
  const [paymentMethod, setPaymentMethod] = useState<string>(CASH_METHOD)
  const [cheque, setCheque] = useState<ChequeFieldsValue>(EMPTY_CHEQUE_FIELDS)
  const [notes, setNotes] = useState<string[]>([])
  const [importantNotes, setImportantNotes] = useState<string[]>([])
  const [procedureTypes, setProcedureTypes] = useState<ProcedureTypeDto[]>([])
  /** The catalogue read failed — kept apart from a clinic that genuinely has no act configured. */
  const [catalogFailed, setCatalogFailed] = useState(false)
  const [priorStates, setPriorStates] = useState<ToothStateDto[]>([])
  const [linkedPlanItemId, setLinkedPlanItemId] = useState<string>(NO_PLAN_ITEM)
  const [openSections, setOpenSections] = useState({ details: false, acts: false, notes: false })
  const [loading, setLoading] = useState(false)
  const guard = useDirtyGuard(open, onOpenChange)
  // A save conflict stays in the form; everything else keeps the existing toast.
  const conflict = useConflict()

  /**
   * The refusal that blocked the last « Confirmer », **anchored to the part of the form that caused it**.
   *
   * <p>All three of this dialog's validation refusals used to be toasts and nothing else. On a phone sonner lands
   * bottom-centre — directly over this dialog's own footer, i.e. over the button just pressed — and is gone in
   * four seconds, so the dentist presses « Confirmer la séance » again and gets the same flash. Nothing on the
   * form itself ever said which field was wrong; « Montant invalide » does not say *which act's* montant, and the
   * offending one may be three sections down and folded shut.</p>
   *
   * <p>The toast is kept as the *secondary* announcement (it is what a user glancing away notices), but the
   * authority is now the inline message: it persists, it names the act, and the region it belongs to is scrolled
   * into view. `aria-invalid` lives on the individual inputs in `ActDetailFields`, which is where a screen reader
   * expects it.</p>
   */
  const [saveError, setSaveError] = useState<{ anchor: "act" | "details"; message: string } | null>(null)
  const actAnchorRef = useRef<HTMLDivElement>(null)
  const detailsAnchorRef = useRef<HTMLDivElement>(null)

  const { acts, selection, draft, hasDraft, draftTotal, grandTotal, editingAct, editingKey, dispatch } =
    useSessionActs(record)

  const toggleSection = (key: keyof typeof openSections) =>
    setOpenSections((prev) => ({ ...prev, [key]: !prev[key] }))

  /*
   * Load the active procedure catalog (the picker's source) when the modal opens.
   *
   * ⚠️ A failure is **recorded**, not written back as `[]`. An empty catalogue is not a neutral state here: the act
   * picker falls through to its free-text row, so the dentist names the act by hand and the fiche is saved with no
   * `procedureTypeId` — no tarif, no état résultant, nothing for the odontogram to paint and nothing for the note
   * d'honoraires to price. « Aucun acte au catalogue » and « le catalogue n'a pas répondu » look identical and only
   * one of them means "type it yourself".
   */
  const loadCatalog = useCallback(async () => {
    try {
      setProcedureTypes((await procedureTypesApi.list(false)) || [])
      setCatalogFailed(false)
    } catch {
      setCatalogFailed(true)
    }
  }, [])

  useEffect(() => {
    if (!open) return
    void loadCatalog()
  }, [open, loadCatalog])

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
    // Back to the seed: an arch the user picked for the *previous* fiche must not decide this one's.
    setChosenView(null)
    dispatch({ type: "reset", record })

    if (record) {
      // The read-back half of the same defect: the stored instant was round-tripped through UTC, so a fiche
      // saved late in the evening reopened showing the previous calendar day — and re-saving wrote that day back.
      setInterventionDate(toLocalIso(new Date(record.interventionDate)))
      // `formatAmount`, never `String(...)` (J8) — the field accepts the comma form the product prints with.
      setAmountPaid(formatAmount(record.amountPaid))
      setPaidDirty(true) // a saved amount is the user's, never re-mirrored from the total
      // A fiche with no method recorded is cash — that is what every row written before the field existed is,
      // and the server reads a null the same way.
      setPaymentMethod(record.paymentMethod ?? CASH_METHOD)
      setCheque({
        number: record.chequeNumber ?? "",
        bankName: record.chequeBankName ?? "",
        // The stored value is a calendar day; slice rather than re-parse, so no timezone touches it.
        dueDate: record.chequeDueDate ? record.chequeDueDate.slice(0, 10) : "",
      })
      setNotes([...record.notes])
      setImportantNotes([...record.importantNotes])
      // A section holding a value opens itself, so editing can never look like it lost data.
      setOpenSections({
        details: false,
        acts: record.acts.length > 0,
        notes: record.notes.length > 0 || record.importantNotes.length > 0,
      })
    } else {
      setInterventionDate(todayLocalIso())
      setAmountPaid("")
      setPaidDirty(false)
      setPaymentMethod(CASH_METHOD)
      setCheque(EMPTY_CHEQUE_FIELDS)
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
    setAmountPaid(grandTotal > 0 ? formatAmount(grandTotal) : "")
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

  /**
   * Teeth this patient has open work on: their most recent charted state is a **diagnosis** *and* that diagnosis
   * calls for treatment.
   *
   * <p>The `needsTreatment` half is the fix for a banner that over-claimed. It filtered on `source` alone, so a
   * tooth charted « Obturation » or « Couronne » — an observation that the tooth is *already restored* — was
   * counted as « à traiter » and bulk-selected alongside the real caries. « 5 dents à traiter » listed teeth that
   * needed nothing, which is the kind of number a dentist stops trusting after the first time.</p>
   */
  const openDiagnosisTeeth = useMemo(
    () =>
      Array.from(priorByTooth.entries())
        .filter(([, s]) => s.source === "Diagnosis" && needsTreatment(s.condition))
        .map(([tooth]) => tooth)
        .sort((a, b) => a - b),
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

  const viewTeeth = FDI_BY_VIEW[dentitionView]
  const { upper: upperQuadrants, lower: lowerQuadrants } = ARCH_QUADRANTS_BY_VIEW[dentitionView]
  const teethInQuadrants = (quadrants: number[]) => viewTeeth.filter((t) => quadrants.includes(Math.floor(t / 10)))

  // Acts charted on teeth the current view does not draw — surfaced so nothing hides behind the switch. Tested
  // against the view's own tooth set rather than `isAdultTooth`, which is what makes the count read **zero** on
  // Mixte: that view draws both dentitions, so there is nothing left off-screen to warn about.
  const hiddenDentitionActs = useMemo(
    () => acts.filter((a) => a.toothNumbers.some((t) => !viewTeeth.includes(t))).length,
    [acts, viewTeeth],
  )

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

  const reste = Math.max(0, roundMillimes(grandTotal - (parseAmountInput(amountPaid) || 0)))

  /**
   * Refuse the save: mark the region, scroll it into view, and *also* toast.
   *
   * <p>Order matters — the state is set before the scroll so the message exists by the time the region is on
   * screen, and `block: "center"` rather than `"nearest"` because the offending field is often just above the
   * footer the user is looking at, where "nearest" would move nothing at all.</p>
   */
  const refuseSave = (anchor: "act" | "details", message: string, description?: string) => {
    setSaveError({ anchor, message })
    const target = anchor === "act" ? actAnchorRef.current : detailsAnchorRef.current
    target?.scrollIntoView({ block: "center", behavior: "smooth" })
    toast.error(message, description ? { description } : undefined)
  }

  // Any edit to the acts or the draft clears the refusal: an inline error that outlives the thing it described
  // is worse than none, because the next press is refused for a reason the message no longer names.
  useEffect(() => {
    setSaveError(null)
  }, [acts, draft])

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
      refuseSave("act", "Ajoutez au moins un acte", "Choisissez l'acte réalisé, puis les dents.")
      return
    }

    const badPrice = actsToPersist.find((a) => hasInvalidPrice(a.unitCost))
    if (badPrice) {
      setOpenSections((prev) => ({ ...prev, details: true }))
      refuseSave(
        "details",
        `Montant invalide pour « ${badPrice.procedureName} »`,
        "Corrigez le tarif de l'acte, puis confirmez.",
      )
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
      // Reached only when every act was filtered out for having no name — which is a *désignation* problem, so
      // it anchors on the detail fields (where the editable désignation lives) rather than on the act slot.
      setOpenSections((prev) => ({ ...prev, details: true }))
      refuseSave("details", "Chaque acte nécessite une désignation", "Nommez l'acte avant d'enregistrer.")
      return
    }

    // The record's dentition flag is derived, not read from the toggle: a session may legitimately chart both
    // dentitions, so it is only a display hint — "enfant" when every charted tooth is deciduous.
    const chartedTeeth = parsedActs.flatMap((a) => a.toothNumbers)
    const isAdultTeeth = chartedTeeth.length === 0 ? dentitionView !== "child" : chartedTeeth.some(isAdultTooth)

    setLoading(true)
    try {
      const linkedItem = planItems.find((p) => p.itemId === linkedPlanItemId)
      const recordData = {
        interventionDate,
        amountPaid: parseAmountInput(amountPaid) || 0,
        paymentMethod,
        // The one builder, shared with the till and the échéancier: it clears the three fields when the method is
        // not a cheque, so « the server refuses cheque details on a cash payment » is unreachable rather than
        // merely unlikely.
        ...chequePaymentFields(paymentMethod, cheque),
        isAdultTeeth,
        notes: notes.filter((n) => n.trim()).map((n) => n.trim()),
        importantNotes: importantNotes.filter((n) => n.trim()).map((n) => n.trim()),
        acts: parsedActs,
        treatmentPlanId: linkedItem?.planId ?? null,
        treatmentPlanItemId: linkedItem?.itemId ?? null,
        // Only carried on create — links the new record to the appointment it documents (closes the prompt).
        appointmentId: appointmentId ?? null,
      }

      const saved = record
        ? await dentalRecordsApi.update(patientId, record.id, { ...recordData, version: record.version })
        : await dentalRecordsApi.create(patientId, recordData)

      // « Montant payé » now becomes real money — the note d'honoraires is issued and the payment recorded on
      // save. Say which, and say it plainly: the whole reason that field was a trap is that it looked like a
      // receipt while nothing downstream read it, so the one thing this must never do is stay quiet.
      const base = record ? "Fiche dentaire mise à jour" : "Fiche dentaire enregistrée"
      // Every outcome is surfaced, and each one gets the tone it deserves (AC-3). The old version had three
      // branches and let AlreadyBilled fall into a plain green « enregistrée » — indistinguishable from a fiche
      // that had just put money in the till, on the one screen whose whole history is money silently not moving.
      switch (saved.billing?.outcome) {
        case "Billed":
          toast.success(base, {
            description: `Note n° ${saved.billing.invoiceNumber} émise — ${formatDT(
              saved.billing.amountCollected ?? 0,
            )} encaissé`,
          })
          break
        case "ToppedUp":
          // The edit this part exists for. It names the *increment*, not the note's new total: the dentist typed
          // a cumulative figure, and what they need confirmed is what this save moved.
          toast.success(base, {
            description: `${formatDT(saved.billing.amountCollected ?? 0)} encaissé en plus sur la note n° ${
              saved.billing.invoiceNumber
            }`,
          })
          break
        case "AlreadyBilled":
          // Informational, not plain green: nothing moved, and « enregistrée » on its own would let a dentist
          // believe an amount they just changed had reached the till.
          toast.info(base, {
            description:
              saved.billing.message ?? "Aucun encaissement supplémentaire — la fiche est déjà facturée.",
          })
          break
        case "Refused":
          // A rule said no and the message names the next step (an avoir). ⚠️ The *record* is saved either way on
          // this path — the refusals that would leave the fiche disagreeing with its note are raised pre-commit
          // by the server and arrive as a thrown error, not here.
          toast.warning(base, {
            description: saved.billing.message ?? "La facturation a été refusée.",
            duration: 10000,
          })
          break
        case "Failed":
          // The record IS saved; only the money failed. Both halves have to be said, or the user either loses
          // work they still have or trusts cash that never landed.
          toast.warning(base, {
            description:
              saved.billing.message ??
              "La facturation automatique a échoué — facturez cette intervention manuellement.",
            duration: 10000,
          })
          break
        // NotCollected — a fiche with no payment. Not news: nothing was supposed to move.
        default:
          toast.success(base)
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

  /**
   * The collapsed header of « Actes de la séance ». It describes **what will be saved**, not just what has been
   * confirmed — so it counts the draft and totals to the same figure as the dialog's own footer.
   *
   * <p>It used to read « aucun acte confirmé » while the footer showed the draft's price. Both were technically
   * true and together they were a contradiction, on the one summary a dentist reads before pressing save. « en
   * cours » is what distinguishes the draft here, rather than pretending it does not exist.</p>
   */
  const actsSummary = (() => {
    const confirmedTotal = roundMillimes(
      acts.reduce((s, a) => s + resolveActCost(a.unitCost, a.perTooth, a.toothNumbers.length), 0),
    )
    if (acts.length === 0 && !hasDraft) return "aucun acte"
    // While an existing act is being edited the draft REPLACES it, so it is not an extra act to announce.
    const pendingCount = hasDraft && !editingKey ? 1 : 0
    const parts: string[] = []
    if (acts.length > 0) parts.push(`${acts.length} acte${acts.length > 1 ? "s" : ""}`)
    if (pendingCount > 0) parts.push(acts.length > 0 ? "+ 1 en cours" : "1 acte en cours")
    return `${parts.join(" ")} · ${formatDT(pendingCount > 0 || editingKey ? grandTotal : confirmedTotal)}`
  })()

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

  /**
   * The séance's other booked acts, resolved against the catalogue and minus anything already in this session
   * (charted or in the draft) — so the shortcuts thin out as the dentist works through the visit instead of
   * re-offering an act they have just recorded.
   */
  const otherBookedActs = useMemo(() => {
    const used = new Set<string>([
      ...acts.map((a) => a.procedureTypeId).filter((id): id is string => !!id),
      ...(draft.procedureTypeId ? [draft.procedureTypeId] : []),
    ])
    return (appointment?.procedures ?? [])
      .slice()
      .sort((a, b) => a.sequenceNumber - b.sequenceNumber)
      .map((p) => (p.procedureTypeId ? procedureTypes.find((pt) => pt.id === p.procedureTypeId) : undefined))
      .filter((pt): pt is ProcedureTypeDto => !!pt && !used.has(pt.id))
  }, [appointment?.procedures, procedureTypes, acts, draft.procedureTypeId])

  return (
    <>
    {/* Only the ROOT and « Annuler » route through the guard — the save path calls the raw prop, so a saved
        fiche closes without asking (AC-23). */}
    <Dialog open={open} onOpenChange={guard.onOpenChange}>
      {/* NOTE: the width override MUST be `md:max-w-*`. DialogContent's base class ends with `md:max-w-lg`,
          and tailwind-merge treats an unprefixed `max-w-*` as a different group — so a plain `max-w-3xl`
          silently loses to it at every viewport ≥768px and the dialog stays 512px wide. (The prefix was `sm:`
          until P4 moved the whole dialog split to `md:`, matching the rest of the feature.) */}
      <DialogContent
        mobile="sheet"
        className="gap-3 md:max-h-[92dvh] md:w-[min(96vw,780px)] md:max-w-[min(96vw,780px)]"
      >
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

        <DialogBody className="flex flex-col gap-3">
        <FormErrorBanner message={conflict.error} />

        {/* Point-of-care medical alerts — surfaced before treatment (safety). */}
        {/* Extracted to `patient/patient-alert-panel.tsx` — it lived here, inline, which is why the document editor
            (where an ordonnance is written) and the résumé modal had nothing of the kind. Two bugs it also fixed on
            the way out: the flag badge printed the raw enum (« HighPriority ») instead of going through
            `patientFlagLabel`, and the allergy line used `red-700` + a hand-maintained `dark:` twin rather than the
            `text-destructive` token. */}
        {patient && <PatientAlertPanel patient={patient} />}

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
          {/* The Adulte/Enfant switch that stood here is gone — the arch follows the patient's stored dentition.
              The warning stays: an act charted on the other dentition is still preserved and must still be visible,
              which is now only reachable by editing an older fiche. */}
          {hiddenDentitionActs > 0 && (
            <div className="space-y-1.5">
              <Label>Autre dentition</Label>
              <p className="text-2xs text-warning-ink">
                {hiddenDentitionActs} acte{hiddenDentitionActs > 1 ? "s" : ""} sur des dents que cette vue n&apos;affiche
                pas (conservé{hiddenDentitionActs > 1 ? "s" : ""}) — choisissez « Mixte » pour les voir.
              </p>
            </div>
          )}
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
        <div ref={actAnchorRef} className="space-y-1.5">
          {/* Above the picker, because that is where the consequence lands: an empty list invites a free-text act. */}
          {catalogFailed && (
            <LoadFailureNotice
              variant="inline"
              message="Le catalogue des actes n'a pas pu être chargé."
              detail="Un acte saisi à la main n'aura ni tarif ni état résultant."
              onRetry={() => void loadCatalog()}
            />
          )}
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
          {saveError?.anchor === "act" && (
            <p role="alert" className="text-xs font-medium text-destructive">
              {saveError.message}
            </p>
          )}
        </div>

        {/*
          The rest of the séance. An appointment can carry several acts, and only the first one is *proposed* —
          proposing all of them would have to commit acts the dentist has not confirmed, with no teeth and no
          price, which is exactly what the confirm-first flow exists to avoid.

          So the others are named rather than pre-filled, each with a one-tap « Ajouter ». Without this the fiche
          would silently document one act of a visit booked for three, and nothing on the screen would say so.
        */}
        {!record && otherBookedActs.length > 0 && (
          <div className="rounded-md border border-dashed bg-muted/30 px-3 py-2">
            <p className="text-xs text-muted-foreground">
              Aussi prévu à ce rendez-vous — à confirmer un par un :
            </p>
            <div className="mt-1.5 flex flex-wrap gap-1.5">
              {otherBookedActs.map((booked) => (
                <Button
                  key={booked.id}
                  type="button"
                  variant="outline"
                  size="sm"
                  className="h-7 gap-1 text-xs"
                  disabled={loading}
                  onClick={() => dispatch({ type: "pickProcedure", procedure: booked })}
                >
                  <Plus className="h-3 w-3" />
                  {booked.name}
                </Button>
              ))}
            </div>
          </div>
        )}

        {/* THE TEETH — full width, no longer competing with a form column for space. */}
        <div className="space-y-2">
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <Label>{hasDraft ? "Sur quelle(s) dent(s) ?" : "Dents concernées"}</Label>
            <div className="flex flex-wrap items-center gap-1.5">
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="h-6 px-2 text-2xs"
                disabled={loading}
                onClick={() => dispatch({ type: "selectMany", teeth: teethInQuadrants(upperQuadrants), additive: true })}
              >
                Haut
              </Button>
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="h-6 px-2 text-2xs"
                disabled={loading}
                onClick={() => dispatch({ type: "selectMany", teeth: teethInQuadrants(lowerQuadrants), additive: true })}
              >
                Bas
              </Button>
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="h-6 px-2 text-2xs"
                disabled={loading}
                onClick={() => dispatch({ type: "selectMany", teeth: viewTeeth, additive: true })}
              >
                Toute la bouche
              </Button>
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="h-6 px-2 text-2xs"
                disabled={loading || selection.length === 0}
                onClick={() => dispatch({ type: "clearSelection" })}
              >
                Vider
              </Button>
            </div>
          </div>

          {/* The dentition switch sits directly above the arch it changes — a control whose effect is one row
              down needs no explanation. Full width below `sm:` so the three segments keep their 44px on a phone. */}
          <DentitionViewSwitch
            value={dentitionView}
            onChange={setChosenView}
            disabled={loading}
            className="sm:max-w-xs"
          />

          <RecordToothChart
            view={dentitionView}
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
                  // Only the teeth the current view actually draws — selecting one that is off-screen would put a
                  // tooth in the act with nothing on the chart to show it.
                  teeth: openDiagnosisTeeth.filter((t) => viewTeeth.includes(t)),
                  additive: true,
                })
              }
              // `touch-target min-h-11`: a bare 28px banner that BULK-SELECTS every tooth needing treatment —
              // one of the highest-consequence taps in the fiche, and it was the smallest. `min-h-11` paints the
              // floor rather than only overlaying it, because the chart sits directly above and an overlay would
              // reach into the last row of teeth.
              className="touch-target flex min-h-11 w-full items-center gap-1.5 rounded-md border border-orange-300 bg-orange-50 px-2 py-1.5 text-left text-2xs text-orange-800 hover:bg-orange-100 disabled:cursor-not-allowed dark:border-orange-900 dark:bg-orange-950/40 dark:text-orange-200"
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

          <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-2xs">
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
        <div ref={detailsAnchorRef} className="space-y-1.5">
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
          {saveError?.anchor === "details" && (
            <p role="alert" className="text-xs font-medium text-destructive">
              {saveError.message}
            </p>
          )}
        </div>

        <RecordSection
          title="Actes de la séance"
          summary={actsSummary}
          open={openSections.acts}
          onToggle={() => toggleSection("acts")}
        >
          <SessionActsList
            acts={acts}
            // Only when it is a NEW act. While an existing one is being edited the draft *is* that act, already
            // listed above — showing it twice would read as two acts and double the apparent work.
            pendingAct={
              hasDraft && !editingKey
                ? { ...draft, key: "pending", toothNumbers: [...selection] }
                : null
            }
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
                <span className="ml-2 text-2xs text-warning-ink">⚠ Mises en évidence</span>
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

        </DialogBody>

        {/*
          ⚠️ The totals live in the FOOTER, not at the end of `DialogBody`.

          Their old comment claimed « the number that will be saved, always on screen », and that was true only at
          ≥768px. `DialogBody` is the scrolling middle of the sheet, so on a phone the block sat roughly 330px
          below the tooth chart — off screen for the entire time the dentist is tapping teeth, which is exactly
          when the running total is the thing being watched. The footer is `shrink-0` and outside the scroll
          container, which is what « always on screen » actually requires.

          `flex-col`, overriding the primitive's `flex-col-reverse sm:flex-row`: this footer has two stacked
          bands (figures, then actions) rather than one row of buttons.
        */}
        <DialogFooter className="flex-col gap-3 sm:flex-col sm:justify-start">
          {/*
            The cheque's identity, above the figures rather than beside « Payé »: it is three fields, and three
            more controls on the figures row is two columns at 320 px. Rendered only for a cheque — `ChequeFields`
            deliberately does not self-hide, so the decision is visible here.

            Placed in the FOOTER with « Payé » for the same reason the totals are: this is chairside, the dentist
            is on a phone or a tablet at the unit, and the payment is the last thing entered before saving.
          */}
          {paymentMethod === CHEQUE_METHOD && !isInvoiced && (
            <div className="w-full">
              <ChequeFields
                idPrefix="fiche"
                value={cheque}
                onChange={setCheque}
                disabled={loading}
              />
            </div>
          )}

          <div className="flex w-full flex-wrap items-center gap-x-3 gap-y-2 rounded-lg border bg-muted/30 p-3">
            <div className="flex min-w-[9rem] flex-1 items-center gap-2">
              <Label htmlFor="paid" className="shrink-0 text-xs text-muted-foreground">
                Payé
              </Label>
              {/* `text` + `inputMode="decimal"`, never `type="number"` (J8): a number input refuses the comma
                  this product prints with, and a rejected keystroke returns an EMPTY value — so « Payé » looked
                  filled and saved nothing. The numeric keypad still appears on a phone. */}
              <Input
                id="paid"
                type="text"
                inputMode="decimal"
                className="h-8 w-full text-right tabular-nums"
                value={amountPaid}
                onChange={(e) => {
                  setPaidDirty(true)
                  setAmountPaid(e.target.value)
                }}
                placeholder="0,000"
                disabled={loading || isInvoiced}
              />
            </div>
            {/* Beside the amount, because « combien » and « comment » are one answer. `min-w` + `flex-1` so it
                wraps to its own full-width line below ~360 px instead of squeezing the amount field. */}
            <div className="flex min-w-[9rem] flex-1 items-center gap-2">
              <Label htmlFor="paid-method" className="shrink-0 text-xs text-muted-foreground">
                Mode
              </Label>
              <Select
                value={paymentMethod}
                onValueChange={setPaymentMethod}
                disabled={loading || isInvoiced}
              >
                <SelectTrigger id="paid-method" className="h-8 w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {FICHE_PAYMENT_METHODS.map((m) => (
                    <SelectItem key={m.value} value={m.value}>
                      {m.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="flex shrink-0 items-center gap-1.5 text-sm">
              <span className="text-muted-foreground">Total</span>
              <span className="text-base font-semibold tabular-nums">{formatDT(grandTotal)}</span>
            </div>
            {/* Wraps to its own line below `sm:` — three figures do not fit 342px, and « Reste à payer » is the
                one of the three that is a sentence rather than a number. */}
            <div className="w-full text-xs sm:w-auto">
              {isInvoiced ? (
                <p className="text-muted-foreground">Facturé — le paiement est géré par la facture.</p>
              ) : (
                <p className="text-muted-foreground">
                  Reste à payer :{" "}
                  {/* `--warning-ink`, not `text-amber-600`: that literal had no `dark:` pair and measured
                      ~3.2:1 on the card — on an outstanding-balance figure. The token was minted for this. */}
                  <span className={reste > 0 ? "font-semibold text-warning-ink" : "font-medium text-foreground"}>
                    {formatDT(reste)}
                  </span>
                </p>
              )}
            </div>
          </div>

          {/*
            `flex-col-reverse` on both levels below `sm:`, mirroring the primitive's own idiom: the DOM keeps the
            desktop reading order (secondary → cancel → confirm) while a phone stacks them primary-first, each
            full width. Three full-width rows rather than a cramped side-by-side, because « Confirmer la séance —
            180,000 DT » is ~230px of unwrappable French and `buttonVariants` is `whitespace-nowrap`.
          */}
          <div className="flex w-full flex-col-reverse gap-2 sm:flex-row sm:items-center sm:justify-between">
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
              className="w-full sm:w-auto"
            >
              <Plus className="mr-1 h-4 w-4" /> Ajouter un autre acte
            </Button>
            <div className="flex w-full flex-col-reverse gap-2 sm:w-auto sm:flex-row">
              <Button
                variant="outline"
                onClick={() => guard.onOpenChange(false)}
                disabled={loading}
                className="w-full sm:w-auto"
              >
                Annuler
              </Button>
              {/*
                The amount rides ON the action. « Confirmer » and the figure it commits were two separate places
                to look, and on a phone only one of them was visible — so the button now states what pressing it
                will book. `formatDT`, never a hand-rolled `toFixed`: the millime and the decimal comma are the
                product's, not this dialog's.
              */}
              <Button onClick={handleSave} disabled={loading} className="w-full sm:w-auto sm:min-w-[150px]">
                {loading
                  ? "Enregistrement…"
                  : `${
                      record ? "Enregistrer" : appointmentId ? "Confirmer la séance" : "Créer la fiche"
                    }${grandTotal > 0 ? ` — ${formatDT(grandTotal)}` : ""}`}
              </Button>
            </div>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
    <DiscardChangesDialog guard={guard} />
    </>
  )
}
