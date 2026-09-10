"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import { Download, Loader2, Printer } from "lucide-react"
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
import { invoicesApi } from "@/lib/api/invoices"
import type { InvoiceDto } from "@/lib/api/types"
import { downloadBlob } from "@/lib/download"
import { getErrorMessage, showErrorToast } from "@/lib/errors"
import { formatDate } from "@/lib/format"

/**
 * « Voir la note » — a note d'honoraires read as the paper it is, wherever it is referred to.
 *
 * <p>It is `DocumentPreviewDialog`'s twin for the one document that is not a `MedicalDocument`, and it exists for
 * the same reason: until now the only thing a surface could do with an existing note was hand you a file. « Voir
 * le détail » showed the acts and the payments — the ledger behind the note, not the note — and the list row's
 * menu downloaded it. Neither answers « montre-moi la facture », which is what someone opening the documents
 * module has come to do.</p>
 *
 * <p>⚠️ It frames the <b>server-rendered PDF</b>, never a second HTML rendering of a legal document — the rule
 * `DocumentPreviewDialog` already carries, and the reason print is `contentWindow.print()` on the real bytes.</p>
 *
 * <p>⚠️ <b>A draft has no paper.</b> It carries no number, `GET /invoices/{id}/pdf` has nothing to issue, and the
 * product's own rule is that a number is minted at emission — so a draft is edited, never previewed, and the
 * callers gate on that rather than this dialog rendering a refusal.</p>
 */
export function InvoicePdfDialog({
  invoice,
  onClose,
}: {
  /** The note to show; null closes the dialog. */
  invoice: InvoiceDto | null
  onClose: () => void
}) {
  const [pdfUrl, setPdfUrl] = useState<string | null>(null)
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

  const invoiceId = invoice?.id ?? null
  const fileName = `note-honoraires-${invoice?.number ?? invoiceId ?? "document"}.pdf`

  useEffect(() => {
    if (!invoiceId) return

    let cancelled = false
    setLoading(true)
    setFailure(null)
    ;(async () => {
      try {
        const blob = await invoicesApi.downloadPdf(invoiceId)
        if (cancelled) return
        blobRef.current = blob
        releaseUrl()
        /*
         * ⚠️ **The blob is for « Télécharger » and for the coarse hand-off — the FRAME is pointed at
         * `pdfSourceUrl` instead**, because Chrome's viewer names its toolbar save from the URL's last path
         * segment and a blob's is a bare UUID (measured; a named `File` changes nothing).
         *
         * This read is therefore not redundant even though the frame fetches the same document: an
         * `<iframe>` reports no error for an HTTP failure — it would simply paint the JSON body as text —
         * so this is also the only thing that can put « Réessayer » on screen instead of a broken document.
         */
        const url = URL.createObjectURL(blob)
        urlRef.current = url
        setPdfUrl(url)
      } catch (error) {
        if (!cancelled) setFailure(getErrorMessage(error))
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()

    return () => {
      cancelled = true
    }
  }, [invoiceId, reload, releaseUrl])

  // Revoked on close rather than on every render: the frame is still displaying the URL until it unmounts.
  useEffect(() => {
    if (invoiceId) return
    releaseUrl()
    setPdfUrl(null)
    setFailure(null)
    blobRef.current = null
  }, [invoiceId, releaseUrl])

  useEffect(() => () => releaseUrl(), [releaseUrl])

  const deliver = useCallback(async () => {
    if (!blobRef.current) return
    try {
      await downloadBlob(blobRef.current, fileName)
    } catch (error) {
      showErrorToast(error, "Le téléchargement a échoué.")
    }
  }, [fileName])

  /** The frame is printed only when it is really rendered — below a coarse pointer it is hidden by CSS. */
  const print = useCallback(() => {
    const frame = frameRef.current
    if (frame && frame.offsetParent !== null && frame.contentWindow) {
      frame.contentWindow.focus()
      frame.contentWindow.print()
      return
    }
    void deliver()
  }, [deliver])

  const title = invoice?.number ? `Note d'honoraires ${invoice.number}` : "Note d'honoraires"

  return (
    <>
      <Dialog open={!!invoice} onOpenChange={(open) => { if (!open) onClose() }}>
        {/* Whole screen on a phone — it is showing a document. `DocumentPreviewDialog`'s shape exactly. */}
        <DialogContent
          mobile="sheet"
          className="gap-0 p-0 md:h-[90dvh] md:max-h-[90dvh] md:max-w-5xl md:overflow-hidden"
        >
          {/* `pe-12` clears the close control Radix pins to the corner. */}
          <DialogHeader className="flex-shrink-0 border-b bg-muted/40 px-4 pb-3 pe-12 pt-4 md:px-6 md:pb-4 md:pe-12 md:pt-6">
            <DialogTitle className="truncate text-base font-semibold md:text-lg">{title}</DialogTitle>
            <DialogDescription className="mt-1 text-xs md:text-sm">
              {invoice
                ? [invoice.patientName, invoice.issueDate ? formatDate(invoice.issueDate) : null]
                    .filter(Boolean)
                    .join(" • ")
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
                previewUrl={pdfSourceUrl('invoice', invoiceId!, fileName)}
                fileName={fileName}
                onDeliver={deliver}
                frameRef={frameRef}
              />
            ) : null}
          </div>

          <DialogFooter className="flex-shrink-0 gap-2 border-t bg-background px-4 py-3 md:px-6">
            <Button type="button" variant="outline" onClick={print} disabled={!pdfUrl} className="coarse:h-11">
              <Printer className="me-2 h-4 w-4" aria-hidden="true" />
              Imprimer
            </Button>
            <Button type="button" variant="outline" onClick={deliver} disabled={!pdfUrl} className="coarse:h-11">
              <Download className="me-2 h-4 w-4" aria-hidden="true" />
              Télécharger
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  )
}
