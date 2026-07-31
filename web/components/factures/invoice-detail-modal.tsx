"use client"

import { useCallback, useEffect, useState } from "react"
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Loader2, FileDown, Undo2, CalendarClock, Mail } from "lucide-react"
import { toast } from "sonner"
import { invoicesApi } from "@/lib/api/invoices"
import { appointmentsApi } from "@/lib/api/appointments"
import { billingApi } from "@/lib/api/billing"
import { ApiError } from "@/lib/api/client"
import type { AppointmentDto, CreditNoteDto, InvoiceDto, PaymentDto } from "@/lib/api/types"
import { formatDT, formatDateFr, formatDateTime } from "@/lib/format"
import { downloadBlob } from "@/lib/download"
import { useSession } from "@/lib/auth/session"
import { canReverseFinancials, REVERSAL_FORBIDDEN_HINT } from "@/lib/auth/can"
import { invoiceStatusLabel, paymentMethodLabel } from "./invoice-labels"
import { SendDocumentEmailDialog } from "@/components/send-document-email-dialog"
import { DOCUMENT_EMAIL_KINDS, type DocumentEmailKind } from "@/lib/api/document-emails"

/** What « Envoyer par email » was clicked for. One dialog serves the reçus and the avoirs of this modal. */
interface EmailTarget {
  kind: DocumentEmailKind
  documentId: string
  paymentId?: string
  label: string
}

interface InvoiceDetailModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Which invoice to show. The modal re-fetches it — the table row is a list snapshot. */
  invoiceId: string | null
  /** Called after any mutation so the list and its KPIs refresh. */
  onChanged?: () => void
}

/**
 * The app's first invoice detail surface: act lines, every payment with its receipt and an « Annuler »
 * action, and (from Part G) the avoirs.
 *
 * It re-fetches rather than reusing the row, because per-payment accuracy is the whole point — the list
 * snapshot can be stale by the time the modal opens, and a stale payment id is the one thing a void must not
 * act on. The void confirm is an in-place panel, not a nested dialog: nothing else in this app nests Radix
 * dialogs, and the one component that came close documented the focus/Enter conflicts it caused.
 */
export function InvoiceDetailModal({ open, onOpenChange, invoiceId, onChanged }: InvoiceDetailModalProps) {
  const [invoice, setInvoice] = useState<InvoiceDto | null>(null)
  // The visit this note bills, when it was raised from an appointment context (AC-P6.13). Fetched separately
  // rather than denormalised onto InvoiceDto: it is one lookup on a modal open, and every other invoice read
  // (the list, the revenue KPIs, the PDF) would otherwise pay for a join none of them use.
  const [billedVisit, setBilledVisit] = useState<AppointmentDto | null>(null)
  const [loading, setLoading] = useState(false)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  // The in-place void confirm: which payment, and the required motif.
  const [voidTarget, setVoidTarget] = useState<PaymentDto | null>(null)
  const [emailTarget, setEmailTarget] = useState<EmailTarget | null>(null)
  const [voidReason, setVoidReason] = useState("")
  const [voidError, setVoidError] = useState<string | null>(null)

  const { user } = useSession()
  const mayReverse = canReverseFinancials(user?.role)

  const load = useCallback(async () => {
    if (!invoiceId) return
    try {
      setLoading(true)
      setLoadError(null)
      setInvoice(await invoicesApi.get(invoiceId))
    } catch (err) {
      // This modal is the only place voidable payments exist, so a silent load failure is a dead end.
      setLoadError(err instanceof ApiError ? err.message : "Échec du chargement de la facture.")
    } finally {
      setLoading(false)
    }
  }, [invoiceId])

  useEffect(() => {
    if (!open) {
      setInvoice(null)
      setBilledVisit(null)
      setLoadError(null)
      setVoidTarget(null)
      setVoidReason("")
      setVoidError(null)
      return
    }
    load()
  }, [open, load])

  // Resolve the billed visit once the invoice is in hand. A failure leaves the block hidden rather than
  // erroring the modal: the visit line is context, and the payments below it are what the modal exists for.
  useEffect(() => {
    const appointmentId = invoice?.appointmentId
    if (!open || !appointmentId) {
      setBilledVisit(null)
      return
    }

    let cancelled = false
    ;(async () => {
      try {
        const visit = await appointmentsApi.get(appointmentId)
        if (!cancelled) setBilledVisit(visit)
      } catch {
        if (!cancelled) setBilledVisit(null)
      }
    })()
    return () => { cancelled = true }
  }, [open, invoice?.appointmentId])

  const handleDownloadAvoir = async (creditNote: CreditNoteDto) => {
    try {
      const blob = await invoicesApi.downloadAvoirPdf(creditNote.id)
      downloadBlob(blob, `avoir-${creditNote.number}.pdf`)
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec du téléchargement de l'avoir.")
    }
  }

  const handleDownloadReceipt = async (payment: PaymentDto) => {
    try {
      const blob = await billingApi.downloadPaymentReceipt(payment.id)
      downloadBlob(blob, `recu-${invoice?.number ?? payment.id.slice(0, 8)}.pdf`)
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec du téléchargement du reçu.")
    }
  }

  const confirmVoid = async () => {
    if (!invoice || !voidTarget) return
    if (!voidReason.trim()) {
      setVoidError("Le motif est requis.")
      return
    }

    try {
      setBusy(true)
      setVoidError(null)
      const updated = await invoicesApi.voidPayment(invoice.id, voidTarget.id, voidReason.trim())
      setInvoice(updated)
      setVoidTarget(null)
      setVoidReason("")
      toast.success("Paiement annulé")
      // The modal deliberately stays open: a second payment may need voiding, and closing to a row whose
      // « Encaissé » just changed is disorienting.
      onChanged?.()
    } catch (err) {
      setVoidError(err instanceof ApiError ? err.message : "Échec de l'annulation du paiement.")
    } finally {
      setBusy(false)
    }
  }

  const livePayments = invoice?.payments.filter((p) => !p.isVoided) ?? []

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[90dvh] overflow-y-auto md:max-w-2xl">
        <DialogHeader>
          <DialogTitle className="flex flex-wrap items-center gap-2">
            {invoice?.number ? `Facture ${invoice.number}` : "Facture"}
            {invoice && (
              <Badge variant="secondary">{invoiceStatusLabel(invoice.status)}</Badge>
            )}
          </DialogTitle>
          <DialogDescription>
            {invoice?.patientName ?? "Détail de la facture, paiements et avoirs."}
          </DialogDescription>
        </DialogHeader>

        {loading && (
          <div className="flex items-center justify-center py-12 text-muted-foreground">
            <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Chargement…
          </div>
        )}

        {loadError && !loading && (
          <div className="space-y-3 py-8 text-center">
            <p className="text-sm text-destructive">{loadError}</p>
            <Button variant="outline" size="sm" onClick={load}>Réessayer</Button>
          </div>
        )}

        {invoice && !loading && !loadError && (
          <div className="space-y-6">
            {/* ---- The visit this note bills (AC-P6.13) ---- */}
            {billedVisit && (
              <section className="flex flex-wrap items-center gap-2 rounded-md border bg-muted/30 px-3 py-2">
                <CalendarClock className="h-4 w-4 text-muted-foreground" />
                <span className="text-sm text-muted-foreground">Consultation facturée&nbsp;:</span>
                <span className="text-sm font-medium">
                  {formatDateTime(billedVisit.appointmentDateTime)}
                </span>
                {billedVisit.procedureTypeName && (
                  <Badge variant="outline" className="text-xs">{billedVisit.procedureTypeName}</Badge>
                )}
              </section>
            )}

            {/* ---- Acts ---- */}
            <section className="space-y-2">
              <h3 className="text-sm font-semibold">Actes</h3>
              <div className="rounded-md border overflow-x-auto">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Désignation</TableHead>
                      <TableHead className="text-right">Qté</TableHead>
                      <TableHead className="text-right">P.U. HT</TableHead>
                      <TableHead className="text-right">Total HT</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {invoice.lines.length === 0 ? (
                      <TableRow>
                        <TableCell colSpan={4} className="py-6 text-center text-muted-foreground">
                          Aucune ligne.
                        </TableCell>
                      </TableRow>
                    ) : (
                      invoice.lines.map((line) => (
                        <TableRow key={line.id}>
                          <TableCell>{line.designation}</TableCell>
                          <TableCell className="text-right">{line.quantity}</TableCell>
                          <TableCell className="text-right">{formatDT(line.unitPriceHt)}</TableCell>
                          <TableCell className="text-right">{formatDT(line.lineTotalHt)}</TableCell>
                        </TableRow>
                      ))
                    )}
                  </TableBody>
                </Table>
              </div>
            </section>

            {/* ---- Payments ---- */}
            <section className="space-y-2">
              <h3 className="text-sm font-semibold">Paiements</h3>
              {invoice.payments.length === 0 ? (
                <p className="rounded-md border py-6 text-center text-sm text-muted-foreground">
                  Aucun paiement enregistré.
                </p>
              ) : (
                <ul className="divide-y rounded-md border">
                  {invoice.payments.map((payment) => (
                    <li key={payment.id} className="space-y-2 p-3">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <div className={payment.isVoided ? "text-muted-foreground line-through" : ""}>
                          <span className="font-medium">{formatDT(payment.amount)}</span>
                          <span className="text-muted-foreground">
                            {" "}· {formatDateFr(payment.paidOn)} · {paymentMethodLabel(payment.method)}
                          </span>
                        </div>
                        <div className="flex items-center gap-1">
                          {payment.isVoided && <Badge variant="outline">Annulé</Badge>}
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => handleDownloadReceipt(payment)}
                            title="Télécharger le reçu"
                            aria-label={`Télécharger le reçu du paiement de ${formatDT(payment.amount)} du ${formatDateFr(payment.paidOn)}`}
                          >
                            <FileDown className="h-4 w-4" />
                          </Button>
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => setEmailTarget({
                              kind: DOCUMENT_EMAIL_KINDS.InvoicePaymentReceipt,
                              documentId: invoice.id,
                              paymentId: payment.id,
                              label: `Reçu de paiement ${formatDT(payment.amount)}`,
                            })}
                            title="Envoyer le reçu par email"
                            aria-label={`Envoyer par email le reçu du paiement de ${formatDT(payment.amount)} du ${formatDateFr(payment.paidOn)}`}
                          >
                            <Mail className="h-4 w-4" />
                          </Button>
                          {!payment.isVoided && (
                            <Button
                              variant="ghost"
                              size="sm"
                              className="text-destructive hover:text-destructive"
                              disabled={!mayReverse || busy}
                              // Rendered-but-disabled rather than hidden: hiding it makes a secretary
                              // conclude the operation is impossible instead of knowing who to ask.
                              title={mayReverse ? "Annuler ce paiement" : REVERSAL_FORBIDDEN_HINT}
                              aria-label={`Annuler le paiement de ${formatDT(payment.amount)} du ${formatDateFr(payment.paidOn)}`}
                              onClick={() => {
                                setVoidTarget(payment)
                                setVoidReason("")
                                setVoidError(null)
                              }}
                            >
                              <Undo2 className="h-4 w-4" />
                            </Button>
                          )}
                        </div>
                      </div>

                      {payment.isVoided && (
                        <p className="text-xs text-muted-foreground">
                          Annulé{payment.voidedAt ? ` le ${formatDateFr(payment.voidedAt)}` : ""}
                          {payment.voidedByName ? ` par ${payment.voidedByName}` : ""}
                          {payment.voidReason ? ` — « ${payment.voidReason} »` : ""}
                        </p>
                      )}

                      {/* In-place confirm — no nested dialog. */}
                      {voidTarget?.id === payment.id && (
                        <div className="space-y-3 rounded-md border border-destructive/40 bg-destructive/5 p-3">
                          <p className="text-sm">
                            Annuler ce paiement de <span className="font-semibold">{formatDT(payment.amount)}</span>{" "}
                            du {formatDateFr(payment.paidOn)} ? L&apos;encaissement sera retiré de la facture et de
                            la caisse, à sa date d&apos;origine. Cette action est définitive.
                          </p>
                          <p className="text-xs text-muted-foreground">
                            Si un reçu a déjà été remis au patient, récupérez-le : sa réimpression portera la
                            mention « ANNULÉ ».
                          </p>
                          <div className="space-y-1">
                            <Label htmlFor={`void-reason-${payment.id}`} className="text-sm">
                              Motif <span className="text-destructive">*</span>
                            </Label>
                            <Textarea
                              id={`void-reason-${payment.id}`}
                              rows={2}
                              value={voidReason}
                              onChange={(e) => setVoidReason(e.target.value)}
                              placeholder="Ex. erreur de saisie du montant"
                            />
                          </div>
                          {voidError && (
                            <div className="rounded-lg border border-red-200 bg-red-50 p-2 text-sm text-red-800 dark:border-red-900 dark:bg-red-950 dark:text-red-200">
                              {voidError}
                            </div>
                          )}
                          <div className="flex justify-end gap-2">
                            <Button
                              variant="outline"
                              size="sm"
                              disabled={busy}
                              onClick={() => { setVoidTarget(null); setVoidReason(""); setVoidError(null) }}
                            >
                              Retour
                            </Button>
                            <Button
                              size="sm"
                              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
                              disabled={busy}
                              onClick={confirmVoid}
                            >
                              {busy ? "Annulation…" : "Annuler le paiement"}
                            </Button>
                          </div>
                        </div>
                      )}
                    </li>
                  ))}
                </ul>
              )}
            </section>

            {/* ---- Avoirs ---- */}
            {invoice.creditNotes.length > 0 && (
              <section className="space-y-2">
                <h3 className="text-sm font-semibold">Avoirs</h3>
                <ul className="divide-y rounded-md border">
                  {invoice.creditNotes.map((creditNote) => (
                    <li key={creditNote.id} className="space-y-1 p-3">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <div>
                          <span className="font-medium">{formatDT(creditNote.amount)}</span>
                          <span className="text-muted-foreground">
                            {" "}· avoir {creditNote.number} · remboursé le {formatDateFr(creditNote.refundedOn)}
                            {creditNote.method ? ` · ${paymentMethodLabel(creditNote.method)}` : ""}
                          </span>
                        </div>
                        <div className="flex items-center gap-1">
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => handleDownloadAvoir(creditNote)}
                            title="Télécharger l'avoir"
                            aria-label={`Télécharger l'avoir ${creditNote.number} de ${formatDT(creditNote.amount)}`}
                          >
                            <FileDown className="h-4 w-4" />
                          </Button>
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => setEmailTarget({
                              kind: DOCUMENT_EMAIL_KINDS.CreditNote,
                              documentId: creditNote.id,
                              label: `Avoir ${creditNote.number}`,
                            })}
                            title="Envoyer l'avoir par email"
                            aria-label={`Envoyer par email l'avoir ${creditNote.number} de ${formatDT(creditNote.amount)}`}
                          >
                            <Mail className="h-4 w-4" />
                          </Button>
                        </div>
                      </div>
                      <p className="text-xs text-muted-foreground">Motif : {creditNote.reason}</p>
                      {creditNote.correctedInvoiceIsTtnRegistered && (
                        <p className="text-xs text-amber-700 dark:text-amber-400">
                          Facture télétransmise à TTN : l&apos;avoir ne l&apos;est pas — la régularisation
                          auprès d&apos;El Fatoora reste à faire.
                        </p>
                      )}
                    </li>
                  ))}
                </ul>
              </section>
            )}

            {/* ---- Totals ---- */}
            <section className="rounded-md border p-3">
              <dl className="space-y-1 text-sm">
                <div className="flex justify-between">
                  <dt className="text-muted-foreground">Total TTC</dt>
                  <dd className="font-medium">{formatDT(invoice.totalTtc)}</dd>
                </div>
                <div className="flex justify-between">
                  <dt className="text-muted-foreground">
                    Encaissé
                    {invoice.payments.length !== livePayments.length && (
                      <span className="ml-1 text-xs">(hors paiements annulés)</span>
                    )}
                  </dt>
                  <dd className="font-medium text-green-700 dark:text-green-400">
                    {formatDT(invoice.amountCollected)}
                  </dd>
                </div>
                {invoice.creditedTotal > 0 && (
                  <div className="flex justify-between">
                    <dt className="text-muted-foreground">Remboursé (avoirs)</dt>
                    <dd className="font-medium text-primary">
                      −{formatDT(invoice.creditedTotal)}
                    </dd>
                  </div>
                )}
                <div className="flex justify-between">
                  <dt className="text-muted-foreground">Reste à payer</dt>
                  <dd className="font-semibold text-amber-700 dark:text-amber-400">
                    {formatDT(invoice.outstanding)}
                  </dd>
                </div>
              </dl>
            </section>
          </div>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>Fermer</Button>
        </DialogFooter>

        {emailTarget && (
          <SendDocumentEmailDialog
            open={Boolean(emailTarget)}
            onOpenChange={(next) => { if (!next) setEmailTarget(null) }}
            documentKind={emailTarget.kind}
            documentId={emailTarget.documentId}
            paymentId={emailTarget.paymentId}
            documentLabel={emailTarget.label}
            patientId={invoice?.patientId}
          />
        )}
      </DialogContent>
    </Dialog>
  )
}
