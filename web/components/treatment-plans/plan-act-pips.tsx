"use client"

import type { TreatmentPlanItemDto } from "@/lib/api/types"
import { cn } from "@/lib/utils"
import { planItemState, type PlanItemState } from "./plan-next-action"
import { itemWorkflowInk } from "./treatment-plan-labels"
import { PlanProgressBar } from "./plan-progress-bar"

/**
 * Past this many acts the pips stop being readable — a row of eighteen 9px dots is a smear, not a count — so the
 * component falls back to the shared thin bar plus the fraction. The threshold lives here rather than at the call
 * site so every surface that shows pips switches over at the same point.
 */
const MAX_PIPS = 12

interface PlanActPipsProps {
  items: TreatmentPlanItemDto[]
  /** Acts done / total, from the DTO's own derived counters rather than recounted here. */
  done: number
  total: number
  className?: string
}

/**
 * One pip per planned act, coloured by its derived état.
 *
 * <p>This replaced a percentage bar on the patient page, for a reason worth keeping: a pip row encodes **four**
 * states — réalisé, séance planifiée, séance passée sans fiche, à planifier — in less width than a bar needs for
 * one. And at zero réalisé it still shows the plan's *shape* (how many acts, all waiting), which is exactly where
 * the bar degenerated into a full-width grey slab carrying no information at all.</p>
 *
 * <p><b>Acts stay in plan order</b>, not sorted by état. The API returns them by `sequenceNumber`, and that order
 * is information: a plan whose 1st and 3rd acts are done while the 2nd is not tells you something got skipped.
 * Sorting done-first would read as a tidier progress meter while destroying that.</p>
 *
 * <p>The pips are <b>decorative</b> (`aria-hidden`) and the fraction beside them carries the accessible value. That
 * is deliberate: the `role="progressbar"` this replaces had to special-case an empty plan because `aria-valuemax`
 * of 0 announces as undefined progress. A text fraction has no such edge.</p>
 */
export function PlanActPips({ items, done, total, className }: PlanActPipsProps) {
  if (total <= 0) return null

  // A long plan: hand over to the bar, which stays legible at any act count.
  if (items.length > MAX_PIPS) {
    return (
      <span className={cn("flex items-center gap-2", className)}>
        <PlanProgressBar done={done} total={total} className="w-24" />
        <span className="text-sm tabular-nums text-muted-foreground">
          {done}/{total} actes
        </span>
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
      <span className="text-sm tabular-nums text-muted-foreground">
        {/* « actes » agrees with the noun. The card this replaces pluralised « réalisé » on `itemsDone`, so a
            plan at 0/2 printed « 0/2 actes réalisé ». The word is dropped here for width; the full phrase is
            below, for screen readers. */}
        {done}/{total} acte{total > 1 ? "s" : ""}
        <span className="sr-only"> réalisé{total > 1 ? "s" : ""}</span>
      </span>
    </span>
  )
}

/**
 * The **form** half of a pip — filled, thick ring, thin ring — so an état is distinguishable without colour and
 * the visual weight tracks how much attention it deserves: réalisé is solid, the two live états carry a full
 * ring, and « à planifier » is the faintest outline.
 *
 * <p>⚠️ The **colour** half is deliberately not here. It comes from `itemWorkflowInk`, which reads the same
 * `ITEM_WORKFLOW_TONE` the badges do, so one état has exactly one colour across the whole plan area. This map
 * used to carry both, and the two drifted on all four états — including a hard-coded `oklch(0.77 0.16 70)` for
 * « à enregistrer » that matched no token and would have been left behind by any palette edit.</p>
 *
 * <p>Still an inline `boxShadow` rather than a Tailwind arbitrary value: the ring widths differ per état, and a
 * `shadow-[inset_0_0_0_2px_var(--warning-ink)]` built by interpolation is a class Tailwind never sees in the
 * source scan — it renders as *nothing*, i.e. an invisible pip rather than an obviously broken one.</p>
 */
const PIP_RING: Record<PlanItemState, number | "fill"> = {
  done: "fill",
  scheduled: 2,
  // The séance has passed with no fiche — the one état that is overdue rather than merely pending, which is why
  // its tone is `active` and its ink the workflow badge's amber.
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
          ["done", "réalisé"],
          ["scheduled", "planifié"],
          ["to-record", "fiche à saisir"],
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
