"use client"

import { useState, useEffect, useCallback } from "react"
import {
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from "@/components/ui/table"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { Textarea } from "@/components/ui/textarea"
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter,
} from "@/components/ui/dialog"
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent,
  AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { FileDown, Pencil, Trash2, Send, CreditCard, Ban, Plus, Loader2, Landmark, FileCode2, ReceiptText } from "lucide-react"
import { toast } from "sonner"
import { invoicesApi } from "@/lib/api/invoices"
import { ApiError } from "@/lib/api/client"
import type { InvoiceDto } from "@/lib/api/types"
import { formatDT, formatDateFr } from "@/lib/format"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useConnectivity } from "@/lib/connectivity/connectivity"
import { InvoiceFormModal } from "./invoice-form-modal"
import { PaymentModal } from "./payment-modal"
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

  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<InvoiceDto | null>(null)
  const [paymentTarget, setPaymentTarget] = useState<InvoiceDto | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<InvoiceDto | null>(null)
  const [cancelTarget, setCancelTarget] = useState<InvoiceDto | null>(null)
  const [cancelReason, setCancelReason] = useState("")

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
                const isCancellable = isPayable || invoice.status === "Paid"
                return (
                  <TableRow key={invoice.id}>
                    <TableCell className="font-medium">{invoice.number ?? "—"}</TableCell>
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
                        {!isDraft && invoice.canSubmitToElFatoora && (
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
    </div>
  )
}
