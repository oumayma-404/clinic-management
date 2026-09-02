"use client"

import { useState, useEffect, useCallback, useMemo, useRef } from "react"
import { Button } from "@/components/ui/button"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Dialog, DialogBody, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from "@/components/ui/dialog"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import { Trash2, Plus, Stethoscope, ChevronDown, ChevronRight } from "lucide-react"
import { PatientAlertPanel } from "@/components/patient/patient-alert-panel"
import { dentalRecordsApi } from "@/lib/api/dental-records"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import { odontogramApi } from "@/lib/api/odontogram"
import { showErrorToast } from "@/lib/errors"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { useConflict } from "@/lib/hooks/use-conflict"
import { useFreshVersion } from "@/lib/hooks/use-fresh-version"
import { toast } from "sonner"
import type {
  ProcedureTypeDto,
  DentalRecordDto,
  DentalActInput,
  PatientDto,
  ToothStateDto,
  AppointmentDto,
  AppointmentProcedureDto,
} from "@/lib/api/types"
import { formatAmount, formatDT, parseAmountInput, quoteFr, roundMillimes, toLocalIso, todayLocalIso } from "@/lib/format"
import { conditionStyle, needsTreatment, serializeSurfaces } from "@/components/odontogram-conditions"
import { ARCH_QUADRANTS_BY_VIEW, FDI_BY_VIEW, isAdultTooth } from "@/components/tooth-multiselect"
import { dentitionViewFor, dentitionViewForTeeth, type DentitionView } from "@/lib/dentition"
import { DentitionViewSwitch } from "@/components/dentition-view-switch"
import { RecordToothChart, type ToothPaint } from "@/components/record-tooth-chart"
import { ApiError } from "@/lib/api/client"
import { CorrectInvoiceDialog, DEFAULT_CORRECTION_REASON, type CorrectionPreview } from "@/components/factures/correct-invoice-dialog"
import { ActCard } from "@/components/record/act-card"
import { RecordSection } from "@/components/record/record-section"
import { actTotal, hasInvalidPrice, isActNamed, isActTouched, useSessionActs } from "@/components/record/use-session-acts"
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

/**
 * Fallback colours for an act the catalogue cannot colour — a free-text act, or one whose catalogue hue another
 * act in the same séance already wears. Chosen to stay apart from each other at a glance and to read on both
 * themes; the chart tints them, so they are never text on a background.
 */
const ACT_PALETTE = ["#7c5cd6", "#0f9b8e", "#c9376d", "#b8792f", "#3b82f6", "#5d7186"]

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
/**
 * The refusals a correction can get past. Mirrors `DentalRecordBillingRefusals.IsCorrectable` on the server —
 * the two are codes, not sentences, precisely so rewording a French refusal cannot change what this offers.
 */
const CORRECTABLE_CODES = new Set(["dental_record_acts_changed_after_billing", "dental_record_payment_lowered"])

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
  /** Set when a save was refused for a reason a correction can get past — drives the confirm dialog. */
  const [correction, setCorrection] = useState<CorrectionPreview | null>(null)
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
  // Only « Notes de séance » folds now. The acts are the point of this dialog and are always open — the old
  // « Actes de la séance » fold, shut by default, is where an act appeared to vanish when a second one was added.
  const [notesOpen, setNotesOpen] = useState(false)
  // The chart's condition legend. Folded by default — the colours stay visible collapsed, so what folds is the
  // labelling, and a dentist who knows the palette does not pay a permanent nine-entry row for it.
  const [legendOpen, setLegendOpen] = useState(false)
  const [loading, setLoading] = useState(false)
  const guard = useDirtyGuard(open, onOpenChange)
  // A save conflict stays in the form; everything else keeps the existing toast.
  const conflict = useConflict()
  /*
   * The version this fiche saves with, kept equal to the row's current one — a fiche is re-saved more than
   * anything else in the app (« re-saving tops the note up »), so a version that drifts is felt here first.
   * ⚠️ The VERSION only: the read lands after hydration, so its field values would clobber what was typed.
   * There is no GET-by-id for a fiche, so the patient's own list is the read.
   */
  const { source: freshRecord, resync } = useFreshVersion(
    open,
    patientId && record ? record.id : null,
    record,
    async () => (await dentalRecordsApi.list(patientId!)).find((r) => r.id === record!.id) ?? null,
  )

  /**
   * The refusal that blocked the last « Confirmer », **anchored to the act that caused it**.
   *
   * <p>All three of this dialog's validation refusals used to be toasts and nothing else. On a phone sonner lands
   * bottom-centre — directly over this dialog's own footer, i.e. over the button just pressed — and is gone in
   * four seconds, so the dentist presses « Confirmer la séance » again and gets the same flash. Nothing on the
   * form itself ever said which field was wrong; « Montant invalide » does not say *which act's* montant, and the
   * offending one may be three sections down and folded shut.</p>
   *
   * <p>The toast is kept as the *secondary* announcement (it is what a user glancing away notices), but the
   * authority is now the inline message: it persists, and it is rendered **inside the offending act's own card**
   * rather than at the top of a region — with several acts on screen, « Montant invalide » anywhere else does not
   * say which one. `aria-invalid` lives on the individual inputs in `ActCard`, where a screen reader expects it.</p>
   */
  const [saveError, setSaveError] = useState<{ actKey: string | null; message: string } | null>(null)
  const actsAnchorRef = useRef<HTMLDivElement>(null)

  const { acts, namedActs, grandTotal, focusedAct, focusKey, dispatch } = useSessionActs(record)

  /**
   * What is being typed into « Total », or `null` when the field simply shows the derived figure.
   *
   * <p>It has to be held separately for the length of the edit: the displayed value is `formatAmount(grandTotal)`,
   * so writing straight through would reformat every keystroke — « 1 » becoming « 1,000 » with the caret behind
   * it, which makes the field impossible to type a second digit into. Clearing the draft on commit is also what
   * re-syncs the field afterwards: once it is `null` the input is again a pure read of the acts, so correcting an
   * act's tarif by hand moves the total with no further wiring.</p>
   */
  const [totalDraft, setTotalDraft] = useState<string | null>(null)

  /**
   * Commit the typed total onto the acts. An unusable or negative entry is dropped and the field snaps back to
   * the real total — the number visibly returning is the refusal, and there is nothing to report beyond it.
   */
  const commitTotal = useCallback(() => {
    setTotalDraft((draft) => {
      if (draft === null) return null
      const parsed = parseAmountInput(draft)
      if (Number.isFinite(parsed) && parsed >= 0) dispatch({ type: "setTotal", total: parsed })
      return null
    })
  }, [dispatch])

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
      setNotesOpen(record.notes.length > 0 || record.importantNotes.length > 0)
    } else {
      setInterventionDate(todayLocalIso())
      setAmountPaid("")
      setPaidDirty(false)
      setPaymentMethod(CASH_METHOD)
      setCheque(EMPTY_CHEQUE_FIELDS)
      setNotes([])
      setImportantNotes([])
      setNotesOpen(false)
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
    // ⚠️ The price comes from the appointment's own act ROW, never from `booked.defaultCost`. The lead act is a
    // derived snapshot of the first row, so the catalogue is the wrong place to ask: a visit booked at a
    // negotiated 120 DT would open the fiche at the 150 DT tarif, and the patient would be billed a price
    // nobody quoted them.
    const bookedRow = appointment.procedures
      ?.slice()
      .sort((a, b) => a.sequenceNumber - b.sequenceNumber)
      .find((p) => p.procedureTypeId === appointment.procedureTypeId)
    if (booked) {
      dispatch({ type: "applyAppointment", procedure: booked, agreedCost: bookedRow?.agreedCost ?? null })
    }
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

  /**
   * One colour per act of the séance — the card's rail, its tooth chips, and its teeth on the chart.
   *
   * <p>⚠️ **Distinctness is enforced here rather than hoped for.** The chart paints every act at once now, and
   * « quelles dents pour quel acte ? » is answered by matching a card to the teeth wearing its colour — which
   * stops working the moment two acts share one. That happens easily: the same procedure recorded twice, two
   * catalogue entries a clinic gave the same hue, or two free-text acts with no colour at all. An act whose
   * catalogue colour is already taken falls through to the palette.</p>
   *
   * <p>It replaced painting each act in its <em>resulting condition</em>'s colour, which is the odontogram's own
   * language and not an act's: two obturations — or, more commonly, two acts with no état résultant at all —
   * were the same colour on the chart, so the séance could not be read back off it.</p>
   */
  const actColors = useMemo(() => {
    const map = new Map<string, string>()
    const used = new Set<string>()
    let next = 0
    for (const act of acts) {
      const catalogue = act.procedureTypeId
        ? procedureTypes.find((p) => p.id === act.procedureTypeId)?.colorHex
        : null
      let chosen = catalogue && !used.has(catalogue.toLowerCase()) ? catalogue : null
      while (!chosen) {
        const candidate = ACT_PALETTE[next % ACT_PALETTE.length]
        next += 1
        // Bounded: past one full lap every entry is spoken for and a repeat is the honest outcome.
        if (!used.has(candidate) || next > ACT_PALETTE.length) chosen = candidate
      }
      used.add(chosen.toLowerCase())
      map.set(act.key, chosen)
    }
    return map
  }, [acts, procedureTypes])

  const focusedColor = focusedAct ? (actColors.get(focusedAct.key) ?? null) : null

  /*
   * Per-tooth paint: prior state as the outline, every act of the séance as the fill, the armed act on top.
   *
   * ⚠️ EVERY act paints now, not only the one being edited. The chart used to show the single draft, so the acts
   * already recorded in the séance were invisible on the one surface that exists to show where work was done —
   * and the count badge was the only hint a tooth carried more than one.
   */
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

    for (const act of namedActs) {
      const color = actColors.get(act.key) ?? null
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

    // The armed act's colour WINS on its own teeth: it is what is being charted now, and the ring plus the count
    // badge are what still say the tooth carries another act. Arming a different card recomputes the map.
    for (const tooth of focusedAct?.toothNumbers ?? []) {
      const prev = map.get(tooth)
      map.set(tooth, {
        selected: true,
        color: focusedColor ?? prev?.color ?? null,
        count: prev?.count ?? 0,
        existingColor: prev?.existingColor ?? null,
        existingIsDiagnosis: prev?.existingIsDiagnosis,
      })
    }

    return map
  }, [priorByTooth, namedActs, focusedAct, focusedColor, actColors])

  const viewTeeth = FDI_BY_VIEW[dentitionView]
  const { upper: upperQuadrants, lower: lowerQuadrants } = ARCH_QUADRANTS_BY_VIEW[dentitionView]
  const teethInQuadrants = (quadrants: number[]) => viewTeeth.filter((t) => quadrants.includes(Math.floor(t / 10)))

  /** What a card at rest measures its teeth against, so a full arch reads as a phrase and not as 32 numbers. */
  const arch = useMemo(
    () => ({
      all: viewTeeth,
      upper: viewTeeth.filter((t) => upperQuadrants.includes(Math.floor(t / 10))),
      lower: viewTeeth.filter((t) => lowerQuadrants.includes(Math.floor(t / 10))),
    }),
    [viewTeeth, upperQuadrants, lowerQuadrants],
  )

  // Acts charted on teeth the current view does not draw — surfaced so nothing hides behind the switch. Tested
  // against the view's own tooth set rather than `isAdultTooth`, which is what makes the count read **zero** on
  // Mixte: that view draws both dentitions, so there is nothing left off-screen to warn about.
  const hiddenDentitionActs = useMemo(
    () => namedActs.filter((a) => a.toothNumbers.some((t) => !viewTeeth.includes(t))).length,
    [namedActs, viewTeeth],
  )

  // Linking a plan step carries its designation / cost / teeth into the first act, so the dentist does not
  // retype what the plan already knows. Only an untouched séance is prefilled.
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

  const paidAmount = parseAmountInput(amountPaid) || 0
  const reste = Math.max(0, roundMillimes(grandTotal - paidAmount))

  // « Reste » clamps at 0, so an amount above the total was invisible here while the server refused it post-commit
  // and the fiche saved anyway. Stated inline and the save disabled, matching the avoir dialog's own pattern.
  //
  // ⚠️ It applies to a BILLED fiche too now. The « Payé » field used to be disabled once a note existed, so
  // `BillDentalRecordCommand`'s whole top-up branch — `ToppedUp` — was unreachable from the product: a patient
  // paying the rest of a balance in a second visit had to be taken to « Factures » and recorded there, on a screen
  // reception does not necessarily have. The server has always accepted it, and its guard is authoritative.
  const overpaid = roundMillimes(paidAmount) > roundMillimes(grandTotal)

  /**
   * The amount already on the note. Lowering it is refused server-side (`dental_record_payment_lowered`) — money
   * recorded on a numbered document is corrected by an avoir, never by retyping a field — so the field says so
   * before the round trip rather than after it.
   */
  const alreadyCollected = isInvoiced ? roundMillimes(record?.amountPaid ?? 0) : 0
  const lowersBilledAmount = isInvoiced && roundMillimes(paidAmount) < alreadyCollected

  /**
   * The edit contradicts the note d'honoraires already carrying this séance — the state in which « Enregistrer »
   * becomes « Corriger la note » rather than going grey.
   *
   * <p>Both halves used to simply disable the button, which is how the correction became unreachable: lowering a
   * price greyed the save out with a sentence beside it, so the refusal that opens the correction could never
   * fire. Deliberately gated on `isInvoiced`: with no note there is nothing to correct against, and `overpaid` is
   * then a plain typo that should still block.</p>
   */
  const contradictsNote = isInvoiced && (overpaid || lowersBilledAmount)

  /**
   * Refuse the save: mark the region, scroll it into view, and *also* toast.
   *
   * <p>Order matters — the state is set before the scroll so the message exists by the time the region is on
   * screen, and `block: "center"` rather than `"nearest"` because the offending field is often just above the
   * footer the user is looking at, where "nearest" would move nothing at all.</p>
   */
  const refuseSave = (actKey: string | null, message: string, description?: string) => {
    setSaveError({ actKey, message })
    // The offending card carries the message; the pile is what gets scrolled to, since a card may be one line.
    actsAnchorRef.current?.scrollIntoView({ block: "center", behavior: "smooth" })
    toast.error(message, description ? { description } : undefined)
  }

  // Any edit to the acts clears the refusal: an inline error that outlives the thing it described is worse than
  // none, because the next press is refused for a reason the message no longer names.
  useEffect(() => {
    setSaveError(null)
  }, [acts])

  const handleSave = async (correctionReason?: string) => {
    if (!patientId) {
      toast.error("Identifiant du patient requis")
      return
    }

    if (namedActs.length === 0) {
      refuseSave(acts[0]?.key ?? null, "Ajoutez au moins un acte", "Choisissez l'acte réalisé, puis les dents.")
      return
    }

    /*
     * An unnamed card the dentist has nonetheless put teeth or a price into. A blank trailing card is dropped in
     * silence — that is what it is for — but one carrying work is refused, because dropping it would throw away
     * something visible on screen and the fiche would save fewer acts than were charted.
     */
    const unnamed = acts.find((a) => !isActNamed(a) && isActTouched(a))
    if (unnamed) {
      refuseSave(
        unnamed.key,
        "Un acte n'a pas de désignation",
        "Choisissez l'acte réalisé, ou supprimez la carte.",
      )
      return
    }

    const badPrice = namedActs.find((a) => hasInvalidPrice(a.unitCost))
    if (badPrice) {
      refuseSave(
        badPrice.key,
        `Montant invalide pour ${quoteFr(badPrice.procedureName)}`,
        "Corrigez le tarif de l'acte, puis confirmez.",
      )
      return
    }

    const parsedActs: DentalActInput[] = namedActs
      .map((a) => {
        // ⚠️ `parseAmountInput`, never `Number.parseFloat` (J8). The field prints « 90,500 » and `parseFloat`
        // stops at the comma, so the stored UNIT price was 90 while `cost` — which goes through
        // `parseAmountInput` — was the correct 181,000 for two teeth. Reopening the fiche then showed 90,000
        // per tooth, and re-saving it wrote that half-dinar loss into the note.
        const unit = parseAmountInput(a.unitCost)
        return {
          procedureTypeId: a.procedureTypeId,
          procedureName: a.procedureName.trim(),
          cost: actTotal(a),
          unitCost: Number.isFinite(unit) ? roundMillimes(unit) : null,
          isPerTooth: a.perTooth && a.toothNumbers.length > 0,
          toothNumbers: a.toothNumbers,
          resultingCondition: a.resultingCondition, // null when "Aucun"
          surfaces: serializeSurfaces(a.surfaces) || null,
          note: a.note.trim() || null,
        }
      })

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
        ? await dentalRecordsApi.update(patientId, record.id, {
            ...recordData,
            // Only ever set by « Corriger la note » below — an ordinary save never retires a numbered document.
            ...(correctionReason ? { correctionReason } : {}),
            version: freshRecord?.version ?? record.version,
          })
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
      // ── A refusal the fiche can be corrected out of ─────────────────────────────────────────────────────
      //
      // « Les actes … ne peuvent plus être modifiés. Établissez un avoir » used to be the end of the road: the
      // action it named lives in another page's row menu, and an avoir is the wrong document anyway — it records
      // money handed back, and a mis-keyed amount handed nothing back. So the refusal now opens the way out it
      // was describing. Branched on the CODE, never the sentence: rewording a refusal must not change behaviour.
      if (!correctionReason && err instanceof ApiError && CORRECTABLE_CODES.has(err.code ?? "")) {
        setCorrection({ previousTotal: record?.cost ?? 0, nextTotal: grandTotal })
        setLoading(false)
        return
      }

      // A conflict is not a transient blip — a colleague saved this fiche while it was open — so it stays
      // in the form rather than flashing past in a toast.
      if (!conflict.capture(err, "L'enregistrement de la fiche a échoué.")) {
        // The fiche may have saved and only the billing failed, which leaves the row a version ahead of this
        // form. Not on the conflict branch: resyncing a real 409 would overwrite the colleague who caused it.
        await resync()
        showErrorToast(err, "L'enregistrement de la fiche a échoué.")
      }
    } finally {
      setLoading(false)
    }
  }

  /**
   * The acts header. Every card on screen is a real act, so this is a plain count and a plain sum — it used to
   * have to say « 1 acte + 1 en cours » because the act being typed was not yet one.
   */
  const actsSummary =
    namedActs.length === 0
      ? "aucun acte"
      : `${namedActs.length} acte${namedActs.length > 1 ? "s" : ""} · ${formatDT(grandTotal)}`

  /**
   * Acts naming the same procedure on the same teeth. Legitimate in principle (a dentist may genuinely do the
   * same thing twice), so it is flagged on the cards rather than blocked.
   */
  const duplicateKeys = useMemo(() => {
    const seen = new Map<string, string>()
    const dupes = new Set<string>()
    for (const a of namedActs) {
      const signature = `${a.procedureName.trim().toLowerCase()}|${a.toothNumbers.join(",")}`
      const first = seen.get(signature)
      if (first) {
        dupes.add(first)
        dupes.add(a.key)
      } else {
        seen.set(signature, a.key)
      }
    }
    return dupes
  }, [namedActs])

  const noteCount = notes.filter((n) => n.trim()).length
  const importantCount = importantNotes.filter((n) => n.trim()).length
  const notesSummary =
    noteCount + importantCount === 0
      ? "aucune note"
      : [noteCount > 0 ? `${noteCount} note${noteCount > 1 ? "s" : ""}` : null,
         importantCount > 0 ? `${importantCount} importante${importantCount > 1 ? "s" : ""}` : null]
          .filter(Boolean)
          .join(" · ")

  // The one card that came from the booking, and only while it is still the whole séance.
  const proposedFromAppointment =
    !record && acts.length === 1 && appointment?.procedureTypeId != null &&
    acts[0].procedureTypeId === appointment.procedureTypeId
      ? acts[0].key
      : null

  /**
   * The séance's other booked acts, resolved against the catalogue and minus anything already in this session
   * (charted or in the draft) — so the shortcuts thin out as the dentist works through the visit instead of
   * re-offering an act they have just recorded.
   */
  const otherBookedActs = useMemo(() => {
    const used = new Set<string>(acts.map((a) => a.procedureTypeId).filter((id): id is string => !!id))
    return (appointment?.procedures ?? [])
      .slice()
      .sort((a, b) => a.sequenceNumber - b.sequenceNumber)
      // ⚠️ The booked ROW travels with the catalogue entry, not just its id. This used to resolve straight to a
      // `ProcedureTypeDto` and throw the row away, which is exactly where a negotiated price would have died:
      // the shortcut would have priced the second act of a séance from the catalogue while the first — reached by
      // a different code path — carried the agreed figure. Two acts of one visit, two pricing rules.
      .map((row) => ({
        row,
        procedure: row.procedureTypeId
          ? procedureTypes.find((pt) => pt.id === row.procedureTypeId)
          : undefined,
      }))
      .filter(
        (entry): entry is { row: AppointmentProcedureDto; procedure: ProcedureTypeDto } =>
          !!entry.procedure && !used.has(entry.procedure.id),
      )
  }, [appointment?.procedures, procedureTypes, acts])

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

        {/*
          Session header: patient, date, dentition view, optional plan-step link.

          ⚠️ **Two columns is the ceiling, and `lg:grid-cols-4` is why this dialog had a horizontal scrollbar.**
          A breakpoint variant keys on the VIEWPORT, not on this container — and the dialog is capped at
          `min(96vw,780px)`. So on any screen ≥1024 px the row became four tracks inside ~750 px of usable width,
          ~175 px each. A grid item's `min-width` is `auto`, so the « Acte planifié » `Select` — whose options are
          whole act designations — could not shrink below its content and pushed the row past the body. And
          `DialogBody` sets only `overflow-y-auto`, which per the CSS overflow spec makes the other axis compute to
          `auto`, so the overflow surfaced as a scrollbar under the whole fiche. `min-w-0` on the cells is the
          second half: it lets the track shrink so a long label truncates instead of pushing.
        */}
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="min-w-0 space-y-1.5">
            <Label htmlFor="patient-name">Patient</Label>
            <Input id="patient-name" value={patientName} readOnly className="h-9 font-medium" />
          </div>
          <div className="min-w-0 space-y-1.5">
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
            <div className="min-w-0 space-y-1.5">
              <Label>Autre dentition</Label>
              <p className="text-2xs text-warning-ink">
                {hiddenDentitionActs} acte{hiddenDentitionActs > 1 ? "s" : ""} sur des dents que cette vue n&apos;affiche
                pas (conservé{hiddenDentitionActs > 1 ? "s" : ""}) — choisissez « Mixte » pour les voir.
              </p>
            </div>
          )}
          {planItems.length > 0 && (
            <div className="min-w-0 space-y-1.5">
              <Label htmlFor="plan-item">
                Acte planifié <span className="font-normal text-muted-foreground">(facultatif)</span>
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

        {/*
          THE ACTS — one card each, all of them real, all of them editable, and « Ajouter un autre acte » always
          last. There is no composer and no read-back list: those were two views of one act, and the split is what
          made adding a second act look like losing the first.
        */}
        <div ref={actsAnchorRef} className="space-y-2">
          <div className="flex flex-wrap items-baseline gap-x-2">
            <Label className="text-xs font-semibold">Actes de la séance</Label>
            <span className="font-mono text-2xs text-muted-foreground">{actsSummary}</span>
          </div>

          {/* Above the cards, because that is where the consequence lands: an empty catalogue invites free text. */}
          {catalogFailed && (
            <LoadFailureNotice
              variant="inline"
              message="Le catalogue des actes n'a pas pu être chargé."
              detail="Un acte saisi à la main n'aura ni tarif ni état résultant."
              onRetry={() => void loadCatalog()}
            />
          )}

          {acts.map((act, i) => (
            <ActCard
              key={act.key}
              act={act}
              index={i + 1}
              focused={act.key === focusKey}
              procedureTypes={procedureTypes}
              color={actColors.get(act.key) ?? ACT_PALETTE[0]}
              arch={arch}
              proposedFromAppointment={act.key === proposedFromAppointment}
              duplicate={duplicateKeys.has(act.key)}
              error={saveError?.actKey === act.key ? saveError.message : null}
              dispatch={dispatch}
              disabled={loading}
            />
          ))}

          {/* A refusal that named no card (« ajoutez au moins un acte » on an empty pile). */}
          {saveError && !acts.some((a) => a.key === saveError.actKey) && (
            <p role="alert" className="text-xs font-medium text-destructive">
              {saveError.message}
            </p>
          )}

          <Button
            type="button"
            variant="outline"
            className="w-full border-dashed coarse:min-h-11"
            onClick={() => dispatch({ type: "addAct" })}
            disabled={loading}
          >
            <Plus className="mr-1 h-4 w-4" /> Ajouter un autre acte
          </Button>
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
              {otherBookedActs.map(({ row, procedure }) => (
                <Button
                  key={procedure.id}
                  type="button"
                  variant="outline"
                  size="sm"
                  className="h-7 gap-1 text-xs"
                  disabled={loading}
                  onClick={() =>
                    dispatch({
                      type: "addFromProcedure",
                      procedure,
                      agreedCost: row.agreedCost ?? null,
                    })
                  }
                >
                  <Plus className="h-3 w-3" />
                  {procedure.name}
                  {/* The agreed price on the chip, because the whole point is that this act is not at its tarif
                      and the dentist should see that before tapping « Ajouter ». */}
                  {row.agreedCost != null && (
                    <span className="tabular-nums text-muted-foreground">
                      · {formatAmount(row.agreedCost)} DT
                    </span>
                  )}
                </Button>
              ))}
            </div>
          </div>
        )}

        {/* THE TEETH — full width, no longer competing with a form column for space. */}
        <div className="space-y-2">
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            {/* Inert, the chart says so where the eye already is — the sentence under it went unread. */}
            <Label>{focusedAct ? "Sur quelle(s) dent(s) ?" : "Cliquez un acte pour modifier ses dents"}</Label>
            {/* The bulk selectors write to the armed act, so with nothing armed they have no subject and are
                disabled rather than silently doing nothing. */}
            <div className="flex flex-wrap items-center gap-1.5">
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="h-6 px-2 text-2xs"
                disabled={loading || !focusedAct}
                onClick={() => dispatch({ type: "selectMany", teeth: teethInQuadrants(upperQuadrants), additive: true })}
              >
                Haut
              </Button>
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="h-6 px-2 text-2xs"
                disabled={loading || !focusedAct}
                onClick={() => dispatch({ type: "selectMany", teeth: teethInQuadrants(lowerQuadrants), additive: true })}
              >
                Bas
              </Button>
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="h-6 px-2 text-2xs"
                disabled={loading || !focusedAct}
                onClick={() => dispatch({ type: "selectMany", teeth: viewTeeth, additive: true })}
              >
                Toute la bouche
              </Button>
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="h-6 px-2 text-2xs"
                disabled={loading || !focusedAct || focusedAct.toothNumbers.length === 0}
                onClick={() => dispatch({ type: "clearTeeth" })}
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
            // Inert until a card is armed. A tapped tooth has to belong to an act, and guessing which one is how
            // reopening a saved fiche would silently re-chart an act that is already on a numbered note.
            disabled={loading || !focusedAct}
            footer={
              /*
                The condition legend, inside the card it describes and folded by default.
                It used to be nine `text-2xs` entries in a permanent row of the dialog body — a lot of standing
                chrome for orientation nobody re-reads after the first week, and it was the row your screenshot
                showed sliced in half by the body's scroll edge. Collapsed it still carries the colours (the key is
                the hues, not the words), so what folds away is the labelling, not the information.
                `min-h-11` on a coarse pointer PAINTS the floor rather than overlaying it: the last row of teeth
                sits directly above, and a 44 px overlay centred on a ~20 px row would reach into it and steal taps.
              */
              <button
                type="button"
                onClick={() => setLegendOpen((v) => !v)}
                aria-expanded={legendOpen}
                className="flex w-full items-center gap-2 rounded text-left text-2xs text-muted-foreground hover:text-foreground coarse:min-h-11"
              >
                {legendOpen ? (
                  <ChevronDown className="h-3 w-3 shrink-0" aria-hidden="true" />
                ) : (
                  <ChevronRight className="h-3 w-3 shrink-0" aria-hidden="true" />
                )}
                <span className="shrink-0 font-medium">Légende</span>
                {legendOpen ? (
                  <span className="flex flex-wrap items-center gap-x-3 gap-y-1">
                    {namedActs.length === 0 ? (
                      <span className="italic">Aucun acte encore — le remplissage prendra la couleur de sa carte.</span>
                    ) : (
                      namedActs.map((a) => (
                        <span key={a.key} className="flex items-center gap-1">
                          <span
                            className="h-2.5 w-2.5 shrink-0 rounded-full"
                            style={{ backgroundColor: actColors.get(a.key) }}
                          />
                          {a.procedureName}
                        </span>
                      ))
                    )}
                    <span className="flex items-center gap-1">
                      <span className="h-2.5 w-2.5 shrink-0 rounded-full border-2 border-dashed border-muted-foreground/70" />
                      contour = état déjà au dossier
                    </span>
                  </span>
                ) : (
                  <>
                    {/* `aria-hidden`: collapsed these dots are a preview of what expanding names, and a row of
                        unlabelled colours announced one by one is noise. The button's own name carries the action. */}
                    <span className="flex shrink-0 items-center gap-1" aria-hidden="true">
                      {namedActs.map((a) => (
                        <span
                          key={a.key}
                          className="h-2.5 w-2.5 rounded-full"
                          style={{ backgroundColor: actColors.get(a.key) }}
                        />
                      ))}
                    </span>
                    <span className="ms-auto truncate">
                      remplissage = l&apos;acte · contour pointillé = état déjà au dossier
                    </span>
                  </>
                )}
              </button>
            }
          />

          {openDiagnosisTeeth.length > 0 && focusedAct && (
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

          {/*
            Where a tapped tooth goes. The teeth themselves are chips on the act's own card — this line only has to
            answer « lequel ? », which is the one thing the chart cannot show while several acts are painted on it.
            With nothing armed it says so instead of leaving a chart that quietly ignores every tap.
          */}
          <div className="flex min-h-[24px] flex-wrap items-center gap-x-2 gap-y-1 text-xs">
            {focusedAct ? (
              <>
                <span
                  className="h-2.5 w-2.5 shrink-0 rounded-full"
                  style={{ backgroundColor: focusedColor ?? "var(--muted-foreground)" }}
                  aria-hidden="true"
                />
                <span className="text-muted-foreground">
                  Les dents tapées vont à{" "}
                  <span className="font-semibold text-foreground">
                    {focusedAct.procedureName.trim() || `l'acte ${acts.indexOf(focusedAct) + 1}`}
                  </span>
                  {focusedAct.toothNumbers.length > 0
                    ? ` · ${focusedAct.toothNumbers.length} dent${focusedAct.toothNumbers.length > 1 ? "s" : ""}`
                    : " · aucune dent pour l'instant"}
                </span>
                <span className="ms-auto tabular-nums text-muted-foreground">
                  {focusedAct.perTooth && focusedAct.toothNumbers.length > 0 && (
                    <>
                      {formatDT(parseAmountInput(focusedAct.unitCost) || 0)} × {focusedAct.toothNumbers.length} dent
                      {focusedAct.toothNumbers.length > 1 ? "s" : ""} ={" "}
                    </>
                  )}
                  <span className="font-semibold text-foreground">{formatDT(actTotal(focusedAct))}</span>
                </span>
              </>
            ) : (
              /* The instruction is the chart's own label now; saying it here too put it twice on one screen. */
              <span role="status" className="italic text-muted-foreground">
                Aucun acte en saisie.
              </span>
            )}
          </div>

        </div>

        <RecordSection
          title="Notes de séance"
          summary={notesSummary}
          open={notesOpen}
          onToggle={() => setNotesOpen((v) => !v)}
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

          `flex-col`, overriding the primitive's `flex-col-reverse md:flex-row`: this footer has two stacked
          bands (figures, then actions) rather than one row of buttons.

          ⚠️ **Every part of this override must key on `md:`, because `DialogFooter` does.** It used to say
          `sm:flex-col sm:justify-start`, and `sm:` does not override `md:` — Tailwind emits `sm:` first, so at
          equal specificity the base's `md:flex-row md:justify-end` won by source order and the footer rendered as
          a ROW above 768 px. The second half was worse: the primitive's `md:[&>*]:w-auto` compiles to a `> *`
          selector of specificity (0,2,0), which beats the figures band's own `w-full` (0,1,0) — so the band
          collapsed to its content width and its `flex-wrap` children stacked into a narrow column with the
          buttons floating beside it. Hence `md:[&>*]:w-full`, which restores full-width bands at every size.
        */}
        <DialogFooter className="flex-col gap-3 md:flex-col md:justify-start md:[&>*]:w-full">
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
                disabled={loading}
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
                disabled={loading}
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
            {/* The total is EDITABLE, and typing in it re-prices the acts (`setTotal` → `distributeSessionTotal`)
                rather than storing a figure of its own — the acts are what the note d'honoraires is built from.
                It is still `Σ actTotal` on the way out, which is why editing an act afterwards simply moves it
                again: there is no contest to resolve, and the last person to type always wins. */}
            <div className="flex shrink-0 items-center gap-1.5 text-sm">
              <Label htmlFor="session-total" className="text-muted-foreground">
                Total
              </Label>
              {/* Same `text` + `inputMode="decimal"` as « Payé » directly above, and for the same J8 reason: a
                  `type="number"` refuses the comma this product prints with and hands back an EMPTY value. */}
              <Input
                id="session-total"
                type="text"
                inputMode="decimal"
                className="h-8 w-28 text-right text-base font-semibold tabular-nums"
                value={totalDraft ?? formatAmount(grandTotal)}
                onChange={(e) => setTotalDraft(e.target.value)}
                onFocus={(e) => e.currentTarget.select()}
                onBlur={commitTotal}
                onKeyDown={(e) => {
                  if (e.key === "Enter") {
                    e.preventDefault()
                    commitTotal()
                  } else if (e.key === "Escape") {
                    setTotalDraft(null)
                  }
                }}
                aria-describedby="session-total-hint"
                disabled={loading || namedActs.length === 0}
                placeholder="0,000"
              />
              <span id="session-total-hint" className="sr-only">
                Modifier ce total répartit le montant sur les actes de la séance.
              </span>
            </div>
            {/* Wraps to its own line below `sm:` — three figures do not fit 342px, and « Reste à payer » is the
                one of the three that is a sentence rather than a number. */}
            <div className="w-full text-xs sm:w-auto">
              {overpaid ? (
                <p role="status" className="font-medium text-destructive">
                  Le montant payé dépasse le total de la séance ({formatDT(grandTotal)}).
                </p>
              ) : lowersBilledAmount ? (
                <p role="status" className="font-medium text-destructive">
                  {formatDT(alreadyCollected)} sont déjà encaissés sur la note. Un montant encaissé ne se diminue
                  pas ici — établissez un avoir.
                </p>
              ) : isInvoiced ? (
                <p className="text-muted-foreground">
                  Facturé{reste > 0 ? ` — reste ${formatDT(reste)}` : ""}. Augmentez « Payé » pour encaisser un
                  complément sur la même note.
                </p>
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
            `flex-col-reverse` below `sm:`, mirroring the primitive's own idiom: the DOM keeps the desktop
            reading order (cancel → confirm) while a phone stacks them primary-first, each full width, because
            « Confirmer la séance — 180,000 DT » is ~230px of unwrappable French and `buttonVariants` is
            `whitespace-nowrap`.

            ⚠️ The « Ajouter un autre acte » / « Enregistrer la modification » pair that stood here is gone. It
            existed to commit the single draft into the séance, and there is no draft any more — the button that
            adds an act now sits under the acts themselves, where it adds one instead of clearing one.
          */}
          <div className="flex w-full flex-col-reverse gap-2 sm:flex-row sm:items-center sm:justify-end">
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
              {/* ⚠️ On a BILLED fiche these two no longer disable the button — they change what it does.
                  Disabling was how the correction became unreachable: the dentist lowered a price, the button
                  went grey with a sentence beside it, and the refusal that opens « Corriger la note » could
                  never fire because the save never ran. A rule with no way to act on it is a wall, not a guard.
                  On an unbilled fiche `overpaid` still blocks: there it is a plain typo, with no note to
                  correct against. */}
              <Button
                // Wrapped, never `onClick={handleSave}`: the handler's first parameter is now the correction
                // reason, and React would hand it the MouseEvent — truthy, so every ordinary save would retire
                // the note. `tsc` catches it; the wrapper is what makes it unsayable.
                onClick={() => {
                  if (contradictsNote) {
                    setCorrection({ previousTotal: alreadyCollected, nextTotal: grandTotal })
                    return
                  }
                  void handleSave()
                }}
                disabled={loading || (!contradictsNote && (overpaid || lowersBilledAmount))}
                className="w-full sm:w-auto sm:min-w-[150px]"
              >
                {loading
                  ? "Enregistrement…"
                  : contradictsNote
                    ? `Corriger la note${grandTotal > 0 ? ` — ${formatDT(grandTotal)}` : ""}`
                    : `${
                        record ? "Enregistrer" : appointmentId ? "Confirmer la séance" : "Créer la fiche"
                      }${grandTotal > 0 ? ` — ${formatDT(grandTotal)}` : ""}`}
              </Button>
            </div>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
    {correction && (
      <CorrectInvoiceDialog
        open
        onOpenChange={(next) => { if (!next) setCorrection(null) }}
        preview={correction}
        onConfirm={async () => {
          // Re-runs the SAME save with the reason attached, rather than a second endpoint: the payload the
          // dentist just tried is exactly what the correction must persist, and rebuilding it here is how the
          // two would drift.
          await handleSave(DEFAULT_CORRECTION_REASON)
          setCorrection(null)
        }}
      />
    )}
    <DiscardChangesDialog guard={guard} />
    </>
  )
}
