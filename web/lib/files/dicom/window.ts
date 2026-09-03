/**
 * Choosing a window, and turning stored readings into greys — the one place either happens.
 *
 * ⚠️ **The window is not a brightness slider; it decides what is in the picture at all.** A DICOM stores 12- or
 * 16-bit sensor readings, optionally rescaled into another unit; 256 greys have to come from a slice of that
 * range, and anything outside the slice is clipped to black or to white. So a lesion the operator has windowed
 * past is not dim — it is **absent**. That is why every rendering this module feeds carries an advisory, and why
 * the numbers are shown rather than hidden behind a slider with no scale.
 *
 * ⚠️ **The photometric inversion is applied HERE and nowhere else.** `MONOCHROME1` runs bright-to-dark, so a
 * frame rendered as `MONOCHROME2` is a photographic negative of itself — bone dark, air bright — which reads as
 * a *finding*. It is folded into the lookup table together with the user's own « Inverser », as an XOR, so the
 * two cannot fight and neither can be applied twice. `check:responsive`'s `monochrome1-has-one-owner` fails the
 * gate on a second place comparing that literal.
 *
 * <h3>Why a lookup table and not per-pixel arithmetic</h3>
 *
 * Every transformation between a stored reading and a packed RGBA pixel — the signed read, the modality
 * rescale, the linear VOI, the clip, the inversion, the alpha — depends only on the **stored value**, of which
 * there are at most 65 536. So they all collapse into one `Uint32Array` built once per window change, and the
 * inner loop becomes a lookup and a store. Measured on this machine (Node/V8, same engine as Chrome), per
 * repaint:
 *
 * | frame | per-pixel arithmetic | lookup table |
 * |---|---|---|
 * | 440×440 (`radiographie-thorax-mono1`) | 8,8 ms | **1,8 ms** |
 * | 512×512 (`coupe-ct-512`) | 12,9 ms | **9,6 ms** |
 * | 2800×1400 (panoramique) | 36,5 ms | **23,2 ms** |
 * | 2560×2560 (capteur plein champ) | 75,2 ms | **34,1 ms** |
 *
 * Building the table costs 0,5–0,95 ms, so a window drag is bounded by the paint and not by the maths. That is
 * what makes a drag on a real panoramique feel continuous instead of stepping.
 */
import type { DicomFrame, DicomStudy, DicomWindow } from './study'

export type { DicomWindow } from './study'

type GreyFrame = Extract<DicomFrame, { kind: 'grey' }>

/**
 * ⚠️ **The one endianness-sensitive byte is alpha, and getting it wrong paints a red image, not a wrong one.**
 * A grey pixel has R = G = B, so channel order is immaterial — but `0xff000000` is alpha on a little-endian
 * machine and *red* on a big-endian one. Detected once here and folded into the table, so the inner loop stays
 * a single store. Every browser this runs on is little-endian; the branch costs one comparison at module load
 * and removes a class of bug that would be invisible until it was not.
 */
const LITTLE_ENDIAN = new Uint8Array(new Uint32Array([1]).buffer)[0] === 1

/** What the frame actually contains, over its own stored values. Computed once per frame and cached by callers. */
export interface FrameStats {
  /** The lowest and highest **rescaled** values present. Ordered, whatever the sign of the slope. */
  low: number
  high: number
  /** Count per stored value, indexed exactly as {@link buildPackedLut}'s table is. */
  histogram: Uint32Array
  pixels: number
}

/** A window offered as a choice, with a label that says where it came from. */
export interface DicomPreset {
  id: string
  label: string
  window: DicomWindow
}

/** How many entries a lookup table over this frame's stored values needs. */
function lutSize(frame: GreyFrame): number {
  return frame.bits === 8 ? 256 : 65536
}

/** The mask that turns a possibly-signed stored reading into its table index. */
function lutMask(frame: GreyFrame): number {
  return frame.bits === 8 ? 0xff : 0xffff
}

/** The stored reading a table index stands for — the inverse of the mask, sign restored. */
function storedAt(index: number, frame: GreyFrame): number {
  if (!frame.signed) return index
  const size = lutSize(frame)
  return index >= size / 2 ? index - size : index
}

/** Table indices in ascending order of the value they stand for. Signed data holds its negatives up top. */
function indexInValueOrder(rank: number, frame: GreyFrame): number {
  const size = lutSize(frame)
  return frame.signed ? (rank + size / 2) % size : rank
}

/**
 * Stored value → packed RGBA, with the modality rescale, the linear VOI LUT, the clip and both inversions
 * already folded in.
 *
 * ⚠️ **Deliberately NOT exported.** It is the only place the photometric inversion is applied, and module
 * privacy is a stronger guarantee of that than any grep: a caller that could build its own table could invert
 * a second time, and a doubly-inverted radiograph is simply the original one — correct-looking, and wrong for
 * every `MONOCHROME2` file beside it. `renderFrame` is the whole public surface.
 *
 * @param invert the user's own « Inverser ». XORed with the file's photometric interpretation, never added.
 */
function buildPackedLut(
  study: Pick<DicomStudy, 'slope' | 'intercept' | 'inverted'>,
  frame: GreyFrame,
  window: DicomWindow,
  invert: boolean,
): Uint32Array {
  const size = lutSize(frame)
  const table = new Uint32Array(size)

  // The DICOM linear VOI LUT, written exactly as PS3.3 C.11.2.1.2 states it.
  const lower = window.centre - 0.5 - (window.width - 1) / 2
  const span = window.width - 1 || 1
  const flipped = study.inverted !== invert

  for (let index = 0; index < size; index++) {
    const value = storedAt(index, frame) * study.slope + study.intercept
    let grey = ((value - lower) / span) * 255
    if (grey < 0) grey = 0
    else if (grey > 255) grey = 255
    grey = flipped ? 255 - grey : grey

    const level = grey | 0
    table[index] = LITTLE_ENDIAN
      ? 0xff000000 | (level << 16) | (level << 8) | level
      : (level << 24) | (level << 16) | (level << 8) | 0xff
  }

  return table
}

/**
 * The frame, windowed, as RGBA ready for `putImageData`.
 *
 * `into` lets the caller keep one buffer for the life of the viewer rather than allocating 96 Mo per repaint;
 * a buffer of the wrong size is replaced rather than trusted.
 */
export function renderFrame(
  study: Pick<DicomStudy, 'slope' | 'intercept' | 'inverted'>,
  frame: DicomFrame,
  window: DicomWindow,
  invert: boolean,
  pixels: number,
  into?: Uint8ClampedArray,
): Uint8ClampedArray {
  const rgba = into && into.length === pixels * 4 ? into : new Uint8ClampedArray(pixels * 4)

  if (frame.kind === 'colour') {
    // A colour DICOM has no window to choose — the samples are already brightnesses. Inversion is still a
    // display operation and stays available, because reading a photograph as a negative is occasionally useful
    // and costs nothing to offer.
    for (let i = 0; i < rgba.length; i += 4) {
      rgba[i] = invert ? 255 - frame.rgba[i] : frame.rgba[i]
      rgba[i + 1] = invert ? 255 - frame.rgba[i + 1] : frame.rgba[i + 1]
      rgba[i + 2] = invert ? 255 - frame.rgba[i + 2] : frame.rgba[i + 2]
      rgba[i + 3] = 255
    }
    return rgba
  }

  const table = buildPackedLut(study, frame, window, invert)
  const mask = lutMask(frame)
  const stored = frame.stored
  const packed = new Uint32Array(rgba.buffer, rgba.byteOffset, pixels)

  for (let i = 0; i < pixels; i++) packed[i] = table[stored[i] & mask]

  return rgba
}

/** What is actually in the frame — one pass, and the basis of every derived window. */
export function frameStats(
  study: Pick<DicomStudy, 'slope' | 'intercept'>,
  frame: GreyFrame,
): FrameStats {
  const size = lutSize(frame)
  const mask = lutMask(frame)
  const histogram = new Uint32Array(size)
  const stored = frame.stored

  for (let i = 0; i < stored.length; i++) histogram[stored[i] & mask]++

  let lowIndex = -1
  let highIndex = -1
  for (let rank = 0; rank < size; rank++) {
    const index = indexInValueOrder(rank, frame)
    if (histogram[index] === 0) continue
    if (lowIndex < 0) lowIndex = index
    highIndex = index
  }

  // An empty frame cannot happen (the geometry is checked at open), but a zero-length view would land here.
  if (lowIndex < 0) return { low: 0, high: 1, histogram, pixels: stored.length }

  const a = storedAt(lowIndex, frame) * study.slope + study.intercept
  const b = storedAt(highIndex, frame) * study.slope + study.intercept

  return { low: Math.min(a, b), high: Math.max(a, b), histogram, pixels: stored.length, }
}

/** The rescaled value at a percentile of the frame's own distribution. */
function valueAtPercentile(
  study: Pick<DicomStudy, 'slope' | 'intercept'>,
  frame: GreyFrame,
  stats: FrameStats,
  percentile: number,
): number {
  const target = (stats.pixels * percentile) / 100
  const size = lutSize(frame)
  let seen = 0

  for (let rank = 0; rank < size; rank++) {
    const index = indexInValueOrder(rank, frame)
    seen += stats.histogram[index]
    if (seen >= target) return storedAt(index, frame) * study.slope + study.intercept
  }
  return stats.high
}

/** A window over an ordered pair of values, never narrower than one level. */
function windowBetween(low: number, high: number): DicomWindow {
  const width = Math.max(1, high - low)
  return { centre: low + width / 2, width }
}

/**
 * The windows to offer, and the reasoning behind this particular list is the whole point.
 *
 * ⚠️ **There are deliberately NO « poumon / os / tissus mous » presets, and their absence is a clinical
 * decision rather than an omission.** Those are fixed Hounsfield windows, and they are only meaningful on
 * values that are really Hounsfield units. Two facts make that a bad bet in a dental product:
 *
 * 1. **CBCT — the DICOM a dental practice actually produces — is not HU-calibrated.** Its grey values shift
 *    with the machine, the field of view and the reconstruction, which is why CBCT literature calls them
 *    « grey values » and not HU. A fixed « Os » window would land somewhere different on every cabinet's
 *    scanner, while *looking* like a standard.
 * 2. **The file rarely says.** `RescaleType` is the tag that would authorise reading the values as HU, and of
 *    the real samples in `follow-up/decoder-samples/` — including `coupe-ct-512.dcm`, a genuine CT — **not one
 *    carries it**. So detection would come down to `Modality == 'CT'`, which a CBCT also reports. That is
 *    exactly the confidently-wrong output this product's rules exist to prevent.
 *
 * What is offered instead is anchored on this file and this frame:
 *
 * - **the window(s) the file itself declares**, under the device's own name where it gave one. The machine that
 *   made the picture chose them, which beats anything this app could invent;
 * - **« Étendue complète »** — nothing clipped, the honest default when the file declares nothing;
 * - **« Contraste renforcé »** — the 2nd to 98th percentile of this frame, which is what makes an
 *   under-exposed radiograph readable without asserting anything about tissue.
 *
 * And the control a dentist actually reaches for is **« Inverser »**, which is not a preset at all: reading a
 * radiograph as a negative is a routine technique for caries and periapical lesions, and no chest preset
 * provides it. It lives on the toolbar beside these.
 */
export function presetsFor(
  study: Pick<DicomStudy, 'slope' | 'intercept' | 'declaredWindows'>,
  frame: GreyFrame,
  stats: FrameStats,
): DicomPreset[] {
  const presets: DicomPreset[] = []

  study.declaredWindows.forEach((declared, index) => {
    presets.push({
      id: `file-${index}`,
      label:
        declared.label ??
        (study.declaredWindows.length > 1 ? `Fenêtre du fichier ${index + 1}` : 'Fenêtre du fichier'),
      window: declared.window,
    })
  })

  presets.push({ id: 'full', label: 'Étendue complète', window: windowBetween(stats.low, stats.high) })
  presets.push({
    id: 'contrast',
    label: 'Contraste renforcé',
    window: windowBetween(
      valueAtPercentile(study, frame, stats, 2),
      valueAtPercentile(study, frame, stats, 98),
    ),
  })

  return presets
}

/**
 * The window a frame opens on.
 *
 * ⚠️ **Shared with `decodeDicom`, deliberately.** The stored stand-in a file carries in its drawer and the
 * viewer's first paint have to agree, or opening the viewer would appear to change the image before the user
 * touched anything.
 */
export function defaultWindowFor(
  study: Pick<DicomStudy, 'slope' | 'intercept' | 'declaredWindows'>,
  frame: GreyFrame,
  stats: FrameStats,
): DicomWindow {
  return study.declaredWindows[0]?.window ?? windowBetween(stats.low, stats.high)
}

/** A number with French decimals, at a precision that suits its magnitude rather than a fixed two places. */
function frenchNumber(value: number, digits: number): string {
  return value.toLocaleString('fr-FR', { minimumFractionDigits: digits, maximumFractionDigits: digits })
}

/**
 * The window, as it is shown on the stage.
 *
 * ⚠️ **The unit word is not decoration.** On a frame decoded out of a lossy 8-bit JPEG these numbers are
 * display levels the exporter already produced, not sensor readings — printing them the same way as a raw
 * 16-bit CT's would claim a calibration that is not there.
 */
export function formatWindow(
  study: Pick<DicomStudy, 'rescaleType' | 'valuesAreRendered'>,
  window: DicomWindow,
): { readout: string; unit: string } {
  const digits = Math.abs(window.width) < 20 ? 1 : 0
  return {
    readout: `C ${frenchNumber(window.centre, digits)} · L ${frenchNumber(window.width, digits)}`,
    unit: study.valuesAreRendered
      ? "niveaux d'affichage"
      : study.rescaleType === 'HU'
        ? 'unités Hounsfield'
        : 'valeurs stockées',
  }
}

/**
 * A distance across the frame, in whatever unit the file actually authorises.
 *
 * ⚠️ **Three states, and collapsing them is the defect this function exists to prevent.** Saying « 12,4 mm »
 * over a file with no `PixelSpacing` is a number invented from a guess, and the most realistic sample in the
 * test set — `radiographie-thorax-mono1.dcm`, a real CR of the same family as a dental cliché — carries no
 * spacing tag at all. So « no scale » is the ordinary case, not an edge case, and it measures in pixels and
 * says so. The middle state, `ImagerPixelSpacing`, is a true measurement **at the detector**: the projection's
 * own magnification is not corrected and nothing in the file says what it is, so the qualifier travels with the
 * figure rather than living in a footnote somebody can scroll past.
 *
 * @param dx horizontal distance in image pixels (columns)
 * @param dy vertical distance in image pixels (rows)
 */
export function formatLength(study: Pick<DicomStudy, 'spacing'>, dx: number, dy: number): string {
  const spacing = study.spacing
  if (!spacing) {
    return `${frenchNumber(Math.hypot(dx, dy), 0)} px`
  }

  const mm = Math.hypot(dx * spacing.column, dy * spacing.row)
  const figure = `${frenchNumber(mm, mm < 10 ? 2 : 1)} mm`
  return spacing.source === 'imager' ? `${figure} au capteur` : figure
}

/**
 * What the viewer must say about its own ruler, once, beside the picture. Null when `PixelSpacing` gives a real
 * distance in the patient and there is nothing to qualify.
 */
export function lengthCaveat(study: Pick<DicomStudy, 'spacing'>): string | null {
  if (!study.spacing) {
    return "Ce fichier ne porte pas d’échelle : les longueurs sont mesurées en pixels, pas en millimètres."
  }
  if (study.spacing.source === 'imager') {
    return (
      "Les longueurs sont mesurées au capteur : le grandissement de la projection n’est pas corrigé, " +
      'la distance réelle est plus petite.'
    )
  }
  return null
}
