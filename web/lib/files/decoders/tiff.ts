/**
 * TIFF, which no browser paints and a dental scanner exports by default.
 *
 * ⚠️ **The whole file is read into memory**, because that is what a TIFF decoder needs — the image data is
 * addressed by absolute offsets recorded in the tags, so there is no streaming form of this. {@link MAX_BYTES}
 * is what keeps a coffre original of several gigabytes from being attempted; past it the viewer says the file
 * is too large for a preview rather than taking the tab down with it.
 *
 * ⚠️ **The largest sub-image is chosen, not the first.** A TIFF is a container of « image file directories »,
 * and cameras and scanners routinely put a small thumbnail IFD ahead of the real page. The dimensions live in
 * the tags, so picking is free: only the chosen page's pixels are ever decompressed.
 */
import { fitWithin, encodeRgba, type DecodedImage } from './raster'

/** A radiograph or a full-mouth series, with room to spare. Above it, no preview is offered. */
const MAX_BYTES = 120 * 1024 * 1024

/** TIFF tag 256 — ImageWidth. */
const TAG_WIDTH = 't256'
/** TIFF tag 257 — ImageLength (the height). */
const TAG_HEIGHT = 't257'

/**
 * The largest page of a TIFF, as a JPEG. Null covers every ordinary refusal — too large, unreadable, or a
 * container with no decodable page in it — so nothing here throws at the caller.
 */
export async function decodeTiff(source: Blob): Promise<DecodedImage | null> {
  if (source.size <= 0 || source.size > MAX_BYTES) return null

  const buffer = await source.arrayBuffer()
  const UTIF = await loadUtif()

  let pages: TiffPage[]
  try {
    pages = UTIF.decode(buffer) as TiffPage[]
  } catch {
    return null
  }
  if (!pages || pages.length === 0) return null

  const chosen = largestPage(pages)
  if (!chosen) return null

  try {
    UTIF.decodeImage(buffer, chosen)
    const rgba = UTIF.toRGBA8(chosen)
    if (!rgba || rgba.length === 0) return null

    // `decodeImage` is what sets these; before it they are absent, which is why the choice above reads tags.
    const width = chosen.width
    const height = chosen.height
    if (!width || !height) return null

    const blob = await encodeRgba(rgba, width, height)
    if (!blob) return null

    const fitted = fitWithin(width, height)
    return { blob, width: fitted.width, height: fitted.height, pages: pages.length }
  } catch {
    return null
  }
}

/**
 * One image file directory, described structurally rather than imported from `utif2`.
 *
 * ⚠️ Its own `.d.ts` opens with `import 'node'`, which does not resolve in a browser project; `skipLibCheck`
 * hides that today, and a local shape means this file does not depend on it continuing to.
 * `width` and `height` are absent until `decodeImage` has run — that is why {@link largestPage} reads tags.
 */
interface TiffPage extends Record<string, unknown> {
  width?: number
  height?: number
}

/** What `utif2` exports, whether the bundler hands it back as a namespace or under `default`. */
interface Utif {
  decode(buffer: ArrayBuffer): TiffPage[]
  decodeImage(buffer: ArrayBuffer, page: TiffPage): void
  toRGBA8(page: TiffPage): Uint8Array
}

async function loadUtif(): Promise<Utif> {
  const imported = (await import('utif2')) as unknown as Utif & { default?: Utif }

  // A CommonJS module reached through `import()`: some bundlers hand back the namespace, others park the whole
  // `module.exports` under `default`. Both shapes appear depending on how the graph was built.
  return typeof imported.decode === 'function' ? imported : imported.default!
}

/** The page with the most pixels, read from tags alone so nothing is decompressed to compare. */
function largestPage<T extends Record<string, unknown>>(pages: T[]): T | null {
  let best: T | null = null
  let bestArea = 0

  for (const page of pages) {
    const width = firstNumber(page[TAG_WIDTH])
    const height = firstNumber(page[TAG_HEIGHT])
    if (!width || !height) continue

    const area = width * height
    if (area > bestArea) {
      best = page
      bestArea = area
    }
  }

  // A container whose pages carry no dimension tags is not necessarily undecodable — try the first one.
  return best ?? pages[0] ?? null
}

/** Tag values are arrays, even when they hold one number. */
function firstNumber(value: unknown): number | null {
  if (typeof value === 'number') return value
  if (Array.isArray(value) && typeof value[0] === 'number') return value[0]
  return null
}
