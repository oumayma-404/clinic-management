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
 * Temporaire / Mixte / **Définitive** — which arch the tooth chart draws.
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
      /* ⚠️ `gap-0.5` below `sm:` is a measured 2 px, and it is only defensible because the row it lives in
         WRAPS rather than overflows — which is now the phone's actual behaviour, not a contingency.

         The margin this note used to record is **spent**. « Adulte / Enfant / Mixte » measured 159 px and the
         pair fitted one row at 390 px with 7 px to spare; naming the dentures instead — « Temporaire / Mixte /
         Définitive », the vocabulary the patient field and the fiche already use — takes this switch to 213 px,
         so the pair needs 136 + 213 + 8 = **357 px** against the **308 px** the odontogramme's control row has
         at 390 px (238 px at 320 px). Measured in the browser: it drops to a second row at both, one row from
         820 px up, with no horizontal scroll on `<main>` at any width.

         That costs the chart ~44 px of chrome on a phone, and it is accepted rather than worked around: the
         alternative is abbreviating a clinical label, and § 10.1 is explicit that the label is the control's
         name. Do **not** add `min-w-0` to buy the row back — that is the previous attempt, and it painted
         « Mixte » outside the card and gave `<main>` a horizontal scrollbar (see `odontogram.tsx:551-568`). */
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
