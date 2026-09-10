"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import { Download, FileEdit, Loader2, Printer } from "lucide-react"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { PatientFilePdfPreview } from "@/components/patient-file-pdf-preview"
import { pdfSourceUrl } from "@/lib/pdf-sources"
import { medicalDocumentsApi } from "@/lib/api/medical-documents"
import type { MedicalDocumentDto } from "@/lib/api/types"
import { downloadBlob } from "@/lib/download"
import { documentTypeLabel, isExamenLine, type PrescriptionLine } from "@/lib/documents"
import { getErrorMessage, showErrorToast } from "@/lib/errors"
import { formatDate } from "@/lib/format"

/**
 * « Voir le document » — a document read where it is referred to, instead of a page you navigate away to.
 *
 * <h4>Why this exists</h4>
 * <p>Before it, the <b>only</b> surface in the app that rendered a saved `MedicalDocument` was the full editor
 * page `/documents/{type}?id=…`. So « Ouvrir l'ordonnance » on a séance row did a `router.push`: you left the
 * patient file to look at one sheet, and from inside the fiche modal there was no door at all — the section
 * promised in prose that an ordonnance would be emitted and gave you no way to see it. A dentist who has just
 * typed an antibiotic wants to read the paper before handing it over, at the chair, without losing the record
 * they are in the middle of.</p>
 *
 * <h4>Two sources, and the difference is stated rather than hidden</h4>
 * <ul>
 *   <li><b>`saved`</b> — the document on file. Rendered server-side from its stored `contentJson`
 *       ({@link medicalDocumentsApi.getPdf}), so it is byte-for-byte what the e-mail attaches and the PDF job
 *       stores. Carries Imprimer / Télécharger / Modifier.</li>
 *   <li><b>`apercu`</b> — the sheet the fiche is <i>about to</i> emit, composed by the server through the
 *       emitter's own path. It exists because on a first save there is no document yet. ⚠️ It deliberately
 *       carries <b>no</b> Imprimer and no Télécharger: a printed ordonnance for an unsaved séance is a
 *       legal paper with no record behind it, and the dialog says so in one line rather than refusing
 *       silently.</li>
 * </ul>
 *
 * <h4>⚠️ It shows the PDF, not a re-rendering of it</h4>
 * <p>The document editor has an on-screen A4 block built from its own form state, and lifting that here was
 * the obvious move. It would have been a <b>second renderer of a legal document</b> — the drift this feature
 * already carries three guards against, one per copy of the printed médicament line. The bytes a pharmacist
 * reads are the only honest preview, so this frames them. It also means print is `contentWindow.print()` on
 * the real PDF rather than a cloned DOM subtree.</p>
 *
 * <p>⚠️ <b>« Modifier » routes by `dentalRecordId`, and that is a correctness fix, not a nicety.</b> A
 * document a fiche owns is recomposed from the section on the fiche's next save, so editing it in the
 * standalone editor is work that will be silently overwritten. When a fiche owns it, this offers « Modifier
 * dans la fiche » (and says which séance); otherwise the editor, as before.</p>
 */
export type DocumentPreviewTarget =
  | { mode: "saved"; documentId: string }
  | {
      mode: "apercu"
      /** `PRESCRIPTION_KINDS.medicament` | `.examen` — which of the two sheets. */
      kind: string
      patientId: string
      doctorId?: string | null
      /** The séance's date, ISO — the document's date. */
      interventionDate: string
      lines: PrescriptionLine[]
    }

interface DocumentPreviewDialogProps {
  target: DocumentPreviewTarget | null
  onClose: () => void
  /**
   * Open the fiche that owns this document, for « Modifier dans la fiche ». Omitted where the host cannot do
   * it (inside the fiche modal itself, where you are already there) — the control is then simply absent.
   */
  onEditInFiche?: (dentalRecordId: string) => void
  /** Open the standalone editor, for a document no fiche owns. Omitted hides the control. */
  onEditInEditor?: (document: MedicalDocumentDto) => void
}

/** What the header calls an aperçu, which has no stored `documentType` to look up yet. */
const apercuLabel = (kind: string) => (isExamenLine(kind) ? "Demande d'examens" : "Ordonnance")

export function DocumentPreviewDialog({
  target,
  onClose,
  onEditInFiche,
  onEditInEditor,
}: DocumentPreviewDialogProps) {
  const [pdfUrl, setPdfUrl] = useState<string | null>(null)
  const [document, setDocument] = useState<MedicalDocumentDto | null>(null)
  const [loading, setLoading] = useState(false)
  const [failure, setFailure] = useState<string | null>(null)
  const [reload, setReload] = useState(0)

  /** The rendered bytes, kept for « Télécharger » and for the coarse-pointer hand-off. */
  const blobRef = useRef<Blob | null>(null)
  const frameRef = useRef<HTMLIFrameElement | null>(null)
  /** The object URL currently painted, so it is revoked exactly once and never while still in the frame. */
  const urlRef = useRef<string | null>(null)

  const releaseUrl = useCallback(() => {
    if (urlRef.current) {
      URL.revokeObjectURL(urlRef.current)
      urlRef.current = null
    }
  }, [])

  useEffect(() => {
    if (!target) {
      return
    }

    let cancelled = false
    setLoading(true)
    setFailure(null)

    const run = async () => {
      try {
        if (target.mode === "saved") {
          // Two reads in parallel: the bytes to paint, and the document itself for the header and — the part
          // that matters — `dentalRecordId`, which decides where « Modifier » goes.
          const [blob, dto] = await Promise.all([
            medicalDocumentsApi.getPdf(target.documentId),
            medicalDocumentsApi.get(target.documentId),
          ])
          if (cancelled) return
          blobRef.current = blob
          setDocument(dto)
          releaseUrl()
          const url = URL.createObjectURL(blob)
          urlRef.current = url
          setPdfUrl(url)
        } else {
          const blob = await medicalDocumentsApi.previewFicheOrdonnance({
            patientId: target.patientId,
            doctorId: target.doctorId ?? undefined,
            interventionDate: target.interventionDate,
            kind: target.kind,
            // Every line, both kinds — the server splits them, so an aperçu cannot sort a line differently
            // from the document the save writes.
            prescription: {
              lines: target.lines.map((line) => ({ ...line, name: line.name.trim() })),
            },
          })
          if (cancelled) return
          blobRef.current = blob
          setDocument(null)
          releaseUrl()
          const url = URL.createObjectURL(blob)
          urlRef.current = url
          setPdfUrl(url)
        }
      } catch (error) {
        if (cancelled) return
        // Named, never a generic « erreur de chargement »: the server's own sentence tells a dentist whether
        // nothing was prescribed of this kind or the renderer is missing a form asset.
        setFailure(getErrorMessage(error))
      } finally {
        if (!cancelled) setLoading(false)
      }
    }

    void run()
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [target, reload, releaseUrl])

  // Revoked on close rather than on every render: the frame is still displaying the URL until it unmounts.
  useEffect(() => {
    if (target) return
    releaseUrl()
    setPdfUrl(null)
    setDocument(null)
    setFailure(null)
    blobRef.current = null
  }, [target, releaseUrl])

  useEffect(() => () => releaseUrl(), [releaseUrl])

  const fileName = document
    ? `${documentTypeLabel(document.documentType)}.pdf`
    : target?.mode === "apercu"
      ? `${apercuLabel(target.kind)}.pdf`
      : "document.pdf"

  const deliver = useCallback(async () => {
    if (!blobRef.current) return
    try {
      await downloadBlob(blobRef.current, fileName)
    } catch (error) {
      showErrorToast(error, "Le téléchargement a échoué.")
    }
  }, [fileName])

  /**
   * ⚠️ The frame is printed only when it is really rendered. Below a coarse pointer `PatientFilePdfPreview`
   * hides it in CSS and shows the hand-off instead, so the ref is non-null and the window unprintable — the
   * file is delivered to the platform viewer, which is where it can be printed on a phone anyway.
   */
  const print = useCallback(() => {
    const frame = frameRef.current
    if (frame && frame.offsetParent !== null && frame.contentWindow) {
      frame.contentWindow.focus()
      frame.contentWindow.print()
      return
    }
    void deliver()
  }, [deliver])

  const isApercu = target?.mode === "apercu"
  const title = document
    ? documentTypeLabel(document.documentType)
    : target?.mode === "apercu"
      ? apercuLabel(target.kind)
      : ""

  return (
    <>
      <Dialog open={!!target} onOpenChange={(open) => { if (!open) onClose() }}>
        {/* The one dialog that wants the whole screen on a phone: it is showing a document. Same fixed height
            as the patient-file preview, for its stated reason — the document is fitted to the frame, never
            the frame to the document. */}
        <DialogContent
          mobile="sheet"
          className="gap-0 p-0 md:h-[90dvh] md:max-h-[90dvh] md:max-w-5xl md:overflow-hidden"
        >
          {/* `pe-12` clears the close control Radix pins to the corner. */}
          <DialogHeader className="flex-shrink-0 border-b bg-muted/40 px-4 pb-3 pe-12 pt-4 md:px-6 md:pb-4 md:pe-12 md:pt-6">
            <DialogTitle className="truncate text-base font-semibold md:text-lg">
              {isApercu ? `Aperçu — ${title}` : title}
            </DialogTitle>
            <DialogDescription className="mt-1 text-xs md:text-sm">
              {isApercu
                ? "Le document tel qu'il sera imprimé. Rien n'est encore enregistré."
                : document
                  ? `${document.patientName} • ${formatDate(document.documentDate)}`
                  : "Chargement…"}
            </DialogDescription>
          </DialogHeader>

          <div className="relative flex min-h-0 flex-1 overflow-auto bg-muted/30 p-0 md:p-3">
            {loading ? (
              <div className="m-auto flex flex-col items-center gap-3 px-6 text-center" role="status">
                <Loader2 className="h-8 w-8 animate-spin text-primary" />
                <p className="text-sm text-muted-foreground">Composition du document…</p>
              </div>
            ) : failure ? (
              <div className="m-auto w-full max-w-lg px-4">
                <LoadFailureNotice message={failure} onRetry={() => setReload((n) => n + 1)} />
              </div>
            ) : pdfUrl ? (
              <PatientFilePdfPreview
                /*
                 * ⚠️ A **named** URL for a saved document, the blob only for an aperçu.
                 *
                 * Chrome's viewer names its toolbar save from the URL's last path segment, so a blob URL
                 * saves as a bare UUID with no extension — the whole of `pdfSourceUrl`. An aperçu has no
                 * such URL and cannot get one: it is composed by a POST and persists nothing, so there is no
                 * id to name. That is the right trade rather than a gap — an aperçu deliberately carries no
                 * Télécharger and no Imprimer, because a printed ordonnance for an unsaved séance is a legal
                 * paper with no record behind it.
                 */
                previewUrl={
                  target?.mode === 'saved' ? pdfSourceUrl('document', target.documentId, fileName) : pdfUrl
                }
                fileName={fileName}
                onDeliver={deliver}
                frameRef={frameRef}
              />
            ) : null}
          </div>

          <DialogFooter className="flex-shrink-0 gap-2 border-t bg-background px-4 py-3 md:px-6">
            {isApercu ? (
              /*
               * ⚠️ No Imprimer and no Télécharger here, on purpose. Handing a patient a paper for a séance that
               * was never saved leaves a prescription with no clinical record behind it — and this product's
               * standing rule is that a prescription is entered clinical data. One sentence rather than a
               * disabled button, so the reader learns what to do instead of what is refused.
               */
              <p className="me-auto text-2xs text-muted-foreground sm:text-xs">
                Enregistrez la fiche pour émettre ce document — vous pourrez alors l&apos;imprimer et le télécharger.
              </p>
            ) : (
              <>
                <Button type="button" variant="outline" onClick={print} disabled={!pdfUrl} className="coarse:h-11">
                  <Printer className="me-2 h-4 w-4" aria-hidden="true" />
                  Imprimer
                </Button>
                <Button type="button" variant="outline" onClick={deliver} disabled={!pdfUrl} className="coarse:h-11">
                  <Download className="me-2 h-4 w-4" aria-hidden="true" />
                  Télécharger
                </Button>
                {document?.dentalRecordId && onEditInFiche ? (
                  <Button
                    type="button"
                    onClick={() => onEditInFiche(document.dentalRecordId!)}
                    className="coarse:h-11"
                  >
                    <FileEdit className="me-2 h-4 w-4" aria-hidden="true" />
                    Modifier dans la fiche
                  </Button>
                ) : document && !document.dentalRecordId && onEditInEditor ? (
                  <Button type="button" onClick={() => onEditInEditor(document)} className="coarse:h-11">
                    <FileEdit className="me-2 h-4 w-4" aria-hidden="true" />
                    Modifier
                  </Button>
                ) : null}
              </>
            )}
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  )
}
