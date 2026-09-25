"use client"

import * as React from "react"
import { Check } from "lucide-react"
import { format, parseISO } from "date-fns"
import { fr } from "date-fns/locale"
import { cn } from "@/lib/utils"

/**
 * The one picture of a multi-séance act — « ✓ Préparation · ● Empreinte · ○ Scellement » — drawn the same way on
 * the booking dialog, the fiche de soins, the treatment page and the patient file.
 *
 * <p>It replaced seven counters (« 0 séance sur 3 faite », « 1 étape faite sur 3 », « 2/5 séances · 1/2 actes »,
 * « Cette séance : étape 2 sur 3 · … », a bare « 0 / 3 faite »…). Each séance says what IS true of it, in words
 * beside the dot: « faite le 12/09 » · « prévue le 28/09 » · « à enregistrer » · « à planifier ». Never a rank
 * and never a bare fraction — see `never-state-the-next-step-as-a-fact`.</p>
 *
 * <p>⚠️ Renders nothing for an act with no séances: a one-sitting act must look exactly as it did.</p>
 */

/** The minimum a step needs to be drawn. `TreatmentPlanItemStepDto` satisfies it, and so does a protocol row. */
export interface SeanceStripStep {
  id: string
  label: string
  doneDate?: string | null
  scheduledAt?: string | null
  earliestOn?: string | null
  /** Replaces the computed caption — « +7 j » on a protocol not yet booked. */
  note?: string | null
}

export type SeanceState = "done" | "booked" | "to-record" | "todo"

export function seanceState(step: SeanceStripStep, now: Date = new Date()): SeanceState {
  if (step.doneDate) return "done"
  if (step.scheduledAt) return new Date(step.scheduledAt).getTime() > now.getTime() ? "booked" : "to-record"
  return "todo"
}

function shortDay(iso: string, now: Date): string {
  try {
    const d = parseISO(iso)
    return format(d, d.getFullYear() === now.getFullYear() ? "dd/MM" : "dd/MM/yy", { locale: fr })
  } catch {
    return ""
  }
}

/** A séance's day in the strip's own short form (« 16/09 », « 16/09/25 » another year) — for its popover and dialog. */
export function seanceDay(iso: string | null | undefined, now: Date = new Date()): string {
  return iso ? shortDay(iso, now) : ""
}

/** « dès le … » is said only while that day is still ahead — past it, the séance is simply bookable. */
export function seanceEarliestAhead(earliestOn: string | null | undefined, now: Date = new Date()): boolean {
  return Boolean(earliestOn) && new Date(earliestOn as string).getTime() > now.getTime()
}

/**
 * What is true of one séance, in the words the strip prints under it. `inactive` — a treatment that is no longer
 * running (arrêté, non réclamé, annulé, terminé): a séance nobody will book is « non faite », never « à planifier ».
 */
export function seanceCaption(step: SeanceStripStep, now: Date = new Date(), inactive = false): string {
  if (step.note) return step.note
  switch (seanceState(step, now)) {
    case "done":
      return `faite le ${shortDay(step.doneDate as string, now)}`
    case "booked":
      return `prévue le ${shortDay(step.scheduledAt as string, now)}`
    case "to-record":
      return "à enregistrer"
    default:
      if (inactive) return "non faite"
      return seanceEarliestAhead(step.earliestOn, now)
        ? `à planifier · dès le ${shortDay(step.earliestOn as string, now)}`
        : "à planifier"
  }
}

/**
 * One line naming where an act stands — for a card too small for the strip. States a fact or names the work
 * still to do (« Empreinte prévue le 28/09 », « Scellement à planifier », « Toutes les séances faites »).
 */
export function seanceSummary(steps: SeanceStripStep[] | undefined, now: Date = new Date()): string | null {
  if (!steps || steps.length === 0) return null
  const open = steps.filter((s) => !s.doneDate)
  if (open.length === 0) return "Toutes les séances faites"
  const recording = open.find((s) => seanceState(s, now) === "to-record")
  if (recording) return `${recording.label} à enregistrer`
  const booked = open.find((s) => seanceState(s, now) === "booked")
  if (booked) return `${booked.label} ${seanceCaption(booked, now)}`
  return `${open[0].label} à planifier`
}

const DOT: Record<SeanceState, string> = {
  done: "border-success bg-success text-white",
  booked: "border-primary bg-primary",
  "to-record": "border-warning-ink bg-warning-ink",
  todo: "border-muted-foreground/60 bg-background",
}

const CAPTION: Record<SeanceState, string> = {
  done: "text-success",
  booked: "font-semibold text-primary",
  "to-record": "font-semibold text-warning-ink",
  todo: "text-muted-foreground",
}

export interface SeanceStripProps {
  steps: SeanceStripStep[] | undefined
  /** The séance(s) this surface is ABOUT — the RDV being booked, the fiche being written. Drawn with a halo. */
  currentStepIds?: readonly string[] | null
  /** The words under a current séance — « ce RDV », « aujourd'hui ». */
  currentLabel?: string
  size?: "md" | "lg"
  /** Makes every séance a button. */
  onStepClick?: (step: SeanceStripStep) => void
  /**
   * Wraps one séance's button — e.g. in a `PopoverTrigger asChild`. Implies the séances are buttons. Return the
   * button unchanged to leave a séance plain.
   */
  wrapStep?: (step: SeanceStripStep, button: React.ReactElement) => React.ReactNode
  /** Accessible name of the whole list. */
  label?: string
  /** A control after the last séance (« + » to add one). */
  trailing?: React.ReactNode
  /** The treatment is no longer running — see {@link seanceCaption}. */
  inactive?: boolean
  className?: string
  now?: Date
}

/** Which edges of the scroller hide séances — measured, so the fade appears only when there is something past it. */
type Overflow = { start: boolean; end: boolean }

const FADE = "2rem"
const MASK: Record<"end" | "start" | "both", string> = {
  end: `linear-gradient(to right, #000 calc(100% - ${FADE}), transparent)`,
  start: `linear-gradient(to left, #000 calc(100% - ${FADE}), transparent)`,
  both: `linear-gradient(to right, transparent, #000 ${FADE}, #000 calc(100% - ${FADE}), transparent)`,
}

/** The fade that says « more séances this way », measured on the scroller with a ResizeObserver. */
function useOverflowFade(count: number) {
  const ref = React.useRef<HTMLDivElement | null>(null)
  const [overflow, setOverflow] = React.useState<Overflow>({ start: false, end: false })

  const measure = React.useCallback(() => {
    const el = ref.current
    if (!el) return
    const scrollable = el.scrollWidth - el.clientWidth > 1
    const next = {
      start: scrollable && el.scrollLeft > 1,
      end: scrollable && el.scrollLeft + el.clientWidth < el.scrollWidth - 1,
    }
    setOverflow((prev) => (prev.start === next.start && prev.end === next.end ? prev : next))
  }, [])

  React.useEffect(() => {
    const el = ref.current
    if (!el) return
    measure()
    if (typeof ResizeObserver === "undefined") return
    const observer = new ResizeObserver(measure)
    observer.observe(el)
    if (el.firstElementChild) observer.observe(el.firstElementChild)
    return () => observer.disconnect()
  }, [measure, count])

  const mask = overflow.start && overflow.end ? MASK.both : overflow.end ? MASK.end : overflow.start ? MASK.start : null
  const style: React.CSSProperties | undefined = mask ? { maskImage: mask, WebkitMaskImage: mask } : undefined
  return { ref, onScroll: measure, style }
}

export function SeanceStrip({
  steps,
  currentStepIds,
  currentLabel,
  size = "md",
  onStepClick,
  wrapStep,
  label = "Séances",
  trailing,
  inactive = false,
  className,
  now: nowProp,
}: SeanceStripProps) {
  const now = nowProp ?? new Date()
  const fade = useOverflowFade(steps?.length ?? 0)
  if (!steps || steps.length === 0) return null

  const interactive = Boolean(onStepClick || wrapStep)

  return (
    <div className={cn("flex min-w-0 items-start gap-2", className)}>
      {/*
        ⚠️ Every séance keeps a real width (`min-w-*`) and the list scrolls in its own box whenever that does not
        fit — at any count. With `min-w-0` a narrow box let « Préparation » run into « Empreinte ». `-m-1 p-1` so
        the scroller does not clip the current séance's halo or a focus ring.
      */}
      <div ref={fade.ref} onScroll={fade.onScroll} style={fade.style} className="-m-1 min-w-0 flex-1 overflow-x-auto p-1">
        <ol aria-label={label} className="flex min-w-0 justify-start">
          {steps.map((step, index) => {
            const state = seanceState(step, now)
            const current = currentStepIds?.includes(step.id) ?? false
            const caption = current && currentLabel ? currentLabel : seanceCaption(step, now, inactive)
            const last = index === steps.length - 1

            const body = (
              <>
                <span
                  aria-hidden="true"
                  className={cn(
                    "relative z-[1] flex flex-none items-center justify-center rounded-full border-2",
                    size === "lg" ? "size-5" : "size-4",
                    current && state !== "done" ? "border-primary bg-primary" : DOT[state],
                    current && "ring-4 ring-primary/25",
                  )}
                >
                  {state === "done" && (
                    <Check className={size === "lg" ? "size-3" : "size-2.5"} strokeWidth={4} />
                  )}
                </span>
                <span className="mt-1.5 block min-w-0 break-words text-start">
                  <span
                    className={cn(
                      "block font-semibold leading-tight text-foreground",
                      size === "lg" ? "text-sm" : "text-xs",
                      interactive && "underline decoration-dotted decoration-muted-foreground underline-offset-4",
                    )}
                  >
                    {step.label}
                  </span>
                  <span
                    className={cn(
                      "block leading-tight",
                      size === "lg" ? "text-xs" : "text-2xs",
                      current ? "font-semibold text-primary" : CAPTION[state],
                    )}
                  >
                    {caption}
                  </span>
                </span>
              </>
            )

            const button = (
              <button
                type="button"
                onClick={onStepClick ? () => onStepClick(step) : undefined}
                aria-label={`${step.label} — ${caption}`}
                className="flex w-full min-w-0 flex-col items-start rounded-md pe-3 text-start outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background coarse:min-h-11"
              >
                {body}
              </button>
            )

            return (
              <li
                key={step.id}
                className={cn("relative flex-1", size === "lg" ? "min-w-[5.75rem] sm:min-w-[6.75rem]" : "min-w-[5.75rem]")}
                aria-current={current ? "step" : undefined}
              >
                {!last && (
                  <span
                    aria-hidden="true"
                    className={cn(
                      "absolute end-1 h-0.5 rounded-full",
                      size === "lg" ? "start-6 top-[9px]" : "start-5 top-[7px]",
                      state === "done" ? "bg-success" : "bg-border",
                    )}
                  />
                )}
                {interactive ? (
                  (wrapStep ? wrapStep(step, button) : button)
                ) : (
                  <div className="flex min-w-0 flex-col items-start pe-3">{body}</div>
                )}
              </li>
            )
          })}
        </ol>
      </div>
      {trailing}
    </div>
  )
}

/**
 * The strip folded to its dots, for a card or a table row. Always followed by words — the dots alone are
 * decoration (`aria-hidden`), and whatever sits beside them carries the meaning.
 */
export function SeancePips({
  steps,
  className,
  now: nowProp,
}: {
  steps: SeanceStripStep[] | undefined
  className?: string
  now?: Date
}) {
  const now = nowProp ?? new Date()
  if (!steps || steps.length === 0) return null
  return (
    <span aria-hidden="true" className={cn("inline-flex flex-none items-center gap-1", className)}>
      {steps.map((step) => (
        <i key={step.id} className={cn("block size-2.5 rounded-full border-[1.5px]", DOT[seanceState(step, now)])} />
      ))}
    </span>
  )
}
