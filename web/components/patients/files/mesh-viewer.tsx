"use client"

import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import { Box, Crosshair, Download, Loader2, Maximize, MapPin, Trash2, TriangleAlert } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { ModeSegmented } from "@/components/ui/mode-segmented"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import type { MeshAnnotation } from "@/lib/files/mesh/annotation"
import {
  formatExtent,
  inferUnit,
  MESH_UNITS,
  STRAIGHT_LINE_NOTE,
  unitCaveat,
  type MeshMeasurement,
  type MeshPoint,
  type MeshUnit,
} from "@/lib/files/mesh/measure"
import { openMeshModel, type MeshFailure, type MeshModel } from "@/lib/files/mesh/model"
import { MESH_ORIENTATION_NOTE, MESH_VIEWS, type MeshShading, type MeshView } from "@/lib/files/mesh/scene"
import { formatFileSize } from "@/lib/format"
import { cn } from "@/lib/utils"
import type { PatientFileDto } from "@/lib/api/types"

import { MeshViewerStage, type MeshTool } from "./mesh-viewer-stage"
import { useMeshAnnotations } from "./use-mesh-annotations"
import { SHORT_VIEWPORT_ASIDE, SHORT_VIEWPORT_ROW, SHORT_VIEWPORT_STRIP } from "./short-viewport"

const SHADINGS: readonly { value: MeshShading; label: string }[] = [
  { value: "solid", label: "Plein" },
  { value: "both", label: "Plein + maillage" },
  { value: "wireframe", label: "Maillage" },
]

const UNIT_NAMES: Readonly<Record<MeshUnit, string>> = {
  mm: "millimètres",
  cm: "centimètres",
  m: "mètres",
  in: "pouces",
}

/**
 * The 3D model viewer — orbit, pan, zoom, seven framings, a straight-line measurement and surface markers.
 *
 * <h3>Why it is its own surface and not a mode of the preview dialog</h3>
 *
 * <p>⚠️ **The preview dialog owns the horizontal swipe**, where dragging sideways means « next file » — which is
 * exactly the gesture orbiting needs. The two cannot share one element without one becoming a modifier of the
 * other. This is the same reasoning that put the DICOM study viewer on its own surface, and the same answer:
 * this opens **over** the dialog from a « Visionneuse 3D » button, and the dialog keeps its job.</p>
 *
 * <p>⚠️ **It fetches the bytes itself, through the preview hook's `loadSource`.** A `.stl` shows its stored
 * stand-in first — the thumbnail rendered on the way up — so opening a file is fast whether or not anybody
 * asks for the model; and the residency rule (a coffre original lives on the machine that recorded it, so
 * asking the server for one can only 404) must not be written a second time here.</p>
 *
 * <h3>⚠️ What this viewer must never do: state a length without stating its unit</h3>
 *
 * <p>None of these three formats records one. See `lib/files/mesh/measure` — the short version is that the unit
 * is a control, the model's own dimensions are on screen at all times so the reader can check the assumption
 * against the thing itself, and every measurement carries a sentence saying which unit was assumed and how well
 * the model's size supports it.</p>
 */
export function MeshViewer({
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
  const [phase, setPhase] = useState<"loading" | "ready" | "failed" | "elsewhere" | "no-webgl">("loading")
  const [model, setModel] = useState<MeshModel | null>(null)
  const [failure, setFailure] = useState<MeshFailure | null>(null)

  const [view, setView] = useState<MeshView>("iso")
  const [shading, setShading] = useState<MeshShading>("solid")
  const [tool, setTool] = useState<MeshTool>("orbit")
  const [unit, setUnit] = useState<MeshUnit>("mm")
  const [fitToken, setFitToken] = useState(0)

  const [measurement, setMeasurement] = useState<MeshMeasurement | null>(null)

  /**
   * ⚠️ **The markers are the one thing on this surface that OUTLIVES the dialog**, so they are the one
   * thing that talks to the server. A measurement is a question asked and answered while looking; a marker is
   * a note left for whoever opens the model next — including the laboratory. Keeping them in `useState`
   * beside the measurement would have made « close » mean « discard », which is not what any of the
   * controls look like they do.
   */
  const markers = useMeshAnnotations(file.patientId, file.id, open)

  // The model outlives the render that produced it and holds GPU buffers, so the release has to survive a
  // re-render to be callable from the teardown.
  const openModel = useRef<MeshModel | null>(null)

  useEffect(() => {
    if (!open) return

    let cancelled = false
    setPhase("loading")
    setFailure(null)
    setMeasurement(null)
    setTool("orbit")
    setView("iso")

    const load = async () => {
      const source = await loadSource()
      if (cancelled) return
      if (!source) {
        setPhase("elsewhere")
        return
      }

      const result = await openMeshModel(source, file.fileName)
      if (cancelled) {
        if (result.ok) result.model.release()
        return
      }

      if (!result.ok) {
        setFailure(result.failure)
        setPhase("failed")
        return
      }

      openModel.current = result.model
      setModel(result.model)
      // ⚠️ The default unit is derived from the model, not fixed at « mm » — a file whose box is only plausible
      // in metres opens in metres, and the caveat then says so rather than showing a 0,1 mm arch.
      setUnit(inferUnit(result.model.bounds).unit)
      setPhase("ready")
    }

    void load()

    return () => {
      cancelled = true
      openModel.current?.release()
      openModel.current = null
      setModel(null)
    }
  }, [open, file.fileName, loadSource])

  const hint = useMemo(() => (model ? inferUnit(model.bounds) : null), [model])

  const onUnavailable = useCallback(() => setPhase("no-webgl"), [])

  const measuring = tool === "measure" || measurement !== null

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      {/* Full screen below `md:` (a model wants the device), a large panel above it. `md:max-w-7xl` is prefixed
          per § 4: an unprefixed width kills the mobile gutter and loses to the base's own clamp. */}
      <DialogContent
        mobile="sheet"
        className="gap-0 p-0 md:h-[92dvh] md:max-h-[92dvh] md:max-w-7xl md:overflow-hidden"
      >
        <DialogHeader className="shrink-0 border-b bg-muted/40 px-3 pb-2 pe-12 pt-3 md:px-5 md:pb-3 md:pt-4">
          <DialogTitle className="truncate text-sm font-semibold md:text-base">{file.fileName}</DialogTitle>
          <DialogDescription className="text-2xs md:text-xs">
            Visionneuse 3D · {formatFileSize(file.fileSize)}
            {model ? ` · ${model.triangles.toLocaleString("fr-FR")} triangles` : ""}
            {model && model.parts > 1 ? ` · ${model.parts} objets` : ""}
          </DialogDescription>
        </DialogHeader>

        <div className={cn("flex min-h-0 flex-1 flex-col", SHORT_VIEWPORT_ROW)}>
          {phase === "ready" && model ? (
            <MeshViewerStage
              model={model}
              view={view}
              shading={shading}
              tool={tool}
              unit={unit}
              measurement={measurement}
              onMeasurementChange={setMeasurement}
              annotations={markers.annotations}
              onAnnotationPlaced={markers.place}
              onAnnotationSelected={markers.select}
              selectedAnnotationId={markers.selectedId}
              fitToken={fitToken}
              onUnavailable={onUnavailable}
            />
          ) : (
            <div className="flex min-h-0 flex-1 items-center justify-center bg-muted/30 p-6">
              <div className="my-auto max-w-sm text-center">
                {phase === "loading" ? (
                  <div role="status" className="flex flex-col items-center gap-3">
                    <Loader2 aria-hidden="true" className="h-8 w-8 animate-spin text-primary" />
                    <p className="text-sm text-muted-foreground">Lecture du modèle…</p>
                    <p className="text-xs text-muted-foreground">
                      Un modèle 3D est lu en entier avant de pouvoir être affiché.
                    </p>
                  </div>
                ) : (
                  <p role="status" className="text-sm text-muted-foreground">
                    {phase === "elsewhere"
                      ? "L’original est conservé au cabinet et n’est pas disponible sur ce poste. Ouvrez-le depuis le poste qui le détient."
                      : phase === "no-webgl"
                        ? "Ce navigateur ne peut pas afficher de 3D sur ce poste. Téléchargez le fichier pour l’ouvrir dans un autre logiciel."
                        : refusalSentence(failure)}
                  </p>
                )}
              </div>
            </div>
          )}

          {phase === "ready" && model && hint && (
            <div className={cn("flex shrink-0 flex-col", SHORT_VIEWPORT_ASIDE)}>
              {/* ⚠️ Outside the stage, so it cannot be orbited away from the model it qualifies. The extent is
                  first and in its own line because it is the load-bearing part: a reader who sees
                  « 62,1 × 48,3 × 21,0 mm » knows the unit is right, and no sentence beats that. */}
              <div
                role="note"
                className={cn(
                  "shrink-0 space-y-1 border-t bg-warning-wash px-3 py-1.5 text-2xs text-warning-ink md:px-5 md:text-xs",
                  "[@media(max-height:560px)]:border-t-0 [@media(max-height:560px)]:md:px-3",
                )}
              >
                <p className="flex items-start gap-2">
                  <Box aria-hidden="true" className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                  <span>
                    Encombrement&nbsp;: <b className="tabular-nums">{formatExtent(model.bounds, unit)}</b>
                  </span>
                </p>
                {/* ⚠️ The unit caveat appears while MEASURING, not permanently — it is a fact about the ruler,
                    and the extent line above already states the unit at all times. Carried always, it added two
                    lines to a strip that is five tall at 320 px. The orientation note is permanent, because the
                    view buttons are always there to be misread. */}
                <p className="flex items-start gap-2">
                  <TriangleAlert aria-hidden="true" className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                  <span>
                    {MESH_ORIENTATION_NOTE}
                    {measuring ? ` ${unitCaveat(hint, unit)} ${STRAIGHT_LINE_NOTE}` : ""}
                    {model.computedNormals
                      ? " Ce fichier ne portait pas de normales : l’ombrage est lissé, donc une arête vive peut paraître arrondie."
                      : ""}
                  </span>
                </p>
              </div>

              <div className="flex shrink-0 flex-col gap-2 border-t bg-muted/40 px-3 pb-[max(0.5rem,env(safe-area-inset-bottom,0px))] pt-2 md:px-5 md:pb-3 md:pt-3">
                <ModeSegmented
                  value={tool}
                  onChange={setTool}
                  ariaLabel="Outil"
                  options={[
                    { value: "orbit", label: "Tourner" },
                    { value: "measure", label: "Mesurer" },
                    { value: "annotate", label: "Repère" },
                  ]}
                />

                {/* ⚠️ One scrolling strip rather than a wrapping grid (§ 11): at 320 px these cannot all fit, and
                    wrapping would grow the chrome by a row exactly where the model is shortest. Nothing is
                    hidden — the strip scrolls, so every control stays reachable (§ 0). */}
                <div className={cn("-mx-1 flex items-center gap-2 overflow-x-auto px-1 pb-0.5", SHORT_VIEWPORT_STRIP)}>
                  <Select value={view} onValueChange={(next) => setView(next as MeshView)}>
                    <SelectTrigger aria-label="Vue" className="h-9 w-[7.5rem] shrink-0 coarse:h-11 md:text-xs">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {MESH_VIEWS.map((option) => (
                        <SelectItem key={option.id} value={option.id}>
                          {option.label}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>

                  <Select value={shading} onValueChange={(next) => setShading(next as MeshShading)}>
                    <SelectTrigger aria-label="Ombrage" className="h-9 w-[10.5rem] shrink-0 coarse:h-11 md:text-xs">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {SHADINGS.map((option) => (
                        <SelectItem key={option.value} value={option.value}>
                          {option.label}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>

                  <Select value={unit} onValueChange={(next) => setUnit(next as MeshUnit)}>
                    <SelectTrigger aria-label="Unité de mesure" className="h-9 w-[8.5rem] shrink-0 coarse:h-11 md:text-xs">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {MESH_UNITS.map((candidate) => (
                        <SelectItem key={candidate} value={candidate}>
                          {UNIT_NAMES[candidate]}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>

                  <Button
                    variant="outline"
                    onClick={() => setFitToken((current) => current + 1)}
                    className="h-9 shrink-0 gap-1.5 coarse:h-11 md:text-xs"
                  >
                    <Maximize aria-hidden="true" className="h-4 w-4 shrink-0" />
                    Ajuster
                  </Button>

                  {measurement && (
                    <Button
                      variant="outline"
                      onClick={() => setMeasurement(null)}
                      className="h-9 shrink-0 gap-1.5 coarse:h-11 md:text-xs"
                    >
                      <Crosshair aria-hidden="true" className="h-4 w-4 shrink-0" />
                      Effacer la mesure
                    </Button>
                  )}

                  <Button
                    variant="outline"
                    onClick={() => onDownload(file)}
                    aria-label={`Télécharger ${file.fileName}`}
                    className="h-9 shrink-0 gap-1.5 coarse:h-11 md:text-xs"
                  >
                    <Download aria-hidden="true" className="h-4 w-4 shrink-0" />
                    Télécharger
                  </Button>
                </div>

                {tool !== "orbit" && (
                  <p className="text-2xs text-muted-foreground md:text-xs">
                    {/* The one thing a reader cannot discover: that a DRAG still turns the model, so arming a
                        tool has not taken the camera away from them. */}
                    Touchez la surface pour {tool === "measure" ? "poser un point de mesure" : "poser un repère"} ;
                    faites glisser pour tourner le modèle.
                  </p>
                )}

                {markers.annotations.length > 0 && (
                  <ul className="max-h-32 space-y-1 overflow-y-auto">
                    {markers.annotations.map((annotation) => (
                      <li key={annotation.id} className="flex items-center gap-1.5">
                        <MapPin
                          aria-hidden="true"
                          className={cn(
                            "h-3.5 w-3.5 shrink-0",
                            annotation.id === markers.selectedId ? "text-amber-500" : "text-rose-500",
                          )}
                        />
                        <Input
                          value={annotation.label}
                          onChange={(event) => markers.rename(annotation.id, event.target.value)}
                          onFocus={() => markers.select(annotation.id)}
                          aria-label="Nom du repère"
                          className="h-8 min-w-0 flex-1 text-xs coarse:h-11"
                        />
                        <Button
                          variant="ghost"
                          onClick={() => markers.remove(annotation.id)}
                          aria-label={`Supprimer le repère ${annotation.label}`}
                          className="size-8 shrink-0 p-0 coarse:size-11"
                        >
                          <Trash2 aria-hidden="true" className="h-4 w-4" />
                        </Button>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            </div>
          )}
        </div>
      </DialogContent>
    </Dialog>
  )
}

/** Why nothing is on screen, in French, naming the limit rather than saying « erreur ». */
function refusalSentence(failure: MeshFailure | null): string {
  switch (failure?.reason) {
    case "too-large":
      return `Ce fichier dépasse ${formatFileSize(failure.limitBytes)}, la taille au-delà de laquelle un modèle ne peut pas être affiché ici. Téléchargez-le pour l’ouvrir dans un autre logiciel.`
    case "too-complex":
      return `Ce modèle contient ${failure.triangles.toLocaleString("fr-FR")} triangles, au-delà des ${failure.limitTriangles.toLocaleString("fr-FR")} que cette visionneuse peut afficher. Téléchargez-le pour l’ouvrir dans un autre logiciel.`
    case "empty":
      return "Ce fichier ne contient aucune surface à afficher : il est peut-être incomplet, ou ne contient que des points."
    case "not-finite":
      return "Ce fichier contient des coordonnées invalides et ne peut pas être affiché. Il est probablement corrompu."
    default:
      return "Ce fichier n’a pas pu être lu comme un modèle 3D."
  }
}
