/**
 * A three-dimensional model, opened for viewing — the STL an intraoral scanner writes, the PLY it writes when
 * it also captured colour, and the OBJ a design package exports.
 *
 * <p>These three formats reached the drawer long before anything could show them: `FileTypeCatalog` has
 * accepted, validated and stored them since AC-3.2, with signature rules already reasoned through (an ASCII
 * STL <i>is</i> a text file, so it deliberately carries no signature at all). What was missing was the looking
 * at — « modèle.stl » beside a grey box tells a dentist nothing about which arch it is, or whether it is a scan
 * or a finished design.</p>
 *
 * <h3>⚠️ No mesh format records a unit, and that one fact shapes this module</h3>
 *
 * <p>A DICOM at least <i>usually</i> states its pixel spacing, and `lengthCaveat` exists for when it does not.
 * STL, PLY and OBJ never state anything: they hold bare floating-point coordinates, and the number `48.2` in a
 * file is 48.2 <i>of whatever the exporter had in mind</i>. Dental scanners write millimetres, near enough
 * universally — but « near enough universally » is not the same as « the file says so », and a viewer that
 * silently prints « 48,2 mm » over a model exported in centimetres has invented a measurement rather than
 * taken one.</p>
 *
 * <p>So the unit is a <b>choice the operator makes and can change</b>, this module reports only what it can
 * actually see — the size of the box the model occupies, in file units — and {@link ./measure} turns that into
 * a sentence that says which. See {@link inferUnit} for the corroboration, which is a hint and never a
 * finding.</p>
 *
 * <h3>Why the parse is on the main thread</h3>
 *
 * <p>⚠️ A worker would need `worker-src`, which is inherited from `default-src 'self'` unless declared and
 * exists in <b>four</b> byte-identical copies across the middleware, both Caddy sites and the console's config
 * — the trap that makes a decoder work on a laptop and show a grey icon on the VPS. libheif already spends
 * eleven seconds on the main thread behind a spinner and that was judged acceptable; a typed-array walk over a
 * few hundred megabytes is far less. The stage says which wait the reader is in instead.</p>
 */
import type { BufferGeometry } from 'three'

/** The formats this module parses. Keyed on the extension, exactly as the server's catalogue is. */
export type MeshFormat = 'stl' | 'ply' | 'obj'

const FORMATS: Readonly<Record<string, MeshFormat>> = {
  stl: 'stl',
  ply: 'ply',
  obj: 'obj',
}

/**
 * ⚠️ **The file ceiling, and it is not the same question as the catalogue's.** `FileTypeCatalog` decides what
 * the deployment will *hold* (150 Mo hosted, far more in the coffre); this decides what a browser can turn into
 * geometry without taking the tab down with it. A file above this is stored, downloadable and simply not
 * painted — which is an ordinary answer here, not a failure.
 */
const MAX_BYTES = 128 * 1024 * 1024

/**
 * ⚠️ **The ceiling that actually bites, and the reason a byte count alone is not enough.** Binary STL spends
 * 50 bytes per triangle on disk and about 72 in a `BufferGeometry` — positions and normals, three vertices
 * each, unindexed — so the file size understates the memory by roughly half. An intraoral scan of one arch is
 * 100 000 to 800 000 triangles and a full-mouth study around 1,5 million, so this is generous for everything
 * clinical while still refusing the CAD export that would otherwise allocate a gigabyte on a tablet.
 */
const MAX_TRIANGLES = 2_500_000

/** Whether this build can parse the file, from its name alone — the gate that avoids fetching bytes for nothing. */
export function meshFormatOf(fileName: string): MeshFormat | null {
  const dot = fileName.lastIndexOf('.')
  if (dot <= 0 || dot === fileName.length - 1) return null
  return FORMATS[fileName.slice(dot + 1).toLowerCase()] ?? null
}

/** The box the model occupies, in **file units** — never in millimetres, because the file does not say. */
export interface MeshBounds {
  min: readonly [number, number, number]
  max: readonly [number, number, number]
  size: readonly [number, number, number]
  centre: readonly [number, number, number]
  /** The box's diagonal: what a fit-to-view frames, and the only evidence {@link inferUnit} has. */
  diagonal: number
}

export interface MeshModel {
  format: MeshFormat
  /**
   * Ready to hand to a `Mesh`. Positions always; normals always (computed when the file carried none); a
   * `color` attribute only when the file carried per-vertex colour.
   */
  geometry: BufferGeometry
  triangles: number
  vertices: number
  bounds: MeshBounds
  /** A scanner that captured texture writes colour per vertex; a design package does not. */
  hasVertexColours: boolean
  /**
   * ⚠️ **True when the normals on screen were derived rather than read**, which changes what the shading means:
   * computed normals are smooth, so a chamfer a design package recorded as a hard edge renders rounded. Worth
   * saying out loud on a surface someone may judge a margin line from.
   */
  computedNormals: boolean
  /** How many separate objects an OBJ held. One for every STL and PLY; more means they were merged. */
  parts: number
  /** Released with the dialog — a `BufferGeometry` holds GPU buffers that outlive garbage collection. */
  release(): void
}

/** Why no model came back. Every one is an ordinary answer that can be said out loud in French. */
export type MeshFailure =
  | { reason: 'too-large'; limitBytes: number }
  | { reason: 'too-complex'; triangles: number; limitTriangles: number }
  /** The parser threw, or the file is not what its extension claims. */
  | { reason: 'unreadable' }
  /** It parsed and holds no triangles: a point cloud, a wireframe-only OBJ, or a truncated export. */
  | { reason: 'empty' }
  /**
   * ⚠️ Coordinates that are not numbers. Worth its own reason because it is the one corruption that produces a
   * **blank stage and no error**: NaN spreads through the bounding box into the camera fit, and the result is a
   * viewer that looks like it is still loading, for ever.
   */
  | { reason: 'not-finite' }

export type MeshOpenResult = { ok: true; model: MeshModel } | { ok: false; failure: MeshFailure }

/**
 * Opens a model for viewing. ⚠️ **Nothing here throws at the caller** — a corrupt file, an unsupported variant
 * and a model too big to paint are all {@link MeshFailure}s, so the stage has one branch and not five.
 */
export async function openMeshModel(source: Blob, fileName: string): Promise<MeshOpenResult> {
  const format = meshFormatOf(fileName)
  if (!format) return { ok: false, failure: { reason: 'unreadable' } }
  if (source.size <= 0) return { ok: false, failure: { reason: 'empty' } }
  if (source.size > MAX_BYTES) {
    return { ok: false, failure: { reason: 'too-large', limitBytes: MAX_BYTES } }
  }

  let geometry: BufferGeometry
  let parts = 1
  try {
    const buffer = await source.arrayBuffer()
    const parsed = format === 'obj' ? await parseObj(buffer) : await parseBinary(format, buffer)
    if (!parsed) return { ok: false, failure: { reason: 'empty' } }
    geometry = parsed.geometry
    parts = parsed.parts
  } catch {
    // A PLY whose header lies about its element counts, an STL truncated mid-triangle, a `.obj` that is
    // actually a Word document renamed. All the same answer.
    return { ok: false, failure: { reason: 'unreadable' } }
  }

  const position = geometry.getAttribute('position')
  const triangles = Math.floor((geometry.getIndex()?.count ?? position?.count ?? 0) / 3)

  if (!position || triangles <= 0) {
    geometry.dispose()
    return { ok: false, failure: { reason: 'empty' } }
  }
  if (triangles > MAX_TRIANGLES) {
    geometry.dispose()
    return {
      ok: false,
      failure: { reason: 'too-complex', triangles, limitTriangles: MAX_TRIANGLES },
    }
  }

  // ⚠️ Before the bounding box, not after: `computeBoundingBox` over a NaN happily produces a NaN box, and
  // every consumer of it — the camera fit, the grid, the measurement — then produces NaN in silence.
  if (!isFinitePositions(position.array)) {
    geometry.dispose()
    return { ok: false, failure: { reason: 'not-finite' } }
  }

  const computedNormals = !geometry.getAttribute('normal')
  if (computedNormals) geometry.computeVertexNormals()

  geometry.computeBoundingBox()
  const box = geometry.boundingBox
  if (!box) {
    geometry.dispose()
    return { ok: false, failure: { reason: 'empty' } }
  }

  const size = [box.max.x - box.min.x, box.max.y - box.min.y, box.max.z - box.min.z] as const
  const diagonal = Math.hypot(size[0], size[1], size[2])

  // A model whose every vertex is the same point parses cleanly and frames to a camera distance of zero.
  if (!(diagonal > 0)) {
    geometry.dispose()
    return { ok: false, failure: { reason: 'empty' } }
  }

  return {
    ok: true,
    model: {
      format,
      geometry,
      triangles,
      vertices: position.count,
      bounds: {
        min: [box.min.x, box.min.y, box.min.z],
        max: [box.max.x, box.max.y, box.max.z],
        size,
        centre: [
          (box.min.x + box.max.x) / 2,
          (box.min.y + box.max.y) / 2,
          (box.min.z + box.max.z) / 2,
        ],
        diagonal,
      },
      hasVertexColours: Boolean(geometry.getAttribute('color')),
      computedNormals,
      parts,
      release: () => geometry.dispose(),
    },
  }
}

/**
 * STL and PLY both parse straight from the buffer and both detect their own ASCII and binary forms, so there
 * is nothing to sniff here — which is the same conclusion `FileUploadValidator` reached from the other side.
 */
async function parseBinary(
  format: Exclude<MeshFormat, 'obj'>,
  buffer: ArrayBuffer,
): Promise<{ geometry: BufferGeometry; parts: number } | null> {
  if (format === 'stl') {
    const { STLLoader } = await import('three/examples/jsm/loaders/STLLoader.js')
    return { geometry: new STLLoader().parse(buffer), parts: 1 }
  }

  const { PLYLoader } = await import('three/examples/jsm/loaders/PLYLoader.js')
  return { geometry: new PLYLoader().parse(buffer), parts: 1 }
}

/**
 * OBJ is the awkward one of the three, in two ways this handles explicitly.
 *
 * <p>⚠️ **It is text, and the encoding is not declared.** `TextDecoder('utf-8')` is used non-fatally so that a
 * file written in Latin-1 — which happens, because OBJ predates the question — yields replacement characters in
 * a comment rather than throwing away a perfectly good mesh.</p>
 *
 * <p>⚠️ **One file may hold several objects, and it may also hold lines and points.** The loader returns a
 * group; only the meshes in it have triangles. They are concatenated rather than merged through
 * `BufferGeometryUtils`, because that helper requires every part to carry the same attributes and a real OBJ
 * routinely mixes parts that have UVs with parts that do not — which would fail the whole file over something
 * a viewer does not use. Normals are kept only when **every** part has them; otherwise they are computed once,
 * for the whole, because a model half smooth-shaded and half flat looks like a defect in the scan.</p>
 */
async function parseObj(buffer: ArrayBuffer): Promise<{ geometry: BufferGeometry; parts: number } | null> {
  const [{ OBJLoader }, { BufferGeometry: Geometry, BufferAttribute }] = await Promise.all([
    import('three/examples/jsm/loaders/OBJLoader.js'),
    import('three'),
  ])

  const text = new TextDecoder('utf-8', { fatal: false }).decode(buffer)
  const group = new OBJLoader().parse(text)

  const parts: BufferGeometry[] = []
  group.traverse((child) => {
    const geometry = (child as { geometry?: BufferGeometry }).geometry
    // `isMesh` rather than an `instanceof`: the loader also emits `LineSegments` and `Points` for an OBJ's
    // `l` and `p` records, and both of those carry a `geometry` with a position attribute too.
    if ((child as { isMesh?: boolean }).isMesh && geometry?.getAttribute('position')) {
      parts.push(geometry)
    }
  })

  if (parts.length === 0) return null
  if (parts.length === 1) return { geometry: parts[0], parts: 1 }

  const keepNormals = parts.every((part) => part.getAttribute('normal'))
  const merged = new Geometry()

  merged.setAttribute('position', new BufferAttribute(concat(parts, 'position'), 3))
  if (keepNormals) merged.setAttribute('normal', new BufferAttribute(concat(parts, 'normal'), 3))

  for (const part of parts) part.dispose()
  return { geometry: merged, parts: parts.length }
}

/** The named attribute of every part, end to end. Non-indexed throughout: `OBJLoader` never indexes. */
function concat(parts: BufferGeometry[], name: string): Float32Array {
  let length = 0
  for (const part of parts) {
    length += part.getAttribute(name).array.length
  }

  const out = new Float32Array(length)
  let at = 0
  for (const part of parts) {
    const array = part.getAttribute(name).array
    out.set(array as ArrayLike<number> & Float32Array, at)
    at += array.length
  }
  return out
}

/**
 * ⚠️ **Every coordinate, not a sample.** A single NaN anywhere poisons the bounding box, and a spot check of
 * the first few hundred vertices would pass a file whose corruption starts a megabyte in. The walk is one pass
 * over a typed array — microseconds against the parse that produced it.
 */
function isFinitePositions(array: ArrayLike<number>): boolean {
  for (let i = 0; i < array.length; i += 1) {
    if (!Number.isFinite(array[i])) return false
  }
  return true
}
