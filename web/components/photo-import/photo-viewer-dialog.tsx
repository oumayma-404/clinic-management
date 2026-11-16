"use client"

import { useEffect, useState } from "react"
import { ChevronLeft, ChevronRight, EyeOff, Undo2, UserRoundPen } from "lucide-react"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { correctedTime, toggleExcluded, type ImportPhoto, type ReviewGroup } from "@/lib/photo-import/session"
import { formatWallClockTime } from "@/lib/photo-import/wall-clock"
import { cn } from "@/lib/utils"

/**
 * One photo of a group, large (`features/camera-photo-import`) — what a dentist taps a thumbnail for.
 *
 * ⚠️ **The thumbnails are the last line against a photo in the wrong group**, and at 64 px a face or a name card cannot
 * be read. So a tap opens the photo instead of setting it aside, and the two decisions about it live here:
 * « Écarter » (it stays on the card) and « Ranger chez un autre patient » (it leaves this group and asks whose it is).
 * ←/→ step through the group.
 */
export function PhotoViewerDialog({
  group,
  photos,
  excluded,
  photoId,
  patientName,
  onPhotoChange,
  onClose,
  onTakeOut,
}: {
  group: ReviewGroup
  photos: Map<string, ImportPhoto>
  excluded: Record<string, true>
  /** The photo shown, or null when closed. */
  photoId: string | null
  /** Who the group goes to now, if anyone. */
  patientName: string | null
  onPhotoChange: (photoId: string) => void
  onClose: () => void
  onTakeOut: (photoId: string) => void
}) {
  const ids = group.photoIds
  const index = photoId ? ids.indexOf(photoId) : -1
  const photo = photoId ? photos.get(photoId) : undefined
  const [url, setUrl] = useState<string | null>(null)

  // The photo itself, decoded by the browser — the EXIF thumbnail is 160 px and this is what it is opened for.
  useEffect(() => {
    if (!photo) return
    const objectUrl = URL.createObjectURL(photo.file)
    setUrl(objectUrl)
    return () => {
      URL.revokeObjectURL(objectUrl)
      setUrl(null)
    }
  }, [photo])

  useEffect(() => {
    if (index < 0) return
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "ArrowLeft" && index > 0) onPhotoChange(ids[index - 1])
      if (event.key === "ArrowRight" && index < ids.length - 1) onPhotoChange(ids[index + 1])
    }
    window.addEventListener("keydown", onKey)
    return () => window.removeEventListener("keydown", onKey)
  }, [index, ids, onPhotoChange])

  const time = photo ? correctedTime(photo) : null
  const out = photoId ? Boolean(excluded[photoId]) : false
  const label = time !== null ? formatWallClockTime(time) : (photo?.file.name ?? "")
  // A group of one is moved with its own « Changer »; a photo already taken out is put back from its card.
  const canTakeOut = ids.length > 1 && group.reason !== "moved"

  return (
    <Dialog open={Boolean(photo)} onOpenChange={(open) => { if (!open) onClose() }}>
      <DialogContent mobile="sheet" className="md:max-w-5xl">
        <DialogHeader>
          <DialogTitle>Photo de {label}</DialogTitle>
          <DialogDescription>
            {[
              // Where in the burst: shots seconds apart all carry the same minute.
              ids.length > 1 && index >= 0 ? `${index + 1} / ${ids.length}` : null,
              photo?.file.name,
              patientName ? `groupe de ${patientName}` : null,
              out ? "écartée" : null,
            ]
              .filter(Boolean)
              .join(" · ")}
          </DialogDescription>
        </DialogHeader>

        <DialogBody>
          <div className="relative flex min-h-full">
            {url && (
              // eslint-disable-next-line @next/next/no-img-element -- a local object URL; next/image cannot optimise it
              <img
                src={url}
                alt={`Photo de ${label}`}
                decoding="async"
                // Capped by what is left of the screen too: on a landscape phone 62 % of the height is taller than the
                // dialog's own body, and the face at the bottom of the photo had to be scrolled to.
                className={cn(
                  "m-auto max-h-[min(62dvh,calc(100dvh-15rem))] w-auto max-w-full rounded-md object-contain",
                  out && "opacity-50",
                )}
              />
            )}
            {index > 0 && (
              <Button
                type="button"
                variant="secondary"
                size="icon"
                className="absolute start-1 top-1/2 size-10 -translate-y-1/2 rounded-full shadow coarse:size-11"
                aria-label="Photo précédente"
                onClick={() => onPhotoChange(ids[index - 1])}
              >
                <ChevronLeft className="size-5" aria-hidden="true" />
              </Button>
            )}
            {index >= 0 && index < ids.length - 1 && (
              <Button
                type="button"
                variant="secondary"
                size="icon"
                className="absolute end-1 top-1/2 size-10 -translate-y-1/2 rounded-full shadow coarse:size-11"
                aria-label="Photo suivante"
                onClick={() => onPhotoChange(ids[index + 1])}
              >
                <ChevronRight className="size-5" aria-hidden="true" />
              </Button>
            )}
          </div>
        </DialogBody>

        <DialogFooter className="gap-2 sm:justify-between">
          <Button type="button" variant="outline" className="coarse:min-h-11" onClick={onClose}>
            Fermer
          </Button>
          <div className="flex flex-col-reverse gap-2 sm:flex-row">
            {photoId && (
              <Button
                type="button"
                variant="outline"
                className="gap-1.5 coarse:min-h-11"
                onClick={() => toggleExcluded(photoId)}
              >
                {out ? <Undo2 className="size-4" aria-hidden="true" /> : <EyeOff className="size-4" aria-hidden="true" />}
                {out ? "Reprendre cette photo" : "Écarter cette photo"}
              </Button>
            )}
            {photoId && canTakeOut && (
              <Button type="button" className="gap-1.5 coarse:min-h-11" onClick={() => onTakeOut(photoId)}>
                <UserRoundPen className="size-4" aria-hidden="true" />
                Ranger chez un autre patient
              </Button>
            )}
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
