"use client"

import { useState, useEffect, useCallback, useMemo, useRef } from "react"
import { Button } from "@/components/ui/button"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Dialog, DialogBody, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from "@/components/ui/dialog"
import { cn } from "@/lib/utils"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import { Trash2, Plus, Stethoscope, ChevronDown, ChevronRight } from "lucide-react"
import { PatientAlertPanel } from "@/components/patient/patient-alert-panel"
import { dentalRecordsApi } from "@/lib/api/dental-records"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import { odontogramApi } from "@/lib/api/odontogram"
import { useToothDragSelect } from "@/components/tooth-drag-select"
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
  TreatmentPlanItemStepDto,
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
import { PrescriptionSection } from "@/components/record/prescription-section"
import {
  DocumentPreviewDialog,
  type DocumentPreviewTarget,
} from "@/components/documents/document-preview-dialog"
import { medicationsApi } from "@/lib/api/medications"
import { medicalDocumentsApi } from "@/lib/api/medical-documents"
import { PRESCRIPTION_KINDS, prescriptionKind, type PrescriptionLine } from "@/lib/documents"
import type { MedicationDto } from "@/lib/api/types"
import {
  actTotal, hasInvalidPrice, isActNamed, isActTouched, useSessionActs, type BookedActPrefill, type PlanItemPrefill,
} from "@/components/record/use-session-acts"
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

/** « Préparation et Empreinte » / « 1, 2 et 3 » — a French list, for the two-steps-in-one-séance case. */
const joinFr = (parts: string[]): string =>
  parts.length <= 1
    ? (parts[0] ?? "")
    : `${parts.slice(0, -1).join(", ")} et ${parts[parts.length - 1]}`

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
  /** The devis number, for the « Déjà facturé » notice. */
  planNumber?: string | null
  /** The note d'honoraires that holds this devis' money, when one does. */
  billedOnInvoiceNumber?: string | null
  /** What is still to collect on the whole devis — meaningless once `billedOnInvoiceNumber` is set. */
  planOutstanding?: number
  /**
   * The act's protocol — every séance it is cut into, with the ones already carried out dated.
   *
   * <p>⚠️ <b>The steps themselves, and not the two counts this used to carry.</b> It was `stepsTotal` +
   * `stepsDone`, from which the fiche printed « Séance 2 sur 3 » as <code>stepsDone + 1</code> — a rank derived
   * from a count, which is only the séance being recorded when the séances happen in order. A dentist may
   * legitimately book the scellement before the essayage, and `DentalRecordLinker` says exactly which steps
   * this fiche will close: the ones the <b>appointment's own rows</b> name. Counts cannot answer that, and they
   * cannot answer the question that was actually asked either — <i>which</i> séance is this? « Séance 1 sur 3 »
   * names a position and never the work, so the fiche said nothing the dentist did not already know.</p>
   *
   * <p>Absent or empty for an act with no protocol, which is most acts, and the fiche then says nothing about
   * séances at all: « étape 1 sur 1 » is a fact nobody needs and it would appear on every ordinary fiche.</p>
   */
  steps?: TreatmentPlanItemStepDto[]
  /**
   * The catalogue act this devis line is priced on. Carried so a REOPENED fiche can tell which of its acts the
   * devis pays for — see the `markBilledOnPlan` effect. Absent (a hand-typed devis line) falls back to the
   * single-act case, which is unambiguous.
   */
  procedureTypeId?: string | null
}

/**
 * What linking a devis act may carry into the séance — and, crucially, what it may **not**.
 *
 * <p>⚠️ <b>This is the fix for the worst defect in the feature: the fiche charged the act's whole fee again at
 * every séance.</b> `plannedCost` is the fee of the WHOLE act, and the appointment's own row already prices this
 * visit — at 0, because a devis act adds no honoraires. Both effects ran on an untouched séance and each no-ops
 * on a non-empty one, so whichever landed first won: with the plan item first, it named the act (making the
 * session « non-empty »), the appointment prefill bailed, and its correct 0,000 row was demoted to a
 * « remettre ? » chip while the plan's 150,000 became the active, billed line. Measured end to end: a patient
 * paid 300,000 DT for a 150,000 DT act, in cash, on the default button, with the duplicate note carrying no plan
 * link — so none of the de-duplication machinery that protects every aggregate figure could see it. A six-visit
 * implant at 1 500 DT would have billed six times.</p>
 *
 * <p>So when the appointment prices the act, only the <b>teeth</b> travel: the séance's own rows are the
 * authority on both the act and its price, and `applyAppointment` reads them. With no such row — a fiche
 * recorded against no visit, or a visit booked before the act joined the devis — the plan is still the best
 * source and everything is carried, exactly as before.</p>
 */
function planItemPrefill(item: PlanItemOption, appointment?: AppointmentDto | null): PlanItemPrefill {
  const pricedByAppointment = (appointment?.procedures ?? []).some(
    (row) => row.treatmentPlanItemId === item.itemId,
  )
  return pricedByAppointment
    ? { toothNumbers: item.toothNumbers }
    : {
        designationFr: item.designationFr,
        plannedCost: item.plannedCost,
        toothNumbers: item.toothNumbers,
        /*
         * ⚠️ **Always true here, and its absence made this whole branch a dead end.** Linking a devis step means
         * the devis carries the act's fee — `PlanCarriedActPricing` imposes 0 server-side whenever
         * `treatmentPlanItemId` is set, with no exception. The appointment branch above never needed the flag
         * because `applyAppointment` had already set it from the booked row; this branch is the fiche opened
         * with no appointment at all (« Ajouter un acte dentaire »), and it had nothing to set it from.
         */
        billedOnPlan: true,
      }
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
  /*
   * The séance's OTHER money: what the patient handed over towards a multi-séance act priced once on its
   * treatment. Its own field, never folded into « Payé », because they settle two different things — see
   * `CreateDentalRecordCommand.AmountCollectedOnPlan`. Cumulative for the séance, like « Payé », so re-saving a
   * fiche adds the difference or nothing.
   */
  const [collectedOnPlan, setCollectedOnPlan] = useState("")
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
  /*
   * ── « Prescription » ──────────────────────────────────────────────────────────────────────────────────────
   *
   * What this séance prescribed, and the ordonnance it becomes. Folded by default like « Notes de séance »,
   * and auto-opened on a fiche that already carries one (below) — the same rule, for the same reason: a value
   * on record must not need a click to be discovered.
   */
  const [prescriptionOpen, setPrescriptionOpen] = useState(false)
  const [prescriptionLines, setPrescriptionLines] = useState<PrescriptionLine[]>([])
  /** Exactly one line is being typed. Null when every line is at rest — which is how a reopened fiche opens. */
  const [armedPrescriptionIndex, setArmedPrescriptionIndex] = useState<number | null>(null)
  /**
   * The ordonnance this fiche already issued, if any. Read from the DOCUMENT rather than trusted from the
   * history row that opened this modal: the same ordonnance is editable at `/documents/prescription`, so a
   * copy carried in on a list read can be stale, and re-saving it would overwrite whatever that door wrote.
   */
  const [prescriptionDocumentId, setPrescriptionDocumentId] = useState<string | null>(null)
  /**
   * The demande d'examens this fiche already issued, if any — a <b>second</b> document, because a médicament
   * and an examen may not share a sheet. Tracked separately for the same reason the id above is: the fiche
   * updates the document it already owns rather than minting another.
   */
  const [examensDocumentId, setExamensDocumentId] = useState<string | null>(null)
  /**
   * The two documents' concurrency tokens, as read when this modal opened, round-tripped on save.
   *
   * ⚠️ **The document's own xmin does not cover this.** The server loads the document inside the fiche's
   * transaction, so its copy always carries the current token; the stale one is here. Measured: a
   * colleague's correction made in `/documents/prescription` while this modal sat open was silently
   * reverted by the next save. 0 means « not read », which turns the check off.
   */
  const [prescriptionDocumentVersion, setPrescriptionDocumentVersion] = useState(0)
  const [examensDocumentVersion, setExamensDocumentVersion] = useState(0)
  /** Bumped by « Recharger » so the document read runs again with the versions the server now holds. */
  const [prescriptionReload, setPrescriptionReload] = useState(0)
  /**
   * The sheet being looked at, if any. « Aperçu » renders what is about to be saved — composed by the SERVER
   * through the emitter's own path, so the letterhead cannot differ from the real paper (see
   * `PreviewFicheOrdonnanceQuery`). It works before the first save, which is when it is asked for.
   */
  const [previewTarget, setPreviewTarget] = useState<DocumentPreviewTarget | null>(null)
  const [medicationCatalog, setMedicationCatalog] = useState<MedicationDto[]>([])
  /** The catalogue READ failed. Kept apart from an empty catalogue, exactly as `catalogFailed` is for acts. */
  const [medicationCatalogFailed, setMedicationCatalogFailed] = useState(false)
  const [medicationCatalogReload, setMedicationCatalogReload] = useState(0)
  /**
   * The ordonnance exists but could not be READ. Distinct from « nothing prescribed », which is what an empty
   * list would otherwise assert on the one screen where a wrong answer gets re-saved over the right one.
   */
  const [prescriptionReadFailed, setPrescriptionReadFailed] = useState(false)
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
   * What « Recharger » does: take the server's current copy of the record's version and of both ordonnances,
   * then take the banner down. `clearMessage` rather than `setError(null)` on purpose - it keeps the
   * consecutive-conflict count, so a second 409 still escalates to « coordonnez-vous ».
   */
  const reloadFromServer = useCallback(async () => {
    await resync()
    setPrescriptionReload((n) => n + 1)
    conflict.clearMessage()
  }, [resync, conflict])

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

  /*
   * The medication catalogue, for the prescription section's picker.
   *
   * ⚠️ Read WHOLE (`paging: null`) and filtered in the browser, exactly as the ordonnance editor does: it is a
   * per-clinic list of a few dozen entries, and a server round trip per keystroke on a screen used at the chair
   * would be worse than the bytes. ⚠️ A failure is recorded rather than written back as `[]`, for the reason
   * `loadCatalog` above gives at length — an empty picker reads as « this clinic never configured one », so the
   * dentist free-texts the drug and the line loses its DCI snapshot.
   */
  const loadMedicationCatalog = useCallback(async () => {
    try {
      setMedicationCatalog((await medicationsApi.list()) || [])
      setMedicationCatalogFailed(false)
    } catch {
      setMedicationCatalogFailed(true)
    }
  }, [])

  useEffect(() => {
    if (!open) return
    void loadMedicationCatalog()
  }, [open, medicationCatalogReload, loadMedicationCatalog])

  /*
   * The ordonnance this fiche already issued, read from the DOCUMENT.
   *
   * ⚠️ Read fresh rather than hydrated from `record.prescriptionSummary`: that summary is row-sized labels for
   * the history, not the lines, and the document is editable from `/documents/prescription` — so the only
   * honest source for what to put back in the form is the document itself. Same reasoning as `useFreshVersion`
   * one field over, and the same failure rule as the two catalogues: a failed read leaves the section EMPTY and
   * says nothing was prescribed, which would be a lie, so it is recorded as a failure instead.
   */
  useEffect(() => {
    if (!open) return
    const documentId = record?.prescriptionDocumentId ?? null
    const examensId = record?.examensDocumentId ?? null
    if (!documentId && !examensId) {
      setPrescriptionDocumentId(null)
      setExamensDocumentId(null)
      return
    }

    let cancelled = false
    void (async () => {
      try {
        /*
         * ⚠️ BOTH sheets, in parallel, and one section holding the union of their lines. A médicament and an
         * examen may not share a document, but they are one thing to the dentist — « qu'est-ce que j'ai
         * prescrit à cette séance » — so the split is the server's business and the form stays single. Reading
         * only the médicament one is how a reopened fiche would silently drop every examen and then, on save,
         * leave a demande d'examens on file that the section had never shown.
         */
        const [doc, examensDoc] = await Promise.all([
          documentId ? medicalDocumentsApi.get(documentId) : Promise.resolve(null),
          examensId ? medicalDocumentsApi.get(examensId) : Promise.resolve(null),
        ])
        if (cancelled) return

        const content = JSON.parse(doc?.contentJson || "{}") as {
          medications?: unknown
        }
        // The array shape is what both writers persist. A pre-array ordonnance holds one plain string; it
        // becomes a single free-text médicament line rather than being dropped, which is how the document
        // editor absorbs the same legacy shape.
        //
        // ⚠️ `prescriptionKind(line.kind)` is what makes the split need no migration: an ordonnance written
        // before it holds its examens here, each marked `kind: "examen"`, and they come back as examen lines —
        // so the next save moves them onto their own sheet. Dropping the kind read would print a panoramique
        // on a médicament form for ever.
        const medicationLines: PrescriptionLine[] = Array.isArray(content.medications)
          ? (content.medications as PrescriptionLine[]).map((line) => ({
              ...line,
              kind: prescriptionKind(line.kind),
            }))
          : typeof content.medications === "string" && content.medications.trim()
            ? [
                {
                  kind: PRESCRIPTION_KINDS.medicament,
                  name: content.medications.trim(),
                  dosage: "",
                  timesPerDay: "",
                  duration: "",
                },
              ]
            : []

        const examensContent = JSON.parse(examensDoc?.contentJson || "{}") as { examens?: unknown }
        // One field per entry, by design — an examen is a sentence. The blanks are what `emptyPrescriptionLine`
        // writes, so a line read back is indistinguishable from one just typed.
        const examenLines: PrescriptionLine[] = Array.isArray(examensContent.examens)
          ? (examensContent.examens as { name?: unknown }[])
              .map((entry) => (typeof entry?.name === "string" ? entry.name.trim() : ""))
              .filter((name) => name.length > 0)
              .map((name) => ({
                kind: PRESCRIPTION_KINDS.examen,
                name,
                dosage: "",
                timesPerDay: "",
                duration: "",
              }))
          : []

        const lines = [...medicationLines, ...examenLines]
        setPrescriptionDocumentId(documentId)
        setExamensDocumentId(examensId)
        setPrescriptionDocumentVersion(doc?.version ?? 0)
        setExamensDocumentVersion(examensDoc?.version ?? 0)
        setPrescriptionLines(lines)
        // A section holding a value opens itself — « Notes de séance »' rule, one section over.
        setPrescriptionOpen(lines.length > 0)
        setArmedPrescriptionIndex(null)
      } catch {
        if (cancelled) return
        // Both ids are kept: the fiche still HAS these documents, and forgetting that would let the next save
        // mint duplicates. The section is told the read failed so it can say so rather than show an empty list.
        setPrescriptionDocumentId(documentId)
        setExamensDocumentId(examensId)
        setPrescriptionReadFailed(true)
      }
    })()

    return () => {
      cancelled = true
    }
  }, [open, record, prescriptionReload])

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
      /*
       * ⚠️ **Hydrated, and the note that used to say « deliberately NOT hydrated » was reasoning from a premise
       * that has since become false.** It argued that what a fiche collected onto a treatment lives on the
       * plan's échéancier (`InstallmentPayment.DentalRecordId`) and that this modal does not read the plan's
       * payments — true when it was written, and `DentalRecordDto.CollectedOnTreatment` has carried exactly that
       * figure on every read since (`GetDentalRecordsQuery` derives it from the ledger), so the value is in hand.
       *
       * It also called an empty field « safe rather than lossy », and that is the half that was wrong. This field
       * is **cumulative for the séance** — the server collects `typed − already` — so showing 0 on a séance that
       * took 200 DT makes every reading of it false and every edit of it wrong: type the real 200 and the delta
       * is 0, so nothing happens and the fiche appears not to save; type anything lower and the save is refused
       * with « 200,000 DT ont déjà été encaissés … ». Reported in exactly those words. Hydrating it is what makes
       * the figure readable, the « reste après cette séance » line true, and a top-up an ordinary edit.
       */
      setCollectedOnPlan(
        record.collectedOnTreatment ? formatAmount(record.collectedOnTreatment) : "",
      )
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
      // The prescription is hydrated by its own effect (it needs a second read); this only clears what a
      // previous opening left behind, so a fiche with no ordonnance never shows the last one's lines.
      setPrescriptionLines([])
      setPrescriptionOpen(false)
      setArmedPrescriptionIndex(null)
      setPrescriptionReadFailed(false)
      setPrescriptionDocumentVersion(0)
      setExamensDocumentVersion(0)
      setPreviewTarget(null)
    } else {
      setInterventionDate(todayLocalIso())
      setAmountPaid("")
      setCollectedOnPlan("")
      setPaidDirty(false)
      setPaymentMethod(CASH_METHOD)
      setCheque(EMPTY_CHEQUE_FIELDS)
      setNotes([])
      setImportantNotes([])
      setNotesOpen(false)
      setPrescriptionLines([])
      setPrescriptionOpen(false)
      setArmedPrescriptionIndex(null)
      setPrescriptionDocumentId(null)
      setExamensDocumentId(null)
      setPrescriptionDocumentVersion(0)
      setExamensDocumentVersion(0)
      setPrescriptionReadFailed(false)
      setPreviewTarget(null)
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
    dispatch({ type: "applyPlanItem", item: planItemPrefill(linked, appointment) })
  }, [open, record, appointment, planItems, dispatch])

  /*
   * The twin of the effect above, for a fiche being EDITED — and its absence was a money defect, not a missing
   * convenience.
   *
   * ⚠️ **`linkedPlanItemId` had three writers and none of them was the record.** The open effect resets it to
   * `NO_PLAN_ITEM`, the appointment effect is guarded by `if (… || record || …) return`, and the page passes no
   * appointment when a record is being edited (`recordAppointment` is null for `editingRecord`, on its own
   * stated reasoning). So a reopened fiche of a devis-carried act had `billedPlanItem == null`, which made
   * `carriedByDevis` false, which meant: « Acte planifié : Aucun » on a fiche the server has linked, no
   * « Suivi comme traitement » / « Déjà facturé » notice, no « Encaissé sur le traitement » field at all — so
   * the séance could not take the patient's money — and, worst, `markBilledOnPlan` could never fire, so the act
   * card read its stored 0 against the catalogue tarif and announced « geste de 500,000 DT » beside a
   * « remettre au tarif » link. A discount nobody granted, one press from re-charging the devis, and that
   * back-fill's own docstring says it exists to prevent exactly it. Measured on a live fiche at 1440 px.
   *
   * ⚠️ **It only ever SELECTS; it never dispatches a prefill.** `applyPlanItem` carries the act's designation,
   * its `plannedCost` and its teeth into the séance — right when a *new* fiche is being composed, and wrong
   * here: the stored acts are what happened, and `use-session-acts`' own note records that the prefill charged
   * a treatment's whole fee at every séance (300,000 DT taken for a 150,000 DT act). Selecting is enough,
   * because `markBilledOnPlan` below is what the flag needs.
   *
   * ⚠️ **Guarded on `planItems`, the same rule as its twin**: that list holds the plan's *open* acts, so an act
   * on a cancelled plan, or one already fully réalisé, falls through to exactly the behaviour it has today
   * rather than selecting an option the Select does not offer.
   */
  useEffect(() => {
    if (!open || !record?.treatmentPlanItemId) return
    // Never overwrite a choice already made — the user's own pick, or this effect's on an earlier pass.
    if (linkedPlanItemId !== NO_PLAN_ITEM) return
    const linked = planItems.find((p) => p.itemId === record.treatmentPlanItemId)
    if (!linked) return

    setLinkedPlanItemId(linked.itemId)
  }, [open, record, planItems, linkedPlanItemId])

  /**
   * Every act booked into this séance, in the dentist's order, resolved against the catalogue.
   *
   * <p>⚠️ The booked ROW travels with the catalogue entry, not just its id — the agreed price lives on the row,
   * and resolving to a bare `ProcedureTypeDto` is what would price a negotiated act from the tarif.</p>
   */
  /*
   * ⚠️ **Two STEPS of one devis act are two rows on the wire and ONE act in the fiche.** The server keys a
   * séance's duplicate rule on the (act, step) *pair* — that is what makes « préparation + empreinte dans la
   * même séance » expressible — so a two-step séance arrives here as two rows carrying the same
   * `procedureTypeId` and the same `treatmentPlanItemId`. Mapped straight through they became two identical
   * cards: the fiche read « Actes de la séance **2 actes** » for one act, offered the same act back twice in
   * the « remettre » row (two chips with one React key, hence « two children with the same key »), and on a
   * non-devis act would have doubled the fee.
   *
   * Grouped on `treatmentPlanItemId`, exactly as `groupActs` does in `appointment-acts-picker` — the same rule
   * because it answers the same question, and grouping on the *procedure* instead would wrongly merge two
   * distinct devis lines that happen to quote the same act (two teeth, priced separately).
   */
  const bookedActs = useMemo(() => {
    const out: { row: AppointmentProcedureDto; procedure: ProcedureTypeDto }[] = []
    const seenPlanItem = new Set<string>()
    for (const row of (appointment?.procedures ?? []).slice().sort((a, b) => a.sequenceNumber - b.sequenceNumber)) {
      const procedure = row.procedureTypeId
        ? procedureTypes.find((pt) => pt.id === row.procedureTypeId)
        : undefined
      if (!procedure) continue
      if (row.treatmentPlanItemId) {
        if (seenPlanItem.has(row.treatmentPlanItemId)) continue
        seenPlanItem.add(row.treatmentPlanItemId)
      }
      out.push({ row, procedure })
    }
    return out
  }, [appointment?.procedures, procedureTypes])

  // Propose EVERY act booked into the séance. Guarded twice over: `applyAppointment` is a no-op unless the
  // session is a single untouched card, so it can never clobber a saved record or work in progress.
  useEffect(() => {
    if (!open || record || procedureTypes.length === 0) return
    // ⚠️ Prices come from the appointment's own act ROWS, never from `defaultCost` — a visit booked at a
    // negotiated 120 DT would otherwise open the fiche at the 150 DT tarif.
    const prefill: BookedActPrefill[] = bookedActs.map(({ row, procedure }) => ({
      procedure,
      agreedCost: row.agreedCost ?? null,
      // The act row's own devis link is the authority: with it, the 0 is a rule and the card must not read it
      // as a discount or offer to undo it.
      billedOnPlan: row.treatmentPlanItemId != null,
    }))
    // A response predating `procedures` carries only the lead-act scalar; without this fallback such a visit
    // would propose nothing at all.
    if (prefill.length === 0 && appointment?.procedureTypeId) {
      const lead = procedureTypes.find((p) => p.id === appointment.procedureTypeId)
      if (lead) prefill.push({ procedure: lead, agreedCost: null, billedOnPlan: appointment.treatmentPlanItemId != null })
    }
    if (prefill.length > 0) dispatch({ type: "applyAppointment", procedures: prefill })
  }, [open, record, appointment?.procedureTypeId, bookedActs, procedureTypes, dispatch])

  /*
   * « Montant payé » mirrors the running total until the user takes the field over.
   *
   * ⚠️ **`record` is a gate in its own right, and `paidDirty` alone could not be one.** The open effect above
   * sets the field from `record.amountPaid` and then `setPaidDirty(true)` — « a saved amount is the user's,
   * never re-mirrored from the total » — but both effects run in the SAME commit, so this one reads the
   * `paidDirty` of the render it was scheduled from, i.e. still `false`, and being declared later its update
   * lands last and wins. Whether it wins is pure ordering luck: it depends on whether the acts have loaded and
   * `grandTotal` is non-zero on that first pass.
   *
   * Measured on a live fiche while verifying the devis-link hydration: a séance with **999,000 DT** recorded
   * reopened showing **40,000** — its act's price — with the save button enabled, so one press would have
   * written the session total over the patient's real payment. The database row was untouched (`AmountPaid`
   * 999.000, last written weeks earlier), which is what makes this a *display* defect rather than a lost one:
   * silent, and a money figure nobody would think to distrust. A saved record's « Payé » is never a mirror of
   * anything, so the honest guard is the record itself rather than a flag whose value arrives one commit late.
   */
  useEffect(() => {
    if (record || paidDirty || isInvoiced) return
    setAmountPaid(grandTotal > 0 ? formatAmount(grandTotal) : "")
  }, [record, grandTotal, paidDirty, isInvoiced])

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

  /**
   * **Drag across the arch to put a run of teeth on the armed act.**
   *
   * ⚠️ **No mode, unlike the patient page's odontogramme**, and the asymmetry is deliberate: a tap here has
   * exactly one meaning — add this tooth to the armed act — so the drag is simply the plural of the tap and
   * there is nothing for a mode to disambiguate. Over there a tap must ALSO be able to open a tooth's editor,
   * which is why a selection readout survives on that side. Do not « unify » the two charts.
   *
   * ⚠️ `setTooth`, never `toggleTooth`: the hook decides the direction once from the anchor tooth and then
   * SETS each tooth of the range, so a toggle would un-tick every tooth the pointer re-crosses.
   *
   * ⚠️ Disabled with nothing armed, matching the chart's own `disabled` — a tapped tooth has to belong to
   * an act, and a drag that paints nothing must also not consume the click that follows.
   */
  const dragSelect = useToothDragSelect({
    enabled: !!focusedAct && !loading,
    isSelected: (tooth) => !!focusedAct?.toothNumbers.includes(tooth),
    onPaint: (tooth, present) => dispatch({ type: "setTooth", tooth, present }),
  })

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
    dispatch({ type: "applyPlanItem", item: planItemPrefill(item, appointment) })
  }

  /** The devis act this séance is carrying out, when the appointment says so — what A0's notice reads. */
  const billedPlanItem = useMemo(
    () => planItems.find((p) => p.itemId === linkedPlanItemId) ?? null,
    [planItems, linkedPlanItemId],
  )
  /**
   * True when the séance's own act rows carry this devis act — i.e. the fee is on the devis and this visit adds
   * no honoraires. The one fact the fiche needed and did not have.
   */
  /**
   * Does the APPOINTMENT say this séance carries the devis act? The booked row is the authority for a visit made
   * from the agenda, and it is what back-fills a reopened fiche below.
   */
  const carriedByAppointment = useMemo(
    () =>
      billedPlanItem != null &&
      (appointment?.procedures ?? []).some((row) => row.treatmentPlanItemId === billedPlanItem.itemId),
    [billedPlanItem, appointment?.procedures],
  )

  /*
   * ⚠️ **This used to BE the appointment test, while its own docstring said « the séance's own act rows ».** With
   * no appointment — a fiche opened from « Ajouter un acte dentaire » and linked to a devis step by hand — the
   * list is empty, so the séance read as un-carried: « Encaissé sur le traitement » was never offered and there
   * was no way to take the patient's money at that visit at all. The act rows are the honest source, and they
   * carry the flag from both doors now (`applyAppointment` from the booked row, `applyPlanItem` from the link).
   * The appointment stays in the OR for the reopened-fiche case, where the rows have not been marked yet.
   */
  const carriedByDevis =
    billedPlanItem != null && (acts.some((a) => a.billedOnPlan) || carriedByAppointment)

  /**
   * WHICH séance of the treatment this fiche is — « Cette séance : étape 1 sur 3 · Préparation ».
   *
   * <p>⚠️ <b>The step, named, and not merely a rank.</b> The fiche is where a multi-séance act is recorded and
   * it said only the act's name: a dentist recording the préparation of a couronne saw « Couronne / bridge (par
   * élément) », exactly what they would see on the scellement six weeks later, and reported it as « nothing
   * mentions the step that was done ». The label was on record all along — `TreatmentPlanItemStep.Label`, which
   * the saved fiche then reads back through `RecordActsSummary` — so the one screen that could not say what the
   * séance was is the screen that creates it.</p>
   *
   * <p>⚠️ <b>Mirrors {@code DentalRecordLinker.ResolveStepsOfTheSeanceAsync} exactly, and that is the whole
   * point.</b> Which steps a fiche closes is decided server-side from the <b>appointment's own procedure
   * rows</b> — several of them when « préparation + empreinte » share one visit — falling back to the act's
   * next pending step when the séance names none (`TreatmentPlanItem.MarkDone`). Anything else here is a second
   * opinion about a fact the database already holds, and it would name one séance while the save recorded
   * another.</p>
   */
  const seanceStepLine = useMemo(() => {
    /*
     * ⚠️ **A REOPENED fiche answers from ITSELF, and the fallback below would have named the wrong séance.**
     * The page passes no appointment when a record is being edited (`recordAppointment` is null for
     * `editingRecord`, deliberately), so with nothing booked to read this would drop to « the next pending
     * step » — and on a fiche that recorded the préparation, the next pending step is the empreinte. That is
     * the very defect this whole change exists to remove, one door further in. `treatmentStepLabel` is the
     * server's own read-back of the step this record closed (`GetPlanLinksByDentalRecordAsync`), so it is the
     * one answer that cannot disagree with what the patient's history prints for the same fiche.
     */
    if (record?.treatmentStepLabel && record.treatmentStepNumber && record.treatmentStepTotal) {
      return `Cette séance : étape ${record.treatmentStepNumber} sur ${record.treatmentStepTotal} · ${record.treatmentStepLabel}`
    }

    const steps = billedPlanItem?.steps ?? []
    // « étape 1 sur 1 » on every ordinary act is noise; an act booked whole has no step to name.
    if (!billedPlanItem || steps.length <= 1) return null

    const booked = new Set(
      (appointment?.procedures ?? [])
        .filter((row) => row.treatmentPlanItemId === billedPlanItem.itemId && row.treatmentPlanItemStepId)
        .map((row) => row.treatmentPlanItemStepId as string),
    )
    const named = steps.filter((s) => booked.has(s.id))
    // The server's own fallback, not a guess: with no step on the booked row it advances `NextStep`.
    const target =
      named.length > 0 ? named : steps.filter((s) => !s.doneDate).slice(0, 1)
    if (target.length === 0) return null

    const ordered = [...target].sort((a, b) => a.sequenceNumber - b.sequenceNumber)
    const ranks = joinFr(ordered.map((s) => String(s.sequenceNumber + 1)))
    const rank = `étape${ordered.length > 1 ? "s" : ""} ${ranks} sur ${steps.length}`
    return `Cette séance : ${rank} · ${joinFr(ordered.map((s) => s.label))}`
  }, [record, billedPlanItem, appointment?.procedures])

  /*
   * Back-fill « this act is carried by the devis » onto a REOPENED fiche. `applyAppointment` carries it per act
   * for a fresh one, but a saved record is built before any plan data has loaded — and without it the reopened
   * card reads its stored 0 against the catalogue tarif and announces « geste de 120,000 DT » with a
   * « remettre au tarif » link, which is a discount nobody granted and one press from re-charging the devis.
   * The same back-fill shape as `edit-appointment-dialog`'s, and for the same reason: the hydration path is
   * where this family of defect reappears.
   */
  /*
   * ⚠️ **The RECORD is the second source, and it is the one that was missing.** A reopened fiche has no
   * appointment, so `carriedByAppointment` is false and this back-fill — the thing that stops the card
   * announcing the catalogue tarif as a « geste » — could never fire on the one path it was written for: a
   * *saved* record. The record's own server-side link says the same fact the booked row says.
   *
   * ⚠️ Still keyed on neither `carriedByDevis` nor `acts.some(billedOnPlan)`: both read the very flag this
   * dispatch sets, so either would make the back-fill depend on its own outcome. These two inputs are
   * independent of it, which is what keeps the effect a fixpoint (the reducer no-ops once every match is
   * marked).
   */
  const recordCarriesPlanItem =
    billedPlanItem != null && record?.treatmentPlanItemId === billedPlanItem.itemId

  useEffect(() => {
    if (!open || !billedPlanItem) return
    if (!carriedByAppointment && !recordCarriesPlanItem) return
    dispatch({ type: "markBilledOnPlan", procedureTypeId: billedPlanItem.procedureTypeId ?? null })
  }, [open, carriedByAppointment, recordCarriesPlanItem, billedPlanItem, dispatch])

  const paidAmount = parseAmountInput(amountPaid) || 0
  const reste = Math.max(0, roundMillimes(grandTotal - paidAmount))

  /**
   * What the patient owes on the treatment **before** this séance — the plan's own outstanding, read from the
   * server, never derived here.
   *
   * <p>⚠️ <b>This replaced `plannedCost − paidAmount`, which was wrong from the second séance onwards.</b> That
   * expression subtracted only what is being typed *now*, so a 250 DT treatment with 150 already collected read
   * « 250 convenus · reste 250 » at the next visit — the figure a dentist would quote to the patient in front of
   * them. `planOutstanding` is `TotalPlanned − Σ collected` on the aggregate, so it already knows.</p>
   *
   * <p>⚠️ Falls back to the act's whole fee only when the server sent no outstanding at all — an un-numbered
   * treatment has no échéancier, so nothing has been collected on it and its full price *is* what remains.</p>
   */
  /**
   * Does the TREATMENT still collect its own money, or has a note d'honoraires taken it over?
   *
   * <p>⚠️ Once a note holds it (`billedOnInvoiceNumber`), the plan's `outstanding` is its untouched auto-échéance
   * and is simply false — the same trap `displayedOutstanding` and the acts picker both document. Offering to
   * collect against that figure would quote a balance the patient has already paid down elsewhere, so the field
   * is withdrawn and the banner's « Encaissement sur la note … » stands alone.</p>
   */
  const collectsOnTreatment = carriedByDevis && !billedPlanItem?.billedOnInvoiceNumber

  const treatmentOutstandingBefore = billedPlanItem
    ? roundMillimes(billedPlanItem.planOutstanding ?? billedPlanItem.plannedCost ?? 0)
    : 0
  const collectedOnPlanAmount = parseAmountInput(collectedOnPlan) || 0
  /**
   * What this séance has ALREADY put on the treatment — served on every read, derived from the échéancier
   * ledger rather than stored beside the fiche, so voiding a payment corrects it too.
   *
   * <p>⚠️ Everything below is arithmetic on the **increment**, `typed − already`, because that is what the
   * server collects (`CollectOnTreatmentCommand`: « the request carries the séance's cumulative figure, so a
   * re-save must add the difference or nothing »). While the field was never hydrated this constant was
   * always 0 and the distinction cost nothing; with a real figure in the box, every one of these three
   * quantities is wrong without it.</p>
   */
  const alreadyCollectedOnPlan = roundMillimes(record?.collectedOnTreatment ?? 0)
  /** What this save will actually add to the treatment. Negative means somebody is lowering it — see below. */
  const collectionDelta = roundMillimes(collectedOnPlanAmount - alreadyCollectedOnPlan)
  /** What will remain on the treatment once this séance's collection is recorded. */
  const treatmentRemaining = Math.max(
    0,
    roundMillimes(treatmentOutstandingBefore - Math.max(0, collectionDelta)),
  )
  /**
   * More than the treatment is worth. Refused server-side (`treatment_collection_exceeds_outstanding`), so the
   * field says so before the round trip — the same shape as `overpaid` one field over.
   *
   * ⚠️ Compared on the **delta**, exactly as the server does (`delta > plan.Outstanding`). Against the raw
   * typed figure it fired on every correct top-up the moment anything had been collected: 200 already in and
   * 100 left would refuse « 250 », which is a 50 DT collection the server accepts.
   */
  const overCollectedOnPlan =
    collectsOnTreatment && collectionDelta > treatmentOutstandingBefore
  /**
   * Somebody is lowering a collection, which is refused server-side
   * (`treatment_collection_lowered`) — money on a numbered devis is un-received by voiding the payment on the
   * échéancier, never by retyping a field.
   *
   * <p>⚠️ Said HERE, beside the field, rather than as a post-save toast. That refusal used to arrive after the
   * press with the fiche's other changes already written, so « il ne s'édite pas » was the reasonable reading;
   * the remedy is now named next to the control the user is typing into, and the save is disabled.</p>
   */
  const loweredOnPlan = collectsOnTreatment && collectionDelta < 0
  /**
   * Every act of this séance belongs to the devis, so « Payé » has nothing left to settle and is withdrawn.
   * Rendering it beside a 0 total is what sent a dentist to overtype the act's price: the only reachable money
   * field refused every amount they typed, and the act's tarif looked like the mistake.
   */
  /*
   * ⚠️ **Structural — « every act belongs to the treatment » — and NOT « the total came to zero ».** The old
   * test was `roundMillimes(grandTotal) === 0`, which is true of a séance holding a carried couronne *and* a
   * détartrage nobody has priced yet: « Payé » vanished exactly as the dentist was about to type the
   * détartrage's fee, on a fiche that will produce a real note d'honoraires. It now says what it means, so a
   * mixed séance keeps « Payé » and « Total » whatever the acts currently add up to.
   *
   * `namedActs.length > 0` because a fiche with no act yet is not « wholly on the treatment » — it is empty,
   * and `every` over nothing answers true.
   */
  const seanceIsWhollyOnTreatment =
    collectsOnTreatment && namedActs.length > 0 && namedActs.every((a) => a.billedOnPlan)

  /**
   * A stored « Payé » on a séance that is wholly carried by the treatment — so the field is **shown anyway**.
   *
   * <p>⚠️ <b>§ 0: hiding it would remove the only way to read or correct money that is already recorded.</b>
   * `seanceIsWhollyOnTreatment` withdraws « Payé », « Mode » and « Total » because on such a séance they can
   * only ever be 0 — true of a fiche composed today, and **false of one saved before that rule existed**. Those
   * fiches are the direct product of the defect this hydration fixes: with the devis link lost on reopen the
   * card offered a tarif and a price field, so somebody could and did put the patient's cash in « Payé ».
   * Measured on the dev database: <b>23</b> single-act fiches carry a non-zero `AmountPaid` on a plan-linked
   * act. The value is never lost either way — the save sends `amountPaid` back from state, not 0 — but unseen
   * money that cannot be corrected is worse than a field that reads 0.</p>
   *
   * <p>⚠️ Keyed on the <b>record's stored</b> figure, never on the live `amountPaid` state: keying on the state
   * would make the field un-hide itself as soon as a digit was typed into it, i.e. exactly when the rule wants
   * it gone. Typing 0 over it and saving therefore removes it from the next reopen, which is the correction.</p>
   */
  const hasStoredSeancePayment = (record?.amountPaid ?? 0) > 0
  /** The withhold, with its one exception applied. Every « Payé » / « Mode » / « Total » gate reads this. */
  const withholdSeanceMoneyFields = seanceIsWhollyOnTreatment && !hasStoredSeancePayment
  /**
   * Collecting will mint the devis number — <c>CollectOnTreatmentCommand</c> issues one when the treatment has
   * none. A gapless number can only be released by a cancellation carrying a motif, so this is said on the
   * button before it is pressed rather than reported in a toast afterwards.
   */
  const collectionWillIssueDevis =
    collectsOnTreatment && !billedPlanItem?.planNumber && collectionDelta > 0

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
          // ⚠️ Always sent, `[]` included. The server rebuilds every act from this payload, so omitting the key
          // on an act whose last pontique was just un-marked would leave the previous shape stored — the same
          // « an update DTO is tri-state » trap, on a field whose empty value is a real answer (« this bridge's
          // shape is not detailed »). The reducer already guarantees it is a subset of `toothNumbers` and empty
          // for a non-bridge act; the aggregate intersects again regardless.
          ponticToothNumbers: a.ponticTeeth,
          // Both role lists travel together. Sending one without the other flattens half a bridge's shape,
          // silently, on the next save — which is the `procedures`/`SetProcedures` trap on a second field.
          implantPilierToothNumbers: a.implantPilierTeeth,
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
        // Only ever sent with a plan: the server ignores it otherwise, and sending a stray figure would be a
        // number with nowhere to land. `|| 0` rather than `?? 0` — an unparseable field means « nothing typed ».
        amountCollectedOnPlan: collectsOnTreatment ? parseAmountInput(collectedOnPlan) || 0 : 0,
        /*
         * Which STEP of that act this fiche carried out, read off the séance the fiche documents.
         *
         * ⚠️ It is the appointment's row that knows, not the plan: the dentist may legitimately book the
         * scellement before the essayage, and without this the server would advance the act's *next pending*
         * step instead — marking « empreinte » done on the day the bridge was actually sealed. Omitting it is
         * still safe (that fallback is deliberate), which is why an appointment booked before steps existed
         * needs nothing here.
         */
        treatmentPlanItemStepId:
          appointment?.procedures?.find((p) => p.treatmentPlanItemId === linkedItem?.itemId)
            ?.treatmentPlanItemStepId ?? null,
        // Only carried on create — links the new record to the appointment it documents (closes the prompt).
        appointmentId: appointmentId ?? null,
        /*
         * What this séance prescribed. **Always sent**, `[]` included — the same discipline as `acts` and the
         * two bridge role lists above, and for the same reason: a field a routine re-save forgets is how this
         * product has lost data before. An empty list means « nothing prescribed », never « leave it alone »,
         * and it still does not delete an ordonnance already issued (see `FicheOrdonnanceEmitter`).
         *
         * ⚠️ A line with no name is dropped here rather than sent: a blank trailing row is the ordinary state
         * of a form somebody has just clicked « + Médicament » on, and refusing the save over it would be the
         * act list's own rejected behaviour.
         */
        prescription: {
          lines: prescriptionLines
            .filter((line) => line.name?.trim())
            .map((line) => ({ ...line, name: line.name.trim() })),
          // The tokens read when this modal opened. Without them the server cannot tell that the document
          // changed underneath the section, and it silently writes this copy over the other door's edit.
          prescriptionDocumentVersion: prescriptionDocumentVersion || undefined,
          examensDocumentVersion: examensDocumentVersion || undefined,
        },
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
          // ⚠️ Only when the treatment took nothing either, or a séance whose only money went to the devis would
          // report a bare « enregistrée » — the silence this whole feature exists to end.
          if (saved.treatmentCollection?.outcome !== "Collected") {
            toast.success(base)
          }
      }

      /*
       * The treatment's own money, reported separately and always — for `billing`'s reason, one quantity over. A
       * mixed séance legitimately produces both toasts: a note d'honoraires for the filling and an échéance
       * receipt for the bridge.
       */
      switch (saved.treatmentCollection?.outcome) {
        case "Collected": {
          const collection = saved.treatmentCollection
          const devis = collection.planNumber ? ` sur le devis ${collection.planNumber}` : ""
          toast.success(base, {
            description:
              `${formatDT(collection.amountCollected ?? 0)} encaissé${devis}` +
              // The number this save minted, named. It cannot be released except by a cancellation with a motif,
              // so the dentist has to see that it happened even though they were warned before pressing.
              (collection.devisIssued ? " — devis établi" : "") +
              ` · reste ${formatDT(collection.outstanding ?? 0)} sur le traitement`,
          })
          break
        }
        case "Refused":
          // A rule said no. The record is saved; the money is not — both halves have to be said.
          toast.warning(base, {
            description:
              saved.treatmentCollection.message ??
              "L'encaissement sur le traitement a été refusé.",
            duration: 10000,
          })
          break
        // NotCollected / AlreadyCollected — nothing was supposed to move, or nothing moved on a re-save.
        default:
          break
      }

      /*
       * The ordonnances this save emitted, named — for the same reason the money is. A fiche that issues a legal
       * prescription and confirms « Fiche dentaire enregistrée » has told the dentist nothing about the document
       * now sitting in the patient's file, which is the silence the two switches above exist to end.
       *
       * ⚠️ It says which SHEETS, plural when both were written: two separate papers leave this save and the
       * patient hands one to the pharmacie and the other to the laboratoire. It is a toast with no action on
       * purpose — the fiche is closing, so the door is the séance's own row behind it, which opens the document
       * in place.
       */
      const emitted = [
        saved.prescriptionDocumentId ? "ordonnance" : null,
        saved.examensDocumentId ? "demande d'examens" : null,
      ].filter(Boolean) as string[]
      if (emitted.length > 0) {
        toast.success(base, {
          description:
            emitted.length === 2
              ? "Ordonnance et demande d'examens au dossier — imprimables depuis la séance."
              : `${emitted[0] === "ordonnance" ? "Ordonnance" : "Demande d'examens"} au dossier — imprimable depuis la séance.`,
        })
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
      : // ⚠️ **No figure at all when every act is carried by a treatment.** The sum is 0 by rule there, and
        // « 1 acte · 0,000 DT » directly above a card that no longer shows a price is the last of the zeros
        // the crowding complaint was about — a total that can only ever be zero states nothing and reads as a
        // fee that was forgotten. The count still earns its place: it says how many acts the séance holds.
        seanceIsWhollyOnTreatment
        ? `${namedActs.length} acte${namedActs.length > 1 ? "s" : ""}`
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

  /**
   * The cards that came from the booking. Every booked act is proposed now, so this is a set rather than one key
   * — labelling only the first left the second and third looking hand-added on a visit that had planned them.
   */
  const proposedFromAppointment = useMemo(() => {
    if (record) return new Set<string>()
    const booked = new Set(bookedActs.map((b) => b.procedure.id))
    return new Set(
      acts.filter((a) => a.procedureTypeId && booked.has(a.procedureTypeId)).map((a) => a.key),
    )
  }, [record, bookedActs, acts])

  /**
   * The séance's other booked acts, resolved against the catalogue and minus anything already in this session
   * (charted or in the draft) — so the shortcuts thin out as the dentist works through the visit instead of
   * re-offering an act they have just recorded.
   */

  /**
   * Booked acts NOT in the pile. Now that every one is proposed on arrival this is normally empty, and it renders
   * for one case only: an act the dentist deleted. Offering it back is what keeps a mistaken deletion recoverable.
   */
  const otherBookedActs = useMemo(() => {
    const used = new Set<string>(acts.map((a) => a.procedureTypeId).filter((id): id is string => !!id))
    return bookedActs.filter((entry) => !used.has(entry.procedure.id))
  }, [bookedActs, acts])

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
        {/*
          ⚠️ « Recharger » is not decoration - without it a 409 leaves this form POISONED: the versions it holds
          never move, so every later press repeats the refusal, which is the trap the root guide describes (six
          refusals over 81 minutes until the user reloaded the page). Every other dialog in the app already
          offers this action and the fiche was the one that did not - which mattered less while the only 409 came
          from the fiche's own row, and matters now that a colleague editing the ordonnance through
          `/documents/prescription` raises one too.

          It re-reads the record's version AND both documents, which is exactly what the server's own sentence
          promises (« Rechargez pour voir la version a jour, puis appliquez a nouveau votre modification ») - so
          the prescription section is repopulated from the server and the user re-applies. Nothing else typed in
          the fiche is touched.
        */}
        <FormErrorBanner
          message={conflict.error}
          action={
            conflict.isConflict
              ? { label: "Recharger", onClick: () => void reloadFromServer(), disabled: loading }
              : undefined
          }
        />

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
                {/*
                  ⚠️ **`w-full`, because `SelectTrigger` is `w-fit` and this trigger now holds a long value.**
                  The primitive already truncates the value correctly (`block min-w-0 truncate`), but a `w-fit`
                  box sizes to its content first, so the cell's `min-w-0` had nothing to shrink. Invisible while
                  the value was « Aucun »; the moment a reopened fiche hydrates its own devis act the label
                  becomes « 2026-0027 · Couronne / bridge (par élément) » and the trigger measured **380 px in a
                  257 px cell** at 320 px, pushing the whole Patient/Date/Acte row 108 px past the dialog. With
                  `w-full` it takes the cell and ellipsises.
                */}
                <SelectTrigger id="plan-item" className="h-9 w-full">
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

          {/*
            ⚠️ The « Déjà facturé » statement the booking dialog makes and this screen did not. The dialog is
            exemplary about it — read-only 0, « facturé sur le devis », the act's own fee and the devis balance
            as two separately-labelled figures — and the fiche, the screen that actually creates money, said
            nothing at all: no « déjà facturé », no « facturé sur le devis », nowhere on the page. The text
            existed one screen away and never reached this one.
          */}
          {/*
            ⚠️ **The séance's own position leads, and the act card no longer repeats any of this.** The banner
            used to carry three sentences — what the act is chiffré at, that the séance adds no honoraires, and
            where to type what the patient hands over — above an act card that then showed a locked 0, the words
            « Chiffré sur le traitement » and the same 0 twice more. Reported as « trop chargé, la même
            information répétée ». The act card now states the « no honoraires » half once, in its own body; this
            states the two facts only the plan knows: which séance this is, and the agreed total.
          */}
          {/*
            ⚠️ « Cette séance : » is a VISIBLE prefix and the rank is no longer alone. « Séance 2 sur 3 » in
            small caps read as a statement about the treatment's progress rather than about the fiche being
            typed, and it named no work at all — see `seanceStepLine`, which also explains why the rank comes
            from the appointment's booked step and not from `stepsDone + 1`. Not `uppercase` any more either: a
            protocol's own label (« Essai de l'armature ») is prose and shouting it makes it hard to read.
          */}
          {/*
            ⚠️ **Gated on the line itself, NOT on `carriedByDevis`, and that difference is what makes it visible
            on a reopened fiche.** `linkedPlanItemId` is never hydrated from a saved record — it is reset to
            `NO_PLAN_ITEM` on open and only the appointment effect sets it — so `billedPlanItem` is null when a
            fiche is edited, and every one of the money statements below is absent there. Which séance this is
            is not a money statement: it is true whether or not the devis carries the fee, and on the edit path
            it comes from the record's own read-back, which exists only when the fiche really closed a step.
          */}
          {seanceStepLine && (
            <p className="text-2xs font-semibold text-primary">{seanceStepLine}</p>
          )}
          {carriedByDevis && billedPlanItem && (
            <p
              role="status"
              className="rounded-md border border-primary bg-primary/[0.07] p-2.5 text-2xs leading-relaxed"
            >
              {/*
                ⚠️ « le devis » names a DOCUMENT, and an un-numbered followed treatment has none — so on one
                this banner asserted a devis that does not exist. `appointment-acts-picker` was given the
                un-numbered wording and this screen was not: the same non-propagation, one surface apart.

                ⚠️ **It said « Déjà facturé. » and no longer does.** A devis is a quote, not a facture — the
                document that bills is the note d'honoraires, named separately on the line below — and
                « déjà » read as « the money is settled » on a séance that may be about to collect some.
                Reworded on the booking dialog's notice at the same time, deliberately: these two sentences
                are the same statement on two screens and have already drifted apart once.
              */}
              <span className="font-semibold text-primary">
                {billedPlanItem.planNumber ? "Chiffré sur le devis." : "Suivi comme traitement."}
              </span>{" "}
              {billedPlanItem.plannedCost != null ? (
                <>
                  L&apos;acte entier est chiffré{" "}
                  <span className="font-mono tabular-nums">{formatDT(billedPlanItem.plannedCost)}</span>
                </>
              ) : (
                "L'acte entier est chiffré une seule fois"
              )}
              {billedPlanItem.planNumber
                ? ` sur le devis ${billedPlanItem.planNumber}`
                : " pour tout le traitement"}
              {/*
                ⚠️ It used to end « laissez « Payé » à 0 », which was half of a contradiction the same dialog
                carried: this banner said the séance takes no money while the footer labelled its payment field
                « Encaissé aujourd'hui » and offered to state what would remain. The instruction is now the true
                one — the séance adds no honoraires, and money for the treatment has its own field.
              */}
              {/*
                ⚠️ The « où taper » half is gone. It named a field two blocks below and, since that field is now
                the only money control on a wholly-carried séance, the instruction had become a direction to the
                one thing that is impossible to miss. The « n'ajoute pas d'honoraires » half moved to the act
                card, where the missing price field is what raises the question.
              */}
              .
              {/* The devis' own balance is unusable once a note holds the money — its auto-échéance will never
                  see a payment — so the note is named instead. Same rule as the booking dialog's notice. */}
              {billedPlanItem.billedOnInvoiceNumber && (
                <>
                  {" "}Encaissement sur la note{" "}
                  <span className="font-mono">{billedPlanItem.billedOnInvoiceNumber}</span>.
                </>
              )}
            </p>
          )}

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
              proposedFromAppointment={proposedFromAppointment.has(act.key)}
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
          ⚠️ This used to be where every act after the first lived — a faint dashed row of « + » chips under the
          one card that had been proposed. It read as « un seul acte était prévu »: the séance's own header said
          « 1 acte » and showed one act's money for a visit booked for three, so the others were missed and never
          billed. Every booked act is a real card now, and this row is what is left over — an act the dentist
          deleted, offered back, so removing one by mistake is recoverable.
        */}
        {!record && otherBookedActs.length > 0 && (
          <div className="rounded-md border border-dashed bg-muted/30 px-3 py-2">
            <p className="text-xs text-muted-foreground">
              Prévu à ce rendez-vous, retiré de la fiche — remettre :
            </p>
            <div className="mt-1.5 flex flex-wrap gap-1.5">
              {otherBookedActs.map(({ row, procedure }, i) => (
                <Button
                  // Not `procedure.id` alone: two acts of the same type are legitimately two rows (two fillings
                  // booked separately), and a shared key lets React drop one of the chips.
                  key={`${procedure.id}-${row.sequenceNumber}-${i}`}
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
            {/* ⚠️ The gesture hint is PERMANENT, not shown once a drag is under way. On the odontogramme the
                same sentence rendered only while a mode was on, so the one thing that taught the gesture was
                behind already knowing it existed — which is what the dentist reported as « it isn't
                noticeable ». It is withheld only when there is nothing to drag onto. */}
            <Label>
              {focusedAct ? (
                <>
                  Sur quelle(s) dent(s) ?{" "}
                  <span className="font-normal text-muted-foreground">— glissez pour en sélectionner plusieurs</span>
                </>
              ) : (
                "Cliquez un acte pour modifier ses dents"
              )}
            </Label>
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
            dragSelect={dragSelect}
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

        {/*
          « Prescription » — the second folding section, deliberately AFTER the notes and last in the body.
          A séance is recorded before it is prescribed for, and the money band in the sticky footer is the one
          block already documented as barely fitting 320 px, so nothing new goes there.
        */}
        <PrescriptionSection
          lines={prescriptionLines}
          armedIndex={armedPrescriptionIndex}
          open={prescriptionOpen}
          onToggle={() => setPrescriptionOpen((v) => !v)}
          onLinesChange={setPrescriptionLines}
          onArmedIndexChange={setArmedPrescriptionIndex}
          catalog={medicationCatalog}
          catalogFailed={medicationCatalogFailed}
          onRetryCatalog={() => setMedicationCatalogReload((n) => n + 1)}
          existingDocumentId={prescriptionDocumentId}
          existingExamensDocumentId={examensDocumentId}
          // The allergy, the maladies and the médicaments, beside the box that acts on them — the banner at the
          // top of this dialog has scrolled away by the time anyone is typing a drug name. See the section.
          patient={patient}
          onPreview={
            patientId
              ? (kind) =>
                  setPreviewTarget({
                    mode: "apercu",
                    kind,
                    patientId,
                    // Served on the DTO precisely so the aperçu is issued in the same practitioner's name as
                    // the document the save will emit. Absent on a new fiche, where the server attributes it
                    // to the caller — which is what it will do on the save too.
                    doctorId: record?.doctorId ?? undefined,
                    interventionDate,
                    lines: prescriptionLines,
                  })
              : undefined
          }
          readFailed={prescriptionReadFailed}
          disabled={loading}
        />

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
            {/*
              ⚠️ « Payé » settles THIS SÉANCE'S OWN ACTS and produces a note d'honoraires. It is withdrawn when
              every act belongs to the devis, because there is then nothing for it to settle: those acts are 0 by
              rule, so the field would refuse every amount typed into it — which is exactly how a dentist came to
              overtype an act's tarif to get money in, raising a second claim for work the treatment already
              prices. The treatment's own field sits below.

              It used to be relabelled « Encaissé aujourd'hui » here and left in place, which promised a
              mechanism that did not exist: nothing on this path could put that money anywhere.
            */}
            {!withholdSeanceMoneyFields && (
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
            )}
            {/* Beside the amount, because « combien » and « comment » are one answer. `min-w` + `flex-1` so it
                wraps to its own full-width line below ~360 px instead of squeezing the amount field.

                ⚠️ **It follows the money, and on a wholly-carried séance the money is « Encaissé sur le
                traitement » — so the field goes down there rather than staying here.** It was paired with
                « Total » while being sent with `amountCollectedOnPlan`: two fields that produce nothing on
                this séance, above the one that does, with the mode attached to the wrong pair. Reported as
                « les champs d'argent en bas ont l'air bizarres ». */}
            {!withholdSeanceMoneyFields && (
              <PaymentMethodField
                value={paymentMethod}
                onChange={setPaymentMethod}
                disabled={loading}
                className="min-w-[9rem] flex-1"
              />
            )}
            {/* The total is EDITABLE, and typing in it re-prices the acts (`setTotal` → `distributeSessionTotal`)
                rather than storing a figure of its own — the acts are what the note d'honoraires is built from.
                It is still `Σ actTotal` on the way out, which is why editing an act afterwards simply moves it
                again: there is no contest to resolve, and the last person to type always wins.

                ⚠️ **Withdrawn when every act is carried by the treatment**, for « Payé »'s reason one field
                over: the note d'honoraires it builds cannot exist, so the field can only ever read 0,000. It
                was worse than useless — `distributeSessionTotal` did not exclude carried acts, so typing here
                wrote the amount into the act's own READ-ONLY price field and the server discarded it on save.
                The distribution is fixed at its source; this hides the control that had no subject.

                ⚠️ **This one keeps `seanceIsWhollyOnTreatment` and takes NO stored-payment exception**, unlike
                « Payé » two fields up. The exception exists so already-recorded money stays readable and
                correctable; « Total » holds no recorded money — it re-prices the acts, and on a wholly-carried
                séance `distributeSessionTotal` has no act eligible to receive it. Un-hiding it would put back a
                control that visibly accepts a figure and changes nothing, which is the defect this hide was
                added for. */}
            {!seanceIsWhollyOnTreatment && (
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
            )}
            {/*
              THE TREATMENT'S OWN MONEY — its own field, on its own row, because it is a different quantity from
              « Payé » and no arithmetic is ever done between them. A multi-séance act is priced ONCE on its
              devis; each visit collects part of that one figure, and the amount typed here goes onto the
              treatment's échéancier rather than onto a note d'honoraires for this séance.

              ⚠️ It replaced a *label change* on « Payé » that promised exactly this and delivered nothing: there
              was no path from that field to a treatment, the save was disabled for any amount typed (a devis act
              is 0, so the séance's total is 0), and the only way through was to overtype the act's tarif — which
              raises a second, unlinked claim for work the treatment already prices.
            */}
            {collectsOnTreatment && (
              <div className="flex w-full flex-wrap items-center gap-x-3 gap-y-2">
                <div className="flex min-w-[11rem] flex-1 items-center gap-2">
                  <Label
                    htmlFor="collected-on-plan"
                    className="shrink-0 text-xs text-muted-foreground"
                  >
                    Encaissé sur le traitement
                  </Label>
                  {/* `text` + `inputMode="decimal"`, never `type="number"` (J8) — same reason as « Payé »: a
                      number input refuses the comma this product prints with and hands back an EMPTY value. */}
                  <Input
                    id="collected-on-plan"
                    type="text"
                    inputMode="decimal"
                    className={cn(
                      "h-8 w-full text-right tabular-nums",
                      overCollectedOnPlan && "border-destructive",
                    )}
                    value={collectedOnPlan}
                    onChange={(e) => setCollectedOnPlan(e.target.value)}
                    placeholder="0,000"
                    disabled={loading}
                    aria-invalid={overCollectedOnPlan}
                    aria-describedby="collected-on-plan-hint"
                  />
                </div>
                {/* The mode, beside the only amount this séance can take. Same field, same state, same payload
                    key — it moved here rather than being duplicated, so « comment » sits with « combien »
                    exactly as it does on an ordinary fiche.

                    ⚠️ **`withholdSeanceMoneyFields`, i.e. the exact complement of the « Payé » gate above, so
                    the two can never both render.** `PaymentMethodField` hard-codes `id="paid-method"` and
                    `htmlFor="paid-method"`, so a second instance is a duplicate id — one label pointing at two
                    controls over one piece of state, which is invalid and picks whichever the browser finds
                    first. Reachable the moment a wholly-carried séance keeps « Payé » for a stored payment. */}
                {withholdSeanceMoneyFields && (
                  <PaymentMethodField
                    value={paymentMethod}
                    onChange={setPaymentMethod}
                    disabled={loading}
                    className="min-w-[9rem] flex-1"
                  />
                )}
                {/*
                  THE THREE FIGURES, so the amount can never be read as a price: what the whole treatment costs,
                  and what is left after this séance. `planOutstanding` — the plan's own figure — not
                  `plannedCost − typed`, which ignored every earlier séance and quoted the full price again at
                  every visit.
                */}
                <p
                  id="collected-on-plan-hint"
                  role="status"
                  className="w-full text-2xs text-muted-foreground"
                >
                  {billedPlanItem?.plannedCost != null && (
                    <>
                      <span className="font-mono tabular-nums">
                        {formatDT(billedPlanItem.plannedCost)}
                      </span>{" "}
                      convenus pour tout le traitement
                      {billedPlanItem.planNumber ? ` (${billedPlanItem.planNumber})` : ""} ·{" "}
                    </>
                  )}
                  {overCollectedOnPlan ? (
                    <span className="font-medium text-destructive">
                      il ne reste que{" "}
                      <span className="font-mono tabular-nums">
                        {formatDT(treatmentOutstandingBefore)}
                      </span>{" "}
                      à encaisser sur ce traitement
                    </span>
                  ) : loweredOnPlan ? (
                    /*
                      ⚠️ The refusal, moved from a post-save toast to the field itself — and it names the way
                      out. Un-receiving money is a real operation and it belongs on the devis' échéancier,
                      where the payment row can be voided with its motif and its author kept; retyping a
                      figure on a fiche would leave la caisse and the échéancier disagreeing.
                    */
                    <span className="font-medium text-destructive">
                      <span className="font-mono tabular-nums">
                        {formatDT(alreadyCollectedOnPlan)}
                      </span>{" "}
                      déjà encaissés sur cette séance · un encaissement ne se diminue pas ici : annulez le
                      paiement sur l&apos;échéancier du devis
                    </span>
                  ) : (
                    /*
                      ⚠️ **Three clauses, and the middle one is what was missing.** With « 200 déjà encaissés »
                      beside « reste 250 » the two figures contradict each other — 200 is the past and 250
                      already counts the 100 being typed, so the line reads as arithmetic that does not add up
                      (reported in exactly those terms). The increment is named between them, and the tense
                      turns: « reste » while nothing is being added, « restera » once something is.
                    */
                    <span className="font-medium text-foreground">
                      {alreadyCollectedOnPlan > 0 && (
                        <>
                          <span className="font-mono tabular-nums">
                            {formatDT(alreadyCollectedOnPlan)}
                          </span>{" "}
                          déjà encaissés sur cette séance ·{" "}
                        </>
                      )}
                      {collectionDelta > 0 && (
                        <>
                          <span className="font-mono tabular-nums">
                            +{formatDT(collectionDelta)}
                          </span>{" "}
                          avec cet enregistrement ·{" "}
                        </>
                      )}
                      {collectionDelta > 0 ? "restera " : "reste "}
                      <span className="font-mono tabular-nums">{formatDT(treatmentRemaining)}</span>{" "}
                      sur ce traitement
                    </span>
                  )}
                </p>
                {/*
                  ⚠️ Said BEFORE the press, never in a toast afterwards. Collecting on a treatment that has no
                  devis gives it one, and a gapless number can only be released by a cancellation carrying a
                  motif — so the one thing this must not do is happen quietly.
                */}
                {collectionWillIssueDevis && (
                  <p role="status" className="w-full text-2xs text-warning-ink">
                    Ce traitement n&apos;a pas encore de devis : l&apos;encaisser lui en attribuera un.
                  </p>
                )}
              </div>
            )}
            {/*
              Wraps to its own line below `sm:` — three figures do not fit 342px, and « Reste à payer » is the
              one of the three that is a sentence rather than a number.

              ⚠️ Withdrawn entirely when every act is the treatment's, because it describes « Payé », which is
              itself withdrawn there. Left in, it rendered « Reste à payer : 0,000 DT » under a séance where the
              patient owes 100 on the treatment — a true statement about the séance read as a false one about
              the patient, directly beneath the field that says otherwise.
            */}
            {!withholdSeanceMoneyFields && (
            <div className="w-full text-xs sm:w-auto">
              {overpaid ? (
                <p role="status" className="font-medium text-destructive">
                  Le montant payé dépasse le total de la séance ({formatDT(grandTotal)}).{" "}
                  {/*
                    ⚠️ The generic advice — « Corrigez le montant, ou ajoutez l'acte qui manque » — is actively
                    wrong on a devis séance: there is no missing act, and a dentist following it literally adds
                    one, which prices the treatment a second time. The correct action is the échéancier, and it
                    was named nowhere: not in the refusal, not on the fiche, not in the booking notice.
                  */}
                  {carriedByDevis ? (
                    <span className="font-normal text-muted-foreground">
                      Cette séance est portée par le devis{" "}
                      {billedPlanItem?.planNumber ?? ""} : l&apos;argent remis au fauteuil s&apos;encaisse sur
                      son échéancier, pas ici.
                    </span>
                  ) : (
                    <span className="font-normal text-muted-foreground">
                      Corrigez le montant, ou ajoutez l&apos;acte qui manque.
                    </span>
                  )}
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
            )}
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
                // ⚠️ `overCollectedOnPlan` DISABLES rather than becoming « Corriger » — unlike `overpaid` on a
                // billed fiche, there is no document to correct here: the server would simply refuse more than
                // the treatment is worth, and the figure to fix is on screen beside the field.
                disabled={
                  loading
                  || overCollectedOnPlan
                  // Same reasoning as `overCollectedOnPlan`: the server refuses it outright with a remedy that
                  // lives on another screen, so there is nothing for a « Corriger » branch to do here.
                  || loweredOnPlan
                  || (!contradictsNote && (overpaid || lowersBilledAmount))
                }
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
    {/*
      The aperçu, over the fiche. A sibling of the Dialog rather than a child so it stacks above it — and it
      carries no `onEditInFiche`, deliberately: the fiche IS what is open behind it, so a door back to it
      would be a control that closes one dialog to reveal the same one.
    */}
    <DocumentPreviewDialog target={previewTarget} onClose={() => setPreviewTarget(null)} />
    </>
  )
}

/**
 * « Mode » — how the money came in. <b>One definition, two placements.</b>
 *
 * <p>The field belongs beside the amount it describes, and which amount that is depends on the séance: an
 * ordinary fiche settles its own acts through « Payé », while a séance every act of which is carried by a
 * treatment settles nothing and takes its money through « Encaissé sur le traitement ». Both send the same
 * `paymentMethod` on the same payload, so this is one control that moves — not two that could drift into
 * offering different lists, or into one of them being left behind when `FICHE_PAYMENT_METHODS` grows.</p>
 */
function PaymentMethodField({
  value,
  onChange,
  disabled,
  className,
}: {
  value: string
  onChange: (next: string) => void
  disabled?: boolean
  className?: string
}) {
  return (
    <div className={cn("flex items-center gap-2", className)}>
      <Label htmlFor="paid-method" className="shrink-0 text-xs text-muted-foreground">
        Mode
      </Label>
      <Select value={value} onValueChange={onChange} disabled={disabled}>
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
  )
}
