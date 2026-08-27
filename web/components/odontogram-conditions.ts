// Shared ToothCondition metadata — used by the read-only odontogram (patient tab) and the dental-record
// editor (where conditions are captured). Keep the enum names in sync with the backend ToothCondition.

export interface ConditionStyle {
  label: string
  /** Tooth "box" fill classes (odontogram cell). */
  box: string
  /** Legend / dot swatch background classes. */
  swatch: string
  /** SVG fill hex (dental chart tooth glyph). */
  color: string
}

export const CONDITIONS: Record<string, ConditionStyle> = {
  Sain: { label: "Sain", box: "bg-background text-foreground border-border", swatch: "bg-background border-border", color: "#e5e7eb" },
  Carie: { label: "Carie", box: "bg-red-500 text-white border-red-600", swatch: "bg-red-500", color: "#ef4444" },
  // ⚠️ Stays literal blue, and must NOT be routed to `--primary`. This is a **categorical clinical palette** — the
  // hues identify tooth conditions by long-standing charting convention, not by the app's accent — and it is
  // mirrored by the `color` hex the SVG chart paints with, so a class and a hex that disagree would render the
  // legend one colour and the tooth another. It also has to stay clear of `Bridge`, which is the teal one.
  Obturation: { label: "Obturation", box: "bg-blue-500 text-white border-blue-600", swatch: "bg-blue-500", color: "#3b82f6" },
  Couronne: { label: "Couronne", box: "bg-amber-500 text-white border-amber-600", swatch: "bg-amber-500", color: "#f59e0b" },
  TraitementDeCanal: { label: "Traitement de canal", box: "bg-purple-500 text-white border-purple-600", swatch: "bg-purple-500", color: "#a855f7" },
  Bridge: { label: "Bridge", box: "bg-teal-500 text-white border-teal-600", swatch: "bg-teal-500", color: "#14b8a6" },
  Implant: { label: "Implant", box: "bg-slate-600 text-white border-slate-700", swatch: "bg-slate-600", color: "#475569" },
  ExtraitAbsent: { label: "Extrait / Absent", box: "bg-gray-300 text-gray-500 border-gray-400 line-through dark:bg-gray-700 dark:text-gray-400", swatch: "bg-gray-300 dark:bg-gray-700", color: "#9ca3af" },
  /*
   * ⚠️ Rose, and it must NOT go back to the orange family. « À traiter » was `orange-400` (#fb923c) against
   * « Couronne »'s `amber-500` (#f59e0b) — **ΔE 18**, the closest pair in this legend by a wide margin and the
   * only one under 25. At the 12 px swatch the legend draws, and at the size a charted tooth is read from a
   * metre away, gold and light orange are the same colour: a crown already placed and a tooth still waiting for
   * work looked alike on the one diagram whose whole job is to tell them apart.
   *
   * `pink-500` takes its nearest neighbour to ΔE 48, and the neighbour it lands next to is « Carie » — which is
   * the right one, because those two are exactly `NEEDS_TREATMENT_CONDITIONS` below and reading as a warm pair
   * is a true statement about them. Couronne keeps amber: gold for a crown is the charting convention this
   * palette exists to honour, so the plan marker is what moves, not the clinical state.
   *
   * White text on it also measures 3.53:1 against orange-400's 2.26:1 — still under the 4.5 floor, so the box
   * is not a place to print small type, but strictly better than what it replaces.
   */
  ATraiter: { label: "À traiter", box: "bg-pink-500 text-white border-pink-600", swatch: "bg-pink-500", color: "#ec4899" },
}

// Order for the condition <Select> and the legend.
export const CONDITION_ORDER = [
  "Sain",
  "Carie",
  "Obturation",
  "Couronne",
  "TraitementDeCanal",
  "Bridge",
  "Implant",
  "ExtraitAbsent",
  "ATraiter",
]

/**
 * The conditions that describe **work still to do**, as opposed to work already done or a tooth that is gone.
 *
 * A charted diagnosis records what the dentist *observed*, and most of the observations are restorations:
 * « Obturation », « Couronne », « Traitement de canal », « Bridge », « Implant » all say the tooth has already been
 * treated, and « Extrait / Absent » says there is no tooth. Only « Carie » and « À traiter » call for an act.
 *
 * Kept here rather than inline in the record modal because it is a statement about the condition set itself, and
 * the same question ("does this tooth need something?") belongs to the odontogram too.
 */
export const NEEDS_TREATMENT_CONDITIONS = ["Carie", "ATraiter"] as const

/** True when a charted condition calls for treatment. Unknown values read as "no" — never invent work. */
export function needsTreatment(condition: string | null | undefined): boolean {
  return !!condition && (NEEDS_TREATMENT_CONDITIONS as readonly string[]).includes(condition)
}

export const SURFACE_ORDER = ["M", "O", "D", "V", "L"]

export const SURFACE_LABELS: Record<string, string> = {
  M: "Mésiale",
  O: "Occlusale",
  D: "Distale",
  V: "Vestibulaire",
  L: "Linguale",
}

export function conditionStyle(condition: string): ConditionStyle {
  return CONDITIONS[condition] ?? CONDITIONS.Sain
}

export function parseSurfaces(surfaces: string | null | undefined): Set<string> {
  const set = new Set<string>()
  if (surfaces) {
    for (const ch of surfaces.toUpperCase()) {
      if (SURFACE_ORDER.includes(ch)) set.add(ch)
    }
  }
  return set
}

export function serializeSurfaces(set: Set<string>): string {
  return SURFACE_ORDER.filter((s) => set.has(s)).join("")
}
