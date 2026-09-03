"use client"

import { useCallback, useEffect, useRef } from "react"

import { facingAway, type MeshAnnotation } from "@/lib/files/mesh/annotation"
import { distanceBetween, formatLength, type MeshMeasurement, type MeshPoint, type MeshUnit } from "@/lib/files/mesh/measure"
import type { MeshModel } from "@/lib/files/mesh/model"
import { createMeshScene, frameCamera, type MeshScene, type MeshShading, type MeshView } from "@/lib/files/mesh/scene"
import { cn } from "@/lib/utils"

/** What a pointer does on the surface. Orbiting never stops working — see the gesture note below. */
export type MeshTool = "orbit" | "measure" | "annotate"

/**
 * The model, and every gesture that acts on it.
 *
 * <h3>The gesture conflict, and how it is resolved</h3>
 *
 * <p>⚠️ **Placing a point and rotating the model both want the same pointer**, and on a coarse pointer there is
 * no modifier key to separate them. The DICOM stage has this problem too and resolves it by making the tool
 * decide what a drag does; that answer is wrong here, because a model you cannot turn is a model you cannot
 * pick a point on — you have to see the far side to measure to it.</p>
 *
 * <p>So the resolution is by <b>gesture length, not by tool</b>:</p>
 * <ol>
 *   <li><b>A drag always orbits</b>, in every tool. Rotate, pan and zoom never stop working.</li>
 *   <li><b>A tap places</b>, when a tool is armed. A pointer that goes down and comes up within
 *       {@link TAP_SLOP} pixels was a tap; anything further was a rotation the reader does not want to also
 *       drop a marker at the end of.</li>
 *   <li><b>Two fingers are always the camera</b>, whatever the tool — `OrbitControls` owns pinch entirely, and
 *       a multi-touch gesture cancels the pending tap so a pinch never ends by placing a point.</li>
 * </ol>
 *
 * <p>⚠️ **{@link TAP_SLOP} is 8 px and not 2.** Measured on a finger rather than a mouse: a tap on a touch
 * screen routinely travels four or five pixels between down and up, so a tight threshold makes the tool feel
 * broken on exactly the device this app is used on most.</p>
 *
 * <h3>Rendering</h3>
 *
 * <p>⚠️ **Markers and the measurement line are DOM, not scene objects**, and positioned by mutating style
 * rather than through React state. Text in a WebGL canvas is either blurry or expensive, cannot be read by a
 * screen reader and cannot be tapped; and re-rendering React sixty times a second to move five absolutely
 * positioned elements is the jank this avoids. React owns the list of markers, the frame loop owns where they
 * are.</p>
 */
const TAP_SLOP = 8

/**
 * ⚠️ Capped at 2. A modern phone reports 3 or more, and rendering a million triangles at nine times the pixels
 * is how a viewer that is smooth on a laptop drops to single-figure frame rates on the tablet at the chair.
 */
const MAX_PIXEL_RATIO = 2

export interface MeshViewerStageProps {
  model: MeshModel
  view: MeshView
  shading: MeshShading
  tool: MeshTool
  unit: MeshUnit
  measurement: MeshMeasurement | null
  onMeasurementChange: (measurement: MeshMeasurement | null) => void
  annotations: MeshAnnotation[]
  onAnnotationPlaced: (point: MeshPoint, normal: MeshPoint) => void
  onAnnotationSelected: (id: string) => void
  selectedAnnotationId: string | null
  /** Changes to ask for a re-frame — « Ajuster » without changing the view. */
  fitToken: number
  /** Told once, so the dialog can say « votre navigateur ne peut pas afficher la 3D » instead of showing black. */
  onUnavailable: () => void
}

export function MeshViewerStage({
  model,
  view,
  shading,
  tool,
  unit,
  measurement,
  onMeasurementChange,
  annotations,
  onAnnotationPlaced,
  onAnnotationSelected,
  selectedAnnotationId,
  fitToken,
  onUnavailable,
}: MeshViewerStageProps) {
  const hostRef = useRef<HTMLDivElement | null>(null)
  const canvasRef = useRef<HTMLCanvasElement | null>(null)
  const overlayRef = useRef<SVGSVGElement | null>(null)
  const lineRef = useRef<SVGLineElement | null>(null)
  const markerRefs = useRef(new Map<string, HTMLElement>())
  const endpointRefs = useRef<(HTMLElement | null)[]>([null, null])
  const readoutRef = useRef<HTMLDivElement | null>(null)

  /**
   * ⚠️ Everything the frame loop reads lives in a ref, never in the closure a render captured. The loop
   * outlives every render, so a value read from props inside it would be frozen at whichever render created it
   * — the bug that makes a viewer respond to the first tool you pick and ignore every one after.
   */
  const live = useRef({
    model,
    view,
    shading,
    tool,
    unit,
    measurement,
    annotations,
    selectedAnnotationId,
    onMeasurementChange,
    onAnnotationPlaced,
    onAnnotationSelected,
  })
  live.current = {
    model,
    view,
    shading,
    tool,
    unit,
    measurement,
    annotations,
    selectedAnnotationId,
    onMeasurementChange,
    onAnnotationPlaced,
    onAnnotationSelected,
  }

  const engine = useRef<{
    renderer: import("three").WebGLRenderer
    controls: import("three").Controls<Record<string, unknown>> & { update(): void; dispose(): void }
    built: MeshScene
    raycaster: import("three").Raycaster
    vector: import("three").Vector2
    scratch: import("three").Vector3
    invalidate: () => void
  } | null>(null)

  const registerMarker = useCallback((id: string, element: HTMLElement | null) => {
    if (element) markerRefs.current.set(id, element)
    else markerRefs.current.delete(id)
  }, [])

  useEffect(() => {
    const host = hostRef.current
    const canvas = canvasRef.current
    if (!host || !canvas) return

    let disposed = false
    let frame = 0

    const boot = async () => {
      const THREE = await import("three")
      const { OrbitControls } = await import("three/examples/jsm/controls/OrbitControls.js")
      if (disposed) return

      let renderer: import("three").WebGLRenderer
      try {
        renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: false })
      } catch {
        // ⚠️ A real answer on an old tablet or a machine with the GPU blocklisted, and it must be *said*: a
        // failed context leaves a black rectangle, which reads as a file that will not open.
        onUnavailable()
        return
      }
      if (disposed) {
        renderer.dispose()
        return
      }

      renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, MAX_PIXEL_RATIO))

      const built = await createMeshScene(live.current.model)
      if (disposed) {
        built.dispose()
        renderer.dispose()
        return
      }
      built.setShading(live.current.shading)

      const controls = new OrbitControls(built.camera, renderer.domElement)
      controls.enableDamping = true
      controls.dampingFactor = 0.08
      controls.rotateSpeed = 0.85
      controls.zoomSpeed = 0.9
      controls.panSpeed = 0.85
      controls.target.set(0, 0, 0)

      let dirty = true
      const invalidate = () => {
        dirty = true
      }
      controls.addEventListener("change", invalidate)

      engine.current = {
        renderer,
        controls: controls as unknown as NonNullable<typeof engine.current>["controls"],
        built,
        raycaster: new THREE.Raycaster(),
        vector: new THREE.Vector2(),
        scratch: new THREE.Vector3(),
        invalidate,
      }

      resize()
      frameCamera(built.camera, live.current.model.bounds, live.current.view, aspect())
      controls.update()

      const loop = () => {
        frame = requestAnimationFrame(loop)
        if (!dirty) return
        dirty = false
        controls.update()
        renderer.render(built.scene, built.camera)
        placeOverlays()
      }
      frame = requestAnimationFrame(loop)
    }

    const aspect = () => {
      const { clientWidth, clientHeight } = host
      return clientHeight > 0 ? clientWidth / clientHeight : 1
    }

    const resize = () => {
      const current = engine.current
      if (!current) return
      const width = host.clientWidth
      const height = host.clientHeight
      if (width <= 0 || height <= 0) return

      current.renderer.setSize(width, height, false)
      current.built.camera.aspect = width / height
      current.built.camera.updateProjectionMatrix()
      if (overlayRef.current) {
        overlayRef.current.setAttribute("viewBox", `0 0 ${width} ${height}`)
      }
      current.invalidate()
    }

    /**
     * Projects a model-space point to stage pixels. ⚠️ The mesh sits at `-centre`, so the scene coordinate of a
     * stored point is the point *minus* the centre — the inverse of the offset `createMeshScene` applied, and
     * the reason that offset lives on the mesh rather than in the vertices.
     */
    const project = (point: MeshPoint) => {
      const current = engine.current!
      const [cx, cy, cz] = current.built.mesh.position.toArray()
      current.scratch.set(point.x + cx, point.y + cy, point.z + cz)
      current.scratch.project(current.built.camera)
      return {
        x: (current.scratch.x * 0.5 + 0.5) * host.clientWidth,
        y: (-current.scratch.y * 0.5 + 0.5) * host.clientHeight,
        // `z > 1` is behind the camera entirely — projecting it gives a mirrored position on screen.
        onScreen: current.scratch.z < 1,
      }
    }

    const toCamera = (): MeshPoint => {
      const current = engine.current!
      const p = current.built.camera.position
      const length = Math.hypot(p.x, p.y, p.z) || 1
      return { x: p.x / length, y: p.y / length, z: p.z / length }
    }

    const placeOverlays = () => {
      const current = engine.current
      if (!current) return

      const direction = toCamera()
      for (const annotation of live.current.annotations) {
        const element = markerRefs.current.get(annotation.id)
        if (!element) continue
        const at = project(annotation.point)
        element.style.transform = `translate(-50%, -50%) translate(${at.x}px, ${at.y}px)`
        element.style.visibility = at.onScreen ? "visible" : "hidden"
        // Dimmed rather than hidden — see `facingAway`: this is a facing test, not true occlusion, so it must
        // never be the reason a marker cannot be found.
        element.style.opacity = facingAway(annotation.normal, direction) ? "0.35" : "1"
      }

      const active = live.current.measurement
      const line = lineRef.current
      const [fromEl, toEl] = endpointRefs.current

      if (!active) {
        if (line) line.style.visibility = "hidden"
        if (fromEl) fromEl.style.visibility = "hidden"
        if (toEl) toEl.style.visibility = "hidden"
        if (readoutRef.current) readoutRef.current.style.visibility = "hidden"
        return
      }

      const from = project(active.from)
      if (fromEl) {
        fromEl.style.transform = `translate(-50%, -50%) translate(${from.x}px, ${from.y}px)`
        fromEl.style.visibility = from.onScreen ? "visible" : "hidden"
      }

      if (!active.to) {
        if (line) line.style.visibility = "hidden"
        if (toEl) toEl.style.visibility = "hidden"
        if (readoutRef.current) readoutRef.current.style.visibility = "hidden"
        return
      }

      const to = project(active.to)
      if (toEl) {
        toEl.style.transform = `translate(-50%, -50%) translate(${to.x}px, ${to.y}px)`
        toEl.style.visibility = to.onScreen ? "visible" : "hidden"
      }
      if (line) {
        line.setAttribute("x1", String(from.x))
        line.setAttribute("y1", String(from.y))
        line.setAttribute("x2", String(to.x))
        line.setAttribute("y2", String(to.y))
        line.style.visibility = from.onScreen && to.onScreen ? "visible" : "hidden"
      }
      if (readoutRef.current) {
        readoutRef.current.style.transform =
          `translate(-50%, -50%) translate(${(from.x + to.x) / 2}px, ${(from.y + to.y) / 2 - 22}px)`
        readoutRef.current.style.visibility = from.onScreen && to.onScreen ? "visible" : "hidden"
        readoutRef.current.textContent = formatLength(
          distanceBetween(active.from, active.to),
          live.current.unit,
        )
      }
    }

    /** The surface point under a client-space position, in the file's own coordinates, or null off the model. */
    const pick = (clientX: number, clientY: number) => {
      const current = engine.current
      if (!current) return null

      const rect = host.getBoundingClientRect()
      current.vector.set(
        ((clientX - rect.left) / rect.width) * 2 - 1,
        -((clientY - rect.top) / rect.height) * 2 + 1,
      )
      current.raycaster.setFromCamera(current.vector, current.built.camera)

      const [hit] = current.raycaster.intersectObject(current.built.mesh, false)
      if (!hit) return null

      const [cx, cy, cz] = current.built.mesh.position.toArray()
      const point: MeshPoint = {
        x: hit.point.x - cx,
        y: hit.point.y - cy,
        z: hit.point.z - cz,
      }
      // The mesh carries a translation and nothing else — no rotation, no scale — so a face normal in object
      // space is already the world normal. `normalMatrix` would be the identity here; skipping it is not a
      // shortcut, and if this ever gains a rotation it becomes one.
      const normal = hit.face
        ? { x: hit.face.normal.x, y: hit.face.normal.y, z: hit.face.normal.z }
        : { x: 0, y: 0, z: 1 }

      return { point, normal }
    }

    // ── the tap, and what cancels it ──────────────────────────────────────────────────────────────────────
    let pending: { id: number; x: number; y: number } | null = null
    let pointers = 0

    const onPointerDown = (event: PointerEvent) => {
      pointers += 1
      // A second finger means the camera, always: whatever tap was in flight is abandoned so a pinch cannot
      // end by dropping a marker.
      pending = pointers > 1 ? null : { id: event.pointerId, x: event.clientX, y: event.clientY }
    }

    const onPointerUp = (event: PointerEvent) => {
      pointers = Math.max(0, pointers - 1)
      const candidate = pending
      pending = null
      if (!candidate || candidate.id !== event.pointerId) return
      if (Math.hypot(event.clientX - candidate.x, event.clientY - candidate.y) > TAP_SLOP) return

      const tool = live.current.tool
      if (tool === "orbit") return

      const hit = pick(event.clientX, event.clientY)
      if (!hit) return

      if (tool === "annotate") {
        live.current.onAnnotationPlaced(hit.point, hit.normal)
        return
      }

      const active = live.current.measurement
      // A finished measurement starts a new one rather than extending it — a third tap that silently moved one
      // end would make the number change with no way to tell which end had moved.
      live.current.onMeasurementChange(
        !active || active.to ? { from: hit.point, to: null } : { from: active.from, to: hit.point },
      )
    }

    const onPointerCancel = () => {
      pointers = Math.max(0, pointers - 1)
      pending = null
    }

    host.addEventListener("pointerdown", onPointerDown)
    host.addEventListener("pointerup", onPointerUp)
    host.addEventListener("pointercancel", onPointerCancel)

    const observer = new ResizeObserver(resize)
    observer.observe(host)

    void boot()

    return () => {
      disposed = true
      cancelAnimationFrame(frame)
      observer.disconnect()
      host.removeEventListener("pointerdown", onPointerDown)
      host.removeEventListener("pointerup", onPointerUp)
      host.removeEventListener("pointercancel", onPointerCancel)

      const current = engine.current
      if (current) {
        current.controls.dispose()
        current.built.dispose()
        // ⚠️ `forceContextLoss` as well as `dispose`: a browser allows a small number of live WebGL contexts
        // (16 in Chrome), and a dentist opening a dozen models in one sitting would otherwise reach it — at
        // which point the oldest context is killed and an *already open* viewer goes black.
        current.renderer.forceContextLoss()
        current.renderer.dispose()
        engine.current = null
      }
    }
    // ⚠️ Built once per model. Every other prop reaches the loop through `live`, so re-running this on a tool
    // change would tear down a WebGL context to answer a button press.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [model, onUnavailable])

  // The view, the fit and the shading are applied to the running scene rather than rebuilding it.
  useEffect(() => {
    const current = engine.current
    const host = hostRef.current
    if (!current || !host || host.clientHeight <= 0) return
    frameCamera(current.built.camera, model.bounds, view, host.clientWidth / host.clientHeight)
    current.controls.update()
    current.invalidate()
  }, [view, fitToken, model.bounds])

  useEffect(() => {
    engine.current?.built.setShading(shading)
    engine.current?.invalidate()
  }, [shading])

  // A measurement or a marker changed without the camera moving — the overlay still has to catch up.
  useEffect(() => {
    engine.current?.invalidate()
  }, [measurement, annotations, unit, selectedAnnotationId])

  return (
    <div
      ref={hostRef}
      className={cn(
        "relative min-h-0 flex-1 overflow-hidden bg-[#1a1d23] touch-none select-none",
        tool === "orbit" ? "cursor-grab active:cursor-grabbing" : "cursor-crosshair",
      )}
    >
      <canvas ref={canvasRef} className="block h-full w-full" />

      {/* The measurement line. `pointer-events-none` throughout: the overlay must never eat a drag that was
          meant for the model underneath it. */}
      <svg
        ref={overlayRef}
        className="pointer-events-none absolute inset-0 h-full w-full"
        aria-hidden="true"
      >
        <line
          ref={lineRef}
          stroke="#38bdf8"
          strokeWidth={2}
          strokeDasharray="5 4"
          style={{ visibility: "hidden" }}
        />
      </svg>

      {[0, 1].map((end) => (
        <span
          key={end}
          ref={(element) => {
            endpointRefs.current[end] = element
          }}
          className="pointer-events-none absolute left-0 top-0 size-3 rounded-full border-2 border-white bg-sky-400 shadow"
          style={{ visibility: "hidden" }}
          aria-hidden="true"
        />
      ))}

      <div
        ref={readoutRef}
        className="pointer-events-none absolute left-0 top-0 rounded-md bg-sky-500 px-2 py-0.5 text-xs font-semibold tabular-nums text-white shadow"
        style={{ visibility: "hidden" }}
        aria-hidden="true"
      />

      {annotations.map((annotation) => (
        <button
          key={annotation.id}
          ref={(element) => registerMarker(annotation.id, element)}
          type="button"
          onClick={() => onAnnotationSelected(annotation.id)}
          style={{ visibility: "hidden" }}
          className={cn(
            // 44 px of tappable box around a small visible dot — § 1's target size on a marker that must stay
            // small enough not to hide the surface it points at.
            "absolute left-0 top-0 flex size-11 items-center justify-center rounded-full",
            "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white",
          )}
          aria-label={`Repère : ${annotation.label}`}
        >
          <span
            className={cn(
              "size-3.5 rounded-full border-2 border-white shadow transition-colors",
              annotation.id === selectedAnnotationId ? "bg-amber-400" : "bg-rose-500",
            )}
          />
        </button>
      ))}
    </div>
  )
}
