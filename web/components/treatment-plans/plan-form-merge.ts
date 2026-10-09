import type { TreatmentPlanDto } from "@/lib/api/types"
import { parseAmountInput, roundMillimes } from "@/lib/format"
import { mergeSections } from "@/lib/forms/form-merge"
import { planInstallmentRows, planLinesFromPlan, type PlanInstallmentRow, type PlanLineRow } from "./plan-amend-payload"

/**
 * A devis, reconciled three ways by « Recharger » (`lib/forms/form-merge.ts`) — the editor's and the workspace's.
 *
 * Sections: the title, the notes, the échéancier, and **each existing act on its own** (`act:<id>`): two people
 * correcting two different acts of one devis is the ordinary case, and neither should lose the other's price. An act
 * the colleague added appears, one they removed goes; a line added on this screen (no id yet) is always kept.
 */
export interface PlanFormValues {
  title: string
  notes: string
  lines: PlanLineRow[]
  installments: PlanInstallmentRow[]
}

export const isActSection = (section: string) => section.startsWith("act:")
export const actIdOf = (section: string) => section.slice(4)

const amount = (typed: string) => {
  const value = parseAmountInput(typed)
  return Number.isFinite(value) ? roundMillimes(value) : typed.trim()
}

/** One act as the save sends it — derived and display-only fields (candidates, diagnosis, touched flags) left out. */
function actSnapshot(line: PlanLineRow): string {
  return JSON.stringify({
    procedureTypeId: line.procedureTypeId,
    designation: line.designationFr.trim(),
    cost: amount(line.plannedCost),
    discount: line.discount ?? 0,
    teeth: [...line.toothNumbers].sort((a, b) => a - b),
    steps: (line.steps ?? [])
      .filter((s) => s.include)
      .map((s) => [s.id, s.label.trim(), (s.duration ?? "").trim(), (s.interval ?? "").trim()]),
  })
}

function snapshot(values: PlanFormValues, sections: readonly string[]): Record<string, string> {
  const out: Record<string, string> = {
    title: values.title.trim(),
    notes: values.notes.trim(),
    schedule: JSON.stringify(values.installments.map((r) => [r.id, r.dueDate, amount(r.amount)])),
  }
  for (const section of sections.filter(isActSection)) {
    const line = values.lines.find((l) => l.id === actIdOf(section))
    out[section] = line ? actSnapshot(line) : ""
  }
  return out
}

/** A stored devis as the editor shows it. `title` is empty over a placeholder, as the editor opens it. */
export function planFormValuesOf(plan: TreatmentPlanDto, isPlaceholderTitle: (t: string) => boolean): PlanFormValues {
  return {
    title: isPlaceholderTitle(plan.title) ? "" : plan.title,
    notes: plan.notes ?? "",
    lines: planLinesFromPlan(plan),
    installments: planInstallmentRows(plan),
  }
}

export interface PlanMergeResult {
  values: PlanFormValues
  /** The sections taken over a change made on this screen — named to the user. */
  takenOver: string[]
}

/**
 * Merges the server's copy into the screen. Untouched sections follow the server; a section only this screen changed
 * stays; both → theirs, named. The line order is the screen's, with acts the colleague added appended.
 */
export function mergePlanForm(opened: PlanFormValues, onScreen: PlanFormValues, server: PlanFormValues): PlanMergeResult {
  const ids = new Set<string>()
  for (const v of [opened, onScreen, server]) for (const l of v.lines) if (l.id) ids.add(l.id)
  const sections = ["title", "notes", "schedule", ...[...ids].map((id) => `act:${id}`)]
  const { take, takenOver } = mergeSections(
    sections,
    snapshot(opened, sections),
    snapshot(onScreen, sections),
    snapshot(server, sections),
  )
  const taken = new Set(take)
  let lines = onScreen.lines
  for (const section of take.filter(isActSection)) {
    const id = actIdOf(section)
    const theirs = server.lines.find((l) => l.id === id)
    const at = lines.findIndex((l) => l.id === id)
    if (!theirs) lines = lines.filter((l) => l.id !== id)
    else if (at >= 0) lines = lines.map((l, i) => (i === at ? theirs : l))
    else lines = [...lines, theirs]
  }
  return {
    values: {
      title: taken.has("title") ? server.title : onScreen.title,
      notes: taken.has("notes") ? server.notes : onScreen.notes,
      lines,
      installments: taken.has("schedule") ? server.installments : onScreen.installments,
    },
    takenOver,
  }
}

/** The French name of a section, for the « modifié entre-temps » line. */
export function planSectionLabel(section: string, values: PlanFormValues, server: PlanFormValues): string {
  if (section === "title") return "Titre"
  if (section === "notes") return "Notes"
  if (section === "schedule") return "Échéancier"
  const id = actIdOf(section)
  const line = server.lines.find((l) => l.id === id) ?? values.lines.find((l) => l.id === id)
  return line?.designationFr.trim() || "Un acte"
}
