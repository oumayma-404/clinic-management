/**
 * What is inside an archive, read from its index alone.
 *
 * ⚠️ **Nothing is decompressed, and that is the whole design.** A laboratory's ZIP is routinely a gigabyte of
 * meshes; inflating it to answer « what is in here? » would spend minutes and the tab's memory to produce a list
 * the archive already carries. Every ZIP ends with a *central directory* — one fixed-size record per member,
 * naming it and giving both its sizes — so the answer is in the last few kilobytes and this reads only those.
 * On a 2 Go coffre archive it touches about 64 Ko.
 *
 * ⚠️ **It is hand-written rather than a library, deliberately.** Every ZIP package on npm is built to *extract*:
 * the async paths spin a `blob:` worker (which this deployment's CSP would have to be widened for) and the sync
 * paths inflate the member they are asked about. Reading an index is sixty lines of a well-specified format, it
 * decompresses nothing — so no archive can expand into memory here — and it is the only part of ZIP this
 * product ever needs.
 */

/** One member of an archive, as the index describes it. */
export interface ArchiveEntry {
  /** The path inside the archive, as it was stored. Directories keep their trailing separator. */
  name: string
  /** Stored size, uncompressed. */
  size: number
  compressedSize: number
  directory: boolean
  /** From the DOS timestamp the record carries; null when it is absent or nonsensical. */
  modifiedAt: Date | null
}

export interface ArchiveListing {
  entries: ArchiveEntry[]
  /** The index named more members than {@link MAX_ENTRIES}; the rest are counted, not listed. */
  truncated: boolean
  /** How many the index claims in total, listed or not. */
  totalEntries: number
}

/**
 * A list is a thing a person reads. Past a couple of thousand rows nobody is reading, and the DOM cost stops
 * being free — so beyond this the count is shown and the rows are not.
 */
const MAX_ENTRIES = 2000

/** The end-of-central-directory record is 22 bytes plus a comment of at most 65535. */
const EOCD_MIN = 22
const EOCD_MAX_SEARCH = EOCD_MIN + 0xffff

const SIG_EOCD = 0x06054b50
const SIG_EOCD64 = 0x06064b50
const SIG_EOCD64_LOCATOR = 0x07064b50
const SIG_CENTRAL = 0x02014b50

/** Bit 11 of the general-purpose flags: the name is UTF-8 rather than the historical CP437. */
const FLAG_UTF8 = 0x0800

/** A 32-bit field holding this means « the real value is in the Zip64 extra field ». */
const ZIP64_SENTINEL = 0xffffffff

/**
 * The archive's index, or null when this does not look like one. ⚠️ **Null is an ordinary answer** — a `.3mf`
 * and a `.docx` are ZIPs too and are not listed here, and a truncated or encrypted-index archive is simply not
 * readable rather than an error worth showing.
 */
export async function readArchiveListing(source: Blob): Promise<ArchiveListing | null> {
  const tail = await readTail(source)
  if (!tail) return null

  const eocdAt = findEocd(tail.view)
  if (eocdAt < 0) return null

  const located = locateDirectory(tail, eocdAt)
  if (!located) return null

  const { offset, size, totalEntries } = located
  if (size <= 0 || offset < 0 || offset + size > source.size) return null

  // The index itself. Bounded by the archive's own claim, and refused when that claim is absurd rather than
  // trusted into a several-hundred-megabyte read.
  if (size > 64 * 1024 * 1024) return null

  const directory = new DataView(await source.slice(offset, offset + size).arrayBuffer())
  return walkDirectory(directory, totalEntries)
}

/** The last stretch of the file, where both the locator and the end record live. */
async function readTail(source: Blob): Promise<{ view: DataView; start: number } | null> {
  if (source.size < EOCD_MIN) return null

  const length = Math.min(source.size, EOCD_MAX_SEARCH)
  const start = source.size - length
  const view = new DataView(await source.slice(start).arrayBuffer())

  return { view, start }
}

/** The end record's offset within `view`, searched backwards because the comment that precedes it is free text. */
function findEocd(view: DataView): number {
  for (let at = view.byteLength - EOCD_MIN; at >= 0; at--) {
    if (view.getUint32(at, true) !== SIG_EOCD) continue

    // A comment length that does not run to the end of the file is a signature that happened to appear inside
    // the archive's data, not the record.
    const commentLength = view.getUint16(at + 20, true)
    if (at + EOCD_MIN + commentLength === view.byteLength) return at
  }

  return -1
}

/** Where the index is and how many members it names — through the Zip64 records when the 32-bit fields overflow. */
function locateDirectory(
  tail: { view: DataView; start: number },
  eocdAt: number,
): { offset: number; size: number; totalEntries: number } | null {
  const { view, start } = tail

  let totalEntries = view.getUint16(eocdAt + 10, true)
  let size = view.getUint32(eocdAt + 12, true)
  let offset = view.getUint32(eocdAt + 16, true)

  const overflowed =
    totalEntries === 0xffff || size === ZIP64_SENTINEL || offset === ZIP64_SENTINEL
  if (!overflowed) return { offset, size, totalEntries }

  // The Zip64 locator sits immediately before the end record and points at the Zip64 end record, which carries
  // the real 64-bit values. An archive over 4 Go or with more than 65535 members has one.
  const locatorAt = eocdAt - 20
  if (locatorAt < 0 || view.getUint32(locatorAt, true) !== SIG_EOCD64_LOCATOR) return null

  const eocd64Absolute = readUint64(view, locatorAt + 8)
  const eocd64At = eocd64Absolute - start
  if (eocd64At < 0 || eocd64At + 56 > view.byteLength) return null
  if (view.getUint32(eocd64At, true) !== SIG_EOCD64) return null

  totalEntries = readUint64(view, eocd64At + 32)
  size = readUint64(view, eocd64At + 40)
  offset = readUint64(view, eocd64At + 48)

  return { offset, size, totalEntries }
}

/** Walks the fixed-size records, stopping at {@link MAX_ENTRIES} or at the first malformed one. */
function walkDirectory(directory: DataView, totalEntries: number): ArchiveListing {
  const entries: ArchiveEntry[] = []
  const decoderUtf8 = new TextDecoder('utf-8')
  // Historical archives store names in CP437. `windows-1252` is not it, but it agrees on every byte a Latin
  // file name uses and is a decoder every browser has — the alternative is a 256-entry table for accented
  // characters in a list nobody will read out loud.
  const decoderLegacy = new TextDecoder('windows-1252')

  let at = 0
  while (at + 46 <= directory.byteLength) {
    if (directory.getUint32(at, true) !== SIG_CENTRAL) break

    const flags = directory.getUint16(at + 8, true)
    const nameLength = directory.getUint16(at + 28, true)
    const extraLength = directory.getUint16(at + 30, true)
    const commentLength = directory.getUint16(at + 32, true)
    const recordLength = 46 + nameLength + extraLength + commentLength
    if (at + recordLength > directory.byteLength) break

    const nameBytes = new Uint8Array(directory.buffer, directory.byteOffset + at + 46, nameLength)
    const stored = (flags & FLAG_UTF8 ? decoderUtf8 : decoderLegacy).decode(nameBytes)

    // ⚠️ The spec says a path inside a ZIP is separated by `/`, and Windows writers ignore it — PowerShell's own
    // `Compress-Archive` writes `photos\radio.tif`. Shown verbatim that reads as part of the file's name rather
    // than as a folder, so the separator is normalised for display. Measured on a real archive, not assumed.
    const name = stored.replace(/\\/g, '/')

    let compressedSize = directory.getUint32(at + 20, true)
    let size = directory.getUint32(at + 24, true)

    if (size === ZIP64_SENTINEL || compressedSize === ZIP64_SENTINEL) {
      // The Zip64 extra field lists ONLY the fields that overflowed, in a fixed order, so which value sits
      // where depends on which sentinels are present.
      const extraAt = at + 46 + nameLength
      const real = readZip64Extra(directory, extraAt, extraLength, size === ZIP64_SENTINEL, compressedSize === ZIP64_SENTINEL)
      if (real) {
        size = real.size ?? size
        compressedSize = real.compressedSize ?? compressedSize
      }
    }

    if (entries.length < MAX_ENTRIES) {
      entries.push({
        name,
        size,
        compressedSize,
        // Both conventions in the wild: a trailing separator, and the DOS directory attribute (bit 4). The
        // separator is already normalised above, so one test covers both writers.
        directory: name.endsWith('/') || (directory.getUint32(at + 38, true) & 0x10) !== 0,
        modifiedAt: dosDate(directory.getUint16(at + 14, true), directory.getUint16(at + 12, true)),
      })
    }

    at += recordLength
  }

  // The index's own count is preferred, but a header that lies must not make the list claim fewer members than
  // it is showing.
  const claimed = Math.max(totalEntries, entries.length)

  return { entries, truncated: claimed > entries.length, totalEntries: claimed }
}

/** The 64-bit replacements for whichever 32-bit fields carried the sentinel, in the order the spec fixes. */
function readZip64Extra(
  directory: DataView,
  extraAt: number,
  extraLength: number,
  wantsSize: boolean,
  wantsCompressed: boolean,
): { size?: number; compressedSize?: number } | null {
  let at = extraAt
  const end = extraAt + extraLength

  while (at + 4 <= end) {
    const id = directory.getUint16(at, true)
    const length = directory.getUint16(at + 2, true)
    if (at + 4 + length > end) return null

    if (id === 0x0001) {
      let field = at + 4
      const result: { size?: number; compressedSize?: number } = {}

      if (wantsSize && field + 8 <= at + 4 + length) {
        result.size = readUint64(directory, field)
        field += 8
      }
      if (wantsCompressed && field + 8 <= at + 4 + length) {
        result.compressedSize = readUint64(directory, field)
      }

      return result
    }

    at += 4 + length
  }

  return null
}

/**
 * A 64-bit little-endian field as a number. ⚠️ Above 2^53 this loses precision — which for a *file size* means a
 * figure no clinic will ever meet, and the alternative is threading `bigint` through a display string.
 */
function readUint64(view: DataView, at: number): number {
  return view.getUint32(at, true) + view.getUint32(at + 4, true) * 0x100000000
}

/** The DOS date and time pair the record carries, or null when it is the « unset » zero. */
function dosDate(date: number, time: number): Date | null {
  if (date === 0) return null

  const year = 1980 + ((date >> 9) & 0x7f)
  const month = (date >> 5) & 0x0f
  const day = date & 0x1f
  if (month < 1 || month > 12 || day < 1 || day > 31) return null

  const parsed = new Date(year, month - 1, day, (time >> 11) & 0x1f, (time >> 5) & 0x3f, (time & 0x1f) * 2)

  return Number.isNaN(parsed.getTime()) ? null : parsed
}
