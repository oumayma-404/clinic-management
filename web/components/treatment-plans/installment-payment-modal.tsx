"use client"

import { useState, useEffect } from "react"
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { toast } from "sonner"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import { ApiError } from "@/lib/api/client"
import type { InstallmentDto } from "@/lib/api/types"
import { formatDT, formatDateFr } from "@/lib/format"
import { downloadBlob } from "@/lib/download"
import { PAYMENT_METHODS, paymentMethodLabel } from "@/components/factures/invoice-labels"

interface InstallmentPaymentModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  planId: string | null
  installment: InstallmentDto | null
  onSuccess?: () => void
}

export function InstallmentPaymentModal({ open, onOpenChange, planId, installment, onSuccess }: InstallmentPaymentModalProps) {
  const [amount, setAmount] = useState("")
  const [method, setMethod] = useState<string>("Cash")
  const [paidOn, setPaidOn] = useState("")
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open || !installment) return
    setAmount(installment.outstanding > 0 ? String(installment.outstanding) : "")
    setMethod("Cash")
    setPaidOn(new Date().toISOString().slice(0, 10))
    setError(null)
  }, [open, installment])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!planId || !installment) return
    setError(null)

    const parsedAmount = Number(amount)
    if (!Number.isFinite(parsedAmount) || parsedAmount <= 0) {
      setError("Le montant doit être supérieur à 0.")
      return
    }
    if (parsedAmount > installment.outstanding) {
      setError(`Le paiement dépasse le reste dû (${formatDT(installment.outstanding)}).`)
      return
    }

    setLoading(true)
    try {
      const currentPlanId = planId
      const currentInstallmentId = installment.id
      await treatmentPlansApi.recordInstallmentPayment(currentPlanId, currentInstallmentId, {
        amount: parsedAmount,
        method,
        paidOn: new Date(paidOn).toISOString(),
      })
      toast.success("Paiement enregistré", {
        action: {
          label: "Télécharger le reçu",
          onClick: () => {
            treatmentPlansApi
              .downloadInstallmentReceipt(currentPlanId, currentInstallmentId)
              .then((blob) => downloadBlob(blob, `recu-echeance-${currentInstallmentId.slice(0, 8)}.pdf`))
              .catch((e) => toast.error(e instanceof Error ? e.message : "Échec du téléchargement du reçu."))
          },
        },
      })
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
            {installment ? `Échéance du ${formatDateFr(installment.dueDate)}` : "Échéance"} — reste dû{" "}
            {installment ? formatDT(installment.outstanding) : ""}
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
          {error && (
            <div className="rounded-lg bg-red-50 border border-red-200 p-3 text-sm text-red-800 dark:bg-red-950 dark:border-red-900 dark:text-red-200">
              {error}
            </div>
          )}

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
