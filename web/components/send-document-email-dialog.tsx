"use client"

import { useCallback, useEffect, useState } from "react"
import { Loader2, Mail, Send } from "lucide-react"
import { toast } from "sonner"

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
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import {
  DOCUMENT_EMAIL_STATUS_LABELS_FR,
  documentEmailsApi,
  type DocumentEmailDto,
  type DocumentEmailKind,
} from "@/lib/api/document-emails"
import { patientsApi } from "@/lib/api/patients"
import { getErrorMessage, showErrorToast } from "@/lib/errors"
import { formatDateTime } from "@/lib/format"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"

interface SendDocumentEmailDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  documentKind: DocumentEmailKind
  documentId: string
  /** What the document is called, for the dialog's own copy. e.g. « Note d'honoraires 2026-0042 ». */
  documentLabel: string
  /** Receipt kinds only — a receipt is identified by its payment, not by its parent. */
  installmentId?: string
  paymentId?: string
  /** Prefilled recipient: the patient's email, or the confrère's for a lettre de liaison. */
  defaultRecipientEmail?: string | null
  /**
   * Resolves the prefilled recipient from the patient when the caller has no address to hand (every money
   * document carries a `patientId` but not an email). Done here rather than at each of the six call sites so
   * there is one answer to "who does this go to by default"; `defaultRecipientEmail` still wins when given.
   */
  patientId?: string | null
  /** Prefilled subject. Falls back to the document label. */
  defaultSubject?: string
  defaultBody?: string
}

/**
 * « Envoyer par email » for any generated document. One dialog for all six kinds — the PDF is rendered
 * server-side from `documentId`, so nothing about a given document's layout lives here and a new kind needs no
 * new dialog.
 *
 * The send is **queued**, not immediate: on an offline LAN install the click means "send this when the server
 * can", which is why the copy says « mis en file » rather than « envoyé » and the history below shows the real
 * outcome once the dispatcher runs.
 */
export function SendDocumentEmailDialog({
  open,
  onOpenChange,
  documentKind,
  documentId,
  documentLabel,
  installmentId,
  paymentId,
  defaultRecipientEmail,
  patientId,
  defaultSubject,
  defaultBody,
}: SendDocumentEmailDialogProps) {
  const [recipientEmail, setRecipientEmail] = useState("")
  const [subject, setSubject] = useState("")
  const [body, setBody] = useState("")
  const [sending, setSending] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  const [history, setHistory] = useState<DocumentEmailDto[]>([])
  const [historyLoading, setHistoryLoading] = useState(false)

  const loadHistory = useCallback(async () => {
    if (!documentId) return
    setHistoryLoading(true)
    try {
      setHistory(await documentEmailsApi.listForDocument(documentKind, documentId))
    } catch {
      // The history is context, not the point of the dialog — a failure here must not block sending.
      setHistory([])
    } finally {
      setHistoryLoading(false)
    }
  }, [documentKind, documentId])

  // Reseed on open rather than on mount: the parent keeps this mounted across documents, and a stale recipient
  // from the previous document is the one mistake that cannot be taken back once sent.
  useEffect(() => {
    if (!open) return

    let cancelled = false

    setRecipientEmail(defaultRecipientEmail ?? "")
    setSubject(defaultSubject ?? documentLabel)
    setBody(
      defaultBody ??
        `Bonjour,\n\nVeuillez trouver ci-joint : ${documentLabel}.\n\nCordialement,`
    )
    setFormError(null)
    void loadHistory()

    // A patient's email is genuinely optional in this product, so an empty field is a normal outcome — never a
    // sentinel address. Prefill is a convenience: a lookup failure leaves the field empty and required.
    if (!defaultRecipientEmail && patientId) {
      void patientsApi
        .get(patientId)
        .then((patient) => {
          if (!cancelled && patient.email) setRecipientEmail(patient.email)
        })
        .catch(() => undefined)
    }

    return () => {
      cancelled = true
    }
  }, [open, defaultRecipientEmail, patientId, defaultSubject, defaultBody, documentLabel, loadHistory])

  // The status changes under the user a minute later, when the dispatcher picks the row up.
  useClinicRealtime(RealtimeResource.DocumentEmails, loadHistory)

  const handleSend = async () => {
    if (!recipientEmail.trim()) {
      setFormError("Renseignez l'adresse email du destinataire.")
      return
    }

    setSending(true)
    setFormError(null)
    try {
      await documentEmailsApi.queue({
        documentKind,
        documentId,
        installmentId,
        paymentId,
        recipientEmail: recipientEmail.trim(),
        subject: subject.trim(),
        body,
      })
      toast.success("Email mis en file d'envoi", {
        description: `${documentLabel} sera envoyé à ${recipientEmail.trim()}.`,
      })
      await loadHistory()
      onOpenChange(false)
    } catch (err) {
      // The dialog stays open with the typed values intact; the server's French refusal is the message.
      setFormError(getErrorMessage(err))
      showErrorToast(err)
    } finally {
      setSending(false)
    }
  }

  return (
    /*
     * `mobile="sheet"`, not the default `mobile="bottom"` (defect #7).
     *
     * This is a heavy form, not a confirmation: three fields — one of them a 120px textarea — plus the « Envois
     * précédents » history block. As a bottom sheet capped at 90dvh the whole thing is one scroll, so « Envoyer »
     * sits below the fold; and the moment the recipient field takes focus the keyboard shrinks the visual
     * viewport and pushes the button *further* away. The full-screen shape gives the middle its own scroll
     * container (`DialogBody`), which leaves the header and the footer outside it — a pinned footer survives a
     * viewport shrink, a footer at the end of a scroll does not.
     *
     * ⚠️ `DialogBody` is what makes the pinning work, not `position: sticky`; and its `min-h-0` is load-bearing
     * (a flex item's default `min-height: auto` refuses to shrink below its content, so the body would push the
     * footer off screen instead of scrolling). Both are documented in `ui/dialog.tsx`.
     */
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent mobile="sheet" className="md:max-w-[560px]">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <Mail className="h-5 w-5" aria-hidden="true" />
            Envoyer par email
          </DialogTitle>
          <DialogDescription>
            {documentLabel} — le PDF est généré par le serveur au moment de l&apos;envoi.
          </DialogDescription>
        </DialogHeader>

        <DialogBody className="space-y-4">
          {formError && <FormErrorBanner message={formError} />}

          <div className="space-y-2">
            <Label htmlFor="documentEmailRecipient">Destinataire *</Label>
            <Input
              id="documentEmailRecipient"
              type="email"
              placeholder="Ex: patient@email.tn"
              value={recipientEmail}
              onChange={(e) => setRecipientEmail(e.target.value)}
              disabled={sending}
              className="h-11"
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="documentEmailSubject">Objet</Label>
            <Input
              id="documentEmailSubject"
              type="text"
              value={subject}
              onChange={(e) => setSubject(e.target.value)}
              disabled={sending}
              className="h-11"
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="documentEmailBody">Message</Label>
            <Textarea
              id="documentEmailBody"
              value={body}
              onChange={(e) => setBody(e.target.value)}
              disabled={sending}
              className="min-h-[120px]"
            />
          </div>

          <div className="rounded-lg border px-4 py-3">
            <p className="text-sm font-semibold text-foreground">Envois précédents</p>
            {historyLoading ? (
              <p className="mt-2 text-sm text-muted-foreground" role="status">
                Chargement…
              </p>
            ) : history.length === 0 ? (
              <p className="mt-2 text-sm text-muted-foreground">Ce document n&apos;a jamais été envoyé.</p>
            ) : (
              <ul className="mt-2 space-y-2">
                {history.map((row) => (
                  <li key={row.id} className="text-sm">
                    <span className="font-medium">{row.recipientEmail}</span>
                    <span className="text-muted-foreground">
                      {" — "}
                      {DOCUMENT_EMAIL_STATUS_LABELS_FR[row.status]}
                      {" · "}
                      {formatDateTime(row.sentAt ?? row.queuedAt)}
                    </span>
                    {row.failureReason && (
                      <span className="block text-xs text-destructive">{row.failureReason}</span>
                    )}
                  </li>
                ))}
              </ul>
            )}
          </div>
        </DialogBody>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={sending}>
            Annuler
          </Button>
          <Button onClick={handleSend} disabled={sending}>
            {sending ? (
              <>
                <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden="true" />
                Envoi…
              </>
            ) : (
              <>
                <Send className="mr-2 h-4 w-4" aria-hidden="true" />
                Envoyer
              </>
            )}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
