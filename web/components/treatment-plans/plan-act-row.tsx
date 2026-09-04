"use client"

import { useRouter } from "next/navigation"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { TableCell, TableRow } from "@/components/ui/table"
import { Checkbox } from "@/components/ui/checkbox"
import { cn } from "@/lib/utils"
import type { CardListField } from "@/components/ui/card-list"
import {
  CalendarPlus, CalendarCheck, FilePlus2, FileText, ChevronUp, ChevronDown, Unlink, Layers,
  ListOrdered,
} from "lucide-react"
import type { TreatmentPlanDto, TreatmentPlanItemDto } from "@/lib/api/types"
import { formatDT, formatDateFr, quoteFr } from "@/lib/format"
import { itemWorkflowLabel, itemWorkflowBadgeClass } from "./treatment-plan-labels"
import { planItemState, nextStepOf } from "./plan-next-action"
import { PlanStepStrip } from "./plan-step-strip"

/** Up/down controls for the act's clinical position; omitted when the plan can't be reordered. */
export interface PlanActReorder {
  disabled: boolean
  canMoveUp: boolean
  canMoveDown: boolean
  onMoveUp: () => void
  onMoveDown: () => void
}

/**
 * Tick state for grouping several acts into one séance. Present only when the plan has more than one bookable act
 * — with a single one there is nothing to group and a lone checkbox is just noise.
 */
export interface PlanActSelection {
  /** False for an act that is already booked or réalisé: the box renders disabled, keeping the column aligned. */
  selectable: boolean
  checked: boolean
  onToggle: () => void
}

interface PlanActRowProps {
  plan: TreatmentPlanDto
  item: TreatmentPlanItemDto
  /** Opens the "Planifier" dialog for this act (only reachable in the `to-schedule` état). */
  onSchedule: (item: TreatmentPlanItemDto) => void
  selection?: PlanActSelection
  /**
   * How many acts share this act's booked appointment. `> 1` means it is part of a grouped séance, which is worth
   * saying: « Planifié » on four rows with the same date otherwise reads as four separate visits.
   */
  sessionActCount?: number
  /**
   * Opens the « Détacher la fiche » confirmation for a `done` act (AC-P2.11). Omitted when the plan is Draft
   * or Cancelled — there is nothing realised to correct — which is also what hides the action.
   */
  onUndo?: (item: TreatmentPlanItemDto) => void
  /**
   * Opens « Modifier les étapes » for this act. Omitted on a Draft or Cancelled plan, which is also what hides
   * the control — the server refuses both.
   */
  onEditSteps?: (item: TreatmentPlanItemDto) => void
  reorder?: PlanActReorder
}

/**
 * The act's tick box. Extracted so the table row and the card list mount the *same* control — a second copy
 * would be a second `aria-label` and a second disabled rule to keep in step.
 */
export function PlanActSelectionBox({
  item,
  selection,
}: {
  item: TreatmentPlanItemDto
  selection: PlanActSelection
}) {
  return (
    <Checkbox
      aria-label={`Sélectionner ${quoteFr(item.designationFr)} pour planification`}
      checked={selection.checked}
      disabled={!selection.selectable}
      onCheckedChange={selection.onToggle}
    />
  )
}

/** The act's up/down controls. `vertical` in a table cell, `horizontal` in a card where height is the cost. */
export function PlanActReorderControls({
  item,
  reorder,
  orientation = "vertical",
}: {
  item: TreatmentPlanItemDto
  reorder: PlanActReorder
  orientation?: "vertical" | "horizontal"
}) {
  // ⚠️ `size-6 coarse:size-8`, never a bare `h-6 w-6`: stacked, these are the same
  // `.touch-target`-on-adjacent-siblings trap that made « Monter » move a *step* down in
  // `plan-item-steps-dialog`. The later sibling paints last, so its 44 px overlay covers its neighbour's
  // painted box and a tap on the visible up-arrow fires « Descendre ». Grow the boxes; never overlay a stack.
  return (
    <div className={orientation === "vertical" ? "flex flex-col" : "flex items-center justify-end gap-1"}>
      <Button
        variant="ghost"
        size="icon"
        className="size-6 coarse:size-8"
        aria-label={`Monter ${quoteFr(item.designationFr)}`}
        disabled={reorder.disabled || !reorder.canMoveUp}
        onClick={reorder.onMoveUp}
      >
        <ChevronUp className="h-4 w-4" />
      </Button>
      <Button
        variant="ghost"
        size="icon"
        className="size-6 coarse:size-8"
        aria-label={`Descendre ${quoteFr(item.designationFr)}`}
        disabled={reorder.disabled || !reorder.canMoveDown}
        onClick={reorder.onMoveDown}
      >
        <ChevronDown className="h-4 w-4" />
      </Button>
    </div>
  )
}

/**
 * The état badge and the date it refers to — the booked séance, or the visit the act was recorded at.
 *
 * The « séance de N actes » badge is **not** here: below `md:` the grouping is a section header over the cards
 * that share the appointment, so repeating it per card would say the same thing twice.
 */
export function PlanActStateBadge({ item }: { item: TreatmentPlanItemDto }) {
  const state = planItemState(item)
  // ⚠️ The date has to follow whatever the badge is answering for. On a stepped act the badge speaks for the
  // NEXT step (see `planItemState`), so printing the act's own `scheduledAt` beside it would pair « Planifié »
  // with the date of a séance that already happened — two facts about different visits, read as one.
  const next = nextStepOf(item)
  const scheduledAt = next ? next.scheduledAt : item.scheduledAt

  return (
    <>
      <Badge variant="secondary" className={itemWorkflowBadgeClass(state)}>
        {itemWorkflowLabel(state)}
      </Badge>
      {state === "done" && item.doneDate && (
        <span className="whitespace-nowrap text-xs text-muted-foreground">{formatDateFr(item.doneDate)}</span>
      )}
      {state !== "done" && scheduledAt && (
        <span className="whitespace-nowrap text-xs text-muted-foreground">{formatDateFr(scheduledAt)}</span>
      )}
    </>
  )
}

/**
 * **Exactly one** primary action — the thing to do next in this act's état. The old plans-table dialog offered
 * every action on every row (eight unlabelled ghost icons), which is how the same act could be booked twice.
 *
 * A `done` act is the one exception: alongside « Voir la fiche » it carries the correction path
 * (« Détacher la fiche »), because reading the fiche is what tells the dentist it is the wrong one.
 *
 * Deliberately **not** a dropdown menu in the card, unlike the other converted surfaces: there is only ever one
 * action, and hiding a single button behind a menu costs a tap and shows nothing in return.
 */
export function PlanActPrimaryAction({
  plan,
  item,
  onSchedule,
  onUndo,
  block = false,
}: {
  plan: TreatmentPlanDto
  item: TreatmentPlanItemDto
  onSchedule: (item: TreatmentPlanItemDto) => void
  onUndo?: (item: TreatmentPlanItemDto) => void
  /**
   * Full width on its own row — the card tree's `primaryAction` slot.
   *
   * ⚠️ In the card header this control is `shrink-0` beside the act's name, and « Planifier l'étape » plus the
   * « Étapes » trigger is ~200 px of a ~288 px card. Measured at 320 px, « Bridge 4 dents (14-17) » was left
   * so little that `[overflow-wrap:anywhere]` broke it to **one character per line** — a 26-line vertical
   * column of letters, and no overflow anywhere for a check to see. `CardList.primaryAction` exists for
   * exactly this, and planning the next étape is what this screen is opened to do.
   */
  block?: boolean
}) {
  const router = useRouter()
  const state = planItemState(item)
  const planIsActive = plan.status === "Accepted" || plan.status === "InProgress"
  // Named on the button when the act has steps: « Planifier » on a bridge two-thirds done answers the wrong
  // question, since what is being booked is one séance of it and the dentist has to know which.
  const next = nextStepOf(item)

  if (state === "to-schedule" && planIsActive) {
    return (
      <Button
        variant="outline"
        size="sm"
        className={cn("h-8 gap-1", block && "w-full justify-center")}
        onClick={() => onSchedule(item)}
        /*
         * ⚠️ The step's NAME stays out of the visible label and goes here instead. The design drafted
         * « Planifier le scellement », which cannot be built: the article depends on the free-text label's
         * gender, so « Planifier le empreinte » is one protocol away. Interpolating the label bare
         * (« Planifier : Contrôle de cicatrisation ») makes a button that cannot shrink and has no bound —
         * the rule the frontend contract states outright. The strip directly above already names the next
         * step in azure semibold, so the visible half stays « l'étape » and the full phrase is announced.
         */
        aria-label={next ? `Planifier l'étape ${quoteFr(next.label)}` : undefined}
      >
        <CalendarPlus className="h-4 w-4" />
        {next ? "Planifier l'étape" : "Planifier"}
      </Button>
    )
  }

  if (state === "scheduled" && item.scheduledAppointmentId) {
    return (
      <Button
        variant="ghost"
        size="sm"
        className={cn("h-8 gap-1", block && "w-full justify-center")}
        onClick={() => router.push(`/appointments?appointmentId=${item.scheduledAppointmentId}`)}
      >
        <CalendarCheck className="h-4 w-4" />
        Voir le RDV
      </Button>
    )
  }

  // The visit has passed with no fiche. The patient page's existing post-visit deep-link opens the record modal
  // already bound to that appointment, so saving closes the loop in one step.
  if (state === "to-record" && item.scheduledAppointmentId) {
    return (
      <Button
        variant="outline"
        size="sm"
        className={cn("h-8 gap-1", block && "w-full justify-center")}
        onClick={() =>
          router.push(`/patients/${plan.patientId}?addRecord=1&appointmentId=${item.scheduledAppointmentId}`)
        }
      >
        <FilePlus2 className="h-4 w-4" />
        Enregistrer la fiche
      </Button>
    )
  }

  if (state === "done") {
    return (
      <div className={cn("flex items-center gap-1", block ? "w-full" : "justify-end")}>
        <Button
          variant="ghost"
          size="sm"
          className={cn("h-8 gap-1", block && "flex-1 justify-center")}
          onClick={() => router.push(`/patients/${plan.patientId}?tab=medical-records`)}
        >
          <FileText className="h-4 w-4" />
          Voir la fiche
        </Button>
        {onUndo && (
          <Button
            variant="ghost"
            size="sm"
            className={cn(
              "h-8 gap-1 text-muted-foreground hover:text-foreground",
              block && "flex-1 justify-center",
            )}
            onClick={() => onUndo(item)}
            title="Ramener cet acte à « Prévu » et détacher sa fiche de soins"
          >
            <Unlink className="h-4 w-4" />
            Détacher
          </Button>
        )}
      </div>
    )
  }

  return null
}

/**
 * « Modifier les étapes » — an icon-only secondary control beside the primary action.
 *
 * <p>⚠️ <b>This is a second control on a row whose whole design is one action</b>, and the deviation is
 * deliberate rather than overlooked. The `done` état already carries two (« Voir la fiche » + « Détacher »), so
 * the rule this row states is not « never two » — it is « never every action on every row », which is what the
 * eight unlabelled ghost icons of the old dialog were. Editing a protocol is genuinely a different job from
 * doing the next thing on it, and there is nowhere else it can live: the act's steps are per-case, so the
 * catalogue cannot own them.</p>
 *
 * <p>Icon-only with a real `aria-label`, so the primary action keeps the row's only word. Hidden on a Draft or
 * Cancelled plan, which the server refuses anyway.</p>
 */
export function PlanActStepsAction({
  item,
  onEditSteps,
}: {
  item: TreatmentPlanItemDto
  onEditSteps: (item: TreatmentPlanItemDto) => void
}) {
  const count = item.steps?.length ?? 0
  return (
    <Button
      variant="ghost"
      size="icon"
      className="size-8 shrink-0 text-muted-foreground coarse:size-10 hover-hover:hover:text-foreground"
      onClick={() => onEditSteps(item)}
      aria-label={
        count > 0
          ? `Modifier les ${count} étapes de ${quoteFr(item.designationFr)}`
          : `Définir les étapes de ${quoteFr(item.designationFr)}`
      }
      title={count > 0 ? "Modifier les étapes" : "Définir des étapes"}
    >
      <ListOrdered className="h-4 w-4" />
    </Button>
  )
}

/**
 * The act's remaining columns as card fields (AC-16: money before date; the dents are the act's subject and
 * come first). An act with no tooth returns `null` rather than the table's « — », which `CardList` drops (AC-17).
 */
export function planActCardFields(item: TreatmentPlanItemDto): CardListField[] {
  return [
    {
      label: "Dents",
      value:
        item.toothNumbers.length > 0 ? (
          <span className="inline-flex flex-wrap justify-end gap-1">
            {item.toothNumbers.map((tooth) => (
              <Badge key={tooth} variant="secondary" className="text-xs">{tooth}</Badge>
            ))}
          </span>
        ) : null,
    },
    { label: "Coût", value: formatDT(item.plannedCost) },
  ]
}

/**
 * One planned act as a table row — the `md:` and up half of the surface. Its card twin is assembled by
 * `plan-workspace` from the exported pieces above, so neither half can drift from the other.
 */
export function PlanActRow({
  plan,
  item,
  onSchedule,
  onUndo,
  onEditSteps,
  reorder,
  selection,
  sessionActCount = 1,
}: PlanActRowProps) {
  return (
    <TableRow data-state={selection?.checked ? "selected" : undefined}>
      {selection && (
        <TableCell>
          <PlanActSelectionBox item={item} selection={selection} />
        </TableCell>
      )}
      {reorder && (
        <TableCell>
          <PlanActReorderControls item={item} reorder={reorder} />
        </TableCell>
      )}
      <TableCell className="align-top">
        <span className="font-medium">{item.designationFr}</span>
        {/* Under the act's own name, not in the État cell: the strip describes THIS ACT's progress, while the
            État cell answers a different question — what to do next about it. */}
        <PlanStepStrip steps={item.steps} nextStepId={item.nextStepId} />
      </TableCell>
      <TableCell className="align-top">
        {item.toothNumbers.length > 0 ? (
          <div className="flex flex-wrap gap-1">
            {item.toothNumbers.map((tooth) => (
              <Badge key={tooth} variant="secondary" className="text-xs">{tooth}</Badge>
            ))}
          </div>
        ) : (
          <span className="text-sm text-muted-foreground">—</span>
        )}
      </TableCell>
      <TableCell className="align-top text-right">{formatDT(item.plannedCost)}</TableCell>
      <TableCell className="align-top">
        <span className="flex flex-wrap items-center gap-2">
          <PlanActStateBadge item={item} />
          {/* Says « this act shares its visit », which is the only way the grouping is visible after booking —
              without it, four acts on the same date look like four appointments. Below `md:` the same fact is
              carried by the card list's section header instead. */}
          {sessionActCount > 1 && item.scheduledAppointmentId && (
            <Badge variant="outline" className="gap-1 whitespace-nowrap text-xs">
              <Layers className="h-3 w-3" />
              séance de {sessionActCount} actes
            </Badge>
          )}
        </span>
      </TableCell>
      <TableCell className="align-top text-right">
        <div className="flex items-center justify-end gap-1">
          <PlanActPrimaryAction plan={plan} item={item} onSchedule={onSchedule} onUndo={onUndo} />
          {onEditSteps && <PlanActStepsAction item={item} onEditSteps={onEditSteps} />}
        </div>
      </TableCell>
    </TableRow>
  )
}
