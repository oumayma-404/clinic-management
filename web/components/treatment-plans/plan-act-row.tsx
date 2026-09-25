"use client"

import { useState } from "react"
import type React from "react"
import { useRouter } from "next/navigation"
import { format, parseISO } from "date-fns"
import { fr } from "date-fns/locale"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { TableCell, TableRow } from "@/components/ui/table"
import { Checkbox } from "@/components/ui/checkbox"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { cn } from "@/lib/utils"
import type { CardListField } from "@/components/ui/card-list"
import {
  CalendarPlus, CalendarCheck, FilePlus2, FileText, ChevronUp, ChevronDown, Undo2, Layers,
  ListOrdered, FilePen, Archive, ArchiveRestore, BadgePercent, Plus, Pencil, MoveRight,
} from "lucide-react"
import type { TreatmentPlanDto, TreatmentPlanItemDto, TreatmentPlanItemStepDto } from "@/lib/api/types"
import { formatAmount, formatDT, formatDateFr, parseAmountInput, quoteFr } from "@/lib/format"
import { planItemStateLabel, itemWorkflowBadgeClass } from "./treatment-plan-labels"
import {
  isPlanLive, planItemState, nextStepOf, isItemWithdrawn, detachOutcome, itemNetCost, itemDiscount,
} from "./plan-next-action"
import { SeanceStrip, seanceDay, seanceEarliestAhead, seanceState, type SeanceStripStep } from "./seance-strip"

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
  /** False for an act that is already booked or done: the box renders disabled, keeping the column aligned. */
  selectable: boolean
  checked: boolean
  onToggle: () => void
}

/**
 * What a séance of the strip can do when it is tapped (T2). Each handler is omitted where the server would
 * refuse the action, which is also what withholds its button in the popover.
 */
export interface PlanActSeanceHandlers {
  /** « Planifier » on a séance not booked yet — the row's own booking action, that séance preselected. */
  onPlanStep?: (item: TreatmentPlanItemDto, stepId: string) => void
  /** « Déplacer » — opens the booked appointment in place. */
  onMoveAppointment?: (appointmentId: string) => void
  /** « Modifier la séance » (a step id) and the strip's « + » (null) — the séances dialog. */
  onEditStep?: (item: TreatmentPlanItemDto, stepId: string | null) => void
  /** « Remettre à faire » — offered on the LAST done séance only, which is what `Unmark` releases. */
  onUndo?: (item: TreatmentPlanItemDto) => void
  /** Leaves the page (Voir la fiche, Voir le RDV…) — the workspace routes it through its discard guard. */
  navigate?: (href: string) => void
}

interface PlanActRowProps {
  plan: TreatmentPlanDto
  item: TreatmentPlanItemDto
  /** Opens the "Planifier" dialog for this act (only reachable in the `to-schedule` état). */
  onSchedule: (item: TreatmentPlanItemDto) => void
  selection?: PlanActSelection
  /**
   * How many acts share this act's booked appointment. `> 1` means it is part of a grouped séance, which is worth
   * saying: « Prévu » on four rows with the same date otherwise reads as four separate visits.
   */
  sessionActCount?: number
  /**
   * Opens the « Remettre à faire » confirmation for a `done` act. Omitted when the plan is closed to writes —
   * there is nothing to correct — which is also what hides the action.
   */
  onUndo?: (item: TreatmentPlanItemDto) => void
  /** Opens the séances dialog. Omitted on a plan the server refuses to correct. */
  onEditSteps?: (item: TreatmentPlanItemDto) => void
  /** Opens « Tout modifier » with this act's line in focus. Omitted on a plan the server will not amend. */
  onEdit?: (item: TreatmentPlanItemDto) => void
  /**
   * Opens « Mettre cet acte de côté » (M1). Omitted for an act that cannot be parked (delivered work, or the last
   * active act of the devis), which is also what hides it.
   */
  onWithdraw?: (item: TreatmentPlanItemDto) => void
  /** Opens « Remettre au devis » (M2) — the mirror, offered only on an act that is already parked. */
  onRestore?: (item: TreatmentPlanItemDto) => void
  /** The séance strip's popover actions. `null` withholds the strip — the header draws it for a one-act plan. */
  seances?: PlanActSeanceHandlers | null
  /** The « Prix » cell — the in-place price/remise editor (T3). Defaults to the read-only figure. */
  cost?: React.ReactNode
  /** @see PlanActSeanceHandlers.navigate */
  navigate?: (href: string) => void
  reorder?: PlanActReorder
  /** The « État » cell — false when the table withholds the column (see `planActStateShown`). */
  showState?: boolean
}

/**
 * Does {@link PlanActStateBadge} draw anything for this act? The table drops the « État » column when no row
 * would — the case of a treatment nobody runs, where only « Fait » and « Mis de côté » still say something.
 */
export function planActStateShown(item: TreatmentPlanItemDto, planLive: boolean): boolean {
  return planLive || isItemWithdrawn(item) || planItemState(item) === "done"
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
      // Square: the primitive's `rounded-sm` is half its 16 px, i.e. a circle that reads as a séance dot.
      className={SQUARE_CHECKBOX}
      aria-label={`Sélectionner ${quoteFr(item.designationFr)} pour planification`}
      checked={selection.checked}
      disabled={!selection.selectable}
      onCheckedChange={selection.onToggle}
    />
  )
}

/** A tick box drawn as a box — shared with the table's « Tout sélectionner ». */
export const SQUARE_CHECKBOX = "rounded-[4px]"

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
  // ⚠️ Grown to 44 px on a finger (`coarse:size-11`), never an overlay on stacked siblings: the later one paints
  // last and its overlay steals the taps aimed at its neighbour (« Monter » fired « Descendre »).
  return (
    <div className={orientation === "vertical" ? "flex flex-col" : "flex items-center justify-end gap-1"}>
      <Button
        variant="ghost"
        size="icon"
        className="size-6 coarse:size-11"
        aria-label={`Monter ${quoteFr(item.designationFr)}`}
        disabled={reorder.disabled || !reorder.canMoveUp}
        onClick={reorder.onMoveUp}
      >
        <ChevronUp className="h-4 w-4" />
      </Button>
      <Button
        variant="ghost"
        size="icon"
        className="size-6 coarse:size-11"
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
 * The « séance de N actes » badge is **not** here: below `lg:` the grouping is a section header over the cards
 * that share the appointment, so repeating it per card would say the same thing twice.
 *
 * <p>`planLive` false (arrêté, non réclamé, annulé, terminé): only « Fait » is drawn — « À planifier » on a
 * treatment nobody is running is false, and the header badge already states why it stopped.</p>
 */
export function PlanActStateBadge({ item, planLive = true }: { item: TreatmentPlanItemDto; planLive?: boolean }) {
  // A parked act is not outstanding work: it replaces the workflow badge rather than sitting beside one.
  if (isItemWithdrawn(item)) {
    return (
      <Badge variant="outline" className="gap-1 whitespace-nowrap font-normal text-muted-foreground">
        <Archive className="h-3 w-3" />
        Mis de côté
      </Badge>
    )
  }

  const state = planItemState(item)
  if (!planLive && state !== "done") return null
  // ⚠️ The date follows whatever the badge answers for: on a stepped act that is the NEXT step (see
  // `planItemState`), so the act's own `scheduledAt` would pair « Prévu » with a séance that already happened.
  const next = nextStepOf(item)
  const scheduledAt = next ? next.scheduledAt : item.scheduledAt

  return (
    <>
      {/* « Suite à planifier » / « Suite prévue » once a séance is done — « À planifier » read as not started. */}
      <Badge variant="secondary" className={itemWorkflowBadgeClass(state)}>
        {planItemStateLabel(item, state)}
      </Badge>
      {state === "done" && item.doneDate && (
        <span className="whitespace-nowrap text-xs text-muted-foreground">{formatDateFr(item.doneDate)}</span>
      )}
      {state !== "done" && scheduledAt && (
        <span className="whitespace-nowrap text-xs text-muted-foreground">{formatDateFr(scheduledAt)}</span>
      )}
      {/*
        S5 — the protocol's own earliest day, SERVED (`TreatmentPlanItemStep.DueFrom`), never re-derived here.
        « dès le », never « en retard »: `MinDaysAfterPrevious` says *pas avant*. Withheld once booked, and once
        that day has passed.
      */}
      {state === "to-schedule" && next?.earliestOn && seanceEarliestAhead(next.earliestOn) && (
        <span className="whitespace-nowrap text-xs text-muted-foreground">
          dès le {formatDateFr(next.earliestOn)}
        </span>
      )}
    </>
  )
}

/** « 28/09 à 10:00 » — the booked slot, in the words a popover states it. */
function slotLabel(iso: string): string {
  try {
    return `${seanceDay(iso)} à ${format(parseISO(iso), "HH:mm", { locale: fr })}`
  } catch {
    return seanceDay(iso)
  }
}

/**
 * One séance's popover (T2): what is true of it in one bold line, then its actions — every action a séance
 * has, where the séance is drawn.
 *
 * <p>⚠️ **« Remettre à faire » only on the LAST done séance by rank.** That is what `TreatmentPlanItem.Unmark`
 * releases (`detachOutcome`), so offering it on an earlier séance would undo a different séance than the one
 * tapped. A middle séance is corrected in the séances dialog, per step.</p>
 */
function SeancePopover({
  plan,
  item,
  step,
  isLastDone,
  handlers,
  children,
}: {
  plan: TreatmentPlanDto
  item: TreatmentPlanItemDto
  step: TreatmentPlanItemStepDto
  isLastDone: boolean
  handlers: PlanActSeanceHandlers
  children: React.ReactElement
}) {
  const router = useRouter()
  const go = handlers.navigate ?? ((href: string) => router.push(href))
  const [open, setOpen] = useState(false)
  const state = seanceState(step)
  const inactive = !isPlanLive(plan.status)
  const appointmentId = step.scheduledAppointmentId ?? null
  /** Every action closes the popover first, so a dialog it opens is not stacked under it. */
  const act = (fn: () => void) => () => {
    setOpen(false)
    fn()
  }
  const button = "h-8 gap-1.5 coarse:h-11"

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>{children}</PopoverTrigger>
      <PopoverContent align="start" className="w-[min(20rem,calc(100vw-2rem))] space-y-3 p-3">
        <p className="text-sm [overflow-wrap:anywhere]">
          <b>{step.label}</b>
          {" · "}
          {/* One short date form, the strip's own (« 16/09 », « 27/09 à 10:00 »). */}
          {state === "done" ? (
            <>faite le <b>{seanceDay(step.doneDate)}</b></>
          ) : state === "booked" ? (
            <>prévue le <b>{slotLabel(step.scheduledAt!)}</b></>
          ) : state === "to-record" ? (
            <>séance du <b>{slotLabel(step.scheduledAt!)}</b> · fiche à enregistrer</>
          ) : inactive ? (
            "non faite"
          ) : seanceEarliestAhead(step.earliestOn) ? (
            <>à planifier · dès le <b>{seanceDay(step.earliestOn)}</b></>
          ) : (
            "à planifier"
          )}
        </p>
        <div className="flex flex-wrap gap-2">
          {state === "done" && (
            <>
              <Button
                size="sm"
                variant="outline"
                className={button}
                onClick={act(() =>
                  go(
                    step.linkedDentalRecordId
                      ? `/patients/${plan.patientId}?editRecord=${encodeURIComponent(step.linkedDentalRecordId)}`
                      : `/patients/${plan.patientId}?tab=medical-records`,
                  ),
                )}
              >
                <FileText className="h-4 w-4" />
                Voir la fiche
              </Button>
              {isLastDone && handlers.onUndo && (
                <Button size="sm" variant="ghost" className={button} onClick={act(() => handlers.onUndo!(item))}>
                  <Undo2 className="h-4 w-4" />
                  Remettre à faire
                </Button>
              )}
            </>
          )}
          {state === "to-record" && appointmentId && (
            <Button
              size="sm"
              className={button}
              onClick={act(() => go(`/patients/${plan.patientId}?addRecord=1&appointmentId=${appointmentId}`))}
            >
              <FilePlus2 className="h-4 w-4" />
              Enregistrer la fiche
            </Button>
          )}
          {(state === "booked" || state === "to-record") && appointmentId && (
            <Button
              size="sm"
              variant="outline"
              className={button}
              onClick={act(() => go(`/appointments?appointmentId=${appointmentId}`))}
            >
              <CalendarCheck className="h-4 w-4" />
              Voir le RDV
            </Button>
          )}
          {state === "booked" && appointmentId && handlers.onMoveAppointment && (
            <Button
              size="sm"
              variant="outline"
              className={button}
              onClick={act(() => handlers.onMoveAppointment!(appointmentId))}
            >
              <MoveRight className="h-4 w-4" />
              Déplacer
            </Button>
          )}
          {state === "todo" && handlers.onPlanStep && (
            <Button size="sm" className={button} onClick={act(() => handlers.onPlanStep!(item, step.id))}>
              <CalendarPlus className="h-4 w-4" />
              Planifier
            </Button>
          )}
          {/* On a done séance too: the séances dialog is where an earlier séance recorded by mistake is undone. */}
          {handlers.onEditStep && (
            <Button
              size="sm"
              variant="ghost"
              className={button}
              onClick={act(() => handlers.onEditStep!(item, step.id))}
            >
              <Pencil className="h-4 w-4" />
              {state === "done" ? "Modifier les séances" : "Modifier la séance"}
            </Button>
          )}
        </div>
      </PopoverContent>
    </Popover>
  )
}

/**
 * The act's séances as ONE interactive strip (P3 + T2) — each séance a popover of its own actions, and a « + »
 * after the last one. Renders nothing for an act done in one sitting, which must look exactly as it did.
 */
export function PlanActSeances({
  plan,
  item,
  handlers,
  size = "md",
}: {
  plan: TreatmentPlanDto
  item: TreatmentPlanItemDto
  handlers: PlanActSeanceHandlers
  size?: "md" | "lg"
}) {
  const steps = item.steps ?? []
  if (steps.length === 0) return null
  const lastDoneId = detachOutcome(item).stepId
  const byId = new Map(steps.map((s) => [s.id, s]))

  return (
    <SeanceStrip
      steps={steps}
      size={size}
      inactive={!isPlanLive(plan.status)}
      // `max-w-md` caps what a long protocol asks of the table's column; past it the strip scrolls in its box.
      className={size === "md" ? "mt-2 max-w-md" : undefined}
      label={`Séances de ${item.designationFr}`}
      wrapStep={(s: SeanceStripStep, button) => {
        const step = byId.get(s.id)
        if (!step) return button
        return (
          <SeancePopover
            plan={plan}
            item={item}
            step={step}
            isLastDone={step.id === lastDoneId}
            handlers={handlers}
          >
            {button}
          </SeancePopover>
        )
      }}
      // Adding a séance is planning: withheld on a treatment nobody is running (its popovers still edit séances).
      trailing={
        handlers.onEditStep && isPlanLive(plan.status) ? (
          <Button
            variant="outline"
            size="sm"
            className="h-8 min-w-8 shrink-0 gap-1 px-2 coarse:h-11 coarse:min-w-11"
            aria-label={`Ajouter une séance à ${quoteFr(item.designationFr)}`}
            onClick={() => handlers.onEditStep!(item, null)}
          >
            <Plus className="h-4 w-4" />
            <span className="hidden sm:inline">Séance</span>
          </Button>
        ) : undefined
      }
    />
  )
}

/**
 * **Exactly one** primary action for an act done in one sitting — the thing to do next in its état. A stepped act
 * has none here: each of its séances carries its own actions on the strip (T2).
 *
 * A `done` act is the one exception: alongside « Voir la fiche » it carries « Remettre à faire », because reading
 * the fiche is what tells the dentist it is the wrong one.
 */
export function PlanActPrimaryAction({
  plan,
  item,
  onSchedule,
  onUndo,
  onRestore,
  navigate,
  block = false,
}: {
  plan: TreatmentPlanDto
  item: TreatmentPlanItemDto
  onSchedule: (item: TreatmentPlanItemDto) => void
  onUndo?: (item: TreatmentPlanItemDto) => void
  /** @see PlanActSeanceHandlers.navigate */
  navigate?: (href: string) => void
  /** @see PlanActRowProps.onRestore */
  onRestore?: (item: TreatmentPlanItemDto) => void
  /**
   * Full width on its own row — the card tree's `primaryAction` slot. In the card header a `shrink-0` button
   * beside the act's name left it one character per line at 320 px.
   */
  block?: boolean
}) {
  const router = useRouter()
  const go = navigate ?? ((href: string) => router.push(href))
  const state = planItemState(item)
  // ⚠️ A Draft counts as live — `isPlanLive`, never a hand-written status test (N23).
  const planIsActive = isPlanLive(plan.status)
  const stepped = (item.steps?.length ?? 0) > 0
  // ⚠️ The booking may sit on the STEP rather than on the act (`actRemovalPlan` proves it).
  const next = nextStepOf(item)
  const bookedAppointmentId = next?.scheduledAppointmentId ?? item.scheduledAppointmentId ?? null

  // A parked act has no next step and nothing to book — what it has is a way back.
  if (isItemWithdrawn(item)) {
    if (!onRestore) return null
    return (
      <Button
        variant="outline"
        size="sm"
        className={cn("h-8 gap-1 coarse:h-11", block && "w-full justify-center")}
        onClick={() => onRestore(item)}
        aria-label={`Remettre ${quoteFr(item.designationFr)} au devis`}
      >
        <ArchiveRestore className="h-4 w-4" />
        Remettre au devis
      </Button>
    )
  }

  // The strip's séances carry every action of a stepped act.
  if (stepped) return null

  if (state === "to-schedule" && planIsActive) {
    return (
      <Button
        variant="outline"
        size="sm"
        className={cn("h-8 gap-1 coarse:h-11", block && "w-full justify-center")}
        onClick={() => onSchedule(item)}
        aria-label={`Planifier ${quoteFr(item.designationFr)}`}
      >
        <CalendarPlus className="h-4 w-4" />
        Planifier
      </Button>
    )
  }

  if (state === "scheduled" && bookedAppointmentId) {
    return (
      <Button
        variant="ghost"
        size="sm"
        className={cn("h-8 gap-1 coarse:h-11", block && "w-full justify-center")}
        onClick={() => go(`/appointments?appointmentId=${bookedAppointmentId}`)}
      >
        <CalendarCheck className="h-4 w-4" />
        Voir le RDV
      </Button>
    )
  }

  // The visit has passed with no fiche: the patient page's deep link opens the record bound to that visit.
  if (state === "to-record" && bookedAppointmentId) {
    return (
      <Button
        variant="outline"
        size="sm"
        className={cn("h-8 gap-1 coarse:h-11", block && "w-full justify-center")}
        onClick={() => go(`/patients/${plan.patientId}?addRecord=1&appointmentId=${bookedAppointmentId}`)}
      >
        <FilePlus2 className="h-4 w-4" />
        Enregistrer la fiche
      </Button>
    )
  }

  if (state === "done") {
    /*
     * ⚠️ `flex-wrap` + a real `basis` + an explicit `shrink`: `Button` is `whitespace-nowrap shrink-0` and
     * `flex-1` does not clear that, so two buttons measured 222 px in a 172 px card at 320 px.
     */
    return (
      <div className={cn("flex items-center gap-1", block ? "w-full flex-wrap" : "justify-end")}>
        <Button
          variant="ghost"
          size="sm"
          className={cn("h-8 gap-1 coarse:h-11", block && "flex-1 basis-28 shrink justify-center")}
          // ⚠️ THAT fiche, through the `?editRecord=` door — `?tab=medical-records` opens none of them.
          onClick={() =>
            go(
              item.linkedDentalRecordId
                ? `/patients/${plan.patientId}?editRecord=${encodeURIComponent(item.linkedDentalRecordId)}`
                : `/patients/${plan.patientId}?tab=medical-records`,
            )
          }
        >
          <FileText className="h-4 w-4" />
          Voir la fiche
        </Button>
        {onUndo && (
          <Button
            variant="ghost"
            size="sm"
            className={cn(
              "h-8 gap-1 text-muted-foreground coarse:h-11 hover:text-foreground",
              block && "flex-1 basis-28 shrink justify-center",
            )}
            onClick={() => onUndo(item)}
            aria-label={`Remettre ${quoteFr(item.designationFr)} à faire`}
          >
            <Undo2 className="h-4 w-4" />
            Remettre à faire
          </Button>
        )}
      </div>
    )
  }

  // A closed treatment: nothing here — the header badge already says why (« Annulé », « Arrêté »…).
  if (!planIsActive) return null
  return (
    <span className={cn("text-xs text-muted-foreground", block && "block w-full text-center")}>
      Rien à faire pour l&apos;instant
    </span>
  )
}

/** Does {@link PlanActPrimaryAction} draw anything for this act? The row asks it so no empty action row renders. */
export function hasPlanActPrimaryAction(
  plan: TreatmentPlanDto,
  item: TreatmentPlanItemDto,
  onRestore?: (item: TreatmentPlanItemDto) => void,
): boolean {
  if (isItemWithdrawn(item)) return Boolean(onRestore)
  if ((item.steps?.length ?? 0) > 0) return false
  if (isPlanLive(plan.status)) return true
  const state = planItemState(item)
  const booked = nextStepOf(item)?.scheduledAppointmentId ?? item.scheduledAppointmentId ?? null
  return state === "done" || ((state === "scheduled" || state === "to-record") && booked !== null)
}

/**
 * « Découper en séances » — an act done in one sitting, cut into séances. A stepped act has the strip's « + » and
 * each séance's « Modifier la séance » instead, so this is offered for a step-less act only.
 */
export function PlanActStepsAction({
  item,
  onEditSteps,
}: {
  item: TreatmentPlanItemDto
  onEditSteps: (item: TreatmentPlanItemDto) => void
}) {
  return (
    <Button
      variant="ghost"
      size="sm"
      className="h-8 shrink-0 gap-1.5 px-2 text-muted-foreground coarse:h-11 hover-hover:hover:text-foreground"
      onClick={() => onEditSteps(item)}
      aria-label={`Découper ${quoteFr(item.designationFr)} en séances`}
    >
      <ListOrdered className="h-4 w-4" />
      Découper en séances
    </Button>
  )
}

/**
 * « Mettre de côté » — the per-act park (M1): the fee leaves the total, the fiche links stay, « Remettre au devis »
 * brings it back. A verb rather than a mute icon: a `title` needs a hover this app's tablet has not got.
 */
export function PlanActWithdrawAction({
  item,
  onWithdraw,
}: {
  item: TreatmentPlanItemDto
  onWithdraw: (item: TreatmentPlanItemDto) => void
}) {
  return (
    <Button
      variant="ghost"
      size="sm"
      className="h-8 shrink-0 gap-1.5 px-2 text-muted-foreground coarse:h-11 hover-hover:hover:text-foreground"
      onClick={() => onWithdraw(item)}
      aria-label={`Mettre ${quoteFr(item.designationFr)} de côté`}
    >
      <Archive className="h-4 w-4" />
      Mettre de côté
    </Button>
  )
}

/**
 * « Modifier » — the act's désignation and teeth, in the full form with this line in focus. It exists because the
 * form's only door was a menu item a dentist did not find (« I struggled to find how to edit »).
 */
export function PlanActEditAction({
  item,
  onEdit,
}: {
  item: TreatmentPlanItemDto
  onEdit: (item: TreatmentPlanItemDto) => void
}) {
  return (
    <Button
      variant="ghost"
      size="sm"
      className="h-8 shrink-0 gap-1.5 px-2 text-muted-foreground coarse:h-11 hover-hover:hover:text-foreground"
      onClick={() => onEdit(item)}
      aria-label={`Modifier ${quoteFr(item.designationFr)} — désignation, dents`}
    >
      <FilePen className="h-4 w-4" />
      Modifier
    </Button>
  )
}

/**
 * What an act's « Prix » column says when it is not being edited.
 *
 * <p>⚠️ **An act another document bills shows the NOTE, never a bare « 0,000 DT ».** The 0 is correct and imposed
 * server-side, but printed alone it reads as a free act — the reported « money gap » was this silence. Same rule as
 * `act-card.tsx`'s « Aucun honoraire sur cette séance », spelled the same way (N34 holds the one owner).</p>
 *
 * <p>⚠️ The amount is printed only when it was recoverable: a séance that billed several acts cannot say which
 * share of the note was this one.</p>
 */
function planActCost(item: TreatmentPlanItemDto) {
  if (!item.billedOnInvoiceId) {
    const discount = itemDiscount(item)
    // S2 — the NET is the figure; the struck tarif and the remise render only when one was granted.
    if (discount <= 0.0005) {
      return <span className="whitespace-nowrap tabular-nums">{formatDT(itemNetCost(item))}</span>
    }
    return (
      <span className="block">
        <span className="whitespace-nowrap font-semibold tabular-nums">{formatDT(itemNetCost(item))}</span>
        <span className="block whitespace-nowrap text-2xs text-muted-foreground">
          <span className="line-through">{formatDT(item.plannedCost)}</span>
          {" · "}
          remise {formatDT(discount)}
        </span>
      </span>
    )
  }

  const amount = item.billedOnInvoiceAmount ?? 0
  const note = item.billedOnInvoiceNumber
    ? `la note n° ${item.billedOnInvoiceNumber}`
    : "un brouillon de note d'honoraires"

  return (
    <span className="block text-xs text-muted-foreground">
      <span className="font-medium text-foreground">
        {amount > 0 ? `${formatDT(amount)} ` : ""}sur {note}
      </span>
      <span className="block">Aucun honoraire sur ce devis.</span>
    </span>
  )
}

/** One act's pending in-place edit (T3) — typed strings, `undefined` = untouched. */
export interface PlanActCostDraft {
  cost?: string
  discount?: string
}

/**
 * The « Prix » cell, editable in place (T3): « ~~300,000~~ remise [50,000] **250,000 DT** » on an act with a
 * remise, a price field otherwise. The tarif of an act carrying a remise is changed in « Tout modifier ».
 *
 * <p>⚠️ Read-only wherever the full form or the remise command is refused: an act another document bills (its 0
 * is the rule — `planActCost` names the note), a parked act, and a devis closed to writes. The caller decides
 * `canPrice` / `canDiscount` from the same predicates the old controls used.</p>
 */
export function PlanActCostEditor({
  item,
  canPrice,
  canDiscount,
  draft,
  onChange,
  disabled,
}: {
  item: TreatmentPlanItemDto
  canPrice: boolean
  canDiscount: boolean
  draft: PlanActCostDraft | undefined
  onChange: (draft: PlanActCostDraft) => void
  disabled?: boolean
}) {
  if (item.billedOnInvoiceId || isItemWithdrawn(item) || (!canPrice && !canDiscount)) {
    return planActCost(item)
  }

  const tarifText = draft?.cost ?? formatAmount(item.plannedCost)
  const tarif = parseAmountInput(tarifText)
  const showDiscount = canDiscount && (draft?.discount !== undefined || itemDiscount(item) > 0.0005)
  const discountText = draft?.discount ?? formatAmount(itemDiscount(item))
  const discount = discountText.trim() === "" ? 0 : parseAmountInput(discountText)
  const net = Number.isFinite(tarif) && Number.isFinite(discount) ? Math.max(0, tarif - discount) : NaN
  const field = "h-8 w-24 text-end tabular-nums md:text-sm"

  if (showDiscount) {
    return (
      <span className="inline-flex flex-wrap items-center justify-end gap-x-2 gap-y-1">
        {/* The tarif stays visible beside its remise — the gross read is the point here (N40). */}
        <span className="whitespace-nowrap text-xs text-muted-foreground line-through tabular-nums">
          {Number.isFinite(tarif) ? formatDT(tarif) : tarifText}
        </span>
        <span className="text-xs text-muted-foreground">remise</span>
        {/* Field + « DT » kept on one line, like the price field. */}
        <span className="inline-flex items-center gap-2">
          <Input
            type="text"
            inputMode="decimal"
            value={discountText}
            onChange={(e) => onChange({ ...draft, discount: e.target.value })}
            disabled={disabled}
            className={field}
            aria-label={`Remise sur ${item.designationFr} (DT)`}
          />
          <span className="text-xs text-muted-foreground">DT</span>
        </span>
        <b className="whitespace-nowrap tabular-nums">{Number.isFinite(net) ? formatDT(net) : "—"}</b>
      </span>
    )
  }

  return (
    <span className="inline-flex flex-wrap items-center justify-end gap-x-2 gap-y-1">
      {canPrice ? (
        <>
          <Input
            type="text"
            inputMode="decimal"
            value={tarifText}
            onChange={(e) => onChange({ ...draft, cost: e.target.value })}
            disabled={disabled}
            className={field}
            aria-label={`Prix de ${item.designationFr} (DT)`}
          />
          <span className="text-xs text-muted-foreground">DT</span>
        </>
      ) : (
        <span className="whitespace-nowrap tabular-nums">{formatDT(itemNetCost(item))}</span>
      )}
      {canDiscount && (
        <Button
          variant="ghost"
          size="sm"
          className="h-8 gap-1 px-2 text-muted-foreground coarse:h-11"
          disabled={disabled}
          onClick={() => onChange({ ...draft, discount: "" })}
          aria-label={`Accorder une remise sur ${quoteFr(item.designationFr)}`}
        >
          <BadgePercent className="h-4 w-4" />
          Remise
        </Button>
      )}
    </span>
  )
}

/**
 * The act's remaining columns as card fields (money before date; the dents are the act's subject and come
 * first). An act with no tooth returns `null` rather than the table's « — », which `CardList` drops (AC-17).
 */
export function planActCardFields(item: TreatmentPlanItemDto, cost?: React.ReactNode): CardListField[] {
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
    { label: "Prix", value: cost ?? planActCost(item) },
  ]
}

/**
 * One planned act as a table row — the `lg:` and up half of the surface. Its card twin is assembled by
 * `plan-workspace` from the exported pieces above, so neither half can drift from the other.
 */
export function PlanActRow({
  plan,
  item,
  onSchedule,
  onUndo,
  onEditSteps,
  onEdit,
  onWithdraw,
  onRestore,
  seances,
  cost,
  navigate,
  reorder,
  selection,
  sessionActCount = 1,
  showState = true,
}: PlanActRowProps) {
  const withdrawn = isItemWithdrawn(item)
  const stepped = (item.steps?.length ?? 0) > 0
  // Désignation · Prix · (État), plus the two optional leading columns — what the action sub-row has to span.
  const columnCount = 2 + (showState ? 1 : 0) + (selection ? 1 : 0) + (reorder ? 1 : 0)
  const primaryShown = hasPlanActPrimaryAction(plan, item, onRestore)
  const hasActions =
    primaryShown || (!withdrawn && ((!stepped && Boolean(onEditSteps)) || Boolean(onEdit) || Boolean(onWithdraw)))
  const selected = selection?.checked ? "selected" : undefined
  /*
   * ⚠️ Two `<tr>` for one act, and the pair has to READ as one act: the top row drops its border and both rows
   * drop the hover. `data-state` goes on both, or ticking the checkbox tints the act and not its controls.
   */
  const pair = cn(withdrawn && "opacity-60", "hover:bg-transparent")
  return (
    <>
      <TableRow
        data-state={selected}
        // A parked act stays on the list quietened, the same treatment a voided payment gets.
        className={cn(pair, hasActions && "border-0")}
      >
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
          <span className={cn("font-medium", withdrawn && "text-muted-foreground")}>{item.designationFr}</span>
          {/* « Dents » stays VISIBLE: a bare « 14 15 16 » only reads as teeth to someone already expecting them. */}
          {item.toothNumbers.length > 0 && (
            <span className="mt-1 flex flex-wrap items-center gap-1">
              <span className="text-2xs uppercase tracking-wide text-muted-foreground">Dents</span>
              {item.toothNumbers.map((tooth) => (
                <Badge key={tooth} variant="secondary" className="text-xs">{tooth}</Badge>
              ))}
            </span>
          )}
          {/* Under the act's own name: the strip is THIS act's séances, each one tappable (T2). */}
          {seances && <PlanActSeances plan={plan} item={item} handlers={seances} />}
        </TableCell>
        <TableCell className="align-top text-right">{cost ?? planActCost(item)}</TableCell>
        {showState && (
          <TableCell className="align-top">
            <span className="flex flex-wrap items-center gap-2">
              <PlanActStateBadge item={item} planLive={isPlanLive(plan.status)} />
              {/* The only way the grouping is visible after booking — four acts on one date otherwise read as
                  four visits. Below `lg:` the card list's section header carries it. */}
              {sessionActCount > 1 && item.scheduledAppointmentId && (
                <Badge variant="outline" className="gap-1 whitespace-nowrap text-xs">
                  <Layers className="h-3 w-3" />
                  séance de {sessionActCount} actes
                </Badge>
              )}
            </span>
          </TableCell>
        )}
      </TableRow>
      {hasActions && (
        <TableRow data-state={selected} className={pair}>
          {/* The controls keep their WORDS — « Modifier » hidden in a « ⋯ » is what a dentist could not find. */}
          <TableCell colSpan={columnCount} className="pt-0 align-top">
            <div className="flex flex-wrap items-center gap-1">
              <PlanActPrimaryAction
                plan={plan}
                item={item}
                onSchedule={onSchedule}
                onUndo={onUndo}
                onRestore={onRestore}
                navigate={navigate}
              />
              {!withdrawn && !stepped && onEditSteps && (
                <PlanActStepsAction item={item} onEditSteps={onEditSteps} />
              )}
              {!withdrawn && onEdit && <PlanActEditAction item={item} onEdit={onEdit} />}
              {/* A secondary control beside the act's own action, never its primary one. */}
              {!withdrawn && onWithdraw && <PlanActWithdrawAction item={item} onWithdraw={onWithdraw} />}
            </div>
          </TableCell>
        </TableRow>
      )}
    </>
  )
}
