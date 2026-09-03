/**
 * How a model is lit, coloured and framed — **one owner**, because two things draw these files and they must
 * not diverge.
 *
 * <p>The interactive viewer draws to a canvas the reader orbits; {@link ./thumbnail} draws one frame off-screen
 * on the way up, so a drawer full of `.stl` shows the arches rather than a wall of grey boxes. If those two
 * built their own scenes, a tile and the viewer it opens would light the same model differently — which reads
 * as the file having changed, not as two code paths disagreeing. This is `monochrome1-has-one-owner`'s lesson
 * applied before it can bite.</p>
 *
 * <h3>⚠️ Which way is up is not knowable, so the views are geometric and never anatomical</h3>
 *
 * <p>STL, PLY and OBJ record no orientation convention, and dental tools disagree — some export Z-up, some
 * Y-up, and a mesh that has been through a conversion may be neither. So the view buttons say « Face »,
 * « Dessus », « Gauche »: statements about the bounding box, which are always true. Labelling them
 * « occlusale » or « vestibulaire » would be a clinical claim the file gives no basis for, and it would be
 * wrong for whichever half of the exporters use the other axis.</p>
 */
import type { PerspectiveCamera } from 'three'

import type { MeshBounds, MeshModel } from './model'

/** A direction to look from. Geometric, for the reason in this module's header. */
export type MeshView = 'iso' | 'front' | 'back' | 'left' | 'right' | 'top' | 'bottom'

export interface MeshViewOption {
  id: MeshView
  label: string
  /** Where the camera sits relative to the model's centre, before being scaled to frame it. */
  direction: readonly [number, number, number]
}

/**
 * ⚠️ **`iso` is first and is what the viewer opens on.** A model seen straight down an axis is the one view in
 * which its depth is invisible — an arch viewed « Face » is a flat outline — so opening on a three-quarter view
 * is what makes the first frame informative. The same choice is why the thumbnail uses it.
 */
export const MESH_VIEWS: readonly MeshViewOption[] = [
  { id: 'iso', label: '3/4', direction: [1, 0.6, 1] },
  { id: 'front', label: 'Face', direction: [0, 0, 1] },
  { id: 'back', label: 'Dos', direction: [0, 0, -1] },
  { id: 'left', label: 'Gauche', direction: [-1, 0, 0] },
  { id: 'right', label: 'Droite', direction: [1, 0, 0] },
  { id: 'top', label: 'Dessus', direction: [0, 1, 0] },
  { id: 'bottom', label: 'Dessous', direction: [0, -1, 0] },
]

export const MESH_ORIENTATION_NOTE =
  'Les vues sont géométriques : ces formats n’enregistrent pas d’orientation, donc « Dessus » est le dessus du ' +
  'fichier et pas nécessairement la face occlusale.'

/** How the surface is drawn. Solid is the default; the other two answer questions solid cannot. */
export type MeshShading =
  /** Matte, plaster-like — what a stone model looks like, which is what a dentist expects to see. */
  | 'solid'
  /**
   * ⚠️ Not decoration: a scan's triangle density is how you tell a high-resolution capture from one that was
   * decimated on export, and it is invisible on a smooth-shaded surface.
   */
  | 'wireframe'
  /** Solid with the wireframe over it — the one that actually gets used, for reading a margin against density. */
  | 'both'

/** The scene, its camera, and everything that has to be given back. */
export interface MeshScene {
  scene: import('three').Scene
  camera: PerspectiveCamera
  mesh: import('three').Mesh
  /** Applies a shading mode. Cheap — it toggles material flags rather than rebuilding anything. */
  setShading(shading: MeshShading): void
  /** Disposes the materials and the lights. ⚠️ Never the geometry: {@link MeshModel.release} owns that. */
  dispose(): void
}

/**
 * ⚠️ **Off-white and matte, not a shiny grey.** A specular material puts a moving highlight on the surface, and
 * on a dental scan a highlight sitting in a fissure reads as a feature of the tooth. Plaster is both what the
 * physical model looks like and the finish that hides nothing.
 */
const SURFACE = 0xe8e4dc
const WIREFRAME = 0x4a5568

/**
 * ⚠️ **Double-sided, and this is not a preference.** Intraoral scans are open surfaces — an arch is a shell,
 * not a solid — and roughly half of them arrive with some normals inverted by the export. Single-sided
 * rendering makes those triangles vanish, so the model appears to have holes in it that are not in the file.
 * The cost is that back faces are lit as if they faced you, which is far less misleading than absent geometry.
 */
export async function createMeshScene(model: MeshModel): Promise<MeshScene> {
  const THREE = await import('three')

  const scene = new THREE.Scene()
  scene.background = new THREE.Color(0x1a1d23)

  const material = new THREE.MeshStandardMaterial({
    color: model.hasVertexColours ? 0xffffff : SURFACE,
    // A scanner's captured colour is the point of a PLY; multiplying it by a tint would misreport it.
    vertexColors: model.hasVertexColours,
    roughness: 0.72,
    metalness: 0.02,
    side: THREE.DoubleSide,
    flatShading: false,
  })

  const wireframe = new THREE.MeshBasicMaterial({
    color: WIREFRAME,
    wireframe: true,
    transparent: true,
    opacity: 0.35,
    side: THREE.DoubleSide,
  })

  const mesh = new THREE.Mesh(model.geometry, material)
  const overlay = new THREE.Mesh(model.geometry, wireframe)
  overlay.visible = false

  // ⚠️ Centred on the origin by moving the MESH, never by rewriting the geometry. A picked point has to come
  // back out in the file's own coordinates — for a measurement, and for an annotation that must still be in the
  // right place after a reload — and baking the offset into the vertices would silently shift every one of them.
  const [cx, cy, cz] = model.bounds.centre
  mesh.position.set(-cx, -cy, -cz)
  overlay.position.copy(mesh.position)

  const pivot = new THREE.Group()
  pivot.add(mesh, overlay)
  scene.add(pivot)

  // Three lights and no environment map: a key to give the form, a fill to keep the shadowed side readable,
  // and a hemisphere so a surface facing straight down is never pure black.
  const key = new THREE.DirectionalLight(0xffffff, 2.1)
  key.position.set(1, 1.4, 1)
  const fill = new THREE.DirectionalLight(0xffffff, 0.8)
  fill.position.set(-1, -0.4, -0.8)
  const ambient = new THREE.HemisphereLight(0xffffff, 0x30343c, 1.1)
  scene.add(key, fill, ambient)

  const camera = new THREE.PerspectiveCamera(35, 1, 0.1, 1000)

  return {
    scene,
    camera,
    mesh,
    setShading(shading) {
      mesh.visible = shading !== 'wireframe'
      overlay.visible = shading !== 'solid'
    },
    dispose() {
      material.dispose()
      wireframe.dispose()
      scene.clear()
    },
  }
}

/**
 * Points the camera at the model from `view` and backs it off just far enough to hold it.
 *
 * ⚠️ **It fits the box as SEEN, not the bounding sphere, and the difference is most of the screen.** Framing on
 * the diagonal treats every model as a ball of that width — correct only for a cube seen corner-on. A dental
 * arch is a flat, wide thing: measured on a real 63 × 34 × 12 mm scan, sphere-fitting left it filling about
 * **40 % of the stage width**, with the rest empty. The eight corners are projected onto the camera's own axes
 * instead, which is exact for every shape and every angle.
 *
 * ⚠️ **The framing must satisfy the NARROWER of the two fields of view, and using the vertical one alone is a
 * bug you only see on a phone.** A camera's `fov` is vertical; at 390 px the stage is far wider than it is
 * tall, so the horizontal field is the smaller of the two and a model framed to the vertical one is cut off at
 * the sides. The same code on a desktop looks perfect.
 *
 * ⚠️ **« Dessus » and « Dessous » need their own up vector, or they render nothing at all.** Those directions
 * are parallel to the default up of (0, 1, 0), so the cross product `lookAt` builds its basis from is the zero
 * vector and the view matrix is degenerate — a stage that is simply empty, with no error and nothing to
 * suggest the file is fine.
 */
export function frameCamera(
  camera: PerspectiveCamera,
  bounds: MeshBounds,
  view: MeshView,
  aspect: number,
): void {
  const option = MESH_VIEWS.find((candidate) => candidate.id === view) ?? MESH_VIEWS[0]

  const [dx, dy, dz] = option.direction
  const length = Math.hypot(dx, dy, dz) || 1
  const forward: [number, number, number] = [dx / length, dy / length, dz / length]

  // Straight up unless we are looking along it, in which case any perpendicular axis will do.
  const up: [number, number, number] = Math.abs(forward[1]) > 0.999 ? [0, 0, 1] : [0, 1, 0]
  const right = normalise(cross(up, forward))
  const trueUp = normalise(cross(forward, right))

  const vertical = (camera.fov * Math.PI) / 180
  const horizontal = 2 * Math.atan(Math.tan(vertical / 2) * Math.max(aspect, 0.01))
  const tanUp = Math.tan(vertical / 2)
  const tanRight = Math.tan(horizontal / 2)

  /**
   * ⚠️ **Solved per corner, because the widest corner and the nearest corner are not the same corner.** Taking
   * `max|up|` and `max depth` independently and adding them is a safe upper bound and a visibly wrong one: it
   * assumes the model is at its widest exactly where it is nearest, which for an arch — wide across, shallow
   * from the front — pushed the camera far enough back to leave the model filling **63 %** of the stage. With
   * the camera at `d` along `forward`, a corner `c` sits `d - (c · forward)` in front of it and needs
   * `|c · up| ≤ (d - c · forward) · tan(fov/2)`, i.e. `d ≥ |c · up| / tan + (c · forward)`. The largest such
   * `d` over the eight corners is the exact fit, and it is eight dot products.
   */
  const [hx, hy, hz] = [bounds.size[0] / 2, bounds.size[1] / 2, bounds.size[2] / 2]
  let distance = 0
  for (const sx of [-1, 1]) {
    for (const sy of [-1, 1]) {
      for (const sz of [-1, 1]) {
        const corner = [sx * hx, sy * hy, sz * hz] as const
        const dot = (axis: readonly number[]) =>
          corner[0] * axis[0] + corner[1] * axis[1] + corner[2] * axis[2]

        const ahead = dot(forward)
        distance = Math.max(
          distance,
          Math.abs(dot(trueUp)) / tanUp + ahead,
          Math.abs(dot(right)) / tanRight + ahead,
        )
      }
    }
  }
  distance *= 1.06

  camera.up.set(up[0], up[1], up[2])
  camera.position.set(forward[0] * distance, forward[1] * distance, forward[2] * distance)

  // ⚠️ Relative to the model, never fixed. A model exported in metres has a diagonal of 0,08 and a near plane
  // of 0.1 would clip the whole thing away — an empty stage, with nothing to say why.
  camera.near = Math.max((distance - bounds.diagonal) / 100, distance / 1000, 1e-5)
  camera.far = distance * 10 + bounds.diagonal
  camera.lookAt(0, 0, 0)
  camera.updateProjectionMatrix()
}

function cross(a: readonly number[], b: readonly number[]): [number, number, number] {
  return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]
}

function normalise(v: readonly number[]): [number, number, number] {
  const length = Math.hypot(v[0], v[1], v[2]) || 1
  return [v[0] / length, v[1] / length, v[2] / length]
}
