/**
 * A DICOM opened as a **study** — its geometry, its scale, and its frames as values rather than as a picture.
 *
 * ⚠️ **This exists so there is exactly ONE pixel pipeline, and that is not tidiness.** `decoders/dicom.ts`
 * used to read the pixels, rescale them, choose a window, invert `MONOCHROME1` and encode a JPEG in one
 * function — which was right while a DICOM was one flattened stand-in. An interactive viewer needs the same
 * bytes with the window *not* yet applied, so the second consumer would have been a second copy of the signed
 * read, the modality rescale and the photometric inversion. This repo's dominant defect is a correct rule
 * wired to one call site; a `MONOCHROME1` inversion that exists twice and drifts once renders a radiograph as
 * its own negative, which reads as a **finding** and not as a bug. So the values come from here and the window
 * is applied in `./window.ts`, and `decodeDicom` is now a consumer of both.
 *
 * <p>What this module deliberately does NOT do: choose a window, map anything to grey, or draw. It hands back
 * the stored readings and what the file says about them.</p>
 *
 * ⚠️ **A frame is a VIEW into the file's own buffer wherever the encoding allows it** — no copy, no decode. That
 * is what makes frame scrolling free on the uncompressed path: `etude-16-images.dcm` is one 1 Mo buffer and
 * sixteen `Uint8Array` windows onto it, so stepping a frame costs a LUT paint (measured 2,5 ms) and nothing
 * else. Only the encapsulated path has anything to decode, and there the browser does it one frame at a time.
 */
import { toArrayBuffer } from '../bytes'

/**
 * A CBCT study is routinely over the catalogue's 150 Mo cap once it is a whole volume, and the coffre takes up
 * to 64 Go. The whole file has to be in memory to parse it, so past this no viewer is offered.
 */
const MAX_BYTES = 150 * 1024 * 1024

/**
 * The largest frame this will render interactively.
 *
 * ⚠️ Not the same question as `raster.ts`'s `MAX_PIXELS`, which bounds a **canvas area** for encoding. Here the
 * ceiling is the RGBA working buffer the window paints into — four bytes per pixel, held for as long as the
 * viewer is open — so 24 Mpx is 96 Mo. It is about ten times the largest real dental frame (a 3000×1500
 * panoramique is 4,5 Mpx; a full-field intraoral CMOS about 3 Mpx), and a file past it is refused **by name**
 * rather than opening onto a blank stage.
 */
const MAX_FRAME_PIXELS = 24 * 1000 * 1000

// ── The tags this reads, named rather than scattered as hex through the code ──────────────────────────────
const TAG = {
  transferSyntax: 'x00020010',
  modality: 'x00080060',
  sliceThickness: 'x00180050',
  imagerPixelSpacing: 'x00181164',
  pixelSpacing: 'x00280030',
  samplesPerPixel: 'x00280002',
  photometricInterpretation: 'x00280004',
  planarConfiguration: 'x00280006',
  numberOfFrames: 'x00280008',
  rows: 'x00280010',
  columns: 'x00280011',
  bitsAllocated: 'x00280100',
  bitsStored: 'x00280101',
  pixelRepresentation: 'x00280103',
  windowCenter: 'x00281050',
  windowWidth: 'x00281051',
  rescaleIntercept: 'x00281052',
  rescaleSlope: 'x00281053',
  rescaleType: 'x00281054',
  windowExplanation: 'x00281055',
  pixelData: 'x7fe00010',
} as const

/**
 * Transfer syntaxes whose fragments are an ordinary JPEG file. ⚠️ Baseline and Extended only — every other
 * `1.2.840.10008.1.2.4.*` is a codec no browser carries.
 *
 * ⚠️ Extended (`.51`) being here does **not** mean it works: it is 12-bit in practice, and no browser decodes
 * a 12-bit JPEG. `radio-jpeg-12-bits.dcm` is that case, and it is refused when frame 0 fails to decode — which
 * is why {@link openDicomStudy} decodes frame 0 before reporting success.
 */
const BROWSER_DECODABLE_JPEG = new Set(['1.2.840.10008.1.2.4.50', '1.2.840.10008.1.2.4.51'])

/** Uncompressed, little-endian. ⚠️ `…1.2.2` (big-endian, retired) is deliberately absent — see below. */
const RAW_LITTLE_ENDIAN = new Set([
  '1.2.840.10008.1.2', // Implicit VR Little Endian
  '1.2.840.10008.1.2.1', // Explicit VR Little Endian
  '1.2.840.10008.1.2.1.99', // Deflated Explicit VR Little Endian
])

/**
 * What an unsupported transfer syntax actually is, in French, so a refusal names the format instead of saying
 * « ce format ».
 *
 * ⚠️ **This is the explicit decision about JPEG 2000** (`.90` / `.91`), which is the most likely of the
 * unsupported ones to arrive: it stays unsupported, and it now says so by name. A codec for it is about a
 * megabyte of WebAssembly *and* a `blob:` Worker — and `worker-src` lives in four byte-identical copies held
 * together by `ContentSecurityPolicyAgreementTests`, so it would fail only in production if one were missed.
 * That is a real cost for a format a dental practice rarely exports; being told which format it is, and that
 * the original downloads, is worth more than a silent placeholder.
 */
const SYNTAX_NAMES: Readonly<Record<string, string>> = {
  '1.2.840.10008.1.2.2': 'DICOM gros-boutien (syntaxe retirée de la norme)',
  '1.2.840.10008.1.2.4.57': 'JPEG sans perte',
  '1.2.840.10008.1.2.4.70': 'JPEG sans perte',
  '1.2.840.10008.1.2.4.80': 'JPEG-LS sans perte',
  '1.2.840.10008.1.2.4.81': 'JPEG-LS',
  '1.2.840.10008.1.2.4.90': 'JPEG 2000 sans perte',
  '1.2.840.10008.1.2.4.91': 'JPEG 2000',
  '1.2.840.10008.1.2.5': 'RLE',
  '1.2.840.10008.1.2.4.100': 'MPEG-2',
  '1.2.840.10008.1.2.4.101': 'MPEG-2 haute définition',
  '1.2.840.10008.1.2.4.102': 'MPEG-4',
}

/** A window the FILE declares, with the device's own name for it when it gave one. */
export interface DicomWindow {
  centre: number
  width: number
}

/** Millimetres per pixel, and — the part that matters — **where that figure came from**. */
export interface DicomSpacing {
  /** Between adjacent rows, i.e. the vertical step. DICOM's `PixelSpacing` gives this value first. */
  row: number
  /** Between adjacent columns, i.e. the horizontal step. */
  column: number
  /**
   * ⚠️ **`imager` is measured at the detector, not in the patient**, and the difference is not a rounding
   * error: a panoramique magnifies by roughly 1,05–1,25 depending on the machine and where in the arch the
   * structure sits, and nothing in the file says by how much. So a length from `ImagerPixelSpacing` is a real
   * measurement of the *image* and is not a distance in the mouth — which is why the readout carries the
   * qualifier rather than the notes carrying an excuse.
   */
  source: 'patient' | 'imager'
}

/** A frame's pixels, in whatever form the encoding produced. */
export type DicomFrame =
  | {
      kind: 'grey'
      /** Stored readings, before the modality rescale. A view into the file's buffer on the raw path. */
      stored: Int8Array | Uint8Array | Int16Array | Uint16Array
      /** 8 or 16 — the width of the LUT `./window.ts` builds over these values. */
      bits: 8 | 16
      signed: boolean
    }
  | {
      kind: 'colour'
      /** Already brightnesses; a colour DICOM has no window to choose. */
      rgba: Uint8ClampedArray
    }

export interface DicomStudy {
  rows: number
  columns: number
  frameCount: number
  /** Verbatim from the file — `MONOCHROME1`, `MONOCHROME2`, `RGB`, `YBR_FULL_422`… */
  photometric: string
  /**
   * `MONOCHROME1`, i.e. the stored scale runs bright-to-dark and the picture has to be inverted to be read.
   *
   * ⚠️ This is the format's famous silent failure. The flag is decided here and applied in `./window.ts`'s
   * (unexported) lookup-table builder, once. `check:responsive`'s `monochrome1-has-one-owner` is what keeps
   * the decision in one place; module privacy keeps the application there.
   */
  inverted: boolean
  slope: number
  intercept: number
  /** `HU` when the file says its rescaled values are Hounsfield units, else null. See `./window.ts`. */
  rescaleType: string | null
  modality: string | null
  /**
   * The windows the file itself declares, in file order, with the device's own explanation where it gave one.
   * Empty when the file declares none — and empty **on purpose** when the pixels arrived already rendered.
   */
  declaredWindows: readonly { window: DicomWindow; label: string | null }[]
  spacing: DicomSpacing | null
  /** Millimetres, when the file says — shown so a reader knows the slice has a thickness the ruler ignores. */
  sliceThickness: number | null
  transferSyntax: string
  /**
   * True when the greyscale values came out of a lossy 8-bit JPEG rather than off the sensor.
   *
   * ⚠️ Two consequences, both load-bearing. The exporter already chose a window when it wrote those 8 bits, so
   * re-applying the file's declared window would apply it **twice** — hence `declaredWindows` is empty here.
   * And the numbers the user then drags are display levels, not sensor readings, so the readout must not print
   * them as though they were.
   */
  valuesAreRendered: boolean
  /** Null for a frame the browser turned out not to be able to decode. */
  frame(index: number): Promise<DicomFrame | null>
  /** Drops the file buffer and any cached frame, so a 150 Mo study does not outlive its dialog. */
  release(): void
}

/** Why no study came back. Every one of these is an ordinary answer, not an exception. */
export type DicomFailure =
  | { reason: 'too-large' }
  | { reason: 'not-dicom' }
  | { reason: 'no-pixel-data' }
  | { reason: 'truncated' }
  | { reason: 'frame-too-large'; pixels: number }
  /** A codec this build does not carry. `syntaxName` is French and names the format. */
  | { reason: 'unsupported-syntax'; transferSyntax: string; syntaxName: string | null }
  /** Listed as browser-decodable, and the browser refused it anyway — a 12-bit JPEG is this. */
  | { reason: 'undecodable-frame'; transferSyntax: string; syntaxName: string | null }

export type DicomOpenResult = { ok: true; study: DicomStudy } | { ok: false; failure: DicomFailure }

/** The parts of `dicom-parser` this uses, described structurally: the package ships no type declarations. */
interface DicomElement {
  dataOffset: number
  length: number
  /**
   * Where each frame starts inside the encapsulated pixel data. ⚠️ **Routinely empty, and that is legal** —
   * most encoders write the table's item with a length of zero rather than filling it in, which is why the
   * fragment has to be read the other way.
   */
  basicOffsetTable?: number[]
}

interface DicomDataSet {
  elements: Record<string, DicomElement | undefined>
  byteArray: Uint8Array
  uint16(tag: string): number | undefined
  intString(tag: string): number | undefined
  floatString(tag: string, index?: number): number | undefined
  string(tag: string): string | undefined
  numStringValues?(tag: string): number | undefined
}

interface DicomParser {
  parseDicom(byteArray: Uint8Array, options?: Record<string, unknown>): DicomDataSet
  readEncapsulatedImageFrame(dataSet: DicomDataSet, element: DicomElement, frame: number): Uint8Array
  readEncapsulatedPixelDataFromFragments(dataSet: DicomDataSet, element: DicomElement, frame: number): Uint8Array
}

async function loadParser(): Promise<DicomParser> {
  const imported = (await import('dicom-parser')) as unknown as DicomParser & { default?: DicomParser }

  // A UMD bundle reached through `import()`: some graphs hand back the namespace, others park it under default.
  return typeof imported.parseDicom === 'function' ? imported : imported.default!
}

/**
 * Opens a DICOM for reading. Nothing here throws at the caller — every refusal is a {@link DicomFailure} that
 * can be said out loud in French.
 *
 * ⚠️ **Frame 0 is decoded before success is reported.** On the encapsulated path the only way to know the
 * browser can handle the codec is to hand it a fragment and see: `radio-jpeg-12-bits.dcm` declares JPEG
 * Extended, which is in the decodable set, and `createImageBitmap` refuses it. Reporting success and then
 * failing to paint would open an empty stage with no sentence on it.
 */
export async function openDicomStudy(source: Blob): Promise<DicomOpenResult> {
  if (source.size <= 0) return { ok: false, failure: { reason: 'not-dicom' } }
  if (source.size > MAX_BYTES) return { ok: false, failure: { reason: 'too-large' } }

  const parser = await loadParser()

  let dataSet: DicomDataSet
  try {
    dataSet = parser.parseDicom(new Uint8Array(await source.arrayBuffer()))
  } catch {
    return { ok: false, failure: { reason: 'not-dicom' } }
  }

  const pixelData = dataSet.elements[TAG.pixelData]
  if (!pixelData) return { ok: false, failure: { reason: 'no-pixel-data' } }

  const transferSyntax = dataSet.string(TAG.transferSyntax) ?? '1.2.840.10008.1.2'
  const encapsulated = BROWSER_DECODABLE_JPEG.has(transferSyntax)

  // ⚠️ Big-endian (`1.2.840.10008.1.2.2`) is not in the raw set on purpose. It is a retired syntax no dental
  // device has produced this decade, and supporting it means a per-pixel byte swap on a path that otherwise
  // reads a native typed array — a real slowdown on every ordinary file to serve one that will not arrive.
  if (!encapsulated && !RAW_LITTLE_ENDIAN.has(transferSyntax)) {
    return {
      ok: false,
      failure: {
        reason: 'unsupported-syntax',
        transferSyntax,
        syntaxName: SYNTAX_NAMES[transferSyntax] ?? null,
      },
    }
  }

  const rows = dataSet.uint16(TAG.rows) ?? 0
  const columns = dataSet.uint16(TAG.columns) ?? 0
  if (rows <= 0 || columns <= 0) return { ok: false, failure: { reason: 'not-dicom' } }
  if (rows * columns > MAX_FRAME_PIXELS) {
    return { ok: false, failure: { reason: 'frame-too-large', pixels: rows * columns } }
  }

  const samplesPerPixel = dataSet.uint16(TAG.samplesPerPixel) ?? 1
  const bitsAllocated = dataSet.uint16(TAG.bitsAllocated) ?? 16
  const bytesPerSample = Math.ceil(bitsAllocated / 8)
  if (!encapsulated && bytesPerSample !== 1 && bytesPerSample !== 2) {
    return { ok: false, failure: { reason: 'not-dicom' } }
  }

  const frameCount = Math.max(1, dataSet.intString(TAG.numberOfFrames) ?? 1)
  const frameBytes = rows * columns * samplesPerPixel * bytesPerSample

  // A truncated file, or a header that disagrees with the bytes that followed it. Either way there is no first
  // frame to draw, and drawing a partial one would be inventing the missing half.
  if (!encapsulated && pixelData.dataOffset + frameBytes > dataSet.byteArray.length) {
    return { ok: false, failure: { reason: 'truncated' } }
  }

  const reader = encapsulated
    ? encapsulatedReader(parser, dataSet, pixelData, rows, columns)
    : rawReader(dataSet, pixelData, rows, columns, bitsAllocated, samplesPerPixel, frameCount)

  const study: DicomStudy = {
    rows,
    columns,
    // ⚠️ The declared count is trusted only as far as the bytes go. A raw file whose header says sixteen and
    // whose pixel data holds four would otherwise offer a scrubber onto twelve frames of somebody else's
    // memory — and the ones past the end read as whatever follows in the buffer, not as an error.
    frameCount: encapsulated
      ? frameCount
      : Math.max(1, Math.min(frameCount, Math.floor((dataSet.byteArray.length - pixelData.dataOffset) / frameBytes))),
    photometric: (dataSet.string(TAG.photometricInterpretation) ?? 'MONOCHROME2').trim(),
    inverted: (dataSet.string(TAG.photometricInterpretation) ?? 'MONOCHROME2').trim() === 'MONOCHROME1',
    slope: finiteOr(dataSet.floatString(TAG.rescaleSlope), 1),
    intercept: finiteOr(dataSet.floatString(TAG.rescaleIntercept), 0),
    rescaleType: dataSet.string(TAG.rescaleType)?.trim() || null,
    modality: dataSet.string(TAG.modality)?.trim() || null,
    declaredWindows: encapsulated ? [] : readDeclaredWindows(dataSet),
    spacing: readSpacing(dataSet),
    sliceThickness: finiteOrNull(dataSet.floatString(TAG.sliceThickness)),
    transferSyntax,
    valuesAreRendered: encapsulated,
    frame: reader.frame,
    release: reader.release,
  }

  const first = await study.frame(0)
  if (!first) {
    study.release()
    return {
      ok: false,
      failure: encapsulated
        ? {
            reason: 'undecodable-frame',
            transferSyntax,
            syntaxName: SYNTAX_NAMES[transferSyntax] ?? null,
          }
        : { reason: 'truncated' },
    }
  }

  return { ok: true, study }
}

/**
 * The uncompressed path: a frame is a **view**, so there is nothing to decode and nothing to cache.
 *
 * ⚠️ A typed array over a shared buffer must be aligned to its element size, and a `Uint16Array` at an odd
 * byte offset throws. DICOM data elements are even-length and even-aligned so this should not arise — measured
 * across every sample in `follow-up/decoder-samples/`, every `dataOffset` is even — but a file that manages it
 * gets a copy rather than an exception.
 */
function rawReader(
  dataSet: DicomDataSet,
  pixelData: DicomElement,
  rows: number,
  columns: number,
  bitsAllocated: number,
  samplesPerPixel: number,
  frameCount: number,
): { frame: DicomStudy['frame']; release: () => void } {
  let bytes: Uint8Array | null = dataSet.byteArray
  const bytesPerSample = Math.ceil(bitsAllocated / 8)
  const pixels = rows * columns
  const frameBytes = pixels * samplesPerPixel * bytesPerSample
  const signed = (dataSet.uint16(TAG.pixelRepresentation) ?? 0) === 1
  const planar = (dataSet.uint16(TAG.planarConfiguration) ?? 0) === 1

  return {
    async frame(index: number) {
      if (!bytes || index < 0 || index >= frameCount) return null
      const at = pixelData.dataOffset + index * frameBytes
      if (at + frameBytes > bytes.length) return null

      if (samplesPerPixel >= 3) {
        return { kind: 'colour', rgba: colourToRgba(bytes, at, pixels, bytesPerSample, planar) }
      }

      const start = bytes.byteOffset + at
      const aligned = bytesPerSample === 1 || start % 2 === 0
      const buffer = aligned
        ? (bytes.buffer as ArrayBuffer)
        : toArrayBuffer(bytes.subarray(at, at + frameBytes))
      const offset = aligned ? start : 0

      if (bytesPerSample === 1) {
        return {
          kind: 'grey',
          bits: 8,
          signed,
          stored: signed ? new Int8Array(buffer, offset, pixels) : new Uint8Array(buffer, offset, pixels),
        }
      }
      return {
        kind: 'grey',
        bits: 16,
        signed,
        stored: signed ? new Int16Array(buffer, offset, pixels) : new Uint16Array(buffer, offset, pixels),
      }
    },
    release() {
      bytes = null
    },
  }
}

/**
 * The encapsulated path: the fragment IS a JPEG file, so the browser decodes it — which means a frame here
 * costs a real decode and is worth caching.
 *
 * ⚠️ **Two readers, and picking the wrong one throws on the ordinary file.** `readEncapsulatedImageFrame`
 * resolves a frame through the basic offset table, and most encoders write that table empty — legally, since it
 * is optional. Measured across the samples: the JPEG ones have an empty table and only `coupe-jpeg-2000.dcm`
 * fills it in. With no table the fragments have to be walked instead.
 *
 * ⚠️ **A greyscale JPEG frame becomes a `grey` frame, not a finished picture**, and that is what lets the
 * viewer window it, invert a `MONOCHROME1` JPEG (which the old flattening path silently did not) and measure
 * on it through exactly the same code as an uncompressed radiograph.
 */
function encapsulatedReader(
  parser: DicomParser,
  dataSet: DicomDataSet,
  pixelData: DicomElement,
  rows: number,
  columns: number,
): { frame: DicomStudy['frame']; release: () => void } {
  let live = true
  const colour = (dataSet.uint16(TAG.samplesPerPixel) ?? 1) >= 3
  /** Bounded on purpose: a multi-frame JPEG study at 1200×900 is a megabyte of decoded frame each. */
  const cache = new Map<number, DicomFrame>()
  const MAX_CACHED = 8

  return {
    async frame(index: number) {
      if (!live) return null
      const cached = cache.get(index)
      if (cached) return cached

      let encoded: Uint8Array
      try {
        const hasOffsetTable = (pixelData.basicOffsetTable?.length ?? 0) > 0
        encoded = hasOffsetTable
          ? parser.readEncapsulatedImageFrame(dataSet, pixelData, index)
          : parser.readEncapsulatedPixelDataFromFragments(dataSet, pixelData, index)
      } catch {
        return null
      }
      if (!encoded || encoded.length === 0) return null

      let pixels: ImageData
      try {
        const bitmap = await createImageBitmap(new Blob([toArrayBuffer(encoded)], { type: 'image/jpeg' }))
        try {
          pixels = bitmapToPixels(bitmap, columns, rows)
        } finally {
          bitmap.close()
        }
      } catch {
        // The browser refused the codec — a 12-bit JPEG Extended is this, and it is not an exception.
        return null
      }

      const frame: DicomFrame = colour
        ? { kind: 'colour', rgba: pixels.data }
        : { kind: 'grey', bits: 8, signed: false, stored: greyChannelOf(pixels.data) }

      if (cache.size >= MAX_CACHED) cache.delete(cache.keys().next().value as number)
      cache.set(index, frame)
      return frame
    },
    release() {
      live = false
      cache.clear()
    },
  }
}

/** A decoded JPEG's samples, at the geometry the header declared. */
function bitmapToPixels(bitmap: ImageBitmap, columns: number, rows: number): ImageData {
  const canvas = document.createElement('canvas')
  canvas.width = columns
  canvas.height = rows
  const context = canvas.getContext('2d', { willReadFrequently: true })
  if (!context) throw new Error('no 2d context')
  // Drawn at the declared geometry rather than the bitmap's own: a fragment whose dimensions disagree with the
  // header would otherwise make every later index computation off by a row.
  context.drawImage(bitmap, 0, 0, columns, rows)
  return context.getImageData(0, 0, columns, rows)
}

/** One channel out of a decoded greyscale JPEG — R, G and B are equal, so the first is the reading. */
function greyChannelOf(rgba: Uint8ClampedArray): Uint8Array {
  const grey = new Uint8Array(rgba.length / 4)
  for (let i = 0; i < grey.length; i++) grey[i] = rgba[i * 4]
  return grey
}

/** RGB and YBR sensors: the samples are already brightnesses, so only the layout has to be right. */
function colourToRgba(
  bytes: Uint8Array,
  offset: number,
  pixels: number,
  bytesPerSample: number,
  planar: boolean,
): Uint8ClampedArray {
  const rgba = new Uint8ClampedArray(pixels * 4)
  const step = bytesPerSample

  for (let i = 0; i < pixels; i++) {
    // ⚠️ `PlanarConfiguration` 1 stores each channel as its own plane (RRR…GGG…BBB…) rather than interleaved.
    const r = planar ? offset + i * step : offset + i * 3 * step
    const g = planar ? offset + (pixels + i) * step : r + step
    const b = planar ? offset + (2 * pixels + i) * step : r + 2 * step

    rgba[i * 4] = bytes[r]
    rgba[i * 4 + 1] = bytes[g]
    rgba[i * 4 + 2] = bytes[b]
    rgba[i * 4 + 3] = 255
  }
  return rgba
}

/**
 * Every window the file declares, with the device's own name for it.
 *
 * ⚠️ **`WindowCenter` and `WindowWidth` are multi-valued, and reading only the first threw away a real
 * feature.** `coupe-jpeg-2000.dcm` carries `50\40` / `600\400` with `WINDOW1\WINDOW2` beside them: the
 * exporter shipped two named windows it considered useful for that image. Those are better presets than
 * anything this app could invent, because the machine that made the picture chose them.
 */
function readDeclaredWindows(dataSet: DicomDataSet): { window: DicomWindow; label: string | null }[] {
  const count = Math.max(
    dataSet.numStringValues?.(TAG.windowCenter) ?? 0,
    dataSet.string(TAG.windowCenter) ? 1 : 0,
  )
  const explanations = (dataSet.string(TAG.windowExplanation) ?? '')
    .split('\\')
    .map((part) => part.trim())

  const windows: { window: DicomWindow; label: string | null }[] = []
  for (let i = 0; i < count; i++) {
    const centre = dataSet.floatString(TAG.windowCenter, i)
    const width = dataSet.floatString(TAG.windowWidth, i)
    if (centre === undefined || width === undefined) continue
    if (!Number.isFinite(centre) || !Number.isFinite(width) || width <= 0) continue
    windows.push({ window: { centre, width }, label: explanations[i] || null })
  }
  return windows
}

/**
 * The file's scale, and which of the two tags it came from.
 *
 * ⚠️ **`PixelSpacing` is preferred and `ImagerPixelSpacing` is a fallback that must stay labelled.** The first
 * is a distance in the patient; the second is a distance on the detector, which a projection radiograph
 * magnifies by an amount the file does not record. Measured across the samples, the file this viewer exists for
 * most — `radiographie-thorax-mono1.dcm`, a real CR of the same family as a dental cliché — carries **neither**,
 * so « no scale at all » is the ordinary case and not an edge case: it measures in pixels and says so.
 */
function readSpacing(dataSet: DicomDataSet): DicomSpacing | null {
  for (const [tag, source] of [
    [TAG.pixelSpacing, 'patient'],
    [TAG.imagerPixelSpacing, 'imager'],
  ] as const) {
    const row = dataSet.floatString(tag, 0)
    const column = dataSet.floatString(tag, 1)
    if (row === undefined || !Number.isFinite(row) || row <= 0) continue
    // A single-valued PixelSpacing is out of spec but does occur; square pixels are the honest reading of it.
    const columnSpacing = column !== undefined && Number.isFinite(column) && column > 0 ? column : row
    return { row, column: columnSpacing, source }
  }
  return null
}

function finiteOr(value: number | undefined, fallback: number): number {
  return value !== undefined && Number.isFinite(value) ? value : fallback
}

function finiteOrNull(value: number | undefined): number | null {
  return value !== undefined && Number.isFinite(value) ? value : null
}
