"use client"

import { ArrowDown, ArrowUp, Minimize2, Plus, RotateCcw, Trash2 } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import type { ProcedureStepTemplateDto } from "@/lib/api/types"
import { cn } from "@/lib/utils"

/**
 * The clinical limits, mirrored from the aggregate so the form refuses before the server has to.
 * `TreatmentPlanItemStep.MaxStepsPerItem` / `MaxLabelLength` / `MaxDaysBetweenSteps`.
 */
export const MAX_PROTOCOL_STEPS = 12
export const MAX_STEP_LABEL = 120
const MAX_DAYS_BETWEEN = 1095

/**
 * « Les séances de ce traitement » — the protocol of an act being booked, edited in place, **for this patient
 * only**.
 *
 * <p>A dentist's own words: « sometimes the steps vary depending on patient ». The catalogue protocol is a
 * proposal (`ProcedureType.DefaultSteps` says so in as many words: « le catalogue *propose*, le devis
 * *possède* »), and until this existed the only way to vary it was to book the visit, open the devis
 * workspace, find the act and edit its steps there — three screens after the decision had been made.</p>
 *
 * <p>⚠️ <b>Nothing here touches the catalogue.</b> The list is form state; it reaches the server as
 * `StartTreatmentCommand.Steps`, which copies it onto the treatment and never writes back to
 * `ProcedureType.DefaultSteps`. « Rétablir le protocole » re-reads the catalogue — it does not save to it.</p>
 *
 * <p>⚠️ <b>Two different quantities, and conflating them is a real defect.</b> « min » is chair time and sizes
 * the appointment; « j » is the minimum wait after the previous séance and decides when it is *due*. An
 * implant's osseointegration is 8–12 weeks of the second and zero of the first — `RecallWorklistRules` reads
 * the second, and an implant missing it reads as abandoned after a flat fortnight.</p>
 *
 * <p>⚠️ Its own component rather than a section of the picker: the edit dialog offers the same list, and the
 * one thing this repository reliably produces is a rule wired to one of its two call sites.</p>
 */
export function AppointmentProtocolEditor({
  steps,
  onChange,
  onReset,
  canReset = false,
  onSingleSeance,
  disabled = false,
  idPrefix,
  actName,
}: {
  steps: ProcedureStepTemplateDto[]
  onChange: (next: ProcedureStepTemplateDto[]) => void
  /** Put the catalogue protocol back. Absent when the act has none to go back to. */
  onReset?: () => void
  canReset?: boolean
  /**
   * Give up on the séances entirely — the act is done in one sitting.
   *
   * ⚠️ It lives **here**, at the foot of the list, and not in the row of controls above while this editor is
   * open. There it sat immediately beside the control that closes the editor, so it read as the « valider »
   * for the séances the dentist had just typed — one press from collapsing the whole treatment into a single
   * visit, reported from use. Among the list's own operations it reads as what it is.
   */
  onSingleSeance?: () => void
  disabled?: boolean
  idPrefix: string
  /** Named in every control's accessible label — a row of « Supprimer » buttons is otherwise unnavigable. */
  actName: string
}) {
  const atCap = steps.length >= MAX_PROTOCOL_STEPS

  const patch = (index: number, next: Partial<ProcedureStepTemplateDto>) =>
    onChange(steps.map((s, i) => (i === index ? { ...s, ...next } : s)))

  const move = (index: number, delta: number) => {
    const to = index + delta
    if (to < 0 || to >= steps.length) return
    const next = [...steps]
    const [row] = next.splice(index, 1)
    next.splice(to, 0, row)
    onChange(next)
  }

  const remove = (index: number) => onChange(steps.filter((_, i) => i !== index))

  const add = () => {
    if (atCap) return
    onChange([...steps, { label: "", durationMinutes: null, minDaysAfterPrevious: null }])
  }

  /** `""` clears the figure; anything unparseable is ignored rather than written as 0. */
  const readNumber = (raw: string, max: number): number | null => {
    const trimmed = raw.trim()
    if (!trimmed) return null
    const n = Number.parseInt(trimmed, 10)
    if (!Number.isFinite(n) || n <= 0) return null
    return Math.min(n, max)
  }

  return (
    <div className="mt-2 space-y-2">
      <ol className="space-y-2">
        {steps.map((step, index) => {
          const blank = step.label.trim().length === 0
          return (
            <li
              key={index}
              className="rounded-md border bg-background/70 p-2"
            >
              <div className="flex items-start gap-2">
                <span
                  className="mt-2 w-4 shrink-0 text-center text-2xs font-mono tabular-nums text-muted-foreground"
                  aria-hidden
                >
                  {index + 1}
                </span>
                {/* The label owns its own line: « Pose de la couronne définitive » is 30 characters and the
                    row has ~200 px at 320 px once the rank and the controls are out. */}
                <Input
                  id={`${idPrefix}-step-${index}`}
                  value={step.label}
                  maxLength={MAX_STEP_LABEL}
                  onChange={(e) => patch(index, { label: e.target.value })}
                  disabled={disabled}
                  aria-invalid={blank}
                  aria-label={`Séance ${index + 1} de ${actName}`}
                  placeholder={`Séance ${index + 1}`}
                  className={cn("h-8 min-w-0 flex-1 text-xs coarse:h-11", blank && "border-destructive")}
                />
              </div>

              <div className="mt-1.5 flex flex-wrap items-center gap-x-2 gap-y-1 ps-6">
                <label
                  className="flex items-center gap-1 text-2xs text-muted-foreground"
                  htmlFor={`${idPrefix}-step-${index}-min`}
                >
                  <Input
                    id={`${idPrefix}-step-${index}-min`}
                    type="text"
                    inputMode="numeric"
                    value={step.durationMinutes?.toString() ?? ""}
                    onChange={(e) => patch(index, { durationMinutes: readNumber(e.target.value, 600) })}
                    disabled={disabled}
                    aria-label={`Durée de la séance ${index + 1} de ${actName}, en minutes`}
                    placeholder="—"
                    className="h-8 w-14 text-center text-xs tabular-nums coarse:h-11"
                  />
                  min au fauteuil
                </label>
                <label
                  className="flex items-center gap-1 text-2xs text-muted-foreground"
                  htmlFor={`${idPrefix}-step-${index}-days`}
                >
                  <Input
                    id={`${idPrefix}-step-${index}-days`}
                    type="text"
                    inputMode="numeric"
                    value={step.minDaysAfterPrevious?.toString() ?? ""}
                    onChange={(e) =>
                      patch(index, { minDaysAfterPrevious: readNumber(e.target.value, MAX_DAYS_BETWEEN) })
                    }
                    disabled={disabled || index === 0}
                    aria-label={`Délai minimum avant la séance ${index + 1} de ${actName}, en jours`}
                    placeholder="—"
                    className="h-8 w-14 text-center text-xs tabular-nums coarse:h-11"
                  />
                  {/* The first séance is this appointment, so « attendre N jours avant » has nothing to wait
                      after — the field is disabled rather than hidden, so the column does not jump. */}
                  {index === 0 ? "j après (1re séance)" : "j après la précédente"}
                </label>

                <div className="ms-auto flex shrink-0 items-center gap-0.5">
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="h-8 w-8 coarse:h-11 coarse:w-11"
                    disabled={disabled || index === 0}
                    onClick={() => move(index, -1)}
                    aria-label={`Monter la séance ${index + 1} de ${actName}`}
                  >
                    <ArrowUp className="h-3.5 w-3.5" />
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="h-8 w-8 coarse:h-11 coarse:w-11"
                    disabled={disabled || index === steps.length - 1}
                    onClick={() => move(index, 1)}
                    aria-label={`Descendre la séance ${index + 1} de ${actName}`}
                  >
                    <ArrowDown className="h-3.5 w-3.5" />
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="h-8 w-8 text-muted-foreground hover:text-destructive coarse:h-11 coarse:w-11"
                    disabled={disabled}
                    onClick={() => remove(index)}
                    aria-label={`Supprimer la séance ${index + 1} de ${actName}`}
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </Button>
                </div>
              </div>

              {blank && (
                <p className="ps-6 pt-1 text-2xs text-destructive">
                  Nommez cette séance, ou supprimez la ligne.
                </p>
              )}
            </li>
          )
        })}
      </ol>

      <div className="flex flex-wrap items-center gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="h-8 gap-1 text-2xs coarse:h-11"
          disabled={disabled || atCap}
          onClick={add}
        >
          <Plus className="h-3.5 w-3.5" />
          Ajouter une séance
        </Button>
        {canReset && onReset && (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            className="h-8 gap-1 text-2xs text-muted-foreground coarse:h-11"
            disabled={disabled}
            onClick={onReset}
          >
            <RotateCcw className="h-3.5 w-3.5" />
            Rétablir le protocole
          </Button>
        )}
        {onSingleSeance && (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            className="h-8 gap-1 text-2xs text-muted-foreground coarse:h-11"
            disabled={disabled}
            onClick={onSingleSeance}
          >
            <Minimize2 className="h-3.5 w-3.5" />
            Tout faire en une séance
          </Button>
        )}
        {atCap && (
          <span className="text-2xs text-muted-foreground">
            {MAX_PROTOCOL_STEPS} séances au maximum.
          </span>
        )}
      </div>

      {/*
        ⚠️ It says there is nothing to validate, because the control that closes this list looked like a
        « valider » until it said otherwise — and the one beside it, which collapses the treatment into a
        single visit, looked like the confirm. There is no save on this list: the booking's own button is.
      */}
      <p className="text-2xs leading-relaxed text-muted-foreground">
        Les modifications sont prises en compte tout de suite — rien à valider ici. Ces séances ne valent que
        pour ce patient&nbsp;: le protocole de l&apos;acte dans le catalogue ne change pas.
      </p>
    </div>
  )
}
