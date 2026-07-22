"use client"

import { useState, useEffect, useCallback } from "react"
import { Button } from "@/components/ui/button"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { cn } from "@/lib/utils"
import { odontogramApi } from "@/lib/api/odontogram"
import type { ToothStateDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { formatDateFr } from "@/lib/format"
import { CONDITION_ORDER, conditionStyle } from "@/components/odontogram-conditions"
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

interface OdontogramProps {
  patientId: string
}

export function Odontogram({ patientId }: OdontogramProps) {
  const [isAdult, setIsAdult] = useState(true)
  const [byTooth, setByTooth] = useState<Map<number, ToothStateDto[]>>(new Map())
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      setLoading(true)
      setError(null)
      const data = await odontogramApi.get(patientId)
      // Group entries by tooth, newest treatment first within each tooth.
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

  // Odontogram entries are written by the dental-record flow (broadcasts the "patients" key),
  // so refresh live when a record is added/edited/removed instead of only on mount.
  useClinicRealtime(RealtimeResource.Patients, load)

  const teeth = isAdult ? ADULT_TEETH : CHILD_TEETH

  const renderTooth = (toothNum: number) => {
    const entries = byTooth.get(toothNum) ?? [] // already sorted newest-first
    const latest = entries[0]
    const style = conditionStyle(latest?.condition ?? "Sain")

    const box = (
      <span className="flex flex-col items-center">
        <span
          className={cn(
            "flex h-9 w-7 items-center justify-center rounded-md border text-[10px] font-semibold",
            style.box,
          )}
        >
          {latest?.surfaces ?? ""}
        </span>
        <span className="mt-0.5 text-[9px] font-medium text-muted-foreground">{toothNum}</span>
        {/* Dots: one per treatment, colored by that entry's condition. */}
        {entries.length > 0 && (
          <span className="mt-0.5 flex items-center gap-0.5">
            {entries.slice(0, MAX_DOTS).map((e) => (
              <span key={e.id} className={cn("h-1.5 w-1.5 rounded-full", conditionStyle(e.condition).swatch)} />
            ))}
            {entries.length > MAX_DOTS && (
              <span className="text-[8px] font-medium text-muted-foreground">+{entries.length - MAX_DOTS}</span>
            )}
          </span>
        )}
      </span>
    )

    // Teeth with no recorded treatment are non-interactive; treated teeth open a read-only history popover.
    if (entries.length === 0) {
      return (
        <div key={toothNum} title={`Dent ${toothNum} — aucun traitement`} className="opacity-70">
          {box}
        </div>
      )
    }

    return (
      <Popover key={toothNum}>
        <PopoverTrigger asChild>
          <button
            type="button"
            title={`Dent ${toothNum} — ${entries.length} traitement(s)`}
            className="group rounded-md transition-all hover:scale-105 focus:outline-none focus:ring-1 focus:ring-ring"
          >
            {box}
          </button>
        </PopoverTrigger>
        <PopoverContent className="w-72 space-y-2" align="center">
          <div>
            <p className="text-sm font-semibold">Dent {toothNum}</p>
            <p className="text-xs text-muted-foreground">
              {entries.length} traitement{entries.length > 1 ? "s" : ""} enregistré{entries.length > 1 ? "s" : ""}
            </p>
          </div>
          <ul className="space-y-2">
            {entries.map((e) => (
              <li key={e.id} className="rounded-md border p-2 text-xs">
                <div className="flex items-center gap-2">
                  <span className={cn("h-2.5 w-2.5 shrink-0 rounded-full border", conditionStyle(e.condition).swatch)} />
                  <span className="font-medium text-foreground">{conditionStyle(e.condition).label}</span>
                  <span className="ml-auto text-muted-foreground">{formatDateFr(e.treatmentDate)}</span>
                </div>
                {e.surfaces && (
                  <p className="mt-1 text-muted-foreground">Faces : {e.surfaces.split("").join(", ")}</p>
                )}
                {e.note && <p className="mt-1 text-foreground">{e.note}</p>}
              </li>
            ))}
          </ul>
        </PopoverContent>
      </Popover>
    )
  }

  return (
    <div className="w-full space-y-4">
      {/* Adult / child toggle */}
      <div className="flex items-center gap-2">
        <span className="text-xs font-medium text-muted-foreground">Dentition :</span>
        <div className="flex items-center gap-1 rounded-lg bg-muted p-1">
          <Button
            variant={isAdult ? "default" : "ghost"}
            size="sm"
            className="h-7 px-3 text-xs"
            onClick={() => setIsAdult(true)}
          >
            Adulte
          </Button>
          <Button
            variant={!isAdult ? "default" : "ghost"}
            size="sm"
            className="h-7 px-3 text-xs"
            onClick={() => setIsAdult(false)}
          >
            Enfant
          </Button>
        </div>
      </div>

      {error && (
        <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-800 dark:border-red-800 dark:bg-red-950 dark:text-red-200">
          {error}
        </div>
      )}

      {loading ? (
        <p className="py-8 text-center text-muted-foreground">Chargement de l'odontogramme…</p>
      ) : (
        <div className="overflow-x-auto rounded-lg border border-border bg-card p-3">
          {/* Upper jaw */}
          <div className="space-y-1.5">
            <div className="text-center text-[10px] font-medium text-muted-foreground">Maxillaire (haut)</div>
            <div className="flex justify-center gap-2">
              <div className="flex gap-0.5">{teeth.upperRight.map(renderTooth)}</div>
              <div className="w-px bg-border" />
              <div className="flex gap-0.5">{teeth.upperLeft.map(renderTooth)}</div>
            </div>
          </div>

          <div className="my-2 border-t border-border" />

          {/* Lower jaw */}
          <div className="space-y-1.5">
            <div className="flex justify-center gap-2">
              <div className="flex gap-0.5">{teeth.lowerRight.map(renderTooth)}</div>
              <div className="w-px bg-border" />
              <div className="flex gap-0.5">{teeth.lowerLeft.map(renderTooth)}</div>
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
      </div>
    </div>
  )
}
