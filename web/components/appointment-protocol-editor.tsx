"use client"

import { useState } from "react"
import { ArrowDown, ArrowUp, ChevronRight, Minimize2, Plus, RotateCcw, Trash2 } from "lucide-react"
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
 * « Les séances de ce traitement » — the protocol of an act being booked, **always on screen**, one line per
 * séance, editable in place, **for this patient only**.
 *
 * <p>A dentist's own words: « sometimes the steps vary depending on patient ». The catalogue protocol is a
 * proposal (`ProcedureType.DefaultSteps` says so in as many words: « le catalogue *propose*, le devis
 * *possède* »), and until this existed the only way to vary it was to book the visit, open the devis
 * workspace, find the act and edit its steps there — three screens after the decision had been made.</p>
 *
 * <h4>⚠️ The list is information; only a ROW is a form</h4>
 * <p>The first version was a form: every séance rendered its name, its chair time and its interval as three
 * stacked fields at once, behind a « Modifier les séances » disclosure. That cost <b>515 px open at 1440 px
 * and 843 px at 390</b> — taller than the phone it was drawn on — for an act of three séances; a six-séance
 * prothèse came to over a thousand. And the disclosure itself was the reported defect: with the list open the
 * row read « Terminer · Tout faire en une séance », so the exit looked like a validation of the séances just
 * typed and the control beside it looked like the confirm.</p>
 *
 * <p>So the whole list is now a <b>readable frise</b> — pip, name, « + 7 j », « 30 min » — and tapping one row
 * opens <i>that</i> row into exactly the editor the whole list used to be. Three séances with one open is
 * ≈ 190 px; six is ≈ 300. There is no list-level mode left, so nothing can look like a « valider », and
 * <b>every one of the nine operations survives</b> (rename · minutes · days · up · down · delete · add ·
 * reset · one-séance) — see the row's own controls and the footer.</p>
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
   * ⚠️ **Full width, at the foot, and permanently on screen.** It used to appear and disappear with the list's
   * open state, and while the list was open it sat immediately beside the control that closed it — so it read
   * as the « valider » for the séances just typed, one press from collapsing the whole treatment into a single
   * visit. Reported from use. It is the only other decision this card offers, so it is drawn as one.
   */
  onSingleSeance?: () => void
  disabled?: boolean
  idPrefix: string
  /** Named in every control's accessible label — a row of « Supprimer » buttons is otherwise unnavigable. */
  actName: string
}) {
  /**
   * Which séance is open for editing — **one at a time, and none on arrival**.
   *
   * <p>Held here rather than lifted: it is presentation, the host has no decision to make about it, and the
   * only channel back to the host is `onChange`, whose edit-dialog implementation also resets
   * `durationTouched` (see `resolvePlannedProtocols`). An index rather than an id because a séance being
   * typed has no identity yet; a reorder or a delete closes the row rather than following it, which is the
   * honest behaviour — after « descendre » the row under the finger is a different séance.</p>
   */
  const [openIndex, setOpenIndex] = useState<number | null>(null)
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
    setOpenIndex(null)
  }

  const remove = (index: number) => {
    onChange(steps.filter((_, i) => i !== index))
    setOpenIndex(null)
  }

  const add = () => {
    if (atCap) return
    onChange([...steps, { label: "", durationMinutes: null, minDaysAfterPrevious: null }])
    // Open the new row: it is blank, it is invalid until named, and the whole point of pressing « Séance » is
    // to type into it.
    setOpenIndex(steps.length)
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
      <ol className="overflow-hidden rounded-md border bg-background/70">
        {steps.map((step, index) => {
          const blank = step.label.trim().length === 0
          const open = openIndex === index
          const first = index === 0

          return (
            <li key={index} className={cn("border-b last:border-b-0", open && "bg-primary/[0.04]")}>
              {/*
                The row at rest. A real button, so Enter and Space open it and a screen reader is told it is a
                disclosure — and so the whole line is the target rather than a chevron a thumb has to find.
              */}
              <button
                type="button"
                disabled={disabled}
                onClick={() => setOpenIndex(open ? null : index)}
                aria-expanded={open}
                className="flex min-h-9 w-full flex-wrap items-center gap-x-2 gap-y-1 px-2 py-1.5 text-start hover:bg-accent/40 coarse:min-h-11"
              >
                {/*
                  ⚠️ **The name takes the whole row below `sm:`, and the two figures drop to a second line.**
                  Both halves of that were eye-pass findings at 320 px, in order: with `truncate` the list read
                  « Empreï… · Rapport… · Essai de… · Contrôl… », and the name IS the row's only information
                  (§ 10.1 — never truncate, the label is the control's name); wrapping alone then left it a
                  ~50 px column breaking « Rapports intermaxi/llaires » and « Contrôle et retouche/s » over four
                  lines, because the chips and the chevron are `flex-none` and take ~120 px of a 290 px card.

                  ⚠️ `grow basis-full sm:basis-0`, never `flex-1`: `flex-1` is `flex: 1 1 0%` and that 0 basis
                  beats a `basis-full` beside it, so the wrap would never trigger — the trap this repo has
                  already paid for twice (`subscription-banner`, `list-toolbar`).
                */}
                <span className="flex min-w-0 grow basis-full items-center gap-2 sm:basis-0">
                  <span
                    aria-hidden
                    className={cn(
                      "size-2.5 flex-none rounded-full border-[1.5px]",
                      first ? "border-dashed border-primary" : "border-border",
                    )}
                  />
                  {/* `break-words`, not `[overflow-wrap:anywhere]`: the latter takes the min-content width to a
                      single character, which is what broke `plan-act-row` into a column of letters. */}
                  <span className={cn("min-w-0 break-words text-xs", blank && "italic text-destructive")}>
                    {blank ? `Séance ${index + 1} — à nommer` : step.label}
                    {/* Only the first séance is dated, because only it is this appointment. */}
                    {first && !blank && (
                      <span className="text-muted-foreground"> · ce rendez-vous</span>
                    )}
                  </span>
                </span>
                {/*
                  The two figures stay READABLE while the row is shut — that is what makes the frise worth
                  having. They are not controls here: opening the row is what edits them, so rendering them as
                  chips a finger can miss would be a second, weaker route to the same fields.
                */}
                <span className="ms-auto flex flex-none items-center gap-2">
                  {!first && step.minDaysAfterPrevious != null && (
                    <span className="rounded-sm bg-muted px-1.5 py-0.5 font-mono text-2xs tabular-nums text-muted-foreground">
                      + {step.minDaysAfterPrevious} j
                    </span>
                  )}
                  {step.durationMinutes != null && (
                    <span className="rounded-sm bg-muted px-1.5 py-0.5 font-mono text-2xs tabular-nums text-muted-foreground">
                      {step.durationMinutes} min
                    </span>
                  )}
                  <ChevronRight
                    aria-hidden
                    className={cn("size-3.5 text-muted-foreground transition-transform", open && "rotate-90")}
                  />
                </span>
              </button>

              {/*
                The opened row — the editor the whole list used to be, applied to one séance. Every control
                that existed before is here, in the same order, with the same rules.
              */}
              {open && (
                <div className="space-y-2 border-t bg-card px-2 pb-2 pt-2">
                  <Input
                    id={`${idPrefix}-step-${index}`}
                    value={step.label}
                    maxLength={MAX_STEP_LABEL}
                    onChange={(e) => patch(index, { label: e.target.value })}
                    disabled={disabled}
                    aria-invalid={blank}
                    aria-label={`Nom de la séance ${index + 1} de ${actName}`}
                    placeholder={`Séance ${index + 1}`}
                    autoFocus
                    className={cn("h-9 w-full text-xs md:text-sm", blank && "border-destructive")}
                  />

                  <div className="flex flex-wrap items-center gap-x-2 gap-y-1.5">
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
                        className="h-9 w-14 text-center text-xs tabular-nums md:text-sm"
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
                        disabled={disabled || first}
                        aria-label={`Délai minimum avant la séance ${index + 1} de ${actName}, en jours`}
                        placeholder="—"
                        className="h-9 w-14 text-center text-xs tabular-nums md:text-sm"
                      />
                      {/* The first séance is this appointment, so « attendre N jours avant » has nothing to wait
                          after — the field is disabled rather than hidden, so the column does not jump. */}
                      {first ? "j après (1re séance)" : "j après la précédente"}
                    </label>

                    {/* ⚠️ These three grow their own box (`coarse:size-11`) rather than taking `.touch-target`:
                        they sit a few pixels apart and the later sibling paints last, so a 44 px overlay would
                        send a thumb aimed at « monter » into « descendre » — or into « supprimer ». */}
                    <div className="ms-auto flex flex-none items-center gap-0.5">
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        className="size-8 coarse:size-11"
                        disabled={disabled || first}
                        onClick={() => move(index, -1)}
                        aria-label={`Monter la séance ${index + 1} de ${actName}`}
                      >
                        <ArrowUp className="h-3.5 w-3.5" />
                      </Button>
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        className="size-8 coarse:size-11"
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
                        className="size-8 text-muted-foreground hover:text-destructive coarse:size-11"
                        disabled={disabled}
                        onClick={() => remove(index)}
                        aria-label={`Supprimer la séance ${index + 1} de ${actName}`}
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </Button>
                    </div>
                  </div>

                  {blank && (
                    <p className="text-2xs text-destructive">Nommez cette séance, ou supprimez la ligne.</p>
                  )}
                </div>
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
          Séance
        </Button>
        {canReset && onReset && (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            className="h-8 gap-1 text-2xs text-muted-foreground coarse:h-11"
            disabled={disabled}
            onClick={() => {
              onReset()
              setOpenIndex(null)
            }}
          >
            <RotateCcw className="h-3.5 w-3.5" />
            Rétablir le protocole
          </Button>
        )}
        {atCap && (
          <span className="text-2xs text-muted-foreground">{MAX_PROTOCOL_STEPS} séances au maximum.</span>
        )}
        <span className="ms-auto text-2xs text-muted-foreground">
          Touchez une séance pour la modifier — pour ce patient seulement.
        </span>
      </div>

      {onSingleSeance && (
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="h-8 w-full gap-1 border-primary/50 text-2xs text-primary coarse:h-11"
          disabled={disabled}
          onClick={onSingleSeance}
        >
          <Minimize2 className="h-3.5 w-3.5" />
          Tout faire en une seule séance
        </Button>
      )}
    </div>
  )
}
