"use client"

import { Progress } from "@/components/ui/progress"
import { formatFileSize } from "@/lib/format"
import type { UploadPolicy } from "@/lib/api/upload-policy"
import { cn } from "@/lib/utils"

/**
 * « 3,1 Go sur 10 Go utilisés » — the cabinet's storage ceiling, where files are added
 * (`large-file-transfer` Part 4).
 *
 * ⚠️ **It renders nothing at all where nothing is enforced**, which is `SelfHostedLan`: the clinic's own machine
 * is the object store there, so a meter would be this product reporting on somebody's own disk as though it
 * managed it. The server says so by publishing a quota of **0**, and that absence is the whole signal — the
 * browser never infers it from a deployment kind it is not told.
 *
 * ⚠️ **One line, not a card, and it is always on screen rather than appearing near the limit.** A card would be
 * the fourth bordered surface above a file list. But hiding it until 80 % was the other candidate and it is
 * worse: the first time a practice learned there was a ceiling would be the day it stopped them, which is
 * exactly the surprise Part 4 exists to prevent. Quiet and permanent beats loud and late.
 *
 * ⚠️ The tone escalates on the **meter and the figures**, never on a background wash: this sits directly above
 * the drop zone, and a tinted band there reads as an error state on a screen where nothing is wrong yet.
 */
export function StorageUsageLine({ policy }: { policy?: UploadPolicy | null }) {
  if (!policy || policy.storageQuotaBytes <= 0) return null

  const used = policy.storageUsedBytes
  const quota = policy.storageQuotaBytes
  // Clamped: a ceiling lowered under a cabinet's feet would otherwise paint a bar past its own track.
  const percent = Math.min(100, Math.round((used / quota) * 100))

  const full = used >= quota
  const nearlyFull = !full && percent >= 80

  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-1" role="status">
      <p className="text-xs text-muted-foreground">
        Stockage du cabinet :{" "}
        <span
          className={cn(
            "font-medium tabular-nums",
            full && "text-destructive",
            nearlyFull && "text-warning-ink",
          )}
        >
          {formatFileSize(used)} sur {formatFileSize(quota)}
        </span>
      </p>

      <Progress
        value={percent}
        className={cn(
          "h-1.5 w-full max-w-40 shrink-0",
          // The bar's own fill, so the figure and the meter say the same thing in the same place.
          full && "[&>*]:bg-destructive",
          nearlyFull && "[&>*]:bg-warning",
        )}
        aria-label={`Stockage utilisé : ${percent} %`}
      />

      {/* Only when it is actionable. « Il reste 6,9 Go » on an empty cabinet is a number nobody needs. */}
      {(full || nearlyFull) && (
        <p className={cn("text-xs", full ? "text-destructive" : "text-warning-ink")}>
          {full
            ? "Plein. Supprimez des fichiers volumineux, ou contactez APEXA pour augmenter l'espace."
            : `Il reste ${formatFileSize(quota - used)}.`}
        </p>
      )}
    </div>
  )
}
