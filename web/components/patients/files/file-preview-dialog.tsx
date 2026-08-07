"use client"

import { useEffect, useRef, useState } from "react"
import { ChevronLeft, ChevronRight, Download, Loader2, X } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { PatientFilePdfPreview } from "@/components/patient-file-pdf-preview"
import { FileThumbnail } from "@/components/patients/files/file-thumbnail"
import { formatDate, formatFileSize } from "@/lib/format"
import { cn } from "@/lib/utils"
import type { UploadPolicy } from "@/lib/api/upload-policy"
import type { PatientFileDto } from "@/lib/api/types"

import { fileIcon, isImageFile, isPdfFile } from "./file-kind"
import type { FilePreview } from "./use-file-preview"

/** Below this a swipe is a tap that wandered, not a gesture. */
const SWIPE_THRESHOLD_PX = 50

/**
 * A patient file's preview — **one copy** (AC-5.3), consumed by the files manager and by the patient page's
 * « Fichiers » tab. `onDelete` is optional: the tab offers no destructive action.
 *
 * <p>It walks the whole list — arrows, ←/→, swipe, filmstrip — and a format the browser cannot paint keeps its
 * place in that walk rather than being skipped, so « suivant » never appears to lose a file.</p>
 */
export function FilePreviewDialog({
  preview,
  patientId,
  policy,
  onDownload,
  onDelete,
}: {
  preview: FilePreview
  /** Only for the filmstrip's thumbnails; with it absent the strip is not rendered. */
  patientId?: string
  policy?: UploadPolicy | null
  onDownload: (file: PatientFileDto) => void
  onDelete?: (file: PatientFileDto) => void
}) {
  const { file, url, loading, files, position, total, hasPrev, hasNext, close, prev, next } = preview
  const [renderFailed, setRenderFailed] = useState(false)
  const swipeStartX = useRef<number | null>(null)

  // A new file gets a fresh verdict: one unpaintable image must not turn every later preview into the fallback.
  useEffect(() => setRenderFailed(false), [file?.id])

  useEffect(() => {
    if (!file) return
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "ArrowLeft") { event.preventDefault(); prev() }
      if (event.key === "ArrowRight") { event.preventDefault(); next() }
    }
    window.addEventListener("keydown", onKey)
    return () => window.removeEventListener("keydown", onKey)
  }, [file, prev, next])

  const pdf = file ? isPdfFile(file) : false
  const navigable = hasPrev || hasNext

  return (
    <Dialog
      open={!!file}
      onOpenChange={(open) => {
        if (!open) close()
      }}
    >
      {/* A preview is the one dialog that wants the whole screen on a phone — it is showing a document.
          ⚠️ ONE fixed size for every format, and a fixed HEIGHT rather than `md:h-auto`: the width used to fork on
          `isPdfFile`, so arrowing from a radiograph to a compte rendu resized the window under the user, and an
          auto height made the panel jump on every step. The document is fitted to the frame, never the reverse. */}
      <DialogContent
        mobile="sheet"
        className="gap-0 p-0 md:h-[90dvh] md:max-h-[90dvh] md:max-w-5xl md:overflow-hidden"
      >
        {file && (
          <>
            {/* `pe-12` clears the close button Radix pins to the corner — without it the file name runs under it. */}
            <DialogHeader className="flex-shrink-0 border-b bg-muted/40 px-4 pb-3 pe-12 pt-4 md:px-6 md:pb-4 md:pe-12 md:pt-6">
              <DialogTitle className="truncate text-base font-semibold md:text-lg">{file.fileName}</DialogTitle>
              <DialogDescription className="mt-1 text-xs md:text-sm">
                {position > 0 && total > 1 && (
                  <span className="me-1 font-medium tabular-nums text-foreground">
                    {position} / {total} •
                  </span>
                )}
                {formatFileSize(file.fileSize)} • {formatDate(file.uploadedAt)}
                {file.description ? ` • ${file.description}` : ""}
              </DialogDescription>
            </DialogHeader>

            <div
              className={cn(
                "relative flex min-h-0 flex-1 overflow-auto bg-muted/30",
                // A document viewer wants the pixels; a photo wants a mount around it.
                pdf ? "p-0 md:p-3" : "p-4 md:p-6",
              )}
              onTouchStart={(e) => { swipeStartX.current = e.touches[0]?.clientX ?? null }}
              onTouchEnd={(e) => {
                const from = swipeStartX.current
                swipeStartX.current = null
                if (from === null) return
                const travelled = (e.changedTouches[0]?.clientX ?? from) - from
                if (Math.abs(travelled) < SWIPE_THRESHOLD_PX) return
                if (travelled < 0) next()
                else prev()
              }}
            >
              {loading ? (
                <div className="m-auto flex flex-col items-center gap-3" role="status">
                  <Loader2 className="h-8 w-8 animate-spin text-primary" />
                  <p className="text-sm text-muted-foreground">Chargement de l&apos;aperçu…</p>
                </div>
              ) : url && !renderFailed ? (
                isImageFile(file) ? (
                  /* `m-auto`, not `items-center`: an auto margin resolves to 0 with no free space, so a tall
                     radiograph stays scrollable from its top edge instead of overflowing above the box (§ 11). */
                  <img
                    src={url}
                    alt={file.fileName}
                    onError={() => setRenderFailed(true)}
                    className="m-auto max-h-full max-w-full rounded-lg object-contain shadow-lg"
                  />
                ) : pdf ? (
                  <PatientFilePdfPreview
                    previewUrl={url}
                    fileName={file.fileName}
                    onDeliver={() => onDownload(file)}
                  />
                ) : (
                  <UnavailablePreview file={file} onDownload={onDownload} />
                )
              ) : (
                <UnavailablePreview file={file} onDownload={onDownload} />
              )}

              {navigable && (
                <>
                  <NavArrow side="prev" disabled={!hasPrev} onClick={prev} />
                  <NavArrow side="next" disabled={!hasNext} onClick={next} />
                </>
              )}
            </div>

            {patientId && files.length > 1 && (
              <Filmstrip preview={preview} patientId={patientId} policy={policy} />
            )}

            <DialogFooter className="flex-shrink-0 border-t bg-muted/40 px-4 py-3 md:px-6 md:py-4">
              <div className="flex w-full items-center justify-between gap-2">
                {/* « Fermer » stays at every width. The 16 px corner glyph is not a way out anybody finds on a
                    phone, and a full-screen sheet with no visible exit reads as the app having hung. */}
                <Button variant="outline" onClick={close} className="coarse:h-11 sm:min-w-[100px]">
                  Fermer
                </Button>
                <div className="flex min-w-0 flex-1 items-center justify-end gap-2">
                  <Button variant="outline" onClick={() => onDownload(file)} className="min-w-0 flex-1 gap-2 coarse:h-11 sm:flex-none">
                    <Download className="h-4 w-4 shrink-0" />
                    <span className="truncate">Télécharger</span>
                  </Button>
                  {onDelete && (
                    <Button
                      variant="destructive"
                      onClick={() => {
                        close()
                        onDelete(file)
                      }}
                      aria-label={`Supprimer ${file.fileName}`}
                      className="shrink-0 gap-2 coarse:h-11"
                    >
                      <X className="h-4 w-4" />
                      {/* The label is what makes an irreversible action legible; below `sm:` it does not fit
                          beside the other two, and the icon keeps its 44 px box either way. */}
                      <span className="hidden sm:inline">Supprimer</span>
                    </Button>
                  )}
                </div>
              </div>
            </DialogFooter>
          </>
        )}
      </DialogContent>
    </Dialog>
  )
}

/** Overlaid on the document, so it is `size-11` painted rather than a `.touch-target` a sibling could steal. */
function NavArrow({
  side,
  disabled,
  onClick,
}: {
  side: "prev" | "next"
  disabled: boolean
  onClick: () => void
}) {
  const Icon = side === "prev" ? ChevronLeft : ChevronRight

  return (
    <Button
      variant="outline"
      size="icon"
      disabled={disabled}
      onClick={onClick}
      aria-label={side === "prev" ? "Fichier précédent" : "Fichier suivant"}
      className={cn(
        // On EVERY width. Hiding these on a phone left swipe as the only discoverable way forward, which is no
        // affordance at all — the document coverage they cost is the ordinary photo-viewer trade.
        "absolute top-1/2 size-11 -translate-y-1/2 rounded-full bg-background/85 shadow-md backdrop-blur",
        side === "prev" ? "start-2 sm:start-3" : "end-2 sm:end-3",
      )}
    >
      <Icon className="h-5 w-5" />
    </Button>
  )
}

/**
 * Jumping straight to a file rather than stepping past the ones between. It shows the **loaded page** only, so
 * the arrows can reach further than the strip does — which is why the counter above it counts the whole set.
 */
function Filmstrip({
  preview,
  patientId,
  policy,
}: {
  preview: FilePreview
  patientId: string
  policy?: UploadPolicy | null
}) {
  const { file, files, open } = preview
  const active = useRef<HTMLButtonElement | null>(null)

  useEffect(() => {
    active.current?.scrollIntoView({ block: "nearest", inline: "nearest" })
  }, [file?.id])

  return (
    <div className="flex flex-shrink-0 items-center gap-2 overflow-x-auto border-t bg-muted/20 px-4 py-2">
      {files.map((candidate) => {
        const current = candidate.id === file?.id
        return (
          <button
            key={candidate.id}
            ref={current ? active : undefined}
            type="button"
            onClick={() => open(candidate)}
            aria-label={candidate.fileName}
            aria-current={current}
            className={cn(
              "flex-none overflow-hidden rounded-md border-2 transition-colors",
              current ? "border-primary" : "border-transparent hover:border-border",
            )}
          >
            <FileThumbnail
              patientId={patientId}
              file={candidate}
              policy={policy}
              className="size-12 rounded-none coarse:size-14"
              iconClassName="h-5 w-5"
            />
          </button>
        )
      })}
    </div>
  )
}

/** What a format the browser cannot paint shows instead — the icon and the way through, never a broken image. */
function UnavailablePreview({
  file,
  onDownload,
}: {
  file: PatientFileDto
  onDownload: (file: PatientFileDto) => void
}) {
  const Icon = fileIcon(file)

  return (
    <div className="m-auto flex flex-col items-center gap-3 p-8 text-center">
      <Icon className="h-16 w-16 text-muted-foreground" />
      <p className="max-w-sm text-sm text-muted-foreground">
        Ce format ne s&apos;affiche pas dans le navigateur. Téléchargez-le pour le consulter.
      </p>
      <Button variant="outline" onClick={() => onDownload(file)}>
        <Download className="mr-2 h-4 w-4" />
        Télécharger pour consulter
      </Button>
    </div>
  )
}
