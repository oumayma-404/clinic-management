"use client"

import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import {
  ChevronLeft,
  ChevronRight,
  Contrast,
  Crosshair,
  Download,
  Loader2,
  Maximize,
  Sun,
  TriangleAlert,
  ZoomIn,
  ZoomOut,
} from "lucide-react"

import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { ModeSegmented } from "@/components/ui/mode-segmented"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { DICOM_RENDERED_VALUES_NOTE, DICOM_VIEWER_ADVISORY } from "@/lib/files/decoders"
import { openDicomStudy, type DicomFailure, type DicomFrame, type DicomStudy } from "@/lib/files/dicom/study"
import {
  defaultWindowFor,
  formatWindow,
  frameStats,
  lengthCaveat,
  presetsFor,
  type DicomPreset,
  type DicomWindow,
  type FrameStats,
} from "@/lib/files/dicom/window"
import { formatFileSize } from "@/lib/format"
import { cn } from "@/lib/utils"
import type { PatientFileDto } from "@/lib/api/types"

import { DicomViewerStage, type DicomTool, type Measurement } from "./dicom-viewer-stage"
import { SHORT_VIEWPORT_ASIDE, SHORT_VIEWPORT_ROW, SHORT_VIEWPORT_STRIP } from "./short-viewport"

/**
 * The DICOM study viewer — window/level, zoom, pan, frame scrolling and one measurement.
 *
 * <h3>Why it is its own surface and not a mode of the preview dialog</h3>
 *
 * ⚠️ **The preview dialog is shared by four formats and owns the horizontal swipe.** In it, dragging sideways
 * means « next file », which is exactly the gesture window/level needs — so the two cannot live in one element
 * without one of them becoming a modifier of the other. Its chrome is also fixed and appropriate for a
 * document: a header, a filmstrip and a footer, about 240 px of furniture around a picture that here wants the
 * whole viewport at the chair.
 *
 * <p>So this opens **over** it, from a « Visionneuse » button, and the dialog keeps its job: a `.dcm` still
 * paints its stored stand-in in about 300 ms and still walks the drawer with the arrows. Nothing about opening
 * a file got slower, and the study viewer is one tap away rather than in the way. ⚠️ While it is open the
 * dialog's own ←/→ handler is suspended — otherwise stepping a frame would also step the file underneath.</p>
 *
 * ⚠️ **It fetches the bytes itself, through the preview hook's `loadSource`.** The fast path never downloads the
 * original, so there is nothing to hand over; and the residency rule (a coffre original lives on the machine
 * that recorded it and asking the server for one can only 404) must not be written a second time here. A file
 * this machine does not hold says where it is, which is not a failure.
 */
export function DicomViewer({
  open,
  onOpenChange,
  file,
  loadSource,
  onDownload,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  file: PatientFileDto
  /** The open file's own bytes, from wherever they are. Null means « not on this machine ». */
  loadSource: () => Promise<Blob | null>
  onDownload: (file: PatientFileDto) => void
}) {
  const [phase, setPhase] = useState<"loading" | "ready" | "failed" | "elsewhere">("loading")
  const [study, setStudy] = useState<DicomStudy | null>(null)
  const [failure, setFailure] = useState<DicomFailure | null>(null)

  const [frameIndex, setFrameIndex] = useState(0)
  const [frame, setFrame] = useState<DicomFrame | null>(null)
  const [stats, setStats] = useState<FrameStats | null>(null)

  const [level, setLevel] = useState<DicomWindow>({ centre: 128, width: 256 })
  const [invert, setInvert] = useState(false)
  const [presetId, setPresetId] = useState<string>("")
  const [tool, setTool] = useState<DicomTool>("window")
  const [zoom, setZoom] = useState(1)
  const [fitToken, setFitToken] = useState(0)
  const [measurement, setMeasurement] = useState<Measurement | null>(null)

  /** Discards a study whose dialog has already been closed and reopened on another file. */
  const requestId = useRef(0)
  /** One histogram per frame, so scrubbing back and forth does not recount six megapixels each way. */
  const statsCache = useRef(new Map<number, FrameStats>())

  // ── open: fetch, parse, and hold the study for as long as the dialog is up ────────────────────────────────
  useEffect(() => {
    if (!open) return
    const token = ++requestId.current
    let opened: DicomStudy | null = null

    setPhase("loading")
    setStudy(null)
    setFailure(null)
    setFrame(null)
    setStats(null)
    setFrameIndex(0)
    setInvert(false)
    setZoom(1)
    setMeasurement(null)
    setTool("window")
    statsCache.current = new Map()

    void (async () => {
      try {
        const source = await loadSource()
        if (token !== requestId.current) return
        if (!source) {
          setPhase("elsewhere")
          return
        }

        const result = await openDicomStudy(source)
        if (token !== requestId.current) {
          if (result.ok) result.study.release()
          return
        }
        if (!result.ok) {
          setFailure(result.failure)
          setPhase("failed")
          return
        }

        opened = result.study
        setStudy(result.study)
        setPhase("ready")
      } catch {
        if (token !== requestId.current) return
        // Nothing here is a toast: the dialog IS the surface, and a sentence on it is what the reader needs.
        setFailure({ reason: "not-dicom" })
        setPhase("failed")
      }
    })()

    return () => {
      requestId.current++
      opened?.release()
    }
  }, [open, loadSource])

  // ── the current frame, its statistics, and the window it opens on ─────────────────────────────────────────
  useEffect(() => {
    if (!study) return
    const token = requestId.current
    let cancelled = false

    void (async () => {
      const next = await study.frame(frameIndex)
      if (cancelled || token !== requestId.current) return
      if (!next) {
        setFrame(null)
        return
      }

      let frameStatistics: FrameStats | null = null
      if (next.kind === "grey") {
        frameStatistics = statsCache.current.get(frameIndex) ?? frameStats(study, next)
        statsCache.current.set(frameIndex, frameStatistics)
      }

      setFrame(next)
      setStats(frameStatistics)

      // ⚠️ The window is set on the FIRST frame only and then persists across the study, which is what a
      // reader expects: scrubbing a series is comparing slices under one contrast, and re-deriving it per
      // frame would make every step change two things at once.
      if (frameIndex === 0 && next.kind === "grey" && frameStatistics) {
        setLevel(defaultWindowFor(study, next, frameStatistics))
        setPresetId(study.declaredWindows.length > 0 ? "file-0" : "full")
      }
    })()

    return () => {
      cancelled = true
    }
  }, [study, frameIndex])

  const greyFrame = frame?.kind === "grey" ? frame : null
  const presets: DicomPreset[] = useMemo(
    () => (study && greyFrame && stats ? presetsFor(study, greyFrame, stats) : []),
    [study, greyFrame, stats],
  )

  const stepFrame = useCallback(
    (delta: -1 | 1) => {
      if (!study) return
      setFrameIndex((current) => Math.min(study.frameCount - 1, Math.max(0, current + delta)))
    },
    [study],
  )

  const applyPreset = useCallback(
    (id: string) => {
      const preset = presets.find((candidate) => candidate.id === id)
      if (!preset) return
      setPresetId(id)
      setLevel(preset.window)
    },
    [presets],
  )

  /** A hand-set window is no longer a preset, and the Select must stop claiming it is. */
  const setWindowManually = useCallback((next: DicomWindow) => {
    setPresetId("")
    setLevel(next)
  }, [])

  const readout = study ? formatWindow(study, level) : null
  const caveat = study ? lengthCaveat(study) : null
  const multiFrame = (study?.frameCount ?? 1) > 1

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      {/* Full screen below `md:` (a study wants the device), and a large panel above it. ⚠️ `md:max-w-7xl` is
          prefixed, per § 4: an unprefixed width kills the mobile gutter and loses to the base's own clamp. */}
      <DialogContent
        mobile="sheet"
        className="gap-0 p-0 md:h-[92dvh] md:max-h-[92dvh] md:max-w-7xl md:overflow-hidden"
      >
        {/* `pe-12` clears the ✕ Radix pins to the corner. The header is deliberately thin: at a 380 px viewport
            height (a landscape phone) every row of chrome is taken out of the picture. */}
        <DialogHeader className="shrink-0 border-b bg-muted/40 px-3 pb-2 pe-12 pt-3 md:px-5 md:pb-3 md:pt-4">
          <DialogTitle className="truncate text-sm font-semibold md:text-base">{file.fileName}</DialogTitle>
          <DialogDescription className="text-2xs md:text-xs">
            Visionneuse DICOM · {formatFileSize(file.fileSize)}
            {study ? ` · ${study.frameCount > 1 ? `${study.frameCount} images` : "1 image"}` : ""}
            {study?.spacing
              ? study.spacing.source === "patient"
                ? " · échelle en millimètres"
                : " · échelle au capteur"
              : study
                ? " · sans échelle"
                : ""}
          </DialogDescription>
        </DialogHeader>

        {/* ⚠️ **A landscape phone has width to spare and no height at all, and stacking the chrome under the
            picture there left 78 px of picture out of 390 — measured, before this wrapper existed.** Header 77 +
            advisory 61 + controls 143 is 281 px of furniture in a 359 px dialog, which is not a viewer. So below
            560 px of viewport height the whole thing becomes a ROW: the chrome moves into a 240 px column beside
            the image and the stage gets 604 × 282 instead. It is a height query rather than a breakpoint because
            the trigger is genuinely the height — an iPad in landscape is 820 px tall and wants the stacked
            layout at the same 1180 px width a phone would not. */}
        <div className={cn("flex min-h-0 flex-1 flex-col", SHORT_VIEWPORT_ROW)}>
        {phase === "ready" && study && frame ? (
          <DicomViewerStage
            study={study}
            frame={frame}
            stats={stats}
            window={level}
            invert={invert}
            tool={tool}
            zoom={zoom}
            fitToken={fitToken}
            measurement={measurement}
            onWindowChange={setWindowManually}
            onZoomChange={setZoom}
            onMeasurementChange={setMeasurement}
            onStepFrame={stepFrame}
            onToggleInvert={() => setInvert((current) => !current)}
          />
        ) : (
          <div className="flex min-h-0 flex-1 items-center justify-center bg-muted/30 p-6">
            <div className="my-auto max-w-sm text-center">
              {phase === "loading" ? (
                <div role="status" className="flex flex-col items-center gap-3">
                  <Loader2 aria-hidden="true" className="h-8 w-8 animate-spin text-primary" />
                  <p className="text-sm text-muted-foreground">Lecture de l’étude…</p>
                  <p className="text-xs text-muted-foreground">
                    Un fichier DICOM est lu en entier avant de pouvoir être affiché.
                  </p>
                </div>
              ) : (
                <p role="status" className="text-sm text-muted-foreground">
                  {phase === "elsewhere"
                    ? "L’original est conservé au cabinet et n’est pas disponible sur ce poste. Ouvrez-le depuis le poste qui le détient."
                    : refusalSentence(failure)}
                </p>
              )}
            </div>
          </div>
        )}

        {/* Only while there is something to control: an empty 240 px column beside a refusal sentence would be
            240 px of nothing on the width that has least of it. */}
        {phase === "ready" && (
        <div className={cn("flex shrink-0 flex-col", SHORT_VIEWPORT_ASIDE)}>
        {/* ⚠️ Outside the stage, so it cannot be panned away from the picture it qualifies — the same rule the
            preview dialog's advisory follows, and it matters more here: the window is now the operator's own
            choice, so « I looked and saw nothing » is a statement about the window and not about the patient. */}
        {phase === "ready" && (
          <p
            role="note"
            className={cn(
              "flex shrink-0 items-start gap-2 border-t bg-warning-wash px-3 py-1.5 text-2xs text-warning-ink md:px-5 md:text-xs",
              // In the side column the aside owns the divider, so this must not draw a second one across it.
              "[@media(max-height:560px)]:border-t-0 [@media(max-height:560px)]:md:px-3",
            )}
          >
            <TriangleAlert aria-hidden="true" className="mt-0.5 h-3.5 w-3.5 shrink-0" />
            <span>
              {DICOM_VIEWER_ADVISORY}
              {study?.valuesAreRendered ? ` ${DICOM_RENDERED_VALUES_NOTE}` : ""}
              {/* ⚠️ The scale caveat appears while MEASURING, not permanently. It is a fact about the ruler, and
                  the ruler already states its own unit (« 288 px ») — so carried at all times it added two lines
                  to a strip that is 5 lines tall at 320 px, above the picture it is warning about. */}
              {caveat && (tool === "measure" || measurement) ? ` ${caveat}` : ""}
            </span>
          </p>
        )}

        {phase === "ready" && study && (
          /* ⚠️ The controls sit BELOW the picture, not above it. On the device this product is used on most — a
             tablet held at the chair — the bottom of the screen is what a thumb reaches without regripping, and
             a toolbar at the top of a full-screen sheet is the furthest point from it.
             `pb-[env(safe-area-inset-bottom)]`: `p-0` on the content removes the sheet variant's own home-
             indicator padding (tailwind-merge folds `pb-*` into `p-*`), and this row is the bottom-most thing on
             the screen, so it puts it back where it is now needed. */
          <div className="flex shrink-0 flex-col gap-2 border-t bg-muted/40 px-3 pb-[max(0.5rem,env(safe-area-inset-bottom,0px))] pt-2 md:px-5 md:pb-3 md:pt-3">
            <ModeSegmented
              value={tool}
              onChange={setTool}
              ariaLabel="Outil"
              options={[
                { value: "window", label: "Contraste" },
                { value: "pan", label: "Déplacer" },
                { value: "measure", label: "Mesurer" },
              ]}
            />

            {/* ⚠️ One scrolling strip rather than a wrapping grid (§ 11): at 320 px these controls cannot all
                fit, and wrapping them would grow the chrome by a row exactly where the picture is shortest.
                Nothing is hidden — the strip scrolls, so every control stays reachable (§ 0). */}
            <div className={cn("-mx-1 flex items-center gap-2 overflow-x-auto px-1 pb-0.5", SHORT_VIEWPORT_STRIP)}>
              <WindowControl
                study={study}
                level={level}
                readout={readout}
                disabled={!greyFrame}
                onChange={setWindowManually}
              />

              {presets.length > 0 && (
                <Select value={presetId} onValueChange={applyPreset}>
                  <SelectTrigger
                    aria-label="Préréglage de fenêtre"
                    className="h-9 w-[10.5rem] shrink-0 coarse:h-11 md:text-xs"
                  >
                    <SelectValue placeholder="Préréglage" />
                  </SelectTrigger>
                  <SelectContent>
                    {presets.map((preset) => (
                      <SelectItem key={preset.id} value={preset.id}>
                        {preset.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}

              <Button
                type="button"
                variant={invert ? "default" : "outline"}
                aria-pressed={invert}
                onClick={() => setInvert((current) => !current)}
                className="h-9 shrink-0 gap-1.5 coarse:h-11"
              >
                <Sun aria-hidden="true" className="h-4 w-4" />
                Inverser
              </Button>

              <div className="flex shrink-0 items-center gap-1">
                <Button
                  type="button"
                  variant="outline"
                  size="icon"
                  aria-label="Réduire le zoom"
                  onClick={() => setZoom((current) => Math.max(0.5, current / 1.25))}
                  className="size-9 shrink-0 coarse:size-11"
                >
                  <ZoomOut aria-hidden="true" className="h-4 w-4" />
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  size="icon"
                  aria-label="Augmenter le zoom"
                  onClick={() => setZoom((current) => Math.min(40, current * 1.25))}
                  className="size-9 shrink-0 coarse:size-11"
                >
                  <ZoomIn aria-hidden="true" className="h-4 w-4" />
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  aria-label="Ajuster l’image à l’écran"
                  onClick={() => {
                    setZoom(1)
                    setFitToken((current) => current + 1)
                  }}
                  className="h-9 shrink-0 gap-1.5 coarse:h-11"
                >
                  <Maximize aria-hidden="true" className="h-4 w-4" />
                  Ajuster
                </Button>
              </div>

              {measurement && (
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => setMeasurement(null)}
                  className="h-9 shrink-0 gap-1.5 coarse:h-11"
                >
                  <Crosshair aria-hidden="true" className="h-4 w-4" />
                  Effacer la mesure
                </Button>
              )}

              <Button
                type="button"
                variant="outline"
                onClick={() => onDownload(file)}
                className="h-9 shrink-0 gap-1.5 coarse:h-11"
              >
                <Download aria-hidden="true" className="h-4 w-4" />
                Original
              </Button>
            </div>

            {multiFrame && (
              <FrameScrubber
                index={frameIndex}
                count={study.frameCount}
                onChange={setFrameIndex}
                onStep={stepFrame}
              />
            )}
          </div>
        )}
        </div>
        )}
        </div>

        {phase !== "ready" && (
          <div className="flex shrink-0 items-center justify-between gap-2 border-t bg-muted/40 px-3 pb-[max(0.75rem,env(safe-area-inset-bottom,0px))] pt-3 md:px-5">
            {/* « Fermer » stays at every width: the 16 px corner glyph is not a way out anybody finds on a
                phone, and a full-screen sheet with no visible exit reads as the app having hung. */}
            <Button variant="outline" onClick={() => onOpenChange(false)} className="coarse:h-11">
              Fermer
            </Button>
            <Button variant="outline" onClick={() => onDownload(file)} className="gap-2 coarse:h-11">
              <Download aria-hidden="true" className="h-4 w-4 shrink-0" />
              <span className="truncate">Télécharger</span>
            </Button>
          </div>
        )}
      </DialogContent>
    </Dialog>
  )
}

/**
 * The window, readable and typable.
 *
 * ⚠️ **The numbers are here because the drag alone is not a capability for everyone** (§ 0). A pointer drag
 * cannot be performed with a keyboard, and « the contrast I had two minutes ago » is unrecoverable from a
 * gesture — so the centre and the width are two fields, and the trigger states them, with the unit that says
 * what they are readings *of*.
 */
function WindowControl({
  study,
  level,
  readout,
  disabled,
  onChange,
}: {
  study: DicomStudy
  level: DicomWindow
  readout: { readout: string; unit: string } | null
  disabled: boolean
  onChange: (next: DicomWindow) => void
}) {
  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button
          type="button"
          variant="outline"
          disabled={disabled}
          aria-label={
            disabled
              ? "Fenêtre — indisponible sur ce fichier"
              : `Fenêtre : ${readout?.readout ?? ""} (${readout?.unit ?? ""})`
          }
          className="h-9 shrink-0 gap-1.5 tabular-nums coarse:h-11"
        >
          <Contrast aria-hidden="true" className="h-4 w-4" />
          {disabled ? "Fenêtre" : (readout?.readout ?? "Fenêtre")}
        </Button>
      </PopoverTrigger>
      {/* § 10: never a fixed `w-80` in a 320 px viewport. */}
      <PopoverContent align="start" className="w-[min(18rem,calc(100vw-2rem))] space-y-3">
        <div>
          <p className="text-sm font-medium">Fenêtre</p>
          <p className="text-xs text-muted-foreground">
            {study.valuesAreRendered
              ? "Niveaux d’affichage — l’appareil a déjà fixé le contraste de ce fichier."
              : study.rescaleType === "HU"
                ? "En unités Hounsfield, telles que le fichier les déclare."
                : "En valeurs stockées par l’appareil — cette échelle n’est pas calibrée."}
          </p>
        </div>
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-1.5">
            <Label htmlFor="dicom-window-centre" className="text-xs">
              Centre
            </Label>
            <Input
              id="dicom-window-centre"
              type="number"
              inputMode="numeric"
              value={Math.round(level.centre)}
              onChange={(event) => {
                const next = Number(event.target.value)
                if (Number.isFinite(next)) onChange({ ...level, centre: next })
              }}
              className="md:text-sm"
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="dicom-window-width" className="text-xs">
              Largeur
            </Label>
            <Input
              id="dicom-window-width"
              type="number"
              inputMode="numeric"
              min={1}
              value={Math.round(level.width)}
              onChange={(event) => {
                const next = Number(event.target.value)
                if (Number.isFinite(next) && next >= 1) onChange({ ...level, width: next })
              }}
              className="md:text-sm"
            />
          </div>
        </div>
      </PopoverContent>
    </Popover>
  )
}

/**
 * Which image of the study is on screen.
 *
 * ⚠️ **A range input, not a drag on the picture.** Vertical drag is already the window's width, and there is no
 * third axis left on a one-finger gesture — but the deciding reason is that a slider is the only shape of this
 * control that a keyboard and a screen reader can both operate, and sixteen slices need to be *reachable*
 * rather than scrubbed past. `globals.css` gives every `input` a 44 px floor on a coarse pointer, so the track
 * is already thumb-sized without a class here.
 */
function FrameScrubber({
  index,
  count,
  onChange,
  onStep,
}: {
  index: number
  count: number
  onChange: (next: number) => void
  onStep: (delta: -1 | 1) => void
}) {
  return (
    <div className="flex items-center gap-2">
      <Button
        type="button"
        variant="outline"
        size="icon"
        aria-label="Image précédente"
        disabled={index <= 0}
        onClick={() => onStep(-1)}
        className="size-9 shrink-0 rounded-full coarse:size-11"
      >
        <ChevronLeft aria-hidden="true" className="h-4 w-4" />
      </Button>
      <input
        type="range"
        min={0}
        max={count - 1}
        step={1}
        value={index}
        aria-label="Image de l’étude"
        aria-valuetext={`Image ${index + 1} sur ${count}`}
        onChange={(event) => onChange(Number(event.target.value))}
        className="min-w-0 flex-1 accent-primary"
      />
      <Button
        type="button"
        variant="outline"
        size="icon"
        aria-label="Image suivante"
        disabled={index >= count - 1}
        onClick={() => onStep(1)}
        className="size-9 shrink-0 rounded-full coarse:size-11"
      >
        <ChevronRight aria-hidden="true" className="h-4 w-4" />
      </Button>
      <span className="shrink-0 text-xs tabular-nums text-muted-foreground">
        {index + 1} / {count}
      </span>
    </div>
  )
}

/**
 * Why a study will not open, in French, naming the format rather than shrugging at it.
 *
 * ⚠️ **This is the explicit answer for JPEG 2000**, the most likely unsupported syntax to arrive from a CBCT
 * export: it is not decoded, and it now says which format it is and what to do instead. « Ce format ne
 * s'affiche pas » was true and useless — a practice cannot act on it, and it reads the same as a bug.
 */
function refusalSentence(failure: DicomFailure | null): string {
  switch (failure?.reason) {
    case "unsupported-syntax":
      return failure.syntaxName
        ? `Les images de ce fichier sont compressées en ${failure.syntaxName}, un format que le navigateur ne sait pas décoder. Téléchargez l’original pour l’ouvrir dans un logiciel d’imagerie.`
        : "Les images de ce fichier utilisent une compression que le navigateur ne sait pas décoder. Téléchargez l’original pour l’ouvrir dans un logiciel d’imagerie."
    case "undecodable-frame":
      // ⚠️ A DIFFERENT sentence from the one above, and the probe over the samples is why: this file declares
      // a JPEG the browser normally handles, and the browser refused it anyway — almost always because it is
      // 12-bit, which no browser decodes. Saying « compressé en JPEG, que le navigateur ne sait pas décoder »
      // would be visibly false to anyone who has ever opened a photograph.
      return "Le navigateur n’a pas réussi à décoder les images de ce fichier. Un DICOM en JPEG 12 bits est le cas le plus courant : aucun navigateur ne le décode. Téléchargez l’original pour l’ouvrir dans un logiciel d’imagerie."
    case "too-large":
      return "Cette étude est trop volumineuse pour être lue dans le navigateur. Téléchargez-la pour l’ouvrir dans un logiciel d’imagerie."
    case "frame-too-large":
      return `Cette image fait ${failure.pixels.toLocaleString("fr-FR")} pixels, au-delà de ce que le navigateur peut afficher. Téléchargez l’original.`
    case "no-pixel-data":
      return "Ce fichier DICOM ne contient aucune image — seulement des informations. Rien ne peut être affiché."
    case "truncated":
      return "Ce fichier DICOM est incomplet : les images annoncées par son en-tête ne sont pas toutes là. Téléchargez-le pour le vérifier."
    default:
      return "Ce fichier n’a pas pu être lu comme un DICOM. Téléchargez-le pour l’ouvrir dans un logiciel d’imagerie."
  }
}
