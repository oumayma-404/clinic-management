"use client"

import { cn } from "@/lib/utils"
import { DENTITION_VIEWS, DENTITION_VIEW_LABELS_FR, type DentitionView } from "@/lib/dentition"

interface DentitionViewSwitchProps {
  value: DentitionView
  onChange: (view: DentitionView) => void
  disabled?: boolean
  className?: string
}

/**
 * Adulte / Enfant / **Mixte** — which arch the tooth chart draws.
 *
 * ## Why this exists at all
 *
 * The charts used to *derive* the arch (`isAdultDentition(patient.dentition)`, or a fiche's own `isAdultTeeth` when
 * editing) and offered no control. That made the mixed stage — roughly ages 6–12, and the better-reimbursed CNAM
 * band — **unchartable**: an eight-year-old's carie on a permanent 36 had nowhere to go, and neither did the
 * remaining baby teeth of a child charted as `Adult`. The server never had that limit
 * (`DentalRecordActParser`: « a mixed-dentition visit … is recordable. The record's `IsAdultTeeth` flag is a
 * **display hint, not a constraint** »), so this was the rare case of the UI being narrower than the data.
 *
 * ## Seeding is not locking
 *
 * The derivation existed for a real reason and it is kept: a fiche saved on baby teeth must **reopen** on baby
 * teeth, or every act it holds reads as "on the other dentition" and the chart opens empty. So callers seed the
 * value (from the fiche's own acts when editing, else from the patient) and this control lets the user override it
 * from then on. Seeding answers the first frame; the switch owns every frame after.
 *
 * Three segments in a row, each growing its own box on a coarse pointer (`coarse:py-3`) rather than overlaying a
 * `.touch-target` — the segments are adjacent, and an overlay would overhang its neighbours and steal their taps.
 */
export function DentitionViewSwitch({ value, onChange, disabled, className }: DentitionViewSwitchProps) {
  return (
    <div
      role="group"
      aria-label="Dentition affichée"
      /* ⚠️ `gap-0.5` below `sm:` is a measured 2 px, and it is only defensible because the row it lives in now
         WRAPS rather than overflows. Side by side the two switches need 128 + 159 + 8 = 295 px against the
         294 px the odontogramme's control row has at 390 px — short by one pixel, which is not a fit anybody
         should rely on. Tightening the two internal gaps and the wrapper's brings it to 287, a 7 px margin;
         and if a relabel ever eats that margin the pair drops to a second row instead of painting a segment
         outside the card, which is what the previous `min-w-0` attempt did. */
      className={cn("flex items-center gap-0.5 rounded-md border bg-muted/40 p-1 sm:gap-1", className)}
    >
      {DENTITION_VIEWS.map((view) => (
        <button
          key={view}
          type="button"
          disabled={disabled}
          onClick={() => onChange(view)}
          aria-pressed={value === view}
          className={cn(
            // `px-1.5` below `sm:` for the measured reason `OdontogramViewSwitch` carries — the two share a row
            // above the odontogramme and must stay the same object.
            "flex-1 rounded px-1.5 py-1.5 text-xs font-medium transition-colors sm:px-2 coarse:py-3 disabled:cursor-not-allowed disabled:opacity-60",
            value === view
              ? "bg-background text-foreground shadow-sm"
              : "text-muted-foreground hover-hover:hover:text-foreground",
          )}
        >
          {DENTITION_VIEW_LABELS_FR[view]}
        </button>
      ))}
    </div>
  )
}
