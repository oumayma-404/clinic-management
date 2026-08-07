import { File as FileIcon, FileArchive, FileText, ImageIcon, Box, type LucideIcon } from "lucide-react"

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
 */
export function isImageFile(file: PatientFileDto): boolean {
  return file.contentType.startsWith("image/")
}

export function isPdfFile(file: PatientFileDto): boolean {
  return file.contentType === "application/pdf" || file.fileName.toLowerCase().endsWith(".pdf")
}

export function isPreviewableFile(file: PatientFileDto, policy?: UploadPolicy | null): boolean {
  const format = policy ? formatFor(policy, file.fileName) : null
  if (format) return format.isBrowserPreviewable
  return isImageFile(file) || isPdfFile(file)
}

/** True only for what a thumbnail may fetch — an image the browser will actually paint. */
export function isThumbnailable(file: PatientFileDto, policy?: UploadPolicy | null): boolean {
  return isImageFile(file) && isPreviewableFile(file, policy)
}

export function fileIcon(file: PatientFileDto): LucideIcon {
  const type = file.contentType
  if (type.startsWith("image/")) return ImageIcon
  if (type.includes("pdf")) return FileText
  if (type.includes("zip") || type.includes("rar")) return FileArchive
  if (type.startsWith("model/") || type === "application/dicom") return Box
  return FileIcon
}
