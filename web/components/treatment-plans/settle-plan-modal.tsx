"use client"

import type React from "react"

import { useState, useEffect } from "react"
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter,
} from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { toast } from "sonner"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import { useConflict } from "@/lib/hooks/use-conflict"
import type { TreatmentPlanDto } from "@/lib/api/types"
import { formatAmount, formatDT, parseAmountInput, todayLocalIso } from "@/lib/format"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { useFreshVersion } from "@/lib/hooks/use-fresh-version"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import { PAYMENT_METHODS, paymentMethodLabel } from "@/components/factures/invoice-labels"
import {
  CHEQUE_METHOD,
  ChequeFields,
  EMPTY_CHEQUE_FIELDS,
  chequePaymentFields,
  type ChequeFieldsValue,
} from "@/components/factures/cheque-fields"
import { displayedOutstanding, installmentsPayableRoom } from "./plan-next-action"
import { installmentDueLabel } from "./treatment-plan-labels"

interface SettlePlanModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  plan: TreatmentPlanDto
  onSuccess?: () => void
}

/**
 * « Régler le devis » (S3) — one payment against the whole treatment, spread over the échéancier from the
 * earliest unpaid row onwards.
 *
 * <p>⚠️ <b>What was missing was a route, not a rule.</b> `TreatmentPlan.CollectChairside` has spread correctly
 * since the fiche started collecting, and it was reachable from <b>one</b> place: a séance's own fiche de
 * soins. A patient settling three instalments at the desk therefore went through « Encaisser » once per row —
 * three dialogs, three receipts, three dates to keep in step — because `Installment.RecordPayment` refuses
 * more than one row's remainder and that modal is addressed to a row.</p>
 *
 * <p>⚠️ The dialog <b>names the rows the money will land on</b> before the press. « Réglé » with nothing said
 * would leave the receptionist to work out afterwards which échéances moved, and the receipts are per-payment:
 * one press here can produce three of them.</p>
 */
export function SettlePlanModal({ open, onOpenChange, plan, onSuccess }: SettlePlanModalProps) {
  const [amount, setAmount] = useState("")
  const [method, setMethod] = useState<string>("Cash")
  const [paidOn, setPaidOn] = useState("")
  const [cheque, setCheque] = useState<ChequeFieldsValue>(EMPTY_CHEQUE_FIELDS)
  const [loading, setLoading] = useState(false)
  const conflict = useConflict()

  /*
   * ⚠️ **The version comes from a READ, never from the `plan` prop.** The prop is a snapshot taken when the
   * page last refetched, and this dialog opens over a workspace that has been sitting there — worse, the
   * user's own preceding actions on this page move the row further than the prop is told. A stale token 409s
   * on a record nobody else touched, and `check:responsive`'s `version-from-a-read` fails a surface that does
   * it. `useFreshVersion` re-reads the plan on open and returns the **version only**: hydrating the amount
   * from that read would replace a figure the user is already typing.
   */
  const { source: freshPlan, resync } = useFreshVersion(
    open,
    plan.id,
    plan,
    () => treatmentPlansApi.get(plan.id),
  )

  // Money being entered is not discarded by a stray tap on the overlay (J9).
  const guard = useDirtyGuard(open, onOpenChange)

  const owed = displayedOutstanding(plan)
  const outstanding = owed && !owed.isBilled ? owed.amount : 0

  useEffect(() => {
    if (!open) return
    // Through `formatAmount`, never `String(...)`: the raw number prints « 45.5 » where this product prints
    // « 45,500 ». The grouping space it emits is stripped again by `parseAmountInput`.
    setAmount(outstanding > 0 ? formatAmount(outstanding) : "")
    setMethod("Cash")
    setPaidOn(todayLocalIso())
    setCheque(EMPTY_CHEQUE_FIELDS)
    conflict.reset()
    // eslint-disable-next-line react-hooks/exhaustive-deps -- `conflict.reset` is stable; listing it re-seeds
  }, [open, outstanding])

  const parsed = parseAmountInput(amount)
  /**
   * Which échéances this amount will land on, in the server's own order (earliest due first) — so the dialog
   * and `CollectChairside` cannot describe two different outcomes.
   */
  const landing = Number.isFinite(parsed) && parsed > 0 ? installmentsPayableRoom(plan, parsed) : []
  // One auto-raised row IS the « Reste à payer » above: listing it would print the same figure twice.
  const namesRows = landing.length > 1 || (landing.length === 1 && !landing[0].isAutoRaised)

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    conflict.clearMessage()

    if (!Number.isFinite(parsed) || parsed <= 0) {
      conflict.setError("Le montant doit être supérieur à 0.")
      return
    }
    if (parsed > outstanding + 0.0005) {
      conflict.setError(`Le montant dépasse le reste à payer (${formatDT(outstanding)}).`)
      return
    }

    setLoading(true)
    try {
      await treatmentPlansApi.settle(plan.id, {
        amount: parsed,
        method,
        paidOn: new Date(paidOn).toISOString(),
        ...chequePaymentFields(method, cheque),
        version: freshPlan?.version ?? plan.version,
      })
      toast.success(
        landing.length > 1
          ? `Paiement enregistré — ${formatDT(parsed)} sur ${landing.length} échéances`
          : "Paiement enregistré",
      )
      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      // A non-conflict failure may still have moved the row; a real 409 is left alone, or the retry would
      // silently overwrite whoever caused it — the user presses again once they have re-read the échéancier.
      if (!conflict.capture(err, "Échec de l'enregistrement du règlement.")) await resync()
    } finally {
      setLoading(false)
    }
  }

  return (
    <>
      {/* Only the ROOT and « Annuler » route through the guard — the save path calls the raw prop. */}
      <Dialog open={open} onOpenChange={guard.onOpenChange}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>Encaisser</DialogTitle>
            <DialogDescription>
              Reste à payer <b className="text-foreground">{formatDT(outstanding)}</b>
            </DialogDescription>
          </DialogHeader>

          <form onSubmit={handleSubmit} className="space-y-4">
            <FormErrorBanner message={conflict.error} />

            <div className="space-y-1.5">
              <Label htmlFor="settle-amount">
                Montant (DT) <span className="text-destructive">*</span>
              </Label>
              {/* `text` + `inputMode="decimal"`, never `type="number"` (J8): a number input refuses the comma
                  this product prints with, and a rejected keystroke returns an EMPTY value. */}
              <Input
                id="settle-amount"
                type="text"
                inputMode="decimal"
                value={amount}
                onChange={(e) => setAmount(e.target.value)}
                disabled={loading}
                required
              />
            </div>

            {/* What the press will actually do — stated before it, because the receipts are per-payment and
                one press here can produce several. */}
            {namesRows && (
              <ul className="space-y-0.5 rounded-md bg-muted/40 p-2.5 text-2xs text-muted-foreground">
                {landing.map((row) => (
                  <li key={row.installmentId} className="flex flex-wrap items-baseline justify-between gap-x-2">
                    <span className="min-w-0 [overflow-wrap:anywhere]">{installmentDueLabel(row)}</span>
                    <span className="shrink-0 font-mono tabular-nums">{formatDT(row.amount)}</span>
                  </li>
                ))}
              </ul>
            )}

            <div className="space-y-1.5">
              <Label htmlFor="settle-method">Mode de paiement</Label>
              <Select value={method} onValueChange={setMethod} disabled={loading}>
                <SelectTrigger id="settle-method">
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

            {/* L8 — an échéancier settled with a book of post-dated cheques is the archetypal case. */}
            {method === CHEQUE_METHOD && (
              <ChequeFields idPrefix="settle-plan" value={cheque} onChange={setCheque} disabled={loading} />
            )}

            <div className="space-y-1.5">
              <Label htmlFor="settle-paidOn">Date</Label>
              <Input
                id="settle-paidOn"
                type="date"
                value={paidOn}
                onChange={(e) => setPaidOn(e.target.value)}
                disabled={loading}
              />
            </div>

            <DialogFooter className="gap-2">
              <Button
                type="button"
                variant="outline"
                onClick={() => guard.onOpenChange(false)}
                disabled={loading}
              >
                Annuler
              </Button>
              <Button type="submit" disabled={loading}>
                {loading ? "Enregistrement…" : "Encaisser"}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>

      <DiscardChangesDialog guard={guard} />
    </>
  )
}
