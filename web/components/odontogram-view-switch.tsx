"use client"

import { cn } from "@/lib/utils"

/** How the odontogram draws a tooth. `boxes` is the original chart and stays the default. */
export type OdontogramChartView = "boxes" | "symbols"

export const ODONTOGRAM_CHART_VIEWS: OdontogramChartView[] = ["boxes", "symbols"]

export const ODONTOGRAM_CHART_VIEW_LABELS_FR: Record<OdontogramChartView, string> = {
  boxes: "Cases",
  symbols: "Symboles",
}

const HINTS: Record<OdontogramChartView, string> = {
  boxes: "Une case par dent, colorée par état — la vue d'origine",
  symbols: "Une dent dessinée, un symbole par état, rouge = à faire",
}

interface OdontogramViewSwitchProps {
  value: OdontogramChartView
  onChange: (view: OdontogramChartView) => void
  className?: string
}

/**
 * Cases / Symboles — the two drawings of the same chart.
 *
 * ## Why both exist rather than one replacing the other
 *
 * They answer different questions, and neither answer is wrong. **Cases** paints one of fifteen condition hues
 * and is the denser of the two: at 28 px a coloured cell is the fastest way to find *a particular condition*
 * across thirty-two teeth. **Symboles** spends the colour on status instead — rouge « à faire », bleu
 * « réalisé » — and puts identity in the drawing, which is the only form that composes: a tooth that is
 * dévitalisée *and* couronnée shows both, where a fill can only ever show the most recent state.
 *
 * ⚠️ **The two must never disagree about the same fact**, which is the failure this pairing invites and the one
 * already on record between « Diagnostics » and « Actes réalisés » (two palettes for one chart). They read the
 * same `ToothStateDto[]` and neither derives anything of its own; `odontogram-conditions.ts` remains the single
 * vocabulary, and `tooth-symbols.tsx` is keyed on the same condition names with a build check over both sets.
 *
 * Two segments, each growing its own box on a coarse pointer (`coarse:py-3`) rather than taking a
 * `.touch-target` overlay — they are adjacent, and an overlay would overhang its neighbour and steal its taps.
 * Same construction as {@link DentitionViewSwitch}, deliberately: they sit side by side in the same row.
 */
export function OdontogramViewSwitch({ value, onChange, className }: OdontogramViewSwitchProps) {
  return (
    <div
      role="group"
      aria-label="Dessin de l'odontogramme"
      className={cn("flex items-center gap-1 rounded-md border bg-muted/40 p-1", className)}
    >
      {ODONTOGRAM_CHART_VIEWS.map((view) => (
        <button
          key={view}
          type="button"
          onClick={() => onChange(view)}
          aria-pressed={value === view}
          title={HINTS[view]}
          className={cn(
            "flex-1 rounded px-2 py-1.5 text-xs font-medium transition-colors coarse:py-3",
            value === view
              ? "bg-background text-foreground shadow-sm"
              : "text-muted-foreground hover-hover:hover:text-foreground",
          )}
        >
          {ODONTOGRAM_CHART_VIEW_LABELS_FR[view]}
        </button>
      ))}
    </div>
  )
}
