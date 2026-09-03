/**
 * One frame of a model, rendered off-screen — the stand-in a `.stl` carries into the drawer.
 *
 * <p>This is what turns « modèle.stl » beside a grey box into a tile showing which arch it is. It runs on the
 * way up, in {@link ../preview}, for the same reason every other stand-in does: the browser already holds the
 * bytes and is idle while the user waits, and a server-side 3D pipeline would mean a GPU on a shared host.</p>
 *
 * <p>⚠️ **It renders through {@link ../mesh/scene}, not through a scene of its own.** A tile and the viewer it
 * opens must light the same model identically, or the difference reads as the file having changed.</p>
 *
 * <h3>⚠️ The WebGL context is the resource that bites here, not the memory</h3>
 *
 * <p>A browser allows a small number of live WebGL contexts — sixteen in Chrome — and creating the seventeenth
 * silently kills the oldest. A dentist dropping twelve STLs onto the upload zone would reach that limit inside
 * one gesture, and the symptom is not an error: it is an *already-open* viewer elsewhere on the page going
 * black. So the context here is destroyed explicitly with `forceContextLoss` before `dispose`, which is the
 * only thing that actually releases it — `dispose` alone leaves it live until garbage collection, which is
 * exactly the non-determinism this must not have.</p>
 */
import type { DecodedImage } from '../decoders/raster'
import { createMeshScene, frameCamera } from './scene'
import { openMeshModel } from './model'

/**
 * ⚠️ **1024, not the raster path's 2560.** A stand-in is painted in a tile a couple of hundred pixels wide and
 * in a preview dialog about a thousand; the extra pixels would be spent rasterising a million triangles a
 * second time for a picture nobody can tell apart, on the machine of somebody waiting for an upload.
 */
const EDGE = 1024

export const MESH_STATIC_NOTE =
  'Aperçu fixe du modèle. Ouvrez la visionneuse 3D pour le tourner, le mesurer et y poser des repères.'

/**
 * A JPEG of the model seen from three-quarters, or null.
 *
 * ⚠️ **Null is an ordinary answer and never an error** — a file past a ceiling, a mesh with no surface, a
 * machine with no WebGL at all. Every one of those means « show the placeholder », which is what a mesh had
 * before this existed anyway.
 */
export async function renderMeshThumbnail(source: Blob, fileName: string): Promise<DecodedImage | null> {
  const opened = await openMeshModel(source, fileName)
  if (!opened.ok) return null

  const model = opened.model
  let renderer: import('three').WebGLRenderer | null = null
  let built: Awaited<ReturnType<typeof createMeshScene>> | null = null

  try {
    const THREE = await import('three')
    const canvas = document.createElement('canvas')
    canvas.width = EDGE
    canvas.height = EDGE

    try {
      renderer = new THREE.WebGLRenderer({
        canvas,
        antialias: true,
        // Required to read the canvas back after the draw: without it the buffer may already be cleared by the
        // time `toBlob` runs, and the result is a silently blank tile rather than a failure.
        preserveDrawingBuffer: true,
      })
    } catch {
      return null
    }

    // Deliberately 1, not the device ratio: this is an off-screen buffer at a fixed size, and a retina machine
    // would otherwise render four times the pixels to produce the identical JPEG.
    renderer.setPixelRatio(1)
    renderer.setSize(EDGE, EDGE, false)

    built = await createMeshScene(model)
    built.setShading('solid')
    frameCamera(built.camera, model.bounds, 'iso', 1)
    renderer.render(built.scene, built.camera)

    const blob = await new Promise<Blob | null>((resolve) => {
      canvas.toBlob(resolve, 'image/jpeg', 0.85)
    })
    if (!blob) return null

    return { blob, width: EDGE, height: EDGE, pages: 1, advisory: MESH_STATIC_NOTE }
  } catch {
    return null
  } finally {
    built?.dispose()
    if (renderer) {
      renderer.forceContextLoss()
      renderer.dispose()
    }
    model.release()
  }
}
