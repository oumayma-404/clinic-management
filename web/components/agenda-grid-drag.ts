"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import type { AppointmentDto } from "@/lib/api/types"

/**
 * The sub-hour unit both gestures snap to — the grid's own, not a new one: the hour rows are `HOUR_HEIGHT` tall
 * and every appointment length a clinic books is a multiple of a quarter of an hour.
 */
export const AGENDA_SNAP_MINUTES = 15

/**
 * How long a finger must rest before a drag begins.
 *
 * ⚠️ **The threshold is what keeps the grid usable at all on a phone.** The same container scrolls 24 hours
 * vertically *and*, in Jour, hosts the horizontal day-swipe — so a gesture that started on contact would take
 * every thumb drag away from both. Below this the touch belongs to the scroll container and we do nothing; above
 * it the pointer is unambiguously a press on one cell or one block.
 *
 * 350 ms is the platform long-press convention, and it is deliberately not configurable: a shorter value re-arms
 * the scroll conflict this exists to prevent.
 */
export const AGENDA_LONG_PRESS_MS = 350

/**
 * How far a finger may travel while waiting for the long press. Past this the touch is a scroll, so the pending
 * gesture is abandoned outright — never re-armed, or a flick through the day would end on a booking dialog.
 */
const LONG_PRESS_SLOP_PX = 10

/**
 * A mouse arms on movement rather than on time: dragging is what a mouse does, and 350 ms of stillness before the
 * calendar responds reads as lag. 4 px is enough to tell a drag from the jitter of a click.
 */
const MOUSE_DRAG_SLOP_PX = 4

/**
 * The attribute every hour cell carries so a drag can ask the document which cell it is over.
 *
 * Hit-testing through `elementFromPoint` rather than per-cell `pointerenter` handlers, because a week grid has
 * **168** cells and a move may cross day columns: one lookup per pointer event beats 168 subscriptions, and it is
 * the only shape that also answers « the pointer is over no cell at all » — the release-outside case.
 */
export const AGENDA_CELL_ATTR = "data-agenda-cell"

/**
 * Marks a control **inside** an appointment block that must keep its own press.
 *
 * ⚠️ Not tidiness: a block hosts the statut popover and the « Envoyer » action, and `pointerdown` bubbles. Those
 * controls already `stopPropagation` on **click**, which is a different event and does nothing here — so without
 * this, holding the statut trigger for the long press would start carrying the appointment around while its
 * popover opened underneath.
 */
export const AGENDA_NO_DRAG_ATTR = "data-agenda-no-drag"

/** One resolved point on the grid: which day column, and which snapped minute of it. */
export interface AgendaCellTarget {
  /** `yyyy-MM-dd`, matching the calendar's own day-index keys. */
  dayKey: string
  /** Minutes from that day's midnight. Snapped to {@link AGENDA_SNAP_MINUTES} whenever it came from a cell. */
  minutes: number
}

/** The provisional span painted while the user drags across empty hours. Always within one day column. */
export interface AgendaCreateSelection {
  dayKey: string
  /** Inclusive start, snapped. */
  fromMinutes: number
  /** Exclusive end, snapped. Equal to `fromMinutes` while the drag has not left its first unit. */
  toMinutes: number
}

/** The appointment currently being dragged, and where it would land if released now. */
export interface AgendaMoveDrag {
  appointment: AppointmentDto
  target: AgendaCellTarget
}

interface UseAgendaGridDragOptions {
  /**
   * Whether the gestures exist at all. The caller passes `false` for Mois, for the phone's Semaine strip and while
   * the grid is loading — there are no cells to drag across in any of those.
   */
  enabled: boolean
  /** A finger, not a mouse — the JS twin of the `coarse:` variant. Decides long-press versus movement arming. */
  coarsePointer: boolean
  /** The scrolling grid. Used to decide whether a release still counts as being over the grid. */
  containerRef: React.RefObject<HTMLElement | null>
  /**
   * Anything that moves every cell out from under an in-flight drag — the rendered hour window, chiefly. A change
   * **cancels** rather than remaps: under « afficher les 24 heures » every cell's position changes at once, so a
   * remapped drag would silently mean a different span than the one the user was painting.
   */
  geometryKey: string
  /** A completed span. `durationMinutes` is always a positive multiple of {@link AGENDA_SNAP_MINUTES}. */
  onCreateSpan: (dayKey: string, startMinutes: number, durationMinutes: number) => void
  /**
   * A press on a cell that never became a drag — today's plain click, and deliberately carrying the **hour**
   * rather than the snapped quarter, so « cliquer sur une heure » keeps meaning exactly what it always has.
   */
  onCellClick: (dayKey: string, hour: number) => void
  /** A completed move. The caller persists it; this hook never touches the network. */
  onMoveDrop: (appointment: AppointmentDto, target: AgendaCellTarget) => void
}

type Gesture =
  | {
      kind: "create"
      pointerId: number
      coarse: boolean
      armed: boolean
      startX: number
      startY: number
      origin: AgendaCellTarget
      originHour: number
      last: AgendaCellTarget
    }
  | {
      kind: "move"
      pointerId: number
      coarse: boolean
      armed: boolean
      startX: number
      startY: number
      appointment: AppointmentDto
      last: AgendaCellTarget
    }

const clamp = (value: number, min: number, max: number) => Math.min(max, Math.max(min, value))

/** The snapped minute of `cell` a viewport `clientY` falls on. */
function minutesWithinCell(cell: HTMLElement, clientY: number): number | null {
  const dayKey = cell.dataset.agendaDay
  const hour = Number(cell.dataset.agendaHour)
  if (!dayKey || !Number.isFinite(hour)) return null
  const rect = cell.getBoundingClientRect()
  if (rect.height <= 0) return null
  // `0.999`, not `1`: a pointer exactly on the row's bottom edge belongs to the last unit of THIS hour, not to
  // the first unit of a row it is not yet over.
  const within = clamp((clientY - rect.top) / rect.height, 0, 0.999)
  return hour * 60 + Math.floor((within * 60) / AGENDA_SNAP_MINUTES) * AGENDA_SNAP_MINUTES
}

/**
 * Which cell a viewport point is over, and which snapped minute of it.
 *
 * ⚠️ `null` means "over no cell", and a caller must treat that as *cancel* rather than as *unchanged*: a release
 * over the toolbar or off the window is an edge case the spec names, and silently reusing the last known cell
 * there would book a visit the user was in the act of abandoning.
 */
function resolveCell(clientX: number, clientY: number): AgendaCellTarget | null {
  if (typeof document === "undefined") return null
  const under = document.elementFromPoint(clientX, clientY)
  const cell = under instanceof Element ? under.closest<HTMLElement>(`[${AGENDA_CELL_ATTR}]`) : null
  if (!cell) return null
  const minutes = minutesWithinCell(cell, clientY)
  const dayKey = cell.dataset.agendaDay
  return minutes === null || !dayKey ? null : { dayKey, minutes }
}

/**
 * The two pointer gestures a calendar is expected to have: drag across empty hours to book a span, drag a block to
 * move it.
 *
 * **It owns the gesture and nothing else.** No fetching, no geometry constants, no rendering — the calendar keeps
 * all three, and this returns the two provisional states it paints plus the handlers it attaches. That split is
 * what lets the hour window, the phone's taller rows and the week's column bands stay in one place.
 *
 * ⚠️ **It deliberately does not own the plain click on an appointment block.** The phone's block is a real
 * `<button>`, so Enter and Space produce a `click` and no pointer events at all — claiming the tap here would have
 * left the keyboard route working only by accident. The block keeps its own `onClick` and guards it with
 * {@link didConsumeGesture}, which is the one thing a click cannot know about the drag that preceded it.
 */
export function useAgendaGridDrag({
  enabled,
  coarsePointer,
  containerRef,
  geometryKey,
  onCreateSpan,
  onCellClick,
  onMoveDrop,
}: UseAgendaGridDragOptions) {
  const [createSelection, setCreateSelection] = useState<AgendaCreateSelection | null>(null)
  const [moveDrag, setMoveDrag] = useState<AgendaMoveDrag | null>(null)
  const gestureRef = useRef<Gesture | null>(null)
  const longPressTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  /**
   * Did a drag actually happen since the pointer went down?
   *
   * Two readers, and neither could work without it. The calendar's day-swipe fires on `touchend`, i.e. *after* our
   * `pointerup` has cleared the gesture — so dragging an appointment leftwards in Jour would otherwise also read
   * as « swipe to the previous day » and the grid would jump out from under the drop. And the `click` a browser
   * synthesises after a drag would re-open the edit dialog of the appointment just moved.
   */
  const consumedRef = useRef(false)

  const cancelLongPress = useCallback(() => {
    if (longPressTimerRef.current !== null) {
      clearTimeout(longPressTimerRef.current)
      longPressTimerRef.current = null
    }
  }, [])

  /** Abandon whatever is in flight without acting on it. */
  const cancelGesture = useCallback(() => {
    cancelLongPress()
    gestureRef.current = null
    setCreateSelection(null)
    setMoveDrag(null)
  }, [cancelLongPress])

  // A cell's position is meaningless once the rendered hour window moves, and so is a drag measured against it.
  useEffect(() => {
    cancelGesture()
  }, [geometryKey, cancelGesture])

  // Nothing may survive the gestures being switched off (a view change, a fetch starting) or the unmount.
  useEffect(() => {
    if (!enabled) cancelGesture()
  }, [enabled, cancelGesture])
  useEffect(() => () => cancelLongPress(), [cancelLongPress])

  const arm = useCallback(() => {
    const gesture = gestureRef.current
    if (!gesture || gesture.armed) return
    gesture.armed = true
    consumedRef.current = true
    if (gesture.kind === "create") {
      setCreateSelection({
        dayKey: gesture.origin.dayKey,
        fromMinutes: gesture.origin.minutes,
        toMinutes: gesture.origin.minutes,
      })
    } else {
      setMoveDrag({ appointment: gesture.appointment, target: gesture.last })
    }
  }, [])

  const applyPoint = useCallback((clientX: number, clientY: number) => {
    const gesture = gestureRef.current
    if (!gesture) return
    const target = resolveCell(clientX, clientY)
    if (!target) return

    if (gesture.kind === "create") {
      // The day is pinned to the press: a span is a statement about one day, and a diagonal drag that silently
      // re-columned it would book the visit somewhere the user was not looking.
      if (target.dayKey !== gesture.origin.dayKey) return
      gesture.last = target
      if (!gesture.armed) return
      setCreateSelection({
        dayKey: gesture.origin.dayKey,
        fromMinutes: Math.min(gesture.origin.minutes, target.minutes),
        toMinutes: Math.max(gesture.origin.minutes, target.minutes),
      })
      return
    }

    gesture.last = target
    if (gesture.armed) setMoveDrag({ appointment: gesture.appointment, target })
  }, [])

  /**
   * The gesture's whole lifetime, on `window` rather than on the element.
   *
   * Pointer capture would be the shorter spelling and is the wrong one: it retargets every subsequent event to the
   * captured element, so `elementFromPoint` would still be needed *and* a release outside the grid would arrive
   * looking exactly like a release on the origin cell — which is the one case that must cancel.
   */
  useEffect(() => {
    if (typeof window === "undefined") return

    const onPointerMove = (event: PointerEvent) => {
      const gesture = gestureRef.current
      if (!gesture || event.pointerId !== gesture.pointerId) return

      if (!gesture.armed) {
        const travelled = Math.hypot(event.clientX - gesture.startX, event.clientY - gesture.startY)
        if (gesture.coarse) {
          if (travelled > LONG_PRESS_SLOP_PX) cancelGesture()
          return
        }
        if (travelled < MOUSE_DRAG_SLOP_PX) return
        arm()
      }

      applyPoint(event.clientX, event.clientY)
    }

    const onPointerUp = (event: PointerEvent) => {
      const gesture = gestureRef.current
      if (!gesture || event.pointerId !== gesture.pointerId) return
      cancelLongPress()
      gestureRef.current = null
      setCreateSelection(null)
      setMoveDrag(null)

      // An unarmed release on a block is left to the block's own `onClick` (see the hook's note above); an
      // unarmed release on a cell is this hook's, because a cell is a plain div with no click handler of its own.
      if (!gesture.armed) {
        if (gesture.kind === "create") onCellClick(gesture.origin.dayKey, gesture.originHour)
        return
      }

      /*
       * Where the pointer actually let go. Over no cell cancels — unless it is still inside the grid's own box,
       * where the last cell the drag was over is the honest answer (releasing past the final hour row, or over the
       * sticky day header, both land here).
       */
      const bounds = containerRef.current?.getBoundingClientRect()
      const insideGrid =
        bounds !== undefined &&
        event.clientX >= bounds.left &&
        event.clientX <= bounds.right &&
        event.clientY >= bounds.top &&
        event.clientY <= bounds.bottom
      const target = resolveCell(event.clientX, event.clientY) ?? (insideGrid ? gesture.last : null)
      if (!target) return

      if (gesture.kind === "create") {
        if (target.dayKey !== gesture.origin.dayKey) return
        const from = Math.min(gesture.origin.minutes, target.minutes)
        const to = Math.max(gesture.origin.minutes, target.minutes)
        // A drag that never left its own quarter-hour is a click that wobbled, and must behave as one — with no
        // duration override, which is what a span of zero would otherwise assert.
        if (to <= from) onCellClick(gesture.origin.dayKey, gesture.originHour)
        else onCreateSpan(gesture.origin.dayKey, from, to - from)
        return
      }

      onMoveDrop(gesture.appointment, target)
    }

    const onPointerCancel = (event: PointerEvent) => {
      const gesture = gestureRef.current
      if (!gesture || event.pointerId !== gesture.pointerId) return
      cancelGesture()
    }

    window.addEventListener("pointermove", onPointerMove)
    window.addEventListener("pointerup", onPointerUp)
    window.addEventListener("pointercancel", onPointerCancel)
    return () => {
      window.removeEventListener("pointermove", onPointerMove)
      window.removeEventListener("pointerup", onPointerUp)
      window.removeEventListener("pointercancel", onPointerCancel)
    }
  }, [applyPoint, arm, cancelGesture, cancelLongPress, containerRef, onCellClick, onCreateSpan, onMoveDrop])

  const dragActive = createSelection !== null || moveDrag !== null

  /**
   * Once a touch drag is armed, the finger must stop scrolling the day.
   *
   * ⚠️ A **non-passive** `touchmove` listener is the only thing that does this. Setting `touch-action: none` on the
   * container mid-gesture is ignored by every engine — the property is read when the gesture begins — so the
   * class-based version compiles, reads correctly and does nothing at all.
   */
  useEffect(() => {
    if (!dragActive || typeof document === "undefined") return
    const swallow = (event: TouchEvent) => event.preventDefault()
    document.addEventListener("touchmove", swallow, { passive: false })
    return () => document.removeEventListener("touchmove", swallow)
  }, [dragActive])

  const beginCellGesture = useCallback(
    (event: React.PointerEvent<HTMLElement>, dayKey: string, hour: number) => {
      if (!enabled || !event.isPrimary) return
      const minutes = minutesWithinCell(event.currentTarget, event.clientY)
      const origin: AgendaCellTarget = { dayKey, minutes: minutes ?? hour * 60 }
      consumedRef.current = false
      cancelLongPress()
      gestureRef.current = {
        kind: "create",
        pointerId: event.pointerId,
        coarse: coarsePointer,
        armed: false,
        startX: event.clientX,
        startY: event.clientY,
        origin,
        originHour: hour,
        last: origin,
      }
      if (coarsePointer) longPressTimerRef.current = setTimeout(arm, AGENDA_LONG_PRESS_MS)
    },
    [arm, cancelLongPress, coarsePointer, enabled],
  )

  /**
   * `origin` is where the block already sits, and the caller supplies it rather than this hook resolving it.
   *
   * ⚠️ Not a convenience: an appointment block is an absolutely-positioned sibling of the hour grid, not a child of
   * a cell, so `elementFromPoint` under the press returns the block and `closest()` finds no cell at all. Resolving
   * it here would leave a touch drag with no ghost until the finger moved — and a drop with no movement would have
   * nowhere to land instead of being the no-op it is.
   */
  const beginAppointmentGesture = useCallback(
    (event: React.PointerEvent<HTMLElement>, appointment: AppointmentDto, origin: AgendaCellTarget) => {
      if (!enabled || !event.isPrimary) return
      // A press that landed on one of the block's own controls belongs to that control, not to the block.
      if (event.target instanceof Element && event.target.closest(`[${AGENDA_NO_DRAG_ATTR}]`)) return
      consumedRef.current = false
      cancelLongPress()
      gestureRef.current = {
        kind: "move",
        pointerId: event.pointerId,
        coarse: coarsePointer,
        armed: false,
        startX: event.clientX,
        startY: event.clientY,
        appointment,
        last: origin,
      }
      if (coarsePointer) longPressTimerRef.current = setTimeout(arm, AGENDA_LONG_PRESS_MS)
    },
    [arm, cancelLongPress, coarsePointer, enabled],
  )

  /** Did the gesture that just ended move something? Read by the day-swipe and by the block's own `onClick`. */
  const didConsumeGesture = useCallback(() => consumedRef.current, [])

  return {
    createSelection,
    moveDrag,
    /** Any drag currently painting — the calendar makes blocks pointer-transparent and suppresses selection. */
    dragActive,
    beginCellGesture,
    beginAppointmentGesture,
    didConsumeGesture,
  }
}
