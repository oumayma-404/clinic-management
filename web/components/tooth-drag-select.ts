"use client"

import { useCallback, useEffect, useRef, useState } from "react"

/**
 * **Ticking a run of teeth by dragging across them**, on a mouse and on a finger.
 *
 * <p>« Plusieurs dents » already lets one diagnosis be charted onto several teeth, but every tooth had to be
 * tapped individually — so a whole quadrant was eight separate presses, and the mode that exists to make
 * charting faster made the commonest case the slowest. Dragging is what both pointers already do.</p>
 *
 * ## ⚠️ The gesture no longer needs a mode, and that is the point
 *
 * <p>It shipped gated on « Plusieurs dents » being ON, and the sentence that taught it (« Glissez pour cocher
 * une série ») was rendered only WHILE the mode was on — so the one thing that told you the gesture existed
 * was behind knowing the gesture existed. The dentist's report was « it isn't noticeable ». The drag is now
 * unconditional on both charts and the toggle became a selection READOUT, which is a change of job, not a
 * deletion: see `odontogram.tsx`, and ⚠️ note that the toggle also stays the ONLY keyboard route into
 * multi-select, because a drag is pointer-only.</p>
 *
 * ## It follows `agenda-grid-drag.ts`, deliberately
 *
 * That hook solved the same three problems for the agenda and its solutions are the ones documented and
 * enforced. Copying its *reasoning* rather than re-deriving it:
 *
 * - **Hit-testing through `elementFromPoint`**, never per-cell `pointerenter`. A drag crosses cells and may
 *   leave the arch entirely; one lookup per pointer event is also the only shape that answers « the pointer is
 *   over no tooth », which must paint nothing rather than reuse the last one.
 * - **A finger arms on a long press, a mouse on movement.** ⚠️ The arch lives in `ToothArchLayout`'s
 *   `overflow-x-auto` scroll box — at 390 px sixteen teeth do not fit — so a touch drag that armed on contact
 *   would take the horizontal scroll away and make teeth 18–15 unreachable, which is the exact defect
 *   `arch-clipping` exists to prevent. Below the threshold the touch belongs to the scroller.
 * - **A non-passive `touchmove` `preventDefault` is the only thing that stops the scroll** once armed. Setting
 *   `touch-action` mid-gesture is ignored by every engine, so the class-based version compiles, reads correctly
 *   and does nothing.
 *
 ## Range semantics, not a paint trail
 *
 * ⚠️ The drag selects **every tooth between the one it started on and the one under the pointer now**, not the
 * ones the pointer happened to touch. Two reasons, and the first is a real defect the browser found: pointer
 * moves are sampled, so a quick flick across a quadrant fires three events and a trail would tick the first
 * tooth, the last, and one in the middle — silently leaving gaps in exactly the fast gesture this exists for.
 * The second is intent: « toute la série » is a range, and re-crossing a tooth on the way back must narrow the
 * range rather than un-tick it.
 *
 * ⚠️ The direction is decided **once**, from whether the anchor tooth was already ticked, so a drag either
 * selects or clears — never a mixture. It is the spreadsheet rule.
 *
 * ⚠️ The range is clamped to **one arch row**: the DOM holds both arches in one container, so a drag that
 * strayed downward would otherwise sweep in every remaining upper tooth on its way. Rows are told apart by the
 * cell's own `top`, which needs no extra prop and survives the layout swapping to one arch on a phone.
 */

/** The attribute every tooth cell carries so a drag can ask the document which tooth it is over. */
export const TOOTH_CELL_ATTR = "data-tooth"

/** See `agenda-grid-drag.ts` — the platform long-press convention, and not configurable for the same reason. */
const LONG_PRESS_MS = 350

/** Past this the touch is a scroll: the pending gesture is abandoned outright, never re-armed. */
const LONG_PRESS_SLOP_PX = 10

/** Enough to tell a mouse drag from the jitter of a click. */
const MOUSE_DRAG_SLOP_PX = 4

interface ToothDragSelectOptions {
  /** Only « Plusieurs dents » drags. Outside it a tooth press opens that tooth's editor. */
  enabled: boolean
  /** Is this tooth currently ticked? Read once, on the tooth the gesture starts on. */
  isSelected: (tooth: number) => boolean
  /** Tick or untick one tooth. Called at most once per tooth per gesture. */
  onPaint: (tooth: number, select: boolean) => void
}

export interface ToothDragSelectHandle {
  /** Spread onto the element wrapping the arch. */
  containerProps: {
    ref: (node: HTMLDivElement | null) => void
    onPointerDown: (e: React.PointerEvent) => void
  }
  /**
   * True while the `click` that follows a drag is still to come, so a tooth's own `onClick` can stand down.
   *
   * ⚠️ Load-bearing: `pointerup` fires before `click`, so without it the last tooth of every drag is painted
   * by the gesture and then immediately toggled back by its own handler.
   */
  didConsumeGesture: () => boolean
}

const cellAt = (x: number, y: number): Element | null =>
  document.elementFromPoint(x, y)?.closest(`[${TOOTH_CELL_ATTR}]`) ?? null

const toothOf = (cell: Element | null): number | null => {
  const raw = cell?.getAttribute(TOOTH_CELL_ATTR)
  const tooth = raw ? Number(raw) : NaN
  return Number.isFinite(tooth) ? tooth : null
}

/** Every tooth cell in the arch, in document order — which is the order they are laid out in. */
const cellsIn = (container: HTMLElement): Element[] =>
  [...container.querySelectorAll(`[${TOOTH_CELL_ATTR}]`)]

/** Same arch row? Compared on the cell's own top, within half a cell. */
const sameRow = (a: Element, b: Element): boolean => {
  const ra = a.getBoundingClientRect()
  const rb = b.getBoundingClientRect()
  return Math.abs(ra.top - rb.top) < ra.height / 2
}

export function useToothDragSelect({ enabled, isSelected, onPaint }: ToothDragSelectOptions): ToothDragSelectHandle {
  /*
   * ⚠️ **State, not a ref, and this is the whole reason the gesture works at all.** The chart renders a loader
   * while the odontogram read is in flight, so on the first effect pass the wrapper is not in the DOM — and a
   * `useRef` would leave the listeners unattached for ever, because nothing in the dependency list changes when
   * the node later mounts. A callback ref that sets state re-runs the effect on attach. The symptom was a drag
   * that did nothing, with no error anywhere.
   */
  const [container, setContainer] = useState<HTMLDivElement | null>(null)

  /*
   * Everything about a live gesture lives in ONE ref, never in state: a drag fires several pointer events per
   * frame, and re-rendering the whole arch on each of them is the jank this avoids — the same reason the mesh
   * viewer reaches its loop through refs.
   */
  const drag = useRef<{
    armed: boolean
    pointerId: number
    startX: number
    startY: number
    touch: boolean
    /** What the drag is applying: the opposite of what the tooth it began on was. */
    select: boolean
    /**
     * The TOOTH the gesture started on — one end of the range, for its whole life.
     *
     * ⚠️ **A number, never the `Element`, and that distinction is a defect that shipped.** The first painted
     * tooth turns « Plusieurs dents » on, and that swaps every cell from the editor branch to the checkbox
     * branch — i.e. React REPLACES all 32 DOM nodes mid-gesture. Holding the element left the anchor pointing
     * at a detached node, so `cells.indexOf(anchor)` returned -1 and `applyRange` bailed out for the whole rest
     * of the drag: the range froze at the anchor plus the one tooth that had triggered the paint, so **every
     * drag selected exactly 2 teeth** however far it went. Reported from real use, and invisible to `tsc`,
     * `check:responsive` and a DOM-query probe alike — only a hand on the mouse finds it.
     */
    anchorTooth: number | null
    /** Everything the gesture has set so far, so narrowing the range puts the difference back. */
    applied: Set<number>
    timer: number | null
  } | null>(null)

  /** Set on release, cleared by the click that follows — or by a timeout, if none does. */
  const consumed = useRef(false)

  const cancelPending = useCallback(() => {
    if (drag.current?.timer != null) {
      window.clearTimeout(drag.current.timer)
      drag.current.timer = null
    }
  }, [])

  /**
   * Re-apply the range anchor → pointer.
   *
   * ⚠️ It **reverses** anything it previously set that is no longer in the range, so dragging out and back
   * narrows the selection instead of leaving a tail behind. That is what makes the gesture correctable without
   * starting over.
   */
  const applyRange = useCallback(
    (x: number, y: number) => {
      const state = drag.current
      if (!state?.armed || !container || state.anchorTooth == null) return

      const cells = cellsIn(container)
      // Re-resolved on every move, because the nodes may have been replaced since the gesture began.
      const from = cells.findIndex((c) => toothOf(c) === state.anchorTooth)
      if (from < 0) return

      const overCell = cellAt(x, y)
      // Over no tooth: hold the range as it stands rather than collapsing it — a pointer crossing the gap
      // between two cells must not make the selection flicker.
      const to = overCell ? cells.indexOf(overCell) : -1
      if (to < 0 || !sameRow(cells[from], cells[to])) return

      const wanted = new Set<number>()
      for (let i = Math.min(from, to); i <= Math.max(from, to); i++) {
        const tooth = toothOf(cells[i])
        if (tooth != null) wanted.add(tooth)
      }

      for (const tooth of state.applied) {
        if (!wanted.has(tooth)) {
          onPaint(tooth, !state.select)
          state.applied.delete(tooth)
        }
      }
      for (const tooth of wanted) {
        if (!state.applied.has(tooth)) {
          onPaint(tooth, state.select)
          state.applied.add(tooth)
        }
      }
    },
    [container, onPaint],
  )

  /**
   * Arm the gesture, anchored on the tooth the press STARTED on — not the one the pointer is over now.
   *
   * ⚠️ **The caller decides WHEN, and the rule is « the pointer has reached a second tooth ».** The mode
   * used to be the thing that made a drag a drag; without it, `MOUSE_DRAG_SLOP_PX` alone meant a 4 px hand
   * tremor or a trackpad tap with drift ticked the tooth instead of opening its editor — and on the
   * odontogramme the editor is the primary action. « Left the tooth it began on » is both the fix and the
   * honest semantics: a gesture that never leaves its anchor **is** a click, and dragging out and back to the
   * start behaves the same way.
   */
  const arm = useCallback(
    (anchorX: number, anchorY: number) => {
      const state = drag.current
      if (!state || state.armed) return
      const cell = cellAt(anchorX, anchorY)
      const tooth = toothOf(cell)
      if (tooth == null) {
        drag.current = null
        return
      }
      state.armed = true
      state.anchorTooth = tooth
      state.select = !isSelected(tooth)
    },
    [isSelected],
  )

  const end = useCallback(() => {
    cancelPending()
    /*
     * ⚠️ **`applied.size`, not `armed`** — and with the mode gone this is what keeps a long press alive.
     * A finger held still on a tooth arms after 350 ms and, under `arm`'s new « reached a second tooth » rule,
     * paints nothing. Swallowing the click on `armed` alone therefore made a long press do NOTHING AT ALL: no
     * editor, no selection, no feedback of any kind. A gesture that painted nothing has no click to protect.
     */
    if (drag.current?.applied.size) {
      consumed.current = true
      // A safety net: if no click follows (a release outside the arch, a cancelled gesture) the flag must not
      // survive to swallow the user's next deliberate tap.
      window.setTimeout(() => {
        consumed.current = false
      }, 400)
    }
    drag.current = null
  }, [cancelPending])

  const onPointerDown = useCallback(
    (e: React.PointerEvent) => {
      if (!enabled || e.button !== 0) return
      const touch = e.pointerType !== "mouse"
      drag.current = {
        armed: false,
        pointerId: e.pointerId,
        startX: e.clientX,
        startY: e.clientY,
        touch,
        select: true,
        anchorTooth: null,
        applied: new Set(),
        timer: null,
      }
      if (touch) {
        const { clientX, clientY } = e
        drag.current.timer = window.setTimeout(() => arm(clientX, clientY), LONG_PRESS_MS)
      }
    },
    [enabled, arm],
  )

  useEffect(() => {
    const node = container
    if (!node || !enabled) return

    const onMove = (e: PointerEvent) => {
      const state = drag.current
      if (!state || e.pointerId !== state.pointerId) return
      const dx = Math.abs(e.clientX - state.startX)
      const dy = Math.abs(e.clientY - state.startY)

      if (!state.armed) {
        if (state.touch) {
          // Moved before the press landed: this is a scroll. Abandon rather than re-arm.
          if (Math.hypot(dx, dy) > LONG_PRESS_SLOP_PX) {
            cancelPending()
            drag.current = null
          }
          return
        }
        if (Math.hypot(dx, dy) > MOUSE_DRAG_SLOP_PX) arm(state.startX, state.startY)
        if (!drag.current?.armed) return
      }

      /*
       * ⚠️ Nothing is painted until the pointer is over a DIFFERENT cell from the anchor — see `arm`. Once
       * it has been, `applyRange` runs on every move including a return to the anchor, so narrowing the range
       * back down to the single starting tooth still works; only the very first paint is gated.
       */
      const state2 = drag.current
      if (!state2?.armed) return
      if (state2.applied.size === 0 && toothOf(cellAt(e.clientX, e.clientY)) === state2.anchorTooth) return
      applyRange(e.clientX, e.clientY)
    }

    /*
     * ⚠️ Non-passive, and this listener is the only thing that stops the arch scrolling under an armed drag.
     * React's own `onTouchMove` is registered passively, so `preventDefault` there is a no-op the console
     * warns about and nothing else.
     */
    const onTouchMove = (e: TouchEvent) => {
      if (drag.current?.armed) e.preventDefault()
    }

    node.addEventListener("pointermove", onMove)
    node.addEventListener("pointerup", end)
    node.addEventListener("pointercancel", end)
    node.addEventListener("touchmove", onTouchMove, { passive: false })
    window.addEventListener("pointerup", end)

    return () => {
      node.removeEventListener("pointermove", onMove)
      node.removeEventListener("pointerup", end)
      node.removeEventListener("pointercancel", end)
      node.removeEventListener("touchmove", onTouchMove)
      window.removeEventListener("pointerup", end)
    }
  }, [container, enabled, arm, applyRange, end, cancelPending])

  const didConsumeGesture = useCallback(() => {
    if (!consumed.current) return false
    consumed.current = false
    return true
  }, [])

  return {
    containerProps: {
      ref: setContainer,
      onPointerDown,
    },
    didConsumeGesture,
  }
}
