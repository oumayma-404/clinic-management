import { File as FileIcon, FileArchive, FileText, ImageIcon, Box, type LucideIcon } from "lucide-react"

import { hasDecoder } from "@/lib/files/decoders"
import { formatFor, type UploadPolicy } from "@/lib/api/upload-policy"
import type { PatientFileDto } from "@/lib/api/types"

/**
 * What kind of thing a stored file is, in the one place both the manager and the patient page ask.
 *
 * ⚠️ **Whether a browser can render it is the server's answer, not a list kept here** (AC-5.2). The catalog
 * carries `isBrowserPreviewable` per format, so a HEIC — an image by MIME type and unrenderable by every
 * desktop browser — is known to be un-previewable rather than discovered by a broken `<img>`. When the policy
 * has not loaded we stay optimistic and let the element's own `onError` say so; a second hardcoded list here
 * is the mirroring AC-5.1 exists to remove.
 *
 * ⚠️ **`lib/files/decoders` is not that second list.** The catalog's flag answers « does a *browser* paint this
 * unaided? », which is a fact about the format and is still the server's to state. Whether this *build* ships a
 * decoder for it is a different fact, about this bundle's module graph, and one no server can know. The two are
 * **unioned** below and never compared, so they cannot drift into disagreeing.
 */
export function isImageFile(file: PatientFileDto): boolean {
  return file.contentType.startsWith("image/")
}

export function isPdfFile(file: PatientFileDto): boolean {
  return file.contentType === "application/pdf" || file.fileName.toLowerCase().endsWith(".pdf")
}

/** How the viewer should show a file, once its bytes are in hand. */
export type PreviewMode =
  /** An `<img>` straight off the original — the browser decodes it itself. */
  | "image"
  /** The PDF frame. */
  | "pdf"
  /** A decoder in this build turns it into a picture or a listing first. */
  | "decode"
  /** Nothing can be shown; the dialog offers the download instead. */
  | "none"

/** Whether the browser paints this format with no help — the server's own answer, when it has been served. */
function paintsNatively(file: PatientFileDto, policy?: UploadPolicy | null): boolean {
  const format = policy ? formatFor(policy, file.fileName) : null
  if (format) return format.isBrowserPreviewable

  // No policy in hand: stay optimistic and let the element's own `onError` be the verdict.
  return isImageFile(file) || isPdfFile(file)
}

export function previewMode(file: PatientFileDto, policy?: UploadPolicy | null): PreviewMode {
  if (isPdfFile(file)) return paintsNatively(file, policy) ? "pdf" : "none"
  if (paintsNatively(file, policy) && isImageFile(file)) return "image"
  if (hasDecoder(file.fileName)) return "decode"

  return "none"
}

/** Whether opening this file shows anything at all — the gate on fetching its bytes. */
export function isPreviewableFile(file: PatientFileDto, policy?: UploadPolicy | null): boolean {
  return previewMode(file, policy) !== "none"
}

/**
 * True only for a row that has a stand-in image to paint.
 *
 * ⚠️ **It asks about the *preview*, not about the original, and asking the wrong one was a real defect.** The
 * tile fetches `downloadPreview` — a JPEG the server validated on the way in — so whether the *original* is a
 * format a browser paints has nothing to do with it. Gating on that hid the thumbnail of every HEIC and every
 * TIFF whose stand-in was sitting there ready to serve, and the size guard beside it measured the original's
 * bytes, so a 40 Mo panoramique with a 200 Ko preview showed an icon for want of a download nobody was making.
 */
export function isThumbnailable(file: PatientFileDto): boolean {
  return file.hasPreview
}

export function fileIcon(file: PatientFileDto): LucideIcon {
  const type = file.contentType
  if (type.startsWith("image/")) return ImageIcon
  if (type.includes("pdf")) return FileText
  if (type.includes("zip") || type.includes("rar")) return FileArchive
  if (type.startsWith("model/") || type === "application/dicom") return Box
  return FileIcon
}
