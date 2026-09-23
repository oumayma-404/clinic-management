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
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
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
  ArrowLeft, Ban, CreditCard, FileDown, Loader2, ReceiptText, CheckCheck, ClipboardCheck, FilePen,
  CalendarClock, CalendarPlus, ChevronRight, Layers, ListChecks, MoreHorizontal, X, Undo2,
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
  nextStepOf,
  planItemToPreset,
  planSeanceProgress,
  planWorkProgress,
  schedulablePlanItems,
  stopNeedsRefundFirst,
  stopWouldCancelPlan,
} from "./plan-next-action"
import { PlanProgressBar } from "./plan-progress-bar"
import {
  PlanActPrimaryAction, PlanActReorderControls, PlanActRow, PlanActSelectionBox, PlanActStateBadge,
  PlanActStepsAction, PlanActEditAction, PlanActWithdrawAction, PlanActDiscountAction, planActCardFields,
} from "./plan-act-row"
import { PlanStepStrip } from "./plan-step-strip"
import { PlanItemStepsDialog } from "./plan-item-steps-dialog"
import { PlanTimeline } from "./plan-timeline"
import { InstallmentPaymentModal } from "./installment-payment-modal"
import { ReviseInstallmentsModal } from "./revise-installments-modal"
import { VoidInstallmentPayment } from "./void-installment-payment"
import { SettlePlanModal } from "./settle-plan-modal"
import { TreatmentPlanFormModal } from "./treatment-plan-form-modal"
import { CreateAppointmentDialog, type PresetPlanAct } from "@/components/create-appointment-dialog"

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
  if (inst.isAutoRaised) {
    return (
      <Badge variant="outline" className="font-normal text-muted-foreground">
        Solde à régler
      </Badge>
    )
  }
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
  if (inst.isAutoRaised) {
    return (
      <span className="text-muted-foreground">
        {installmentDueLabel(inst)}
        <span className="hidden sm:inline"> — aucune échéance convenue</span>
      </span>
    )
  }
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
  /** The act whose remise is being set (S2); null = dialog closed. */
  const [discountTarget, setDiscountTarget] = useState<TreatmentPlanItemDto | null>(null)
  const [discountDraft, setDiscountDraft] = useState("")
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
  const bookedVisitsNotice =
    bookedVisitCount === 0
      ? null
      : bookedVisitCount === 1
        ? "Le rendez-vous prévu sera libéré."
        : `Les ${bookedVisitCount} rendez-vous prévus seront libérés.`
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
  /** Opens « Remise » on one act, seeded with what it already carries — 0 is how one is cleared. */
  const openDiscount = (item: TreatmentPlanItemDto) => {
    setDiscountDraft(itemDiscount(item) > 0 ? formatAmount(itemDiscount(item)) : "")
    setDiscountTarget(item)
  }

  const openAmend = (item?: TreatmentPlanItemDto) => {
    setAmendFocusItemId(item?.id ?? null)
    setAmendOpen(true)
  }
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

  // Step-weighted progress for the header — see `planWorkProgress`.
  const work = useMemo(() => planWorkProgress(plan), [plan])
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
   * The teeth this treatment is on, for the identity line.
   *
   * <p>The union of the **active** acts' own `toothNumbers` — the devis lines, not
   * `treatedToothNumbers`, which is the whole SÉANCE's teeth and would put a tooth on the header that no act
   * of this plan touches (the union `teethUnderTreatment` measured wrong and documents). A parked act
   * contributes nothing: its teeth are not under treatment any more.</p>
   */
  const treatedTeeth = useMemo(() => {
    const seen = new Set<number>()
    for (const item of liveItems) {
      for (const t of item.toothNumbers ?? []) seen.add(t)
    }
    return seen.size > 0 ? [...seen].sort((a, b) => a - b).join(", ") : null
  }, [liveItems])

  /**
   * One pip per **séance**, in plan order: a step of a stepped act, or the single visit a step-less act is.
   *
   * <p>⚠️ Séances, not acts — `plan-act-pips.tsx` is act-based, and an act is only `Done` once every step is,
   * so a six-visit implant showed one grey pip from its first appointment to its last. Order is the plan's own
   * (`sequenceNumber`), never sorted by état: an act whose 1st and 3rd séances are done while the 2nd is not is
   * telling you something got skipped.</p>
   *
   * <p>⚠️ The **next** pip is the first undone one, and it is marked so the block says what is coming as well as
   * what is behind. Past 14 séances the block falls back to the bar — a row of twenty dots is a smear, which is
   * the same threshold and the same reason `PlanActPips` records.</p>
   */
  const seancePips = useMemo(() => {
    const pips: ("done" | "next" | "todo")[] = []
    for (const item of liveItems) {
      const steps = item.steps ?? []
      if (steps.length === 0) {
        pips.push(item.status === "Done" ? "done" : "todo")
        continue
      }
      for (const step of steps) pips.push(step.doneDate ? "done" : "todo")
    }
    const next = pips.indexOf("todo")
    if (next >= 0) pips[next] = "next"
    return pips
  }, [liveItems])

  /**
   * The act whose next séance the header hoists — the plan's own first bookable one.
   *
   * <p>⚠️ **`schedulablePlanItems`, never `plan.items[0]`.** That is the gate the booking dialog's
   * `planIdByItem` is built from, and a priced « travail restant » makes a continuation's first act `Done` on
   * creation — so `items[0]` is dropped from the map and the save is refused outright with « Le plan de
   * traitement est requis pour lier l'acte. » `check:responsive`'s N28 holds it.</p>
   *
   * <p>Any *other* bookable act keeps its own « Planifier » on its row: the header hoists the nearest séance,
   * the rest stay where they are.</p>
   */
  /*
   * ⚠️ **Any state, not `to-schedule` only — filtering on that arm made the header LIE.** Caught in the eye
   * pass: a prothèse at « 1 séance sur 6 faite » whose act was `to-record` (the visit happened, the fiche is
   * not written) matched nothing, fell through to the last arm and printed « TRAVAIL TERMINÉ · Tous les actes
   * sont réalisés » — beside « 1 séance sur 6 faite » and « 0 acte sur 1 réalisé », two figures contradicting
   * it, on the block whose whole job is to say where the treatment stands. `NextSeanceBlock` branches on the
   * act's own état instead, so « terminé » is reachable only when there is genuinely no act left.
   */
  const nextSchedulable = useMemo(
    () => (isActive ? schedulablePlanItems(plan)[0] ?? null : null),
    [plan, isActive],
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
   * <b>Why</b> « Encaisser » is not on any row — the sentence, derived from the same three terms as the rule
   * above so it cannot name the wrong one.
   *
   * <p>⚠️ The notice was gated on `billed` alone, so a « Sans devis » treatment carrying échéances (or a
   * cancelled one) rendered rows with no button and no explanation at all. Null when the rows are collectable,
   * which is the ordinary case and renders nothing.</p>
   */
  const noCollectReason = canCollectInstallments
    ? null
    : isDraft
      ? "Ce traitement n'a pas de devis édité. Les échéances deviennent exigibles avec « Éditer le devis » — "
        + "un encaissement en séance se saisit sur la fiche de soins."
      : plan.status === "Cancelled"
        ? "Ce devis est annulé : plus rien ne s'encaisse dessus. « Rétablir ce devis annulé » le remet en service."
        : isPlanWrittenOff(plan.status)
          ? "La créance de ce devis a été abandonnée : plus rien ne s'encaisse dessus. « Reprendre le "
            + "traitement » la remet en service."
        : `Ce devis est facturé${plan.linkedInvoiceNumber ? ` (note n° ${plan.linkedInvoiceNumber})` : ""}. `
          + "Les paiements s'enregistrent désormais sur la note d'honoraires — un encaissement saisi ici "
          + "n'apparaîtrait ni dans la caisse ni dans les recettes."

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

  /** How the devis is named in a confirmation sentence — the number when it has one, else « ce plan ». */
  const planLabel = plan.number ? `Le devis ${plan.number}` : "Ce plan"

  /*
   * The plan-level confirmations. Each names the devis and states the consequence the button label cannot.
   *
   * ⚠️ `confirmAccept` was here and was **dead code** — nothing in the workspace referenced it, because the only
   * « Accepter le devis » a dentist can press lives on the patient band. It is removed rather than wired up: on
   * this screen the acceptance door is « Éditer le devis », which is the same transition named for what it
   * actually does to the document.
   *
   * ⚠️ `confirmComplete` was here too. « Terminer le traitement » is gone from every surface — completion is
   * derived (`AdvanceAfterWorkRecorded` closes a plan when its last act lands), and the one case the manual
   * button served, closing with acts left unrealised, is what « Arrêter le traitement » means, except that it
   * parks them reversibly instead of abandoning them. No capability was lost; one verb was.
   */
  /**
   * « Éditer le devis » — take the number.
   *
   * <p>Confirmed, and this is the one thing in the treatment flow that still is: a number is gapless,
   * per-clinic-per-year and can only be released by a cancellation carrying a motif. Everything else about a
   * treatment is now free precisely so that this one press can be deliberate.</p>
   */
  const confirmIssueDevis = () =>
    setConfirmAction({
      title: "Éditer le devis ?",
      description: (
        <>
          Un numéro de devis sera attribué à {planLabel} et l&apos;échéancier devient exigible — c&apos;est le
          document que le patient reçoit. Le numéro est définitif : une erreur s&apos;annule avec un motif, elle
          ne se supprime pas.
          {plan.totalPlanned > 0 && (
            <> Total : {formatDT(plan.totalPlanned)}.</>
          )}
        </>
      ),
      confirmLabel: "Éditer le devis",
      onConfirm: async () => {
        await run(
          () => treatmentPlansApi.issueDevis(plan.id, plan.version),
          "Devis édité",
          "Échec de l'édition du devis.",
        )
      },
    })

  const confirmBill = () =>
    setConfirmAction({
      title: "Facturer ce devis ?",
      description: (
        <>
          Une note d&apos;honoraires en brouillon sera créée et vous serez redirigé vers Factures.
          {plan.amountPaid > 0 && (
            <>
              {" "}
              {/* The carry-over happens at ISSUE, not at draft creation — a draft invoice cannot hold payments.
                  Said here as well as in the success toast: the toast arrives on the /factures page, after the
                  navigation, which is too late to be a decision. */}
              Les {formatDT(plan.amountPaid)} déjà encaissés sur ce devis seront reportés sur la facture à son
              émission, pas sur le brouillon.
            </>
          )}
        </>
      ),
      confirmLabel: "Créer la facture",
      onConfirm: async () => {
        await run(
          async () => {
            await invoicesApi.createFromPlan(plan.id)
            router.push("/factures")
          },
          plan.amountPaid > 0
            ? `Facture brouillon créée — ${formatDT(plan.amountPaid)} déjà encaissé sera reporté à l'émission`
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
            ? `Traitement arrêté — ${parked} acte${parked > 1 ? "s" : ""} mis de côté, à reprendre si le patient revient.`
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
      "Devis rétabli — son numéro et son motif d'origine sont conservés.",
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
      `${item.designationFr} mis de côté — le total et l'échéancier sont ajustés.`,
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
      "Note détachée — ce devis porte de nouveau son propre solde.",
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
      "Créance passée en perte — les encaissements déjà reçus sont conservés.",
      "Échec de la mise en perte de la créance.",
    )
    setWriteOffOpen(false)
    setWriteOffReason("")
  }

  /** « Remise » on one act (S2). 0 clears it; the server caps it at the act's tarif and re-spreads. */
  const saveDiscount = async () => {
    if (!discountTarget) return
    const parsed = parseAmountInput(discountDraft)
    if (!Number.isFinite(parsed) || parsed < 0) {
      showErrorToast(new Error("La remise doit être un montant positif, ou 0 pour l'annuler."))
      return
    }
    const outcome = await run(
      () => withRefund((refundMethod) =>
        treatmentPlansApi.setItemDiscount(plan.id, discountTarget.id, parsed, plan.version, refundMethod)),
      parsed > 0
        ? `Remise de ${formatDT(parsed)} accordée sur ${discountTarget.designationFr}.`
        : "Remise retirée.",
      "Échec de l'enregistrement de la remise.",
    )
    if (outcome !== "declined") setDiscountTarget(null)
  }

  /** « Dupliquer ce devis » (S1) — a new un-numbered Draft, and the page follows it. */
  const duplicatePlan = async () => {
    setBusy(true)
    try {
      const copy = await treatmentPlansApi.duplicate(plan.id)
      toast.success("Devis dupliqué — la copie est un brouillon sans numéro.")
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
        <>
          {planLabel} repassera « en cours »
          {withdrawnItems.length > 0 ? (
            <>
              {" "}et {withdrawnItems.length === 1 ? "l'acte mis de côté revient" : `les ${withdrawnItems.length} actes mis de côté reviennent`} au
              devis, avec les séances déjà réalisées et leurs fiches de soins.
            </>
          ) : (
            "."
          )}{" "}
          L&apos;échéancier n&apos;est pas rétabli : ajustez-le avec « Modifier l&apos;échéancier » une fois les
          séances à venir replanifiées.
        </>
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
   * The ONE action offered as a button; everything else lives in the « ⋯ » menu.
   *
   * <p>Derived from the state rather than listed per status, so the header cannot end up offering two answers to
   * « et maintenant ? ». The order is the order a treatment actually moves in:</p>
   * <ol>
   *   <li><b>Sans devis</b> → « Éditer le devis ». The only place a devis number is taken, and the moment the
   *       money becomes a claim. Nothing else on a followed treatment competes with it.</li>
   *   <li><b>Facturable</b> → « Facturer le devis » — see `canBill`, which is deliberately wider than `isActive`
   *       because a plan auto-completes the instant its last step lands, i.e. exactly when it becomes billable.</li>
   *   <li><b>Terminé</b> → « Reprendre le traitement », the way back from a stop.</li>
   * </ol>
   * <p>An accepted devis mid-treatment gets <b>no</b> primary button, and that is right: what it needs next is a
   * séance, which is booked from the acts below, not from this row.</p>
   */
  const primaryAction = useMemo(() => {
    /*
     * ⚠️ **`Cancelled` had no primary action and no action at all — see `uncancelPlan`.** It is first because
     * on a cancelled devis there is exactly one thing to decide, and « Devis PDF » was the only control left.
     */
    if (canUncancelPlan(plan)) {
      return {
        label: "Rétablir ce devis annulé",
        icon: RotateCcw,
        run: () => {
          setUncancelReason("")
          setUncancelOpen(true)
        },
      }
    }
    if (isDraft) {
      return { label: "Éditer le devis", icon: ClipboardCheck, run: confirmIssueDevis }
    }
    /*
     * ⚠️ **Closed-and-reopenable is tested BEFORE billable, and the other order was a dead end.**
     * `canBill` is true of a stopped devis, so with the tests the other way round a treatment the patient
     * abandoned offered « Facturer le devis » and « Reprendre le traitement » was reachable from nowhere in the
     * product. On a followed treatment it was worse: every act is parked, `ActiveItems` is empty, and the server
     * refuses the facturation too — no way forward and no way back.
     */
    /*
     * ⚠️ `WrittenOff` shares this arm with `Stopped`, and that is what stops it becoming a second absorbing
     * state — the defect « Rétablir ce devis annulé » had to be written for. Reopening restores the parked
     * acts, clears the write-off record and brings the créance back.
     */
    if (isStopped || isPlanWrittenOff(plan.status)) {
      return { label: "Reprendre le traitement", icon: RotateCcw, run: confirmReopen }
    }
    /*
     * ⚠️ And « Facturer » is the primary only once the work is done. It stays available in the « ⋯ » menu at
     * every other moment — the capability is unchanged — but offered as *the* thing to do next, on a treatment
     * with séances still to come, it reads as advice: bill before you have delivered. 25 of the 34 notes raised
     * from a devis on the dev database were raised on a plan still in progress.
     */
    if (canBill && actsRemaining === 0) {
      return { label: "Facturer le devis", icon: ReceiptText, run: confirmBill }
    }
    /*
     * ⚠️ **`Completed` gets NO filled button, and it used to get « Reprendre le traitement ».** A filled
     * primary is this app's grammar for « this is what to do next », and nothing is recommended on a treatment
     * carried to term — least of all reopening it. The verb is not lost: it moved into the « ⋯ » menu, where a
     * dentist who genuinely needs it will look. `Stopped` keeps it as the primary above, because there it IS
     * the answer to « et maintenant ? ».
     */
    return null
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the confirm openers are stable per render
  }, [isDraft, isStopped, canBill, actsRemaining, plan, plan.status, plan.version, plan.totalPlanned, plan.amountPaid])

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
      {/* router.push, never router.back(): the workspace is reachable from /factures, the patient page and
          the plans list, and "back" to a different surface than the one the button names is disorienting.
          router.back() has zero uses in this codebase. */}
      <Button variant="ghost" size="sm" className="gap-2" onClick={() => router.push("/treatment-plans")}>
        <ArrowLeft className="h-4 w-4" />
        Retour aux plans
      </Button>

      {/* ---- Header -------------------------------------------------------------------------------- */}
      <Card>
        <CardHeader className="pb-3">
          <div className="flex flex-wrap items-center justify-between gap-3">
            {/*
              ⚠️ **The TREATMENT is the title; the devis number moved to the identity line below.** This read
              `plan.number ?? plan.title`, so on every numbered devis the largest text on the page was a gapless
              accounting reference — « 2026-0028 » — and the thing it is about was the small print. A dentist
              opens this page to see where an implant has got to.
            */}
            <CardTitle className="flex flex-wrap items-center gap-2 text-xl [overflow-wrap:anywhere]">
              {plan.title || plan.number || "Traitement"}
              <Badge variant="secondary" className={planStatusBadgeClass(plan.status, planHasRecordedWork(plan))}>
                {planStatusLabel(plan.status, planHasRecordedWork(plan))}
              </Badge>
              {billed && (
                <Badge variant="outline">
                  Facturé{plan.linkedInvoiceNumber ? ` — ${plan.linkedInvoiceNumber}` : ""}
                </Badge>
              )}
            </CardTitle>
            {/*
              ⚠️ **ONE primary action and a « ⋯ » menu, where this row used to hold SEVEN buttons.**

              Measured, and identical at every width from 320 to 1440: « Facturer le devis · Modifier le devis ·
              Arrêter le traitement · Terminer · Devis PDF · Envoyer par e-mail · Annuler ». Seven controls of
              equal weight state that seven things are equally likely, which is never true — at any moment there
              is one thing to do and six things to be able to find. Two of them were also, side by side on a
              followed treatment, « Éditer le devis » and « Modifier le devis »: near-identical French for
              minting a gapless numbered financial document and for correcting an act's price.

              The menu is the pattern this feature already uses for the plans list and for an échéance's own
              actions, so nothing new is being learnt here.
            */}
            <div className="flex flex-wrap items-center gap-2">
              {busy && <Loader2 className="h-4 w-4 animate-spin" />}

              {/*
                The one act, by state — `primaryAction`. Filled, so it reads as the answer to « et maintenant ? »
                rather than as one option among several.
              */}
              {primaryAction && (
                <Button size="sm" className="gap-2" disabled={busy} onClick={primaryAction.run}>
                  <primaryAction.icon className="h-4 w-4" />
                  {primaryAction.label}
                </Button>
              )}

              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <Button
                    variant="outline"
                    size="sm"
                    className="gap-2 touch-target"
                    disabled={busy}
                    aria-label="Autres actions sur ce devis"
                  >
                    <MoreHorizontal className="h-4 w-4" />
                    <span className="hidden sm:inline">Actions</span>
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end" className="w-64">
                  {/* Le document — always available, never destructive, and what a dentist reaches for with the
                      patient in the chair. */}
                  <DropdownMenuItem onSelect={handleDownloadDevis} disabled={busy}>
                    <FileDown className="h-4 w-4" />
                    Devis PDF
                  </DropdownMenuItem>

                  {/*
                    Le traitement. ⚠️ « Modifier les actes et les prix », not « Modifier le devis » — see the
                    header note: the old label was a homophone of « Éditer le devis » and named the document
                    rather than what it edits. `canAmend` is unchanged; only the wording is.
                  */}
                  {(canAmend || isActive || plan.status === "Completed") && <DropdownMenuSeparator />}
                  {canAmend && (
                    <DropdownMenuItem disabled={busy} onSelect={() => openAmend()}>
                      <FilePen className="h-4 w-4" />
                      Modifier les actes et les prix
                    </DropdownMenuItem>
                  )}
                  {/* J5 — « Facturer » is the header's button only once the work is done; at every other moment it
                      lives here, as the primary action's own note has always said. It was simply missing. */}
                  {canBill && primaryAction?.label !== "Facturer le devis" && (
                    <DropdownMenuItem disabled={busy} onSelect={confirmBill}>
                      <ReceiptText className="h-4 w-4" />
                      Facturer le devis
                    </DropdownMenuItem>
                  )}
                  {/* AC-P2.1 — the amendable window, matching the server's widened `EnsureAmendable`. */}
                  {isActive && (
                    <DropdownMenuItem disabled={busy} onSelect={() => setStopOpen(true)}>
                      <CircleSlash className="h-4 w-4" />
                      Arrêter le traitement
                    </DropdownMenuItem>
                  )}
                  {/* S1 — « Dupliquer ». Beside « Devis PDF » rather than with the destructive block: it
                      consumes no number, claims no money and changes nothing about this devis. */}
                  <DropdownMenuItem disabled={busy} onSelect={() => setDuplicateOpen(true)}>
                    <Copy className="h-4 w-4" />
                    Dupliquer ce devis
                  </DropdownMenuItem>
                  {/* m13 — « Reprendre le traitement » on a plan carried to term is a capability, never a
                      recommendation, so it lives here rather than as the header's filled button. On a STOPPED
                      plan it is still the primary: there it is the answer to « et maintenant ? ». */}
                  {plan.status === "Completed" && (
                    <DropdownMenuItem disabled={busy} onSelect={confirmReopen}>
                      <RotateCcw className="h-4 w-4" />
                      Reprendre le traitement
                    </DropdownMenuItem>
                  )}
                  {/* M6 — the praticien was invisible and permanent: `TreatmentPlanDto` carried no doctor field
                      at all, and the note d'honoraires snapshots it, so in a two-dentist cabinet every dinar
                      was attributed to the wrong person for ever. */}
                  <DropdownMenuItem
                    disabled={busy}
                    onSelect={() => {
                      setDoctorDraft(plan.doctorId ?? "")
                      setDoctorOpen(true)
                    }}
                  >
                    <Stethoscope className="h-4 w-4" />
                    Changer le praticien
                  </DropdownMenuItem>
                  {/* M5 — a devis on the wrong patient was unfixable: no mutator, and a disabled field in every
                      edit mode. The server refuses once work is delivered or a live note names it. */}
                  <DropdownMenuItem
                    disabled={busy}
                    onSelect={() => {
                      setPatientQuery("")
                      setPatientResults(null)
                      setPatientDraft(null)
                      setReassignOpen(true)
                    }}
                  >
                    <UserCog className="h-4 w-4" />
                    Changer de patient
                  </DropdownMenuItem>
                  {/* M3 — « détachez-la de ce traitement » is named by three server refusals and had no route:
                      the detach's only callers were the cancel and the stop, so releasing a note meant killing
                      the treatment. */}
                  {billed && plan.linkedInvoiceId && (
                    <DropdownMenuItem disabled={busy} onSelect={() => setDetachNoteOpen(true)}>
                      <Unlink className="h-4 w-4" />
                      Détacher la note d&apos;honoraires
                    </DropdownMenuItem>
                  )}
                  {/*
                    ⚠️ **« Annuler le devis » is gone, folded into « Arrêter le traitement » above.**

                    The two were separate buttons asking the dentist a question the system had already answered.
                    « Le patient ne poursuit pas » is one intention; what differs is arithmetic — nothing
                    delivered on a numbered devis ⇒ a cancellation (the number is spent, the document may be in
                    the patient's hands, a motif is owed); anything delivered ⇒ a stop (park the rest, keep what
                    was done). `TreatmentPlan.StopWouldCancel` states that rule once and the stop dialog asks for
                    a motif on exactly that branch. The old dialog already flipped its own confirm button to
                    « Annuler le devis… » on the same condition — and then made the dentist start again in the
                    other dialog.

                    Its predecessor's defect is worth keeping in view: « Annuler » was gated on `isActive`, which
                    includes Draft since « Suivre ce traitement », while `TreatmentPlan.Cancel` throws on exactly
                    that status — so a dentist whose patient declined a followed treatment typed a motif, pressed
                    confirm and was refused. The fold cannot reproduce it: the branch is chosen from the plan, not
                    from which button was pressed.

                    « Supprimer » stays and is a different thing entirely — a correction of a treatment created by
                    mistake, only while no number was consumed and nothing was carried out (`CanBeDeleted`).
                  */}
                  {(canWriteOffPlan(plan) || canCancelPlan(plan) || canDelete) && <DropdownMenuSeparator />}
                  {/*
                    S4 — « Passer la créance en perte ». In the destructive block because it gives up money the
                    cabinet is owed, but NOT `variant="destructive"`: it destroys no record, keeps every
                    encaissement and is undone by « Reprendre le traitement ». AdminOnly server-side.
                  */}
                  {canWriteOffPlan(plan) && (
                    <DropdownMenuItem
                      disabled={busy}
                      onSelect={() => {
                        setWriteOffReason("")
                        setWriteOffOpen(true)
                      }}
                    >
                      <HandCoins className="h-4 w-4" />
                      Passer la créance en perte
                    </DropdownMenuItem>
                  )}
                  {/*
                    ⚠️ **« Annuler le devis » is back — for the cases the fold cannot reach.** Folding it into
                    « Arrêter le traitement » was right for the common shape (the arithmetic decides, not the
                    dentist) but it left a numbered devis with one séance recorded, and every closed devis,
                    with **no** route to an annulation anywhere in the browser: `POST /{id}/cancel` was
                    callerless. A devis issued to the wrong patient is a real case and its number has to be
                    voided with a motif. `canCancelPlan` offers it only where the stop branch cannot.
                  */}
                  {canCancelPlan(plan) && (
                    <DropdownMenuItem
                      variant="destructive"
                      disabled={busy}
                      onSelect={() => {
                        setCancelReason("")
                        setCancelPlanOpen(true)
                      }}
                    >
                      <Ban className="h-4 w-4" />
                      Annuler le devis
                    </DropdownMenuItem>
                  )}
                  {canDelete && (
                    <DropdownMenuItem
                      variant="destructive"
                      disabled={busy}
                      onSelect={() => setDeleteOpen(true)}
                    >
                      <Trash2 className="h-4 w-4" />
                      Supprimer le traitement
                    </DropdownMenuItem>
                  )}
                </DropdownMenuContent>
              </DropdownMenu>
            </div>
          </div>
          {/*
            ⚠️ **ONE identity line, and the devis number is at the END of it.** The title used to be
            `plan.number ?? plan.title` with the patient relegated to a subtitle — so the largest text on the
            screen was a gapless accounting reference, and the treatment and the person it is for were the
            small print under it. Nobody opens this page looking for « 2026-0028 »; they open it to see where
            an implant has got to. The number keeps a home because a patient rings up holding a printout, and
            it is `ms-auto` mono at `text-xs` for the same reason a reference is set that way on paper.
          */}
          {/*
            ⚠️ **The separator lives INSIDE the span it precedes, never as a sibling.** As its own flex item a
            « · » is free to end a wrapped line, and at 320 px it did — the identity line broke after
            « Emna Belhadj · », leaving a dangling middot with nothing after it. Bound to the following item,
            the pair wraps together. Same class of defect as the trailing guillemet `quoteFr` exists for.
          */}
          <p className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5 text-sm text-muted-foreground">
            <PatientNameLink patientId={plan.patientId} name={plan.patientName ?? "Patient"} />
            {treatedTeeth && (
              <span>
                <span aria-hidden="true">· </span>
                dents {treatedTeeth}
              </span>
            )}
            {/* M6 — the praticien, on the line that says whose this devis is. Printed even when absent, since
                « praticien non attribué » is the state that needs correcting and silence hides it. */}
            <span>
              <span aria-hidden="true">· </span>
              {plan.doctorName ? `Dr ${plan.doctorName}` : "praticien non attribué"}
            </span>
            <span>
              <span aria-hidden="true">· </span>
              depuis le {formatDateFr(plan.createdAt)}
            </span>
            {plan.number && (
              <span className="ms-auto font-mono text-xs tabular-nums">
                {plan.number}
                {/* The devis PDF re-renders live from current state under the same number and is archived
                    nowhere, so this counter is the only way a patient's earlier printout can be identified. */}
                {plan.revisionNumber > 0 && ` · rév. ${plan.revisionNumber}`}
              </span>
            )}
          </p>
        </CardHeader>

        <CardContent className="space-y-4">
          {/*
            ⚠️ **Two blocks side by side — « où en est-on ? » and « quoi faire ? » — where this was a bar, four
            money figures and a bare date.** The money moved out entirely (it is « L'argent » below, after the
            acts), because a treatment's page is opened to answer a clinical question and the séance count was
            sitting in a row of dinars as though it were one of them.

            `lg:`, not `md:`: at 820 px the 256 px rail leaves ~532 px, and « Planifier la séance » beside a
            named step does not fit half of that.
          */}
          <div className="grid gap-3 lg:grid-cols-[minmax(0,17rem)_minmax(0,1fr)]">
            <ProgressBlock
              seances={seances}
              fraction={work.fraction}
              itemsDone={plan.itemsDone}
              itemsTotal={plan.itemsTotal}
              withdrawn={withdrawnItems.length}
              pips={seancePips}
            />
            <NextSeanceBlock
              plan={plan}
              isStopped={isStopped}
              isCancelled={plan.status === "Cancelled"}
              isWrittenOff={isPlanWrittenOff(plan.status)}
              nextAct={nextSchedulable}
              onSchedule={(item) => startBooking([[item]])}
            />
          </div>

          {isDraft && (
            <p className="text-xs text-muted-foreground">
              Aucun devis édité — le suivi fonctionne sans. « Éditer le devis » lui attribue un numéro, le jour
              où le patient en demande un.
            </p>
          )}

          {plan.notes && (
            <p className="whitespace-pre-line rounded-md bg-muted/50 p-3 text-sm text-muted-foreground">
              {plan.notes}
            </p>
          )}
          {plan.cancellationReason && (
            /* The theme's own destructive family, not `red-*` literals with a hand-maintained `dark:` twin —
               `--destructive-wash` exists for exactly this pairing and flips with the palette on its own, so the
               two dark: classes this carried are not just redundant, they were a second palette to keep in sync. */
            <p className="rounded-md border border-destructive/25 bg-destructive-wash p-3 text-sm text-destructive">
              Motif d&apos;annulation : {plan.cancellationReason}
            </p>
          )}
        </CardContent>
      </Card>

      {/* ---- Actes --------------------------------------------------------------------------------- */}
      <Card>
        <CardHeader className="pb-3">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <CardTitle className="text-base">Actes</CardTitle>
            {canGroup && selectedActIds.length === 0 && (
              <p className="text-xs text-muted-foreground">
                Cochez plusieurs actes pour les regrouper dans une même séance.
              </p>
            )}
          </div>
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
              className="mb-3 rounded-md border border-warning/40 bg-warning-wash p-3 text-2xs leading-relaxed text-warning-ink"
            >
              <p className="font-medium">
                {(plan.duplicateActs ?? []).length === 1
                  ? "Un acte de ce devis est déjà devisé ailleurs"
                  : `${(plan.duplicateActs ?? []).length} actes de ce devis sont déjà devisés ailleurs`}
              </p>
              <ul className="mt-1 space-y-0.5">
                {(plan.duplicateActs ?? []).map((dup) => (
                  <li key={dup.itemId} className="[overflow-wrap:anywhere]">
                    {dup.designationFr}
                    {dup.toothNumbers.length > 0 && ` (dents ${dup.toothNumbers.join(", ")})`} — aussi sur{" "}
                    <Link
                      href={`/treatment-plans/${dup.otherPlanId}`}
                      className="underline underline-offset-2"
                    >
                      {dup.otherPlanNumber ? `le devis ${dup.otherPlanNumber}` : dup.otherPlanTitle}
                    </Link>
                    .
                  </li>
                ))}
              </ul>
              <p className="mt-1.5">
                Le patient le doit donc deux fois. Si c&apos;est une erreur, annulez ou modifiez l&apos;un des
                deux devis&nbsp;; si c&apos;est voulu, il n&apos;y a rien à faire.
              </p>
            </div>
          )}

          {catalogFailed && (
            <LoadFailureNotice
              variant="inline"
              message="Le catalogue des actes n'a pas pu être chargé."
              detail="Un rendez-vous planifié depuis ce devis partira sans procédure (ni couleur, ni durée)."
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
              title="Aucun acte planifié"
              description={
                canAmend
                  ? "Ce devis ne contient encore aucun acte. Ajoutez-les avec « Modifier les actes et les prix »."
                  : isDraft
                    ? "Ce brouillon ne contient encore aucun acte. Modifiez-le pour en ajouter."
                    : "Ce devis ne contient aucun acte."
              }
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
                      {selectedActIds.length} / {schedulableItems.length} actes à planifier
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
                      /* The strip goes under the act's own name, exactly as it sits in the table — but through
                         `underTitle`, NOT `subtitle`. `subtitle` renders a `<p class="line-clamp-2">`, and a
                         `<div>` inside a `<p>` is invalid: React logged a hydration failure and the browser
                         closed the paragraph early, so the strip left the title column altogether, while the
                         clamp stood ready to cut a fourth step off with no sign. `divider={false}`: the card's
                         own gaps already separate it, and a second dashed rule inside a card reads as a divider
                         between two records. */
                      underTitle={(a) =>
                        a.item.steps && a.item.steps.length > 0 ? (
                          <PlanStepStrip
                            steps={a.item.steps}
                            nextStepId={a.item.nextStepId}
                            divider={false}
                          />
                        ) : undefined
                      }
                      status={(a) => <PlanActStateBadge item={a.item} />}
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
                        ...planActCardFields(a.item),
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
                                onMoveUp: () => handleMove(a.index, -1),
                                onMoveDown: () => handleMove(a.index, 1),
                              }}
                            />
                          ),
                        },
                      ]}
                      /*
                       * ⚠️ The labelled action is on its OWN full-width row (`primaryAction`) and only the
                       * icon-only « Étapes » control stays in the header. Both in the header, the act's name —
                       * which is the card's identity — got what was left of a ~288 px card after ~200 px of
                       * controls: measured at 320 px, « Bridge 4 dents (14-17) » rendered **one character per
                       * line**, a 26-line vertical column of letters. `[overflow-wrap:anywhere]` is what makes
                       * that possible rather than an overflow, so nothing looks broken from the code's side.
                       * This is verbatim the case `CardList.primaryAction` documents — « the action a user
                       * opens the page to perform » — and planning the next étape is why this screen exists.
                       */
                      actions={(a) => {
                        const steps = canCorrectActs ? (
                          <PlanActStepsAction item={a.item} onEditSteps={setStepsTarget} />
                        ) : null
                        // « Modifier » sits beside « Séances » in the card header for the same reason it
                        // does in the row: the act is where a dentist looks for it.
                        const edit = canAmend ? (
                          <PlanActEditAction item={a.item} onEdit={openAmend} />
                        ) : null
                        const park = canWithdrawItem(a.item) ? (
                          <PlanActWithdrawAction item={a.item} onWithdraw={setWithdrawTarget} />
                        ) : null
                        const remise = canDiscountItem(a.item) ? (
                          <PlanActDiscountAction item={a.item} onDiscount={openDiscount} />
                        ) : null
                        if (!steps && !edit && !park && !remise) return undefined
                        return (
                          <span className="flex items-center gap-1">
                            {steps}
                            {edit}
                            {remise}
                            {park}
                          </span>
                        )
                      }}
                      primaryAction={(a) => (
                        <PlanActPrimaryAction
                          plan={plan}
                          item={a.item}
                          onSchedule={(target) => startBooking([[target]])}
                          onUndo={canCorrectActs ? setUndoTarget : undefined}
                          onWithdraw={canWithdrawItem(a.item) ? setWithdrawTarget : undefined}
                          onRestore={canAmend ? setRestoreTarget : undefined}
                          block
                        />
                      )}
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
                          aria-label="Sélectionner tous les actes à planifier"
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
                    <TableHead className="whitespace-nowrap text-right">Coût</TableHead>
                    <TableHead>État</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {plan.items.map((item, index) => (
                    <PlanActRow
                      key={item.id}
                      plan={plan}
                      item={item}
                      onSchedule={(target) => startBooking([[target]])}
                      onUndo={canCorrectActs ? setUndoTarget : undefined}
                      onEditSteps={canCorrectActs ? setStepsTarget : undefined}
                      onEdit={canAmend ? openAmend : undefined}
                      onWithdraw={canWithdrawItem(item) ? setWithdrawTarget : undefined}
                      onRestore={canAmend ? setRestoreTarget : undefined}
                      onDiscount={canDiscountItem(item) ? openDiscount : undefined}
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
                              onMoveUp: () => handleMove(index, -1),
                              onMoveDown: () => handleMove(index, 1),
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
            ⚠️ The correction sentence names « Détacher la fiche », which renders only on an act that is
            entirely réalisé — so on a plan whose acts are « en cours » it pointed at a control that is not on
            the screen. For a stepped act the correction lives in the « Étapes » dialog, per step, which is also
            the honest place for it: the act-level « Détacher » undoes only the LAST séance recorded.
          */}
          <p className="mt-2 text-xs text-muted-foreground">
            Un acte passe à « Réalisé » à l&apos;enregistrement de la fiche de soins liée — il n&apos;y a pas de
            bascule manuelle.{" "}
            {liveItems.some((i) => (i.steps?.length ?? 0) > 0)
              ? "Une séance cochée par erreur se détache de sa fiche depuis « Étapes », sur la ligne de l'acte ; un acte entièrement réalisé porte « Détacher la fiche »."
              : "Un acte coché par erreur se corrige avec « Détacher la fiche », qui le ramène à « Prévu » et réouvre le devis si celui-ci s'était clos dessus."}
          </p>
        </CardContent>
      </Card>

      {/* ---- L'argent ------------------------------------------------------------------------------- */}
      {/*
        ⚠️ **« L'argent », and it comes AFTER the acts.** It was « Échéancier », above nothing and below a
        header carrying four money figures — so the page opened on dinars and the treatment came third. The
        three figures moved here, where the payments they summarise already are; the card is the whole money
        answer in one place instead of a headline in one card and its detail in another.
      */}
      <Card>
        <CardHeader className="pb-3">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <CardTitle className="text-base">L&apos;argent</CardTitle>
            {/* AC-P2.5 — `PUT /installments` was equally callerless, so a patient who could no longer pay on
                the agreed dates had to have the devis cancelled and retyped. Same window as the amendment. */}
            <div className="flex flex-wrap items-center gap-2">
              {/*
                S3 — « Régler le devis ». One encaissement spread over the échéancier, where the only route was
                « Encaisser » once per row: `Installment.RecordPayment` refuses more than a row's remainder, so
                a patient settling three instalments at the desk went through three dialogs and three receipts.
                Offered only when there is something to collect AND the rows can take it — the same
                `canCollectInstallments` the rows themselves read, so the button and the rows agree.
              */}
              {canCollectInstallments && owed && !owed.isBilled && owed.amount > 0.0005 && (
                <Button size="sm" className="gap-2" disabled={busy} onClick={() => setSettleOpen(true)}>
                  <Wallet className="h-4 w-4" />
                  Régler le devis
                </Button>
              )}
              {canAmend && (
                <Button
                  size="sm"
                  variant="outline"
                  className="gap-2"
                  disabled={busy}
                  onClick={() => setReviseOpen(true)}
                >
                  <CalendarClock className="h-4 w-4" />
                  Modifier l&apos;échéancier
                </Button>
              )}
            </div>
          </div>
        </CardHeader>
        <CardContent>
          {/*
            The three figures, moved out of the header. « Reste à encaisser » vs « Reste dû » is the same
            distinction `InstallmentLateness` makes and it is now said in the label rather than left to the
            reader: while the work is under way the balance is collected séance by séance and is not a debt;
            once the treatment is finished or stopped, it is.

            ⚠️ **`displayedOutstanding`, never `plan.outstanding`.** A devis a note d'honoraires collects has an
            auto-raised échéance that will never see a payment, so its own `outstanding` reports the whole devis
            as unpaid — measured on 4 of 4 bridged plans, two of them fully settled, one patient shown
            « Solde dû 31,000 DT » in their file header and « Reste 120,000 DT » here on the same page.
          */}
          {/*
            ⚠️ **When a note carries part of this treatment, the headline is the TREATMENT and not the devis.**
            That is the one change the reported gap actually asked for: « Total convenu 10,000 · Encaissé 0,000 »
            was a true statement about the devis and a false one about the treatment in front of the dentist.
            The devis' own three figures are not lost — they move into the composition below, beside the note's,
            so every number on this screen still has exactly one owner.
          */}
          {/*
            ⚠️ **`flex-wrap` with a real `basis`, never a bare `grid-cols-2`.** « Total des deux documents » in
            a 320 px half-column wrapped to three lines above its figure, so the three tiles were a block of
            labels with dinars scattered through it. A wrapping row lets a long label take the full width and
            drop its neighbours below, which is what the reader wants at that size.
          */}
          {treatmentMoney ? (
            <div className="mb-4 flex flex-wrap gap-4">
              {/*
                ⚠️ **« Total du traitement » only when the note bills this treatment ALONE.** A note is raised
                per *fiche*, so one that also bills a détartrage done the same séance holds money this treatment
                has nothing to do with — calling that sum « le traitement » would overstate it, which is the same
                class of false statement this whole card exists to remove. « Reste à encaisser » needs no such
                care: what the patient still owes across the two documents is true either way, which is the
                figure a dentist is actually reading.
              */}
              <Figure
                label={treatmentMoney.mixed ? "Total des deux documents" : "Total du traitement"}
                value={formatDT(treatmentMoney.total)}
                hint={
                  treatmentMoney.mixed
                    ? "la note couvre aussi d'autres actes"
                    : carried.length === 1
                      ? "devis + note d'honoraires"
                      : "devis + notes d'honoraires"
                }
              />
              <Figure label="Encaissé" value={formatDT(treatmentMoney.collected)} />
              <Figure
                label={isActive ? "Reste à encaisser" : "Reste dû"}
                value={formatDT(treatmentMoney.outstanding)}
                hint="sur les deux documents"
              />
            </div>
          ) : (
            <div className="mb-4 flex flex-wrap gap-4">
              {/*
                S2 — the remise is a SECOND figure beside the total, never a lower total on its own. That is the
                whole reason it is a field: « Total convenu 350,000 » alone is a number nobody can account for,
                and the practice cannot report what it gave away. Rendered only when there is one, so a devis
                that grants none looks exactly as it always did.
              */}
              <Figure
                label="Total convenu"
                value={formatDT(plan.totalPlanned)}
                hint={
                  (plan.totalDiscount ?? 0) > 0.0005
                    ? `${formatDT(plan.totalGross ?? plan.totalPlanned)} − ${formatDT(plan.totalDiscount ?? 0)} de remise`
                    : undefined
                }
              />
              <Figure label="Encaissé" value={formatDT(plan.amountPaid)} />
              {owed && (
                <Figure
                  label={isActive ? "Reste à encaisser" : "Reste dû"}
                  value={formatDT(owed.amount)}
                  hint={
                    owed.isBilled
                      ? `sur la note ${owed.invoiceNumber ?? "d'honoraires"}`
                      : isActive
                        ? "au fil des séances"
                        : undefined
                  }
                />
              )}
            </div>
          )}

          {/*
            S4 — what was given up, said on the card that answers « où en est l'argent ? ». A written-off devis
            is otherwise indistinguishable from a settled one here: `CarriesDebt` is false, so « Reste » reads
            0 and nothing on the money card says why. The motif travels with it, because that is the evidence.
          */}
          {isPlanWrittenOff(plan.status) && (
            <p role="note" className="mb-4 rounded-md border bg-muted/40 p-3 text-xs text-muted-foreground">
              Créance abandonnée&nbsp;: {formatDT(plan.writeOffAmount ?? 0)} ne seront pas réclamés
              {plan.writeOffReason ? ` — ${plan.writeOffReason}` : ""}. Les{" "}
              {formatDT(plan.amountPaid)} déjà encaissés sont conservés, dans la caisse comme sur le reçu.
              « Reprendre le traitement » rétablit la créance.
            </p>
          )}

          {/*
            The composition — **where each half of that total is settled**, because they are settled in two
            different places. Merging them into one payable line was the obvious reading of « une seule ligne »
            and is wrong: a payment on a note and a payment on an échéance produce different receipts and reach
            la caisse by different ledgers, so a single « Encaisser » here would have to guess which.

            ⚠️ `min-w-0` on the text block and `flex-wrap` on the row: the note's number and its figures are
            un-truncatable strings, and at 320 px one nowrap descendant sets the min-content width of every
            sibling in the card (the `RecordSection` scar).
          */}
          {/*
            ⚠️ Gated on `carried`, **not** on `treatmentMoney`: a DRAFT note is deliberately absent from the
            headline (it claims nothing in « Solde patient », so counting it here would have this screen and the
            patient's file disagree about one patient) — but the act it holds at 0 still has to be explained, and
            this is where that is said.
          */}
          {carried.length > 0 && (
            <div className="mb-4 space-y-2">
              {carried.map((note) => (
                <div
                  key={note.invoiceId}
                  className="flex flex-wrap items-center justify-between gap-x-3 gap-y-2 rounded-md border bg-muted/30 p-3"
                >
                  <div className="min-w-0 flex-1 basis-48">
                    <p className="text-sm font-medium">
                      {note.number ? `Note d'honoraires ${note.number}` : "Brouillon de note d'honoraires"}
                    </p>
                    <p className="text-xs text-muted-foreground">
                      {note.billedActAmount > 0
                        ? `La 1re séance, ${formatDT(note.billedActAmount)}.`
                        : "La 1re séance."}{" "}
                      {/* Said out loud rather than folded into the total: a note is per-fiche, so it may bill a
                          détartrage done the same day that has nothing to do with this treatment. */}
                      {note.billsOtherWork && "Elle facture aussi d'autres actes de la séance. "}
                      {note.status === "Draft" ? (
                        // A draft claims nothing yet — exactly what « Solde patient » says about it — so it is
                        // stated rather than summed, and « Encaisser » is not offered on a document that cannot
                        // take money.
                        <>Brouillon de {formatDT(note.total)} — rien n&apos;est encore réclamé.</>
                      ) : (
                        <>
                          Encaissé {formatDT(note.collected)} · reste {formatDT(note.outstanding)}.
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
              {/* The devis' own half — shown only when the headline speaks for both, since otherwise the three
                  figures above ARE the devis and this would repeat them. */}
              {treatmentMoney && (
                <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-2 rounded-md border p-3">
                  <div className="min-w-0 flex-1 basis-48">
                    <p className="text-sm font-medium">
                      {plan.number ? `Devis ${plan.number}` : "Ce traitement"}
                    </p>
                    <p className="text-xs text-muted-foreground">
                      Le travail restant, {formatDT(plan.totalPlanned)}. Encaissé {formatDT(plan.amountPaid)} ·
                      reste {formatDT(owed?.amount ?? plan.outstanding)}.
                    </p>
                  </div>
                </div>
              )}
            </div>
          )}

          {/*
            The reason « Encaisser » is gone, as **visible text** and not a `title` (J1): a tooltip is unreachable
            on a touch device, and this is the screen a dentist opens with the patient in front of them. Without
            it the action simply vanishes from every row and the « Facturé » badge in the header is the only clue.
          */}
          {noCollectReason && plan.installments.length > 0 && (
            <p role="note" className="mb-3 rounded-md bg-muted/40 p-3 text-xs text-muted-foreground">
              {noCollectReason}
            </p>
          )}
          {plan.installments.length === 0 ? (
            /* An empty échéancier is a legitimate, common state — the patient pays in one go — so this says
               that rather than implying something is missing. « Modifier l'échéancier » is only named when the
               plan is actually in the amendable window, or the description would point at a button that is
               not on screen. */
            <EmptyState
              icon={CalendarClock}
              size="compact"
              title="Aucune échéance définie"
              description={
                canAmend
                  ? "Le règlement n'est pas échelonné. Utilisez « Modifier l'échéancier » pour en définir un."
                  : "Le règlement de ce devis n'est pas échelonné."
              }
            />
          ) : (
            <>
              {/*
                ⚠️ The **date is the title** here, against the card rule's « date last ». An échéance has no other
                identity — « 15/03 » is what the patient agreed to, and every other column is a number about it.

                Its actions are the one variable-length set in the feature: « Encaisser » plus **one « Reçu » per
                payment**, and an échéance can hold several. That is why this card takes the menu the other
                surfaces use while the actes card does not — three or four buttons cannot share a 320 px title row,
                and dropping the extra receipts would remove the only way to reprint a specific payment.
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
                  { label: "Encaissé", value: formatDT(inst.amountPaid) },
                  { label: "Reste", value: formatDT(inst.outstanding) },
                  // Only rendered when there is something to render — `CardList` drops a field with no value, and
                  // « Paiements : — » on the majority of échéances would cost a line for nothing.
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
                          <DropdownMenuItem onSelect={() => setPaymentTarget(inst)}>Encaisser</DropdownMenuItem>
                        )}
                        {receipts.map((payment) => (
                          <DropdownMenuItem
                            key={payment.id}
                            onSelect={() => handleDownloadReceipt(inst.id, payment.id)}
                          >
                            Reçu — {formatDT(payment.amount)} du {formatDateFr(payment.paidOn)}
                          </DropdownMenuItem>
                        ))}
                        {/* AC-5 — the correction the échéancier never had. Offered per live payment, like the
                            receipts: an échéance can hold several and only one of them is the mis-keyed one. */}
                        {receipts.map((payment) => (
                          <DropdownMenuItem
                            key={`void-${payment.id}`}
                            className="text-destructive focus:text-destructive"
                            onSelect={() => setVoidTarget({ installment: inst, payment })}
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
                    <TableHead className="text-right">Encaissé</TableHead>
                    <TableHead className="text-right">Reste</TableHead>
                    <TableHead>Statut</TableHead>
                    <TableHead className="text-right">Action</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {plan.installments.map((inst) => {
                    return (
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
                                onClick={() => setPaymentTarget(inst)}
                              >
                                <CreditCard className="h-4 w-4" />
                                Encaisser
                              </Button>
                            )}
                          </TableCell>
                        </TableRow>

                        {/*
                          ⚠️ **ONE ROW PER ENCAISSEMENT, and it replaced three loops that grouped the actions by
                          BUTTON rather than by payment.** « Reçu » was mapped over the payments, then « Email »
                          over them again, then « Annuler » — so two payments rendered
                          « Reçu Reçu · Email Email · Annuler Annuler » in one cell, with nothing but a hover
                          `title` to say which belonged to which. A finger cannot reach a title (§ 9.2).

                          It became the ordinary case rather than an edge one when a treatment started being
                          collected séance by séance: `Accept` raises a SINGLE lump-sum échéance for the whole
                          total, so an implant paid across six visits puts six payments on one row.
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
                            {/* The séance it came from, when it was collected at the chair — the fact that makes
                                the échéancier and the patient's fiche history reconcile without arithmetic. */}
                            <TableCell className="py-1.5 text-xs text-muted-foreground" colSpan={2}>
                              {payment.isVoided ? (
                                <span>
                                  annulé{payment.voidReason ? ` — ${payment.voidReason}` : ""}
                                  {payment.voidedByName ? ` (${payment.voidedByName})` : ""}
                                </span>
                              ) : payment.amount < 0 ? (
                                <span>rendu au patient</span>
                              ) : payment.dentalRecordId ? (
                                <span>encaissé en séance</span>
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
                                  {/* AC-5. `text-destructive` rather than a `destructive` variant: it sits in a
                                      row of ghost/outline buttons and a filled red block there reads as the
                                      row's primary action, which annuler is not. */}
                                  <Button
                                    variant="ghost"
                                    size="sm"
                                    className="h-8 gap-1 text-destructive"
                                    disabled={busy}
                                    onClick={() => setVoidTarget({ installment: inst, payment })}
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
                    )
                  })}
                </TableBody>
              </Table>
            </>
          )}

          {/*
            AC-5 — an in-place confirm, following `invoice-detail-modal`'s idiom rather than a nested dialog:
            nothing in this app nests Radix dialogs, and this workspace is itself often reached from one. It sits
            below the échéancier so the row being annulled stays on screen beside its own confirmation.
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
          Historique du devis
        </summary>
        <div className="pb-2">
          <PlanTimeline plan={plan} />
        </div>
      </details>

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
            <AlertDialogTitle>Supprimer ce traitement ?</AlertDialogTitle>
            <AlertDialogDescription>
              {planLabel} sera supprimé, avec ses actes et ses séances à planifier. Aucun numéro de devis
              n&apos;a été consommé, donc rien ne manquera dans la numérotation — et aucune séance n&apos;a été
              réalisée, donc aucune fiche de soins n&apos;est touchée. Cette action est irréversible.
              {bookedVisitsNotice && <span className="text-warning-ink"> {bookedVisitsNotice}</span>}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={busy}>Retour</AlertDialogCancel>
            <AlertDialogAction
              disabled={busy}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
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

      {/*
        AC-P2.11 — « Détacher la fiche ». Confirmed rather than immediate: it reopens a devis that may have
        auto-completed and returns the act to « Prévu », which is not what a mis-click should do silently. The
        server refuses outright once the plan or the act's own fiche is billed, and that French sentence is
        surfaced by `run()`'s toast — this dialog is a normal action, not one that needs an in-form banner.
      */}
      <PlanItemStepsDialog
        plan={plan}
        item={stepsTarget}
        open={!!stepsTarget}
        onOpenChange={(open) => { if (!open) setStepsTarget(null) }}
        onSaved={onChanged}
      />

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
            <AlertDialogTitle>Détacher la fiche de cet acte ?</AlertDialogTitle>
            <AlertDialogDescription>
              {/*
                ⚠️ **It said « repassera à « Prévu » » whatever the act was, and on a multi-séance act that is
                simply false**: `Unmark` releases the LAST séance recorded, so a three-séance couronne lands on
                « En cours », 2 étapes sur 3 faites. `detachOutcome` is the one place that arithmetic lives —
                the same rule the server applies — and the count is phrased AS a count, never as a bare
                « 2 / 3 », which is read as progress (§ 13).
              */}
              {undoOutcome?.stepLabel && (undoOutcome.remaining?.done ?? 0) > 0 ? (
                <>
                  La dernière séance enregistrée ({quoteFr(undoOutcome.stepLabel)}) sera détachée de sa fiche
                  de soins. {quoteFr(undoTarget?.designationFr ?? "")} repassera à « En cours » —{" "}
                  {undoOutcome.remaining!.done} étape
                  {undoOutcome.remaining!.done > 1 ? "s" : ""} sur {undoOutcome.remaining!.total}{" "}
                  {undoOutcome.remaining!.done > 1 ? "faites" : "faite"}.
                </>
              ) : (
                <>
                  {quoteFr(undoTarget?.designationFr ?? "")} repassera à « Prévu » et sa fiche de soins sera
                  détachée.
                </>
              )}{" "}
              La fiche elle-même n&apos;est pas supprimée. Si ce devis s&apos;était clos sur cet acte, il sera
              réouvert.{" "}
              {/* The same forewarning as the step-level dialog — see its note. */}
              Si sa fiche est facturée sur une note d&apos;honoraires, il faudra d&apos;abord créditer cette
              note en totalité.
            </AlertDialogDescription>
          </AlertDialogHeader>

          {/*
            S6 — « … et la rattacher à ». It was two screens and two saves, in an order that hides the answer:
            the dentist detached here, then reopened the fiche and re-picked the act — and the right act was
            INVISIBLE until the wrong one had been released. Worse, the fiche's Select names acts, not steps,
            so a séance on the wrong STEP of the right act could not be corrected there at all.

            ⚠️ Default « nowhere », so the control adds a capability and changes nothing about the plain
            detach. Rendered only when there is somewhere to put it.
          */}
          {relinkOptions.length > 0 && (
            <div className="space-y-1.5">
              <Label htmlFor="plan-relink-target">Rattacher cette séance à</Label>
              <Select value={relinkKey} onValueChange={setRelinkKey} disabled={busy}>
                <SelectTrigger id="plan-relink-target" className="w-full">
                  <SelectValue placeholder="Ne rien rattacher — détacher seulement" />
                </SelectTrigger>
                <SelectContent>
                  {/* Radix refuses an empty-string value, so « nowhere » carries a sentinel. */}
                  <SelectItem value="none">Ne rien rattacher — détacher seulement</SelectItem>
                  {relinkOptions.map((target) => (
                    <SelectItem key={target.key} value={target.key}>
                      {target.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <p className="text-2xs text-muted-foreground">
                La fiche et sa date suivent la séance&nbsp;: elle n&apos;est ni dupliquée ni re-datée.
              </p>
            </div>
          )}
          <AlertDialogFooter>
            <AlertDialogCancel disabled={busy}>Retour</AlertDialogCancel>
            {/*
              `variant="destructive"` on the primitive, rather than the hand-written fill this carried. Without
              it the default renders the *primary* colour, which is the visual grammar for « this is the
              recommended action » — and detaching reopens a closed devis and undoes a réalisé act.
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
                    : outcome.stepLabel && (outcome.remaining?.done ?? 0) > 0
                      ? `Séance ${quoteFr(outcome.stepLabel)} détachée de sa fiche`
                      : "Acte ramené à « Prévu »",
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
              {relinkOptions.some((o) => o.key === relinkKey) ? "Détacher et rattacher" : "Détacher la fiche"}
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
            <AlertDialogDescription>{confirmAction?.description}</AlertDialogDescription>
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
            <DialogTitle>Rétablir ce devis annulé&nbsp;?</DialogTitle>
            <DialogDescription>
              {planLabel} revient en service avec son numéro — la série reste sans trou — et son échéancier est
              re-réparti sur le total des actes actifs. Le motif d&apos;annulation d&apos;origine est
              <b> conservé</b> et le vôtre lui est ajouté&nbsp;: le document que le patient détient peut-être
              reste expliqué.
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
              aria-describedby="plan-uncancel-hint"
            />
            <p id="plan-uncancel-hint" className="text-2xs text-muted-foreground">
              « Rétablir le devis » reste inactif tant qu&apos;aucun motif n&apos;est saisi.
            </p>
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
            <DialogTitle>Annuler le devis {plan.number}&nbsp;?</DialogTitle>
            <DialogDescription>
              Le devis sort de tous les soldes, des créances et de la caisse. Son numéro reste consommé —
              c&apos;est ce qui garde la série sans trou — et le motif est conservé avec lui. Les séances déjà
              réalisées et leurs fiches de soins ne sont pas touchées, et « Rétablir ce devis annulé » permet de
              revenir en arrière.{" "}
              {/* Stated rather than left to be discovered: `Cancel` refuses live money outright, so a devis
                  carrying an encaissement never reaches this dialog — the avoir is its remedy. */}
              Aucun encaissement n&apos;a été enregistré sur ce devis.
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
              aria-describedby="plan-cancel-plan-hint"
            />
            <p id="plan-cancel-plan-hint" className="text-2xs text-muted-foreground">
              « Annuler le devis » reste inactif tant qu&apos;aucun motif n&apos;est saisi.
            </p>
          </div>
          <DialogFooter className="gap-2">
            <Button variant="outline" disabled={busy} onClick={() => setCancelPlanOpen(false)}>
              Retour
            </Button>
            <Button
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
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
            <AlertDialogDescription>
              L&apos;acte reste sur le devis, marqué « mis de côté ». Ses{" "}
              {formatDT(withdrawTarget ? itemNetCost(withdrawTarget) : 0)} sortent du total et
              l&apos;échéancier est
              re-réparti sur ce qui reste. Rien n&apos;est supprimé — ni l&apos;acte, ni ses séances, ni les
              fiches de soins — et « Remettre au devis » le ramène à tout moment.
              {withdrawTarget?.scheduledAppointmentId && (
                <>
                  {" "}
                  <span className="text-warning-ink">
                    Le rendez-vous prévu pour cet acte sera libéré.
                  </span>
                </>
              )}
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
            <AlertDialogDescription>
              Ses {formatDT(restoreTarget ? itemNetCost(restoreTarget) : 0)} reviennent dans le total et
              l&apos;échéancier est
              re-réparti. L&apos;acte retrouve l&apos;état que ses propres séances lui donnent — le statut du
              devis, lui, n&apos;est pas recalculé&nbsp;: un traitement arrêté le reste tant que « Reprendre le
              traitement » n&apos;a pas été pressé.
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
              Détacher la note {plan.linkedInvoiceNumber ?? "d'honoraires"} de ce devis&nbsp;?
            </AlertDialogTitle>
            <AlertDialogDescription>
              La note n&apos;est ni annulée ni modifiée&nbsp;: elle cesse simplement de représenter ce devis.
              Le devis reprend son propre solde dans « Solde patient », dans les créances et dans la caisse, et
              son échéancier redevient encaissable. À faire uniquement si la note ne correspond plus à ce
              traitement — sans quoi le patient apparaîtra devoir la même somme deux fois.
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
            <DialogTitle>Praticien du devis</DialogTitle>
            <DialogDescription>
              Qui ce traitement crédite. La prochaine note d&apos;honoraires émise depuis ce devis lui sera
              attribuée&nbsp;; les notes déjà émises gardent le praticien qu&apos;elles portent.
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
            <AlertDialogTitle>Dupliquer ce devis&nbsp;?</AlertDialogTitle>
            <AlertDialogDescription>
              Une copie de {planLabel.toLowerCase()} sera créée pour {plan.patientName ?? "ce patient"}, avec ses
              actes, leurs honoraires, leurs remises, leurs dents et leurs séances. La copie est un{" "}
              <b>brouillon sans numéro</b>&nbsp;: aucun numéro de devis n&apos;est consommé et rien n&apos;est
              réclamé au patient tant qu&apos;elle n&apos;est pas éditée. Les séances déjà réalisées, les
              paiements et l&apos;échéancier ne sont pas copiés.
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
        S2 — « Remise ». Its own dialog and its own command: the amend form sends every act's cost on every
        save, so folding the remise in would let an older caller clear one silently.
      */}
      <Dialog
        open={!!discountTarget}
        onOpenChange={(open) => { if (!open && !busy) setDiscountTarget(null) }}
      >
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>Remise sur {quoteFr(discountTarget?.designationFr ?? "")}</DialogTitle>
            <DialogDescription>
              L&apos;acte garde son tarif de{" "}
              <b>{formatDT(discountTarget?.plannedCost ?? 0)}</b> et la remise est une ligne à part — sur le
              devis imprimé comme dans vos totaux, pour que le cabinet sache ce qu&apos;il a offert. Le patient
              doit le tarif moins la remise, et l&apos;échéancier est ajusté. Saisissez <b>0</b> pour retirer
              une remise.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-1.5">
            <Label htmlFor="plan-item-discount">Remise (DT)</Label>
            {/* `text` + `inputMode="decimal"`, never `type="number"` (J8): a number input refuses the comma
                this product prints with, and a rejected keystroke returns an EMPTY value. */}
            <Input
              id="plan-item-discount"
              type="text"
              inputMode="decimal"
              value={discountDraft}
              onChange={(e) => setDiscountDraft(e.target.value)}
              disabled={busy}
              aria-describedby="plan-item-discount-hint"
            />
            <p id="plan-item-discount-hint" className="text-2xs text-muted-foreground">
              Au maximum le tarif de l&apos;acte ({formatDT(discountTarget?.plannedCost ?? 0)}).
            </p>
          </div>
          <DialogFooter className="gap-2">
            <Button variant="outline" disabled={busy} onClick={() => setDiscountTarget(null)}>
              Retour
            </Button>
            <Button disabled={busy} onClick={() => void saveDiscount()}>
              Enregistrer la remise
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/*
        S4 — « Passer la créance en perte ». A `Dialog` rather than an `AlertDialog` because it carries an
        input, like the stop and the uncancel. The motif is required: this figure is what the practice reports
        as a loss, and a loss with no reason is not evidence.
      */}
      <Dialog open={writeOffOpen} onOpenChange={(open) => { if (!open && !busy) setWriteOffOpen(false) }}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>Passer la créance en perte&nbsp;?</DialogTitle>
            <DialogDescription>
              Les <b>{formatDT(owed?.amount ?? plan.outstanding)}</b> encore dus sur {planLabel.toLowerCase()} ne
              seront plus réclamés&nbsp;: le devis sort de « Créances », du solde du patient et du tableau de
              bord.{" "}
              {/* The half that separates this from an annulation, and the reason `Cancel` is not the
                  instrument: cancelling is refused over collected cash precisely because it rewrites days that
                  are already closed. */}
              Ce qui a déjà été encaissé est <b>conservé</b> — les {formatDT(plan.amountPaid)} restent dans la
              caisse, à leur date, avec leurs reçus. Les séances réalisées et leurs fiches ne sont pas touchées,
              et « Reprendre le traitement » rétablit la créance.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-1.5">
            <Label htmlFor="plan-write-off-reason">Motif de la mise en perte (obligatoire)</Label>
            <Textarea
              id="plan-write-off-reason"
              value={writeOffReason}
              onChange={(e) => setWriteOffReason(e.target.value)}
              placeholder="Ex. : patient décédé / parti à l'étranger / créance irrécouvrable"
              rows={3}
              disabled={busy}
              aria-describedby="plan-write-off-hint"
            />
            <p id="plan-write-off-hint" className="text-2xs text-muted-foreground">
              « Passer en perte » reste inactif tant qu&apos;aucun motif n&apos;est saisi.
            </p>
          </div>
          <DialogFooter className="gap-2">
            <Button variant="outline" disabled={busy} onClick={() => setWriteOffOpen(false)}>
              Retour
            </Button>
            <Button disabled={busy || !writeOffReason.trim()} onClick={() => void writeOffPlan()}>
              Passer en perte
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
            <DialogTitle>Changer le patient du devis</DialogTitle>
            <DialogDescription>
              Le devis, ses actes et son échéancier passent dans le dossier d&apos;une autre personne — son
              numéro et son historique le suivent. Refusé dès qu&apos;une séance a été réalisée ou qu&apos;une
              note d&apos;honoraires vivante le nomme&nbsp;: à ce stade c&apos;est un dossier médical, pas une
              erreur de saisie. Les rendez-vous rattachés à ce devis sont détachés.
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
              {stopNeedsRefund
                ? `Rendre ${formatDT(plan.amountPaid)} et arrêter ?`
                : stopWouldCancel
                  ? `Annuler le devis ${plan.number} ?`
                  : `Arrêter le traitement de ${plan.patientName ?? "ce patient"} ?`}
            </AlertDialogTitle>
            <AlertDialogDescription>
              {stopNeedsRefund ? (
                <>
                  {/*
                    ⚠️ The server's third arm, stated BEFORE the press instead of after it. This dialog used to
                    run the ordinary stop wording here — « Ce qui a déjà été fait est conservé, et le traitement
                    passe à « Arrêté » » — over a devis the aggregate refuses outright, so the dentist read a
                    promise, pressed, and got a refusal with the devis still « En cours ».
                  */}
                  Aucun acte de ce devis n&apos;a été réalisé, mais{" "}
                  <b>{formatDT(plan.amountPaid)} y ont déjà été encaissés</b>. Ils sont <b>rendus au patient
                  aujourd&apos;hui</b> et les actes sont mis de côté ; « Reprendre le traitement » les remet au
                  devis.
                </>
              ) : stopWouldCancel ? (
                <>
                  Aucune séance de ce devis n&apos;a été réalisée et{" "}
                  <b>aucun encaissement n&apos;y est enregistré</b> : il n&apos;y a rien à conserver, le devis
                  est <b>annulé</b>. Son numéro reste consommé — c&apos;est ce qui garde la série sans trou — et
                  le motif est conservé avec lui.{" "}
                  {/*
                    ⚠️ **The dialog used to say « rien à conserver » over a devis carrying a deposit**, and the
                    press then wrote a cancellation that erased that cash from la caisse retroactively.
                    `StopWouldCancel` now also asks the money, so a devis with an encaissement takes the STOP
                    branch and this sentence can be stated as a fact rather than assumed.
                  */}
                  Le numéro, lui, est définitif ; le devis peut être remis en service par « Rétablir ce devis
                  annulé », avec un motif.
                  {bookedVisitsNotice && <span className="text-warning-ink"> {bookedVisitsNotice}</span>}
                </>
              ) : (
                <>
                  Le patient ne poursuit pas. Les actes dont aucune séance n&apos;a été réalisée sont{" "}
                  <b>mis de côté</b> — rien n&apos;est supprimé, et « Reprendre le traitement » les remet au devis
                  si le patient revient. Ce qui a déjà été fait est conservé, et le traitement passe à
                  « Arrêté ».
                </>
              )}
            </AlertDialogDescription>
          </AlertDialogHeader>

          <div className="space-y-3 text-sm">
            {stopNeedsRefund ? (
              /* Nothing to list: no act is kept and none is put aside. What the reader needs is the money. */
              <RefundMethodField id="plan-stop-refund-method" value={stopRefundMethod} onChange={setStopRefundMethod} />
            ) : stopWouldCancel ? (
              /*
                ⚠️ **This was a dead end and is now the branch itself.** Nothing delivered on a numbered devis
                used to render a paragraph telling the dentist to go and use the other button — after the server
                had already refused the press, written a zero-amount échéance the aggregate rejects, and answered
                with a .NET parameter name over a dialog with no way out. The motif is asked for here, and the
                same press cancels.

                A real `<Label htmlFor>`, never a placeholder standing in for one: a placeholder disappears on the
                first keystroke, so the field becomes unlabelled exactly when it holds content, and it is never
                announced as a label at all. The motif is printed on the cancelled devis and read by whoever picks
                the file up later — it is the reason the cancellation is defensible.
              */
              <div className="space-y-1.5">
                {/* m12 — « (obligatoire) » on the label and a hint bound to the field. The disabled-until-valid
                    confirm below is deliberate and documented, but a control that is grey for a reason the
                    reader cannot see is a refusal without a message. */}
                <Label htmlFor="plan-cancel-reason">Motif d&apos;annulation (obligatoire)</Label>
                <Textarea
                  id="plan-cancel-reason"
                  value={cancelReason}
                  onChange={(e) => setCancelReason(e.target.value)}
                  placeholder="Ex. : patient a renoncé au traitement"
                  rows={3}
                  disabled={stopping}
                  aria-describedby="plan-cancel-reason-hint"
                />
                <p id="plan-cancel-reason-hint" className="text-2xs text-muted-foreground">
                  « Annuler le devis » reste inactif tant qu&apos;aucun motif n&apos;est saisi.
                </p>
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
                            {/* Stopping now lets a parked act's booked séance go (`PlanBookingRelease`), the way
                                parking one act always did — so the dialog says what happens, not a chore. */}
                            {i.scheduledAppointmentId && (
                              <span className="text-warning-ink"> · son rendez-vous sera libéré</span>
                            )}
                          </span>
                          <span className="shrink-0 font-mono text-2xs tabular-nums text-muted-foreground">
                            {formatDT(itemNetCost(i))}
                          </span>
                        </li>
                      ))}
                    </ul>
                  </div>
                )}

                {/*
                  What is kept, stated as plainly as what is put aside. A dentist stopping a treatment is
                  deciding about a patient's mouth and a patient's bill; « N actes retirés » alone leaves them
                  to work out what survives.
                */}
                <div>
                  <p className="text-2xs font-medium uppercase tracking-wide text-muted-foreground">
                    Conservés
                  </p>
                  <ul className="mt-1 space-y-0.5">
                    {keptItems.map((i) => (
                      <li key={i.id} className="flex items-baseline justify-between gap-3">
                        <span className="min-w-0 flex-1 [overflow-wrap:anywhere]">{i.designationFr}</span>
                        <span className="shrink-0 font-mono text-2xs tabular-nums text-muted-foreground">
                          {formatDT(itemNetCost(i))}
                        </span>
                      </li>
                    ))}
                  </ul>
                </div>

                <p className="rounded-md bg-muted/50 p-2.5 text-2xs leading-relaxed text-muted-foreground">
                  L&apos;échéancier est ramené au total conservé (
                  <span className="font-mono tabular-nums">
                    {formatDT(keptItems.reduce((sum, i) => sum + itemNetCost(i), 0))}
                  </span>
                  ). Ce qui a déjà été encaissé est conservé.
                  {billed && plan.linkedInvoiceNumber && (
                    <>
                      {" "}La note {plan.linkedInvoiceNumber} n&apos;est pas modifiée&nbsp;: corrigez-la par un
                      avoir si elle ne correspond plus.
                    </>
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

/**
 * « Où en est-on ? » — one pip per séance, then the count **phrased as a count**.
 *
 * <p>⚠️ « 3 séances sur 6 faites », never « 3 / 6 ». A bare fraction is read as progress toward a next step and
 * this product has shipped that defect twice (`step-counter-says-done-or-to-do`, N31); the word goes beside the
 * **visible** figure, not in an `sr-only` span. Acts stay in the quieter second line, because a bridge is not
 * réalisé until it is scellé and rounding that up would be a claim about a patient's mouth.</p>
 */
function ProgressBlock({
  seances,
  fraction,
  itemsDone,
  itemsTotal,
  withdrawn,
  pips,
}: {
  seances: { done: number; total: number }
  fraction: number
  itemsDone: number
  itemsTotal: number
  withdrawn: number
  pips: readonly ("done" | "next" | "todo")[]
}) {
  const plural = seances.done > 1
  return (
    <div className="flex flex-col justify-center gap-2 rounded-md border p-3">
      {seances.total === 0 ? (
        <p className="text-sm text-muted-foreground">Aucune séance à ce devis.</p>
      ) : (
        <>
          {/* Past 14 the dots stop being countable — the bar carries the same fraction. */}
          {pips.length <= 14 ? (
            <span className="flex gap-1" aria-hidden="true">
              {pips.map((p, i) => (
                <span
                  key={i}
                  className={cn(
                    "h-2 flex-1 rounded-full",
                    p === "done" && "bg-success",
                    p === "next" && "bg-warning",
                    p === "todo" && "bg-border",
                  )}
                />
              ))}
            </span>
          ) : (
            <PlanProgressBar done={seances.done} total={seances.total} fraction={fraction} />
          )}
          <p className="text-sm font-semibold">
            {seances.done} séance{plural ? "s" : ""} sur {seances.total} faite{plural ? "s" : ""}
          </p>
        </>
      )}
      {itemsTotal > 0 && (
        <p className="text-xs text-muted-foreground">
          {itemsDone} acte{itemsDone > 1 ? "s" : ""} sur {itemsTotal} réalisé{itemsDone > 1 ? "s" : ""}
          {withdrawn > 0 && ` · ${withdrawn} mis de côté`}
        </p>
      )}
    </div>
  )
}

/**
 * « Et maintenant ? » — the next séance, **named**, with the control that books it.
 *
 * <p>⚠️ This is the whole point of the header, and before it the page said « Prochaine séance : 14/08 » in one
 * muted line while the action that books one lived on an act's row below the fold — and the header's own filled
 * button offered « Facturer ». Naming the step matters as much as hoisting the button: « Couronne / bridge » is
 * identical on the préparation and on the scellement six weeks later.</p>
 *
 * <p>⚠️ **« À planifier », never « en retard ».** `MinDaysAfterPrevious` says *pas avant*, not *pas après*, so a
 * protocol delay that has elapsed breaks no promise — the same error `InstallmentLateness` was rewritten to stop
 * making about an auto-raised échéance.</p>
 */
function NextSeanceBlock({
  plan,
  isStopped,
  isCancelled,
  isWrittenOff,
  nextAct,
  onSchedule,
}: {
  plan: TreatmentPlanDto
  isStopped: boolean
  /** ⚠️ Its own arm, and it must be tested BEFORE `!nextAct` — see the branch. */
  isCancelled: boolean
  /** @see isCancelled — the same trap, one status over (S4). */
  isWrittenOff: boolean
  nextAct: TreatmentPlanItemDto | null
  onSchedule: (item: TreatmentPlanItemDto) => void
}) {
  const step = nextAct ? nextStepOf(nextAct) : null
  const state = nextAct ? planItemState(nextAct) : null

  /**
   * ⚠️ **Every arm branches on the act's own état, and « terminé » is reachable only with no act left.**
   * An earlier version tested `to-schedule` and let everything else fall through to « Tous les actes sont
   * réalisés » — so a `to-record` act (the visit happened, the fiche is not written) printed « TRAVAIL
   * TERMINÉ » on a treatment at one séance of six, beside the two figures saying otherwise.
   */
  const total = nextAct?.steps?.length ?? 0

  /**
   * The step's name, and — only where the rank means one thing — its rank **with the word that says which
   * question it answers**.
   *
   * <p>⚠️ **A bare « séance 2 sur 6 » is read as progress, and here it would be read wrong in two directions at
   * once.** In `to-schedule` it is the séance still to come; in `to-record` it is the one that has already
   * happened. Identical words, opposite facts. `check:responsive`'s N31 caught this in the eye pass and the
   * rule it enforces is the right one — the word goes beside the visible figure.</p>
   *
   * <p>On `to-record` the rank is dropped altogether rather than labelled: the séance took place but its fiche
   * is not written, so neither « faite » nor « à faire » is true of it, and the block's own label
   * (« Séance à enregistrer ») plus its meta line already say exactly where it stands.</p>
   */
  const stepName = step?.label ?? nextAct?.designationFr ?? ""
  const nth = step ? step.sequenceNumber + 1 : 0
  const hasRank = Boolean(step) && total > 1

  let label = "Prochaine séance"
  let title: string
  let meta: string | null = null

  if (isCancelled) {
    /*
     * ⚠️ **A cancelled devis was announced as « TRAVAIL TERMINÉ · Tous les actes sont réalisés ».**
     * `nextSchedulable` is gated on `isActive`, so a `Cancelled` plan hands this block a null act — and the
     * `!nextAct` arm below reads that as « everything is done », which is the opposite of what happened. On a
     * devis annulé before a single séance it claimed, in the largest words on the page, that the whole
     * treatment had been carried out. Tested first, for that reason.
     */
    label = "Devis annulé"
    title = "Ce devis ne fait plus foi"
    meta = plan.cancellationReason
      ? `Motif : ${plan.cancellationReason}`
      : "« Rétablir ce devis annulé » le remet en service, avec un motif."
  } else if (isWrittenOff) {
    /*
     * ⚠️ The same trap as the `Cancelled` arm above, one status over: `nextSchedulable` is gated on `isActive`,
     * so a written-off devis hands this block a null act and the `!nextAct` arm would announce « TRAVAIL
     * TERMINÉ · Tous les actes sont réalisés » about a treatment whose remaining work was given up on.
     */
    label = "Créance abandonnée"
    title = "Le solde ne sera pas réclamé"
    meta = "« Reprendre le traitement » rétablit la créance et les actes mis de côté."
  } else if (isStopped) {
    label = "Traitement arrêté"
    title = "Le patient ne poursuit pas"
    meta = "« Reprendre le traitement » remet les actes mis de côté au devis."
  } else if (!nextAct) {
    label = "Travail terminé"
    title = "Tous les actes sont réalisés"
  } else if (state === "scheduled") {
    label = "Séance réservée"
    // The accepted word is a LITERAL beside the figure, not interpolated: N31 reads source, and a variable
    // leaves the counter looking bare to it — which is also how it looks to a reader scanning the file.
    title = hasRank ? `${stepName} — séance ${nth} sur ${total} à faire` : stepName
    if (plan.nextAppointmentAt) title += ` — ${formatDateFr(plan.nextAppointmentAt)}`
    meta = "Rien à faire d'ici là."
  } else if (state === "to-record") {
    // The séance happened and nobody wrote it up. Stated, not actioned: « Enregistrer la fiche » is already on
    // the act's own row a few centimetres below, and a second door to it is a second thing to read.
    label = "Séance à enregistrer"
    // No rank: the séance happened but its fiche is not written, so neither « faite » nor « à faire » is true.
    title = stepName
    meta = "La séance a eu lieu — sa fiche de soins reste à saisir."
  } else {
    title = hasRank ? `${stepName} — séance ${nth} sur ${total} à planifier` : stepName
    meta = "À planifier."
  }

  return (
    <div
      className={cn(
        "flex flex-wrap items-center gap-3 rounded-md border p-3",
        isStopped || isCancelled || isWrittenOff ? "bg-muted" : "border-primary/35 bg-accent",
      )}
    >
      <div className="min-w-0 flex-1 basis-40">
        <p
          className={cn(
            "font-mono text-2xs uppercase tracking-wider",
            isStopped || isCancelled || isWrittenOff ? "text-muted-foreground" : "text-accent-foreground",
          )}
        >
          {label}
        </p>
        <p className="mt-0.5 text-base font-semibold [overflow-wrap:anywhere]">{title}</p>
        {meta && <p className="mt-0.5 text-xs text-muted-foreground">{meta}</p>}
      </div>
      {nextAct && state === "to-schedule" && !isStopped && !isCancelled && !isWrittenOff && (
        <Button size="sm" className="shrink-0" onClick={() => onSchedule(nextAct)}>
          <CalendarPlus className="h-4 w-4" />
          Planifier
        </Button>
      )}
    </div>
  )
}

function Figure({ label, value, hint }: { label: string; value: string; hint?: string }) {
  return (
    // `basis-28` + `min-w-0`: three tiles share a row wherever there is room and fall to one or two where
    // there is not, instead of being forced into halves that cannot hold their own labels.
    <div className="min-w-0 flex-1 basis-28">
      <p className="text-xs text-muted-foreground">{label}</p>
      <p className="text-lg font-semibold">{value}</p>
      {/* A second, quieter line — for the part of the figure the figure itself cannot carry. */}
      {hint && <p className="text-2xs text-primary">{hint}</p>}
    </div>
  )
}
