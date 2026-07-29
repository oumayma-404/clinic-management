"use client"

import { useState, useEffect } from "react"
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { toast } from "sonner"
import { invoicesApi } from "@/lib/api/invoices"
import { billingApi } from "@/lib/api/billing"
import { ApiError } from "@/lib/api/client"
import type { InvoiceDto } from "@/lib/api/types"
import { formatDT, todayLocalIso } from "@/lib/format"
import { downloadBlob } from "@/lib/download"
import { PAYMENT_METHODS, paymentMethodLabel } from "./invoice-labels"

interface PaymentModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  invoice: InvoiceDto | null
  onSuccess?: () => void
}

export function PaymentModal({ open, onOpenChange, invoice, onSuccess }: PaymentModalProps) {
  const [amount, setAmount] = useState("")
  const [method, setMethod] = useState<string>("Cash")
  const [paidOn, setPaidOn] = useState("")
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open || !invoice) return
    setAmount(invoice.outstanding > 0 ? String(invoice.outstanding) : "")
    setMethod("Cash")
    setPaidOn(todayLocalIso())
    setError(null)
  }, [open, invoice])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!invoice) return
    setError(null)

    const parsedAmount = Number(amount)
    if (!Number.isFinite(parsedAmount) || parsedAmount <= 0) {
      setError("Le montant doit être supérieur à 0.")
      return
    }
    if (parsedAmount > invoice.outstanding) {
      setError(`Le paiement dépasse le reste dû (${formatDT(invoice.outstanding)}).`)
      return
    }

    setLoading(true)
    try {
      const priorPaymentIds = new Set(invoice.payments.map((p) => p.id))
      const updated = await invoicesApi.recordPayment(invoice.id, {
        amount: parsedAmount,
        method,
        paidOn: new Date(paidOn).toISOString(),
      })
      const newPayment =
        updated.payments.find((p) => !priorPaymentIds.has(p.id)) ??
        updated.payments[updated.payments.length - 1]

      toast.success("Paiement enregistré", newPayment ? {
        action: {
          label: "Télécharger le reçu",
          onClick: () => {
            billingApi
              .downloadPaymentReceipt(newPayment.id)
              .then((blob) => downloadBlob(blob, `recu-${updated.number ?? updated.id}.pdf`))
              .catch((e) => toast.error(e instanceof Error ? e.message : "Échec du téléchargement du reçu."))
          },
        },
      } : undefined)
      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Échec de l'enregistrement du paiement.")
    } finally {
      setLoading(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Enregistrer un paiement</DialogTitle>
          <DialogDescription>
            {invoice?.number ? `Facture ${invoice.number}` : "Facture"} — reste dû{" "}
            {invoice ? formatDT(invoice.outstanding) : ""}
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
          <FormErrorBanner message={error} />

          <div className="space-y-1.5">
            <Label htmlFor="amount">
              Montant (DT) <span className="text-destructive">*</span>
            </Label>
            <Input
              id="amount"
              type="number"
              min="0"
              step="0.001"
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
              disabled={loading}
              required
            />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="method">Mode de paiement</Label>
            <Select value={method} onValueChange={setMethod} disabled={loading}>
              <SelectTrigger id="method">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {PAYMENT_METHODS.map((m) => (
                  <SelectItem key={m} value={m}>
                    {paymentMethodLabel(m)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="paidOn">Date</Label>
            <Input
              id="paidOn"
              type="date"
              value={paidOn}
              onChange={(e) => setPaidOn(e.target.value)}
              disabled={loading}
            />
          </div>

          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={loading}>
              Annuler
            </Button>
            <Button type="submit" disabled={loading}>
              {loading ? "Enregistrement..." : "Enregistrer"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
