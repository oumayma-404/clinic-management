/**
 * What a camera photo is stored as (`features/camera-photo-import`): on a hosted cabinet, a JPEG copy no longer than
 * the server's `cameraCopy.maxEdgePx` on its long edge (≈ 1,5 Mo instead of 8–12); on a cabinet's own server, the
 * photo itself. **The server decides** — `cameraCopy` is null where the original is kept as it is.
 *
 * ⚠️ **The rotation is baked in** (`imageOrientation: "from-image"`): the copy carries no EXIF, so a portrait photo
 * whose pixels lie on their side would be stored lying down. Dropping the EXIF is also a privacy gain — a phone's GPS
 * position never reaches the server — and it is why the capture time and the model travel as fields of their own.
 */
import { encodeBitmap, fitWithin } from "@/lib/files/decoders/raster"

export interface CameraCopyRule {
  maxEdgePx: number
  quality: number
}

export class CloudCopyError extends Error {
  constructor() {
    super("Cette photo n'a pas pu être lue : le fichier est peut-être abîmé.")
    this.name = "CloudCopyError"
  }
}

/** The file to upload for this photo. Throws {@link CloudCopyError} when a copy was required and could not be made. */
export async function makeCloudCopy(file: File, rule: CameraCopyRule | null | undefined): Promise<File> {
  if (!rule) return file

  let bitmap: ImageBitmap
  try {
    bitmap = await createImageBitmap(file, { imageOrientation: "from-image" })
  } catch {
    throw new CloudCopyError()
  }

  try {
    const size = fitWithin(bitmap.width, bitmap.height, rule.maxEdgePx)
    if (size.width === 0) throw new CloudCopyError()
    const blob = await encodeBitmap(bitmap, size.width, size.height, rule.quality)
    if (!blob) throw new CloudCopyError()
    return new File([blob], jpegName(file.name), { type: "image/jpeg", lastModified: file.lastModified })
  } finally {
    bitmap.close()
  }
}

/** The camera's own name, with a JPEG extension whatever case the camera wrote it in. */
function jpegName(name: string): string {
  const dot = name.lastIndexOf(".")
  const base = dot > 0 ? name.slice(0, dot) : name
  return `${base}.jpg`
}
