"use client"

import { useRouter } from "next/navigation"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { TableCell, TableRow } from "@/components/ui/table"
import { CalendarPlus, CalendarCheck, FilePlus2, FileText, ChevronUp, ChevronDown, Unlink } from "lucide-react"
import type { TreatmentPlanDto, TreatmentPlanItemDto } from "@/lib/api/types"
import { formatDT, formatDateFr } from "@/lib/format"
import { itemWorkflowLabel, itemWorkflowBadgeClass } from "./treatment-plan-labels"
import { planItemState } from "./plan-next-action"

/** Up/down controls for the act's clinical position; omitted when the plan can't be reordered. */
interface PlanActReorder {
  disabled: boolean
  canMoveUp: boolean
  canMoveDown: boolean
  onMoveUp: () => void
  onMoveDown: () => void
}

interface PlanActRowProps {
  plan: TreatmentPlanDto
  item: TreatmentPlanItemDto
  /** Opens the "Planifier" dialog for this act (only reachable in the `to-schedule` état). */
  onSchedule: (item: TreatmentPlanItemDto) => void
  /**
   * Opens the « Détacher la fiche » confirmation for a `done` act (AC-P2.11). Omitted when the plan is Draft
   * or Cancelled — there is nothing realised to correct — which is also what hides the action.
   */
  onUndo?: (item: TreatmentPlanItemDto) => void
  reorder?: PlanActReorder
}

/**
 * One planned act, with **exactly one** primary action — the thing to do next in its état. The old plans-table
 * dialog offered every action on every row (eight unlabelled ghost icons), which is how the same act could be
 * booked twice.
 *
 * A `done` act is the one exception: alongside « Voir la fiche » it carries the correction path
 * (« Détacher la fiche »), because reading the fiche is what tells the dentist it is the wrong one.
 */
export function PlanActRow({ plan, item, onSchedule, onUndo, reorder }: PlanActRowProps) {
  const router = useRouter()
  const state = planItemState(item)
  const planIsActive = plan.status === "Accepted" || plan.status === "InProgress"

  return (
    <TableRow>
      {reorder && (
        <TableCell>
          <div className="flex flex-col">
            <Button
              variant="ghost"
              size="icon"
              className="h-6 w-6"
              aria-label={`Monter « ${item.designationFr} »`}
              disabled={reorder.disabled || !reorder.canMoveUp}
              onClick={reorder.onMoveUp}
            >
              <ChevronUp className="h-4 w-4" />
            </Button>
            <Button
              variant="ghost"
              size="icon"
              className="h-6 w-6"
              aria-label={`Descendre « ${item.designationFr} »`}
              disabled={reorder.disabled || !reorder.canMoveDown}
              onClick={reorder.onMoveDown}
            >
              <ChevronDown className="h-4 w-4" />
            </Button>
          </div>
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
        <Badge variant="secondary" className={itemWorkflowBadgeClass(state)}>
          {itemWorkflowLabel(state)}
        </Badge>
        {/* The date the état refers to: the booked séance, or the visit the act was recorded at. */}
        {state === "done" && item.doneDate && (
          <span className="ml-2 whitespace-nowrap text-xs text-muted-foreground">
            {formatDateFr(item.doneDate)}
          </span>
        )}
        {state !== "done" && item.scheduledAt && (
          <span className="ml-2 whitespace-nowrap text-xs text-muted-foreground">
            {formatDateFr(item.scheduledAt)}
          </span>
        )}
      </TableCell>
      <TableCell className="text-right">
        {state === "to-schedule" && planIsActive && (
          <Button variant="outline" size="sm" className="h-8 gap-1" onClick={() => onSchedule(item)}>
            <CalendarPlus className="h-4 w-4" />
            Planifier
          </Button>
        )}

        {state === "scheduled" && item.scheduledAppointmentId && (
          <Button
            variant="ghost"
            size="sm"
            className="h-8 gap-1"
            onClick={() => router.push(`/appointments?appointmentId=${item.scheduledAppointmentId}`)}
          >
            <CalendarCheck className="h-4 w-4" />
            Voir le RDV
          </Button>
        )}

        {/* The visit has passed with no fiche. The patient page's existing post-visit deep-link opens the
            record modal already bound to that appointment, so saving closes the loop in one step. */}
        {state === "to-record" && item.scheduledAppointmentId && (
          <Button
            variant="outline"
            size="sm"
            className="h-8 gap-1"
            onClick={() =>
              router.push(
                `/patients/${plan.patientId}?addRecord=1&appointmentId=${item.scheduledAppointmentId}`,
              )
            }
          >
            <FilePlus2 className="h-4 w-4" />
            Enregistrer la fiche
          </Button>
        )}

        {state === "done" && (
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
        )}
      </TableCell>
    </TableRow>
  )
}
