"use client"

import { useMemo, useState } from "react"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { conditionStyle } from "@/components/odontogram-conditions"
import { cn } from "@/lib/utils"
import { formatDateFr } from "@/lib/format"
import type { DentalRecordDto, ProcedureTypeDto, ToothStateDto } from "@/lib/api/types"

/**
 * « Actes réalisés » — the read-only half of the odontogram: which teeth were worked on, and with which act.
 *
 * <p><b>Nothing is written here.</b> A tooth state whose source is `Treatment` is created by the server when a
 * fiche de soins is saved, and the server refuses to remove one through the charting endpoint. This view reflects
 * recorded work; the « Diagnostics » tab is where charting happens.</p>
 *
 * <h4>Design notes — the three things the first version got wrong</h4>
 * <ol>
 *   <li><b>It looked like a different product.</b> It reused the fiche editor's SVG tooth glyphs while the
 *   diagnosis tab uses cells, so switching tabs redrew the mouth in another visual language. The cell geometry
 *   here (<c>h-9 w-7 rounded-md border</c>, number underneath, dots for multiplicity) is deliberately
 *   character-for-character the diagnosis chart's, so the two tabs read as one chart with two filters.</li>
 *   <li><b>It coloured by resulting condition, not by act.</b> Four teeth all came out the same grey because
 *   « Extraction » resolves to <i>Extrait / Absent</i> — the chart said "something happened here" and nothing
 *   more. Each tooth now takes the colour of the <b>procedure</b> performed (<c>ProcedureType.ColorHex</c>, the
 *   palette the clinic already picked for its acts), so an extraction and an implant are distinguishable at a
 *   glance.</li>
 *   <li><b>The legend listed all nine conditions.</b> It was rendered outside the tabs, so the diagnosis
 *   palette appeared under this chart where it means nothing. The legend below lists <b>only the acts actually
 *   on this chart</b>.</li>
 * </ol>
 *
 * <h4>Why a Popover and not a Tooltip</h4>
 * <p>A tooltip opens on hover and focus, which on a tablet means the act names are simply unreachable — and this
 * is the only place they appear, so on touch the view would show coloured teeth with no way to learn what was
 * done. The popover opens on <b>tap</b> natively and is also opened on <b>hover</b> for mouse users, so the
 * desktop reading stays hover-only and the same markup works under a finger. Radix keeps outside-click and
 * Escape dismissal, which a tooltip does not offer at all.</p>
 *
 * <p>The act name is the one thing a tooth state does not carry: it holds the resulting condition, while the act
 * lives on <c>DentalRecordAct</c>. Reached through the state's <c>dentalRecordId</c> and joined here — both
 * sides are already loaded by the parent, so this is a join and not a new endpoint.</p>
 */

/** One act performed on a tooth. */
interface ToothAct {
  name: string
  color: string
  /** ISO date of the session, for ordering and display. */
  date: string
}

/** Fallback fill for a worked tooth whose act has no catalog colour (free-text act, or a deleted fiche). */
const UNKNOWN_ACT_COLOR = "#94a3b8"

interface OdontogramActsChartProps {
  /** FDI layout, passed in so both tabs lay the mouth out identically (and so `isAdult` lives in one place). */
  teeth: { upperRight: number[]; upperLeft: number[]; lowerRight: number[]; lowerLeft: number[] }
  /** Every tooth state of the patient — this component picks out the treatment-sourced ones itself. */
  entries: ToothStateDto[]
  /** The patient's fiches, for the act names. */
  records: DentalRecordDto[]
  /** The clinic's act catalog, for each act's colour. */
  procedureTypes: ProcedureTypeDto[]
}

export function OdontogramActsChart({ teeth, entries, records, procedureTypes }: OdontogramActsChartProps) {
  /**
   * Which tooth's acts are showing, tracked in two independent channels so the two input methods cannot fight:
   * a tap pins the panel open until it is dismissed, while hover opens it only while the pointer is over the
   * tooth. Held here rather than per-cell so only one panel is ever open — 32 cells a few pixels apart would
   * otherwise stack panels as the pointer crosses them.
   */
  const [tappedTooth, setTappedTooth] = useState<number | null>(null)
  const [hoveredTooth, setHoveredTooth] = useState<number | null>(null)
  /** tooth number → acts performed on it, newest session first. */
  const actsByTooth = useMemo(() => {
    const recordsById = new Map(records.map((r) => [r.id, r]))
    const colorById = new Map(procedureTypes.map((p) => [p.id, p.colorHex]))
    const map = new Map<number, ToothAct[]>()

    for (const entry of entries) {
      if (entry.source !== "Treatment") continue

      const record = entry.dentalRecordId ? recordsById.get(entry.dentalRecordId) : undefined
      const performed = record?.acts?.filter((a) => (a.toothNumbers ?? []).includes(entry.toothNumber)) ?? []

      const acts: ToothAct[] = performed.length > 0
        ? performed.map((a) => ({
            name: a.procedureName,
            // The clinic's own colour for that act. A free-text act has no catalog entry and no colour.
            color: (a.procedureTypeId ? colorById.get(a.procedureTypeId) : undefined) ?? UNKNOWN_ACT_COLOR,
            date: record!.interventionDate,
          }))
        // No act to name: the fiche was deleted, or the state predates the record link. Falling back to the
        // resulting condition is honest — it is what the state actually records — and beats a blank tooltip on a
        // tooth the reader can plainly see is coloured.
        : [{
            name: conditionStyle(entry.condition).label,
            color: conditionStyle(entry.condition).color,
            date: entry.treatmentDate,
          }]

      const existing = map.get(entry.toothNumber) ?? []
      // One entry per act per tooth. Two states on one tooth from the same fiche (an extraction *and* an implant)
      // would otherwise list every act of that fiche twice.
      for (const act of acts) {
        if (!existing.some((a) => a.name === act.name && a.date === act.date)) existing.push(act)
      }
      map.set(entry.toothNumber, existing)
    }

    for (const acts of map.values()) {
      acts.sort((a, b) => (a.date < b.date ? 1 : a.date > b.date ? -1 : 0))
    }
    return map
  }, [entries, records, procedureTypes])

  /** Only the acts actually on this chart, in the order a reader meets them. */
  const legend = useMemo(() => {
    const seen = new Map<string, string>()
    for (const acts of actsByTooth.values()) {
      for (const act of acts) if (!seen.has(act.name)) seen.set(act.name, act.color)
    }
    return [...seen.entries()].map(([name, color]) => ({ name, color }))
  }, [actsByTooth])

  if (actsByTooth.size === 0) {
    return (
      <p className="rounded-lg border border-dashed border-border py-8 text-center text-sm text-muted-foreground">
        Aucun acte réalisé enregistré pour ce patient. Les actes s&apos;ajoutent automatiquement à
        l&apos;enregistrement d&apos;une fiche de soins.
      </p>
    )
  }

  const renderTooth = (toothNum: number) => {
    const acts = actsByTooth.get(toothNum)
    // The most recent act owns the fill, matching the first line of the tooltip.
    const fill = acts?.[0]?.color

    const cell = (
      <span className="flex flex-col items-center">
        <span
          className={cn(
            // Same geometry as the diagnosis chart's cell — the two tabs must draw the same mouth.
            "flex h-9 w-7 items-center justify-center rounded-md border text-[10px] font-semibold",
            !acts && "border-border bg-background",
          )}
          style={acts ? { backgroundColor: fill, borderColor: fill } : undefined}
        />
        <span className="mt-0.5 text-[9px] font-medium text-muted-foreground">{toothNum}</span>
        {acts && (
          // One dot per act, mirroring the diagnosis cell's multiplicity indicator.
          <span className="mt-0.5 flex items-center gap-0.5">
            {acts.slice(0, 4).map((a, i) => (
              <span
                key={`${a.name}-${a.date}-${i}`}
                className="h-1.5 w-1.5 rounded-full"
                style={{ backgroundColor: a.color }}
              />
            ))}
            {acts.length > 4 && (
              <span className="text-[8px] font-medium text-muted-foreground">+{acts.length - 4}</span>
            )}
          </span>
        )}
      </span>
    )

    // An untouched tooth has nothing to say — no tooltip, and no hover affordance suggesting otherwise.
    if (!acts) return <span key={toothNum}>{cell}</span>

    return (
      <Popover
        key={toothNum}
        open={tappedTooth === toothNum || hoveredTooth === toothNum}
        // Radix reports its own dismissals (outside click, Escape) here; clearing both channels is what makes a
        // tap-pinned panel closable without another tap on the same tooth.
        onOpenChange={(open) => {
          if (!open) {
            setTappedTooth((t) => (t === toothNum ? null : t))
            setHoveredTooth((t) => (t === toothNum ? null : t))
          }
        }}
      >
        <PopoverTrigger asChild>
          <button
            type="button"
            // A real button, so tap, click, Enter and Space all reach it — this is the only place the act name
            // appears, and a hover-only affordance would hide it from every tablet.
            onClick={() => setTappedTooth((t) => (t === toothNum ? null : toothNum))}
            onMouseEnter={() => setHoveredTooth(toothNum)}
            onMouseLeave={() => setHoveredTooth((t) => (t === toothNum ? null : t))}
            aria-label={`Dent ${toothNum} — ${acts.length} acte${acts.length > 1 ? "s" : ""} réalisé${acts.length > 1 ? "s" : ""}`}
            className="rounded-md focus:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          >
            {cell}
          </button>
        </PopoverTrigger>
        <PopoverContent side="top" align="center" className="w-auto max-w-xs p-3 text-xs">
          <p className="mb-1.5 font-semibold">Dent {toothNum}</p>
          <ul className="space-y-1">
            {acts.map((a, i) => (
              <li key={`${a.name}-${a.date}-${i}`} className="flex items-start gap-1.5">
                <span
                  className="mt-1 h-2 w-2 shrink-0 rounded-full"
                  style={{ backgroundColor: a.color }}
                />
                <span>
                  {a.name}
                  <span className="text-muted-foreground"> — {formatDateFr(a.date)}</span>
                </span>
              </li>
            ))}
          </ul>
        </PopoverContent>
      </Popover>
    )
  }

  return (
    <div className="space-y-3">
        {/* Same container and arch labels as the diagnosis chart. */}
        <div className="overflow-x-auto rounded-lg border border-border bg-card p-3">
          <div className="space-y-1.5">
            <div className="text-center text-[10px] font-medium text-muted-foreground">Maxillaire (haut)</div>
            <div className="flex justify-center gap-2">
              <div className="flex gap-0.5">{teeth.upperRight.map(renderTooth)}</div>
              <div className="w-px bg-border" />
              <div className="flex gap-0.5">{teeth.upperLeft.map(renderTooth)}</div>
            </div>
          </div>

          <div className="my-2 border-t border-border" />

          <div className="space-y-1.5">
            <div className="flex justify-center gap-2">
              <div className="flex gap-0.5">{teeth.lowerRight.map(renderTooth)}</div>
              <div className="w-px bg-border" />
              <div className="flex gap-0.5">{teeth.lowerLeft.map(renderTooth)}</div>
            </div>
            <div className="text-center text-[10px] font-medium text-muted-foreground">Mandibule (bas)</div>
          </div>
        </div>

        <p className="text-xs text-muted-foreground">
          Touchez ou survolez une dent colorée pour voir les actes réalisés. Vue en lecture seule — les actes
          proviennent des fiches de soins.
        </p>

        {/* Only the acts on this chart. The full catalog under a chart showing three of them is noise. */}
        <div className="flex flex-wrap items-center gap-x-4 gap-y-2 text-xs">
          {legend.map((a) => (
            <div key={a.name} className="flex items-center gap-1.5">
              <span className="h-4 w-4 rounded border" style={{ backgroundColor: a.color, borderColor: a.color }} />
              <span className="text-muted-foreground">{a.name}</span>
            </div>
          ))}
      </div>
    </div>
  )
}
