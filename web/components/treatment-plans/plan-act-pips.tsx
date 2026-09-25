"use client"

import type { TreatmentPlanDto, TreatmentPlanItemDto } from "@/lib/api/types"
import { cn } from "@/lib/utils"
import { isPlanLive, planItemState, planSeanceProgress, type PlanItemState } from "./plan-next-action"
import { itemWorkflowInk } from "./treatment-plan-labels"
import { PlanProgressBar } from "./plan-progress-bar"

/**
 * Past this many acts the pips stop being readable — a row of eighteen 9px dots is a smear, not a count — so the
 * component falls back to the shared thin bar plus the count. The threshold lives here rather than at the call
 * site so every surface that shows pips switches over at the same point.
 */
const MAX_PIPS = 12

/**
 * A séance count in words — « 3 séances à faire » · « 2 séances sur 5 faites » · « Toutes les séances faites ».
 *
 * <p>⚠️ Never a bare « 2 / 5 »: a fraction is read as progress by some and as « the next one » by others (N31).
 * Zero done is said as the work still to do, and all done as a fact, so no line ever reads « 0 séance sur 3
 * faite ».</p>
 *
 * <p>`closed` — a treatment no longer running (arrêté, non réclamé, annulé, terminé) counts only what was DONE:
 * « Aucune séance faite », never « 3 séances à faire » of work nobody will book.</p>
 */
export function seanceCountLabel(done: number, total: number, closed = false): string | null {
  if (total <= 0) return null
  if (done <= 0) return closed ? "Aucune séance faite" : `${total} séance${total > 1 ? "s" : ""} à faire`
  if (done >= total) return total === 1 ? "1 séance faite" : "Toutes les séances faites"
  return `${done} séance${done > 1 ? "s" : ""} sur ${total} faite${done > 1 ? "s" : ""}`
}

/** {@link seanceCountLabel} for a whole plan, counted by `planSeanceProgress` (a step-less act is one séance). */
export function planSeanceCountLabel(plan: TreatmentPlanDto): string | null {
  const { done, total } = planSeanceProgress(plan)
  return seanceCountLabel(done, total, !isPlanLive(plan.status))
}

interface PlanActPipsProps {
  items: TreatmentPlanItemDto[]
  /** Acts done / total, from the DTO's own derived counters rather than recounted here. */
  done: number
  total: number
  /**
   * The plan the acts belong to, so the words count **séances** — an act only becomes `Done` when its last step
   * lands, so an act count printed « 0 acte » for the whole life of a six-visit treatment. Optional, so a caller
   * with only the items still gets words (counted in acts).
   */
  plan?: TreatmentPlanDto
  className?: string
}

/**
 * One pip per planned act, coloured by its derived état, then the count in words.
 *
 * <p>A pip row encodes four états — fait, prévu, à enregistrer, à planifier — in less width than a bar needs for
 * one, and at zero done it still shows the plan's shape. <b>Acts stay in plan order</b>, not sorted by état: a
 * plan whose 1st and 3rd acts are done while the 2nd is not tells you something got skipped.</p>
 *
 * <p>The pips are decorative (`aria-hidden`); the words beside them carry the meaning.</p>
 */
export function PlanActPips({ items, done, total, plan, className }: PlanActPipsProps) {
  if (total <= 0) return null

  const seances = plan ? planSeanceProgress(plan) : null
  const words =
    seances && seances.total > 0
      ? seanceCountLabel(seances.done, seances.total, plan ? !isPlanLive(plan.status) : false)
      : actCountLabel(done, total)

  // A long plan: hand over to the bar, which stays legible at any act count.
  if (items.length > MAX_PIPS) {
    return (
      <span className={cn("flex items-center gap-2", className)}>
        <PlanProgressBar done={done} total={total} className="w-24" />
        <span className="text-sm text-muted-foreground">{words}</span>
      </span>
    )
  }

  return (
    <span className={cn("flex items-center gap-1.5", className)}>
      <span className="flex items-center gap-1" aria-hidden="true">
        {items.map((item) => (
          <i
            key={item.id}
            className="h-2.5 w-2.5 shrink-0 rounded-full"
            style={pipStyle(planItemState(item))}
          />
        ))}
      </span>
      <span className="text-sm text-muted-foreground">{words}</span>
    </span>
  )
}

/** The act-count twin of {@link seanceCountLabel}, for a caller that has no plan to count séances on. */
function actCountLabel(done: number, total: number): string {
  if (done <= 0) return `${total} acte${total > 1 ? "s" : ""} à faire`
  if (done >= total) return total === 1 ? "1 acte fait" : "Tous les actes faits"
  return `${done} acte${done > 1 ? "s" : ""} sur ${total} fait${done > 1 ? "s" : ""}`
}

/**
 * The **form** half of a pip — filled, thick ring, thin ring — so an état is distinguishable without colour and
 * the visual weight tracks how much attention it deserves: fait is solid, the two live états carry a full ring,
 * and « à planifier » is the faintest outline.
 *
 * <p>⚠️ The **colour** half is deliberately not here. It comes from `itemWorkflowInk`, which reads the same
 * `ITEM_WORKFLOW_TONE` the badges do, so one état has exactly one colour across the whole plan area.</p>
 *
 * <p>Still an inline `boxShadow` rather than a Tailwind arbitrary value: the ring widths differ per état, and a
 * `shadow-[inset_0_0_0_2px_var(--warning-ink)]` built by interpolation is a class Tailwind never sees in the
 * source scan — it renders as *nothing*, i.e. an invisible pip rather than an obviously broken one.</p>
 */
const PIP_RING: Record<PlanItemState, number | "fill"> = {
  done: "fill",
  scheduled: 2,
  // The séance has passed with no fiche — the one état that is overdue rather than merely pending.
  "to-record": 2,
  "to-schedule": 1.5,
}

function pipStyle(state: PlanItemState): React.CSSProperties {
  const ink = itemWorkflowInk(state)
  const ring = PIP_RING[state]
  return ring === "fill" ? { backgroundColor: ink } : { boxShadow: `inset 0 0 0 ${ring}px ${ink}` }
}

/** The pip legend, for surfaces that show pips without the workspace's own état badges beside them. */
export function PlanActPipsLegend({ className }: { className?: string }) {
  return (
    <span className={cn("flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground", className)}>
      {(
        [
          ["done", "fait"],
          ["scheduled", "prévu"],
          ["to-record", "à enregistrer"],
          ["to-schedule", "à planifier"],
        ] as [PlanItemState, string][]
      ).map(([state, label]) => (
        <span key={state} className="flex items-center gap-1.5">
          <i className="h-2.5 w-2.5 shrink-0 rounded-full" style={pipStyle(state)} aria-hidden="true" />
          {label}
        </span>
      ))}
    </span>
  )
}
