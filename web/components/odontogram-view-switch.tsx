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
 * same two sources and neither derives anything of its own: the charted `ToothStateDto[]` — for which
 * `odontogram-conditions.ts` remains the single vocabulary, and `tooth-symbols.tsx` is keyed on the same
 * condition names with a build check over both sets — **and** the recorded acts that charted no state at all
 * (`odontogram-recorded-acts.ts`), which each drawing renders in its own idiom.
 *
 * ⚠️ That second source is not an extension of the first and must not become one. Nearly half of all recorded
 * work produces no `ToothCondition` — by design, since « Coiffage pulpaire » charts nothing and a multi-séance
 * act's state is withheld until it finishes — so a chart reading tooth states alone showed a treated tooth as
 * untouched, in **both** drawings. `check:responsive`'s `recorded-act-reaches-both-drawings` is what stops one
 * of them being wired and the other forgotten, which is the exact shape of this repo's dominant defect.
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
      /* ⚠️ `gap-0.5` below `sm:` is a measured 2 px, and it is only defensible because the row it lives in now
         WRAPS rather than overflows. Side by side the two switches need 128 + 159 + 8 = 295 px against the
         294 px the odontogramme's control row has at 390 px — short by one pixel, which is not a fit anybody
         should rely on. Tightening the two internal gaps and the wrapper's brings it to 287, a 7 px margin;
         and if a relabel ever eats that margin the pair drops to a second row instead of painting a segment
         outside the card, which is what the previous `min-w-0` attempt did. */
      className={cn("flex items-center gap-0.5 rounded-md border bg-muted/40 p-1 sm:gap-1", className)}
    >
      {ODONTOGRAM_CHART_VIEWS.map((view) => (
        <button
          key={view}
          type="button"
          onClick={() => onChange(view)}
          aria-pressed={value === view}
          title={HINTS[view]}
          className={cn(
            /* ⚠️ `px-1.5` below `sm:` is a measured fit, not taste. At 390 px the odontogramme's control row
               has 294 px of content box, and the two switches come to 136 + 170 + 8 = 314 — so the pair wrapped
               onto a second row and the card spent 38 px of chrome on the wrap alone, directly above the teeth.
               6 px of horizontal padding per segment brings the pair inside 294. At 320 px they wrap again,
               which is the honest answer at the narrowest supported width. Kept identical in
               {@link DentitionViewSwitch} — the two sit in one row and must stay the same object. */
            "flex-1 rounded px-1.5 py-1.5 text-xs font-medium transition-colors sm:px-2 coarse:py-3",
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
