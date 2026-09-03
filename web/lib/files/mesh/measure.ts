/**
 * What a length on a mesh means, and what the viewer is obliged to say about it.
 *
 * <p>This is {@link ../dicom/window}'s `lengthCaveat` problem in a harder form. A DICOM usually declares its
 * pixel spacing and the caveat covers the case where it does not; STL, PLY and OBJ <b>never</b> declare
 * anything. The coordinates are bare floats, so the distance between two picked points is a number in units the
 * file does not name.</p>
 *
 * <h3>⚠️ The rule this module exists to enforce: corroborate, never assert</h3>
 *
 * <p>Dental scanners write millimetres — 3Shape, Medit and exocad all do — so millimetres is the right
 * <i>default</i>. It is not a right <i>claim</i>. A design package set to centimetres, or a mesh that has been
 * through a conversion, produces a file that looks identical and measures ten times wrong, and a viewer that
 * printed « 4,8 mm » over it would have invented a measurement rather than taken one.</p>
 *
 * <p>So three things happen instead, and the third is the one that actually works:</p>
 *
 * <ol>
 *   <li>The unit is a <b>control</b>, not a constant — millimetres by default, changeable.</li>
 *   <li>{@link inferUnit} says whether reading the file in that unit puts the model at a size a dental object
 *       plausibly is, and the caveat is graded on the answer.</li>
 *   <li>⚠️ <b>The viewer shows the model's own dimensions in the chosen unit, always.</b> « 62,1 × 48,3 ×
 *       21,0 mm » is an arch and « 6,2 × 4,8 × 2,1 mm » is not, and a dentist knows which they are looking at
 *       instantly. No sentence this module could write is worth as much as that one line, because it lets the
 *       reader check the assumption against the thing itself rather than trusting a heuristic.</li>
 * </ol>
 */
import type { MeshBounds } from './model'

/** The units an operator can read a model in. No format records one; every one of these is a choice. */
export type MeshUnit = 'mm' | 'cm' | 'm' | 'in'

export const MESH_UNITS: readonly MeshUnit[] = ['mm', 'cm', 'm', 'in']

/** How many millimetres one file unit is worth, if the file is read in that unit. */
const MILLIMETRES: Readonly<Record<MeshUnit, number>> = {
  mm: 1,
  cm: 10,
  m: 1000,
  in: 25.4,
}

/** What each is called on screen. `in` is « po » in French usage, but « pouces » is what a dentist reads. */
const NAMES: Readonly<Record<MeshUnit, string>> = {
  mm: 'mm',
  cm: 'cm',
  m: 'm',
  in: 'pouces',
}

/**
 * ⚠️ **The plausible range for a dental object, in real millimetres, and both ends are deliberate.** The floor
 * is a single prepared die — about 5 mm across — and the ceiling is an articulated full-mouth model on a base,
 * which reaches roughly 200 mm; 400 gives that headroom without admitting a model measured in metres. Anything
 * outside says « this reading of the file does not describe a dental object », which is a much weaker and much
 * more defensible claim than « the file is in centimetres ».
 */
const PLAUSIBLE_MIN_MM = 5
const PLAUSIBLE_MAX_MM = 400

export interface MeshScaleHint {
  /** What the viewer opens on. Millimetres unless the box makes that frankly impossible. */
  unit: MeshUnit
  /**
   * Every unit that would put the model at a plausible dental size — possibly none, possibly several.
   *
   * ⚠️ **Several is shown rather than hidden**: a box that reads sensibly as both millimetres and centimetres
   * is genuinely ambiguous, and saying so is the honest answer. Empty means no reading of this file describes
   * a dental object, which is worth saying loudest of all.
   */
  plausible: MeshUnit[]
}

/** Whether reading the file in `unit` puts the model at a size a dental object plausibly has. */
export function corroborates(hint: MeshScaleHint, unit: MeshUnit): boolean {
  return hint.plausible.includes(unit)
}

/**
 * The unit to open on, and how much the model's own size supports it.
 *
 * ⚠️ **This is a hint and never a finding.** It reads exactly one piece of evidence — the diagonal of the
 * bounding box — and a mesh that is genuinely a 2 mm fragment or a 900 mm impression tray will be judged
 * « not corroborated » while being perfectly correct. That is why nothing here changes the measurement; it only
 * changes how loudly the viewer hedges.
 */
export function inferUnit(bounds: Pick<MeshBounds, 'diagonal'>): MeshScaleHint {
  const plausible = MESH_UNITS.filter((unit) => {
    const millimetres = bounds.diagonal * MILLIMETRES[unit]
    return millimetres >= PLAUSIBLE_MIN_MM && millimetres <= PLAUSIBLE_MAX_MM
  })

  // Millimetres wins whenever it is in the running at all — it is what every dental scanner writes, so among
  // equally plausible readings it is overwhelmingly the likeliest.
  const unit = plausible.includes('mm') ? 'mm' : (plausible[0] ?? 'mm')

  return { unit, plausible }
}

/**
 * A length in file units, rendered in the chosen unit.
 *
 * ⚠️ **There is no conversion here, and that is the whole point.** Choosing « mm » does not scale the
 * coordinates — it *interprets* them, by asserting that the floats in the file were always millimetres. A
 * multiplication would mean the file had a known unit that we were converting away from, which is exactly the
 * thing these formats never give us.
 */
export function formatLength(fileUnits: number, unit: MeshUnit): string {
  return `${format(fileUnits, unit)} ${NAMES[unit]}`
}

/** The model's own dimensions — the line that lets a reader check the unit against the thing itself. */
export function formatExtent(bounds: Pick<MeshBounds, 'size'>, unit: MeshUnit): string {
  const [x, y, z] = bounds.size
  return `${format(x, unit)} × ${format(y, unit)} × ${format(z, unit)} ${NAMES[unit]}`
}

/**
 * Decimals by unit, so a figure carries about a tenth of a millimetre of real precision either way — the
 * resolution an intraoral scanner actually delivers, and past which extra digits are decoration.
 */
function format(value: number, unit: MeshUnit): string {
  const decimals = unit === 'mm' ? 1 : unit === 'cm' ? 2 : unit === 'in' ? 3 : 4
  return value.toLocaleString('fr-FR', {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  })
}

/**
 * What the viewer must say beside a measurement, graded by how well the box supports the unit.
 *
 * ⚠️ **Never null, unlike `lengthCaveat`'s.** A DICOM that declares millimetre spacing has genuinely stated its
 * scale and earns silence; no mesh ever does, so there is no reading of these three formats that deserves an
 * unqualified number. The strongest thing this can say is « you chose this unit ».
 */
export function unitCaveat(hint: MeshScaleHint, unit: MeshUnit): string {
  const named = NAMES[unit]
  const preamble = 'Ce format n’enregistre aucune unité :'

  if (!corroborates(hint, unit)) {
    return (
      `${preamble} les longueurs sont lues en ${named} parce que vous l’avez choisi. À cette échelle le ` +
      `modèle n’a pas une taille dentaire plausible — vérifiez l’encombrement ci-dessus avant de vous fier ` +
      `à une mesure.`
    )
  }

  const others = hint.plausible.filter((candidate) => candidate !== unit)
  if (others.length > 0) {
    return (
      `${preamble} les longueurs sont lues en ${named}, ce qui donne au modèle une taille plausible — mais ` +
      `${others.map((candidate) => NAMES[candidate]).join(' ou ')} le serait aussi. Vérifiez l’encombrement ` +
      `ci-dessus.`
    )
  }

  return (
    `${preamble} les longueurs sont lues en ${named}, la convention des scanners intra-oraux, et ` +
    `l’encombrement ci-dessus est cohérent avec ce choix.`
  )
}

/** A point picked on the surface, in model space. */
export interface MeshPoint {
  x: number
  y: number
  z: number
}

/** Straight-line distance in file units — the geometry, with no unit attached to it at all. */
export function distanceBetween(a: MeshPoint, b: MeshPoint): number {
  return Math.hypot(b.x - a.x, b.y - a.y, b.z - a.z)
}

/**
 * A measurement in progress or finished.
 *
 * ⚠️ **Straight-line, and the viewer must say so.** A dentist measuring across an arch is very often after the
 * distance *over the surface*, which is longer; a chord presented without qualification quietly under-reports
 * it. Geodesic measurement on an arbitrary mesh is a different and much larger piece of work, so the honest
 * move is to name what this is rather than to approximate what it is not.
 */
export interface MeshMeasurement {
  from: MeshPoint
  to: MeshPoint | null
}

export const STRAIGHT_LINE_NOTE =
  'Mesure en ligne droite, d’un point à l’autre : une distance suivant la courbure de la surface est plus ' +
  'longue.'
