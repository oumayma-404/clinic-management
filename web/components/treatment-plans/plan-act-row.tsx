"use client"

import { useRouter } from "next/navigation"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { TableCell, TableRow } from "@/components/ui/table"
import { Checkbox } from "@/components/ui/checkbox"
import type { CardListField } from "@/components/ui/card-list"
import {
  CalendarPlus, CalendarCheck, FilePlus2, FileText, ChevronUp, ChevronDown, Unlink, Layers,
} from "lucide-react"
import type { TreatmentPlanDto, TreatmentPlanItemDto } from "@/lib/api/types"
import { formatDT, formatDateFr, quoteFr } from "@/lib/format"
import { itemWorkflowLabel, itemWorkflowBadgeClass } from "./treatment-plan-labels"
import { planItemState } from "./plan-next-action"

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
  return (
    <div className={orientation === "vertical" ? "flex flex-col" : "flex items-center justify-end"}>
      <Button
        variant="ghost"
        size="icon"
        className="h-6 w-6"
        aria-label={`Monter ${quoteFr(item.designationFr)}`}
        disabled={reorder.disabled || !reorder.canMoveUp}
        onClick={reorder.onMoveUp}
      >
        <ChevronUp className="h-4 w-4" />
      </Button>
      <Button
        variant="ghost"
        size="icon"
        className="h-6 w-6"
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
  return (
    <>
      <Badge variant="secondary" className={itemWorkflowBadgeClass(state)}>
        {itemWorkflowLabel(state)}
      </Badge>
      {state === "done" && item.doneDate && (
        <span className="whitespace-nowrap text-xs text-muted-foreground">{formatDateFr(item.doneDate)}</span>
      )}
      {state !== "done" && item.scheduledAt && (
        <span className="whitespace-nowrap text-xs text-muted-foreground">{formatDateFr(item.scheduledAt)}</span>
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
}: {
  plan: TreatmentPlanDto
  item: TreatmentPlanItemDto
  onSchedule: (item: TreatmentPlanItemDto) => void
  onUndo?: (item: TreatmentPlanItemDto) => void
}) {
  const router = useRouter()
  const state = planItemState(item)
  const planIsActive = plan.status === "Accepted" || plan.status === "InProgress"

  if (state === "to-schedule" && planIsActive) {
    return (
      <Button variant="outline" size="sm" className="h-8 gap-1" onClick={() => onSchedule(item)}>
        <CalendarPlus className="h-4 w-4" />
        Planifier
      </Button>
    )
  }

  if (state === "scheduled" && item.scheduledAppointmentId) {
    return (
      <Button
        variant="ghost"
        size="sm"
        className="h-8 gap-1"
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
        className="h-8 gap-1"
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
      <div className="flex items-center justify-end gap-1">
        <Button
          variant="ghost"
          size="sm"
          className="h-8 gap-1"
          onClick={() => router.push(`/patients/${plan.patientId}?tab=medical-records`)}
        >
          <FileText className="h-4 w-4" />
          Voir la fiche
        </Button>
        {onUndo && (
          <Button
            variant="ghost"
            size="sm"
            className="h-8 gap-1 text-muted-foreground hover:text-foreground"
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
      <TableCell>
        <span className="font-medium">{item.designationFr}</span>
        {item.codeActe && (
          <span className="ml-2 text-xs text-muted-foreground">{item.codeActe}</span>
        )}
      </TableCell>
      <TableCell>
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
      <TableCell className="text-right">{formatDT(item.plannedCost)}</TableCell>
      <TableCell>
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
      <TableCell className="text-right">
        <PlanActPrimaryAction plan={plan} item={item} onSchedule={onSchedule} onUndo={onUndo} />
      </TableCell>
    </TableRow>
  )
}
