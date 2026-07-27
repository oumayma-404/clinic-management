"use client"

import { useState, useEffect, useCallback } from "react"
import {
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from "@/components/ui/table"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { Textarea } from "@/components/ui/textarea"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter,
} from "@/components/ui/dialog"
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent,
  AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { FileDown, Pencil, Trash2, Send, CreditCard, Ban, Plus, Loader2, Landmark, FileCode2, ReceiptText } from "lucide-react"
import Link from "next/link"
import { toast } from "sonner"
import { invoicesApi } from "@/lib/api/invoices"
import { ApiError } from "@/lib/api/client"
import type { InvoiceDto } from "@/lib/api/types"
import { formatDT, formatDateFr } from "@/lib/format"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useConnectivity } from "@/lib/connectivity/connectivity"
import { useClinicAccess } from "@/lib/hooks/use-clinic-access"
import { InvoiceFormModal } from "./invoice-form-modal"
import { PaymentModal } from "./payment-modal"
import { InvoiceDetailModal } from "./invoice-detail-modal"
import { invoiceStatusLabel, eInvoiceStatusLabel, eInvoiceStatusBadgeClass } from "./invoice-labels"

interface InvoicesTableProps {
  patientId?: string
  patientName?: string
  from?: string
  to?: string
  status?: string
  showPatientColumn?: boolean
  /** Bumped by the parent (e.g. after filter change) to force a reload. */
  reloadKey?: number
  /** Called after any mutation so the parent can refresh dependent views (e.g. revenue totals). */
  onChanged?: () => void
}

function statusBadgeClass(status: string): string {
  switch (status) {
    case "Draft":
      return "bg-muted text-muted-foreground"
    case "Issued":
      return "bg-blue-100 text-blue-800 dark:bg-blue-950 dark:text-blue-200"
    case "PartiallyPaid":
      return "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-200"
    case "Paid":
      return "bg-green-100 text-green-800 dark:bg-green-950 dark:text-green-200"
    case "Cancelled":
      return "bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-200"
    default:
      return "bg-muted text-muted-foreground"
  }
}

export function InvoicesTable({
  patientId,
  patientName,
  from,
  to,
  status,
  showPatientColumn = true,
  reloadKey = 0,
  onChanged,
}: InvoicesTableProps) {
  const [invoices, setInvoices] = useState<InvoiceDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  const { internetReachable } = useConnectivity()
  // El Fatoora is per-clinic opt-in: only surface the submit action when the clinic has enabled TTN
  // e-invoicing (otherwise the submit would just fail server-side with "non activée").
  const { status: clinicStatus } = useClinicAccess(false)
  const eInvoicingEnabled = clinicStatus?.clinic?.ttnEInvoicingEnabled ?? false

  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<InvoiceDto | null>(null)
  const [paymentTarget, setPaymentTarget] = useState<InvoiceDto | null>(null)
  // The invoice detail modal — the app's first invoice detail surface, and the only place a specific
  // payment can be voided.
  const [detailInvoiceId, setDetailInvoiceId] = useState<string | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<InvoiceDto | null>(null)
  const [cancelTarget, setCancelTarget] = useState<InvoiceDto | null>(null)
  const [cancelReason, setCancelReason] = useState("")
  // Avoir (credit note) modal state (finding #8).
  const [avoirTarget, setAvoirTarget] = useState<InvoiceDto | null>(null)
  const [avoirAmount, setAvoirAmount] = useState("")
  const [avoirReason, setAvoirReason] = useState("")

  const load = useCallback(async () => {
    try {
      setLoading(true)
      setError(null)
      const data = await invoicesApi.list({ patientId, from, to, status })
      setInvoices(data)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Échec du chargement des factures.")
    } finally {
      setLoading(false)
    }
  }, [patientId, from, to, status])

  useEffect(() => {
    load()
  }, [load, reloadKey])

  useClinicRealtime(RealtimeResource.Invoices, load)

  const afterMutation = () => {
    load()
    onChanged?.()
  }

  const openAvoir = (invoice: InvoiceDto) => {
    setAvoirTarget(invoice)
    setAvoirAmount("")
    setAvoirReason("")
  }

  const confirmAvoir = async () => {
    if (!avoirTarget) return
    const amount = Number.parseFloat(avoirAmount)
    if (!Number.isFinite(amount) || amount <= 0) {
      toast.error("Le montant de l'avoir doit être supérieur à 0.")
      return
    }
    if (!avoirReason.trim()) {
      toast.error("Le motif de l'avoir est requis.")
      return
    }
    setBusyId(avoirTarget.id)
    try {
      await invoicesApi.createAvoir(avoirTarget.id, { amount, reason: avoirReason.trim() })
      toast.success("Avoir établi")
      setAvoirTarget(null)
      afterMutation()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de l'établissement de l'avoir.")
    } finally {
      setBusyId(null)
    }
  }

  const handleIssue = async (invoice: InvoiceDto) => {
    setBusyId(invoice.id)
    try {
      await invoicesApi.issue(invoice.id)
      toast.success("Facture émise")
      afterMutation()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de l'émission.")
    } finally {
      setBusyId(null)
    }
  }

  // P1-D: the common "paid on the spot" case in one action — issue the draft, then open the payment
  // modal (prefilled with the full outstanding) on the freshly-issued invoice.
  const handleIssueAndPay = async (invoice: InvoiceDto) => {
    setBusyId(invoice.id)
    try {
      const issued = await invoicesApi.issue(invoice.id)
      toast.success("Facture émise")
      afterMutation()
      setPaymentTarget(issued)
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de l'émission.")
    } finally {
      setBusyId(null)
    }
  }

  const handleDownloadPdf = async (invoice: InvoiceDto) => {
    setBusyId(invoice.id)
    try {
      const blob = await invoicesApi.downloadPdf(invoice.id)
      const url = URL.createObjectURL(blob)
      const a = document.createElement("a")
      a.href = url
      a.download = `note-honoraires-${invoice.number ?? invoice.id}.pdf`
      document.body.appendChild(a)
      a.click()
      a.remove()
      URL.revokeObjectURL(url)
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Échec du téléchargement du PDF.")
    } finally {
      setBusyId(null)
    }
  }

  const handleSubmitEInvoice = async (invoice: InvoiceDto) => {
    setBusyId(invoice.id)
    try {
      const updated = await invoicesApi.submitToElFatoora(invoice.id)
      // Offline installs queue the invoice; the outbox sends it when internet returns (US-2).
      if (updated.eInvoiceStatus === "Valid") {
        toast.success("Facture enregistrée auprès de El Fatoora")
      } else if (updated.eInvoiceStatus === "Rejected" || updated.eInvoiceStatus === "Failed") {
        toast.error(updated.eInvoiceLastError || "Envoi à El Fatoora refusé.")
      } else if (updated.eInvoiceStatus === "Queued" && updated.eInvoiceLastError) {
        // Online attempt hit a transient error — it stays queued and will auto-retry, but don't imply success.
        toast.warning(updated.eInvoiceLastError)
      } else {
        toast.success(internetReachable
          ? "Envoi à El Fatoora en cours…"
          : "Facture mise en file d'attente — elle sera envoyée dès le retour d'internet.")
      }
      afterMutation()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de l'envoi à El Fatoora.")
    } finally {
      setBusyId(null)
    }
  }

  const handleDownloadArtifact = async (invoice: InvoiceDto, artifact: "xml" | "receipt") => {
    setBusyId(invoice.id)
    try {
      const blob = await invoicesApi.downloadEInvoiceArtifact(invoice.id, artifact)
      const url = URL.createObjectURL(blob)
      const a = document.createElement("a")
      a.href = url
      const suffix = artifact === "xml" ? "teif" : "recu-ttn"
      a.download = `${suffix}-${invoice.number ?? invoice.id}.xml`
      document.body.appendChild(a)
      a.click()
      a.remove()
      URL.revokeObjectURL(url)
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Échec du téléchargement.")
    } finally {
      setBusyId(null)
    }
  }

  const confirmDelete = async () => {
    if (!deleteTarget) return
    setBusyId(deleteTarget.id)
    try {
      await invoicesApi.delete(deleteTarget.id)
      toast.success("Brouillon supprimé")
      afterMutation()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la suppression.")
    } finally {
      setBusyId(null)
      setDeleteTarget(null)
    }
  }

  const confirmCancel = async () => {
    if (!cancelTarget) return
    if (!cancelReason.trim()) {
      toast.error("Le motif d'annulation est requis.")
      return
    }
    setBusyId(cancelTarget.id)
    try {
      await invoicesApi.cancel(cancelTarget.id, cancelReason.trim())
      toast.success("Facture annulée")
      afterMutation()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de l'annulation.")
    } finally {
      setBusyId(null)
      setCancelTarget(null)
      setCancelReason("")
    }
  }

  const openCreate = () => {
    setEditing(null)
    setFormOpen(true)
  }

  const openEdit = (invoice: InvoiceDto) => {
    setEditing(invoice)
    setFormOpen(true)
  }

  const colSpan = showPatientColumn ? 9 : 8

  return (
    <div className="space-y-3">
      <div className="flex justify-end">
        <Button onClick={openCreate} className="gap-2">
          <Plus className="h-4 w-4" /> Nouvelle facture
        </Button>
      </div>

      {error && (
        <div className="rounded-lg bg-red-50 border border-red-200 p-3 text-sm text-red-800 dark:bg-red-950 dark:border-red-900 dark:text-red-200">
          {error}
        </div>
      )}

      <div className="rounded-md border overflow-x-auto">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Numéro</TableHead>
              {showPatientColumn && <TableHead>Patient</TableHead>}
              <TableHead>Date</TableHead>
              <TableHead>Statut</TableHead>
              <TableHead>El Fatoora</TableHead>
              <TableHead className="text-right">Total TTC</TableHead>
              <TableHead className="text-right">Encaissé</TableHead>
              <TableHead className="text-right">Reste</TableHead>
              <TableHead className="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {loading ? (
              <TableRow>
                <TableCell colSpan={colSpan} className="text-center text-muted-foreground py-8">
                  Chargement...
                </TableCell>
              </TableRow>
            ) : invoices.length === 0 ? (
              <TableRow>
                <TableCell colSpan={colSpan} className="text-center text-muted-foreground py-8">
                  Aucune facture.
                </TableCell>
              </TableRow>
            ) : (
              invoices.map((invoice) => {
                const isBusy = busyId === invoice.id
                const isDraft = invoice.status === "Draft"
                const isPayable = invoice.status === "Issued" || invoice.status === "PartiallyPaid"
                // Both gates now come from the SERVER. Re-deriving them here from status + amountCollected is
                // what produced an enabled « Annuler » the API refuses: after a full void the status is Issued
                // and collected is 0, but the voided payment rows are still there — and a TTN-registered
                // invoice can never be cancelled regardless of either value.
                const isCancellable = invoice.canCancel
                const canCreateAvoir = invoice.canCreateAvoir
                return (
                  <TableRow key={invoice.id}>
                    <TableCell className="font-medium">
                      <div className="flex items-center gap-2">
                        {/* The number is the detail affordance. It was inert text, and the row already
                            carries up to eight icon buttons — a ninth would not have been readable. This
                            also gives draft rows (« — ») a target. */}
                        <button
                          type="button"
                          className="underline-offset-2 hover:underline"
                          title="Voir le détail"
                          onClick={(e) => { e.stopPropagation(); setDetailInvoiceId(invoice.id) }}
                        >
                          {invoice.number ?? "—"}
                        </button>
                        {/* A devis-born note is otherwise indistinguishable here from a standalone one, and
                            the devis→facture link was write-only until now. Mirrors the plans table's
                            « Facturé — N° » badge, closing the loop from both ends. */}
                        {invoice.treatmentPlanId && (
                          <Link
                            href={`/treatment-plans/${invoice.treatmentPlanId}`}
                            title="Voir le devis d'origine"
                          >
                            <Badge variant="outline" className="whitespace-nowrap hover:bg-accent">
                              Devis
                            </Badge>
                          </Link>
                        )}
                      </div>
                    </TableCell>
                    {showPatientColumn && <TableCell>{invoice.patientName ?? "—"}</TableCell>}
                    <TableCell>{invoice.issueDate ? formatDateFr(invoice.issueDate) : formatDateFr(invoice.createdAt)}</TableCell>
                    <TableCell>
                      <Badge variant="secondary" className={statusBadgeClass(invoice.status)}>
                        {invoiceStatusLabel(invoice.status)}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      {isDraft ? (
                        <span className="text-muted-foreground">—</span>
                      ) : (
                        <Badge
                          variant="secondary"
                          className={eInvoiceStatusBadgeClass(invoice.eInvoiceStatus)}
                          title={invoice.eInvoiceLastError ?? undefined}
                        >
                          {eInvoiceStatusLabel(invoice.eInvoiceStatus)}
                        </Badge>
                      )}
                    </TableCell>
                    <TableCell className="text-right">{formatDT(invoice.totalTtc)}</TableCell>
                    <TableCell className="text-right">{formatDT(invoice.amountCollected)}</TableCell>
                    <TableCell className="text-right">{formatDT(invoice.outstanding)}</TableCell>
                    <TableCell>
                      <div className="flex justify-end gap-1">
                        {isBusy && <Loader2 className="h-4 w-4 animate-spin self-center" />}
                        {isDraft && (
                          <>
                            <Button variant="ghost" size="icon" title="Modifier" onClick={() => openEdit(invoice)} disabled={isBusy}>
                              <Pencil className="h-4 w-4" />
                            </Button>
                            <Button variant="ghost" size="icon" title="Émettre" onClick={() => handleIssue(invoice)} disabled={isBusy}>
                              <Send className="h-4 w-4" />
                            </Button>
                            <Button variant="ghost" size="icon" title="Émettre et encaisser" onClick={() => handleIssueAndPay(invoice)} disabled={isBusy}>
                              <CreditCard className="h-4 w-4" />
                            </Button>
                            <Button variant="ghost" size="icon" title="Supprimer" onClick={() => setDeleteTarget(invoice)} disabled={isBusy}>
                              <Trash2 className="h-4 w-4" />
                            </Button>
                          </>
                        )}
                        {isPayable && (
                          <Button variant="ghost" size="icon" title="Enregistrer un paiement" onClick={() => setPaymentTarget(invoice)} disabled={isBusy}>
                            <CreditCard className="h-4 w-4" />
                          </Button>
                        )}
                        {canCreateAvoir && (
                          <Button variant="ghost" size="icon" title="Établir un avoir" onClick={() => openAvoir(invoice)} disabled={isBusy}>
                            <ReceiptText className="h-4 w-4" />
                          </Button>
                        )}
                        {!isDraft && eInvoicingEnabled && invoice.canSubmitToElFatoora && (
                          <Button
                            variant="ghost"
                            size="icon"
                            title={
                              invoice.eInvoiceStatus === "Rejected" || invoice.eInvoiceStatus === "Failed"
                                ? "Renvoyer à El Fatoora"
                                : internetReachable
                                  ? "Envoyer à El Fatoora"
                                  : "Mettre en file d'attente (envoi au retour d'internet)"
                            }
                            onClick={() => handleSubmitEInvoice(invoice)}
                            disabled={isBusy}
                          >
                            <Landmark className="h-4 w-4" />
                          </Button>
                        )}
                        {invoice.hasSignedXml && (
                          <Button variant="ghost" size="icon" title="Télécharger le TEIF signé" onClick={() => handleDownloadArtifact(invoice, "xml")} disabled={isBusy}>
                            <FileCode2 className="h-4 w-4" />
                          </Button>
                        )}
                        {invoice.hasTtnReceipt && (
                          <Button variant="ghost" size="icon" title="Télécharger le reçu TTN" onClick={() => handleDownloadArtifact(invoice, "receipt")} disabled={isBusy}>
                            <ReceiptText className="h-4 w-4" />
                          </Button>
                        )}
                        {!isDraft && (
                          <Button variant="ghost" size="icon" title="Télécharger le PDF" onClick={() => handleDownloadPdf(invoice)} disabled={isBusy}>
                            <FileDown className="h-4 w-4" />
                          </Button>
                        )}
                        {isCancellable && (
                          <Button variant="ghost" size="icon" title="Annuler" onClick={() => setCancelTarget(invoice)} disabled={isBusy}>
                            <Ban className="h-4 w-4" />
                          </Button>
                        )}
                      </div>
                    </TableCell>
                  </TableRow>
                )
              })
            )}
          </TableBody>
        </Table>
      </div>

      <InvoiceFormModal
        open={formOpen}
        onOpenChange={setFormOpen}
        editingInvoice={editing}
        presetPatientId={patientId}
        presetPatientName={patientName}
        onSuccess={afterMutation}
      />

      <PaymentModal
        open={!!paymentTarget}
        onOpenChange={(open) => !open && setPaymentTarget(null)}
        invoice={paymentTarget}
        onSuccess={afterMutation}
      />

      <InvoiceDetailModal
        open={!!detailInvoiceId}
        onOpenChange={(open) => !open && setDetailInvoiceId(null)}
        invoiceId={detailInvoiceId}
        onChanged={afterMutation}
      />

      <AlertDialog open={!!deleteTarget} onOpenChange={(open) => !open && setDeleteTarget(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Supprimer ce brouillon ?</AlertDialogTitle>
            <AlertDialogDescription>
              Cette action est irréversible. Seuls les brouillons peuvent être supprimés.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={busyId === deleteTarget?.id}>Annuler</AlertDialogCancel>
            <AlertDialogAction
              onClick={confirmDelete}
              disabled={busyId === deleteTarget?.id}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              Supprimer
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <Dialog open={!!cancelTarget} onOpenChange={(open) => { if (!open) { setCancelTarget(null); setCancelReason("") } }}>
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>Annuler la facture</DialogTitle>
            <DialogDescription>
              {cancelTarget?.number ? `Facture ${cancelTarget.number}` : "Facture"} — le numéro est conservé. Un motif est requis.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-1.5">
            <Textarea
              value={cancelReason}
              onChange={(e) => setCancelReason(e.target.value)}
              placeholder="Motif d'annulation"
              rows={3}
            />
          </div>
          <DialogFooter className="gap-2">
            <Button variant="outline" onClick={() => { setCancelTarget(null); setCancelReason("") }} disabled={busyId === cancelTarget?.id}>
              Retour
            </Button>
            <Button
              onClick={confirmCancel}
              disabled={busyId === cancelTarget?.id}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              Confirmer l'annulation
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={!!avoirTarget} onOpenChange={(open) => { if (!open) setAvoirTarget(null) }}>
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>Établir un avoir</DialogTitle>
            <DialogDescription>
              {avoirTarget?.number ? `Facture ${avoirTarget.number}` : "Facture"} — encaissé {avoirTarget ? formatDT(avoirTarget.amountCollected) : ""}. L'avoir crédite tout ou partie du montant encaissé.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-3">
            <div className="space-y-1.5">
              <Label htmlFor="avoirAmount">Montant (DT)</Label>
              <Input
                id="avoirAmount"
                type="number"
                min="0"
                step="0.001"
                value={avoirAmount}
                onChange={(e) => setAvoirAmount(e.target.value)}
                placeholder="0,000"
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="avoirReason">Motif</Label>
              <Textarea
                id="avoirReason"
                value={avoirReason}
                onChange={(e) => setAvoirReason(e.target.value)}
                placeholder="Motif de l'avoir"
                rows={3}
              />
            </div>
          </div>
          <DialogFooter className="gap-2">
            <Button variant="outline" onClick={() => setAvoirTarget(null)} disabled={busyId === avoirTarget?.id}>
              Retour
            </Button>
            <Button onClick={confirmAvoir} disabled={busyId === avoirTarget?.id}>
              Établir l'avoir
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
