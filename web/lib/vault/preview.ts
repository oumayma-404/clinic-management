/**
 * The small stand-in image a coffre file carries, for the machines that cannot reach the coffre.
 *
 * ⚠️ **Returning null is the normal outcome today, and that is deliberate.** Every format the catalog files in
 * the coffre — DICOM, STL, PLY, OBJ, 3MF, ZIP — is `isBrowserPreviewable: false`, i.e. no browser can decode
 * one without a format-specific parser. Rather than pull a DICOM decoder into a medical app for a thumbnail,
 * v1 registers those with **no preview** and the list renders a typed placeholder. The plumbing is here, and
 * the server already stores, caps and serves a preview, so adding a decoder later changes this file alone.
 *
 * A registration is **never** refused for want of a preview: the row is the record, the picture is a courtesy.
 */

/** The server drops anything larger, so there is no point producing one. Mirrors `FileTypeCatalog.PreviewBytes`. */
const PREVIEW_MAX_BYTES = 4 * 1024 * 1024

const PREVIEW_EDGE = 1400
const PREVIEW_QUALITY = 0.82

/** What a browser can decode on its own, without a format-specific parser. */
function decodable(file: File): boolean {
  return /^image\/(png|jpeg|webp|gif|bmp)$/.test(file.type)
}

/**
 * A downscaled JPEG of `file`, or null when none can be made — an undecodable format, a decode failure, or a
 * result still over the cap. Every one of those is a normal answer, so nothing here throws.
 */
export async function buildPreview(file: File): Promise<Blob | null> {
  if (typeof window === 'undefined' || !decodable(file)) return null

  let bitmap: ImageBitmap
  try {
    bitmap = await createImageBitmap(file)
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
