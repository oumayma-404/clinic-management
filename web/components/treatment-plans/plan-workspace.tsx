"use client"

import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import { useRouter } from "next/navigation"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { Table, TableBody, TableHead, TableHeader, TableRow, TableCell } from "@/components/ui/table"
import { CardList, CARDS_ONLY, TABLE_ONLY } from "@/components/ui/card-list"
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
  CalendarClock, CalendarPlus, Layers, ListChecks, MoreHorizontal, X, Mail,
} from "lucide-react"
import { toast } from "sonner"
import { showErrorToast } from "@/lib/errors"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import { invoicesApi } from "@/lib/api/invoices"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import type { InstallmentDto, ProcedureTypeDto, TreatmentPlanDto, TreatmentPlanItemDto } from "@/lib/api/types"
import { formatDT, formatDateFr, isBeforeToday } from "@/lib/format"
import { downloadBlob } from "@/lib/download"
import { planStatusLabel, planStatusBadgeClass } from "./treatment-plan-labels"
import { isPlanBilled } from "./plan-next-action"
import { PlanProgressBar } from "./plan-progress-bar"
import {
  PlanActPrimaryAction, PlanActReorderControls, PlanActRow, PlanActSelectionBox, PlanActStateBadge,
  planActCardFields,
} from "./plan-act-row"
import { PlanTimeline } from "./plan-timeline"
import { InstallmentPaymentModal } from "./installment-payment-modal"
import { ReviseInstallmentsModal } from "./revise-installments-modal"
import { TreatmentPlanFormModal } from "./treatment-plan-form-modal"
import { CreateAppointmentDialog, type PresetPlanAct } from "@/components/create-appointment-dialog"
import { SendDocumentEmailDialog } from "@/components/send-document-email-dialog"
import { DOCUMENT_EMAIL_KINDS, type DocumentEmailKind } from "@/lib/api/document-emails"

/** What « Envoyer par email » was clicked for — the devis itself, or one échéance's receipt. */
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

interface PlanWorkspaceProps {
  plan: TreatmentPlanDto
  /** Refetch the plan after any mutation (the parent owns the fetch). */
  onChanged: () => void
}

/**
 * The devis's home: header, actes, échéancier and parcours on one page. Replaces the plans-table "Gérer"
 * dialog, which was the only place a plan's contents were visible and offered every action on every row.
 */
export function PlanWorkspace({ plan, onChanged }: PlanWorkspaceProps) {
  const router = useRouter()
  const [busy, setBusy] = useState(false)
  const [paymentTarget, setPaymentTarget] = useState<InstallmentDto | null>(null)
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
  /** The act whose « réalisé » state is being corrected (AC-P2.11); null = dialog closed. */
  const [undoTarget, setUndoTarget] = useState<TreatmentPlanItemDto | null>(null)

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
  const toPresetAct = useCallback(
    (item: TreatmentPlanItemDto): PresetPlanAct => ({
      planItemId: item.id,
      procedureTypeId: resolveProcedureTypeId(item),
      label:
        item.toothNumbers.length > 0
          ? `${item.designationFr} (dents ${item.toothNumbers.join(", ")})`
          : item.designationFr,
    }),
    [resolveProcedureTypeId],
  )

  const isDraft = plan.status === "Draft"
  const isActive = plan.status === "Accepted" || plan.status === "InProgress"
  const billed = isPlanBilled(plan)
  // Reordering is cosmetic, so it stays available on a Completed plan too — only a cancelled devis (and a
  // one-act plan, where there is nothing to move) hides the controls.
  const canReorder = plan.status !== "Cancelled" && plan.items.length > 1
  /**
   * Amending (acts or échéancier) needs the same conditions as « Facturer le devis » — an active plan not yet
   * represented by an invoice (AC-P2.2). Both server handlers apply `EnsureAmendable` (Accepted/InProgress)
   * plus the billed-plan block, so this mirrors the server rather than inventing a rule.
   */
  const canAmend = isActive && !billed
  /**
   * Correcting a réalisé act is *not* gated on `isActive`: marking the last act done auto-completes the plan,
   * so requiring an active plan would lock out the exact mistake the correction exists for. The server's
   * `EnsureCorrectable` admits Accepted / InProgress / **Completed** — mirrored here.
   */
  const canCorrectActs = plan.status !== "Draft" && plan.status !== "Cancelled"
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
            ? `Les ${actsRemaining} acte${actsRemaining > 1 ? "s" : ""} non réalisé${actsRemaining > 1 ? "s" : ""} resteront non réalisé${actsRemaining > 1 ? "s" : ""} — la clôture ne les valide pas.`
            : "Tous les actes sont réalisés."}{" "}
          Les échéances restantes resteront encaissables.
        </>
      ),
      confirmLabel: "Terminer le plan",
      onConfirm: () =>
        run(() => treatmentPlansApi.complete(plan.id), "Plan terminé", "Échec de la clôture du plan."),
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
                  fire on the first click, in a row that also holds « Devis PDF » and « Envoyer par email ». */}
              {isDraft && (
                <Button size="sm" className="gap-2" disabled={busy} onClick={confirmAccept}>
                  <ClipboardCheck className="h-4 w-4" />
                  Accepter le devis
                </Button>
              )}
              {isActive && !billed && (
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
              {isActive && (
                <Button size="sm" variant="outline" className="gap-2" disabled={busy} onClick={confirmComplete}>
                  <CheckCheck className="h-4 w-4" />
                  Terminer
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
                Envoyer par email
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
            <button
              type="button"
              className="underline underline-offset-2 hover:text-foreground"
              onClick={() => router.push(`/patients/${plan.patientId}`)}
            >
              {plan.patientName ?? "Patient"}
            </button>
            {plan.number && plan.title ? ` · ${plan.title}` : ""}
          </p>
        </CardHeader>

        <CardContent className="space-y-4">
          <PlanProgressBar done={plan.itemsDone} total={plan.itemsTotal} />

          <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
            <Figure label="Total" value={formatDT(plan.totalPlanned)} />
            <Figure label="Encaissé" value={formatDT(plan.amountPaid)} />
            {/* A Draft devis contributes 0 to « Solde patient » by design, so showing a « Reste » here would
                contradict the balance the rest of the app reports. */}
            {!isDraft && <Figure label="Reste" value={formatDT(plan.outstanding)} />}
            <Figure
              label="Actes réalisés"
              value={plan.itemsTotal > 0 ? `${plan.itemsDone}/${plan.itemsTotal}` : "—"}
            />
          </div>

          <p className="text-sm text-foreground">
            {isDraft
              ? "À accepter pour démarrer le suivi."
              : plan.nextAppointmentAt
                ? `Prochaine séance : ${formatDateFr(plan.nextAppointmentAt)}`
                : "Aucune séance planifiée"}
          </p>

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
              <div className={`${CARDS_ONLY} space-y-3`}>
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
                      subtitle={(a) => a.item.codeActe}
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
                      actions={(a) => (
                        <PlanActPrimaryAction
                          plan={plan}
                          item={a.item}
                          onSchedule={(target) => startBooking([[target]])}
                          onUndo={canCorrectActs ? setUndoTarget : undefined}
                        />
                      )}
                    />
                  </section>
                ))}
              </div>

              <Table containerClassName={`${TABLE_ONLY} rounded-md border`}>
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
          <p className="mt-2 text-xs text-muted-foreground">
            Un acte passe à « Réalisé » à l&apos;enregistrement de la fiche de soins liée — il n&apos;y a pas de
            bascule manuelle. Un acte coché par erreur se corrige avec « Détacher la fiche », qui le ramène à
            « Prévu » et réouvre le devis si celui-ci s&apos;était clos dessus.
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
                className={CARDS_ONLY}
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
                            Envoyer par email — {formatDT(payment.amount)}
                          </DropdownMenuItem>
                        ))}
                      </DropdownMenuContent>
                    </DropdownMenu>
                  )
                }}
              />

              <Table containerClassName={`${TABLE_ONLY} rounded-md border`}>
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
                        <TableCell className="text-right">{formatDT(inst.amountPaid)}</TableCell>
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
                                  title={`Envoyer par email le reçu du paiement de ${formatDT(payment.amount)}`}
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
                          </div>
                        </TableCell>
                      </TableRow>
                    )
                  })}
                </TableBody>
              </Table>
            </>
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
      <Dialog open={!!undoTarget} onOpenChange={(open) => { if (!open) setUndoTarget(null) }}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>Détacher la fiche de cet acte ?</DialogTitle>
            <DialogDescription>
              « {undoTarget?.designationFr} » repassera à « Prévu » et sa fiche de soins sera détachée. La fiche
              elle-même n&apos;est pas supprimée. Si ce devis s&apos;était clos sur cet acte, il sera réouvert.
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
    </div>
  )
}

function Figure({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <p className="text-xs text-muted-foreground">{label}</p>
      <p className="text-lg font-semibold">{value}</p>
    </div>
  )
}
