/**
 * Where the letterhead's two bands are on the page, and cutting them out.
 *
 * Positions are FRACTIONS of the page height (0 = top, 1 = bottom), so they survive the on-screen page being any
 * size. The bands are cut at full resolution from the page canvas, cleaned, and encoded as PNG — never JPEG, whose
 * ringing around printed text is exactly the « amateur » look this feature exists to avoid.
 */

import type { LetterheadBands } from "@/lib/api/clinics"

/** A4's height over its width — what maps a band's pixel height onto millimetres. */
export const A4_HEIGHT_MM = 297
export const A4_WIDTH_MM = 210

export interface BandCuts {
  /** Where the header band ends. */
  headerEnd: number
  /** Where the footer band starts, or null when the paper has no footer. */
  footerStart: number | null
}

/** A band's printed height once the page's width is mapped to 210 mm. */
export function bandHeightMm(fraction: number, page: { width: number; height: number }): number {
  return (fraction * page.height * A4_WIDTH_MM) / page.width
}

// The server's caps (`LetterheadRules`): an entête takes at most a third of A4, a pied de page a sixth.
const MAX_HEADER_MM = A4_HEIGHT_MM / 3
const MAX_FOOTER_MM = A4_HEIGHT_MM / 6

const HEADER_SEARCH_SHARE = 0.4
const FOOTER_SEARCH_SHARE = 0.3
// How far past the last printing the line is placed, as a share of the page (~3.5 mm on A4).
const MARGIN_SHARE = 0.012
// Ink is darker than the paper by INK_CONTRAST, and never lighter than INK_LUMINANCE on white paper.
const INK_LUMINANCE = 232
const INK_CONTRAST = 22

/**
 * Proposes the two lines from where the ink is: below the LONGEST blank stretch under the top printing, and above
 * the longest one over the bottom printing. A suggestion the user moves, never a decision — a page with no printing
 * near its bottom gets no footer line at all.
 *
 * ⚠️ The longest stretch, not the first: a letterhead's text block is usually followed by a thin rule a few
 * millimetres lower, and cutting at the first gap left that rule off every document.
 */
export function suggestCuts(page: HTMLCanvasElement): CutSuggestion {
  const { rows, framed } = inkRows(page)
  const height = rows.length
  const margin = Math.max(1, Math.round(height * MARGIN_SHARE))

  const headerLimit = Math.round(height * HEADER_SEARCH_SHARE)
  const firstInk = rows.findIndex((ink) => ink)
  let headerEnd = 0.12
  if (firstInk >= 0 && firstInk < headerLimit) {
    const blank = longestBlankRun(rows, firstInk, headerLimit)
    if (blank) headerEnd = (blank.start + Math.min(margin, Math.floor(blank.length / 2))) / height
  }

  const footerLimit = Math.round(height * (1 - FOOTER_SEARCH_SHARE))
  let lastInk = -1
  for (let row = height - 1; row >= footerLimit; row -= 1) {
    if (rows[row]) {
      lastInk = row
      break
    }
  }

  let footerStart: number | null = null
  if (lastInk >= 0) {
    const blank = longestBlankRun(rows, footerLimit, lastInk)
    const end = blank ? blank.start + blank.length : footerLimit
    footerStart = (end - (blank ? Math.min(margin, Math.floor(blank.length / 2)) : 0)) / height
  }

  // Never suggest a band the server refuses: the user still sees the line, and moves it from there.
  headerEnd = Math.min(headerEnd, bandFraction(MAX_HEADER_MM, page))
  if (footerStart !== null) footerStart = Math.max(footerStart, 1 - bandFraction(MAX_FOOTER_MM, page))

  return {
    cuts: { headerEnd, footerStart: footerStart !== null && footerStart > headerEnd ? footerStart : null },
    framed,
  }
}

export interface CutSuggestion {
  cuts: BandCuts
  /** The paper has a frame down its sides — cut into bands alone, it would print broken: offer « Page entière ». */
  framed: boolean
}

// The share of the page height a band of `mm` takes once the page's width is mapped to 210 mm.
function bandFraction(mm: number, page: { width: number; height: number }): number {
  return (mm * page.width) / (A4_WIDTH_MM * page.height)
}

// The longest run of blank rows in [from, to), or null when every row carries ink.
function longestBlankRun(rows: boolean[], from: number, to: number): { start: number; length: number } | null {
  let best: { start: number; length: number } | null = null
  let start = -1
  for (let row = from; row <= to; row += 1) {
    const blank = row < to && !rows[row]
    if (blank && start < 0) start = row
    if (!blank && start >= 0) {
      if (!best || row - start > best.length) best = { start, length: row - start }
      start = -1
    }
  }
  return best
}

// ~0.26 mm a row on A4: a letterhead's thin coloured rule still fills a whole row. At 240 px it averaged into the
// paper and the suggested line cut it off.
const PROBE_WIDTH = 800

// One boolean per row of a reduced copy of the page: does it carry any printing? And is there a frame?
function inkRows(page: HTMLCanvasElement): { rows: boolean[]; framed: boolean } {
  const width = PROBE_WIDTH
  const height = Math.max(1, Math.round((page.height * width) / page.width))
  const probe = document.createElement("canvas")
  probe.width = width
  probe.height = height
  const context = probe.getContext("2d", { willReadFrequently: true })!
  context.imageSmoothingQuality = "high"
  context.drawImage(page, 0, 0, width, height)
  const { data } = context.getImageData(0, 0, width, height)

  const luminance = new Uint8Array(width * height)
  const histogram = new Uint32Array(256)
  for (let p = 0; p < luminance.length; p += 1) {
    const i = p * 4
    const value = Math.round(0.299 * data[i] + 0.587 * data[i + 1] + 0.114 * data[i + 2])
    luminance[p] = value
    histogram[value] += 1
  }
  const threshold = Math.min(INK_LUMINANCE, paperLuminance(histogram, luminance.length) - INK_CONTRAST)
  const minInk = Math.max(2, Math.round(width * 0.004))

  // A column inked down most of the page is a frame or a side rule: every row crosses it, so counting it left no
  // blank row anywhere and the footer line fell to the bottom third of the page.
  const columnInk = new Uint32Array(width)
  for (let p = 0; p < luminance.length; p += 1) {
    if (luminance[p] < threshold) columnInk[p % width] += 1
  }
  const frame = Array.from(columnInk, (count) => count > height * 0.5)

  const rows: boolean[] = new Array(height)
  for (let y = 0; y < height; y += 1) {
    let ink = 0
    for (let x = 0; x < width; x += 1) {
      if (!frame[x] && luminance[y * width + x] < threshold) ink += 1
    }
    rows[y] = ink >= minInk
  }
  return { rows, framed: frame.some(Boolean) }
}

// The paper's own shade (a scan is never 255): the 90th percentile, since nearly all of a letterhead page is paper.
function paperLuminance(histogram: Uint32Array, total: number): number {
  let seen = 0
  for (let value = 0; value < 256; value += 1) {
    seen += histogram[value]
    if (seen >= total * 0.9) return value
  }
  return 255
}

/**
 * The bands as PNG, cleaned and small enough for the server's door — plus, for « Page entière », the strip between
 * them, which the documents stretch behind their text. Throws a French `Error` if they cannot be made small enough.
 */
export async function cutBands(
  page: HTMLCanvasElement,
  cuts: BandCuts,
  maxBytes: number,
  fullPage: boolean,
): Promise<LetterheadBands> {
  const paper = paperColor(page)
  const header = await encodeBand(page, 0, cuts.headerEnd, maxBytes, paper)
  const footer = cuts.footerStart === null ? null : await encodeBand(page, cuts.footerStart, 1, maxBytes, paper)
  const body = fullPage ? await encodeBand(page, cuts.headerEnd, cuts.footerStart ?? 1, maxBytes, paper) : null
  return { header, footer, body }
}

type Rgb = [number, number, number]

// The paper's own colour — the median of a reduced copy, since nearly all of a letterhead page is paper. Null when
// the page is dark (a full-bleed design): there is no paper to take out, and whitening it would erase the design.
function paperColor(page: HTMLCanvasElement): Rgb | null {
  const width = 200
  const height = Math.max(1, Math.round((page.height * width) / page.width))
  const probe = document.createElement("canvas")
  probe.width = width
  probe.height = height
  const context = probe.getContext("2d", { willReadFrequently: true })!
  context.imageSmoothingQuality = "high"
  context.drawImage(page, 0, 0, width, height)
  const { data } = context.getImageData(0, 0, width, height)

  const channels: number[][] = [[], [], []]
  for (let i = 0; i < data.length; i += 4) {
    channels[0].push(data[i])
    channels[1].push(data[i + 1])
    channels[2].push(data[i + 2])
  }
  const median = (values: number[]) => values.sort((a, b) => a - b)[values.length >> 1]
  const color: Rgb = [median(channels[0]), median(channels[1]), median(channels[2])]
  return 0.299 * color[0] + 0.587 * color[1] + 0.114 * color[2] >= 200 ? color : null
}

// Every step down keeps the band at least this wide; below it the server refuses it as blurred anyway.
const MIN_ENCODE_WIDTH = 1240

async function encodeBand(
  page: HTMLCanvasElement,
  from: number,
  to: number,
  maxBytes: number,
  paper: Rgb | null,
): Promise<Blob> {
  const top = Math.round(from * page.height)
  const height = Math.max(1, Math.round(to * page.height) - top)

  for (let width = page.width; ; width = Math.round(width * 0.85)) {
    const scaled = Math.max(1, Math.round((height * width) / page.width))
    const band = document.createElement("canvas")
    band.width = width
    band.height = scaled
    const context = band.getContext("2d", { willReadFrequently: true })!
    context.imageSmoothingQuality = "high"
    context.drawImage(page, 0, top, page.width, height, 0, 0, width, scaled)
    whiten(context, width, scaled, paper)

    const blob = await toPng(band)
    if (blob.size <= maxBytes) return blob
    if (Math.round(width * 0.85) < MIN_ENCODE_WIDTH) {
      throw new Error("Cet en-tête est trop chargé pour être enregistré. Essayez un PDF ou un scan en noir et blanc.")
    }
  }
}

// How far from the paper's colour a pixel may be and still count as paper (sum over the three channels).
const PAPER_TOLERANCE = 40

// The band prints on a white page, so the paper must become white: a scan's grey and a tinted stationery's cream
// would otherwise print as coloured bars at the top and bottom of every document. Ink and colours are left alone.
function whiten(context: CanvasRenderingContext2D, width: number, height: number, paper: Rgb | null) {
  const image = context.getImageData(0, 0, width, height)
  const { data } = image
  for (let i = 0; i < data.length; i += 4) {
    const r = data[i]
    const g = data[i + 1]
    const b = data[i + 2]
    const nearWhite = Math.min(r, g, b) >= 225 && Math.max(r, g, b) - Math.min(r, g, b) <= 28
    const isPaper =
      paper !== null && Math.abs(r - paper[0]) + Math.abs(g - paper[1]) + Math.abs(b - paper[2]) <= PAPER_TOLERANCE
    if (nearWhite || isPaper) {
      data[i] = 255
      data[i + 1] = 255
      data[i + 2] = 255
    }
    data[i + 3] = 255
  }
  context.putImageData(image, 0, 0)
}

function toPng(canvas: HTMLCanvasElement): Promise<Blob> {
  return new Promise((resolve, reject) =>
    canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error("L'image n'a pas pu être préparée."))), "image/png"),
  )
}
