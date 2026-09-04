"use client"

import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import { useRouter } from "next/navigation"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { Table, TableBody, TableHead, TableHeader, TableRow, TableCell } from "@/components/ui/table"
import { CardList, CARDS_ONLY_LG, TABLE_ONLY_LG } from "@/components/ui/card-list"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger,
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
  CalendarClock, CalendarPlus, Layers, ListChecks, MoreHorizontal, X, Mail, Undo2,
  CircleSlash,
  RotateCcw,
} from "lucide-react"
import { toast } from "sonner"
import { showErrorToast } from "@/lib/errors"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import { invoicesApi } from "@/lib/api/invoices"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import type {
  InstallmentDto,
  InstallmentPaymentDto,
  ProcedureTypeDto,
  TreatmentPlanDto,
  TreatmentPlanItemDto,
} from "@/lib/api/types"
import { formatDT, formatDateFr, isBeforeToday, quoteFr } from "@/lib/format"
import { downloadBlob } from "@/lib/download"
import { planStatusLabel, planStatusBadgeClass } from "./treatment-plan-labels"
import {
  activeItems,
  displayedOutstanding,
  isPlanLive,
  hasDeliveredWork,
  isItemWithdrawn,
  isPlanBilled,
  planItemToPreset,
  planSeanceProgress,
  planWorkProgress,
} from "./plan-next-action"
import { PlanProgressBar } from "./plan-progress-bar"
import {
  PlanActPrimaryAction, PlanActReorderControls, PlanActRow, PlanActSelectionBox, PlanActStateBadge,
  PlanActStepsAction, planActCardFields,
} from "./plan-act-row"
import { PlanStepStrip } from "./plan-step-strip"
import { PlanItemStepsDialog } from "./plan-item-steps-dialog"
import { PlanTimeline } from "./plan-timeline"
import { InstallmentPaymentModal } from "./installment-payment-modal"
import { ReviseInstallmentsModal } from "./revise-installments-modal"
import { VoidInstallmentPayment } from "./void-installment-payment"
import { TreatmentPlanFormModal } from "./treatment-plan-form-modal"
import { CreateAppointmentDialog, type PresetPlanAct } from "@/components/create-appointment-dialog"
import { SendDocumentEmailDialog } from "@/components/send-document-email-dialog"
import { DOCUMENT_EMAIL_KINDS, type DocumentEmailKind } from "@/lib/api/document-emails"

/** What « Envoyer par e-mail » was clicked for — the devis itself, or one échéance's receipt. */
interface PlanEmailTarget {
  kind: DocumentEmailKind
  documentId: string
  installmentId?: string
  paymentId?: string
  label: string
}

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
            {formatDT(payment.amount)} · {formatDateFr(payment.paidOn)}
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
  const [emailTarget, setEmailTarget] = useState<PlanEmailTarget | null>(null)
  /** Bookings still to make, each element being one appointment. Only ever empty or a single group now — the bar's
   * « séparément » split (N groups of one) was removed as a duplicate of each act's own « Planifier ». */
  const [bookingQueue, setBookingQueue] = useState<PresetPlanAct[][]>([])
  /** Plan acts ticked for booking (ids). Only « À planifier » acts can be in here. */
  const [selectedActIds, setSelectedActIds] = useState<string[]>([])
  /** True for the one close that follows a successful create — see `finishCurrentBooking`. */
  const justAdvancedRef = useRef(false)
  const [cancelOpen, setCancelOpen] = useState(false)
  const [cancelReason, setCancelReason] = useState("")
  /** The plan-level state change awaiting confirmation; null = no dialog. See {@link PlanConfirm}. */
  const [confirmAction, setConfirmAction] = useState<PlanConfirm | null>(null)
  const [procedureTypes, setProcedureTypes] = useState<ProcedureTypeDto[]>([])
  /** The act catalogue read failed — not the same as a devis whose acts are legitimately all free text. */
  const [catalogFailed, setCatalogFailed] = useState(false)
  const [amendOpen, setAmendOpen] = useState(false)
  const [reviseOpen, setReviseOpen] = useState(false)
  /** « Arrêter le traitement » — see the button's note. */
  const [stopOpen, setStopOpen] = useState(false)
  const [stopping, setStopping] = useState(false)
  /** The act whose « réalisé » state is being corrected (AC-P2.11); null = dialog closed. */
  const [undoTarget, setUndoTarget] = useState<TreatmentPlanItemDto | null>(null)
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

  const isDraft = plan.status === "Draft"
  const isActive = isPlanLive(plan.status)
  const billed = isPlanBilled(plan)
  // Reordering is cosmetic, so it stays available on a Completed plan too — only a cancelled devis (and a
  // one-act plan, where there is nothing to move) hides the controls.
  const canReorder = plan.status !== "Cancelled" && plan.items.length > 1
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
  const canAmend = plan.status !== "Cancelled"

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
  const canBill =
    !isDraft &&
    plan.status !== "Cancelled" &&
    (!billed ||
      (plan.linkedInvoiceTotal != null && plan.totalPlanned - plan.linkedInvoiceTotal > 0.0005))

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
   * Correcting a réalisé act is *not* gated on `isActive`: marking the last act done auto-completes the plan,
   * so requiring an active plan would lock out the exact mistake the correction exists for. The server's
   * `EnsureCorrectable` admits Accepted / InProgress / **Completed** — mirrored here.
   */
  // A Draft is clinically live now — its acts book, record and detach like any other. See `canAmend`.
  const canCorrectActs = plan.status !== "Cancelled"
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
  const canCollectInstallments = !isDraft && plan.status !== "Cancelled" && !billed

  /**
   * The acts that can be booked right now. Same état the row's own « Planifier » keys off, so the tick boxes and
   * the per-row button can never disagree about what is bookable.
   */
  const schedulableItems = useMemo(
    () => (isActive ? plan.items.filter((i) => planItemState(i) === "to-schedule") : []),
    [isActive, plan.items],
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
  const run = async (action: () => Promise<unknown>, success: string, failure: string) => {
    setBusy(true)
    try {
      await action()
      toast.success(success)
      onChanged()
    } catch (err) {
      showErrorToast(err, failure)
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
  const actsRemaining = Math.max(plan.itemsTotal - plan.itemsDone, 0)

  /**
   * The three plan-level confirmations. Each names the numbered devis and states the consequence the button
   * label cannot: acceptance ends free editing, facturation writes a document **and navigates away**, and
   * « Terminer » leaves unfinished acts unfinished rather than completing them.
   */
  const confirmAccept = () =>
    setConfirmAction({
      title: "Accepter ce devis ?",
      description: (
        <>
          {planLabel} passera à « Accepté » : ses actes pourront être planifiés et l&apos;échéancier devient
          exigible. Il ne pourra plus être supprimé — une correction passera par « Modifier le devis », qui
          incrémente le numéro de révision.
        </>
      ),
      confirmLabel: "Accepter le devis",
      onConfirm: () =>
        run(() => treatmentPlansApi.accept(plan.id), "Devis accepté", "Échec de l'acceptation."),
    })

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
      onConfirm: () =>
        run(
          () => treatmentPlansApi.issueDevis(plan.id, plan.version),
          "Devis édité",
          "Échec de l'édition du devis.",
        ),
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
      onConfirm: () =>
        run(
          async () => {
            await invoicesApi.createFromPlan(plan.id)
            router.push("/factures")
          },
          plan.amountPaid > 0
            ? `Facture brouillon créée — ${formatDT(plan.amountPaid)} déjà encaissé sera reporté à l'émission`
            : "Facture brouillon créée depuis le devis",
          "Échec de la facturation du devis.",
        ),
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
      await treatmentPlansApi.stopTreatment(plan.id, plan.version)
      toast.success(
        parked > 0
          ? `Traitement arrêté — ${parked} acte${parked > 1 ? "s" : ""} mis de côté, à reprendre si le patient revient.`
          : "Traitement arrêté.",
      )
      setStopOpen(false)
      onChanged()
    } catch (err) {
      showErrorToast(err)
    } finally {
      setStopping(false)
    }
  }

  const confirmComplete = () =>
    setConfirmAction({
      title: "Terminer ce plan ?",
      description: (
        <>
          {planLabel} passera à « Terminé ».{" "}
          {/* The count, not the generic sentence: « 3 actes non réalisés resteront non réalisés » is the whole
              reason to hesitate, and a plan whose acts are all done has nothing to warn about. Clôturer does
              NOT mark them done, and it does not close the money either — both are stated because both are
              what a user would otherwise assume « Terminé » means. */}
          {actsRemaining > 0
            ? `${actsRemaining === 1 ? "L'acte non réalisé restera non réalisé" : `Les ${actsRemaining} actes non réalisés resteront non réalisés`} — la clôture ne les valide pas.`
            : "Tous les actes sont réalisés."}{" "}
          {/* ⚠️ « Les échéances restantes resteront encaissables » is false on a billed devis, and it was said
              there anyway: the échéance has no « Encaisser » action once a note holds the money, and the panel
              above says so. Same non-propagation as the « Reste » figure. */}
          {billed
            ? `L'encaissement se fait sur la note ${plan.linkedInvoiceNumber ?? "d'honoraires"}, pas sur l'échéancier.`
            : "Les échéances restantes resteront encaissables."}
        </>
      ),
      confirmLabel: "Terminer le plan",
      onConfirm: () =>
        run(() => treatmentPlansApi.complete(plan.id), "Plan terminé", "Échec de la clôture du plan."),
    })

  /**
   * « Reprendre le traitement » — the patient came back, which is what patients do.
   *
   * <p>⚠️ A stopped plan was a terminal state: `Completed` withdraws « Arrêter », « Terminer », « Facturer » and
   * « Annuler » alike, and the parked acts were *deleted*, so the only recovery was to re-type them as new ids
   * — orphaning the fiches, re-quoting the act at the catalogue default rather than the fee it was quoted at,
   * and walking the header back to « 0 / N » on a treatment several séances in.</p>
   */
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
      onConfirm: () =>
        run(
          () => treatmentPlansApi.reopenTreatment(plan.id, plan.version),
          "Traitement repris",
          "Échec de la reprise du traitement.",
        ),
    })

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
      () => treatmentPlansApi.reorderItems(plan.id, ids),
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
            <CardTitle className="flex flex-wrap items-center gap-2 text-xl">
              {plan.number ?? plan.title}
              {/* The devis PDF re-renders live from current state under the same number and is archived
                  nowhere, so this counter is the only way a patient's earlier printout can be identified.
                  Hidden at 0 — a never-amended devis says nothing about revisions. */}
              {plan.revisionNumber > 0 && (
                <span className="text-base font-normal text-muted-foreground">
                  · révision {plan.revisionNumber}
                </span>
              )}
              <Badge variant="secondary" className={planStatusBadgeClass(plan.status)}>
                {planStatusLabel(plan.status)}
              </Badge>
              {billed && (
                <Badge variant="outline">
                  Facturé{plan.linkedInvoiceNumber ? ` — ${plan.linkedInvoiceNumber}` : ""}
                </Badge>
              )}
            </CardTitle>
            <div className="flex flex-wrap items-center gap-2">
              {busy && <Loader2 className="h-4 w-4 animate-spin" />}
              {/* All three plan-level state changes go through a confirmation (see PlanConfirm). They used to
                  fire on the first click, in a row that also holds « Devis PDF » and « Envoyer par e-mail ». */}
              {/*
                « Éditer le devis » — the ONLY place a devis number is taken, and the reason a treatment can be
                started for nothing. A treatment followed from the agenda is an un-numbered draft: no number, no
                échéancier, no créance. Pressing this is the moment the patient is handed paper, so it is also
                the moment the money becomes a claim.
                ⚠️ It replaces « Accepter le devis » on this row rather than sitting beside it: two controls
                promoting the same draft is the « second door » this file already argues against, and « accepter »
                described a decision the patient makes, not one the dentist records.
              */}
              {isDraft && (
                <Button size="sm" className="gap-2" disabled={busy} onClick={confirmIssueDevis}>
                  <ClipboardCheck className="h-4 w-4" />
                  Éditer le devis
                </Button>
              )}
              {/*
                ⚠️ **`canBill`, not `isActive`, and the difference is the whole point of the feature.** Recording
                the last step auto-completes the plan, so the moment a treatment is finished — the moment it
                should be billed — this button, the only caller of that endpoint anywhere in the frontend,
                disappeared. An unbilled devis whose séances were all correctly recorded at 0 DT reached
                `Completed` with the full amount outstanding and no route to a note d'honoraires, while the
                continuation dialog promised precisely that: « sera facturé une fois le traitement terminé ».
                The server had always permitted it (it refuses only Draft and Cancelled), so this is the gate
                catching up with what the endpoint already allowed.
              */}
              {canBill && (
                <Button
                  size="sm"
                  variant="outline"
                  className="gap-2"
                  disabled={busy}
                  onClick={confirmBill}
                  title={
                    plan.amountPaid > 0
                      ? `${formatDT(plan.amountPaid)} déjà encaissé sur ce devis sera reporté sur la facture à son émission`
                      : undefined
                  }
                >
                  <ReceiptText className="h-4 w-4" />
                  Facturer le devis
                </Button>
              )}
              {/* AC-P2.1 — `POST /amend` has been fully implemented and validated since
                  `treatment-plan-workspace` with no caller, so a typo on an accepted devis forced cancelling
                  it and losing its number. Called unconditionally within the amendable window and the 403 is
                  surfaced, matching the other financial-reversal actions rather than gating on role. */}
              {canAmend && (
                <Button
                  size="sm"
                  variant="outline"
                  className="gap-2"
                  disabled={busy}
                  onClick={() => setAmendOpen(true)}
                >
                  <FilePen className="h-4 w-4" />
                  Modifier le devis
                </Button>
              )}
              {/*
                « Arrêter le traitement » — the patient who stops halfway, which is ordinary and had no action at
                all. What it does is exactly what a dentist would otherwise have to discover: put the acts not
                yet started aside, keep what was carried out, and close the plan. Cancelling the devis was the
                only thing on offer and it is the wrong answer — it voids the work that WAS done along with the
                rest.

                ⚠️ **Offered whenever the treatment is live, and it used to be gated on `stoppableItems.length`.**
                That hid it in the commonest abandon shape of all: the patient who cancels the next séance and
                never returns. With that séance still in the agenda the act reads « planifié », nothing was
                « stoppable », and the button was simply absent — so the dentist had to find and cancel the
                appointment on another screen with nothing telling them so. The dialog explains the booked
                séances instead, which is the honest place for it.
              */}
              {isActive && (
                <Button size="sm" variant="outline" className="gap-2" disabled={busy} onClick={() => setStopOpen(true)}>
                  <CircleSlash className="h-4 w-4" />
                  Arrêter le traitement
                </Button>
              )}
              {isActive && (
                <Button size="sm" variant="outline" className="gap-2" disabled={busy} onClick={confirmComplete}>
                  <CheckCheck className="h-4 w-4" />
                  Terminer
                </Button>
              )}
              {/* The way back from a stopped treatment. Only on a Completed plan, and only worth offering when
                  something was actually put aside or the patient is resuming a closed course. */}
              {plan.status === "Completed" && (
                <Button size="sm" variant="outline" className="gap-2" disabled={busy} onClick={confirmReopen}>
                  <RotateCcw className="h-4 w-4" />
                  Reprendre le traitement
                </Button>
              )}
              <Button size="sm" variant="outline" className="gap-2" disabled={busy} onClick={handleDownloadDevis}>
                <FileDown className="h-4 w-4" />
                Devis PDF
              </Button>
              <Button
                size="sm"
                variant="outline"
                className="gap-2"
                disabled={busy}
                onClick={() => setEmailTarget({
                  kind: DOCUMENT_EMAIL_KINDS.TreatmentPlan,
                  documentId: plan.id,
                  label: `Devis ${plan.number ?? ""}`.trim(),
                })}
              >
                <Mail className="h-4 w-4" />
                Envoyer par e-mail
              </Button>
              {/* Cancelling a numbered devis lives here rather than in the list: it is the one destructive
                  action on the plan and needs the context of what is being voided. Server-side it is
                  AdminOrDoctor; the UI calls it unconditionally and surfaces the 403, matching the other
                  financial-reversal actions. */}
              {isActive && (
                <Button
                  size="sm"
                  variant="outline"
                  className="gap-2 text-destructive hover:text-destructive"
                  disabled={busy}
                  onClick={() => setCancelOpen(true)}
                >
                  <Ban className="h-4 w-4" />
                  Annuler
                </Button>
              )}
            </div>
          </div>
          <p className="text-sm text-muted-foreground">
            <PatientNameLink patientId={plan.patientId} name={plan.patientName ?? "Patient"} />
            {plan.number && plan.title ? ` · ${plan.title}` : ""}
          </p>
        </CardHeader>

        <CardContent className="space-y-4">
          <PlanProgressBar done={seances.done} total={seances.total} fraction={work.fraction} />

          <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
            <Figure label="Total" value={formatDT(plan.totalPlanned)} />
            <Figure label="Encaissé" value={formatDT(plan.amountPaid)} />
            {/*
              ⚠️ **`displayedOutstanding`, never `plan.outstanding`, and the guard used to be `!isDraft` alone.**
              That exclusion was added with a stated reason — a draft contributes 0 to « Solde patient », so a
              « Reste » here « would contradict the balance the rest of the app reports » — and the identical
              argument applies to a devis a note d'honoraires already collects: its auto-raised échéance will
              never see a payment, so this figure reported the whole devis as unpaid. Measured on 4 of 4 bridged
              plans, two of them fully settled, one patient shown « Solde dû 31,000 DT » in their file header and
              « Reste 120,000 DT » here on the same page. The note's own balance is named instead, so the answer
              is still a figure rather than an absence.
            */}
            {owed && (
              <Figure
                label="Reste"
                value={formatDT(owed.amount)}
                hint={
                  owed.isBilled
                    ? `sur la note ${owed.invoiceNumber ?? "d'honoraires"}`
                    : undefined
                }
              />
            )}
            {/*
              ⚠️ **Séances, not acts.** « Actes réalisés 0 / 1 » is what a six-visit implant read from its first
              appointment to its last, because an act is only Done when every step is — so the feature's whole
              subject had no progress signal. The bar beside it was already step-weighted, which made the two
              disagree on one screen. Whole acts stay in the hint: a bridge is not réalisé until it is scellé,
              and rounding that up would be a claim about a patient's mouth.
            */}
            <Figure
              label="Séances réalisées"
              value={seances.total > 0 ? seances.label : "—"}
              hint={
                plan.itemsTotal > 0
                  ? `${plan.itemsDone} / ${plan.itemsTotal} acte${plan.itemsTotal > 1 ? "s" : ""}${
                      withdrawnItems.length > 0
                        ? ` · ${withdrawnItems.length} mis de côté`
                        : ""
                    }`
                  : undefined
              }
            />
          </div>

          {/*
            ⚠️ « À accepter pour démarrer le suivi. » was true when a Draft was an unfinished form. It is false
            now: a treatment followed from the agenda is un-numbered and already running — its séances book, its
            fiches record — so telling the dentist to accept something before starting describes a gate that no
            longer exists. The next séance is the same fact for a draft as for a numbered devis, so it is stated
            the same way; what the draft adds is one line saying the devis has not been issued.
          */}
          <p className="text-sm text-foreground">
            {plan.nextAppointmentAt
              ? `Prochaine séance : ${formatDateFr(plan.nextAppointmentAt)}`
              : "Aucune séance planifiée"}
          </p>
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
                  ? "Ce devis ne contient encore aucun acte. Ajoutez-les avec « Modifier le devis »."
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
                      actions={(a) =>
                        canCorrectActs ? (
                          <PlanActStepsAction item={a.item} onEditSteps={setStepsTarget} />
                        ) : undefined
                      }
                      primaryAction={(a) => (
                        <PlanActPrimaryAction
                          plan={plan}
                          item={a.item}
                          onSchedule={(target) => startBooking([[target]])}
                          onUndo={canCorrectActs ? setUndoTarget : undefined}
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
                    <TableHead>Désignation</TableHead>
                    <TableHead>Dents</TableHead>
                    <TableHead className="text-right">Coût</TableHead>
                    <TableHead>État</TableHead>
                    <TableHead className="text-right">Action</TableHead>
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

      {/* ---- Échéancier ---------------------------------------------------------------------------- */}
      <Card>
        <CardHeader className="pb-3">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <CardTitle className="text-base">Échéancier</CardTitle>
            {/* AC-P2.5 — `PUT /installments` was equally callerless, so a patient who could no longer pay on
                the agreed dates had to have the devis cancelled and retyped. Same window as the amendment. */}
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
        </CardHeader>
        <CardContent>
          {/*
            The reason « Encaisser » is gone, as **visible text** and not a `title` (J1): a tooltip is unreachable
            on a touch device, and this is the screen a dentist opens with the patient in front of them. Without
            it the action simply vanishes from every row and the « Facturé » badge in the header is the only clue.
          */}
          {billed && plan.installments.length > 0 && (
            <p role="note" className="mb-3 rounded-md bg-muted/40 p-3 text-xs text-muted-foreground">
              Ce devis est facturé{plan.linkedInvoiceNumber ? ` (note n° ${plan.linkedInvoiceNumber})` : ""}.
              Les paiements s&apos;enregistrent désormais sur la note d&apos;honoraires — un encaissement saisi ici
              n&apos;apparaîtrait ni dans la caisse ni dans les recettes.
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
                title={(inst) => formatDateFr(inst.dueDate)}
                status={(inst) =>
                  inst.isPaid ? (
                    <Badge variant="secondary">Payée</Badge>
                  ) : isBeforeToday(inst.dueDate) ? (
                    <Badge variant="destructive">En retard</Badge>
                  ) : (
                    <Badge variant="outline">En attente</Badge>
                  )
                }
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
                  const receipts = inst.payments.filter((p) => !p.isVoided)
                  if (!canCollect && receipts.length === 0) return null
                  return (
                    <DropdownMenu>
                      <DropdownMenuTrigger asChild>
                        <Button
                          variant="ghost"
                          size="icon"
                          disabled={busy}
                          aria-label={`Actions de l'échéance du ${formatDateFr(inst.dueDate)}`}
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
                        {receipts.map((payment) => (
                          <DropdownMenuItem
                            key={`email-${payment.id}`}
                            onSelect={() => setEmailTarget({
                              kind: DOCUMENT_EMAIL_KINDS.InstallmentPaymentReceipt,
                              documentId: plan.id,
                              installmentId: inst.id,
                              paymentId: payment.id,
                              label: `Reçu d'échéance ${formatDT(payment.amount)}`,
                            })}
                          >
                            Envoyer par e-mail — {formatDT(payment.amount)}
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
                    // Late only once the due DAY has passed — an échéance due today still has the day to run.
                    const isOverdue = !inst.isPaid && isBeforeToday(inst.dueDate)
                    return (
                      <TableRow key={inst.id}>
                        <TableCell>{formatDateFr(inst.dueDate)}</TableCell>
                        <TableCell className="text-right">{formatDT(inst.amount)}</TableCell>
                        <TableCell className="text-right">
                          {formatDT(inst.amountPaid)}
                          {/* A voided encaissement is *kept and shown*, struck through with its motif and its
                              actor — § 1's rule, and the only way « Encaissé 0,000 » on an échéance that clearly
                              took money is explicable rather than alarming. */}
                          {inst.payments.length > 0 && (
                            <InstallmentPaymentLines payments={inst.payments} className="mt-1 text-right" />
                          )}
                        </TableCell>
                        <TableCell className="text-right">{formatDT(inst.outstanding)}</TableCell>
                        <TableCell>
                          {inst.isPaid ? (
                            <Badge variant="secondary">Payée</Badge>
                          ) : isOverdue ? (
                            <Badge variant="destructive">En retard</Badge>
                          ) : (
                            <Badge variant="outline">En attente</Badge>
                          )}
                        </TableCell>
                        <TableCell className="text-right">
                          <div className="flex justify-end gap-1">
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
                            {/* One receipt per PAYMENT — an échéance can hold several, and the receipt used
                                to print the cumulative total instead of the money handed over. */}
                            {inst.payments
                              .filter((p) => !p.isVoided)
                              .map((payment) => (
                                <Button
                                  key={payment.id}
                                  variant="ghost"
                                  size="sm"
                                  className="h-8 gap-1"
                                  disabled={busy}
                                  title={`Reçu du paiement de ${formatDT(payment.amount)} du ${formatDateFr(payment.paidOn)}`}
                                  onClick={() => handleDownloadReceipt(inst.id, payment.id)}
                                >
                                  <ReceiptText className="h-4 w-4" />
                                  Reçu
                                </Button>
                              ))}
                            {inst.payments
                              .filter((p) => !p.isVoided)
                              .map((payment) => (
                                <Button
                                  key={`email-${payment.id}`}
                                  variant="outline"
                                  size="sm"
                                  className="h-8 gap-1"
                                  disabled={busy}
                                  title={`Envoyer par e-mail le reçu du paiement de ${formatDT(payment.amount)}`}
                                  onClick={() => setEmailTarget({
                                    kind: DOCUMENT_EMAIL_KINDS.InstallmentPaymentReceipt,
                                    documentId: plan.id,
                                    installmentId: inst.id,
                                    paymentId: payment.id,
                                    label: `Reçu d'échéance ${formatDT(payment.amount)}`,
                                  })}
                                >
                                  <Mail className="h-4 w-4" />
                                  Email
                                </Button>
                              ))}
                            {/* AC-5. `text-destructive` rather than a `destructive` variant: it sits in a row of
                                ghost/outline buttons and a filled red block there reads as the row's primary
                                action, which annuler is not. */}
                            {inst.payments
                              .filter((p) => !p.isVoided)
                              .map((payment) => (
                                <Button
                                  key={`void-${payment.id}`}
                                  variant="ghost"
                                  size="sm"
                                  className="h-8 gap-1 text-destructive hover:text-destructive"
                                  disabled={busy}
                                  title={`Annuler l'encaissement de ${formatDT(payment.amount)} du ${formatDateFr(payment.paidOn)}`}
                                  onClick={() => setVoidTarget({ installment: inst, payment })}
                                >
                                  <Undo2 className="h-4 w-4" />
                                  Annuler
                                </Button>
                              ))}
                          </div>
                        </TableCell>
                      </TableRow>
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
              installmentDueDate={voidTarget.installment.dueDate}
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

      {/* ---- Parcours ------------------------------------------------------------------------------ */}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">Parcours</CardTitle>
        </CardHeader>
        <CardContent className="px-0">
          <PlanTimeline plan={plan} />
        </CardContent>
      </Card>

      {emailTarget && (
        <SendDocumentEmailDialog
          open={Boolean(emailTarget)}
          onOpenChange={(next) => { if (!next) setEmailTarget(null) }}
          documentKind={emailTarget.kind}
          documentId={emailTarget.documentId}
          installmentId={emailTarget.installmentId}
          paymentId={emailTarget.paymentId}
          documentLabel={emailTarget.label}
          patientId={plan.patientId}
        />
      )}

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

      <Dialog
        open={cancelOpen}
        onOpenChange={(open) => { if (!open) { setCancelOpen(false); setCancelReason("") } }}
      >
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>Annuler le plan</DialogTitle>
            <DialogDescription>
              {plan.number ? `Devis ${plan.number}` : "Plan de traitement"} — le numéro est conservé. Un motif
              est requis.
            </DialogDescription>
          </DialogHeader>
          {/*
            A real `<Label htmlFor>`, not a placeholder standing in for one. A placeholder disappears on the
            first keystroke, so the field the motif is being typed into becomes unlabelled at exactly the moment
            it holds content — and it is never announced as a label by a screen reader at all. The motif is
            printed on the cancelled devis and read by whoever picks the file up later, so this field is the
            reason the cancellation is defensible; it deserves a name and a hint about what to write.
          */}
          <div className="space-y-1.5">
            <Label htmlFor="plan-cancel-reason">Motif d&apos;annulation</Label>
            <Textarea
              id="plan-cancel-reason"
              value={cancelReason}
              onChange={(e) => setCancelReason(e.target.value)}
              placeholder="Ex. : patient a renoncé au traitement"
              rows={3}
              disabled={busy}
            />
          </div>
          <DialogFooter className="gap-2">
            <Button
              variant="outline"
              disabled={busy}
              onClick={() => { setCancelOpen(false); setCancelReason("") }}
            >
              Retour
            </Button>
            {/*
              Gated by `disabled`, not by a toast after the click. The empty-motif check used to run *inside*
              the handler and surface as a toast, so the requirement was invisible until the user had already
              committed to cancelling a numbered devis — and the toast appears in a corner, away from the field
              that needs filling. `payment-modal.tsx` disables its own confirm for the same reason: a rule the
              form can enforce should never be discovered by breaking it.
            */}
            <Button
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
              disabled={busy || !cancelReason.trim()}
              onClick={async () => {
                await run(
                  () => treatmentPlansApi.cancel(plan.id, cancelReason.trim()),
                  "Plan annulé",
                  "Échec de l'annulation.",
                )
                setCancelOpen(false)
                setCancelReason("")
              }}
            >
              Confirmer l&apos;annulation
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* AC-P2.1–2.4 — the plan form in amend mode. Refusals land in its own FormErrorBanner rather than a
          toast, which would fire behind the open dialog. */}
      <TreatmentPlanFormModal
        open={amendOpen}
        onOpenChange={setAmendOpen}
        editingPlan={plan}
        amendMode
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

      <Dialog open={!!undoTarget} onOpenChange={(open) => { if (!open) setUndoTarget(null) }}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>Détacher la fiche de cet acte ?</DialogTitle>
            <DialogDescription>
              {quoteFr(undoTarget?.designationFr ?? "")} repassera à « Prévu » et sa fiche de soins sera détachée. La fiche
              elle-même n&apos;est pas supprimée. Si ce devis s&apos;était clos sur cet acte, il sera réouvert.{" "}
              {/* The same forewarning as the step-level dialog — see its note. */}
              Si sa fiche est facturée sur une note d&apos;honoraires, il faudra d&apos;abord créditer cette
              note en totalité.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter className="gap-2">
            <Button variant="outline" disabled={busy} onClick={() => setUndoTarget(null)}>
              Retour
            </Button>
            {/*
              Destructive styling, matching every other confirm in this file. Without the class the default
              `Button` variant renders the *primary* fill, so the footer read « Retour » (outline) beside a blue
              « Détacher la fiche » — which is the visual grammar for "this is the recommended action". It is
              not: it reopens a closed devis and undoes a réalisé act, and the outline/primary pairing was
              actively steering the user toward it.
            */}
            <Button
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
              disabled={busy}
              onClick={async () => {
                const target = undoTarget
                if (!target) return
                await run(
                  () => treatmentPlansApi.markItemUndone(plan.id, target.id),
                  "Acte ramené à « Prévu »",
                  "Échec de la correction de l'acte.",
                )
                setUndoTarget(null)
              }}
            >
              Détacher la fiche
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

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
        « Arrêter le traitement ».

        ⚠️ Its own dialog rather than a `PlanConfirm` row, because it is NOT a pure yes/no: it has to name the
        acts it will drop and the ones it will keep, and « êtes-vous sûr ? » over an irreversible edit to a
        patient's treatment is exactly what the repo's destructive-confirm rule forbids.
      */}
      <AlertDialog open={stopOpen} onOpenChange={(open) => { if (!open && !stopping) setStopOpen(false) }}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Arrêter le traitement de {plan.patientName ?? "ce patient"} ?</AlertDialogTitle>
            <AlertDialogDescription>
              Le patient ne poursuit pas. Les actes dont aucune séance n&apos;a été réalisée sont{" "}
              <b>mis de côté</b> — rien n&apos;est supprimé, et « Reprendre le traitement » les remet au devis si
              le patient revient. Ce qui a déjà été fait est conservé, et le devis est clôturé.
            </AlertDialogDescription>
          </AlertDialogHeader>

          <div className="space-y-3 text-sm">
            {keptItems.length === 0 ? (
              /*
                Nothing has been delivered, so there is nothing to keep and this is not the right record: the
                patient accepted a devis and never came. The server refuses it in these terms — before this it
                accepted the press, wrote a zero-amount échéance the aggregate refuses, and answered with a .NET
                parameter name over a dialog with no way out.
              */
              <p
                role="status"
                className="rounded-md border border-dashed p-2.5 text-2xs leading-relaxed text-muted-foreground"
              >
                Aucune séance de ce devis n&apos;a encore été réalisée, donc il n&apos;y a rien à conserver.
                Annulez le devis (un motif est demandé) plutôt que d&apos;arrêter le traitement.
              </p>
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
                            {/* A booked séance is the commonest abandon shape, and the dialog is where it has
                                to be said: the appointment is not cancelled by stopping the treatment. */}
                            {i.scheduledAppointmentId && (
                              <span className="text-warning-ink"> · un rendez-vous reste à annuler</span>
                            )}
                          </span>
                          <span className="shrink-0 font-mono text-2xs tabular-nums text-muted-foreground">
                            {formatDT(i.plannedCost)}
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
                          {formatDT(i.plannedCost)}
                        </span>
                      </li>
                    ))}
                  </ul>
                </div>

                <p className="rounded-md bg-muted/50 p-2.5 text-2xs leading-relaxed text-muted-foreground">
                  L&apos;échéancier est ramené au total conservé (
                  <span className="font-mono tabular-nums">
                    {formatDT(keptItems.reduce((sum, i) => sum + i.plannedCost, 0))}
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
            {keptItems.length === 0 ? (
              /* The route out of the dead end, rather than a live red button that can only fail. */
              <AlertDialogAction
                variant="destructive"
                disabled={stopping}
                onClick={(event) => {
                  event.preventDefault()
                  setStopOpen(false)
                  setCancelOpen(true)
                }}
              >
                Annuler le devis…
              </AlertDialogAction>
            ) : (
              <AlertDialogAction
                variant="destructive"
                disabled={stopping}
                onClick={(event) => {
                  event.preventDefault()
                  void stopTreatment()
                }}
              >
                {stopping ? "Arrêt…" : "Arrêter le traitement"}
              </AlertDialogAction>
            )}
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  )
}

function Figure({ label, value, hint }: { label: string; value: string; hint?: string }) {
  return (
    <div>
      <p className="text-xs text-muted-foreground">{label}</p>
      <p className="text-lg font-semibold">{value}</p>
      {/* A second, quieter line — for the part of the figure the figure itself cannot carry. */}
      {hint && <p className="text-2xs text-primary">{hint}</p>}
    </div>
  )
}
