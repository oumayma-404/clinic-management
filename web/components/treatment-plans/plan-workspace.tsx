"use client"

import { Fragment, useCallback, useEffect, useMemo, useRef, useState } from "react"
import Link from "next/link"
import { useRouter } from "next/navigation"
// The anchor « Encaisser sur la note » lands on. Imported rather than retyped, so the two ends cannot drift —
// the patient page switches its own tab and scrolls to this id.
import { patientOutstandingHref } from "@/components/patient/patient-outstanding-strip"
import { paymentMethodLabel } from "@/components/factures/invoice-labels"
import { cn } from "@/lib/utils"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { Table, TableBody, TableHead, TableHeader, TableRow, TableCell } from "@/components/ui/table"
import { CardList, CARDS_ONLY_LG, TABLE_ONLY_LG } from "@/components/ui/card-list"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import type { LucideIcon } from "lucide-react"
import { Consequences } from "./plan-consequences"
import { Textarea } from "@/components/ui/textarea"
import { Label } from "@/components/ui/label"
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog"
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent,
  AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { Checkbox } from "@/components/ui/checkbox"
import { EmptyState } from "@/components/ui/empty-state"
import {
  AlertTriangle, ArrowLeft, Ban, CreditCard, FileDown, Loader2, ReceiptText, CheckCheck, ClipboardCheck, FilePen,
  CalendarClock, CalendarPlus, ChevronRight, FilePlus2, Layers, ListChecks, MoreHorizontal, Plus, X, Undo2,
  Trash2,
  CircleSlash,
  RotateCcw,
  Stethoscope,
  Unlink,
  UserCog,
  Copy,
  HandCoins,
  Wallet,
} from "lucide-react"
import { toast } from "sonner"
import { showErrorToast } from "@/lib/errors"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import { invoicesApi } from "@/lib/api/invoices"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import { patientsApi } from "@/lib/api/patients"
import { Input } from "@/components/ui/input"
import type {
  AppointmentDto,
  InstallmentDto,
  InstallmentPaymentDto,
  PatientDto,
  ProcedureTypeDto,
  TreatmentPlanDto,
  TreatmentPlanItemDto,
} from "@/lib/api/types"
import { formatAmount, formatDT, formatDateFr, parseAmountInput, quoteFr } from "@/lib/format"
import { downloadBlob } from "@/lib/download"
import {
  planStatusLabel,
  planStatusBadgeClass,
  planHasRecordedWork,
  installmentDueLabel,
  installmentDueSentence,
  planDevisLabel,
  teethSuffix,
  treatmentName,
} from "./treatment-plan-labels"
import { doctorsForPicker, useDoctors } from "@/lib/hooks/use-doctors"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import {
  REFUND_DECLINED, RefundMethodField, usePlanRefundConfirm, type RefundMethod,
} from "./plan-refund-confirm"
import {
  activeItems,
  canAmendPlan,
  canBillPlan,
  canCancelPlan,
  canDeletePlan,
  canUncancelPlan,
  canWriteOffPlan,
  detachOutcome,
  isPlanWrittenOff,
  itemDiscount,
  itemNetCost,
  isPlanClosedToWrites,
  relinkTargets,
  displayedOutstanding,
  isPlanLive,
  isPlanStopped,
  hasDeliveredWork,
  isItemWithdrawn,
  isPlanBilled,
  firstUnbookedStep,
  nextStepOf,
  planItemToPreset,
  planSeanceProgress,
  schedulablePlanItems,
  stopNeedsRefundFirst,
  stopWouldCancelPlan,
} from "./plan-next-action"
import {
  PlanActPrimaryAction, PlanActReorderControls, PlanActRow, PlanActSelectionBox, PlanActStateBadge,
  PlanActStepsAction, PlanActEditAction, PlanActWithdrawAction, PlanActSeances, PlanActCostEditor,
  planActCardFields, hasPlanActPrimaryAction, planActStateShown, SQUARE_CHECKBOX, type PlanActCostDraft,
  type PlanActSeanceHandlers,
} from "./plan-act-row"
import { EditAppointmentDialog } from "@/components/edit-appointment-dialog"
import { appointmentsApi } from "@/lib/api/appointments"
import { PlanItemStepsDialog } from "./plan-item-steps-dialog"
import { PlanTimeline } from "./plan-timeline"
import { seanceCountLabel } from "./plan-act-pips"
import { InstallmentPaymentModal } from "./installment-payment-modal"
import { ReviseInstallmentsModal } from "./revise-installments-modal"
import { VoidInstallmentPayment } from "./void-installment-payment"
import { SettlePlanModal } from "./settle-plan-modal"
import { TreatmentPlanFormModal } from "./treatment-plan-form-modal"
import { CreateAppointmentDialog, type PresetPlanAct } from "@/components/create-appointment-dialog"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from "@/components/ui/command"
import { ToothMultiSelect } from "@/components/tooth-multiselect"
import { groupProceduresByCategory } from "@/components/procedure-categories"
import { useConflict } from "@/lib/hooks/use-conflict"
import type { AmendTreatmentPlanRequest } from "@/lib/api/treatment-plans"
import {
  buildAmendRequest, lineFromProcedure, planInstallmentRows, planLinesFromPlan, repricedCost, type PlanLineRow,
} from "./plan-amend-payload"

/**
 * A plan-level state change waiting for the user to say yes.
 *
 * <p><b>Why this exists.</b> « Accepter le devis », « Facturer le devis » and « Terminer » all fired on the
 * first click, and all three are one-way. Accepting numbers a devis and ends free editing; facturer creates a
 * note d'honoraires **and navigates away to /factures**, so a mis-click both writes a document and loses the
 * page; terminer closes the plan. Worse, they sat in the same header row as « Devis PDF » and « Envoyer par
 * email » — two harmless reads — and « Terminer » wore `variant="outline"` among the neutral buttons, so
 * nothing about the control's appearance distinguished « print this » from « close this plan for good ».</p>
 *
 * <p>Modelled as one dialog driven by state rather than three dialogs, so every plan-level confirmation is
 * guaranteed to carry the same shape: what will happen, to which numbered devis, and what will <i>not</i>
 * happen. A per-action dialog is how one of them ends up without the consequence sentence.</p>
 */
interface PlanConfirm {
  title: string
  description: React.ReactNode
  confirmLabel: string
  /** Runs the mutation. Resolved before the dialog closes so the busy state covers the whole round trip. */
  onConfirm: () => Promise<void>
}

/** The header's one large button — see `primaryAction`. `kind` lets the menu withhold its own twin. */
interface PrimaryAction {
  kind: "uncancel" | "reopen" | "bill" | "record" | "schedule"
  label: string
  icon: LucideIcon
  run: () => void
}

/** The small caps heading over each group of the « ⋯ » menu. */
const MENU_GROUP_LABEL = "text-2xs font-semibold uppercase tracking-wide text-muted-foreground"
/**
 * The page's three cards, tighter than the primitive: its `gap-6` plus the header's empty second grid row put
 * ~45 px between « Actes » and its first act. Here only — other pages keep `Card`'s own rhythm.
 */
const CARD_GAP = "gap-4"
const CARD_HEADER = "gap-0"
import { planItemState } from "./plan-next-action"
import { PatientNameLink } from "@/components/patient-name-link"

interface PlanWorkspaceProps {
  plan: TreatmentPlanDto
  /** Refetch the plan after any mutation (the parent owns the fetch). */
  onChanged: () => void
}

/**
 * One line per encaissement on an échéance — including the **voided** ones, struck through with their motif and
 * the colleague who annulled them.
 *
 * <p>Before AC-5 the workspace rendered only live payments, as buttons. A void therefore made a row vanish, so an
 * échéance that had visibly taken money could read « Encaissé 0,000 » with nothing on the screen explaining it.
 * Keeping the row is the same rule the invoice detail follows and the same one la caisse's extrait follows: a
 * correction is evidence, not an erasure.</p>
 */
/**
 * The état of one échéance, in the ONE place both the table and the card list read it.
 *
 * <p>⚠️ <b>`inst.isOverdue` is served, and it must never be recomputed here from `dueDate`.</b> Both forms used
 * to write `isBeforeToday(inst.dueDate)`, which answers a simpler question and gave the wrong answer on
 * <b>25 of 27</b> unpaid rows in the dev database — every devis went red the day after it was signed, cancelled
 * and already-invoiced ones included. The reason is that `TreatmentPlan.Accept` raises a lump-sum row for the
 * whole total <i>dated at the acceptance instant</i> when no schedule was given: a ledger container so a payment
 * has somewhere to live, not a day anybody promised. `InstallmentLateness` is the rule; it also needs the plan's
 * status, its note d'honoraires and whether any act is still unrealised, none of which a row knows.</p>
 *
 * <p>⚠️ <b>« Solde à régler », and it used to read « Échéance non convenue » beside a date.</b> That row is not
 * an échéance at all — it is the ledger container, and printing a fabricated date next to a badge saying nobody
 * agreed it is a contradiction inside one row. 159 of the 184 installment rows on the dev database are these,
 * 133 of them never paid. It is named for what it is and {@link InstallmentDueCell} withholds its date; the
 * dated table is reserved for a schedule a dentist actually typed.</p>
 */
function InstallmentStatusBadge({ inst }: { inst: InstallmentDto }) {
  if (inst.isPaid) return <Badge variant="secondary">Payée</Badge>
  if (inst.isOverdue) return <Badge variant="destructive">En retard</Badge>
  // The auto-raised row is already named « Solde à régler » by its due cell (N39's owner) — once is enough.
  if (inst.isAutoRaised) return null
  return <Badge variant="outline">En attente</Badge>
}

/**
 * What an échéance row prints where its due date goes.
 *
 * <p>⚠️ <b>An auto-raised row shows NO date.</b> Its `dueDate` is the acceptance instant — a value
 * `TreatmentPlan.Accept` has to write because a payment needs somewhere to attach, not a day anybody promised —
 * and printing it invites exactly the reading the server now refuses to make: `InstallmentLateness` stopped
 * comparing that date at all, so the screen must stop showing it too, or the two disagree about what the row
 * means. The sentence in its place says what the figure IS.</p>
 *
 * <p>A row a dentist typed keeps its date, because that date is the whole point of an échéancier.</p>
 */
function InstallmentDueCell({ inst }: { inst: InstallmentDto }) {
  if (inst.isAutoRaised) return <span className="text-muted-foreground">{installmentDueLabel(inst)}</span>
  return <span className="tabular-nums">{installmentDueLabel(inst)}</span>
}

function InstallmentPaymentLines({
  payments,
  className,
}: {
  payments: InstallmentPaymentDto[]
  className?: string
}) {
  return (
    <ul className={`space-y-0.5 text-xs text-muted-foreground ${className ?? ""}`}>
      {payments.map((payment) => (
        <li key={payment.id}>
          <span className={payment.isVoided ? "line-through" : ""}>
            {payment.amount < 0
              ? `Rendu au patient ${formatDT(-payment.amount)} · ${formatDateFr(payment.paidOn)}`
              : `${formatDT(payment.amount)} · ${formatDateFr(payment.paidOn)}`}
          </span>
          {payment.isVoided && (
            <span className="block">
              Annulé{payment.voidedAt ? ` le ${formatDateFr(payment.voidedAt)}` : ""}
              {payment.voidedByName ? ` par ${payment.voidedByName}` : ""}
              {payment.voidReason ? ` — ${quoteFr(payment.voidReason)}` : ""}
            </span>
          )}
        </li>
      ))}
    </ul>
  )
}

/**
 * The devis's home: header, actes, échéancier and parcours on one page. Replaces the plans-table "Gérer"
 * dialog, which was the only place a plan's contents were visible and offered every action on every row.
 */
export function PlanWorkspace({ plan, onChanged }: PlanWorkspaceProps) {
  const router = useRouter()
  const [busy, setBusy] = useState(false)
  const [paymentTarget, setPaymentTarget] = useState<InstallmentDto | null>(null)
  /**
   * The échéance payment whose annulation is being confirmed; null = no panel (AC-5).
   *
   * <p>The void endpoint has existed, tested and reachable from the client module, with **no caller** — so a
   * mis-keyed installment payment was permanent while the identical mistake on an invoice payment was two clicks
   * from being corrected. Held as `{installmentId, payment}` because an `InstallmentPayment` is only addressable
   * as (plan, échéance, paiement).</p>
   */
  const [voidTarget, setVoidTarget] = useState<{ installment: InstallmentDto; payment: InstallmentPaymentDto } | null>(
    null,
  )
  /** Bookings still to make, each element being one appointment. Only ever empty or a single group now — the bar's
   * « séparément » split (N groups of one) was removed as a duplicate of each act's own « Planifier ». */
  const [bookingQueue, setBookingQueue] = useState<PresetPlanAct[][]>([])
  /** Plan acts ticked for booking (ids). Only « À planifier » acts can be in here. */
  const [selectedActIds, setSelectedActIds] = useState<string[]>([])
  /** True for the one close that follows a successful create — see `finishCurrentBooking`. */
  const justAdvancedRef = useRef(false)
  const [cancelOpen, setCancelOpen] = useState(false)
  /** « Annuler le devis » — its own door now that the stop branch cannot reach every case. See `canCancelPlan`. */
  const [cancelPlanOpen, setCancelPlanOpen] = useState(false)
  /** « Rétablir ce devis annulé » (C3) — the only way out of `Cancelled`, and it carries a motif of its own. */
  const [uncancelOpen, setUncancelOpen] = useState(false)
  const [uncancelReason, setUncancelReason] = useState("")
  /** « Passer la créance en perte » (S4) — its motif is the evidence for a loss, so it is required. */
  const [writeOffOpen, setWriteOffOpen] = useState(false)
  const [writeOffReason, setWriteOffReason] = useState("")
  /** « Régler le devis » (S3) — one encaissement spread over the échéancier. */
  const [settleOpen, setSettleOpen] = useState(false)
  /** « Dupliquer ce devis » (S1) — confirmed, because it creates a second document in the patient's file. */
  const [duplicateOpen, setDuplicateOpen] = useState(false)
  /**
   * Where the séance being detached should go instead (S6) — the `itemId:stepId` key, or "" for « nowhere »,
   * which is the ordinary detach.
   */
  const [relinkKey, setRelinkKey] = useState("")
  /** The act being parked or brought back — per-act, which is the literal complaint M1/M2 describe. */
  const [withdrawTarget, setWithdrawTarget] = useState<TreatmentPlanItemDto | null>(null)
  const [restoreTarget, setRestoreTarget] = useState<TreatmentPlanItemDto | null>(null)
  /** The note d'honoraires being detached from this devis — the remedy three server refusals name. */
  const [detachNoteOpen, setDetachNoteOpen] = useState(false)
  /** « Praticien » — open editor, plus the id being chosen. See the header's own note. */
  const [doctorOpen, setDoctorOpen] = useState(false)
  const [doctorDraft, setDoctorDraft] = useState<string>("")
  /**
   * « Changer de patient » (M5) — the devis was written under the wrong person.
   *
   * <p>⚠️ `PatientId` was ctor-only with no mutator and a disabled `Input` in every edit mode, so the only
   * remedy was to delete the devis (possible for a pristine draft alone) or to retype the whole thing under a
   * new number. The server refuses once anything has been delivered or a live note names it, which is what
   * makes offering it safe.</p>
   */
  const [reassignOpen, setReassignOpen] = useState(false)
  const [patientQuery, setPatientQuery] = useState("")
  const [patientResults, setPatientResults] = useState<PatientDto[] | null>(null)
  const [patientDraft, setPatientDraft] = useState<PatientDto | null>(null)
  /** What cancelling or deleting does to the séances already booked on this plan — null when none is. */
  const bookedVisitCount = new Set(plan.items.map((i) => i.scheduledAppointmentId).filter(Boolean)).size
  const bookedVisitsBullet =
    bookedVisitCount === 0 ? null : (
      <>
        <b className="text-foreground">
          {bookedVisitCount} RDV prévu{bookedVisitCount > 1 ? "s" : ""}
        </b>{" "}
        {bookedVisitCount > 1 ? "seront libérés" : "sera libéré"}
      </>
    )
  /** « Supprimer le traitement » — a followed treatment nothing has been recorded on. See `canDelete`. */
  const [deleteOpen, setDeleteOpen] = useState(false)
  const [cancelReason, setCancelReason] = useState("")
  /** The plan-level state change awaiting confirmation; null = no dialog. See {@link PlanConfirm}. */
  const [confirmAction, setConfirmAction] = useState<PlanConfirm | null>(null)
  const [procedureTypes, setProcedureTypes] = useState<ProcedureTypeDto[]>([])
  /** The act catalogue read failed — not the same as a devis whose acts are legitimately all free text. */
  const [catalogFailed, setCatalogFailed] = useState(false)
  const [amendOpen, setAmendOpen] = useState(false)
  /**
   * Which act « Modifier le devis » should open on. Null when the header's « ⋯ » opened it, which is the
   * whole-plan case and legitimately lands on the first line.
   */
  const [amendFocusItemId, setAmendFocusItemId] = useState<string | null>(null)

  const openAmend = (item?: TreatmentPlanItemDto) => {
    setAmendFocusItemId(item?.id ?? null)
    setAmendOpen(true)
  }
  /** « Tout modifier » — the full form. Routed through the inline editor's discard guard (see `confirmDiscard`). */
  const requestFullEdit = (item?: TreatmentPlanItemDto) => confirmDiscard(() => openAmend(item))
  const [reviseOpen, setReviseOpen] = useState(false)
  /** « Arrêter le traitement » — see the button's note. */
  const [stopOpen, setStopOpen] = useState(false)
  const [stopping, setStopping] = useState(false)
  // G3 — how the money is given back when « Arrêter » has nothing to keep but collected cash.
  const [stopRefundMethod, setStopRefundMethod] = useState<RefundMethod>("Cash")
  const { withRefund, refundDialog } = usePlanRefundConfirm()
  /** The act whose « réalisé » state is being corrected (AC-P2.11); null = dialog closed. */
  const [undoTarget, setUndoTarget] = useState<TreatmentPlanItemDto | null>(null)
  /**
   * What detaching that act will actually do — the séance released, the fiche behind it, and what stays
   * recorded. Derived so the confirmation, the toast and the server all describe one outcome.
   */
  const undoOutcome = useMemo(() => (undoTarget ? detachOutcome(undoTarget) : null), [undoTarget])
  /**
   * S6 — where this séance could belong instead. Derived from the plan and the act being released, so the
   * Select and the call cannot describe different destinations.
   */
  const relinkOptions = useMemo(
    () =>
      undoTarget && undoOutcome
        ? relinkTargets(plan, { itemId: undoTarget.id, stepId: undoOutcome.stepId })
        : [],
    [plan, undoTarget, undoOutcome],
  )
  /** The act whose protocol is being edited — same window as `canCorrectActs`, which the server enforces too. */
  const [stepsTarget, setStepsTarget] = useState<TreatmentPlanItemDto | null>(null)
  /** The séance « Modifier la séance » is about, or `add` for the strip's « + » (T2). */
  const [stepsFocus, setStepsFocus] = useState<{ stepId: string | null; add: boolean }>({ stepId: null, add: false })
  const openSteps = (item: TreatmentPlanItemDto, stepId: string | null) => {
    setStepsFocus({ stepId, add: stepId === null })
    setStepsTarget(item)
  }
  /** « Déplacer » — the booked appointment, opened here in the agenda's own edit dialog (T2). */
  const [movingAppointment, setMovingAppointment] = useState<AppointmentDto | null>(null)
  const openMoveAppointment = async (appointmentId: string) => {
    setBusy(true)
    try {
      setMovingAppointment(await appointmentsApi.get(appointmentId))
    } catch (err) {
      showErrorToast(err, "Le rendez-vous n'a pas pu être chargé.")
    } finally {
      setBusy(false)
    }
  }

  /*
   * Only needed to resolve an act's procedure when booking it (below). A failure still degrades to the free-text
   * behaviour rather than blocking the workspace — but it is **recorded** now instead of written back as `[]`.
   *
   * With no catalogue, `resolveProcedureTypeId` returns `undefined` for every act, so « Planifier » books a visit
   * with no procédure: no colour on the agenda, no duration, and no act proposal in the fiche de soins. That is
   * indistinguishable from a devis whose acts are all hand-typed, which is a legitimate state — hence the notice.
   */
  /*
   * The praticien picker's options. Loaded with the workspace rather than on opening the dialog: the header
   * prints `plan.doctorName`, which the server resolves, so the list is only needed to CHANGE it — but a
   * Select that populates after it opens shows « Aucun médecin » for a beat and reads as an empty cabinet.
   */
  const { allDoctors, isLoading: loadingDoctors } = useDoctors()

  const loadCatalog = useCallback(async () => {
    try {
      setProcedureTypes((await procedureTypesApi.list(false)) || [])
      setCatalogFailed(false)
    } catch {
      setCatalogFailed(true)
    }
  }, [])

  useEffect(() => {
    void loadCatalog()
  }, [loadCatalog])

  /*
   * The patient lookup behind « Changer de patient ». ⚠️ Server-side (`searchTerm`), never a filter over a page
   * already cut: a clinic with three hundred patients would search the twenty-five it happened to fetch and
   * report « aucun résultat » about somebody who is on page 2. Debounced, and a failed read leaves the list
   * null so « aucun résultat » is never claimed out of a network error.
   */
  useEffect(() => {
    if (!reassignOpen) return
    const term = patientQuery.trim()
    if (term.length < 2) {
      setPatientResults(null)
      return
    }
    let cancelled = false
    const timer = setTimeout(async () => {
      try {
        const page = await patientsApi.listPaged({ page: 1, pageSize: 10, searchTerm: term })
        if (!cancelled) setPatientResults(page.items)
      } catch {
        if (!cancelled) setPatientResults(null)
      }
    }, 250)
    return () => {
      cancelled = true
      clearTimeout(timer)
    }
  }, [reassignOpen, patientQuery])

  /**
   * The procedure an act stands for, so booking it produces a real `procedureTypeId` (colour, default
   * duration, and the act proposal in the dental-record modal) instead of just a name in the notes.
   *
   * Prefers the act's stored `procedureTypeId`. Falls back to matching `designationFr` against the catalog by
   * name, which works for **acts created before that column existed**: the plan editor used to snapshot a
   * « Mes actes » pick as a free-text line whose designation is `pt.name` verbatim, so the name is a reliable
   * key for those rows. Lines from the CNAM catalogue, typed by hand, or renamed after picking match neither
   * way and keep the previous free-text behaviour.
   */
  const resolveProcedureTypeId = useCallback(
    (item: TreatmentPlanItemDto): string | undefined => {
      const stored = item.procedureTypeId
      // Still verified against the loaded catalog — a procedure retired since the devis was written must not
      // preselect an option that no longer exists (the link is a soft reference, with no FK to guarantee it).
      if (stored && procedureTypes.some((p) => p.id === stored)) return stored

      const designation = item.designationFr?.trim().toLowerCase()
      if (!designation) return undefined
      const matches = procedureTypes.filter((p) => p.name.trim().toLowerCase() === designation)
      // Ambiguity means the catalog holds two procedures with the same name; guessing one would put the wrong
      // fee and colour on the appointment, so prefer no prefill.
      return matches.length === 1 ? matches[0].id : undefined
    },
    [procedureTypes],
  )

  /** One plan act in the shape the booking dialog takes. */
  // One builder, shared with the edit dialog's « Actes du devis » group — see `planItemToPreset`.
  const toPresetAct = useCallback(
    (item: TreatmentPlanItemDto): PresetPlanAct =>
      planItemToPreset(plan, item, resolveProcedureTypeId),
    [plan, resolveProcedureTypeId],
  )

  /** Progress in séances — the figure every surface prints, so the bar and the number cannot disagree. */
  const seances = useMemo(() => planSeanceProgress(plan), [plan])
  /** What may honestly be printed as « Reste » — null on a draft, and on a billed devis with no note figure. */
  const owed = useMemo(() => displayedOutstanding(plan), [plan])

  /**
   * The notes d'honoraires that already collect an act this devis holds at **0** — empty on every ordinary
   * plan, so nothing below renders and the card keeps the shape it has always had.
   *
   * <p>⚠️ **This is the whole of the reported « money gap », and the money was never wrong.** A continuation
   * prices the already-billed act 0 and deliberately leaves its note *unattached* — that is what stops
   * `PlanBillingRules.BilledPlanIds` dropping the plan and hiding the new work. The cost of that correctness is
   * that the plan had no structural knowledge of the note at all: « L'argent » could only report the devis'
   * own 10 / 0 / 10 on a treatment the patient had already paid 50 towards and still owed 40 on. The figures
   * added up on the patient's file; this screen simply never said what it was half of.</p>
   *
   * <p>⚠️ **Never `linkedInvoice*`, which means the opposite** — that a note *replaces* this devis, which is
   * why « Encaisser » disappears from its échéancier. Here both documents are live and both collect.</p>
   */
  const carried = useMemo(() => plan.carriedInvoices ?? [], [plan.carriedInvoices])
  /** Served, never derived — see `treatmentTotal`. Null/absent means « nothing carried », not « zero ». */
  const treatmentMoney =
    carried.length > 0 && plan.treatmentOutstanding != null
      ? {
          total: plan.treatmentTotal ?? 0,
          collected: plan.treatmentCollected ?? 0,
          outstanding: plan.treatmentOutstanding,
          /** Any carried note that also bills work outside this treatment — see the headline's label. */
          mixed: carried.some((c) => c.billsOtherWork),
        }
      : null

  const isDraft = plan.status === "Draft"
  const isActive = isPlanLive(plan.status)
  const isStopped = isPlanStopped(plan.status)
  const billed = isPlanBilled(plan)
  // Reordering is cosmetic, so it stays available on a Completed plan too — only a cancelled devis (and a
  // one-act plan, where there is nothing to move) hides the controls.
  const canReorder = !isPlanClosedToWrites(plan) && plan.items.length > 1
  /** ⚠️ The plan-level permissions are `plan-next-action.ts`'s now — the devis LIST reads the same four. */
  /**
   * Amending — the acts, their fees, the échéancier — is open on **everything except a draft and a cancelled
   * plan**, mirroring the server's widened `EnsureAmendable`.
   *
   * <p>⚠️ It used to be `isActive && !billed`, and both halves of that were wrong in practice. <b>Completed</b>
   * excluded the plan at the exact moment a plan completes — automatically, when the last act is marked réalisé
   * — so a mistyped fee became uncorrectable precisely when the dentist was most likely to spot it. <b>Billed</b>
   * excluded it because the note was raised from the devis total; that consequence is real, but the remedy was
   * to ask the dentist to reverse a numbered fiscal document in order to fix a plan. The consequence is now
   * <i>stated where the edit is made</i> (see the amend dialog's notice) instead of being pre-empted.</p>
   *
   * <p>⚠️ Every plan the <b>continuation</b> feature creates is born attached to a note, so under the old rule it
   * was born uncorrectable — a treatment still under way that could never be adjusted.</p>
   */
  // ⚠️ A **Draft is included**: an un-numbered treatment is followed work, not an unfinished form, and it
  // reaches this same workspace. Excluding it left no way to correct a total on the treatments a dentist
  // starts from the agenda — « the app should never refuse edits ». `EnsureAmendable` was widened to match.
  const canAmend = canAmendPlan(plan)

  /**
   * Whether this plan can be destroyed outright — mirrors `TreatmentPlan.CanBeDeleted`.
   *
   * <p>⚠️ <b>Both terms, and the second is the one that was missing everywhere.</b> A `Draft` is a followed
   * treatment now, so it routinely carries recorded séances and links to the fiches that evidence them, and
   * deleting one cascades its acts and step rows away — leaving the fiches attached to nothing, which is exactly
   * the wreckage « Arrêter le traitement » exists to avoid. The list screen offered « Supprimer le brouillon »
   * on the status alone, with a dialog reassuring that « aucun numéro n'a été consommé ».</p>
   */
  const canDelete = canDeletePlan(plan)

  /*
   * ⚠️ `canCancel` lived here and is gone with the button. « Annuler le devis » folded into « Arrêter le
   * traitement », whose branch is derived from the plan (`stopWouldCancel`) rather than chosen by the user —
   * so there is no longer a control to gate. `POST /{id}/cancel` still exists and is still `AdminOrDoctor`;
   * nothing in the browser calls it.
   */

  /**
   * Whether « Facturer le devis » is offered — every live status except a draft, minus a devis a note already
   * bills. Mirrors what `CreateInvoiceFromTreatmentPlanCommand` actually permits.
   *
   * <p>⚠️ It read `isActive && !billed`, and the first half made the feature's own promise unreachable: the
   * plan auto-completes the instant its last step is recorded, so the button vanished at the exact moment the
   * treatment became billable. The `!billed` half is correct and stays — the « two notes for one devis » risk
   * is real — except where an amendment has grown the devis past what its note carries, which the server now
   * bills as a supplementary note.</p>
   */
  const canBill = canBillPlan(plan)

  /** The acts still part of this treatment — a parked one keeps its history and counts towards nothing. */
  const liveItems = useMemo(() => activeItems(plan), [plan])
  const withdrawnItems = useMemo(() => plan.items.filter(isItemWithdrawn), [plan.items])

  /**
   * The acts « Arrêter le traitement » would park — those with **no delivered work**, which is the only question
   * a drop may ask.
   *
   * <p>⚠️ <b>It used to filter on `planItemState(i) === "to-schedule"`, and that answers a different question.</b>
   * That function keys on an act's *next step*, deliberately — a bridge with two of three séances done carries
   * an appointment that already happened, so reading the act would report « À enregistrer » for ever. But it
   * therefore returns `"to-schedule"` for a two-thirds-delivered bridge whose last séance is unbooked, so this
   * list offered it for deletion under a dialog promising « ce qui a déjà été fait est conservé » — and on a
   * purpose-built devis it took 1 000 DT, three step rows and the links to two real fiches with it.</p>
   *
   * <p>Now only a display: {@link hasDeliveredWork} is the same test the server's own `StopTreatment` applies,
   * so the list the dialog shows and the acts the server parks cannot disagree.</p>
   */
  const stoppableItems = useMemo(
    () => liveItems.filter((i) => !hasDeliveredWork(i)),
    [liveItems],
  )
  /** What survives the stop — named in the dialog, so nothing is parked silently. */
  const keptItems = useMemo(() => liveItems.filter(hasDeliveredWork), [liveItems])
  /** The price the treatment keeps after a stop — the dialog's last line. */
  const keptTotal = keptItems.reduce((sum, i) => sum + itemNetCost(i), 0)

  /**
   * May THIS act be put aside? Mirrors `TreatmentPlan.WithdrawItem`'s two refusals, so the control is absent
   * rather than offered-then-refused: an act with delivered work stays (that is the correct keep the whole
   * finding turns on), and the last active act of a devis cannot go — a devis with nothing left is what
   * « Arrêter le traitement » is for.
   */
  /**
   * May THIS act carry a remise (S2)? Mirrors `TreatmentPlanItem.SetDiscount`'s own refusal: an act another
   * document bills sits at 0 on the devis by rule, so a remise here would take the line negative and claim a
   * reduction on money this document does not hold — the remise belongs on the note that carries the fee.
   */
  const canDiscountItem = useCallback(
    (item: TreatmentPlanItemDto) => canAmend && !item.billedOnInvoiceId && !isItemWithdrawn(item),
    [canAmend],
  )

  const canWithdrawItem = useCallback(
    (item: TreatmentPlanItemDto) =>
      canAmend && !hasDeliveredWork(item) && !isItemWithdrawn(item) && liveItems.length > 1,
    [canAmend, liveItems.length],
  )

  /**
   * Would this stop be a **cancellation** rather than a stop? Mirrors `TreatmentPlan.StopWouldCancel`.
   *
   * <p>A numbered devis with nothing delivered has nothing to keep: the number is spent and the document may be
   * in the patient's hands, so the outcome is an annulation carrying a motif. An un-numbered treatment answers
   * false — no document, nothing to explain, and stopping is the ordinary outcome for one that never started.</p>
   *
   * <p>⚠️ It decides only the <b>wording and the motif field</b>. The server re-derives it from the aggregate and
   * is the authority; a client that got this wrong would show the other dialog and then be refused, never write
   * the wrong thing.</p>
   */
  const stopWouldCancel = stopWouldCancelPlan(plan)

  /**
   * The server's THIRD arm: money taken, nothing delivered, so neither branch above can run and the stop is
   * refused until an avoir gives the cash back. Named here rather than discovered by pressing — see
   * `stopNeedsRefundFirst`.
   */
  const stopNeedsRefund = stopNeedsRefundFirst(plan)

  /** Acts not yet réalisés. Read by the primary action, which withholds « Facturer » while any remain. */
  const actsRemaining = Math.max(plan.itemsTotal - plan.itemsDone, 0)

  /**
   * The act whose séances the header draws — the plan's ONLY active act, when it has séances. With several acts
   * each row keeps its own strip and the header states a count in words instead.
   */
  const headerStripItem = useMemo(
    () => (liveItems.length === 1 && (liveItems[0].steps?.length ?? 0) > 0 ? liveItems[0] : null),
    [liveItems],
  )
  /**
   * Correcting a réalisé act is *not* gated on `isActive`: marking the last act done auto-completes the plan,
   * so requiring an active plan would lock out the exact mistake the correction exists for. The server's
   * `EnsureCorrectable` admits Accepted / InProgress / **Completed** — mirrored here.
   */
  // A Draft is clinically live now — its acts book, record and detach like any other. See `canAmend`.
  const canCorrectActs = !isPlanClosedToWrites(plan)
  /**
   * Whether an échéance of this plan can still take money (J1).
   *
   * <p>Payable on a `Completed` plan too — « Terminé » means every act was carried out, not that the patient has
   * paid. Draft and Cancelled refuse. **And a billed plan refuses**: once a devis has been bridged to a note
   * d'honoraires, that invoice represents it, its collected payments were carried across at issue, and both
   * installment money reads exclude the plan from then on. So cash taken here after the bridge reduced the
   * patient's balance and reached **no** money read — not la caisse, not the dashboard, not « Encaissé ». The
   * server now refuses it outright; this is the same rule, so the button is not offered in the first place.</p>
   *
   * <p>⚠️ Derived **once** and read by both the card list and the table. The condition used to be written inline
   * in each, which is exactly how a guard lands on one surface and not the other — and the phone is the surface
   * that would have kept the button.</p>
   */
  const canCollectInstallments = !isDraft && !isPlanClosedToWrites(plan) && !billed

  /**
   * <b>Why</b> « Encaisser » is not offered — one short fact and, when there is one, where to collect instead.
   * Derived from the same terms as the rule above so it cannot name the wrong one; null when collectable.
   * (A billed devis: cash taken here would reach neither la caisse nor les recettes — the note collects.)
   */
  const noCollectFact: React.ReactNode = canCollectInstallments ? null : isDraft ? (
    <><b className="text-foreground">Pas de devis</b> · l&apos;encaissement se fait sur la fiche de soins</>
  ) : plan.status === "Cancelled" ? (
    <><b className="text-foreground">Devis annulé</b> · rien à encaisser</>
  ) : isPlanWrittenOff(plan.status) ? (
    <><b className="text-foreground">Non réclamé</b> · rien à encaisser</>
  ) : (
    <>
      <b className="text-foreground">
        Facturé sur la note{plan.linkedInvoiceNumber ? ` n° ${plan.linkedInvoiceNumber}` : " d'honoraires"}
      </b>
      {plan.linkedInvoiceId && (
        <>
          {" · "}
          <Link
            href={patientOutstandingHref(plan.patientId, plan.linkedInvoiceId)}
            className="font-medium text-primary underline underline-offset-2 coarse:inline-flex coarse:min-h-11 coarse:items-center"
          >
            Encaisser sur la note
          </Link>
        </>
      )}
    </>
  )
  /** « Échéancier (N) » — folded by default: the three figures and « Encaisser » answer the money question. */
  const [scheduleOpen, setScheduleOpen] = useState(false)
  /**
   * Whether the fold is offered at all — only when opening it shows something: rows, or « Modifier l'échéancier »
   * on a numbered devis. An un-numbered treatment with no row opened onto nothing (« Échéancier (0) »).
   */
  const scheduleFold = plan.installments.length > 0 || (!isDraft && plan.number != null && canAmend)
  /** The one « Encaisser » — offered where the rows can take money and something is owed on the devis itself. */
  const showSettle = Boolean(canCollectInstallments && owed && !owed.isBilled && owed.amount > 0.0005)
  /** Otherwise the reason, as one visible fact; a billed devis always names its note here (once). */
  const showNoCollectFact = !showSettle && noCollectFact !== null && (plan.installments.length > 0 || billed)

  /**
   * The acts that can be booked right now. Same état the row's own « Planifier » keys off, so the tick boxes and
   * the per-row button can never disagree about what is bookable.
   */
  /*
   * ⚠️ `schedulablePlanItems(plan)`, never `plan.items` — it filters the PARKED acts out, which the inline
   * copy here did not. Latent while « Arrêter » was the only parking gesture (`Reopen` restored them all at
   * once, so no live plan could carry one); real the moment per-act « Mettre de côté » ships, which is this
   * same change. A parked act ticked into « Planifier ensemble » books a visit for work the patient is not
   * coming back for, and the server then refuses to record anything against it.
   */
  const schedulableItems = useMemo(
    () => schedulablePlanItems(plan).filter((i) => planItemState(i) === "to-schedule"),
    [plan],
  )
  const canGroup = schedulableItems.length > 1

  /**
   * How many acts share each booked appointment — what turns « ces deux-là ensemble » into something the plan can
   * *show* afterwards. Derived from the read-back the API already supplies, so it needs no extra field: two acts
   * pointing at one appointment **are** one séance.
   */
  const actsPerAppointment = useMemo(() => {
    const counts = new Map<string, number>()
    for (const item of plan.items) {
      if (item.scheduledAppointmentId) {
        counts.set(item.scheduledAppointmentId, (counts.get(item.scheduledAppointmentId) ?? 0) + 1)
      }
    }
    return counts
  }, [plan.items])

  /**
   * The « État » column (table header + cells, card status) — dropped on a treatment nobody runs when no act
   * states anything there (« À planifier » is withheld on it). Kept for « Fait » / « Mis de côté » / a grouped séance.
   */
  const showStateColumn = plan.items.some(
    (item) =>
      planActStateShown(item, isActive) ||
      (item.scheduledAppointmentId != null && (actsPerAppointment.get(item.scheduledAppointmentId) ?? 1) > 1),
  )

  /**
   * The same acts, grouped for the card list below `md:` — **Exception 2**.
   *
   * A card is read on its own, so the row badge « séance de N actes » has nowhere to point: repeated on four
   * cards it reads as four séances, which is the very confusion the badge exists to remove. Grouped acts become
   * a **section header** over the cards that share the appointment, and the badge is dropped from the card.
   *
   * ⚠️ A séance's acts are pulled together at their **first** position in the plan rather than left where they
   * fall. Plan order is otherwise preserved (`plan-act-pips` explains why it is meaningful), but a séance split
   * across the order would otherwise print its header twice, each time claiming a count larger than the cards
   * under it — a header that lies about what it heads.
   */
  const actGroups = useMemo(() => {
    type GroupedAct = { item: TreatmentPlanItemDto; index: number }
    const groups: { key: string; appointmentId: string | null; acts: GroupedAct[] }[] = []
    const groupOfAppointment = new Map<string, number>()

    plan.items.forEach((item, index) => {
      const apptId = item.scheduledAppointmentId
      const shared = apptId ? (actsPerAppointment.get(apptId) ?? 1) > 1 : false

      if (apptId && shared) {
        const existing = groupOfAppointment.get(apptId)
        if (existing !== undefined) {
          groups[existing].acts.push({ item, index })
          return
        }
        groupOfAppointment.set(apptId, groups.length)
        groups.push({ key: `seance-${apptId}`, appointmentId: apptId, acts: [{ item, index }] })
        return
      }

      // Consecutive standalone acts share one headerless list, so the rhythm of the page is not broken by a
      // heading over every single card.
      const last = groups[groups.length - 1]
      if (last && last.appointmentId === null) {
        last.acts.push({ item, index })
        return
      }
      groups.push({ key: `acte-${item.id}`, appointmentId: null, acts: [{ item, index }] })
    })

    return groups
  }, [plan.items, actsPerAppointment])

  // Acts that leave the « À planifier » state (a peer books one, a fiche is saved) must not stay ticked, or
  // « Planifier ensemble » would silently re-book something already scheduled.
  useEffect(() => {
    const bookable = new Set(schedulableItems.map((i) => i.id))
    setSelectedActIds((prev) => {
      const kept = prev.filter((id) => bookable.has(id))
      return kept.length === prev.length ? prev : kept
    })
  }, [schedulableItems])

  const toggleActSelection = (itemId: string) =>
    setSelectedActIds((prev) => (prev.includes(itemId) ? prev.filter((id) => id !== itemId) : [...prev, itemId]))

  /** The ticked acts, in the plan's own clinical order rather than the order they were clicked. */
  const selectedItems = useMemo(
    () => schedulableItems.filter((i) => selectedActIds.includes(i.id)),
    [schedulableItems, selectedActIds],
  )

  /** Queue one appointment per group, then clear the ticks — the dialog walks the queue. */
  const startBooking = (groups: TreatmentPlanItemDto[][]) => {
    const queued = groups.filter((g) => g.length > 0).map((g) => g.map(toPresetAct))
    if (queued.length === 0) return
    setBookingQueue(queued)
    setSelectedActIds([])
  }

  /** « Planifier » on one séance of the strip — the row's own booking, that séance ticked instead of the next. */
  const startBookingStep = (item: TreatmentPlanItemDto, stepId: string) => {
    setBookingQueue([[{ ...toPresetAct(item), preselectedStepId: stepId }]])
    setSelectedActIds([])
  }

  /** The acts a séance can still be booked for — `schedulablePlanItems`, the booking dialog's own gate (N28). */
  const schedulableIds = useMemo(() => new Set(schedulablePlanItems(plan).map((i) => i.id)), [plan])

  /**
   * What each séance of an act's strip may do — the same gates the row's controls had: correcting needs a devis
   * open to writes, and a parked act is read-only until « Remettre au devis ».
   */
  const seanceHandlers = (item: TreatmentPlanItemDto): PlanActSeanceHandlers => {
    const withdrawn = isItemWithdrawn(item)
    return {
      onPlanStep: schedulableIds.has(item.id) ? startBookingStep : undefined,
      onMoveAppointment: (appointmentId) => void openMoveAppointment(appointmentId),
      onEditStep: canCorrectActs && !withdrawn ? guarded(openSteps) : undefined,
      onUndo: canCorrectActs && !withdrawn ? guarded(setUndoTarget) : undefined,
      navigate: (href) => guardedPush(href),
    }
  }

  /*
   * ── T3 — price and remise edited in the row ────────────────────────────────────────────────────────────────
   *
   * Edits accumulate (a fee typed, a remise, an act added from the catalogue) and ONE « Enregistrer » sends them:
   * the prices and additions as ONE amendment built by `buildAmendRequest` — the full form's own builder, so every
   * act goes with its id, every step with its id and `minDaysAfterPrevious`, the version the edit was read at —
   * then each remise through its own command (`setItemDiscount`: the amend payload has no remise, S2), each with
   * the version the previous write returned. Read-only wherever the full form is refused (`canAmendPlan`).
   */
  const [costDrafts, setCostDrafts] = useState<Record<string, PlanActCostDraft>>({})
  const [addedLines, setAddedLines] = useState<PlanLineRow[]>([])
  const [savingInline, setSavingInline] = useState(false)
  const inlineConflict = useConflict()
  /**
   * The version the first edit was made against. ⚠️ Captured then, never read live at save: the page re-reads the
   * plan on every realtime event, so the live version would let these edits overwrite a colleague with a 200 (F3).
   */
  const draftBaseVersion = useRef<number | null>(null)
  /** What « Abandonner » will do next — a navigation, another action — while edits are pending. */
  const [discardRequest, setDiscardRequest] = useState<(() => void) | null>(null)
  const [addActOpen, setAddActOpen] = useState(false)

  const setCostDraft = (item: TreatmentPlanItemDto, draft: PlanActCostDraft) => {
    if (draftBaseVersion.current === null) draftBaseVersion.current = plan.version
    setCostDrafts((prev) => ({ ...prev, [item.id]: draft }))
  }

  /** Acts whose typed fee differs from the stored one — an unparseable entry counts, so the save can refuse it. */
  const priceChanges = liveItems.filter((item) => {
    const typed = costDrafts[item.id]?.cost
    if (typed === undefined) return false
    const value = parseAmountInput(typed)
    return !Number.isFinite(value) || Math.abs(value - item.plannedCost) > 0.0005
  })
  /** Acts whose remise changed. An emptied field is « 0 » — how a remise is taken off. */
  const discountValue = (raw: string) => (raw.trim() === "" ? 0 : parseAmountInput(raw))
  const discountChanges = liveItems.filter((item) => {
    const typed = costDrafts[item.id]?.discount
    if (typed === undefined) return false
    const value = discountValue(typed)
    return !Number.isFinite(value) || Math.abs(value - itemDiscount(item)) > 0.0005
  })
  const inlineChangeCount = priceChanges.length + discountChanges.length + addedLines.length
  const inlineDirty = inlineChangeCount > 0

  useEffect(() => {
    if (!inlineDirty) draftBaseVersion.current = null
  }, [inlineDirty])

  const discardInline = () => {
    setCostDrafts({})
    setAddedLines([])
    inlineConflict.reset()
    draftBaseVersion.current = null
  }

  /** The discard guard: with edits pending, leaving or starting another write asks first. */
  const confirmDiscard = (then: () => void) => {
    if (!inlineDirty) {
      then()
      return
    }
    setDiscardRequest(() => then)
  }
  /** Wraps a handler so it goes through {@link confirmDiscard}. */
  const guarded =
    <A extends unknown[]>(fn: (...args: A) => void) =>
    (...args: A) =>
      confirmDiscard(() => fn(...args))
  /** A navigation that leaves the page — Voir la fiche, Voir le RDV, Enregistrer la fiche. */
  const guardedPush = (href: string) => confirmDiscard(() => router.push(href))

  // The other channels a page has: closing the tab, and any in-app link (the patient's name, the rail).
  useEffect(() => {
    if (!inlineDirty) return
    const beforeUnload = (event: BeforeUnloadEvent) => {
      event.preventDefault()
      event.returnValue = ""
    }
    const onLink = (event: MouseEvent) => {
      if (event.defaultPrevented || event.button !== 0) return
      if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return
      const anchor = (event.target as HTMLElement | null)?.closest?.("a[href]") as HTMLAnchorElement | null
      if (!anchor || anchor.target === "_blank" || anchor.hasAttribute("download")) return
      const url = new URL(anchor.href, window.location.href)
      if (url.origin !== window.location.origin) return
      if (url.pathname === window.location.pathname && url.search === window.location.search) return
      event.preventDefault()
      event.stopPropagation()
      setDiscardRequest(() => () => router.push(url.pathname + url.search + url.hash))
    }
    window.addEventListener("beforeunload", beforeUnload)
    document.addEventListener("click", onLink, true)
    return () => {
      window.removeEventListener("beforeunload", beforeUnload)
      document.removeEventListener("click", onLink, true)
    }
  }, [inlineDirty, router])

  /*
   * The browser's Back button never reaches a link or a handler, so while edits are pending the page owns one
   * history entry (`useDirtyGuard`'s technique) and a Back asks first. Confirming leaves past both entries.
   */
  const leavingByBack = useRef(false)
  useEffect(() => {
    if (!inlineDirty) return
    const marker = { planEditsGuard: true }
    let pushed = false
    const timer = window.setTimeout(() => {
      window.history.pushState(marker, "")
      pushed = true
    }, 0)
    const onPop = () => {
      if (window.history.state?.planEditsGuard) return
      window.history.pushState(marker, "")
      setDiscardRequest(() => () => {
        leavingByBack.current = true
        window.history.go(-2)
      })
    }
    window.addEventListener("popstate", onPop)
    return () => {
      window.clearTimeout(timer)
      window.removeEventListener("popstate", onPop)
      if (leavingByBack.current) {
        leavingByBack.current = false
        return
      }
      if (pushed && window.history.state?.planEditsGuard) window.history.back()
    }
  }, [inlineDirty])

  /** The « Prix » cell of an act — the in-place editor, read-only where the form would be refused. */
  const costCell = (item: TreatmentPlanItemDto): React.ReactNode => (
    <PlanActCostEditor
      item={item}
      canPrice={canAmend}
      canDiscount={canDiscountItem(item)}
      draft={costDrafts[item.id]}
      onChange={(draft) => setCostDraft(item, draft)}
      disabled={savingInline || busy}
    />
  )

  /** « + Ajouter un acte » — the catalogue act, priced and cut into séances exactly as the form's own pick. */
  const addActFromCatalogue = (pt: ProcedureTypeDto) => {
    if (draftBaseVersion.current === null) draftBaseVersion.current = plan.version
    setAddedLines((prev) => [...prev, lineFromProcedure(pt)])
    setAddActOpen(false)
  }
  const updateAddedLine = (index: number, patch: Partial<PlanLineRow>) =>
    setAddedLines((prev) => prev.map((line, i) => (i === index ? { ...line, ...patch } : line)))
  /** Teeth changed on an added act whose fee nobody typed: the fee follows the tooth count (G4). */
  const updateAddedTeeth = (index: number, teeth: number[]) =>
    setAddedLines((prev) =>
      prev.map((line, i) => {
        if (i !== index) return line
        const next = { ...line, toothNumbers: teeth }
        const pt = procedureTypes.find((p) => p.id === line.procedureTypeId)
        return { ...next, plannedCost: repricedCost(next, pt) }
      }),
    )
  const procedureGroups = useMemo(() => groupProceduresByCategory(procedureTypes), [procedureTypes])

  const savedToast = (n: number) => (n > 1 ? `${n} modifications enregistrées` : "Modification enregistrée")

  const saveInline = async () => {
    inlineConflict.clearMessage()

    const discounts: { item: TreatmentPlanItemDto; value: number }[] = []
    for (const item of discountChanges) {
      const value = discountValue(costDrafts[item.id]!.discount!)
      if (!Number.isFinite(value) || value < 0) {
        inlineConflict.setError("La remise doit être un montant positif, ou 0 pour l'annuler.")
        return
      }
      discounts.push({ item, value })
    }

    let version = draftBaseVersion.current ?? plan.version
    const lineEdits = priceChanges.length + addedLines.length
    let amendment: Omit<AmendTreatmentPlanRequest, "refundMethod"> | null = null
    if (lineEdits > 0) {
      const lines = [
        ...planLinesFromPlan(plan).map((line) => {
          const typed = line.id ? costDrafts[line.id]?.cost : undefined
          return typed !== undefined ? { ...line, plannedCost: typed, costTouched: true } : line
        }),
        ...addedLines,
      ]
      const built = buildAmendRequest(
        plan,
        {
          lines,
          installments: planInstallmentRows(plan),
          installmentsTouched: false,
          title: plan.title,
          notes: plan.notes ?? "",
          inPlace: true,
        },
        version,
      )
      if (!built.ok) {
        inlineConflict.setError(built.error)
        return
      }
      amendment = built.value
    }

    setSavingInline(true)
    let saved = 0
    try {
      if (amendment) {
        const body = amendment
        const amended = await withRefund((refundMethod) => treatmentPlansApi.amend(plan.id, { ...body, refundMethod }))
        // « Retour » on « Rendre au patient ? »: nothing was saved, every edit stays on screen.
        if (amended === REFUND_DECLINED) return
        version = amended.version
        draftBaseVersion.current = version
        saved += lineEdits
        const savedIds = new Set(priceChanges.map((i) => i.id))
        setCostDrafts((prev) => {
          const next: Record<string, PlanActCostDraft> = {}
          for (const [id, draft] of Object.entries(prev)) {
            const kept: PlanActCostDraft = savedIds.has(id) ? { discount: draft.discount } : draft
            if (kept.cost !== undefined || kept.discount !== undefined) next[id] = kept
          }
          return next
        })
        setAddedLines([])
      }
      for (const { item, value } of discounts) {
        const discounted = await withRefund((refundMethod) =>
          treatmentPlansApi.setItemDiscount(plan.id, item.id, value, version, refundMethod),
        )
        if (discounted === REFUND_DECLINED) {
          if (saved > 0) {
            toast.success(savedToast(saved))
            onChanged()
          }
          return
        }
        version = discounted.version
        draftBaseVersion.current = version
        saved += 1
        setCostDrafts((prev) => {
          const { [item.id]: draft, ...rest } = prev
          return draft?.cost !== undefined ? { ...rest, [item.id]: { cost: draft.cost } } : rest
        })
      }
      toast.success(savedToast(saved))
      onChanged()
    } catch (err) {
      // Every edit not yet saved stays on screen; a 409 offers « Recharger » (useConflict), never a dead repeat.
      // What DID land is said first, or the reader retypes a price that is already saved.
      if (saved > 0) toast.success(savedToast(saved))
      inlineConflict.capture(err, "Échec de l'enregistrement.")
      if (saved > 0) onChanged()
    } finally {
      setSavingInline(false)
    }
  }

  /** Close the booking dialog, then refetch so états update. The flag marks this close as a successful create
   * rather than the user backing out — the dialog calls `onSuccess` and *then* `onOpenChange(false)`. */
  const finishCurrentBooking = () => {
    justAdvancedRef.current = true
    const rest = bookingQueue.slice(1)
    setBookingQueue([])
    if (rest.length > 0) setTimeout(() => setBookingQueue(rest), 0)
    onChanged()
  }

  /*
   * ⚠️ `showErrorToast`, not a hand-rolled `toast.error(err instanceof ApiError ? … )`.
   *
   * The hand-rolled form (which is what all four of these used) inherits the *global* 4-second duration meant
   * for success confirmations, never offers « Réessayer » on a transport failure, and silently drops the
   * message of anything that is not an `ApiError` — a plain `Error` from `downloadBlob` fell through to the
   * generic French sentence. `lib/errors.ts` is the single formatting point and supplies all three.
   */
  const run = async (
    action: () => Promise<unknown>,
    success: string,
    failure: string,
    /**
     * An optional control on the success toast — « Ouvrir la fiche » after a detach. Here rather than at the
     * one call site because the toast is raised here, and a second `toast.success` beside this helper is how
     * two mutations come to report success two different ways.
     */
    successAction?: { label: string; onClick: () => void },
  ) => {
    setBusy(true)
    try {
      // « Retour » on « Rendre au patient ? » saved nothing: no toast, no reload, the dialog stays open.
      if ((await action()) === REFUND_DECLINED) return "declined" as const
      toast.success(success, { action: successAction })
      onChanged()
      return "ok" as const
    } catch (err) {
      showErrorToast(err, failure)
      return "failed" as const
    } finally {
      setBusy(false)
    }
  }

  const handleDownloadDevis = async () => {
    setBusy(true)
    try {
      const blob = await treatmentPlansApi.downloadDevisPdf(plan.id)
      downloadBlob(blob, `devis-${plan.number ?? plan.id}.pdf`)
    } catch (err) {
      showErrorToast(err, "Échec du téléchargement du devis.", handleDownloadDevis)
    } finally {
      setBusy(false)
    }
  }

  /*
   * The plan-level confirmations: the title is the question with its figure, the body 2–3 bold consequences.
   * (`confirmAccept` and `confirmComplete` are gone for good — see the notes on « Créer le devis » and « Arrêter ».)
   */
  /**
   * « Créer le devis » — take the number. The one confirmed step of a followed treatment: a number is gapless and
   * can only be released by a cancellation carrying a motif.
   */
  const confirmIssueDevis = () =>
    setConfirmAction({
      title: plan.totalPlanned > 0 ? `Créer le devis de ${formatDT(plan.totalPlanned)} ?` : "Créer le devis ?",
      description: (
        <Consequences
          items={[
            <>Un <b className="text-foreground">numéro de devis</b> est attribué</>,
            <>Les échéances sont <b className="text-foreground">à payer</b></>,
            <><b className="text-foreground">Définitif</b> : une erreur s&apos;annule avec un motif</>,
          ]}
        />
      ),
      confirmLabel: "Créer le devis",
      onConfirm: async () => {
        await run(
          () => treatmentPlansApi.issueDevis(plan.id, plan.version),
          "Devis créé",
          "Échec de la création du devis.",
        )
      },
    })

  const confirmBill = () =>
    setConfirmAction({
      title: "Facturer ce devis ?",
      description: (
        <Consequences
          items={[
            <>Une <b className="text-foreground">note d&apos;honoraires en brouillon</b> est créée</>,
            <>Vous allez sur <b className="text-foreground">Factures</b></>,
            /* The carry-over happens at ISSUE, not at draft creation — said before the navigation, not after. */
            plan.amountPaid > 0 && (
              <>
                <b className="text-foreground">{formatDT(plan.amountPaid)}</b> déjà payés passent sur la note à
                son émission
              </>
            ),
          ]}
        />
      ),
      confirmLabel: "Facturer",
      onConfirm: async () => {
        await run(
          async () => {
            await invoicesApi.createFromPlan(plan.id)
            router.push("/factures")
          },
          plan.amountPaid > 0
            ? `Facture brouillon créée — ${formatDT(plan.amountPaid)} déjà payés passeront sur la note à l'émission`
            : "Facture brouillon créée depuis le devis",
          "Échec de la facturation du devis.",
        )
      },
    })

  /**
   * Stop the treatment: park the acts with no delivered work, keep the rest, re-spread the échéancier onto the
   * kept total, and close the devis — **one server call**.
   *
   * <p>⚠️ <b>It used to be two calls with the whole decision made here, and every part of that was a defect.</b>
   * The client chose which acts to drop (on the wrong question — see {@link stoppableItems}), built the new
   * schedule itself, then sent `amend` followed by `complete`. So: the removals committed and the clôture threw
   * after them, leaving a half-stopped plan with « Arrêter » no longer on screen and no way to retry; a devis
   * with nothing collected produced a zero-amount row the aggregate refuses, answering « Le montant de
   * l'échéance doit être supérieur à 0. (Parameter 'amount') » in a product whose refusals are otherwise
   * French, over a screen with no way out; the arithmetic used raw floats against a server that checks the
   * échéancier total with exact equality; and the new due date came from `new Date()`, the browser's clock,
   * which for the first hour of every Tunisian day dates it to yesterday and makes it « En retard » at birth.
   * All four are gone with the client-side version of them.</p>
   */
  const stopTreatment = async () => {
    setStopping(true)
    try {
      const parked = stoppableItems.length
      // The motif travels only on the branch that needs one; the server ignores it otherwise and re-derives
      // the branch itself, so a client that disagreed would be refused rather than write the wrong outcome.
      // G3: on the refund branch the method was chosen in this dialog; elsewhere the server may still ask.
      const result = await withRefund((refundMethod) =>
        treatmentPlansApi.stopTreatment(
          plan.id,
          plan.version,
          stopWouldCancel ? cancelReason.trim() : undefined,
          stopNeedsRefund ? stopRefundMethod : refundMethod,
        ),
      )
      if (result === REFUND_DECLINED) return
      toast.success(
        stopWouldCancel
          ? "Devis annulé — le numéro est conservé avec son motif."
          : parked > 0
            ? `Traitement arrêté — ${parked} acte${parked > 1 ? "s" : ""} mis de côté`
            : "Traitement arrêté.",
      )
      setStopOpen(false)
      setCancelReason("")
      onChanged()
    } catch (err) {
      showErrorToast(err)
    } finally {
      setStopping(false)
    }
  }

  /**
   * « Reprendre le traitement » — the patient came back, which is what patients do.
   *
   * <p>⚠️ A stopped plan was a terminal state: `Completed` withdraws « Arrêter », « Terminer », « Facturer » and
   * « Annuler » alike, and the parked acts were *deleted*, so the only recovery was to re-type them as new ids
   * — orphaning the fiches, re-quoting the act at the catalogue default rather than the fee it was quoted at,
   * and walking the header back to « 0 / N » on a treatment several séances in.</p>
   */

  /**
   * Destroy a followed treatment nothing has been recorded on — the workspace's first caller of
   * `DELETE /api/treatment-plans/{id}`, which had shipped with none anywhere but the list screen.
   *
   * <p>It replaces « Annuler » on a Draft, where that button could only ever be refused. See `canDelete`.</p>
   */
  const handleDelete = () =>
    run(
      async () => {
        await treatmentPlansApi.remove(plan.id)
        // Nothing is left to reload — go back to the list rather than refetching a plan that is gone.
        router.push("/treatment-plans")
      },
      "Traitement supprimé",
      "Échec de la suppression du traitement.",
    )

  /**
   * « Rétablir ce devis annulé » (C3).
   *
   * <p>⚠️ <b>`Cancelled` was an absorbing state: nothing in the product could leave it.</b> Every guard excludes
   * it and `CanBeDeleted` is Draft-only, so a devis cancelled by mistake left this workspace with « Devis PDF »
   * as its only surviving control. The motif is required and <b>appended</b> to the original rather than
   * written over it — the first one explains a document that may be in a patient's hands, and un-cancelling
   * does not make it untrue.</p>
   */
  const uncancelPlan = async () => {
    await run(
      () => treatmentPlansApi.uncancel(plan.id, uncancelReason.trim(), plan.version),
      "Devis rétabli",
      "Échec du rétablissement du devis.",
    )
    setUncancelOpen(false)
    setUncancelReason("")
  }

  /** « Annuler le devis » — its own door; see `canCancelPlan` for when it is offered at all. */
  const cancelPlan = async () => {
    await run(
      () => treatmentPlansApi.cancel(plan.id, cancelReason.trim(), plan.version),
      "Devis annulé — le numéro est conservé avec son motif.",
      "Échec de l'annulation du devis.",
    )
    setCancelPlanOpen(false)
    setCancelReason("")
  }

  /** « Mettre cet acte de côté » (M1) — per-act, keeping its fiche links. */
  const withdrawItem = async (item: TreatmentPlanItemDto) => {
    const outcome = await run(
      () => withRefund((refundMethod) =>
        treatmentPlansApi.withdrawItem(plan.id, item.id, plan.version, refundMethod)),
      `${item.designationFr} mis de côté`,
      "Échec de la mise de côté de l'acte.",
    )
    if (outcome !== "declined") setWithdrawTarget(null)
  }

  /** « Remettre au devis » (M2) — the mirror, per act rather than all-or-nothing. */
  const restoreItem = async (item: TreatmentPlanItemDto) => {
    await run(
      () => treatmentPlansApi.restoreItem(plan.id, item.id, plan.version),
      `${item.designationFr} remis au devis.`,
      "Échec de la remise au devis.",
    )
    setRestoreTarget(null)
  }

  /**
   * « Détacher la note d'honoraires » (M3) — the remedy three server refusals name by hand and which nothing
   * in the browser could reach: the only callers of the detach were the cancel and the stop, so releasing a
   * note meant killing the treatment.
   */
  const detachNote = async () => {
    if (!plan.linkedInvoiceId) return
    await run(
      () => treatmentPlansApi.detachNote(plan.id, plan.linkedInvoiceId!, plan.version),
      "Note détachée",
      "Échec du détachement de la note.",
    )
    setDetachNoteOpen(false)
  }

  /**
   * « Passer la créance en perte » (S4) — the practice abandons what is still owed.
   *
   * <p>⚠️ <b>Not a cancellation.</b> The unpaid balance leaves every money read; the cash already collected,
   * its receipts and its days in la caisse are untouched — which is exactly why `cancel` is the wrong
   * instrument here (it is refused outright once any money has been taken, and it rewrites closed days).</p>
   */
  const writeOffPlan = async () => {
    await run(
      () => treatmentPlansApi.writeOff(plan.id, writeOffReason.trim(), plan.version),
      "Le reste n'est plus réclamé",
      "Échec de l'opération.",
    )
    setWriteOffOpen(false)
    setWriteOffReason("")
  }

  /** « Dupliquer ce devis » (S1) — a new un-numbered Draft, and the page follows it. */
  const duplicatePlan = async () => {
    setBusy(true)
    try {
      const copy = await treatmentPlansApi.duplicate(plan.id)
      toast.success("Traitement dupliqué")
      setDuplicateOpen(false)
      // Navigate rather than refetch: the copy is the thing the dentist is about to edit, and leaving them on
      // the original with a toast is how two devis get confused for each other.
      router.push(`/treatment-plans/${copy.id}`)
    } catch (err) {
      showErrorToast(err, "Échec de la duplication du devis.")
    } finally {
      setBusy(false)
    }
  }

  /** « Changer de patient » (M5) — re-file the devis under the person it was actually for. */
  const reassignPatient = async () => {
    if (!patientDraft) return
    await run(
      () => treatmentPlansApi.reassignPatient(plan.id, patientDraft.id, plan.version),
      `Devis rattaché à ${patientDraft.firstName} ${patientDraft.lastName}.`,
      "Échec du changement de patient.",
    )
    setReassignOpen(false)
  }

  /** « Praticien » (M6) — who the next note d'honoraires raised from this devis credits. */
  const saveDoctor = async () => {
    await run(
      () => treatmentPlansApi.setDoctor(plan.id, doctorDraft || null, plan.version),
      "Praticien mis à jour",
      "Échec du changement de praticien.",
    )
    setDoctorOpen(false)
  }

  const confirmReopen = () =>
    setConfirmAction({
      title: "Reprendre ce traitement ?",
      description: (
        <Consequences
          items={[
            <>Le traitement repasse <b className="text-foreground">En cours</b></>,
            withdrawnItems.length > 0 && (
              <>
                <b className="text-foreground">
                  {withdrawnItems.length} acte{withdrawnItems.length > 1 ? "s" : ""} mis de côté
                </b>{" "}
                {withdrawnItems.length > 1 ? "reviennent" : "revient"}
              </>
            ),
            isPlanWrittenOff(plan.status) &&
              ((plan.writeOffAmount ?? 0) > 0.0005 ? (
                <>
                  Le reste de <b className="text-foreground">{formatDT(plan.writeOffAmount ?? 0)}</b> redevient à
                  payer
                </>
              ) : (
                <>Le reste redevient <b className="text-foreground">à payer</b></>
              )),
            <>Séances faites et fiches de soins <b className="text-foreground">conservées</b></>,
          ]}
        />
      ),
      confirmLabel: "Reprendre le traitement",
      onConfirm: async () => {
        await run(
          () => treatmentPlansApi.reopenTreatment(plan.id, plan.version),
          "Traitement repris",
          "Échec de la reprise du traitement.",
        )
      },
    })

  /**
   * The act the header's button is about — `planNextAction`'s order: a séance to write up before one to book.
   *
   * <p>⚠️ **`schedulablePlanItems`, never `plan.items[0]`** — that is the gate the booking dialog's `planIdByItem`
   * is built from, and a priced « travail restant » makes a continuation's first act `Done` on creation (N28).
   * A booked séance needs no button of its own (the strip says « prévue le »), but a LATER one still unbooked does:
   * « Planifier : Scellement » while the empreinte is in the agenda — `firstUnbookedStep`, the dialog's own preset.</p>
   */
  const nextAct = useMemo(() => {
    if (!isActive) return null
    const items = schedulablePlanItems(plan)
    return (
      items.find((i) => planItemState(i) === "to-record") ??
      items.find((i) => planItemState(i) === "to-schedule") ??
      items.find((i) => planItemState(i) === "scheduled" && firstUnbookedStep(i) !== null) ??
      null
    )
  }, [plan, isActive])

  /**
   * The ONE large button — the next outcome, named (« Planifier : Scellement »); everything else is in the « ⋯ ».
   *
   * <p>Order: Rétablir (a cancelled devis) · Reprendre (stopped or non réclamé — tested BEFORE billable, since
   * `canBill` is true of a stopped devis and the other order left « Reprendre » unreachable) · Facturer (only once
   * every act is done — earlier it reads as advice to bill before delivering) · the next act. A treatment carried
   * to term gets none: « Reprendre » is a capability there, not a recommendation, and lives in the menu. The
   * numbering action (« Créer le devis ») is the devis chip's.</p>
   */
  const primaryAction = useMemo((): PrimaryAction | null => {
    if (canUncancelPlan(plan)) {
      return {
        kind: "uncancel",
        label: "Rétablir",
        icon: RotateCcw,
        run: () => {
          setUncancelReason("")
          setUncancelOpen(true)
        },
      }
    }
    if (isStopped || isPlanWrittenOff(plan.status)) {
      return { kind: "reopen", label: "Reprendre le traitement", icon: RotateCcw, run: confirmReopen }
    }
    if (canBill && actsRemaining === 0) {
      return { kind: "bill", label: "Facturer", icon: ReceiptText, run: confirmBill }
    }
    if (nextAct) {
      const step = nextStepOf(nextAct)
      if (planItemState(nextAct) === "to-record") {
        const appointmentId = step?.scheduledAppointmentId ?? nextAct.scheduledAppointmentId ?? null
        if (appointmentId) {
          return {
            kind: "record",
            label: `Enregistrer la fiche : ${step?.label ?? nextAct.designationFr}`,
            icon: FilePlus2,
            run: () => router.push(`/patients/${plan.patientId}?addRecord=1&appointmentId=${appointmentId}`),
          }
        }
        return null
      }
      // Named after the séance the dialog preselects (`preselectedStepId`), so the button says what gets booked.
      const preset = toPresetAct(nextAct)
      const booked = preset.steps?.find((s) => s.id === preset.preselectedStepId)?.label
      const name = booked ?? step?.label ?? nextAct.designationFr
      return { kind: "schedule", label: `Planifier : ${name}`, icon: CalendarPlus, run: () => startBooking([[nextAct]]) }
    }
    return null
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the confirm openers are stable per render
  }, [isStopped, canBill, actsRemaining, nextAct, toPresetAct, plan, plan.status, plan.version, plan.totalPlanned, plan.amountPaid])

  /**
   * Move an act one position up or down. The endpoint takes the **whole** order, not a delta — a partial
   * list would leave the untouched acts at stale positions and silently interleave them — so this rebuilds
   * the full id list and sends it.
   */
  const handleMove = async (index: number, direction: -1 | 1) => {
    const target = index + direction
    if (target < 0 || target >= plan.items.length) return

    const ids = plan.items.map((i) => i.id)
    ;[ids[index], ids[target]] = [ids[target], ids[index]]

    await run(
      // ⚠️ `plan.version` — the reorder was the last plan write with no concurrency token, so it landed over a
      // colleague's concurrent amendment with no 409 and no trace.
      () => treatmentPlansApi.reorderItems(plan.id, ids, plan.version),
      "Ordre des actes mis à jour",
      "Échec du réordonnancement.",
    )
  }

  const handleDownloadReceipt = async (installmentId: string, paymentId: string) => {
    setBusy(true)
    try {
      const blob = await treatmentPlansApi.downloadInstallmentReceipt(plan.id, installmentId, paymentId)
      downloadBlob(blob, `recu-echeance-${paymentId.slice(0, 8)}.pdf`)
    } catch (err) {
      showErrorToast(err, "Échec du téléchargement du reçu.", () =>
        handleDownloadReceipt(installmentId, paymentId),
      )
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="mx-auto max-w-5xl space-y-6">
      {/* router.push, never router.back(): the workspace is reachable from /factures, the patient page and the
          list, and « back » to a surface other than the one the button names is disorienting. */}
      <Button
        variant="ghost"
        size="sm"
        className="gap-2 coarse:h-11"
        onClick={() => confirmDiscard(() => router.push("/treatment-plans"))}
      >
        <ArrowLeft className="h-4 w-4" />
        Traitements
      </Button>

      {/* ---- Header -------------------------------------------------------------------------------- */}
      <Card className={CARD_GAP}>
        <CardHeader className={CARD_HEADER}>
          <div className="flex flex-wrap items-start justify-between gap-3">
            {/*
              Who, then what: the patient above the treatment's own name (« Couronne · dent 16 »). The devis
              number is not the title — it is the paper, and it has its own chip.
            */}
            <div className="min-w-0 flex-1 basis-60 space-y-1">
              <PatientNameLink patientId={plan.patientId} name={plan.patientName ?? "Patient"} />
              <CardTitle className="flex flex-wrap items-center gap-2 text-xl [overflow-wrap:anywhere]">
                {treatmentName(plan)}
                <Badge variant="secondary" className={planStatusBadgeClass(plan.status, planHasRecordedWork(plan))}>
                  {planStatusLabel(plan.status, planHasRecordedWork(plan))}
                </Badge>
              </CardTitle>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              {busy && <Loader2 className="h-4 w-4 animate-spin" aria-label="Enregistrement en cours" />}
              {/*
                The devis chip — the paper only. Numbered: opens its PDF. None yet: « Créer le devis », the one
                confirmed step of a followed treatment (it takes a gapless number).
              */}
              {plan.number ? (
                <Button
                  variant="outline"
                  size="sm"
                  className="gap-2 coarse:h-11"
                  disabled={busy}
                  onClick={handleDownloadDevis}
                  aria-label={`${planDevisLabel(plan)} — télécharger le PDF`}
                >
                  <FileDown className="h-4 w-4" />
                  {planDevisLabel(plan)}
                </Button>
              ) : (
                <span className="inline-flex flex-wrap items-center gap-2">
                  {/* A fact, not a control: plain text, so it never reads as a disabled chip beside the button. */}
                  <span className="text-xs text-muted-foreground">{planDevisLabel(plan)}</span>
                  {isDraft && (
                    <Button
                      variant="outline"
                      size="sm"
                      className="gap-2 coarse:h-11"
                      disabled={busy}
                      onClick={guarded(confirmIssueDevis)}
                    >
                      <ClipboardCheck className="h-4 w-4" />
                      Créer le devis
                    </Button>
                  )}
                </span>
              )}
              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <Button
                    variant="outline"
                    size="icon"
                    className="touch-target"
                    disabled={busy}
                    aria-label="Autres actions sur ce traitement"
                  >
                    <MoreHorizontal className="h-4 w-4" />
                  </Button>
                </DropdownMenuTrigger>
                {/* Grouped — Document · Modifier · Fin du traitement. Same actions and gates as the flat list. */}
                <DropdownMenuContent align="end" className="w-64">
                  <DropdownMenuLabel className={MENU_GROUP_LABEL}>Document</DropdownMenuLabel>
                  <DropdownMenuItem onSelect={handleDownloadDevis} disabled={busy}>
                    <FileDown className="h-4 w-4" />
                    Devis PDF
                  </DropdownMenuItem>
                  {/* J5 — « Facturer » is the header's button only once the work is done; here otherwise. */}
                  {canBill && primaryAction?.kind !== "bill" && (
                    <DropdownMenuItem disabled={busy} onSelect={guarded(confirmBill)}>
                      <ReceiptText className="h-4 w-4" />
                      Facturer
                    </DropdownMenuItem>
                  )}

                  <DropdownMenuSeparator />
                  <DropdownMenuLabel className={MENU_GROUP_LABEL}>Modifier</DropdownMenuLabel>
                  {canAmend && (
                    <DropdownMenuItem disabled={busy} onSelect={() => requestFullEdit()}>
                      <FilePen className="h-4 w-4" />
                      Tout modifier
                    </DropdownMenuItem>
                  )}
                  {/* M6 — who the next note d'honoraires raised from this devis credits. */}
                  <DropdownMenuItem
                    disabled={busy}
                    onSelect={guarded(() => {
                      setDoctorDraft(plan.doctorId ?? "")
                      setDoctorOpen(true)
                    })}
                  >
                    <Stethoscope className="h-4 w-4" />
                    Changer le praticien
                  </DropdownMenuItem>
                  {/* M5 — the server refuses once work is delivered or a live note names it. */}
                  <DropdownMenuItem
                    disabled={busy}
                    onSelect={guarded(() => {
                      setPatientQuery("")
                      setPatientResults(null)
                      setPatientDraft(null)
                      setReassignOpen(true)
                    })}
                  >
                    <UserCog className="h-4 w-4" />
                    Changer de patient
                  </DropdownMenuItem>
                  {/* S1 — consumes no number, claims no money, changes nothing about this devis. */}
                  <DropdownMenuItem disabled={busy} onSelect={guarded(() => setDuplicateOpen(true))}>
                    <Copy className="h-4 w-4" />
                    Dupliquer
                  </DropdownMenuItem>
                  {/* M3 — the remedy three server refusals name. */}
                  {billed && plan.linkedInvoiceId && (
                    <DropdownMenuItem disabled={busy} onSelect={guarded(() => setDetachNoteOpen(true))}>
                      <Unlink className="h-4 w-4" />
                      Détacher la note
                    </DropdownMenuItem>
                  )}

                  {(plan.status === "Completed" || isActive || canWriteOffPlan(plan) || canCancelPlan(plan) || canDelete) && (
                    <>
                      <DropdownMenuSeparator />
                      <DropdownMenuLabel className={MENU_GROUP_LABEL}>Fin du traitement</DropdownMenuLabel>
                    </>
                  )}
                  {/* m13 — on a plan carried to term « Reprendre » is a capability, never a recommendation. On a
                      STOPPED plan it is the header's primary instead. */}
                  {plan.status === "Completed" && (
                    <DropdownMenuItem disabled={busy} onSelect={guarded(confirmReopen)}>
                      <RotateCcw className="h-4 w-4" />
                      Reprendre le traitement
                    </DropdownMenuItem>
                  )}
                  {isActive && (
                    <DropdownMenuItem disabled={busy} onSelect={guarded(() => setStopOpen(true))}>
                      <CircleSlash className="h-4 w-4" />
                      Arrêter
                    </DropdownMenuItem>
                  )}
                  {/* S4 — gives up money owed but destroys no record and is undone by « Reprendre », so not red. */}
                  {canWriteOffPlan(plan) && (
                    <DropdownMenuItem
                      disabled={busy}
                      onSelect={guarded(() => {
                        setWriteOffReason("")
                        setWriteOffOpen(true)
                      })}
                    >
                      <HandCoins className="h-4 w-4" />
                      Ne plus réclamer le reste
                    </DropdownMenuItem>
                  )}
                  {/*
                    « Annuler le devis » only where « Arrêter » cannot reach the cancellation itself — see
                    `canCancelPlan`. « Supprimer » is a different thing: a treatment created by mistake, while no
                    number was consumed and nothing was carried out (`CanBeDeleted`).
                  */}
                  {canCancelPlan(plan) && (
                    <DropdownMenuItem
                      variant="destructive"
                      disabled={busy}
                      onSelect={guarded(() => {
                        setCancelReason("")
                        setCancelPlanOpen(true)
                      })}
                    >
                      <Ban className="h-4 w-4" />
                      Annuler le devis
                    </DropdownMenuItem>
                  )}
                  {canDelete && (
                    <DropdownMenuItem
                      variant="destructive"
                      disabled={busy}
                      onSelect={guarded(() => setDeleteOpen(true))}
                    >
                      <Trash2 className="h-4 w-4" />
                      Supprimer
                    </DropdownMenuItem>
                  )}
                </DropdownMenuContent>
              </DropdownMenu>
            </div>
          </div>
        </CardHeader>

        <CardContent className="space-y-4">
          {/*
            « Où en est-on ? » folded into ONE picture: the act's own séances when the treatment is one stepped
            act (the row below then drops its strip), else a count in words — never a bare « 2 / 5 » (N31).
          */}
          {headerStripItem ? (
            <PlanActSeances plan={plan} item={headerStripItem} handlers={seanceHandlers(headerStripItem)} size="lg" />
          ) : (
            seances.total > 0 && (
              <p className="text-sm text-muted-foreground">
                {/* A closed treatment counts only what was done — « à faire » is false of one nobody runs. */}
                <b className="text-foreground">{seanceCountLabel(seances.done, seances.total, !isActive)}</b>
                {isActive && plan.nextAppointmentAt && (
                  <> · prochain RDV le <b className="text-foreground">{formatDateFr(plan.nextAppointmentAt)}</b></>
                )}
              </p>
            )
          )}

          {/* « Et maintenant ? » — who, since when, and the ONE large button naming the next outcome. */}
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="min-w-0 text-sm text-muted-foreground">
              {/* M6 — printed even when absent: « praticien non attribué » is the state that needs correcting. */}
              {plan.doctorName ? `Dr ${plan.doctorName}` : "Praticien non attribué"}
              <span className="whitespace-nowrap"> · depuis le {formatDateFr(plan.createdAt)}</span>
            </p>
            {primaryAction && (
              /*
               * The step name is free text of unknown width and `Button` is `whitespace-nowrap shrink-0`, so the
               * label is allowed to wrap inside a bounded box rather than push the row off the card.
               */
              <Button
                size="lg"
                className="h-auto min-h-10 max-w-full gap-2 whitespace-normal py-2 text-start coarse:min-h-11"
                disabled={busy}
                onClick={primaryAction.kind === "schedule" ? primaryAction.run : guarded(primaryAction.run)}
              >
                <primaryAction.icon className="h-4 w-4" />
                {primaryAction.label}
              </Button>
            )}
          </div>

          {plan.notes && (
            <p className="whitespace-pre-line rounded-md bg-muted/50 p-3 text-sm text-muted-foreground">
              {plan.notes}
            </p>
          )}
          {plan.cancellationReason && (
            <p className="rounded-md border border-destructive/25 bg-destructive-wash p-3 text-sm text-destructive">
              Motif d&apos;annulation : {plan.cancellationReason}
            </p>
          )}
        </CardContent>
      </Card>

      {/* ---- Actes --------------------------------------------------------------------------------- */}
      <Card className={CARD_GAP}>
        <CardHeader className={CARD_HEADER}>
          <CardTitle className="text-base">Actes</CardTitle>
        </CardHeader>
        <CardContent>
          {/*
            S7 — « cet acte est déjà sur un autre devis ». A NOTICE, never a refusal: a second opinion
            legitimately re-quotes, and a patient may have the same act on two teeth. But two live devis both
            carry debt and `PlanBillingRules.BilledPlanIds` de-duplicates per PLAN, so the act's fee is counted
            twice in « Solde patient » with nothing anywhere saying so. Served on this read only.
          */}
          {(plan.duplicateActs ?? []).length > 0 && (
            <div
              role="note"
              className="mb-3 rounded-md border border-warning/40 bg-warning-wash p-3 text-xs text-warning-ink"
            >
              <ul className="space-y-1">
                {(plan.duplicateActs ?? []).map((dup) => (
                  <li key={dup.itemId} className="flex flex-wrap items-center gap-x-2 [overflow-wrap:anywhere]">
                    <AlertTriangle className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                    <b>
                      {dup.designationFr}
                      {teethSuffix(dup.toothNumbers)} aussi sur{" "}
                      {dup.otherPlanNumber ? `le devis n° ${dup.otherPlanNumber}` : quoteFr(dup.otherPlanTitle)}
                    </b>
                    <Link
                      href={`/treatment-plans/${dup.otherPlanId}`}
                      className="inline-flex items-center underline underline-offset-2 coarse:min-h-11"
                    >
                      Ouvrir
                    </Link>
                  </li>
                ))}
              </ul>
            </div>
          )}

          {catalogFailed && (
            <LoadFailureNotice
              variant="inline"
              message="Le catalogue des actes n'a pas pu être chargé."
              detail="Un RDV planifié d'ici partira sans procédure (ni couleur, ni durée)."
              onRetry={() => void loadCatalog()}
              className="mb-3"
            />
          )}
          {/*
            The grouping bar — one action: the ticked acts become **one** appointment. Booking them separately is
            what each act's own « Planifier » already does, one row at a time, so the bar carrying a second
            « séparément » button duplicated that path and walked a queue of dialogs to do it.
          */}
          {selectedActIds.length > 0 && (
            <div
              role="status"
              className="mb-3 flex flex-wrap items-center gap-2 rounded-md border bg-muted/50 px-3 py-2"
            >
              <span className="text-sm font-medium">
                {selectedActIds.length} acte{selectedActIds.length > 1 ? "s" : ""} sélectionné
                {selectedActIds.length > 1 ? "s" : ""}
              </span>
              <div className="ml-auto flex flex-wrap items-center gap-2">
                <Button
                  size="sm"
                  className="h-8 gap-1"
                  disabled={busy}
                  onClick={() => startBooking([selectedItems])}
                >
                  {selectedActIds.length > 1 ? (
                    <Layers className="h-4 w-4" />
                  ) : (
                    <CalendarPlus className="h-4 w-4" />
                  )}
                  {selectedActIds.length > 1 ? "Planifier ensemble — 1 RDV" : "Planifier"}
                </Button>
                <Button
                  size="sm"
                  variant="ghost"
                  className="h-8 gap-1"
                  onClick={() => setSelectedActIds([])}
                >
                  <X className="h-4 w-4" />
                  Effacer
                </Button>
              </div>
            </div>
          )}

          {plan.items.length === 0 ? (
            /* « Aucun acte planifié. » on its own is a statement that the software is working correctly, which
               is not what the reader was worried about. A devis with no acts is either a draft nobody finished
               or an amendment that removed the last one, and both have exactly one next move — so name it. */
            <EmptyState
              icon={ListChecks}
              size="compact"
              title="Aucun acte"
            />
          ) : (
            <>
              {/*
                Exception 2 — the card half of the actes surface. Three things it does that the generic
                conversion does not:

                • **« séance de N actes » is a section header**, not a per-card badge. See `actGroups`.
                • **The tick box is `leading`, never a menu item.** It is the state of the row *and* the control
                  that changes it; a menu would hide the state behind a tap, and the grouping gesture is « tick,
                  tick, planifier ensemble » — three taps that cannot each open a menu first.
                • **The reorder arrows are a field's value** (« Ordre »), the pattern `lab-orders` already sets
                  with its status `<select>`. Beside the title they would eat the désignation's only line on a
                  320 px card; as a labelled line they say what they move.
              */}
              <div className={`${CARDS_ONLY_LG} space-y-3`}>
                {/* The card list has no header row, so the table's « Sélectionner tous » checkbox has nowhere to
                    live — without this, ticking eight acts on a phone is eight taps and the grouping gesture the
                    whole selection exists for stops being worth making. */}
                {canGroup && (
                  <div className="flex items-center justify-between gap-2">
                    <span className="text-xs text-muted-foreground">
                      {selectedActIds.length} sélectionné{selectedActIds.length > 1 ? "s" : ""} sur{" "}
                      {schedulableItems.length} à planifier
                    </span>
                    <Button
                      variant="ghost"
                      size="sm"
                      className="h-8"
                      onClick={() =>
                        setSelectedActIds(
                          selectedActIds.length === schedulableItems.length
                            ? []
                            : schedulableItems.map((i) => i.id),
                        )
                      }
                    >
                      {selectedActIds.length === schedulableItems.length
                        ? "Tout désélectionner"
                        : "Tout sélectionner"}
                    </Button>
                  </div>
                )}

                {actGroups.map((group) => (
                  <section key={group.key} className="rounded-md border bg-card">
                    {group.appointmentId && (
                      <h3 className="flex flex-wrap items-center gap-x-1.5 gap-y-0.5 border-b px-3 py-2 text-sm font-medium">
                        <Layers className="h-4 w-4 shrink-0 text-muted-foreground" />
                        Séance de {group.acts.length} actes
                        {group.acts[0].item.scheduledAt && (
                          <span className="font-normal text-muted-foreground">
                            · {formatDateFr(group.acts[0].item.scheduledAt)}
                          </span>
                        )}
                      </h3>
                    )}
                    <CardList
                      ariaLabel={
                        group.appointmentId
                          ? `Actes de la séance de ${group.acts.length} actes`
                          : "Actes planifiés"
                      }
                      items={group.acts}
                      getKey={(a) => a.item.id}
                      title={(a) => a.item.designationFr}
                      /* The strip goes under the act's name through `underTitle`, never `subtitle` (a `<p>`
                         cannot hold it). Withheld when the header already draws this act's séances. */
                      underTitle={(a) =>
                        headerStripItem?.id === a.item.id ? undefined : (
                          <PlanActSeances plan={plan} item={a.item} handlers={seanceHandlers(a.item)} />
                        )
                      }
                      status={
                        showStateColumn ? (a) => <PlanActStateBadge item={a.item} planLive={isActive} /> : undefined
                      }
                      leading={(a) =>
                        canGroup ? (
                          <PlanActSelectionBox
                            item={a.item}
                            selection={{
                              selectable: schedulableItems.some((i) => i.id === a.item.id),
                              checked: selectedActIds.includes(a.item.id),
                              onToggle: () => toggleActSelection(a.item.id),
                            }}
                          />
                        ) : null
                      }
                      fields={(a) => [
                        ...planActCardFields(a.item, costCell(a.item)),
                        canReorder && {
                          label: "Ordre",
                          value: (
                            <PlanActReorderControls
                              item={a.item}
                              orientation="horizontal"
                              reorder={{
                                disabled: busy,
                                canMoveUp: a.index > 0,
                                canMoveDown: a.index < plan.items.length - 1,
                                onMoveUp: guarded(() => void handleMove(a.index, -1)),
                                onMoveDown: guarded(() => void handleMove(a.index, 1)),
                              }}
                            />
                          ),
                        },
                      ]}
                      /*
                       * ⚠️ EVERY act control is on its own wrapping row under the fields (`primaryAction`) —
                       * none in the header. There, « Découper en séances · Modifier · De côté » left the act's
                       * name — the card's identity — one letter per line at 820 px (and « Bridge 4 dents
                       * (14-17) » a 26-line column at 320 px before that). `[overflow-wrap:anywhere]` makes
                       * that possible rather than an overflow, so nothing looks broken from the code's side.
                       */
                      primaryAction={(a) => {
                        const withdrawn = isItemWithdrawn(a.item)
                        const onRestore = canAmend ? guarded(setRestoreTarget) : undefined
                        // A stepped act's own actions live on its séances; a step-less one keeps them here.
                        const primary = hasPlanActPrimaryAction(plan, a.item, onRestore) ? (
                          <PlanActPrimaryAction
                            plan={plan}
                            item={a.item}
                            onSchedule={(target) => startBooking([[target]])}
                            onUndo={canCorrectActs ? guarded(setUndoTarget) : undefined}
                            onRestore={onRestore}
                            navigate={guardedPush}
                            block
                          />
                        ) : null
                        // A stepped act is cut from its strip (« + », « Modifier la séance »).
                        const steps =
                          canCorrectActs && !withdrawn && (a.item.steps?.length ?? 0) === 0 ? (
                            <PlanActStepsAction item={a.item} onEditSteps={guarded((it: TreatmentPlanItemDto) => openSteps(it, null))} />
                          ) : null
                        const edit = canAmend && !withdrawn ? (
                          <PlanActEditAction item={a.item} onEdit={requestFullEdit} />
                        ) : null
                        const park = canWithdrawItem(a.item) ? (
                          <PlanActWithdrawAction item={a.item} onWithdraw={guarded(setWithdrawTarget)} />
                        ) : null
                        if (!primary && !steps && !edit && !park) return null
                        return (
                          <div className="flex flex-wrap items-center gap-1">
                            {primary}
                            {steps}
                            {edit}
                            {park}
                          </div>
                        )
                      }}
                    />
                  </section>
                ))}
              </div>

              <Table containerClassName={`${TABLE_ONLY_LG} rounded-md border`}>
                <TableHeader>
                  <TableRow>
                    {canGroup && (
                      <TableHead className="w-10">
                        {/* Selects only what is bookable — an already-booked or réalisé act has nothing to plan. */}
                        <Checkbox
                          className={SQUARE_CHECKBOX}
                          aria-label="Tout sélectionner"
                          checked={
                            selectedActIds.length > 0 && selectedActIds.length === schedulableItems.length
                          }
                          onCheckedChange={(checked) =>
                            setSelectedActIds(checked ? schedulableItems.map((i) => i.id) : [])
                          }
                        />
                      </TableHead>
                    )}
                    {canReorder && <TableHead className="w-16">Ordre</TableHead>}
                    {/*
                      ⚠️ **Three columns, and « Dents » and « Action » are deliberately not among them.**
                      The action cell carried up to five word-buttons — « Planifier la séance · Séances ·
                      Modifier · Remise · De côté », ~450 px that `Button` makes `whitespace-nowrap shrink-0`
                      and therefore un-shrinkable. A `<table>` in a `w-full` container squeezes the cells that
                      CAN wrap down to their min-content to pay for that, so at the 1 217 px of page an
                      ordinary laptop has, « 300,000 DT » broke across two lines and the remise line across
                      four, beside a désignation column three words wide. Reported as « very bad ui ux ».
                      The controls now sit on the act's own full-width sub-row (`PlanActRow`) — nothing folded
                      into a « ⋯ », because `PlanActEditAction` exists precisely because a dentist could not
                      find « Modifier » inside the header's menu. « Dents » moved under the désignation it
                      qualifies; it was « — » on most rows and cost a column either way.
                    */}
                    <TableHead>Désignation</TableHead>
                    <TableHead className="whitespace-nowrap text-right">Prix</TableHead>
                    {showStateColumn && <TableHead>État</TableHead>}
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {plan.items.map((item, index) => (
                    <PlanActRow
                      key={item.id}
                      plan={plan}
                      item={item}
                      onSchedule={(target) => startBooking([[target]])}
                      onUndo={canCorrectActs ? guarded(setUndoTarget) : undefined}
                      onEditSteps={canCorrectActs ? guarded((it: TreatmentPlanItemDto) => openSteps(it, null)) : undefined}
                      onEdit={canAmend ? requestFullEdit : undefined}
                      onWithdraw={canWithdrawItem(item) ? guarded(setWithdrawTarget) : undefined}
                      onRestore={canAmend ? guarded(setRestoreTarget) : undefined}
                      seances={headerStripItem?.id === item.id ? null : seanceHandlers(item)}
                      navigate={guardedPush}
                      cost={costCell(item)}
                      showState={showStateColumn}
                      selection={
                        canGroup
                          ? {
                              // Rendered (disabled) even for a non-bookable act, so the column keeps its width
                              // and the rows stay aligned.
                              selectable: schedulableItems.some((i) => i.id === item.id),
                              checked: selectedActIds.includes(item.id),
                              onToggle: () => toggleActSelection(item.id),
                            }
                          : undefined
                      }
                      sessionActCount={
                        item.scheduledAppointmentId
                          ? actsPerAppointment.get(item.scheduledAppointmentId) ?? 1
                          : 1
                      }
                      reorder={
                        canReorder
                          ? {
                              disabled: busy,
                              canMoveUp: index > 0,
                              canMoveDown: index < plan.items.length - 1,
                              onMoveUp: guarded(() => void handleMove(index, -1)),
                              onMoveDown: guarded(() => void handleMove(index, 1)),
                            }
                          : undefined
                      }
                    />
                  ))}
                </TableBody>
              </Table>
            </>
          )}
          {/*
            T3 — acts added from the catalogue, pending until « Enregistrer »: the form's own pick (tarif, protocol
            proposed), with the teeth and the fee editable here. Free text, title, notes and the échéancier are
            « Tout modifier ».
          */}
          {addedLines.length > 0 && (
            <ul className="mt-3 space-y-2" aria-label="Actes ajoutés, non enregistrés">
              {addedLines.map((line, index) => (
                <li
                  key={`added-${index}`}
                  className="flex flex-wrap items-center gap-x-3 gap-y-2 rounded-md border border-dashed border-primary/40 p-3"
                >
                  <span className="min-w-0 flex-1 basis-40 text-sm font-medium [overflow-wrap:anywhere]">
                    {line.designationFr}
                    <span className="ms-2 text-2xs font-normal text-primary">nouveau</span>
                  </span>
                  <ToothMultiSelect
                    value={line.toothNumbers}
                    onChange={(teeth) => updateAddedTeeth(index, teeth)}
                    disabled={savingInline}
                  />
                  <span className="inline-flex items-center gap-2">
                    <Input
                      type="text"
                      inputMode="decimal"
                      value={line.plannedCost}
                      onChange={(e) => updateAddedLine(index, { plannedCost: e.target.value, costTouched: true })}
                      disabled={savingInline}
                      className="h-8 w-24 text-end tabular-nums md:text-sm"
                      aria-label={`Prix de ${line.designationFr} (DT)`}
                    />
                    <span className="text-xs text-muted-foreground">DT</span>
                  </span>
                  <Button
                    variant="ghost"
                    size="icon"
                    className="size-8 coarse:size-11"
                    disabled={savingInline}
                    onClick={() => setAddedLines((prev) => prev.filter((_, i) => i !== index))}
                    aria-label={`Retirer ${quoteFr(line.designationFr)} des ajouts`}
                  >
                    <X className="h-4 w-4" />
                  </Button>
                </li>
              ))}
            </ul>
          )}

          {canAmend && (
            <div className="mt-3 flex flex-wrap items-center justify-between gap-2">
              <Popover open={addActOpen} onOpenChange={setAddActOpen}>
                <PopoverTrigger asChild>
                  <Button variant="ghost" size="sm" className="gap-1.5 text-primary coarse:h-11" disabled={savingInline}>
                    <Plus className="h-4 w-4" />
                    Ajouter un acte
                  </Button>
                </PopoverTrigger>
                {/* The same grouped catalogue the form and the booking dialog offer (`groupProceduresByCategory`). */}
                <PopoverContent align="start" className="w-[min(22rem,calc(100vw-2rem))] p-0">
                  <Command>
                    <CommandInput placeholder="Rechercher un acte…" />
                    <CommandList>
                      <CommandEmpty>Aucun acte trouvé.</CommandEmpty>
                      {procedureGroups.map(({ label, items }) => (
                        <CommandGroup key={label} heading={label}>
                          {items.map((pt) => (
                            <CommandItem
                              key={pt.id}
                              // cmdk matches on `value` alone — the discipline goes in it too.
                              value={pt.category ? `${pt.name} ${pt.category}` : pt.name}
                              onSelect={() => addActFromCatalogue(pt)}
                              className="coarse:py-3"
                            >
                              <span
                                className="me-2 size-3 shrink-0 rounded-full"
                                style={{ backgroundColor: pt.colorHex }}
                                aria-hidden
                              />
                              <span className="min-w-0 flex-1">
                                <span className="block truncate text-sm font-medium">{pt.name}</span>
                                {pt.defaultCost != null && pt.defaultCost > 0 && (
                                  <span className="block text-xs text-muted-foreground">{formatDT(pt.defaultCost)}</span>
                                )}
                              </span>
                              {(pt.defaultSteps?.length ?? 0) > 1 && (
                                <span className="ms-2 shrink-0 rounded-full bg-primary/10 px-1.5 text-2xs font-medium text-primary">
                                  {pt.defaultSteps!.length} séances
                                </span>
                              )}
                            </CommandItem>
                          ))}
                        </CommandGroup>
                      ))}
                    </CommandList>
                  </Command>
                </PopoverContent>
              </Popover>
              <Button
                variant="link"
                size="sm"
                className="h-auto px-0 whitespace-normal text-start coarse:min-h-11"
                disabled={busy}
                onClick={() => requestFullEdit()}
              >
                Tout modifier
              </Button>
            </div>
          )}
        </CardContent>
      </Card>

      {/*
        T3 — the pending in-place edits, and the one control that saves them. `sticky`, not `fixed`: AppShell's
        <main> is the scroller and the bottom bar its flex sibling, so no `--bottom-inset` is needed.

        ⚠️ Directly under « Actes », the card it edits — not at the page's end. A sticky bar only covers what
        comes BEFORE its place in the flow, so here « L'argent » can never sit under it (X1: at the end it covered
        the money figures until the page was scrolled to the bottom). Padding below it would not change that.
      */}
      {/*
        ⚠️ A refusal is ONE line inside the bar, never a banner above it: the long 409 sentence stacked over the
        bar covered the very prices being edited. The edits stay on screen until « Recharger » or « Annuler ».
      */}
      {inlineDirty && (
        <div className="sticky bottom-4 z-20 flex flex-wrap items-center justify-between gap-x-3 gap-y-2 rounded-lg bg-foreground px-4 py-3 text-background shadow-lg">
          <div className="min-w-0 flex-1 basis-48 space-y-0.5">
            <p role="status" className="text-sm font-semibold">
              {inlineChangeCount} modification{inlineChangeCount > 1 ? "s" : ""}
            </p>
            {billed && plan.linkedInvoiceNumber && (
              <p className="text-xs opacity-80">
                Note n° {plan.linkedInvoiceNumber} inchangée : avoir si le montant change
              </p>
            )}
            {inlineConflict.error && (
              <p role="alert" className="flex items-start gap-1.5 text-xs font-semibold [overflow-wrap:anywhere]">
                <AlertTriangle className="mt-px h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                {inlineConflict.isConflict ? "Modifié par quelqu'un d'autre" : inlineConflict.error}
              </p>
            )}
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Button
              variant="outline"
              className="border-background/40 bg-transparent text-background hover:bg-background/10 hover:text-background coarse:h-11"
              disabled={savingInline}
              onClick={discardInline}
            >
              Annuler
            </Button>
            {inlineConflict.isConflict ? (
              /*
               * Re-read from the server and drop the stale edits — resyncing the version alone would let them
               * overwrite the colleague on the next press (F3). It replaces « Enregistrer », which would only
               * repeat the refusal: the version it holds never moves.
               */
              <Button
                className="coarse:h-11"
                disabled={savingInline}
                onClick={() => {
                  discardInline()
                  onChanged()
                }}
              >
                Recharger
              </Button>
            ) : (
              <Button className="coarse:h-11" disabled={savingInline} onClick={() => void saveInline()}>
                {savingInline ? "Enregistrement…" : "Enregistrer"}
              </Button>
            )}
          </div>
        </div>
      )}

      {/* ---- L'argent ------------------------------------------------------------------------------- */}
      {/*
        T4 — three figures and ONE « Encaisser ». The échéancier folds behind its own count, holding every
        control it had (Modifier l'échéancier, per-row Encaisser, Reçu, Annuler).

        ⚠️ `displayedOutstanding`, never `plan.outstanding`: a devis a note collects has an auto-raised échéance
        that never sees a payment, so its own figure reports the whole devis as unpaid (4 of 4 bridged plans).
        ⚠️ When a note carries part of this treatment the figures are the TREATMENT's (served), and the two
        documents are listed below — each is settled in its own place.
      */}
      <Card className={CARD_GAP}>
        <CardHeader className={CARD_HEADER}>
          <CardTitle className="text-base">L&apos;argent</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          {/* `flex-wrap` with a real `basis`, never a bare `grid-cols-2`: a long label takes its own line. */}
          <div className="flex flex-wrap gap-4">
            {treatmentMoney ? (
              <>
                {/* The treatment's price only when the note bills this treatment ALONE — see `mixed`. */}
                <Figure
                  label="Prix du traitement"
                  value={formatDT(treatmentMoney.total)}
                  hint={treatmentMoney.mixed ? "avec d'autres actes de la note" : "devis + note"}
                />
                <Figure label="Payé" value={formatDT(treatmentMoney.collected)} />
                <Figure label="Reste à payer" value={formatDT(treatmentMoney.outstanding)} emphasis />
              </>
            ) : (
              <>
                {/* S2 — the remise stays a second figure beside the price, never a silently lower total. */}
                <Figure
                  label="Prix du traitement"
                  value={formatDT(plan.totalPlanned)}
                  hint={
                    (plan.totalDiscount ?? 0) > 0.0005
                      ? `dont remise ${formatDT(plan.totalDiscount ?? 0)}`
                      : undefined
                  }
                />
                <Figure label="Payé" value={formatDT(plan.amountPaid)} />
                {/* A billed devis names its note ONCE — the « Facturé sur la note n° … » line below. */}
                {owed && <Figure label="Reste à payer" value={formatDT(owed.amount)} emphasis />}
              </>
            )}
          </div>

          {/* S4 — what was given up, with its motif: the evidence of the loss. */}
          {isPlanWrittenOff(plan.status) && (
            <p role="note" className="rounded-md border bg-muted/40 p-3 text-sm text-muted-foreground">
              Non réclamé&nbsp;: <b className="text-foreground">{formatDT(plan.writeOffAmount ?? 0)}</b>
              {plan.writeOffReason ? ` — ${plan.writeOffReason}` : ""}
            </p>
          )}

          {/*
            The composition — where each half of the treatment is settled, because a payment on a note and one on
            an échéance produce different receipts and reach la caisse by different ledgers. Gated on `carried`,
            not `treatmentMoney`: a DRAFT note is absent from the figures but its act at 0 still needs saying.
          */}
          {carried.length > 0 && (
            <div className="space-y-2">
              {carried.map((note) => (
                <div
                  key={note.invoiceId}
                  className="flex flex-wrap items-center justify-between gap-x-3 gap-y-2 rounded-md border bg-muted/30 p-3"
                >
                  <div className="min-w-0 flex-1 basis-48">
                    <p className="text-sm font-medium">
                      {note.number ? `Note n° ${note.number}` : "Brouillon de note d'honoraires"}
                    </p>
                    <p className="text-xs text-muted-foreground">
                      Séance 1
                      {note.billedActAmount > 0 && <> : <b className="text-foreground">{formatDT(note.billedActAmount)}</b></>}
                      {/* A note is per fiche, so it may bill a détartrage done the same day. */}
                      {note.billsOtherWork && " · avec d'autres actes"}
                      {note.status === "Draft" ? (
                        <> · brouillon <b className="text-foreground">{formatDT(note.total)}</b>, rien réclamé</>
                      ) : (
                        <>
                          {" "}· payé <b className="text-foreground">{formatDT(note.collected)}</b> · reste à payer{" "}
                          <b className="text-foreground">{formatDT(note.outstanding)}</b>
                        </>
                      )}
                    </p>
                  </div>
                  {note.status !== "Draft" && note.outstanding > 0.0005 && (
                    <Button asChild size="sm" variant="outline" className="h-auto whitespace-normal coarse:min-h-11">
                      <Link href={patientOutstandingHref(plan.patientId, note.invoiceId)}>
                        Encaisser sur la note
                      </Link>
                    </Button>
                  )}
                </div>
              ))}
              {/* The devis' own half — only when the figures above speak for both documents. */}
              {treatmentMoney && (
                <div className="rounded-md border p-3">
                  <p className="text-sm font-medium">{planDevisLabel(plan)}</p>
                  <p className="text-xs text-muted-foreground">
                    Reste du traitement <b className="text-foreground">{formatDT(plan.totalPlanned)}</b> · payé{" "}
                    <b className="text-foreground">{formatDT(plan.amountPaid)}</b> · reste à payer{" "}
                    <b className="text-foreground">{formatDT(owed?.amount ?? plan.outstanding)}</b>
                  </p>
                </div>
              )}
            </div>
          )}

          {/*
            The échéancier's fold + the ONE « Encaisser » (S3's settle: one payment spread from the oldest unpaid
            row, amount prefilled). Offered only when the rows can take money — the same `canCollectInstallments`
            the rows read. Otherwise the reason, as one visible fact (a tooltip is unreachable on a tablet).
          */}
          {(scheduleFold || showSettle || showNoCollectFact) && (
            <div className="flex flex-wrap items-center justify-between gap-3">
              {scheduleFold && (
                <Button
                  variant="ghost"
                  size="sm"
                  className="gap-1.5 px-2 coarse:h-11"
                  aria-expanded={scheduleOpen}
                  aria-controls="plan-echeancier"
                  onClick={() => setScheduleOpen((open) => !open)}
                >
                  <ChevronRight className={cn("h-4 w-4 transition-transform", scheduleOpen && "rotate-90")} />
                  Échéancier ({plan.installments.length})
                </Button>
              )}
              {showSettle ? (
                <Button
                  className="ms-auto gap-2 coarse:h-11"
                  disabled={busy}
                  onClick={guarded(() => setSettleOpen(true))}
                >
                  <Wallet className="h-4 w-4" />
                  Encaisser
                </Button>
              ) : (
                showNoCollectFact && (
                  <p role="note" className="ms-auto text-sm text-muted-foreground">
                    {noCollectFact}
                  </p>
                )
              )}
            </div>
          )}

          {scheduleFold && scheduleOpen && (
            <div id="plan-echeancier" className="space-y-3">
              {/* AC-P2.5 — re-spread the échéancier without touching the acts; same window as the amendment. */}
              {canAmend && (
                <Button
                  size="sm"
                  variant="outline"
                  className="gap-2 coarse:h-11"
                  disabled={busy}
                  onClick={guarded(() => setReviseOpen(true))}
                >
                  <CalendarClock className="h-4 w-4" />
                  Modifier l&apos;échéancier
                </Button>
              )}
              {plan.installments.length === 0 ? (
                // A legitimate, common state — the patient pays in one go.
                <EmptyState icon={CalendarClock} size="compact" title="Aucune échéance" />
              ) : (
                <>
                  {/*
                    The **date is the title** here — an échéance has no other identity. Its actions take the menu:
                    « Encaisser » plus one « Reçu » per payment is a variable-length set a 320 px row cannot hold.
                  */}
                  <CardList
                    className={CARDS_ONLY_LG}
                    ariaLabel="Échéancier du devis"
                    items={plan.installments}
                    getKey={(inst) => inst.id}
                    title={(inst) => installmentDueLabel(inst)}
                    status={(inst) => <InstallmentStatusBadge inst={inst} />}
                    fields={(inst) => [
                      { label: "Montant", value: formatDT(inst.amount) },
                      { label: "Payé", value: formatDT(inst.amountPaid) },
                      { label: "Reste à payer", value: formatDT(inst.outstanding) },
                      // `CardList` drops a field with no value — « Paiements : — » would cost a line for nothing.
                      inst.payments.length > 0
                        ? { label: "Paiements", value: <InstallmentPaymentLines payments={inst.payments} /> }
                        : null,
                    ]}
                    actions={(inst) => {
                      const canCollect = !inst.isPaid && canCollectInstallments
                      // A rendu (negative) has no receipt and is not voidable — G3.
                      const receipts = inst.payments.filter((p) => !p.isVoided && p.amount > 0)
                      if (!canCollect && receipts.length === 0) return null
                      return (
                        <DropdownMenu>
                          <DropdownMenuTrigger asChild>
                            <Button
                              variant="ghost"
                              size="icon"
                              disabled={busy}
                              aria-label={`Actions de ${installmentDueSentence(inst)}`}
                            >
                              <MoreHorizontal className="h-4 w-4" />
                            </Button>
                          </DropdownMenuTrigger>
                          <DropdownMenuContent align="end">
                            {canCollect && (
                              <DropdownMenuItem onSelect={guarded(() => setPaymentTarget(inst))}>Encaisser</DropdownMenuItem>
                            )}
                            {receipts.map((payment) => (
                              <DropdownMenuItem
                                key={payment.id}
                                onSelect={() => handleDownloadReceipt(inst.id, payment.id)}
                              >
                                Reçu — {formatDT(payment.amount)} du {formatDateFr(payment.paidOn)}
                              </DropdownMenuItem>
                            ))}
                            {/* AC-5 — per live payment, like the receipts: only one of them is the mis-keyed one. */}
                            {receipts.map((payment) => (
                              <DropdownMenuItem
                                key={`void-${payment.id}`}
                                className="text-destructive focus:text-destructive"
                                onSelect={guarded(() => setVoidTarget({ installment: inst, payment }))}
                              >
                                Annuler l&apos;encaissement — {formatDT(payment.amount)}
                              </DropdownMenuItem>
                            ))}
                          </DropdownMenuContent>
                        </DropdownMenu>
                      )
                    }}
                  />

                  <Table containerClassName={`${TABLE_ONLY_LG} rounded-md border`}>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Échéance</TableHead>
                        <TableHead className="text-right">Montant</TableHead>
                        <TableHead className="text-right">Payé</TableHead>
                        <TableHead className="text-right">Reste à payer</TableHead>
                        <TableHead>Statut</TableHead>
                        <TableHead className="text-right">Action</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {plan.installments.map((inst) => (
                        <Fragment key={inst.id}>
                          <TableRow>
                            <TableCell><InstallmentDueCell inst={inst} /></TableCell>
                            <TableCell className="text-right">{formatDT(inst.amount)}</TableCell>
                            <TableCell className="text-right">{formatDT(inst.amountPaid)}</TableCell>
                            <TableCell className="text-right">{formatDT(inst.outstanding)}</TableCell>
                            <TableCell>
                              <InstallmentStatusBadge inst={inst} />
                            </TableCell>
                            <TableCell className="text-right">
                              {/* See `canCollectInstallments` — one derived rule shared with the card list. */}
                              {!inst.isPaid && canCollectInstallments && (
                                <Button
                                  variant="outline"
                                  size="sm"
                                  className="h-8 gap-1"
                                  disabled={busy}
                                  onClick={guarded(() => setPaymentTarget(inst))}
                                >
                                  <CreditCard className="h-4 w-4" />
                                  Encaisser
                                </Button>
                              )}
                            </TableCell>
                          </TableRow>

                          {/*
                            ⚠️ ONE ROW PER ENCAISSEMENT, never one loop per button: two payments rendered
                            « Reçu Reçu · Annuler Annuler » in one cell with only a hover `title` to tell them apart.
                            A lump-sum échéance collected séance by séance carries several.
                          */}
                          {inst.payments.map((payment) => (
                            <TableRow key={payment.id} className="border-0 bg-muted/30 hover:bg-muted/40">
                              <TableCell className="py-1.5 ps-8 text-xs text-muted-foreground">
                                {formatDateFr(payment.paidOn)}
                                <span className="ms-1.5 opacity-80">
                                  · {paymentMethodLabel(payment.method)}
                                </span>
                              </TableCell>
                              <TableCell className="py-1.5" />
                              <TableCell
                                className={cn(
                                  "py-1.5 text-right text-xs tabular-nums",
                                  payment.isVoided && "text-muted-foreground line-through",
                                )}
                              >
                                {payment.amount < 0 ? `− ${formatDT(-payment.amount)}` : formatDT(payment.amount)}
                              </TableCell>
                              {/* The séance it came from, when collected at the chair. */}
                              <TableCell className="py-1.5 text-xs text-muted-foreground" colSpan={2}>
                                {payment.isVoided ? (
                                  <span>
                                    annulé{payment.voidReason ? ` — ${payment.voidReason}` : ""}
                                    {payment.voidedByName ? ` (${payment.voidedByName})` : ""}
                                  </span>
                                ) : payment.amount < 0 ? (
                                  <span>rendu au patient</span>
                                ) : payment.dentalRecordId ? (
                                  <span>payé en séance</span>
                                ) : null}
                              </TableCell>
                              <TableCell className="py-1.5 text-right">
                                {/* A rendu has no receipt and is not voidable (G3) — the server refuses both. */}
                                {!payment.isVoided && payment.amount > 0 && (
                                  <div className="flex justify-end gap-1">
                                    <Button
                                      variant="ghost"
                                      size="sm"
                                      className="h-8 gap-1"
                                      disabled={busy}
                                      onClick={() => handleDownloadReceipt(inst.id, payment.id)}
                                    >
                                      <ReceiptText className="h-4 w-4" />
                                      Reçu
                                    </Button>
                                    {/* `text-destructive`, not a filled variant: annuler is not the row's primary. */}
                                    <Button
                                      variant="ghost"
                                      size="sm"
                                      className="h-8 gap-1 text-destructive"
                                      disabled={busy}
                                      onClick={guarded(() => setVoidTarget({ installment: inst, payment }))}
                                    >
                                      <Undo2 className="h-4 w-4" />
                                      Annuler
                                    </Button>
                                  </div>
                                )}
                              </TableCell>
                            </TableRow>
                          ))}
                        </Fragment>
                      ))}
                    </TableBody>
                  </Table>
                </>
              )}

              {/*
                AC-5 — an in-place confirm (`invoice-detail-modal`'s idiom) rather than a nested dialog: nothing in
                this app nests Radix dialogs, and it keeps the row being annulled on screen beside it.
              */}
              {voidTarget && (
                <VoidInstallmentPayment
                  planId={plan.id}
                  installmentId={voidTarget.installment.id}
                  installment={voidTarget.installment}
                  payment={voidTarget.payment}
                  onCancel={() => setVoidTarget(null)}
                  onVoided={() => {
                    setVoidTarget(null)
                    onChanged()
                  }}
                />
              )}
            </div>
          )}
        </CardContent>
      </Card>

      {/* ---- Historique du devis --------------------------------------------------------------------- */}
      {/*
        ⚠️ **Folded, and renamed from « Parcours ».** It is a journal of what has been recorded — created,
        accepted, séance planifiée, acte réalisé, paiement encaissé — and it is not read at the chair: nothing
        in it is actionable, and every fact it carries is stated where that fact lives (the acts card, the money
        card, the header). As a fourth open card it spent a quarter of the page restating them in date order.

        ⚠️ **Nothing is removed** (§ 0) — one press opens it, and « Parcours » was a name for the *shape* of the
        thing rather than for what it answers, which is « qu'est-ce qui s'est passé sur ce devis ? ».

        A native `<details>`, not a `Collapsible`: it needs no state, it is open to Enter and to a screen
        reader's own controls for free, and it prints expanded.
      */}
      {/* `group` + `group-open:` — the documented idiom. An arbitrary variant with a nested bracket
          (`[details[open]_&]:`) is not reliably parsed and would fail silently, leaving the chevron pointing
          right on an open panel. */}
      <details className="group rounded-xl border bg-card">
        <summary className="flex cursor-pointer items-center gap-2 p-4 text-sm font-medium text-muted-foreground coarse:py-5 [&::-webkit-details-marker]:hidden">
          <ChevronRight className="h-4 w-4 shrink-0 transition-transform group-open:rotate-90" />
          Historique
        </summary>
        <div className="pb-2">
          <PlanTimeline plan={plan} />
        </div>
      </details>

      {/* The discard guard (T3): leaving, or starting another write, with edits pending. */}
      <AlertDialog open={discardRequest !== null} onOpenChange={(open) => { if (!open) setDiscardRequest(null) }}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              Abandonner {inlineChangeCount} modification{inlineChangeCount > 1 ? "s" : ""}&nbsp;?
            </AlertDialogTitle>
            <AlertDialogDescription asChild>
              <Consequences
                items={[
                  <>
                    <b className="text-foreground">Non enregistrée{inlineChangeCount > 1 ? "s" : ""}</b> : elle
                    {inlineChangeCount > 1 ? "s seront perdues" : " sera perdue"}
                  </>,
                ]}
              />
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Retour</AlertDialogCancel>
            <AlertDialogAction
              variant="destructive"
              onClick={() => {
                const then = discardRequest
                discardInline()
                setDiscardRequest(null)
                then?.()
              }}
            >
              Abandonner
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <InstallmentPaymentModal
        open={!!paymentTarget}
        onOpenChange={(open) => !open && setPaymentTarget(null)}
        planId={paymentTarget ? plan.id : null}
        installment={paymentTarget}
        onSuccess={() => {
          setPaymentTarget(null)
          onChanged()
        }}
      />

      {/*
        ⚠️ Deliberately **not** keyed on the queue's head. That key changed in the same render that flipped `open`
        to true, so clicking « Planifier » unmounted the closed instance and mounted a new one that was *already
        open* — the case `useDirtyGuard` documents as hazardous, since its history push/back round-trip then fires
        a real `popstate` on mount and the dialog closed itself a frame later. One stable instance only ever
        *toggles*; `finishCurrentBooking` supplies the closed render that resets the form between two séances.
      */}
      <CreateAppointmentDialog
        open={bookingQueue.length > 0}
        onOpenChange={(open) => {
          if (open) return
          // A close that follows a successful create is already handled by `finishCurrentBooking`.
          if (justAdvancedRef.current) {
            justAdvancedRef.current = false
            return
          }
          setBookingQueue([])
        }}
        presetPatientId={plan.patientId}
        presetPatientName={plan.patientName ?? "Patient"}
        presetPlanId={plan.id}
        presetPlanActs={bookingQueue[0]}
        onSuccess={finishCurrentBooking}
      />

      {/*
        « Supprimer le traitement » — the way out of a followed treatment the patient decided against.

        ⚠️ An `AlertDialog`, not a `Dialog`, matching every other destructive confirmation in the product — and
        no motif field, deliberately: a motif exists to explain a **number** that was consumed and can never be
        reissued, and this treatment never had one. Asking for one here would imply a document is being voided.
      */}
      <AlertDialog open={deleteOpen} onOpenChange={(open) => { if (!open && !busy) setDeleteOpen(false) }}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Supprimer {quoteFr(treatmentName(plan))} ?</AlertDialogTitle>
            <AlertDialogDescription asChild>
              <Consequences
                items={[
                  bookedVisitsBullet,
                  <>
                    <b className="text-foreground">Aucune fiche de soins</b> touchée · le numéro de devis n&apos;est
                    pas utilisé
                  </>,
                  <b className="text-foreground">Irréversible</b>,
                ]}
              />
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={busy}>Retour</AlertDialogCancel>
            <AlertDialogAction
              variant="destructive"
              disabled={busy}
              onClick={(event) => {
                // Radix dismisses on click; prevented so a refusal keeps the dialog open (the list's rule).
                event.preventDefault()
                void handleDelete().then(() => setDeleteOpen(false))
              }}
            >
              Supprimer
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {/* AC-P2.1–2.4 — the plan form in amend mode. Refusals land in its own FormErrorBanner rather than a
          toast, which would fire behind the open dialog. */}
      <TreatmentPlanFormModal
        open={amendOpen}
        onOpenChange={setAmendOpen}
        editingPlan={plan}
        amendMode
        focusItemId={amendFocusItemId}
        presetPatientId={plan.patientId}
        presetPatientName={plan.patientName ?? "Patient"}
        onSuccess={() => {
          setAmendOpen(false)
          onChanged()
        }}
      />

      {/* AC-P2.5–2.7 — re-spread the échéancier without touching the acts. */}
      <ReviseInstallmentsModal
        open={reviseOpen}
        onOpenChange={setReviseOpen}
        plan={plan}
        onSuccess={() => {
          setReviseOpen(false)
          onChanged()
        }}
      />

      {/* The séances dialog — opened on one séance (« Modifier la séance ») or with a new row (« + »). */}
      <PlanItemStepsDialog
        plan={plan}
        item={stepsTarget}
        open={!!stepsTarget}
        onOpenChange={(open) => { if (!open) setStepsTarget(null) }}
        onSaved={onChanged}
        focusStepId={stepsFocus.stepId}
        addOnOpen={stepsFocus.add}
      />

      {/* « Déplacer » (T2) — the agenda's own edit dialog, hosted here so the date moves without leaving. */}
      <EditAppointmentDialog
        open={!!movingAppointment}
        onOpenChange={(open) => { if (!open) setMovingAppointment(null) }}
        appointment={movingAppointment}
        onSuccess={() => {
          setMovingAppointment(null)
          onChanged()
        }}
      />

      {/*
        « Remettre à faire » — confirmed rather than immediate: it reopens a devis that may have auto-completed.
        The server refuses once the fiche is billed; that sentence reaches `run()`'s toast.
      */}

      {/*
        m11 — an `AlertDialog`, like every other destructive confirm in this file and like the step-level twin
        in `plan-item-steps-dialog`. It was the one `Dialog` among them, with a hand-styled destructive button:
        dismissible by a click outside, focus on the destructive action rather than on the cancel, and it had
        to re-implement the red styling the primitive already carries. This file's own note beside the
        `PlanConfirm` dialog states that convention in as many words.
      */}
      <AlertDialog
        open={!!undoTarget}
        onOpenChange={(open) => {
          if (open || busy) return
          setUndoTarget(null)
          setRelinkKey("")
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              Remettre {quoteFr(undoOutcome?.stepLabel ?? undoTarget?.designationFr ?? "")} à faire&nbsp;?
            </AlertDialogTitle>
            {/*
              ⚠️ `detachOutcome` is the one place the arithmetic lives: `Unmark` releases the LAST séance recorded,
              so the bullet names THAT séance, then what stays done — counted in words (§ 13).
            */}
            <AlertDialogDescription asChild>
              <Consequences
                items={[
                  undoOutcome?.stepLabel ? (
                    <>
                      <b className="text-foreground">{undoOutcome.stepLabel}</b> redevient à faire
                      {(undoOutcome.remaining?.done ?? 0) > 0 && (
                        <>
                          {" — "}
                          <b className="text-foreground">
                            {undoOutcome.remaining!.done} séance{undoOutcome.remaining!.done > 1 ? "s" : ""} sur{" "}
                            {undoOutcome.remaining!.total} {undoOutcome.remaining!.done > 1 ? "faites" : "faite"}
                          </b>
                        </>
                      )}
                    </>
                  ) : (
                    <>L&apos;acte <b className="text-foreground">redevient à faire</b></>
                  ),
                  <>La fiche de soins est <b className="text-foreground">conservée</b>, seulement détachée</>,
                  /* The same forewarning as the step-level dialog — the fiche→note link is not on the plan DTO. */
                  <>Fiche facturée : <b className="text-foreground">créditer la note</b> d&apos;abord</>,
                ]}
              />
            </AlertDialogDescription>
          </AlertDialogHeader>

          {/*
            S6 — « … et la rattacher à »: one call, so the fiche and its date travel with the séance. Default
            « nowhere », so the plain detach is unchanged; rendered only when there is somewhere to put it.
          */}
          {relinkOptions.length > 0 && (
            <div className="space-y-1.5">
              <Label htmlFor="plan-relink-target">Rattacher cette séance à</Label>
              <Select value={relinkKey} onValueChange={setRelinkKey} disabled={busy}>
                <SelectTrigger id="plan-relink-target" className="w-full">
                  <SelectValue placeholder="Ne rien rattacher" />
                </SelectTrigger>
                <SelectContent>
                  {/* Radix refuses an empty-string value, so « nowhere » carries a sentinel. */}
                  <SelectItem value="none">Ne rien rattacher</SelectItem>
                  {relinkOptions.map((target) => (
                    <SelectItem key={target.key} value={target.key}>
                      {target.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}
          <AlertDialogFooter>
            <AlertDialogCancel disabled={busy}>Retour</AlertDialogCancel>
            {/*
              `variant="destructive"` on the primitive, rather than the hand-written fill this carried. Without
              it the default renders the *primary* colour, which is the visual grammar for « this is the
              recommended action » — and this reopens a closed devis and undoes work recorded as done.
            */}
            <AlertDialogAction
              variant="destructive"
              disabled={busy}
              onClick={async (event) => {
                // Radix dismisses on click; prevented so the dialog stays put for the whole round trip.
                event.preventDefault()
                const target = undoTarget
                if (!target) return
                // Captured before the call: detaching clears the link, and this is the only route from the
                // devis back to the fiche being corrected. See `detachOutcome`.
                const outcome = detachOutcome(target)
                const destination = relinkOptions.find((o) => o.key === relinkKey) ?? null

                await run(
                  /*
                   * S6 — ONE call when a destination was chosen, and it is deliberately not
                   * detach-then-mark from here: detaching clears the only pointer to the fiche and the
                   * séance's own date has to travel with it, so two calls re-attach work dated today.
                   */
                  () =>
                    destination
                      ? treatmentPlansApi.relinkSeance(plan.id, {
                          fromItemId: target.id,
                          fromStepId: outcome.stepId,
                          toItemId: destination.itemId,
                          toStepId: destination.stepId,
                          version: plan.version,
                        })
                      : treatmentPlansApi.markItemUndone(plan.id, target.id, plan.version),
                  destination
                    ? `Séance rattachée à ${destination.label}`
                    : outcome.stepLabel
                      ? `${quoteFr(outcome.stepLabel)} remise à faire`
                      : `${quoteFr(target.designationFr)} remis à faire`,
                  destination
                    ? "Échec du rattachement de la séance."
                    : "Échec de la correction de l'acte.",
                  outcome.dentalRecordId
                    ? {
                        label: "Ouvrir la fiche",
                        onClick: () =>
                          router.push(
                            `/patients/${plan.patientId}?editRecord=${encodeURIComponent(
                              outcome.dentalRecordId!,
                            )}`,
                          ),
                      }
                    : undefined,
                )
                setUndoTarget(null)
                setRelinkKey("")
              }}
            >
              {relinkOptions.some((o) => o.key === relinkKey) ? "Rattacher" : "Remettre à faire"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {/*
        The one dialog behind « Accepter le devis » / « Facturer le devis » / « Terminer » — see PlanConfirm.

        `AlertDialog` rather than the `Dialog` its two siblings above use, deliberately: those two carry input
        (a motif) or a long explanation, while these three are a pure yes/no on a one-way change. AlertDialog is
        the repo's convention for that — it is modal, it cannot be dismissed by clicking outside, and it puts
        focus on the cancel, so the confirmation cannot be walked past by a stray keypress.
      */}
      <AlertDialog
        open={confirmAction !== null}
        onOpenChange={(open) => { if (!open && !busy) setConfirmAction(null) }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{confirmAction?.title}</AlertDialogTitle>
            <AlertDialogDescription asChild>
              <div>{confirmAction?.description}</div>
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={busy}>Retour</AlertDialogCancel>
            <AlertDialogAction
              disabled={busy}
              onClick={async (event) => {
                // Radix closes an AlertDialogAction on click. Prevented so the dialog stays put — and disabled
                // — for the whole round trip, then closed here; otherwise « Facturer » would dismiss instantly
                // and the user would be looking at the plan for a second before the redirect fires.
                event.preventDefault()
                const action = confirmAction
                if (!action) return
                await action.onConfirm()
                setConfirmAction(null)
              }}
            >
              {confirmAction?.confirmLabel}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {/*
        C3 — « Rétablir ce devis annulé ». A `Dialog` rather than an `AlertDialog` for the same reason the stop
        uses its own: it carries an input. The motif is required and the confirm stays disabled until it is
        typed, rather than refusing afterwards.
      */}
      <Dialog open={uncancelOpen} onOpenChange={(open) => { if (!open && !busy) setUncancelOpen(false) }}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>Rétablir le devis n° {plan.number}&nbsp;?</DialogTitle>
            <DialogDescription asChild>
              <Consequences
                items={[
                  <>Même <b className="text-foreground">numéro</b> · les échéances sont recalculées sur les actes</>,
                  <>Motif d&apos;annulation <b className="text-foreground">conservé</b>, le vôtre ajouté</>,
                ]}
              />
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-1.5">
            <Label htmlFor="plan-uncancel-reason">Motif du rétablissement (obligatoire)</Label>
            <Textarea
              id="plan-uncancel-reason"
              value={uncancelReason}
              onChange={(e) => setUncancelReason(e.target.value)}
              placeholder="Ex. : annulé par erreur, le patient poursuit le traitement"
              rows={3}
              disabled={busy}
            />
          </div>
          <DialogFooter className="gap-2">
            <Button variant="outline" disabled={busy} onClick={() => setUncancelOpen(false)}>
              Retour
            </Button>
            <Button disabled={busy || !uncancelReason.trim()} onClick={() => void uncancelPlan()}>
              Rétablir le devis
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* C5 — « Annuler le devis », for the cases « Arrêter le traitement » cannot reach. See `canCancelPlan`. */}
      <Dialog open={cancelPlanOpen} onOpenChange={(open) => { if (!open && !busy) setCancelPlanOpen(false) }}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>Annuler le devis n° {plan.number}&nbsp;?</DialogTitle>
            {/* `Cancel` refuses live money outright, so a devis carrying an encaissement never reaches here. */}
            <DialogDescription asChild>
              <Consequences
                items={[
                  <>Sort de <b className="text-foreground">tous les soldes</b> et de la caisse</>,
                  <>Numéro <b className="text-foreground">conservé</b> avec le motif · fiches de soins intactes</>,
                  <>Réversible par <b className="text-foreground">Rétablir</b></>,
                ]}
              />
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-1.5">
            <Label htmlFor="plan-cancel-plan-reason">Motif d&apos;annulation (obligatoire)</Label>
            <Textarea
              id="plan-cancel-plan-reason"
              value={cancelReason}
              onChange={(e) => setCancelReason(e.target.value)}
              placeholder="Ex. : devis édité pour le mauvais patient"
              rows={3}
              disabled={busy}
            />
          </div>
          <DialogFooter className="gap-2">
            <Button variant="outline" disabled={busy} onClick={() => setCancelPlanOpen(false)}>
              Retour
            </Button>
            <Button
              variant="destructive"
              disabled={busy || !cancelReason.trim()}
              onClick={() => void cancelPlan()}
            >
              Annuler le devis
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/*
        M1 — « Mettre cet acte de côté ». The dialog names what is KEPT, because that is the whole difference
        between this and removing the act: nothing is deleted, no fiche link is touched, and it comes back.
      */}
      <AlertDialog
        open={!!withdrawTarget}
        onOpenChange={(open) => { if (!open && !busy) setWithdrawTarget(null) }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              Mettre {quoteFr(withdrawTarget?.designationFr ?? "")} de côté&nbsp;?
            </AlertDialogTitle>
            <AlertDialogDescription asChild>
              <Consequences
                items={[
                  <>
                    <b className="text-foreground">{formatDT(withdrawTarget ? itemNetCost(withdrawTarget) : 0)}</b>{" "}
                    sortent du total · les échéances sont recalculées
                  </>,
                  withdrawTarget?.scheduledAppointmentId && (
                    <><b className="text-foreground">1 RDV prévu</b> sera libéré</>
                  ),
                  <>Rien n&apos;est supprimé · <b className="text-foreground">Réversible</b></>,
                ]}
              />
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={busy}>Retour</AlertDialogCancel>
            <AlertDialogAction
              disabled={busy}
              onClick={(event) => {
                event.preventDefault()
                if (withdrawTarget) void withdrawItem(withdrawTarget)
              }}
            >
              Mettre de côté
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {/* M2 — « Remettre au devis », the per-act mirror. Parking used to be all-or-nothing in both directions. */}
      <AlertDialog
        open={!!restoreTarget}
        onOpenChange={(open) => { if (!open && !busy) setRestoreTarget(null) }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              Remettre {quoteFr(restoreTarget?.designationFr ?? "")} au devis&nbsp;?
            </AlertDialogTitle>
            <AlertDialogDescription asChild>
              <Consequences
                items={[
                  <>
                    <b className="text-foreground">{formatDT(restoreTarget ? itemNetCost(restoreTarget) : 0)}</b>{" "}
                    reviennent dans le total · les échéances sont recalculées
                  </>,
                  /* The plan status is not recomputed: a stopped treatment stays stopped until « Reprendre ». */
                  isStopped && (
                    <>Le traitement reste <b className="text-foreground">Arrêté</b> jusqu&apos;à « Reprendre »</>
                  ),
                ]}
              />
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={busy}>Retour</AlertDialogCancel>
            <AlertDialogAction
              disabled={busy}
              onClick={(event) => {
                event.preventDefault()
                if (restoreTarget) void restoreItem(restoreTarget)
              }}
            >
              Remettre au devis
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {/* M3 — « Détacher la note d'honoraires », the remedy three refusals name. */}
      <AlertDialog open={detachNoteOpen} onOpenChange={(open) => { if (!open && !busy) setDetachNoteOpen(false) }}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              {plan.linkedInvoiceNumber
                ? `Détacher la note n° ${plan.linkedInvoiceNumber}`
                : "Détacher la note d'honoraires"}
              &nbsp;?
            </AlertDialogTitle>
            <AlertDialogDescription asChild>
              <Consequences
                items={[
                  <>La note n&apos;est <b className="text-foreground">ni annulée ni modifiée</b></>,
                  <>Le devis reprend <b className="text-foreground">son propre reste à payer</b></>,
                  <>Si la note correspond encore : le patient doit <b className="text-foreground">deux fois</b></>,
                ]}
              />
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={busy}>Retour</AlertDialogCancel>
            <AlertDialogAction
              disabled={busy}
              onClick={(event) => {
                event.preventDefault()
                void detachNote()
              }}
            >
              Détacher la note
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {/*
        M6 — « Praticien ». It decides who the next note d'honoraires raised from this devis credits
        (`CreateInvoiceFromTreatmentPlanCommand` snapshots it), which is why it is worth being able to correct
        at all: notes already issued keep the praticien they were issued with.
      */}
      <Dialog open={doctorOpen} onOpenChange={(open) => { if (!open && !busy) setDoctorOpen(false) }}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>Changer le praticien&nbsp;?</DialogTitle>
            {/* The practitioner is snapshotted onto the next note d'honoraires raised from this devis. */}
            <DialogDescription asChild>
              <Consequences
                items={[
                  <>La <b className="text-foreground">prochaine note</b> sera à son nom</>,
                  <>Notes déjà émises : <b className="text-foreground">inchangées</b></>,
                ]}
              />
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-1.5">
            <Label htmlFor="plan-doctor">Praticien</Label>
            <Select value={doctorDraft} onValueChange={setDoctorDraft} disabled={busy || loadingDoctors}>
              <SelectTrigger id="plan-doctor" className="w-full">
                <SelectValue
                  placeholder={loadingDoctors ? "Chargement des praticiens…" : "Choisir un praticien…"}
                />
              </SelectTrigger>
              <SelectContent>
                {/* The active roster plus this devis' own practitioner, even retired (I3). */}
                {doctorsForPicker(allDoctors, plan.doctorId).length === 0 && !loadingDoctors ? (
                  <div className="px-2 py-1.5 text-sm text-muted-foreground">Aucun praticien enregistré</div>
                ) : (
                  doctorsForPicker(allDoctors, plan.doctorId).map((doctor) => (
                    <SelectItem key={doctor.id || doctor.name} value={doctor.id || ""}>
                      {doctor.name}
                    </SelectItem>
                  ))
                )}
              </SelectContent>
            </Select>
          </div>
          <DialogFooter className="gap-2">
            <Button variant="outline" disabled={busy} onClick={() => setDoctorOpen(false)}>
              Retour
            </Button>
            <Button disabled={busy || doctorDraft === (plan.doctorId ?? "")} onClick={() => void saveDoctor()}>
              Enregistrer
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* S3 — « Régler le devis », one encaissement spread over the échéancier. */}
      <SettlePlanModal
        open={settleOpen}
        onOpenChange={setSettleOpen}
        plan={plan}
        onSuccess={onChanged}
      />

      {/*
        S1 — « Dupliquer ce devis ». Confirmed rather than immediate: it creates a second document in the
        patient's file, and two devis with the same acts are easy to confuse afterwards. The dialog states the
        two facts that make it safe — no number, no créance.
      */}
      <AlertDialog open={duplicateOpen} onOpenChange={(open) => { if (!open && !busy) setDuplicateOpen(false) }}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Dupliquer {quoteFr(treatmentName(plan))}&nbsp;?</AlertDialogTitle>
            <AlertDialogDescription asChild>
              <Consequences
                items={[
                  <>Actes, prix, remises, dents et séances <b className="text-foreground">copiés</b></>,
                  <>La copie n&apos;a <b className="text-foreground">pas de devis</b> : rien n&apos;est réclamé</>,
                  <>Séances faites, paiements et échéancier <b className="text-foreground">non copiés</b></>,
                ]}
              />
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={busy}>Retour</AlertDialogCancel>
            <AlertDialogAction
              disabled={busy}
              onClick={(event) => {
                event.preventDefault()
                void duplicatePlan()
              }}
            >
              Dupliquer
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {/*
        S4 — « Passer la créance en perte ». A `Dialog` rather than an `AlertDialog` because it carries an
        input, like the stop and the uncancel. The motif is required: this figure is what the practice reports
        as a loss, and a loss with no reason is not evidence.
      */}
      <Dialog open={writeOffOpen} onOpenChange={(open) => { if (!open && !busy) setWriteOffOpen(false) }}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>
              Ne plus réclamer <b>{formatDT(owed?.amount ?? plan.outstanding)}</b>&nbsp;?
            </DialogTitle>
            {/* What separates this from an annulation: collected cash is kept, at its date, with its receipts. */}
            <DialogDescription asChild>
              <Consequences
                items={[
                  <>Sort du <b className="text-foreground">reste à payer</b> du patient</>,
                  <>
                    <b className="text-foreground">{formatDT(plan.amountPaid)}</b> déjà payés restent en caisse
                  </>,
                  <>Réversible par <b className="text-foreground">Reprendre le traitement</b></>,
                ]}
              />
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-1.5">
            <Label htmlFor="plan-write-off-reason">Motif (obligatoire)</Label>
            <Textarea
              id="plan-write-off-reason"
              value={writeOffReason}
              onChange={(e) => setWriteOffReason(e.target.value)}
              placeholder="Ex. : patient parti à l'étranger"
              rows={3}
              disabled={busy}
            />
          </div>
          <DialogFooter className="gap-2">
            <Button variant="outline" disabled={busy} onClick={() => setWriteOffOpen(false)}>
              Retour
            </Button>
            <Button disabled={busy || !writeOffReason.trim()} onClick={() => void writeOffPlan()}>
              Ne plus réclamer
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/*
        M5 — « Changer de patient ». The search is server-side and the choice is explicit: nothing is written
        until a person is picked AND confirmed, because this moves a numbered document into somebody else's
        file.
      */}
      <Dialog open={reassignOpen} onOpenChange={(open) => { if (!open && !busy) setReassignOpen(false) }}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>Changer le patient de ce traitement&nbsp;?</DialogTitle>
            {/* The server refuses once a séance is done or a live note names the devis. */}
            <DialogDescription asChild>
              <Consequences
                items={[
                  <>Actes, échéancier, numéro et historique <b className="text-foreground">suivent</b></>,
                  bookedVisitCount > 0 && (
                    <>
                      <b className="text-foreground">{bookedVisitCount} RDV</b> détaché{bookedVisitCount > 1 ? "s" : ""} du
                      devis
                    </>
                  ),
                  <>Refusé si une <b className="text-foreground">séance est faite</b> ou une note émise</>,
                ]}
              />
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-3">
            <div className="space-y-1.5">
              <Label htmlFor="plan-reassign-search">Chercher le bon patient</Label>
              <Input
                id="plan-reassign-search"
                value={patientQuery}
                onChange={(e) => {
                  setPatientQuery(e.target.value)
                  setPatientDraft(null)
                }}
                placeholder="Nom ou prénom…"
                disabled={busy}
                autoComplete="off"
              />
              <p className="text-2xs text-muted-foreground">
                Actuellement&nbsp;: {plan.patientName ?? "patient inconnu"}.
              </p>
            </div>

            {patientQuery.trim().length >= 2 && (
              <div className="max-h-56 space-y-1 overflow-y-auto rounded-md border p-1">
                {patientResults === null ? (
                  <p className="p-2 text-xs text-muted-foreground">Recherche…</p>
                ) : patientResults.length === 0 ? (
                  <p className="p-2 text-xs text-muted-foreground">Aucun patient ne correspond.</p>
                ) : (
                  patientResults.map((candidate) => (
                    <button
                      key={candidate.id}
                      type="button"
                      disabled={busy || candidate.id === plan.patientId}
                      onClick={() => setPatientDraft(candidate)}
                      className={cn(
                        "flex w-full items-baseline justify-between gap-2 rounded-md px-2 py-2 text-start text-sm coarse:min-h-11",
                        "hover-hover:hover:bg-accent disabled:opacity-50",
                        patientDraft?.id === candidate.id && "bg-accent text-accent-foreground",
                      )}
                    >
                      <span className="min-w-0 [overflow-wrap:anywhere]">
                        {candidate.firstName} {candidate.lastName}
                      </span>
                      {candidate.id === plan.patientId && (
                        <span className="shrink-0 text-2xs text-muted-foreground">patient actuel</span>
                      )}
                    </button>
                  ))
                )}
              </div>
            )}
          </div>

          <DialogFooter className="gap-2">
            <Button variant="outline" disabled={busy} onClick={() => setReassignOpen(false)}>
              Retour
            </Button>
            <Button disabled={busy || !patientDraft} onClick={() => void reassignPatient()}>
              {patientDraft
                ? `Rattacher à ${patientDraft.firstName} ${patientDraft.lastName}`
                : "Rattacher au patient"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/*
        « Arrêter le traitement ».

        ⚠️ Its own dialog rather than a `PlanConfirm` row, because it is NOT a pure yes/no: it has to name the
        acts it will drop and the ones it will keep, and « êtes-vous sûr ? » over an irreversible edit to a
        patient's treatment is exactly what the repo's destructive-confirm rule forbids.
      */}
      <AlertDialog open={stopOpen} onOpenChange={(open) => { if (!open && !stopping) setStopOpen(false) }}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              {/* The refund title and its button say the same thing: « Rendre … et arrêter » / « Rendre et arrêter ». */}
              {stopNeedsRefund ? (
                <>Rendre <b>{formatDT(plan.amountPaid)}</b> et arrêter&nbsp;?</>
              ) : stopWouldCancel ? (
                `Annuler le devis n° ${plan.number} ?`
              ) : (
                `Arrêter le traitement de ${plan.patientName ?? "ce patient"} ?`
              )}
            </AlertDialogTitle>
            {/*
              ⚠️ Three branches, the plan's own (`stopNeedsRefundFirst` · `stopWouldCancelPlan` · stop) — the refund
              arm is the server's third outcome stated BEFORE the press (N40). No « irréversible » on any of them:
              every branch has its way back.
            */}
            <AlertDialogDescription asChild>
              {stopNeedsRefund ? (
                <Consequences
                  items={[
                    <><b className="text-foreground">{formatDT(plan.amountPaid)}</b> rendus aujourd&apos;hui</>,
                    <>
                      <b className="text-foreground">
                        {liveItems.length} acte{liveItems.length > 1 ? "s" : ""}
                      </b>{" "}
                      mis de côté
                    </>,
                    <>Réversible par <b className="text-foreground">Reprendre le traitement</b></>,
                  ]}
                />
              ) : stopWouldCancel ? (
                <Consequences
                  items={[
                    <>Rien de fait, <b className="text-foreground">rien de payé</b> : le devis est annulé</>,
                    <>Numéro <b className="text-foreground">définitif</b>, conservé avec le motif</>,
                    bookedVisitsBullet,
                    <>Réversible par <b className="text-foreground">Rétablir</b></>,
                  ]}
                />
              ) : (
                <Consequences
                  items={[
                    stoppableItems.length > 0 && (
                      <>
                        <b className="text-foreground">
                          {stoppableItems.length} acte{stoppableItems.length > 1 ? "s" : ""}
                        </b>{" "}
                        mis de côté
                      </>
                    ),
                    <>Ce qui est fait est <b className="text-foreground">conservé</b></>,
                    <>Réversible par <b className="text-foreground">Reprendre le traitement</b></>,
                  ]}
                />
              )}
            </AlertDialogDescription>
          </AlertDialogHeader>

          <div className="space-y-3 text-sm">
            {stopNeedsRefund ? (
              /* Nothing to list: no act is kept and none is put aside. What the reader needs is the money. */
              <RefundMethodField id="plan-stop-refund-method" value={stopRefundMethod} onChange={setStopRefundMethod} />
            ) : stopWouldCancel ? (
              /*
                The motif is asked for here and the same press cancels — this used to be a hand-off to another
                dialog. A real `<Label htmlFor>`: it is printed on the cancelled devis and read by whoever picks
                the file up later. « (obligatoire) » is the reason the confirm is grey (m12).
              */
              <div className="space-y-1.5">
                <Label htmlFor="plan-cancel-reason">Motif d&apos;annulation (obligatoire)</Label>
                <Textarea
                  id="plan-cancel-reason"
                  value={cancelReason}
                  onChange={(e) => setCancelReason(e.target.value)}
                  placeholder="Ex. : patient a renoncé au traitement"
                  rows={3}
                  disabled={stopping}
                />
              </div>
            ) : (
              <>
                {stoppableItems.length > 0 && (
                  <div>
                    <p className="text-2xs font-medium uppercase tracking-wide text-muted-foreground">
                      Mis de côté
                    </p>
                    <ul className="mt-1 space-y-0.5">
                      {stoppableItems.map((i) => (
                        <li key={i.id} className="flex items-baseline justify-between gap-3">
                          <span className="min-w-0 flex-1 [overflow-wrap:anywhere]">
                            {i.designationFr}
                            {/* Stopping lets a parked act's booked séance go (`PlanBookingRelease`). */}
                            {i.scheduledAppointmentId && (
                              <span className="text-warning-ink"> · RDV libéré</span>
                            )}
                          </span>
                          <span className="shrink-0 text-2xs tabular-nums text-muted-foreground">
                            {formatDT(itemNetCost(i))}
                          </span>
                        </li>
                      ))}
                    </ul>
                  </div>
                )}

                {/* What is kept, stated as plainly as what is put aside. */}
                <div>
                  <p className="text-2xs font-medium uppercase tracking-wide text-muted-foreground">
                    Conservés
                  </p>
                  <ul className="mt-1 space-y-0.5">
                    {keptItems.map((i) => (
                      <li key={i.id} className="flex items-baseline justify-between gap-3">
                        <span className="min-w-0 flex-1 [overflow-wrap:anywhere]">{i.designationFr}</span>
                        <span className="shrink-0 text-2xs tabular-nums text-muted-foreground">
                          {formatDT(itemNetCost(i))}
                        </span>
                      </li>
                    ))}
                  </ul>
                </div>

                {/* « Nouveau » only when the stop actually changes the price. */}
                <p className="rounded-md bg-muted/50 p-2.5 text-xs text-muted-foreground">
                  {Math.abs(keptTotal - plan.totalPlanned) > 0.0005 ? "Nouveau prix du traitement" : "Prix du traitement"}
                  {" : "}
                  <b className="tabular-nums text-foreground">{formatDT(keptTotal)}</b>
                  {billed && plan.linkedInvoiceNumber && (
                    <> · note n° {plan.linkedInvoiceNumber} inchangée (avoir si besoin)</>
                  )}
                </p>
              </>
            )}
          </div>

          <AlertDialogFooter>
            <AlertDialogCancel disabled={stopping}>Retour</AlertDialogCancel>
            {/*
              ⚠️ **One action, and the branch is the plan's own — it used to be a hand-off to a second dialog.**

              With nothing delivered on a numbered devis the stop IS a cancellation (`stopWouldCancel`, mirroring
              `TreatmentPlan.StopWouldCancel`), so the motif is asked for here rather than by closing this dialog
              and opening another one the dentist has to find their way back into. On an un-numbered treatment
              the same press stops normally and asks for nothing — there is no document and nothing to explain.

              The confirm stays `disabled` until the motif is typed rather than refusing afterwards: the rule the
              form can enforce should never be discovered by breaking it (`payment-modal.tsx`'s reason).
            */}
            {/* G3: the refund branch has its action now — the money is given back today, then the stop lands. */}
            {(
              <AlertDialogAction
                variant="destructive"
                disabled={stopping || (stopWouldCancel && !cancelReason.trim())}
                onClick={(event) => {
                  event.preventDefault()
                  void stopTreatment()
                }}
              >
                {stopping
                  ? (stopWouldCancel ? "Annulation…" : "Arrêt…")
                  : stopNeedsRefund
                    ? "Rendre et arrêter"
                    : (stopWouldCancel ? "Annuler le devis" : "Arrêter le traitement")}
              </AlertDialogAction>
            )}
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
      {refundDialog}
    </div>
  )
}

function Figure({
  label,
  value,
  hint,
  emphasis = false,
}: {
  label: string
  value: string
  hint?: string
  /** « Reste à payer » — the one figure reception reads for. */
  emphasis?: boolean
}) {
  return (
    // `basis-28` + `min-w-0`: three tiles share a row wherever there is room and fall to one or two where
    // there is not, instead of being forced into halves that cannot hold their own labels.
    <div className="min-w-0 flex-1 basis-28">
      <p className="text-xs text-muted-foreground">{label}</p>
      <p className={cn("text-lg font-bold tabular-nums", emphasis && "text-xl text-primary")}>{value}</p>
      {/* A second, quieter line — for the part of the figure the figure itself cannot carry. */}
      {hint && <p className="text-2xs text-primary">{hint}</p>}
    </div>
  )
}
