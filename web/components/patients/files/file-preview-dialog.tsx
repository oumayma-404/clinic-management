"use client"

import { useEffect, useRef, useState } from "react"
import { Box, ChevronLeft, ChevronRight, Contrast, Download, File as FileIcon, Loader2, Maximize2, TriangleAlert, X } from "lucide-react"

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
import { PREVIEW_EDGE } from "@/lib/files/preview"
import { interactiveViewerFor, type ArchiveListing } from "@/lib/files/decoders"
import { cn } from "@/lib/utils"
import type { PatientFileDto } from "@/lib/api/types"

import { DicomViewer } from "./dicom-viewer"
import { MeshViewer } from "./mesh-viewer"
import { fileIcon, isPdfFile } from "./file-kind"
import type { FilePreview, PreviewUnavailable } from "./use-file-preview"

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
  onDownload,
  onDelete,
}: {
  preview: FilePreview
  /** Only for the filmstrip's thumbnails; with it absent the strip is not rendered. */
  patientId?: string
  onDownload: (file: PatientFileDto) => void
  onDelete?: (file: PatientFileDto) => void
}) {
  const {
    file, url, archive, render, unavailable, advisory, loading, stage, showFullResolution, loadSource,
    files, position, total, hasPrev, hasNext, close, prev, next,
  } = preview
  const [renderFailed, setRenderFailed] = useState(false)
  /**
   * Whether what is on screen was actually shrunk to become a stand-in.
   *
   * ⚠️ **A stand-in of an original smaller than `PREVIEW_EDGE` is the original**, pixel for pixel — measured:
   * two 640×480 TIFFs offered « Pleine résolution » and produced a byte-identical picture after a full decode.
   * A control that spends seconds to change nothing is worse than no control, so the offer is gated on the
   * loaded image really being at the cap.
   */
  const [standInWasShrunk, setStandInWasShrunk] = useState(false)
  const swipeStartX = useRef<number | null>(null)
  /**
   * Whether the DICOM study viewer is up over this dialog.
   *
   * ⚠️ It is state here rather than inside the viewer because **this** component owns the ←/→ handler the
   * viewer has to borrow — see the effect below.
   */
  const [viewerOpen, setViewerOpen] = useState(false)

  // A new file gets a fresh verdict: one unpaintable image must not turn every later preview into the fallback.
  useEffect(() => {
    setRenderFailed(false)
    setStandInWasShrunk(false)
    setViewerOpen(false)
  }, [file?.id])

  /*
   * ⚠️ **Suspended while the study viewer is open, and that is not tidiness.** This listener is on `window`,
   * so a nested dialog cannot stop it from a React handler — and ←/→ inside the viewer step a FRAME. Left
   * live, one key press would step the frame and the underlying file at once, so closing the viewer would
   * land on a different radiograph than the one it was opened from.
   */
  useEffect(() => {
    if (!file || viewerOpen) return
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "ArrowLeft") { event.preventDefault(); prev() }
      if (event.key === "ArrowRight") { event.preventDefault(); next() }
    }
    window.addEventListener("keydown", onKey)
    return () => window.removeEventListener("keydown", onKey)
  }, [file, viewerOpen, prev, next])

  const pdf = file ? isPdfFile(file) : false
  /**
   * ⚠️ Asked of the **decoder registry**, never of a list kept here: it already answers « which decoder handles
   * this name? », and a second `.dcm`/`.dicom` list in this file is the drift AC-5.1 removed.
   *
   * ⚠️ **Which viewer, not whether one** — there are two now (a DICOM study, and a 3D model), and they share
   * this row's single « Visionneuse » slot. Asking `=== "dicom"` here is what would have left `.stl` files
   * silently without their button while everything else about them worked.
   */
  const interactive = file ? interactiveViewerFor(file.fileName) : null
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
                /* ⚠️ Two different waits, and one sentence for both was the complaint. Fetching a stand-in is a
                   fifth of a second; decoding a 51 Mpx HEIF is eleven, and a spinner that says nothing for
                   eleven seconds reads as a hung screen rather than as work in progress. */
                <div className="m-auto flex flex-col items-center gap-3 px-6 text-center" role="status">
                  <Loader2 className="h-8 w-8 animate-spin text-primary" />
                  <p className="text-sm text-muted-foreground">
                    {stage === "decoding" ? "Décodage de l’image…" : "Chargement de l’aperçu…"}
                  </p>
                  {stage === "decoding" && (
                    <p className="max-w-xs text-xs text-muted-foreground">
                      Ce format demande un décodage complet ; sur une grande image cela prend quelques secondes.
                    </p>
                  )}
                </div>
              ) : render === "image" && url && !renderFailed ? (
                /* `m-auto`, not `items-center`: an auto margin resolves to 0 with no free space, so a tall
                   radiograph stays scrollable from its top edge instead of overflowing above the box (§ 11). */
                <img
                  src={url}
                  alt={file.fileName}
                  onError={() => setRenderFailed(true)}
                  onLoad={(event) => {
                    const { naturalWidth, naturalHeight } = event.currentTarget
                    setStandInWasShrunk(Math.max(naturalWidth, naturalHeight) >= PREVIEW_EDGE)
                  }}
                  className="m-auto max-h-full max-w-full rounded-lg object-contain shadow-lg"
                />
              ) : render === "pdf" && url && !renderFailed ? (
                <PatientFilePdfPreview
                  previewUrl={url}
                  fileName={file.fileName}
                  onDeliver={() => onDownload(file)}
                />
              ) : render === "archive" && archive ? (
                <ArchiveContents listing={archive} />
              ) : (
                <UnavailablePreview file={file} reason={unavailable} onDownload={onDownload} />
              )}

            </div>

            {/* ⚠️ Outside the scrolling pane, so it cannot be scrolled away from the picture it qualifies. A
                DICOM rendered without this is a clinical image that looks authoritative and is not: the window
                is chosen, so a finding outside it is simply not in the picture. */}
            {advisory && render === "image" && !loading && (
              <p
                role="note"
                className="flex flex-shrink-0 items-start gap-2 border-t bg-warning-wash px-4 py-2 text-xs text-warning-ink md:px-6"
              >
                <TriangleAlert aria-hidden="true" className="mt-px h-3.5 w-3.5 flex-shrink-0" />
                <span>{advisory}</span>
              </p>
            )}

            {navigable && (
              /* ⚠️ A real row, not two `absolute` arrows over the document. Overlaid they pinned to the wrong
                 edge on a narrow sheet and collided in the middle, and an arrow the width of a thumb sitting on
                 a radiograph was never good on a phone either. One bar, identical at every width. */
              <div className="flex flex-shrink-0 items-center gap-2 border-t bg-muted/20 px-2 py-2">
                <NavArrow side="prev" disabled={!hasPrev} onClick={prev} />
                {patientId && files.length > 1 ? (
                  <Filmstrip preview={preview} patientId={patientId} />
                ) : (
                  <span className="flex-1 text-center text-sm tabular-nums text-muted-foreground">
                    {position} / {total}
                  </span>
                )}
                <NavArrow side="next" disabled={!hasNext} onClick={next} />
              </div>
            )}

            <DialogFooter className="flex-shrink-0 border-t bg-muted/40 px-4 py-3 md:px-6 md:py-4">
              <div className="flex w-full items-center justify-between gap-2">
                {/* « Fermer » stays at every width. The 16 px corner glyph is not a way out anybody finds on a
                    phone, and a full-screen sheet with no visible exit reads as the app having hung. */}
                <Button variant="outline" onClick={close} className="coarse:h-11 sm:min-w-[100px]">
                  Fermer
                </Button>
                <div className="flex min-w-0 flex-1 items-center justify-end gap-2">
                  {/* ⚠️ What is on screen is the stored stand-in, so the original stays reachable rather than
                      being quietly withheld (§ 0). It is a BUTTON and not automatic: the decode costs about
                      eleven seconds and several hundred megabytes on a large image, which is not something to
                      spend on every file somebody arrows past. */}
                  {/* ⚠️ **For a DICOM this REPLACES « Pleine résolution », rather than sitting beside it.**
                      The viewer opens the original at its own resolution and adds the window, the zoom and the
                      ruler, so the older control would be a second, weaker way to the same bytes — and a fifth
                      button in this row does not fit at 390 px, which is how « Télécharger » got clipped the
                      last time something was added to it. */}
                  {interactive && !loading ? (
                    <Button
                      onClick={() => setViewerOpen(true)}
                      aria-label={`Ouvrir ${file.fileName} dans la visionneuse ${
                        interactive === "dicom" ? "DICOM" : "3D"
                      }`}
                      className="shrink-0 gap-2 coarse:h-11"
                    >
                      {interactive === "dicom" ? (
                        <Contrast className="h-4 w-4 shrink-0" />
                      ) : (
                        <Box className="h-4 w-4 shrink-0" />
                      )}
                      {/* The label stays at EVERY width here, unlike its neighbours': for a `.dcm` or a `.stl`
                          this is the primary action, and an unlabelled glyph is not how a dentist discovers the
                          one control that turns a flat stand-in into something they can actually read. */}
                      Visionneuse
                    </Button>
                  ) : (
                    showFullResolution &&
                    !loading &&
                    standInWasShrunk && (
                      <Button
                        variant="ghost"
                        onClick={showFullResolution}
                        aria-label="Afficher en pleine résolution"
                        title="Afficher l’image d’origine plutôt que l’aperçu enregistré"
                        className="shrink-0 gap-2 coarse:h-11 coarse:min-w-11"
                      >
                        <Maximize2 className="h-4 w-4 shrink-0" />
                        {/* ⚠️ The label goes below `sm:`, exactly as « Supprimer »'s does. Measured at 390 px: with
                            this label visible the row squeezed « Télécharger » from 221 px to 56 px — a clipped
                            word on the primary way out, to make room for the secondary control. The `aria-label`
                            is not optional, because `hidden` removes the span from the accessibility tree too. */}
                        <span className="hidden sm:inline">Pleine résolution</span>
                      </Button>
                    )
                  )}
                  <Button
                    variant="outline"
                    onClick={() => onDownload(file)}
                    aria-label={`Télécharger ${file.fileName}`}
                    className={cn(
                      "min-w-0 gap-2 coarse:h-11 sm:flex-none",
                      // With « Visionneuse » carrying its label, this one gives its own up below `sm:` and keeps
                      // the icon plus the `aria-label` — the row cannot hold two labelled buttons at 390 px.
                      interactive ? "shrink-0" : "flex-1",
                    )}
                  >
                    <Download className="h-4 w-4 shrink-0" />
                    <span className={cn("truncate", interactive && "hidden sm:inline")}>Télécharger</span>
                  </Button>
                  {onDelete && (
                    <Button
                      variant="destructive"
                      onClick={() => {
                        close()
                        onDelete(file)
                      }}
                      aria-label={`Supprimer ${file.fileName}`}
                      className="shrink-0 gap-2 coarse:h-11 coarse:min-w-11"
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

            {/* ⚠️ Mounted for every file that has one and opened by the button, rather than mounted when
                opened: Radix renders nothing while `open` is false, and a component that appears at the same
                instant as its own `open` skips the enter transition — so the viewer would snap onto the screen.
                ⚠️ Only ONE of the two is ever mounted: they are separate dialogs, and mounting both would put
                two `Dialog`s on the same `viewerOpen`. */}
            {interactive === "dicom" && (
              <DicomViewer
                open={viewerOpen}
                onOpenChange={setViewerOpen}
                file={file}
                loadSource={loadSource}
                onDownload={onDownload}
              />
            )}
            {interactive === "mesh" && (
              <MeshViewer
                open={viewerOpen}
                onOpenChange={setViewerOpen}
                file={file}
                loadSource={loadSource}
                onDownload={onDownload}
              />
            )}
          </>
        )}
      </DialogContent>
    </Dialog>
  )
}

/** `size-11` painted rather than a `.touch-target` overlay — it sits in a row, where an overlay steals its
 *  neighbour's taps. */
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
      className="size-11 shrink-0 rounded-full"
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
}: {
  preview: FilePreview
  patientId: string
}) {
  const { file, files, open } = preview
  const active = useRef<HTMLButtonElement | null>(null)

  useEffect(() => {
    active.current?.scrollIntoView({ block: "nearest", inline: "nearest" })
  }, [file?.id])

  return (
    <div className="flex min-w-0 flex-1 items-center gap-2 overflow-x-auto">
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
              className="size-12 rounded-none coarse:size-14"
              iconClassName="h-5 w-5"
            />
          </button>
        )
      })}
    </div>
  )
}

/**
 * What is inside an archive. ⚠️ **A list, not a table** — a name and a size have nothing to compare across
 * columns, and § 6's two-tree hinge would buy nothing here while doubling the DOM inside a dialog that already
 * scrolls. Nothing is extracted: this is the archive's own index, read from its last few kilobytes.
 */
function ArchiveContents({ listing }: { listing: ArchiveListing }) {
  const files = listing.entries.filter((entry) => !entry.directory)

  return (
    <div className="flex min-h-0 w-full flex-col gap-3">
      <p className="text-sm text-muted-foreground" role="status">
        {files.length === 0
          ? "Cette archive ne contient aucun fichier."
          : `${files.length} fichier${files.length > 1 ? "s" : ""} dans cette archive`}
        {listing.truncated ? ` — les ${listing.totalEntries} éléments ne sont pas tous listés.` : ""}
      </p>

      {files.length > 0 && (
        <ul className="divide-y rounded-lg border bg-card">
          {files.map((entry) => (
            <li key={entry.name} className="flex items-center gap-3 px-3 py-2">
              <FileIcon className="h-4 w-4 shrink-0 text-muted-foreground" />
              {/* `min-w-0` on the flex child, or `break-all` never gets the chance: a flex item's default
                  `min-width: auto` refuses to shrink below its content and pushes the size out of the row. */}
              <span className="min-w-0 flex-1 break-all text-sm">{entry.name}</span>
              <span className="shrink-0 text-xs tabular-nums text-muted-foreground">
                {formatFileSize(entry.size)}
              </span>
            </li>
          ))}
        </ul>
      )}

      <p className="text-xs text-muted-foreground">
        Téléchargez l’archive pour ouvrir son contenu.
      </p>
    </div>
  )
}

/** What a file with nothing to show renders instead — the icon and the way through, never a broken image. */
function UnavailablePreview({
  file,
  reason,
  onDownload,
}: {
  file: PatientFileDto
  reason: PreviewUnavailable | null
  onDownload: (file: PatientFileDto) => void
}) {
  const Icon = fileIcon(file)

  // ⚠️ Three different facts, and one sentence for them was the defect. « Nothing can display this », « the
  // original is on another machine » and « something can display it, but not unasked » call for three
  // different actions, and only the first is a failure at all.
  const message =
    reason === "elsewhere"
      ? "L’original est conservé au cabinet et n’est pas disponible sur ce poste. Téléchargez-le depuis le poste qui le détient."
      : reason === "viewer-only"
        ? "Ce fichier est trop volumineux pour un aperçu automatique. Ouvrez la visionneuse pour l’afficher."
        : "Ce format ne s’affiche pas dans le navigateur. Téléchargez-le pour le consulter."

  return (
    <div className="m-auto flex flex-col items-center gap-3 p-8 text-center">
      <Icon className="h-16 w-16 text-muted-foreground" />
      <p className="max-w-sm text-sm text-muted-foreground">{message}</p>
      <Button variant="outline" onClick={() => onDownload(file)}>
        <Download className="mr-2 h-4 w-4" />
        {reason === "elsewhere"
          ? "Voir où il se trouve"
          : reason === "viewer-only"
            ? "Télécharger l’original"
            : "Télécharger pour consulter"}
      </Button>
    </div>
  )
}
