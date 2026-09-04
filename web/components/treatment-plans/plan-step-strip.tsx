import { Check } from "lucide-react"
import { cn } from "@/lib/utils"
import type { TreatmentPlanItemStepDto } from "@/lib/api/types"

/**
 * The clinical steps of one planned act — « Préparation ✓ · Empreinte ✓ · Scellement ».
 *
 * <p>The one visual element multi-séance acts add. It carries the act's <b>progress</b>; the row's état badge
 * carries the next <b>action</b> and the primary button names it. Splitting those three jobs is what let the
 * feature ship with no new état label and no new colour: « en cours » as a badge would have said less than
 * « À planifier » does, because it names no next step.</p>
 *
 * <p>⚠️ <b>Purely descriptive — nothing here is interactive</b>, which is why the 44 px coarse-pointer floor does
 * not apply to the pips. Editing the list is `plan-item-steps-dialog`, reached from the row's own control.</p>
 *
 * <p>⚠️ <b>Renders nothing for an act with no steps</b>, and that is the property the whole feature rests on: an
 * act done in one séance is almost every act, and it must look exactly as it did before. No « 1 / 1 », no empty
 * rule, not a pixel.</p>
 */
export function PlanStepStrip({
  steps,
  nextStepId,
  className,
  divider = true,
}: {
  steps: TreatmentPlanItemStepDto[] | undefined
  nextStepId: string | null | undefined
  className?: string
  /**
   * The dashed rule above the strip. On in a table cell, where the strip sits under the act's own name; off in
   * a card, where the card's own gaps already separate it and a second rule reads as a divider between records.
   */
  divider?: boolean
}) {
  if (!steps || steps.length === 0) {
    return null
  }

  const done = steps.filter((s) => s.doneDate).length

  // Beyond five, the labels stop fitting anywhere — 320 px cannot take five names and neither can a table cell
  // at 820 px. Pips alone plus the next step's name is the same trade `plan-act-pips` / `plan-progress-bar`
  // already make on a long devis, not a new invention.
  const compact = steps.length > 5
  const next = nextStepId ? steps.find((s) => s.id === nextStepId) : undefined

  return (
    <div
      className={cn(
        "flex flex-wrap items-center gap-y-1.5",
        divider && "mt-2.5 border-t border-dashed pt-2",
        className,
      )}
    >
      {compact ? (
        <>
          <span className="flex shrink-0 items-center gap-1" aria-hidden="true">
            {steps.map((step) => (
              <StepPip key={step.id} step={step} isNext={step.id === nextStepId} compact />
            ))}
          </span>
          {next && (
            <span className="ms-2.5 text-xs font-semibold text-primary">{next.label}</span>
          )}
        </>
      ) : (
        steps.map((step, index) => (
          <span key={step.id} className="flex items-center">
            <span
              className={cn(
                "flex items-center gap-1.5 text-xs",
                step.doneDate
                  ? "text-foreground"
                  : step.id === nextStepId
                    ? "font-semibold text-primary"
                    : "text-muted-foreground",
              )}
            >
              <StepPip step={step} isNext={step.id === nextStepId} />
              {/* Never truncated: a protocol abbreviated to « Scelle… » has stopped saying anything. */}
              {step.label}
            </span>
            {/*
              ⚠️ The connector TRAILS its step rather than leading the next one, and that is a wrap fix rather
              than a preference. Three steps with real French labels — « Préparation · Empreinte · Scellement »
              is the seeded protocol — do not fit a designation cell at any width this product supports, so the
              common case wraps. Led by the connector, the second line opened with a dangling rule floating in
              space before the pip, which reads as a stray hyphen or a missing step. Trailing, a line can never
              begin with one: it ends with one, which reads as « continues below ». Measured at 1440 px with the
              rail open — the widest case there is.
            */}
            {index < steps.length - 1 && (
              <span aria-hidden="true" className="ms-1.5 me-1 h-px w-5 bg-border" />
            )}
          </span>
        ))
      )}

      {/*
        The count is what stays legible once the strip wraps or goes compact, so it is pushed to the end and
        read out for a screen reader — which gets the whole sentence rather than a row of decorative dots.
      */}
      <span className="ms-auto ps-2.5 font-mono text-2xs tabular-nums text-muted-foreground">
        <span className="sr-only">Étapes réalisées : </span>
        {done} / {steps.length}
        {next && <span className="sr-only">. Prochaine étape : {next.label}</span>}
      </span>
    </div>
  )
}

/**
 * One step's pip. Filled `--success` once carried out, a dashed `--primary` ring for the one that comes next,
 * a plain border for the rest — the same three readings the four état badges already use, so the strip and the
 * badge beside it cannot describe the same act in two visual languages (the defect `plan-act-pips` documents).
 */
function StepPip({
  step,
  isNext,
  compact = false,
}: {
  step: TreatmentPlanItemStepDto
  isNext: boolean
  compact?: boolean
}) {
  const size = compact ? "size-2.5" : "size-3.5"

  if (step.doneDate) {
    return (
      <span
        aria-hidden="true"
        className={cn(
          "flex flex-none items-center justify-center rounded-full border-[1.5px] border-success bg-success",
          size,
        )}
      >
        {!compact && <Check className="size-2 text-white" strokeWidth={4} />}
      </span>
    )
  }

  return (
    <span
      aria-hidden="true"
      className={cn(
        "flex-none rounded-full border-[1.5px]",
        size,
        isNext ? "border-dashed border-primary" : "border-border",
      )}
    />
  )
}
