"use client"

import { useState, type ReactNode } from "react"
import { format, parseISO } from "date-fns"
import { CalendarPlus, ChevronDown, History, X } from "lucide-react"
import { Button } from "@/components/ui/button"
import { formatDurationFr } from "@/components/appointment-recap"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { SeancePips } from "@/components/treatment-plans/seance-strip"
import { teethSuffix } from "@/components/treatment-plans/treatment-plan-labels"
import type { ContinuableActs } from "@/components/treatment-plans/continue-session-dialog"
import type { ContinuableActDto, TreatmentPlanDto } from "@/lib/api/types"
import { quoteFr } from "@/lib/format"
import { cn } from "@/lib/utils"
import { MAX_PLAN_SUGGESTIONS, suggestedPlanSteps } from "./plan-next-action"
import type { PlanStepSuggestion } from "./plan-next-action"

/**
 * Every treatment worth continuing, most urgent first — {@link suggestedPlanSteps} past its cap of three.
 *
 * <p>⚠️ Re-asked on the plans it has not named yet rather than re-derived: one-per-treatment and the urgency
 * order stay in `suggestedPlanSteps` alone, and a stable sort over the remaining plans continues the same
 * order. The first three are what the list shows; the rest go in its « N autres » fold.</p>
 */
export function allPlanSuggestions(plans: readonly TreatmentPlanDto[]): PlanStepSuggestion[] | null {
  const out: PlanStepSuggestion[] = []
  let rest = [...plans]
  for (;;) {
    const set = suggestedPlanSteps(rest)
    if (!set) break
    out.push(...set.suggestions)
    if (set.hiddenCount === 0) break
    const named = new Set(set.suggestions.map((s) => s.plan.id))
    rest = rest.filter((p) => !named.has(p.id))
  }
  return out.length > 0 ? out : null
}

interface ContinueTreatmentListProps {
  /** Treatments with a séance to book, from {@link allPlanSuggestions}; null when none is offered. */
  suggestions: PlanStepSuggestion[] | null
  /** Puts that act (and its step) on the séance — the same preset path as « Actes du devis ». */
  onAccept: (suggestion: PlanStepSuggestion) => void
  /** Past séances this booking may continue; null when the door is not offered (no patient, a devis act…). */
  continuable: ContinuableActs | null
  /** The act already continued on this RDV — not offered twice. */
  pendingActId?: string | null
  /** Adds the continuation row (replacing a pending one). Writes nothing — see `materialiseTreatments`. */
  onContinue: (act: ContinuableActDto) => void
  /** Hides the prompts for this patient; « Suite d'une séance précédente… » and « Actes du devis » still hold everything. */
  dismissed: boolean
  onDismiss: () => void
  /** Set while the caller is still fetching what it needs to accept — the catalogue, usually. */
  disabled?: boolean
}

type Card =
  | { kind: "plan"; key: string; suggestion: PlanStepSuggestion }
  | { kind: "past"; key: string; act: ContinuableActDto }

/**
 * « Continuer un traitement » — what this patient has under way, before anything is picked.
 *
 * <p>⚠️ <b>A suggestion, never a default.</b> Nothing lands on the séance until a card's button is pressed:
 * pre-filling would put a devis link — and its money — on a visit nobody said was part of it.</p>
 *
 * <p>Two kinds of card, one list: a treatment's next séance (« Planifier : Empreinte · 30 min ») and a past
 * séance ticked « non terminé » (« Continuer »). Three are shown, the rest are one tap away under « N autres »;
 * an unticked past séance sits under « Suite d'une séance précédente… », which is also what stays once the
 * prompts are hidden.</p>
 *
 * <p>⚠️ That fold is <b>always there while the door is offered</b>, even with nothing behind it (« Aucune séance
 * passée pour ce patient. »): shown only with rows, the door read as removed — the old link was always visible.</p>
 */
export function ContinueTreatmentList({
  suggestions,
  onAccept,
  continuable,
  pendingActId,
  onContinue,
  dismissed,
  onDismiss,
  disabled = false,
}: ContinueTreatmentListProps) {
  const [showMore, setShowMore] = useState(false)
  const [showPast, setShowPast] = useState(false)

  const pastActs = (continuable?.acts ?? []).filter((a) => a.actId !== pendingActId)
  const prompts: Card[] = dismissed
    ? []
    : [
        ...(suggestions ?? []).map((s): Card => ({ kind: "plan", key: `plan-${s.item.id}`, suggestion: s })),
        ...pastActs.filter((a) => a.isUnfinished).map((a): Card => ({ kind: "past", key: `past-${a.actId}`, act: a })),
      ]
  const visible = prompts.slice(0, MAX_PLAN_SUGGESTIONS)
  const more = prompts.slice(MAX_PLAN_SUGGESTIONS)
  const promptedIds = new Set(prompts.flatMap((c) => (c.kind === "past" ? [c.act.actId] : [])))
  const foldedPast = pastActs.filter((a) => !promptedIds.has(a.actId))

  // ⚠️ Always offered while the door is open, rows or not: a door that appears only when it has something behind
  // it read as « la suite d'une séance précédente a disparu ». Loading / empty / failed are said inside it.
  const offerPast = continuable != null
  if (visible.length === 0 && !offerPast) return null

  const renderCard = (card: Card) =>
    card.kind === "plan" ? (
      <PlanCard key={card.key} suggestion={card.suggestion} onAccept={onAccept} disabled={disabled} />
    ) : (
      <PastCard key={card.key} act={card.act} onContinue={onContinue} disabled={disabled} />
    )

  return (
    <section aria-label="Continuer un traitement" className="space-y-2">
      {visible.length > 0 && (
        <>
          <div className="flex items-center justify-between gap-2">
            <p className="text-sm font-medium">Continuer un traitement</p>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              className="size-8 shrink-0 text-muted-foreground coarse:size-11"
              aria-label="Masquer les traitements à continuer"
              onClick={onDismiss}
            >
              <X className="h-4 w-4" />
            </Button>
          </div>
          <ul className="space-y-2">
            {visible.map((card) => (
              <li key={card.key}>{renderCard(card)}</li>
            ))}
          </ul>
          {more.length > 0 && (
            <>
              <FoldButton open={showMore} onToggle={() => setShowMore((v) => !v)}>
                {more.length} autre{more.length > 1 ? "s" : ""}
              </FoldButton>
              {showMore && (
                <ul className="space-y-2">
                  {more.map((card) => (
                    <li key={card.key}>{renderCard(card)}</li>
                  ))}
                </ul>
              )}
            </>
          )}
        </>
      )}

      {offerPast && (
        <>
          <FoldButton open={showPast} onToggle={() => setShowPast((v) => !v)} icon>
            {promptedIds.size > 0 ? "Autre séance précédente…" : "Suite d'une séance précédente…"}
          </FoldButton>
          {showPast &&
            (continuable?.failed ? (
              <LoadFailureNotice
                variant="inline"
                message="Séances passées non chargées."
                onRetry={continuable.reload}
              />
            ) : continuable?.acts == null ? (
              <p role="status" className="text-xs text-muted-foreground">Chargement…</p>
            ) : foldedPast.length === 0 ? (
              <p className="text-xs text-muted-foreground">
                {promptedIds.size > 0 ? "Aucune autre séance passée." : "Aucune séance passée pour ce patient."}
              </p>
            ) : (
              <ul className="space-y-2">
                {foldedPast.map((act) => (
                  <li key={act.actId}>
                    <PastCard act={act} onContinue={onContinue} disabled={disabled} />
                  </li>
                ))}
              </ul>
            ))}
        </>
      )}
    </section>
  )
}

function FoldButton({
  open,
  onToggle,
  icon = false,
  children,
}: {
  open: boolean
  onToggle: () => void
  icon?: boolean
  children: ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onToggle}
      aria-expanded={open}
      className="inline-flex min-h-9 items-center gap-1.5 text-xs font-medium text-muted-foreground hover-hover:hover:text-foreground coarse:min-h-11"
    >
      {icon && <History className="h-3.5 w-3.5" aria-hidden="true" />}
      {children}
      <ChevronDown className={cn("h-3.5 w-3.5 transition-transform", open && "rotate-180")} aria-hidden="true" />
    </button>
  )
}

/** « Couronne · dent 16 » — the act as the card names it, bold. */
function planCardName(suggestion: PlanStepSuggestion): string {
  const { item } = suggestion
  return `${item.designationFr}${teethSuffix(item.toothNumbers)}`
}

/**
 * One treatment: its name, its séances as dots, and ONE button naming the séance it books.
 *
 * <p>⚠️ The button label carries a free-text séance name, so it breaks rather than paints past the card
 * (`Button` is `whitespace-nowrap`, § 10.1).</p>
 *
 * <p>⚠️ `outline`, never the primary fill: « Créer le rendez-vous » is the dialog's one primary action, and a
 * full-width filled button per card competed with it. Same for `PastCard`'s « Continuer ».</p>
 *
 * <p>⚠️ A slot that passed with no fiche is still offered (47 of 318 patients were shown nothing when it was
 * not), and the date is stated: it may be a séance done and not written up, or one that never happened.</p>
 */
function PlanCard({
  suggestion,
  onAccept,
  disabled,
}: {
  suggestion: PlanStepSuggestion
  onAccept: (suggestion: PlanStepSuggestion) => void
  disabled: boolean
}) {
  const { item, step, passedSlotAt } = suggestion
  const name = planCardName(suggestion)
  const minutes = step?.estimatedDurationMinutes
  const label = step
    ? `Planifier : ${step.label}${minutes != null && minutes > 0 ? ` · ${formatDurationFr(minutes)}` : ""}`
    : "Planifier"

  return (
    <div className="space-y-2 rounded-md border bg-card p-2.5">
      <div className="flex flex-wrap items-center justify-between gap-x-2 gap-y-1">
        <p className="min-w-0 text-sm font-semibold [overflow-wrap:anywhere]">{name}</p>
        <SeancePips steps={item.steps} />
      </div>
      {passedSlotAt && (
        <p className="text-2xs font-medium text-warning-ink">
          {step?.label ?? "Séance"} du {shortDay(passedSlotAt)} : pas de fiche
        </p>
      )}
      <Button
        type="button"
        size="sm"
        variant="outline"
        className="h-auto min-h-9 w-full gap-1 whitespace-normal py-1.5 coarse:min-h-11"
        disabled={disabled}
        onClick={() => onAccept(suggestion)}
        aria-label={step ? `Planifier ${quoteFr(step.label)} — ${name}` : `Planifier ${quoteFr(name)}`}
      >
        <CalendarPlus className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
        {label}
      </Button>
    </div>
  )
}

/** A séance already carried out that this RDV may continue — « Obturation · dent 26 · Non terminée le 12/09 ». */
function PastCard({
  act,
  onContinue,
  disabled,
}: {
  act: ContinuableActDto
  onContinue: (act: ContinuableActDto) => void
  disabled: boolean
}) {
  const name = `${act.procedureName}${teethSuffix(act.toothNumbers)}`
  const day = shortDay(act.interventionDate)
  return (
    <div data-continuable-act className="space-y-2 rounded-md border bg-card p-2.5">
      <div className="flex flex-wrap items-center justify-between gap-x-2 gap-y-1">
        <p className="min-w-0 text-sm font-semibold [overflow-wrap:anywhere]">{name}</p>
        {act.isUnfinished ? (
          <span className="shrink-0 rounded-md bg-warning-wash px-2 py-0.5 text-2xs font-semibold text-warning-ink">
            Non terminée le {day}
          </span>
        ) : (
          <span className="shrink-0 text-2xs text-muted-foreground">le {day}</span>
        )}
      </div>
      <Button
        type="button"
        size="sm"
        variant="outline"
        className="min-h-9 w-full coarse:min-h-11"
        disabled={disabled}
        onClick={() => onContinue(act)}
        aria-label={`Continuer ${quoteFr(name)} du ${day}`}
      >
        Continuer
      </Button>
    </div>
  )
}

/** « 12/09 », with the year only when it is not this one. */
function shortDay(iso: string): string {
  try {
    const d = parseISO(iso)
    return format(d, d.getFullYear() === new Date().getFullYear() ? "dd/MM" : "dd/MM/yy")
  } catch {
    return ""
  }
}
