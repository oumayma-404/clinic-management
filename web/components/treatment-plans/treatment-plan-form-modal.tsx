"use client"

import type React from "react"
import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import { Dialog, DialogBody, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from "@/components/ui/dialog"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import { Button } from "@/components/ui/button"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { useConflict } from "@/lib/hooks/use-conflict"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Badge } from "@/components/ui/badge"
import { Textarea } from "@/components/ui/textarea"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent,
  AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from "@/components/ui/command"
import { Check, Plus, ReceiptText, Search, Trash2, X } from "lucide-react"
import { toast } from "sonner"
import {
  treatmentPlansApi,
  type TreatmentPlanItemInput,
  type TreatmentPlanItemStepInput,
  type TreatmentPlanInstallmentInput,
  type CreateTreatmentPlanRequest,
  type UpdateTreatmentPlanRequest,
} from "@/lib/api/treatment-plans"
import {
  catalogueLineCost, seedCost, type OdontogramPlanSeed, type SeedCandidate,
} from "@/components/odontogram-plan-seed"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import { groupProceduresByCategory } from "@/components/procedure-categories"
import { patientsApi } from "@/lib/api/patients"
import type {
  TreatmentPlanDto,
  TreatmentPlanItemDto,
  PatientDto,
  ProcedureTypeDto,
} from "@/lib/api/types"
import { formatAmount, formatDT, formatDateTime, parseAmountInput, quoteFr, todayLocalIso } from "@/lib/format"
import { addMonths, format, parseISO } from "date-fns"
import { installmentDueLabel } from "./treatment-plan-labels"
import { ToothMultiSelect } from "@/components/tooth-multiselect"
import { conditionStyle } from "@/components/odontogram-conditions"
import { cn } from "@/lib/utils"
import { actRemovalPlan, type ActRemovalPlan } from "@/components/treatment-plans/plan-next-action"
import { REFUND_DECLINED, usePlanRefundConfirm } from "@/components/treatment-plans/plan-refund-confirm"

interface LineRow {
  /**
   * The existing act this row stands for, when editing. Echoed back on save so the server keeps that act's
   * id — otherwise every draft edit re-issues the ids and silently orphans any appointment or dental-record
   * link pointing at those acts (neither has an FK to catch it).
   */
  id: string | null
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
  /** The other acts that treat this line's diagnosis, best first. Empty on a hand-added row. */
  candidates?: SeedCandidate[]
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
  /** The remise already granted on this act (read-only here) — the total is the fee minus it (F2). */
  discount?: number
  toothNumbers: number[]
  /**
   * The séances this act will be carried out over — the procedure's protocol, ticked and editable before the
   * devis is accepted.
   *
   * ⚠️ Absent means « this act has no protocol », which is most acts: nineteen of the thirty-three starter
   * acts are single-séance and the panel does not mention them at all. An empty array is different and is a
   * decision — every step unticked, « cet acte se fait en une séance » — and is sent as `[]` so the server
   * does not helpfully re-apply the protocol the dentist just declined.
   */
  steps?: StepRow[]
  /**
   * Has the dentist touched this act's séances? Until they do, the list **follows** whichever act the row is
   * set to, so picking a different act re-proposes that act's protocol. Afterwards it is left alone — the same
   * rule and the same reason as `costTouched`, which exists because a fee prefilled by a previous pick could
   * not be told from one the dentist typed.
   */
  stepsTouched?: boolean
}

/**
 * A line's séances as one comparable string, over the fields that are actually sent — so « did the steps
 * change? » and « what will be saved » cannot answer differently. `null` for a line carrying no protocol,
 * which is not the same as one whose protocol is empty.
 */
function stepSignature(steps: TreatmentPlanItemStepInput[] | undefined): string | null {
  // ⚠️ `[]` and « no steps » are the same stored fact — returning "" here made every step-less act look edited,
  // so a save with no change bumped the révision (F9).
  if (!steps || steps.length === 0) return null
  return steps
    .map(
      (st) =>
        `${st.id ?? ""}|${st.label.trim()}|${st.estimatedDurationMinutes ?? ""}|${st.minDaysAfterPrevious ?? ""}`,
    )
    .join("~")
}

/** The same signature, computed from the stored act — the other half of the comparison. */
function storedStepSignature(item: TreatmentPlanItemDto): string | null {
  const steps = item.steps ?? []
  if (steps.length === 0) return null
  return steps
    .map(
      (st) =>
        `${st.id}|${st.label.trim()}|${st.estimatedDurationMinutes ?? ""}|${st.minDaysAfterPrevious ?? ""}`,
    )
    .join("~")
}

/**
 * Accent-, case- and space-insensitive comparison key for an act's name — the same fold the backend's
 * `CategoryFolding` applies to a category, and for the same reason: « Implant dentaire » typed by hand and
 * picked from the catalogue must be recognised as one act.
 */
const fold = (value: string): string =>
  value
    .normalize("NFD")
    // The combining-diacritic block, as escapes: a literal range here is invisible in a diff and easy to
    // mangle in an editor that normalises the file.
    .replace(/[\u0300-\u036f]/g, "")
    .toLowerCase()
    .replace(/\s+/g, " ")
    .trim()

/** One proposed séance in the confirmation panel. `duration` is a string because it is typed into an input. */
interface StepRow {
  /**
   * The existing step this row stands for, echoed back on an amendment so it keeps its id — and with it its
   * `doneDate`, its fiche link and any appointment booked for it. Null (or absent) means a new séance.
   */
  id?: string | null
  label: string
  duration: string
  /**
   * Calendar days to wait after the previous séance. A different quantity from `duration`: that one sizes the
   * appointment, this one decides when it is due, and its absence is why the worklist alarmed on a flat
   * fortnight whatever the protocol.
   */
  interval?: string
  /** Unticked = not part of this devis. Kept in the list rather than removed, so it can be re-ticked. */
  include: boolean
  /** Already carried out. It cannot be unticked or removed — the aggregate refuses it, and rightly. */
  done?: boolean
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

/**
 * An act's protocol as proposed séances, all ticked. `undefined` when the act has none — which is most acts,
 * and is what keeps the confirmation panel off the screen for an ordinary devis.
 */
const proposedStepsFor = (pt: ProcedureTypeDto | undefined): StepRow[] | undefined => {
  if (!pt?.defaultSteps || pt.defaultSteps.length === 0) return undefined
  return pt.defaultSteps.map((step) => ({
    id: null,
    label: step.label,
    duration: step.durationMinutes != null ? String(step.durationMinutes) : "",
    // The clinical interval travels with the label — see `ProcedureStepTemplateDto.minDaysAfterPrevious`.
    interval: step.minDaysAfterPrevious != null ? String(step.minDaysAfterPrevious) : "",
    include: true,
  }))
}

const emptyLine = (): LineRow => ({
  id: null,
  procedureTypeId: null,
  designationFr: "",
  plannedCost: "",
  costTouched: false,
  toothNumbers: [],
})

/**
 * A draft act line pre-filled from the odontogram (« Créer un plan depuis l'odontogramme »).
 *
 * ⚠️ An **alias**, not a second declaration. The two were kept in step by hand and had already drifted on
 * whether the diagnosis fields were optional; every field here is produced by exactly one writer, so the seed's
 * own shape is the contract.
 */
export type TreatmentPlanSeedLine = OdontogramPlanSeed

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
  /**
   * The act the dialog was opened **about**, from « Modifier » on that act's own row. Its line is scrolled to
   * and outlined; every other line is editable exactly as before.
   *
   * ⚠️ Without it, « Modifier » on the third act opens a dialog showing the first — which on a six-act devis
   * is the same « where is it? » the control was added to remove.
   */
  focusItemId?: string | null
  /** When opened from a patient page, the patient is preset and locked. */
  presetPatientId?: string
  presetPatientName?: string
  /** Pre-fill the act lines from charted diagnoses (new plans only). */
  seedLines?: TreatmentPlanSeedLine[]
  onSuccess?: () => void
}

export function TreatmentPlanFormModal({
  open,
  onOpenChange,
  editingPlan,
  amendMode = false,
  focusItemId = null,
  presetPatientId,
  presetPatientName,
  seedLines,
  onSuccess,
}: TreatmentPlanFormModalProps) {
  const [patients, setPatients] = useState<PatientDto[]>([])
  const [procedureTypes, setProcedureTypes] = useState<ProcedureTypeDto[]>([])
  /** At least one of the three picker reads failed — never conflated with "the catalogue is empty". */
  const [pickersFailed, setPickersFailed] = useState(false)
  const [patientId, setPatientId] = useState("")
  const [title, setTitle] = useState("")
  const [notes, setNotes] = useState("")
  const [lines, setLines] = useState<LineRow[]>([emptyLine()])
  /** Which act's séances are open for editing in the confirmation panel, by line index. */
  const [editingStepsFor, setEditingStepsFor] = useState<number | null>(null)
  /*
   * The acts with a protocol, with their line index kept — the panel needs the index to write back, and the
   * list is derived rather than stored so adding, removing or re-picking an act cannot leave it stale.
   */
  /*
   * ⚠️ **Every named act, not only the ones that already carry a protocol.** The filter required
   * `steps.length > 0`, so an act the catalogue does not cut into séances never rendered a panel — and the
   * panel is the only place the creation form can add a step. Its absence then read as « this act has no
   * steps » rather than « you cannot set them here », which is a different claim: the workspace carries
   * « Définir les étapes de … » one click away, and `/procedure-types` offers « + Découper en étapes » on all
   * twenty stepless rows. A protocol-less act now gets the same affordance where the devis is written.
   */
  const stepProposals = useMemo(
    () =>
      lines
        .map((line, index) => ({ index, line }))
        .filter(({ line }) => line.designationFr.trim() !== ""),
    [lines],
  )
  const [installments, setInstallments] = useState<InstallmentRow[]>([])
  const [pickerOpenIndex, setPickerOpenIndex] = useState<number | null>(null)
  const [loading, setLoading] = useState(false)
  /**
   * The form's error state, and it knows about 409s — C10.
   *
   * <p>⚠️ <b>This dialog is the poisoned-dialog trap the root guide records, and it had the whole shape of
   * it.</b> A hand-rolled `conflictMessage` wrote the refusal into a plain `setError`, and the save
   * deliberately does <b>not</b> re-read on a 409 (rightly — a silent resync + retry overwrites the colleague
   * who caused it). So the version the form holds never moved, every later press repeated the identical
   * refusal, and the escalated second message then told the user to coordinate with a colleague while
   * offering no control to see what that colleague had written. The measured production case on the
   * appointment dialog was six refusals over 81 minutes until the user reloaded the page.</p>
   *
   * <p>`useConflict` supplies the same escalation and, crucially, `isConflict` — which drives the
   * « Recharger » action on the banner. The rule that a retry must not silently resync is unchanged; what
   * changed is that reloading is now something the user can <b>ask for</b>, which is exactly what the server's
   * own sentence tells them to do.</p>
   */
  const conflict = useConflict()
  // G3 — a total lowered below what was collected asks « Rendre au patient ? » once, then re-sends.
  const { withRefund, refundDialog } = usePlanRefundConfirm()
  const error = conflict.error
  const setError = conflict.setError
  const guard = useDirtyGuard(open, onOpenChange)
  /*
   * ⚠️ The version this form's COPY was read at — set by `hydrateFrom`, never by the page's live `editingPlan`:
   * the workspace re-reads the plan on every realtime event, so saving with the page's version let a stale
   * form overwrite a colleague with a 200 (F3). Moved only by our own writes and by « Recharger ».
   */
  const hydratedVersionRef = useRef(0)
  /** After a failure that is not a 409, our own partial write may have moved the row: take its version. */
  const resync = async () => {
    if (!editingPlan) return
    try {
      hydratedVersionRef.current = (await treatmentPlansApi.get(editingPlan.id)).version
    } catch {
      // The save's own 409 remains the backstop.
    }
  }

  const isEditing = !!editingPlan
  const isAmending = amendMode && !!editingPlan

  /**
   * What removing each existing act would do, keyed by act id — the refusal when there is one, and otherwise
   * the rendez-vous that travels with it. `actRemovalPlan` is the single mirror of
   * `TreatmentPlan.EnsureItemRemovable`.
   *
   * ⚠️ It used to be derived from `planItemState`, which answers for the act's NEXT STEP: a passed visit
   * disabled the bin with « un rendez-vous est prévu » on a removal the server allows, while a bridge one
   * séance in enabled it on one the server refuses.
   */
  const removalPlans = new Map<string, ActRemovalPlan>()
  if (isAmending && editingPlan) {
    for (const item of editingPlan.items) {
      removalPlans.set(item.id, actRemovalPlan(editingPlan, item))
    }
  }

  const removalBlockerFor = (id: string | null): string | undefined => {
    if (!id) return undefined
    const plan = removalPlans.get(id)
    return plan && !plan.removable ? plan.reason : undefined
  }

  /** The catalogue grouped by discipline — shared with the booking and fiche pickers, never re-derived. */
  const procedureGroups = useMemo(() => groupProceduresByCategory(procedureTypes), [procedureTypes])

  /*
   * The three lists this editor picks from — and, crucially, **whether each read failed**.
   *
   * ⚠️ Both used to end in `.catch(() => setX([]))`. On a devis editor that is not a soft degradation: an
   * empty « Mes actes » leaves the practitioner typing a désignation and a fee by hand, so the plan is created
   * with no `procedureTypeId` — no procédure when the act is later booked. An empty patient list on the create
   * path is worse: the form's only required field has no selectable value, and « aucun patient » in a clinic
   * with three hundred reads as the software having lost them.
   */
  const loadPickers = useCallback(async () => {
    const [proceduresResult, patientsResult] = await Promise.allSettled([
      procedureTypesApi.list(false),
      presetPatientId ? Promise.resolve(null) : patientsApi.list({ limit: 500 }),
    ])

    if (proceduresResult.status === "fulfilled") setProcedureTypes(proceduresResult.value)
    if (patientsResult.status === "fulfilled" && patientsResult.value) setPatients(patientsResult.value)

    setPickersFailed(proceduresResult.status === "rejected" || patientsResult.status === "rejected")
  }, [presetPatientId])

  /** Whether the échéancier was edited by hand — only then is it sent (F9). */
  const [installmentsTouched, setInstallmentsTouched] = useState(false)

  /*
   * The form's copy of a plan. ⚠️ Called ONCE per open (and again on « Recharger »), never on every new
   * `editingPlan` object: the workspace re-reads the plan on every realtime event, so hydrating on identity wiped
   * whatever the dentist had typed the moment anyone saved anything (F1).
   */
  const hydrateFrom = (plan: TreatmentPlanDto) => {
      // A parked act is not part of the devis' total and cannot be amended — it comes back via « Rétablir » (F2).
      hydratedVersionRef.current = plan.version
      const liveItems = plan.items.filter((it) => !it.isWithdrawn)
      setPatientId(plan.patientId)
      setTitle(plan.title)
      setNotes(plan.notes ?? "")
      setLines(
        liveItems.length > 0
          ? liveItems.map((it) => ({
              id: it.id,
              procedureTypeId: it.procedureTypeId,
              designationFr: it.designationFr,
              plannedCost: formatAmount(it.plannedCost),
              // A stored fee is the number that was agreed with the patient, whatever the catalogue says today.
              // It is never re-derived from a default, so re-picking an act to fix its designation cannot
              // reprice work already quoted.
              costTouched: true,
              discount: it.discountAmount ?? 0,
              toothNumbers: it.toothNumbers,
              /*
               * ⚠️ **The act's own séances, and they were not hydrated at all** — so « Étapes proposées » was
               * empty for every existing act and the dialog's own description (« Seul le patient n'est pas
               * modifiable ») was false: the steps were not modifiable there either. Confirmed in the browser
               * on a 6-step act with zero step controls on screen.
               *
               * `stepsTouched: true` because a stored protocol is already somebody's decision: re-picking the
               * act to correct its designation must not silently re-propose the catalogue's version over the
               * sequence this patient was quoted.
               */
              steps: (it.steps ?? []).map((st) => ({
                id: st.id,
                label: st.label,
                duration: st.estimatedDurationMinutes != null ? String(st.estimatedDurationMinutes) : "",
                interval: st.minDaysAfterPrevious != null ? String(st.minDaysAfterPrevious) : "",
                include: true,
                // A réalisé step cannot be dropped or re-ordered — the aggregate refuses it, because the row
                // holds the only link to the fiche that evidences it. Said on the control rather than as a
                // refusal after the save.
                done: st.doneDate != null,
              })),
              stepsTouched: true,
            }))
          : [emptyLine()],
      )
      setInstallments(
        plan.installments.map((inst) => ({
          id: inst.id,
          dueDate: inst.dueDate.slice(0, 10),
          amount: formatAmount(inst.amount),
          amountPaid: inst.amountPaid,
        })),
      )
      setInstallmentsTouched(false)
  }

  const hydratedForRef = useRef<string | null>(null)
  useEffect(() => {
    if (!open) {
      hydratedForRef.current = null
      return
    }
    const key = editingPlan?.id ?? "new"
    if (hydratedForRef.current === key) return
    hydratedForRef.current = key

    void loadPickers()

    if (editingPlan) {
      hydrateFrom(editingPlan)
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
              // Carried when the odontogram could tie the charted condition to exactly one procedure.
              procedureTypeId: s.procedureTypeId ?? null,
              designationFr: s.designationFr,
              diagnosisLabel: s.diagnosisLabel,
              diagnosisCondition: s.diagnosisCondition,
              candidates: s.candidates,
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
      setInstallmentsTouched(false)
    }
    // `reset`, not `setError(null)`: a fresh open starts a fresh conflict streak.
    conflict.reset()
    // Seeds once when the dialog opens.
    // eslint-disable-next-line react-hooks/exhaustive-deps -- `conflict.reset` is stable; listing it re-seeds
  }, [open, editingPlan, presetPatientId, seedLines, loadPickers])

  const updateLine = (index: number, patch: Partial<LineRow>) => {
    setLines((prev) => prev.map((l, i) => (i === index ? { ...l, ...patch } : l)))
  }

  // Teeth changed on a line whose fee nobody typed: the fee follows the tooth count (G4).
  const updateLineTeeth = (index: number, teeth: number[]) => {
    setLines((prev) =>
      prev.map((l, i) => {
        if (i !== index) return l
        const next = { ...l, toothNumbers: teeth }
        if (l.costTouched || !l.procedureTypeId) return next
        return { ...next, plannedCost: repricedFor(next, procedureTypes.find((pt) => pt.id === l.procedureTypeId)) }
      }),
    )
  }

  const addLine = () => setLines((prev) => [...prev, emptyLine()])
  const removeLine = (index: number) =>
    setLines((prev) => (prev.length > 1 ? prev.filter((_, i) => i !== index) : prev))

  /*
   * The bin on an act the patient is booked for asks first, because the save will settle that booking too —
   * cancelling a visit this act was the only reason for, and dropping the act from one that carries others.
   * Asked at the press rather than at the save: the row is gone from the form immediately and cannot be
   * re-added, so that press IS the decision.
   */
  const [pendingRemoval, setPendingRemoval] = useState<{ index: number; label: string } | null>(null)

  /*
   * Put the act « Modifier » was pressed on in front of the reader. Deferred a frame because the dialog's
   * content mounts in the same tick `open` flips, so `scrollIntoView` in the effect body finds an element
   * with no scrollport above it.
   */
  const lineRefs = useRef(new Map<string, HTMLDivElement | null>())
  useEffect(() => {
    if (!open || !focusItemId) return
    const frame = requestAnimationFrame(() => {
      lineRefs.current.get(focusItemId)?.scrollIntoView({ block: "center" })
    })
    return () => cancelAnimationFrame(frame)
    // `lines` so the scroll happens once the acts have been hydrated, not against an empty form.
  }, [open, focusItemId, lines])

  const requestRemoveLine = (index: number) => {
    const line = lines[index]
    const plan = line.id ? removalPlans.get(line.id) : undefined
    if (!plan || !plan.removable || !plan.booking) {
      removeLine(index)
      return
    }
    const when = formatDateTime(plan.booking.at)
    setPendingRemoval({
      index,
      label:
        plan.booking.sharedWith > 0
          ? `L'acte sera retiré du rendez-vous du ${when}, qui reste prévu pour les autres actes.`
          : `Le rendez-vous du ${when} sera annulé : cet acte est la seule raison de cette séance.`,
    })
  }

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
  const repricedFor = (line: LineRow, pt: ProcedureTypeDto | undefined): string => {
    if (line.costTouched) return line.plannedCost
    // G4: the same per-tooth rule as the odontogram seed — a 3-tooth act was quoted once.
    const cost = pt ? catalogueLineCost(pt, line.toothNumbers.length) : undefined
    return cost != null && cost > 0 ? formatAmount(cost) : ""
  }

  /**
   * The catalogue act a typed designation names, when it names one — accent- and case-insensitively, on the
   * whole trimmed string, so « implant dentaire » finds « Implant dentaire » and « implant » finds nothing
   * (a prefix match would offer the wrong one of two similarly-named acts).
   *
   * Null for a row already linked to a procedure, and for a genuinely bespoke designation.
   */
  const catalogueMatchFor = useCallback(
    (line: LineRow): ProcedureTypeDto | null => {
      if (line.procedureTypeId) return null
      const typed = fold(line.designationFr)
      if (typed.length < 3) return null
      return procedureTypes.find((pt) => fold(pt.name) === typed) ?? null
    },
    [procedureTypes],
  )

  // The procedure is the ONLY catalog a devis line comes from now, and its `procedureTypeId` IS
  // kept. Snapshotting only the name (as this did) meant booking the act could not preselect the procedure,
  // so a plan-scheduled appointment got no procedure type, no colour and no default duration.
  const selectProcedureType = (index: number, pt: ProcedureTypeDto) => {
    setLines((prev) =>
      prev.map((l, i) => {
        if (i !== index) return l
        /*
         * ⚠️ **Changing the act is not the same gesture as correcting its name, and conflating them is what
         * the client reported.** A line loaded from an existing devis arrives `costTouched` + `stepsTouched`
         * — rightly: a stored fee is the number agreed with the patient and a stored protocol is somebody's
         * decision, so re-picking the *same* act to fix a typo must leave both alone. But swapping the act
         * for a **different** one makes that fee and that protocol the *previous* act's, and keeping them is
         * how « Détartrage » sat at the couronne's fee with the couronne's three séances under it.
         *
         * ⚠️ The séances only follow when **nothing has been recorded** on them: `SetSteps` refuses dropping
         * a séance already carried out, so re-proposing the catalogue's list over a part-done act would make
         * the save fail outright.
         */
        const swapped = (l.procedureTypeId ?? null) !== pt.id
        const stepsAreRecorded = (l.steps ?? []).some((st) => st.done)
        const follows = swapped && !stepsAreRecorded
        const base: LineRow = follows ? { ...l, costTouched: false, stepsTouched: false } : l
        return {
          ...base,
          procedureTypeId: pt.id,
          designationFr: pt.name,
          plannedCost: repricedFor(base, pt),
          // The act's protocol, proposed — unless the dentist has already edited this row's séances.
          ...(base.stepsTouched ? {} : { steps: proposedStepsFor(pt) }),
        }
      }),
    )
    setPickerOpenIndex(null)
  }

  /*
   * "Detach from the catalogue" makes the line pure free text, so it drops the procedure link too — and with
   * it the protocol, which belonged to that procedure. A hand-typed line the dentist has already cut into
   * séances keeps them (`stepsTouched`): those are their own work, not the catalogue's.
   */
  const detachAct = (index: number) =>
    setLines((prev) =>
      prev.map((l, i) =>
        i === index
          ? { ...l, procedureTypeId: null, ...(l.stepsTouched ? {} : { steps: undefined }) }
          : l,
      ),
    )

  /** Tick or untick one proposed séance. Marks the row touched, so a later act change leaves it alone. */
  const toggleStepRow = (lineIndex: number, stepIndex: number) =>
    setLines((prev) =>
      prev.map((l, i) =>
        i === lineIndex
          ? {
              ...l,
              stepsTouched: true,
              steps: l.steps?.map((st, j) => (j === stepIndex ? { ...st, include: !st.include } : st)),
            }
          : l,
      ),
    )

  const updateStepRow = (lineIndex: number, stepIndex: number, patch: Partial<StepRow>) =>
    setLines((prev) =>
      prev.map((l, i) =>
        i === lineIndex
          ? {
              ...l,
              stepsTouched: true,
              steps: l.steps?.map((st, j) => (j === stepIndex ? { ...st, ...patch } : st)),
            }
          : l,
      ),
    )

  const addStepRow = (lineIndex: number) =>
    setLines((prev) =>
      prev.map((l, i) =>
        i === lineIndex
          ? {
              ...l,
              stepsTouched: true,
              steps: [...(l.steps ?? []), { id: null, label: "", duration: "", interval: "", include: true }],
            }
          : l,
      ),
    )

  const removeStepRow = (lineIndex: number, stepIndex: number) =>
    setLines((prev) =>
      prev.map((l, i) =>
        i === lineIndex
          ? { ...l, stepsTouched: true, steps: l.steps?.filter((_, j) => j !== stepIndex) }
          : l,
      ),
    )

  /** Puts an act's séances back to its procedure's protocol — the way out of any edit. */
  const resetStepRows = (lineIndex: number) =>
    setLines((prev) =>
      prev.map((l, i) =>
        i === lineIndex
          ? {
              ...l,
              stepsTouched: false,
              steps: proposedStepsFor(procedureTypes.find((pt) => pt.id === l.procedureTypeId)),
            }
          : l,
      ),
    )

  /** Take one of the diagnosis' other treatments. Reprices unless the dentist has typed a fee themselves. */
  const applyCandidate = (index: number, candidate: SeedCandidate) => {
    const line = lines[index]
    const cost = seedCost(candidate, line.toothNumbers.length)
    // Same rule as `selectProcedureType`: a chip that swaps the act reprices and re-proposes the séances,
    // one that re-picks the act already chosen leaves both alone. It used to reprice and never touch the
    // protocol at all, so swapping a one-séance act for a six-séance one left the line with no protocol.
    const swapped = (line.procedureTypeId ?? null) !== candidate.procedureTypeId
    const stepsAreRecorded = (line.steps ?? []).some((st) => st.done)
    const follows = swapped && !stepsAreRecorded
    const pt = procedureTypes.find((p) => p.id === candidate.procedureTypeId)
    updateLine(index, {
      procedureTypeId: candidate.procedureTypeId,
      designationFr: candidate.name,
      ...(follows ? { costTouched: false, stepsTouched: false, steps: proposedStepsFor(pt) } : {}),
      ...((follows || !line.costTouched) && cost != null ? { plannedCost: formatAmount(cost) } : {}),
    })
  }

  /**
   * One line per tooth, from a line carrying several.
   *
   * ⚠️ A grouped line is planned, booked and marked réalisé as a unit, and two teeth in opposite quadrants are
   * very often two sessions — so this is the escape hatch grouping needs, not a nicety. The fee is **divided**
   * for an act priced per tooth (the grouped line's cost was `unit × teeth`) and **kept whole** otherwise, which
   * is the same rule that built the line: a session fee does not shrink because the work was split in two.
   */
  const splitLine = (index: number) => {
    setLines((prev) => {
      const line = prev[index]
      if (line.toothNumbers.length < 2) return prev
      const procedure = procedureTypes.find((pt) => pt.id === line.procedureTypeId)
      const perTooth = procedure?.resultingCondition != null
      const total = parseAmountInput(line.plannedCost)
      const each =
        perTooth && Number.isFinite(total)
          ? formatAmount(Math.round((total / line.toothNumbers.length) * 1000) / 1000)
          : line.plannedCost
      const split = line.toothNumbers.map((tooth, position) => ({
        ...line,
        // A split line is a NEW act, never the one being edited — echoing one id on several rows would have the
        // server rewrite the same act N times and keep only the last.
        id: null,
        toothNumbers: [tooth],
        plannedCost: each,
        /*
         * ⚠️ **The protocol stays on the FIRST row only, and `{...line}` used to copy it onto every one.** So
         * the money was divided per tooth and the séances were multiplied by it: splitting a 3-step act across
         * four teeth quoted **twelve séances** for a bridge that takes three, each row claiming Préparation,
         * Empreinte and Scellement. And the catalogue actively invites the split — « Couronne / bridge (par
         * élément) », « Facette (par élément) » — so the button sits right beside a per-element act.
         *
         * A protocol describes the ACT, not each tooth. Keeping it on the first row and giving the rest a fresh
         * `id: null` step list means the dentist confirms the séances once, on the line that carries them, and
         * can still cut the others up by hand if this really is four separate courses of treatment.
         */
        steps:
          position === 0
            ? line.steps?.map((st) => ({ ...st, id: null }))
            : undefined,
        stepsTouched: position === 0 ? line.stepsTouched : false,
        diagnosisLabel: line.diagnosisCondition
          ? `${conditionStyle(line.diagnosisCondition).label} — dent ${tooth}`
          : line.diagnosisLabel,
      }))
      return [...prev.slice(0, index), ...split, ...prev.slice(index + 1)]
    })
  }

  const updateInstallment = (index: number, patch: Partial<InstallmentRow>) => {
    setInstallmentsTouched(true)
    setInstallments((prev) => prev.map((r, i) => (i === index ? { ...r, ...patch } : r)))
  }
  const addInstallment = () => {
    setInstallmentsTouched(true)
    setInstallments((prev) => [
      ...prev,
      { id: null, dueDate: todayLocalIso(), amount: "", amountPaid: 0 },
    ])
  }
  const removeInstallment = (index: number) => {
    setInstallmentsTouched(true)
    setInstallments((prev) => prev.filter((_, i) => i !== index))
  }

  // What the patient owes: each fee minus the remise already granted on it (F2).
  const total = Math.round(lines.reduce((sum, l) => {
    const cost = parseAmountInput(l.plannedCost)
    return Number.isFinite(cost) ? sum + Math.max(0, cost - (l.discount ?? 0)) : sum
  }, 0) * 1000) / 1000

  /** « Répartir le solde sur N mois » — paid rows keep what they took, the rest is spread monthly (F10). */
  const [spreadMonths, setSpreadMonths] = useState("3")
  const spreadBalance = () => {
    const months = Number.parseInt(spreadMonths, 10)
    if (!Number.isFinite(months) || months < 1 || months > 60) {
      setError("Indiquez un nombre de mois entre 1 et 60.")
      return
    }
    const paid = installments
      .filter((r) => r.amountPaid > 0)
      .map((r) => ({ ...r, amount: formatAmount(r.amountPaid) }))
    const collected = paid.reduce((sum, r) => sum + r.amountPaid, 0)
    const remaining = Math.round((total - collected) * 1000) / 1000
    const rows: InstallmentRow[] = [...paid]
    if (remaining > 0) {
      const share = Math.floor((remaining * 1000) / months) / 1000
      const start = parseISO(todayLocalIso())
      for (let i = 0; i < months; i++) {
        const amount = i === months - 1 ? Math.round((remaining - share * (months - 1)) * 1000) / 1000 : share
        rows.push({ id: null, dueDate: format(addMonths(start, i), "yyyy-MM-dd"), amount: formatAmount(amount), amountPaid: 0 })
      }
    }
    setInstallmentsTouched(true)
    setInstallments(rows)
  }

  const installmentsSum = installments.reduce((sum, r) => {
    const amt = parseAmountInput(r.amount)
    return Number.isFinite(amt) ? sum + amt : sum
  }, 0)

  const installmentsMatch = installments.length === 0 || Math.abs(installmentsSum - total) < 0.0005

  /**
   * The title the devis takes when the dentist types none — the first act's name, or « Plan de traitement » for
   * a plan of several. Shown as the field's placeholder, so what will be used is visible before the save.
   */
  const derivedTitle = useMemo(() => {
    const named = lines.filter((l) => l.designationFr.trim() !== "")
    if (named.length === 0) return ""
    return named.length === 1 ? named[0].designationFr.trim() : "Plan de traitement"
  }, [lines])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    // ⚠️ `clearMessage`, never `setError(null)`: that one resets the consecutive-conflict counter, so a form
    // clearing its banner at the top of every submit can never reach a second 409 and the escalated wording
    // is unreachable. `use-conflict.ts` records this measured verbatim.
    conflict.clearMessage()

    if (!patientId) {
      setError("Veuillez sélectionner un patient.")
      return
    }
    /*
     * ⚠️ **The title is derived rather than demanded.** It was the SOLE required field: pressing « Créer le
     * plan » with everything else already filled from one act pick — the designation, the fee, the six séances
     * and their durations — answered « Le titre est obligatoire. » and fired no request. Twenty devis a week,
     * and the only keystrokes the form demanded were a label derivable from the act just inserted. The field
     * stays editable and its placeholder now shows what will be used.
     */
    const effectiveTitle = title.trim() || derivedTitle
    if (!effectiveTitle) {
      setError("Ajoutez au moins un acte, ou saisissez un titre.")
      return
    }

    // ⚠️ A blank name on an EXISTING act used to drop the line, i.e. delete the act and cancel its visit with no
    // confirmation (F8). Removing an act is the bin's job.
    const blankExisting = lines.find((l) => l.id && l.designationFr.trim() === "")
    if (blankExisting) {
      setError("Donnez un nom à chaque acte — pour en retirer un, utilisez la corbeille.")
      return
    }

    const parsedLines: TreatmentPlanItemInput[] = lines
      .map((l) => ({
        id: l.id,
        procedureTypeId: l.procedureTypeId,
        designationFr: l.designationFr.trim(),
        plannedCost: parseAmountInput(l.plannedCost),
        toothNumbers: l.toothNumbers,
        /*
         * The séances the dentist confirmed, and the tri-state matters at every point:
         *   • no protocol at all      → `undefined`, and the server applies the procedure's own (which is
         *                               also nothing for the nineteen single-séance acts)
         *   • a protocol, some ticked → the ticked ones, in order
         *   • a protocol, none ticked → `[]`, an explicit « cet acte se fait en une séance ». Sending
         *                               `undefined` here would have the server helpfully re-apply the very
         *                               protocol the dentist just declined.
         * A blank label is dropped rather than refused: a row added and left empty is not a decision.
         */
        steps: l.steps
          ? l.steps
              .filter((st) => st.include && st.label.trim() !== "")
              .map((st) => ({
                // Echoed back so an existing séance keeps its identity — its réalisé date, the fiche that
                // evidences it and any appointment already booked for it. Without it every amendment would be
                // a delete-and-recreate, which the aggregate refuses as soon as one step is carried out.
                id: st.id ?? null,
                label: st.label.trim(),
                estimatedDurationMinutes: st.duration.trim() === "" ? null : Number(st.duration),
                minDaysAfterPrevious:
                  !st.interval || st.interval.trim() === "" ? null : Number(st.interval),
              }))
          : undefined,
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
    // ⚠️ On an amendment the schedule goes only when it was edited: re-sending it unchanged bumped the révision
    // on a no-op save (F9), and a changed total is re-spread server-side while agreed dates are kept.
    const sendSchedule = !isAmending || installmentsTouched
    // Lowering the total can leave nothing for the later rows: unpaid rows at the end give way (F10).
    const workingRows = [...installments]
    const typedSum = (rows: InstallmentRow[]) =>
      rows.slice(0, -1).reduce((sum, r) => {
        const a = parseAmountInput(r.amount)
        return sum + (Number.isFinite(a) ? a : 0)
      }, 0)
    while (
      sendSchedule
      && workingRows.length > 1
      && workingRows[workingRows.length - 1].amountPaid <= 0
      && total - typedSum(workingRows) < -0.0005
    ) {
      workingRows.pop()
    }
    if (sendSchedule && workingRows.length > 0) {
      const installments = workingRows
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
            // Named the way the échéancier names it. It printed the raw `2026-03-14` here.
            `${installmentDueLabel({ dueDate: row.dueDate })} : déjà encaissé ${formatDT(row.amountPaid)} — `
              + "son montant ne peut pas être ramené en dessous.",
          )
          return
        }
      }
      // An unpaid row left at 0 is not an échéance — the aggregate refuses one, so it is dropped (F10).
      const kept = parsedInstallments.filter((r, i) => r.amount > 0.0005 || installments[i].amountPaid > 0)
      if (kept.length > 0) parsedInstallments = kept
    }

    if (isAmending && editingPlan) {
      const originalIds = new Set(editingPlan.items.filter((i) => !i.isWithdrawn).map((i) => i.id))
      const keptIds = new Set(parsedLines.map((l) => l.id).filter((id): id is string => !!id))
      const removeItemIds = [...originalIds].filter((id) => !keptIds.has(id))

      const blocked = removeItemIds.map((id) => removalBlockerFor(id)).find(Boolean)
      if (blocked) {
        setError(blocked)
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
          (l.procedureTypeId ?? null) !== (before.procedureTypeId ?? null) ||
          l.toothNumbers.join(",") !== before.toothNumbers.join(",") ||
          // ⚠️ **The steps, which this test did not compare.** So a steps-only edit — a renamed séance, a
          // re-ordered protocol, a deleted middle step — was dropped from `updateItems`, and if nothing else
          // had changed the form answered « Aucune modification demandée. » for a change the dentist had just
          // made. The comparison is on the shape actually sent, so it cannot drift from the payload.
          stepSignature(l.steps) !== storedStepSignature(before)
        )
      })

      const retitling = title.trim() !== "" && title.trim() !== editingPlan.title
      const renoting = (notes.trim() || null) !== (editingPlan.notes ?? null)

      if (
        addItems.length === 0 &&
        updateItems.length === 0 &&
        removeItemIds.length === 0 &&
        !(sendSchedule && installments.length > 0) &&
        !retitling &&
        !renoting
      ) {
        setError("Aucune modification demandée.")
        return
      }

      // An échéancier that was dropped entirely is sent as an empty list; the server answers
      // "L'échéancier ne peut pas être vide sur un devis accepté." rather than us guessing a spread.
      const droppedPaidRow = sendSchedule && editingPlan.installments.some(
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
        const amended = await withRefund((refundMethod) => treatmentPlansApi.amend(editingPlan.id, {
          addItems,
          updateItems,
          removeItemIds,
          installments: parsedInstallments,
          title: effectiveTitle,
          // Tri-state server-side and always sent: it compares against the stored value, so an unchanged note
          // is not counted as an amendment and does not bump the révision.
          notes: notes.trim() || null,
          // The row's version as last read, so a peer's edit 409s instead of overwriting their fees — and
          // our own earlier write is not mistaken for one.
          version: hydratedVersionRef.current || editingPlan.version,
          refundMethod,
        }))
        // « Retour » on the rendu question: nothing was saved and the form stays as typed.
        if (amended === REFUND_DECLINED) return
        toast.success("Devis modifié")
        onSuccess?.()
        onOpenChange(false)
      } catch (err) {
        // A non-conflict failure may still have moved the row; a real 409 is left alone, or the retry would
        // silently overwrite the colleague who caused it — « Recharger » on the banner is their door instead.
        if (!conflict.capture(err, "Échec de la modification du devis.")) await resync()
      } finally {
        setLoading(false)
      }
      return
    }

    setLoading(true)
    try {
      if (isEditing && editingPlan) {
        const payload: UpdateTreatmentPlanRequest = {
          title: effectiveTitle,
          notes: notes.trim() || null,
          items: parsedLines,
          installments: parsedInstallments,
        }
        await treatmentPlansApi.update(editingPlan.id, {
          ...payload,
          version: hydratedVersionRef.current || editingPlan.version,
        })
        toast.success("Plan de traitement mis à jour")
      } else {
        const payload: CreateTreatmentPlanRequest = {
          patientId,
          title: effectiveTitle,
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
      // A non-conflict failure may still have moved the row; a real 409 is left alone, or the retry would
      // silently overwrite the colleague who caused it — « Recharger » on the banner is their door instead.
      if (!conflict.capture(err, "Échec de l'enregistrement du plan.")) await resync()
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
            {/* m15 — « Modifier les actes et les prix », the same words the workspace's menu uses. « Modifier
                le devis » was renamed there precisely because it is a homophone of « Éditer le devis », which
                mints the number; leaving the stale wording on the dialog's own title reintroduced it. */}
            {isAmending
              ? "Modifier les actes et les prix"
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

        {/*
          ⚠️ The one thing that used to be REFUSED and is now merely stated. A devis already bridged to a note
          d'honoraires can be corrected — the owner's call, « le médecin doit pouvoir tout corriger » — but the
          note was raised from the old total and does not follow, so the pair diverges the moment this is saved.
          Refusing the edit asked a dentist to reverse a numbered fiscal document in order to fix a plan; saying
          so here puts the fact where the decision is made, and names the correction.

          It is `role="status"`, not `alert`: nothing has gone wrong and nothing is being refused.
        */}
        {isAmending && editingPlan?.linkedInvoiceNumber && (
          <div
            role="status"
            className="flex items-start gap-2 rounded-md border border-warning/40 bg-warning-wash p-3"
          >
            <ReceiptText className="mt-0.5 h-4 w-4 shrink-0 text-warning-ink" aria-hidden="true" />
            <p className="text-2xs leading-relaxed text-warning-ink">
              Ce devis est facturé sur la note{" "}
              <span className="font-mono">{editingPlan.linkedInvoiceNumber}</span>. La note ne suivra pas cette
              correction&nbsp;: si le montant change, corrigez-la par un avoir.
            </p>
          </div>
        )}

        {/* The form owns the remaining height so `DialogBody` scrolls and the footer stays on screen (AC-21). */}
        <form onSubmit={handleSubmit} className="flex min-h-0 flex-1 flex-col gap-4">
          <DialogBody className="space-y-4">
          {/*
            ⚠️ **The « Recharger » that makes a 409 recoverable.** `resync` re-reads the plan and updates the
            version the form saves with — and the user's own input is deliberately left untouched, so they
            re-read what they typed and press Enregistrer again rather than losing it. Without this the banner
            said « rechargez » with no control to do it, which is half a fix.
          */}
          <FormErrorBanner
            message={error}
            action={
              conflict.isConflict
                ? {
                    label: "Recharger",
                    onClick: () => {
                      // Re-hydrated from the server's copy: resyncing the version alone let the stale form
                      // overwrite the colleague on the next press (F3).
                      if (!editingPlan) return
                      void treatmentPlansApi.get(editingPlan.id)
                        .then((row) => { hydrateFrom(row); conflict.clearMessage() })
                        .catch(() => undefined)
                    },
                    disabled: loading,
                  }
                : undefined
            }
          />

          {/* One notice for the three picker reads: they load together, they fail together in practice, and three
              separate banners over one form would say the same thing three times. */}
          {pickersFailed && (
            <LoadFailureNotice
              message="Les listes de sélection n'ont pas pu être chargées."
              detail="La liste des patients et celle de « Mes actes » sont peut-être incomplètes — un acte saisi à la main ne sera rattaché à aucune procédure du catalogue."
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
                Titre
              </Label>
              <Input
                id="title"
                value={title}
                onChange={(e) => setTitle(e.target.value)}
                placeholder={derivedTitle || "Ex. Réhabilitation prothétique"}
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
              placeholder="Notes (facultatives)"
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
                const removalBlocked = removalBlockerFor(line.id)
                const focused = !!line.id && line.id === focusItemId
                return (
                <div
                  key={index}
                  ref={(el) => {
                    if (line.id) lineRefs.current.set(line.id, el)
                  }}
                  className={cn(
                    "rounded-lg border p-3 space-y-2",
                    // A ring rather than a colour: the line is not in a state, it is the one being pointed at.
                    focused && "ring-2 ring-primary/40",
                  )}
                >
                  {/*
                    ⚠️ **`min-w-0` on both flex children, or « Supprimer l'acte » leaves the screen.** A flex
                    item's `min-width` is `auto`, and an `<Input>`'s intrinsic width comes from its placeholder
                    — « Désignation de l'acte (ou choisir au catalogue) » — so the column refused to shrink
                    below ~316 px inside a 215 px box and pushed the row 62 px past the dialog's scrollport.
                    Measured at 320 px: the delete button's right edge at **343** against a port ending at
                    **296**, i.e. the only way to remove a line was a sideways drag nobody tries (§ 0/§ 11).
                    The magnifier beside the field went the same way. Releasing the floor lets the field
                    truncate its own placeholder, which is what it is for.
                  */}
                  <div className="flex min-w-0 items-start gap-2">
                    <div className="min-w-0 flex-1 space-y-1">
                      <div className="flex min-w-0 items-center gap-2">
                        <Input
                          className="min-w-0"
                          value={line.designationFr}
                          onChange={(e) => updateLine(index, { designationFr: e.target.value })}
                          placeholder="Désignation de l'acte (ou choisir au catalogue)"
                          disabled={loading}
                        />
                        {/*
                          ⚠️ **Typing an act's name instead of picking it silently opts out of the entire
                          feature.** The field is labelled « ou choisir au catalogue » so free text is a
                          first-class option, and the magnifier beside it is icon-only — but a typed line carries
                          no `procedureTypeId`, and `TreatmentPlanItemPricing` resolves by that id alone (« les
                          lignes en texte libre ne sont pas touchées »), `TreatmentPlanStepProtocol` needs it to
                          find a protocol, and the séances panel had nothing to render. So a dentist who types
                          « Implant dentaire » gets a one-line devis at whatever price they typed, with no
                          protocol and no fee prefill, and nothing tells them the six researched séances exist.
                          The two paths look equivalent on screen. This offers the match rather than applying
                          it — the typed name may deliberately be a bespoke act.
                        */}
                        {catalogueMatchFor(line) && (
                          <Button
                            type="button"
                            variant="outline"
                            size="sm"
                            className="h-9 shrink-0 gap-1 border-dashed px-2 text-2xs text-primary"
                            disabled={loading}
                            onClick={() => selectProcedureType(index, catalogueMatchFor(line)!)}
                            title={`Utiliser ${quoteFr(catalogueMatchFor(line)!.name)} du catalogue`}
                          >
                            <Check className="h-3.5 w-3.5" />
                            Utiliser le catalogue
                          </Button>
                        )}
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
                          {/*
                            The same list the booking dialog offers — grouped by discipline, with the act's
                            own colour and, crucially, **how many séances it takes**. It was a flat run of the
                            whole catalogue carrying a name and a price, so « Implant dentaire » sat between
                            « Greffe osseuse » and « Contention » with nothing saying that picking it proposes
                            six visits; the dentist learned that after the pick, which is the wrong order on a
                            document a patient signs. `groupProceduresByCategory` is shared with that picker
                            and the fiche's, so the three cannot disagree about where an act lives.

                            ⚠️ The price is shown here and the duration there, deliberately: a devis is about
                            money and a booking is about the diary.
                          */}
                          <PopoverContent
                            className="p-0 w-[min(22rem,calc(100vw-2rem))]"
                            align="end"
                          >
                            <Command>
                              <CommandInput placeholder="Rechercher un acte…" />
                              <CommandList>
                                <CommandEmpty>Aucun acte trouvé.</CommandEmpty>
                                {procedureGroups.map(({ label, items }) => (
                                  <CommandGroup key={label} heading={label}>
                                    {items.map((pt) => (
                                      <CommandItem
                                        key={pt.id}
                                        /* cmdk matches on `value` alone, so the discipline goes in it too —
                                           « endo » has to reach « Traitement de canal ». */
                                        value={pt.category ? `${pt.name} ${pt.category}` : pt.name}
                                        onSelect={() => selectProcedureType(index, pt)}
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
                                            <span className="block text-xs text-muted-foreground">
                                              {formatDT(pt.defaultCost)}
                                            </span>
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

                      {/* The acts that treat it, least invasive first — the answer to « et on fait quoi ? »,
                          which the odontogram could not give at all for a pathology. One tap fills the line.
                          Nothing is auto-applied when several share the first rank: choosing between a simple
                          and a surgical extraction is a judgement about access, not a tie to break. */}
                      {(line.candidates?.length ?? 0) > 0 && (
                        <div className="flex flex-wrap items-center gap-1.5 pt-1">
                          <span className="shrink-0 text-xs text-muted-foreground">Traitements proposés :</span>
                          {line.candidates!.map((candidate) => {
                            const chosen = candidate.procedureTypeId === line.procedureTypeId
                            const cost = seedCost(candidate, line.toothNumbers.length)
                            return (
                              <Button
                                key={candidate.procedureTypeId}
                                type="button"
                                variant={chosen ? "default" : "outline"}
                                size="sm"
                                /* ⚠️ All four of these, and `max-w-full` is the one that actually does it.
                                   `buttonVariants` is `whitespace-nowrap shrink-0`, so « Extraction chirurgicale
                                   (sagesse / dent incluse) 400,000 DT » measured ~400 px and became the act
                                   block's min-content width: at 320 px the line could not shrink below it and the
                                   dialog's own scroller took 160 px of sideways travel, clipping every chip.
                                   `whitespace-normal` alone still left the chip at its max-content width (348 px)
                                   because nothing bounded it — the cap is what makes the label break. An act's
                                   name is the control's name, so it wraps rather than truncates (§ 10.1). */
                                className="h-auto min-h-7 max-w-full flex-wrap gap-x-1.5 gap-y-0 whitespace-normal py-1 text-left text-xs coarse:min-h-11"
                                aria-pressed={chosen}
                                disabled={loading}
                                onClick={() => applyCandidate(index, candidate)}
                              >
                                {candidate.name}
                                {cost != null && (
                                  <span className={cn("tabular-nums", chosen ? "opacity-80" : "text-muted-foreground")}>
                                    {formatDT(cost)}
                                  </span>
                                )}
                              </Button>
                            )
                          })}
                        </div>
                      )}

{/* Was the DCH code. A line now names the procedure it is performed as, and this is the only
                          place that says so — without it, « détacher » would have no control and a line chosen
                          from the catalog would be indistinguishable from a typed one. */}
                      {line.procedureTypeId && (
                        <Badge variant="secondary" className="gap-1 text-xs">
                          {procedureTypes.find((pt) => pt.id === line.procedureTypeId)?.name ?? "Acte du catalogue"}
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

                      {/* Grouping is the default because two caries are one decision; splitting is one tap
                          because two caries in opposite quadrants are two appointments — a grouped line is
                          booked and marked réalisé as a unit. `basis-full` so it takes its own row rather than
                          sitting beside the catalogue badge, where the two read as one control. */}
                      {line.toothNumbers.length > 1 && (
                        // A wrapper, not `basis-full`: the parent is a `space-y-*` block, so the flex basis did
                        // nothing and the link sat beside the catalogue badge where the two read as one control.
                        <div>
                          <button
                            type="button"
                            onClick={() => splitLine(index)}
                            disabled={loading}
                            className="touch-target text-left text-xs text-muted-foreground underline underline-offset-2 hover:text-foreground disabled:opacity-50"
                          >
                            Séparer par dent — {line.toothNumbers.length} actes distincts
                          </button>
                        </div>
                      )}
                    </div>
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      onClick={() => requestRemoveLine(index)}
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
                      onChange={(teeth) => updateLineTeeth(index, teeth)}
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
                      {(line.discount ?? 0) > 0 && (
                        <span className="text-xs text-muted-foreground">remise −{formatDT(line.discount)}</span>
                      )}
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

          {/*
            « Séances » — what each act will be carried out over, shown before the devis is accepted so the
            dentist confirms them rather than discovering them afterwards.

            ⚠️ Every named act appears, including the ones the catalogue does not cut up: a protocol-less act
            gets « + Découper en séances » rather than being absent, because absence read as « this act has no
            steps » when the truth was « you cannot set them here ». An act left as one séance costs one grey
            line, so the panel still does not become a step everyone clicks past.

            ⚠️ The séances carry **no money** — the act's fee is the act's, whatever it is split into — and the
            panel says so, because a list of five lines under a 1 000 DT act invites exactly the opposite
            reading.
          */}
          {stepProposals.length > 0 && (
            <div className="space-y-2 rounded-md border border-dashed bg-muted/20 p-3">
              <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
                <Label className="text-sm">Séances</Label>
                <span className="text-2xs text-muted-foreground">
                  {(() => {
                    const stepped = stepProposals.filter(({ line }) => (line.steps?.length ?? 0) > 0).length
                    return stepped === 0
                      ? "Chaque acte se fait en une séance"
                      : stepped === 1
                        ? "1 acte se fait en plusieurs séances"
                        : `${stepped} actes se font en plusieurs séances`
                  })()}
                </span>
              </div>

              {stepProposals.map(({ index, line }) => {
                const steps = line.steps ?? []
                const kept = steps.filter((st) => st.include).length
                const editing = editingStepsFor === index
                return (
                  <div key={index} className="rounded-md border bg-card p-2.5">
                    <div className="flex flex-wrap items-center justify-between gap-x-2 gap-y-1">
                      {/*
                        ⚠️ **`basis-full sm:flex-1`, never a bare `flex-1`** — § 10.1, and it was measured here
                        rather than assumed. `flex-1` is `flex: 1 1 0%`, so the name's hypothetical size is zero
                        and the row can never wrap: at 320 px it kept ~63 px beside the counter and the
                        « Modifier » button, and `[overflow-wrap:anywhere]` then broke « Prothèse amovible
                        (partielle / complète) » to **one word per line** — nine lines of a 160 px row. That is
                        `plan-act-row`'s own recorded defect, in a second place. With a real basis the name takes
                        its own line below `sm:` and the two controls wrap under it, so the label breaks at
                        spaces like ordinary prose.
                      */}
                      <span className="min-w-0 basis-full text-sm font-medium [overflow-wrap:anywhere] sm:basis-auto sm:flex-1">
                        {line.designationFr || "Acte sans nom"}
                      </span>
                      {steps.length > 0 ? (
                        <>
                          {/* ⚠️ « incluses » is visible: this counts the séances TICKED for the devis, and the
                              same bare fraction elsewhere counts the ones already carried out. Two questions,
                              one shape — the reader needs the word to know which was answered. */}
                          <span className="shrink-0 text-2xs text-muted-foreground">
                            <span className="font-mono tabular-nums">
                              {kept} / {steps.length}
                            </span>{" "}
                            {kept > 1 ? "incluses" : "incluse"}
                          </span>
                          <Button
                            type="button"
                            variant="ghost"
                            size="sm"
                            className="h-8 shrink-0 text-xs coarse:h-11"
                            onClick={() => setEditingStepsFor(editing ? null : index)}
                          >
                            {editing ? "Terminer" : "Modifier"}
                          </Button>
                        </>
                      ) : (
                        <>
                          <span className="shrink-0 text-2xs text-muted-foreground">une séance</span>
                          <Button
                            type="button"
                            variant="ghost"
                            size="sm"
                            className="h-8 shrink-0 gap-1 text-xs text-primary coarse:h-11"
                            onClick={() => {
                              addStepRow(index)
                              setEditingStepsFor(index)
                            }}
                          >
                            <Plus className="h-3.5 w-3.5" />
                            Découper en séances
                          </Button>
                        </>
                      )}
                    </div>

                    <ul className="mt-1.5 space-y-1">
                      {steps.map((step, stepIndex) => (
                        <li key={stepIndex} className="flex flex-wrap items-center gap-x-2 gap-y-1">
                          {/* Grown, not overlaid: these sit in a stack and a 44 px pseudo-element would
                              overhang its neighbours and steal their taps (§ 2). */}
                          <button
                            type="button"
                            role="checkbox"
                            aria-checked={step.include}
                            aria-label={`Inclure l'étape ${quoteFr(step.label || String(stepIndex + 1))}`}
                            // A réalisé séance cannot be dropped: the row holds the only link to the fiche
                            // that evidences it, and the aggregate refuses it. Disabled with the reason on the
                            // control rather than as a refusal after the save.
                            disabled={step.done}
                            title={
                              step.done
                                ? "Cette séance est déjà réalisée : elle ne peut pas être retirée du devis."
                                : undefined
                            }
                            onClick={() => !step.done && toggleStepRow(index, stepIndex)}
                            className={cn(
                              "flex size-9 flex-none items-center justify-center rounded-md coarse:size-11",
                              step.include ? "text-primary" : "text-muted-foreground",
                              step.done && "cursor-not-allowed opacity-60",
                            )}
                          >
                            <span
                              aria-hidden="true"
                              className={cn(
                                "flex size-4 items-center justify-center rounded-[5px] border-[1.5px]",
                                step.include ? "border-primary bg-primary" : "border-border",
                              )}
                            >
                              {step.include && (
                                <Check className="size-2.5 text-primary-foreground" strokeWidth={4} />
                              )}
                            </span>
                          </button>

                          {editing ? (
                            <>
                              <Input
                                value={step.label}
                                onChange={(e) => updateStepRow(index, stepIndex, { label: e.target.value })}
                                placeholder="ex. : Empreinte"
                                aria-label={`Libellé de l'étape ${stepIndex + 1}`}
                                className="min-w-0 flex-1 md:text-sm"
                              />
                              <Input
                                value={step.duration}
                                onChange={(e) => updateStepRow(index, stepIndex, { duration: e.target.value })}
                                inputMode="numeric"
                                placeholder="30"
                                aria-label={`Durée de l'étape , en minutes`}
                                title="Temps au fauteuil, en minutes."
                                className="w-20 text-end font-mono tabular-nums md:text-sm"
                              />
                              {/* The interval — « après », not « pendant ». The first séance has none. */}
                              {stepIndex > 0 && (
                                <Input
                                  value={step.interval ?? ""}
                                  onChange={(e) =>
                                    updateStepRow(index, stepIndex, { interval: e.target.value })
                                  }
                                  inputMode="numeric"
                                  placeholder="7 j"
                                  aria-label={`Délai après la séance précédente, en jours, pour l'étape `}
                                  title="Délai minimum après la séance précédente, en jours. Vide = délai libre."
                                  className="w-20 text-end font-mono tabular-nums md:text-sm"
                                />
                              )}
                              <Button
                                type="button"
                                variant="ghost"
                                size="icon"
                                className="size-9 shrink-0 text-muted-foreground coarse:size-11"
                                aria-label={`Supprimer l'étape ${quoteFr(step.label || String(stepIndex + 1))}`}
                                disabled={step.done}
                                title={
                                  step.done
                                    ? "Cette séance est déjà réalisée : détachez sa fiche de soins avant de la retirer."
                                    : undefined
                                }
                                onClick={() => removeStepRow(index, stepIndex)}
                              >
                                <Trash2 className="h-4 w-4" />
                              </Button>
                            </>
                          ) : (
                            <>
                              <span
                                className={cn(
                                  "min-w-0 flex-1 text-xs [overflow-wrap:anywhere]",
                                  step.include ? "text-foreground" : "text-muted-foreground line-through",
                                )}
                              >
                                {step.label || `Étape ${stepIndex + 1}`}
                              </span>
                              <span className="shrink-0 font-mono text-2xs tabular-nums text-muted-foreground">
                                {step.duration ? `${step.duration} min` : "—"}
                              </span>
                            </>
                          )}
                        </li>
                      ))}
                    </ul>

                    {editing && (
                      <div className="mt-1.5 flex flex-wrap gap-2">
                        <Button
                          type="button"
                          variant="outline"
                          size="sm"
                          className="h-8 gap-1 text-xs coarse:h-11"
                          onClick={() => addStepRow(index)}
                        >
                          <Plus className="h-3.5 w-3.5" /> Ajouter une étape
                        </Button>
                        <Button
                          type="button"
                          variant="ghost"
                          size="sm"
                          className="h-8 text-xs text-muted-foreground coarse:h-11"
                          onClick={() => resetStepRows(index)}
                        >
                          Rétablir le protocole
                        </Button>
                      </div>
                    )}

                    {kept === 0 && (
                      <p className="mt-1.5 text-2xs text-muted-foreground" role="status">
                        Aucune étape retenue — cet acte se fera en une seule séance.
                      </p>
                    )}
                  </div>
                )
              })}

              <p className="text-2xs leading-relaxed text-muted-foreground">
                Ces étapes ne portent <span className="font-medium text-foreground">aucun prix</span> : le coût de
                l&apos;acte reste celui de sa ligne, quel que soit le nombre de séances. Vous pourrez les modifier
                à tout moment depuis le devis.
              </p>
            </div>
          )}

          {/* Installments */}
          <div className="space-y-2">
            <Label>Échéancier</Label>
            <div className="space-y-2">
              {installments.length === 0 && (
                /*
                  ⚠️ **« facultatif » was true and misleading.** Leaving this empty does not mean « no
                  schedule »: the server writes ONE échéance for the full total dated at the creation instant,
                  so a 1 500 DT implant running six visits over months is recorded as payable the day the devis
                  is signed. The consequence is stated, with the one-press way out beside it.

                  ⚠️ **It no longer says « qui apparaîtra en retard dès demain », and that sentence must not
                  come back.** It described the behaviour `InstallmentLateness` exists to have removed: an
                  auto-raised row is never compared against its own fabricated date, it renders as
                  « Solde à régler » with no date at all, and it goes red only once the work is finished and the
                  balance unpaid. Warning about a badge the product stopped showing teaches the dentist to
                  distrust a badge that now means something.
                */
                <div className="space-y-2 rounded-md border border-dashed p-2.5">
                  <p className="text-sm text-muted-foreground">
                    Sans échéancier, <b>le total est dû à la signature</b> — une seule ligne
                    « Solde à régler », sans date convenue. Elle ne passe « en retard » qu&apos;une fois tout le
                    traitement terminé et le solde impayé.
                  </p>
                  {total > 0 && (
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      className="gap-1 text-xs coarse:h-11"
                      disabled={loading}
                      onClick={addInstallment}
                    >
                      <Plus className="h-3.5 w-3.5" />
                      Échelonner {formatDT(total)}
                    </Button>
                  )}
                </div>
              )}
              {installments.map((row, index) => {
                // AC-P2.6: an échéance that has collected cash is locked against deletion and against being
                // lowered — the user sees which rows and why *before* submitting, not as an API refusal.
                const collected = row.amountPaid > 0
                return (
                <div key={index} className="space-y-1">
                  {/*
                    ⚠️ **`flex-wrap` + a real `basis-*`, identical to `revise-installments-modal`'s row.** With
                    no wrap, a `flex-1` `type="date"` (~120 px intrinsic minimum) beside a `w-36` amount and a
                    40 px bin cannot reach its floor in the ~90 px left at 320 px, and the dialog scrolls
                    sideways. `min-w-0` on both, since `Input` does not shrink past its intrinsic width alone.
                  */}
                  <div className="flex flex-wrap items-end gap-2">
                    <div className="min-w-0 flex-1 basis-40 space-y-1">
                      {index === 0 && <span className="text-xs text-muted-foreground">Échéance</span>}
                      <Input
                        type="date"
                        value={row.dueDate}
                        onChange={(e) => updateInstallment(index, { dueDate: e.target.value })}
                        disabled={loading}
                        aria-label="Date de l'échéance"
                      />
                    </div>
                    <div className="min-w-0 flex-1 basis-28 space-y-1 sm:max-w-36">
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
            <div className="flex flex-wrap items-center gap-2">
              <Button type="button" variant="outline" size="sm" onClick={addInstallment} disabled={loading} className="gap-2">
                <Plus className="h-4 w-4" /> Ajouter une échéance
              </Button>
              {total > 0 && (
                <div className="flex items-center gap-1.5">
                  <Label htmlFor="spread-months" className="text-xs font-normal text-muted-foreground">
                    Répartir le solde sur
                  </Label>
                  <Input
                    id="spread-months"
                    type="text"
                    inputMode="numeric"
                    value={spreadMonths}
                    onChange={(e) => setSpreadMonths(e.target.value)}
                    className="w-16"
                    disabled={loading}
                  />
                  <span className="text-xs text-muted-foreground">mois</span>
                  <Button type="button" variant="outline" size="sm" onClick={spreadBalance} disabled={loading}>
                    Répartir
                  </Button>
                </div>
              )}
            </div>
            {installments.length > 0 && (
              <div className="flex justify-end text-xs">
                {/* Token, not `amber-600` + a `dark:` twin — same reasoning as `revise-installments-modal`:
                    `--warning-ink` is the amber step that stays legible at this size and follows the palette. */}
                <span className={installmentsMatch ? "text-muted-foreground" : "text-warning-ink"}>
                  Total des échéances : {formatDT(installmentsSum)} / {formatDT(total)}
                  {!installmentsMatch && (isAmending && !installmentsTouched
                    ? " — les échéances non encaissées suivront le nouveau total."
                    : " — la dernière échéance sera ajustée à l'enregistrement.")}
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
                ? "Enregistrement…"
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
    {refundDialog}
    <AlertDialog
      open={pendingRemoval !== null}
      onOpenChange={(o) => !o && setPendingRemoval(null)}
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Retirer cet acte du devis ?</AlertDialogTitle>
          <AlertDialogDescription>
            {pendingRemoval?.label} Rien n&apos;est modifié tant que la révision n&apos;est pas enregistrée.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Garder l&apos;acte</AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            onClick={() => {
              if (pendingRemoval) removeLine(pendingRemoval.index)
              setPendingRemoval(null)
            }}
          >
            Retirer l&apos;acte
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
    </>
  )
}
