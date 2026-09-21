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
  ListOrdered, FilePen, Archive, ArchiveRestore, BadgePercent,
} from "lucide-react"
import type { TreatmentPlanDto, TreatmentPlanItemDto } from "@/lib/api/types"
import { formatDT, formatDateFr, quoteFr } from "@/lib/format"
import { itemWorkflowLabel, itemWorkflowBadgeClass } from "./treatment-plan-labels"
import {
  isPlanLive, planItemState, nextStepOf, isItemWithdrawn, detachOutcome, itemNetCost, itemDiscount,
} from "./plan-next-action"
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
  /**
   * Opens « Modifier le devis » with this act's line in focus. Omitted on a plan the server will not amend,
   * which is also what hides the control.
   */
  onEdit?: (item: TreatmentPlanItemDto) => void
  /**
   * Opens « Mettre cet acte de côté » (M1) — the per-act park: its fee leaves the total, the échéancier
   * re-spreads, its fiche links are kept and « Remettre au devis » brings it back. Omitted for an act that
   * cannot be parked (delivered work, or the last active act of the devis), which is also what hides it.
   */
  onWithdraw?: (item: TreatmentPlanItemDto) => void
  /** Opens « Remettre au devis » (M2) — the mirror, offered only on an act that is already parked. */
  onRestore?: (item: TreatmentPlanItemDto) => void
  /**
   * Opens « Remise » for this act (S2). Omitted on a plan the server will not amend, and on an act another
   * document bills (its fee is 0 here by rule, so a remise would take the line negative).
   */
  onDiscount?: (item: TreatmentPlanItemDto) => void
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
  /*
   * ⚠️ **A parked act rendered as an ordinary outstanding one.** Both trees map `plan.items`, so an act
   * « mis de côté » showed the badge « À planifier » with no button and no marker — indistinguishable from
   * work the patient is still waiting for, on the screen that answers « what is left to do? ». The header's
   * « · N mis de côté » counted it and no row said which. This is the état, so it replaces the workflow badge
   * rather than sitting beside it: `planItemState` speaks for the act's next step, which a parked act has not
   * got.
   */
  if (isItemWithdrawn(item)) {
    return (
      <>
        <Badge variant="outline" className="gap-1 whitespace-nowrap font-normal text-muted-foreground">
          <Archive className="h-3 w-3" />
          Mis de côté
        </Badge>
        <span className="text-xs text-muted-foreground">ne compte plus dans le total</span>
      </>
    )
  }

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
      {/*
        S5 — the protocol's own earliest day, in the cell that already answers « when? ».

        ⚠️ **SERVED, not derived here.** `TreatmentPlanItemStep.DueFrom(previousStepDoneOn)` is the rule and it
        lives in the domain; the wire carries `minDaysAfterPrevious` but not the SIBLING's date, so a
        browser-side `previous.doneDate + N` would be a second implementation of it — this repository's
        dominant defect shape. `earliestOn` is that method's answer.

        ⚠️ **« dès le », never « en retard ».** `MinDaysAfterPrevious` says *pas avant*, not *pas après*, so a
        date that has gone by breaks no promise and calling the step late would invent one — the exact error
        `InstallmentLateness` was rewritten to stop making about an auto-raised échéance. That is also why
        nothing here compares it with today: there is no state in which this line turns red.

        ⚠️ Withheld once the séance is booked: the appointment IS the answer to « when? » by then, and two
        dates in one cell read as a contradiction.
      */}
      {state === "to-schedule" && next?.earliestOn && (
        <span className="whitespace-nowrap text-xs text-muted-foreground">
          dès le {formatDateFr(next.earliestOn)}
        </span>
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
  onWithdraw,
  onRestore,
  block = false,
}: {
  plan: TreatmentPlanDto
  item: TreatmentPlanItemDto
  onSchedule: (item: TreatmentPlanItemDto) => void
  onUndo?: (item: TreatmentPlanItemDto) => void
  /** @see PlanActRowProps.onWithdraw */
  onWithdraw?: (item: TreatmentPlanItemDto) => void
  /** @see PlanActRowProps.onRestore */
  onRestore?: (item: TreatmentPlanItemDto) => void
  /**
   * Full width on its own row — the card tree's `primaryAction` slot.
   *
   * ⚠️ In the card header this control is `shrink-0` beside the act's name, and « Planifier la séance » plus the
   * « Étapes » trigger is ~200 px of a ~288 px card. Measured at 320 px, « Bridge 4 dents (14-17) » was left
   * so little that `[overflow-wrap:anywhere]` broke it to **one character per line** — a 26-line vertical
   * column of letters, and no overflow anywhere for a check to see. `CardList.primaryAction` exists for
   * exactly this, and planning the next étape is what this screen is opened to do.
   */
  block?: boolean
}) {
  const router = useRouter()
  const state = planItemState(item)
  /*
   * ⚠️ **A Draft counts as live, and this was the third copy of that rule.** « Suivre ce traitement » creates
   * an un-numbered treatment on purpose, and with `Accepted || InProgress` here its acts rendered « À
   * planifier » — the right état — beside **no button at all**, so the treatment the dentist had just started
   * offered no way to book its first séance. `schedulablePlanItems` and `usePatientPlanActs` were the other
   * two; a status test written out by hand is exactly how the third one is missed.
   */
  const planIsActive = isPlanLive(plan.status)
  // Named on the button when the act has steps: « Planifier » on a bridge two-thirds done answers the wrong
  // question, since what is being booked is one séance of it and the dentist has to know which.
  const next = nextStepOf(item)

  /*
   * ⚠️ **The booking may be on the STEP, not on the act**, and the two action arms below asked the act. The
   * badge is derived from the next *step*'s `scheduledAt` (`planItemState`), so a stepped act whose séance is
   * booked against the step read « À enregistrer · 12/08 » with no « Enregistrer la fiche » beside it — an
   * empty cell on the row that says work is waiting. `actRemovalPlan` already proves the act's own
   * `scheduledAppointmentId` can be null while the step carries one.
   */
  const bookedAppointmentId = next?.scheduledAppointmentId ?? item.scheduledAppointmentId ?? null

  // A parked act has no next step and nothing to book — what it has is a way back.
  if (isItemWithdrawn(item)) {
    if (!onRestore) return null
    return (
      <Button
        variant="outline"
        size="sm"
        className={cn("h-8 gap-1", block && "w-full justify-center")}
        onClick={() => onRestore(item)}
        aria-label={`Remettre ${quoteFr(item.designationFr)} au devis`}
      >
        <ArchiveRestore className="h-4 w-4" />
        Remettre au devis
      </Button>
    )
  }

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
        aria-label={next ? `Planifier la séance ${quoteFr(next.label)}` : undefined}
      >
        <CalendarPlus className="h-4 w-4" />
        {next ? "Planifier la séance" : "Planifier"}
      </Button>
    )
  }

  if (state === "scheduled" && bookedAppointmentId) {
    return (
      <Button
        variant="ghost"
        size="sm"
        className={cn("h-8 gap-1", block && "w-full justify-center")}
        onClick={() => router.push(`/appointments?appointmentId=${bookedAppointmentId}`)}
      >
        <CalendarCheck className="h-4 w-4" />
        Voir le RDV
      </Button>
    )
  }

  // The visit has passed with no fiche. The patient page's existing post-visit deep-link opens the record modal
  // already bound to that appointment, so saving closes the loop in one step.
  if (state === "to-record" && bookedAppointmentId) {
    return (
      <Button
        variant="outline"
        size="sm"
        className={cn("h-8 gap-1", block && "w-full justify-center")}
        onClick={() =>
          router.push(`/patients/${plan.patientId}?addRecord=1&appointmentId=${bookedAppointmentId}`)
        }
      >
        <FilePlus2 className="h-4 w-4" />
        Enregistrer la fiche
      </Button>
    )
  }

  if (state === "done") {
    const outcome = detachOutcome(item)
    const detachLabel =
      outcome.stepLabel && (outcome.remaining?.done ?? 0) > 0
        ? `Détacher la séance ${quoteFr(outcome.stepLabel)} de sa fiche de soins — `
          + `${quoteFr(item.designationFr)} repassera à « En cours »`
        : `Détacher la fiche de soins de ${quoteFr(item.designationFr)} — il repassera à « Prévu »`
    /*
     * ⚠️ **`flex-wrap` + a real `basis` + an explicit `shrink`, and all three are load-bearing.** `Button` is
     * `whitespace-nowrap shrink-0`, and `flex-1` does **not** clear that — they are different tailwind-merge
     * groups, so the element ends up with `flex: 1 1 0%` *and* `flex-shrink: 0`. Two un-shrinkable buttons
     * measured **222 px** of content in a **172 px** card at 320 px, and « Détacher » was cut in half; found by
     * the eye pass with `tsc`, `check:responsive` and `build` all green, which is this trap's whole signature.
     */
    return (
      <div className={cn("flex items-center gap-1", block ? "w-full flex-wrap" : "justify-end")}>
        <Button
          variant="ghost"
          size="sm"
          className={cn("h-8 gap-1", block && "flex-1 basis-28 shrink justify-center")}
          // ⚠️ THAT fiche, through the `?editRecord=` door `/factures` already uses — not `?tab=medical-records`,
          // which lands on a list of every séance the patient has and opens none of them.
          onClick={() =>
            router.push(
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
              "h-8 gap-1 text-muted-foreground hover:text-foreground",
              block && "flex-1 basis-28 shrink justify-center",
            )}
            onClick={() => onUndo(item)}
            /*
             * ⚠️ **The `title` said « Ramener cet acte à « Prévu » », which is the exact false claim
             * `detachOutcome` exists to remove**: `Unmark` releases the LAST séance recorded, so a
             * three-séance couronne lands on « En cours », 2 étapes sur 3 faites. And a `title` needs a hover,
             * which the tablet this app runs on has not got. Replaced by the outcome's own sentence on the
             * accessible name, where a screen reader and a keyboard both reach it; the full arithmetic is in
             * the confirmation the press opens.
             */
            aria-label={detachLabel}
          >
            <Unlink className="h-4 w-4" />
            Détacher
          </Button>
        )}
      </div>
    )
  }

  /*
   * ⚠️ **The tail was a bare `return null` — an empty cell on a row that plainly has something to say.** Every
   * arm above needs either a live plan or a booking, so a `Completed`, `Stopped` or `Cancelled` devis with
   * unrealised acts rendered a column of blanks: the badge said « À planifier » and the action column said
   * nothing at all, on the one screen a dentist opens to find out what is left. A muted sentence naming *why*
   * there is no action is not a control and costs no capability.
   */
  return (
    <span className={cn("text-xs text-muted-foreground", block && "block w-full text-center")}>
      {plan.status === "Cancelled"
        ? "Devis annulé"
        : plan.status === "Stopped"
          ? "Traitement arrêté"
          : plan.status === "WrittenOff"
            ? "Créance abandonnée"
            : plan.status === "Completed"
              ? "Traitement clôturé"
              : "Rien à faire pour l'instant"}
    </span>
  )
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
  /*
   * ⚠️ **A word, not a mute icon.** This was `size="icon"` with a `ListOrdered` glyph and a `title` — and a
   * `title` needs a hover, which the device this product is used on most does not have (§ 9.2), so on a tablet
   * the only editor of an act's protocol was an unlabelled square. The nine operations behind it (renommer ·
   * minutes · jours · monter · descendre · supprimer · ajouter · rétablir · tout-en-une) are all still there;
   * what changed is that a dentist can now see that they are.
   *
   * « Séances », never « Étapes »: the rest of this feature says séance everywhere a human reads it, and the
   * dialog it opens is titled « Séances de l'acte ».
   */
  return (
    <Button
      variant="ghost"
      size="sm"
      className="h-8 shrink-0 gap-1.5 px-2 text-muted-foreground coarse:h-10 hover-hover:hover:text-foreground"
      onClick={() => onEditSteps(item)}
      aria-label={
        count > 0
          ? `Modifier les ${count} séances de ${quoteFr(item.designationFr)}`
          : `Définir les séances de ${quoteFr(item.designationFr)}`
      }
    >
      <ListOrdered className="h-4 w-4" />
      <span className="hidden sm:inline">Séances</span>
    </Button>
  )
}

/**
 * « Remise » — the act's discount (S2), beside « Modifier ».
 *
 * <p>⚠️ <b>Its own control and its own command, not a field on the amend form.</b> The amend dialog sends every
 * act's designation and cost on every save, so folding the remise in would mean an older caller — or a save
 * made from a surface that does not know about remises — silently clears one. That is the tri-state trap this
 * repository has paid for three times on `DentalRecordAct`.</p>
 */
export function PlanActDiscountAction({
  item,
  onDiscount,
}: {
  item: TreatmentPlanItemDto
  onDiscount: (item: TreatmentPlanItemDto) => void
}) {
  const discount = itemDiscount(item)
  return (
    <Button
      variant="ghost"
      size="sm"
      className={cn(
        "h-8 shrink-0 gap-1.5 px-2 coarse:h-10 hover-hover:hover:text-foreground",
        discount > 0.0005 ? "text-foreground" : "text-muted-foreground",
      )}
      onClick={() => onDiscount(item)}
      aria-label={
        discount > 0.0005
          ? `Modifier la remise de ${quoteFr(item.designationFr)}`
          : `Accorder une remise sur ${quoteFr(item.designationFr)}`
      }
    >
      <BadgePercent className="h-4 w-4" />
      <span className="hidden sm:inline">Remise</span>
    </Button>
  )
}

/**
 * « Mettre de côté » — the per-act park (M1), beside « Séances » and « Modifier ».
 *
 * <p>⚠️ <b>This is the capability behind the literal complaint.</b> Removing an act with delivered work is
 * refused, and the refusal's named remedy is a three-deep chain the message does not disclose: détacher la
 * fiche → refused if that fiche is on a live note → whose own remedy is refused if the note has a payment. The
 * keep is correct; what was missing is a way to say « cet acte ne se fera pas » that preserves the fiche
 * links, drops the fee from the total, re-spreads the échéancier and needs none of the chain.</p>
 *
 * <p>A word rather than a mute icon, for `PlanActStepsAction`'s reason: a `title` needs a hover and this app
 * runs on a tablet.</p>
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
      className="h-8 shrink-0 gap-1.5 px-2 text-muted-foreground coarse:h-10 hover-hover:hover:text-foreground"
      onClick={() => onWithdraw(item)}
      aria-label={`Mettre ${quoteFr(item.designationFr)} de côté — il sort du total, rien n'est supprimé`}
    >
      <Archive className="h-4 w-4" />
      <span className="hidden sm:inline">De côté</span>
    </Button>
  )
}

/**
 * « Modifier » — the act's désignation, its fee and its teeth, on the row where they are read.
 *
 * <p>⚠️ It opens the plan-wide amendment dialog, and it exists because that dialog had exactly one door: a
 * « Modifier les actes et les prix » item inside the header's « ⋯ » menu. A dentist looking at the act they
 * wanted to change did not find it — reported as « I struggled to find how to edit ». The act's id travels
 * with it so the dialog can put that line in front of the reader instead of opening on the first one.</p>
 *
 * <p>A word rather than a mute icon, for `PlanActStepsAction`'s reason one component up: a `title` needs a
 * hover, and the device this product is used on most does not have one.</p>
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
      className="h-8 shrink-0 gap-1.5 px-2 text-muted-foreground coarse:h-10 hover-hover:hover:text-foreground"
      onClick={() => onEdit(item)}
      aria-label={`Modifier ${quoteFr(item.designationFr)} — désignation, honoraires, dents`}
    >
      <FilePen className="h-4 w-4" />
      <span className="hidden sm:inline">Modifier</span>
    </Button>
  )
}

/**
 * What an act's « Coût » column says.
 *
 * <p>⚠️ **An act another document bills shows the NOTE, never a bare « 0,000 DT ».** The 0 is correct and
 * imposed server-side (the note collected that fee and still does), but printed alone it says nothing — it
 * reads as a free act, and on the screen that is supposed to be the treatment's money it made the devis' 10 DT
 * look like the whole treatment while the patient had already paid 50 and still owed 40. Reported as a money
 * gap; the money was right and the screen was silent.</p>
 *
 * <p>⚠️ The rule was already written one file over — `act-card.tsx` withholds the price field on a
 * plan-carried act and prints « Aucun honoraire sur cette séance » in its place, on the stated ground that
 * « 0,000 DT » « is the third of the séance's zeros and it says nothing ». This is the same rule at the other
 * end of the same arrangement, which is why it is spelled the same way rather than invented again.</p>
 *
 * <p>⚠️ The amount is printed only when it was recoverable: a séance that billed several acts cannot say which
 * share of the note was this one, so the row names the note and stops there rather than quoting the whole
 * note's TTC as this act's fee.</p>
 */
function planActCost(item: TreatmentPlanItemDto) {
  if (!item.billedOnInvoiceId) {
    const discount = itemDiscount(item)
    /*
     * ⚠️ **S2 — the tarif and the remise are both shown, and the NET is the figure.** Printing the net alone
     * is exactly the state the remise exists to leave behind: a 350 nobody can account for, on a devis whose
     * catalogue says 400. The two extra lines render only when a remise was granted, so an ordinary act's cell
     * is unchanged.
     */
    if (discount <= 0.0005) {
      // The same figure as the tarif on an act with no remise, read through the owner all the same: what the
      // cell means is « what the patient owes for this act », and N40 is what keeps that one question one read.
      return <span className="tabular-nums">{formatDT(itemNetCost(item))}</span>
    }
    return (
      <span className="block">
        <span className="tabular-nums">{formatDT(itemNetCost(item))}</span>
        <span className="block text-2xs text-muted-foreground">
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
    { label: "Coût", value: planActCost(item) },
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
  onEdit,
  onWithdraw,
  onRestore,
  onDiscount,
  reorder,
  selection,
  sessionActCount = 1,
}: PlanActRowProps) {
  const withdrawn = isItemWithdrawn(item)
  // Désignation · Coût · État, plus the two optional leading columns — what the action sub-row has to span.
  const columnCount = 3 + (selection ? 1 : 0) + (reorder ? 1 : 0)
  /*
   * The only case with nothing to offer: a parked act on a devis the reader may not amend. Every other path
   * through `PlanActPrimaryAction` returns something — its tail is a muted sentence naming why there is no
   * control, which is itself the fix for an earlier bare `return null`.
   */
  const hasActions = !withdrawn || Boolean(onRestore)
  const selected = selection?.checked ? "selected" : undefined
  /*
   * ⚠️ **Two `<tr>` for one act, and the pair has to READ as one act.** `TableRow`'s default hover tint would
   * otherwise light up half of it, and the hairline would run between the act and its own buttons — so the
   * top row drops its border and both rows drop the hover. `data-state` goes on both, or ticking the checkbox
   * tints the act and not its controls.
   */
  const pair = cn(withdrawn && "opacity-60", "hover:bg-transparent")
  return (
    <>
      <TableRow
        data-state={selected}
        // A parked act is still part of the devis' history and stays on the list; it is quietened rather than
        // hidden, which is the same treatment a voided payment gets two cards down.
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
          {/*
            The teeth, under the act they qualify rather than in a column of their own — see the table header
            for why the column went. ⚠️ The word « Dents » is VISIBLE and not a `title`: it was a column
            heading, so dropping it silently would leave a bare « 14 15 16 » that only a dentist already
            expecting teeth can read, and a `title` needs a hover this app's tablet has not got. Nothing is
            drawn when the act names none — the old « — » said the same thing and cost a line.
          */}
          {item.toothNumbers.length > 0 && (
            <span className="mt-1 flex flex-wrap items-center gap-1">
              <span className="text-2xs uppercase tracking-wide text-muted-foreground">Dents</span>
              {item.toothNumbers.map((tooth) => (
                <Badge key={tooth} variant="secondary" className="text-xs">{tooth}</Badge>
              ))}
            </span>
          )}
          {/* Under the act's own name, not in the État cell: the strip describes THIS ACT's progress, while the
              État cell answers a different question — what to do next about it. */}
          <PlanStepStrip steps={item.steps} nextStepId={item.nextStepId} />
        </TableCell>
        {/* ⚠️ `whitespace-nowrap`: it is the cell the squeeze lands on first, because « 300,000 DT » has a
            space to break at and a button has none — it rendered as « 300,000 / DT » with the remise line
            below it on four. */}
        <TableCell className="align-top whitespace-nowrap text-right">{planActCost(item)}</TableCell>
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
      </TableRow>
      {hasActions && (
        <TableRow data-state={selected} className={pair}>
          {/*
            ⚠️ The controls keep their WORDS. Folding them behind a « ⋯ » was the obvious way to reclaim the
            width and it is the mistake `PlanActEditAction` records: « Modifier » already lived in the header's
            menu and a dentist « struggled to find how to edit ». A full-width row costs vertical space, which
            this page has, instead of capability, which it has not.
          */}
          <TableCell colSpan={columnCount} className="pt-0 align-top">
            <div className="flex flex-wrap items-center gap-1">
              <PlanActPrimaryAction
                plan={plan}
                item={item}
                onSchedule={onSchedule}
                onUndo={onUndo}
                onWithdraw={onWithdraw}
                onRestore={onRestore}
              />
              {!withdrawn && onEditSteps && <PlanActStepsAction item={item} onEditSteps={onEditSteps} />}
              {!withdrawn && onEdit && <PlanActEditAction item={item} onEdit={onEdit} />}
              {!withdrawn && onDiscount && <PlanActDiscountAction item={item} onDiscount={onDiscount} />}
              {/* « Mettre de côté » is a secondary control beside the act's own action, never the row's primary
                  one: the thing to do with an act is to carry it out. */}
              {!withdrawn && onWithdraw && <PlanActWithdrawAction item={item} onWithdraw={onWithdraw} />}
            </div>
          </TableCell>
        </TableRow>
      )}
    </>
  )
}
