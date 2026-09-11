"use client"

import { CalendarPlus, ChevronRight, Route, X } from "lucide-react"
import { Button } from "@/components/ui/button"
import { formatDT, formatDateFr, quoteFr } from "@/lib/format"
import type { PlanStepSuggestion, PlanSuggestionSet } from "./plan-next-action"

interface PlanStepSuggestionNoticeProps {
  /**
   * What {@link suggestedPlanSteps} found — the treatments to name **and** the ones that did not fit.
   *
   * <p>⚠️ One object rather than a list plus a count, so that a surface rendering the rows cannot forget the
   * « et N autres » line: a patient with six open treatments shown three of them and no hint of the rest is
   * told a silent half-truth by the one surface whose job is to stop something being overlooked.</p>
   */
  set: PlanSuggestionSet
  /** Accepts one of them: the caller puts that act (and its step) into the séance being booked. */
  onAccept: (suggestion: PlanStepSuggestion) => void
  /** Hides the whole notice for this patient. The picker's « Actes du devis » group still holds every act. */
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
 * one of these controls is pressed, and the dentist who came to book something else simply picks something else.
 * Silently pre-filling would put a devis link — and a devis' money — on a visit nobody said was part of it, and
 * it would be found only by whoever later wondered why the bridge advanced a step in a séance about a
 * toothache.</p>
 *
 * <p>⚠️ It is <b>dismissible</b>. A reminder that cannot be put away is an obstacle by the third booking, and the
 * information is not lost: every act it could have offered is in the picker's « Actes du devis » group below.</p>
 *
 * <p>⚠️ <b>ONE suggestion and SEVERAL are drawn differently, and the split is measured rather than chosen.</b>
 * This named exactly one treatment until 2026-09-11 — « the first one under way » over plans read
 * most-recently-created-first — so a patient with two live treatments was told about an arbitrary one of them
 * and nothing whatever about the other, on the only surface in the product that says « ce patient a un
 * traitement en cours ». Listing them at that first row's weight is the trap on the other side: measured at
 * 320×730, two rows of full detail came to <b>462 px inside a 385 px scrolling region</b> — the date, the heure
 * and the acts all below it, with the same « n'ajoute pas d'honoraires » sentence costing 32 px in *each* row —
 * and three would have been ~640 px. So the single case keeps its full detail (it is 309 of the 318 patients
 * who have a live treatment), and several become compact one-tap rows with the money stated once for all of
 * them. Nothing is lost by that: accepting a row puts the act in the picker, whose card states « Déjà facturé »
 * with this act's fee and the devis' own balance.</p>
 */
export function PlanStepSuggestionNotice({
  set,
  onAccept,
  onDismiss,
  disabled = false,
}: PlanStepSuggestionNoticeProps) {
  const { suggestions, hiddenCount } = set
  const several = suggestions.length > 1

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
          <p className="text-sm font-medium">{headline(suggestions)}</p>
          {/* The whole point of listing them: the app is not choosing, it is asking. */}
          {several && <p className="text-xs text-muted-foreground">Lequel continuez-vous&nbsp;?</p>}
        </div>
        {/* Icon-only, so it carries its own name (P2). It puts the reminder away; it decides nothing. */}
        <Button
          type="button"
          variant="ghost"
          size="icon"
          className="size-8 shrink-0 text-muted-foreground coarse:size-11"
          aria-label={
            several
              ? "Ignorer les suggestions de traitement"
              : `Ignorer la suggestion ${quoteFr(actLabelOf(suggestions[0]))}`
          }
          onClick={onDismiss}
        >
          <X className="h-4 w-4" />
        </Button>
      </div>

      {several ? (
        <>
          <ul className="space-y-2">
            {suggestions.map((suggestion) => (
              <li key={suggestion.item.id}>
                <CompactRow suggestion={suggestion} onAccept={onAccept} disabled={disabled} />
              </li>
            ))}
          </ul>
          {/*
            ⚠️ The money fact, once. It is the reason the client asked for this feature — the séance that
            finishes a 1 000 DT bridge must not be billed a second time — and it is equally true of every row,
            so per-row it was one sentence repeated N times and 32 px of a 385 px scrollport each.
          */}
          <p className="text-2xs text-muted-foreground [overflow-wrap:anywhere]">
            Ces actes sont déjà facturés sur leur devis&nbsp;: la séance n&apos;ajoute pas d&apos;honoraires.
          </p>
        </>
      ) : (
        <DetailedRow suggestion={suggestions[0]} onAccept={onAccept} disabled={disabled} />
      )}

      {hiddenCount > 0 && (
        <p className="text-2xs text-muted-foreground [overflow-wrap:anywhere]">
          et {hiddenCount} autre{hiddenCount > 1 ? "s" : ""} traitement
          {hiddenCount > 1 ? "s" : ""} ouvert{hiddenCount > 1 ? "s" : ""} — voir «&nbsp;Actes du
          devis&nbsp;» ci-dessous.
        </p>
      )}
    </div>
  )
}

/**
 * The only treatment this patient has open, in full — unchanged from the day the notice shipped.
 *
 * <p>⚠️ <b>Full width, not indented under the icon.</b> The icon and its gap cost 24 px of every line and the
 * act's own name is the widest thing here: an `[overflow-wrap:anywhere]` désignation has ~232 px to live in at
 * 320 px.</p>
 */
function DetailedRow({
  suggestion,
  onAccept,
  disabled,
}: {
  suggestion: PlanStepSuggestion
  onAccept: (suggestion: PlanStepSuggestion) => void
  disabled: boolean
}) {
  const { plan, item, step, passedSlotAt } = suggestion

  return (
    <div className="space-y-1">
      <p className="text-xs text-muted-foreground [overflow-wrap:anywhere]">
        {actLabelOf(suggestion)}
        {plan.number && <span className="font-mono"> · {plan.number}</span>}
      </p>

      {/*
        The step is what the séance would BE, so it leads its own line rather than being folded into the act
        — the same reading « Traitements en cours » settled on for its « prochaine étape » column.
      */}
      {step && (
        <p className="text-xs [overflow-wrap:anywhere]">
          <span className="text-muted-foreground">Prochaine étape&nbsp;: </span>
          <span className="font-medium">{step.label}</span>
          {step.estimatedDurationMinutes != null && (
            <span className="text-muted-foreground"> · {step.estimatedDurationMinutes} min</span>
          )}
        </p>
      )}

      <StepsDoneLine item={item} />
      <PassedSlotLine passedSlotAt={passedSlotAt} />

      <p className="text-2xs text-muted-foreground [overflow-wrap:anywhere]">
        {/*
          ⚠️ It used to end « rien à encaisser pour cette séance », and that became false the day the fiche
          gained « Encaissé sur le traitement »: a multi-séance act is priced once and collected a visit at a
          time, so there is very often something to take — just not a second fee. The figure's point is
          unchanged (it must not be charged again); what was wrong was the claim about the money.
        */}
        {formatDT(item.plannedCost)} pour tout le traitement — cette séance n&apos;ajoute pas
        d&apos;honoraires.
      </p>

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
        onClick={() => onAccept(suggestion)}
      >
        <CalendarPlus className="h-3.5 w-3.5 shrink-0" />
        {step ? `Planifier ${quoteFr(step.label)}` : "Planifier cet acte"}
      </Button>
    </div>
  )
}

/**
 * One treatment among several: what it is, which séance would be booked, and how far along it is — as a single
 * control.
 *
 * <p>⚠️ <b>The row IS the button</b>, rather than a block with a « Planifier … » button under it. That button
 * repeats the step name it sits beneath, and at 320 px it wraps to two lines, so per row it cost ~36 px to say
 * nothing new. A real `<button>` also brings the keyboard and the accessible role that § 13 would otherwise
 * require to be added by hand to a clickable div.</p>
 *
 * <p>⚠️ It carries an explicit `aria-label` because its own text is three fragments in reading order: announced
 * as-is, the first thing a screen-reader user hears is a désignation with no verb anywhere near it.</p>
 */
function CompactRow({
  suggestion,
  onAccept,
  disabled,
}: {
  suggestion: PlanStepSuggestion
  onAccept: (suggestion: PlanStepSuggestion) => void
  disabled: boolean
}) {
  const { plan, item, step, passedSlotAt } = suggestion
  const actLabel = actLabelOf(suggestion)

  return (
    <button
      type="button"
      disabled={disabled}
      onClick={() => onAccept(suggestion)}
      aria-label={
        step
          ? `Planifier ${quoteFr(step.label)} — ${actLabel}${plan.number ? `, devis ${plan.number}` : ""}`
          : `Planifier ${quoteFr(actLabel)}${plan.number ? `, devis ${plan.number}` : ""}`
      }
      className="flex w-full items-start gap-2 rounded-md border bg-card p-2 text-start transition-colors hover:bg-accent/40 disabled:pointer-events-none disabled:opacity-60"
    >
      <div className="min-w-0 flex-1 space-y-0.5">
        <p className="text-xs font-medium [overflow-wrap:anywhere]">{actLabel}</p>

        {step && (
          <p className="text-xs [overflow-wrap:anywhere]">
            <span className="text-muted-foreground">Prochaine étape&nbsp;: </span>
            <span className="font-medium">{step.label}</span>
            {step.estimatedDurationMinutes != null && (
              <span className="text-muted-foreground"> · {step.estimatedDurationMinutes} min</span>
            )}
          </p>
        )}

        <p className="text-2xs text-muted-foreground [overflow-wrap:anywhere]">
          {plan.number && <span className="font-mono">{plan.number}</span>}
          {plan.number && " · "}
          {formatDT(item.plannedCost)}
        </p>

        <StepsDoneLine item={item} />
        <PassedSlotLine passedSlotAt={passedSlotAt} />
      </div>
      <ChevronRight className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground" aria-hidden="true" />
    </button>
  )
}

/**
 * « 3 étapes faites sur 6 » — how much of this act is behind us.
 *
 * <p>⚠️ <b>« faites » is not decoration — N31.</b> A bare « 3 / 6 » beside « Prochaine étape » has been read as
 * progress every time it has been measured in this product, and here the two counters sit one line apart.
 * Printed only when some work is behind us, because « 0 étape faite » says nothing the act's own état does
 * not.</p>
 */
function StepsDoneLine({ item }: { item: PlanStepSuggestion["item"] }) {
  const stepsTotal = item.steps?.length ?? 0
  const stepsDone = item.steps?.filter((s) => s.doneDate != null).length ?? 0
  if (stepsTotal === 0 || stepsDone === 0) return null

  return (
    <p className="text-2xs text-muted-foreground">
      {stepsDone} étape{stepsDone > 1 ? "s" : ""} faite{stepsDone > 1 ? "s" : ""} sur {stepsTotal}
    </p>
  )
}

/**
 * « La séance du 10 sept. 2026 n'a pas encore de fiche. »
 *
 * <p>⚠️ <b>A statement about our RECORDS, never about the mouth.</b> The slot has gone by with nobody closing
 * the visit, which may mean the séance was carried out and not written up, or that it never happened — so the
 * date is printed and the dentist decides. It is also why this act is offered at all: withholding it left 47 of
 * the 318 patients with a live treatment shown no reminder whatsoever.</p>
 */
function PassedSlotLine({ passedSlotAt }: { passedSlotAt: string | null }) {
  if (!passedSlotAt) return null
  return (
    <p className="text-2xs text-muted-foreground">
      La séance du {formatDateFr(passedSlotAt)} n&apos;a pas encore de fiche.
    </p>
  )
}

/** An act as this notice names it — its désignation, with the teeth it is quoted on. */
function actLabelOf(suggestion: PlanStepSuggestion): string {
  const { item } = suggestion
  return item.toothNumbers.length > 0
    ? `${item.designationFr} (dents ${item.toothNumbers.join(", ")})`
    : item.designationFr
}

/**
 * What the patient has open, counted and phrased honestly.
 *
 * <p>⚠️ Three branches because « traitement en cours » about a devis accepted last week with nothing done yet
 * would be a small lie told by the one surface whose job is to remind somebody of a fact they had forgotten —
 * « un devis accepté » is the truth and just as useful. Mixed, « ouverts » is the word that covers both without
 * claiming either.</p>
 */
function headline(suggestions: readonly PlanStepSuggestion[]): string {
  const n = suggestions.length
  const started = suggestions.filter((s) => s.continuing).length

  if (n === 1) {
    return suggestions[0].continuing
      ? "Ce patient a un traitement en cours."
      : "Ce patient a un devis accepté."
  }
  if (started === n) return `Ce patient a ${n} traitements en cours.`
  if (started === 0) return `Ce patient a ${n} devis acceptés.`
  return `Ce patient a ${n} traitements ouverts.`
}
