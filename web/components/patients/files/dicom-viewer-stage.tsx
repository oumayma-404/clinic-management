"use client"

import { useCallback, useEffect, useMemo, useRef, useState } from "react"

import { formatLength, renderFrame, type DicomWindow, type FrameStats } from "@/lib/files/dicom/window"
import type { DicomFrame, DicomStudy } from "@/lib/files/dicom/study"
import { cn } from "@/lib/utils"

/**
 * The picture, and every gesture that acts on it.
 *
 * <h3>The gesture conflict, and how it is resolved</h3>
 *
 * ⚠️ **Window/level by dragging competes with scrolling and with pinch-zoom on a coarse pointer, and this app's
 * primary device is a tablet at the chair.** Three decisions settle it, and none of them is a heuristic:
 *
 * 1. **The stage never scrolls.** `touch-action: none` and `overflow-hidden`: pan and zoom are this component's
 *    own transform, so there is no browser scroll for a drag to be mistaken for. § 11 asks that wide content
 *    scroll inside its own container — here the container does not scroll at all, it *transforms*, which is the
 *    same promise kept a different way. The page behind is a modal dialog, so nothing else wants the drag.
 * 2. **One finger does whatever the toolbar says, and the toolbar says so in words.** A hidden mode — one
 *    finger windows, two fingers pan — is undiscoverable and unlearnable through a glove. An explicit
 *    `radiogroup` (« Contraste / Déplacer / Mesurer ») is how every tablet radiology application does it, and it
 *    is the only shape that can be *read* rather than guessed.
 * 3. **Two fingers always pinch, whatever the tool** — and the tool's own change is **rolled back** when the
 *    second finger lands. A pinch begins as one finger touching down and travelling a few pixels before its
 *    partner arrives, so without the rollback every zoom would also nudge the contrast. The gesture snapshots
 *    the window, the pan and the measurement at the first pointer-down and restores all three.
 *
 * <p>A mouse gets the same tools plus the wheel (zoom about the cursor) — one model, not two, so nothing
 * behaves differently depending on what you happen to be holding.</p>
 *
 * ⚠️ **Every gesture reads its own baseline out of a ref, never out of React state.** A pinch or a wheel
 * produces several events inside one frame, and `zoom`/`pan` from the last render are one event behind for all
 * but the first of them — which reads as a picture that stutters and drifts under the fingers. So the pan has a
 * ref mirror written at the same time as the state, and every gesture is expressed relative to the values
 * captured when it *began* rather than to whatever the last render happened to see.
 */

/** Where in the image a measurement's ends are, in image pixels, so zoom and pan cannot move them. */
export interface Measurement {
  a: { x: number; y: number }
  b: { x: number; y: number }
}

export type DicomTool = "window" | "pan" | "measure"

/** How close, in CSS pixels, a pointer must be to grab an existing endpoint. A gloved finger needs the margin. */
const HANDLE_GRAB_PX = 22

/** The floor and ceiling on zoom, as multiples of « the whole frame fits ». */
const MIN_ZOOM = 0.5
const MAX_ZOOM = 40

interface Point {
  x: number
  y: number
}

export function DicomViewerStage({
  study,
  frame,
  stats,
  window: level,
  invert,
  tool,
  zoom,
  fitToken,
  measurement,
  onWindowChange,
  onZoomChange,
  onMeasurementChange,
  onStepFrame,
  onToggleInvert,
  className,
}: {
  study: DicomStudy
  frame: DicomFrame
  /** Null for a colour frame, which has no window and therefore no range to drag over. */
  stats: FrameStats | null
  window: DicomWindow
  invert: boolean
  tool: DicomTool
  /** 1 means « the whole frame fits »; the real scale is this times the fit scale. */
  zoom: number
  /** Bumped by « Ajuster » to recentre; also resets the pan. */
  fitToken: number
  measurement: Measurement | null
  onWindowChange: (next: DicomWindow) => void
  onZoomChange: (next: number) => void
  onMeasurementChange: (next: Measurement | null) => void
  onStepFrame: (delta: -1 | 1) => void
  onToggleInvert: () => void
  className?: string
}) {
  const hostRef = useRef<HTMLDivElement | null>(null)
  const canvasRef = useRef<HTMLCanvasElement | null>(null)

  /** The frame painted at its own resolution, windowed. Pan and zoom only ever `drawImage` from this. */
  const sourceRef = useRef<HTMLCanvasElement | null>(null)
  const rgbaRef = useRef<Uint8ClampedArray | null>(null)
  /** What the source canvas currently holds, so pan and zoom do not re-window six megapixels per frame. */
  const paintedRef = useRef<string>("")

  const [size, setSize] = useState({ width: 0, height: 0 })
  const [pan, setPan] = useState<Point>({ x: 0, y: 0 })

  // The ref mirrors this component's docstring: a gesture reads these, a render reads the state.
  const panRef = useRef<Point>(pan)
  const zoomRef = useRef(zoom)
  zoomRef.current = zoom
  const applyPan = useCallback((next: Point) => {
    panRef.current = next
    setPan(next)
  }, [])

  const live = useRef({ study, stats, level, tool, measurement, size })
  live.current = { study, stats, level, tool, measurement, size }

  const handlers = useRef({ onWindowChange, onZoomChange, onMeasurementChange, onStepFrame, onToggleInvert })
  handlers.current = { onWindowChange, onZoomChange, onMeasurementChange, onStepFrame, onToggleInvert }

  const dpr = useMemo(
    // Capped at 2: past it the compositing cost doubles again for a difference no eye resolves on a radiograph.
    () => (typeof globalThis.window === "undefined" ? 1 : Math.min(globalThis.window.devicePixelRatio || 1, 2)),
    [],
  )

  /** The scale at which the whole frame fits the stage. Zoom is a multiple of it, so « ×1 » always means « fits ». */
  const fitScale =
    size.width > 0 && size.height > 0 ? Math.min(size.width / study.columns, size.height / study.rows) : 1
  const scale = fitScale * zoom

  /** Where the image's top-left corner sits in the stage, in CSS pixels. */
  const origin = useMemo(
    () => ({
      x: (size.width - study.columns * scale) / 2 + pan.x,
      y: (size.height - study.rows * scale) / 2 + pan.y,
    }),
    [size.width, size.height, study.columns, study.rows, scale, pan.x, pan.y],
  )
  const originRef = useRef(origin)
  originRef.current = origin
  const scaleRef = useRef(scale)
  scaleRef.current = scale

  // ── the stage's own size, measured rather than assumed (§ 1: never a `window.innerWidth` snapshot) ────────
  useEffect(() => {
    const host = hostRef.current
    if (!host) return

    const observer = new ResizeObserver((entries) => {
      const box = entries[0]?.contentRect
      if (box) setSize({ width: Math.floor(box.width), height: Math.floor(box.height) })
    })
    observer.observe(host)
    return () => observer.disconnect()
  }, [])

  // « Ajuster » and a change of frame geometry both recentre.
  useEffect(() => {
    panRef.current = { x: 0, y: 0 }
    setPan({ x: 0, y: 0 })
  }, [fitToken, study.columns, study.rows])

  // ── paint ────────────────────────────────────────────────────────────────────────────────────────────────
  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas || size.width <= 0 || size.height <= 0) return

    // The windowed frame, rebuilt only when the window, the inversion or the frame itself moved.
    const signature = `${study.columns}x${study.rows}|${level.centre}|${level.width}|${invert}|${frameToken(frame)}`
    if (paintedRef.current !== signature) {
      const pixels = study.rows * study.columns
      rgbaRef.current = renderFrame(study, frame, level, invert, pixels, rgbaRef.current ?? undefined)

      const source = sourceRef.current ?? document.createElement("canvas")
      sourceRef.current = source
      if (source.width !== study.columns) source.width = study.columns
      if (source.height !== study.rows) source.height = study.rows

      const sourceContext = source.getContext("2d")
      if (!sourceContext) return
      // ⚠️ The re-wrap is not ceremony: a typed array's `buffer` is `ArrayBufferLike`, i.e. possibly shared,
      // and `ImageData` will not take one — the same cast `raster.ts` carries, for the same reason. Nothing
      // here ever runs on a `SharedArrayBuffer`.
      const view = new Uint8ClampedArray(
        rgbaRef.current.buffer as ArrayBuffer,
        rgbaRef.current.byteOffset,
        pixels * 4,
      )
      sourceContext.putImageData(new ImageData(view, study.columns, study.rows), 0, 0)
      paintedRef.current = signature
    }

    const source = sourceRef.current
    const context = canvas.getContext("2d")
    if (!source || !context) return

    // Assigning either dimension clears the canvas, so it is done only when the box actually changed.
    const targetWidth = Math.max(1, Math.round(size.width * dpr))
    const targetHeight = Math.max(1, Math.round(size.height * dpr))
    if (canvas.width !== targetWidth) canvas.width = targetWidth
    if (canvas.height !== targetHeight) canvas.height = targetHeight

    context.setTransform(dpr, 0, 0, dpr, 0, 0)
    context.clearRect(0, 0, size.width, size.height)

    // ⚠️ Smoothing OFF past 1:1, deliberately. Interpolating a radiograph invents intermediate greys, and a
    // reader zooming to the pixel is asking to see the sampling — not a smoother guess at it.
    context.imageSmoothingEnabled = scale < 1
    context.imageSmoothingQuality = "high"
    context.drawImage(source, origin.x, origin.y, study.columns * scale, study.rows * scale)

    if (measurement) drawMeasurement(context, measurement, origin, scale)
  }, [study, frame, level, invert, measurement, size, origin, scale, dpr])

  // ── gestures ─────────────────────────────────────────────────────────────────────────────────────────────
  const toImage = useCallback((clientX: number, clientY: number): Point => {
    const box = hostRef.current?.getBoundingClientRect()
    if (!box) return { x: 0, y: 0 }
    return {
      x: (clientX - box.left - originRef.current.x) / scaleRef.current,
      y: (clientY - box.top - originRef.current.y) / scaleRef.current,
    }
  }, [])

  /**
   * The pan that keeps the image point currently under `(localX, localY)` under it at `nextZoom`.
   *
   * Derived rather than approximated with a ratio: `origin` folds the centring term, which itself depends on
   * the scale, so a « multiply the offset » shortcut drifts a few pixels per step and visibly walks the image
   * off the pointer over a long pinch.
   */
  const panForZoomAbout = useCallback(
    (from: { zoom: number; pan: Point }, nextZoom: number, localX: number, localY: number): Point => {
      const { size: stage, study: current } = live.current
      const fit =
        stage.width > 0 && stage.height > 0
          ? Math.min(stage.width / current.columns, stage.height / current.rows)
          : 1

      const scaleFrom = fit * from.zoom
      const scaleNext = fit * nextZoom
      const originFrom = {
        x: (stage.width - current.columns * scaleFrom) / 2 + from.pan.x,
        y: (stage.height - current.rows * scaleFrom) / 2 + from.pan.y,
      }
      const image = { x: (localX - originFrom.x) / scaleFrom, y: (localY - originFrom.y) / scaleFrom }

      return {
        x: localX - image.x * scaleNext - (stage.width - current.columns * scaleNext) / 2,
        y: localY - image.y * scaleNext - (stage.height - current.rows * scaleNext) / 2,
      }
    },
    [],
  )

  useEffect(() => {
    const host = hostRef.current
    if (!host) return

    /** Live pointers, by id. Two or more means pinch, whatever the tool says. */
    const pointers = new Map<number, Point>()

    /** The single-pointer gesture, and everything it may have to put back. */
    let gesture:
      | null
      | {
          start: Point
          window: DicomWindow
          pan: Point
          measurement: Measurement | null
          /** Which end of a measurement is being dragged, when one was grabbed. */
          grabbed: "a" | "b" | null
          /** Set once two pointers have been down: this gesture pans from here on and never re-windows. */
          pinched: boolean
        }

    /** The two-pointer baseline, captured once and never re-based — see the component docstring. */
    let pinch: null | { spread: number; centre: Point; zoom: number; pan: Point } = null

    const localOf = (clientX: number, clientY: number): Point => {
      const box = host.getBoundingClientRect()
      return { x: clientX - box.left, y: clientY - box.top }
    }
    const centreOf = () => {
      const points = [...pointers.values()]
      return {
        x: points.reduce((sum, p) => sum + p.x, 0) / points.length,
        y: points.reduce((sum, p) => sum + p.y, 0) / points.length,
      }
    }
    const spreadOf = () => {
      const [a, b] = [...pointers.values()]
      return Math.hypot(a.x - b.x, a.y - b.y) || 1
    }
    const setZoom = (next: number) => {
      const clamped = Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, next))
      zoomRef.current = clamped
      handlers.current.onZoomChange(clamped)
      return clamped
    }

    const onPointerDown = (event: PointerEvent) => {
      host.setPointerCapture(event.pointerId)
      pointers.set(event.pointerId, { x: event.clientX, y: event.clientY })

      if (pointers.size === 1) {
        const { level: currentLevel, measurement: currentMeasurement, tool: currentTool } = live.current
        const image = toImage(event.clientX, event.clientY)
        let grabbed: "a" | "b" | null = null

        if (currentTool === "measure" && currentMeasurement) {
          // Measured in CSS pixels, so the grab radius is a real 22 px at every zoom.
          const near = (end: Point) =>
            Math.hypot((end.x - image.x) * scaleRef.current, (end.y - image.y) * scaleRef.current) <= HANDLE_GRAB_PX
          if (near(currentMeasurement.a)) grabbed = "a"
          else if (near(currentMeasurement.b)) grabbed = "b"
        }

        gesture = {
          start: { x: event.clientX, y: event.clientY },
          window: currentLevel,
          pan: panRef.current,
          measurement: currentMeasurement,
          grabbed,
          pinched: false,
        }

        // A fresh line starts collapsed at the touch point and grows with the drag.
        if (currentTool === "measure" && !grabbed) handlers.current.onMeasurementChange({ a: image, b: image })
        return
      }

      if (pointers.size === 2) {
        // ⚠️ The rollback that makes a pinch clean: undo whatever the first finger's travel did before its
        // partner arrived, then zoom. Without it every two-finger zoom also nudges the contrast.
        if (gesture) {
          handlers.current.onWindowChange(gesture.window)
          handlers.current.onMeasurementChange(gesture.measurement)
          applyPan(gesture.pan)
          gesture.pinched = true
        }
        pinch = {
          spread: spreadOf(),
          centre: centreOf(),
          zoom: zoomRef.current,
          pan: gesture?.pan ?? panRef.current,
        }
      }
    }

    const onPointerMove = (event: PointerEvent) => {
      if (!pointers.has(event.pointerId)) return
      pointers.set(event.pointerId, { x: event.clientX, y: event.clientY })

      if (pointers.size >= 2 && pinch) {
        const factor = spreadOf() / pinch.spread
        const zoomed = setZoom(pinch.zoom * factor)
        const from = localOf(pinch.centre.x, pinch.centre.y)
        const zoomPan = panForZoomAbout({ zoom: pinch.zoom, pan: pinch.pan }, zoomed, from.x, from.y)
        // Two fingers travelling together pan as well as pinch — the same as every map. Both are measured from
        // the one baseline, so the two never compound.
        const centre = centreOf()
        applyPan({
          x: zoomPan.x + (centre.x - pinch.centre.x),
          y: zoomPan.y + (centre.y - pinch.centre.y),
        })
        return
      }

      if (!gesture) return
      const dx = event.clientX - gesture.start.x
      const dy = event.clientY - gesture.start.y
      const { tool: currentTool, stats: currentStats, size: stage } = live.current

      // After a pinch the remaining finger pans: re-running the tool would re-apply a change the user has
      // already been shown the result of undoing.
      if (gesture.pinched || currentTool === "pan") {
        applyPan({ x: gesture.pan.x + dx, y: gesture.pan.y + dy })
        return
      }

      if (currentTool === "measure") {
        const image = toImage(event.clientX, event.clientY)
        const base = gesture.grabbed ? gesture.measurement : live.current.measurement
        if (!base) return
        handlers.current.onMeasurementChange(
          gesture.grabbed === "a"
            ? { ...base, a: image }
            : gesture.grabbed === "b"
              ? { ...base, b: image }
              : { a: base.a, b: image },
        )
        return
      }

      // Window/level. ⚠️ The sweep is scaled to the frame's OWN range, not to a fixed number of units: a CT in
      // Hounsfield units spans thousands and an 8-bit sensor spans 255, and one sensitivity for both makes the
      // drag either useless or unusable. One full-width sweep covers the whole range for the centre; one
      // full-height sweep does the same for the width.
      const range = currentStats ? Math.max(1, currentStats.high - currentStats.low) : 255
      handlers.current.onWindowChange({
        centre: gesture.window.centre + (dx / Math.max(1, stage.width)) * range,
        width: Math.max(1, gesture.window.width + (dy / Math.max(1, stage.height)) * range),
      })
    }

    const endPointer = (event: PointerEvent) => {
      pointers.delete(event.pointerId)
      if (pointers.size < 2) pinch = null
      if (pointers.size === 0) {
        gesture = null
        return
      }
      // One finger left after a pinch: re-baseline it against the pan the pinch finished on, so the picture
      // does not jump on the next move.
      if (gesture) {
        const [remaining] = [...pointers.values()]
        gesture = { ...gesture, start: remaining, pan: panRef.current, pinched: true }
      }
    }

    const onWheel = (event: WheelEvent) => {
      // ⚠️ Attached natively with `passive: false`: React registers its own `onWheel` passively at the root, so
      // `preventDefault` there is ignored and the page scrolls behind the dialog instead of the image zooming.
      event.preventDefault()
      const local = localOf(event.clientX, event.clientY)
      const from = { zoom: zoomRef.current, pan: panRef.current }
      const zoomed = setZoom(from.zoom * Math.exp(-event.deltaY / 400))
      applyPan(panForZoomAbout(from, zoomed, local.x, local.y))
    }

    host.addEventListener("pointerdown", onPointerDown)
    host.addEventListener("pointermove", onPointerMove)
    host.addEventListener("pointerup", endPointer)
    host.addEventListener("pointercancel", endPointer)
    host.addEventListener("wheel", onWheel, { passive: false })

    return () => {
      host.removeEventListener("pointerdown", onPointerDown)
      host.removeEventListener("pointermove", onPointerMove)
      host.removeEventListener("pointerup", endPointer)
      host.removeEventListener("pointercancel", endPointer)
      host.removeEventListener("wheel", onWheel)
    }
  }, [toImage, panForZoomAbout, applyPan])

  const onKeyDown = useCallback(
    (event: React.KeyboardEvent<HTMLDivElement>) => {
      switch (event.key) {
        case "ArrowLeft":
          event.preventDefault()
          handlers.current.onStepFrame(-1)
          break
        case "ArrowRight":
          event.preventDefault()
          handlers.current.onStepFrame(1)
          break
        case "+":
        case "=":
          event.preventDefault()
          handlers.current.onZoomChange(Math.min(MAX_ZOOM, zoomRef.current * 1.25))
          break
        case "-":
        case "_":
          event.preventDefault()
          handlers.current.onZoomChange(Math.max(MIN_ZOOM, zoomRef.current / 1.25))
          break
        case "0":
          event.preventDefault()
          handlers.current.onZoomChange(1)
          applyPan({ x: 0, y: 0 })
          break
        case "i":
        case "I":
          event.preventDefault()
          handlers.current.onToggleInvert()
          break
        default:
          break
      }
    },
    [applyPan],
  )

  const lengthLabel = measurement
    ? formatLength(study, measurement.b.x - measurement.a.x, measurement.b.y - measurement.a.y)
    : null

  return (
    <div
      ref={hostRef}
      tabIndex={0}
      role="group"
      // ⚠️ The keys are IN the accessible name, because there is nowhere else a keyboard user meets them: the
      // stage has no visible legend, and a `title` needs a hover this app's primary device cannot perform (§ 13).
      aria-label={
        `Image ${study.columns} × ${study.rows}. ` +
        "Flèches gauche et droite : image précédente ou suivante. Plus et moins : zoom. Zéro : ajuster. I : inverser."
      }
      aria-describedby={lengthLabel ? "dicom-measure-readout" : undefined}
      onKeyDown={onKeyDown}
      className={cn(
        // ⚠️ `relative` is not optional: a container that is `static` does not clip its own `absolute` children,
        // so the overlays below would resolve against the page and make the document taller than the dialog —
        // § 11's own trap, and the one `page-scroller-contains-its-absolutes` exists for.
        "relative min-h-0 flex-1 overflow-hidden bg-neutral-950 outline-none",
        "touch-none select-none [overscroll-behavior:contain] focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-inset",
        tool === "measure" ? "cursor-crosshair" : tool === "pan" ? "cursor-grab" : "cursor-ns-resize",
        className,
      )}
    >
      <canvas ref={canvasRef} className="block h-full w-full" />

      {/* ⚠️ A dark stage in BOTH themes, on purpose — a radiograph is read against black, and a light mount
          raises the perceived black point so the bottom of the window stops being distinguishable. It is the one
          surface in this app that does not follow the theme, so the overlay ink is fixed to match it rather than
          reading a token that would invert underneath it. */}
      <div className="pointer-events-none absolute inset-x-2 top-2 flex flex-wrap items-start justify-between gap-2">
        <StageChip>
          {study.columns} × {study.rows}
          {study.modality ? ` · ${study.modality}` : ""}
        </StageChip>
        <StageChip>{`×${zoom.toLocaleString("fr-FR", { maximumFractionDigits: 1 })}`}</StageChip>
      </div>

      {lengthLabel && (
        /* ⚠️ A chip, like the two above it, not shadowed text: measured on `radiographie-thorax-mono1.dcm`, the
           readout lands over the mediastinum — the brightest part of a chest film — where white-on-light with a
           shadow is legible but weak. The figure is the whole point of the tool, so it gets the same opaque
           ground as everything else painted over an image of unknown brightness. */
        <div className="pointer-events-none absolute inset-x-2 bottom-2 flex justify-center">
          <p
            id="dicom-measure-readout"
            role="status"
            className="rounded bg-black/70 px-2 py-1 text-xs font-semibold tabular-nums text-white"
          >
            {lengthLabel}
          </p>
        </div>
      )}
    </div>
  )
}

/** A legible chip over an image of unknown brightness — a shadowed white on a translucent black. */
function StageChip({ children }: { children: React.ReactNode }) {
  return (
    <span className="rounded bg-black/55 px-1.5 py-0.5 text-2xs font-medium tabular-nums text-white md:text-xs">
      {children}
    </span>
  )
}

/**
 * What identifies the frame currently in the source canvas.
 *
 * ⚠️ It has to be the buffer's own identity, not the frame index: on the uncompressed path a frame is a **view**
 * into the file, so consecutive frames differ only by `byteOffset` — and the object identity changes on every
 * read, which would repaint six megapixels on every render.
 */
function frameToken(frame: DicomFrame): string {
  return frame.kind === "colour"
    ? `colour:${frame.rgba.byteOffset}:${frame.rgba.length}`
    : `grey:${frame.stored.byteOffset}:${frame.stored.length}:${frame.bits}`
}

/** The ruler: a line and a ring at each end, so a tap that has not travelled yet is still visible. */
function drawMeasurement(
  context: CanvasRenderingContext2D,
  measurement: Measurement,
  origin: Point,
  scale: number,
): void {
  const a = { x: origin.x + measurement.a.x * scale, y: origin.y + measurement.a.y * scale }
  const b = { x: origin.x + measurement.b.x * scale, y: origin.y + measurement.b.y * scale }

  context.save()
  // Two strokes, dark under light, so the line survives both a black air gap and a white restoration.
  for (const [colour, width] of [
    ["rgba(0,0,0,0.75)", 4],
    ["#fbbf24", 2],
  ] as const) {
    context.strokeStyle = colour
    context.lineWidth = width
    context.lineCap = "round"
    context.beginPath()
    context.moveTo(a.x, a.y)
    context.lineTo(b.x, b.y)
    context.stroke()

    for (const end of [a, b]) {
      context.beginPath()
      context.arc(end.x, end.y, width + 2, 0, Math.PI * 2)
      context.stroke()
    }
  }
  context.restore()
}
