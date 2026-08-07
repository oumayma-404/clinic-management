"use client"

import type React from "react"

import { useState, useEffect, useRef } from "react"
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { toast } from "sonner"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import { ApiError } from "@/lib/api/client"
import { showErrorToast } from "@/lib/errors"
import type { InstallmentDto } from "@/lib/api/types"
import { formatAmount, formatDT, formatDateFr, parseAmountInput, todayLocalIso } from "@/lib/format"
import { downloadBlob } from "@/lib/download"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import { PAYMENT_METHODS, paymentMethodLabel } from "@/components/factures/invoice-labels"
import {
  CHEQUE_METHOD,
  ChequeFields,
  EMPTY_CHEQUE_FIELDS,
  chequePaymentFields,
  type ChequeFieldsValue,
} from "@/components/factures/cheque-fields"

interface InstallmentPaymentModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  planId: string | null
  installment: InstallmentDto | null
  onSuccess?: () => void
}

/**
 * Upgrade the message when the same edit conflicts twice running. The first 409 means "someone saved before
 * you"; the second means "someone is editing this right now", and telling the user to reload again would be
 * repeating advice that has already failed.
 */
function conflictMessage(err: unknown, fallback: string, consecutive: React.MutableRefObject<number>): string {
  if (err instanceof ApiError && err.status === 409) {
    consecutive.current += 1
    if (consecutive.current > 1) {
      return "L'enregistrement a encore été modifié pendant votre saisie. Quelqu'un travaille probablement "
        + "dessus en même temps — coordonnez-vous avant de réessayer."
    }
    return err.message || fallback
  }
  consecutive.current = 0
  return err instanceof ApiError ? err.message : fallback
}

export function InstallmentPaymentModal({ open, onOpenChange, planId, installment, onSuccess }: InstallmentPaymentModalProps) {
  const [amount, setAmount] = useState("")
  const [method, setMethod] = useState<string>("Cash")
  const [paidOn, setPaidOn] = useState("")
  const [cheque, setCheque] = useState<ChequeFieldsValue>(EMPTY_CHEQUE_FIELDS)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const conflictStreak = useRef(0)

  // Money being entered is not discarded by a stray tap on the overlay (J9). This dialog is a `mobile="bottom"`
  // sheet, so on a phone the strip above it is a live dismiss target sitting over an amount being keyed in.
  const guard = useDirtyGuard(open, onOpenChange)

  useEffect(() => {
    if (!open || !installment) return
    // Through `formatAmount`, never `String(...)`: the raw number prints « 45.5 » where this product prints
    // « 45,500 ». The grouping space it emits is stripped again by `parseAmountInput`.
    setAmount(installment.outstanding > 0 ? formatAmount(installment.outstanding) : "")
    setMethod("Cash")
    setPaidOn(todayLocalIso())
    setCheque(EMPTY_CHEQUE_FIELDS)
    setError(null)
  }, [open, installment])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!planId || !installment) return
    setError(null)

    const parsedAmount = parseAmountInput(amount)
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
      const priorPaymentIds = new Set((installment.payments ?? []).map((p) => p.id))

      const updated = await treatmentPlansApi.recordInstallmentPayment(currentPlanId, currentInstallmentId, {
        amount: parsedAmount,
        method,
        paidOn: new Date(paidOn).toISOString(),
        ...chequePaymentFields(method, cheque),
      })

      // The receipt is per-PAYMENT now, so find the row we just created rather than the échéance total —
      // that is the whole point: a second partial payment used to reprint a receipt for the running sum.
      const refreshed = updated.installments.find((i) => i.id === currentInstallmentId)
      const newPayment = refreshed?.payments.find((p) => !priorPaymentIds.has(p.id))

      toast.success("Paiement enregistré", newPayment ? {
        action: {
          label: "Télécharger le reçu",
          onClick: () => {
            treatmentPlansApi
              .downloadInstallmentReceipt(currentPlanId, currentInstallmentId, newPayment.id)
              .then((blob) => downloadBlob(blob, `recu-echeance-${newPayment.id.slice(0, 8)}.pdf`))
              // `showErrorToast`: this fires from inside another toast's action, so the dialog is already gone
              // and this message is the only trace of the failure — it needs the 8-second life, not the 4 the
              // hand-rolled `toast.error` inherited from the success default.
              .catch((e) => showErrorToast(e, "Échec du téléchargement du reçu."))
          },
        },
      } : undefined)
      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      setError(conflictMessage(err, "Échec de l'enregistrement du paiement.", conflictStreak))
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
            {installment ? `Échéance du ${formatDateFr(installment.dueDate)}` : "Échéance"} — reste dû{" "}
            {installment ? formatDT(installment.outstanding) : ""}
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
          <FormErrorBanner message={error} />

          <div className="space-y-1.5">
            <Label htmlFor="amount">
              Montant (DT) <span className="text-destructive">*</span>
            </Label>
            {/* `text` + `inputMode="decimal"`, never `type="number"` (J8): a number input refuses the comma this
                product prints with, and a rejected keystroke returns an EMPTY value — so the amount looked typed
                and the submit sent nothing. The numeric keypad still appears on a phone. */}
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

          {/* L8 — an échéancier settled with a book of post-dated cheques is the archetypal case this exists for. */}
          {method === CHEQUE_METHOD && (
            <ChequeFields
              idPrefix="installment-payment"
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
              {loading ? "Enregistrement..." : "Enregistrer"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
    <DiscardChangesDialog guard={guard} />
    </>
  )
}
