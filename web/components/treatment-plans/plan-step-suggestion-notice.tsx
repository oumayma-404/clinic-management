"use client"

import { CalendarPlus, Route, X } from "lucide-react"
import { Button } from "@/components/ui/button"
import { formatDT, quoteFr } from "@/lib/format"
import type { PlanStepSuggestion } from "./plan-next-action"

interface PlanStepSuggestionNoticeProps {
  suggestion: PlanStepSuggestion
  /** Accepts it: the caller puts the act (and its step) into the séance being booked. */
  onAccept: () => void
  /** Hides it for this patient. The picker's « Actes du devis » group still holds every act. */
  onDismiss: () => void
  /** Set while the caller is still fetching what it needs to accept — the catalogue, usually. */
  disabled?: boolean
}

/**
 * « Ce patient a un traitement en cours » — the reminder a booking dialog gives before anything is picked.
 *
 * <p>The dentist books from the agenda, in a hurry, for a patient whose bridge is half done, and nothing on that
 * path used to mention the devis. So the séance went in as a loose act, the devis went on reporting the
 * scellement as unplanned, and the money owed on it stayed attached to a plan nobody was advancing.</p>
 *
 * <p>⚠️ <b>A suggestion, not a default.</b> It is never applied on its own: the act appears in the séance when
 * this button is pressed, and the dentist who came to book something else simply picks something else. Silently
 * pre-filling would put a devis link — and a devis' money — on a visit nobody said was part of it, and it would
 * be found only by whoever later wondered why the bridge advanced a step in a séance about a toothache.</p>
 *
 * <p>⚠️ <b>The fee is stated, and it is stated as already covered.</b> « Déjà facturé sur le devis » is the whole
 * reason the client asked for this feature — the séance that finishes a 1 000 DT bridge must not be billed a
 * second time — so the one figure worth showing here is the one that says « ne rien encaisser ».</p>
 *
 * <p>⚠️ It is <b>dismissible</b>. A reminder that cannot be put away is an obstacle by the third booking, and the
 * information is not lost: every act it could have offered is in the picker's « Actes du devis » group below.</p>
 */
export function PlanStepSuggestionNotice({
  suggestion,
  onAccept,
  onDismiss,
  disabled = false,
}: PlanStepSuggestionNoticeProps) {
  const { plan, item, step, continuing } = suggestion
  const actLabel =
    item.toothNumbers.length > 0
      ? `${item.designationFr} (dents ${item.toothNumbers.join(", ")})`
      : item.designationFr

  return (
    <div
      // `role="status"`, not `alert`: nothing has gone wrong and nothing is being refused. It is offered on a
      // dashed border rather than a filled surface for the same reason — this is a proposal, and a solid
      // accent block above the form would read as the form's own first field.
      role="status"
      className="space-y-2 rounded-md border border-dashed border-primary/60 bg-primary/[0.05] p-3"
    >
      <div className="flex items-start gap-2">
        <Route className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
        <div className="min-w-0 flex-1 space-y-0.5">
          <p className="text-sm font-medium">
            {continuing ? "Ce patient a un traitement en cours." : "Ce patient a un devis accepté."}
          </p>
          <p className="text-xs text-muted-foreground [overflow-wrap:anywhere]">
            {actLabel}
            {plan.number && <span className="font-mono"> · {plan.number}</span>}
          </p>
          {/*
            The step is what the séance would BE, so it leads its own line rather than being folded into the act
            — the same reading « Traitements en cours » settled on for its « prochaine étape » column.
          */}
          {step && (
            <p className="text-xs">
              <span className="text-muted-foreground">Prochaine étape&nbsp;: </span>
              <span className="font-medium">{step.label}</span>
              {step.estimatedDurationMinutes != null && (
                <span className="text-muted-foreground"> · {step.estimatedDurationMinutes} min</span>
              )}
            </p>
          )}
          <p className="text-2xs text-muted-foreground">
            {/* See the class note: the point of the figure is that it must NOT be collected again. */}
            {formatDT(item.plannedCost)} — déjà facturé sur le devis, rien à encaisser pour cette séance.
          </p>
        </div>
        {/* Icon-only, so it carries its own name (P2). It puts the reminder away; it decides nothing. */}
        <Button
          type="button"
          variant="ghost"
          size="icon"
          className="size-8 shrink-0 text-muted-foreground coarse:size-11"
          aria-label={`Ignorer la suggestion ${quoteFr(actLabel)}`}
          onClick={onDismiss}
        >
          <X className="h-4 w-4" />
        </Button>
      </div>

      {/*
        Full width and on its own row — `treatments-in-progress-list`'s `primaryAction` note, measured: a
        ~180 px label beside a title inside a dialog that is ~288 px wide at 320 px breaks the act's name
        mid-word.
      */}
      <Button
        type="button"
        variant="outline"
        size="sm"
        // ⚠️ `whitespace-normal` + `h-auto`, because the label carries a free-text step name of unknown
        // width: `ui/button.tsx` sets `whitespace-nowrap` on every button, so « Planifier « Essayage +
        // scellement définitif » » measures ~255 px against a ~232 px box at 320 px and paints out through
        // both edges. § 10.1 — the label is the control's name, so break it rather than truncate it, and a
        // fixed height would then clip the second line.
        className="h-auto min-h-8 w-full gap-1 whitespace-normal py-1.5 text-xs coarse:min-h-11"
        disabled={disabled}
        onClick={onAccept}
      >
        <CalendarPlus className="h-3.5 w-3.5" />
        {step ? `Planifier ${quoteFr(step.label)}` : "Planifier cet acte"}
      </Button>
    </div>
  )
}
