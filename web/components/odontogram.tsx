"use client"

import { useState, useEffect, useCallback, useMemo } from "react"
import { toast } from "sonner"
import { Plus, Trash2, Stethoscope, ClipboardList } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import { cn } from "@/lib/utils"
import { odontogramApi } from "@/lib/api/odontogram"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import type { ToothStateDto, ProcedureTypeDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { formatDateFr } from "@/lib/format"
import { CONDITION_ORDER, conditionStyle, SURFACE_LABELS, serializeSurfaces } from "@/components/odontogram-conditions"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"

// FDI layout (mirrors dental-chart.tsx). Adult = quadrants 1–4 (32 teeth), child = quadrants 5–8 (20 teeth).
const ADULT_TEETH = {
  upperRight: [18, 17, 16, 15, 14, 13, 12, 11],
  upperLeft: [21, 22, 23, 24, 25, 26, 27, 28],
  lowerRight: [48, 47, 46, 45, 44, 43, 42, 41],
  lowerLeft: [31, 32, 33, 34, 35, 36, 37, 38],
}

const CHILD_TEETH = {
  upperRight: [55, 54, 53, 52, 51],
  upperLeft: [61, 62, 63, 64, 65],
  lowerRight: [85, 84, 83, 82, 81],
  lowerLeft: [71, 72, 73, 74, 75],
}

// Max dots drawn under a tooth before collapsing the overflow into a "+N".
const MAX_DOTS = 4

// Conditions offerable as a diagnosis (everything except the implicit-healthy "Sain").
const DIAGNOSIS_CONDITIONS = CONDITION_ORDER.filter((c) => c !== "Sain")

const isDiagnosis = (entry: ToothStateDto) => entry.source === "Diagnosis"

/** One draft plan line seeded from an open diagnosis (consumed by the treatment-plan editor). */
export interface OdontogramPlanSeed {
  toothNumbers: number[]
  designationFr: string
  /** Prefilled planned cost from the matching procedure-type default (omitted when no catalog match). */
  plannedCost?: number
  /**
   * The procedure that treats the charted condition, when exactly one was matched. Carried through to the plan
   * act so booking it can preselect the procedure — the seeded designation is a condition label
   * (« Couronne — dent 16 »), so it would never resolve by name.
   */
  procedureTypeId?: string
}

interface OdontogramProps {
  patientId: string
  /** Called with one seed per tooth carrying an open diagnosis, to pre-fill a new treatment plan. */
  onCreatePlan?: (seeds: OdontogramPlanSeed[]) => void
}

export function Odontogram({ patientId, onCreatePlan }: OdontogramProps) {
  const [isAdult, setIsAdult] = useState(true)
  const [byTooth, setByTooth] = useState<Map<number, ToothStateDto[]>>(new Map())
  const [procedureTypes, setProcedureTypes] = useState<ProcedureTypeDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      setLoading(true)
      setError(null)
      const data = await odontogramApi.get(patientId)
      // Group entries by tooth, newest first within each tooth.
      const map = new Map<number, ToothStateDto[]>()
      for (const entry of data) {
        const list = map.get(entry.toothNumber) ?? []
        list.push(entry)
        map.set(entry.toothNumber, list)
      }
      for (const list of map.values()) {
        list.sort((a, b) => new Date(b.treatmentDate).getTime() - new Date(a.treatmentDate).getTime())
      }
      setByTooth(map)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Échec du chargement de l'odontogramme.")
    } finally {
      setLoading(false)
    }
  }, [patientId])

  useEffect(() => {
    load()
  }, [load])

  // Procedure catalog — used only to prefill a seeded plan line's cost (by resulting condition).
  useEffect(() => {
    procedureTypesApi.list(false).then(setProcedureTypes).catch(() => setProcedureTypes([]))
  }, [])

  // The odontogram also changes through the dental-record flow (broadcasts "patients"), so refresh live.
  useClinicRealtime(RealtimeResource.Patients, load)

  const teeth = isAdult ? ADULT_TEETH : CHILD_TEETH

  // A charted diagnosis names the desired end-state (e.g. "Couronne"); a procedure whose ResultingCondition
  // is that state is its treatment, so its default cost is the planned cost. Pathology diagnoses (Carie…)
  // have no such procedure and fall back to no prefill (0) — as allowed by the spec.
  const procedureByCondition = useMemo(() => {
    const map = new Map<string, ProcedureTypeDto>()
    for (const pt of procedureTypes) {
      if (pt.resultingCondition && !map.has(pt.resultingCondition)) {
        map.set(pt.resultingCondition, pt)
      }
    }
    return map
  }, [procedureTypes])

  // Open diagnoses (not yet treated), one seed per tooth, for "create a plan from the odontogram".
  const planSeeds = useMemo<OdontogramPlanSeed[]>(() => {
    const seeds: OdontogramPlanSeed[] = []
    for (const [tooth, entries] of Array.from(byTooth.entries()).sort((a, b) => a[0] - b[0])) {
      const diagnoses = entries.filter(isDiagnosis)
      if (diagnoses.length === 0) continue
      const conditions = Array.from(new Set(diagnoses.map((d) => d.condition)))
      const labels = conditions.map((c) => conditionStyle(c).label)
      const matchedCost = conditions.reduce(
        (sum, c) => sum + (procedureByCondition.get(c)?.defaultCost ?? 0),
        0,
      )
      // Only a single charted condition names a single procedure. A tooth carrying two diagnoses becomes one
      // aggregated line whose cost is the sum, and no one procedure speaks for it — so it carries no link
      // rather than an arbitrary one.
      const soleProcedure = conditions.length === 1 ? procedureByCondition.get(conditions[0]) : undefined
      seeds.push({
        toothNumbers: [tooth],
        designationFr: `${labels.join(", ")} — dent ${tooth}`,
        plannedCost: matchedCost > 0 ? matchedCost : undefined,
        procedureTypeId: soleProcedure?.id,
      })
    }
    return seeds
  }, [byTooth, procedureByCondition])

  return (
    <div className="w-full space-y-4">
      {/* Toolbar: dentition toggle + create-plan action */}
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <span className="text-xs font-medium text-muted-foreground">Dentition :</span>
          <div className="flex items-center gap-1 rounded-lg bg-muted p-1">
            <Button variant={isAdult ? "default" : "ghost"} size="sm" className="h-7 px-3 text-xs" onClick={() => setIsAdult(true)}>
              Adulte
            </Button>
            <Button variant={!isAdult ? "default" : "ghost"} size="sm" className="h-7 px-3 text-xs" onClick={() => setIsAdult(false)}>
              Enfant
            </Button>
          </div>
        </div>
        {onCreatePlan && (
          <Button
            size="sm"
            variant="outline"
            className="h-7 gap-1.5 text-xs"
            disabled={planSeeds.length === 0}
            onClick={() => onCreatePlan(planSeeds)}
            title={planSeeds.length === 0 ? "Aucun diagnostic à planifier" : undefined}
          >
            <ClipboardList className="h-3.5 w-3.5" />
            Créer un plan depuis l'odontogramme
          </Button>
        )}
      </div>

      <p className="text-xs text-muted-foreground">
        Cliquez sur une dent pour noter un diagnostic (à traiter). Les actes réalisés s'ajoutent
        automatiquement lors de l'enregistrement d'un acte médical.
      </p>

      {error && (
        <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-800 dark:border-red-800 dark:bg-red-950 dark:text-red-200">
          {error}
        </div>
      )}

      {loading ? (
        <p className="py-8 text-center text-muted-foreground">Chargement de l'odontogramme…</p>
      ) : (
        <div className="overflow-x-auto rounded-lg border border-border bg-card p-3">
          <div className="space-y-1.5">
            <div className="text-center text-[10px] font-medium text-muted-foreground">Maxillaire (haut)</div>
            <div className="flex justify-center gap-2">
              <div className="flex gap-0.5">
                {teeth.upperRight.map((t) => (
                  <ToothCell key={t} toothNum={t} entries={byTooth.get(t) ?? []} patientId={patientId} onChanged={load} />
                ))}
              </div>
              <div className="w-px bg-border" />
              <div className="flex gap-0.5">
                {teeth.upperLeft.map((t) => (
                  <ToothCell key={t} toothNum={t} entries={byTooth.get(t) ?? []} patientId={patientId} onChanged={load} />
                ))}
              </div>
            </div>
          </div>

          <div className="my-2 border-t border-border" />

          <div className="space-y-1.5">
            <div className="flex justify-center gap-2">
              <div className="flex gap-0.5">
                {teeth.lowerRight.map((t) => (
                  <ToothCell key={t} toothNum={t} entries={byTooth.get(t) ?? []} patientId={patientId} onChanged={load} />
                ))}
              </div>
              <div className="w-px bg-border" />
              <div className="flex gap-0.5">
                {teeth.lowerLeft.map((t) => (
                  <ToothCell key={t} toothNum={t} entries={byTooth.get(t) ?? []} patientId={patientId} onChanged={load} />
                ))}
              </div>
            </div>
            <div className="text-center text-[10px] font-medium text-muted-foreground">Mandibule (bas)</div>
          </div>
        </div>
      )}

      {/* Legend */}
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2 text-xs">
        {CONDITION_ORDER.map((c) => (
          <div key={c} className="flex items-center gap-1.5">
            <span className={cn("h-4 w-4 rounded border", conditionStyle(c).swatch)} />
            <span className="text-muted-foreground">{conditionStyle(c).label}</span>
          </div>
        ))}
        <div className="flex items-center gap-1.5">
          <span className="h-4 w-4 rounded border-2 border-dashed border-muted-foreground/60" />
          <span className="text-muted-foreground">Diagnostic (à traiter)</span>
        </div>
      </div>
    </div>
  )
}

interface ToothCellProps {
  toothNum: number
  entries: ToothStateDto[]
  patientId: string
  onChanged: () => void
}

function ToothCell({ toothNum, entries, patientId, onChanged }: ToothCellProps) {
  const [open, setOpen] = useState(false)
  const [condition, setCondition] = useState(DIAGNOSIS_CONDITIONS[0])
  const [note, setNote] = useState("")
  const [surfaces, setSurfaces] = useState<Set<string>>(new Set())
  const [saving, setSaving] = useState(false)

  const toggleSurface = (code: string) => {
    setSurfaces((prev) => {
      const next = new Set(prev)
      if (next.has(code)) next.delete(code)
      else next.add(code)
      return next
    })
  }

  const latest = entries[0]
  const style = conditionStyle(latest?.condition ?? "Sain")
  const latestIsDiagnosis = latest ? isDiagnosis(latest) : false

  const handleDiagnose = async () => {
    try {
      setSaving(true)
      await odontogramApi.diagnose(patientId, {
        toothNumber: toothNum,
        condition,
        surfaces: serializeSurfaces(surfaces) || null,
        note: note.trim() || null,
      })
      toast.success(`Diagnostic ajouté (dent ${toothNum})`)
      setNote("")
      setSurfaces(new Set())
      setOpen(false)
      onChanged()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de l'enregistrement du diagnostic.")
    } finally {
      setSaving(false)
    }
  }

  const handleRemove = async (id: string) => {
    try {
      await odontogramApi.removeCondition(patientId, id)
      toast.success("Diagnostic retiré")
      onChanged()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la suppression du diagnostic.")
    }
  }

  const box = (
    <span className="flex flex-col items-center">
      <span
        className={cn(
          "flex h-9 w-7 items-center justify-center rounded-md border text-[10px] font-semibold",
          style.box,
          latestIsDiagnosis && "border-2 border-dashed",
        )}
      >
        {latest?.surfaces ?? ""}
      </span>
      <span className="mt-0.5 text-[9px] font-medium text-muted-foreground">{toothNum}</span>
      {entries.length > 0 && (
        <span className="mt-0.5 flex items-center gap-0.5">
          {entries.slice(0, MAX_DOTS).map((e) => (
            <span
              key={e.id}
              className={cn(
                "h-1.5 w-1.5 rounded-full",
                isDiagnosis(e)
                  ? cn("border", conditionStyle(e.condition).swatch, "bg-transparent")
                  : conditionStyle(e.condition).swatch,
              )}
            />
          ))}
          {entries.length > MAX_DOTS && (
            <span className="text-[8px] font-medium text-muted-foreground">+{entries.length - MAX_DOTS}</span>
          )}
        </span>
      )}
    </span>
  )

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <button
          type="button"
          title={`Dent ${toothNum}`}
          className="group rounded-md transition-all hover:scale-105 focus:outline-none focus:ring-1 focus:ring-ring"
        >
          {box}
        </button>
      </PopoverTrigger>
      <PopoverContent className="w-80 space-y-3" align="center">
        <div>
          <p className="text-sm font-semibold">Dent {toothNum}</p>
          <p className="text-xs text-muted-foreground">
            {entries.length === 0
              ? "Aucun état enregistré"
              : `${entries.length} état${entries.length > 1 ? "s" : ""} enregistré${entries.length > 1 ? "s" : ""}`}
          </p>
        </div>

        {entries.length > 0 && (
          <ul className="space-y-2">
            {entries.map((e) => (
              <li key={e.id} className="rounded-md border p-2 text-xs">
                <div className="flex items-center gap-2">
                  <span className={cn("h-2.5 w-2.5 shrink-0 rounded-full border", conditionStyle(e.condition).swatch)} />
                  <span className="font-medium text-foreground">{conditionStyle(e.condition).label}</span>
                  <span
                    className={cn(
                      "rounded px-1 py-0.5 text-[9px] font-medium",
                      isDiagnosis(e)
                        ? "bg-orange-100 text-orange-700 dark:bg-orange-950 dark:text-orange-300"
                        : "bg-muted text-muted-foreground",
                    )}
                  >
                    {isDiagnosis(e) ? "Diagnostic" : "Réalisé"}
                  </span>
                  <span className="ml-auto text-muted-foreground">{formatDateFr(e.treatmentDate)}</span>
                </div>
                {e.surfaces && <p className="mt-1 text-muted-foreground">Faces : {e.surfaces.split("").join(", ")}</p>}
                {e.note && <p className="mt-1 text-foreground">{e.note}</p>}
                {isDiagnosis(e) && (
                  <button
                    type="button"
                    onClick={() => handleRemove(e.id)}
                    className="mt-1 flex items-center gap-1 text-[10px] font-medium text-red-600 hover:underline"
                  >
                    <Trash2 className="h-3 w-3" /> Retirer le diagnostic
                  </button>
                )}
              </li>
            ))}
          </ul>
        )}

        {/* Add-diagnosis form */}
        <div className="space-y-2 border-t pt-2">
          <p className="flex items-center gap-1.5 text-xs font-medium text-foreground">
            <Stethoscope className="h-3.5 w-3.5" /> Noter un diagnostic
          </p>
          <Select value={condition} onValueChange={setCondition}>
            <SelectTrigger className="h-8 text-xs">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {DIAGNOSIS_CONDITIONS.map((c) => (
                <SelectItem key={c} value={c} className="text-xs">
                  {conditionStyle(c).label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          {/* Surfaces (MODVL) — optional, finding #19 */}
          <div className="flex flex-wrap gap-1">
            {Object.entries(SURFACE_LABELS).map(([code, label]) => (
              <Button
                key={code}
                type="button"
                variant={surfaces.has(code) ? "default" : "outline"}
                size="sm"
                className="h-7 px-2 text-xs"
                title={label}
                onClick={() => toggleSurface(code)}
              >
                {code}
              </Button>
            ))}
          </div>
          <Textarea
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder="Note (optionnelle)"
            className="min-h-[52px] text-xs"
          />
          <Button size="sm" className="h-8 w-full gap-1.5 text-xs" onClick={handleDiagnose} disabled={saving}>
            <Plus className="h-3.5 w-3.5" />
            {saving ? "Enregistrement…" : "Ajouter le diagnostic"}
          </Button>
        </div>
      </PopoverContent>
    </Popover>
  )
}
