/**
 * Where an import's files come from (`features/camera-photo-import`): a card's folder, a file picker (a phone's
 * gallery included), or a drop. Every path ends in the same {@link SourceBatch} — the photos to read and a count of
 * what was set aside, so « 37 RAW ignorées » can say what to change on the camera.
 *
 * ⚠️ **Read-only, always.** Nothing here writes to, renames or deletes anything on the card: the folder picker asks
 * for `mode: "read"`, and a card emptied by an import tool is a dentist's photos gone.
 */

export type SkippedKind = "raw" | "heic" | "video" | "other"

export interface SourceBatch {
  photos: File[]
  skipped: Record<SkippedKind, number>
  /** Set when the Windows app handed these photos over (`shell-camera.ts`) — absent on every browser path. */
  shell?: ShellBatchInfo
}

/** A Windows-app batch: which shell item each file answers for, and what stayed on the source. */
export interface ShellBatchInfo {
  batchId: string
  label: string
  /** Photos left on the card or the camera, outside the window — « Voir les plus anciennes » asks the shell. */
  older: number
  /** Photos the shell could not copy off the source. */
  failed: number
  itemIds: Map<File, string>
}

const RAW = new Set(["cr2", "cr3", "crw", "nef", "nrw", "arw", "srf", "sr2", "dng", "raf", "orf", "rw2", "pef", "srw", "x3f", "3fr", "iiq", "rwl"])
const HEIC = new Set(["heic", "heif", "hif"])
const VIDEO = new Set(["mp4", "mov", "avi", "mts", "m2ts", "mpg", "mpeg", "3gp", "wmv", "mkv"])
const JPEG = new Set(["jpg", "jpeg", "jpe"])

/**
 * A card's own bookkeeping, never a photo: hidden and AppleDouble (`._IMG…`) files, thumbnail databases.
 * ⚠️ Not a leading `_`: a Nikon in Adobe RGB names every photo `_DSC0001.JPG`.
 */
const IGNORED_NAMES = /^(\.|thumbs\.db$|desktop\.ini$)/i
const IGNORED_FOLDERS = /^(\.|misc$|\$recycle\.bin$|system volume information$|\.trashes$)/i

/** At most this many files are read from one source — a card mounted at the wrong folder can hold a whole disk. */
export const MAX_SOURCE_FILES = 5000

function extensionOf(name: string): string {
  const dot = name.lastIndexOf(".")
  return dot > 0 ? name.slice(dot + 1).toLowerCase() : ""
}

export function emptyBatch(): SourceBatch {
  return { photos: [], skipped: { raw: 0, heic: 0, video: 0, other: 0 } }
}

/** Sorts one file into the batch. Exported for the drop and picker paths, which see files one at a time. */
export function addToBatch(batch: SourceBatch, file: File): void {
  if (IGNORED_NAMES.test(file.name)) return
  const extension = extensionOf(file.name)
  if (JPEG.has(extension) || (!extension && file.type === "image/jpeg")) batch.photos.push(file)
  else if (RAW.has(extension)) batch.skipped.raw++
  else if (HEIC.has(extension)) batch.skipped.heic++
  else if (VIDEO.has(extension)) batch.skipped.video++
  else batch.skipped.other++
}

export function batchOfFiles(files: Iterable<File>): SourceBatch {
  const batch = emptyBatch()
  for (const file of files) {
    if (batch.photos.length >= MAX_SOURCE_FILES) break
    addToBatch(batch, file)
  }
  return batch
}

/** Whether this browser can open a folder for reading (Chromium on a computer). Elsewhere: the file picker. */
export function canPickFolder(): boolean {
  return typeof window !== "undefined" && typeof window.showDirectoryPicker === "function"
}

/**
 * Asks for a folder (the card, or its `DCIM`) and lists its photos, recursively. Resolves null when the user
 * cancels — an ordinary answer, not an error.
 */
export async function pickFolder(): Promise<SourceBatch | null> {
  const picker = typeof window !== "undefined" ? window.showDirectoryPicker : undefined
  if (!picker) return null

  let root: FileSystemDirectoryHandle
  try {
    root = await picker.call(window, { id: "camera-photo-import", mode: "read", startIn: "pictures" })
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") return null
    throw error
  }

  return batchOfDirectory(root)
}

export async function batchOfDirectory(root: FileSystemDirectoryHandle): Promise<SourceBatch> {
  const batch = emptyBatch()

  const walk = async (directory: FileSystemDirectoryHandle, depth: number): Promise<void> => {
    // A card is DCIM/100CANON/IMG_0001.JPG — four levels covers every camera's layout with room to spare.
    if (depth > 4) return
    for await (const entry of directory.values()) {
      if (batch.photos.length >= MAX_SOURCE_FILES) return
      if (entry.kind === "directory") {
        if (!IGNORED_FOLDERS.test(entry.name)) await walk(entry as FileSystemDirectoryHandle, depth + 1)
      } else {
        addToBatch(batch, await (entry as FileSystemFileHandle).getFile())
      }
    }
  }

  await walk(root, 0)
  return batch
}

/**
 * The files of a drop, folders included. `webkitGetAsEntry` is the only way to see into a dropped folder in every
 * browser, and it must be called synchronously inside the drop handler — hence the entries are taken first.
 */
export async function batchOfDrop(items: DataTransferItemList): Promise<SourceBatch> {
  const batch = emptyBatch()
  const entries: FileSystemEntry[] = []
  const loose: File[] = []

  for (const item of Array.from(items)) {
    if (item.kind !== "file") continue
    const entry = item.webkitGetAsEntry?.()
    if (entry) entries.push(entry)
    else {
      const file = item.getAsFile()
      if (file) loose.push(file)
    }
  }

  for (const file of loose) addToBatch(batch, file)

  const readAll = (reader: FileSystemDirectoryReader) =>
    new Promise<FileSystemEntry[]>((resolve, reject) => {
      const all: FileSystemEntry[] = []
      // `readEntries` returns at most 100 at a time; an empty answer is the end.
      const next = () =>
        reader.readEntries((chunk) => {
          if (chunk.length === 0) resolve(all)
          else {
            all.push(...chunk)
            next()
          }
        }, reject)
      next()
    })

  const fileOf = (entry: FileSystemFileEntry) => new Promise<File>((resolve, reject) => entry.file(resolve, reject))

  const visit = async (entry: FileSystemEntry, depth: number): Promise<void> => {
    if (batch.photos.length >= MAX_SOURCE_FILES || depth > 5) return
    if (entry.isDirectory) {
      if (IGNORED_FOLDERS.test(entry.name)) return
      for (const child of await readAll((entry as FileSystemDirectoryEntry).createReader())) await visit(child, depth + 1)
    } else if (entry.isFile) {
      addToBatch(batch, await fileOf(entry as FileSystemFileEntry))
    }
  }

  for (const entry of entries) await visit(entry, 0)
  return batch
}

/** « 37 RAW · 2 vidéos ignorées » — what was set aside and why, or null when nothing was. */
export function skippedSummary(skipped: Record<SkippedKind, number>): string | null {
  const parts: string[] = []
  if (skipped.raw > 0) parts.push(`${skipped.raw} RAW`)
  if (skipped.heic > 0) parts.push(`${skipped.heic} HEIC`)
  if (skipped.video > 0) parts.push(`${skipped.video} vidéo${skipped.video > 1 ? "s" : ""}`)
  if (skipped.other > 0) parts.push(`${skipped.other} autre${skipped.other > 1 ? "s" : ""}`)
  if (parts.length === 0) return null
  const total = skipped.raw + skipped.heic + skipped.video + skipped.other
  return `${parts.join(" · ")} ${total > 1 ? "ignorés" : "ignoré"}`
}
