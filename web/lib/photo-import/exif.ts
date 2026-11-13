/**
 * The one EXIF reader (`features/camera-photo-import`) — seven tags and the embedded thumbnail of a JPEG, read from
 * the first {@link EXIF_READ_BYTES} of the file. No dependency: the format is a TIFF directory inside one JPEG segment,
 * and the seven tags the import needs fit in a page.
 *
 * ⚠️ **It reads a slice, never the photo.** A card holds hundreds of 8–12 Mo files behind a USB 2 reader; reading
 * each one whole to learn its date would take minutes. EXIF lives in the APP1 segment, which the JPEG format caps at
 * 64 Ko and which cameras write first, so {@link EXIF_READ_BYTES} covers it with room for a JFIF segment in front.
 *
 * ⚠️ **Nothing here throws on a bad file.** A truncated card, a phone screenshot or a file renamed `.jpg` all return
 * an {@link ExifFacts} with what could be read — usually nothing — and the import treats a missing date as
 * « heure incertaine ». `check:responsive`'s `exif-has-one-owner` keeps a second parser from appearing elsewhere.
 */

export const EXIF_READ_BYTES = 192 * 1024

/** What one photo says about itself. Every field is optional because every camera omits something. */
export interface ExifFacts {
  make: string | null
  model: string | null
  /** 0xA431. Older Canons keep the serial in the maker notes instead, so this is often absent. */
  bodySerialNumber: string | null
  /** 0x9003 as written, « 2026:11:12 09:14:03 » — kept raw because the capture key hashes it as written. */
  dateTimeOriginal: string | null
  /** 0x9291, the sub-second digits. */
  subSecTimeOriginal: string | null
  /** 0x9011, « +01:00 », when the camera knows its own zone. */
  offsetTimeOriginal: string | null
  /** 0x0112, 1–8. 1 (or absent) is upright. */
  orientation: number
  /** The IFD1 thumbnail's byte range inside the file — sliced, never copied. */
  thumbnail: { start: number; length: number } | null
}

const EMPTY: ExifFacts = {
  make: null,
  model: null,
  bodySerialNumber: null,
  dateTimeOriginal: null,
  subSecTimeOriginal: null,
  offsetTimeOriginal: null,
  orientation: 1,
  thumbnail: null,
}

const TAG = {
  make: 0x010f,
  model: 0x0110,
  orientation: 0x0112,
  exifPointer: 0x8769,
  dateTimeOriginal: 0x9003,
  offsetTimeOriginal: 0x9011,
  subSecTimeOriginal: 0x9291,
  bodySerialNumber: 0xa431,
  thumbnailOffset: 0x0201,
  thumbnailLength: 0x0202,
} as const

/** Bytes per component, by TIFF field type. */
const TYPE_SIZE: Record<number, number> = { 1: 1, 2: 1, 3: 2, 4: 4, 5: 8, 6: 1, 7: 1, 8: 2, 9: 4, 10: 8, 11: 4, 12: 8 }

/** Reads the EXIF of a JPEG file. Resolves with empty facts — never rejects — when there is none to read. */
export async function readExif(file: Blob): Promise<ExifFacts> {
  try {
    const head = new Uint8Array(await file.slice(0, EXIF_READ_BYTES).arrayBuffer())
    return parseExif(head)
  } catch {
    return { ...EMPTY }
  }
}

/** The parser itself, over the first bytes of a JPEG. Exported for the matcher's test fixtures. */
export function parseExif(bytes: Uint8Array): ExifFacts {
  const tiffStart = findExifTiffStart(bytes)
  if (tiffStart < 0) return { ...EMPTY }

  // A card read short can end inside the header itself; nothing below may read past the slice.
  if (tiffStart + 8 > bytes.byteLength) return { ...EMPTY }
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength)
  const order = view.getUint16(tiffStart)
  const little = order === 0x4949
  if (!little && order !== 0x4d4d) return { ...EMPTY }

  const u16 = (offset: number) => view.getUint16(offset, little)
  const u32 = (offset: number) => view.getUint32(offset, little)
  const inBounds = (offset: number, length: number) => offset >= 0 && length >= 0 && offset + length <= bytes.byteLength

  if (!inBounds(tiffStart, 8) || u16(tiffStart + 2) !== 0x002a) return { ...EMPTY }

  const facts: ExifFacts = { ...EMPTY }

  /** Reads one directory, calling `visit` per entry; returns the next directory's offset (0 when none). */
  const walk = (ifdOffset: number, visit: (tag: number, type: number, count: number, valueAt: number) => void) => {
    const at = tiffStart + ifdOffset
    if (ifdOffset <= 0 || !inBounds(at, 2)) return 0
    const entries = u16(at)
    if (entries > 512 || !inBounds(at + 2, entries * 12 + 4)) return 0

    for (let index = 0; index < entries; index++) {
      const entry = at + 2 + index * 12
      const tag = u16(entry)
      const type = u16(entry + 2)
      const count = u32(entry + 4)
      const size = (TYPE_SIZE[type] ?? 0) * count
      // A value of four bytes or fewer sits in the entry itself; a longer one is pointed at from the TIFF header.
      const valueAt = size <= 4 ? entry + 8 : tiffStart + u32(entry + 8)
      if (size > 0 && inBounds(valueAt, size)) visit(tag, type, count, valueAt)
    }
    return u32(at + 2 + entries * 12)
  }

  const ascii = (valueAt: number, count: number) => {
    let text = ""
    for (let index = 0; index < count; index++) {
      const code = bytes[valueAt + index]
      if (code === 0) break
      text += String.fromCharCode(code)
    }
    const trimmed = text.trim()
    return trimmed.length > 0 ? trimmed : null
  }

  const number = (type: number, valueAt: number) => (type === 3 ? u16(valueAt) : type === 4 ? u32(valueAt) : null)

  let exifIfd = 0
  const nextIfd = walk(u32(tiffStart + 4), (tag, type, count, valueAt) => {
    if (tag === TAG.make && type === 2) facts.make = ascii(valueAt, count)
    else if (tag === TAG.model && type === 2) facts.model = ascii(valueAt, count)
    else if (tag === TAG.orientation) {
      const value = number(type, valueAt)
      if (value && value >= 1 && value <= 8) facts.orientation = value
    } else if (tag === TAG.exifPointer) exifIfd = number(type, valueAt) ?? 0
  })

  if (exifIfd > 0) {
    walk(exifIfd, (tag, type, count, valueAt) => {
      if (type !== 2 && type !== 7) return
      if (tag === TAG.dateTimeOriginal) facts.dateTimeOriginal = ascii(valueAt, count)
      else if (tag === TAG.subSecTimeOriginal) facts.subSecTimeOriginal = ascii(valueAt, count)
      else if (tag === TAG.offsetTimeOriginal) facts.offsetTimeOriginal = ascii(valueAt, count)
      else if (tag === TAG.bodySerialNumber) facts.bodySerialNumber = ascii(valueAt, count)
    })
  }

  if (nextIfd > 0) {
    let thumbOffset = 0
    let thumbLength = 0
    walk(nextIfd, (tag, type, _count, valueAt) => {
      if (tag === TAG.thumbnailOffset) thumbOffset = number(type, valueAt) ?? 0
      else if (tag === TAG.thumbnailLength) thumbLength = number(type, valueAt) ?? 0
    })
    // Offsets are relative to the TIFF header, which sits inside the file — the slice starts at 0 of the file.
    const start = tiffStart + thumbOffset
    if (thumbOffset > 0 && thumbLength > 0 && thumbLength < 512 * 1024) {
      facts.thumbnail = { start, length: thumbLength }
    }
  }

  return facts
}

/** The offset of the TIFF header inside APP1 « Exif\0\0 », or -1. Walks the segments from SOI. */
function findExifTiffStart(bytes: Uint8Array): number {
  if (bytes.length < 4 || bytes[0] !== 0xff || bytes[1] !== 0xd8) return -1

  let offset = 2
  while (offset + 4 <= bytes.length) {
    if (bytes[offset] !== 0xff) return -1
    const marker = bytes[offset + 1]
    // Fill bytes between segments are legal.
    if (marker === 0xff) {
      offset += 1
      continue
    }
    // Start of scan or end of image: the metadata segments are behind us.
    if (marker === 0xda || marker === 0xd9) return -1

    const length = (bytes[offset + 2] << 8) | bytes[offset + 3]
    if (length < 2) return -1

    if (
      marker === 0xe1 &&
      offset + 10 <= bytes.length &&
      bytes[offset + 4] === 0x45 && // E
      bytes[offset + 5] === 0x78 && // x
      bytes[offset + 6] === 0x69 && // i
      bytes[offset + 7] === 0x66 && // f
      bytes[offset + 8] === 0 &&
      bytes[offset + 9] === 0
    ) {
      return offset + 10
    }

    offset += 2 + length
  }
  return -1
}

/**
 * The CSS that turns a raw EXIF thumbnail upright. A thumbnail is stored as the sensor saw it; only the main image's
 * Orientation tag says how to hold it — so a portrait photo's thumbnail lies on its side without this.
 */
export function orientationTransform(orientation: number): string | undefined {
  switch (orientation) {
    case 2:
      return "scaleX(-1)"
    case 3:
      return "rotate(180deg)"
    case 4:
      return "scaleY(-1)"
    case 5:
      return "rotate(90deg) scaleX(-1)"
    case 6:
      return "rotate(90deg)"
    case 7:
      return "rotate(-90deg) scaleX(-1)"
    case 8:
      return "rotate(-90deg)"
    default:
      return undefined
  }
}
