"use client"

import { useCallback, useEffect, useId, useRef, useState } from "react"
import type { CSSProperties, KeyboardEvent as ReactKeyboardEvent, PointerEvent as ReactPointerEvent } from "react"
import { toast } from "sonner"
import { AlertTriangle, FileUp, Loader2 } from "lucide-react"

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
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Label } from "@/components/ui/label"
import { ModeSegmented } from "@/components/ui/mode-segmented"
import { Switch } from "@/components/ui/switch"
import { PatientFilePdfPreview } from "@/components/patient-file-pdf-preview"
import { clinicsApi, type ClinicLetterheadDto, type LetterheadBands } from "@/lib/api/clinics"
import { useConflict } from "@/lib/hooks/use-conflict"
import { useUploadPolicy } from "@/lib/hooks/use-upload-policy"
import { getErrorMessage } from "@/lib/errors"
import { downloadBlob } from "@/lib/download"
import { ACCEPTED_SOURCES, SHARP_SOURCE_WIDTH_PX, loadPageImage } from "@/lib/letterhead/page-image"
import { bandHeightMm, cutBands, suggestCuts, type BandCuts } from "@/lib/letterhead/bands"
import { cn } from "@/lib/utils"

type Step = "file" | "cut" | "preview"

// The door's cap when the policy read has not answered yet; the server re-checks either way.
const FALLBACK_MAX_BYTES = 5 * 1024 * 1024
// The closest two lines may come, and how far one keyboard press moves a line.
const MIN_SPAN = 0.05
const KEY_STEP = 0.0025
const KEY_STEP_LARGE = 0.02

interface LetterheadImportDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** The clinic row's version read with the current letterhead; round-tripped on save. */
  version: number
  onSaved: (letterhead: ClinicLetterheadDto) => void
}

/**
 * « Importer mon papier à en-tête » — the cabinet's paper, cut into two bands at true scale.
 *
 * Three steps, each one screen: the file (a PDF from the printer, Word saved as PDF, or a scan), the two lines that
 * say where the entête ends and the pied de page starts, and the REAL server-rendered ordonnance on those bands. The
 * source never leaves the browser; only the cut bands are sent.
 */
export function LetterheadImportDialog({ open, onOpenChange, version, onSaved }: LetterheadImportDialogProps) {
  const policy = useUploadPolicy("letterhead-band")
  const conflict = useConflict()
  const inputId = useId()
  const inputRef = useRef<HTMLInputElement>(null)

  const [step, setStep] = useState<Step>("file")
  const [page, setPage] = useState<HTMLCanvasElement | null>(null)
  const [sourceWidthPx, setSourceWidthPx] = useState<number | null>(null)
  const [fullPage, setFullPage] = useState(false)
  const [pageUrl, setPageUrl] = useState<string | null>(null)
  const [cuts, setCuts] = useState<BandCuts>({ headerEnd: 0.12, footerStart: null })
  const [bands, setBands] = useState<LetterheadBands | null>(null)
  const [previewUrl, setPreviewUrl] = useState<string | null>(null)
  const [previewPdf, setPreviewPdf] = useState<Blob | null>(null)
  const [busy, setBusy] = useState<"reading" | "preview" | "saving" | null>(null)
  const [currentVersion, setCurrentVersion] = useState(version)

  const resetConflict = conflict.reset

  // Every OPEN starts clean on the version the card read. Only the opening: `version` also moves on every `clinics`
  // broadcast, and resetting on it threw away a cut page — and the 409 « Recharger » that recovers from it.
  const wasOpen = useRef(false)
  useEffect(() => {
    if (open && !wasOpen.current) {
      setStep("file")
      setPage(null)
      setSourceWidthPx(null)
      setFullPage(false)
      setCuts({ headerEnd: 0.12, footerStart: null })
      setBands(null)
      setBusy(null)
      setCurrentVersion(version)
      resetConflict()
      // The card's version can be older than the row: a write that broadcasts nothing still moves it.
      void (async () => {
        try {
          setCurrentVersion((await clinicsApi.getLetterhead()).version)
        } catch {
          // Keep the card's version: a stale one still meets the 409 and its « Recharger ».
        }
      })()
    }
    wasOpen.current = open
  }, [open, version, resetConflict])

  // Object URLs are released with the state that held them.
  useEffect(() => () => { if (pageUrl) URL.revokeObjectURL(pageUrl) }, [pageUrl])
  useEffect(() => () => { if (previewUrl) URL.revokeObjectURL(previewUrl) }, [previewUrl])

  const pickFile = useCallback(async (file: File | undefined) => {
    if (!file) return
    conflict.setError(null)
    setBusy("reading")
    try {
      const { canvas, sourceWidthPx: width } = await loadPageImage(file)
      const display = await new Promise<Blob>((resolve, reject) =>
        canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error("L'image n'a pas pu être affichée."))), "image/jpeg", 0.85),
      )
      setPage(canvas)
      setSourceWidthPx(width)
      setPageUrl(URL.createObjectURL(display))
      const suggestion = suggestCuts(canvas)
      setCuts(suggestion.cuts)
      // A framed paper cut into bands alone prints with its frame broken, so it starts on « Page entière ».
      setFullPage(suggestion.framed)
      setStep("cut")
    } catch (error) {
      conflict.setError(getErrorMessage(error, "Ce fichier n'a pas pu être lu."))
    } finally {
      setBusy(null)
      if (inputRef.current) inputRef.current.value = ""
    }
  }, [conflict])

  const showPreview = useCallback(async () => {
    if (!page) return
    conflict.setError(null)
    setBusy("preview")
    try {
      const cut = await cutBands(page, cuts, policy?.maxBytes ?? FALLBACK_MAX_BYTES, fullPage)
      const pdf = await clinicsApi.previewLetterhead(cut)
      setBands(cut)
      setPreviewPdf(pdf)
      setPreviewUrl(URL.createObjectURL(pdf))
      setStep("preview")
    } catch (error) {
      // The server's own refusal (« Image trop petite… », « Remontez la ligne… ») is the sentence to show.
      conflict.setError(getErrorMessage(error, "L'aperçu n'a pas pu être généré."))
    } finally {
      setBusy(null)
    }
  }, [page, cuts, policy, conflict, fullPage])

  const save = useCallback(async () => {
    if (!bands) return
    conflict.clearMessage()
    setBusy("saving")
    try {
      const saved = await clinicsApi.saveLetterhead(bands, currentVersion)
      toast.success("En-tête enregistré")
      onSaved(saved)
      onOpenChange(false)
    } catch (error) {
      conflict.capture(error, "L'en-tête n'a pas pu être enregistré.")
    } finally {
      setBusy(null)
    }
  }, [bands, currentVersion, conflict, onSaved, onOpenChange])

  const reload = useCallback(async () => {
    try {
      const fresh = await clinicsApi.getLetterhead()
      setCurrentVersion(fresh.version)
      conflict.setError(null)
    } catch (error) {
      conflict.setError(getErrorMessage(error))
    }
  }, [conflict])

  const headerMm = page ? bandHeightMm(cuts.headerEnd, page) : 0
  const footerMm = page && cuts.footerStart !== null ? bandHeightMm(1 - cuts.footerStart, page) : 0
  const lowResolution = sourceWidthPx !== null && sourceWidthPx < SHARP_SOURCE_WIDTH_PX

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        mobile="sheet"
        className="md:max-w-3xl"
        // A page already cut is a few gestures of work; an outside tap must not throw it away.
        onInteractOutside={(event) => { if (page) event.preventDefault() }}
      >
        <DialogHeader>
          <DialogTitle>
            Importer mon papier à <span className="whitespace-nowrap">en-tête</span>
          </DialogTitle>
          <DialogDescription className="sr-only">
            Choisissez votre papier à en-tête, placez les deux lignes, vérifiez l'aperçu puis enregistrez.
          </DialogDescription>
          <StepTrail step={step} />
        </DialogHeader>

        <DialogBody className="space-y-4">
          {step === "file" && (
            <label
              htmlFor={inputId}
              className={cn(
                "flex min-h-56 cursor-pointer flex-col items-center justify-center gap-3 rounded-xl border-2 border-dashed",
                "border-border bg-muted/30 p-6 text-center transition-colors hover:border-primary/50 hover:bg-primary/5",
                busy === "reading" && "pointer-events-none opacity-70",
              )}
              onDragOver={(event) => event.preventDefault()}
              onDrop={(event) => {
                event.preventDefault()
                void pickFile(event.dataTransfer.files?.[0])
              }}
            >
              {busy === "reading" ? (
                <Loader2 className="size-8 animate-spin text-primary" aria-hidden="true" />
              ) : (
                <FileUp className="size-8 text-primary" aria-hidden="true" />
              )}
              <span className="text-base font-medium md:text-sm">
                {busy === "reading" ? "Lecture du fichier…" : "Choisir le fichier de mon en-tête"}
              </span>
              <span className="text-xs text-muted-foreground">PDF, PNG ou JPEG</span>
              <input
                ref={inputRef}
                id={inputId}
                type="file"
                accept={ACCEPTED_SOURCES}
                className="sr-only"
                onChange={(event) => void pickFile(event.target.files?.[0])}
              />
            </label>
          )}

          {step !== "file" && lowResolution && (
            <p
              role="status"
              className="flex items-start gap-2 rounded-lg border border-warning/40 bg-warning-wash px-3 py-2 text-sm text-warning-ink"
            >
              <AlertTriangle className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
              <span className="min-w-0">
                Image de faible résolution ({sourceWidthPx} px) : l&apos;impression sera moins nette qu&apos;avec le
                PDF de votre imprimeur ou un scan à 300 dpi.
              </span>
            </p>
          )}

          {step === "cut" && page && pageUrl && (
            <>
              <ModeSegmented
                ariaLabel="Ce que les documents reprennent de votre papier"
                className="sm:w-[26rem]"
                value={fullPage ? "page" : "bands"}
                onChange={(value) => setFullPage(value === "page")}
                options={[
                  { value: "bands", label: "En-tête et pied de page" },
                  { value: "page", label: "Page entière" },
                ]}
              />
              <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2">
                <p className="text-sm tabular-nums" role="status">
                  En-tête <strong>{Math.round(headerMm)} mm</strong>
                  {cuts.footerStart !== null && (
                    <> · Pied de page <strong>{Math.round(footerMm)} mm</strong></>
                  )}
                </p>
                <div className="flex items-center gap-2">
                  <Switch
                    id={`${inputId}-footer`}
                    checked={cuts.footerStart !== null}
                    onCheckedChange={(checked) =>
                      setCuts((current) => ({
                        ...current,
                        footerStart: checked ? Math.max(0.88, current.headerEnd + MIN_SPAN) : null,
                      }))
                    }
                  />
                  <Label htmlFor={`${inputId}-footer`}>Pied de page</Label>
                </div>
              </div>
              <CutCanvas
                pageUrl={pageUrl}
                aspect={page.width / page.height}
                cuts={cuts}
                onChange={setCuts}
                chromeRem={(lowResolution ? 25 : 21) + 4.5}
              />
            </>
          )}

          {step === "preview" && previewUrl && (
            <div className="h-[min(70dvh,52rem)]">
              <PatientFilePdfPreview
                previewUrl={previewUrl}
                fileName="apercu-en-tete.pdf"
                onDeliver={() => { if (previewPdf) void downloadBlob(previewPdf, "apercu-en-tete.pdf") }}
              />
            </div>
          )}

          <FormErrorBanner
            message={conflict.error}
            action={
              conflict.isConflict ? { label: "Recharger", onClick: () => void reload(), disabled: busy !== null } : undefined
            }
          />
        </DialogBody>

        <DialogFooter className="gap-2">
          {step === "file" && (
            <Button variant="outline" onClick={() => onOpenChange(false)}>
              Annuler
            </Button>
          )}
          {step === "cut" && (
            <>
              <Button variant="outline" onClick={() => setStep("file")} disabled={busy !== null}>
                Changer de fichier
              </Button>
              <Button onClick={() => void showPreview()} disabled={busy !== null}>
                {busy === "preview" && <Loader2 className="size-4 animate-spin" aria-hidden="true" />}
                Voir l'aperçu
              </Button>
            </>
          )}
          {step === "preview" && (
            <>
              <Button variant="outline" onClick={() => setStep("cut")} disabled={busy !== null}>
                Ajuster les lignes
              </Button>
              <Button onClick={() => void save()} disabled={busy !== null}>
                {busy === "saving" && <Loader2 className="size-4 animate-spin" aria-hidden="true" />}
                Enregistrer
              </Button>
            </>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

const STEPS: { key: Step; label: string }[] = [
  { key: "file", label: "Fichier" },
  { key: "cut", label: "Découpe" },
  { key: "preview", label: "Aperçu" },
]

function StepTrail({ step }: { step: Step }) {
  const current = STEPS.findIndex((s) => s.key === step)
  return (
    <ol className="flex flex-wrap items-center gap-1 text-xs text-muted-foreground sm:gap-2" aria-label="Étapes">
      {STEPS.map((s, index) => (
        <li key={s.key} className="flex items-center gap-1 sm:gap-2">
          {index > 0 && <span aria-hidden="true" className="hidden h-px w-4 bg-border sm:block" />}
          <span
            aria-current={index === current ? "step" : undefined}
            className={cn(
              "whitespace-nowrap rounded-full px-2 py-0.5",
              index === current && "bg-primary/10 font-medium text-primary",
              index < current && "text-foreground",
            )}
          >
            {index + 1}. {s.label}
          </span>
        </li>
      ))}
    </ol>
  )
}

/**
 * The page with its two lines. The bands are tinted, the document's own area stays white, and each line is a real
 * slider: drag it, or focus it and use the arrow keys (Maj + flèche for a larger step).
 */
function CutCanvas({
  pageUrl,
  aspect,
  cuts,
  onChange,
  chromeRem,
}: {
  pageUrl: string
  aspect: number
  cuts: BandCuts
  onChange: (cuts: BandCuts) => void
  /** The dialog's height around the page — title, steps, readout, buttons, and the warning when it shows. */
  chromeRem: number
}) {
  const frameRef = useRef<HTMLDivElement>(null)
  const dragging = useRef<"header" | "footer" | null>(null)

  const clamp = useCallback(
    (line: "header" | "footer", value: number): BandCuts => {
      if (line === "header") {
        const ceiling = (cuts.footerStart ?? 1) - MIN_SPAN
        return { ...cuts, headerEnd: Math.min(Math.max(value, 0.02), ceiling) }
      }
      return { ...cuts, footerStart: Math.max(Math.min(value, 0.98), cuts.headerEnd + MIN_SPAN) }
    },
    [cuts],
  )

  const fractionAt = (clientY: number) => {
    const rect = frameRef.current!.getBoundingClientRect()
    return (clientY - rect.top) / rect.height
  }

  const startDrag = (line: "header" | "footer") => (event: ReactPointerEvent<HTMLDivElement>) => {
    event.preventDefault()
    dragging.current = line
    event.currentTarget.setPointerCapture(event.pointerId)
  }

  const moveDrag = (event: ReactPointerEvent<HTMLDivElement>) => {
    if (!dragging.current) return
    onChange(clamp(dragging.current, fractionAt(event.clientY)))
  }

  const endDrag = () => {
    dragging.current = null
  }

  const keyMove = (line: "header" | "footer") => (event: ReactKeyboardEvent<HTMLDivElement>) => {
    const step = event.shiftKey ? KEY_STEP_LARGE : KEY_STEP
    const current = line === "header" ? cuts.headerEnd : cuts.footerStart
    if (current === null) return
    if (event.key === "ArrowUp" || event.key === "ArrowDown") {
      event.preventDefault()
      onChange(clamp(line, current + (event.key === "ArrowUp" ? -step : step)))
    }
  }

  return (
    // Sized by the HEIGHT left on screen, so the whole page — and both lines — are visible at once. The 12rem floor
    // keeps a landscape phone usable, where the body scrolls instead.
    // `--lh-h` is the height the page may take: the sheet is the whole screen on a phone, the dialog 85 % of it above.
    <div
      className="flex w-full justify-center [--lh-h:calc(100dvh_-_var(--lh-chrome))] md:[--lh-h:calc(85dvh_-_var(--lh-chrome)_+_7rem)]"
      style={{ "--lh-chrome": `${chromeRem}rem` } as CSSProperties}
    >
      <div
        ref={frameRef}
        className="relative touch-none select-none overflow-hidden rounded-md bg-white shadow-md ring-1 ring-border"
        style={{ aspectRatio: aspect, width: `min(100%, max(12rem, calc(var(--lh-h) * ${aspect})))` }}
        onPointerMove={moveDrag}
        onPointerUp={endDrag}
        onPointerCancel={endDrag}
      >
        <img src={pageUrl} alt="Votre papier à en-tête" className="absolute inset-0 size-full object-fill" draggable={false} />
        <BandShade top={0} bottom={cuts.headerEnd} label="En-tête" />
        {cuts.footerStart !== null && <BandShade top={cuts.footerStart} bottom={1} label="Pied de page" />}
        <CutLine
          at={cuts.headerEnd}
          label="Fin de l'en-tête"
          onPointerDown={startDrag("header")}
          onKeyDown={keyMove("header")}
        />
        {cuts.footerStart !== null && (
          <CutLine
            at={cuts.footerStart}
            label="Début du pied de page"
            onPointerDown={startDrag("footer")}
            onKeyDown={keyMove("footer")}
          />
        )}
      </div>
    </div>
  )
}

function BandShade({ top, bottom, label }: { top: number; bottom: number; label: string }) {
  return (
    <div
      aria-hidden="true"
      className="pointer-events-none absolute inset-x-0 bg-primary/10"
      style={{ top: `${top * 100}%`, height: `${(bottom - top) * 100}%` }}
    >
      <span className="absolute start-2 top-1 rounded bg-primary px-1.5 py-0.5 text-2xs font-medium text-primary-foreground">
        {label}
      </span>
    </div>
  )
}

function CutLine({
  at,
  label,
  onPointerDown,
  onKeyDown,
}: {
  at: number
  label: string
  onPointerDown: (event: ReactPointerEvent<HTMLDivElement>) => void
  onKeyDown: (event: ReactKeyboardEvent<HTMLDivElement>) => void
}) {
  return (
    <div
      role="slider"
      tabIndex={0}
      aria-label={label}
      aria-orientation="vertical"
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={Math.round(at * 100)}
      aria-valuetext={`${Math.round(at * 100)} % de la page`}
      className="group absolute inset-x-0 -translate-y-1/2 cursor-row-resize py-2 outline-none coarse:py-6"
      style={{ top: `${at * 100}%` }}
      onPointerDown={onPointerDown}
      onKeyDown={onKeyDown}
    >
      <div className="h-0.5 w-full bg-primary group-focus-visible:h-1" />
      <span
        aria-hidden="true"
        className="absolute end-2 top-1/2 flex h-6 -translate-y-1/2 items-center rounded-full bg-primary px-2 text-2xs font-medium text-primary-foreground shadow-sm ring-2 ring-white group-focus-visible:ring-primary/40 coarse:h-9"
      >
        {label}
      </span>
    </div>
  )
}
