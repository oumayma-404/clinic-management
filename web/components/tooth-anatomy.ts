/**
 * Tooth **geometry** — the shapes the symbol view draws on, and nothing about what they mean.
 *
 * ## Why a second geometry module exists beside `record-tooth-chart`'s `TOOTH_SHAPES`
 *
 * That one is a **single silhouette per tooth class**, sized 18–22 × 30 px, meant to be filled with one colour.
 * It answers « which tooth is this » and has nowhere to put a symbol: an implant's threads live in the *root*, a
 * couronne's margin sits at the *cervix*, a périapicale is a halo *past the apex* — none of which exist in a
 * shape that has no root and no cervical line. So this module draws the tooth **from the side, crown and roots
 * apart**, which is the only view those marks can be placed on.
 *
 * The two are not duplicates and neither replaces the other: the fiche's chart paints one act-colour per tooth
 * and is right to stay a silhouette; this one carries a clinical symbol set. See `tooth-symbols.tsx`.
 *
 * ## The canonical box
 *
 * Every path is written in **one coordinate box, crown at the BOTTOM and root at the TOP** — the orientation of
 * an upper tooth. The lower arch is the same drawing flipped once ({@link LOWER_ARCH_TRANSFORM}), which is why a
 * symbol only ever has to be authored once: an implant placed in the root is in the root on both jaws.
 *
 * ⚠️ Nothing here may contain **text**. The lower arch is drawn by a vertical flip, and a `<text>` inside it
 * comes out mirrored. Tooth numbers are DOM, outside the SVG, exactly as they are today.
 */

/** The four crown shapes a chart distinguishes. */
export type ToothKind = "incisor" | "canine" | "premolar" | "molar"

/**
 * Which crown shape an FDI number wears.
 *
 * ⚠️ **A deciduous 54 is a MOLAR, not a premolar** — the primary dentition has no premolars at all; positions 4
 * and 5 of quadrants 5–8 are the first and second primary molars. Keying on the last digit alone (as the fiche
 * chart's `toothKind` does, where every shape is a plain silhouette and the error is invisible) would draw a
 * child's molars as premolars on the one view whose whole point is that the shape carries meaning.
 */
export function toothKind(fdi: number): ToothKind {
  const index = fdi % 10
  const deciduous = Math.floor(fdi / 10) >= 5
  if (index <= 2) return "incisor"
  if (index === 3) return "canine"
  if (index <= 5) return deciduous ? "molar" : "premolar"
  return "molar"
}

/** Upper jaw (FDI quadrants 1, 2, 5, 6) — the canonical orientation. Lower jaw is the flip. */
export function isUpperTooth(fdi: number): boolean {
  const quadrant = Math.floor(fdi / 10)
  return quadrant === 1 || quadrant === 2 || quadrant === 5 || quadrant === 6
}

/**
 * The patient's RIGHT (FDI quadrants 1, 4, 5, 8), which is the **viewer's left**.
 *
 * Mésial means « toward the midline », so on the viewer's left half of the chart the mesial face is on the
 * right of the cell and the two swap. Without this a carie MO renders on the wrong side of half the mouth —
 * silently, and only a dentist would catch it.
 */
export function isPatientRightTooth(fdi: number): boolean {
  const quadrant = Math.floor(fdi / 10)
  return quadrant === 1 || quadrant === 4 || quadrant === 5 || quadrant === 8
}

export interface ToothAnatomy {
  /** Crown outline, cervix to incisal/occlusal edge. Also the clip path for anything drawn *inside* the crown. */
  crown: string
  /** One closed path per root. Two for a molar — see the note on root counts below. */
  roots: string[]
  /** Root canal axes as `[x1, y1, x2, y2]`, cervix → just short of the apex. One per root. */
  canals: [number, number, number, number][]
  /** Apex points, where a périapicale halo is centred. One per root. */
  apex: [number, number][]
  /** Mesio-distal extent at the cervix — what a cervical line or a bone level spans. */
  span: [number, number]
  /** y of the cervical line (the crown/root junction). */
  neck: number
}

/**
 * ⚠️ **Root COUNT is deliberately simplified to one or two** — an upper molar really has three and an upper
 * first premolar often two. This mirrors the catalogue seed's own recorded decision to collapse mono- vs
 * pluriradiculaire because the distinction « read as noise ». The chart is a diagram, not a radiograph: what it
 * has to say is *there is a root canal here*, and a third root would halve the width of each one at 30 px.
 */
export const TOOTH_ANATOMY: Record<ToothKind, ToothAnatomy> = {
  incisor: {
    crown:
      "M30 102 C30 93 36 86 50 86 C64 86 70 93 70 102 C71 114 72 132 70 144 C70 149 66 151 50 151 C34 151 30 149 30 144 C28 132 29 114 30 102 Z",
    roots: ["M37 88 C38 60 35 30 43 10 C46 3 54 3 57 10 C65 30 62 60 63 88 Z"],
    canals: [[50, 90, 50, 20]],
    apex: [[50, 13]],
    span: [37, 63],
    neck: 88,
  },
  canine: {
    crown:
      "M28 102 C28 93 34 86 50 86 C66 86 72 93 72 102 C73 112 74 124 72 134 C68 146 58 154 50 154 C42 154 32 146 28 134 C26 124 27 112 28 102 Z",
    roots: ["M34 88 C36 58 32 22 42 5 C45 -1 55 -1 58 5 C68 22 64 58 66 88 Z"],
    canals: [[50, 90, 50, 14]],
    apex: [[50, 8]],
    span: [34, 66],
    neck: 88,
  },
  premolar: {
    crown:
      "M26 102 C26 93 33 86 50 86 C67 86 74 93 74 102 C75 118 77 136 74 146 C68 152 61 148 55 140 C52 136 48 136 45 140 C39 148 32 152 26 146 C23 136 25 118 26 102 Z",
    roots: ["M31 88 C33 62 31 32 40 12 C43 4 57 4 60 12 C69 32 67 62 69 88 Z"],
    canals: [[50, 90, 50, 22]],
    apex: [[50, 15]],
    span: [31, 69],
    neck: 88,
  },
  molar: {
    crown:
      "M18 102 C18 92 27 85 50 85 C73 85 82 92 82 102 C83 118 84 138 80 147 C74 153 64 150 58 142 C54 137 46 137 42 142 C36 150 26 153 20 147 C16 138 17 118 18 102 Z",
    roots: [
      "M24 89 C25 66 21 34 30 13 C33 5 41 5 43 13 C47 36 45 64 46 89 Z",
      "M76 89 C75 66 79 34 70 13 C67 5 59 5 57 13 C53 36 55 64 54 89 Z",
    ],
    canals: [
      [34, 90, 32, 22],
      [66, 90, 68, 22],
    ],
    apex: [
      [33, 15],
      [67, 15],
    ],
    span: [23, 77],
    neck: 87,
  },
}

/**
 * The drawing box, wide enough for what the *symbols* reach — not just the tooth.
 *
 * The canine's root control points go to y = −1 and the « à traiter » asterisk sits outside the crown's mesial
 * edge, so a viewBox fitted to the silhouette alone would clip both. Height/width is 172/100, so a 30 px wide
 * cell is 52 px tall.
 */
export const TOOTH_VIEWBOX = "0 -8 100 172"
export const TOOTH_ASPECT = 172 / 100

/**
 * Flip for the lower arch: `y → 156 − y`, which maps the viewBox onto itself.
 *
 * Derived from {@link TOOTH_VIEWBOX} (−8 … 164): −8 ↦ 164 and 164 ↦ −8. Change one and this must move with it.
 */
export const LOWER_ARCH_TRANSFORM = "translate(0 156) scale(1 -1)"

/**
 * The five faces as an occlusal cell, in their own 0–100 box: four trapezoids around a central square.
 *
 * `V` is toward the cheek and `L` toward the tongue on **both** jaws, because the cell is read as if looking
 * into the mouth. `M`/`D` swap per side — see {@link isPatientRightTooth}.
 */
export const OCCLUSAL_ZONES: Record<string, string> = {
  V: "2,2 98,2 70,32 30,32",
  L: "2,98 98,98 70,68 30,68",
  M: "2,2 30,32 30,68 2,98",
  D: "98,2 70,32 70,68 98,98",
  O: "30,32 70,32 70,68 30,68",
}

/** The cell's own dividing lines, drawn over any fill so the five faces stay countable. */
export const OCCLUSAL_GRID: [number, number, number, number][] = [
  [30, 32, 30, 68],
  [70, 32, 70, 68],
  [30, 32, 70, 32],
  [30, 68, 70, 68],
  [2, 2, 30, 32],
  [98, 2, 70, 32],
  [2, 98, 30, 68],
  [98, 98, 70, 68],
]

/**
 * Resolve a stored surface letter to the zone it paints on **this** tooth.
 *
 * Returns `null` for a letter outside `MODVL`, which `parseSurfaces` already refuses on the way in — this is
 * the read-side twin, so a value that somehow got past it draws nothing rather than throwing on a chart.
 */
export function surfaceZone(fdi: number, letter: string): string | null {
  return OCCLUSAL_ZONES[surfaceZoneCode(fdi, letter)] ?? null
}

/**
 * The **geometric** zone a stored surface letter occupies on this tooth — i.e. which of `OCCLUSAL_ZONES`' five
 * keys it lands on, after the mésial/distal mirror.
 *
 * <p>⚠️ **Extracted so the mirror has exactly one owner, because a second copy of it shipped and was wrong for
 * half the mouth.** `surfaceZone` swaps M↔D on the patient's right, and `tooth-symbols.tsx`'s
 * `ZONE_LABEL_POSITION` — which decides where the letter sits inside its trapezoid — was a fixed map that
 * always put M on the left and D on the right. So on quadrants 1 and 4 the « M » button was clipped to the
 * RIGHT trapezoid while its letter was positioned at the LEFT edge: outside its own `clip-path`, and therefore
 * **not painted at all**. Measured on tooth 16 at 820 px — the picker showed V, O and L with two unlabelled,
 * silent hit zones, on the one control whose entire subject is *where on the tooth*.</p>
 *
 * <p>Anything that positions something inside a face's shape must key on this, never on the stored letter.</p>
 */
export function surfaceZoneCode(fdi: number, letter: string): string {
  const code = letter.toUpperCase()
  if (isPatientRightTooth(fdi)) {
    if (code === "M") return "D"
    if (code === "D") return "M"
  }
  return code
}
