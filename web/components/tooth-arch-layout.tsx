"use client"

import { useState, type ReactNode } from "react"
import { cn } from "@/lib/utils"
import { useMediaQuery } from "@/lib/hooks/use-media-query"
import type { ToothQuadrants } from "@/components/tooth-multiselect"

/** Which arch is on screen. `both` is the desktop/tablet default; below `md:` one at a time (AC-33). */
export type ToothArch = "upper" | "lower"

interface ToothArchLayoutProps {
  /** The FDI quadrants to draw — `ADULT_TEETH` or `CHILD_TEETH` from `tooth-multiselect`, the one source. */
  teeth: ToothQuadrants
  /** How the caller draws one tooth. Everything interactive lives in here, on purpose — see below. */
  renderTooth: (toothNumber: number) => ReactNode
  /** Controlled arch, for a caller that wants to drive it. Omit and the layout keeps its own. */
  arch?: ToothArch
  onArchChange?: (arch: ToothArch) => void
  /**
   * Which arch to open on when the user has not chosen one — **derived from the data**, not a preference.
   *
   * <p>Below `md:` only one arch shows, and the layout used to hardcode `"upper"`. So a fiche whose acts are all
   * on lower molars, an odontogramme whose only charted teeth are 3x/4x, and the résumé of a patient treated
   * exclusively on the mandible all opened on an empty MAXILLAIRE — one wasted tap per open, every time, on the
   * device where the switch exists in the first place.</p>
   *
   * <p>Optional and late-binding: see the `internalArch` note below for why it is honoured even when it only
   * resolves after an async read lands.</p>
   */
  defaultArch?: ToothArch
  /** Override the arch captions. Defaults to « Maxillaire (haut) » / « Mandibule (bas) ». */
  labels?: { upper?: string; lower?: string }
  /**
   * Rendered inside the card, below the arches, on its own hairline — for content that describes *this chart*
   * rather than the page around it (the fiche's condition legend).
   *
   * <p>Deliberately outside the `overflow-x-auto` region: a legend that scrolled sideways with the teeth would
   * leave the colour key off screen exactly when a narrow viewport makes it hardest to read the chart.</p>
   */
  footer?: ReactNode
}

/**
 * The mouth's **geometry**, shared by the three charts that draw one: the odontogram's Diagnostics tab, its
 * Actes réalisés tab, and the fiche de soins' `RecordToothChart` (AC-34).
 *
 * ## What it deliberately does NOT take
 *
 * `paint`, `onToggleTooth`, `disabled`, `toothTitle`, `entries`, and any open/hover state stay with the
 * callers. That is not tidiness — pulling interaction up here breaks one of two real contracts:
 *
 * - **`odontogram-acts-chart` holds `tappedTooth`/`hoveredTooth` in the PARENT** (the `476a2e3` fix). Thirty-two
 *   cells sit a few pixels apart, so per-cell hover state stacked a panel for every tooth the pointer crossed.
 *   One owner, one open panel.
 * - **`record-tooth-chart` deliberately has no selection chrome**, which is what lets the read-only patient
 *   summary reuse it with `disabled` — and is why it uses a native `title` rather than a Radix tooltip, since a
 *   disabled button fires no pointer events and a tooltip would simply never open.
 *
 * So this component knows about **rows, gaps, the midline, the labels, the scroll box and which arch is
 * showing** — and nothing else.
 *
 * ## The scroll box is load-bearing (AC-32)
 *
 * ⚠️ `mx-auto w-max`, never `justify-center`. `justify-content: center` distributes overflow to **both** sides
 * and the inline-start overflow is **not in the scrollable region** — at 390 px that made teeth 18–15 and 48–45
 * unreachable by any means. `w-max` sizes the block to its content and `mx-auto` still centres it while there
 * is room; once the content is wider the auto margins collapse to zero, so the arch starts at the scroll
 * origin. `arch-clipping` in `scripts/check-responsive.mjs` is enforced and fails the build if the old class
 * comes back.
 */
export function ToothArchLayout({
  teeth,
  renderTooth,
  arch,
  onArchChange,
  defaultArch,
  labels,
  footer,
}: ToothArchLayoutProps) {
  /*
   * When one arch at a time is the only honest layout.
   *
   * Below `md:` an adult arch is ~597px of cells, so a phone would show half of it and the other half would have to
   * be scrolled to — reachable since AC-32, but never both at once.
   *
   * ⚠️ **The second clause is the fix for the 768–1023px band.** The gate used to be `(max-width: 767px)` alone,
   * i.e. purely about *space* — while the thing that decides how much space the arch needs, `coarse:min-w-11` on
   * each cell, is a **pointer** query. On a tablet portrait (820px, coarse) the two disagreed: sixteen 44px cells
   * are ~740px of arch inside a ~532px content column, both arches drawn, and no switch offered because the width
   * query said "desktop room". Tying the switch to the same condition that widens the cells is what keeps the two
   * from diverging again. A 1180px tablet in landscape still shows both arches happily; a 1440px mouse machine is
   * untouched, since it never widened its cells in the first place.
   */
  const isNarrow = useMediaQuery("(max-width: 767px), (max-width: 1023px) and (pointer: coarse)")
  /*
   * `null` means "the user has not chosen", NOT "upper".
   *
   * ⚠️ That distinction is the whole reason `defaultArch` works. Seeding `useState(defaultArch ?? "upper")`
   * would freeze the answer at first render — and every one of these charts paints before its data arrives
   * (`RecordToothChart`'s `paint` is fed by an async odontogram read, the odontogram's `byTooth` by its own),
   * so the seed would be `undefined` on the frame that counts and the derived arch would never be honoured.
   * Resolving at render time instead lets a late `defaultArch` take effect, while any deliberate tap wins from
   * then on: once `internalArch` holds a value, nothing overwrites it.
   */
  const [internalArch, setInternalArch] = useState<ToothArch | null>(null)

  // Uncontrolled by default — none of the three charts has a reason to own this, and making them would have
  // meant the same three lines of state in three files.
  const shownArch = arch ?? internalArch ?? defaultArch ?? "upper"
  const selectArch = (next: ToothArch) => {
    setInternalArch(next)
    onArchChange?.(next)
  }

  const upperLabel = labels?.upper ?? "Maxillaire (haut)"
  const lowerLabel = labels?.lower ?? "Mandibule (bas)"

  const showUpper = !isNarrow || shownArch === "upper"
  const showLower = !isNarrow || shownArch === "lower"

  return (
    <div className="space-y-2">
      {/* The arch switch exists only where an arch does not fit. At `md:` and up both are drawn and a control
          offering to hide one would be a control with nothing to fix. */}
      {isNarrow && (
        <div
          role="group"
          aria-label="Arcade affichée"
          className="flex items-center gap-1 rounded-md border bg-muted/40 p-1"
        >
          {([
            ["upper", upperLabel],
            ["lower", lowerLabel],
          ] as const).map(([value, label]) => (
            <button
              key={value}
              type="button"
              onClick={() => selectArch(value)}
              aria-pressed={shownArch === value}
              className={cn(
                "touch-target flex-1 rounded px-3 py-1.5 text-sm font-medium transition-colors",
                shownArch === value
                  ? "bg-background text-foreground shadow-sm"
                  : "text-muted-foreground hover-hover:hover:text-foreground",
              )}
            >
              {label}
            </button>
          ))}
        </div>
      )}

      {/* The border and the ground moved OUT to this wrapper so `footer` can sit on a hairline inside the card
          while only the arches scroll. With no footer the rendering is identical to before. */}
      <div className="rounded-lg border border-border bg-card">
      <div className="overflow-x-auto p-3">
        <div className="mx-auto w-max">
          {showUpper && (
            <div className="space-y-1.5">
              <div className="text-center text-2xs font-medium text-muted-foreground">{upperLabel}</div>
              <div className="flex gap-2">
                <div className="flex gap-0.5">{teeth.upperRight.map(renderTooth)}</div>
                <div className="w-px bg-border" />
                <div className="flex gap-0.5">{teeth.upperLeft.map(renderTooth)}</div>
              </div>
            </div>
          )}

          {/* The midline only separates two things when two things are showing. */}
          {showUpper && showLower && <div className="my-2 border-t border-border" />}

          {showLower && (
            <div className="space-y-1.5">
              <div className="flex gap-2">
                <div className="flex gap-0.5">{teeth.lowerRight.map(renderTooth)}</div>
                <div className="w-px bg-border" />
                <div className="flex gap-0.5">{teeth.lowerLeft.map(renderTooth)}</div>
              </div>
              <div className="text-center text-2xs font-medium text-muted-foreground">{lowerLabel}</div>
            </div>
          )}
        </div>
      </div>
      {footer && <div className="border-t border-border px-3 py-1.5">{footer}</div>}
      </div>
    </div>
  )
}
