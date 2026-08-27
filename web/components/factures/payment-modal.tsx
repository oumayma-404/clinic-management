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
import { formatAmount, formatDT, parseAmountInput, todayLocalIso } from "@/lib/format"
import { downloadBlob } from "@/lib/download"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import { PAYMENT_METHODS, paymentMethodLabel } from "./invoice-labels"
import {
  CHEQUE_METHOD,
  ChequeFields,
  EMPTY_CHEQUE_FIELDS,
  chequePaymentFields,
  type ChequeFieldsValue,
} from "./cheque-fields"

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
  const [cheque, setCheque] = useState<ChequeFieldsValue>(EMPTY_CHEQUE_FIELDS)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Money being entered is not discarded by a stray tap on the overlay (J9).
  const guard = useDirtyGuard(open, onOpenChange)

  useEffect(() => {
    if (!open || !invoice) return
    // Pre-filled through `formatAmount`, never `String(...)`: the raw number renders « 45.5 » in a product that
    // prints « 45,500 » everywhere else, so the dentist saw a figure in a format the app itself never uses and
    // a decimal mark this field used to refuse. The grouping space it emits is stripped again on parse.
    setAmount(invoice.outstanding > 0 ? formatAmount(invoice.outstanding) : "")
    setMethod("Cash")
    setPaidOn(todayLocalIso())
    setCheque(EMPTY_CHEQUE_FIELDS)
    setError(null)
  }, [open, invoice])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!invoice) return
    setError(null)

    const parsedAmount = parseAmountInput(amount)
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
        ...chequePaymentFields(method, cheque),
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
    <>
    {/* Only the ROOT and « Annuler » route through the guard — the save path calls the raw prop (§ 5). */}
    <Dialog open={open} onOpenChange={guard.onOpenChange}>
      <DialogContent className="md:max-w-md">
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
            {/* `text` + `inputMode="decimal"`, never `type="number"` (J8): a number input refuses the comma this
                product prints with, and when the browser rejects the keystroke `e.target.value` comes back
                empty — so the amount looked typed and the submit sent nothing. The keypad still appears. */}
            <Input
              id="amount"
              type="text"
              inputMode="decimal"
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
              disabled={loading}
              required
            />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="method">Mode de paiement</Label>
            <Select value={method} onValueChange={setMethod} disabled={loading}>
              {/* `w-full`: `ui/select.tsx` ships `w-fit`, so the trigger otherwise renders narrower than the
                  amount field stacked directly above it — the form reads as a mis-aligned column. */}
              <SelectTrigger id="method" className="w-full">
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

          {/* L8 — only for a cheque, and the payload builder clears them anyway if the method changes back, so a
              stale typed value can never be submitted. */}
          {method === CHEQUE_METHOD && (
            <ChequeFields
              idPrefix="invoice-payment"
              value={cheque}
              onChange={setCheque}
              disabled={loading}
            />
          )}

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
            <Button type="button" variant="outline" onClick={() => guard.onOpenChange(false)} disabled={loading}>
              Annuler
            </Button>
            <Button type="submit" disabled={loading}>
              {loading ? "Enregistrement…" : "Enregistrer"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
    <DiscardChangesDialog guard={guard} />
    </>
  )
}
