/**
 * A marker a reader drops on the surface of a model.
 *
 * <p>⚠️ **Its own module, and deliberately minimal, because this shape is a contract with a table that does not
 * exist yet.** The viewer holds annotations in state today, so they last as long as the dialog does. Persisting
 * them is a backend slice — an entity, a migration, commands and a realtime key — and the one thing that would
 * make that slice painful is a viewer that had grown its own richer notion of an annotation in the meantime.
 * So: a point in the file's own coordinates, a normal, and a line of text. Nothing derived, nothing that only
 * makes sense while a camera is pointing somewhere.</p>
 */
import type { MeshPoint } from './measure'

export interface MeshAnnotation {
  /** Stable for the life of the marker. A `crypto.randomUUID()` today, a row id when there is a table. */
  id: string
  /**
   * ⚠️ **In the file's own coordinates, never the scene's.** The mesh is moved to sit on the origin for
   * orbiting, and storing the moved position would put every marker in the wrong place the moment anything
   * about that centring changed — including a later version of this app that centred differently.
   */
  point: MeshPoint
  /**
   * The surface normal where it was dropped. Kept for one reason: {@link facingAway} uses it to dim a marker
   * on the far side of the model, which is the only affordable stand-in for real occlusion.
   */
  normal: MeshPoint
  label: string
}

/**
 * Whether a marker sits on a face pointing away from the camera — i.e. is probably behind the model.
 *
 * <p>⚠️ **A facing test, and not occlusion, and the difference is worth stating.** True occlusion means asking
 * whether any triangle lies between the camera and the point, which is a ray against a million-triangle mesh
 * on every frame — far too slow without an acceleration structure this build has no reason to carry. The
 * surface normal answers the same question correctly for a convex shape, which an arch and a die approximately
 * are, and gets it wrong in a concavity: a marker deep in a fissure may stay bright when it is technically
 * hidden. It dims a marker rather than removing one, so the failure mode is a marker that is merely too
 * visible — never one that has silently disappeared.</p>
 */
export function facingAway(normal: MeshPoint, toCamera: MeshPoint): boolean {
  return normal.x * toCamera.x + normal.y * toCamera.y + normal.z * toCamera.z < 0
}

/** What a marker is called before anybody names it. Numbered, so two unnamed markers are still distinguishable. */
export function defaultLabel(existing: readonly MeshAnnotation[]): string {
  return `Repère ${existing.length + 1}`
}
