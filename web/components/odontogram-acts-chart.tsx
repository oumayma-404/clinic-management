"use client"

import { useMemo, useState } from "react"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { cn } from "@/lib/utils"
import { formatDateFr } from "@/lib/format"
import type { DentalRecordDto, ProcedureTypeDto } from "@/lib/api/types"

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
 *   palette the clinic already picked for its acts), so an extraction and a prothèse are distinguishable at a
 *   glance.</li>
 *   <li><b>It showed only the acts that change a tooth's state.</b> The worst of the three, because it looked
 *   like a display bug and was a wrong source: the chart was built from the odontogram's <c>ToothState</c> rows,
 *   and one of those exists only when the act has a <c>ResultingCondition</c>. Extraction has one; Consultation,
 *   Blanchiment and Prothèse amovible do not — so a patient with five fiches across nine teeth showed four. It
 *   now reads the fiches' own acts, so « worked on » means <b>every tooth an act names</b>, which is the question
 *   the tab asks. Tooth states are not consulted here at all.</li>
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
 * <p>Everything comes from data the parent already holds — the patient's fiches and the clinic's act catalog —
 * so this is a derivation, not a new endpoint.</p>
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
  /** The patient's fiches — the authority on which teeth were worked on, and with which act. */
  records: DentalRecordDto[]
  /** The clinic's act catalog, for each act's colour. */
  procedureTypes: ProcedureTypeDto[]
}

export function OdontogramActsChart({ teeth, records, procedureTypes }: OdontogramActsChartProps) {
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
    const colorById = new Map(procedureTypes.map((p) => [p.id, p.colorHex]))
    const map = new Map<number, ToothAct[]>()

    // Straight from the fiches: every act, on every tooth it names. See the note above on why this is NOT
    // driven by the odontogram's tooth states.
    for (const record of records) {
      for (const act of record.acts ?? []) {
        for (const tooth of act.toothNumbers ?? []) {
          const acts = map.get(tooth) ?? []
          acts.push({
            name: act.procedureName,
            // The clinic's own colour for that act; a free-text act has no catalog entry and no colour.
            color: (act.procedureTypeId ? colorById.get(act.procedureTypeId) : undefined) ?? UNKNOWN_ACT_COLOR,
            date: record.interventionDate,
          })
          map.set(tooth, acts)
        }
      }
    }

    for (const acts of map.values()) {
      acts.sort((a, b) => (a.date < b.date ? 1 : a.date > b.date ? -1 : 0))
    }
    return map
  }, [records, procedureTypes])

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
