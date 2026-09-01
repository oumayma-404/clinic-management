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

  /*
   * ⚠️ The six added by `odontogram-plan-suggestions` all sit in hue families the nine above had left free, and
   * all at a LOWER lightness than their nearest neighbour — the legend's 12 px swatch is the size this palette
   * has to survive, and the « À traiter » lesson recorded above is that two hues within ~20 ΔE read as one
   * colour at that size. Nearest neighbour is named per entry.
   */
  // brown; nearest is Couronne #f59e0b — same warm family, ~35 points darker
  Fracture: { label: "Fracture", box: "bg-amber-800 text-white border-amber-900", swatch: "bg-amber-800", color: "#92400e" },
  // olive; the green/lime region was entirely unused
  RacineResiduelle: { label: "Racine résiduelle", box: "bg-lime-700 text-white border-lime-800", swatch: "bg-lime-700", color: "#4d7c0f" },
  // indigo; between Obturation's blue and Traitement de canal's purple, and darker than both
  DentIncluse: { label: "Dent incluse", box: "bg-indigo-700 text-white border-indigo-800", swatch: "bg-indigo-700", color: "#4338ca" },
  // dark cyan; deliberately the blue family — it IS an obturation, the failing one
  RestaurationDefectueuse: { label: "Restauration défectueuse", box: "bg-cyan-700 text-white border-cyan-800", swatch: "bg-cyan-700", color: "#0e7490" },
  // dark purple; the endodontic family, one step below Traitement de canal, which is what it indicates
  LesionPeriapicale: { label: "Lésion périapicale", box: "bg-purple-700 text-white border-purple-800", swatch: "bg-purple-700", color: "#7e22ce" },
  // wine; the colour of inflamed gingiva, and ~35 points below « À traiter »'s pink
  MaladieParodontale: { label: "Maladie parodontale", box: "bg-pink-900 text-white border-pink-950", swatch: "bg-pink-900", color: "#831843" },
}

/**
 * Order for the condition <Select> and the legend — **what is wrong first, what was done second**.
 *
 * At fifteen members a flat alphabetical-ish list is a scan; charting starts from « what am I looking at », and
 * that is a pathology far more often than it is a restoration. The two runs are separated by `CONDITION_FAMILY`
 * so a picker can head them without keeping its own copy of the split.
 */
export const CONDITION_ORDER = [
  "Sain",
  // à soigner
  "Carie",
  "RestaurationDefectueuse",
  "Fracture",
  "LesionPeriapicale",
  "RacineResiduelle",
  "MaladieParodontale",
  "ATraiter",
  // constat
  "DentIncluse",
  "ExtraitAbsent",
  // déjà traité
  "Obturation",
  "TraitementDeCanal",
  "Couronne",
  "Bridge",
  "Implant",
]

/** Which run of {@link CONDITION_ORDER} a condition belongs to, for a picker that heads its groups. */
export type ConditionFamily = "sain" | "pathologie" | "constat" | "traite"

export const CONDITION_FAMILY: Record<string, ConditionFamily> = {
  Sain: "sain",
  Carie: "pathologie",
  RestaurationDefectueuse: "pathologie",
  Fracture: "pathologie",
  LesionPeriapicale: "pathologie",
  RacineResiduelle: "pathologie",
  MaladieParodontale: "pathologie",
  ATraiter: "pathologie",
  DentIncluse: "constat",
  ExtraitAbsent: "constat",
  Obturation: "traite",
  TraitementDeCanal: "traite",
  Couronne: "traite",
  Bridge: "traite",
  Implant: "traite",
}

export const CONDITION_FAMILY_LABEL: Record<ConditionFamily, string> = {
  sain: "",
  pathologie: "À soigner",
  constat: "Constat",
  traite: "Déjà traité",
}

/**
 * The conditions that describe **work still to do** — the ones that seed a plan from the odontogram and the ones
 * counted in « N dents à traiter ».
 *
 * <p>⚠️ `DentIncluse` and `ExtraitAbsent` are absent although both carry treatments: an impacted tooth is
 * usually monitored and a missing one is only replaced if the patient wants it, so counting either as
 * outstanding work inflates the one figure a dentist has to be able to trust.</p>
 *
 * <p>⚠️ **Mirrors `ConditionTreatments.NeedsTreatment` on the server**, and
 * `OdontogramConditionMirrorTests` parses this file and fails the build if the two sets drift.</p>
 */
export const NEEDS_TREATMENT_CONDITIONS = [
  "Carie",
  "ATraiter",
  "Fracture",
  "RacineResiduelle",
  "RestaurationDefectueuse",
  "LesionPeriapicale",
  "MaladieParodontale",
] as const

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
