// Generates test meshes: a dental-arch-like horseshoe in binary STL, plus an ASCII STL and a PLY,
// sized so the bounding box reads plausibly in millimetres (~62 x 48 x 12).
import fs from 'node:fs'

function archTriangles({ radiusX = 26, radiusY = 22, tube = 5.5, arc = Math.PI * 1.15, seg = 96, ring = 20 }) {
  const pts = (i, j) => {
    const t = -arc / 2 + (arc * i) / seg
    const v = (2 * Math.PI * j) / ring
    // Centre-line of a horseshoe, with a slight occlusal rise so the model is not flat.
    const cx = Math.sin(t) * radiusX
    const cy = Math.cos(t) * radiusY
    const rise = Math.cos(t) * 1.5
    // Frame
    const dx = Math.cos(t) * radiusX
    const dy = -Math.sin(t) * radiusY
    const len = Math.hypot(dx, dy) || 1
    const nx = -dy / len
    const ny = dx / len
    const r = tube * (0.75 + 0.25 * Math.cos(v))
    return [cx + nx * r, cy + ny * r, rise + Math.sin(v) * tube * 0.9]
  }

  const tris = []
  for (let i = 0; i < seg; i++) {
    for (let j = 0; j < ring; j++) {
      const a = pts(i, j)
      const b = pts(i + 1, j)
      const c = pts(i + 1, j + 1)
      const d = pts(i, j + 1)
      tris.push([a, b, c], [a, c, d])
    }
  }
  return tris
}

function normalOf([a, b, c]) {
  const u = [b[0] - a[0], b[1] - a[1], b[2] - a[2]]
  const v = [c[0] - a[0], c[1] - a[1], c[2] - a[2]]
  const n = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]]
  const l = Math.hypot(...n) || 1
  return [n[0] / l, n[1] / l, n[2] / l]
}

function writeBinaryStl(path, tris) {
  const buf = Buffer.alloc(84 + tris.length * 50)
  buf.write('Arche de test - genere pour la visionneuse 3D', 0, 80, 'ascii')
  buf.writeUInt32LE(tris.length, 80)
  let o = 84
  for (const t of tris) {
    const n = normalOf(t)
    for (const value of n) { buf.writeFloatLE(value, o); o += 4 }
    for (const p of t) for (const value of p) { buf.writeFloatLE(value, o); o += 4 }
    buf.writeUInt16LE(0, o); o += 2
  }
  fs.writeFileSync(path, buf)
  return buf.length
}

function writeAsciiStl(path, tris) {
  const out = ['solid arche']
  for (const t of tris) {
    const n = normalOf(t)
    out.push(`facet normal ${n[0]} ${n[1]} ${n[2]}`, '  outer loop')
    for (const p of t) out.push(`    vertex ${p[0]} ${p[1]} ${p[2]}`)
    out.push('  endloop', 'endfacet')
  }
  out.push('endsolid arche')
  fs.writeFileSync(path, out.join('\n'))
}

/** PLY with per-vertex colour, which is what a colour intraoral scan looks like. */
function writePly(path, tris) {
  const verts = []
  const faces = []
  for (const t of tris) {
    const base = verts.length
    for (const p of t) verts.push(p)
    faces.push([base, base + 1, base + 2])
  }
  const header = [
    'ply', 'format ascii 1.0',
    `element vertex ${verts.length}`,
    'property float x', 'property float y', 'property float z',
    'property uchar red', 'property uchar green', 'property uchar blue',
    `element face ${faces.length}`,
    'property list uchar int vertex_index',
    'end_header',
  ]
  const body = verts.map((p) => {
    // Gum-pink low down, enamel-white on top: obvious enough to tell colour is being read.
    const t = Math.min(1, Math.max(0, (p[2] + 5) / 12))
    const r = Math.round(200 + 40 * t), g = Math.round(120 + 125 * t), b = Math.round(120 + 120 * t)
    return `${p[0]} ${p[1]} ${p[2]} ${r} ${g} ${b}`
  })
  const f = faces.map((x) => `3 ${x[0]} ${x[1]} ${x[2]}`)
  fs.writeFileSync(path, [...header, ...body, ...f].join('\n'))
}

/** OBJ with TWO named objects, to exercise the merge path. */
function writeObj(path, tris) {
  const out = []
  let n = 0
  const half = Math.floor(tris.length / 2)
  tris.forEach((t, i) => {
    if (i === 0) out.push('o arcade_superieure')
    if (i === half) out.push('o arcade_inferieure')
    for (const p of t) out.push(`v ${p[0]} ${p[1]} ${p[2]}`)
    out.push(`f ${n + 1} ${n + 2} ${n + 3}`)
    n += 3
  })
  fs.writeFileSync(path, out.join('\n'))
}

const dir = process.argv[2] || '.'
const tris = archTriangles({})
console.log('triangles:', tris.length)

const xs = tris.flat().map((p) => p[0]), ys = tris.flat().map((p) => p[1]), zs = tris.flat().map((p) => p[2])
const ext = [Math.max(...xs) - Math.min(...xs), Math.max(...ys) - Math.min(...ys), Math.max(...zs) - Math.min(...zs)]
console.log('extent (file units):', ext.map((v) => v.toFixed(1)).join(' x '))

console.log('binary stl bytes:', writeBinaryStl(`${dir}/arcade-superieure.stl`, tris))
writeAsciiStl(`${dir}/arcade-ascii.stl`, tris)
writePly(`${dir}/arcade-couleur.ply`, tris)
writeObj(`${dir}/arcade-deux-objets.obj`, tris)
console.log('wrote 4 files to', dir)
