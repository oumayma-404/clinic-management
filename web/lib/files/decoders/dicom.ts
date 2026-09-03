/**
 * DICOM — what a CBCT and an intraoral sensor actually export.
 *
 * ⚠️ **A DICOM image is not a picture until somebody chooses a window, and choosing it wrong produces a
 * misleading clinical image rather than an ugly one.** The stored values are not brightnesses: they are 12- or
 * 16-bit sensor readings, optionally rescaled into another unit entirely, and turning them into 256 grey levels
 * means picking which slice of that range to show. The file usually says which (`WindowCenter` / `WindowWidth`);
 * when it does not, this derives one from the frame's own range — a reasonable picture, and **not** the one a
 * radiologist would read. Everything here is therefore labelled « aperçu non diagnostique », and the original
 * is always one click away.
 *
 * ⚠️ **`MONOCHROME1` is inverted, and forgetting it is the silent failure this format is famous for.** A
 * MONOCHROME1 radiograph rendered as MONOCHROME2 looks like a photographic negative of itself — bone dark,
 * air bright — which reads as a *finding* to anyone who does not know the file's photometric interpretation.
 *
 * ⚠️ **Compressed pixel data is only handled where the browser itself can decode it.** JPEG Baseline and
 * Extended are ordinary JPEGs inside the container, so the fragment is handed to `createImageBitmap` verbatim.
 * JPEG Lossless, JPEG-LS, JPEG 2000 and RLE each need their own codec, which is another megabyte of WebAssembly
 * for formats a dental practice rarely exports — so they return null and the viewer says the format cannot be
 * shown, which is true and not a failure.
 */
import { dicomAdvisoryFor } from './advisory'
import { encodeBitmap, encodeRgba, fitWithin, type DecodedImage } from './raster'

/**
 * A CBCT study is routinely over the catalogue's 150 Mo cap once it is a whole volume, and the coffre takes up
 * to 64 Go. The whole file has to be in memory to parse it, so past this no preview is attempted.
 */
const MAX_BYTES = 150 * 1024 * 1024

// ── The tags this reads, named rather than scattered as hex through the code ──────────────────────────────
const TAG = {
  transferSyntax: 'x00020010',
  samplesPerPixel: 'x00280002',
  photometricInterpretation: 'x00280004',
  planarConfiguration: 'x00280006',
  numberOfFrames: 'x00280008',
  rows: 'x00280010',
  columns: 'x00280011',
  bitsAllocated: 'x00280100',
  pixelRepresentation: 'x00280103',
  windowCenter: 'x00281050',
  windowWidth: 'x00281051',
  rescaleIntercept: 'x00281052',
  rescaleSlope: 'x00281053',
  pixelData: 'x7fe00010',
} as const

/**
 * Transfer syntaxes whose fragments are an ordinary JPEG file. ⚠️ Baseline and Extended only — every other
 * `1.2.840.10008.1.2.4.*` is a codec no browser carries.
 */
const BROWSER_DECODABLE_JPEG = new Set(['1.2.840.10008.1.2.4.50', '1.2.840.10008.1.2.4.51'])

/** Uncompressed, little-endian. ⚠️ `…1.2.2` (big-endian, retired) is deliberately absent — see {@link decodeDicom}. */
const RAW_LITTLE_ENDIAN = new Set([
  '1.2.840.10008.1.2', // Implicit VR Little Endian
  '1.2.840.10008.1.2.1', // Explicit VR Little Endian
  '1.2.840.10008.1.2.1.99', // Deflated Explicit VR Little Endian
])

/** The parts of `dicom-parser` this uses, described structurally: the package ships no type declarations. */
interface DicomElement {
  dataOffset: number
  length: number
  /**
   * Where each frame starts inside the encapsulated pixel data. ⚠️ **Routinely empty, and that is legal** —
   * most encoders write the table's item with a length of zero rather than filling it in, which is why the
   * fragment has to be read the other way. See {@link fromEmbeddedJpeg}.
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
 * The first frame of a DICOM, as a JPEG. Null covers every ordinary refusal — too large, not a DICOM, a codec
 * this build does not carry, a truncated file — so nothing here throws at the caller.
 */
export async function decodeDicom(source: Blob): Promise<DecodedImage | null> {
  if (source.size <= 0 || source.size > MAX_BYTES) return null

  const parser = await loadParser()

  let dataSet: DicomDataSet
  try {
    dataSet = parser.parseDicom(new Uint8Array(await source.arrayBuffer()))
  } catch {
    return null
  }

  const pixelData = dataSet.elements[TAG.pixelData]
  if (!pixelData) return null

  const transferSyntax = dataSet.string(TAG.transferSyntax) ?? '1.2.840.10008.1.2'
  const frames = dataSet.intString(TAG.numberOfFrames) ?? 1

  try {
    if (BROWSER_DECODABLE_JPEG.has(transferSyntax)) {
      return await fromEmbeddedJpeg(parser, dataSet, pixelData, frames)
    }

    // ⚠️ Big-endian (`1.2.840.10008.1.2.2`) is not in the raw set on purpose. It is a retired syntax no dental
    // device has produced this decade, and supporting it means a per-pixel byte swap on a path that otherwise
    // reads a native typed array — a real slowdown on every ordinary file to serve one that will not arrive.
    if (RAW_LITTLE_ENDIAN.has(transferSyntax)) {
      return await fromRawPixels(dataSet, pixelData, frames)
    }
  } catch {
    return null
  }

  // JPEG Lossless, JPEG-LS, JPEG 2000, RLE, MPEG — each its own codec, none of them the browser's.
  return null
}

/** The compressed case: the fragment IS a JPEG file, so the browser decodes it. */
async function fromEmbeddedJpeg(
  parser: DicomParser,
  dataSet: DicomDataSet,
  pixelData: DicomElement,
  frames: number,
): Promise<DecodedImage | null> {
  // ⚠️ **Two readers, and picking the wrong one throws on the ordinary file.** `readEncapsulatedImageFrame`
  // resolves a frame through the basic offset table, and most encoders write that table empty — legally, since
  // it is optional. With no table the fragments have to be walked instead. Measured: a file with an empty table
  // threw here and the viewer showed « ce format ne s'affiche pas » for a JPEG the browser could decode.
  const hasOffsetTable = (pixelData.basicOffsetTable?.length ?? 0) > 0
  const encoded = hasOffsetTable
    ? parser.readEncapsulatedImageFrame(dataSet, pixelData, 0)
    : parser.readEncapsulatedPixelDataFromFragments(dataSet, pixelData, 0)

  if (!encoded || encoded.length === 0) return null

  const bitmap = await createImageBitmap(new Blob([toArrayBuffer(encoded)], { type: 'image/jpeg' }))

  try {
    const fitted = fitWithin(bitmap.width, bitmap.height)
    if (fitted.width === 0) return null

    const blob = await encodeBitmap(bitmap, fitted.width, fitted.height)
    return blob ? { blob, width: fitted.width, height: fitted.height, pages: frames, advisory: dicomAdvisoryFor(frames) } : null
  } finally {
    bitmap.close()
  }
}

/** The uncompressed case: read the sensor values, choose a window, and map them into 256 greys. */
async function fromRawPixels(
  dataSet: DicomDataSet,
  pixelData: DicomElement,
  frames: number,
): Promise<DecodedImage | null> {
  const rows = dataSet.uint16(TAG.rows) ?? 0
  const columns = dataSet.uint16(TAG.columns) ?? 0
  if (rows <= 0 || columns <= 0) return null

  const samplesPerPixel = dataSet.uint16(TAG.samplesPerPixel) ?? 1
  const bitsAllocated = dataSet.uint16(TAG.bitsAllocated) ?? 16
  const bytesPerSample = Math.ceil(bitsAllocated / 8)
  if (bytesPerSample !== 1 && bytesPerSample !== 2) return null

  const pixelsPerFrame = rows * columns
  const frameBytes = pixelsPerFrame * samplesPerPixel * bytesPerSample

  // A truncated file, or a header that disagrees with the bytes that followed it. Either way there is no first
  // frame to draw, and drawing a partial one would be inventing the missing half.
  if (pixelData.dataOffset + frameBytes > dataSet.byteArray.length) return null

  const rgba = new Uint8ClampedArray(pixelsPerFrame * 4)

  if (samplesPerPixel === 3) {
    fillFromColour(dataSet, pixelData.dataOffset, pixelsPerFrame, bytesPerSample, rgba)
  } else {
    const filled = fillFromGreyscale(dataSet, pixelData, rows, columns, bitsAllocated, bytesPerSample, rgba)
    if (!filled) return null
  }

  const blob = await encodeRgba(rgba, columns, rows)
  if (!blob) return null

  const fitted = fitWithin(columns, rows)
  return { blob, width: fitted.width, height: fitted.height, pages: frames, advisory: dicomAdvisoryFor(frames) }
}

/** RGB and YBR sensors: the samples are already brightnesses, so only the layout has to be right. */
function fillFromColour(
  dataSet: DicomDataSet,
  offset: number,
  pixels: number,
  bytesPerSample: number,
  rgba: Uint8ClampedArray,
): void {
  const bytes = dataSet.byteArray
  // ⚠️ `PlanarConfiguration` 1 stores each channel as its own plane (RRR…GGG…BBB…) rather than interleaved.
  const planar = (dataSet.uint16(TAG.planarConfiguration) ?? 0) === 1
  const step = bytesPerSample

  for (let i = 0; i < pixels; i++) {
    const r = planar ? offset + i * step : offset + i * 3 * step
    const g = planar ? offset + (pixels + i) * step : r + step
    const b = planar ? offset + (2 * pixels + i) * step : r + 2 * step

    rgba[i * 4] = bytes[r]
    rgba[i * 4 + 1] = bytes[g]
    rgba[i * 4 + 2] = bytes[b]
    rgba[i * 4 + 3] = 255
  }
}

/**
 * The greyscale case, and the one that carries the clinical risk.
 *
 * ⚠️ Four transformations in a fixed order, and every one of them changes what the image *means*: the signed
 * reading, the modality rescale (`slope`/`intercept` — what turns a raw count into Hounsfield units), the
 * window, and the photometric inversion.
 */
function fillFromGreyscale(
  dataSet: DicomDataSet,
  pixelData: DicomElement,
  rows: number,
  columns: number,
  bitsAllocated: number,
  bytesPerSample: number,
  rgba: Uint8ClampedArray,
): boolean {
  const pixels = rows * columns
  const bytes = dataSet.byteArray
  const signed = (dataSet.uint16(TAG.pixelRepresentation) ?? 0) === 1

  // A view over the file's own buffer, little-endian, which is what every modern CPU reads natively.
  const view = new DataView(bytes.buffer, bytes.byteOffset + pixelData.dataOffset, pixels * bytesPerSample)
  const read = bytesPerSample === 1
    ? (i: number) => (signed ? view.getInt8(i) : view.getUint8(i))
    : (i: number) => (signed ? view.getInt16(i * 2, true) : view.getUint16(i * 2, true))

  const slope = dataSet.floatString(TAG.rescaleSlope) ?? 1
  const intercept = dataSet.floatString(TAG.rescaleIntercept) ?? 0

  let centre = dataSet.floatString(TAG.windowCenter, 0)
  let width = dataSet.floatString(TAG.windowWidth, 0)

  // ⚠️ **No window in the file is the common case for an intraoral sensor**, and « show everything » is the only
  // honest default: the frame's own range, so nothing is clipped away that the file bothered to record.
  if (centre === undefined || width === undefined || !Number.isFinite(centre) || !Number.isFinite(width) || width <= 0) {
    let low = Infinity
    let high = -Infinity
    for (let i = 0; i < pixels; i++) {
      const value = read(i) * slope + intercept
      if (value < low) low = value
      if (value > high) high = value
    }
    if (!Number.isFinite(low) || !Number.isFinite(high) || high <= low) return false

    centre = (high + low) / 2
    width = high - low
  }

  // The DICOM linear VOI LUT, written exactly as PS3.3 C.11.2.1.2 states it.
  const lower = centre - 0.5 - (width - 1) / 2
  const span = width - 1 || 1

  const inverted = (dataSet.string(TAG.photometricInterpretation) ?? 'MONOCHROME2').trim() === 'MONOCHROME1'

  for (let i = 0; i < pixels; i++) {
    const value = read(i) * slope + intercept
    let grey = ((value - lower) / span) * 255
    if (grey < 0) grey = 0
    else if (grey > 255) grey = 255
    if (inverted) grey = 255 - grey

    const at = i * 4
    rgba[at] = grey
    rgba[at + 1] = grey
    rgba[at + 2] = grey
    rgba[at + 3] = 255
  }

  return true
}

/** A `Uint8Array` may be a view into a larger buffer; a `Blob` must be given only its own bytes. */
function toArrayBuffer(bytes: Uint8Array): ArrayBuffer {
  return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer
}
