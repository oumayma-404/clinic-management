import { Camera } from "lucide-react"

import type { PatientFileDto } from "@/lib/api/types"
import { formatDateTime, formatFileSize } from "@/lib/format"
import { cn } from "@/lib/utils"

/** « Prise le 12/11/2026 à 09:14 » — or the model alone when the camera's clock was not believed. */
export function cameraCaptureLine(file: PatientFileDto): string | null {
  if (file.origin !== "Camera") return null
  if (file.capturedAt) return `Prise le ${formatDateTime(file.capturedAt).replace(" ", " à ")}`
  return file.cameraModel ? `Appareil photo · ${file.cameraModel}` : "Appareil photo"
}

/** « Original : PC ACCUEIL (8,4 Mo) » — where the full-size photo is, when a clinic PC reported keeping it. */
export function cameraOriginalLine(file: PatientFileDto): string | null {
  if (file.origin !== "Camera" || !file.originalHeldBy) return null
  const size = file.originalSizeBytes ? ` (${formatFileSize(file.originalSizeBytes)})` : ""
  return `Original : ${file.originalHeldBy}${size}`
}

/**
 * Says a file is a camera photo and when it was taken (`features/camera-photo-import`). The upload date beside it is
 * when it was FILED — on an end-of-day import, hours later, and on a card imported after a holiday, days.
 *
 * ⚠️ **Nothing for an ordinary upload**, which is most files: a badge on every row would carry no decision.
 */
export function CameraBadge({ file, className }: { file: PatientFileDto; className?: string }) {
  const line = cameraCaptureLine(file)
  if (!line) return null
  const original = cameraOriginalLine(file)

  return (
    <span
      className={cn(
        "inline-flex max-w-full items-center gap-1 rounded-full border border-border bg-muted/60 px-1.5 py-0.5",
        "text-2xs font-medium text-muted-foreground",
        className,
      )}
      title={[file.cameraModel, original].filter(Boolean).join(" · ") || undefined}
    >
      <Camera className="h-3 w-3 shrink-0" aria-hidden="true" />
      <span className="truncate">{line}</span>
    </span>
  )
}
