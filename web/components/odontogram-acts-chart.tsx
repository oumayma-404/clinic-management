"use client"

import { useMemo } from "react"
import { RecordToothChart, type ToothPaint } from "@/components/record-tooth-chart"
import { conditionStyle } from "@/components/odontogram-conditions"
import { formatDateFr } from "@/lib/format"
import type { DentalRecordDto, ToothStateDto } from "@/lib/api/types"

/**
 * « Actes réalisés » — the read-only half of the odontogram: which teeth have actually been worked on, and with
 * which act.
 *
 * <p><b>Nothing is written here.</b> A `ToothState` whose source is `Treatment` is created by the server when a
 * fiche de soins is saved, and the server refuses to remove one through the charting endpoint — so this view
 * reflects recorded clinical work rather than offering to edit it. The diagnosis tab is where charting happens.</p>
 *
 * <p><b>Why the act name is joined client-side.</b> A tooth state carries the resulting *condition* (« Obturation »)
 * — which is what the colour shows — but not the *act* that produced it. The act lives on
 * `DentalRecordAct.procedureName`, reachable through the state's `dentalRecordId`. Both sides are already loaded,
 * so this is a join and not a new endpoint.</p>
 */

/** One act performed on a tooth, with the session it belongs to. */
interface ToothAct {
  label: string
  /** ISO date of the session, used for ordering and shown to the reader. */
  date: string
}

interface OdontogramActsChartProps {
  isAdult: boolean
  /** Every tooth state of the patient — this component picks out the treatment-sourced ones itself. */
  entries: ToothStateDto[]
  /** The patient's fiches, for the act names. An empty list still renders: the fallback below covers it. */
  records: DentalRecordDto[]
}

export function OdontogramActsChart({ isAdult, entries, records }: OdontogramActsChartProps) {
  const treatments = useMemo(() => entries.filter((e) => e.source === "Treatment"), [entries])

  /** tooth number → acts performed on it, newest session first. */
  const actsByTooth = useMemo(() => {
    const byId = new Map(records.map((r) => [r.id, r]))
    const map = new Map<number, ToothAct[]>()

    for (const entry of treatments) {
      const record = entry.dentalRecordId ? byId.get(entry.dentalRecordId) : undefined
      const matching = record?.acts?.filter((a) => (a.toothNumbers ?? []).includes(entry.toothNumber)) ?? []

      const acts: ToothAct[] = matching.length > 0
        ? matching.map((a) => ({ label: a.procedureName, date: record!.interventionDate }))
        // No act to name: the fiche was deleted, or the state predates the record link. Falling back to the
        // resulting condition is honest — it is what the colour already means — and beats an empty tooltip on a
        // tooth the reader can plainly see is coloured.
        : [{ label: conditionStyle(entry.condition).label, date: entry.treatmentDate }];

      map.set(entry.toothNumber, [...(map.get(entry.toothNumber) ?? []), ...acts])
    }

    for (const acts of map.values()) {
      acts.sort((a, b) => (a.date < b.date ? 1 : a.date > b.date ? -1 : 0))
    }
    return map
  }, [treatments, records])

  /**
   * The chart's fill per tooth. `selected` is true for every worked tooth so the colour reads at full strength —
   * this view has no selection, and the shared chart mutes anything unselected.
   */
  const paint = useMemo(() => {
    const map = new Map<number, ToothPaint>()
    // Latest state wins the colour, matching what the tooltip lists first.
    const latest = new Map<number, ToothStateDto>()
    for (const e of treatments) {
      const held = latest.get(e.toothNumber)
      if (!held || held.treatmentDate < e.treatmentDate) latest.set(e.toothNumber, e)
    }
    for (const [tooth, entry] of latest) {
      map.set(tooth, {
        selected: true,
        color: conditionStyle(entry.condition).color,
        count: actsByTooth.get(tooth)?.length ?? 1,
      })
    }
    return map
  }, [treatments, actsByTooth])

  const toothTitle = (num: number) => {
    const acts = actsByTooth.get(num)
    if (!acts || acts.length === 0) return `Dent ${num} — aucun acte`
    // Newlines render in a native title tooltip, so several acts read as several lines.
    return [`Dent ${num}`, ...acts.map((a) => `${a.label} — ${formatDateFr(a.date)}`)].join("\n")
  }

  if (treatments.length === 0) {
    return (
      <p className="rounded-lg border border-dashed border-border py-8 text-center text-sm text-muted-foreground">
        Aucun acte réalisé enregistré pour ce patient. Les actes s&apos;ajoutent automatiquement à
        l&apos;enregistrement d&apos;une fiche de soins.
      </p>
    )
  }

  return (
    <div className="space-y-2">
      {/* `disabled` is what makes this read-only: the shared chart's teeth become inert buttons, so the handler
          below can never fire. It is passed anyway because the prop is required. */}
      <RecordToothChart
        isAdult={isAdult}
        paint={paint}
        onToggleTooth={() => {}}
        disabled
        toothTitle={toothTitle}
      />
      <p className="text-xs text-muted-foreground">
        Survolez une dent colorée pour voir les actes réalisés. Cette vue est en lecture seule.
      </p>
    </div>
  )
}
