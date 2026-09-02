/**
 * The small stand-in image a stored file carries — the thumbnail behind every tile in a patient's drawer, and
 * the only picture a coffre original has on the machines that cannot reach the coffre.
 *
 * ⚠️ **It is built here, in the browser, on the way up, and that is a deliberate choice with a real cost.** The
 * alternative is a server-side image pipeline: another dependency, another decoder to keep patched, and CPU on
 * a shared host for every upload in every clinic. The browser already holds the bytes, already has the codecs
 * (and, since `lib/files/decoders`, the ones it lacks), and is idle while the user waits — so the picture is
 * made where the file is. The cost is that a file uploaded by an *older* client carries no preview, and one
 * uploaded before this existed carries none at all. `DownloadPatientFilePreviewQuery` answers that second case
 * by serving a small hosted original in the stand-in's place.
 *
 * ⚠️ **It used to live under `lib/vault/` and to return null for almost everything.** Only the coffre asked for
 * one, and its `decodable()` was `png|jpeg|webp|gif|bmp` — which is precisely the set of formats the coffre
 * never takes, so in practice it returned null every time it was called. Both halves are fixed here: hosted
 * uploads build one too, and the decoders cover HEIC and TIFF.
 *
 * A registration is **never** refused for want of a preview: the row is the record, the picture is a courtesy.
 */
import { decodeToImage, decodesToImage } from './decoders'

/** The server drops anything larger, so there is no point producing one. Mirrors `FileTypeCatalog.PreviewBytes`. */
const PREVIEW_MAX_BYTES = 4 * 1024 * 1024

const PREVIEW_EDGE = 1400
const PREVIEW_QUALITY = 0.82

/** What a browser can decode on its own, without a format-specific parser. */
function nativelyDecodable(file: File): boolean {
  return /^image\/(png|jpeg|webp|gif|bmp)$/.test(file.type)
}

/** Whether a stand-in can be attempted at all — the gate a caller uses to skip the work entirely. */
export function canBuildPreview(file: File): boolean {
  return nativelyDecodable(file) || decodesToImage(file.name)
}

/**
 * A downscaled JPEG of `file`, or null when none can be made — an undecodable format, a decode failure, or a
 * result still over the cap. Every one of those is a normal answer, so nothing here throws.
 */
export async function buildPreview(file: File): Promise<Blob | null> {
  if (typeof window === 'undefined') return null

  const source = await paintable(file)
  if (!source) return null

  let bitmap: ImageBitmap
  try {
    bitmap = await createImageBitmap(source)
  } catch {
    return null
  }

  try {
    const scale = Math.min(1, PREVIEW_EDGE / Math.max(bitmap.width, bitmap.height))
    const width = Math.max(1, Math.round(bitmap.width * scale))
    const height = Math.max(1, Math.round(bitmap.height * scale))

    const canvas = document.createElement('canvas')
    canvas.width = width
    canvas.height = height

    const context = canvas.getContext('2d')
    if (!context) return null
    context.drawImage(bitmap, 0, 0, width, height)

    const blob = await new Promise<Blob | null>((resolve) => {
      canvas.toBlob(resolve, 'image/jpeg', PREVIEW_QUALITY)
    })

    return blob && blob.size <= PREVIEW_MAX_BYTES ? blob : null
  } catch {
    return null
  } finally {
    bitmap.close()
  }
}

/** The bytes `createImageBitmap` can take: the file itself, or what a decoder made of it. */
async function paintable(file: File): Promise<Blob | null> {
  if (nativelyDecodable(file)) return file
  if (!decodesToImage(file.name)) return null

  try {
    return await decodeToImage(file, file.name)
  } catch {
    return null
  }
}

/**
 * The name a preview is uploaded under. ⚠️ The server validates it through the **`ProfileImage`** door, whose
 * allow-list is PNG and JPEG, so the extension has to match what {@link buildPreview} actually encodes — a
 * mismatch is dropped silently, which reads as « thumbnails do not work » with nothing in any log.
 */
export const PREVIEW_FILE_NAME = 'preview.jpg'
