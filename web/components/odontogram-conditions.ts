// Shared ToothCondition metadata — used by the read-only odontogram (patient tab) and the dental-record
// editor (where conditions are captured). Keep the enum names in sync with the backend ToothCondition.

export interface ConditionStyle {
  label: string
  /** Tooth "box" fill classes (odontogram cell). */
  box: string
  /** Legend / dot swatch background classes. */
  swatch: string
}

export const CONDITIONS: Record<string, ConditionStyle> = {
  Sain: { label: "Sain", box: "bg-background text-foreground border-border", swatch: "bg-background border-border" },
  Carie: { label: "Carie", box: "bg-red-500 text-white border-red-600", swatch: "bg-red-500" },
  Obturation: { label: "Obturation", box: "bg-blue-500 text-white border-blue-600", swatch: "bg-blue-500" },
  Couronne: { label: "Couronne", box: "bg-amber-500 text-white border-amber-600", swatch: "bg-amber-500" },
  TraitementDeCanal: { label: "Traitement de canal", box: "bg-purple-500 text-white border-purple-600", swatch: "bg-purple-500" },
  Bridge: { label: "Bridge", box: "bg-teal-500 text-white border-teal-600", swatch: "bg-teal-500" },
  Implant: { label: "Implant", box: "bg-slate-600 text-white border-slate-700", swatch: "bg-slate-600" },
  ExtraitAbsent: { label: "Extrait / Absent", box: "bg-gray-300 text-gray-500 border-gray-400 line-through dark:bg-gray-700 dark:text-gray-400", swatch: "bg-gray-300 dark:bg-gray-700" },
  ATraiter: { label: "À traiter", box: "bg-orange-400 text-white border-orange-500", swatch: "bg-orange-400" },
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
